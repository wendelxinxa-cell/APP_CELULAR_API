using APP_CELULAR.Services;

namespace APP_CELULAR;

public partial class EmpresasPage : ContentPage
{
    private readonly MasterEmpresasApiService _api = new();
    private string? _tokenMaster;

    public EmpresasPage() => InitializeComponent();

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!SessaoUsuario.EhMaster && !SessaoUsuario.EhDeus)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await DisplayAlert("Acesso restrito", "Somente o Administrador Master ou o usuário Zeus podem configurar empresas.", "OK");
                await Navigation.PopAsync();
            });
            return;
        }

        if (SessaoUsuario.EhDeus && _tokenMaster is null)
        {
            LoginPanel.IsVisible = false;
            CadastroPanel.IsVisible = true;
            try
            {
                _tokenMaster = SessaoUsuario.TokenMaster ?? throw new InvalidOperationException("Entre novamente como Administrador Master.");
                await CarregarEmpresas();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Acesso Zeus", ex.Message, "OK");
            }
        }
    }

    private async void EntrarMaster_Clicked(object sender, EventArgs e)
    {
        EntrarMasterButton.IsEnabled = false;
        try
        {
            _tokenMaster = await _api.Login(MasterUsuarioEntry.Text?.Trim() ?? "", MasterSenhaEntry.Text ?? "");
            MasterSenhaEntry.Text = "";
            LoginPanel.IsVisible = false;
            CadastroPanel.IsVisible = true;
            await CarregarEmpresas();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Acesso Master", ex.Message, "OK");
        }
        finally { EntrarMasterButton.IsEnabled = true; }
    }

    private async Task CarregarEmpresas()
    {
        if (_tokenMaster is null) return;
        try
        {
            var empresas = await _api.Listar(_tokenMaster);
            EmpresasList.ItemsSource = empresas.Select(x => new EmpresaLinha(x)).ToList();
        }
        catch (Exception ex) { await DisplayAlert("Empresas", ex.Message, "OK"); }
    }

    private async void Cadastrar_Clicked(object sender, EventArgs e)
    {
        if (_tokenMaster is null) return;
        CadastrarButton.IsEnabled = false;
        try
        {
            var empresa = await _api.Cadastrar(
                _tokenMaster,
                NomeEmpresaEntry.Text?.Trim() ?? "",
                EmailResponsavelEntry.Text?.Trim() ?? "",
                ConexaoSupabaseEntry.Text?.Trim() ?? "");
            NomeEmpresaEntry.Text = "";
            EmailResponsavelEntry.Text = "";
            ConexaoSupabaseEntry.Text = "";
            await CarregarEmpresas();
            await DisplayAlert("Aprovação solicitada", $"{empresa.Nome} ficou pendente. Enviamos uma notificação ao e-mail do Administrador Master.", "OK");
        }
        catch (Exception ex) { await DisplayAlert("Cadastro da empresa", ex.Message, "OK"); }
        finally { CadastrarButton.IsEnabled = true; }
    }

    private async void AprovarEmpresa_Clicked(object sender, EventArgs e)
    {
        if (_tokenMaster is null || sender is not Button { CommandParameter: long empresaId }) return;
        try
        {
            await _api.Aprovar(_tokenMaster, empresaId);
            await CarregarEmpresas();
            await DisplayAlert("Empresa aprovada", "O responsável foi avisado por e-mail. Código da empresa: " + empresaId, "OK");
        }
        catch (Exception ex) { await DisplayAlert("Aprovação", ex.Message, "OK"); }
    }

    private void SairMaster_Clicked(object sender, EventArgs e)
    {
        _tokenMaster = null;
        CadastroPanel.IsVisible = false;
        LoginPanel.IsVisible = true;
        EmpresasList.ItemsSource = null;
        MasterSenhaEntry.Text = "";
    }

    private sealed class EmpresaLinha
    {
        public string Nome { get; }
        public string Codigo { get; }
        public string EmailResponsavel { get; }
        public string Status { get; }
        public bool Pendente { get; }
        public EmpresaLinha(EmpresaCatalogoItem empresa)
        {
            Nome = empresa.Nome;
            Codigo = $"Código {empresa.Id}";
            EmailResponsavel = empresa.EmailResponsavel;
            Status = empresa.Status == "ATIVA" ? "Ativa" : "Aguardando aprovação do Master";
            Pendente = empresa.Status == "PENDENTE";
        }
    }
}
