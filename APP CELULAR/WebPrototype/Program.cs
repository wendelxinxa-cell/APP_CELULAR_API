using WebPrototype.Components;
using WebPrototype.Services;

var builder = WebApplication.CreateBuilder(args);

// Keep the prototype runnable in restricted desktop environments where
// writing to the Windows Event Log is not permitted.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddHttpClient<ChatApiClient>((services, client) =>
{
    var endpoint = services.GetRequiredService<IConfiguration>()["Api:BaseUrl"]
        ?? "https://app-celular-api.onrender.com/";
    client.BaseAddress = new Uri(endpoint.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddScoped<ChatPortalState>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
