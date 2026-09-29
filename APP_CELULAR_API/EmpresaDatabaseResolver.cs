namespace APP_CELULAR_API.Services;

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

        return connectionString;
    }
}