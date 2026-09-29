using APP_CELULAR_API.Models;
using APP_CELULAR_API.Services;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace APP_CELULAR_API.Controllers;

[ApiController]
[Route("api/autenticacao")]
public class AutenticacaoController : ControllerBase
{
    private readonly IEmpresaDatabaseResolver _resolver;
    private readonly TenantSessionStore _sessions;

    public AutenticacaoController(IEmpresaDatabaseResolver resolver, TenantSessionStore sessions)
    {
        _resolver = resolver;
        _sessions = sessions;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginApiRequest request)
    {
        if (request.EmpresaId <= 0 || string.IsNullOrWhiteSpace(request.Nome) || string.IsNullOrWhiteSpace(request.SenhaHash))
            return BadRequest(new { mensagem = "Informe usuário, senha e empresa." });

        string connectionString;
        try { connectionString = _resolver.ObterConnectionString(request.EmpresaId); }
        catch (Exception) { return Unauthorized(new { mensagem = "Empresa sem acesso configurado." }); }

        await using var db = new NpgsqlConnection(connectionString);
        await db.OpenAsync();
        const string sql = """
            SELECT id, COALESCE(eh_master, FALSE), upper(trim(COALESCE(funcao, '')))
            FROM app.usuario
            WHERE empresa_id = @empresaId
              AND lower(nome) = lower(@nome)
              AND senha_hash = @senhaHash
              AND COALESCE(excluido, FALSE) = FALSE
            ORDER BY id
            LIMIT 1;
            """;
        await using var cmd = new NpgsqlCommand(sql, db);
        cmd.Parameters.AddWithValue("empresaId", request.EmpresaId);
        cmd.Parameters.AddWithValue("nome", request.Nome.Trim());
        cmd.Parameters.AddWithValue("senhaHash", request.SenhaHash);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return Unauthorized(new { mensagem = "Este usuário não está habilitado na empresa selecionada." });

        long usuarioId = reader.GetInt64(0);
        bool ehMaster = reader.GetBoolean(1);
        bool ehAdministrador = ehMaster || reader.GetString(2) == "ADMINISTRADOR";
        await reader.CloseAsync();
        var (token, session) = _sessions.Create(request.EmpresaId, usuarioId, ehAdministrador);
        return Ok(new LoginApiResponse
        {
            Token = token,
            EmpresaId = session.EmpresaId,
            UsuarioId = session.UsuarioId,
            ExpiraEm = session.ExpiresAt
        });
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
