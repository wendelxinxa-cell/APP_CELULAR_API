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
        var versao = configuration["AppUpdate:Version"];
        var arquivo = configuration["AppUpdate:FileName"];
        var arquivoPublicado = Path.Combine(environment.ContentRootPath, "wwwroot", "releases", "CHATCAT_0.0.009_build_9.apk");
        if (System.IO.File.Exists(arquivoPublicado))
        {
            versao = "0.0.009";
            arquivo = Path.GetFileName(arquivoPublicado);
        }
        if (!int.TryParse(configuration["AppUpdate:Build"], out var build) ||
            string.IsNullOrWhiteSpace(versao) || string.IsNullOrWhiteSpace(arquivo) ||
            !arquivo.EndsWith(".apk", StringComparison.OrdinalIgnoreCase) ||
            arquivo.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { mensagem = "Atualização ainda não configurada." });

        if (arquivo.Equals("CHATCAT_0.0.009_build_9.apk", StringComparison.OrdinalIgnoreCase))
            build = 9;

        var hash = configuration["AppUpdate:Sha256"];
        if (arquivo.Equals("CHATCAT_0.0.009_build_9.apk", StringComparison.OrdinalIgnoreCase))
            hash = "8918C376A4ECF7559465949F02848C28FC22B307DB3F8A4D8493C726E940B0E6";
        if (string.IsNullOrWhiteSpace(hash) || hash.Length != 64 || hash.Any(c => !Uri.IsHexDigit(c)))
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { mensagem = "Checksum da atualização ainda não configurado." });

        Response.Headers.CacheControl = "no-store";
        return Ok(new
        {
            versao,
            build,
            url = $"https://app-celular-api.onrender.com/api/app/atualizacao/arquivo",
            sha256 = hash,
            notas = configuration["AppUpdate:Notes"] ?? "Atualização do aplicativo."
        });
    }

    [HttpGet("atualizacao/arquivo")]
    public IActionResult BaixarAtualizacao()
    {
        var arquivo = configuration["AppUpdate:FileName"];
        var arquivoPublicado = Path.Combine(environment.ContentRootPath, "wwwroot", "releases", "CHATCAT_0.0.009_build_9.apk");
        if (System.IO.File.Exists(arquivoPublicado))
            arquivo = Path.GetFileName(arquivoPublicado);
        if (string.IsNullOrWhiteSpace(arquivo) || Path.GetFileName(arquivo) != arquivo)
            return NotFound();

        var caminho = Path.Combine(environment.ContentRootPath, "wwwroot", "releases", arquivo);
        if (!System.IO.File.Exists(caminho)) return NotFound();

        var hashEsperado = configuration["AppUpdate:Sha256"];
        if (arquivo.Equals("CHATCAT_0.0.009_build_9.apk", StringComparison.OrdinalIgnoreCase))
            hashEsperado = "8918C376A4ECF7559465949F02848C28FC22B307DB3F8A4D8493C726E940B0E6";
        using (var stream = System.IO.File.OpenRead(caminho))
        {
            var hashReal = Convert.ToHexString(SHA256.HashData(stream));
            if (!string.Equals(hashReal, hashEsperado, StringComparison.OrdinalIgnoreCase))
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new { mensagem = "O APK hospedado não corresponde ao checksum publicado." });
        }

        return PhysicalFile(caminho, "application/vnd.android.package-archive", arquivo, enableRangeProcessing: true);
    }
}
