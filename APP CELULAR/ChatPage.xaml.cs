using APP_CELULAR.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Microsoft.Maui.Storage;
#if ANDROID
using Microsoft.Maui.Platform;
#endif

namespace APP_CELULAR;

public partial class ChatPage : ContentPage
{
    private const int TamanhoMaximoFoto = 8 * 1024 * 1024;
    private readonly ApiService _api = new();
    private readonly IdentidadeConversa _identidade;
    private readonly ContatoConversa _contato;
    private readonly ObservableCollection<MensagemLinha> _mensagens = new();
    private readonly ObservableCollection<FigurinhaLocalItem> _figurinhasLocais = new();
    private readonly APP_CELULAR.Data.AppDatabase _database = new();
    private string? _fotoNome;
    private string? _fotoTipo;
    private byte[]? _foto;
    private CancellationTokenSource? _atualizacaoConversaCts;
    private bool _carregandoMensagens;
    private bool _atualizacaoConversaSolicitada;
    private bool _historicoProcessando;
    private bool _primeiraCarga = true;
    private bool _usuarioNoFimDaConversa = true;
    private PreferenciasConversa _preferencias = new();
    private readonly HashSet<long> _figurinhasAnimadas = new();
    private DateTime _proximaConsultaAcoesEm = DateTime.MinValue;

    public ChatPage(IdentidadeConversa identidade, ContatoConversa contato)
    {
        InitializeComponent();
        _identidade = identidade;
        _contato = contato;
        Title = contato.Nome;
        ContatoLabel.Text = $"Conversa com {contato.Nome}";
        var usuario = SessaoUsuario.UsuarioAtual;
        UsuarioLogadoLabel.Text = usuario is null
            ? "Sessão não identificada"
            : $"Logado como {usuario.Nome} · Empresa {usuario.EmpresaId}";
        MensagensList.ItemsSource = _mensagens;
        FigurinhasLocaisList.ItemsSource = _figurinhasLocais;
        StatusPicker.ItemsSource = new[] { "online", "ocupado", "ausente", "invisível" };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await CarregarFigurinhasLocais();
        _atualizacaoConversaCts?.Cancel();
        _atualizacaoConversaCts?.Dispose();
        _atualizacaoConversaCts = new CancellationTokenSource();
        await CarregarPreferencias();
        await CarregarWinksPersonalizados();
        await CarregarMensagensLocais();
        await AtualizarConversa(exibirErro: true);
        _ = AcompanharConversa(_atualizacaoConversaCts.Token);
    }

    protected override void OnDisappearing()
    {
        _atualizacaoConversaCts?.Cancel();
        _ = _api.EncerrarRealtimeConversasAsync();
        base.OnDisappearing();
    }

    private async Task AcompanharConversa(CancellationToken cancellationToken)
    {
        using var temporizador = new PeriodicTimer(TimeSpan.FromSeconds(4));
        var proximaConsultaFallback = DateTime.UtcNow.AddSeconds(30);
        try
        {
            try
            {
                await _api.IniciarRealtimeConversas(async remetenteId =>
                {
                    if (remetenteId == 0 || remetenteId == _contato.Id)
                        await MainThread.InvokeOnMainThreadAsync(() => AtualizarConversa(exibirErro: false));
                }, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Realtime do chat indisponível; usando consulta periódica: {ex.Message}");
            }

            while (await temporizador.WaitForNextTickAsync(cancellationToken))
            {
                if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
                    continue;

                if (_api.ConversaRealtimeConectada && DateTime.UtcNow < proximaConsultaFallback)
                    continue;

                await MainThread.InvokeOnMainThreadAsync(() => AtualizarConversa(exibirErro: false));
                proximaConsultaFallback = DateTime.UtcNow.AddSeconds(_api.ConversaRealtimeConectada ? 30 : 4);
            }
        }
        catch (OperationCanceledException)
        {
            // A página saiu de foco; o próximo OnAppearing inicia outra atualização.
        }
    }

    private async Task AtualizarConversa(bool exibirErro)
    {
        if (_historicoProcessando)
            return;

        if (_carregandoMensagens)
        {
            // Não descarte refreshes disparados por novas mensagens enquanto o
            // ciclo atual está enviando pendências e reconstruindo a lista.
            _atualizacaoConversaSolicitada = true;
            return;
        }

        _carregandoMensagens = true;
        bool carregouDaApi = false;
        bool enviouPendente = false;
        bool primeiraCarga = _primeiraCarga;
        try
        {
            if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
            {
                enviouPendente = await TentarEnviarPendentes();
                var cacheAnterior = await _database.ObterMensagensCache(_identidade.EmpresaId, _identidade.UsuarioId, _contato.Id);
                var cursor = cacheAnterior.Where(x => x.Id > 0).Select(x => x.Id).DefaultIfEmpty(0).Max();
                var novasMensagens = await _api.ObterMensagens(Request(cursor));
                var mensagensOriginais = cacheAnterior
                    .Concat(novasMensagens)
                    .GroupBy(x => x.Id > 0 ? $"id:{x.Id}" : $"cliente:{x.ClienteMensagemId}")
                    .Select(grupo => grupo.Last())
                    .OrderBy(x => x.EnviadaEm).ThenBy(x => x.Id)
                    .ToList();
                mensagensOriginais = mensagensOriginais.Where(x => !ChatCatAcaoVisual.EhAcaoLegada(x.Texto)).ToList();
                var mensagensOcultas = await ObterChavesMensagensOcultasAsync();
                var idsVisiveis = _mensagens.Select(x => x.IdServidor).Where(x => x > 0).ToHashSet();
                bool recebeuMensagem = mensagensOriginais.Any(x => x.RemetenteId != _identidade.UsuarioId &&
                    (x.Id <= 0 || !idsVisiveis.Contains(x.Id)));
                var pendentesLocais = await _database.ObterMensagensPendentes(_identidade.EmpresaId, _identidade.UsuarioId);
                foreach (var pendenteLocal in pendentesLocais.Where(x => x.ContatoId == _contato.Id && !ChatCatAcaoVisual.EhAcaoLegada(x.Texto)))
                {
                    var chave = Guid.TryParse(pendenteLocal.Chave, out var chaveLocal) ? chaveLocal : Guid.Empty;
                    if (chave != Guid.Empty && mensagensOriginais.Any(x => x.ClienteMensagemId == chave)) continue;
                    mensagensOriginais.Add(new MensagemConversa
                    {
                        ClienteMensagemId = chave == Guid.Empty ? null : chave,
                        RemetenteId = pendenteLocal.UsuarioId,
                        RemetenteNome = "Você",
                        DestinatarioId = pendenteLocal.ContatoId,
                        Texto = pendenteLocal.Texto,
                        Foto = pendenteLocal.Foto,
                        FotoNome = pendenteLocal.FotoNome,
                        FotoTipo = pendenteLocal.FotoTipo,
                        EnviadaEm = pendenteLocal.CriadaEm,
                        PendenteLocal = true
                    });
                }
                var mensagens = mensagensOriginais.OrderBy(x => x.EnviadaEm).ThenBy(x => x.Id).ToList();
                var idsAnteriores = _mensagens.Select(x => x.IdServidor).Where(x => x > 0).ToHashSet();
                var novasFigurinhas = _primeiraCarga
                    ? new List<MensagemConversa>()
                    : mensagensOriginais.Where(x => x.RemetenteId != _identidade.UsuarioId && ChatCatFigurinha.EhFigurinha(x) &&
                        (x.Id <= 0 || (!idsAnteriores.Contains(x.Id) && _figurinhasAnimadas.Add(x.Id)))).ToList();
                if (_primeiraCarga)
                    foreach (var figurinha in mensagensOriginais.Where(ChatCatFigurinha.EhFigurinha))
                        if (figurinha.Id > 0) _figurinhasAnimadas.Add(figurinha.Id);
                if (MensagensDiferentes(mensagens, mensagensOcultas))
                {
                    await _database.SubstituirMensagensCache(_identidade.EmpresaId, _identidade.UsuarioId, _contato.Id,
                        mensagens.Where(x => !x.PendenteLocal));
                    var usuarioEstavaNoFim = _usuarioNoFimDaConversa;
                    AtualizarColecaoMensagens(mensagens, mensagensOcultas);
                    if (_mensagens.Count > 0 && (primeiraCarga || (recebeuMensagem && usuarioEstavaNoFim)))
                    {
                        _usuarioNoFimDaConversa = true;
                        RolarParaUltimaMensagem(forcar: primeiraCarga);
                    }
                }
                if (primeiraCarga || recebeuMensagem)
                    await _api.MarcarComoLidas(Request());
                _primeiraCarga = false;
                carregouDaApi = true;
                foreach (var figurinha in novasFigurinhas)
                {
                    var simbolo = ChatCatFigurinha.TentarLer(figurinha.Texto, out var emoji) ? emoji : "✨";
                    _ = ExibirAcaoVisualAsync(simbolo, eAtencao: false, enviar: false, figurinha.Foto);
                }
                if (DateTime.UtcNow >= _proximaConsultaAcoesEm)
                {
                    _proximaConsultaAcoesEm = DateTime.UtcNow.AddSeconds(12);
                    try
                    {
                        var acoes = await _api.ObterAcoesVisuais(Request());
                        foreach (var acao in acoes.Where(x => x.Simbolo != "⭐"))
                            _ = ExibirAcaoVisualAsync(acao.Simbolo, acao.Tipo == "ATTENTION", enviar: false);
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Ações visuais temporárias: {ex.Message}"); }
                }
            }
            else
            {
                await CarregarMensagensLocais();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Atualização do chat: {ex.Message}");
            if (exibirErro)
                await DisplayAlert("Chat", ex.Message, "OK");
            await CarregarMensagensLocais();

            if (carregouDaApi && enviouPendente)
                System.Diagnostics.Debug.WriteLine("Mensagem pendente enviada e conversa atualizada.");
        }
        finally
        {
            _carregandoMensagens = false;
            if (_atualizacaoConversaSolicitada)
            {
                _atualizacaoConversaSolicitada = false;
                _ = MainThread.InvokeOnMainThreadAsync(() => AtualizarConversa(exibirErro: false));
            }
        }

    }

    private bool MensagensDiferentes(IReadOnlyList<MensagemConversa> novas, HashSet<string> mensagensOcultas)
    {
        if (_mensagens.Count != novas.Count) return true;
        for (var i = 0; i < novas.Count; i++)
        {
            var atual = _mensagens[i];
            var chaveOculta = ChaveHistorico(novas[i]);
            if (!string.Equals(atual.Chave, ChaveMensagem(novas[i]), StringComparison.Ordinal) ||
                atual.Oculta != mensagensOcultas.Contains(chaveOculta) ||
                atual.PendenteLocal != novas[i].PendenteLocal ||
                !string.Equals(atual.TextoOriginal, novas[i].Texto, StringComparison.Ordinal) ||
                atual.FotoBytes?.Length != novas[i].Foto?.Length ||
                atual.DadosMensagem.LidaEm != novas[i].LidaEm)
                return true;
        }
        return false;
    }

    private async Task CarregarMensagensLocais()
    {
        var locais = await _database.ObterMensagensCache(_identidade.EmpresaId, _identidade.UsuarioId, _contato.Id);
        var pendentes = await _database.ObterMensagensPendentes(_identidade.EmpresaId, _identidade.UsuarioId);
        foreach (var acaoLegada in pendentes.Where(x => x.ContatoId == _contato.Id && ChatCatAcaoVisual.EhAcaoLegada(x.Texto)))
            await _database.RemoverMensagemPendente(acaoLegada.Chave);
        var mensagensVisiveis = locais.Where(x => !ChatCatAcaoVisual.EhAcaoLegada(x.Texto)).ToList();
        var mensagensOcultas = await ObterChavesMensagensOcultasAsync();
        foreach (var pendente in pendentes.Where(x => x.ContatoId == _contato.Id && !ChatCatAcaoVisual.EhAcaoLegada(x.Texto)))
        {
            var chave = Guid.TryParse(pendente.Chave, out var id) ? id : (Guid?)null;
            if (chave.HasValue && mensagensVisiveis.Any(x => x.ClienteMensagemId == chave)) continue;
            mensagensVisiveis.Add(new MensagemConversa
            {
                RemetenteId = pendente.UsuarioId, RemetenteNome = "Você", ClienteMensagemId = chave,
                PendenteLocal = true, DestinatarioId = pendente.ContatoId, Texto = pendente.Texto,
                Foto = pendente.Foto, FotoNome = pendente.FotoNome, FotoTipo = pendente.FotoTipo,
                EnviadaEm = pendente.CriadaEm
            });
        }
        mensagensVisiveis = mensagensVisiveis.OrderBy(x => x.EnviadaEm).ThenBy(x => x.Id).ToList();
        AtualizarColecaoMensagens(mensagensVisiveis, mensagensOcultas);
        if (_mensagens.Count > 0 && (_primeiraCarga || _usuarioNoFimDaConversa))
            RolarParaUltimaMensagem(forcar: _primeiraCarga);
    }

    private async Task<HashSet<string>> ObterChavesMensagensOcultasAsync()
    {
        var protegidas = await _database.ObterMensagensHistoricoProtegidas(_identidade.EmpresaId, _identidade.UsuarioId, _contato.Id);
        return protegidas.Where(x => x.Ocultar).Select(x => x.Chave).ToHashSet(StringComparer.Ordinal);
    }

    private string ChaveHistorico(MensagemConversa mensagem) => HistoricoCriptografia.ChaveMensagem(
        _identidade.EmpresaId, _identidade.UsuarioId, _contato.Id, mensagem.ClienteMensagemId, mensagem.Id);

    private static string ChaveMensagem(MensagemConversa mensagem) => mensagem.Id > 0
        ? $"id:{mensagem.Id}"
        : $"cliente:{mensagem.ClienteMensagemId?.ToString("N") ?? ""}";

    private void AtualizarColecaoMensagens(IReadOnlyList<MensagemConversa> novas, HashSet<string> mensagensOcultas)
    {
        for (var destino = 0; destino < novas.Count; destino++)
        {
            var mensagem = novas[destino];
            var chave = ChaveMensagem(mensagem);
            var origem = -1;
            for (var i = destino; i < _mensagens.Count; i++)
            {
                if (string.Equals(_mensagens[i].Chave, chave, StringComparison.Ordinal))
                {
                    origem = i;
                    break;
                }
            }

            if (origem < 0)
            {
                _mensagens.Insert(destino, new MensagemLinha(mensagem, _identidade.UsuarioId, _preferencias,
                    mensagensOcultas.Contains(ChaveHistorico(mensagem))));
            }
            else
            {
                if (origem != destino) _mensagens.Move(origem, destino);
                _mensagens[destino].Atualizar(mensagem, mensagensOcultas.Contains(ChaveHistorico(mensagem)));
            }
        }

        while (_mensagens.Count > novas.Count)
            _mensagens.RemoveAt(_mensagens.Count - 1);
    }

    private void DefinirHistoricoProcessando(bool processando, string? mensagem = null)
    {
        _historicoProcessando = processando;
        HistoricoOperacaoTexto.Text = mensagem ?? "Processando mensagens…";
        HistoricoOperacaoIndicador.IsVisible = processando;
        HistoricoOperacaoAtividade.IsRunning = processando;
        OcultarMensagensButton.IsEnabled = !processando;
        ExibirMensagensButton.IsEnabled = !processando;
    }

    private void RolarParaUltimaMensagem(bool forcar = false)
    {
        if (forcar) _usuarioNoFimDaConversa = true;
        Dispatcher.Dispatch(async () =>
        {
            foreach (var atraso in new[] { 80, 220, 420 })
            {
                await Task.Delay(atraso);
                if (_mensagens.Count == 0) return;
                MensagensList.ScrollTo(_mensagens[^1], position: ScrollToPosition.End, animate: false);
                _usuarioNoFimDaConversa = true;
            }
        });
    }

    private void MensagensList_Scrolled(object sender, ItemsViewScrolledEventArgs e)
    {
        if (_mensagens.Count == 0) return;
        _usuarioNoFimDaConversa = e.LastVisibleItemIndex >= _mensagens.Count - 1;
    }

    private async Task<bool> TentarEnviarPendentes()
    {
        var pendentes = await _database.ObterMensagensPendentes(_identidade.EmpresaId, _identidade.UsuarioId);
        bool enviou = false;
        foreach (var pendente in pendentes.Where(x => x.ContatoId == _contato.Id && !ChatCatAcaoVisual.EhAcaoLegada(x.Texto)))
        {
            try
            {
                await _api.EnviarMensagem(new EnviarMensagemConversa
                {
                    EmpresaId = pendente.EmpresaId,
                    UsuarioId = pendente.UsuarioId,
                    ContatoId = pendente.ContatoId,
                    ClienteMensagemId = Guid.Parse(pendente.Chave),
                    Texto = pendente.Texto,
                    Foto = pendente.Foto,
                    FotoNome = pendente.FotoNome,
                    FotoTipo = pendente.FotoTipo
                });
                await _database.RemoverMensagemPendente(pendente.Chave);
                enviou = true;
            }
            catch (Exception ex)
            {
                pendente.MensagemErro = ex.Message;
                await _database.SalvarMensagemPendente(pendente);
                System.Diagnostics.Debug.WriteLine($"Mensagem de chat ainda pendente: {ex.Message}");
                break;
            }
        }
        return enviou;
    }

    private async void Enviar_Clicked(object sender, EventArgs e)
    {
        FecharMenusConversa();
        await EnviarMensagemInterna();
    }

    private async Task EnviarMensagemInterna(string? textoFigurinha = null)
    {
        var texto = textoFigurinha ?? TextoEntry.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(texto) && _foto is null) return;
        var pendente = new MensagemPendente
        {
            Chave = Guid.NewGuid().ToString("N"),
            EmpresaId = _identidade.EmpresaId,
            UsuarioId = _identidade.UsuarioId,
            ContatoId = _contato.Id,
            Texto = texto,
            Foto = _foto,
            FotoNome = _fotoNome,
            FotoTipo = _fotoTipo
        };
        try
        {
            await _database.SalvarMensagemPendente(pendente);
            var mensagemLocal = new MensagemConversa
            {
                ClienteMensagemId = Guid.Parse(pendente.Chave),
                RemetenteId = _identidade.UsuarioId,
                RemetenteNome = "Você",
                DestinatarioId = _contato.Id,
                Texto = texto,
                Foto = _foto,
                FotoNome = _fotoNome,
                FotoTipo = _fotoTipo,
                EnviadaEm = DateTime.Now,
                PendenteLocal = true
            };
            _usuarioNoFimDaConversa = true;
            _mensagens.Add(new MensagemLinha(mensagemLocal, _identidade.UsuarioId, _preferencias));
            RolarParaUltimaMensagem(forcar: true);
            if (textoFigurinha is null)
            {
                TextoEntry.Text = "";
                _foto = null; _fotoNome = null; _fotoTipo = null;
            }
            _ = AtualizarConversa(exibirErro: false);
            if (textoFigurinha is not null && ChatCatFigurinha.TentarLer(textoFigurinha, out var emoji))
                await ExibirAcaoVisualAsync(emoji, eAtencao: false, enviar: false);
        }
        catch (Exception ex) { await DisplayAlert("Envio", ex.Message, "OK"); }
    }

    private async Task CarregarPreferencias()
    {
        _preferencias = await _database.ObterPreferenciasConversa(_identidade.EmpresaId, _identidade.UsuarioId, _contato.Id);
        StatusPicker.SelectedItem = _preferencias.StatusLocal;
        ReaplicarEstilo();
    }

    private void ReaplicarEstilo()
    {
        foreach (var mensagem in _mensagens)
            mensagem.AtualizarEstilo(_preferencias);
    }

    private void FecharMenusConversa()
    {
        FigurinhasPanel.IsVisible = false;
        WinksPanel.IsVisible = false;
        MenuDismissLayer.IsVisible = false;
    }

    private void Conversa_Tapped(object sender, TappedEventArgs e) => FecharMenusConversa();

    private void FigurinhasToggle_Clicked(object sender, EventArgs e)
    {
        var abrir = !FigurinhasPanel.IsVisible;
        FecharMenusConversa();
        FigurinhasPanel.IsVisible = abrir;
        MenuDismissLayer.IsVisible = abrir;
    }

    private void WinksToggle_Clicked(object sender, EventArgs e)
    {
        var abrir = !WinksPanel.IsVisible;
        FecharMenusConversa();
        WinksPanel.IsVisible = abrir;
        MenuDismissLayer.IsVisible = abrir;
    }

    private async void CriptografarHistorico_Clicked(object sender, EventArgs e)
    {
        FecharMenusConversa();
        var todosRegistros = await _database.ObterMensagensHistoricoProtegidas(_identidade.EmpresaId, _identidade.UsuarioId, _contato.Id);
        var senha = await PedirSenhaHistoricoAsync("Ocultar mensagens", todosRegistros.Any(x => x.Ocultar));
        if (senha is null) return;
        if (!ValidarSenhaHistorico(senha))
        {
            await DisplayAlert("Senha incorreta", "Digite a senha da conta do usuário conectado.", "OK");
            return;
        }

        var registrosPorChave = todosRegistros.ToDictionary(x => x.Chave, StringComparer.Ordinal);
        var mensagens = _mensagens
            .Where(x => x.IdServidor > 0 && !x.PendenteLocal && !x.Oculta)
            .Select(x => x.DadosMensagem)
            .ToList();
        if (mensagens.Count == 0)
        {
            var haMensagensPersistidas = _mensagens.Any(x => x.IdServidor > 0 && !x.PendenteLocal);
            await DisplayAlert("Histórico oculto", haMensagensPersistidas
                ? "As mensagens desta conversa já estão ocultas neste aparelho."
                : "Não há mensagens carregadas nesta conversa para ocultar.", "OK");
            return;
        }

        DefinirHistoricoProcessando(true, "Ocultando mensagens neste aparelho…");
        try
        {
            foreach (var mensagem in mensagens)
            {
                var chaveMensagem = HistoricoCriptografia.ChaveMensagem(
                    _identidade.EmpresaId, _identidade.UsuarioId, _contato.Id, mensagem.ClienteMensagemId, mensagem.Id);
                if (registrosPorChave.TryGetValue(chaveMensagem, out var registroExistente))
                {
                    registroExistente.Ocultar = true;
                    await _database.SalvarMensagemHistoricoProtegida(registroExistente);
                }
                else
                {
                    var registro = new MensagemHistoricoProtegida
                    {
                        Chave = chaveMensagem,
                        EmpresaId = _identidade.EmpresaId, UsuarioId = _identidade.UsuarioId, ContatoId = _contato.Id,
                        IdServidor = mensagem.Id, ClienteMensagemId = mensagem.ClienteMensagemId,
                        RemetenteId = mensagem.RemetenteId, RemetenteNome = mensagem.RemetenteNome,
                        DestinatarioId = mensagem.DestinatarioId, EnviadaEm = mensagem.EnviadaEm, LidaEm = mensagem.LidaEm,
                        SaltDerivacao = "", Ocultar = true, PayloadCifrado = Array.Empty<byte>()
                    };
                    await _database.SalvarMensagemHistoricoProtegida(registro);
                    registrosPorChave[chaveMensagem] = registro;
                }
                var linha = _mensagens.FirstOrDefault(x => x.Chave == ChaveMensagem(mensagem));
                linha?.DefinirOculta(true);
            }

            await DisplayAlert("Mensagens ocultas", $"{mensagens.Count} mensagens foram ocultadas com cadeado neste aparelho. O conteúdo continua no servidor.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Histórico", $"Não consegui ocultar as mensagens. {ex.Message}", "OK");
        }
        finally { DefinirHistoricoProcessando(false); }
    }

    private async void DescriptografarHistorico_Clicked(object sender, EventArgs e)
    {
        FecharMenusConversa();
        var registros = (await _database.ObterMensagensHistoricoProtegidas(_identidade.EmpresaId, _identidade.UsuarioId, _contato.Id))
            .Where(x => x.Ocultar).ToList();
        if (registros.Count == 0)
        {
            await DisplayAlert("Histórico oculto", "Não há mensagens ocultas nesta conversa neste aparelho.", "OK");
            return;
        }

        var senha = await PedirSenhaHistoricoAsync("Descriptografar mensagens", protecaoJaExiste: true);
        if (senha is null) return;
        if (!ValidarSenhaHistorico(senha))
        {
            await DisplayAlert("Senha incorreta", "Digite a senha da conta do usuário conectado.", "OK");
            return;
        }
        DefinirHistoricoProcessando(true, "Restaurando mensagens da conversa…");
        try
        {
            if (!await RestaurarMensagensOcultasSemChaveAsync(registros)) return;
            await CarregarMensagensLocais();
            await DisplayAlert("Mensagens visíveis", $"{registros.Count} mensagens tiveram o cadeado removido.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Histórico", $"Não consegui exibir as mensagens. {ex.Message}", "OK");
        }
        finally { DefinirHistoricoProcessando(false); }
    }

    private bool ValidarSenhaHistorico(string senha)
    {
        var usuario = SessaoUsuario.UsuarioAtual;
        return usuario is not null && !string.IsNullOrWhiteSpace(usuario.SenhaHash) &&
            string.Equals(_database.CriarHashSenha(senha), usuario.SenhaHash, StringComparison.OrdinalIgnoreCase);
    }

    private bool TryObterSaltHistorico(MensagemHistoricoProtegida mensagem, out string salt)
    {
        salt = string.IsNullOrWhiteSpace(mensagem.SaltDerivacao)
            ? _preferencias.SaltHistoricoProtegido
            : mensagem.SaltDerivacao;
        if (string.IsNullOrWhiteSpace(salt)) return false;
        try { _ = Convert.FromBase64String(salt); return true; }
        catch (FormatException) { return false; }
    }

    private async Task<bool> RestaurarMensagensOcultasSemChaveAsync(List<MensagemHistoricoProtegida> registros)
    {
        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
        {
            await DisplayAlert("Mensagens ocultas", "Para remover o cadeado sem a chave local, conecte-se à internet para carregar novamente o histórico do servidor. As mensagens continuam ocultas.", "OK");
            return false;
        }

        try
        {
            var originais = (await _api.ObterMensagens(Request()))
                .Where(x => !ChatCatAcaoVisual.EhAcaoLegada(x.Texto)).ToList();
            foreach (var registro in registros)
            {
                registro.Ocultar = false;
                await _database.SalvarMensagemHistoricoProtegida(registro);
            }
            await _database.SubstituirMensagensCache(_identidade.EmpresaId, _identidade.UsuarioId, _contato.Id, originais);
            return true;
        }
        catch (Exception ex)
        {
            await DisplayAlert("Mensagens ocultas", $"Não consegui recarregar a conversa. O cadeado e os dados protegidos foram mantidos. {ex.Message}", "OK");
            return false;
        }
    }

    private async Task<string?> PedirSenhaHistoricoAsync(string titulo, bool protecaoJaExiste = false)
    {
        var resposta = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var senha = new Entry
        {
            Placeholder = "Senha da conta", IsPassword = true, ReturnType = ReturnType.Done,
            TextColor = PaletasAppService.CorAtual("BrandText", Colors.Black),
            PlaceholderColor = PaletasAppService.CorAtual("BrandMuted", Colors.Gray)
        };
        var botoes = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new() { Width = GridLength.Star }, new() { Width = GridLength.Star }
            },
            ColumnSpacing = 10
        };
        var cancelar = new Button { Text = "Cancelar", BackgroundColor = PaletasAppService.CorAtual("BrandSoft", Colors.LightGray) };
        var confirmar = new Button { Text = "Continuar", BackgroundColor = PaletasAppService.CorAtual("BrandPrimary", Colors.Blue), TextColor = Colors.White };
        botoes.Add(cancelar);
        botoes.Add(confirmar, 1);
        var pagina = new ContentPage
        {
            Title = titulo,
            BackgroundColor = PaletasAppService.CorAtual("BrandBackground", Colors.White),
            Content = new VerticalStackLayout
            {
                Padding = 24, Spacing = 16, VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label { Text = titulo, FontSize = 22, FontAttributes = FontAttributes.Bold, TextColor = PaletasAppService.CorAtual("BrandText", Colors.Black) },
                    new Label { Text = protecaoJaExiste
                        ? "Digite a senha da conta do usuário conectado. Apenas essa senha é aceita e não será armazenada."
                        : "Digite a senha da conta do usuário conectado. A senha não será armazenada.",
                        FontSize = 14, TextColor = PaletasAppService.CorAtual("BrandMuted", Colors.Gray) },
                    senha, botoes
                }
            }
        };
        pagina.Disappearing += (_, _) => resposta.TrySetResult(null);
        async Task ConfirmarAsync()
        {
            if (string.IsNullOrEmpty(senha.Text))
            {
                await pagina.DisplayAlert("Senha", "Digite sua senha para continuar.", "OK");
                return;
            }
            resposta.TrySetResult(senha.Text);
            await Navigation.PopModalAsync();
        }
        cancelar.Clicked += async (_, _) =>
        {
            resposta.TrySetResult(null);
            await Navigation.PopModalAsync();
        };
        confirmar.Clicked += async (_, _) => await ConfirmarAsync();
        senha.Completed += async (_, _) => await ConfirmarAsync();
        await Navigation.PushModalAsync(pagina);
        return await resposta.Task;
    }

    private async Task CarregarWinksPersonalizados()
    {
        WinksUsuarioPanel.Children.Clear();
        var usuarioId = _identidade.UsuarioId;
        foreach (var wink in await _database.ObterWinksPersonalizados(usuarioId))
        {
            var botao = new Button
            {
                Text = wink.Simbolo,
                FontSize = 25,
                BackgroundColor = Colors.Transparent,
                Padding = new Thickness(8, 0),
                CommandParameter = wink.Simbolo
            };
            botao.Clicked += WinkItem_Clicked;
            WinksUsuarioPanel.Children.Add(botao);
        }
    }

    private async void AdicionarWink_Clicked(object sender, EventArgs e)
    {
        FecharMenusConversa();
        var simbolo = await DisplayPromptAsync("Meu pacote", "Digite um emoji ou símbolo para o seu wink", initialValue: "😺");
        if (string.IsNullOrWhiteSpace(simbolo)) return;
        var nome = await DisplayPromptAsync("Meu pacote", "Como deseja chamar este wink?", initialValue: "Meu wink");
        await _database.SalvarWinkPersonalizado(new WinkPersonalizado
        {
            UsuarioId = _identidade.UsuarioId,
            Nome = string.IsNullOrWhiteSpace(nome) ? "Meu wink" : nome.Trim(),
            Simbolo = simbolo.Trim()
        });
        await CarregarWinksPersonalizados();
    }

    private async void WinkItem_Clicked(object? sender, EventArgs e)
    {
        FecharMenusConversa();
        if (sender is Button { CommandParameter: string simbolo })
            await ExibirAcaoVisualAsync(simbolo, eAtencao: false, enviar: true);
    }

    private async void Figurinha_Clicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: string emoji }) return;
        FecharMenusConversa();
        await EnviarMensagemInterna(ChatCatFigurinha.Criar(emoji));
    }

    private async void FigurinhaArquivo_Clicked(object sender, EventArgs e)
    {
        FecharMenusConversa();
        await ImportarFigurinhaDoAparelhoAsync();
    }

    private async Task ImportarFigurinhaDoAparelhoAsync()
    {
        try
        {
            var arquivo = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Escolha uma figurinha do aparelho",
                FileTypes = FilePickerFileType.Images
            });
            if (arquivo is null) return;
            await using var stream = await arquivo.OpenReadAsync();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            if (buffer.Length is 0 or > TamanhoMaximoFoto)
            {
                await DisplayAlert("Figurinha", "Escolha uma imagem de até 8 MB.", "OK");
                return;
            }

            var bytes = buffer.ToArray();
            var tipo = arquivo.ContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true
                ? arquivo.ContentType : "image/png";
            var extensao = ExtensaoFigurinha(arquivo.FileName, tipo);
            var nomeLocal = $"{Guid.NewGuid():N}{extensao}";
            var pasta = PastaFigurinhasLocais();
            Directory.CreateDirectory(pasta);
            var caminhoLocal = Path.Combine(pasta, nomeLocal);
            await File.WriteAllBytesAsync(caminhoLocal, bytes);
            AdicionarFigurinhaLocal(caminhoLocal);
            var nome = $"CHATCAT_STICKER_{nomeLocal}";
            var pendente = new MensagemPendente
            {
                Chave = Guid.NewGuid().ToString("N"), EmpresaId = _identidade.EmpresaId,
                UsuarioId = _identidade.UsuarioId, ContatoId = _contato.Id, Texto = "",
                Foto = bytes, FotoNome = nome, FotoTipo = tipo
            };
            await _database.SalvarMensagemPendente(pendente);
            FigurinhasPanel.IsVisible = false;
            await ExibirAcaoVisualAsync("", eAtencao: false, enviar: false, bytes);
            await AtualizarConversa(exibirErro: false);
        }
        catch (Exception ex) { await DisplayAlert("Figurinha", ex.Message, "OK"); }
    }

    private async void AbrirMyEmotes_Clicked(object sender, EventArgs e)
    {
        FecharMenusConversa();
        try
        {
            await Launcher.Default.OpenAsync(new Uri("https://myemotes.io/open-stickers"));
            var importar = await DisplayAlert("myEmotes", "Escolha uma figurinha, baixe a imagem e volte aqui para importá-la para o pacote local do CATCHAT.", "Importar agora", "Depois");
            if (importar) await ImportarFigurinhaDoAparelhoAsync();
        }
        catch (Exception ex) { await DisplayAlert("myEmotes", $"Não foi possível abrir o site: {ex.Message}", "OK"); }
    }

    private async void FigurinhaLocal_Tapped(object sender, TappedEventArgs e)
    {
        FecharMenusConversa();
        if ((sender as Element)?.BindingContext is not FigurinhaLocalItem item) return;
        try
        {
            var bytes = await File.ReadAllBytesAsync(item.Caminho);
            if (bytes.Length is 0 or > TamanhoMaximoFoto)
                throw new InvalidOperationException("A figurinha deve ter até 8 MB.");
            var tipo = TipoFigurinha(item.Caminho);
            var pendente = new MensagemPendente
            {
                Chave = Guid.NewGuid().ToString("N"), EmpresaId = _identidade.EmpresaId,
                UsuarioId = _identidade.UsuarioId, ContatoId = _contato.Id, Texto = "",
                Foto = bytes, FotoNome = $"CHATCAT_STICKER_{Path.GetFileName(item.Caminho)}", FotoTipo = tipo
            };
            await _database.SalvarMensagemPendente(pendente);
            FigurinhasPanel.IsVisible = false;
            await ExibirAcaoVisualAsync("", eAtencao: false, enviar: false, bytes);
            await AtualizarConversa(exibirErro: false);
        }
        catch (Exception ex) { await DisplayAlert("Figurinha", ex.Message, "OK"); }
    }

    private async Task CarregarFigurinhasLocais()
    {
        try
        {
            var pasta = PastaFigurinhasLocais();
            Directory.CreateDirectory(pasta);
            _figurinhasLocais.Clear();
            foreach (var caminho in Directory.EnumerateFiles(pasta).OrderByDescending(File.GetCreationTimeUtc))
                AdicionarFigurinhaLocal(caminho);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Carregar pacote local de figurinhas: {ex.Message}"); }
        await Task.CompletedTask;
    }

    private void AdicionarFigurinhaLocal(string caminho)
    {
        if (!_figurinhasLocais.Any(x => string.Equals(x.Caminho, caminho, StringComparison.OrdinalIgnoreCase)))
            _figurinhasLocais.Insert(0, new FigurinhaLocalItem(caminho));
        FigurinhasLocaisList.IsVisible = _figurinhasLocais.Count > 0;
    }

    private static string PastaFigurinhasLocais() => Path.Combine(FileSystem.AppDataDirectory, "figurinhas");

    private static string ExtensaoFigurinha(string nome, string tipo)
    {
        var ext = Path.GetExtension(nome).ToLowerInvariant();
        if (ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp") return ext;
        return tipo.ToLowerInvariant() switch
        {
            "image/gif" => ".gif", "image/webp" => ".webp", "image/jpeg" => ".jpg", _ => ".png"
        };
    }

    private static string TipoFigurinha(string caminho) => Path.GetExtension(caminho).ToLowerInvariant() switch
    {
        ".gif" => "image/gif", ".webp" => "image/webp", ".jpg" or ".jpeg" => "image/jpeg", _ => "image/png"
    };

    private async Task EnviarFigurinhaStipopAsync(StipopFigurinha figurinha)
    {
        if (!Uri.TryCreate(figurinha.StickerImg, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("A figurinha retornou um endereço inválido.");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > TamanhoMaximoFoto)
            throw new InvalidOperationException("Esta figurinha excede o limite de 8 MB.");
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        if (buffer.Length is 0 or > TamanhoMaximoFoto)
            throw new InvalidOperationException("A figurinha deve ter até 8 MB.");

        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (contentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true)
            throw new InvalidOperationException("A Stipop não retornou um arquivo de imagem válido.");
        var nome = $"CHATCAT_STIPOP_{figurinha.StickerId}{ExtensaoImagem(contentType)}";
        var pendente = new MensagemPendente
        {
            Chave = Guid.NewGuid().ToString("N"), EmpresaId = _identidade.EmpresaId,
            UsuarioId = _identidade.UsuarioId, ContatoId = _contato.Id, Texto = "",
            Foto = buffer.ToArray(), FotoNome = nome, FotoTipo = contentType
        };
        await _database.SalvarMensagemPendente(pendente);
        try
        {
            await _api.RegistrarEnvioFigurinhaStipop(new StipopFigurinhaRequest
            {
                EmpresaId = _identidade.EmpresaId, UsuarioId = _identidade.UsuarioId, StickerId = figurinha.StickerId
            });
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Registro de envio Stipop: {ex.Message}"); }
        await ExibirAcaoVisualAsync("", eAtencao: false, enviar: false, pendente.Foto);
        await AtualizarConversa(exibirErro: false);
    }

    private static string ExtensaoImagem(string? tipo) => tipo?.ToLowerInvariant() switch
    {
        "image/gif" => ".gif", "image/webp" => ".webp", "image/jpeg" => ".jpg", _ => ".png"
    };

    private async void Fonte_Clicked(object sender, EventArgs e)
    {
        FecharMenusConversa();
        _preferencias.FamiliaFonte = _preferencias.FamiliaFonte == "OpenSansRegular" ? "OpenSansSemibold" : "OpenSansRegular";
        _preferencias.TamanhoFonte = _preferencias.TamanhoFonte switch { < 15 => 15, < 18 => 18, < 22 => 22, _ => 13 };
        await _database.SalvarPreferenciasConversa(_preferencias);
        ReaplicarEstilo();
    }

    private async void StatusPicker_Changed(object? sender, EventArgs e)
    {
        if (StatusPicker.SelectedItem is not string status) return;
        _preferencias.StatusLocal = status;
        await _database.SalvarPreferenciasConversa(_preferencias);
    }

    private async void ChamarAtencao_Clicked(object sender, EventArgs e)
    {
        FecharMenusConversa();
        try
        {
            // O retorno local acontece antes de qualquer chamada de rede; o pedido
            // remoto apenas avisa o outro participante e não controla o feedback.
            VibrarAcao(420);
            await TocarBeepAtencaoAsync();
            SemanticScreenReader.Announce("Atenção solicitada");
            await ExibirAcaoVisualAsync("⚡", eAtencao: true, enviar: true);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Atenção: {ex.Message}"); }
    }

    private async Task ExibirAcaoVisualAsync(string simbolo, bool eAtencao, bool enviar, byte[]? figurinha = null)
    {
        if (eAtencao)
        {
            await ExibirLedNotificacaoAsync(enviar);
            return;
        }

        Task<string?>? envio = null;
        string? mensagemErroVisual = null;
        var imagemFigurinha = figurinha is { Length: > 0 };
        VisualElement elementoAnimado = imagemFigurinha ? EfeitoWinkFigurinha : EfeitoWinkIcone;
#if ANDROID
        Android.Media.ToneGenerator? geradorTom = null;
#endif
        try
        {
            EfeitoWinkIcone.Text = string.IsNullOrWhiteSpace(simbolo) ? "⚡" : simbolo;
            EfeitoWinkIcone.IsVisible = !imagemFigurinha;
            EfeitoWinkFigurinha.IsVisible = imagemFigurinha;
            if (imagemFigurinha)
                EfeitoWinkFigurinha.Source = ImageSource.FromStream(() => new MemoryStream(figurinha!));
            EfeitoWink.IsVisible = true;
            EfeitoWink.Opacity = 1;
            elementoAnimado.Opacity = 1;
            elementoAnimado.Scale = 0.18;
            EfeitoNeon.Opacity = 0;
            VibrarAcao(300);
#if ANDROID
            try
            {
                geradorTom = new Android.Media.ToneGenerator(Android.Media.Stream.Notification, 85);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Beep indisponível: {ex.Message}"); }
#endif
            if (enviar)
                envio = EnviarAcaoVisual(simbolo, eAtencao);

            // Winks e figurinhas mantêm a animação; o raio usa o LED de notificação.
            for (int ciclo = 0; ciclo < 3; ciclo++)
            {
#if ANDROID
                TocarBeep(geradorTom);
#endif
                await Task.WhenAll(
                    elementoAnimado.ScaleTo(4.5, 500, Easing.CubicOut),
                    EfeitoNeon.FadeTo(0.95, 120, Easing.CubicOut));
#if ANDROID
                TocarBeep(geradorTom);
#endif
                await Task.WhenAll(
                    elementoAnimado.ScaleTo(0.18, 500, Easing.CubicIn),
                    EfeitoNeon.FadeTo(0, 180, Easing.CubicIn));
            }
        }
        catch (Exception ex)
        {
            mensagemErroVisual = ex.Message;
            System.Diagnostics.Debug.WriteLine($"Animação da ação: {ex.Message}");
        }
        finally
        {
            EfeitoWink.IsVisible = false;
            EfeitoWinkIcone.Scale = 1;
            EfeitoWinkFigurinha.Scale = 1;
            EfeitoNeon.Opacity = 0;
#if ANDROID
            try { geradorTom?.Release(); } catch { }
#endif
        }

        if (envio is not null)
        {
            try { mensagemErroVisual ??= await envio; }
            catch (Exception ex) { mensagemErroVisual ??= ex.Message; }
        }

        if (enviar && !string.IsNullOrWhiteSpace(mensagemErroVisual))
        {
            await DisplayAlert("Ação pendente", mensagemErroVisual, "OK");
        }
    }

    private async Task ExibirLedNotificacaoAsync(bool enviar)
    {
        Task<string?>? envio = enviar ? EnviarAcaoVisual("⚡", eAtencao: true) : null;
#if ANDROID
        Android.Media.ToneGenerator? geradorTom = null;
#endif
        try
        {
            EfeitoWink.IsVisible = false;
            var corLed = Color.FromArgb("#FFD400");
            foreach (var borda in new[] { LedBordaBrilhante, LedBordaMedia, LedBordaDifusa })
            {
                borda.Stroke = new SolidColorBrush(corLed);
                if (borda.Shadow is not null) borda.Shadow.Brush = Colors.White;
                borda.Opacity = 0;
            }
            if (LedGatoAnimado.Shadow is not null)
                LedGatoAnimado.Shadow.Brush = Colors.White;
            LedNotificacao.IsVisible = true;
            await Task.Yield();
#if ANDROID
            if (LedGato.Handler?.PlatformView is Android.Widget.ImageView imagemNativa)
                imagemNativa.ImageTintList = Android.Content.Res.ColorStateList.ValueOf(corLed.ToPlatform());
#endif
            LedGatoAnimado.Opacity = 0;
            LedGatoAnimado.Scale = 0.72;
            LedGatoAnimado.AnchorX = 0.5;
            LedGatoAnimado.AnchorY = 0.5;
            LedGatoAnimado.TranslationX = 0;
            LedGatoAnimado.TranslationY = 0;
            var ladoDisponivel = Math.Min(LedNotificacao.Width, LedNotificacao.Height);
            if (ladoDisponivel <= 0)
            {
                var tela = DeviceDisplay.MainDisplayInfo;
                ladoDisponivel = tela.Density > 0
                    ? Math.Min(tela.Width, tela.Height) / tela.Density
                    : 360;
            }
            var escalaMaxima = Math.Max(1.05, ladoDisponivel * 0.78 / 176.0);
#if ANDROID
            try { geradorTom = new Android.Media.ToneGenerator(Android.Media.Stream.Notification, 100); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Som do raio indisponível: {ex.Message}"); }
#endif
            for (var pulso = 0; pulso < 3; pulso++)
            {
                VibrarAcao(350);
#if ANDROID
                TocarBeep(geradorTom);
#endif
                await Task.WhenAll(
                    LedBordaBrilhante.FadeTo(0.98, 420, Easing.CubicOut),
                    LedBordaMedia.FadeTo(0.55, 480, Easing.CubicOut),
                    LedBordaDifusa.FadeTo(0.22, 540, Easing.CubicOut),
                    LedGatoAnimado.FadeTo(1, 330, Easing.CubicOut),
                    LedGatoAnimado.ScaleTo(escalaMaxima, 600, Easing.CubicOut));
                await Task.WhenAll(
                    LedBordaBrilhante.FadeTo(0.18, 460, Easing.CubicIn),
                    LedBordaMedia.FadeTo(0.09, 460, Easing.CubicIn),
                    LedBordaDifusa.FadeTo(0.025, 460, Easing.CubicIn),
                    LedGatoAnimado.FadeTo(0.48, 420, Easing.CubicIn),
                    LedGatoAnimado.ScaleTo(0.82, 600, Easing.CubicIn));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LED de notificação: {ex.Message}");
        }
        finally
        {
            LedNotificacao.IsVisible = false;
            LedGatoAnimado.Opacity = 0;
            LedGatoAnimado.Scale = 1;
            LedBordaBrilhante.Opacity = 0;
            LedBordaMedia.Opacity = 0;
            LedBordaDifusa.Opacity = 0;
#if ANDROID
            try { geradorTom?.Release(); } catch { }
#endif
        }

        if (envio is not null)
        {
            try
            {
                var erro = await envio;
                if (!string.IsNullOrWhiteSpace(erro))
                    await DisplayAlert("Ação pendente", erro, "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Ação pendente", $"O LED foi exibido neste aparelho, mas a API não confirmou o envio. {ex.Message}", "OK");
            }
        }
    }

    private static void TocarBeep(
#if ANDROID
        Android.Media.ToneGenerator? gerador
#else
        object? gerador
#endif
    )
    {
#if ANDROID
        try { gerador?.StartTone(Android.Media.Tone.PropBeep2, 100); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Beep da ação: {ex.Message}"); }
#endif
    }

    private static async Task TocarBeepAtencaoAsync()
    {
#if ANDROID
        Android.Media.ToneGenerator? gerador = null;
        try
        {
            gerador = new Android.Media.ToneGenerator(Android.Media.Stream.Notification, 100);
            gerador.StartTone(Android.Media.Tone.PropBeep2, 180);
            await Task.Delay(220);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Beep imediato da ação: {ex.Message}");
        }
        finally
        {
            try { gerador?.Release(); } catch { }
        }
#else
        await Task.CompletedTask;
#endif
    }

    private void VibrarAcao(long milissegundos)
    {
        try { HapticFeedback.Default.Perform(HapticFeedbackType.LongPress); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Retorno tátil: {ex.Message}"); }
#if ANDROID
        try
        {
            var contexto = Android.App.Application.Context;
            Android.OS.Vibrator? vibrator;
            if (OperatingSystem.IsAndroidVersionAtLeast(31))
            {
                var gerenciador = contexto.GetSystemService(Android.Content.Context.VibratorManagerService)
                    as Android.OS.VibratorManager;
                vibrator = gerenciador?.DefaultVibrator;
            }
            else
            {
#pragma warning disable CA1422
                vibrator = contexto.GetSystemService(Android.Content.Context.VibratorService) as Android.OS.Vibrator;
#pragma warning restore CA1422
            }
            if (vibrator?.HasVibrator != true) return;
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
                vibrator.Vibrate(Android.OS.VibrationEffect.CreateOneShot(milissegundos, 255));
            else
#pragma warning disable CA1422
                vibrator.Vibrate(milissegundos);
#pragma warning restore CA1422
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Vibração da ação: {ex.Message}");
        }
#else
        try
        {
            if (Vibration.Default.IsSupported)
                Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(milissegundos));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Vibração da ação: {ex.Message}");
        }
#endif
    }

    private async Task<string?> EnviarAcaoVisual(string simbolo, bool eAtencao)
    {
        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            return "Sem conexão: a animação foi exibida neste aparelho, mas não foi enviada.";

        try
        {
            await _api.EnviarAcaoVisual(new EnviarAcaoVisualConversa
            {
                EmpresaId = _identidade.EmpresaId,
                UsuarioId = _identidade.UsuarioId,
                ContatoId = _contato.Id,
                Tipo = eAtencao ? "ATTENTION" : "WINK",
                Simbolo = simbolo
            });
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ação visual não enviada: {ex.Message}");
            return $"A animação foi exibida neste aparelho, mas a API não confirmou o envio. {ex.Message}";
        }
    }

    private void Mensagem_Tapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border { BindingContext: MensagemLinha mensagem })
            mensagem.AlternarReacao();
    }

    private async void Foto_Clicked(object sender, EventArgs e)
    {
        FecharMenusConversa();
        try
        {
            var arquivo = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Escolha uma foto",
                FileTypes = FilePickerFileType.Images
            });
            if (arquivo is null) return;
            await using var stream = await arquivo.OpenReadAsync();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            if (buffer.Length > TamanhoMaximoFoto)
            {
                await DisplayAlert("Foto", "A foto deve ter no máximo 8 MB.", "OK");
                return;
            }
            _foto = buffer.ToArray();
            _fotoNome = Path.GetFileName(arquivo.FileName);
            _fotoTipo = arquivo.ContentType ?? "image/jpeg";
            await DisplayAlert("Foto selecionada", $"{_fotoNome} será enviada junto com a próxima mensagem.", "OK");
        }
        catch (Exception ex) { await DisplayAlert("Foto", ex.Message, "OK"); }
    }

    private ConversaRequest Request(long depoisDoId = 0) => new()
    {
        EmpresaId = _identidade.EmpresaId, UsuarioId = _identidade.UsuarioId,
        ContatoId = _contato.Id, DepoisDoId = depoisDoId
    };

    private async void AbrirMidia_Tapped(object? sender, EventArgs e)
    {
        MensagemLinha? linha = null;
        if (sender is Image img && img.BindingContext is MensagemLinha ml) linha = ml;
        else if (sender is VisualElement ve && ve.BindingContext is MensagemLinha ml2) linha = ml2;
        else if (sender is TappedEventArgs te && te.Parameter is MensagemLinha ml3) linha = ml3;

        if (linha is null) return;
        var bytes = linha.FotoBytes;
        var tipo = linha.FotoTipo ?? "application/octet-stream";
        var nome = linha.FotoNome ?? $"midia_{DateTime.Now:yyyyMMddHHmmss}";

        if (bytes is null || bytes.Length == 0)
        {
            await DisplayAlert("Mídia", "Arquivo não disponível localmente.", "OK");
            return;
        }

        await Navigation.PushAsync(new MediaViewer(bytes, tipo, nome));
    }

}

public class MensagemLinha : INotifyPropertyChanged
{
    private MensagemConversa _mensagem;
    private readonly long _usuarioId;
    private PreferenciasConversa _preferencias;
    private bool _oculta;
    public string Texto => _oculta
        ? (_mensagem.Foto is { Length: > 0 } ? "🔒 Mídia protegida" : "🔒 Mensagem protegida")
        : ChatCatFigurinha.TentarLer(_mensagem.Texto, out var emoji) ? emoji : _mensagem.Texto;
    public string TextoOriginal => _mensagem.Texto;
    public MensagemConversa DadosMensagem => _mensagem;
    public string Chave => _mensagem.Id > 0 ? $"id:{_mensagem.Id}" : $"cliente:{_mensagem.ClienteMensagemId?.ToString("N") ?? ""}";
    public bool Oculta => _oculta;
    public Guid? ClienteMensagemId => _mensagem.ClienteMensagemId;
    public long IdServidor => _mensagem.Id;
    public bool PendenteLocal => _mensagem.PendenteLocal;
    public bool EhFigurinha => !_oculta && ChatCatFigurinha.EhFigurinha(_mensagem);
    public bool TemTexto => !string.IsNullOrWhiteSpace(Texto);
    public bool TemStatus => !EhFigurinha;

    public byte[]? FotoBytes => _oculta ? null : _mensagem.Foto;
    public string? FotoTipo => _oculta ? null : _mensagem.FotoTipo;
    public string? FotoNome => _oculta ? null : _mensagem.FotoNome;

    public bool TemFoto => FotoBytes is { Length: > 0 } && (FotoTipo ?? "").StartsWith("image/");
    public bool TemVideo => FotoBytes is { Length: > 0 } && (FotoTipo ?? "").StartsWith("video/");
    public bool TemMidia => FotoBytes is { Length: > 0 };

    public ImageSource? Foto => TemFoto ? ImageSource.FromStream(() => new MemoryStream(FotoBytes!)) : null;

    public LayoutOptions Alinhamento => _mensagem.RemetenteId == _usuarioId ? LayoutOptions.End : LayoutOptions.Start;
    public string FamiliaFonte => _preferencias.FamiliaFonte;
    public double TamanhoFonte => EhFigurinha ? 76 : _preferencias.TamanhoFonte;
    public Color CorTexto => PaletasAppService.CorAtual(
        _mensagem.RemetenteId == _usuarioId ? "BrandBubbleOutgoingText" : "BrandBubbleIncomingText",
        _mensagem.RemetenteId == _usuarioId ? Colors.White : Color.FromArgb("#1E293B"));
    public Color CorFundo => EhFigurinha ? Colors.Transparent : PaletasAppService.CorAtual(
        _mensagem.RemetenteId == _usuarioId ? "BrandBubbleOutgoing" : "BrandBubbleIncoming",
        _mensagem.RemetenteId == _usuarioId ? Color.FromArgb("#3B82F6") : Colors.White);
    public string Status => _oculta ? "🔒 Oculta neste aparelho" : _mensagem.PendenteLocal
        ? (Connectivity.Current.NetworkAccess == NetworkAccess.Internet ? "Enviando…" : "Aguardando sincronização")
        : _mensagem.RemetenteId == _usuarioId && _mensagem.LidaEm.HasValue ? "Lida" : _mensagem.RemetenteId == _usuarioId ? "Enviada" : _mensagem.RemetenteNome;
    public string Reacao { get; private set; } = "";
    public bool TemReacao => !string.IsNullOrWhiteSpace(Reacao);
    public MensagemLinha(MensagemConversa mensagem, long usuarioId, PreferenciasConversa? preferencias = null, bool oculta = false)
    {
        _mensagem = mensagem; _usuarioId = usuarioId; _preferencias = preferencias ?? new PreferenciasConversa(); _oculta = oculta;
    }

    public void DefinirOculta(bool oculta)
    {
        if (_oculta == oculta) return;
        _oculta = oculta;
        NotificarConteudo();
    }

    public void Atualizar(MensagemConversa mensagem, bool oculta)
    {
        _mensagem = mensagem;
        _oculta = oculta;
        NotificarConteudo();
    }

    private void NotificarConteudo()
    {
        OnPropertyChanged(nameof(Texto)); OnPropertyChanged(nameof(TextoOriginal));
        OnPropertyChanged(nameof(DadosMensagem)); OnPropertyChanged(nameof(Chave)); OnPropertyChanged(nameof(Oculta));
        OnPropertyChanged(nameof(ClienteMensagemId)); OnPropertyChanged(nameof(IdServidor));
        OnPropertyChanged(nameof(PendenteLocal)); OnPropertyChanged(nameof(EhFigurinha));
        OnPropertyChanged(nameof(TemTexto)); OnPropertyChanged(nameof(TemStatus));
        OnPropertyChanged(nameof(FotoBytes)); OnPropertyChanged(nameof(FotoTipo)); OnPropertyChanged(nameof(FotoNome));
        OnPropertyChanged(nameof(TemFoto)); OnPropertyChanged(nameof(TemVideo)); OnPropertyChanged(nameof(TemMidia));
        OnPropertyChanged(nameof(Foto)); OnPropertyChanged(nameof(Alinhamento)); OnPropertyChanged(nameof(TamanhoFonte));
        OnPropertyChanged(nameof(CorTexto)); OnPropertyChanged(nameof(CorFundo)); OnPropertyChanged(nameof(Status));
    }

    public void AtualizarEstilo(PreferenciasConversa preferencias)
    {
        _preferencias = preferencias;
        OnPropertyChanged(nameof(FamiliaFonte)); OnPropertyChanged(nameof(TamanhoFonte)); OnPropertyChanged(nameof(CorFundo)); OnPropertyChanged(nameof(CorTexto));
    }

    public void AlternarReacao()
    {
        Reacao = Reacao == "👍" ? "" : "👍";
        OnPropertyChanged(nameof(Reacao));
        OnPropertyChanged(nameof(TemReacao));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal static class ChatCatAcaoVisual
{
    private const string PrefixoAtencao = "[[CHATCAT:ATTENTION]]";
    private const string PrefixoWink = "[[CHATCAT:WINK]]";
    private static readonly HashSet<string> WinksAntigos = new(StringComparer.Ordinal)
    {
        "✨", "💥", "🎉", "😺", "💫", "🚀"
    };

    public static bool EhAcaoLegada(string? texto)
    {
        var valor = texto?.Trim() ?? string.Empty;
        return valor.Equals(PrefixoAtencao, StringComparison.Ordinal)
            || valor.StartsWith(PrefixoWink, StringComparison.Ordinal)
            || valor == "⚡"
            || WinksAntigos.Contains(valor);
    }
}

internal static class ChatCatFigurinha
{
    private const string PrefixoImagem = "CHATCAT_STICKER_";
    private const string PrefixoStipop = "CHATCAT_STIPOP_";
    private static readonly HashSet<string> EmojiFigurinhas = new(StringComparer.Ordinal)
    {
        "😺", "😂", "😍", "🙌", "👍", "❤️", "🎉", "🔥"
    };

    public static string Criar(string emoji) => emoji.Trim();

    public static bool TentarLer(string? texto, out string emoji)
    {
        var valor = texto?.Trim() ?? string.Empty;
        emoji = valor;
        return EmojiFigurinhas.Contains(valor);
    }

    public static bool EhFigurinha(MensagemConversa mensagem) =>
        TentarLer(mensagem.Texto, out _) ||
        mensagem.Foto is { Length: > 0 } && (mensagem.FotoNome?.StartsWith(PrefixoImagem, StringComparison.OrdinalIgnoreCase) == true
            || mensagem.FotoNome?.StartsWith(PrefixoStipop, StringComparison.OrdinalIgnoreCase) == true);
}

internal sealed record FigurinhaLocalItem(string Caminho);
