using APP_CELULAR.Data;
using APP_CELULAR.Models;

namespace APP_CELULAR;

public partial class MainPage : ContentPage
{
    private readonly AppDatabase _database;

    public MainPage()
    {
        InitializeComponent();

        _database = new AppDatabase();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await CarregarResumo();
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
        await DisplayAlert(
            "Teste",
            "Configurações ainda serão desenvolvidas.",
            "OK");
    }


    // ============================================================
    // USUÁRIOS
    // ============================================================

    private async void Usuarios_Clicked(
        object sender,
        EventArgs e)
    {
        await DisplayAlert(
            "Teste",
            "Usuários ainda serão desenvolvidos.",
            "OK");
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
            Application.Current!.Windows[0].Page =
                new LoginPage();
        }
    }
}