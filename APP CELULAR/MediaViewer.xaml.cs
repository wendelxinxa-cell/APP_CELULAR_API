using System.Text.RegularExpressions;

namespace APP_CELULAR;

public partial class MediaViewer : ContentPage
{
    private readonly byte[] _bytes;
    private readonly string _mime;

    public MediaViewer(byte[] bytes, string mime, string nome)
    {
        InitializeComponent();
        _bytes = bytes;
        _mime = mime;
        Title = nome;
        Shell.SetNavBarIsVisible(this, false);
        MostrarConteudo();
    }

    private void MostrarConteudo()
    {
        if (_mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            FotoViewer.Source = ImageSource.FromStream(() => new MemoryStream(_bytes));
            FotoViewer.IsVisible = true;
            return;
        }

        if (_mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            var tipoSeguro = Regex.IsMatch(_mime, @"^video/[a-zA-Z0-9.+-]+$") ? _mime : "video/mp4";
            var dados = Convert.ToBase64String(_bytes);
            VideoViewer.Source = new HtmlWebViewSource
            {
                Html = $"""
                    <!doctype html>
                    <html><head><meta name="viewport" content="width=device-width, initial-scale=1, maximum-scale=1"></head>
                    <body style="margin:0;background:#000;width:100vw;height:100vh;display:flex;align-items:center;justify-content:center;overflow:hidden">
                    <video controls autoplay playsinline style="width:100%;height:100%;object-fit:contain" src="data:{tipoSeguro};base64,{dados}"></video>
                    </body></html>
                    """
            };
            VideoViewer.IsVisible = true;
            return;
        }

        DisplayAlert("Mídia", "Este formato não pode ser reproduzido.", "OK");
    }

    private async void Fechar_Clicked(object sender, EventArgs e) => await Navigation.PopAsync();

    protected override void OnDisappearing()
    {
        VideoViewer.Source = new HtmlWebViewSource { Html = string.Empty };
        base.OnDisappearing();
    }
}
