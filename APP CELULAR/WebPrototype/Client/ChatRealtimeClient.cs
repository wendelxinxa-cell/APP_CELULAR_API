using Microsoft.JSInterop;
using System.Text.Json;

namespace WebPrototype.Services;

/// <summary>Supabase Realtime subscription created directly by supabase-js in the browser.</summary>
public sealed class ChatRealtimeClient(IJSRuntime js, SupabaseChatClient supabase) : IAsyncDisposable
{
    private DotNetObjectReference<ChatRealtimeClient>? reference;
    public event Func<MensagemConversa, Task>? MensagemRecebida;
    public event Func<Task>? Reconectado;
    public bool Conectado { get; private set; }

    public async Task ConectarAsync(ChatSessao sessao)
    {
        if (Conectado) return;
        await supabase.InicializarAsync();
        reference ??= DotNetObjectReference.Create(this);
        await js.InvokeVoidAsync("appCelular.supabase.subscribeMessages", sessao.EmpresaId, reference);
        Conectado = true;
        if (Reconectado is not null) await Reconectado.Invoke();
    }

    [JSInvokable]
    public async Task AoReceberMensagemSupabase(JsonElement payload)
    {
        var mensagem = payload.Deserialize<MensagemConversa>(new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        });
        if (mensagem is not null && MensagemRecebida is not null)
            await MensagemRecebida.Invoke(mensagem);
    }

    public async ValueTask DisposeAsync()
    {
        if (Conectado)
        {
            try { await js.InvokeVoidAsync("appCelular.supabase.unsubscribeMessages"); }
            catch (JSDisconnectedException) { }
            Conectado = false;
        }
        reference?.Dispose();
        reference = null;
    }
}
