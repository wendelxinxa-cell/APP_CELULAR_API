using APP_CELULAR.Services;

namespace APP_CELULAR;

internal static class SincronizacaoAutomaticaService
{
    private static readonly object EstadoLock = new();
    private static readonly SemaphoreSlim TravaExecucao = new(1, 1);
    private static CancellationTokenSource? _cancelamento;

    public static void Iniciar()
    {
        CancellationToken token;
        lock (EstadoLock)
        {
            if (_cancelamento is not null)
                return;

            _cancelamento = new CancellationTokenSource();
            token = _cancelamento.Token;
            Connectivity.Current.ConnectivityChanged += Connectivity_ConnectivityChanged;
        }

        _ = AcompanharSessao(token);
    }

    public static void Parar()
    {
        lock (EstadoLock)
        {
            if (_cancelamento is null)
                return;

            Connectivity.Current.ConnectivityChanged -= Connectivity_ConnectivityChanged;
            _cancelamento.Cancel();
            _cancelamento.Dispose();
            _cancelamento = null;
        }
    }

    private static async Task AcompanharSessao(CancellationToken cancellationToken)
    {
        using var temporizador = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            await SincronizarSePossivel(cancellationToken);
            while (await temporizador.WaitForNextTickAsync(cancellationToken))
                await SincronizarSePossivel(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // O usuário encerrou a sessão.
        }
    }

    private static void Connectivity_ConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        if (e.NetworkAccess != NetworkAccess.Internet)
            return;

        CancellationToken token;
        lock (EstadoLock)
        {
            if (_cancelamento is null)
                return;
            token = _cancelamento.Token;
        }

        _ = SincronizarSePossivel(token);
    }

    private static async Task SincronizarSePossivel(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested ||
            SessaoUsuario.UsuarioAtual is null ||
            SessaoUsuario.EhMaster ||
            string.IsNullOrWhiteSpace(SessaoUsuario.ApiToken) ||
            Connectivity.Current.NetworkAccess != NetworkAccess.Internet ||
            !await TravaExecucao.WaitAsync(0))
            return;

        try
        {
            var resultado = await new SincronizacaoService().SincronizarTudo();
            if (!resultado.Sucesso && resultado.RegistrosComErro > 0)
                System.Diagnostics.Debug.WriteLine($"Sincronização automática: {resultado.Mensagem}");
        }
        catch (OperationCanceledException)
        {
            // O usuário encerrou a sessão durante a operação.
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Falha na sincronização automática: {ex.Message}");
        }
        finally
        {
            TravaExecucao.Release();
        }
    }
}
