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
        if (id <= 0 || request.EmpresaId <= 0 || request.UsuarioId <= 0 || request.TipoNegocio is not ("chat" or "comercial"))
            return BadRequest(new { mensagem = "Selecione empresa, tipo de negócio e usuário CATCHAT válidos." });

        try
        {
            string tenantConnection = await resolver.ObterConnectionString(request.EmpresaId);
            await using (var tenantDb = new NpgsqlConnection(tenantConnection))
            {
                await tenantDb.OpenAsync();
                await using var userCmd = new NpgsqlCommand(
                    "SELECT 1 FROM app.usuario WHERE id=@usuarioId AND empresa_id=@empresaId AND COALESCE(excluido,FALSE)=FALSE",
                    tenantDb);
                userCmd.Parameters.AddWithValue("usuarioId", request.UsuarioId);
                userCmd.Parameters.AddWithValue("empresaId", request.EmpresaId);
                if (await userCmd.ExecuteScalarAsync() is null)
                    return BadRequest(new { mensagem = "O usuário CATCHAT selecionado não existe ou está inativo na empresa." });
            }

            await using var db = await BancoChatCentral.Abrir(configuration);
            await using var transaction = await db.BeginTransactionAsync();
            Guid authUid;
            string email;
            string nome;
            await using (var requestCmd = new NpgsqlCommand("""
                SELECT r.auth_uid,r.email,r.nome,r.email_verificado_em,u.email,u.email_confirmed_at
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
                if (reader.IsDBNull(3) || reader.IsDBNull(5))
                    return Conflict(new { mensagem = "O usuário precisa confirmar o e-mail antes da aprovação." });
                authUid = reader.GetGuid(0);
                email = reader.GetString(1);
                nome = reader.GetString(2);
                if (reader.IsDBNull(4) || !string.Equals(reader.GetString(4), email, StringComparison.OrdinalIgnoreCase))
                    return Conflict(new { mensagem = "O e-mail da conta autenticada não corresponde ao cadastro." });
            }

            await using (var companyCmd = new NpgsqlCommand(
                "SELECT 1 FROM platform.empresa_catalogo WHERE id=@empresaId AND ativa=TRUE AND status='ATIVA'",
                db, transaction))
            {
                companyCmd.Parameters.AddWithValue("empresaId", request.EmpresaId);
                if (await companyCmd.ExecuteScalarAsync() is null)
                    return BadRequest(new { mensagem = "A empresa selecionada não está ativa no catálogo." });
            }

            const string insert = """
                INSERT INTO chat.usuario_auth (auth_uid,empresa_id,usuario_id,nome,email,tipo_negocio,ativo)
                VALUES (@authUid,@empresaId,@usuarioId,@nome,@email,@tipoNegocio,TRUE);
                UPDATE chat.solicitacao_cadastro
                SET status='APROVADO',auth_uid=@authUid,usuario_id=@usuarioId,empresa_id=@empresaId,
                    tipo_negocio=@tipoNegocio,analisada_em=NOW()
                WHERE id=@id AND status='PENDENTE';
                """;
            await using (var approveCmd = new NpgsqlCommand(insert, db, transaction))
            {
                approveCmd.Parameters.AddWithValue("authUid", authUid);
                approveCmd.Parameters.AddWithValue("empresaId", request.EmpresaId);
                approveCmd.Parameters.AddWithValue("usuarioId", request.UsuarioId);
                approveCmd.Parameters.AddWithValue("nome", nome);
                approveCmd.Parameters.AddWithValue("email", email);
                approveCmd.Parameters.AddWithValue("tipoNegocio", request.TipoNegocio);
                approveCmd.Parameters.AddWithValue("id", id);
                await approveCmd.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
            return Ok(new { sucesso = true, mensagem = $"Cadastro de {nome} aprovado." });
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Conflict(new { mensagem = "A conta ou o usuário CATCHAT já está vinculado. Atualize a lista antes de tentar novamente." });
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
    public long UsuarioId { get; set; }
    public string TipoNegocio { get; set; } = "";
}
