using Microsoft.JSInterop;

namespace WebPrototype.Services;

/// <summary>
/// Supabase browser client bridge. Database, Auth, Realtime and Storage traffic
/// is sent by supabase-js from the user's browser directly to Supabase.
/// </summary>
public sealed class SupabaseChatClient(IJSRuntime js)
{
    public Task InicializarAsync() => InvokeVoidAsync("appCelular.supabase.configureFromPage");

    public async Task<ChatSessao?> RestaurarSessaoAsync()
    {
        await InicializarAsync();
        return await InvokeAsync<ChatSessao?>("appCelular.supabase.restoreSession");
    }

    public async Task<ChatSessao> EntrarAsync(string email, string senha)
    {
        await InicializarAsync();
        return await InvokeAsync<ChatSessao>("appCelular.supabase.signIn", email.Trim(), senha);
    }

    public async Task<bool> EstaEmRecuperacaoSenhaAsync()
    {
        await InicializarAsync();
        return await InvokeAsync<bool>("appCelular.supabase.isPasswordRecovery");
    }

    public async Task SolicitarRedefinicaoSenhaAsync(string email)
    {
        await InicializarAsync();
        await InvokeVoidAsync("appCelular.supabase.requestPasswordRecovery", email.Trim());
    }

    public async Task AtualizarSenhaAsync(string senha)
    {
        await InicializarAsync();
        await InvokeVoidAsync("appCelular.supabase.updatePassword", senha);
    }

    public async Task<bool> SolicitarAcessoAsync(string email, string senha, string username)
    {
        await InicializarAsync();
        return await InvokeAsync<bool>("appCelular.supabase.signUp", email.Trim(), senha, username.Trim());
    }

    public async Task ConfirmarEmailCadastroAsync()
    {
        await InicializarAsync();
        await InvokeVoidAsync("appCelular.supabase.confirmSignupEmail");
    }

    public Task<bool> EhZeusAsync() => InvokeAsync<bool>("appCelular.supabase.isZeus");
    public Task<List<SolicitacaoAcesso>> ListarSolicitacoesAsync() => InvokeAsync<List<SolicitacaoAcesso>>("appCelular.supabase.listAccessRequests");
    public Task AprovarSolicitacaoAsync(string authUid, long empresaId, string tipoNegocio) => InvokeVoidAsync("appCelular.supabase.approveAccessRequest", authUid, empresaId, tipoNegocio);
    public Task RecusarSolicitacaoAsync(string authUid) => InvokeVoidAsync("appCelular.supabase.rejectAccessRequest", authUid);
    public Task<List<AprovacaoUsuarioAparelho>> ListarUsuariosAparelhosPendentesAsync() => InvokeAsync<List<AprovacaoUsuarioAparelho>>("appCelular.supabase.listPendingUserDevices");
    public Task AprovarUsuarioAparelhoAsync(AprovacaoUsuarioAparelho request) => InvokeVoidAsync("appCelular.supabase.approvePendingUserDevice", request);
    public Task<List<UsuarioAuthGerenciavel>> ListarUsuariosGerenciaveisAsync() =>
        InvokeAsync<List<UsuarioAuthGerenciavel>>("appCelular.supabase.listManagedUsers");
    public Task<List<CadastroPendente>> ListarCadastrosPendentesAsync() => InvokeAsync<List<CadastroPendente>>("appCelular.supabase.listSignupRequests");
    public Task AprovarCadastroAsync(long requestId, long usuarioId) => InvokeVoidAsync("appCelular.supabase.approveSignupRequest", requestId, usuarioId);
    public Task RecusarCadastroAsync(long requestId) => InvokeVoidAsync("appCelular.supabase.rejectSignupRequest", requestId);
    public Task<List<UsuarioEmpresaDisponivel>> ListarUsuariosEmpresaAsync() => InvokeAsync<List<UsuarioEmpresaDisponivel>>("appCelular.supabase.listCompanyUsers");
    public Task DefinirUsuarioMasterAsync(string authUid, bool ativo) =>
        InvokeVoidAsync("appCelular.supabase.setManagedUserMaster", authUid, ativo);
    public Task DefinirUsuarioAtivoAsync(string authUid, bool ativo) =>
        InvokeVoidAsync("appCelular.supabase.setManagedUserActive", authUid, ativo);
    public Task<ConviteUsuarioResponse> ConvidarUsuarioAsync(string email, string nome, bool master, long empresaId, long? usuarioId) =>
        InvokeAsync<ConviteUsuarioResponse>("appCelular.supabase.inviteManagedUser", email, nome, master, empresaId, usuarioId);

    public Task SairAsync() => InvokeVoidAsync("appCelular.supabase.signOut");

    public Task<List<ContatoConversa>> ObterContatosAsync() =>
        InvokeAsync<List<ContatoConversa>>("appCelular.supabase.listContacts");

    public Task<List<MensagemConversa>> ObterMensagensAsync(long contatoId, long depoisDoId = 0) =>
        InvokeAsync<List<MensagemConversa>>("appCelular.supabase.listMessages", contatoId, depoisDoId);

    public Task<string> UploadMidiaAsync(long contatoId, Guid clienteMensagemId, string inputId, string nome, string tipo) =>
        InvokeAsync<string>("appCelular.supabase.uploadMessageMedia", contatoId, clienteMensagemId.ToString(), inputId, nome, tipo);

    public async Task<UrlMidiaResponse> CriarUrlLeituraAsync(string caminho)
    {
        var url = await InvokeAsync<string>("appCelular.supabase.createSignedMediaUrl", caminho, 3600);
        return new UrlMidiaResponse { Url = url };
    }

    public Task<MensagemConversa> EnviarMensagemAsync(long contatoId, string texto, Guid clienteMensagemId, string? midiaChave, string? midiaNome, string? midiaTipo, long? midiaTamanhoBytes) =>
        InvokeAsync<MensagemConversa>("appCelular.supabase.sendMessage", contatoId, texto, clienteMensagemId.ToString(), midiaChave, midiaNome, midiaTipo, midiaTamanhoBytes);

    public Task MarcarComoLidasAsync(long contatoId) =>
        InvokeVoidAsync("appCelular.supabase.markRead", contatoId);

    public Task EnviarAcaoVisualAsync(long contatoId, string tipo, string simbolo) =>
        InvokeVoidAsync("appCelular.supabase.sendVisualAction", contatoId, tipo, simbolo);

    public Task<List<AcaoVisualConversa>> ObterAcoesVisuaisAsync(long contatoId) =>
        InvokeAsync<List<AcaoVisualConversa>>("appCelular.supabase.getVisualActions", contatoId);

    private async Task<T> InvokeAsync<T>(string identifier, params object?[] args)
    {
        try { return await js.InvokeAsync<T>(identifier, args); }
        catch (JSException ex) { throw new InvalidOperationException(ex.Message, ex); }
    }

    private async Task InvokeVoidAsync(string identifier, params object?[] args)
    {
        try { await js.InvokeVoidAsync(identifier, args); }
        catch (JSException ex) { throw new InvalidOperationException(ex.Message, ex); }
    }

}

public sealed class ChatPortalState(SupabaseChatClient supabase)
{
    private List<ContatoConversa> contatosSessao = [];
    private bool contatosCarregados;
    public ChatSessao? Sessao { get; private set; }
    public bool Autenticado => Sessao is not null && Sessao.ExpiraEm > DateTimeOffset.UtcNow;
    public bool EhZeus { get; private set; }

    public async Task<bool> RestaurarSessaoAsync()
    {
        Sessao = await supabase.RestaurarSessaoAsync();
        EhZeus = Sessao is not null && await supabase.EhZeusAsync();
        LimparContatosSessao();
        return Autenticado;
    }

    public async Task EntrarAsync(string email, string senha)
    {
        LimparContatosSessao();
        Sessao = await supabase.EntrarAsync(email, senha);
        EhZeus = await supabase.EhZeusAsync();
    }

    public Task<bool> EstaEmRecuperacaoSenhaAsync() => supabase.EstaEmRecuperacaoSenhaAsync();
    public Task SolicitarRedefinicaoSenhaAsync(string email) => supabase.SolicitarRedefinicaoSenhaAsync(email);
    public Task AtualizarSenhaAsync(string senha) => supabase.AtualizarSenhaAsync(senha);

    public async Task<List<ContatoConversa>> ObterContatosAsync()
    {
        if (!contatosCarregados)
        {
            contatosSessao = await supabase.ObterContatosAsync();
            contatosCarregados = true;
        }
        return [.. contatosSessao];
    }

    public Task<List<MensagemConversa>> ObterMensagensAsync(long contatoId, long depoisDoId = 0) => supabase.ObterMensagensAsync(contatoId, depoisDoId);
    public Task<string> UploadMidiaAsync(long contatoId, Guid id, string inputId, string nome, string tipo) => supabase.UploadMidiaAsync(contatoId, id, inputId, nome, tipo);
    public Task<UrlMidiaResponse> CriarUrlLeituraAsync(string caminho) => supabase.CriarUrlLeituraAsync(caminho);
    public Task<MensagemConversa> EnviarMensagemAsync(long contatoId, string texto, Guid id, string? chave, string? nome, string? tipo, long? tamanho) => supabase.EnviarMensagemAsync(contatoId, texto, id, chave, nome, tipo, tamanho);
    public Task MarcarComoLidasAsync(long contatoId) => supabase.MarcarComoLidasAsync(contatoId);
    public Task EnviarAcaoVisualAsync(long contatoId, string tipo, string simbolo) => supabase.EnviarAcaoVisualAsync(contatoId, tipo, simbolo);
    public Task<List<AcaoVisualConversa>> ObterAcoesVisuaisAsync(long contatoId) => supabase.ObterAcoesVisuaisAsync(contatoId);
    public Task<bool> SolicitarAcessoAsync(string email, string senha, string username) => supabase.SolicitarAcessoAsync(email, senha, username);
    public Task ConfirmarEmailCadastroAsync() => supabase.ConfirmarEmailCadastroAsync();
    public Task<List<SolicitacaoAcesso>> ListarSolicitacoesAsync() => supabase.ListarSolicitacoesAsync();
    public Task AprovarSolicitacaoAsync(string authUid, long empresaId, string tipoNegocio) => supabase.AprovarSolicitacaoAsync(authUid, empresaId, tipoNegocio);
    public Task RecusarSolicitacaoAsync(string authUid) => supabase.RecusarSolicitacaoAsync(authUid);
    public Task<List<AprovacaoUsuarioAparelho>> ListarUsuariosAparelhosPendentesAsync() => supabase.ListarUsuariosAparelhosPendentesAsync();
    public Task AprovarUsuarioAparelhoAsync(AprovacaoUsuarioAparelho request) => supabase.AprovarUsuarioAparelhoAsync(request);
    public Task<List<UsuarioAuthGerenciavel>> ListarUsuariosGerenciaveisAsync() => supabase.ListarUsuariosGerenciaveisAsync();
    public Task<List<CadastroPendente>> ListarCadastrosPendentesAsync() => supabase.ListarCadastrosPendentesAsync();
    public Task AprovarCadastroAsync(long requestId, long usuarioId) => supabase.AprovarCadastroAsync(requestId, usuarioId);
    public Task RecusarCadastroAsync(long requestId) => supabase.RecusarCadastroAsync(requestId);
    public Task<List<UsuarioEmpresaDisponivel>> ListarUsuariosEmpresaAsync() => supabase.ListarUsuariosEmpresaAsync();
    public Task DefinirUsuarioMasterAsync(string authUid, bool ativo) => supabase.DefinirUsuarioMasterAsync(authUid, ativo);
    public Task DefinirUsuarioAtivoAsync(string authUid, bool ativo) => supabase.DefinirUsuarioAtivoAsync(authUid, ativo);
    public Task<ConviteUsuarioResponse> ConvidarUsuarioAsync(string email, string nome, bool master, long empresaId, long? usuarioId) =>
        supabase.ConvidarUsuarioAsync(email, nome, master, empresaId, usuarioId);

    public async Task SairAsync()
    {
        Sessao = null;
        EhZeus = false;
        LimparContatosSessao();
        try { await supabase.SairAsync(); } catch (InvalidOperationException) { }
    }

    private void LimparContatosSessao()
    {
        contatosSessao.Clear();
        contatosCarregados = false;
    }
}

public sealed class ChatSessao
{
    public long EmpresaId { get; set; }
    public long UsuarioId { get; set; }
    public string Usuario { get; set; } = "";
    public string SupabaseUserId { get; set; } = "";
    public DateTimeOffset ExpiraEm { get; set; }
}

public sealed class SolicitacaoAcesso
{
    public string AuthUid { get; set; } = "";
    public string Email { get; set; } = "";
    public string Username { get; set; } = "";
    public string Nome { get; set; } = "";
    public long EmpresaId { get; set; }
    public long? DispositivoId { get; set; }
    public string NomeDispositivo { get; set; } = "";
    public string Status { get; set; } = "PENDENTE";
    public DateTimeOffset CriadaEm { get; set; }
}

public sealed class CadastroPendente
{
    public long Id { get; set; }
    public string Nome { get; set; } = "";
    public string Email { get; set; } = "";
    public long EmpresaId { get; set; }
    public DateTimeOffset CriadaEm { get; set; }
    public long UsuarioIdSelecionado { get; set; }
}

public sealed class UsuarioEmpresaDisponivel
{
    public long Id { get; set; }
    public long EmpresaId { get; set; }
    public string Empresa { get; set; } = "";
    public string Nome { get; set; } = "";
    public string Funcao { get; set; } = "";
    public string Email { get; set; } = "";
    public bool Aprovado { get; set; }
    public bool Bloqueado { get; set; }
    public bool AparelhoPendente { get; set; }
    public string TipoNegocio { get; set; } = "sucata";
}

public sealed class AprovacaoUsuarioAparelho
{
    public long EmpresaId { get; set; }
    public string Empresa { get; set; } = "";
    public long? DispositivoId { get; set; }
    public long? UsuarioId { get; set; }
    public long? UsuarioPendenteId { get; set; }
    public string Usuario { get; set; } = "";
    public string Dispositivo { get; set; } = "";
    public string Tipo { get; set; } = "";
    public DateTimeOffset? CriadoEm { get; set; }
    public bool SemDispositivo { get; set; }
}

public sealed class UsuarioAuthGerenciavel
{
    public string AuthUid { get; set; } = "";
    public string Email { get; set; } = "";
    public string Nome { get; set; } = "";
    public long? EmpresaId { get; set; }
    public long? UsuarioId { get; set; }
    public bool Ativo { get; set; }
    public bool Master { get; set; }
    public DateTimeOffset CriadoEm { get; set; }
}

public sealed class ConviteUsuarioResponse
{
    public string Email { get; set; } = "";
    public string AuthUid { get; set; } = "";
    public bool Master { get; set; }
}

public sealed class UrlMidiaResponse { public string Url { get; set; } = ""; }
public sealed class AcaoVisualConversa { public string Tipo { get; set; } = "WINK"; public string Simbolo { get; set; } = "✨"; }

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
    public Guid ClienteMensagemId { get; set; }
    public long RemetenteId { get; set; }
    public string RemetenteNome { get; set; } = "";
    public long DestinatarioId { get; set; }
    public string Texto { get; set; } = "";
    public byte[]? Foto { get; set; }
    public string? FotoNome { get; set; }
    public string? FotoTipo { get; set; }
    public string? MidiaChave { get; set; }
    public string? MidiaNome { get; set; }
    public string? MidiaTipo { get; set; }
    public long? MidiaTamanhoBytes { get; set; }
    public string? FotoUrl { get; set; }
    public DateTime EnviadaEm { get; set; }
    public DateTime? LidaEm { get; set; }
    public string? StatusEnvio { get; set; }
}
