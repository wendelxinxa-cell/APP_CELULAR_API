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

    private async void Pessoas_Clicked(object sender, EventArgs e)
    {
        string tipo = await DisplayActionSheet(
            "Abrir cadastro",
            "Cancelar",
            null,
            "Clientes",
            "Fornecedores",
            "Todos");

        if (tipo == "Cancelar" || string.IsNullOrWhiteSpace(tipo))
            return;

        string parametro = tipo switch
        {
            "Clientes" => "CLI",
            "Fornecedores" => "FOR",
            _ => "TODOS"
        };

        await Shell.Current.GoToAsync($"{nameof(PessoasPage)}?tipo={parametro}");
    }


    private async void Fornecedores_Clicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(
            $"{nameof(PessoasPage)}?tipo=FOR");
    }

    private async void Produtos_Clicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(MateriaisPage));
    }

    private async void Vendas_Clicked(
    object sender,
    EventArgs e)
    {
        await Shell.Current.GoToAsync(
            nameof(VendasPage));
    }

    private async void Compras_Clicked(
    object sender,
    EventArgs e)
    {
        await Shell.Current.GoToAsync(
            nameof(ComprasPage));
    }

    private async void Financeiro_Clicked(object sender, EventArgs e)
    {
        await DisplayAlert(
            "Teste",
            "Financeiro ainda será desenvolvido.",
            "OK");
    }


    private async void Relatorios_Clicked(object sender, EventArgs e)
    {
        await DisplayAlert(
            "Teste",
            "Relatórios ainda serão desenvolvidos.",
            "OK");
    }


    private async void Configuracoes_Clicked(object sender, EventArgs e)
    {
        await DisplayAlert(
            "Teste",
            "Configurações ainda serão desenvolvidas.",
            "OK");
    }


    private async void Usuarios_Clicked(object sender, EventArgs e)
    {
        await DisplayAlert(
            "Teste",
            "Usuários ainda serão desenvolvidos.",
            "OK");
    }


    private async void Outros_Clicked(object sender, EventArgs e)
    {
        await DisplayAlert(
            "Teste",
            "Outras funções ainda serão desenvolvidas.",
            "OK");
    }


    private async void Sair_Clicked(object sender, EventArgs e)
    {
        bool sair = await DisplayAlert(
            "Sair",
            "Deseja realmente sair do sistema?",
            "Sim",
            "Não");

        if (sair)
        {
            Application.Current!.Windows[0].Page = new LoginPage();
        }
    }
}