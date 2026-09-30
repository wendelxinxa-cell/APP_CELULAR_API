using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace APP_CELULAR_API.Services;

public sealed record MasterSession(DateTimeOffset ExpiraEm);

public sealed class MasterSessionStore
{
    private static readonly TimeSpan Duracao = TimeSpan.FromHours(4);
    private readonly ConcurrentDictionary<string, MasterSession> _sessions = new();

    public (string Token, DateTimeOffset ExpiraEm) Criar()
    {
        var agora = DateTimeOffset.UtcNow;
        foreach (var tokenVelho in _sessions.Where(x => x.Value.ExpiraEm <= agora).Select(x => x.Key))
            _sessions.TryRemove(tokenVelho, out _);
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expiry = agora.Add(Duracao);
        _sessions[token] = new MasterSession(expiry);
        return (token, expiry);
    }

    public bool Validar(string? authorization)
    {
        const string prefix = "Bearer ";
        if (authorization is null || !authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        string token = authorization[prefix.Length..].Trim();
        if (!_sessions.TryGetValue(token, out var session)) return false;
        if (session.ExpiraEm > DateTimeOffset.UtcNow) return true;
        _sessions.TryRemove(token, out _);
        return false;
    }
}
