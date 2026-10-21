using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using APP_CELULAR.Models;

namespace APP_CELULAR.Services;

public sealed class MasterEmpresasApiService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };

    private Uri BaseAddress()
    {
        string? endpoint = ApiEndpointSettings.Get();
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException("Configure primeiro o endereço da API.");
        return new Uri(endpoint.TrimEnd('/') + "/");
    }

    public async Task<string> Login(string username, string password)
    {
        using var response = await _http.PostAsJsonAsync(new Uri(BaseAddress(), "api/administracao/master/login"),
            new { usuario = username, senha = password }, JsonOptions);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ErrorMessage(response));
        var token = await response.Content.ReadFromJsonAsync<MasterLoginResponse>(JsonOptions);
        return token?.Token ?? throw new InvalidOperationException("A API não retornou uma sessão Master.");
    }

    public async Task<List<EmpresaCatalogoItem>> Listar(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(BaseAddress(), "api/administracao/empresas"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ErrorMessage(response));
        return await response.Content.ReadFromJsonAsync<List<EmpresaCatalogoItem>>(JsonOptions) ?? new();
    }

    public async Task<EmpresaCatalogoItem> Cadastrar(string token, string nome, string emailResponsavel, string connectionString)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseAddress(), "api/administracao/empresas"))
        {
            Content = JsonContent.Create(new { nome, emailResponsavel, connectionString }, options: JsonOptions)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ErrorMessage(response));
        return await response.Content.ReadFromJsonAsync<EmpresaCatalogoItem>(JsonOptions)
            ?? throw new InvalidOperationException("A API não retornou os dados da empresa criada.");
    }

    public async Task Aprovar(string token, long empresaId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseAddress(), $"api/administracao/empresas/{empresaId}/aprovar"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ErrorMessage(response));
    }

    public Task<AprovacoesAcessoResponse> ObterAprovacoesPendentes(string token) =>
        EnviarAutorizado<AprovacoesAcessoResponse>(token, HttpMethod.Get, "api/administracao/acessos/pendentes");

    public Task<List<CadastroPendente>> ListarCadastrosPendentes(string token) =>
        EnviarAutorizado<List<CadastroPendente>>(token, HttpMethod.Get, "api/administracao/acessos/cadastros");

    public Task AprovarCadastro(string token, long id, long empresaId, long usuarioId, string tipoNegocio) =>
        EnviarAutorizado<object>(token, HttpMethod.Post, $"api/administracao/acessos/cadastros/{id}/aprovar", new { empresaId, usuarioId, tipoNegocio });

    public Task RecusarCadastro(string token, long id) =>
        EnviarAutorizado<object>(token, HttpMethod.Post, $"api/administracao/acessos/cadastros/{id}/recusar", new { });

    public Task<List<StatusUsuarioAcesso>> ObterStatusUsuarios(string token) =>
        EnviarAutorizado<List<StatusUsuarioAcesso>>(token, HttpMethod.Get, "api/administracao/acessos/usuarios");

    public Task<List<MasterChatConversa>> ObterConversasChat(string token) =>
        EnviarAutorizado<List<MasterChatConversa>>(token, HttpMethod.Get, "api/administracao/chat/contatos");

    public Task<List<MensagemConversa>> ObterMensagensChat(string token, MasterChatConversa chat) =>
        EnviarAutorizado<List<MensagemConversa>>(token, HttpMethod.Get,
            $"api/administracao/chat/empresas/{chat.EmpresaId}/usuarios/{chat.UsuarioId}/contatos/{chat.ContatoId}/mensagens");

    public Task MarcarChatComoLido(string token, MasterChatConversa chat) =>
        EnviarAutorizado<object>(token, HttpMethod.Post,
            $"api/administracao/chat/empresas/{chat.EmpresaId}/usuarios/{chat.UsuarioId}/contatos/{chat.ContatoId}/marcar-lidas",
            new { });

    public Task DefinirBloqueioUsuario(string token, long empresaId, long id, bool bloqueado) =>
        EnviarAutorizado<object>(token, HttpMethod.Post,
            $"api/administracao/acessos/empresas/{empresaId}/usuarios/{id}/bloqueio",
            new { bloqueado });

    public Task DefinirTipoNegocioUsuario(string token, long empresaId, long id, string tipoNegocio) =>
        EnviarAutorizado<object>(token, HttpMethod.Put,
            $"api/administracao/acessos/empresas/{empresaId}/usuarios/{id}/tipo-negocio",
            new { tipoNegocio });

    public async Task<long> RegistrarUsuario(string token, Usuario usuario)
    {
        var resultado = await EnviarAutorizado<RegistroUsuarioAcessoResponse>(token, HttpMethod.Post, "api/administracao/acessos/usuarios", new
        {
            empresaId = usuario.EmpresaId,
            idLocal = usuario.Id,
            idServidor = usuario.IdServidor,
            nome = usuario.Nome,
            senhaHash = usuario.SenhaHash,
            funcao = usuario.Funcao,
            tipoNegocio = usuario.TipoNegocio,
            criadoEm = usuario.DataCadastro,
            alteradoEm = usuario.DataAlteracao,
            bloqueado = usuario.Excluido
        });
        return resultado.Id;
    }

    public Task AprovarAcessoUsuario(string token, long empresaId, long id) =>
        EnviarAutorizado<object>(token, HttpMethod.Post, $"api/administracao/acessos/empresas/{empresaId}/usuarios/{id}/aprovar", new { });

    public Task AprovarAcessoDispositivo(string token, long empresaId, long id) =>
        EnviarAutorizado<object>(token, HttpMethod.Post, $"api/administracao/acessos/empresas/{empresaId}/dispositivos/{id}/aprovar", new { });

    private async Task<T> EnviarAutorizado<T>(string token, HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(BaseAddress(), path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);
        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await ErrorMessage(response));
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions)
            ?? throw new InvalidOperationException("A API retornou uma resposta vazia.");
    }

    private static async Task<string> ErrorMessage(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<ApiError>(JsonOptions);
        return body?.Mensagem ?? $"A API respondeu com o código {(int)response.StatusCode}.";
    }

    private sealed class MasterLoginResponse { public string Token { get; set; } = ""; }
    private sealed class ApiError { public string Mensagem { get; set; } = ""; }
    private sealed class RegistroUsuarioAcessoResponse { public long Id { get; set; } }
}

public sealed class EmpresaCatalogoItem
{
    public long Id { get; set; }
    public string Nome { get; set; } = "";
    public string EmailResponsavel { get; set; } = "";
    public string Status { get; set; } = "PENDENTE";
    public DateTimeOffset CriadaEm { get; set; }
}

public sealed class CadastroPendente
{
    public long Id { get; set; }
    public string Email { get; set; } = "";
    public string Nome { get; set; } = "";
    public long? EmpresaId { get; set; }
    public DateTime CriadaEm { get; set; }
    public bool EmailVerificado { get; set; }
}
