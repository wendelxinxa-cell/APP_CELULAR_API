using APP_CELULAR.Data;
using APP_CELULAR.Models;
using Microsoft.Maui.Controls.Shapes;

namespace APP_CELULAR;

[QueryProperty(nameof(VendaId), "vendaId")]
public partial class DetalhesVendaPage : ContentPage
{
    private readonly AppDatabase _database;

    private int _vendaId;

    private Venda? _venda;

    private bool _carregando;


    public string VendaId
    {
        get => _vendaId.ToString();

        set
        {
            if (int.TryParse(value, out int id))
                _vendaId = id;
        }
    }


    public DetalhesVendaPage()
    {
        InitializeComponent();

        _database = new AppDatabase();
    }


    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_carregando)
            return;

        _carregando = true;

        try
        {
            await CarregarVenda();
        }
        finally
        {
            _carregando = false;
        }
    }


    // ==================================================
    // CARREGAR VENDA
    // ==================================================

    private async Task CarregarVenda()
    {
        try
        {
            if (_vendaId <= 0)
            {
                await DisplayAlert(
                    "Erro",
                    "ID da venda inválido.",
                    "OK");

                await Shell.Current.GoToAsync("..");

                return;
            }


            _venda =
                await _database.ObterVenda(_vendaId);


            if (_venda == null)
            {
                await DisplayAlert(
                    "Pedido",
                    "Pedido não encontrado.",
                    "OK");

                await Shell.Current.GoToAsync("..");

                return;
            }


            // ==================================================
            // DADOS DA VENDA
            // ==================================================

            NumeroLabel.Text =
                $"#{_venda.Id:0000}";

            CabecalhoNumeroLabel.Text =
                $"Pedido #{_venda.Id:0000}";


            ClienteLabel.Text =
                string.IsNullOrWhiteSpace(
                    _venda.PessoaNome)
                    ? "Cliente não informado"
                    : _venda.PessoaNome;


            DataLabel.Text =
                _venda.DataVenda
                    .ToString(
                        "dd/MM/yyyy • HH:mm");


            TotalLabel.Text =
                $"R$ {_venda.ValorTotal:N2}";


            SubtotalLabel.Text =
                $"R$ {_venda.ValorTotal:N2}";


            if (string.IsNullOrWhiteSpace(
                _venda.Observacao))
            {
                ObservacaoLabel.Text =
                    "Nenhuma observação.";
            }
            else
            {
                ObservacaoLabel.Text =
                    _venda.Observacao;
            }


            // ==================================================
            // CARREGAR ITENS
            // ==================================================

            var itens =
                await _database.ObterItensVenda(
                    _vendaId);


            QuantidadeItensLabel.Text =
                itens.Count == 1
                    ? "1 item"
                    : $"{itens.Count} itens";


            MontarItens(itens);


            // ==================================================
            // STATUS
            // ==================================================

            AplicarStatus(_venda.Status);
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Erro",
                $"Não foi possível carregar o pedido.\n\n{ex.Message}",
                "OK");
        }
    }


    // ==================================================
    // MONTAR PRODUTOS
    // ==================================================

    private void MontarItens(
        List<ItemVenda> itens)
    {
        ItensLayout.Children.Clear();


        if (itens.Count == 0)
        {
            var vazio =
                new Label
                {
                    Text =
                        "Nenhum produto registrado neste pedido.",
                    FontSize = 13,
                    TextColor =
                        Color.FromArgb("#888"),
                    HorizontalOptions =
                        LayoutOptions.Center,
                    Margin =
                        new Thickness(0, 8)
                };

            ItensLayout.Children.Add(vazio);

            return;
        }


        foreach (var item in itens)
        {
            var produto =
                CriarItemProduto(item);

            ItensLayout.Children.Add(
                produto);
        }
    }


    // ==================================================
    // CARD DE PRODUTO
    // ==================================================

    private View CriarItemProduto(
        ItemVenda item)
    {
        var nome =
            new Label
            {
                Text = item.MaterialNome,
                FontSize = 15,
                FontAttributes =
                    FontAttributes.Bold,
                TextColor =
                    Color.FromArgb("#222")
            };


        var quantidade =
            new Label
            {
                Text =
                    $"{item.Quantidade:N2} {item.Unidade} × R$ {item.PrecoUnitario:N2}",
                FontSize = 12,
                TextColor =
                    Color.FromArgb("#777")
            };


        var descricao =
            new VerticalStackLayout
            {
                Spacing = 2,
                Children =
                {
                    nome,
                    quantidade
                }
            };


        var total =
            new Label
            {
                Text =
                    $"R$ {item.ValorTotal:N2}",
                FontSize = 15,
                FontAttributes =
                    FontAttributes.Bold,
                TextColor =
                    Color.FromArgb("#2E2A6A"),
                HorizontalTextAlignment =
                    TextAlignment.End,
                VerticalOptions =
                    LayoutOptions.Center
            };


        var grid =
            new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(
                        GridLength.Star),

                    new ColumnDefinition(
                        GridLength.Auto)
                },

                ColumnSpacing = 10
            };


        grid.Add(
            descricao,
            0,
            0);

        grid.Add(
            total,
            1,
            0);


        var border =
            new Border
            {
                BackgroundColor =
                    Color.FromArgb("#F8F8FB"),

                Stroke =
                    Color.FromArgb("#EEEEEE"),

                StrokeThickness = 1,

                Padding =
                    new Thickness(11, 9),

                Content = grid
            };


        border.StrokeShape =
            new RoundRectangle
            {
                CornerRadius = 11
            };


        return border;
    }


    // ==================================================
    // STATUS
    // ==================================================

    private void AplicarStatus(
        string? status)
    {
        string statusNormalizado =
            status?
                .Trim()
                .ToUpperInvariant() ?? "";


        switch (statusNormalizado)
        {
            case "FINALIZADA":
            case "FINALIZADO":
            case "CONCLUIDA":
            case "CONCLUIDO":

                StatusLabel.Text =
                    "✓  Concluído";

                StatusLabel.TextColor =
                    Color.FromArgb("#19A957");

                StatusBorder.BackgroundColor =
                    Color.FromArgb("#E8F8EE");

                CancelarButton.IsVisible = true;

                break;


            case "CANCELADA":
            case "CANCELADO":

                StatusLabel.Text =
                    "✕  Cancelado";

                StatusLabel.TextColor =
                    Color.FromArgb("#EF3340");

                StatusBorder.BackgroundColor =
                    Color.FromArgb("#FDEBED");

                CancelarButton.IsVisible = false;

                break;


            default:

                StatusLabel.Text =
                    "◷  Pendente";

                StatusLabel.TextColor =
                    Color.FromArgb("#1689E8");

                StatusBorder.BackgroundColor =
                    Color.FromArgb("#E8F3FD");

                CancelarButton.IsVisible = true;

                break;
        }
    }


    // ==================================================
    // CANCELAR VENDA
    // ==================================================

    private async void Cancelar_Clicked(
        object sender,
        EventArgs e)
    {
        if (_venda == null)
            return;


        string status =
            _venda.Status?
                .Trim()
                .ToUpperInvariant() ?? "";


        if (status == "CANCELADA" ||
            status == "CANCELADO")
        {
            await DisplayAlert(
                "Pedido",
                "Este pedido já está cancelado.",
                "OK");

            return;
        }


        bool confirmar =
            await DisplayAlert(
                "Cancelar pedido",
                $"Deseja realmente cancelar o pedido #{_venda.Id:0000}?\n\n" +
                "Os produtos deste pedido serão devolvidos ao estoque.",
                "Sim, cancelar",
                "Não");


        if (!confirmar)
            return;


        try
        {
            CancelarButton.IsEnabled = false;


            bool cancelado =
                await _database
                    .CancelarVendaComDevolucaoEstoque(
                        _venda.Id);


            if (!cancelado)
            {
                await DisplayAlert(
                    "Pedido",
                    "O pedido não pôde ser cancelado.",
                    "OK");

                return;
            }


            await DisplayAlert(
                "Pedido cancelado",
                $"Pedido #{_venda.Id:0000} cancelado com sucesso.\n\n" +
                "Os produtos foram devolvidos ao estoque.",
                "OK");


            await CarregarVenda();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Erro ao cancelar",
                $"Não foi possível cancelar o pedido.\n\n{ex.Message}",
                "OK");
        }
        finally
        {
            CancelarButton.IsEnabled = true;
        }
    }


    // ==================================================
    // EDITAR
    // ==================================================

    private async void Editar_Clicked(
        object sender,
        EventArgs e)
    {
        await DisplayAlert(
            "Editar pedido",
            "A função de edição vamos implementar na próxima etapa.",
            "OK");
    }


    // ==================================================
    // VOLTAR
    // ==================================================

    private async void Voltar_Clicked(
        object sender,
        EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}