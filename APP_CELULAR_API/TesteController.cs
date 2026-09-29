using APP_CELULAR_API.Services;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace APP_CELULAR_API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TesteController : ControllerBase
{
    private readonly IEmpresaDatabaseResolver _databaseResolver;
    private readonly TenantSessionStore _sessions;

    public TesteController(
        IEmpresaDatabaseResolver databaseResolver,
        TenantSessionStore sessions)
    {
        _databaseResolver = databaseResolver;
        _sessions = sessions;
    }

    [HttpGet]
    public async Task<IActionResult> TestarBanco(
        [FromQuery] long empresaId = 1)
    {
        if (!_sessions.TryGet(Request.Headers.Authorization.ToString(), out var session))
            return Unauthorized(new { sucesso = false, mensagem = "Sessão da API inválida ou expirada." });
        if (session.EmpresaId != empresaId)
            return StatusCode(403, new { sucesso = false, mensagem = "A sessão não pertence à empresa informada." });

        try
        {
            string connectionString =
                _databaseResolver.ObterConnectionString(
                    empresaId);

            await using var conexao =
                new NpgsqlConnection(connectionString);

            await conexao.OpenAsync();

            await using var comando =
                new NpgsqlCommand(
                    "SELECT NOW();",
                    conexao);

            object? resultado =
                await comando.ExecuteScalarAsync();

            return Ok(new
            {
                sucesso = true,
                empresaId,
                mensagem =
                    "API conectada ao Supabase da empresa com sucesso.",
                servidor = resultado
            });
        }
        catch (Exception ex)
        {
            return StatusCode(
                500,
                new
                {
                    sucesso = false,
                    empresaId,
                    mensagem =
                        "Erro ao conectar ao banco da empresa.",
                    erro = ex.Message
                });
        }
    }
}
