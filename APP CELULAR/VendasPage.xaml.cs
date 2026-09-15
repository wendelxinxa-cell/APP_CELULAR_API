using APP_CELULAR.Data;
using APP_CELULAR.Models;

namespace APP_CELULAR;

public partial class VendasPage : ContentPage
{
    private readonly AppDatabase _database;

    private List<Venda> _vendas = new();

    private string _filtroStatus = "TODOS";

    public VendasPage()
    {
        InitializeComponent();

        _database = new AppDatabase();

        DataFiltro.Date = DateTime.Today;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        DataFiltro.Date = DataFiltro.Date == default
            ? DateTime.Today
            : DataFiltro.Date;

        await CarregarVendas();
    }


    // =========================
    // CARREGAR VENDAS
    // =========================

    private async Task CarregarVendas()
    {
        try
        {
            _vendas = await _database.ObterVendas();

            AtualizarLista();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Erro",
                $"Não foi possível carregar os pedidos.\n\n{ex.Message}",
                "OK");
        }
    }


    // =========================
    // FILTRAR
    // =========================

    private void AtualizarLista()
    {
        DateTime dataSelecionada = DataFiltro.Date.Date;

        IEnumerable<Venda> vendas = _vendas
            .Where(x => x.DataVenda.Date == dataSelecionada);

        if (_filtroStatus != "TODOS")
        {
            vendas = vendas.Where(x =>
                ObterStatus(x.Status) == _filtroStatus);
        }

        var lista = vendas
            .OrderByDescending(x => x.DataVenda)
            .Select(x => new VendaListaItem(x))
            .ToList();

        PedidosCollection.ItemsSource = lista;

        int concluidos = _vendas.Count(x =>
            x.DataVenda.Date == dataSelecionada &&
            ObterStatus(x.Status) == "CONCLUIDO");

        int pendentes = _vendas.Count(x =>
            x.DataVenda.Date == dataSelecionada &&
            ObterStatus(x.Status) == "PENDENTE");

        int cancelados = _vendas.Count(x =>
            x.DataVenda.Date == dataSelecionada &&
            ObterStatus(x.Status) == "CANCELADO");

        BtnConcluidos.Text = $"✓  Concluídos  {concluidos}";
        BtnPendentes.Text = $"◷  Pendentes  {pendentes}";
        BtnCancelados.Text = $"✕  Cancelados  {cancelados}";

        FiltroLabel.Text =
            _filtroStatus == "TODOS"
                ? $"Pedidos de {dataSelecionada:dd/MM/yyyy}"
                : $"{NomeStatus(_filtroStatus)} • {dataSelecionada:dd/MM/yyyy}";
    }


    // =========================
    // STATUS
    // =========================

    private string ObterStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return "PENDENTE";

        status = status.Trim().ToUpperInvariant();

        return status switch
        {
            "FINALIZADA" => "CONCLUIDO",
            "FINALIZADO" => "CONCLUIDO",
            "CONCLUIDA" => "CONCLUIDO",
            "CONCLUIDO" => "CONCLUIDO",

            "CANCELADA" => "CANCELADO",
            "CANCELADO" => "CANCELADO",

            "ABERTA" => "PENDENTE",
            "ABERTO" => "PENDENTE",
            "PENDENTE" => "PENDENTE",

            _ => "PENDENTE"
        };
    }

    private string NomeStatus(string status)
    {
        return status switch
        {
            "CONCLUIDO" => "Pedidos concluídos",
            "PENDENTE" => "Pedidos pendentes",
            "CANCELADO" => "Pedidos cancelados",
            _ => "Todos os pedidos"
        };
    }


    // =========================
    // DATA
    // =========================

    private void DataFiltro_DateSelected(
        object sender,
        DateChangedEventArgs e)
    {
        _filtroStatus = "TODOS";

        AtualizarLista();
    }


    // =========================
    // FILTROS
    // =========================

    private void FiltroConcluidos_Clicked(
        object sender,
        EventArgs e)
    {
        _filtroStatus =
            _filtroStatus == "CONCLUIDO"
                ? "TODOS"
                : "CONCLUIDO";

        AtualizarLista();
    }


    private void FiltroPendentes_Clicked(
        object sender,
        EventArgs e)
    {
        _filtroStatus =
            _filtroStatus == "PENDENTE"
                ? "TODOS"
                : "PENDENTE";

        AtualizarLista();
    }


    private void FiltroCancelados_Clicked(
        object sender,
        EventArgs e)
    {
        _filtroStatus =
            _filtroStatus == "CANCELADO"
                ? "TODOS"
                : "CANCELADO";

        AtualizarLista();
    }


    // =========================
    // NOVO PEDIDO
    // =========================

    private async void NovoPedido_Tapped(
        object sender,
        TappedEventArgs e)
    {
        await Shell.Current.GoToAsync(
            nameof(NovaVendaPage));
    }


    // =========================
    // ABRIR PEDIDO
    // =========================

    private async void PedidosCollection_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault()
            is not VendaListaItem item)
        {
            return;
        }

        PedidosCollection.SelectedItem = null;

        await Shell.Current.GoToAsync(
            $"{nameof(DetalhesVendaPage)}?vendaId={item.Id}");
    }


    // =========================
    // VOLTAR
    // =========================

    private async void Voltar_Clicked(
        object sender,
        EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}


// ======================================================
// ITEM DA LISTA
// ======================================================

public class VendaListaItem
{
    public int Id { get; }

    public string NumeroPedido { get; }

    public string Cliente { get; }

    public string DataHora { get; }

    public string Valor { get; }

    public string StatusTexto { get; }

    public Color StatusFundo { get; }


    public VendaListaItem(Venda venda)
    {
        Id = venda.Id;

        NumeroPedido =
            $"#{venda.Id:0000}";

        Cliente =
            string.IsNullOrWhiteSpace(venda.PessoaNome)
                ? "Cliente não informado"
                : venda.PessoaNome;

        DataHora =
            venda.DataVenda.ToString("dd/MM/yyyy • HH:mm");

        Valor =
            $"R$ {venda.ValorTotal:N2}";

        string status =
            venda.Status?.Trim().ToUpperInvariant() ?? "";

        status = status switch
        {
            "FINALIZADA" => "CONCLUIDO",
            "FINALIZADO" => "CONCLUIDO",
            "CONCLUIDA" => "CONCLUIDO",
            "CONCLUIDO" => "CONCLUIDO",

            "CANCELADA" => "CANCELADO",
            "CANCELADO" => "CANCELADO",

            "ABERTA" => "PENDENTE",
            "ABERTO" => "PENDENTE",
            "PENDENTE" => "PENDENTE",

            _ => "PENDENTE"
        };

        switch (status)
        {
            case "CONCLUIDO":
                StatusTexto = "✓ Concluído";
                StatusFundo = Color.FromArgb("#19A957");
                break;

            case "CANCELADO":
                StatusTexto = "✕ Cancelado";
                StatusFundo = Color.FromArgb("#EF3340");
                break;

            default:
                StatusTexto = "◷ Pendente";
                StatusFundo = Color.FromArgb("#1689E8");
                break;
        }
    }
}