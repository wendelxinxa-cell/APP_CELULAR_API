using APP_CELULAR.Data;
using APP_CELULAR.Models;
using System;
using Microsoft.Maui.Controls;

namespace APP_CELULAR;

public partial class MateriaisPage : ContentPage
{
    private readonly AppDatabase _database;

    private List<Material> _materiais = new();

    public MateriaisPage()
    {
        InitializeComponent();

        _database = new AppDatabase();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        UsuarioLogadoLabel.Text = "Usuário | " + (SessaoUsuario.UsuarioAtual?.Nome ?? "");
        await CarregarMateriais();
    }

    private async Task CarregarMateriais()
    {
        try
        {
            _materiais = await _database.ObterMateriais();

            MateriaisCollection.ItemsSource = _materiais;

            QuantidadeLabel.Text = _materiais.Count.ToString();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Erro",
                $"Não foi possível carregar os materiais.\n\n{ex.Message}",
                "OK");
        }
    }


    private void Pesquisa_TextChanged(object sender, TextChangedEventArgs e)
    {
        string texto = e.NewTextValue?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(texto))
        {
            MateriaisCollection.ItemsSource = _materiais;
            return;
        }

        var resultado = _materiais
            .Where(x =>
                x.Nome.Contains(
                    texto,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();

        MateriaisCollection.ItemsSource = resultado;
    }


    private async void Adicionar_Clicked(object sender, TappedEventArgs e)
    {
        string nome = await DisplayPromptAsync(
            "Novo material",
            "Digite o nome do material:",
            "Salvar",
            "Cancelar",
            "Ex.: Cobre");

        if (string.IsNullOrWhiteSpace(nome))
            return;

        nome = nome.Trim();

        bool existe = _materiais.Any(x =>
            x.Nome.Equals(
                nome,
                StringComparison.OrdinalIgnoreCase));

        if (existe)
        {
            await DisplayAlert(
                "Material já cadastrado",
                "Já existe um material com esse nome.",
                "OK");

            return;
        }


        string unidade = await DisplayActionSheet(
            "Escolha a unidade",
            "Cancelar",
            null,
            "KG",
            "UN",
            "TON",
            "M",
            "L");

        if (string.IsNullOrWhiteSpace(unidade) ||
            unidade == "Cancelar")
        {
            return;
        }


        var material = new Material
        {
            Nome = nome,
            Unidade = unidade,
            EstoqueAtual = 0,
            StatusSincronizacao = "PENDENTE"
        };

        await _database.SalvarMaterial(material);

        await CarregarMateriais();

        await DisplayAlert(
            "Material cadastrado",
            $"{nome} foi cadastrado com sucesso.",
            "OK");
    }


    private async void MenuMaterial_Clicked(
        object sender,
        TappedEventArgs e)
    {
        if (sender is not Label label)
            return;

        if (label.BindingContext is not Material material)
            return;

        string opcao = await DisplayActionSheet(
            material.Nome,
            "Cancelar",
            "Excluir",
            "Editar");

        if (opcao == "Editar")
        {
            await EditarMaterial(material);
        }
        else if (opcao == "Excluir")
        {
            await ExcluirMaterial(material);
        }
    }


    private async Task EditarMaterial(Material material)
    {
        string? novoNome = await DisplayPromptAsync(
            "Editar material",
            "Nome do material:",
            "Salvar",
            "Cancelar",
            initialValue: material.Nome);

        if (string.IsNullOrWhiteSpace(novoNome))
            return;

        novoNome = novoNome.Trim();

        material.Nome = novoNome;
        material.DataAlteracao = DateTime.Now;
        material.StatusSincronizacao = "PENDENTE";

        await _database.AtualizarMaterial(material);

        await CarregarMateriais();
    }


    private async Task ExcluirMaterial(Material material)
    {
        bool confirmar = await DisplayAlert(
            "Excluir material",
            $"Deseja excluir \"{material.Nome}\"?",
            "Sim",
            "Não");

        if (!confirmar)
            return;

        await _database.ExcluirMaterial(material);

        await CarregarMateriais();
    }


    private async void Voltar_Clicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}