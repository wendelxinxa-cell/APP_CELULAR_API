namespace APP_CELULAR_API.Services;

using Npgsql;
using System.Collections.Concurrent;

public interface IEmpresaDatabaseResolver
{
    Task<string> ObterConnectionString(long empresaId);
    void InvalidarCache(long empresaId);
}

public class EmpresaDatabaseResolver : IEmpresaDatabaseResolver
{
    private readonly IConfiguration _configuration;
    private readonly CatalogoCriptografia _criptografia;
    private readonly ConcurrentDictionary<long, (string Valor, DateTime Expira)> _cache = new();

    public EmpresaDatabaseResolver(
        IConfiguration configuration,
        CatalogoCriptografia criptografia)
    {
        _configuration = configuration;
        _criptografia = criptografia;
    }

    public async Task<string> ObterConnectionString(long empresaId)
    {
        if (empresaId <= 0)
        {
            throw new ArgumentException(
                "EmpresaId inválido.",
                nameof(empresaId));
        }

        if (_cache.TryGetValue(empresaId, out var cached) && cached.Expira > DateTime.UtcNow)
            return cached.Valor;

        // O produto usa um único banco físico (o banco da Empresa 1) para todas
        // as empresas. O empresa_id continua isolando os dados dentro desse
        // banco; o código da empresa não seleciona outro servidor Supabase.
        string? bancoUnico = _configuration["Empresas:1"];
        string? connectionUnica = string.IsNullOrWhiteSpace(bancoUnico)
            ? null
            : _configuration.GetConnectionString(bancoUnico);
        if (!string.IsNullOrWhiteSpace(connectionUnica))
        {
            string resolvido = NormalizarConnectionString(connectionUnica);
            _cache[empresaId] = (resolvido, DateTime.UtcNow.AddMinutes(5));
            return resolvido;
        }

        var catalogo = _configuration.GetConnectionString("CadastroCentral");
        if (!string.IsNullOrWhiteSpace(catalogo))
        {
            await using var db = new NpgsqlConnection(NormalizarConnectionString(catalogo));
            await db.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT conexao_criptografada FROM platform.empresa_catalogo WHERE id = @id AND ativa = TRUE AND status = 'ATIVA'",
                db);
            cmd.Parameters.AddWithValue("id", empresaId);
            var valor = await cmd.ExecuteScalarAsync();
            if (valor is byte[] criptografado)
            {
                string resolvido = NormalizarConnectionString(_criptografia.Descriptografar(criptografado));
                _cache[empresaId] = (resolvido, DateTime.UtcNow.AddMinutes(5));
                return resolvido;
            }
        }

        // Compatibilidade com as empresas antigas pré-configuradas no Render.

        string? nomeConnectionString =
            _configuration[$"Empresas:{empresaId}"];

        if (string.IsNullOrWhiteSpace(nomeConnectionString))
        {
            throw new InvalidOperationException(
                $"Não existe banco configurado para a empresa {empresaId}.");
        }

        string? connectionString =
            _configuration.GetConnectionString(
                nomeConnectionString);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"A ConnectionString '{nomeConnectionString}' " +
                $"não está configurada.");
        }

        return NormalizarConnectionString(connectionString);
    }

    public void InvalidarCache(long empresaId) => _cache.TryRemove(empresaId, out _);

    public static string NormalizarConnectionString(string value)
    {
        value = value.Trim();
        bool isPostgresUri = value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase);

        if (!isPostgresUri)
            return value;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || string.IsNullOrWhiteSpace(uri.Host))
            throw new InvalidOperationException("A URL de conexão PostgreSQL do Supabase é inválida.");

        string userInfo = Uri.UnescapeDataString(uri.UserInfo);
        int separator = userInfo.IndexOf(':');
        string database = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'));
        if (separator <= 0 || string.IsNullOrWhiteSpace(database))
            throw new InvalidOperationException("A URL de conexão PostgreSQL do Supabase está incompleta.");

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
            Database = database,
            Username = Uri.UnescapeDataString(userInfo[..separator]),
            Password = Uri.UnescapeDataString(userInfo[(separator + 1)..]),
            SslMode = SslMode.Require
        };

        return builder.ConnectionString;
    }
}
