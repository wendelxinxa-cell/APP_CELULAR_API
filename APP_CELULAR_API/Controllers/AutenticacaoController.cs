using APP_CELULAR_API.Models;
using APP_CELULAR_API.Services;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;

namespace APP_CELULAR_API.Controllers;

[ApiController]
[Route("api/autenticacao")]
public class AutenticacaoController : ControllerBase
{
    private readonly IEmpresaDatabaseResolver _resolver;
    private readonly TenantSessionStore _sessions;
    private readonly ILogger<AutenticacaoController> _logger;
    private readonly IConfiguration _configuration;

    public AutenticacaoController(
        IEmpresaDatabaseResolver resolver,
        TenantSessionStore sessions,
        ILogger<AutenticacaoController> logger,
        IConfiguration configuration)
    {
        _resolver = resolver;
        _sessions = sessions;
        _logger = logger;
        _configuration = configuration;
    }

    [HttpPost("login-supabase")]
    public async Task<IActionResult> LoginSupabase([FromBody] LoginSupabaseApiRequest request, [FromServices] IHttpClientFactory clients)
    {
        if (request.EmpresaId <= 0 || string.IsNullOrWhiteSpace(request.Identificador) || string.IsNullOrWhiteSpace(request.Senha) || request.ChaveInstalacao == Guid.Empty)
            return BadRequest(new { mensagem = "Informe apelido ou e-mail, senha, empresa e identidade deste aparelho." });

        var supabaseUrl = (_configuration["SupabaseAuth:Url"] ?? "https://zuqwlupzxfjqswfseuat.supabase.co").TrimEnd('/');
        var anonKey = _configuration["SupabaseAuth:PublishableKey"] ?? "sb_publishable_gFiEwBm-VXwx5MsMzNxo7g_1scloOC-";
        try
        {
            using var loginRequest = new HttpRequestMessage(HttpMethod.Post, $"{supabaseUrl}/functions/v1/master-user-invite")
            {
                Content = JsonContent.Create(new { action = "signIn", identificador = request.Identificador.Trim(), senha = request.Senha, empresaId = request.EmpresaId })
            };
            loginRequest.Headers.TryAddWithoutValidation("apikey", anonKey);
            loginRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", anonKey);
            using var loginResponse = await clients.CreateClient().SendAsync(loginRequest);
            using var loginJson = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
            if (!loginResponse.IsSuccessStatusCode)
            {
                var message = loginJson.RootElement.TryGetProperty("message", out var errorMessage) ? errorMessage.GetString() : null;
                return Unauthorized(new { codigo = "AUTH_SUPABASE", mensagem = message ?? "Apelido/e-mail ou senha incorretos." });
            }
            if (!loginJson.RootElement.TryGetProperty("session", out var tokenSession) ||
                !tokenSession.TryGetProperty("access_token", out var accessTokenElement))
                return StatusCode(502, new { mensagem = "O Supabase não retornou uma sessão válida." });
            var accessToken = accessTokenElement.GetString();
            if (string.IsNullOrWhiteSpace(accessToken)) return StatusCode(502, new { mensagem = "O Supabase não retornou uma sessão válida." });

            using var identityRequest = new HttpRequestMessage(HttpMethod.Post, $"{supabaseUrl}/rest/v1/rpc/obter_identidade")
            {
                Content = JsonContent.Create(new { p_empresa_id = request.EmpresaId })
            };
            identityRequest.Headers.TryAddWithoutValidation("apikey", anonKey);
            identityRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            using var identityResponse = await clients.CreateClient().SendAsync(identityRequest);
            if (!identityResponse.IsSuccessStatusCode) return StatusCode(502, new { mensagem = "Não foi possível validar o vínculo Supabase." });
            using var identityJson = JsonDocument.Parse(await identityResponse.Content.ReadAsStringAsync());
            var identity = identityJson.RootElement.ValueKind == JsonValueKind.Array && identityJson.RootElement.GetArrayLength() > 0
                ? identityJson.RootElement[0] : default;
            if (identity.ValueKind != JsonValueKind.Object || !identity.TryGetProperty("usuario_id", out var idElement) || !idElement.TryGetInt64(out var usuarioId))
                return StatusCode(403, new { codigo = "USUARIO_NAO_VINCULADO", mensagem = "Este apelido ainda não foi aprovado e vinculado por Zeus para esta empresa." });

            string connectionString;
            try { connectionString = await _resolver.ObterConnectionString(request.EmpresaId); }
            catch (Exception ex)
            {
                _logger.LogWarning("Database configuration could not be resolved for company {EmpresaId}. Reason: {Reason}", request.EmpresaId, ex.Message);
                return StatusCode(503, new { mensagem = "Configuração do banco da empresa incompleta ou inválida." });
            }

            await using var db = new NpgsqlConnection(connectionString);
            await db.OpenAsync();
            await GarantirColunaTipoNegocio(db);
            const string sql = "SELECT id, COALESCE(eh_master,FALSE), upper(trim(COALESCE(funcao,''))), COALESCE(aprovado,TRUE), COALESCE(excluido,FALSE), COALESCE(bloqueado_por_master,FALSE), COALESCE(tipo_negocio,'sucata'), trim(nome) FROM app.usuario WHERE empresa_id=@empresaId AND id=@usuarioId LIMIT 1;";
            await using var cmd = new NpgsqlCommand(sql, db);
            cmd.Parameters.AddWithValue("empresaId", request.EmpresaId);
            cmd.Parameters.AddWithValue("usuarioId", usuarioId);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return NotFound(new { codigo = "USUARIO_NAO_CADASTRADO", mensagem = "O usuário vinculado não existe nesta empresa." });
            long legacyId = reader.GetInt64(0);
            bool ehMaster = reader.GetBoolean(1);
            string funcao = reader.GetString(2);
            bool aprovado = reader.GetBoolean(3);
            bool excluido = reader.GetBoolean(4);
            bool bloqueado = reader.GetBoolean(5);
            string tipoNegocio = reader.GetString(6);
            string nome = reader.GetString(7);
            await reader.CloseAsync();
            if (excluido || bloqueado) return StatusCode(403, new { codigo = "USUARIO_BLOQUEADO", mensagem = "Este usuário está bloqueado. Solicite liberação a Zeus." });
            if (!aprovado) return StatusCode(403, new { codigo = "USUARIO_PENDENTE", mensagem = "Este usuário aguarda aprovação da empresa." });

            var (dispositivoId, dispositivoAtivo) = await ObterOuSolicitarDispositivo(db, request.EmpresaId, legacyId, request.ChaveInstalacao, request.NomeDispositivo);
            if (!dispositivoAtivo) return StatusCode(403, new { codigo = "APARELHO_PENDENTE", mensagem = "Este aparelho aguarda aprovação de Zeus." });
            var (token, session) = _sessions.Create(request.EmpresaId, legacyId, ehMaster || funcao == "ADMINISTRADOR", dispositivoId);
            return Ok(new LoginApiResponse { Nome = nome, Token = token, EmpresaId = session.EmpresaId, UsuarioId = session.UsuarioId, DispositivoId = session.DispositivoId, Funcao = funcao, TipoNegocio = tipoNegocio == "chat" ? "chat" : "sucata", EhMaster = ehMaster, ExpiraEm = session.ExpiresAt });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha no login com Supabase para empresa {EmpresaId}.", request.EmpresaId);
            return StatusCode(503, new { mensagem = "Não foi possível autenticar pelo Supabase. Confira a conexão e tente novamente." });
        }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginApiRequest request)
    {
        if (request.EmpresaId <= 0 || string.IsNullOrWhiteSpace(request.Nome) || string.IsNullOrWhiteSpace(request.SenhaHash) || request.ChaveInstalacao == Guid.Empty)
            return BadRequest(new { mensagem = "Informe usuário, senha, empresa e a identidade deste aparelho." });

        string connectionString;
        try
        {
            connectionString = await _resolver.ObterConnectionString(request.EmpresaId);
        }
        catch (Exception ex)
        {
            // Resolver messages identify the missing setting, and never include its value.
            _logger.LogWarning(
                "Database configuration could not be resolved for company {EmpresaId}. Reason: {Reason}",
                request.EmpresaId,
                ex.Message);
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new { mensagem = "Configuração do banco da empresa incompleta ou inválida." });
        }

        await using var db = new NpgsqlConnection(connectionString);
        await db.OpenAsync();
        await GarantirColunaTipoNegocio(db);
        const string sql = """
            SELECT id, senha_hash, COALESCE(eh_master, FALSE), upper(trim(COALESCE(funcao, ''))),
                   COALESCE(aprovado, TRUE), COALESCE(excluido, FALSE), COALESCE(bloqueado_por_master, FALSE),
                   COALESCE(tipo_negocio, 'sucata')
            FROM app.usuario
            WHERE empresa_id = @empresaId
              AND lower(trim(nome)) = lower(trim(@nome))
            ORDER BY id
            LIMIT 1;
            """;
        await using var cmd = new NpgsqlCommand(sql, db);
        cmd.Parameters.AddWithValue("empresaId", request.EmpresaId);
        cmd.Parameters.AddWithValue("nome", request.Nome.Trim());
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return NotFound(new { codigo = "USUARIO_NAO_CADASTRADO", mensagem = "Este usuário não está cadastrado na empresa selecionada." });

        long usuarioId = reader.GetInt64(0);
        string senhaHashCadastrada = reader.GetString(1);
        bool ehMaster = reader.GetBoolean(2);
        string funcao = reader.GetString(3);
        bool usuarioAprovado = reader.GetBoolean(4);
        bool usuarioExcluido = reader.GetBoolean(5);
        bool usuarioBloqueado = reader.GetBoolean(6);
        string tipoNegocio = reader.GetString(7);
        await reader.CloseAsync();
        byte[] senhaCadastrada = Encoding.UTF8.GetBytes(senhaHashCadastrada.Trim().ToUpperInvariant());
        byte[] senhaInformada = Encoding.UTF8.GetBytes(request.SenhaHash.Trim().ToUpperInvariant());
        if (!CryptographicOperations.FixedTimeEquals(senhaCadastrada, senhaInformada))
            return Unauthorized(new { codigo = "SENHA_INCORRETA", mensagem = "A senha informada está incorreta." });
        if (usuarioExcluido || usuarioBloqueado)
            return StatusCode(403, new { codigo = "USUARIO_BLOQUEADO", mensagem = "Este usuário está bloqueado. Solicite a liberação ao Administrador Master (Zeus)." });
        if (!usuarioAprovado)
            return StatusCode(403, new { codigo = "USUARIO_PENDENTE", mensagem = "Cadastro pendente de aprovação do Administrador Master (Zeus). O acesso será liberado após a aprovação." });

        var (dispositivoId, dispositivoAtivo) = await ObterOuSolicitarDispositivo(
            db, request.EmpresaId, usuarioId, request.ChaveInstalacao, request.NomeDispositivo);
        if (!dispositivoAtivo)
            return StatusCode(403, new { codigo = "APARELHO_PENDENTE", mensagem = "Este aparelho aguarda aprovação do Administrador Master (Zeus). O acesso será liberado após a aprovação." });

        bool ehAdministrador = ehMaster || funcao == "ADMINISTRADOR";
        var (token, session) = _sessions.Create(request.EmpresaId, usuarioId, ehAdministrador, dispositivoId);
        return Ok(new LoginApiResponse
        {
            Token = token,
            EmpresaId = session.EmpresaId,
            UsuarioId = session.UsuarioId,
            DispositivoId = session.DispositivoId,
            Funcao = funcao,
            TipoNegocio = tipoNegocio == "chat" ? "chat" : "sucata",
            EhMaster = ehMaster,
            ExpiraEm = session.ExpiresAt
        });
    }

    private static async Task GarantirColunaTipoNegocio(NpgsqlConnection db)
    {
        const string sql = "ALTER TABLE app.usuario ADD COLUMN IF NOT EXISTS tipo_negocio TEXT NOT NULL DEFAULT 'sucata';";
        await using var command = new NpgsqlCommand(sql, db);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<(long Id, bool Ativo)> ObterOuSolicitarDispositivo(
        NpgsqlConnection db, long empresaId, long usuarioId, Guid chaveInstalacao, string? nome)
    {
        const string localizar = "SELECT id, ativo, usuario_id, usuario_id_solicitado FROM app.dispositivo WHERE empresa_id=@empresaId AND chave_instalacao=@chave LIMIT 1;";
        await using (var cmd = new NpgsqlCommand(localizar, db))
        {
            cmd.Parameters.AddWithValue("empresaId", empresaId);
            cmd.Parameters.AddWithValue("chave", chaveInstalacao);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                long id = reader.GetInt64(0);
                bool ativo = reader.GetBoolean(1);
                long? usuarioVinculado = reader.IsDBNull(2) ? null : reader.GetInt64(2);
                long? usuarioSolicitado = reader.IsDBNull(3) ? null : reader.GetInt64(3);
                await reader.CloseAsync();

                if (usuarioVinculado is null)
                {
                    const string vincularLegado = "UPDATE app.dispositivo SET usuario_id=@usuarioId WHERE id=@id AND empresa_id=@empresaId AND usuario_id IS NULL;";
                    await using var atualizar = new NpgsqlCommand(vincularLegado, db);
                    atualizar.Parameters.AddWithValue("usuarioId", usuarioId);
                    atualizar.Parameters.AddWithValue("id", id);
                    atualizar.Parameters.AddWithValue("empresaId", empresaId);
                    await atualizar.ExecuteNonQueryAsync();
                    return (id, ativo);
                }

                if (usuarioVinculado == usuarioId)
                {
                    if (ativo) return (id, true);

                    // Um aparelho que já foi substituído continua vinculado ao usuário,
                    // mas precisa gerar uma nova solicitação se tentar voltar a acessar.
                    const string solicitarReativacao = "UPDATE app.dispositivo SET solicitado_em=COALESCE(solicitado_em,NOW()), nome_dispositivo=@nome WHERE id=@id AND empresa_id=@empresaId AND usuario_id=@usuarioId AND ativo=FALSE;";
                    await using var reativar = new NpgsqlCommand(solicitarReativacao, db);
                    reativar.Parameters.AddWithValue("nome", string.IsNullOrWhiteSpace(nome) ? "Aparelho" : nome.Trim());
                    reativar.Parameters.AddWithValue("id", id);
                    reativar.Parameters.AddWithValue("empresaId", empresaId);
                    reativar.Parameters.AddWithValue("usuarioId", usuarioId);
                    await reativar.ExecuteNonQueryAsync();
                    return (id, false);
                }
                if (usuarioSolicitado == usuarioId) return (id, false);

                // A identidade da instalação não muda de proprietário sem aprovação
                // explícita; a solicitação fica auditável sem apagar mensagens.
                if (usuarioSolicitado is null)
                {
                    const string solicitarTroca = "UPDATE app.dispositivo SET usuario_id_solicitado=@usuarioId, solicitado_em=NOW(), nome_dispositivo=@nome WHERE id=@id AND empresa_id=@empresaId AND usuario_id=@usuarioVinculado AND usuario_id_solicitado IS NULL;";
                    await using var solicitar = new NpgsqlCommand(solicitarTroca, db);
                    solicitar.Parameters.AddWithValue("usuarioId", usuarioId);
                    solicitar.Parameters.AddWithValue("nome", string.IsNullOrWhiteSpace(nome) ? "Aparelho" : nome.Trim());
                    solicitar.Parameters.AddWithValue("id", id);
                    solicitar.Parameters.AddWithValue("empresaId", empresaId);
                    solicitar.Parameters.AddWithValue("usuarioVinculado", usuarioVinculado.Value);
                    await solicitar.ExecuteNonQueryAsync();
                }
                return (id, false);
            }
        }

        // Vincula uma única instalação legada ao registro ativo existente. Instalações seguintes
        // precisam ser aprovadas por um administrador já autorizado.
        const string legado = "SELECT id FROM app.dispositivo WHERE empresa_id=@empresaId AND usuario_id=@usuarioId AND ativo=TRUE AND chave_instalacao IS NULL ORDER BY id LIMIT 1;";
        await using (var cmd = new NpgsqlCommand(legado, db))
        {
            cmd.Parameters.AddWithValue("empresaId", empresaId);
            cmd.Parameters.AddWithValue("usuarioId", usuarioId);
            var legacyId = await cmd.ExecuteScalarAsync();
            if (legacyId is not null)
            {
                const string vincular = "UPDATE app.dispositivo SET chave_instalacao=@chave, nome_dispositivo=@nome, usuario_id=@usuarioId WHERE id=@id AND empresa_id=@empresaId RETURNING id;";
                await using var update = new NpgsqlCommand(vincular, db);
                update.Parameters.AddWithValue("chave", chaveInstalacao);
                update.Parameters.AddWithValue("nome", string.IsNullOrWhiteSpace(nome) ? "Aparelho" : nome.Trim());
                update.Parameters.AddWithValue("usuarioId", usuarioId);
                update.Parameters.AddWithValue("id", Convert.ToInt64(legacyId));
                update.Parameters.AddWithValue("empresaId", empresaId);
                var id = await update.ExecuteScalarAsync();
                if (id is not null) return (Convert.ToInt64(id), true);
            }
        }

        const string existe = "SELECT EXISTS(SELECT 1 FROM app.dispositivo WHERE empresa_id=@empresaId);";
        bool jaTemDispositivo;
        await using (var cmd = new NpgsqlCommand(existe, db))
        {
            cmd.Parameters.AddWithValue("empresaId", empresaId);
            jaTemDispositivo = (bool)(await cmd.ExecuteScalarAsync() ?? false);
        }

        bool primeiro = !jaTemDispositivo;
        const string inserir = "INSERT INTO app.dispositivo (empresa_id, identificador, ativo, chave_instalacao, nome_dispositivo, usuario_id, solicitado_em) VALUES (@empresaId, @identificador, @ativo, @chave, @nome, @usuarioId, CASE WHEN @ativo THEN NULL ELSE NOW() END) RETURNING id;";
        await using (var cmd = new NpgsqlCommand(inserir, db))
        {
            cmd.Parameters.AddWithValue("empresaId", empresaId);
            cmd.Parameters.AddWithValue("identificador", chaveInstalacao.ToString("D"));
            cmd.Parameters.AddWithValue("ativo", primeiro);
            cmd.Parameters.AddWithValue("chave", chaveInstalacao);
            cmd.Parameters.AddWithValue("nome", string.IsNullOrWhiteSpace(nome) ? "Aparelho" : nome.Trim());
            cmd.Parameters.AddWithValue("usuarioId", usuarioId);
            long id = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            return (id, primeiro);
        }
    }

    [HttpPost("sair")]
    public IActionResult Sair()
    {
        _sessions.Revoke(Request.Headers.Authorization.ToString());
        return Ok(new { sucesso = true });
    }

    [HttpGet("health")]
    public IActionResult Health() => Ok(new { online = true });
}
