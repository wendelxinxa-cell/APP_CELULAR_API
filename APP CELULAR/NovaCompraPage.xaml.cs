using APP_CELULAR.Data;
using APP_CELULAR.Models;
using System.Globalization;

namespace APP_CELULAR;

public partial class NovaCompraPage : ContentPage
{
    private readonly AppDatabase _database;

    private List<Pessoa> _fornecedores = new();
    private List<Material> _materiais = new();

    private Pessoa? _fornecedorSelecionado;
    private Material? _materialSelecionado;

    private readonly List<ItemCompra> _itens = new();

    public NovaCompraPage()
    {
        InitializeComponent();

        _database = new AppDatabase();

        ItensCollection.ItemsSource = _itens;
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
            var pessoas = await _database.ObterPessoas();

            _fornecedores = pessoas
                .Where(x => x.Fornecedor)
                .OrderBy(x => x.Nome)
                .ToList();

            _materiais = await _database.ObterMateriais();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Erro",
                $"Não foi possível carregar os dados.\n\n{ex.Message}",
                "OK");
        }
    }

    // =========================
    // FORNECEDOR
    // =========================

    private async void SelecionarFornecedor_Clicked(
        object sender,
        TappedEventArgs e)
    {
        if (_fornecedores.Count == 0)
        {
            await DisplayAlert(
                "Nenhum fornecedor",
                "Cadastre primeiro uma pessoa como fornecedor.",
                "OK");

            return;
        }

        string[] nomes = _fornecedores
            .Select(x => x.Nome)
            .ToArray();

        string opcao = await DisplayActionSheet(
            "Selecionar fornecedor",
            "Cancelar",
            null,
            nomes);

        if (opcao == "Cancelar" ||
            string.IsNullOrWhiteSpace(opcao))
        {
            return;
        }

        _fornecedorSelecionado = _fornecedores
            .FirstOrDefault(x => x.Nome == opcao);

        if (_fornecedorSelecionado != null)
        {
            FornecedorLabel.Text =
                _fornecedorSelecionado.Nome;

            FornecedorLabel.TextColor = Colors.Black;
        }
    }

    // =========================
    // MATERIAL
    // =========================

    private async void SelecionarMaterial_Clicked(
        object sender,
        TappedEventArgs e)
    {
        if (_materiais.Count == 0)
        {
            await DisplayAlert(
                "Nenhum material",
                "Cadastre primeiro um material.",
                "OK");

            return;
        }

        string[] nomes = _materiais
            .Select(x => x.Nome)
            .ToArray();

        string opcao = await DisplayActionSheet(
            "Selecionar material",
            "Cancelar",
            null,
            nomes);

        if (opcao == "Cancelar" ||
            string.IsNullOrWhiteSpace(opcao))
        {
            return;
        }

        _materialSelecionado = _materiais
            .FirstOrDefault(x => x.Nome == opcao);

        if (_materialSelecionado != null)
        {
            MaterialLabel.Text =
                _materialSelecionado.Nome;

            MaterialLabel.TextColor = Colors.Black;

            AtualizarEstoque();
        }
    }

    private void AtualizarEstoque()
    {
        if (_materialSelecionado == null)
        {
            EstoqueLabel.Text =
                "Selecione um material";

            return;
        }

        EstoqueLabel.Text =
            $"{_materialSelecionado.EstoqueAtual:N2} {_materialSelecionado.Unidade}";
    }

    // =========================
    // CÁLCULO
    // =========================

    private void Quantidade_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        AtualizarTotalItem();
    }

    private void Preco_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        AtualizarTotalItem();
    }

    private void AtualizarTotalItem()
    {
        decimal quantidade = LerDecimal(
            QuantidadeEntry.Text);

        decimal preco = LerDecimal(
            PrecoEntry.Text);

        decimal total = quantidade * preco;

        TotalItemLabel.Text =
            $"R$ {total:N2}";
    }

    private decimal LerDecimal(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return 0;

        texto = texto
            .Replace("R$", "")
            .Trim();

        if (decimal.TryParse(
            texto,
            NumberStyles.Any,
            new CultureInfo("pt-BR"),
            out decimal resultado))
        {
            return resultado;
        }

        if (decimal.TryParse(
            texto,
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out resultado))
        {
            return resultado;
        }

        return 0;
    }

    // =========================
    // ADICIONAR ITEM
    // =========================

    private async void AdicionarItem_Clicked(
        object sender,
        EventArgs e)
    {
        if (_fornecedorSelecionado == null)
        {
            await DisplayAlert(
                "Fornecedor",
                "Selecione o fornecedor da compra.",
                "OK");

            return;
        }

        if (_materialSelecionado == null)
        {
            await DisplayAlert(
                "Material",
                "Selecione o material.",
                "OK");

            return;
        }

        decimal quantidade = LerDecimal(
            QuantidadeEntry.Text);

        decimal preco = LerDecimal(
            PrecoEntry.Text);

        if (quantidade <= 0)
        {
            await DisplayAlert(
                "Quantidade",
                "Informe uma quantidade maior que zero.",
                "OK");

            return;
        }

        if (preco <= 0)
        {
            await DisplayAlert(
                "Preço",
                "Informe um preço de compra maior que zero.",
                "OK");

            return;
        }

        decimal total = quantidade * preco;

        var itemExistente = _itens.FirstOrDefault(
            x => x.MaterialId == _materialSelecionado.Id);

        if (itemExistente != null)
        {
            itemExistente.Quantidade += quantidade;

            itemExistente.ValorTotal =
                itemExistente.Quantidade *
                itemExistente.PrecoUnitario;
        }
        else
        {
            var item = new ItemCompra
            {
                MaterialId = _materialSelecionado.Id,

                MaterialNome =
                    _materialSelecionado.Nome,

                Unidade =
                    _materialSelecionado.Unidade,

                Quantidade = quantidade,

                PrecoUnitario = preco,

                ValorTotal = total
            };

            _itens.Add(item);
        }

        AtualizarLista();

        LimparItemAtual();
    }

    private void AtualizarLista()
    {
        ItensCollection.ItemsSource = null;
        ItensCollection.ItemsSource = _itens;

        decimal total = _itens.Sum(
            x => x.ValorTotal);

        TotalCompraLabel.Text =
            $"R$ {total:N2}";
    }

    private void LimparItemAtual()
    {
        _materialSelecionado = null;

        MaterialLabel.Text =
            "Selecione o material";

        MaterialLabel.TextColor =
            Colors.Gray;

        EstoqueLabel.Text =
            "Selecione um material";

        QuantidadeEntry.Text = "";
        PrecoEntry.Text = "";

        TotalItemLabel.Text =
            "R$ 0,00";
    }

    // =========================
    // REMOVER ITEM
    // =========================

    private async void RemoverItem_Clicked(
        object sender,
        TappedEventArgs e)
    {
        if (sender is not Label label)
            return;

        if (label.BindingContext is not ItemCompra item)
            return;

        bool confirmar = await DisplayAlert(
            "Remover item",
            $"Remover {item.MaterialNome} da compra?",
            "Sim",
            "Não");

        if (!confirmar)
            return;

        _itens.Remove(item);

        AtualizarLista();
    }

    // =========================
    // FINALIZAR COMPRA
    // =========================

    private async void FinalizarCompra_Clicked(
        object sender,
        EventArgs e)
    {
        if (_fornecedorSelecionado == null)
        {
            await DisplayAlert(
                "Fornecedor",
                "Selecione o fornecedor.",
                "OK");

            return;
        }

        if (_itens.Count == 0)
        {
            await DisplayAlert(
                "Compra vazia",
                "Adicione pelo menos um item à compra.",
                "OK");

            return;
        }

        decimal total = _itens.Sum(
            x => x.ValorTotal);

        bool confirmar = await DisplayAlert(
            "Finalizar compra",
            $"Fornecedor: {_fornecedorSelecionado.Nome}\n\n" +
            $"Itens: {_itens.Count}\n" +
            $"Total: R$ {total:N2}\n\n" +
            "Deseja finalizar esta compra?",
            "Finalizar",
            "Cancelar");

        if (!confirmar)
            return;

        try
        {
            var compra = new Compra
            {
                PessoaId =
         _fornecedorSelecionado.Id,

                PessoaNome =
         _fornecedorSelecionado.Nome,

                DataCompra =
         DateTime.Now,

                ValorTotal =
         total,

                Observacao =
         ObservacaoEditor.Text ?? "",

                // A compra fica aguardando o beneficiamento/perda
                Status =
         "AGUARDANDO",

                StatusSincronizacao =
         "AGUARDANDO",

                MensagemErro =
         ""
            };

            int compraId =
                await _database.SalvarCompra(compra);


            foreach (var item in _itens)
            {
                item.CompraId = compraId;

                // Ainda não sabemos a perda.
                // Portanto, nada entra no estoque agora.
                item.QuantidadePerda = 0;
                item.QuantidadeLiquida = 0;
                item.EstoqueLancado = false;

               

                await _database.SalvarItemCompra(item);

            }

            await DisplayAlert(
                "Compra registrada",
                $"Compra registrada com sucesso!\n\n" +
                $"Status: AGUARDANDO...\n" +
                $"Total: R$ {total:N2}\n\n" +
                "Informe a perda posteriormente para concluir a entrada no estoque.",
                "OK");

            LimparCompra();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Erro",
                $"Não foi possível finalizar a compra.\n\n{ex.Message}",
                "OK");
        }
    }

    // =========================
    // LIMPAR COMPRA
    // =========================

    private void LimparCompra()
    {
        _fornecedorSelecionado = null;
        _materialSelecionado = null;

        _itens.Clear();

        FornecedorLabel.Text =
            "Selecione o fornecedor";

        FornecedorLabel.TextColor =
            Colors.Gray;

        MaterialLabel.Text =
            "Selecione o material";

        MaterialLabel.TextColor =
            Colors.Gray;

        EstoqueLabel.Text =
            "Selecione um material";

        QuantidadeEntry.Text = "";
        PrecoEntry.Text = "";

        TotalItemLabel.Text =
            "R$ 0,00";

        TotalCompraLabel.Text =
            "R$ 0,00";

        ObservacaoEditor.Text = "";

        AtualizarLista();
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