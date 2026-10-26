using APP_CELULAR_API.Services;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Net.Http.Json;

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
    public async Task<IActionResult> Aprovar(long id, [FromBody] AprovarCadastroPendenteRequest request, IHttpClientFactory httpClientFactory)
    {
        if (!MasterAutorizado()) return Unauthorized(new { mensagem = "Acesso restrito ao Administrador Master." });
        if (id <= 0 || request.EmpresaId <= 0 || request.TipoNegocio is not ("chat" or "comercial" or "agendamento"))
            return BadRequest(new { mensagem = "Selecione uma empresa e um tipo de negócio válidos." });

        try
        {
            string tenantConnection;
            try { tenantConnection = await resolver.ObterConnectionString(request.EmpresaId); }
            catch (InvalidOperationException)
            {
                return BadRequest(new { mensagem = "A empresa selecionada não está ativa ou configurada no sistema." });
            }

            await using var tenantDb = new NpgsqlConnection(tenantConnection);
            await tenantDb.OpenAsync();
            await using var tenantTransaction = await tenantDb.BeginTransactionAsync();

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

            const string createTenantUserSql = """
                INSERT INTO app.usuario
                    (nome,senha_hash,funcao,tipo_negocio,eh_master,aprovado,empresa_id,status_sincronizacao,
                     data_cadastro,data_alteracao,excluido,email)
                VALUES
                    (@nome,@senhaHash,'USUARIO',@tipoNegocio,FALSE,TRUE,@empresaId,'SINCRONIZADO',
                     NOW(),NOW(),FALSE,@email)
                RETURNING id;
                """;
            await using (var createTenantUser = new NpgsqlCommand(createTenantUserSql, tenantDb, tenantTransaction))
            {
                createTenantUser.Parameters.AddWithValue("nome", nome);
                createTenantUser.Parameters.AddWithValue("senhaHash", Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
                createTenantUser.Parameters.AddWithValue("tipoNegocio", request.TipoNegocio);
                createTenantUser.Parameters.AddWithValue("empresaId", request.EmpresaId);
                createTenantUser.Parameters.AddWithValue("email", email);
                usuarioId = Convert.ToInt64(await createTenantUser.ExecuteScalarAsync());
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
                INSERT INTO chat.usuario_auth (auth_uid,empresa_id,usuario_id,nome,email,tipo_negocio,ativo)
                VALUES (@authUid,@empresaId,@usuarioId,@nome,@email,@tipoNegocio,TRUE);
                """;
            await using (var createUserCmd = new NpgsqlCommand(insertUser, db, transaction))
            {
                createUserCmd.Parameters.AddWithValue("authUid", authUid);
                createUserCmd.Parameters.AddWithValue("empresaId", request.EmpresaId);
                createUserCmd.Parameters.AddWithValue("usuarioId", usuarioId);
                createUserCmd.Parameters.AddWithValue("nome", nome);
                createUserCmd.Parameters.AddWithValue("email", email);
                createUserCmd.Parameters.AddWithValue("tipoNegocio", request.TipoNegocio);
                await createUserCmd.ExecuteNonQueryAsync();
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
            await tenantTransaction.CommitAsync();
            bool linkEnviado = await EnviarLinkCriacaoSenhaAsync(httpClientFactory, email);
            string mensagem = linkEnviado
                ? $"Cadastro de {nome} aprovado. Enviamos o link para criar a senha para {email}."
                : $"Cadastro de {nome} aprovado, mas não foi possível enviar o link de senha. Solicite uma redefinição de senha para {email}.";
            return Ok(new { sucesso = true, usuarioId, emailEnviado = linkEnviado, mensagem });
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

    private async Task<bool> EnviarLinkCriacaoSenhaAsync(IHttpClientFactory httpClientFactory, string email)
    {
        string supabaseUrl = (configuration["SupabaseAuth:Url"] ?? "https://zuqwlupzxfjqswfseuat.supabase.co").TrimEnd('/');
        string publishableKey = configuration["SupabaseAuth:PublishableKey"] ?? "sb_publishable_gFiEwBm-VXwx5MsMzNxo7g_1scloOC-";
        string appUrl = (configuration["SupabaseAuth:AppUrl"] ?? "https://catchat-razor-test.onrender.com").TrimEnd('/');
        string redirectUrl = Uri.EscapeDataString($"{appUrl}/redefinir-senha");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{supabaseUrl}/auth/v1/recover?redirect_to={redirectUrl}");
        request.Headers.Add("apikey", publishableKey);
        request.Content = JsonContent.Create(new { email });
        try
        {
            using var response = await httpClientFactory.CreateClient().SendAsync(request);
            if (response.IsSuccessStatusCode) return true;
            logger.LogWarning("Supabase não aceitou o envio do link de criação de senha para o cadastro aprovado (HTTP {StatusCode}).", (int)response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Falha de rede ao enviar link de criação de senha para o cadastro aprovado.");
        }
        return false;
    }
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
