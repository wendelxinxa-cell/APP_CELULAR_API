using APP_CELULAR.Data;
using APP_CELULAR.Models;

namespace APP_CELULAR;

public partial class ChequesClientePage : ContentPage
{
    private readonly AppDatabase _database;

    private readonly int _pessoaId;

    private List<Cheque> _cheques = new();

    public ChequesClientePage(
        int pessoaId,
        string clienteNome)
    {
        InitializeComponent();

        _database = new AppDatabase();

        _pessoaId = pessoaId;

        ClienteLabel.Text = clienteNome;

        Title = clienteNome;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await CarregarCheques();
    }

    private async void NovoCheque_Tapped(
    object sender,
    TappedEventArgs e)
    {
        try
        {
            var pessoas =
                await _database.ObterPessoas();

            if (pessoas.Count == 0)
            {
                await DisplayAlert(
                    "Clientes",
                    "Nenhum cliente cadastrado.",
                    "OK");

                return;
            }

            string clienteNome =
                await DisplayActionSheet(
                    "Cliente",
                    "Cancelar",
                    null,
                    pessoas
                        .OrderBy(x => x.Nome)
                        .Select(x => x.Nome)
                        .ToArray());

            if (string.IsNullOrWhiteSpace(clienteNome) ||
                clienteNome == "Cancelar")
            {
                return;
            }

            var cliente =
                pessoas.First(x =>
                    x.Nome == clienteNome);

            string numeroCheque =
                await DisplayPromptAsync(
                    "Cheque",
                    "Número do cheque");

            if (string.IsNullOrWhiteSpace(
                numeroCheque))
            {
                return;
            }

            string valorTexto =
                await DisplayPromptAsync(
                    "Valor",
                    "Valor do cheque",
                    keyboard: Keyboard.Numeric);

            if (!decimal.TryParse(
                valorTexto,
                out decimal valor))
            {
                await DisplayAlert(
                    "Erro",
                    "Valor inválido.",
                    "OK");

                return;
            }

            string vencimentoTexto =
                await DisplayPromptAsync(
                    "Vencimento",
                    "Data de vencimento (dd/MM/yyyy)");

            if (!DateTime.TryParse(
                vencimentoTexto,
                out DateTime vencimento))
            {
                await DisplayAlert(
                    "Erro",
                    "Data inválida.",
                    "OK");

                return;
            }

            var cheque = new Cheque
            {
                PessoaId = cliente.Id,
                ClienteNome = cliente.Nome,
                NumeroCheque = numeroCheque,
                DataEmissao = DateTime.Now,
                DataVencimento = vencimento,
                ValorOriginal = valor,
                SaldoAtual = valor,
                Status = "ABERTO"
            };

            await _database.SalvarCheque(
                cheque);

            await DisplayAlert(
                "Sucesso",
                "Cheque cadastrado com sucesso.",
                "OK");

            await CarregarCheques();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Erro",
                ex.Message,
                "OK");
        }
    }

    private async Task CarregarCheques()
    {
        _cheques =
            await _database.ObterChequesCliente(
                _pessoaId);

        ChequesCollection.ItemsSource =
            _cheques;

        ResumoLabel.Text =
            $"{_cheques.Count} cheque(s) | " +
            $"R$ {_cheques.Sum(x => x.SaldoAtual):N2}";
    }

    private async void ChequesCollection_SelectionChanged(
     object sender,
     SelectionChangedEventArgs e)
    {
        var cheque =
            e.CurrentSelection
            .FirstOrDefault() as Cheque;

        if (cheque == null)
            return;

        ChequesCollection.SelectedItem = null;

        string opcao =
            await DisplayActionSheet(
                $"Cheque {cheque.NumeroCheque}",
                "Cancelar",
                null,
                "Detalhes",
                "Registrar Baixa",
                "Prorrogar",
                "Histórico");

        switch (opcao)
        {
            case "Detalhes":
                await MostrarDetalhes(cheque);
                break;

            case "Registrar Baixa":
                await RegistrarBaixa(cheque);
                break;

            case "Prorrogar":
                await ProrrogarCheque(cheque);
                break;

            case "Histórico":
                await MostrarHistorico(cheque);
                break;
        }
    }

    private async Task MostrarDetalhes(
    Cheque cheque)
    {
        await DisplayAlert(
            "Detalhes",

            $"Cliente: {cheque.ClienteNome}\n\n" +

            $"Cheque: {cheque.NumeroCheque}\n\n" +

            $"Valor Original: R$ {cheque.ValorOriginal:N2}\n\n" +

            $"Saldo Atual: R$ {cheque.SaldoAtual:N2}\n\n" +

            $"Vencimento: {cheque.DataVencimento:dd/MM/yyyy}\n\n" +

            $"Status: {cheque.Status}",

            "OK");
    }

    private async Task RegistrarBaixa(
    Cheque cheque)
    {
        string valorTexto =
            await DisplayPromptAsync(
                "Baixa",
                "Valor da baixa:",
                keyboard: Keyboard.Numeric);

        if (string.IsNullOrWhiteSpace(
            valorTexto))
        {
            return;
        }

        if (!decimal.TryParse(
            valorTexto,
            out decimal valor))
        {
            await DisplayAlert(
                "Erro",
                "Valor inválido.",
                "OK");

            return;
        }

        string observacao =
            await DisplayPromptAsync(
                "Observação",
                "Observação da baixa:");

        await _database.RegistrarBaixaCheque(
            cheque.Id,
            valor,
            observacao ?? "");

        await DisplayAlert(
            "Sucesso",
            "Baixa registrada.",
            "OK");

        await CarregarCheques();
    }

    private async Task ProrrogarCheque(
    Cheque cheque)
    {
        string dataTexto =
            await DisplayPromptAsync(
                "Prorrogação",
                "Nova data (dd/MM/yyyy)");

        if (string.IsNullOrWhiteSpace(
            dataTexto))
        {
            return;
        }

        if (!DateTime.TryParse(
            dataTexto,
            out DateTime novaData))
        {
            await DisplayAlert(
                "Erro",
                "Data inválida.",
                "OK");

            return;
        }

        string observacao =
            await DisplayPromptAsync(
                "Motivo",
                "Motivo da prorrogação:");

        await _database.SalvarProrrogacao(
            new ProrrogacaoCheque
            {
                ChequeId = cheque.Id,
                VencimentoAnterior =
                    cheque.DataVencimento,

                NovoVencimento =
                    novaData,

                Observacao =
                    observacao ?? ""
            });

        await _database.AtualizarVencimentoCheque(
            cheque.Id,
            novaData);

        await DisplayAlert(
            "Sucesso",
            "Cheque prorrogado.",
            "OK");

        await CarregarCheques();
    }

    private async Task MostrarHistorico(
    Cheque cheque)
    {
        var historico =
            await _database
            .ObterHistoricoCheque(
                cheque.Id);

        if (historico.Count == 0)
        {
            await DisplayAlert(
                "Histórico",
                "Nenhum registro.",
                "OK");

            return;
        }

        string texto = "";

        foreach (var item in historico)
        {
            texto +=
                $"{item.DataMovimento:dd/MM/yyyy HH:mm}\n" +

                $"{item.Tipo}\n" +

                $"{item.Descricao}\n\n";
        }

        await DisplayAlert(
            "Histórico",
            texto,
            "OK");
    }
}