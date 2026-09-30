using APP_CELULAR_API.Services;

var builder = WebApplication.CreateBuilder(args);

// O Render define PORT dinamicamente; local/Azure podem usar ASPNETCORE_URLS.
var renderPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(renderPort))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{renderPort}");
}

// ============================================================
// SERVIÇOS
// ============================================================

builder.Services.AddControllers();

builder.Services.AddOpenApi();

// ============================================================
// RESOLVER DE BANCO POR EMPRESA
// ============================================================

builder.Services.AddSingleton<
    IEmpresaDatabaseResolver,
    EmpresaDatabaseResolver>();
builder.Services.AddSingleton<TenantSessionStore>();
builder.Services.AddSingleton<MasterSessionStore>();
builder.Services.AddSingleton<CatalogoCriptografia>();
builder.Services.AddSingleton<EmailNotificacaoService>();

var app = builder.Build();

// ============================================================
// PIPELINE
// ============================================================

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}


app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { online = true }));

app.MapControllers();

app.Run();
