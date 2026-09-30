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
    private readonly EmailNotificacaoService _email;
    private readonly ILogger<AdministracaoEmpresasController> _logger;

    public AdministracaoEmpresasController(
        IConfiguration configuration,
        MasterSessionStore sessions,
        CatalogoCriptografia criptografia,
        IEmpresaDatabaseResolver resolver,
        EmailNotificacaoService email,
        ILogger<AdministracaoEmpresasController> logger)
    {
        _configuration = configuration;
        _sessions = sessions;
        _criptografia = criptografia;
        _resolver = resolver;
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
                    SELECT d.id, d.nome_dispositivo, d.criado_em,
                           COALESCE(solicitado.nome, vinculado.nome, 'Usuário não identificado'),
                           CASE WHEN d.usuario_id_solicitado IS NULL THEN 'Novo aparelho' ELSE 'Troca de usuário solicitada' END
                    FROM app.dispositivo d
                    LEFT JOIN app.usuario vinculado ON vinculado.id=d.usuario_id AND vinculado.empresa_id=d.empresa_id
                    LEFT JOIN app.usuario solicitado ON solicitado.id=d.usuario_id_solicitado AND solicitado.empresa_id=d.empresa_id
                    WHERE d.empresa_id=@empresaId AND (d.ativo=FALSE OR d.usuario_id_solicitado IS NOT NULL)
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
        try
        {
            var usuarios = new List<object>();
            foreach (var empresa in await EmpresasAtivas())
            {
                await using var db = new NpgsqlConnection(await _resolver.ObterConnectionString(empresa.Id));
                await db.OpenAsync();
                const string sql = "SELECT id, id_local, nome, funcao, aprovado, (COALESCE(excluido,FALSE) OR COALESCE(bloqueado_por_master,FALSE)) FROM app.usuario WHERE empresa_id=@empresaId AND COALESCE(eh_master,FALSE)=FALSE ORDER BY nome;";
                await using var cmd = new NpgsqlCommand(sql, db);
                cmd.Parameters.AddWithValue("empresaId", empresa.Id);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) usuarios.Add(new { id = reader.GetInt64(0), idLocal = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1), empresaId = empresa.Id, empresa = empresa.Nome, nome = reader.GetString(2), funcao = reader.GetString(3), aprovado = reader.GetBoolean(4), bloqueado = reader.GetBoolean(5) });
            }
            return Ok(usuarios);
        }
        catch
        {
            return StatusCode(503, new { mensagem = "Não foi possível consultar os usuários das empresas ativas. Confira a migração 003 e as conexões do catálogo." });
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
            const string findSql = "SELECT id FROM app.usuario WHERE empresa_id=@empresaId AND id_local=@idLocal ORDER BY id LIMIT 1;";
            await using var find = new NpgsqlCommand(findSql, db);
            find.Parameters.AddWithValue("idLocal", request.IdLocal);
            find.Parameters.AddWithValue("empresaId", request.EmpresaId);
            var found = await find.ExecuteScalarAsync();
            string sql = found is null
                ? "INSERT INTO app.usuario (id_local,nome,senha_hash,funcao,eh_master,aprovado,empresa_id,status_sincronizacao,data_cadastro,data_alteracao,excluido) VALUES (@idLocal,@nome,@senhaHash,@funcao,FALSE,FALSE,@empresaId,'SINCRONIZADO',@criadoEm,@alteradoEm,@bloqueado) RETURNING id;"
                : "UPDATE app.usuario SET nome=@nome,senha_hash=@senhaHash,funcao=@funcao,data_alteracao=@alteradoEm,excluido=@bloqueado WHERE id=@id AND empresa_id=@empresaId RETURNING id;";
            await using var cmd = new NpgsqlCommand(sql, db);
            cmd.Parameters.AddWithValue("nome", request.Nome.Trim());
            cmd.Parameters.AddWithValue("senhaHash", request.SenhaHash);
            cmd.Parameters.AddWithValue("funcao", request.Funcao?.Trim() ?? "USUARIO");
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
        const string sql = """
            UPDATE app.dispositivo SET usuario_id=COALESCE(usuario_id_solicitado,usuario_id), usuario_id_solicitado=NULL,
                solicitado_em=NULL, ativo=TRUE, aprovado_em=NOW()
            WHERE id=@id AND empresa_id=@empresaId AND (ativo=FALSE OR usuario_id_solicitado IS NOT NULL)
            RETURNING nome_dispositivo;
            """;
        await using var cmd = new NpgsqlCommand(sql, db);
        cmd.Parameters.AddWithValue("id", id); cmd.Parameters.AddWithValue("empresaId", empresaId);
        var nome = await cmd.ExecuteScalarAsync();
        return nome is null ? NotFound(new { mensagem = "Solicitação pendente não encontrada." }) : Ok(new { sucesso = true, mensagem = $"Aparelho {nome} aprovado." });
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
    public string Nome { get; set; } = "";
    public string SenhaHash { get; set; } = "";
    public string? Funcao { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime AlteradoEm { get; set; }
    public bool Bloqueado { get; set; }
}

public sealed class BloqueioUsuarioRequest
{
    public bool Bloqueado { get; set; }
}
