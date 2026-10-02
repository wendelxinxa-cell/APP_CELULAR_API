using System.Security.Cryptography;
using System.Text;
using System.Net.Mail;
using APP_CELULAR_API.Models;
using APP_CELULAR_API.Services;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace APP_CELULAR_API.Controllers;

[ApiController]
[Route("api/administracao")]
public sealed class AdministracaoEmpresasController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly MasterSessionStore _sessions;
    private readonly CatalogoCriptografia _criptografia;
    private readonly IEmpresaDatabaseResolver _resolver;
    private readonly TenantSessionStore _tenantSessions;
    private readonly EmailNotificacaoService _email;
    private readonly ILogger<AdministracaoEmpresasController> _logger;

    public AdministracaoEmpresasController(
        IConfiguration configuration,
        MasterSessionStore sessions,
        CatalogoCriptografia criptografia,
        IEmpresaDatabaseResolver resolver,
        TenantSessionStore tenantSessions,
        EmailNotificacaoService email,
        ILogger<AdministracaoEmpresasController> logger)
    {
        _configuration = configuration;
        _sessions = sessions;
        _criptografia = criptografia;
        _resolver = resolver;
        _tenantSessions = tenantSessions;
        _email = email;
        _logger = logger;
    }

    [HttpPost("master/login")]
    public IActionResult LoginMaster([FromBody] LoginMasterRequest request)
    {
        string? configuredUser = _configuration["MasterAdmin:Username"];
        string? configuredHash = _configuration["MasterAdmin:PasswordHash"];
        if (string.IsNullOrWhiteSpace(configuredUser) || string.IsNullOrWhiteSpace(configuredHash))
            return StatusCode(503, new { mensagem = "O acesso do Administrador Master ainda não foi configurado no servidor." });

        byte[] expected;
        try { expected = Convert.FromHexString(configuredHash.Trim()); }
        catch (FormatException) { return StatusCode(503, new { mensagem = "A configuração de acesso Master no servidor é inválida." }); }
        byte[] provided = SHA256.HashData(Encoding.UTF8.GetBytes(request.Senha ?? ""));
        bool userMatches = string.Equals(configuredUser.Trim(), request.Usuario?.Trim(), StringComparison.OrdinalIgnoreCase);
        bool passwordMatches = expected.Length == provided.Length && CryptographicOperations.FixedTimeEquals(expected, provided);
        if (!userMatches || !passwordMatches)
            return Unauthorized(new { mensagem = "Credenciais de Administrador Master inválidas." });

        var (token, expiresAt) = _sessions.Criar();
        return Ok(new { token, expiraEm = expiresAt });
    }

    [HttpGet("empresas")]
    public async Task<IActionResult> ListarEmpresas()
    {
        if (!MasterAutorizado()) return Unauthorized(new { mensagem = "Acesso restrito ao Administrador Master." });
        try
        {
            await using var db = await AbrirCatalogo();
            await using var cmd = new NpgsqlCommand(
                "SELECT id, nome, email_responsavel, status, criada_em FROM platform.empresa_catalogo ORDER BY CASE WHEN status = 'PENDENTE' THEN 0 ELSE 1 END, nome",
                db);
            await using var reader = await cmd.ExecuteReaderAsync();
            var empresas = new List<EmpresaCatalogoResponse>();
            while (await reader.ReadAsync())
                empresas.Add(new EmpresaCatalogoResponse
                {
                    Id = reader.GetInt64(0),
                    Nome = reader.GetString(1),
                    EmailResponsavel = reader.GetString(2),
                    Status = reader.GetString(3),
                    CriadaEm = reader.GetFieldValue<DateTimeOffset>(4)
                });
            return Ok(empresas);
        }
        catch
        {
            return StatusCode(503, new { mensagem = "Não foi possível acessar o catálogo central. Verifique a configuração do servidor e a migração do banco." });
        }
    }

    [HttpPost("empresas")]
    public async Task<IActionResult> CadastrarEmpresa([FromBody] CadastroEmpresaRequest request)
    {
        if (!MasterAutorizado()) return Unauthorized(new { mensagem = "Acesso restrito ao Administrador Master." });
        string nome = request.Nome?.Trim() ?? "";
        string emailResponsavel = request.EmailResponsavel?.Trim() ?? "";
        string connectionString = request.ConnectionString?.Trim() ?? "";
        string? bancoUnicoConfigurado = ObterConnectionBancoUnico();
        if (!string.IsNullOrWhiteSpace(bancoUnicoConfigurado))
            connectionString = bancoUnicoConfigurado;
        if (nome.Length is < 2 or > 120)
            return BadRequest(new { mensagem = "O nome da empresa deve ter entre 2 e 120 caracteres." });
        if (connectionString.Length is < 20 or > 2048)
            return BadRequest(new { mensagem = "Informe a conexão Supabase da empresa." });
        if (!MailAddress.TryCreate(emailResponsavel, out _))
            return BadRequest(new { mensagem = "Informe um e-mail válido do responsável pela empresa." });
        string? emailMaster = _configuration["MasterAdmin:Email"];
        bool emailMasterValido = MailAddress.TryCreate(emailMaster, out _);

        string normalized;
        try
        {
            normalized = EmpresaDatabaseResolver.NormalizarConnectionString(connectionString);
            var parsed = new NpgsqlConnectionStringBuilder(normalized);
            string? host = parsed.Host;
            if (string.IsNullOrWhiteSpace(host) ||
                (!host.EndsWith(".supabase.co", StringComparison.OrdinalIgnoreCase) &&
                 !host.EndsWith(".pooler.supabase.com", StringComparison.OrdinalIgnoreCase)))
                return BadRequest(new { mensagem = "Por segurança, o banco precisa estar hospedado no Supabase." });
        }
        catch
        {
            return BadRequest(new { mensagem = "A conexão informada não está em um formato válido." });
        }

        try
        {
            await using (var test = new NpgsqlConnection(normalized))
            {
                await test.OpenAsync();
                await using var check = new NpgsqlCommand("SELECT to_regclass('app.usuario') IS NOT NULL", test);
                if (await check.ExecuteScalarAsync() is not true)
                    return BadRequest(new { mensagem = "O banco conectado não possui o esquema do aplicativo (app.usuario). Prepare o banco da empresa antes do cadastro." });
            }

            byte[] encrypted = _criptografia.Criptografar(normalized);
            await using var db = await AbrirCatalogo();
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO platform.empresa_catalogo (nome, email_responsavel, conexao_criptografada, status, ativa, criada_por) VALUES (@nome, @email, @conexao, 'PENDENTE', FALSE, @criadaPor) RETURNING id, criada_em",
                db);
            cmd.Parameters.AddWithValue("nome", nome);
            cmd.Parameters.AddWithValue("email", emailResponsavel);
            cmd.Parameters.AddWithValue("conexao", encrypted);
            cmd.Parameters.AddWithValue("criadaPor", _configuration["MasterAdmin:Username"]!.Trim());
            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            long id = reader.GetInt64(0);
            var criadaEm = reader.GetFieldValue<DateTimeOffset>(1);
            await reader.CloseAsync();
            _resolver.InvalidarCache(id);
            // E-mail é um aviso complementar. A solicitação fica disponível na lista do
            // Master mesmo quando o servidor não tem um provedor de e-mail configurado.
            if (_email.EstaConfigurado && emailMasterValido)
            {
                try
                {
                    await _email.Enviar(emailMaster!, "Aprovação de nova empresa pendente",
                        $"Uma empresa foi cadastrada e aguarda sua aprovação.\n\nEmpresa: {nome}\nCódigo: {id}\nResponsável: {emailResponsavel}\n\nAbra o aplicativo como Administrador Master para aprovar ou manter pendente.");
                }
                catch
                {
                    // Não reverte o cadastro nem oculta o pedido pendente da tela Master.
                }
            }
            return Created($"/api/administracao/empresas/{id}", new EmpresaCatalogoResponse
            {
                Id = id,
                Nome = nome,
                EmailResponsavel = emailResponsavel,
                Status = "PENDENTE",
                CriadaEm = criadaEm
            });
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Conflict(new { mensagem = "Já existe uma empresa com esse nome." });
        }
        catch
        {
            return StatusCode(503, new { mensagem = "Não foi possível cadastrar a empresa. Confira o banco informado e a configuração do catálogo central." });
        }
    }

    [HttpPost("empresas/{id:long}/aprovar")]
    public async Task<IActionResult> AprovarEmpresa(long id)
    {
        if (!MasterAutorizado()) return Unauthorized(new { mensagem = "Acesso restrito ao Administrador Master." });
        try
        {
            await using var db = await AbrirCatalogo();
            await using var cmd = new NpgsqlCommand(
                "UPDATE platform.empresa_catalogo SET status = 'ATIVA', ativa = TRUE, aprovada_por = @aprovadaPor, aprovada_em = NOW() WHERE id = @id AND status = 'PENDENTE' RETURNING nome, email_responsavel",
                db);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("aprovadaPor", _configuration["MasterAdmin:Username"]!.Trim());
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return NotFound(new { mensagem = "Solicitação pendente não encontrada." });
            string nome = reader.GetString(0);
            string email = reader.GetString(1);
            await reader.CloseAsync();
            _resolver.InvalidarCache(id);

            bool notificacaoEnviada = false;
            try
            {
                await _email.Enviar(email, "A empresa foi aprovada para usar o aplicativo",
                    $"A empresa {nome} foi aprovada pelo Administrador Master. O código dela é {id}. O responsável já pode concluir a configuração dos usuários autorizados.");
                notificacaoEnviada = true;
            }
            catch { }
            return Ok(new { sucesso = true, notificacaoEnviada, mensagem = notificacaoEnviada ? "Empresa aprovada e responsável avisado por e-mail." : "Empresa aprovada, mas não foi possível enviar o aviso ao responsável." });
        }
        catch
        {
            return StatusCode(503, new { mensagem = "Não foi possível aprovar a empresa. Confira o catálogo central." });
        }
    }

    [HttpGet("acessos/pendentes")]
    public async Task<IActionResult> ListarAcessosPendentes()
    {
        if (!MasterAutorizado()) return Unauthorized(new { mensagem = "Acesso restrito ao usuário Zeus." });
        long? empresaEmConsulta = null;
        string etapa = "catálogo central";
        try
        {
            var empresas = await EmpresasAtivas();
            var usuarios = new List<object>();
            var dispositivos = new List<object>();
            foreach (var empresa in empresas)
            {
                empresaEmConsulta = empresa.Id;
                etapa = "conexão com a empresa";
                await using var db = new NpgsqlConnection(await _resolver.ObterConnectionString(empresa.Id));
                await db.OpenAsync();
                etapa = "consulta de usuários (migração 003)";
                const string usuariosSql = "SELECT id, nome, funcao, data_cadastro FROM app.usuario WHERE empresa_id=@empresaId AND aprovado=FALSE AND COALESCE(excluido,FALSE)=FALSE AND COALESCE(bloqueado_por_master,FALSE)=FALSE ORDER BY data_cadastro;";
                await using (var cmd = new NpgsqlCommand(usuariosSql, db))
                {
                    cmd.Parameters.AddWithValue("empresaId", empresa.Id);
                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync()) usuarios.Add(new { id = reader.GetInt64(0), empresaId = empresa.Id, empresa = empresa.Nome, nome = reader.GetString(1), funcao = reader.GetString(2), criadoEm = reader.GetDateTime(3) });
                }

                etapa = "consulta de aparelhos (migração 004)";
                const string dispositivosSql = """
                    SELECT d.id,
                           concat_ws(' · ', NULLIF(trim(d.nome_dispositivo), ''),
                               CASE WHEN d.chave_instalacao IS NOT NULL
                                    THEN 'ID ' || upper(right(replace(d.chave_instalacao::text, '-', ''), 8)) END),
                           d.criado_em,
                           COALESCE(solicitado.nome, vinculado.nome, 'Usuário não identificado'),
                           CASE WHEN d.usuario_id_solicitado IS NULL THEN 'Novo aparelho' ELSE 'Troca de usuário solicitada' END
                    FROM app.dispositivo d
                    LEFT JOIN app.usuario vinculado ON vinculado.id=d.usuario_id AND vinculado.empresa_id=d.empresa_id
                    LEFT JOIN app.usuario solicitado ON solicitado.id=d.usuario_id_solicitado AND solicitado.empresa_id=d.empresa_id
                    WHERE d.empresa_id=@empresaId AND (d.solicitado_em IS NOT NULL OR d.usuario_id_solicitado IS NOT NULL)
                    ORDER BY COALESCE(d.solicitado_em, d.criado_em), d.id;
                    """;
                await using (var cmd = new NpgsqlCommand(dispositivosSql, db))
                {
                    cmd.Parameters.AddWithValue("empresaId", empresa.Id);
                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync()) dispositivos.Add(new { id = reader.GetInt64(0), empresaId = empresa.Id, empresa = empresa.Nome, nome = reader.GetString(1), criadoEm = reader.IsDBNull(2) ? (DateTime?)null : reader.GetDateTime(2), usuario = reader.GetString(3), descricao = reader.GetString(4) });
                }
            }
            return Ok(new { usuarios, dispositivos });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha nas aprovações. Etapa: {Etapa}; EmpresaId: {EmpresaId}.", etapa, empresaEmConsulta);
            string local = empresaEmConsulta.HasValue ? $"na empresa código {empresaEmConsulta.Value}" : "no catálogo central";
            return StatusCode(503, new { mensagem = $"Falha {local} durante {etapa}. Confira a configuração correspondente no Render e as migrações indicadas." });
        }
    }

    [HttpGet("acessos/usuarios")]
    public async Task<IActionResult> ListarStatusUsuarios()
    {
        if (!MasterAutorizado()) return Unauthorized(new { mensagem = "Acesso restrito ao usuário Zeus." });
        long? empresaEmConsulta = null;
        string etapa = "catálogo central";
        try
        {
            var usuarios = new List<object>();
            foreach (var empresa in await EmpresasAtivas())
            {
                empresaEmConsulta = empresa.Id;
                etapa = "conexão com a empresa";
                await using var db = new NpgsqlConnection(await _resolver.ObterConnectionString(empresa.Id));
                await db.OpenAsync();
                await GarantirColunaTipoNegocio(db);
                etapa = "consulta de usuário e aparelho";
                const string sql = """
                    SELECT u.id,u.id_local,u.nome,u.funcao,COALESCE(u.aprovado,FALSE),
                           (COALESCE(u.excluido,FALSE) OR COALESCE(u.bloqueado_por_master,FALSE)),
                           COALESCE(u.tipo_negocio,'sucata'),
                           EXISTS (SELECT 1 FROM app.dispositivo d WHERE d.empresa_id=u.empresa_id
                               AND (d.usuario_id=u.id OR d.usuario_id_solicitado=u.id)
                               AND (d.solicitado_em IS NOT NULL OR d.usuario_id_solicitado IS NOT NULL))
                    FROM app.usuario u
                    WHERE u.empresa_id=@empresaId AND COALESCE(u.eh_master,FALSE)=FALSE ORDER BY u.nome;
                    """;
                await using var cmd = new NpgsqlCommand(sql, db);
                cmd.Parameters.AddWithValue("empresaId", empresa.Id);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) usuarios.Add(new { id = reader.GetInt64(0), idLocal = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1), empresaId = empresa.Id, empresa = empresa.Nome, nome = reader.GetString(2), funcao = reader.GetString(3), aprovado = reader.GetBoolean(4), bloqueado = reader.GetBoolean(5), tipoNegocio = reader.GetString(6), aparelhoPendente = reader.GetBoolean(7) });
            }
            return Ok(usuarios);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha na lista de usuários. Etapa: {Etapa}; EmpresaId: {EmpresaId}.", etapa, empresaEmConsulta);
            string local = empresaEmConsulta.HasValue ? $"na empresa código {empresaEmConsulta.Value}" : "no catálogo central";
            return StatusCode(503, new { mensagem = $"Falha {local} durante {etapa}. Execute a migração 003 no banco único da Empresa 1 e confira a configuração do Render." });
        }
    }

    [HttpGet("chat/contatos")]
    public async Task<IActionResult> ListarConversasChatMaster()
    {
        if (!MasterAutorizado()) return Unauthorized(new { mensagem = "Acesso restrito ao usuário Zeus." });
        try
        {
            if (await BancoChatCentral.Pronto(_configuration))
                return await ListarConversasChatCentral();

            var conversas = new List<MasterChatConversaResponse>();
            foreach (var empresa in await EmpresasAtivas())
            {
                await using var db = new NpgsqlConnection(await _resolver.ObterConnectionString(empresa.Id));
                await db.OpenAsync();
                const string sql = """
                    WITH recentes AS (
                        SELECT DISTINCT ON (LEAST(m.remetente_id,m.destinatario_id), GREATEST(m.remetente_id,m.destinatario_id))
                               m.remetente_id, m.destinatario_id, m.texto, m.foto, m.enviada_em
                        FROM app.mensagem_conversa m
                        WHERE m.empresa_id=@empresaId
                        ORDER BY LEAST(m.remetente_id,m.destinatario_id), GREATEST(m.remetente_id,m.destinatario_id), m.enviada_em DESC, m.id DESC
                    )
                    SELECT x.remetente_id, r.nome, x.destinatario_id, d.nome,
                           COALESCE(NULLIF(x.texto,''), CASE WHEN x.foto IS NOT NULL THEN '[Foto]' ELSE '' END),
                           x.enviada_em,
                           (SELECT COUNT(*)::int FROM app.mensagem_conversa u
                            WHERE u.empresa_id=@empresaId
                              AND LEAST(u.remetente_id,u.destinatario_id)=LEAST(x.remetente_id,x.destinatario_id)
                              AND GREATEST(u.remetente_id,u.destinatario_id)=GREATEST(x.remetente_id,x.destinatario_id)
                              AND u.lida_em IS NULL)
                    FROM recentes x
                    JOIN app.usuario r ON r.id=x.remetente_id AND r.empresa_id=@empresaId
                    JOIN app.usuario d ON d.id=x.destinatario_id AND d.empresa_id=@empresaId
                    ORDER BY x.enviada_em DESC;
                    """;
                await using var cmd = new NpgsqlCommand(sql, db);
                cmd.Parameters.AddWithValue("empresaId", empresa.Id);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    conversas.Add(new MasterChatConversaResponse
                    {
                        EmpresaId = empresa.Id,
                        Empresa = empresa.Nome,
                        UsuarioId = reader.GetInt64(0),
                        Usuario = reader.GetString(1),
                        ContatoId = reader.GetInt64(2),
                        Contato = reader.GetString(3),
                        UltimaMensagem = reader.GetString(4),
                        DataUltimaMensagem = reader.GetDateTime(5),
                        NaoLidas = reader.GetInt32(6)
                    });
            }
            return Ok(conversas.OrderByDescending(x => x.DataUltimaMensagem));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao listar chats para Zeus.");
            return StatusCode(503, new { mensagem = "Não foi possível carregar os chats das empresas ativas." });
        }
    }

    [HttpGet("chat/empresas/{empresaId:long}/usuarios/{usuarioId:long}/contatos/{contatoId:long}/mensagens")]
    public async Task<IActionResult> ListarMensagensChatMaster(long empresaId, long usuarioId, long contatoId)
    {
        if (!MasterAutorizado()) return Unauthorized(new { mensagem = "Acesso restrito ao usuário Zeus." });
        if (empresaId <= 0 || usuarioId <= 0 || contatoId <= 0 || usuarioId == contatoId)
            return BadRequest(new { mensagem = "Chat inválido." });

        try
        {
            if (await BancoChatCentral.Pronto(_configuration))
                return await ListarMensagensChatCentral(empresaId, usuarioId, contatoId);

            await using var db = new NpgsqlConnection(await _resolver.ObterConnectionString(empresaId));
            await db.OpenAsync();
            const string sql = """
                SELECT m.id, m.cliente_mensagem_id, m.remetente_id, r.nome, m.destinatario_id, m.texto,
                       m.foto, m.foto_nome, m.foto_tipo, m.enviada_em, m.lida_em
                FROM app.mensagem_conversa m
                JOIN app.usuario r ON r.id=m.remetente_id AND r.empresa_id=m.empresa_id
                WHERE m.empresa_id=@empresaId
                  AND ((m.remetente_id=@usuarioId AND m.destinatario_id=@contatoId)
                    OR (m.remetente_id=@contatoId AND m.destinatario_id=@usuarioId))
                ORDER BY m.enviada_em, m.id;
                """;
            await using var cmd = new NpgsqlCommand(sql, db);
            cmd.Parameters.AddWithValue("empresaId", empresaId);
            cmd.Parameters.AddWithValue("usuarioId", usuarioId);
            cmd.Parameters.AddWithValue("contatoId", contatoId);
            await using var reader = await cmd.ExecuteReaderAsync();
            var mensagens = new List<object>();
            while (await reader.ReadAsync())
                mensagens.Add(new
                {
                    id = reader.GetInt64(0),
                    clienteMensagemId = reader.IsDBNull(1) ? (Guid?)null : reader.GetGuid(1),
                    remetenteId = reader.GetInt64(2),
                    remetenteNome = reader.GetString(3),
                    destinatarioId = reader.GetInt64(4),
                    texto = reader.GetString(5),
                    foto = reader.IsDBNull(6) ? null : (byte[])reader[6],
                    fotoNome = reader.IsDBNull(7) ? null : reader.GetString(7),
                    fotoTipo = reader.IsDBNull(8) ? null : reader.GetString(8),
                    enviadaEm = reader.GetDateTime(9),
                    lidaEm = reader.IsDBNull(10) ? (DateTime?)null : reader.GetDateTime(10)
                });
            return Ok(mensagens);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao abrir chat da empresa {EmpresaId}.", empresaId);
            return StatusCode(503, new { mensagem = $"Não foi possível abrir o chat da empresa código {empresaId}." });
        }
    }

    [HttpPost("chat/empresas/{empresaId:long}/usuarios/{usuarioId:long}/contatos/{contatoId:long}/marcar-lidas")]
    public async Task<IActionResult> MarcarChatComoLidoMaster(long empresaId, long usuarioId, long contatoId)
    {
        if (!MasterAutorizado()) return Unauthorized(new { mensagem = "Acesso restrito ao usuário Zeus." });
        if (empresaId <= 0 || usuarioId <= 0 || contatoId <= 0 || usuarioId == contatoId)
            return BadRequest(new { mensagem = "Chat inválido." });

        try
        {
            if (await BancoChatCentral.Pronto(_configuration))
                return await MarcarChatComoLidoCentral(empresaId, usuarioId, contatoId);

            await using var db = new NpgsqlConnection(await _resolver.ObterConnectionString(empresaId));
            await db.OpenAsync();
            const string sql = """
                UPDATE app.mensagem_conversa SET lida_em=NOW()
                WHERE empresa_id=@empresaId
                  AND ((remetente_id=@usuarioId AND destinatario_id=@contatoId)
                    OR (remetente_id=@contatoId AND destinatario_id=@usuarioId))
                  AND lida_em IS NULL;
                """;
            await using var cmd = new NpgsqlCommand(sql, db);
            cmd.Parameters.AddWithValue("empresaId", empresaId);
            cmd.Parameters.AddWithValue("usuarioId", usuarioId);
            cmd.Parameters.AddWithValue("contatoId", contatoId);
            await cmd.ExecuteNonQueryAsync();
            return Ok(new { sucesso = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao marcar chat como lido na empresa {EmpresaId}.", empresaId);
            return StatusCode(503, new { mensagem = $"Não foi possível atualizar o chat da empresa código {empresaId}." });
        }
    }

    [HttpPost("acessos/empresas/{empresaId:long}/usuarios/{id:long}/bloqueio")]
    public async Task<IActionResult> DefinirBloqueioUsuario(long empresaId, long id, [FromBody] BloqueioUsuarioRequest request)
    {
        if (!MasterAutorizado()) return Unauthorized(new { mensagem = "Acesso restrito ao usuário Zeus." });
        if (empresaId <= 0 || id <= 0) return BadRequest(new { mensagem = "Empresa ou usuário inválido." });

        try
        {
            await using var db = new NpgsqlConnection(await _resolver.ObterConnectionString(empresaId));
            await db.OpenAsync();
            const string sql = "UPDATE app.usuario SET bloqueado_por_master=@bloqueado WHERE id=@id AND empresa_id=@empresaId AND COALESCE(eh_master,FALSE)=FALSE RETURNING nome, (COALESCE(excluido,FALSE) OR COALESCE(bloqueado_por_master,FALSE));";
            await using var cmd = new NpgsqlCommand(sql, db);
            cmd.Parameters.AddWithValue("bloqueado", request.Bloqueado);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("empresaId", empresaId);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return NotFound(new { mensagem = "Usuário não encontrado ou protegido como administrador Master." });
            string nome = reader.GetString(0);
            bool bloqueado = reader.GetBoolean(1);
            return Ok(new { sucesso = true, id, empresaId, bloqueado, mensagem = bloqueado ? $"Usuário {nome} bloqueado." : $"Usuário {nome} desbloqueado." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao alterar bloqueio do usuário {UsuarioId} na empresa {EmpresaId}.", id, empresaId);
            return StatusCode(503, new { mensagem = $"Não foi possível alterar o bloqueio do usuário na empresa código {empresaId}." });
        }
    }

    [HttpPost("acessos/usuarios")]
    public async Task<IActionResult> RegistrarUsuario([FromBody] UsuarioAcessoRequest request)
    {
        if (!MasterAutorizado()) return Unauthorized(new { mensagem = "Acesso restrito ao usuário Zeus." });
        if (request.EmpresaId <= 0 || request.IdLocal <= 0 || string.IsNullOrWhiteSpace(request.Nome) || string.IsNullOrWhiteSpace(request.SenhaHash))
            return BadRequest(new { mensagem = "Informe empresa, nome e credenciais do usuário." });
        try
        {
            await using var db = new NpgsqlConnection(await _resolver.ObterConnectionString(request.EmpresaId));
            await db.OpenAsync();
            await GarantirColunaTipoNegocio(db);
            string findSql = request.IdServidor is > 0
                ? "SELECT id FROM app.usuario WHERE empresa_id=@empresaId AND id=@idServidor LIMIT 1;"
                : "SELECT id FROM app.usuario WHERE empresa_id=@empresaId AND lower(trim(nome))=lower(trim(@nome)) AND senha_hash=@senhaHash ORDER BY id LIMIT 1;";
            await using var find = new NpgsqlCommand(findSql, db);
            find.Parameters.AddWithValue("empresaId", request.EmpresaId);
            find.Parameters.AddWithValue("nome", request.Nome.Trim());
            find.Parameters.AddWithValue("senhaHash", request.SenhaHash);
            if (request.IdServidor is > 0) find.Parameters.AddWithValue("idServidor", request.IdServidor.Value);
            var found = await find.ExecuteScalarAsync();
            if (found is null && request.IdServidor is > 0)
                return NotFound(new { mensagem = "O usuário indicado não existe nesta empresa." });
            if (found is null && request.IdServidor is null)
            {
                const string nomeEmUsoSql = "SELECT EXISTS(SELECT 1 FROM app.usuario WHERE empresa_id=@empresaId AND lower(trim(nome))=lower(trim(@nome)));";
                await using var nomeEmUso = new NpgsqlCommand(nomeEmUsoSql, db);
                nomeEmUso.Parameters.AddWithValue("empresaId", request.EmpresaId);
                nomeEmUso.Parameters.AddWithValue("nome", request.Nome.Trim());
                if ((bool)(await nomeEmUso.ExecuteScalarAsync() ?? false))
                    return Conflict(new { mensagem = "Já existe um usuário com esse nome e credenciais diferentes nesta empresa. O perfil não foi alterado." });
            }
            string sql = found is null
                ? "INSERT INTO app.usuario (id_local,nome,senha_hash,funcao,tipo_negocio,eh_master,aprovado,empresa_id,status_sincronizacao,data_cadastro,data_alteracao,excluido) VALUES (@idLocal,@nome,@senhaHash,@funcao,@tipoNegocio,FALSE,FALSE,@empresaId,'SINCRONIZADO',@criadoEm,@alteradoEm,@bloqueado) RETURNING id;"
                : "UPDATE app.usuario SET nome=@nome,senha_hash=@senhaHash,funcao=@funcao,tipo_negocio=@tipoNegocio,data_alteracao=@alteradoEm,excluido=@bloqueado WHERE id=@id AND empresa_id=@empresaId RETURNING id;";
            await using var cmd = new NpgsqlCommand(sql, db);
            cmd.Parameters.AddWithValue("nome", request.Nome.Trim());
            cmd.Parameters.AddWithValue("senhaHash", request.SenhaHash);
            cmd.Parameters.AddWithValue("funcao", request.Funcao?.Trim() ?? "USUARIO");
            cmd.Parameters.AddWithValue("tipoNegocio", request.TipoNegocio == "chat" ? "chat" : "sucata");
            cmd.Parameters.AddWithValue("empresaId", request.EmpresaId);
            cmd.Parameters.AddWithValue("alteradoEm", request.AlteradoEm);
            cmd.Parameters.AddWithValue("bloqueado", request.Bloqueado);
            if (found is not null)
                cmd.Parameters.AddWithValue("id", Convert.ToInt64(found));
            else
            {
                cmd.Parameters.AddWithValue("idLocal", request.IdLocal);
                cmd.Parameters.AddWithValue("criadoEm", request.CriadoEm);
            }
            long id = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            return Ok(new { id, empresaId = request.EmpresaId, aprovado = false });
        }
        catch
        {
            return StatusCode(503, new { mensagem = "Não foi possível registrar o usuário na empresa selecionada." });
        }
    }

    [HttpPut("acessos/empresas/{empresaId:long}/usuarios/{id:long}/tipo-negocio")]
    public async Task<IActionResult> DefinirTipoNegocio(long empresaId, long id, [FromBody] TipoNegocioUsuarioRequest request)
    {
        if (!MasterAutorizado()) return Unauthorized(new { mensagem = "Acesso restrito ao usuário Zeus." });
        if (request.TipoNegocio is not ("sucata" or "chat")) return BadRequest(new { mensagem = "Tipo de negócio inválido." });
        await using var db = new NpgsqlConnection(await _resolver.ObterConnectionString(empresaId));
        await db.OpenAsync();
        await GarantirColunaTipoNegocio(db);
        await using var cmd = new NpgsqlCommand("UPDATE app.usuario SET tipo_negocio=@tipo WHERE id=@id AND empresa_id=@empresaId AND COALESCE(eh_master,FALSE)=FALSE RETURNING nome;", db);
        cmd.Parameters.AddWithValue("tipo", request.TipoNegocio);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("empresaId", empresaId);
        var nome = await cmd.ExecuteScalarAsync();
        return nome is null ? NotFound(new { mensagem = "Usuário não encontrado." }) : Ok(new { sucesso = true, nome, tipoNegocio = request.TipoNegocio });
    }

    private static async Task GarantirColunaTipoNegocio(NpgsqlConnection db)
    {
        await using var cmd = new NpgsqlCommand("ALTER TABLE app.usuario ADD COLUMN IF NOT EXISTS tipo_negocio TEXT NOT NULL DEFAULT 'sucata';", db);
        await cmd.ExecuteNonQueryAsync();
    }

    [HttpPost("acessos/empresas/{empresaId:long}/usuarios/{id:long}/aprovar")]
    public async Task<IActionResult> AprovarUsuario(long empresaId, long id)
    {
        if (!MasterAutorizado()) return Unauthorized(new { mensagem = "Acesso restrito ao usuário Zeus." });
        await using var db = new NpgsqlConnection(await _resolver.ObterConnectionString(empresaId));
        await db.OpenAsync();
        await using var cmd = new NpgsqlCommand("UPDATE app.usuario SET aprovado=TRUE WHERE id=@id AND empresa_id=@empresaId AND aprovado=FALSE AND COALESCE(excluido,FALSE)=FALSE RETURNING nome;", db);
        cmd.Parameters.AddWithValue("id", id); cmd.Parameters.AddWithValue("empresaId", empresaId);
        var nome = await cmd.ExecuteScalarAsync();
        return nome is null ? NotFound(new { mensagem = "Solicitação pendente não encontrada." }) : Ok(new { sucesso = true, mensagem = $"Usuário {nome} aprovado." });
    }

    [HttpPost("acessos/empresas/{empresaId:long}/dispositivos/{id:long}/aprovar")]
    public async Task<IActionResult> AprovarDispositivo(long empresaId, long id)
    {
        if (!MasterAutorizado()) return Unauthorized(new { mensagem = "Acesso restrito ao usuário Zeus." });
        await using var db = new NpgsqlConnection(await _resolver.ObterConnectionString(empresaId));
        await db.OpenAsync();
        await using var transaction = await db.BeginTransactionAsync();
        const string localizar = "SELECT nome_dispositivo, COALESCE(usuario_id_solicitado, usuario_id) FROM app.dispositivo WHERE id=@id AND empresa_id=@empresaId AND (solicitado_em IS NOT NULL OR usuario_id_solicitado IS NOT NULL);";
        string nome;
        long? usuarioId;
        await using (var localizarCmd = new NpgsqlCommand(localizar, db, transaction))
        {
            localizarCmd.Parameters.AddWithValue("id", id);
            localizarCmd.Parameters.AddWithValue("empresaId", empresaId);
            await using var reader = await localizarCmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return NotFound(new { mensagem = "Solicitação pendente não encontrada." });
            nome = reader.GetString(0);
            usuarioId = reader.IsDBNull(1) ? null : reader.GetInt64(1);
        }
        if (usuarioId is null)
            return BadRequest(new { mensagem = "O aparelho não está associado a um usuário." });

        // Serializa as trocas do mesmo usuário para que duas aprovações simultâneas
        // não deixem dois aparelhos ativos.
        await using (var lockCmd = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@chave, 0));", db, transaction))
        {
            lockCmd.Parameters.AddWithValue("chave", $"{empresaId}:{usuarioId.Value}");
            await lockCmd.ExecuteNonQueryAsync();
        }

        const string confirmarSolicitacao = "SELECT id FROM app.dispositivo WHERE id=@id AND empresa_id=@empresaId AND (solicitado_em IS NOT NULL OR usuario_id_solicitado IS NOT NULL) FOR UPDATE;";
        await using (var confirmarCmd = new NpgsqlCommand(confirmarSolicitacao, db, transaction))
        {
            confirmarCmd.Parameters.AddWithValue("id", id);
            confirmarCmd.Parameters.AddWithValue("empresaId", empresaId);
            if (await confirmarCmd.ExecuteScalarAsync() is null)
                return NotFound(new { mensagem = "Solicitação pendente não encontrada." });
        }

        var aparelhosAnteriores = new List<long>();
        const string desativarAnteriores = "UPDATE app.dispositivo SET ativo=FALSE WHERE empresa_id=@empresaId AND usuario_id=@usuarioId AND ativo=TRUE AND id<>@id RETURNING id;";
        await using (var anterioresCmd = new NpgsqlCommand(desativarAnteriores, db, transaction))
        {
            anterioresCmd.Parameters.AddWithValue("empresaId", empresaId);
            anterioresCmd.Parameters.AddWithValue("usuarioId", usuarioId.Value);
            anterioresCmd.Parameters.AddWithValue("id", id);
            await using var reader = await anterioresCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) aparelhosAnteriores.Add(reader.GetInt64(0));
        }

        // Se havia mais de uma tentativa de troca pendente, aprovar este cartão
        // libera somente o aparelho escolhido e cancela as outras solicitações.
        const string cancelarOutrasSolicitacoes = "UPDATE app.dispositivo SET usuario_id_solicitado=NULL, solicitado_em=NULL, ativo=FALSE WHERE empresa_id=@empresaId AND id<>@id AND COALESCE(usuario_id_solicitado,usuario_id)=@usuarioId AND (solicitado_em IS NOT NULL OR usuario_id_solicitado IS NOT NULL);";
        await using (var cancelarCmd = new NpgsqlCommand(cancelarOutrasSolicitacoes, db, transaction))
        {
            cancelarCmd.Parameters.AddWithValue("empresaId", empresaId);
            cancelarCmd.Parameters.AddWithValue("id", id);
            cancelarCmd.Parameters.AddWithValue("usuarioId", usuarioId.Value);
            await cancelarCmd.ExecuteNonQueryAsync();
        }

        const string aprovar = "UPDATE app.dispositivo SET usuario_id=COALESCE(usuario_id_solicitado,usuario_id), usuario_id_solicitado=NULL, solicitado_em=NULL, ativo=TRUE, aprovado_em=NOW() WHERE id=@id AND empresa_id=@empresaId RETURNING id;";
        await using (var aprovarCmd = new NpgsqlCommand(aprovar, db, transaction))
        {
            aprovarCmd.Parameters.AddWithValue("id", id);
            aprovarCmd.Parameters.AddWithValue("empresaId", empresaId);
            if (await aprovarCmd.ExecuteScalarAsync() is null)
                return NotFound(new { mensagem = "Solicitação pendente não encontrada." });
        }
        await transaction.CommitAsync();

        foreach (var aparelhoAnterior in aparelhosAnteriores)
            _tenantSessions.RevokeDeviceSessions(empresaId, aparelhoAnterior);
        _tenantSessions.RevokeDeviceSessions(empresaId, id);

        return Ok(new { sucesso = true, mensagem = $"Aparelho {nome} aprovado. O aparelho anterior foi desativado e as sessões antigas foram encerradas." });
    }

    private async Task<List<(long Id, string Nome)>> EmpresasAtivas()
    {
        var empresas = new List<(long, string)>();
        string? raw = _configuration.GetConnectionString("CadastroCentral");
        if (!string.IsNullOrWhiteSpace(raw))
        {
            await using var catalogo = new NpgsqlConnection(EmpresaDatabaseResolver.NormalizarConnectionString(raw));
            await catalogo.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT id,nome FROM platform.empresa_catalogo WHERE ativa=TRUE AND status='ATIVA' ORDER BY id", catalogo);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) empresas.Add((reader.GetInt64(0), reader.GetString(1)));

            // Empresas antigas podem continuar configuradas no Render sem registro no catálogo.
            // Só usa esse modo de compatibilidade quando o catálogo não tem nenhuma empresa ativa.
            if (empresas.Count == 0)
                AdicionarEmpresasLegadas(empresas);
        }
        else
        {
            AdicionarEmpresasLegadas(empresas);
        }
        return empresas;
    }

    private async Task<IActionResult> ListarConversasChatCentral()
    {
        var nomesEmpresas = (await EmpresasAtivas()).ToDictionary(x => x.Id, x => x.Nome);
        await using var db = await BancoChatCentral.Abrir(_configuration);
        const string sql = """
            WITH recentes AS
            (
                SELECT DISTINCT ON (m.empresa_id,m.conversa_id)
                       c.empresa_id,c.id AS conversa_id,c.usuario_menor_id,c.usuario_maior_id,
                       m.remetente_id,m.remetente_nome,m.destinatario_nome,m.texto,m.foto,m.enviada_em
                FROM chat.conversa c
                JOIN chat.mensagem m ON m.empresa_id=c.empresa_id AND m.conversa_id=c.id
                WHERE c.tipo='DIRETA'
                ORDER BY m.empresa_id,m.conversa_id,m.enviada_em DESC,m.id DESC
            )
            SELECT r.empresa_id,r.usuario_menor_id,
                   CASE WHEN r.remetente_id=r.usuario_menor_id THEN r.remetente_nome ELSE r.destinatario_nome END,
                   r.usuario_maior_id,
                   CASE WHEN r.remetente_id=r.usuario_menor_id THEN r.destinatario_nome ELSE r.remetente_nome END,
                   COALESCE(NULLIF(r.texto,''),CASE WHEN r.foto IS NOT NULL THEN '[Foto]' ELSE '' END),r.enviada_em,
                   (SELECT COUNT(*)::int FROM chat.recibo_mensagem rec
                    JOIN chat.mensagem msg ON msg.empresa_id=rec.empresa_id AND msg.id=rec.mensagem_id
                    WHERE rec.empresa_id=r.empresa_id AND msg.conversa_id=r.conversa_id AND rec.lida_em IS NULL)
            FROM recentes r
            ORDER BY r.enviada_em DESC;
            """;
        await using var cmd = new NpgsqlCommand(sql, db);
        await using var reader = await cmd.ExecuteReaderAsync();
        var conversas = new List<MasterChatConversaResponse>();
        while (await reader.ReadAsync())
        {
            long empresaId = reader.GetInt64(0);
            conversas.Add(new MasterChatConversaResponse
            {
                EmpresaId = empresaId,
                Empresa = nomesEmpresas.GetValueOrDefault(empresaId, $"Empresa {empresaId}"),
                UsuarioId = reader.GetInt64(1),
                Usuario = reader.GetString(2),
                ContatoId = reader.GetInt64(3),
                Contato = reader.GetString(4),
                UltimaMensagem = reader.GetString(5),
                DataUltimaMensagem = reader.GetDateTime(6),
                NaoLidas = reader.GetInt32(7)
            });
        }
        return Ok(conversas);
    }

    private async Task<IActionResult> ListarMensagensChatCentral(long empresaId, long usuarioId, long contatoId)
    {
        await using var db = await BancoChatCentral.Abrir(_configuration);
        const string sql = """
            SELECT m.id,m.cliente_mensagem_id,m.remetente_id,m.remetente_nome,
                   CASE WHEN m.remetente_id=@usuarioId THEN @contatoId ELSE @usuarioId END,
                   m.texto,m.foto,m.foto_nome,m.foto_tipo,m.enviada_em,rec.lida_em
            FROM chat.conversa c
            JOIN chat.mensagem m ON m.empresa_id=c.empresa_id AND m.conversa_id=c.id
            LEFT JOIN chat.recibo_mensagem rec
              ON rec.empresa_id=m.empresa_id AND rec.mensagem_id=m.id
             AND rec.usuario_id=CASE WHEN m.remetente_id=@usuarioId THEN @contatoId ELSE @usuarioId END
            WHERE c.empresa_id=@empresaId AND c.tipo='DIRETA'
              AND c.usuario_menor_id=LEAST(@usuarioId,@contatoId)
              AND c.usuario_maior_id=GREATEST(@usuarioId,@contatoId)
            ORDER BY m.enviada_em,m.id;
            """;
        await using var cmd = new NpgsqlCommand(sql, db);
        cmd.Parameters.AddWithValue("empresaId", empresaId);
        cmd.Parameters.AddWithValue("usuarioId", usuarioId);
        cmd.Parameters.AddWithValue("contatoId", contatoId);
        await using var reader = await cmd.ExecuteReaderAsync();
        var mensagens = new List<object>();
        while (await reader.ReadAsync())
            mensagens.Add(new
            {
                id = reader.GetInt64(0),
                clienteMensagemId = reader.IsDBNull(1) ? (Guid?)null : reader.GetGuid(1),
                remetenteId = reader.GetInt64(2),
                remetenteNome = reader.GetString(3),
                destinatarioId = reader.GetInt64(4),
                texto = reader.GetString(5),
                foto = reader.IsDBNull(6) ? null : (byte[])reader[6],
                fotoNome = reader.IsDBNull(7) ? null : reader.GetString(7),
                fotoTipo = reader.IsDBNull(8) ? null : reader.GetString(8),
                enviadaEm = reader.GetDateTime(9),
                lidaEm = reader.IsDBNull(10) ? (DateTime?)null : reader.GetDateTime(10)
            });
        return Ok(mensagens);
    }

    private async Task<IActionResult> MarcarChatComoLidoCentral(long empresaId, long usuarioId, long contatoId)
    {
        await using var db = await BancoChatCentral.Abrir(_configuration);
        const string sql = """
            WITH conversa_atual AS
            (
                SELECT id FROM chat.conversa WHERE empresa_id=@empresaId AND tipo='DIRETA'
                  AND usuario_menor_id=LEAST(@usuarioId,@contatoId)
                  AND usuario_maior_id=GREATEST(@usuarioId,@contatoId)
            )
            UPDATE chat.recibo_mensagem r SET entregue_em=COALESCE(r.entregue_em,NOW()),lida_em=COALESCE(r.lida_em,NOW())
            FROM chat.mensagem m,conversa_atual c
            WHERE r.empresa_id=@empresaId AND r.mensagem_id=m.id AND m.empresa_id=r.empresa_id
              AND m.conversa_id=c.id AND r.lida_em IS NULL;
            """;
        await using var cmd = new NpgsqlCommand(sql, db);
        cmd.Parameters.AddWithValue("empresaId", empresaId);
        cmd.Parameters.AddWithValue("usuarioId", usuarioId);
        cmd.Parameters.AddWithValue("contatoId", contatoId);
        await cmd.ExecuteNonQueryAsync();
        return Ok(new { sucesso = true });
    }

    private void AdicionarEmpresasLegadas(List<(long Id, string Nome)> empresas)
    {
        foreach (var empresa in _configuration.GetSection("Empresas").GetChildren())
        {
            if (!long.TryParse(empresa.Key, out long id)) continue;
            string? nomeConnectionString = empresa.Value;
            if (string.IsNullOrWhiteSpace(nomeConnectionString)
                || string.IsNullOrWhiteSpace(_configuration.GetConnectionString(nomeConnectionString)))
                continue;

            empresas.Add((id, $"Empresa {id}"));
        }
    }

    private string? ObterConnectionBancoUnico()
    {
        string? nome = _configuration["Empresas:1"];
        if (string.IsNullOrWhiteSpace(nome)) return null;
        string? valor = _configuration.GetConnectionString(nome);
        return string.IsNullOrWhiteSpace(valor) ? null : EmpresaDatabaseResolver.NormalizarConnectionString(valor);
    }

    private bool MasterAutorizado() => _sessions.Validar(Request.Headers.Authorization.ToString());

    private async Task<NpgsqlConnection> AbrirCatalogo()
    {
        string? raw = _configuration.GetConnectionString("CadastroCentral");
        if (string.IsNullOrWhiteSpace(raw)) throw new InvalidOperationException("CadastroCentral não está configurado no Render.");
        var db = new NpgsqlConnection(EmpresaDatabaseResolver.NormalizarConnectionString(raw));
        await db.OpenAsync();
        return db;
    }
}

public sealed class UsuarioAcessoRequest
{
    public long EmpresaId { get; set; }
    public int IdLocal { get; set; }
    public long? IdServidor { get; set; }
    public string Nome { get; set; } = "";
    public string SenhaHash { get; set; } = "";
    public string? Funcao { get; set; }
    public string TipoNegocio { get; set; } = "sucata";
    public DateTime CriadoEm { get; set; }
    public DateTime AlteradoEm { get; set; }
    public bool Bloqueado { get; set; }
}

public sealed class TipoNegocioUsuarioRequest
{
    public string TipoNegocio { get; set; } = "sucata";
}

public sealed class BloqueioUsuarioRequest
{
    public bool Bloqueado { get; set; }
}

public sealed class MasterChatConversaResponse
{
    public long EmpresaId { get; set; }
    public string Empresa { get; set; } = "";
    public long UsuarioId { get; set; }
    public string Usuario { get; set; } = "";
    public long ContatoId { get; set; }
    public string Contato { get; set; } = "";
    public string UltimaMensagem { get; set; } = "";
    public DateTime DataUltimaMensagem { get; set; }
    public int NaoLidas { get; set; }
}
