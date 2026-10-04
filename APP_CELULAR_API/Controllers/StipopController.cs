using APP_CELULAR_API.Models;
using APP_CELULAR_API.Services;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Text.Json;

namespace APP_CELULAR_API.Controllers;

[ApiController]
[Route("api/stipop")]
public sealed class StipopController(
    IEmpresaDatabaseResolver resolver,
    TenantSessionStore sessions,
    IHttpClientFactory clients,
    IConfiguration configuration) : ControllerBase
{
    private const string BaseUrl = "https://messenger.stipop.io/v1/";

    [HttpPost("pacotes")]
    public async Task<IActionResult> Pacotes([FromBody] StipopListaRequest request)
    {
        if (!await Validar(request)) return Unauthorized(new { mensagem = "Sessão inválida." });
        var category = request.Categoria == "new" ? "package/new" : "package";
        var page = Math.Clamp(request.PageNumber, 1, 10000);
        var path = $"{category}?userId={UserId(request)}&pageNumber={page}&lang=pt&countryCode=BR&limit=50";
        using var response = await Chamar(HttpMethod.Get, path);
        if (response is null) return StatusCode(503, new { mensagem = "Integração Stipop indisponível: falta configurar a chave no servidor." });
        if (!response.IsSuccessStatusCode) return StatusCode(502, await ErroStipop(response));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var body = json.RootElement.TryGetProperty("body", out var value) ? value : default;
        var packs = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("packageList", out var list) ? list.Clone() : JsonDocument.Parse("[]").RootElement.Clone();
        var (currentPage, totalPages) = PageMap(body, page);
        return Ok(new { pacotes = packs, pagina = currentPage, totalPaginas = totalPages });
    }

    [HttpPost("buscar")]
    public async Task<IActionResult> Buscar([FromBody] StipopBuscaRequest request)
    {
        if (!await Validar(request)) return Unauthorized(new { mensagem = "Sessão inválida." });
        if (string.IsNullOrWhiteSpace(request.Termo)) return BadRequest(new { mensagem = "Digite o que deseja buscar." });
        var query = Uri.EscapeDataString(request.Termo.Trim());
        var page = Math.Clamp(request.PageNumber, 1, 10000);
        using var response = await Chamar(HttpMethod.Get, $"search?q={query}&userId={UserId(request)}&lang=pt&countryCode=BR&pageNumber={page}&limit=50");
        if (response is null) return StatusCode(503, new { mensagem = "Integração Stipop indisponível: falta configurar a chave no servidor." });
        if (!response.IsSuccessStatusCode) return StatusCode(502, await ErroStipop(response));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var body = json.RootElement.TryGetProperty("body", out var value) ? value : default;
        var stickers = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("stickerList", out var list) ? list.Clone() : JsonDocument.Parse("[]").RootElement.Clone();
        var (currentPage, totalPages) = PageMap(body, page);
        return Ok(new { figurinhas = stickers, pagina = currentPage, totalPaginas = totalPages });
    }

    [HttpPost("pacote")]
    public async Task<IActionResult> Pacote([FromBody] StipopPacoteRequest request)
    {
        if (!await Validar(request)) return Unauthorized(new { mensagem = "Sessão inválida." });
        if (request.PackageId <= 0) return BadRequest(new { mensagem = "Pacote inválido." });
        using var response = await Chamar(HttpMethod.Get, $"package/{request.PackageId}?userId={UserId(request)}");
        if (response is null) return StatusCode(503, new { mensagem = "Integração Stipop indisponível: falta configurar a chave no servidor." });
        if (!response.IsSuccessStatusCode) return StatusCode(502, await ErroStipop(response));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var body = json.RootElement.TryGetProperty("body", out var value) ? value : default;
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("package", out var pack))
            return NotFound(new { mensagem = "Pacote não encontrado." });
        return Ok(new { pacote = pack.Clone() });
    }

    [HttpPost("pacote/baixar")]
    public async Task<IActionResult> BaixarPacote([FromBody] StipopPacoteRequest request)
    {
        if (!await Validar(request)) return Unauthorized(new { mensagem = "Sessão inválida." });
        if (request.PackageId <= 0) return BadRequest(new { mensagem = "Pacote inválido." });
        using var response = await Chamar(HttpMethod.Post, $"download/{request.PackageId}?userId={UserId(request)}&isPurchase=N");
        if (response is null) return StatusCode(503, new { mensagem = "Integração Stipop indisponível: falta configurar a chave no servidor." });
        return response.IsSuccessStatusCode ? Ok(new { sucesso = true }) : StatusCode(502, await ErroStipop(response));
    }

    [HttpPost("figurinha/enviada")]
    public async Task<IActionResult> RegistrarEnvio([FromBody] StipopFigurinhaRequest request)
    {
        if (!await Validar(request)) return Unauthorized(new { mensagem = "Sessão inválida." });
        if (request.StickerId <= 0) return BadRequest(new { mensagem = "Figurinha inválida." });
        using var response = await Chamar(HttpMethod.Post, $"analytics/send/{request.StickerId}?userId={UserId(request)}&lang=pt&countryCode=BR");
        if (response is null) return StatusCode(503, new { mensagem = "Integração Stipop indisponível: falta configurar a chave no servidor." });
        return response.IsSuccessStatusCode ? Ok(new { sucesso = true }) : StatusCode(502, await ErroStipop(response));
    }

    private async Task<bool> Validar(IdentidadeConversaRequest request)
    {
        if (request.EmpresaId <= 0 || request.UsuarioId <= 0 ||
            !sessions.TryGet(Request.Headers.Authorization.ToString(), out var session) ||
            session.EmpresaId != request.EmpresaId || session.UsuarioId != request.UsuarioId)
            return false;
        var connection = await resolver.ObterConnectionString(request.EmpresaId);
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM app.dispositivo WHERE id=@device AND empresa_id=@company AND usuario_id=@user AND ativo=TRUE)", db);
        cmd.Parameters.AddWithValue("device", session.DispositivoId);
        cmd.Parameters.AddWithValue("company", session.EmpresaId);
        cmd.Parameters.AddWithValue("user", session.UsuarioId);
        return await cmd.ExecuteScalarAsync() is true;
    }

    private async Task<HttpResponseMessage?> Chamar(HttpMethod method, string path)
    {
        var key = configuration["Stipop:ApiKey"];
        if (string.IsNullOrWhiteSpace(key)) return null;
        using var request = new HttpRequestMessage(method, BaseUrl + path);
        request.Headers.TryAddWithoutValidation("apikey", key);
        return await clients.CreateClient().SendAsync(request);
    }

    private static string UserId(IdentidadeConversaRequest request) => $"chatcat-{request.EmpresaId}-{request.UsuarioId}";

    private static (int Page, int TotalPages) PageMap(JsonElement body, int fallback)
    {
        if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("pageMap", out var map) && map.ValueKind == JsonValueKind.Object)
        {
            var page = map.TryGetProperty("pageNumber", out var pageValue) && pageValue.TryGetInt32(out var parsedPage) ? parsedPage : fallback;
            var total = map.TryGetProperty("pageCount", out var countValue) && countValue.TryGetInt32(out var parsedCount) ? parsedCount : page;
            return (Math.Max(1, page), Math.Max(1, total));
        }
        return (fallback, fallback);
    }

    private static async Task<object> ErroStipop(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return new { mensagem = "A Stipop não conseguiu concluir esta solicitação.", detalhe = body.Length > 500 ? body[..500] : body };
    }
}

public sealed class StipopListaRequest : IdentidadeConversaRequest { public string Categoria { get; set; } = "trending"; public int PageNumber { get; set; } = 1; }
public sealed class StipopBuscaRequest : IdentidadeConversaRequest { public string Termo { get; set; } = ""; public int PageNumber { get; set; } = 1; }
public sealed class StipopPacoteRequest : IdentidadeConversaRequest { public int PackageId { get; set; } }
public sealed class StipopFigurinhaRequest : IdentidadeConversaRequest { public int StickerId { get; set; } }
