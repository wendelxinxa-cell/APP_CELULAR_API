using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace APP_CELULAR_API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TesteController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public TesteController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpGet]
    public async Task<IActionResult> TestarBanco()
    {
        try
        {
            string? connectionString =
                _configuration.GetConnectionString("Supabase");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return StatusCode(
                    500,
                    "ConnectionString 'Supabase' não configurada.");
            }

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
                mensagem = "API conectada ao Supabase com sucesso.",
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
                    mensagem = "Erro ao conectar ao banco.",
                    erro = ex.Message
                });
        }
    }
}