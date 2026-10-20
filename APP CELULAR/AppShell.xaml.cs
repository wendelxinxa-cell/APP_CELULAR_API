using Microsoft.Maui.Controls.Shapes;

namespace APP_CELULAR
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
            ConfigurarTituloComUsuario();

            Routing.RegisterRoute(
                nameof(MateriaisPage),
                typeof(MateriaisPage));

            Routing.RegisterRoute(
                nameof(PessoasPage),
                typeof(PessoasPage));

            Routing.RegisterRoute(
                nameof(ComprasPage),
                typeof(ComprasPage));

            Routing.RegisterRoute(
              nameof(NovaCompraPage),
              typeof(NovaCompraPage));

            Routing.RegisterRoute(
               nameof(DetalhesCompraPage),
               typeof(DetalhesCompraPage));

            Routing.RegisterRoute(
                nameof(VendasPage),
                typeof(VendasPage));

            Routing.RegisterRoute(
                nameof(NovaVendaPage),
                typeof(NovaVendaPage));

            Routing.RegisterRoute(
                nameof(DetalhesVendaPage),
                typeof(DetalhesVendaPage));

            Routing.RegisterRoute(
                nameof(UsuariosPage),
                typeof(UsuariosPage));

            Routing.RegisterRoute(
                nameof(NovoUsuarioPage),
                typeof(NovoUsuarioPage));

            Routing.RegisterRoute(
                nameof(DetalhaUsuariosPage),
                typeof(DetalhaUsuariosPage));

            Routing.RegisterRoute(nameof(ConversasPage), typeof(ConversasPage));
            Routing.RegisterRoute(nameof(ChatPage), typeof(ChatPage));
            Routing.RegisterRoute(nameof(EmpresasPage), typeof(EmpresasPage));

        }

        private void ConfigurarTituloComUsuario()
        {
            var usuario = SessaoUsuario.UsuarioAtual;
            if (usuario is null) return;

            var tituloPagina = new Label
            {
                FontSize = 15,
                FontAttributes = FontAttributes.Bold,
                TextColor = PaletasAppService.CorAtual("BrandOnPrimary", Colors.White),
                VerticalOptions = LayoutOptions.Center,
                LineBreakMode = LineBreakMode.TailTruncation,
                MaxLines = 1
            };
            tituloPagina.SetBinding(Label.TextProperty, new Binding("CurrentPage.Title", source: this));

            var iniciais = string.Join("", usuario.Nome
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Take(2)
                .Select(p => char.ToUpperInvariant(p[0])));
            var avatar = new Border
            {
                WidthRequest = 30,
                HeightRequest = 30,
                Padding = 0,
                BackgroundColor = PaletasAppService.CorAtual("BrandSoft", Color.FromArgb("#EEEAFE")),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 15 },
                Content = new Label
                {
                    Text = iniciais,
                    FontSize = 11,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = PaletasAppService.CorAtual("BrandPrimary", Color.FromArgb("#2E2A6A")),
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                }
            };

            var nomeUsuario = new Label
            {
                Text = usuario.Nome,
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                TextColor = PaletasAppService.CorAtual("BrandOnPrimary", Colors.White),
                LineBreakMode = LineBreakMode.TailTruncation,
                MaxLines = 1,
                WidthRequest = 112
            };
            var estado = new Label
            {
                Text = "LOGADO",
                FontSize = 8,
                CharacterSpacing = 0.6,
                TextColor = PaletasAppService.CorAtual("BrandSoft", Color.FromArgb("#EEEAFE"))
            };
            var dadosUsuario = new VerticalStackLayout
            {
                Spacing = 0,
                VerticalOptions = LayoutOptions.Center,
                Children = { estado, nomeUsuario }
            };
            var chipUsuario = new Border
            {
                Padding = new Thickness(7, 4),
                BackgroundColor = PaletasAppService.CorAtual("BrandPrimary", Color.FromArgb("#5B4BBA")),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 18 },
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Center,
                Content = new HorizontalStackLayout
                {
                    Spacing = 7,
                    Children = { avatar, dadosUsuario }
                }
            };

            var titleView = new Grid
            {
                HeightRequest = 44,
                ColumnSpacing = 8,
                HorizontalOptions = LayoutOptions.FillAndExpand
            };
            titleView.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            titleView.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            titleView.Children.Add(tituloPagina);
            Grid.SetColumn(chipUsuario, 1);
            titleView.Children.Add(chipUsuario);
            Shell.SetTitleView(this, titleView);
        }
    }
}
