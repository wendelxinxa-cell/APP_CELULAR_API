using APP_CELULAR.Services;

namespace APP_CELULAR;

public partial class ChatZeusPage : ContentPage
{
    private readonly MasterEmpresasApiService _api = new();
    private string? _token;

    public ChatZeusPage() => InitializeComponent();

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await CarregarChats();
    }

    private async Task CarregarChats()
    {
        try
        {
            _token = SessaoUsuario.TokenMaster ?? throw new InvalidOperationException("Entre novamente como Administrador Master.");
            var chats = await _api.ObterConversasChat(_token);
            ChatsList.ItemsSource = chats.Select(x => new ChatZeusLinha(x)).ToList();
            int pendentes = chats.Sum(x => x.NaoLidas);
            SubtituloLabel.Text = pendentes > 0
                ? $"{chats.Count} chats · {pendentes} mensagens não lidas"
                : $"{chats.Count} chats das empresas";
        }
        catch (Exception ex)
        {
            await DisplayAlert("Chat", ex.Message, "OK");
        }
    }

    private async void ChatSelecionado(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not ChatZeusLinha linha) return;
        await Navigation.PushAsync(new ChatZeusDetalhePage(_api, _token!, linha.Chat));
    }
}

internal sealed class ChatZeusLinha
{
    public MasterChatConversa Chat { get; }
    public string Titulo => $"{Chat.Usuario}  ↔  {Chat.Contato}";
    public string EmpresaExibida => $"{Chat.Empresa} · código {Chat.EmpresaId}";
    public string UltimaMensagem => string.IsNullOrWhiteSpace(Chat.UltimaMensagem) ? "[Foto]" : Chat.UltimaMensagem;
    public int NaoLidas => Chat.NaoLidas;
    public bool TemNaoLidas => NaoLidas > 0;

    public ChatZeusLinha(MasterChatConversa chat) => Chat = chat;
}

internal sealed class ChatZeusDetalhePage : ContentPage
{
    private readonly MasterEmpresasApiService _api;
    private readonly string _token;
    private readonly MasterChatConversa _chat;
    private readonly CollectionView _mensagens = new() { SelectionMode = SelectionMode.None };

    public ChatZeusDetalhePage(MasterEmpresasApiService api, string token, MasterChatConversa chat)
    {
        _api = api;
        _token = token;
        _chat = chat;
        Title = "Chat";
        BackgroundColor = PaletasAppService.CorAtual("BrandBackground", Color.FromArgb("#F5F5F7"));
        ToolbarItems.Add(new ToolbarItem("Atualizar", null, async () => await CarregarMensagens()));
        _mensagens.ItemTemplate = new DataTemplate(() =>
        {
            var remetente = new Label { FontAttributes = FontAttributes.Bold, FontSize = 12, TextColor = PaletasAppService.CorAtual("BrandPrimary", Color.FromArgb("#2E2A6A")) };
            remetente.SetBinding(Label.TextProperty, nameof(ChatZeusMensagemLinha.Remetente));
            var texto = new Label { FontSize = 15, TextColor = PaletasAppService.CorAtual("BrandText", Color.FromArgb("#222")), LineBreakMode = LineBreakMode.WordWrap };
            texto.SetBinding(Label.TextProperty, nameof(ChatZeusMensagemLinha.Texto));
            var foto = new Image { HeightRequest = 180, Aspect = Aspect.AspectFit };
            foto.SetBinding(Image.SourceProperty, nameof(ChatZeusMensagemLinha.Foto));
            foto.SetBinding(VisualElement.IsVisibleProperty, nameof(ChatZeusMensagemLinha.TemFoto));
            var data = new Label { FontSize = 10, TextColor = PaletasAppService.CorAtual("BrandMuted", Color.FromArgb("#777")), HorizontalOptions = LayoutOptions.End };
            data.SetBinding(Label.TextProperty, nameof(ChatZeusMensagemLinha.Data));
            var conteudo = new VerticalStackLayout { Spacing = 5, Children = { remetente, texto, foto, data } };
            return new Border
            {
                Margin = new Thickness(12, 4),
                Padding = new Thickness(12),
                BackgroundColor = PaletasAppService.CorAtual("BrandSurface", Colors.White),
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(12) },
                Content = conteudo
            };
        });

        var layout = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) } };
        var cabecalho = new VerticalStackLayout
        {
            Padding = new Thickness(16, 10),
            Spacing = 3,
            BackgroundColor = PaletasAppService.CorAtual("BrandPrimary", Color.FromArgb("#2E2A6A")),
            Children =
            {
                new Label { Text = $"{chat.Usuario}  ↔  {chat.Contato}", FontAttributes = FontAttributes.Bold, FontSize = 16, TextColor = Colors.White },
                new Label { Text = $"{chat.Empresa} · código {chat.EmpresaId}", FontSize = 13, TextColor = PaletasAppService.CorAtual("BrandOnPrimary", Color.FromArgb("#D9D7F5")) }
            }
        };
        layout.Add(cabecalho, 0, 0);
        layout.Add(_mensagens, 0, 1);
        Content = layout;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await CarregarMensagens();
    }

    private async Task CarregarMensagens()
    {
        try
        {
            var mensagens = await _api.ObterMensagensChat(_token, _chat);
            _mensagens.ItemsSource = mensagens.Select(x => new ChatZeusMensagemLinha(x)).ToList();
            if (mensagens.Count > 0)
                await _api.MarcarChatComoLido(_token, _chat);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Chat", ex.Message, "OK");
        }
    }
}

internal sealed class ChatZeusMensagemLinha
{
    public string Remetente { get; }
    public string Texto { get; }
    public string Data { get; }
    public ImageSource? Foto { get; }
    public bool TemFoto => Foto is not null;

    public ChatZeusMensagemLinha(MensagemConversa mensagem)
    {
        Remetente = mensagem.RemetenteNome;
        Texto = string.IsNullOrWhiteSpace(mensagem.Texto) ? "" : mensagem.Texto;
        Data = mensagem.EnviadaEm.ToLocalTime().ToString("dd/MM HH:mm");
        if (!string.IsNullOrWhiteSpace(mensagem.FotoUrl) && (mensagem.MidiaTipo ?? mensagem.FotoTipo ?? "image/").StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            Foto = ImageSource.FromUri(new Uri(mensagem.FotoUrl));
        else if (mensagem.Foto is { Length: > 0 })
            Foto = ImageSource.FromStream(() => new MemoryStream(mensagem.Foto));
    }
}
