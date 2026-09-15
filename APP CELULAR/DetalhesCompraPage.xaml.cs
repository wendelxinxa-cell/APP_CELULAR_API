using APP_CELULAR.Data;
using APP_CELULAR.Models;
using Microsoft.Maui.Controls.Shapes;
using System.Globalization;

namespace APP_CELULAR;

[QueryProperty(nameof(CompraId), "compraId")]
public partial class DetalhesCompraPage : ContentPage
{
    private readonly AppDatabase _database;

    private Compra? _compra;

    private List<ItemCompra> _itens = new();

    private int _compraId;

    public int CompraId
    {
        get => _compraId;
        set
        {
            _compraId = value;

            if (IsLoaded)
                _ = CarregarCompra();
        }
    }

    private readonly CultureInfo _ptBR =
        new("pt-BR");

    public DetalhesCompraPage()
    {
        InitializeComponent();

        _database = new AppDatabase();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_compraId > 0)
            await CarregarCompra();
    }

    private async Task CarregarCompra()
    {
        try
        {
            _compra = await _database.ObterCompra(_compraId);

            if (_compra == null)
            {
                await DisplayAlert(
                    "Erro",
                    "Compra não encontrada.",
                    "OK");

                await Shell.Current.GoToAsync("..");
                return;
            }

            _itens = await _database.ObterItensCompra(_compra.Id);

            PreencherCabecalho();
            MontarItens();
            AtualizarResumo();
            AtualizarEstadoTela();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Erro",
                $"Não foi possível carregar a compra.\n\n{ex.Message}",
                "OK");
        }
    }

    private void PreencherCabecalho()
    {
        if (_compra == null)
            return;

        TituloLabel.Text =
            $"Compra #{_compra.Id:000}";

        NumeroCompraLabel.Text =
            $"Compra #{_compra.Id:000}";

        FornecedorLabel.Text =
            string.IsNullOrWhiteSpace(_compra.PessoaNome)
                ? "Não informado"
                : _compra.PessoaNome;

        DataLabel.Text =
            _compra.DataCompra.ToString(
                "dd/MM/yyyy HH:mm",
                _ptBR);

        TotalLabel.Text =
            _compra.ValorTotal.ToString(
                "C",
                _ptBR);

        if (!string.IsNullOrWhiteSpace(_compra.Observacao))
        {
            ObservacaoLayout.IsVisible = true;
            ObservacaoLabel.Text = _compra.Observacao;
        }
        else
        {
            ObservacaoLayout.IsVisible = false;
        }
    }

    private void MontarItens()
    {
        ItensLayout.Children.Clear();

        QuantidadeItensLabel.Text =
            _itens.Count == 1
                ? "1 produto"
                : $"{_itens.Count} produtos";

        foreach (var item in _itens)
        {
            ItensLayout.Children.Add(
                CriarCardItem(item));
        }
    }

    private Border CriarCardItem(ItemCompra item)
    {
        var nomeMaterial = new Label
        {
            Text = item.MaterialNome,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#33333A")
        };

        var unidade = string.IsNullOrWhiteSpace(item.Unidade)
            ? "KG"
            : item.Unidade;

        var brutoLabel = new Label
        {
            Text =
                $"Bruto: {item.Quantidade.ToString("N2", _ptBR)} {unidade}",
            FontSize = 13,
            TextColor = Color.FromArgb("#777780")
        };

        var valorLabel = new Label
        {
            Text =
                $"Valor: {item.ValorTotal.ToString("C", _ptBR)}",
            FontSize = 13,
            TextColor = Color.FromArgb("#777780")
        };

        var perdaLabel = new Label
        {
            Text = "REBATE / PERDA",
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#C62828"),
            VerticalOptions = LayoutOptions.Center
        };

        var perdaEntry = new Entry
        {
            Text = item.QuantidadePerda
                .ToString("0.##", _ptBR),

            Keyboard = Keyboard.Numeric,

            FontSize = 16,

            FontAttributes = FontAttributes.Bold,

            HorizontalTextAlignment =
                TextAlignment.Center,

            TextColor =
                Color.FromArgb("#C62828"),

            BackgroundColor =
                Color.FromArgb("#FFF0F0"), //campo onde digita a qtd perda

            HeightRequest = 46,

            WidthRequest = 105,

            Placeholder = "0,00",

            PlaceholderColor =
                Color.FromArgb("#D88A8A")
        };

        var unidadePerdaLabel = new Label
        {
            Text = unidade,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#C62828"),
            VerticalOptions = LayoutOptions.Center
        };

        var perdaGrid = new Grid
        {
            ColumnDefinitions =
    {
        new ColumnDefinition(GridLength.Star),
        new ColumnDefinition(GridLength.Auto),
        new ColumnDefinition(GridLength.Auto)
    },

            ColumnSpacing = 8,

            Padding = new Thickness(10, 6),

            BackgroundColor =
                Color.FromArgb("#FA8989") //fundo da linha toda de perda
        };

        Grid.SetColumn(perdaLabel, 0);
        Grid.SetColumn(perdaEntry, 1);
        Grid.SetColumn(unidadePerdaLabel, 2);

        perdaGrid.Children.Add(perdaLabel);
        perdaGrid.Children.Add(perdaEntry);
        perdaGrid.Children.Add(unidadePerdaLabel);

        var liquidaLabel = new Label
        {
            Text =
                $"Líquido: {(item.Quantidade - item.QuantidadePerda).ToString("N2", _ptBR)} {unidade}",

            FontSize = 14,

            FontAttributes =
                FontAttributes.Bold,

            TextColor =
                Color.FromArgb("#16803C"),

            Margin =
                new Thickness(0, 8, 0, 0)
        };

        perdaEntry.TextChanged += (_, _) =>
        {
            AtualizarItemAPartirDaEntrada(
                item,
                perdaEntry,
                liquidaLabel);

            AtualizarResumo();
        };

        var conteudo = new VerticalStackLayout
        {
            Spacing = 5
        };

        conteudo.Children.Add(nomeMaterial);
        conteudo.Children.Add(brutoLabel);
        conteudo.Children.Add(valorLabel);
        conteudo.Children.Add(perdaGrid);
        conteudo.Children.Add(liquidaLabel);

        var border = new Border
        {
            Content = conteudo,

            BackgroundColor = Colors.White,

            Stroke =
                Color.FromArgb("#E5E5EA"),

            StrokeThickness = 1,

            Padding = 16
        };

        border.StrokeShape =
            new RoundRectangle
            {
                CornerRadius = 14
            };

        return border;
    }

    private void AtualizarItemAPartirDaEntrada(
        ItemCompra item,
        Entry entry,
        Label liquidaLabel)
    {
        string texto = entry.Text?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(texto))
        {
            item.QuantidadePerda = 0;
        }
        else if (decimal.TryParse(
            texto,
            NumberStyles.Number,
            _ptBR,
            out decimal perda))
        {
            item.QuantidadePerda = perda;
        }
        else
        {
            item.QuantidadePerda = 0;
        }

        decimal liquida =
            item.Quantidade -
            item.QuantidadePerda;

        string unidade =
            string.IsNullOrWhiteSpace(item.Unidade)
                ? "KG"
                : item.Unidade;

        liquidaLabel.Text =
            $"Líquido: {liquida.ToString("N2", _ptBR)} {unidade}";

        if (item.QuantidadePerda > item.Quantidade)
        {
            liquidaLabel.TextColor =
                Color.FromArgb("#C62828");
        }
        else if (item.QuantidadePerda < 0)
        {
            liquidaLabel.TextColor =
                Color.FromArgb("#C62828");
        }
        else
        {
            liquidaLabel.TextColor =
                Color.FromArgb("#16803C");
        }
    }

    private void AtualizarResumo()
    {
        decimal bruto = _itens.Sum(x => x.Quantidade);

        decimal perda = _itens.Sum(x => x.QuantidadePerda);

        decimal liquido = bruto - perda;

        PesoBrutoLabel.Text =
            bruto.ToString("N2", _ptBR);

        PerdaTotalLabel.Text =
            perda.ToString("N2", _ptBR);

        QuantidadeLiquidaLabel.Text =
            liquido.ToString("N2", _ptBR);
    }

    private void AtualizarEstadoTela()
    {
        if (_compra == null)
            return;

        string status =
            (_compra.Status ?? "")
            .Trim()
            .ToUpperInvariant();

        bool finalizada =
            status == "FINALIZADA" ||
            status == "FINALIZADO" ||
            status == "CONCLUIDA" ||
            status == "CONCLUÍDA" ||
            status == "CONCLUIDO" ||
            status == "CONCLUÍDO";

        bool cancelada =
            status == "CANCELADA" ||
            status == "CANCELADO";

        if (finalizada)
        {
            StatusLabel.Text = "CONCLUÍDA";

            StatusLabel.TextColor =
                Color.FromArgb("#16803C");

            StatusBorder.BackgroundColor =
                Color.FromArgb("#DDF5E5");

            OrientacaoLabel.Text =
                "Compra concluída e lançada no estoque.";

            BtnConcluir.IsVisible = false;
            BtnCancelar.IsVisible = true;
            BtnCancelar.Text = "Cancelar compra e devolver ao estoque";

            return;
        }

        if (cancelada)
        {
            StatusLabel.Text = "CANCELADA";

            StatusLabel.TextColor =
                Color.FromArgb("#C62828");

            StatusBorder.BackgroundColor =
                Color.FromArgb("#FFE0E0");

            OrientacaoLabel.Text =
                "Esta compra foi cancelada.";

            BtnConcluir.IsVisible = false;
            BtnCancelar.IsVisible = false;

            return;
        }

        StatusLabel.Text = "AGUARDANDO...";

        StatusLabel.TextColor =
            Color.FromArgb("#2463B5");

        StatusBorder.BackgroundColor =
            Color.FromArgb("#DDEBFF");

        OrientacaoLabel.Text =
            "Informe a perda de cada material para concluir.";

        BtnConcluir.IsVisible = true;
        BtnCancelar.IsVisible = true;
    }

    private async void Concluir_Clicked(
        object sender,
        EventArgs e)
    {
        if (_compra == null)
            return;

        // Garante que os valores digitados sejam válidos.
        foreach (var item in _itens)
        {
            if (item.QuantidadePerda < 0)
            {
                await DisplayAlert(
                    "Perda inválida",
                    $"A perda do material '{item.MaterialNome}' não pode ser negativa.",
                    "OK");

                return;
            }

            if (item.QuantidadePerda > item.Quantidade)
            {
                await DisplayAlert(
                    "Perda inválida",
                    $"A perda do material '{item.MaterialNome}' não pode ser maior que a quantidade bruta.",
                    "OK");

                return;
            }
        }

        decimal perdaTotal =
            _itens.Sum(x => x.QuantidadePerda);

        decimal liquidoTotal =
            _itens.Sum(x =>
                x.Quantidade - x.QuantidadePerda);

        string mensagem =
            $"Peso bruto: {_itens.Sum(x => x.Quantidade).ToString("N2", _ptBR)}\n" +
            $"Perda: {perdaTotal.ToString("N2", _ptBR)}\n" +
            $"Entrada no estoque: {liquidoTotal.ToString("N2", _ptBR)}\n\n" +
            "Deseja concluir esta compra?";

        bool confirmar =
            await DisplayAlert(
                "Concluir compra",
                mensagem,
                "Concluir",
                "Voltar");

        if (!confirmar)
            return;

        try
        {
            BtnConcluir.IsEnabled = false;
            BtnCancelar.IsEnabled = false;

            // Salva as perdas informadas.
            foreach (var item in _itens)
            {
                item.QuantidadeLiquida =
                    item.Quantidade -
                    item.QuantidadePerda;

                await _database.AtualizarItemCompra(item);
            }

            // O método ConcluirCompra:
            // 1. valida os itens
            // 2. lança somente QuantidadeLiquida
            // 3. marca EstoqueLancado
            // 4. muda a compra para FINALIZADA
            // 5. tudo dentro de uma transação.
            await _database.ConcluirCompra(
                _compra.Id);

            await DisplayAlert(
                "Compra concluída",
                $"A compra foi concluída.\n\n" +
                $"Entrada líquida no estoque: " +
                $"{liquidoTotal.ToString("N2", _ptBR)}",
                "OK");

            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Erro",
                $"Não foi possível concluir a compra.\n\n{ex.Message}",
                "OK");

            BtnConcluir.IsEnabled = true;
            BtnCancelar.IsEnabled = true;
        }
    }

    private async void Cancelar_Clicked(
        object sender,
        EventArgs e)
    {
        if (_compra == null)
            return;

        bool confirmar =
            await DisplayAlert(
                "Cancelar compra",
                "Deseja realmente cancelar esta compra?",
                "Cancelar compra",
                "Voltar");

        if (!confirmar)
            return;

        try
        {
            BtnConcluir.IsEnabled = false;
            BtnCancelar.IsEnabled = false;

            await _database
                .CancelarCompraComDevolucaoEstoque(
                    _compra.Id);

            await DisplayAlert(
                "Compra cancelada",
                "A compra foi cancelada com sucesso.",
                "OK");

            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Erro",
                $"Não foi possível cancelar a compra.\n\n{ex.Message}",
                "OK");

            BtnConcluir.IsEnabled = true;
            BtnCancelar.IsEnabled = true;
        }
    }

    private async void Voltar_Clicked(
        object sender,
        EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}