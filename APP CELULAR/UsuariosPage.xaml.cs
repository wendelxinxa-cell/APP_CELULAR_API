using APP_CELULAR.Data;
using APP_CELULAR.Models;
using APP_CELULAR.Services;

namespace APP_CELULAR;

public partial class UsuariosPage : ContentPage
{
    private readonly AppDatabase _database;
    private readonly MasterEmpresasApiService _apiMaster = new();
    private string? _tokenZeus;

    public UsuariosPage()
    {
        InitializeComponent();

        _database = new AppDatabase();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await CarregarUsuarios();
    }


    private async Task CarregarUsuarios()
    {
        try
        {
            var usuarios = await _database.ObterUsuarios();
            if (SessaoUsuario.EhDeus)
            {
                var estados = await AtualizarStatusPeloServidor(usuarios);
                var linhasRemotas = estados.Select(x => new UsuarioLinha(x)).ToList();
                UsuariosCollection.ItemsSource = linhasRemotas;
                int empresas = linhasRemotas.Select(x => x.EmpresaId).Distinct().Count();
                SubtituloLabel.Text = $"Toque em um usuário para alterar · {linhasRemotas.Count} em {empresas} empresas";
            }
            else
            {
                foreach (var usuario in usuarios)
                    usuario.StatusAcesso = usuario.EhMaster || usuario.Nome.Equals("Zeus", StringComparison.OrdinalIgnoreCase)
                        ? "LIBERADO"
                        : usuario.Excluido ? "BLOQUEADO" : "PENDENTE";

                UsuariosCollection.ItemsSource = usuarios.Select(x => new UsuarioLinha(x)).ToList();
                SubtituloLabel.Text = usuarios.Count == 1
                    ? "1 usuário cadastrado"
                    : $"{usuarios.Count} usuários cadastrados";
            }

            BtnAprovacoes.IsVisible = SessaoUsuario.EhDeus;
            BtnNovoUsuario.IsVisible =
                SessaoUsuario.EhAdministrador && !SessaoUsuario.EhDeus;
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Erro",
                $"N�o foi poss�vel carregar os usu�rios.\n\n{ex.Message}",
                "OK");
        }
    }

    private async Task<List<StatusUsuarioAcesso>> AtualizarStatusPeloServidor(List<Usuario> usuarios)
    {
        _tokenZeus = SessaoUsuario.TokenMaster ?? throw new InvalidOperationException("Entre novamente como Administrador Master.");
        foreach (var usuario in usuarios.Where(u => !u.EhMaster && !u.Nome.Equals("Zeus", StringComparison.OrdinalIgnoreCase)))
        {
            if (usuario.IdServidor is null || usuario.StatusSincronizacao != "SINCRONIZADO" || usuario.Excluido)
                usuario.IdServidor = await _apiMaster.RegistrarUsuario(_tokenZeus, usuario);
        }

        var estados = await _apiMaster.ObterStatusUsuarios(_tokenZeus);
        foreach (var usuario in usuarios)
        {
            var remoto = estados.FirstOrDefault(x => x.EmpresaId == usuario.EmpresaId && x.IdLocal == usuario.Id);
            if (remoto is null) continue;
            usuario.IdServidor = remoto.Id;
            usuario.StatusAcesso = remoto.Bloqueado ? "BLOQUEADO" : remoto.Aprovado ? "LIBERADO" : "PENDENTE";
            await _database.MarcarUsuarioSincronizado(usuario.Id, remoto.Id, remoto.EmpresaId);
        }

        return estados;
    }


    private async void Usuario_Tocado(object sender, TappedEventArgs e)
    {
        var linha = (sender as BindableObject)?.BindingContext as UsuarioLinha;

        if (linha is null) return;

        if (SessaoUsuario.EhDeus)
        {
            if (linha.IdServidor <= 0 || linha.EmpresaId <= 0)
            {
                await DisplayAlert("Alterar usuário", "Não foi possível identificar este usuário no servidor. Atualize a lista e tente novamente.", "OK");
                return;
            }
            await Navigation.PushAsync(new EditarUsuarioZeusPage(linha.IdServidor, linha.EmpresaId, linha.Nome, linha.TipoNegocio, linha.StatusAcesso, linha.Bloqueado));
            await CarregarUsuarios();
            return;
        }

        if (linha.UsuarioLocal is null) return;

        await Shell.Current.GoToAsync(
            $"{nameof(DetalhaUsuariosPage)}?usuarioId={linha.UsuarioLocal.Id}");
    }

    private async Task AcaoZeusUsuario(UsuarioLinha linha)
    {
        if (!SessaoUsuario.EhDeus || linha.IdServidor <= 0 || linha.EmpresaId <= 0) return;

        string acao = linha.Bloqueado ? "Desbloquear usuário" : "Bloquear usuário";
        string escolha = await DisplayActionSheet(
            $"{linha.Nome}\n{linha.EmpresaExibida}",
            "Cancelar",
            null,
            "Alterar tipo de negócio",
            acao);
        if (escolha == "Alterar tipo de negócio")
        {
            await Navigation.PushAsync(new EditarUsuarioZeusPage(linha.IdServidor, linha.EmpresaId, linha.Nome, linha.TipoNegocio, linha.StatusAcesso, linha.Bloqueado));
            await CarregarUsuarios();
            return;
        }
        if (escolha != acao) return;

        bool confirmar = await DisplayAlert(
            acao,
            linha.Bloqueado
                ? $"Deseja desbloquear {linha.Nome} em {linha.EmpresaExibida}?"
                : $"Deseja bloquear {linha.Nome} em {linha.EmpresaExibida}?",
            acao,
            "Cancelar");
        if (!confirmar) return;

        try
        {
            _tokenZeus = SessaoUsuario.TokenMaster ?? throw new InvalidOperationException("Entre novamente como Administrador Master.");
            await _apiMaster.DefinirBloqueioUsuario(_tokenZeus, linha.EmpresaId, linha.IdServidor, !linha.Bloqueado);
            await CarregarUsuarios();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Bloqueio de usuário", ex.Message, "OK");
        }
    }


    private async void NovoUsuario_Clicked(
        object sender,
        EventArgs e)
    {
        if (!SessaoUsuario.EhAdministrador || SessaoUsuario.EhDeus)
        {
            await DisplayAlert(
                "Acesso negado",
                "Somente administradores podem cadastrar usu�rios.",
                "OK");

            return;
        }

        await Shell.Current.GoToAsync(
            nameof(NovoUsuarioPage));
    }


    private async void Aprovacoes_Clicked(object sender, EventArgs e)
    {
        if (!SessaoUsuario.EhDeus)
        {
            await DisplayAlert("Acesso negado", "Somente Zeus pode abrir a tela de aprovações.", "OK");
            return;
        }
        await Navigation.PushAsync(new AprovacoesAcessoPage());
    }

    private async void Voltar_Clicked(
        object sender,
        EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }


    private async void RefreshUsuarios_Refreshing(
        object sender,
        EventArgs e)
    {
        await CarregarUsuarios();

        RefreshUsuarios.IsRefreshing = false;
    }

    private sealed class UsuarioLinha
    {
        public Usuario? UsuarioLocal { get; }
        public long IdServidor { get; }
        public long EmpresaId { get; }
        public string Nome { get; }
        public string Funcao { get; }
        public string TipoNegocio { get; }
        public string Email { get; }
        public string StatusAcesso { get; }
        public string CorStatusAcesso => StatusAcesso switch
        {
            "LIBERADO" => "#238636",
            "BLOQUEADO" => "#D1242F",
            _ => "#F28C28"
        };
        public bool EhMaster { get; }
        public bool Bloqueado { get; }
        public bool ExibirEmpresa { get; }
        public string EmpresaExibida { get; }

        public UsuarioLinha(Usuario usuario)
        {
            UsuarioLocal = usuario;
            IdServidor = usuario.IdServidor ?? 0;
            EmpresaId = usuario.EmpresaId;
            Nome = usuario.Nome;
            Funcao = usuario.Funcao;
            TipoNegocio = usuario.TipoNegocio;
            StatusAcesso = usuario.StatusAcesso;
            EhMaster = usuario.EhMaster;
            Bloqueado = usuario.Excluido;
            EmpresaExibida = "";
            Email = "";
        }

        public UsuarioLinha(StatusUsuarioAcesso usuario)
        {
            IdServidor = usuario.Id;
            EmpresaId = usuario.EmpresaId;
            Nome = usuario.Nome;
            Funcao = usuario.Funcao;
            TipoNegocio = usuario.TipoNegocio == "chat" ? "chat" : "sucata";
            Bloqueado = usuario.Bloqueado;
            StatusAcesso = usuario.Bloqueado ? "BLOQUEADO"
                : !usuario.Aprovado && usuario.AparelhoPendente ? "PENDENTE: USUÁRIO E APARELHO"
                : !usuario.Aprovado ? "PENDENTE: USUÁRIO"
                : usuario.AparelhoPendente ? "PENDENTE: APARELHO"
                : "LIBERADO";
            ExibirEmpresa = true;
            EmpresaExibida = string.IsNullOrWhiteSpace(usuario.Empresa)
                ? $"Empresa não identificada · código {usuario.EmpresaId}"
                : $"Empresa: {usuario.Empresa} · código {usuario.EmpresaId}";
            Email = usuario.Email;
        }
    }
}
