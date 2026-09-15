using APP_CELULAR.Data;
using APP_CELULAR.Models;

namespace APP_CELULAR;

public partial class NovoUsuarioPage : ContentPage
{
    private readonly AppDatabase _database;

    public NovoUsuarioPage()
    {
        InitializeComponent();

        _database = new AppDatabase();

        FuncaoPicker.SelectedIndex = 0;
    }


    private async void Salvar_Clicked(
        object sender,
        EventArgs e)
    {
        if (!SessaoUsuario.EhAdministrador)
        {
            await DisplayAlert(
                "Acesso negado",
                "Somente administradores podem cadastrar usuários.",
                "OK");

            return;
        }

        string nome =
            NomeEntry.Text?.Trim() ?? "";

        string senha =
            SenhaEntry.Text ?? "";

        string confirmarSenha =
            ConfirmarSenhaEntry.Text ?? "";

        string funcao =
            FuncaoPicker.SelectedItem?.ToString() ?? "";


        // =====================================================
        // VALIDAR NOME
        // =====================================================

        if (string.IsNullOrWhiteSpace(nome))
        {
            await DisplayAlert(
                "Atenção",
                "Informe o nome do usuário.",
                "OK");

            NomeEntry.Focus();
            return;
        }


        // =====================================================
        // NÃO PERMITIR NOME DO MASTER
        // =====================================================

        if (string.Equals(
            nome,
            "ADMINISTRADOR MASTER",
            StringComparison.OrdinalIgnoreCase))
        {
            await DisplayAlert(
                "Nome reservado",
                "Esse nome é reservado para o ADMINISTRADOR MASTER.",
                "OK");

            NomeEntry.Focus();
            return;
        }


        // =====================================================
        // VALIDAR SENHA
        // =====================================================

        if (string.IsNullOrWhiteSpace(senha))
        {
            await DisplayAlert(
                "Atenção",
                "Informe uma senha.",
                "OK");

            SenhaEntry.Focus();
            return;
        }


        if (senha.Length < 4)
        {
            await DisplayAlert(
                "Atenção",
                "A senha deve possuir pelo menos 4 caracteres.",
                "OK");

            SenhaEntry.Focus();
            return;
        }


        // =====================================================
        // CONFIRMAR SENHA
        // =====================================================

        if (senha != confirmarSenha)
        {
            await DisplayAlert(
                "Atenção",
                "As senhas não são iguais.",
                "OK");

            ConfirmarSenhaEntry.Focus();
            return;
        }


        // =====================================================
        // VALIDAR FUNÇÃO
        // =====================================================

        if (funcao != "USUARIO" &&
            funcao != "ADMINISTRADOR")
        {
            await DisplayAlert(
                "Atenção",
                "Selecione uma função válida.",
                "OK");

            return;
        }


        try
        {
            BtnSalvar.IsEnabled = false;


            // =================================================
            // VERIFICAR NOME DUPLICADO
            // =================================================

            bool existe =
                await _database.ExisteUsuarioComNome(nome);

            if (existe)
            {
                await DisplayAlert(
                    "Usuário existente",
                    "Já existe um usuário com esse nome.",
                    "OK");

                NomeEntry.Focus();
                return;
            }


            // =================================================
            // CRIAR USUÁRIO
            // =================================================

            var usuario = new Usuario
            {
                Nome = nome,
                SenhaHash =
                    _database.CriarHashSenha(senha),
                Funcao = funcao,
                EhMaster = false
            };


            await _database.SalvarUsuario(usuario);


            await DisplayAlert(
                "Usuário cadastrado",
                $"O usuário '{nome}' foi cadastrado com sucesso.",
                "OK");


            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Erro",
                $"Não foi possível cadastrar o usuário.\n\n{ex.Message}",
                "OK");
        }
        finally
        {
            BtnSalvar.IsEnabled = true;
        }
    }


    private async void Voltar_Clicked(
        object sender,
        EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}