using APP_CELULAR.Data;
using APP_CELULAR.Models;

namespace APP_CELULAR;

[QueryProperty(nameof(Tipo), "tipo")]
public partial class PessoasPage : ContentPage
{
    private readonly AppDatabase _database;

    private List<Pessoa> _pessoas = new();

    public string Tipo { get; set; } = "TODOS";

    public PessoasPage()
    {
        InitializeComponent();

        _database = new AppDatabase();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await CarregarPessoas();
    }
    private async Task CarregarPessoas()
    {
        try
        {
            switch (Tipo?.ToUpperInvariant())
            {
                case "CLI":
                    _pessoas = await _database.ObterClientes();
                    TituloLabel.Text = "Clientes";
                    break;
                case "FOR":
                    _pessoas = await _database.ObterFornecedores();
                    TituloLabel.Text = "Fornecedores";
                    break;
                default:
                    _pessoas = await _database.ObterPessoas();
                    TituloLabel.Text = "Todas as pessoas";
                    break;
            }

            PessoasCollection.ItemsSource = _pessoas;
            QuantidadeLabel.Text = $"{_pessoas.Count} cadastradas";
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erro", $"Não foi possível carregar as pessoas.\n\n{ex.Message}", "OK");
        }
    }

    private void Pesquisa_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        string texto = e.NewTextValue?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(texto))
        {
            PessoasCollection.ItemsSource = _pessoas;
            return;
        }

        var resultado = _pessoas
            .Where(x =>
                x.Nome.Contains(
                    texto,
                    StringComparison.OrdinalIgnoreCase)
                ||
                x.Documento.Contains(
                    texto,
                    StringComparison.OrdinalIgnoreCase)
                ||
                x.Telefone.Contains(
                    texto,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();

        PessoasCollection.ItemsSource = resultado;
    }

    private async void Adicionar_Clicked(
        object sender,
        TappedEventArgs e)
    {
        string? nome = await DisplayPromptAsync(
            "Nova pessoa",
            "Nome completo:",
            "Continuar",
            "Cancelar",
            placeholder: "Digite o nome");

        if (string.IsNullOrWhiteSpace(nome))
            return;

        nome = nome.Trim();

        string documento = await DisplayPromptAsync(
            "Documento",
            "CPF ou CNPJ:",
            "Continuar",
            "Cancelar",
            placeholder: "Digite o documento") ?? "";

        string telefone = await DisplayPromptAsync(
            "Telefone",
            "Telefone:",
            "Continuar",
            "Cancelar",
            placeholder: "(00) 00000-0000") ?? "";

        string email = await DisplayPromptAsync(
            "E-mail",
            "E-mail:",
            "Continuar",
            "Cancelar",
            placeholder: "email@exemplo.com") ?? "";

        string cidade = await DisplayPromptAsync(
            "Cidade",
            "Cidade:",
            "Continuar",
            "Cancelar",
            placeholder: "Digite a cidade") ?? "";


        string[] opcoes;

        if (Tipo?.ToUpperInvariant() == "CLI")
        {
            opcoes = new[] { "Cliente", "Cliente e Fornecedor" };
        }
        else if (Tipo?.ToUpperInvariant() == "FOR")
        {
            opcoes = new[] { "Fornecedor", "Cliente e Fornecedor" };
        }
        else
        {
            opcoes = new[] { "Cliente", "Fornecedor", "Cliente e Fornecedor" };
        }

        string tipo = await DisplayActionSheet(
            "Tipo de cadastro",
            "Cancelar",
            null,
            opcoes);


        if (tipo == "Cancelar" ||
            string.IsNullOrWhiteSpace(tipo))
        {
            return;
        }

        bool cliente =
            tipo == "Cliente" ||
            tipo == "Cliente e Fornecedor";

        bool fornecedor =
            tipo == "Fornecedor" ||
            tipo == "Cliente e Fornecedor";

        var pessoa = new Pessoa
        {
            Nome = nome,
            Documento = documento.Trim(),
            Telefone = telefone.Trim(),
            Email = email.Trim(),
            Cidade = cidade.Trim(),
            Cliente = cliente,
            Fornecedor = fornecedor,
            StatusSincronizacao = "PENDENTE"
        };

        await _database.SalvarPessoa(pessoa);

        await CarregarPessoas();

        await DisplayAlert(
            "Cadastro realizado",
            $"{nome} foi cadastrado com sucesso.",
            "OK");
    }

    private async void MenuPessoa_Clicked(
        object sender,
        TappedEventArgs e)
    {
        if (sender is not Label label)
            return;

        if (label.BindingContext is not Pessoa pessoa)
            return;

        string opcao = await DisplayActionSheet(
            pessoa.Nome,
            "Cancelar",
            "Excluir",
            "Editar");

        if (opcao == "Editar")
        {
            await EditarPessoa(pessoa);
        }
        else if (opcao == "Excluir")
        {
            await ExcluirPessoa(pessoa);
        }
    }

    private async Task EditarPessoa(Pessoa pessoa)
    {
        string? nome = await DisplayPromptAsync(
            "Editar pessoa",
            "Nome:",
            "Salvar",
            "Cancelar",
            initialValue: pessoa.Nome);

        if (string.IsNullOrWhiteSpace(nome))
            return;

        string? documento = await DisplayPromptAsync(
            "Editar documento",
            "CPF ou CNPJ:",
            "Salvar",
            "Cancelar",
            initialValue: pessoa.Documento);

        if (documento == null)
            return;

        string? telefone = await DisplayPromptAsync(
            "Editar telefone",
            "Telefone:",
            "Salvar",
            "Cancelar",
            initialValue: pessoa.Telefone);

        if (telefone == null)
            return;

        string? email = await DisplayPromptAsync(
            "Editar e-mail",
            "E-mail:",
            "Salvar",
            "Cancelar",
            initialValue: pessoa.Email);

        if (email == null)
            return;

        string? cidade = await DisplayPromptAsync(
            "Editar cidade",
            "Cidade:",
            "Salvar",
            "Cancelar",
            initialValue: pessoa.Cidade);

        if (cidade == null)
            return;

        string tipo = await DisplayActionSheet(
            "Tipo de cadastro",
            "Cancelar",
            null,
            "Cliente",
            "Fornecedor",
            "Cliente e Fornecedor");

        if (tipo == "Cancelar" ||
            string.IsNullOrWhiteSpace(tipo))
        {
            return;
        }

        bool cliente =
            tipo == "Cliente" ||
            tipo == "Cliente e Fornecedor";

        bool fornecedor =
            tipo == "Fornecedor" ||
            tipo == "Cliente e Fornecedor";

        pessoa.Nome = nome.Trim();
        pessoa.Documento = documento.Trim();
        pessoa.Telefone = telefone.Trim();
        pessoa.Email = email.Trim();
        pessoa.Cidade = cidade.Trim();

        pessoa.Cliente = cliente;
        pessoa.Fornecedor = fornecedor;

        pessoa.DataAlteracao = DateTime.Now;
        pessoa.StatusSincronizacao = "PENDENTE";

        await _database.AtualizarPessoa(pessoa);

        await CarregarPessoas();

        await DisplayAlert(
            "Cadastro atualizado",
            $"{pessoa.Nome} foi atualizado com sucesso.",
            "OK");
    }
    private async Task ExcluirPessoa(Pessoa pessoa)
    {
        bool confirmar = await DisplayAlert(
            "Excluir pessoa",
            $"Deseja excluir \"{pessoa.Nome}\"?",
            "Sim",
            "Não");

        if (!confirmar)
            return;

        await _database.ExcluirPessoa(pessoa);

        await CarregarPessoas();
    }

    private async void Voltar_Clicked(
        object sender,
        EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}