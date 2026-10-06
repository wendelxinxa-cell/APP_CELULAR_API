using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WebPrototype.Services;

public sealed class ChatApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public string HashSenha(string senha) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(senha)));

    public Task<ChatLoginResponse> EntrarAsync(long empresaId, string usuario, string senha, Guid chaveInstalacao) =>
        PostAsync<ChatLoginResponse>("api/autenticacao/login", new ChatLoginRequest
        {
            EmpresaId = empresaId,
            Nome = usuario,
            SenhaHash = HashSenha(senha),
            ChaveInstalacao = chaveInstalacao,
            NomeDispositivo = "APP CELULAR Web"
        });

    public Task<List<ContatoConversa>> ObterContatosAsync(ChatSessao sessao) =>
        PostAsync<List<ContatoConversa>>("api/conversas/contatos", new IdentidadeConversa
        {
            EmpresaId = sessao.EmpresaId,
            UsuarioId = sessao.UsuarioId
        }, sessao.Token);

    public Task<List<MensagemConversa>> ObterMensagensAsync(ChatSessao sessao, long contatoId) =>
        PostAsync<List<MensagemConversa>>("api/conversas/listar", new ConversaRequest
        {
            EmpresaId = sessao.EmpresaId,
            UsuarioId = sessao.UsuarioId,
            ContatoId = contatoId
        }, sessao.Token);

    public Task<MensagemConversa> EnviarMensagemAsync(ChatSessao sessao, long contatoId, string texto) =>
        PostAsync<MensagemConversa>("api/conversas/enviar", new EnviarMensagemRequest
        {
            EmpresaId = sessao.EmpresaId,
            UsuarioId = sessao.UsuarioId,
            ContatoId = contatoId,
            ClienteMensagemId = Guid.NewGuid(),
            Texto = texto
        }, sessao.Token);

    public Task MarcarComoLidasAsync(ChatSessao sessao, long contatoId) =>
        PostSemRespostaAsync("api/conversas/marcar-lidas", new ConversaRequest
        {
            EmpresaId = sessao.EmpresaId,
            UsuarioId = sessao.UsuarioId,
            ContatoId = contatoId
        }, sessao.Token);

    public Task EnviarAcaoVisualAsync(ChatSessao sessao, long contatoId, string tipo, string simbolo) =>
        PostSemRespostaAsync("api/conversas/acao-visual", new EnviarAcaoVisualRequest
        {
            EmpresaId = sessao.EmpresaId,
            UsuarioId = sessao.UsuarioId,
            ContatoId = contatoId,
            Tipo = tipo,
            Simbolo = simbolo
        }, sessao.Token);

    public Task<List<AcaoVisualConversa>> ObterAcoesVisuaisAsync(ChatSessao sessao, long contatoId) =>
        PostAsync<List<AcaoVisualConversa>>("api/conversas/acoes-visuais/pendentes", new ConversaRequest
        {
            EmpresaId = sessao.EmpresaId,
            UsuarioId = sessao.UsuarioId,
            ContatoId = contatoId
        }, sessao.Token);

    public Task SairAsync(ChatSessao sessao) =>
        PostSemRespostaAsync("api/autenticacao/sair", new { }, sessao.Token);

    private async Task<T> PostAsync<T>(string path, object body, string? token = null)
    {
        using var request = CriarRequest(path, body, token);
        using var response = await EnviarAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw CriarErro(response.StatusCode, content);

        try
        {
            return JsonSerializer.Deserialize<T>(content, JsonOptions)
                ?? throw new InvalidOperationException("A API retornou uma resposta vazia.");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("A API retornou uma resposta que o protótipo não reconheceu.");
        }
    }

    private async Task PostSemRespostaAsync(string path, object body, string? token)
    {
        using var request = CriarRequest(path, body, token);
        using var response = await EnviarAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            throw CriarErro(response.StatusCode, content);
        }
    }

    private static HttpRequestMessage CriarRequest(string path, object body, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task<HttpResponseMessage> EnviarAsync(HttpRequestMessage request)
    {
        try
        {
            return await http.SendAsync(request);
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("Não foi possível conectar ao serviço de conversas. Tente novamente.");
        }
        catch (TaskCanceledException)
        {
            throw new InvalidOperationException("O serviço de conversas demorou para responder. Tente novamente.");
        }
    }

    private static InvalidOperationException CriarErro(System.Net.HttpStatusCode status, string content)
    {
        try
        {
            var error = JsonSerializer.Deserialize<ApiError>(content, JsonOptions);
            if (!string.IsNullOrWhiteSpace(error?.Mensagem))
                return new InvalidOperationException(error.Mensagem);
        }
        catch (JsonException)
        {
            // Respostas que não sejam JSON usam uma mensagem curta sem conteúdo do servidor.
        }

        return new InvalidOperationException($"A API recusou a operação ({(int)status}).");
    }

    private sealed class ApiError
    {
        public string? Mensagem { get; set; }
    }
}

public sealed class ChatPortalState(ChatApiClient api)
{
    public ChatSessao? Sessao { get; private set; }
    public bool Autenticado => Sessao is not null && Sessao.ExpiraEm > DateTimeOffset.UtcNow;

    public async Task EntrarAsync(long empresaId, string usuario, string senha, Guid chaveInstalacao)
    {
        var login = await api.EntrarAsync(empresaId, usuario.Trim(), senha, chaveInstalacao);
        Sessao = new ChatSessao
        {
            EmpresaId = login.EmpresaId,
            UsuarioId = login.UsuarioId,
            Usuario = usuario.Trim(),
            Token = login.Token,
            ExpiraEm = login.ExpiraEm
        };
    }

    public Task<List<ContatoConversa>> ObterContatosAsync() =>
        api.ObterContatosAsync(ObterSessao());

    public Task<List<MensagemConversa>> ObterMensagensAsync(long contatoId) =>
        api.ObterMensagensAsync(ObterSessao(), contatoId);

    public Task<MensagemConversa> EnviarMensagemAsync(long contatoId, string texto) =>
        api.EnviarMensagemAsync(ObterSessao(), contatoId, texto);

    public Task MarcarComoLidasAsync(long contatoId) =>
        api.MarcarComoLidasAsync(ObterSessao(), contatoId);

    public Task EnviarAcaoVisualAsync(long contatoId, string tipo, string simbolo) =>
        api.EnviarAcaoVisualAsync(ObterSessao(), contatoId, tipo, simbolo);

    public Task<List<AcaoVisualConversa>> ObterAcoesVisuaisAsync(long contatoId) =>
        api.ObterAcoesVisuaisAsync(ObterSessao(), contatoId);

    public async Task SairAsync()
    {
        var sessao = Sessao;
        Sessao = null;
        if (sessao is not null && sessao.ExpiraEm > DateTimeOffset.UtcNow)
            await api.SairAsync(sessao);
    }

    private ChatSessao ObterSessao()
    {
        if (Sessao is null || Sessao.ExpiraEm <= DateTimeOffset.UtcNow)
        {
            Sessao = null;
            throw new InvalidOperationException("Sua sessão expirou. Entre novamente.");
        }

        return Sessao;
    }
}

public sealed class ChatSessao
{
    public long EmpresaId { get; init; }
    public long UsuarioId { get; init; }
    public string Usuario { get; init; } = "";
    public string Token { get; init; } = "";
    public DateTimeOffset ExpiraEm { get; init; }
}

public sealed class ChatLoginRequest
{
    public long EmpresaId { get; set; }
    public string Nome { get; set; } = "";
    public string SenhaHash { get; set; } = "";
    public Guid ChaveInstalacao { get; set; }
    public string NomeDispositivo { get; set; } = "";
}

public sealed class ChatLoginResponse
{
    public string Token { get; set; } = "";
    public long EmpresaId { get; set; }
    public long UsuarioId { get; set; }
    public DateTimeOffset ExpiraEm { get; set; }
}

public class IdentidadeConversa
{
    public long EmpresaId { get; set; }
    public long UsuarioId { get; set; }
}

public class ConversaRequest : IdentidadeConversa
{
    public long ContatoId { get; set; }
}

public sealed class EnviarMensagemRequest : ConversaRequest
{
    public Guid ClienteMensagemId { get; set; }
    public string Texto { get; set; } = "";
}

public sealed class EnviarAcaoVisualRequest : ConversaRequest
{
    public string Tipo { get; set; } = "WINK";
    public string Simbolo { get; set; } = "✨";
}

public sealed class AcaoVisualConversa
{
    public string Tipo { get; set; } = "WINK";
    public string Simbolo { get; set; } = "✨";
}

public sealed class ContatoConversa
{
    public long Id { get; set; }
    public string Nome { get; set; } = "";
    public string NomeOriginal { get; set; } = "";
    public string UltimaMensagem { get; set; } = "";
    public DateTime? DataUltimaMensagem { get; set; }
    public int NaoLidas { get; set; }
}

public sealed class MensagemConversa
{
    public long Id { get; set; }
    public long RemetenteId { get; set; }
    public string RemetenteNome { get; set; } = "";
    public long DestinatarioId { get; set; }
    public string Texto { get; set; } = "";
    public byte[]? Foto { get; set; }
    public string? FotoNome { get; set; }
    public string? FotoTipo { get; set; }
    public DateTime EnviadaEm { get; set; }
    public DateTime? LidaEm { get; set; }
}
