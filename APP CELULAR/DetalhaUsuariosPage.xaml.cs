using APP_CELULAR.Data;
using APP_CELULAR.Models;
using APP_CELULAR.Services;

namespace APP_CELULAR;

[QueryProperty(nameof(UsuarioId), "usuarioId")]
public partial class DetalhaUsuariosPage : ContentPage
{
    private readonly AppDatabase _database;

    private Usuario? _usuario;

    private int _usuarioId;

    public string UsuarioId
    {
        get => _usuarioId.ToString();

        set
        {
            if (int.TryParse(value, out int id))
                _usuarioId = id;
        }
    }


    public DetalhaUsuariosPage()
    {
        InitializeComponent();

        _database = new AppDatabase();

        FuncaoPicker.SelectedIndex = 0;
        TipoNegocioPicker.SelectedIndex = 0;
    }


    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_usuario == null && _usuarioId > 0)
            await CarregarUsuario();
    }


    private async Task CarregarUsuario()
    {
        try
        {
            _usuario =
                await _database.ObterUsuario(_usuarioId);

            if (_usuario == null)
            {
                await DisplayAlert(
                    "Erro",
                    "Usuário não encontrado.",
                    "OK");

                await Shell.Current.GoToAsync("..");

                return;
            }


            NomeEntry.Text =
                _usuario.Nome;
            TipoNegocioPicker.SelectedIndex = _usuario.TipoNegocio == "chat" ? 1 : 0;


            if (_usuario.Funcao == "ADMINISTRADOR")
                FuncaoPicker.SelectedIndex = 1;
            else
                FuncaoPicker.SelectedIndex = 0;


            // =================================================
            // MASTER
            // =================================================

            if (_usuario.EhMaster)
            {
                MasterFrame.IsVisible = true;

                NomeEntry.IsEnabled = false;
                FuncaoPicker.IsEnabled = false;
                TipoNegocioPicker.IsEnabled = false;

                BtnExcluir.IsVisible = false;

                SubtituloLabel.Text =
                    "Conta protegida do sistema";

                SenhaTituloLabel.Text =
                    "NOVA SENHA";

                return;
            }


            // =================================================
            // USUÁRIO NORMAL
            // =================================================

            MasterFrame.IsVisible = false;


            bool souAdministrador =
                SessaoUsuario.EhAdministrador;

            bool souEu =
                SessaoUsuario.UsuarioAtual?.Id ==
                _usuario.Id;


            // =================================================
            // ADMINISTRADOR
            // =================================================

            if (souAdministrador)
            {
                NomeEntry.IsEnabled = true;
                FuncaoPicker.IsEnabled = true;
                TipoNegocioPicker.IsEnabled = true;

                // Administrador não pode excluir a própria conta.
                BtnExcluir.IsVisible =
                    !souEu;

                SubtituloLabel.Text =
                    "Gerenciar usuário";

                return;
            }


            // =================================================
            // USUÁRIO COMUM
            // =================================================

            if (souEu)
            {
                NomeEntry.IsEnabled = false;
                FuncaoPicker.IsEnabled = false;
                TipoNegocioPicker.IsEnabled = false;

                BtnExcluir.IsVisible = false;

                SubtituloLabel.Text =
                    "Alterar minha senha";

                return;
            }


            // =================================================
            // NÃO PODE EDITAR OUTRO USUÁRIO
            // =================================================

            await DisplayAlert(
                "Acesso negado",
                "Você não possui permissão para editar este usuário.",
                "OK");

            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Erro",
                $"Não foi possível carregar o usuário.\n\n{ex.Message}",
                "OK");
        }
    }


    private async void Salvar_Clicked(
        object sender,
        EventArgs e)
    {
        if (_usuario == null)
            return;


        bool souMaster =
            SessaoUsuario.EhMaster;

        bool souAdministrador =
            SessaoUsuario.EhAdministrador;

        bool souEu =
            SessaoUsuario.UsuarioAtual?.Id ==
            _usuario.Id;


        // =====================================================
        // VERIFICAR PERMISSÃO
        // =====================================================

        if (!souAdministrador && !souEu)
        {
            await DisplayAlert(
                "Acesso negado",
                "Você não possui permissão para alterar este usuário.",
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
            FuncaoPicker.SelectedItem?.ToString() ?? "USUARIO";
        string tipoNegocio = TipoNegocioPicker.SelectedIndex == 1 ? "chat" : "sucata";


        // =====================================================
        // MASTER
        // =====================================================

        if (_usuario.EhMaster)
        {
            // Nome e função nunca podem mudar.
            nome = "ADMINISTRADOR MASTER";
            funcao = "ADMINISTRADOR MASTER";
        }
        else
        {
            // Usuário comum só pode alterar a própria senha.
            if (!souAdministrador)
            {
                nome = _usuario.Nome;
                funcao = _usuario.Funcao;
                tipoNegocio = _usuario.TipoNegocio;
            }
        }


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
        // NOME RESERVADO
        // =====================================================

        if (!_usuario.EhMaster &&
            string.Equals(
                nome,
                "ADMINISTRADOR MASTER",
                StringComparison.OrdinalIgnoreCase))
        {
            await DisplayAlert(
                "Nome reservado",
                "Esse nome pertence ao ADMINISTRADOR MASTER.",
                "OK");

            return;
        }


        // =====================================================
        // VERIFICAR NOME DUPLICADO
        // =====================================================

        if (!_usuario.EhMaster)
        {
            bool existe =
                await _database.ExisteUsuarioComNome(
                    nome,
                    _usuario.Id);

            if (existe)
            {
                await DisplayAlert(
                    "Usuário existente",
                    "Já existe outro usuário com esse nome.",
                    "OK");

                return;
            }
        }


        // =====================================================
        // VALIDAR SENHA
        // =====================================================

        if (!string.IsNullOrWhiteSpace(senha))
        {
            if (senha.Length < 4)
            {
                await DisplayAlert(
                    "Atenção",
                    "A senha deve possuir pelo menos 4 caracteres.",
                    "OK");

                SenhaEntry.Focus();
                return;
            }


            if (senha != confirmarSenha)
            {
                await DisplayAlert(
                    "Atenção",
                    "As senhas não são iguais.",
                    "OK");

                ConfirmarSenhaEntry.Focus();
                return;
            }


            _usuario.SenhaHash =
                _database.CriarHashSenha(senha);
        }
        else
        {
            // Se não informou senha, mantém a atual.
            if (souEu &&
                string.IsNullOrWhiteSpace(
                    _usuario.SenhaHash))
            {
                await DisplayAlert(
                    "Atenção",
                    "Informe uma senha.",
                    "OK");

                SenhaEntry.Focus();
                return;
            }
        }


        // =====================================================
        // FUNÇÃO
        // =====================================================

        if (!_usuario.EhMaster)
        {
            if (funcao != "USUARIO" &&
                funcao != "ADMINISTRADOR")
            {
                await DisplayAlert(
                    "Atenção",
                    "Selecione uma função válida.",
                    "OK");

                return;
            }

            _usuario.Funcao = funcao;
            _usuario.TipoNegocio = tipoNegocio;
        }
        else
        {
            _usuario.Funcao =
                "ADMINISTRADOR MASTER";

            _usuario.Nome =
                "ADMINISTRADOR MASTER";

            _usuario.EhMaster = true;
        }


        try
        {
            BtnSalvar.IsEnabled = false;

            await _database.AtualizarUsuario(_usuario);
            if (!souMaster)
            {
                var sincronizacao = await new SincronizacaoService().SincronizarUsuario(_usuario);
                if (!sincronizacao.Sucesso)
                    throw new InvalidOperationException($"O usuário foi salvo neste aparelho, mas não sincronizou com a API: {sincronizacao.Mensagem}");
            }


            // Atualiza a sessão caso o próprio usuário tenha
            // alterado seus dados.
            if (SessaoUsuario.UsuarioAtual?.Id ==
                _usuario.Id)
            {
                SessaoUsuario.UsuarioAtual =
                    _usuario;
            }


            await DisplayAlert(
                "Usuário atualizado",
                "As alterações foram salvas com sucesso.",
                "OK");


            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Erro",
                $"Não foi possível salvar as alterações.\n\n{ex.Message}",
                "OK");
        }
        finally
        {
            BtnSalvar.IsEnabled = true;
        }
    }


    private async void Excluir_Clicked(
        object sender,
        EventArgs e)
    {
        if (_usuario == null)
            return;


        // =====================================================
        // MASTER
        // =====================================================

        if (_usuario.EhMaster)
        {
            await DisplayAlert(
                "Acesso negado",
                "O ADMINISTRADOR MASTER não pode ser excluído.",
                "OK");

            return;
        }


        // =====================================================
        // ADMINISTRADOR PRECISA ESTAR LOGADO
        // =====================================================

        if (!SessaoUsuario.EhAdministrador)
        {
            await DisplayAlert(
                "Acesso negado",
                "Somente administradores podem excluir usuários.",
                "OK");

            return;
        }


        // =====================================================
        // NÃO PODE EXCLUIR A PRÓPRIA CONTA
        // =====================================================

        if (SessaoUsuario.UsuarioAtual?.Id ==
            _usuario.Id)
        {
            await DisplayAlert(
                "Acesso negado",
                "Você não pode excluir a própria conta.",
                "OK");

            return;
        }


        bool confirmar =
            await DisplayAlert(
                "Excluir usuário",
                $"Deseja realmente excluir o usuário:\n\n{_usuario.Nome}?",
                "Sim, excluir",
                "Cancelar");


        if (!confirmar)
            return;


        try
        {
            BtnExcluir.IsEnabled = false;


            await _database.ExcluirUsuario(
                _usuario.Id);


            await DisplayAlert(
                "Usuário excluído",
                "O usuário foi excluído com sucesso.",
                "OK");


            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Não foi possível excluir",
                ex.Message,
                "OK");
        }
        finally
        {
            BtnExcluir.IsEnabled = true;
        }
    }


    private async void Voltar_Clicked(
        object sender,
        EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}
