using APP_CELULAR.Data;
using APP_CELULAR.Models;
using APP_CELULAR.Services;

namespace APP_CELULAR;

public partial class NovoUsuarioPage : ContentPage
{
    private readonly AppDatabase _database;

    public NovoUsuarioPage()
    {
        InitializeComponent();

        _database = new AppDatabase();

        FuncaoPicker.SelectedIndex = 0;
        TipoNegocioPicker.SelectedIndex = 0;
    }


    private async void Salvar_Clicked(
        object sender,
        EventArgs e)
    {
        if (!SessaoUsuario.EhAdministrador || SessaoUsuario.EhDeus)
        {
            await DisplayAlert(
                "Acesso negado",
                "Somente administradores podem cadastrar usuÃ¡rios.",
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
                "AtenÃ§Ã£o",
                "Informe o nome do usuÃ¡rio.",
                "OK");

            NomeEntry.Focus();
            return;
        }


        // =====================================================
        // NÃƒO PERMITIR NOME DO MASTER
        // =====================================================

        if (string.Equals(
            nome,
            "ADMINISTRADOR MASTER",
            StringComparison.OrdinalIgnoreCase))
        {
            await DisplayAlert(
                "Nome reservado",
                "Esse nome Ã© reservado para o ADMINISTRADOR MASTER.",
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
                "AtenÃ§Ã£o",
                "Informe uma senha.",
                "OK");

            SenhaEntry.Focus();
            return;
        }


        if (senha.Length < 4)
        {
            await DisplayAlert(
                "AtenÃ§Ã£o",
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
                "AtenÃ§Ã£o",
                "As senhas nÃ£o sÃ£o iguais.",
                "OK");

            ConfirmarSenhaEntry.Focus();
            return;
        }


        // =====================================================
        // VALIDAR FUNÃ‡ÃƒO
        // =====================================================

        if (funcao != "USUARIO" &&
            funcao != "ADMINISTRADOR")
        {
            await DisplayAlert(
                "AtenÃ§Ã£o",
                "Selecione uma funÃ§Ã£o vÃ¡lida.",
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
                    "UsuÃ¡rio existente",
                    "JÃ¡ existe um usuÃ¡rio com esse nome.",
                    "OK");

                NomeEntry.Focus();
                return;
            }


            // =================================================
            // CRIAR USUÃRIO
            // =================================================

            var usuario = new Usuario
            {
                Nome = nome,
                SenhaHash =
                    _database.CriarHashSenha(senha),
                Funcao = funcao,
                TipoNegocio = TipoNegocioPicker.SelectedIndex == 1 ? "chat" : "sucata",
                EhMaster = false
            };


            await _database.SalvarUsuario(usuario);

            // Publica o novo perfil pela sessÃ£o do administrador que o cadastrou.
            // A API o manterÃ¡ pendente atÃ© a aprovaÃ§Ã£o do Administrador Master.
            var sincronizacao = await new SincronizacaoService().SincronizarUsuario(usuario);
            if (!sincronizacao.Sucesso)
            {
                await DisplayAlert(
                    "Cadastro salvo, aguardando sincronizaÃ§Ã£o",
                    $"O usuÃ¡rio ficou salvo neste aparelho, mas ainda nÃ£o chegou Ã  API. Ele nÃ£o conseguirÃ¡ entrar atÃ© a sincronizaÃ§Ã£o e a aprovaÃ§Ã£o do Administrador Master.\n\n{sincronizacao.Mensagem}",
                    "OK");
                await Shell.Current.GoToAsync("..");
                return;
            }


            await DisplayAlert(
                "UsuÃ¡rio cadastrado",
                $"O usuÃ¡rio '{nome}' foi cadastrado e enviado para aprovaÃ§Ã£o do Administrador Master.",
                "OK");


            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Erro",
                $"NÃ£o foi possÃ­vel cadastrar o usuÃ¡rio.\n\n{ex.Message}",
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
