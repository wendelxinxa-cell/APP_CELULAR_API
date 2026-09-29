using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace APP_CELULAR_API.Services;

public sealed record TenantSession(long EmpresaId, long UsuarioId, bool EhAdministrador, DateTimeOffset ExpiresAt);

public sealed class TenantSessionStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);
    private readonly ConcurrentDictionary<string, TenantSession> _sessions = new();

    public (string Token, TenantSession Session) Create(long empresaId, long usuarioId, bool ehAdministrador)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var expired in _sessions.Where(x => x.Value.ExpiresAt <= now).Select(x => x.Key))
            _sessions.TryRemove(expired, out _);

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var session = new TenantSession(empresaId, usuarioId, ehAdministrador, now.Add(Lifetime));
        _sessions[token] = session;
        return (token, session);
    }

    public bool TryGet(string? authorizationHeader, out TenantSession session)
    {
        session = default!;
        const string prefix = "Bearer ";
        if (authorizationHeader is null || !authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var token = authorizationHeader[prefix.Length..].Trim();
        if (!_sessions.TryGetValue(token, out var found)) return false;
        if (found.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _sessions.TryRemove(token, out _);
            return false;
        }
        session = found;
        return true;
    }

    public void Revoke(string? authorizationHeader)
    {
        const string prefix = "Bearer ";
        if (authorizationHeader?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true)
            _sessions.TryRemove(authorizationHeader[prefix.Length..].Trim(), out _);
    }
}
