using APP_CELULAR.Services;
using System.Net.Http;

namespace APP_CELULAR;

public partial class FigurinhasStipopPage : ContentPage
{
    private readonly IdentidadeConversa _identidade;
    private readonly ApiService _api = new();
    private readonly Func<StipopFigurinha, Task> _selecionar;
    private StipopPacote? _pacoteAtual;
    private bool _exibindoBusca;
    private string _categoriaAtual = "trending";
    private int _paginaAtual;
    private int _totalPaginas = 1;
    private readonly List<StipopPacote> _pacotesCarregados = new();

    public FigurinhasStipopPage(IdentidadeConversa identidade, Func<StipopFigurinha, Task> selecionar)
    {
        InitializeComponent();
        _identidade = identidade;
        _selecionar = selecionar;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await CarregarPacotes("trending");
    }

    private async Task CarregarPacotes(string categoria)
    {
        _categoriaAtual = categoria;
        _paginaAtual = 0;
        _totalPaginas = 1;
        _pacotesCarregados.Clear();
        await CarregarProximaPagina();
    }

    private async Task CarregarProximaPagina()
    {
        await Executar(async () =>
        {
            var proxima = _paginaAtual + 1;
            var resposta = await _api.ObterPacotesStipop(new StipopListaRequest
            {
                EmpresaId = _identidade.EmpresaId, UsuarioId = _identidade.UsuarioId,
                Categoria = _categoriaAtual, PageNumber = proxima
            });
            _pacotesCarregados.AddRange(resposta.Pacotes);
            _paginaAtual = resposta.Pagina;
            _totalPaginas = resposta.TotalPaginas;
            PacotesList.ItemsSource = null;
            PacotesList.ItemsSource = _pacotesCarregados.ToList();
            StickersList.ItemsSource = null;
            ListaAcoes.IsVisible = true;
            PacotesList.IsVisible = true;
            DetalhePanel.IsVisible = false;
            _pacoteAtual = null;
            _exibindoBusca = false;
            CarregarMaisButton.IsVisible = _paginaAtual < _totalPaginas;
        });
    }

    private async void CarregarMais_Clicked(object sender, EventArgs e) => await CarregarProximaPagina();

    private async Task Executar(Func<Task> acao)
    {
        Carregando.IsVisible = Carregando.IsRunning = true;
        try { await acao(); }
        catch (Exception ex) { await DisplayAlert("Figurinhas", ex.Message, "OK"); }
        finally { Carregando.IsRunning = Carregando.IsVisible = false; }
    }

    private async void EmAlta_Clicked(object sender, EventArgs e) => await CarregarPacotes("trending");
    private async void Novos_Clicked(object sender, EventArgs e) => await CarregarPacotes("new");
    private async void Buscar_Clicked(object sender, EventArgs e) => await Buscar();
    private async void Buscar_Completed(object sender, EventArgs e) => await Buscar();

    private async Task Buscar()
    {
        var termo = BuscaEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(termo)) return;
        await Executar(async () =>
        {
            var resposta = await _api.BuscarFigurinhasStipop(new StipopBuscaRequest
            {
                EmpresaId = _identidade.EmpresaId, UsuarioId = _identidade.UsuarioId, Termo = termo
            });
            PacoteTitulo.Text = $"Resultados: {termo}";
            ArtistaLabel.Text = "Toque em uma figurinha para enviar";
            _pacoteAtual = null;
            _exibindoBusca = true;
            StickersList.ItemsSource = resposta.Figurinhas;
            ListaAcoes.IsVisible = PacotesList.IsVisible = false;
            DetalhePanel.IsVisible = true;
            CarregarMaisButton.IsVisible = false;
        });
    }

    private async void AbrirPacote_Clicked(object sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not StipopPacote pacote) return;
        await Executar(async () =>
        {
            var resposta = await _api.ObterPacoteStipop(new StipopPacoteRequest
            {
                EmpresaId = _identidade.EmpresaId, UsuarioId = _identidade.UsuarioId, PackageId = pacote.PackageId
            });
            _pacoteAtual = resposta.Pacote;
            _exibindoBusca = false;
            PacoteTitulo.Text = _pacoteAtual.PackageName;
            ArtistaLabel.Text = $"Ilustrações de {_pacoteAtual.ArtistName}";
            StickersList.ItemsSource = _pacoteAtual.Stickers;
            ListaAcoes.IsVisible = PacotesList.IsVisible = false;
            DetalhePanel.IsVisible = true;
            CarregarMaisButton.IsVisible = false;
        });
    }

    private async void EnviarFigurinha_Tapped(object sender, TappedEventArgs e)
    {
        if ((sender as Element)?.BindingContext is not StipopFigurinha figurinha) return;
        try
        {
            if (figurinha.PackageId > 0)
                await _api.RegistrarDownloadPacoteStipop(new StipopPacoteRequest
                {
                    EmpresaId = _identidade.EmpresaId, UsuarioId = _identidade.UsuarioId,
                    PackageId = figurinha.PackageId
                });
            await _selecionar(figurinha);
            await Navigation.PopAsync();
        }
        catch (Exception ex) { await DisplayAlert("Envio da figurinha", ex.Message, "OK"); }
    }

    private async void Voltar_Clicked(object sender, EventArgs e)
    {
        if (_pacoteAtual is not null || _exibindoBusca) await CarregarPacotes("trending");
        else await Navigation.PopAsync();
    }
}
