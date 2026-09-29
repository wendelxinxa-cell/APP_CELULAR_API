namespace APP_CELULAR_API.Services;

using Npgsql;

public interface IEmpresaDatabaseResolver
{
    string ObterConnectionString(long empresaId);
}

public class EmpresaDatabaseResolver : IEmpresaDatabaseResolver
{
    private readonly IConfiguration _configuration;

    public EmpresaDatabaseResolver(
        IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string ObterConnectionString(long empresaId)
    {
        if (empresaId <= 0)
        {
            throw new ArgumentException(
                "EmpresaId inválido.",
                nameof(empresaId));
        }

        // ---------------------------------------------------------
        // Descobre qual ConnectionString pertence à empresa.
        //
        // Exemplo:
        // Empresas:1 = SupabaseEmpresa1
        // Empresas:2 = SupabaseEmpresa2
        // ---------------------------------------------------------

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

    private static string NormalizarConnectionString(string value)
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
