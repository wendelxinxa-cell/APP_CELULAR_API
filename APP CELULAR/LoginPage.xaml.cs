using APP_CELULAR.Data;

namespace APP_CELULAR;

public partial class LoginPage : ContentPage
{
    private readonly AppDatabase _database;

    public LoginPage()
    {
        InitializeComponent();

        _database = new AppDatabase();
    }

    private async void OnLoginClicked(
        object sender,
        EventArgs e)
    {
        string usuario =
            UsernameEntry.Text?.Trim() ?? "";

        string senha =
            PasswordEntry.Text ?? "";

        if (string.IsNullOrWhiteSpace(usuario))
        {
            await DisplayAlert(
                "Login",
                "Informe o usuário.",
                "OK");

            UsernameEntry.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(senha))
        {
            await DisplayAlert(
                "Login",
                "Informe a senha.",
                "OK");

            PasswordEntry.Focus();
            return;
        }

        try
        {
            UsuarioAtualizando(true);

            var usuarioEncontrado =
                await _database.ValidarLogin(
                    usuario,
                    senha);

            if (usuarioEncontrado == null)
            {
                await DisplayAlert(
                    "Login",
                    "Usuário ou senha inválidos.",
                    "OK");

                PasswordEntry.Text = "";
                PasswordEntry.Focus();

                return;
            }

            // Guarda o usuário logado
            SessaoUsuario.UsuarioAtual =
                usuarioEncontrado;

            // Abre o sistema
            Application.Current!.Windows[0].Page =
                new AppShell();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Erro",
                $"Não foi possível realizar o login.\n\n{ex.Message}",
                "OK");
        }
        finally
        {
            UsuarioAtualizando(false);
        }
    }

    private void UsuarioAtualizando(bool carregando)
    {
        if (carregando)
        {
            UsernameEntry.IsEnabled = false;
            PasswordEntry.IsEnabled = false;
        }
        else
        {
            UsernameEntry.IsEnabled = true;
            PasswordEntry.IsEnabled = true;
        }
    }
}