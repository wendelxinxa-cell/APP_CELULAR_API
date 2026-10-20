using System.Text.Json;
using System.Net.Http.Headers;
using System.Text;
using WebPrototype.Components;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();
builder.Services.AddHttpClient();

var app = builder.Build();
if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
app.UseStaticFiles();
app.UseAntiforgery();

app.MapGet("/version.json", () => Results.Json(new
{
    version = "0.0.001",
    build = 42,
    release = "0.0.001-build42"
})).AllowAnonymous();

app.MapPost("/api/chat/contacts", async (HttpRequest request, IHttpClientFactory clients, IConfiguration config) =>
{
    var authorization = request.Headers.Authorization.ToString();
    if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        return Results.Unauthorized();

    var apiBase = (config["Api:BaseUrl"] ?? "https://app-celular-api-test.onrender.com").TrimEnd('/');
    var body = await new StreamReader(request.Body).ReadToEndAsync(request.HttpContext.RequestAborted);
    using var upstream = new HttpRequestMessage(HttpMethod.Post, $"{apiBase}/api/conversas/contatos-web");
    upstream.Headers.Authorization = AuthenticationHeaderValue.Parse(authorization);
    upstream.Content = new StringContent(body, Encoding.UTF8, "application/json");

    try
    {
        using var response = await clients.CreateClient().SendAsync(upstream, request.HttpContext.RequestAborted);
        var content = await response.Content.ReadAsStringAsync(request.HttpContext.RequestAborted);
        return Results.Content(content, response.Content.Headers.ContentType?.ToString() ?? "application/json", statusCode: (int)response.StatusCode);
    }
    catch (HttpRequestException)
    {
        return Results.Json(new { mensagem = "A API de conversas está temporariamente inacessível. Tente atualizar novamente." }, statusCode: StatusCodes.Status502BadGateway);
    }
}).AllowAnonymous();

app.MapPost("/api/chat/messages", async (HttpRequest request, IHttpClientFactory clients, IConfiguration config) =>
{
    var authorization = request.Headers.Authorization.ToString();
    if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        return Results.Unauthorized();

    var apiBase = (config["Api:BaseUrl"] ?? "https://app-celular-api-test.onrender.com").TrimEnd('/');
    var body = await new StreamReader(request.Body).ReadToEndAsync(request.HttpContext.RequestAborted);
    var query = request.QueryString.HasValue ? request.QueryString.Value : "";
    using var upstream = new HttpRequestMessage(HttpMethod.Post, $"{apiBase}/api/conversas/listar-web{query}");
    upstream.Headers.Authorization = AuthenticationHeaderValue.Parse(authorization);
    upstream.Content = new StringContent(body, Encoding.UTF8, "application/json");

    try
    {
        using var response = await clients.CreateClient().SendAsync(upstream, request.HttpContext.RequestAborted);
        var content = await response.Content.ReadAsStringAsync(request.HttpContext.RequestAborted);
        return Results.Content(content, response.Content.Headers.ContentType?.ToString() ?? "application/json", statusCode: (int)response.StatusCode);
    }
    catch (HttpRequestException)
    {
        return Results.Json(new { mensagem = "A API de conversas está temporariamente inacessível. Tente atualizar novamente." }, statusCode: StatusCodes.Status502BadGateway);
    }
}).AllowAnonymous();

// Only the browser-safe Supabase URL and publishable key are exposed. All chat,
// authentication, realtime and media operations run from the browser to Supabase.
app.MapGet("/supabase-config.js", (IConfiguration config) =>
{
    var publicConfig = new
    {
        url = config["Supabase:Url"],
        publishableKey = config["Supabase:PublishableKey"],
        bucket = config["Supabase:Bucket"] ?? "chat-media",
        empresaId = config["Supabase:EmpresaId"],
        apiBaseUrl = config["Api:BaseUrl"] ?? "https://app-celular-api-test.onrender.com"
    };
    var json = JsonSerializer.Serialize(publicConfig);
    return Results.Content($"window.chatcatSupabaseConfig = {json};", "application/javascript");
}).AllowAnonymous();

app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(WebPrototype.Client.ClientAssemblyMarker).Assembly);
app.Run();
