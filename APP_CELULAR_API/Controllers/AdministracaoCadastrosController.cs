using System.Security.Cryptography;
using System.Text.Json;
using APP_CELULAR_API.Services;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace APP_CELULAR_API.Controllers;

[ApiController]
[Route("api/administracao/acessos/cadastros")]
public sealed class AdministracaoCadastrosController(
    IConfiguration configuration,
    MasterSessionStore sessions,
    IEmpresaDatabaseResolver resolver,
    ILogger<AdministracaoCadastrosController> logger) : ControllerBase
{
    private static readonly HashSet<string> ModulosValidos = new(StringComparer.Ordinal)
    {
        "chat", "pessoas", "produtos", "vendas", "compras", "financeiro",
        "agenda", "outros", "usuarios", "configuracoes"
    };

    [HttpGet]
    public async Task<IActionResult> ListarPendentes()
    {
        if (!Autorizado()) return Unauthorized(new { mensagem = "Acesso restrito ao usuário Zeus." });
        try
        {
            await using var db = await BancoChatCentral.Abrir(configuration);
            await using var cmd = new NpgsqlCommand("""
                SELECT id,email,nome,COALESCE(username,nome),empresa_id,criada_em
                FROM chat.solicitacao_cadastro WHERE status='PENDENTE'
                ORDER BY criada_em,id LIMIT 500;
                """, db);
            await using var reader = await cmd.ExecuteReaderAsync();
            var items = new List<CadastroPendenteResponse>();
            while (await reader.ReadAsync())
                items.Add(new CadastroPendenteResponse
                {
                    Id = reader.GetInt64(0), Email = reader.GetString(1), Nome = reader.GetString(2),
                    Username = reader.GetString(3), EmpresaId = reader.IsDBNull(4) ? null : reader.GetInt64(4),
                    CriadaEm = reader.GetDateTime(5)
                });
            return Ok(items);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao listar cadastros pendentes do banco central.");
            return StatusCode(503, new { mensagem = "Não foi possível carregar os cadastros pendentes. Confira a conexão central e a migração 040." });
        }
    }

    [HttpPost("{id:long}/aprovar")]
    public async Task<IActionResult> Aprovar(long id, [FromBody] AprovarCadastroRequest request)
    {
        if (!Autorizado()) return Unauthorized(new { mensagem = "Acesso restrito ao usuário Zeus." });
        string[] modules = (request.Icones ?? []).Where(ModulosValidos.Contains).Distinct(StringComparer.Ordinal).ToArray();
        if (id <= 0 || request.EmpresaId <= 0) return BadRequest(new { mensagem = "Selecione uma empresa válida." });
        if (modules.Length == 0) return BadRequest(new { mensagem = "Libere pelo menos um módulo para este usuário." });

        NpgsqlConnection? tenant = null;
        long? createdUserId = null;
        bool tenantCommitted = false;
        try
        {
            tenant = new NpgsqlConnection(await resolver.ObterConnectionString(request.EmpresaId));
            await tenant.OpenAsync();
            await using var tenantTx = await tenant.BeginTransactionAsync();
            await GarantirColunasUsuario(tenant, tenantTx);

            await using var central = await BancoChatCentral.Abrir(configuration);
            await using var centralTx = await central.BeginTransactionAsync();
            Guid authUid;
            string email, nome, username;
            string nomeEmpresa;
            await using (var company = new NpgsqlCommand("SELECT nome FROM platform.empresa_catalogo WHERE id=@id AND ativa=TRUE AND status='ATIVA'", central, centralTx))
            {
                company.Parameters.AddWithValue("id", request.EmpresaId);
                nomeEmpresa = await company.ExecuteScalarAsync() as string
                    ?? throw new InvalidOperationException("A empresa selecionada não está ativa no catálogo central.");
            }
            await using (var cmd = new NpgsqlCommand("""
                SELECT r.auth_uid,r.email,r.nome,COALESCE(r.username,r.nome),u.email
                FROM chat.solicitacao_cadastro r LEFT JOIN auth.users u ON u.id=r.auth_uid
                WHERE r.id=@id AND r.status='PENDENTE' FOR UPDATE OF r;
                """, central, centralTx))
            {
                cmd.Parameters.AddWithValue("id", id);
                await using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync()) return NotFound(new { mensagem = "Cadastro pendente não encontrado." });
                if (reader.IsDBNull(0)) return Conflict(new { mensagem = "A conta de autenticação ainda não foi preparada. Peça à pessoa para reenviar o cadastro." });
                authUid = reader.GetGuid(0); email = reader.GetString(1); nome = reader.GetString(2); username = reader.GetString(3);
                if (reader.IsDBNull(4) || !string.Equals(reader.GetString(4), email, StringComparison.OrdinalIgnoreCase))
                    return Conflict(new { mensagem = "A conta de autenticação não corresponde ao e-mail deste cadastro." });
            }

            // O cadastro público não exige confirmação por e-mail: a aprovação de Zeus
            // libera a conta. Confirme o usuário pendente no Auth dentro da transação
            // central para que a senha escolhida no cadastro funcione após a aprovação.
            await using (var confirm = new NpgsqlCommand("""
                UPDATE auth.users
                SET email_confirmed_at=COALESCE(email_confirmed_at,NOW()),updated_at=NOW()
                WHERE id=@authUid AND LOWER(email)=LOWER(@email)
                RETURNING email_confirmed_at;
                """, central, centralTx))
            {
                confirm.Parameters.AddWithValue("authUid", authUid);
                confirm.Parameters.AddWithValue("email", email);
                var confirmedAt = await confirm.ExecuteScalarAsync();
                if (confirmedAt is null or DBNull)
                    return Conflict(new { mensagem = "Não foi possível preparar a conta de autenticação para aprovação. Confira o e-mail e tente novamente." });
            }

            string tipoNegocio = await ObterTipoEmpresa(central, centralTx, request.EmpresaId);
            long usuarioId;
            await GarantirEmpresaOperacional(tenant, tenantTx, request.EmpresaId, nomeEmpresa, email, tipoNegocio);
            await using (var check = new NpgsqlCommand("""
                SELECT EXISTS(SELECT 1 FROM app.usuario WHERE empresa_id=@empresaId
                    AND (lower(trim(COALESCE(username,'')))=lower(trim(@username)) OR lower(trim(COALESCE(email,'')))=lower(trim(@email)))
                    AND COALESCE(excluido,FALSE)=FALSE);
                """, tenant, tenantTx))
            {
                check.Parameters.AddWithValue("empresaId", request.EmpresaId);
                check.Parameters.AddWithValue("username", username);
                check.Parameters.AddWithValue("email", email);
                if ((bool)(await check.ExecuteScalarAsync() ?? false)) return Conflict(new { mensagem = "Já existe um usuário com este login ou e-mail nessa empresa." });
            }

            string randomLegacyPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            string iconJson = JsonSerializer.Serialize(modules);
            await using (var insert = new NpgsqlCommand("""
                INSERT INTO app.usuario
                    (username,email,nome,senha_hash,funcao,tipo_negocio,eh_master,aprovado,empresa_id,
                     status_sincronizacao,data_cadastro,data_alteracao,excluido,icones_permitidos)
                VALUES (@username,@email,@nome,@senha,'USUARIO',@tipo,FALSE,TRUE,@empresaId,
                        'SINCRONIZADO',NOW(),NOW(),FALSE,@icones) RETURNING id;
                """, tenant, tenantTx))
            {
                insert.Parameters.AddWithValue("username", username);
                insert.Parameters.AddWithValue("email", email);
                insert.Parameters.AddWithValue("nome", nome);
                insert.Parameters.AddWithValue("senha", randomLegacyPassword);
                insert.Parameters.AddWithValue("tipo", tipoNegocio);
                insert.Parameters.AddWithValue("empresaId", request.EmpresaId);
                insert.Parameters.AddWithValue("icones", iconJson);
                usuarioId = Convert.ToInt64(await insert.ExecuteScalarAsync());
                createdUserId = usuarioId;
            }

            await using (var insert = new NpgsqlCommand("""
                INSERT INTO chat.usuario_auth(auth_uid,empresa_id,usuario_id,nome,email,username,tipo_negocio,ativo)
                VALUES(@authUid,@empresaId,@usuarioId,@nome,@email,@username,@tipo,TRUE);
                """, central, centralTx))
            {
                insert.Parameters.AddWithValue("authUid", authUid); insert.Parameters.AddWithValue("empresaId", request.EmpresaId);
                insert.Parameters.AddWithValue("usuarioId", usuarioId); insert.Parameters.AddWithValue("nome", nome);
                insert.Parameters.AddWithValue("email", email); insert.Parameters.AddWithValue("username", username);
                insert.Parameters.AddWithValue("tipo", tipoNegocio);
                await insert.ExecuteNonQueryAsync();
            }
            await using (var update = new NpgsqlCommand("""
                UPDATE chat.solicitacao_cadastro
                SET status='APROVADO',empresa_id=@empresaId,usuario_id=@usuarioId,analisada_em=NOW()
                WHERE id=@id AND status='PENDENTE';
                """, central, centralTx))
            {
                update.Parameters.AddWithValue("empresaId", request.EmpresaId);
                update.Parameters.AddWithValue("usuarioId", usuarioId);
                update.Parameters.AddWithValue("id", id);
                if (await update.ExecuteNonQueryAsync() != 1) throw new InvalidOperationException("O pedido mudou durante a aprovação.");
            }

            await tenantTx.CommitAsync();
            tenantCommitted = true;
            await centralTx.CommitAsync();
            return Ok(new { sucesso = true, usuarioId, mensagem = $"Cadastro de {nome} aprovado com {modules.Length} módulo(s)." });
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Conflict(new { mensagem = "O usuário já está vinculado ou o login já existe nessa empresa." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao aprovar cadastro pendente {RequestId}.", id);
            if (tenantCommitted && createdUserId.HasValue && tenant is not null)
            {
                try
                {
                    await using var cleanup = new NpgsqlCommand("DELETE FROM app.usuario WHERE id=@id AND empresa_id=@empresaId AND COALESCE(eh_master,FALSE)=FALSE", tenant);
                    cleanup.Parameters.AddWithValue("id", createdUserId.Value);
                    cleanup.Parameters.AddWithValue("empresaId", request.EmpresaId);
                    await cleanup.ExecuteNonQueryAsync();
                }
                catch (Exception cleanupError) { logger.LogError(cleanupError, "Falha ao reverter usuário operacional órfão {UsuarioId}.", createdUserId); }
            }
            return StatusCode(503, new { mensagem = "Não foi possível concluir a aprovação nos dois bancos. O pedido continua pendente; atualize e tente novamente." });
        }
        finally
        {
            if (tenant is not null) await tenant.DisposeAsync();
        }
    }

    [HttpPost("{id:long}/recusar")]
    public async Task<IActionResult> Recusar(long id)
    {
        if (!Autorizado()) return Unauthorized(new { mensagem = "Acesso restrito ao usuário Zeus." });
        if (id <= 0) return BadRequest(new { mensagem = "Cadastro inválido." });
        try
        {
            await using var db = await BancoChatCentral.Abrir(configuration);
            await using var cmd = new NpgsqlCommand("UPDATE chat.solicitacao_cadastro SET status='RECUSADO',analisada_em=NOW() WHERE id=@id AND status='PENDENTE' RETURNING id", db);
            cmd.Parameters.AddWithValue("id", id);
            return await cmd.ExecuteScalarAsync() is null
                ? NotFound(new { mensagem = "Cadastro pendente não encontrado." })
                : Ok(new { sucesso = true, mensagem = "Cadastro recusado." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao recusar cadastro pendente {RequestId}.", id);
            return StatusCode(503, new { mensagem = "Não foi possível recusar o cadastro." });
        }
    }

    private bool Autorizado() => sessions.Validar(Request.Headers.Authorization.ToString());

    private static async Task GarantirColunasUsuario(NpgsqlConnection db, NpgsqlTransaction tx)
    {
        await using var cmd = new NpgsqlCommand("""
            ALTER TABLE app.usuario ADD COLUMN IF NOT EXISTS username TEXT;
            ALTER TABLE app.usuario ADD COLUMN IF NOT EXISTS email TEXT;
            ALTER TABLE app.usuario ADD COLUMN IF NOT EXISTS tipo_negocio TEXT NOT NULL DEFAULT 'comercial';
            ALTER TABLE app.usuario ADD COLUMN IF NOT EXISTS icones_permitidos TEXT NOT NULL DEFAULT '["chat","pessoas","produtos","vendas","compras","financeiro","agenda","outros","usuarios","configuracoes"]';
            """, db, tx);
        await cmd.ExecuteNonQueryAsync();
    }

    // O banco legado mantém uma FK app.usuario.empresa_id para seu cadastro local
    // de empresas. O catálogo central passou a ser a fonte oficial, então espelhamos
    // a empresa (mesmo ID) no alvo real da FK antes de inserir o usuário.
    public static async Task GarantirEmpresaOperacional(NpgsqlConnection db, NpgsqlTransaction tx,
        long empresaId, string nome, string emailResponsavel, string tipoNegocio)
    {
        string? schema = null, tabela = null, chave = null;
        await using (var fk = new NpgsqlCommand("""
            SELECT ns.nspname, rel.relname, att.attname
            FROM pg_constraint c
            JOIN pg_class rel ON rel.oid=c.confrelid
            JOIN pg_namespace ns ON ns.oid=rel.relnamespace
            JOIN LATERAL unnest(c.confkey) WITH ORDINALITY k(attnum,ord) ON TRUE
            JOIN pg_attribute att ON att.attrelid=rel.oid AND att.attnum=k.attnum
            WHERE c.conrelid='app.usuario'::regclass AND c.conname='fk_usuario_empresa'
            ORDER BY k.ord LIMIT 1;
            """, db, tx))
        await using (var reader = await fk.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync()) { schema = reader.GetString(0); tabela = reader.GetString(1); chave = reader.GetString(2); }
        }
        if (schema is null || tabela is null || chave is null) return;

        static string Q(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        string qualified = Q(schema) + "." + Q(tabela);
        await using (var exists = new NpgsqlCommand($"SELECT EXISTS(SELECT 1 FROM {qualified} WHERE {Q(chave)}=@id)", db, tx))
        {
            exists.Parameters.AddWithValue("id", empresaId);
            if ((bool)(await exists.ExecuteScalarAsync() ?? false)) return;
        }

        var columns = new List<(string Name, bool Required)>();
        await using (var metadata = new NpgsqlCommand("""
            SELECT a.attname, (a.attnotnull AND a.attgenerated='') AS required
            FROM pg_attribute a JOIN pg_class t ON t.oid=a.attrelid
            JOIN pg_namespace n ON n.oid=t.relnamespace
            WHERE n.nspname=@schema AND t.relname=@table AND a.attnum>0 AND NOT a.attisdropped
              AND (a.attnotnull AND a.attgenerated='')
            ORDER BY a.attnum;
            """, db, tx))
        {
            metadata.Parameters.AddWithValue("schema", schema); metadata.Parameters.AddWithValue("table", tabela);
            await using var reader = await metadata.ExecuteReaderAsync();
            while (await reader.ReadAsync()) columns.Add((reader.GetString(0), reader.GetBoolean(1)));
        }
        var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { [chave] = empresaId };
        foreach (var (column, _) in columns)
        {
            if (column.Equals("nome", StringComparison.OrdinalIgnoreCase) || column.Equals("name", StringComparison.OrdinalIgnoreCase)) values[column] = nome;
            else if (column.Equals("email", StringComparison.OrdinalIgnoreCase) || column.Equals("email_responsavel", StringComparison.OrdinalIgnoreCase)) values[column] = emailResponsavel;
            else if (column.Equals("ativa", StringComparison.OrdinalIgnoreCase) || column.Equals("ativo", StringComparison.OrdinalIgnoreCase)) values[column] = true;
            else if (column.Equals("status", StringComparison.OrdinalIgnoreCase)) values[column] = "ATIVA";
            else if (column.Equals("tipo_negocio", StringComparison.OrdinalIgnoreCase)) values[column] = tipoNegocio;
            else if (column.Equals("criada_em", StringComparison.OrdinalIgnoreCase) || column.Equals("created_at", StringComparison.OrdinalIgnoreCase)) values[column] = DateTime.UtcNow;
            else if (column.Equals("id", StringComparison.OrdinalIgnoreCase)) values[column] = empresaId;
            else if (!values.ContainsKey(column))
                throw new InvalidOperationException($"A tabela {schema}.{tabela} exige o campo '{column}' para sincronizar a empresa. Configure esse campo na API antes de aprovar usuários.");
        }
        var insertColumns = values.Keys.ToArray();
        string sql = $"INSERT INTO {qualified} ({string.Join(",", insertColumns.Select(Q))}) VALUES ({string.Join(",", insertColumns.Select((_, i) => "@v" + i))}) ON CONFLICT ({Q(chave)}) DO NOTHING";
        await using var insert = new NpgsqlCommand(sql, db, tx);
        for (int i = 0; i < insertColumns.Length; i++) insert.Parameters.AddWithValue("v" + i, values[insertColumns[i]]);
        await insert.ExecuteNonQueryAsync();
    }

    private static async Task<string> ObterTipoEmpresa(NpgsqlConnection central, NpgsqlTransaction tx, long empresaId)
    {
        await using var cmd = new NpgsqlCommand("SELECT tipo_negocio FROM platform.empresa_catalogo WHERE id=@id AND ativa=TRUE AND status='ATIVA'", central, tx);
        cmd.Parameters.AddWithValue("id", empresaId);
        var tipo = await cmd.ExecuteScalarAsync() as string;
        return tipo is "chat" or "agendamento" ? tipo : tipo is null ? throw new InvalidOperationException("A empresa selecionada não está ativa no catálogo central.") : "comercial";
    }
}

public sealed class CadastroPendenteResponse
{
    public long Id { get; set; }
    public string Email { get; set; } = "";
    public string Nome { get; set; } = "";
    public string Username { get; set; } = "";
    public long? EmpresaId { get; set; }
    public DateTime CriadaEm { get; set; }
}

public sealed class AprovarCadastroRequest
{
    public long EmpresaId { get; set; }
    public List<string>? Icones { get; set; }
}
