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
    [HttpGet]
    public async Task<IActionResult> ListarPendentes()
    {
        if (!MasterAutorizado()) return Unauthorized(new { mensagem = "Acesso restrito ao Administrador Master." });
        try
        {
            await using var db = await BancoChatCentral.Abrir(configuration);
            const string sql = """
                SELECT id,email,nome,empresa_id,criada_em,email_verificado_em
                FROM chat.solicitacao_cadastro
                WHERE status='PENDENTE'
                ORDER BY criada_em
                LIMIT 500;
                """;
            await using var cmd = new NpgsqlCommand(sql, db);
            await using var reader = await cmd.ExecuteReaderAsync();
            var pedidos = new List<CadastroPendenteResponse>();
            while (await reader.ReadAsync())
            {
                pedidos.Add(new CadastroPendenteResponse
                {
                    Id = reader.GetInt64(0),
                    Email = reader.GetString(1),
                    Nome = reader.GetString(2),
                    EmpresaId = reader.IsDBNull(3) ? null : reader.GetInt64(3),
                    CriadaEm = reader.GetDateTime(4),
                    EmailVerificado = !reader.IsDBNull(5)
                });
            }
            return Ok(pedidos);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao listar cadastros Web pendentes para o Administrador Master.");
            return StatusCode(503, new { mensagem = "Não foi possível carregar os cadastros Web pendentes. Confira a conexão CadastroCentral e as migrações Supabase." });
        }
    }

    [HttpPost("{id:long}/aprovar")]
    public async Task<IActionResult> Aprovar(long id, [FromBody] AprovarCadastroPendenteRequest request)
    {
        if (!MasterAutorizado()) return Unauthorized(new { mensagem = "Acesso restrito ao Administrador Master." });
        if (id <= 0 || request.EmpresaId <= 0 || request.TipoNegocio is not ("chat" or "comercial"))
            return BadRequest(new { mensagem = "Selecione uma empresa e um tipo de negócio válidos." });

        try
        {
            try { _ = await resolver.ObterConnectionString(request.EmpresaId); }
            catch (InvalidOperationException)
            {
                return BadRequest(new { mensagem = "A empresa selecionada não está ativa ou configurada no sistema." });
            }

            await using var db = await BancoChatCentral.Abrir(configuration);
            await using var transaction = await db.BeginTransactionAsync();
            Guid authUid;
            string email;
            string nome;
            long usuarioId;
            await using (var requestCmd = new NpgsqlCommand("""
                SELECT r.auth_uid,r.email,r.nome,u.email
                FROM chat.solicitacao_cadastro r
                LEFT JOIN auth.users u ON u.id=r.auth_uid
                WHERE r.id=@id AND r.status='PENDENTE'
                FOR UPDATE OF r;
                """, db, transaction))
            {
                requestCmd.Parameters.AddWithValue("id", id);
                await using var reader = await requestCmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync()) return NotFound(new { mensagem = "Cadastro pendente não encontrado." });
                if (reader.IsDBNull(0)) return Conflict(new { mensagem = "O cadastro ainda não está vinculado à conta de autenticação." });
                authUid = reader.GetGuid(0);
                email = reader.GetString(1);
                nome = reader.GetString(2);
                if (reader.IsDBNull(3) || !string.Equals(reader.GetString(3), email, StringComparison.OrdinalIgnoreCase))
                    return Conflict(new { mensagem = "O e-mail da conta autenticada não corresponde ao cadastro." });
            }

            await using (var confirmCmd = new NpgsqlCommand(
                "UPDATE auth.users SET email_confirmed_at=COALESCE(email_confirmed_at,NOW()),updated_at=NOW() WHERE id=@authUid AND lower(email)=lower(@email)",
                db, transaction))
            {
                confirmCmd.Parameters.AddWithValue("authUid", authUid);
                confirmCmd.Parameters.AddWithValue("email", email);
                await confirmCmd.ExecuteNonQueryAsync();
            }

            const string insertUser = """
                INSERT INTO chat.usuario_auth (auth_uid,empresa_id,nome,email,tipo_negocio,ativo)
                VALUES (@authUid,@empresaId,@nome,@email,@tipoNegocio,TRUE)
                RETURNING usuario_id;
                """;
            await using (var createUserCmd = new NpgsqlCommand(insertUser, db, transaction))
            {
                createUserCmd.Parameters.AddWithValue("authUid", authUid);
                createUserCmd.Parameters.AddWithValue("empresaId", request.EmpresaId);
                createUserCmd.Parameters.AddWithValue("nome", nome);
                createUserCmd.Parameters.AddWithValue("email", email);
                createUserCmd.Parameters.AddWithValue("tipoNegocio", request.TipoNegocio);
                usuarioId = Convert.ToInt64(await createUserCmd.ExecuteScalarAsync());
            }

            const string approveRequest = """
                UPDATE chat.solicitacao_cadastro
                SET status='APROVADO',auth_uid=@authUid,usuario_id=@usuarioId,empresa_id=@empresaId,
                    tipo_negocio=@tipoNegocio,analisada_em=NOW()
                WHERE id=@id AND status='PENDENTE';
                """;
            await using (var approveCmd = new NpgsqlCommand(approveRequest, db, transaction))
            {
                approveCmd.Parameters.AddWithValue("authUid", authUid);
                approveCmd.Parameters.AddWithValue("empresaId", request.EmpresaId);
                approveCmd.Parameters.AddWithValue("usuarioId", usuarioId);
                approveCmd.Parameters.AddWithValue("tipoNegocio", request.TipoNegocio);
                approveCmd.Parameters.AddWithValue("id", id);
                await approveCmd.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
            return Ok(new { sucesso = true, usuarioId, mensagem = $"Novo cadastro de {nome} aprovado." });
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Conflict(new { mensagem = "Esta conta já está vinculada a uma empresa. Atualize a lista antes de tentar novamente." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao aprovar cadastro Web {RequestId}.", id);
            return StatusCode(503, new { mensagem = "Não foi possível aprovar o cadastro. Confira as conexões Supabase e tente atualizar a lista." });
        }
    }

    [HttpPost("{id:long}/recusar")]
    public async Task<IActionResult> Recusar(long id)
    {
        if (!MasterAutorizado()) return Unauthorized(new { mensagem = "Acesso restrito ao Administrador Master." });
        if (id <= 0) return BadRequest(new { mensagem = "Cadastro inválido." });
        try
        {
            await using var db = await BancoChatCentral.Abrir(configuration);
            await using var cmd = new NpgsqlCommand("""
                UPDATE chat.solicitacao_cadastro SET status='RECUSADO',analisada_em=NOW()
                WHERE id=@id AND status='PENDENTE' RETURNING id;
                """, db);
            cmd.Parameters.AddWithValue("id", id);
            return await cmd.ExecuteScalarAsync() is null
                ? NotFound(new { mensagem = "Cadastro pendente não encontrado." })
                : Ok(new { sucesso = true, mensagem = "Cadastro recusado." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao recusar cadastro Web {RequestId}.", id);
            return StatusCode(503, new { mensagem = "Não foi possível recusar o cadastro." });
        }
    }

    private bool MasterAutorizado() => sessions.Validar(Request.Headers.Authorization.ToString());
}

public sealed class CadastroPendenteResponse
{
    public long Id { get; set; }
    public string Email { get; set; } = "";
    public string Nome { get; set; } = "";
    public long? EmpresaId { get; set; }
    public DateTime CriadaEm { get; set; }
    public bool EmailVerificado { get; set; }
}

public sealed class AprovarCadastroPendenteRequest
{
    public long EmpresaId { get; set; }
    public string TipoNegocio { get; set; } = "";
}
