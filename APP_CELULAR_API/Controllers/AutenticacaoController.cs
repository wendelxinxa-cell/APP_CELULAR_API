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
            string loginEmail = request.Identificador.Trim();
            if (!loginEmail.Contains('@'))
            {
                string centralConnection = _configuration.GetConnectionString("CadastroCentral") ?? "";
                if (string.IsNullOrWhiteSpace(centralConnection))
                    return StatusCode(503, new { mensagem = "O banco central não está configurado para localizar o nome de login." });

                await using var central = new NpgsqlConnection(EmpresaDatabaseResolver.NormalizarConnectionString(centralConnection));
                await central.OpenAsync();
                const string lookupSql = """
                    SELECT DISTINCT empresa_id, lower(trim(email))
                    FROM chat.usuario_auth
                    WHERE COALESCE(ativo,FALSE)=TRUE AND trim(COALESCE(email,''))<>''
                      AND (lower(trim(COALESCE(username,'')))=lower(trim(@identifier))
                           OR lower(trim(COALESCE(nome,'')))=lower(trim(@identifier)))
                    ORDER BY empresa_id, 2 LIMIT 20;
                    """;
                await using (var lookup = new NpgsqlCommand(lookupSql, central))
                {
                    lookup.Parameters.AddWithValue("identifier", request.Identificador.Trim());
                    await using var centralReader = await lookup.ExecuteReaderAsync();
                    var matches = new List<(long EmpresaId, string Email)>();
                    while (await centralReader.ReadAsync()) matches.Add((centralReader.GetInt64(0), centralReader.GetString(1)));
                    if (matches.Count == 0)
                    {
                        await centralReader.CloseAsync();
                        await using var pending = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM chat.solicitacao_cadastro WHERE lower(trim(COALESCE(username,'')))=lower(trim(@identifier)) AND status='PENDENTE')", central);
                        pending.Parameters.AddWithValue("identifier", request.Identificador.Trim());
                        if ((bool)(await pending.ExecuteScalarAsync() ?? false))
                            return StatusCode(403, new { codigo = "USUARIO_PENDENTE", mensagem = "Seu cadastro está aguardando aprovação de Zeus." });
                        return NotFound(new { codigo = "USUARIO_NAO_CADASTRADO", mensagem = "Nome de login não encontrado nesta empresa. Confira o nome ou entre com seu e-mail." });
                    }
                    var companyMatches = matches.Where(x => x.EmpresaId == request.EmpresaId).ToList();
                    var selected = companyMatches.Count > 0 ? companyMatches : matches;
                    var distinctCompanies = selected.Select(x => x.EmpresaId).Distinct().ToList();
                    var distinctEmails = selected.Select(x => x.Email).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    if (distinctCompanies.Count != 1 || distinctEmails.Count != 1)
                        return BadRequest(new { codigo = "LOGIN_AMBIGUO", mensagem = "Esse nome está associado a mais de uma conta. Entre com seu e-mail." });
                    request.EmpresaId = distinctCompanies[0];
                    loginEmail = distinctEmails[0];
                }
            }

            using var loginRequest = new HttpRequestMessage(HttpMethod.Post, $"{supabaseUrl}/auth/v1/token?grant_type=password")
            {
                Content = JsonContent.Create(new { email = loginEmail, password = request.Senha })
            };
            loginRequest.Headers.TryAddWithoutValidation("apikey", anonKey);
            using var loginResponse = await clients.CreateClient().SendAsync(loginRequest);
            using var loginJson = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
            if (!loginResponse.IsSuccessStatusCode)
            {
                return Unauthorized(new { codigo = "AUTH_SUPABASE", mensagem = "Nome, e-mail ou senha incorretos." });
            }
            if (!loginJson.RootElement.TryGetProperty("access_token", out var accessTokenElement))
                return StatusCode(502, new { mensagem = "O Supabase não retornou uma sessão válida." });
            var accessToken = accessTokenElement.GetString();
            if (string.IsNullOrWhiteSpace(accessToken)) return StatusCode(502, new { mensagem = "O Supabase não retornou uma sessão válida." });

            using var identityRequest = new HttpRequestMessage(HttpMethod.Post, $"{supabaseUrl}/rest/v1/rpc/obter_identidade")
            {
                Content = JsonContent.Create(new { p_empresa_id = request.EmpresaId })
            };
            identityRequest.Headers.TryAddWithoutValidation("apikey", anonKey);
            identityRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            identityRequest.Headers.TryAddWithoutValidation("Accept-Profile", "chat");
            identityRequest.Headers.TryAddWithoutValidation("Content-Profile", "chat");
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
            const string sql = "SELECT id, COALESCE(eh_master,FALSE), upper(trim(COALESCE(funcao,''))), COALESCE(aprovado,TRUE), COALESCE(excluido,FALSE), COALESCE(bloqueado_por_master,FALSE), COALESCE(tipo_negocio,'sucata'), trim(nome), COALESCE(icones_permitidos,'[\"chat\",\"agenda\",\"pessoas\",\"produtos\",\"vendas\",\"compras\",\"financeiro\",\"outros\",\"usuarios\",\"configuracoes\"]') FROM app.usuario WHERE empresa_id=@empresaId AND id=@usuarioId LIMIT 1;";
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
            var iconesPermitidos = LerIcones(reader.GetString(8));
            await reader.CloseAsync();
            var configuracaoEmpresa = await ObterConfiguracaoEmpresaAsync(request.EmpresaId, tipoNegocio);
            tipoNegocio = configuracaoEmpresa.TipoNegocio;
            if (excluido || bloqueado) return StatusCode(403, new { codigo = "USUARIO_BLOQUEADO", mensagem = "Este usuário está bloqueado. Solicite liberação a Zeus." });
            if (!aprovado) return StatusCode(403, new { codigo = "USUARIO_PENDENTE", mensagem = "Este usuário aguarda aprovação da empresa." });

            var (dispositivoId, dispositivoAtivo) = await ObterOuSolicitarDispositivo(db, request.EmpresaId, legacyId, request.ChaveInstalacao, request.NomeDispositivo);
            if (!dispositivoAtivo) return StatusCode(403, new { codigo = "APARELHO_PENDENTE", mensagem = "Este aparelho aguarda aprovação de Zeus." });

            // A senha acabou de ser validada pelo Supabase. Mantenha o hash
            // legado operacional sincronizado para que o desbloqueio biométrico
            // do app consiga renovar a sessão pela rota de compatibilidade.
            string legacyPasswordHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Senha)));
            await using (var updatePassword = new NpgsqlCommand(
                "UPDATE app.usuario SET senha_hash=@senhaHash WHERE id=@usuarioId AND empresa_id=@empresaId AND COALESCE(aprovado,TRUE)=TRUE AND COALESCE(excluido,FALSE)=FALSE",
                db))
            {
                updatePassword.Parameters.AddWithValue("senhaHash", legacyPasswordHash);
                updatePassword.Parameters.AddWithValue("usuarioId", legacyId);
                updatePassword.Parameters.AddWithValue("empresaId", request.EmpresaId);
                await updatePassword.ExecuteNonQueryAsync();
            }

            var (token, session) = _sessions.Create(request.EmpresaId, legacyId, ehMaster || funcao == "ADMINISTRADOR", dispositivoId);
            return Ok(new LoginApiResponse { Nome = nome, Token = token, EmpresaId = session.EmpresaId, UsuarioId = session.UsuarioId, DispositivoId = session.DispositivoId, Funcao = funcao, TipoNegocio = tipoNegocio, NichoAgendamento = configuracaoEmpresa.NichoAgendamento, EhMaster = ehMaster, IconesPermitidos = iconesPermitidos, ExpiraEm = session.ExpiresAt });
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
                   COALESCE(tipo_negocio, 'sucata'), COALESCE(icones_permitidos,'["chat","agenda","pessoas","produtos","vendas","compras","financeiro","outros","usuarios","configuracoes"]')
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
        var iconesPermitidos = LerIcones(reader.GetString(8));
        await reader.CloseAsync();
        var configuracaoEmpresa = await ObterConfiguracaoEmpresaAsync(request.EmpresaId, tipoNegocio);
        tipoNegocio = configuracaoEmpresa.TipoNegocio;
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
            TipoNegocio = tipoNegocio,
            NichoAgendamento = configuracaoEmpresa.NichoAgendamento,
            EhMaster = ehMaster,
            IconesPermitidos = iconesPermitidos,
            ExpiraEm = session.ExpiresAt
        });
    }

    private async Task<(string TipoNegocio, string? NichoAgendamento)> ObterConfiguracaoEmpresaAsync(long empresaId, string fallback)
    {
        string connectionString = _configuration.GetConnectionString("CadastroCentral") ?? "";
        if (string.IsNullOrWhiteSpace(connectionString)) return (NormalizarTipoNegocio(fallback), null);
        try
        {
            await using var central = new NpgsqlConnection(EmpresaDatabaseResolver.NormalizarConnectionString(connectionString));
            await central.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT tipo_negocio,nicho_agendamento FROM platform.empresa_catalogo WHERE id=@empresa AND ativa=TRUE AND status='ATIVA'", central);
            cmd.Parameters.AddWithValue("empresa", empresaId);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return (NormalizarTipoNegocio(fallback), null);
            string tipo = reader.IsDBNull(0) ? "comercial" : reader.GetString(0);
            string? nicho = reader.IsDBNull(1) ? null : reader.GetString(1);
            return (tipo is "chat" or "agendamento" ? tipo : NormalizarTipoNegocio(fallback), tipo == "agendamento" ? nicho : null);
        }
        catch
        {
            return (NormalizarTipoNegocio(fallback), null);
        }
    }

    private static string NormalizarTipoNegocio(string valor) => valor switch
    {
        "chat" => "chat",
        "agendamento" => "agendamento",
        _ => "sucata"
    };

    private static async Task GarantirColunaTipoNegocio(NpgsqlConnection db)
    {
        const string sql = "ALTER TABLE app.usuario ADD COLUMN IF NOT EXISTS tipo_negocio TEXT NOT NULL DEFAULT 'sucata'; ALTER TABLE app.usuario ADD COLUMN IF NOT EXISTS icones_permitidos TEXT NOT NULL DEFAULT '[\"chat\",\"agenda\",\"pessoas\",\"produtos\",\"vendas\",\"compras\",\"financeiro\",\"outros\",\"usuarios\",\"configuracoes\"]';";
        await using var command = new NpgsqlCommand(sql, db);
        await command.ExecuteNonQueryAsync();
    }

    private static List<string> LerIcones(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new(); }
        catch (JsonException) { return new(); }
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
