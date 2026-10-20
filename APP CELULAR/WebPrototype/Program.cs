using System.Text.Json;
using WebPrototype.Components;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

var app = builder.Build();
if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
app.UseStaticFiles();
app.UseAntiforgery();

app.MapGet("/version.json", () => Results.Json(new
{
    version = "0.0.001",
    build = 41,
    release = "0.0.001-build41"
})).AllowAnonymous();

// Only the browser-safe Supabase URL and publishable key are exposed. All chat,
// authentication, realtime and media operations run from the browser to Supabase.
app.MapGet("/supabase-config.js", (IConfiguration config) =>
{
    var publicConfig = new
    {
        url = config["Supabase:Url"],
        publishableKey = config["Supabase:PublishableKey"],
        bucket = config["Supabase:Bucket"] ?? "chat-media",
        empresaId = config["Supabase:EmpresaId"]
    };
    var json = JsonSerializer.Serialize(publicConfig);
    return Results.Content($"window.chatcatSupabaseConfig = {json};", "application/javascript");
}).AllowAnonymous();

app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(WebPrototype.Client.ClientAssemblyMarker).Assembly);
app.Run();
