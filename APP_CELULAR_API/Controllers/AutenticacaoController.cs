using APP_CELULAR_API.Models;
using APP_CELULAR_API.Services;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Security.Cryptography;
using System.Text;

namespace APP_CELULAR_API.Controllers;

[ApiController]
[Route("api/autenticacao")]
public class AutenticacaoController : ControllerBase
{
    private readonly IEmpresaDatabaseResolver _resolver;
    private readonly TenantSessionStore _sessions;
    private readonly ILogger<AutenticacaoController> _logger;

    public AutenticacaoController(
        IEmpresaDatabaseResolver resolver,
        TenantSessionStore sessions,
        ILogger<AutenticacaoController> logger)
    {
        _resolver = resolver;
        _sessions = sessions;
        _logger = logger;
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
