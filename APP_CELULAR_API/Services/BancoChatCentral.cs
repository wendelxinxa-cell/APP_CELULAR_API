using Npgsql;

namespace APP_CELULAR_API.Services;

public static class BancoChatCentral
{
    public static bool Ativo(IConfiguration configuration) =>
        configuration.GetValue<bool>("ChatCentral:Ativo");

    public static async Task<bool> Pronto(IConfiguration configuration)
    {
        if (!Ativo(configuration)) return false;
        await using var db = await Abrir(configuration);
        try
        {
            await using var cmd = new NpgsqlCommand("SELECT ativo FROM chat.configuracao WHERE id=TRUE;", db);
            return await cmd.ExecuteScalarAsync() is true;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // A API ainda pode estar atualizada antes do schema central ser criado.
            return false;
        }
    }

    public static async Task<NpgsqlConnection> Abrir(IConfiguration configuration)
    {
        var raw = configuration.GetConnectionString("CadastroCentral");
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("O banco central não está configurado no servidor.");

        var db = new NpgsqlConnection(EmpresaDatabaseResolver.NormalizarConnectionString(raw));
        await db.OpenAsync();
        return db;
    }
}
