using APP_CELULAR.Data;
using APP_CELULAR.Models;

namespace APP_CELULAR;

public partial class MainPage : ContentPage
{
    private readonly AppDatabase _database;
    private readonly APP_CELULAR.Services.ApiService _api = new();
    private readonly APP_CELULAR.Services.MasterEmpresasApiService _apiMaster = new();
    private readonly SemaphoreSlim _travaNotificacao = new(1, 1);
    private CancellationTokenSource? _notificacoesCts;
    private string? _tokenZeus;
    private int _mensagensNaoLidas;

    public MainPage()
    {
        InitializeComponent();

        _database = new AppDatabase();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        UsuarioLogadoLabel.Text = "Ol\u00E1, " + (SessaoUsuario.UsuarioAtual?.Nome ?? "bem-vindo") + "!";
        var modoChat = PreferenciasNegocioApp.Atual == "chat";
        PainelSubtitleLabel.Text = modoChat ? "Modo Chat" : "Painel principal";
        ResumoGrid.IsVisible = !modoChat;
        ModulosHeaderGrid.IsVisible = !modoChat;
        AdministracaoCard.IsVisible = SessaoUsuario.EhDeus || (!modoChat && SessaoUsuario.EhAdministrador);
        AdministracaoTituloLabel.Text = "Usuários";
        AdministracaoSubtituloLabel.Text = "Cadastro e permissões";
        EmpresasCard.IsVisible = SessaoUsuario.EhMaster || SessaoUsuario.EhDeus;
        ConversasCard.IsVisible = true;
        ConfiguracoesCard.IsVisible = true;
        PessoasCard.IsVisible = !modoChat;
        ProdutosCard.IsVisible = !modoChat;
        VendasCard.IsVisible = !modoChat;
        ComprasCard.IsVisible = !modoChat;
        FinanceiroCard.IsVisible = !modoChat;
        OutrosCard.IsVisible = !modoChat;
        EmpresasCard.IsVisible = !modoChat && (SessaoUsuario.EhMaster || SessaoUsuario.EhDeus);

        await CarregarResumo();

        _notificacoesCts?.Cancel();
        _notificacoesCts = new CancellationTokenSource();
        _ = AtualizarIndicadorNotificacao();
        _ = AcompanharNotificacoes(_notificacoesCts.Token);
    }

    protected override void OnDisappearing()
    {
        _notificacoesCts?.Cancel();
        base.OnDisappearing();
    }

    private async Task AcompanharNotificacoes(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
                await AtualizarIndicadorNotificacao();
        }
        catch (OperationCanceledException)
        {
            // A tela principal saiu de foco.
        }
    }

    private async Task AtualizarIndicadorNotificacao()
    {
        var usuario = SessaoUsuario.UsuarioAtual;
        if (usuario is null || Connectivity.Current.NetworkAccess != NetworkAccess.Internet ||
            !await _travaNotificacao.WaitAsync(0))
            return;

        try
        {
            int total;
            if (SessaoUsuario.EhDeus)
            {
                _tokenZeus = SessaoUsuario.TokenMaster ?? throw new InvalidOperationException("Entre novamente como Administrador Master.");
                var chats = await _apiMaster.ObterConversasChat(_tokenZeus);
                total = chats.Sum(x => Math.Max(0, x.NaoLidas));
            }
            else
            {
                if (SessaoUsuario.EhMaster || string.IsNullOrWhiteSpace(SessaoUsuario.ApiToken) || usuario.IdServidor is null)
                    return;
                var contatos = await _api.ObterContatos(new APP_CELULAR.Services.IdentidadeConversa
                {
                    EmpresaId = usuario.EmpresaId,
                    UsuarioId = usuario.IdServidor.Value
                });
                total = contatos.Sum(x => Math.Max(0, x.NaoLidas));
            }
            MainThread.BeginInvokeOnMainThread(() => DefinirMensagensNaoLidas(total));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Não foi possível atualizar as notificações do chat: {ex.Message}");
        }
        finally
        {
            _travaNotificacao.Release();
        }
    }

    private void DefinirMensagensNaoLidas(int total)
    {
        _mensagensNaoLidas = total;
        NotificacaoContagemLabel.Text = total > 9 ? "9+" : Math.Max(0, total).ToString();
        NotificacaoBadge.IsVisible = total > 0 && !SessaoUsuario.EhDeus;
    }

    private async void Notificacoes_Clicked(object sender, EventArgs e)
    {
        if (_mensagensNaoLidas <= 0)
        {
            await DisplayAlert("Notificações", "Nenhuma mensagem nova no chat.", "OK");
            return;
        }

        bool abrir = await DisplayAlert(
            "Notificação",
            "Nova mensagem no chat.",
            "Abrir chat",
            "Depois");
        if (abrir)
            await AbrirChat();
    }

    // ============================================================
    // RESUMO DO PAINEL
    // ============================================================

    private async Task CarregarResumo()
    {
        try
        {
            // ----------------------------------------------------
            // CLIENTES
            // ----------------------------------------------------

            var clientes =
                await _database.ObterClientes();

            ClientesResumoLabel.Text =
                clientes.Count.ToString();


            // ----------------------------------------------------
            // VENDAS DE HOJE
            // ----------------------------------------------------

            var vendas =
                await _database.ObterVendas();

            DateTime hoje = DateTime.Today;

            decimal totalVendasHoje =
                vendas
                    .Where(x => x.DataVenda.Date == hoje)
                    .Sum(x => x.ValorTotal);

            VendasResumoLabel.Text =
                totalVendasHoje.ToString("C", new System.Globalization.CultureInfo("pt-BR"));
        }
        catch (Exception ex)
        {
            ClientesResumoLabel.Text = "--";
            VendasResumoLabel.Text = "R$ --";

            System.Diagnostics.Debug.WriteLine(
                $"Erro ao carregar resumo da MainPage: {ex.Message}");
        }
    }


    // ============================================================
    // PESSOAS
    // ============================================================

    private async void Pessoas_Clicked(
        object sender,
        EventArgs e)
    {
        string tipo = await DisplayActionSheet(
            "Abrir cadastro",
            "Cancelar",
            null,
            "Clientes",
            "Fornecedores",
            "Todos");

        if (tipo == "Cancelar" ||
            string.IsNullOrWhiteSpace(tipo))
            return;

        string parametro = tipo switch
        {
            "Clientes" => "CLI",
            "Fornecedores" => "FOR",
            _ => "TODOS"
        };

        await Shell.Current.GoToAsync(
            $"{nameof(PessoasPage)}?tipo={parametro}");
    }


    // ============================================================
    // FORNECEDORES
    // ============================================================

    private async void Fornecedores_Clicked(
        object sender,
        EventArgs e)
    {
        await Shell.Current.GoToAsync(
            $"{nameof(PessoasPage)}?tipo=FOR");
    }


    // ============================================================
    // PRODUTOS
    // ============================================================

    private async void Produtos_Clicked(
        object sender,
        EventArgs e)
    {
        await Shell.Current.GoToAsync(
            nameof(MateriaisPage));
    }


    // ============================================================
    // VENDAS
    // ============================================================

    private async void Vendas_Clicked(
        object sender,
        EventArgs e)
    {
        await Shell.Current.GoToAsync(
            nameof(VendasPage));
    }


    // ============================================================
    // COMPRAS
    // ============================================================

    private async void Compras_Clicked(
        object sender,
        EventArgs e)
    {
        await Shell.Current.GoToAsync(
            nameof(ComprasPage));
    }


    // ============================================================
    // FINANCEIRO
    // ============================================================

    private async void Financeiro_Clicked(
        object sender,
        EventArgs e)
    {
        await DisplayAlert(
            "Teste",
            "Financeiro ainda será desenvolvido.",
            "OK");
    }


    // ============================================================
    // RELATÓRIOS
    // ============================================================

    private async void Relatorios_Clicked(
        object sender,
        EventArgs e)
    {
        await DisplayAlert(
            "Teste",
            "Relatórios ainda serão desenvolvidos.",
            "OK");
    }


    // ============================================================
    // CONFIGURAÇÕES
    // ============================================================

    private async void Configuracoes_Clicked(
        object sender,
        EventArgs e)
    {
        await Navigation.PushAsync(new ConfiguracoesPage());
    }


    // ============================================================
    // USUÁRIOS
    // ============================================================

    private async void Usuarios_Clicked(
        object sender,
        EventArgs e)
    {
        if (SessaoUsuario.EhAdministrador)
        {
            await Navigation.PushAsync(new UsuariosPage());
            return;
        }

        await DisplayAlert("Acesso negado", "Somente administradores podem gerenciar usuários.", "OK");
    }

    private async void Empresas_Clicked(object sender, EventArgs e)
    {
        if (!SessaoUsuario.EhMaster && !SessaoUsuario.EhDeus)
        {
            await DisplayAlert("Acesso negado", "Somente o Administrador Master pode cadastrar empresas.", "OK");
            return;
        }

        await Navigation.PushAsync(new EmpresasPage());
    }

    private async void Chat_Clicked(object sender, EventArgs e)
        => await AbrirChat();

    private async Task AbrirChat()
    {
        if (SessaoUsuario.EhDeus)
        {
            await Navigation.PushAsync(new ChatZeusPage());
            return;
        }

        var usuario = SessaoUsuario.UsuarioAtual;
        if (usuario is null)
        {
            await DisplayAlert("Chat", "Entre novamente no aplicativo para abrir o chat.", "OK");
            return;
        }

        if (string.IsNullOrWhiteSpace(SessaoUsuario.ApiToken))
        {
            try
            {
                var resposta = await new APP_CELULAR.Services.ApiService().Autenticar(new APP_CELULAR.Services.LoginApiRequest
                {
                    EmpresaId = usuario.EmpresaId,
                    Nome = usuario.Nome,
                    SenhaHash = usuario.SenhaHash
                });
                usuario.EmpresaId = resposta.EmpresaId;
                usuario.IdServidor = resposta.UsuarioId;
                SessaoUsuario.DispositivoId = resposta.DispositivoId;
                SessaoUsuario.ApiToken = resposta.Token;
                SessaoUsuario.ApiTokenExpiraEm = resposta.ExpiraEm;
                await _database.MarcarUsuarioSincronizado(usuario.Id, resposta.UsuarioId, resposta.EmpresaId);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Acesso ao chat", ex.Message, "OK");
                return;
            }
        }

        await Navigation.PushAsync(new ConversasPage());
    }


    // ============================================================
    // OUTROS
    // ============================================================

    private async void Outros_Clicked(
        object sender,
        EventArgs e)
    {
        await DisplayAlert(
            "Teste",
            "Outras funções ainda serão desenvolvidas.",
            "OK");
    }


    // ============================================================
    // SAIR
    // ============================================================

    private async void Sair_Clicked(
        object sender,
        EventArgs e)
    {
        bool sair = await DisplayAlert(
            "Sair",
            "Deseja realmente sair do sistema?",
            "Sim",
            "Não");

        if (sair)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(SessaoUsuario.ApiToken))
                    await new APP_CELULAR.Services.ApiService().EncerrarSessaoApi();
            }
            catch
            {
                // O encerramento local continua mesmo sem rede.
            }
            SessaoUsuario.Encerrar();
            Application.Current!.Windows[0].Page =
                new LoginPage();
        }
    }
}
