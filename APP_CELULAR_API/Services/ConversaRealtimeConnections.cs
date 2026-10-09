using System.Collections.Concurrent;
using APP_CELULAR_API.Services;
using Microsoft.AspNetCore.SignalR;

namespace APP_CELULAR_API.Controllers;

public sealed class ConversaRealtimeHub : Hub
{
    private const string SessionItem = "TenantSession";
    private const string TokenItem = "TenantToken";
    private readonly TenantSessionStore _sessions;
    private readonly ConversaRealtimeConnections _connections;

    public ConversaRealtimeHub(TenantSessionStore sessions, ConversaRealtimeConnections connections)
    {
        _sessions = sessions;
        _connections = connections;
    }

    public override async Task OnConnectedAsync()
    {
        var context = Context.GetHttpContext();
        var authorization = context?.Request.Headers.Authorization.ToString();
        var token = ExtrairToken(authorization);
        TenantSession session = default!;
        var autenticada = token is not null && _sessions.TryGet($"Bearer {token}", out session);
        if (!autenticada && context is not null)
        {
            // SignalR clients may need the query parameter when negotiating WebSockets.
            token = context.Request.Query["access_token"].FirstOrDefault();
            autenticada = !string.IsNullOrWhiteSpace(token) && _sessions.TryGet($"Bearer {token}", out session);
            if (!autenticada)
            {
                Context.Abort();
                return;
            }
        }

        if (token is null || !autenticada)
        {
            Context.Abort();
            return;
        }

        Context.Items[SessionItem] = session;
        Context.Items[TokenItem] = token;
        _connections.Registrar(Context.ConnectionId, token, session);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _connections.Remover(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    private static string? ExtrairToken(string? authorization)
    {
        const string prefix = "Bearer ";
        return authorization?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true
            ? authorization[prefix.Length..].Trim()
            : null;
    }
}

public sealed class ConversaRealtimeConnections
{
    private sealed record Conexao(string Token, TenantSession Sessao);

    private readonly ConcurrentDictionary<string, Conexao> _conexoes = new();
    private readonly TenantSessionStore _sessions;
    private readonly IHubContext<ConversaRealtimeHub> _hub;
    private readonly ILogger<ConversaRealtimeConnections> _logger;

    public ConversaRealtimeConnections(
        TenantSessionStore sessions,
        IHubContext<ConversaRealtimeHub> hub,
        ILogger<ConversaRealtimeConnections> logger)
    {
        _sessions = sessions;
        _hub = hub;
        _logger = logger;
    }

    public void Registrar(string connectionId, string token, TenantSession session) =>
        _conexoes[connectionId] = new(token, session);

    public void Remover(string connectionId) => _conexoes.TryRemove(connectionId, out _);

    public async Task NotificarNovaMensagemAsync(long empresaId, long destinatarioId, long remetenteId)
    {
        foreach (var item in _conexoes.ToArray())
        {
            var conexao = item.Value;
            if (conexao.Sessao.EmpresaId != empresaId || conexao.Sessao.UsuarioId != destinatarioId)
                continue;

            if (!_sessions.TryGet($"Bearer {conexao.Token}", out var sessaoAtual) || sessaoAtual != conexao.Sessao)
            {
                Remover(item.Key);
                continue;
            }

            try
            {
                // The event carries only the sender id; the client fetches the
                // authorized message through the existing conversation endpoint.
                await _hub.Clients.Client(item.Key).SendAsync("NovaMensagem", remetenteId);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Não foi possível avisar a conexão {ConnectionId} sobre nova mensagem.", item.Key);
                Remover(item.Key);
            }
        }
    }
}
