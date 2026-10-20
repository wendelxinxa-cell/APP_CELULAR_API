using APP_CELULAR.Data;
using APP_CELULAR.Models;

namespace APP_CELULAR;

public partial class ControleChequesPage : ContentPage
{
    private readonly AppDatabase _database;

    private List<ClienteChequeResumo> _clientes = new();

    public ControleChequesPage()
    {
        InitializeComponent();

        _database = new AppDatabase();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await CarregarDados();
    }

    private async Task CarregarDados()
    {
        try
        {
            var cheques =
                await _database.ObterChequesEmAberto();

            _clientes = cheques
                .GroupBy(x => new
                {
                    x.PessoaId,
                    x.ClienteNome
                })
                .Select(x =>
                    new ClienteChequeResumo
                    {
                        PessoaId =
                            x.Key.PessoaId,

                        ClienteNome =
                            x.Key.ClienteNome,

                        QuantidadeCheques =
                            x.Count(),

                        TotalAberto =
                            x.Sum(y =>
                                y.SaldoAtual)
                    })
                .OrderBy(x =>
                    x.ClienteNome)
                .ToList();

            ClientesCollection.ItemsSource =
                _clientes;
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Erro",
                ex.Message,
                "OK");
        }
    }

    private void PesquisaSearchBar_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        string texto =
            e.NewTextValue ?? "";

        ClientesCollection.ItemsSource =
            _clientes
            .Where(x =>
                x.ClienteNome
                .Contains(
                    texto,
                    StringComparison
                    .OrdinalIgnoreCase))
            .ToList();
    }

    private async void ClientesCollection_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        var cliente =
            e.CurrentSelection
            .FirstOrDefault()
            as ClienteChequeResumo;

        if (cliente == null)
            return;

        await Navigation.PushAsync(
            new ChequesClientePage(
                cliente.PessoaId,
                cliente.ClienteNome));

    }
}