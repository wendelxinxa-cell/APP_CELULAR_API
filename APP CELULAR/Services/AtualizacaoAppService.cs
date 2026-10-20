using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace APP_CELULAR.Services;

public sealed record AtualizacaoProgresso(string Etapa, double Percentual);

public sealed class AtualizacaoAppService
{
    private static readonly SemaphoreSlim VerificacaoEmAndamento = new(1, 1);
    private static DateTime _ultimaVerificacaoUtc;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };

    public async Task VerificarNaTelaLogin(Page pagina, IProgress<AtualizacaoProgresso>? progresso = null)
    {
#if ANDROID
        if (!await VerificacaoEmAndamento.WaitAsync(0)) return;
        try
        {
            if (DateTime.UtcNow - _ultimaVerificacaoUtc < TimeSpan.FromMinutes(30)) return;
            var endpoint = ApiEndpointSettings.Get()?.TrimEnd('/');
            if (string.IsNullOrWhiteSpace(endpoint)) return;

            using var resposta = await _http.GetAsync($"{endpoint}/api/app/atualizacao");
            if (!resposta.IsSuccessStatusCode) return;
            _ultimaVerificacaoUtc = DateTime.UtcNow;

            using var documento = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
            var dados = documento.RootElement;
            var buildDisponivel = dados.GetProperty("build").GetInt32();
            var codigoDisponivel = dados.TryGetProperty("codigoVersao", out var codigo)
                ? codigo.GetInt32()
                : buildDisponivel;
            if (!int.TryParse(AppInfo.Current.BuildString, out var buildInstalado) || codigoDisponivel <= buildInstalado)
                return;

            var versao = dados.GetProperty("versao").GetString() ?? "nova versão";
            var url = dados.GetProperty("url").GetString();
            var sha256Esperado = dados.GetProperty("sha256").GetString();
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(sha256Esperado)) return;

            var desejaAtualizar = await pagina.DisplayAlert(
                "Atualização disponível",
                $"CATCHAT {versao} • build {buildDisponivel} está disponível. Baixar e instalar agora?",
                "Atualizar", "Depois");
            if (!desejaAtualizar) return;

            var arquivoApk = Path.Combine(FileSystem.CacheDirectory, "app-celular-atualizacao.apk");
            var urlDownload = Uri.TryCreate(url, UriKind.Absolute, out var absoluta)
                ? absoluta
                : new Uri($"{endpoint}/{url.TrimStart('/')}");
            progresso?.Report(new("Baixando atualização…", 0));
            using var respostaArquivo = await _http.GetAsync(urlDownload);
            respostaArquivo.EnsureSuccessStatusCode();
            var pacote = await respostaArquivo.Content.ReadAsByteArrayAsync();
            await File.WriteAllBytesAsync(arquivoApk, pacote);
            progresso?.Report(new("Download concluído. Verificando…", 1));

            progresso?.Report(new("Verificando integridade…", 1));
            await using (var arquivo = File.OpenRead(arquivoApk))
            {
                var hashReal = Convert.ToHexString(await SHA256.HashDataAsync(arquivo));
                if (!hashReal.Equals(sha256Esperado, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(arquivoApk);
                    await pagina.DisplayAlert("Atualização", "O arquivo baixado não passou na verificação de integridade. A instalação foi cancelada.", "OK");
                    return;
                }
            }

            var atividade = Platform.CurrentActivity;
            if (atividade is null) return;

            if (OperatingSystem.IsAndroidVersionAtLeast(26) &&
                !atividade.PackageManager!.CanRequestPackageInstalls())
            {
                var abrirConfiguracao = await pagina.DisplayAlert(
                    "Permitir atualização",
                    "O Android precisa permitir que o CATCHAT instale atualizações. Ative “Permitir desta fonte”; ao voltar, confirme a instalação.",
                    "Abrir configuração", "Agora não");
                if (abrirConfiguracao)
                {
                    var configurar = new Android.Content.Intent(Android.Provider.Settings.ActionManageUnknownAppSources,
                        Android.Net.Uri.Parse($"package:{atividade.PackageName}"));
                    atividade.StartActivity(configurar);
                }
                progresso?.Report(new("Permissão necessária nas configurações do Android.", 1));
                return;
            }

            var apk = new Java.IO.File(arquivoApk);
            var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(
                atividade, $"{atividade.PackageName}.fileProvider", apk);
            var instalar = new Android.Content.Intent(Android.Content.Intent.ActionInstallPackage);
            instalar.SetDataAndType(uri, "application/vnd.android.package-archive");
            instalar.AddFlags(Android.Content.ActivityFlags.GrantReadUriPermission |
                              Android.Content.ActivityFlags.NewTask |
                              Android.Content.ActivityFlags.ClearTop);
            instalar.ClipData = Android.Content.ClipData.NewRawUri("APP_CELULAR_APK", uri);
            progresso?.Report(new("Abrindo instalador do Android…", 1));
            atividade.StartActivity(instalar);
        }
        catch (Exception ex)
        {
            progresso?.Report(new($"Falha na atualização: {ex.Message}", 0));
            await pagina.DisplayAlert("Falha na atualização", ex.ToString(), "OK");
        }
        finally
        {
            VerificacaoEmAndamento.Release();
        }
#else
        await Task.CompletedTask;
#endif
    }
}

