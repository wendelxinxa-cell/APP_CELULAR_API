using APP_CELULAR.Data;
using APP_CELULAR.Models;
using System.Globalization;

namespace APP_CELULAR;

public partial class NovaVendaPage : ContentPage
{
    private readonly AppDatabase _database;

    private List<Pessoa> _clientes = new();
    private List<Material> _materiais = new();

    private Pessoa? _clienteSelecionado;
    private Material? _materialSelecionado;

    private readonly List<ItemVenda> _itens = new();

    public NovaVendaPage()
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

            _clientes = pessoas
                .Where(x => x.Cliente)
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
    // CLIENTE
    // =========================

    private async void SelecionarCliente_Clicked(
        object sender,
        TappedEventArgs e)
    {
        if (_clientes.Count == 0)
        {
            await DisplayAlert(
                "Nenhum cliente",
                "Cadastre primeiro uma pessoa como cliente.",
                "OK");

            return;
        }

        string[] nomes = _clientes
            .Select(x => x.Nome)
            .ToArray();

        string opcao = await DisplayActionSheet(
            "Selecionar cliente",
            "Cancelar",
            null,
            nomes);

        if (opcao == "Cancelar" ||
            string.IsNullOrWhiteSpace(opcao))
        {
            return;
        }

        _clienteSelecionado = _clientes
            .FirstOrDefault(x => x.Nome == opcao);

        if (_clienteSelecionado != null)
        {
            ClienteLabel.Text =
                _clienteSelecionado.Nome;

            ClienteLabel.TextColor =
                Colors.Black;
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

            MaterialLabel.TextColor =
                Colors.Black;

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
        decimal quantidade =
            LerDecimal(QuantidadeEntry.Text);

        decimal preco =
            LerDecimal(PrecoEntry.Text);

        decimal total =
            quantidade * preco;

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
        if (_clienteSelecionado == null)
        {
            await DisplayAlert(
                "Cliente",
                "Selecione o cliente da venda.",
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

        decimal quantidade =
            LerDecimal(QuantidadeEntry.Text);

        decimal preco =
            LerDecimal(PrecoEntry.Text);

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
                "Informe um preço de venda maior que zero.",
                "OK");

            return;
        }

        if (quantidade >
            _materialSelecionado.EstoqueAtual)
        {
            await DisplayAlert(
                "Estoque insuficiente",
                $"Você tentou vender {quantidade:N2} " +
                $"{_materialSelecionado.Unidade}, " +
                $"mas existem apenas " +
                $"{_materialSelecionado.EstoqueAtual:N2} " +
                $"{_materialSelecionado.Unidade} em estoque.",
                "OK");

            return;
        }

        decimal quantidadeJaAdicionada =
            _itens
                .Where(x =>
                    x.MaterialId ==
                    _materialSelecionado.Id)
                .Sum(x => x.Quantidade);

        if (quantidadeJaAdicionada +
            quantidade >
            _materialSelecionado.EstoqueAtual)
        {
            await DisplayAlert(
                "Estoque insuficiente",
                "A quantidade total deste material na venda " +
                "ultrapassa o estoque disponível.",
                "OK");

            return;
        }

        decimal total =
            quantidade * preco;

        var itemExistente =
            _itens.FirstOrDefault(
                x => x.MaterialId ==
                     _materialSelecionado.Id);

        if (itemExistente != null)
        {
            itemExistente.Quantidade += quantidade;

            itemExistente.ValorTotal =
                itemExistente.Quantidade *
                itemExistente.PrecoUnitario;
        }
        else
        {
            var item = new ItemVenda
            {
                MaterialId =
                    _materialSelecionado.Id,

                MaterialNome =
                    _materialSelecionado.Nome,

                Unidade =
                    _materialSelecionado.Unidade,

                Quantidade =
                    quantidade,

                PrecoUnitario =
                    preco,

                ValorTotal =
                    total
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

        decimal total =
            _itens.Sum(x => x.ValorTotal);

        TotalVendaLabel.Text =
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

        if (label.BindingContext is not ItemVenda item)
            return;

        bool confirmar = await DisplayAlert(
            "Remover item",
            $"Remover {item.MaterialNome} da venda?",
            "Sim",
            "Não");

        if (!confirmar)
            return;

        _itens.Remove(item);

        AtualizarLista();
    }

    // =========================
    // FINALIZAR VENDA
    // =========================

    private async void FinalizarVenda_Clicked(
        object sender,
        EventArgs e)
    {
        if (_clienteSelecionado == null)
        {
            await DisplayAlert(
                "Cliente",
                "Selecione o cliente.",
                "OK");

            return;
        }

        if (_itens.Count == 0)
        {
            await DisplayAlert(
                "Venda vazia",
                "Adicione pelo menos um item à venda.",
                "OK");

            return;
        }

        // Conferir novamente o estoque
        // antes de gravar a venda.

        foreach (var item in _itens)
        {
            var material =
                _materiais.FirstOrDefault(
                    x => x.Id == item.MaterialId);

            if (material == null)
            {
                await DisplayAlert(
                    "Erro",
                    $"O material {item.MaterialNome} não foi encontrado.",
                    "OK");

                return;
            }

            if (item.Quantidade >
                material.EstoqueAtual)
            {
                await DisplayAlert(
                    "Estoque insuficiente",
                    $"O material {item.MaterialNome} " +
                    $"não possui estoque suficiente.",
                    "OK");

                return;
            }
        }

        decimal total =
            _itens.Sum(x => x.ValorTotal);

        bool confirmar = await DisplayAlert(
            "Finalizar venda",
            $"Cliente: {_clienteSelecionado.Nome}\n\n" +
            $"Itens: {_itens.Count}\n" +
            $"Total: R$ {total:N2}\n\n" +
            "Deseja finalizar esta venda?",
            "Finalizar",
            "Cancelar");

        if (!confirmar)
            return;

        try
        {
            var venda = new Venda
            {
                PessoaId =
                    _clienteSelecionado.Id,

                PessoaNome =
                    _clienteSelecionado.Nome,

                DataVenda =
                    DateTime.Now,

                ValorTotal =
                    total,

                Observacao =
                    ObservacaoEditor.Text ?? "",

                Status =
                    "FINALIZADA",

                StatusSincronizacao =
                    "PENDENTE"
            };

            int vendaId =
                await _database.SalvarVenda(venda);

            foreach (var item in _itens)
            {
                item.VendaId =
                    vendaId;

                await _database.SalvarItemVenda(item);

                var material =
                    _materiais.FirstOrDefault(
                        x => x.Id == item.MaterialId);

                if (material != null)
                {
                    material.EstoqueAtual -=
                        item.Quantidade;

                    material.DataAlteracao =
                        DateTime.Now;

                    material.StatusSincronizacao =
                        "PENDENTE";

                    await _database.AtualizarMaterial(
                        material);
                }
            }

            await DisplayAlert(
               "Venda finalizada",
               $"Venda registrada com sucesso!\n\n" +
               $"Pedido: #{vendaId:0000}\n" +
               $"Total: R$ {total:N2}",
               "OK");

            await Shell.Current.GoToAsync("..");

            LimparVenda();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Erro",
                $"Não foi possível finalizar a venda.\n\n{ex.Message}",
                "OK");
        }
    }

    // =========================
    // LIMPAR VENDA
    // =========================

    private void LimparVenda()
    {
        _clienteSelecionado = null;
        _materialSelecionado = null;

        _itens.Clear();

        ClienteLabel.Text =
            "Selecione o cliente";

        ClienteLabel.TextColor =
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

        TotalVendaLabel.Text =
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