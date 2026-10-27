using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace APP_CELULAR_API.Services;

public sealed class SupabaseLoginService(IConfiguration configuration, IHttpClientFactory clients, IEmpresaDatabaseResolver resolver)
{
    private readonly string _url = (configuration["SupabaseAuth:Url"] ?? "").TrimEnd('/');
    private readonly string _key = configuration["SupabaseAuth:PublishableKey"] ?? "";
    private readonly IHttpClientFactory _clients = clients;
    private readonly IEmpresaDatabaseResolver _resolver = resolver;

    public async Task<SupabaseIdentity> AuthenticateAsync(string identifier, string password, long empresaId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_url) || string.IsNullOrWhiteSpace(_key))
            throw new InvalidOperationException("A autenticação central do Supabase não está configurada na API.");

        using var client = _clients.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        using var response = identifier.Contains('@')
            ? await SignInByEmail(client, identifier.Trim(), password, cancellationToken)
            : await SignInByName(client, identifier.Trim(), password, empresaId, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new SupabaseLoginException((int)response.StatusCode, ReadMessage(payload));

        using var session = JsonDocument.Parse(payload);
        var accessToken = session.RootElement.TryGetProperty("access_token", out var token)
            ? token.GetString()
            : session.RootElement.TryGetProperty("session", out var nested) && nested.TryGetProperty("access_token", out token)
                ? token.GetString() : null;
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new SupabaseLoginException(401, "O Supabase não retornou uma sessão válida.");

        using var authRequest = new HttpRequestMessage(HttpMethod.Get, $"{_url}/auth/v1/user");
        authRequest.Headers.Add("apikey", _key);
        authRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var authResponse = await client.SendAsync(authRequest, cancellationToken);
        if (!authResponse.IsSuccessStatusCode)
            throw new SupabaseLoginException(401, "A sessão retornada pelo Supabase não pôde ser validada.");
        using var authUser = JsonDocument.Parse(await authResponse.Content.ReadAsStringAsync(cancellationToken));
        var authUid = authUser.RootElement.GetProperty("id").GetGuid();

        using var identityRequest = new HttpRequestMessage(HttpMethod.Post, $"{_url}/rest/v1/rpc/obter_identidade")
        {
            Content = JsonContent.Create(new { p_empresa_id = empresaId })
        };
        identityRequest.Headers.Add("apikey", _key);
        identityRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        identityRequest.Headers.Add("Accept-Profile", "chat");
        identityRequest.Headers.Add("Content-Profile", "chat");
        using var identityResponse = await client.SendAsync(identityRequest, cancellationToken);
        var identityPayload = await identityResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!identityResponse.IsSuccessStatusCode)
            throw new SupabaseLoginException(403, ReadMessage(identityPayload));
        using var identityJson = JsonDocument.Parse(identityPayload);
        var row = identityJson.RootElement.ValueKind == JsonValueKind.Array
            ? identityJson.RootElement.EnumerateArray().FirstOrDefault()
            : identityJson.RootElement;
        if (row.ValueKind != JsonValueKind.Object || !TryLong(row, "usuario_id", out var usuarioId) || !TryLong(row, "empresa_id", out var identityEmpresa))
        {
            using var statusRequest = new HttpRequestMessage(HttpMethod.Post, $"{_url}/rest/v1/rpc/status_solicitacao_cadastro");
            statusRequest.Headers.Add("apikey", _key);
            statusRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            statusRequest.Headers.Add("Accept-Profile", "chat");
            statusRequest.Headers.Add("Content-Profile", "chat");
            statusRequest.Content = JsonContent.Create(new { });
            using var statusResponse = await client.SendAsync(statusRequest, cancellationToken);
            if (statusResponse.IsSuccessStatusCode)
            {
                var status = (await statusResponse.Content.ReadAsStringAsync(cancellationToken)).Trim().Trim('"');
                if (status == "PENDENTE") throw new SupabaseLoginException(403, "Seu cadastro está aguardando aprovação de Zeus.");
                if (status == "RECUSADO") throw new SupabaseLoginException(403, "Seu cadastro foi recusado. Fale com Zeus para solicitar novamente.");
            }
            throw new SupabaseLoginException(403, "Esta conta ainda não está vinculada a um usuário ativo da empresa no Supabase.");
        }
        var nome = TryString(row, "nome") ?? identifier;
        var active = !row.TryGetProperty("ativo", out var activeValue) || activeValue.ValueKind != JsonValueKind.False;
        if (!active || identityEmpresa != empresaId)
            throw new SupabaseLoginException(403, "Esta conta não está ativa para a empresa selecionada.");
        return new SupabaseIdentity(authUid, identityEmpresa, usuarioId, nome);
    }

    public async Task<List<string>> GetModulesAsync(string accessToken, long empresaId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_url) || string.IsNullOrWhiteSpace(_key) || empresaId <= 0)
            throw new InvalidOperationException("A autenticação central ou a empresa não está configurada.");
        using var client = _clients.CreateClient();
        using var identityRequest = new HttpRequestMessage(HttpMethod.Post, $"{_url}/rest/v1/rpc/obter_identidade")
        {
            Content = JsonContent.Create(new { p_empresa_id = empresaId })
        };
        identityRequest.Headers.Add("apikey", _key);
        identityRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        identityRequest.Headers.Add("Accept-Profile", "chat");
        identityRequest.Headers.Add("Content-Profile", "chat");
        using var identityResponse = await client.SendAsync(identityRequest, cancellationToken);
        if (!identityResponse.IsSuccessStatusCode) throw new SupabaseLoginException(403, "Não foi possível validar o vínculo desta conta.");
        using var identityJson = JsonDocument.Parse(await identityResponse.Content.ReadAsStringAsync(cancellationToken));
        var identity = identityJson.RootElement.ValueKind == JsonValueKind.Array
            ? identityJson.RootElement.EnumerateArray().FirstOrDefault() : identityJson.RootElement;
        if (identity.ValueKind != JsonValueKind.Object || !TryLong(identity, "usuario_id", out var usuarioId))
            throw new SupabaseLoginException(403, "Esta conta não está vinculada a um usuário ativo.");
        if (usuarioId == 0) return ["chat", "pessoas", "produtos", "vendas", "compras", "financeiro", "agenda", "outros", "usuarios", "configuracoes"];

        await using var db = new NpgsqlConnection(await _resolver.ObterConnectionString(empresaId));
        await db.OpenAsync(cancellationToken);
        const string sql = "SELECT COALESCE(icones_permitidos,'[]'),COALESCE(aprovado,TRUE),COALESCE(excluido,FALSE),COALESCE(bloqueado_por_master,FALSE) FROM app.usuario WHERE id=@id AND empresa_id=@empresaId LIMIT 1";
        await using var cmd = new NpgsqlCommand(sql, db);
        cmd.Parameters.AddWithValue("id", usuarioId);
        cmd.Parameters.AddWithValue("empresaId", empresaId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || !reader.GetBoolean(1) || reader.GetBoolean(2) || reader.GetBoolean(3))
            throw new SupabaseLoginException(403, "Este usuário está bloqueado ou aguarda liberação da empresa.");
        try { return JsonSerializer.Deserialize<List<string>>(reader.GetString(0)) ?? []; }
        catch (JsonException) { return []; }
    }

    private async Task<HttpResponseMessage> SignInByEmail(HttpClient client, string email, string password, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_url}/auth/v1/token?grant_type=password")
        {
            Content = JsonContent.Create(new { email, password })
        };
        request.Headers.Add("apikey", _key);
        return await client.SendAsync(request, ct);
    }

    private async Task<HttpResponseMessage> SignInByName(HttpClient client, string name, string password, long empresaId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_url}/functions/v1/master-user-invite")
        {
            Content = JsonContent.Create(new { action = "signIn", identificador = name, senha = password, empresaId })
        };
        request.Headers.Add("apikey", _key);
        return await client.SendAsync(request, ct);
    }

    private static bool TryLong(JsonElement row, string key, out long value)
    {
        value = 0;
        return row.TryGetProperty(key, out var property) && property.TryGetInt64(out value);
    }

    private static string? TryString(JsonElement row, string key) =>
        row.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;

    private static string ReadMessage(string payload)
    {
        try
        {
            using var json = JsonDocument.Parse(payload);
            foreach (var key in new[] { "message", "msg", "error_description", "error" })
                if (json.RootElement.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
                    return value.GetString()!;
        }
        catch (JsonException) { }
        return "Não foi possível autenticar no Supabase.";
    }
}

public sealed record SupabaseIdentity(Guid AuthUid, long EmpresaId, long UsuarioId, string Nome);

public sealed class SupabaseLoginException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
