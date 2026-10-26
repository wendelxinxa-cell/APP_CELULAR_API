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

        string etapa = "resolvendo o banco operacional da empresa";
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

            etapa = "preparando as colunas de usuário do banco operacional";
            await GarantirEsquemaUsuarioOperacionalAsync(tenantDb);

            etapa = "abrindo o catálogo central";
            await using var db = await BancoChatCentral.Abrir(configuration);
            etapa = "preparando as colunas de aprovação do catálogo central";
            await GarantirEsquemaAprovacaoCentralAsync(db);

            etapa = "verificando o vínculo do pedido com o Supabase Auth";
            Guid? authUidInicial;
            string emailInicial;
            string nomeInicial;
            long empresaOrigemId;
            await using (var initialRequestCmd = new NpgsqlCommand("""
                SELECT auth_uid,email,nome,empresa_id
                FROM chat.solicitacao_cadastro
                WHERE id=@id AND status='PENDENTE';
                """, db))
            {
                initialRequestCmd.Parameters.AddWithValue("id", id);
                await using var reader = await initialRequestCmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync()) return NotFound(new { mensagem = "Cadastro pendente não encontrado." });
                authUidInicial = reader.IsDBNull(0) ? null : reader.GetGuid(0);
                emailInicial = reader.GetString(1);
                nomeInicial = reader.GetString(2);
                empresaOrigemId = reader.GetInt64(3);
            }

            if (authUidInicial is null)
            {
                etapa = "reparando o vínculo com a conta do Supabase Auth";
                string? erroVinculo = await GarantirContaAutenticacaoVinculadaAsync(
                    httpClientFactory, emailInicial, nomeInicial, empresaOrigemId);
                if (erroVinculo is not null)
                    return Conflict(new { mensagem = $"O pedido ainda não está vinculado à conta de autenticação. {erroVinculo}" });
            }

            etapa = "iniciando as transações de aprovação";
            await using var tenantTransaction = await tenantDb.BeginTransactionAsync();
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
                if (reader.IsDBNull(0)) return Conflict(new { mensagem = "A conta de autenticação ainda não foi associada ao pedido. Reenvie a solicitação pelo formulário e tente novamente." });
                authUid = reader.GetGuid(0);
                email = reader.GetString(1);
                nome = reader.GetString(2);
                if (reader.IsDBNull(3) || !string.Equals(reader.GetString(3), email, StringComparison.OrdinalIgnoreCase))
                    return Conflict(new { mensagem = "O e-mail da conta autenticada não corresponde ao cadastro." });
            }

            etapa = "incluindo o usuário no banco operacional da empresa";
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

            etapa = "confirmando o cadastro no Supabase Auth";
            await using (var confirmCmd = new NpgsqlCommand(
                "UPDATE auth.users SET email_confirmed_at=COALESCE(email_confirmed_at,NOW()),updated_at=NOW() WHERE id=@authUid AND lower(email)=lower(@email)",
                db, transaction))
            {
                confirmCmd.Parameters.AddWithValue("authUid", authUid);
                confirmCmd.Parameters.AddWithValue("email", email);
                await confirmCmd.ExecuteNonQueryAsync();
            }

            etapa = "vinculando o usuário à empresa no catálogo central";
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

            etapa = "marcando a solicitação como aprovada";
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
            etapa = "enviando o link para criação de senha";
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
            string? sqlState = (ex as PostgresException)?.SqlState;
            logger.LogError(ex, "Falha ao aprovar cadastro Web {RequestId}. Etapa: {Etapa}; SQLSTATE: {SqlState}.", id, etapa, sqlState ?? "indisponível");
            return StatusCode(503, new { mensagem = $"Não foi possível aprovar o cadastro ao {etapa}. Confira os logs da API e tente novamente." });
        }
    }

    private static async Task GarantirEsquemaUsuarioOperacionalAsync(NpgsqlConnection db)
    {
        const string sql = """
            ALTER TABLE app.usuario ADD COLUMN IF NOT EXISTS tipo_negocio TEXT NOT NULL DEFAULT 'comercial';
            ALTER TABLE app.usuario ADD COLUMN IF NOT EXISTS email TEXT NULL;
            """;
        await using var cmd = new NpgsqlCommand(sql, db);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task GarantirEsquemaAprovacaoCentralAsync(NpgsqlConnection db)
    {
        const string sql = """
            ALTER TABLE chat.usuario_auth ADD COLUMN IF NOT EXISTS email TEXT NULL;
            ALTER TABLE chat.usuario_auth ADD COLUMN IF NOT EXISTS tipo_negocio TEXT NOT NULL DEFAULT 'comercial';
            ALTER TABLE chat.solicitacao_cadastro ADD COLUMN IF NOT EXISTS tipo_negocio TEXT NULL;
            """;
        await using var cmd = new NpgsqlCommand(sql, db);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<string?> GarantirContaAutenticacaoVinculadaAsync(
        IHttpClientFactory httpClientFactory, string email, string nome, long empresaOrigemId)
    {
        string supabaseUrl = (configuration["SupabaseAuth:Url"] ?? "https://zuqwlupzxfjqswfseuat.supabase.co").TrimEnd('/');
        string publishableKey = configuration["SupabaseAuth:PublishableKey"] ?? "sb_publishable_gFiEwBm-VXwx5MsMzNxo7g_1scloOC-";
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{supabaseUrl}/functions/v1/master-user-invite");
        request.Headers.TryAddWithoutValidation("apikey", publishableKey);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", publishableKey);
        request.Content = JsonContent.Create(new { action = "requestSignup", email, nome, empresaId = empresaOrigemId });
        try
        {
            using var response = await httpClientFactory.CreateClient().SendAsync(request);
            string content = await response.Content.ReadAsStringAsync();
            using var document = System.Text.Json.JsonDocument.Parse(content);
            var root = document.RootElement;
            bool ok = response.IsSuccessStatusCode && root.TryGetProperty("ok", out var okElement) && okElement.ValueKind == System.Text.Json.JsonValueKind.True;
            if (ok) return null;
            string? message = root.TryGetProperty("message", out var messageElement) ? messageElement.GetString() : null;
            logger.LogWarning("Não foi possível reparar o vínculo Auth do pedido para {Email}. HTTP {StatusCode}.", email, (int)response.StatusCode);
            return string.IsNullOrWhiteSpace(message)
                ? "Não foi possível criar ou localizar a conta de ativação. Reenvie o cadastro pelo formulário."
                : message;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            logger.LogWarning(ex, "Falha ao reparar o vínculo Auth do pedido para {Email}.", email);
            return "Não foi possível acessar a função de cadastro do Supabase. Tente novamente em instantes.";
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
