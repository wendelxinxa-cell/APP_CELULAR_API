using APP_CELULAR.Data;
using APP_CELULAR.Models;

namespace APP_CELULAR;

public partial class UsuariosPage : ContentPage
{
    private readonly AppDatabase _database;

    public UsuariosPage()
    {
        InitializeComponent();

        _database = new AppDatabase();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await CarregarUsuarios();
    }


    private async Task CarregarUsuarios()
    {
        try
        {
            var usuarios = await _database.ObterUsuarios();

            UsuariosCollection.ItemsSource = usuarios;

            int quantidade = usuarios.Count;

            SubtituloLabel.Text =
                quantidade == 1
                    ? "1 usuário cadastrado"
                    : $"{quantidade} usuários cadastrados";

            // Somente administradores podem criar usuários.
            BtnNovoUsuario.IsVisible =
                SessaoUsuario.EhAdministrador;
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Erro",
                $"Não foi possível carregar os usuários.\n\n{ex.Message}",
                "OK");
        }
    }


    private async void Usuario_Selecionado(
        object sender,
        SelectionChangedEventArgs e)
    {
        var usuario = e.CurrentSelection
            .FirstOrDefault() as Usuario;

        if (usuario == null)
            return;

        UsuariosCollection.SelectedItem = null;

        await Shell.Current.GoToAsync(
            $"{nameof(DetalhaUsuariosPage)}?usuarioId={usuario.Id}");
    }


    private async void NovoUsuario_Clicked(
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

        await Shell.Current.GoToAsync(
            nameof(NovoUsuarioPage));
    }


    private async void Voltar_Clicked(
        object sender,
        EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }


    private async void RefreshUsuarios_Refreshing(
        object sender,
        EventArgs e)
    {
        await CarregarUsuarios();

        RefreshUsuarios.IsRefreshing = false;
    }
}