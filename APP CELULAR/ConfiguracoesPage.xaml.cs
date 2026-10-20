namespace APP_CELULAR;

public partial class ConfiguracoesPage : ContentPage
{
    public ConfiguracoesPage()
    {
        InitializeComponent();
        MontarPaletas();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        PaletasAppService.AplicarPreferenciaAtual();
        MontarPaletas();
    }

    private void MontarPaletas()
    {
        PaletasGrid.Children.Clear();
        var paletas = PaletasAppService.ObterPaletas(PaletasAppService.ObterPreferenciaAtual());
        for (int i = 0; i < paletas.Count; i++)
        {
            var paleta = paletas[i];
            var cartao = new Border
            {
                Padding = 12, BackgroundColor = paleta.Superficie, Stroke = paleta.Borda,
                StrokeThickness = paleta.Selecionada ? 2 : 1,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
                Content = CriarConteudo(paleta)
            };
            var toque = new TapGestureRecognizer();
            toque.Tapped += (_, _) => SelecionarPaleta(paleta);
            cartao.GestureRecognizers.Add(toque);
            PaletasGrid.Add(cartao, i % 2, i / 2);
        }
    }

    private static View CriarConteudo(PaletaApp paleta)
    {
        var amostras = new HorizontalStackLayout
        {
            Spacing = 5,
            Children =
            {
                Amostra(paleta.Primaria, 34),
                Amostra(paleta.BalaoOutro, 28),
                Amostra(paleta.BalaoVoce, 28),
                Amostra(paleta.Fundo, 20),
                Amostra(paleta.Accent, 20)
            }
        };
        return new VerticalStackLayout
        {
            Spacing = 7,
            Children =
            {
                amostras,
                new Label { Text = paleta.Nome, FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = paleta.Texto },
                new Label { Text = paleta.Descricao, FontSize = 11, TextColor = paleta.TextoSecundario, LineBreakMode = LineBreakMode.WordWrap },
                new Label { Text = paleta.Indicador, FontSize = 10, FontAttributes = FontAttributes.Bold, TextColor = paleta.Primaria }
            }
        };
    }

    private static Border Amostra(Color cor, double tamanho) => new()
    {
        WidthRequest = tamanho, HeightRequest = 22, BackgroundColor = cor, StrokeThickness = 0,
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 }
    };

    private void SelecionarPaleta(PaletaApp paleta)
    {
        PaletasAppService.SalvarPreferencia(paleta.Id);
        StatusLabel.Text = $"Paleta {paleta.Nome} aplicada para este usuário.";
        MontarPaletas();
    }
}
