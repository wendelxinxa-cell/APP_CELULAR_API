using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;

namespace APP_CELULAR_API.Controllers;

[ApiController]
[Route("api/app")]
public sealed class AppUpdateController(IConfiguration configuration, IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet("atualizacao")]
    public IActionResult ObterAtualizacao()
    {
        if (!TentarObterRelease(out var versao, out var build, out var arquivo, out var caminho, out var hashEsperado))
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { mensagem = "Atualização não configurada ou arquivo publicado inválido." });

        Response.Headers.CacheControl = "no-store";
        return Ok(new
        {
            versao,
            build,
            url = $"https://app-celular-api.onrender.com/api/app/atualizacao/arquivo",
            sha256 = hashEsperado,
            notas = configuration["AppUpdate:Notes"] ?? "Atualização do aplicativo."
        });
    }

    [HttpGet("atualizacao/arquivo")]
    public IActionResult BaixarAtualizacao()
    {
        if (!TentarObterRelease(out _, out _, out var arquivo, out var caminho, out _))
            return NotFound();

        return PhysicalFile(caminho, "application/vnd.android.package-archive", arquivo, enableRangeProcessing: true);
    }

    private bool TentarObterRelease(out string versao, out int build, out string arquivo, out string caminho, out string hashEsperado)
    {
        versao = configuration["AppUpdate:Version"] ?? string.Empty;
        arquivo = configuration["AppUpdate:FileName"] ?? string.Empty;
        hashEsperado = configuration["AppUpdate:Sha256"] ?? string.Empty;
        caminho = string.Empty;
        build = 0;

        if (!int.TryParse(configuration["AppUpdate:Build"], out build) ||
            string.IsNullOrWhiteSpace(versao) || string.IsNullOrWhiteSpace(arquivo) ||
            Path.GetFileName(arquivo) != arquivo ||
            !arquivo.EndsWith(".apk", StringComparison.OrdinalIgnoreCase) ||
            hashEsperado.Length != 64 || hashEsperado.Any(c => !Uri.IsHexDigit(c)))
            return false;

        caminho = Path.Combine(environment.ContentRootPath, "wwwroot", "releases", arquivo);
        if (!System.IO.File.Exists(caminho)) return false;

        using var stream = System.IO.File.OpenRead(caminho);
        var hashReal = Convert.ToHexString(SHA256.HashData(stream));
        return string.Equals(hashReal, hashEsperado, StringComparison.OrdinalIgnoreCase);
    }
}
