using APP_CELULAR.Services;

namespace APP_CELULAR;

public partial class EditarUsuarioZeusPage : ContentPage
{
    private readonly long _usuarioId;
    private readonly long _empresaId;
    private readonly bool _bloqueado;
    private readonly MasterEmpresasApiService _api = new();

    public EditarUsuarioZeusPage(long usuarioId, long empresaId, string nome, string tipoNegocio, string status, bool bloqueado)
    {
        InitializeComponent();
        _usuarioId = usuarioId;
        _empresaId = empresaId;
        _bloqueado = bloqueado;
        NomeLabel.Text = nome;
        StatusLabel.Text = $"{status} · empresa {empresaId}";
        TipoNegocioPicker.SelectedIndex = tipoNegocio == "chat" ? 1 : 0;
        BloqueioButton.Text = bloqueado ? "Desbloquear usuário" : "Bloquear usuário";
    }

    private async void Salvar_Clicked(object sender, EventArgs e)
    {
        if (!SessaoUsuario.EhDeus || _usuarioId <= 0 || _empresaId <= 0) return;
        try
        {
            SalvarButton.IsEnabled = false;
            var token = SessaoUsuario.TokenMaster ?? throw new InvalidOperationException("Entre novamente como Zeus.");
            string tipoNegocio = TipoNegocioPicker.SelectedIndex == 1 ? "chat" : "sucata";
            await _api.DefinirTipoNegocioUsuario(token, _empresaId, _usuarioId, tipoNegocio);
            await DisplayAlert("Usuário atualizado", "O tipo de negócio foi vinculado ao usuário.", "OK");
            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Alterar usuário", ex.Message, "OK");
        }
        finally
        {
            SalvarButton.IsEnabled = true;
        }
    }

    private async void Bloqueio_Clicked(object sender, EventArgs e)
    {
        if (!SessaoUsuario.EhDeus || _usuarioId <= 0 || _empresaId <= 0) return;
        string acao = _bloqueado ? "desbloquear" : "bloquear";
        if (!await DisplayAlert("Confirmar", $"Deseja {acao} este usuário?", "Continuar", "Cancelar")) return;
        try
        {
            BloqueioButton.IsEnabled = false;
            var token = SessaoUsuario.TokenMaster ?? throw new InvalidOperationException("Entre novamente como Zeus.");
            await _api.DefinirBloqueioUsuario(token, _empresaId, _usuarioId, !_bloqueado);
            await DisplayAlert("Acesso atualizado", $"O usuário foi {acao} com sucesso.", "OK");
            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Alterar acesso", ex.Message, "OK");
        }
        finally
        {
            BloqueioButton.IsEnabled = true;
        }
    }
}
