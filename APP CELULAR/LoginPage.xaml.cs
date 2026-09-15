namespace APP_CELULAR;

public partial class LoginPage : ContentPage
{
    public LoginPage()
    {
        InitializeComponent();
    }

    private async void OnLoginClicked(object sender, EventArgs e)
    {
        string usuario = UsernameEntry.Text?.Trim() ?? "";
        string senha = PasswordEntry.Text ?? "";

        // TESTE TEMPORÁRIO
        if (usuario == "admin" && senha == "123")
        {
            // Abre o sistema principal
            Application.Current!.Windows[0].Page = new AppShell();
        }
        else
        {
            await DisplayAlert(
                "Login",
                "Usuário ou senha inválidos.",
                "OK");
        }
    }
}