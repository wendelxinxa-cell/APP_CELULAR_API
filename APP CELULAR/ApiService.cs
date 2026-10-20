using System.Net.Http.Json;
using System.Text.Json;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.SignalR.Client;

namespace APP_CELULAR.Services;

public class ApiService
{
    private sealed class RealtimeRetryPolicy : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext) =>
            TimeSpan.FromSeconds(Math.Min(60, 2 + retryContext.PreviousRetryCount * 5));
    }

    private HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(60) };
    private string? _activeEndpoint;
    private HubConnection? _conversaRealtime;

    public bool ConversaRealtimeConectada => _conversaRealtime?.State == HubConnectionState.Connected;

    private readonly JsonSerializerOptions _jsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true
        };

    private HttpClient Client()
    {
        var endpoint = ApiEndpointSettings.Get();
        if (endpoint is null)
            throw new InvalidOperationException("Configure o endereço HTTPS da API na tela inicial antes de conectar.");
        if (_activeEndpoint != endpoint)
        {
            _httpClient.Dispose();
            _httpClient = new HttpClient { BaseAddress = new Uri(endpoint), Timeout = TimeSpan.FromSeconds(60) };
            _activeEndpoint = endpoint;
        }
        return _httpClient;
    }

    // ============================================================
    // TESTE DE CONEXÃO
    // ============================================================

    public async Task<string> TestarConexao()
    {
        var resposta = await Client().GetAsync("api/autenticacao/health");

        resposta.EnsureSuccessStatusCode();

        return await resposta.Content.ReadAsStringAsync();
    }

    // ============================================================
    // SINCRONIZAÇÃO COMPLETA
    // ============================================================

    public async Task<SincronizacaoResponse> Sincronizar(
        SincronizacaoRequest request)
    {
        try
        {
            var resposta = await EnviarPost("api/Sincronizacao/enviar", request, exigirToken: true);

            string conteudo =
                await resposta.Content.ReadAsStringAsync();

            if (!resposta.IsSuccessStatusCode)
            {
                return new SincronizacaoResponse
                {
                    Sucesso = false,
                    Mensagem =
                        $"Erro HTTP {(int)resposta.StatusCode}: {conteudo}"
                };
            }

            var resultado =
                JsonSerializer.Deserialize<SincronizacaoResponse>(
                    conteudo,
                    _jsonOptions);

            return resultado ??
                new SincronizacaoResponse
                {
                    Sucesso = false,
                    Mensagem =
                        "A API retornou uma resposta inválida."
                };
        }
        catch (HttpRequestException ex)
        {
            return new SincronizacaoResponse
            {
                Sucesso = false,
                Mensagem =
                    $"Não foi possível conectar à API: {ex.Message}"
            };
        }
        catch (TaskCanceledException)
        {
            return new SincronizacaoResponse
            {
                Sucesso = false,
                Mensagem =
                    "A comunicação com a API expirou."
            };
        }
        catch (Exception ex)
        {
            return new SincronizacaoResponse
            {
                Sucesso = false,
                Mensagem = ex.Message
            };
        }
    }

    public async Task<LoginApiResponse> Autenticar(LoginApiRequest request)
    {
        request.ChaveInstalacao = ApiEndpointSettings.GetChaveInstalacao();
        request.NomeDispositivo = DeviceInfo.Name;
        using var response = await EnviarPost("api/autenticacao/login", request, exigirToken: false);
        if (!response.IsSuccessStatusCode)
        {
            var detalhe = await response.Content.ReadAsStringAsync();
            string mensagem = detalhe;
            try
            {
                var erro = JsonSerializer.Deserialize<ApiErrorResponse>(detalhe, _jsonOptions);
                if (!string.IsNullOrWhiteSpace(erro?.Mensagem))
                    mensagem = erro.Mensagem;
            }
            catch (JsonException)
            {
                // Mantém o corpo original quando a API não retorna JSON.
            }
            throw new InvalidOperationException($"A API recusou o acesso ({(int)response.StatusCode}): {mensagem}");
        }
        return await response.Content.ReadFromJsonAsync<LoginApiResponse>(_jsonOptions)
            ?? throw new InvalidOperationException("A API retornou uma resposta de login vazia.");
    }

    public async Task<LoginApiResponse> AutenticarSupabase(LoginSupabaseApiRequest request)
    {
        request.ChaveInstalacao = ApiEndpointSettings.GetChaveInstalacao();
        request.NomeDispositivo = DeviceInfo.Name;
        using var response = await EnviarPost("api/autenticacao/login-supabase", request, exigirToken: false);
        var conteudo = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            string mensagem = conteudo;
            try { mensagem = JsonSerializer.Deserialize<ApiErrorResponse>(conteudo, _jsonOptions)?.Mensagem ?? mensagem; }
            catch (JsonException) { }
            throw new InvalidOperationException($"A API recusou o acesso ({(int)response.StatusCode}): {mensagem}");
        }
        return JsonSerializer.Deserialize<LoginApiResponse>(conteudo, _jsonOptions)
            ?? throw new InvalidOperationException("A API retornou uma resposta de login vazia.");
    }

    private sealed class ApiErrorResponse
    {
        public string? Codigo { get; set; }
        public string? Mensagem { get; set; }
    }

    public async Task EncerrarSessaoApi()
    {
        using var response = await EnviarPost("api/autenticacao/sair", new { }, exigirToken: true);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("A API não confirmou o encerramento da sessão.");
    }

    public Task<List<ContatoConversa>> ObterContatos(IdentidadeConversa identidade) =>
        EnviarConversa<List<ContatoConversa>>("api/conversas/contatos", identidade);

    public Task AtualizarApelido(AtualizarApelidoConversa request) =>
        EnviarConversa<object>("api/conversas/apelido", request);

    public async Task<List<MensagemConversa>> ObterMensagens(ConversaRequest request)
    {
        var mensagens = await EnviarConversa<List<MensagemConversa>>("api/conversas/listar", request);
        foreach (var mensagem in mensagens.Where(x => !string.IsNullOrWhiteSpace(x.MidiaChave)))
        {
            var url = await EnviarConversa<UrlMidiaResponse>("api/conversas/midia/url-leitura", new
            {
                request.EmpresaId, request.UsuarioId, request.ContatoId, mensagemId = mensagem.Id
            });
            using var resposta = await Client().GetAsync(url.Url);
            resposta.EnsureSuccessStatusCode();
            mensagem.Foto = await resposta.Content.ReadAsByteArrayAsync();
            mensagem.FotoNome ??= mensagem.MidiaNome ?? Path.GetFileName(new Uri(url.Url).AbsolutePath);
            mensagem.FotoTipo ??= mensagem.MidiaTipo ?? resposta.Content.Headers.ContentType?.MediaType;
        }
        return mensagens;
    }

    public async Task<MensagemConversa> EnviarMensagem(EnviarMensagemConversa request)
    {
        if (request.Foto is { Length: > 0 })
        {
            var nome = request.FotoNome ?? "midia";
            var tipo = request.FotoTipo ?? "application/octet-stream";
            var presign = await EnviarConversa<UrlUploadMidiaResponse>("api/conversas/midia/url-upload", new
            {
                request.EmpresaId, request.UsuarioId, request.ContatoId,
                clienteMensagemId = request.ClienteMensagemId,
                nomeArquivo = nome, tipoConteudo = tipo, tamanhoBytes = request.Foto.LongLength
            });
            using (var conteudo = new ByteArrayContent(request.Foto))
            {
                conteudo.Headers.ContentType = new MediaTypeHeaderValue(tipo);
                using var respostaUpload = await Client().PutAsync(presign.Url, conteudo);
                respostaUpload.EnsureSuccessStatusCode();
            }
            request.MidiaChave = presign.Chave;
            request.MidiaNome = nome;
            request.MidiaTipo = tipo;
            request.MidiaTamanhoBytes = request.Foto.LongLength;
        }
        return await EnviarConversa<MensagemConversa>("api/conversas/enviar", request);
    }

    public Task EnviarAcaoVisual(EnviarAcaoVisualConversa request) =>
        EnviarConversa<object>("api/conversas/acao-visual", request);

    public Task<List<AcaoVisualConversa>> ObterAcoesVisuais(ConversaRequest request) =>
        EnviarConversa<List<AcaoVisualConversa>>("api/conversas/acoes-visuais/pendentes", request);

    public Task<StipopListaResponse> ObterPacotesStipop(IdentidadeConversa identidade) =>
        EnviarConversa<StipopListaResponse>("api/stipop/pacotes", identidade);

    public Task<StipopListaResponse> BuscarFigurinhasStipop(StipopBuscaRequest request) =>
        EnviarConversa<StipopListaResponse>("api/stipop/buscar", request);

    public Task<StipopPacoteResponse> ObterPacoteStipop(StipopPacoteRequest request) =>
        EnviarConversa<StipopPacoteResponse>("api/stipop/pacote", request);

    public Task RegistrarDownloadPacoteStipop(StipopPacoteRequest request) =>
        EnviarConversa<object>("api/stipop/pacote/baixar", request);

    public Task RegistrarEnvioFigurinhaStipop(StipopFigurinhaRequest request) =>
        EnviarConversa<object>("api/stipop/figurinha/enviada", request);

    public async Task MarcarComoLidas(ConversaRequest request) =>
        await EnviarConversa<object>("api/conversas/marcar-lidas", request);

    public async Task IniciarRealtimeConversas(Func<long, Task> aoReceber, CancellationToken cancellationToken)
    {
        await EncerrarRealtimeConversasAsync();
        var endpoint = ApiEndpointSettings.Get()
            ?? throw new InvalidOperationException("Configure o endereço HTTPS da API antes de conectar.");
        var baseUri = new Uri(endpoint.EndsWith('/') ? endpoint : endpoint + "/", UriKind.Absolute);
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(baseUri, "hubs/conversas"), options =>
            {
                options.AccessTokenProvider = async () =>
                {
                    await GarantirTokenDaSessao();
                    return APP_CELULAR.SessaoUsuario.ApiToken;
                };
            })
            .WithAutomaticReconnect(new RealtimeRetryPolicy())
            .Build();

        connection.On<long>("NovaMensagem", aoReceber);
        connection.Reconnecting += _ => Task.CompletedTask;
        connection.Reconnected += async _ => await aoReceber(0);
        connection.Closed += _ => Task.CompletedTask;
        _conversaRealtime = connection;
        try
        {
            await connection.StartAsync(cancellationToken);
        }
        catch
        {
            _conversaRealtime = null;
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task EncerrarRealtimeConversasAsync()
    {
        var connection = _conversaRealtime;
        _conversaRealtime = null;
        if (connection is null) return;
        try { await connection.StopAsync(); }
        finally { await connection.DisposeAsync(); }
    }

    public Task<AprovacoesAcessoResponse> ObterAprovacoesPendentes() =>
        EnviarConversa<AprovacoesAcessoResponse>("api/acessos/pendentes", new { });

    public Task AprovarUsuario(long id) =>
        EnviarConversa<object>($"api/acessos/usuarios/{id}/aprovar", new { });

    public Task AprovarDispositivo(long id) =>
        EnviarConversa<object>($"api/acessos/dispositivos/{id}/aprovar", new { });

    private async Task<T> EnviarConversa<T>(string caminho, object request)
    {
        using var response = await EnviarPost(caminho, request, exigirToken: true);
        if (!response.IsSuccessStatusCode)
        {
            var detalhe = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"A API retornou {(int)response.StatusCode}: {detalhe}");
        }

        return await response.Content.ReadFromJsonAsync<T>(_jsonOptions)
            ?? throw new InvalidOperationException("A API retornou uma resposta vazia.");
    }

    private async Task<HttpResponseMessage> EnviarPost(string caminho, object request, bool exigirToken)
    {
        for (int tentativa = 0; tentativa < (exigirToken ? 2 : 1); tentativa++)
        {
            if (exigirToken &&
                (string.IsNullOrWhiteSpace(APP_CELULAR.SessaoUsuario.ApiToken) ||
                 APP_CELULAR.SessaoUsuario.ApiTokenExpiraEm <= DateTimeOffset.UtcNow.AddMinutes(1)))
                await RenovarTokenDaSessao();

            using var message = new HttpRequestMessage(HttpMethod.Post, caminho)
            {
                Content = JsonContent.Create(request, options: _jsonOptions)
            };
            if (exigirToken)
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", APP_CELULAR.SessaoUsuario.ApiToken);

            var response = await Client().SendAsync(message);
            if (!exigirToken || response.StatusCode != System.Net.HttpStatusCode.Unauthorized || tentativa == 1)
                return response;

            response.Dispose();
            await RenovarTokenDaSessao();
        }
        throw new InvalidOperationException("Não foi possível renovar a sessão da API.");
    }

    private async Task RenovarTokenDaSessao()
    {
        var usuario = APP_CELULAR.SessaoUsuario.UsuarioAtual
            ?? throw new InvalidOperationException("Entre novamente no aplicativo para acessar a API.");
        var resposta = await Autenticar(new LoginApiRequest
        {
            // A empresa fica fixada na sessão obtida no login. A preferência local
            // serve somente para escolher a empresa no primeiro acesso.
            EmpresaId = usuario.EmpresaId,
            Nome = usuario.Nome,
            SenhaHash = usuario.SenhaHash
        });
        usuario.EmpresaId = resposta.EmpresaId;
        usuario.IdServidor = resposta.UsuarioId;
        usuario.TipoNegocio = resposta.TipoNegocio == "chat" ? "chat" : "sucata";
        APP_CELULAR.SessaoUsuario.DispositivoId = resposta.DispositivoId;
        APP_CELULAR.SessaoUsuario.ApiToken = resposta.Token;
        APP_CELULAR.SessaoUsuario.ApiTokenExpiraEm = resposta.ExpiraEm;
    }

    private async Task GarantirTokenDaSessao()
    {
        if (string.IsNullOrWhiteSpace(APP_CELULAR.SessaoUsuario.ApiToken) ||
            APP_CELULAR.SessaoUsuario.ApiTokenExpiraEm <= DateTimeOffset.UtcNow.AddMinutes(1))
            await RenovarTokenDaSessao();
    }
}

public sealed class StipopListaRequest : IdentidadeConversa
{
    public string Categoria { get; set; } = "trending";
    public int PageNumber { get; set; } = 1;
}

public sealed class StipopListaResponse
{
    public List<StipopPacote> Pacotes { get; set; } = new();
    public List<StipopFigurinha> Figurinhas { get; set; } = new();
    public int Pagina { get; set; } = 1;
    public int TotalPaginas { get; set; } = 1;
}

public sealed class StipopPacoteResponse { public StipopPacote Pacote { get; set; } = new(); }

public sealed class StipopPacote
{
    public int PackageId { get; set; }
    public string PackageName { get; set; } = "";
    public string PackageImg { get; set; } = "";
    public string ArtistName { get; set; } = "";
    public string PackageCategory { get; set; } = "";
    public List<StipopFigurinha> Stickers { get; set; } = new();
}

public sealed class StipopFigurinha
{
    public int StickerId { get; set; }
    public int PackageId { get; set; }
    public string StickerImg { get; set; } = "";
}

public sealed class StipopBuscaRequest : IdentidadeConversa
{
    public string Termo { get; set; } = "";
    public int PageNumber { get; set; } = 1;
}

public sealed class StipopPacoteRequest : IdentidadeConversa { public int PackageId { get; set; } }
public sealed class StipopFigurinhaRequest : IdentidadeConversa { public int StickerId { get; set; } }

public static class ApiEndpointSettings
{
    private const string PreferenceKey = "API_BASE_URL";
    private const string CompanyPreferenceKey = "API_EMPRESA_ID";
    private const string InstallationKey = "API_INSTALLATION_ID";
    private const string DefaultApiEndpoint = "https://app-celular-api-test.onrender.com";

    public static long GetEmpresaId()
    {
        var empresaId = Preferences.Default.Get(CompanyPreferenceKey, 1L);
        return empresaId > 0 ? empresaId : 1L;
    }

    public static void SetEmpresaId(long empresaId)
    {
        if (empresaId <= 0) throw new ArgumentOutOfRangeException(nameof(empresaId));
        Preferences.Default.Set(CompanyPreferenceKey, empresaId);
    }

    public static Guid GetChaveInstalacao()
    {
        var value = Preferences.Default.Get(InstallationKey, "");
        if (Guid.TryParse(value, out var existing) && existing != Guid.Empty)
            return existing;
        var created = Guid.NewGuid();
        Preferences.Default.Set(InstallationKey, created.ToString("D"));
        return created;
    }

    public static string? Get()
    {
        var value = Preferences.Default.Get(PreferenceKey, DefaultApiEndpoint).Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return null;
        return uri.ToString().TrimEnd('/') + "/";
    }

    public static void Set(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Informe um endereço HTTPS válido, por exemplo https://api.suaempresa.com.");
        Preferences.Default.Set(PreferenceKey, uri.ToString().TrimEnd('/') + "/");
    }
}


// ================================================================
// REQUEST
// ================================================================

public class SincronizacaoRequest
{
    public long EmpresaId { get; set; }

    public long DispositivoId { get; set; }

    public List<PessoaSync> Pessoas { get; set; } = new();

    public List<MaterialSync> Materiais { get; set; } = new();

    public List<CompraSync> Compras { get; set; } = new();

    public List<ItemCompraSync> ItensCompra { get; set; } = new();

    public List<VendaSync> Vendas { get; set; } = new();

    public List<ItemVendaSync> ItensVenda { get; set; } = new();

    public List<UsuarioSync> Usuarios { get; set; } = new();

    public List<ConfiguracaoSync> Configuracoes { get; set; } = new();
}


// ================================================================
// PESSOA
// ================================================================

public class PessoaSync
{
    public int? IdLocal { get; set; }

    public long? IdServidor { get; set; }

    public long EmpresaId { get; set; }

    public long DispositivoId { get; set; }

    public string Nome { get; set; } = "";

    public string Documento { get; set; } = "";

    public string Telefone { get; set; } = "";

    public string Email { get; set; } = "";

    public string Endereco { get; set; } = "";

    public string Cidade { get; set; } = "";

    public bool Cliente { get; set; }

    public bool Fornecedor { get; set; }

    public DateTime DataCadastro { get; set; }

    public DateTime DataAlteracao { get; set; }

    public string StatusSincronizacao { get; set; } = "PENDENTE";

    public string MensagemErro { get; set; } = "";

    public bool Excluido { get; set; }
}


// ================================================================
// MATERIAL
// ================================================================

public class MaterialSync
{
    public int? IdLocal { get; set; }

    public long? IdServidor { get; set; }

    public long EmpresaId { get; set; }

    public long DispositivoId { get; set; }

    public string Nome { get; set; } = "";

    public string Unidade { get; set; } = "KG";

    public decimal PrecoCompra { get; set; }

    public decimal PrecoVenda { get; set; }

    public decimal EstoqueAtual { get; set; }

    public DateTime DataCadastro { get; set; }

    public DateTime DataAlteracao { get; set; }

    public string StatusSincronizacao { get; set; } = "PENDENTE";

    public string MensagemErro { get; set; } = "";

    public bool Excluido { get; set; }
}


// ================================================================
// COMPRA
// ================================================================

public class CompraSync
{
    public int? IdLocal { get; set; }

    public long? IdServidor { get; set; }

    public long EmpresaId { get; set; }

    public long DispositivoId { get; set; }

    public int PessoaId { get; set; }

    public string PessoaNome { get; set; } = "";

    public DateTime DataCompra { get; set; }

    public decimal ValorTotal { get; set; }

    public string Observacao { get; set; } = "";

    public string Status { get; set; } = "";

    public string StatusSincronizacao { get; set; } = "PENDENTE";

    public string MensagemErro { get; set; } = "";

    public DateTime DataAlteracao { get; set; }

    public bool Excluido { get; set; }
}


// ================================================================
// ITEM COMPRA
// ================================================================

public class ItemCompraSync
{
    public int? IdLocal { get; set; }

    public long? IdServidor { get; set; }

    public int CompraId { get; set; }

    public int MaterialId { get; set; }

    public string MaterialNome { get; set; } = "";

    public string Unidade { get; set; } = "KG";

    public decimal Quantidade { get; set; }

    public decimal PrecoUnitario { get; set; }

    public decimal ValorTotal { get; set; }

    public decimal QuantidadePerda { get; set; }

    public decimal QuantidadeLiquida { get; set; }

    public bool EstoqueLancado { get; set; }

    public string StatusSincronizacao { get; set; } = "PENDENTE";

    public string MensagemErro { get; set; } = "";

    public DateTime DataAlteracao { get; set; }

    public long EmpresaId { get; set; }

    public long DispositivoId { get; set; }

    public bool Excluido { get; set; }
}


// ================================================================
// VENDA
// ================================================================

public class VendaSync
{
    public int? IdLocal { get; set; }

    public long? IdServidor { get; set; }

    public long EmpresaId { get; set; }

    public long DispositivoId { get; set; }

    public int PessoaId { get; set; }

    public string PessoaNome { get; set; } = "";

    public DateTime DataVenda { get; set; }

    public decimal ValorTotal { get; set; }

    public string Observacao { get; set; } = "";

    public string Status { get; set; } = "";

    public string StatusSincronizacao { get; set; } = "PENDENTE";

    public string MensagemErro { get; set; } = "";

    public DateTime DataAlteracao { get; set; }

    public bool Excluido { get; set; }
}


// ================================================================
// ITEM VENDA
// ================================================================

public class ItemVendaSync
{
    public int? IdLocal { get; set; }

    public long? IdServidor { get; set; }

    public int VendaId { get; set; }

    public int MaterialId { get; set; }

    public string MaterialNome { get; set; } = "";

    public string Unidade { get; set; } = "KG";

    public decimal Quantidade { get; set; }

    public decimal PrecoUnitario { get; set; }

    public decimal ValorTotal { get; set; }

    public string StatusSincronizacao { get; set; } = "PENDENTE";

    public string MensagemErro { get; set; } = "";

    public DateTime DataAlteracao { get; set; }

    public long EmpresaId { get; set; }

    public long DispositivoId { get; set; }

    public bool Excluido { get; set; }
}


// ================================================================
// USUARIO
// ================================================================

public class UsuarioSync
{
    public int? IdLocal { get; set; }

    public long? IdServidor { get; set; }

    public string Nome { get; set; } = "";

    public string SenhaHash { get; set; } = "";

    public string Funcao { get; set; } = "USUARIO";

    public string TipoNegocio { get; set; } = "sucata";

    public bool EhMaster { get; set; }

    public long EmpresaId { get; set; }

    public string StatusSincronizacao { get; set; } = "PENDENTE";

    public string MensagemErro { get; set; } = "";

    public DateTime DataCadastro { get; set; }

    public DateTime DataAlteracao { get; set; }

    public bool Excluido { get; set; }
}


// ================================================================
// CONFIGURAÇÃO
// ================================================================

public class ConfiguracaoSync
{
    public int? IdLocal { get; set; }

    public string Chave { get; set; } = "";

    public string Valor { get; set; } = "";

    public long EmpresaId { get; set; }

    public string StatusSincronizacao { get; set; } = "PENDENTE";

    public string MensagemErro { get; set; } = "";

    public DateTime DataAlteracao { get; set; }

    public bool Excluido { get; set; }
}


// ================================================================
// RESPONSE
// ================================================================

public class SincronizacaoResponse
{
    public bool Sucesso { get; set; }

    public string Mensagem { get; set; } = "";

    public List<RegistroSincronizado> Registros { get; set; } = new();

    public List<ErroSincronizacao> Erros { get; set; } = new();
}


public class RegistroSincronizado
{
    public string Tabela { get; set; } = "";

    public int? IdLocal { get; set; }

    public long IdServidor { get; set; }
}


public class ErroSincronizacao
{
    public string Tabela { get; set; } = "";

    public int? IdLocal { get; set; }

    public string Mensagem { get; set; } = "";
}
