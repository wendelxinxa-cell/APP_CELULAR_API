using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace APP_CELULAR_API.Services;

public sealed record TenantSession(long EmpresaId, long UsuarioId, bool EhAdministrador, long DispositivoId, DateTimeOffset ExpiresAt);

public sealed class TenantSessionStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);
    private readonly ConcurrentDictionary<string, TenantSession> _sessions = new();
    private readonly string? _sharedConnectionString;

    public TenantSessionStore(IConfiguration configuration)
    {
        if (configuration.GetValue<bool>("SessionStore:UsePersistentDatabase"))
        {
            var connectionString = configuration.GetConnectionString("CadastroCentral");
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("SessionStore persistente requer ConnectionStrings:CadastroCentral.");
            _sharedConnectionString = EmpresaDatabaseResolver.NormalizarConnectionString(connectionString);
        }
    }

    public (string Token, TenantSession Session) Create(long empresaId, long usuarioId, bool ehAdministrador, long dispositivoId)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var expired in _sessions.Where(x => x.Value.ExpiresAt <= now).Select(x => x.Key))
            _sessions.TryRemove(expired, out _);

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var session = new TenantSession(empresaId, usuarioId, ehAdministrador, dispositivoId, now.Add(Lifetime));
        if (_sharedConnectionString is not null)
        {
            using var db = new NpgsqlConnection(_sharedConnectionString);
            db.Open();
            using var cleanup = new NpgsqlCommand("DELETE FROM platform.api_tenant_session WHERE expires_at <= now()", db);
            cleanup.ExecuteNonQuery();
            using var command = new NpgsqlCommand("""
                INSERT INTO platform.api_tenant_session (token_hash, empresa_id, usuario_id, eh_administrador, dispositivo_id, expires_at)
                VALUES (@hash, @empresa, @usuario, @admin, @dispositivo, @expira)
                """, db);
            command.Parameters.AddWithValue("hash", HashToken(token));
            command.Parameters.AddWithValue("empresa", empresaId);
            command.Parameters.AddWithValue("usuario", usuarioId);
            command.Parameters.AddWithValue("admin", ehAdministrador);
            command.Parameters.AddWithValue("dispositivo", dispositivoId);
            command.Parameters.AddWithValue("expira", session.ExpiresAt);
            command.ExecuteNonQuery();
            return (token, session);
        }
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
        if (_sharedConnectionString is not null)
        {
            using var db = new NpgsqlConnection(_sharedConnectionString);
            db.Open();
            using var command = new NpgsqlCommand("""
                SELECT empresa_id, usuario_id, eh_administrador, dispositivo_id, expires_at
                FROM platform.api_tenant_session WHERE token_hash = @hash AND expires_at > now()
                """, db);
            command.Parameters.AddWithValue("hash", HashToken(token));
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return false;
            session = new TenantSession(reader.GetInt64(0), reader.GetInt64(1), reader.GetBoolean(2),
                reader.GetInt64(3), reader.GetFieldValue<DateTimeOffset>(4));
            return true;
        }
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
        {
            var token = authorizationHeader[prefix.Length..].Trim();
            if (_sharedConnectionString is not null)
            {
                using var db = new NpgsqlConnection(_sharedConnectionString);
                db.Open();
                using var command = new NpgsqlCommand("DELETE FROM platform.api_tenant_session WHERE token_hash = @hash", db);
                command.Parameters.AddWithValue("hash", HashToken(token));
                command.ExecuteNonQuery();
            }
            else _sessions.TryRemove(token, out _);
        }
    }

    public void RevokeDeviceSessions(long empresaId, long dispositivoId)
    {
        if (_sharedConnectionString is not null)
        {
            using var db = new NpgsqlConnection(_sharedConnectionString);
            db.Open();
            using var command = new NpgsqlCommand("DELETE FROM platform.api_tenant_session WHERE empresa_id = @empresa AND dispositivo_id = @dispositivo", db);
            command.Parameters.AddWithValue("empresa", empresaId);
            command.Parameters.AddWithValue("dispositivo", dispositivoId);
            command.ExecuteNonQuery();
            return;
        }
        foreach (var entry in _sessions.Where(x => x.Value.EmpresaId == empresaId && x.Value.DispositivoId == dispositivoId).ToArray())
            _sessions.TryRemove(entry.Key, out _);
    }

    private static byte[] HashToken(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
