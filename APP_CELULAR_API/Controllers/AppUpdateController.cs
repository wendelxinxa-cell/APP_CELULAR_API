using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace APP_CELULAR_API.Controllers;

[ApiController]
[Route("api/app")]
public sealed class AppUpdateController(IConfiguration configuration, IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet("atualizacao")]
    public IActionResult ObterAtualizacao()
    {
        if (!TentarObterRelease(out var versao, out var build, out var codigoVersao, out var arquivo, out var caminho, out var hashEsperado, out var notas))
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { mensagem = "Atualização não configurada ou arquivo publicado inválido." });

        Response.Headers.CacheControl = "no-store";
        var baseUrl = configuration["AppUpdate:PublicBaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = $"https://{Request.Host.Value}";
        return Ok(new
        {
            versao,
            build,
            codigoVersao,
            url = $"{baseUrl.TrimEnd('/')}/api/app/atualizacao/arquivo",
            sha256 = hashEsperado,
            notas
        });
    }

    [HttpGet("atualizacao/arquivo")]
    public IActionResult BaixarAtualizacao()
    {
        if (!TentarObterRelease(out _, out _, out _, out var arquivo, out var caminho, out _, out _))
            return NotFound();

        return PhysicalFile(caminho, "application/vnd.android.package-archive", arquivo, enableRangeProcessing: true);
    }

    private bool TentarObterRelease(out string versao, out int build, out int codigoVersao, out string arquivo, out string caminho, out string hashEsperado, out string notas)
    {
        versao = configuration["AppUpdate:Version"] ?? string.Empty;
        arquivo = configuration["AppUpdate:FileName"] ?? string.Empty;
        hashEsperado = configuration["AppUpdate:Sha256"] ?? string.Empty;
        notas = configuration["AppUpdate:Notes"] ?? "Atualização do aplicativo.";
        caminho = string.Empty;
        build = 0;
        codigoVersao = 0;
        int? codigoVersaoManifesto = null;

        var manifesto = Path.Combine(environment.ContentRootPath, "wwwroot", "releases", "current.json");
        if (System.IO.File.Exists(manifesto))
        {
            try
            {
                using var json = JsonDocument.Parse(System.IO.File.ReadAllText(manifesto));
                var root = json.RootElement;
                versao = root.GetProperty("version").GetString() ?? string.Empty;
                build = root.GetProperty("build").GetInt32();
                arquivo = root.GetProperty("fileName").GetString() ?? string.Empty;
                hashEsperado = root.GetProperty("sha256").GetString() ?? string.Empty;
                if (root.TryGetProperty("notes", out var notes) && notes.ValueKind == JsonValueKind.String)
                    notas = notes.GetString() ?? notas;
                if (root.TryGetProperty("versionCode", out var versionCode) &&
                    versionCode.ValueKind == JsonValueKind.Number &&
                    versionCode.TryGetInt32(out var parsedVersionCode))
                    codigoVersaoManifesto = parsedVersionCode;
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or KeyNotFoundException)
            {
                return false;
            }
        }

        if ((build == 0 && !int.TryParse(configuration["AppUpdate:Build"], out build)) ||
            build is < 1 or > 99 ||
            string.IsNullOrWhiteSpace(versao) || string.IsNullOrWhiteSpace(arquivo) ||
            Path.GetFileName(arquivo) != arquivo ||
            !arquivo.EndsWith(".apk", StringComparison.OrdinalIgnoreCase) ||
            hashEsperado.Length != 64 || hashEsperado.Any(c => !Uri.IsHexDigit(c)))
            return false;

        var partesVersao = versao.Split('.');
        if (partesVersao.Length != 3 ||
            !int.TryParse(partesVersao[0], out var major) || major < 0 ||
            !int.TryParse(partesVersao[1], out var minor) || minor < 0 ||
            !int.TryParse(partesVersao[2], out var ciclo) || ciclo < 0)
            return false;

        if (codigoVersaoManifesto is int codigoConfigurado)
        {
            if (codigoConfigurado < 1) return false;
            codigoVersao = codigoConfigurado;
        }
        else
        {
            if (major != 0 || minor != 0 || ciclo < 1) return false;
            codigoVersao = checked((ciclo - 1) * 99 + build);
        }

        caminho = Path.Combine(environment.ContentRootPath, "wwwroot", "releases", arquivo);
        if (!System.IO.File.Exists(caminho)) return false;

        using var stream = System.IO.File.OpenRead(caminho);
        var hashReal = Convert.ToHexString(SHA256.HashData(stream));
        return string.Equals(hashReal, hashEsperado, StringComparison.OrdinalIgnoreCase);
    }
}
