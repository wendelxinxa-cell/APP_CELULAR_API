using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using WebPrototype.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<SupabaseChatClient>();
builder.Services.AddScoped<ChatPortalState>();
builder.Services.AddScoped<ChatRealtimeClient>();
await builder.Build().RunAsync();
