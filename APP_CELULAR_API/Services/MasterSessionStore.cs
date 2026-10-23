using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace APP_CELULAR_API.Services;

public sealed record MasterSession(DateTimeOffset ExpiraEm);

public sealed class MasterSessionStore
{
    private static readonly TimeSpan Duracao = TimeSpan.FromHours(4);
    private readonly ConcurrentDictionary<string, MasterSession> _sessions = new();
    private readonly string? _sharedConnectionString;

    public MasterSessionStore(IConfiguration configuration)
    {
        if (configuration.GetValue<bool>("SessionStore:UsePersistentDatabase"))
        {
            var connectionString = configuration.GetConnectionString("CadastroCentral");
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("SessionStore persistente requer ConnectionStrings:CadastroCentral.");
            _sharedConnectionString = EmpresaDatabaseResolver.NormalizarConnectionString(connectionString);
        }
    }

    public (string Token, DateTimeOffset ExpiraEm) Criar()
    {
        var agora = DateTimeOffset.UtcNow;
        foreach (var tokenVelho in _sessions.Where(x => x.Value.ExpiraEm <= agora).Select(x => x.Key))
            _sessions.TryRemove(tokenVelho, out _);
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expiry = agora.Add(Duracao);
        if (_sharedConnectionString is not null)
        {
            using var db = new NpgsqlConnection(_sharedConnectionString);
            db.Open();
            using var cleanup = new NpgsqlCommand("DELETE FROM platform.api_master_session WHERE expires_at <= now()", db);
            cleanup.ExecuteNonQuery();
            using var command = new NpgsqlCommand("INSERT INTO platform.api_master_session (token_hash, expires_at) VALUES (@hash, @expira)", db);
            command.Parameters.AddWithValue("hash", HashToken(token));
            command.Parameters.AddWithValue("expira", expiry);
            command.ExecuteNonQuery();
            return (token, expiry);
        }
        _sessions[token] = new MasterSession(expiry);
        return (token, expiry);
    }

    public bool Validar(string? authorization)
    {
        const string prefix = "Bearer ";
        if (authorization is null || !authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        string token = authorization[prefix.Length..].Trim();
        if (_sharedConnectionString is not null)
        {
            using var db = new NpgsqlConnection(_sharedConnectionString);
            db.Open();
            using var command = new NpgsqlCommand("SELECT expires_at > now() FROM platform.api_master_session WHERE token_hash = @hash", db);
            command.Parameters.AddWithValue("hash", HashToken(token));
            var active = command.ExecuteScalar();
            return active is bool isActive && isActive;
        }
        if (!_sessions.TryGetValue(token, out var session)) return false;
        if (session.ExpiraEm > DateTimeOffset.UtcNow) return true;
        _sessions.TryRemove(token, out _);
        return false;
    }

    private static byte[] HashToken(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
