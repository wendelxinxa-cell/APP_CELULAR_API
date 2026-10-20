using APP_CELULAR.Services;

namespace APP_CELULAR;

public partial class ApiEndpointPage : ContentPage
{
    public ApiEndpointPage()
    {
        InitializeComponent();
        EnderecoEntry.Text = ApiEndpointSettings.Get()?.TrimEnd('/');
        EmpresaEntry.Text = ApiEndpointSettings.GetEmpresaId().ToString();
    }

    private async void SalvarTestar_Clicked(object sender, EventArgs e)
    {
        try
        {
            ApiEndpointSettings.Set(EnderecoEntry.Text ?? "");
            if (!long.TryParse(EmpresaEntry.Text, out var empresaId) || empresaId <= 0)
                throw new ArgumentException("Informe o código da empresa configurado na API.");
            ApiEndpointSettings.SetEmpresaId(empresaId);
            var resposta = await new ApiService().TestarConexao();
            await DisplayAlert("API conectada", "O endereço foi salvo neste dispositivo e a API respondeu normalmente.", "OK");
            await Navigation.PopModalAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Não foi possível conectar", ex.Message, "OK");
        }
    }

    private async void Voltar_Clicked(object sender, EventArgs e) => await Navigation.PopModalAsync();
}
