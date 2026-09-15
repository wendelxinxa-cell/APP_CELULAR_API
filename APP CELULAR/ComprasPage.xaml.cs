using APP_CELULAR.Data;
using APP_CELULAR.Models;
using System.Collections.ObjectModel;
using System.Globalization;

namespace APP_CELULAR;

public partial class ComprasPage : ContentPage
{
    private readonly AppDatabase _database;

    private readonly ObservableCollection<CompraListaItem> _compras = new();

    private DateTime _dataSelecionada = DateTime.Today;

    private string _filtroAtual = "FINALIZADA";

    public ComprasPage()
    {
        InitializeComponent();

        _database = new AppDatabase();

        ComprasCollection.ItemsSource = _compras;

        DataPicker.Date = DateTime.Today;

        AtualizarTextoData();
        AtualizarBotoesFiltro();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await CarregarCompras();
    }

    // ============================================================
    // CARREGAR COMPRAS
    // ============================================================

    private async Task CarregarCompras()
    {
        try
        {
            _compras.Clear();

            var todasCompras = await _database.ObterCompras();

            var comprasData = todasCompras
                .Where(x => x.DataCompra.Date == _dataSelecionada.Date)
                .Where(x => StatusCorresponde(x.Status))
                .OrderByDescending(x => x.DataCompra)
                .ToList();

            foreach (var compra in comprasData)
            {
                var itens = await _database.ObterItensCompra(compra.Id);

                _compras.Add(new CompraListaItem
                {
                    Compra = compra,
                    QuantidadeItens = itens.Count
                });
            }

            MensagemVaziaLabel.Text =
                $"Não existem compras para {DataSelecionadaLabel.Text.ToLower()}.";
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Erro",
                $"Não foi possível carregar as compras.\n\n{ex.Message}",
                "OK");
        }
    }

    // ============================================================
    // FILTRO DE STATUS
    // ============================================================

    private bool StatusCorresponde(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return false;

        status = status.Trim().ToUpperInvariant();

        return _filtroAtual switch
        {
            "FINALIZADA" =>
                status == "FINALIZADA" ||
                status == "FINALIZADO" ||
                status == "CONCLUIDA" ||
                status == "CONCLUÍDA" ||
                status == "CONCLUIDO" ||
                status == "CONCLUÍDO",

            "PENDENTE" =>
                status == "ABERTA" ||
                status == "PENDENTE" ||
                status == "PENDENTES",

            "CANCELADA" =>
                status == "CANCELADA" ||
                status == "CANCELADO",

            _ => true
        };
    }

    // ============================================================
    // DATA
    // ============================================================

    private async void DataPicker_DateSelected(
        object? sender,
        DateChangedEventArgs e)
    {
        _dataSelecionada = e.NewDate;

        AtualizarTextoData();

        await CarregarCompras();
    }

    private void AtualizarTextoData()
    {
        if (_dataSelecionada.Date == DateTime.Today)
        {
            DataSelecionadaLabel.Text = "Hoje";
        }
        else
        {
            DataSelecionadaLabel.Text =
                _dataSelecionada.ToString(
                    "dd/MM/yyyy",
                    new CultureInfo("pt-BR"));
        }
    }

    // ============================================================
    // BOTÕES DE FILTRO
    // ============================================================

    private async void FiltroConcluidas_Clicked(
        object sender,
        EventArgs e)
    {
        _filtroAtual = "FINALIZADA";

        AtualizarBotoesFiltro();

        await CarregarCompras();
    }

    private async void FiltroPendentes_Clicked(
        object sender,
        EventArgs e)
    {
        _filtroAtual = "PENDENTE";

        AtualizarBotoesFiltro();

        await CarregarCompras();
    }

    private async void FiltroCanceladas_Clicked(
        object sender,
        EventArgs e)
    {
        _filtroAtual = "CANCELADA";

        AtualizarBotoesFiltro();

        await CarregarCompras();
    }

    private void AtualizarBotoesFiltro()
    {
        // Estado normal
        BtnConcluidas.BackgroundColor =
            Color.FromArgb("#DDF5E5");

        BtnConcluidas.TextColor =
            Color.FromArgb("#16803C");

        BtnPendentes.BackgroundColor =
            Color.FromArgb("#DDEBFF");

        BtnPendentes.TextColor =
            Color.FromArgb("#2463B5");

        BtnCanceladas.BackgroundColor =
            Color.FromArgb("#FFE0E0");

        BtnCanceladas.TextColor =
            Color.FromArgb("#C62828");

        // Estado selecionado
        switch (_filtroAtual)
        {
            case "FINALIZADA":

                BtnConcluidas.BackgroundColor =
                    Color.FromArgb("#16803C");

                BtnConcluidas.TextColor =
                    Colors.White;

                break;

            case "PENDENTE":

                BtnPendentes.BackgroundColor =
                    Color.FromArgb("#2463B5");

                BtnPendentes.TextColor =
                    Colors.White;

                break;

            case "CANCELADA":

                BtnCanceladas.BackgroundColor =
                    Color.FromArgb("#C62828");

                BtnCanceladas.TextColor =
                    Colors.White;

                break;
        }
    }

    // ============================================================
    // NOVA COMPRA
    // ============================================================

    private async void NovaCompra_Clicked(
        object sender,
        EventArgs e)
    {
        await Shell.Current.GoToAsync("NovaCompraPage");
    }

    // ============================================================
    // DETALHES
    // ============================================================

    private async void Compra_Tapped(
        object sender,
        TappedEventArgs e)
    {
        if (sender is Border border &&
            border.BindingContext is CompraListaItem item)
        {
            int compraId = item.Compra.Id;

            await Shell.Current.GoToAsync(
                $"DetalhesCompraPage?compraId={compraId}");
        }
    }

    // ============================================================
    // VOLTAR
    // ============================================================

    private async void Voltar_Clicked(
        object sender,
        EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }

    // ============================================================
    // MODELO DA LISTA
    // ============================================================

    private class CompraListaItem
    {
        public Compra Compra { get; set; } = new();

        public int QuantidadeItens { get; set; }

        public string NumeroCompra =>
            $"Compra #{Compra.Id:000}";

        public string Horario =>
            Compra.DataCompra.ToString(
                "HH:mm",
                new CultureInfo("pt-BR"));

        public string QuantidadeItensTexto
        {
            get
            {
                if (QuantidadeItens == 1)
                    return "1 produto";

                return $"{QuantidadeItens} produtos";
            }
        }

        public string TotalFormatado =>
            Compra.ValorTotal.ToString(
                "C",
                new CultureInfo("pt-BR"));

        public string StatusTexto
        {
            get
            {
                string status =
                    (Compra.Status ?? "")
                    .Trim()
                    .ToUpperInvariant();

                if (status == "CANCELADA" ||
                    status == "CANCELADO")
                    return "CANCELADA";

                if (status == "ABERTA" ||
                    status == "PENDENTE" ||
                    status == "PENDENTES")
                    return "PENDENTE";

                return "CONCLUÍDA";
            }
        }

        public Color StatusCor
        {
            get
            {
                return StatusTexto switch
                {
                    "CANCELADA" =>
                        Color.FromArgb("#C62828"),

                    "PENDENTE" =>
                        Color.FromArgb("#2463B5"),

                    _ =>
                        Color.FromArgb("#16803C")
                };
            }
        }

        public Color StatusFundo
        {
            get
            {
                return StatusTexto switch
                {
                    "CANCELADA" =>
                        Color.FromArgb("#FFE0E0"),

                    "PENDENTE" =>
                        Color.FromArgb("#DDEBFF"),

                    _ =>
                        Color.FromArgb("#DDF5E5")
                };
            }
        }
    }
}