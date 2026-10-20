using System.Text.Json;
using Microsoft.Maui.Storage;
using APP_CELULAR.Models;
using APP_CELULAR.Services;
using APP_CELULAR.Data;
using System;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;

namespace APP_CELULAR;

public partial class ConversasPage : ContentPage
{
    private readonly ApiService _api = new();
    private readonly AppDatabase _database = new();
    private List<ContatoConversa> _contatosEmpresa = new();
    private Usuario Usuario => SessaoUsuario.UsuarioAtual!;

    public ConversasPage()
    {
        InitializeComponent();
        UsuarioLogadoLabel.Text = $"Conectado como {Usuario.Nome} · Empresa {Usuario.EmpresaId}";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Atualiza a largura da tela para o recurso usado pelo estilo do SwipeItem
        UpdateScreenWidthResource();
        DeviceDisplay.MainDisplayInfoChanged += OnMainDisplayInfoChanged;

        await CarregarContatos();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        DeviceDisplay.MainDisplayInfoChanged -= OnMainDisplayInfoChanged;
    }

    private void UpdateScreenWidthResource()
    {
        try
        {
            var info = DeviceDisplay.MainDisplayInfo;
            // converter pixels físicos para DIP
            var widthDip = info.Width / info.Density;
            // armazena no dicionário de recursos da página (DynamicResource usado no XAML)
            Resources["ScreenWidth"] = widthDip;
        }
        catch
        {
            // fallback: não quebrar UI se algo falhar
            Resources["ScreenWidth"] = 360;
        }
    }

    private void OnMainDisplayInfoChanged(object? sender, DisplayInfoChangedEventArgs e) => UpdateScreenWidthResource();

    private async Task CarregarContatos()
    {
        Carregando.IsVisible = true;
        try
        {
            var contatos = await _api.ObterContatos(Identidade());
            _contatosEmpresa = contatos;
            SalvarContatosCache(contatos);
            var ocultas = ObterConversasOcultas();
            var pendentes = await _database.ObterMensagensPendentes(Usuario.EmpresaId, Identidade().UsuarioId);
            var contatosComEnvioLocal = pendentes.Select(x => x.ContatoId).ToHashSet();
            var lista = contatos
                .Where(c => c.DataUltimaMensagem.HasValue || contatosComEnvioLocal.Contains(c.Id))
                .Where(c => !ocultas.Contains(c.Id))
                .Select(x => new ContatoLinha(x, pendentes
                    .Where(p => p.ContatoId == x.Id)
                    .OrderByDescending(p => p.CriadaEm)
                    .Select(p => string.IsNullOrWhiteSpace(p.Texto) ? "[Foto pendente]" : p.Texto)
                    .FirstOrDefault()))
                .ToList();
            ContatosList.ItemsSource = lista;
        }
        catch (Exception ex)
        {
            _contatosEmpresa = ObterContatosCache();
            if (_contatosEmpresa.Count == 0)
            {
                var locais = await _database.ObterUsuarios();
                _contatosEmpresa = locais
                    .Where(x => x.IdServidor.HasValue && x.IdServidor.Value != Identidade().UsuarioId && !x.Excluido)
                    .Select(x => new ContatoConversa
                    {
                        Id = x.IdServidor!.Value, Nome = x.Nome, NomeOriginal = x.Nome
                    })
                    .ToList();
            }

            var pendentesOffline = await _database.ObterMensagensPendentes(Usuario.EmpresaId, Identidade().UsuarioId);
            var ocultasOffline = ObterConversasOcultas();
            ContatosList.ItemsSource = _contatosEmpresa
                .Where(x => !ocultasOffline.Contains(x.Id) || pendentesOffline.Any(p => p.ContatoId == x.Id))
                .Select(x => new ContatoLinha(x, pendentesOffline.Where(p => p.ContatoId == x.Id)
                    .OrderByDescending(p => p.CriadaEm)
                    .Select(p => string.IsNullOrWhiteSpace(p.Texto) ? "[Foto pendente]" : p.Texto)
                    .FirstOrDefault()))
                .ToList();
            System.Diagnostics.Debug.WriteLine($"Chat offline: {ex.Message}");
        }
        finally { Carregando.IsVisible = false; }
    }

    private async void NovoChat_Clicked(object sender, EventArgs e)
    {
        var ocultas = ObterConversasOcultas();
        var contatosComConversa = _contatosEmpresa
            .Where(x => x.DataUltimaMensagem.HasValue)
            .Select(x => x.Id)
            .ToHashSet();
        if (_contatosEmpresa.Count == 0)
        {
            await CarregarContatos();
            contatosComConversa = _contatosEmpresa.Where(x => x.DataUltimaMensagem.HasValue).Select(x => x.Id).ToHashSet();
        }

        var candidatos = _contatosEmpresa
            .Where(x => !contatosComConversa.Contains(x.Id) || ocultas.Contains(x.Id))
            .ToList();
        await AbrirSeletorUsuarios("Novo chat", "Escolha alguém para iniciar uma conversa.", candidatos);
    }

    private async void ListarUsuarios_Clicked(object sender, EventArgs e)
    {
        if (_contatosEmpresa.Count == 0)
            await CarregarContatos();

        await AbrirSeletorUsuarios("Usuários da empresa", "Toque em um usuário para abrir a conversa.", _contatosEmpresa);
    }

    private async Task AbrirSeletorUsuarios(string titulo, string descricao, List<ContatoConversa> contatos)
    {
        if (contatos.Count == 0)
        {
            await DisplayAlert(titulo, "Não há usuários disponíveis para esta empresa.", "OK");
            return;
        }

        var pagina = new SeletorUsuariosChatPage(titulo, descricao, contatos, async contato =>
        {
            var ocultas = ObterConversasOcultas();
            if (ocultas.Remove(contato.Id)) SalvarConversasOcultas(ocultas);
            await Navigation.PushAsync(new ChatPage(Identidade(), contato));
        });
        await Navigation.PushModalAsync(pagina);
    }

    private async void Contato_Tapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not ContatoLinha contato) return;
        await Navigation.PushAsync(new ChatPage(Identidade(), contato.Contato));
    }

    private async void DefinirApelido_Clicked(object sender, EventArgs e)
    {
        var contato = sender switch
        {
            SwipeItem { CommandParameter: ContatoLinha linha } => linha,
            Button { CommandParameter: ContatoLinha linha } => linha,
            _ => null
        };
        if (contato is null) return;

        var apelidoAtual = string.Equals(contato.Nome, contato.NomeOriginal, StringComparison.OrdinalIgnoreCase)
            ? ""
            : contato.Nome;
        var apelido = await DisplayPromptAsync(
            "Apelido do contato no chat",
            "Esse nome será particular e só aparecerá para você. Deixe vazio para restaurar o nome cadastrado.",
            "Salvar",
            "Cancelar",
            placeholder: contato.NomeOriginal,
            maxLength: 60,
            initialValue: apelidoAtual);
        if (apelido is null) return;

        try
        {
            await _api.AtualizarApelido(new AtualizarApelidoConversa
            {
                EmpresaId = Identidade().EmpresaId,
                UsuarioId = Identidade().UsuarioId,
                ContatoId = contato.Contato.Id,
                Apelido = apelido
            });
            await CarregarContatos();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Apelido do chat", ex.Message, "OK");
        }
    }

    private async void ApagarParaMim_Clicked(object sender, EventArgs e)
    {
        var contato = sender switch
        {
            SwipeItem { CommandParameter: ContatoLinha linha } => linha,
            _ => null
        };
        if (contato is null) return;

        bool confirma = await DisplayAlert("Apagar", "Apagar esta conversa apenas para você?", "Sim", "Não");
        if (!confirma) return;

        try
        {
            var ocultas = ObterConversasOcultas();
            if (!ocultas.Contains(contato.Contato.Id))
            {
                ocultas.Add(contato.Contato.Id);
                SalvarConversasOcultas(ocultas);
            }

            if (ContatosList.ItemsSource is IEnumerable<ContatoLinha> existente)
            {
                var nova = existente.Where(x => x.Contato.Id != contato.Contato.Id).ToList();
                ContatosList.ItemsSource = nova;
            }
            else
            {
                await CarregarContatos();
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erro", $"Não foi possível apagar a conversa: {ex.Message}", "OK");
        }
    }

    private async void ExcluirConversa_Clicked(object sender, EventArgs e)
    {
        // Obter o contato a partir do CommandParameter do SwipeItem (EventArgs não tem Parameter)
        var contato = (sender as SwipeItem)?.CommandParameter as ContatoLinha;
        if (contato is null) return;

        bool confirma = await DisplayAlert("Excluir conversa", $"Excluir conversa com {contato.Nome}?", "Sim", "Não");
        if (!confirma) return;

        try
        {
            if (ContatosList.ItemsSource is IEnumerable<ContatoLinha> existente)
            {
                var nova = existente.Where(x => x.Contato.Id != contato.Contato.Id).ToList();
                ContatosList.ItemsSource = nova;
            }
            else
            {
                await CarregarContatos();
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erro", $"Não foi possível excluir a conversa: {ex.Message}", "OK");
        }
    }

    private IdentidadeConversa Identidade() => new()
    {
        EmpresaId = Usuario.EmpresaId,
        UsuarioId = Usuario.IdServidor!.Value
    };

    private string ConversasOcultasKey() => $"chat_hidden_{Usuario.Id}";

    private List<long> ObterConversasOcultas()
    {
        try
        {
            var json = Preferences.Default.Get(ConversasOcultasKey(), string.Empty);
            if (string.IsNullOrWhiteSpace(json)) return new List<long>();
            return JsonSerializer.Deserialize<List<long>>(json) ?? new List<long>();
        }
        catch
        {
            return new List<long>();
        }
    }

    private void SalvarConversasOcultas(List<long> ids)
    {
        try
        {
            var json = JsonSerializer.Serialize(ids);
            Preferences.Default.Set(ConversasOcultasKey(), json);
        }
        catch
        {
            // silencioso
        }
    }

    private string ContatosCacheKey() => $"chat_contacts_{Usuario.EmpresaId}_{Identidade().UsuarioId}";

    private void SalvarContatosCache(List<ContatoConversa> contatos)
    {
        try { Preferences.Default.Set(ContatosCacheKey(), JsonSerializer.Serialize(contatos)); }
        catch { }
    }

    private List<ContatoConversa> ObterContatosCache()
    {
        try
        {
            var json = Preferences.Default.Get(ContatosCacheKey(), string.Empty);
            return string.IsNullOrWhiteSpace(json)
                ? new List<ContatoConversa>()
                : JsonSerializer.Deserialize<List<ContatoConversa>>(json) ?? new List<ContatoConversa>();
        }
        catch { return new List<ContatoConversa>(); }
    }
}

public class ContatoLinha
{
    public ContatoConversa Contato { get; }
    public string Nome => Contato.Nome;
    public string NomeOriginal => Contato.NomeOriginal;
    public string UltimaMensagem { get; }
    public int NaoLidas => Contato.NaoLidas;
    public bool TemNaoLidas => NaoLidas > 0;
    public ContatoLinha(ContatoConversa contato, string? mensagemPendente = null)
    {
        Contato = contato;
        UltimaMensagem = !string.IsNullOrWhiteSpace(mensagemPendente)
            ? mensagemPendente
            : string.IsNullOrWhiteSpace(contato.UltimaMensagem) ? "Conversa iniciada" : contato.UltimaMensagem;
    }
}

internal sealed class SeletorUsuariosChatPage : ContentPage
{
    private readonly List<ContatoConversa> _contatos;
    private readonly CollectionView _lista;
    private readonly Func<ContatoConversa, Task> _selecionar;

    public SeletorUsuariosChatPage(string titulo, string descricao, List<ContatoConversa> contatos, Func<ContatoConversa, Task> selecionar)
    {
        _contatos = contatos;
        _selecionar = selecionar;
        Title = titulo;
        BackgroundColor = PaletasAppService.CorAtual("BrandBackground", Color.FromArgb("#F5F6FA"));

        var fechar = new Button
        {
            Text = "Fechar",
            TextColor = PaletasAppService.CorAtual("BrandPrimary", Color.FromArgb("#2E2A6A")),
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.End,
            Padding = new Thickness(12, 6)
        };
        fechar.Clicked += async (_, _) => await Navigation.PopModalAsync();

        var busca = new SearchBar
        {
            Placeholder = "Buscar usuário",
            BackgroundColor = PaletasAppService.CorAtual("BrandSurface", Colors.White),
            Margin = new Thickness(0, 8)
        };
        _lista = new CollectionView { SelectionMode = SelectionMode.None, ItemSizingStrategy = ItemSizingStrategy.MeasureAllItems };
        _lista.EmptyView = new Label
        {
            Text = "Nenhum usuário encontrado.",
            TextColor = PaletasAppService.CorAtual("BrandMuted", Color.FromArgb("#777777")),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        _lista.ItemTemplate = new DataTemplate(() =>
        {
            var nome = new Label { FontSize = 16, FontAttributes = FontAttributes.Bold, TextColor = PaletasAppService.CorAtual("BrandText", Color.FromArgb("#292929")) };
            nome.SetBinding(Label.TextProperty, nameof(ContatoConversa.Nome));
            var dica = new Label { FontSize = 12, TextColor = PaletasAppService.CorAtual("BrandMuted", Color.FromArgb("#777777")) };
            dica.SetBinding(Label.TextProperty, new Binding(nameof(ContatoConversa.DataUltimaMensagem), converter: new EstadoConversaConverter()));
            var conteudo = new VerticalStackLayout { Spacing = 3, Children = { nome, dica } };
            var cartao = new Border
            {
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 9),
                BackgroundColor = PaletasAppService.CorAtual("BrandSurface", Colors.White),
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 15 },
                Content = conteudo
            };
            var toque = new TapGestureRecognizer();
            toque.SetBinding(TapGestureRecognizer.CommandParameterProperty, new Binding("."));
            toque.Tapped += Usuario_Tapped;
            cartao.GestureRecognizers.Add(toque);
            return cartao;
        });
        busca.TextChanged += (_, args) => AtualizarLista(args.NewTextValue);
        AtualizarLista("");

        var cabecalho = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        cabecalho.Children.Add(new Label { Text = titulo, FontSize = 24, FontAttributes = FontAttributes.Bold, TextColor = PaletasAppService.CorAtual("BrandPrimary", Color.FromArgb("#2E2A6A")), VerticalOptions = LayoutOptions.Center });
        cabecalho.Children.Add(fechar);
        Grid.SetColumn(fechar, 1);

        var descricaoLabel = new Label { Text = descricao, FontSize = 13, TextColor = PaletasAppService.CorAtual("BrandMuted", Color.FromArgb("#68708A")) };
        var grade = new Grid
        {
            Padding = new Thickness(18, 12),
            RowSpacing = 6,
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) }
        };
        grade.Children.Add(cabecalho);
        grade.Children.Add(descricaoLabel);
        grade.Children.Add(busca);
        grade.Children.Add(_lista);
        Grid.SetRow(cabecalho, 0);
        Grid.SetRow(descricaoLabel, 1);
        Grid.SetRow(busca, 2);
        Grid.SetRow(_lista, 3);
        Content = grade;
    }

    private void AtualizarLista(string? texto)
    {
        var filtro = texto?.Trim() ?? "";
        _lista.ItemsSource = _contatos
            .Where(x => filtro.Length == 0 || x.Nome.Contains(filtro, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(x => x.Nome)
            .ToList();
    }

    private async void Usuario_Tapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not ContatoConversa contato) return;
        await Navigation.PopModalAsync();
        await _selecionar(contato);
    }

    private sealed class EstadoConversaConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value is DateTime ? "Conversa iniciada · toque para abrir" : "Sem conversa · toque para iniciar";
        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
}
