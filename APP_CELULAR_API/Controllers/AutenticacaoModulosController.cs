using APP_CELULAR_API.Services;
using Microsoft.AspNetCore.Mvc;

namespace APP_CELULAR_API.Controllers;

[ApiController]
[Route("api/autenticacao")]
public sealed class AutenticacaoModulosController(
    SupabaseLoginService supabaseLogin,
    ILogger<AutenticacaoModulosController> logger) : ControllerBase
{
    [HttpGet("modulos")]
    public async Task<IActionResult> ObterModulos([FromQuery] long empresaId, CancellationToken cancellationToken)
    {
        string authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || empresaId <= 0)
            return Unauthorized(new { mensagem = "Sessão ou empresa inválida." });

        try
        {
            var modules = await supabaseLogin.GetModulesAsync(authorization[7..].Trim(), empresaId, cancellationToken);
            return Ok(new { modulos = modules });
        }
        catch (SupabaseLoginException ex)
        {
            return StatusCode(ex.StatusCode, new { mensagem = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao carregar módulos para a empresa {EmpresaId}.", empresaId);
            return StatusCode(503, new { mensagem = "Não foi possível carregar as permissões do usuário." });
        }
    }
}
