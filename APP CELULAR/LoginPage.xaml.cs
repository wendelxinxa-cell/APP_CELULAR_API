using APP_CELULAR.Data;
using APP_CELULAR.Models;
using APP_CELULAR.Services;
using System.Reflection;

namespace APP_CELULAR;

public partial class LoginPage : ContentPage
{
    private const string ChaveUltimoUsuario = "LOGIN_ULTIMO_USUARIO";
    private const string ChaveUltimaEmpresa = "LOGIN_ULTIMA_EMPRESA";
    private const string EmailUsuarioZeus = "wendelfrank.dev@gmail.com";
    private readonly AppDatabase _database;
    private readonly BiometriaService _biometria;
    private readonly ApiService _apiService;

    private Usuario? _usuarioBiometria;
    private string? _usuarioLembrado;
    private long _empresaLembrada;

    public LoginPage()
    {
        InitializeComponent();

        var buildExibido = typeof(LoginPage).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(x => x.Key == "ChatCatReleaseBuild")?.Value ?? AppInfo.Current.BuildString;
        VersionLabel.Text = $"Versão {AppInfo.Current.VersionString} • build {buildExibido}";

        _database = new AppDatabase();
        _biometria = new BiometriaService();
        _apiService = new ApiService();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        CarregarUsuarioLembrado();
        await CarregarBiometria();
        var progresso = new Progress<AtualizacaoProgresso>(AtualizarProgresso);
        await new AtualizacaoAppService().VerificarNaTelaLogin(this, progresso);
    }

    private void AtualizarProgresso(AtualizacaoProgresso progresso)
    {
        AtualizacaoPanel.IsVisible = true;
        AtualizacaoStatusLabel.Text = progresso.Etapa;
        AtualizacaoProgressBar.IsVisible = progresso.Percentual >= 0;
        if (progresso.Percentual >= 0)
            AtualizacaoProgressBar.Progress = Math.Clamp(progresso.Percentual, 0, 1);
    }

    // =========================================================
    // LOGIN
    // =========================================================

    private async void OnLoginClicked(
        object sender,
        EventArgs e)
    {
        string usuario = UsernameFrame.IsVisible
            ? UsernameEntry.Text?.Trim() ?? ""
            : _usuarioLembrado?.Trim() ?? "";

        string senha =
            PasswordEntry.Text ?? "";

        if (string.IsNullOrWhiteSpace(usuario))
        {
            await DisplayAlert(
                "Login",
                "Informe seu nome ou e-mail.",
                "OK");

            UsernameEntry.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(senha))
        {
            await DisplayAlert(
                "Login",
                "Informe a senha.",
                "OK");

            PasswordEntry.Focus();
            return;
        }

        try
        {
            UsuarioAtualizando(true);

            // As credenciais globais são verificadas no servidor e nunca ficam
            // embutidas no APK nem salvas como uma conta local compartilhada.
            if (string.Equals(usuario, "Zeus", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(usuario, EmailUsuarioZeus, StringComparison.OrdinalIgnoreCase))
            {
                SessaoUsuario.Encerrar();
                SessaoUsuario.TokenMaster = await new MasterEmpresasApiService().Login(usuario, senha);
                SessaoUsuario.UsuarioAtual = new Usuario
                {
                    Nome = "Zeus",
                    Funcao = "Deus",
                    EmpresaId = 0
                };
                SalvarUsuarioLembrado("Zeus", 0);
                PasswordEntry.Text = "";
                EntrarNoSistema();
                return;
            }

            // -------------------------------------------------
            // LOGIN LOCAL
            // -------------------------------------------------

            long empresaId = !UsernameFrame.IsVisible && _empresaLembrada > 0
                ? _empresaLembrada
                : ApiEndpointSettings.GetEmpresaId();
            Usuario? usuarioEncontrado = null;
            try
            {
                var apiLogin = await _apiService.AutenticarSupabase(new LoginSupabaseApiRequest
                {
                    EmpresaId = empresaId,
                    Identificador = usuario,
                    Senha = senha
                });
                var remoto = new Usuario
                {
                    Nome = string.IsNullOrWhiteSpace(apiLogin.Nome) ? usuario : apiLogin.Nome,
                    SenhaHash = _database.CriarHashSenha(senha),
                    EmpresaId = empresaId
                };
                AplicarLoginApi(remoto, apiLogin);
                usuarioEncontrado = await _database.ImportarUsuarioAutenticado(remoto);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Acesso à empresa", ex.Message, "OK");
                PasswordEntry.Text = "";
                PasswordEntry.Focus();
                return;
            }

            // Guarda o usuário logado
            SessaoUsuario.UsuarioAtual =
                usuarioEncontrado;

            SalvarUsuarioLembrado(usuarioEncontrado.Nome, usuarioEncontrado.EmpresaId);

            // -------------------------------------------------
            // OFERECER BIOMETRIA
            // -------------------------------------------------

            bool biometriaDisponivel =
                await _biometria.EstaDisponivel();

            if (biometriaDisponivel &&
                !usuarioEncontrado.BiometriaAtivada)
            {
                bool ativar =
                    await DisplayAlert(
                        "Acesso rápido",
                        "Deseja ativar a biometria para entrar mais rapidamente nas próximas vezes?",
                        "Ativar",
                        "Agora não");

                if (ativar)
                {
                    // Primeiro confirma a biometria.
                    bool autenticou =
                        await _biometria.Autenticar();

                    if (autenticou)
                    {
                        // Só agora grava como ativada.
                        await _database.DefinirBiometria(
                            usuarioEncontrado.Id,
                            true);
                        usuarioEncontrado.BiometriaAtivada = true;

                        Preferences.Default.Set(
                            "BIOMETRIA_USUARIO",
                            usuarioEncontrado.Nome);
                        Preferences.Default.Set(
                            "BIOMETRIA_EMPRESA",
                            usuarioEncontrado.EmpresaId);
                        _usuarioBiometria = usuarioEncontrado;

                        /*   await DisplayAlert(
                               "Biometria ativada",
                               "A biometria foi cadastrada para este usuário.",
                               "OK");*/
                    }
                    else
                    {
                        // Não autentificou = não salva.
                        await _database.DefinirBiometria(
                            usuarioEncontrado.Id,
                            false);
                        usuarioEncontrado.BiometriaAtivada = false;

                        Preferences.Default.Remove(
                            "BIOMETRIA_USUARIO");

                        /*  await DisplayAlert(
                              "Biometria",
                              "A biometria não foi ativada.",
                              "OK"); */
                    }
                }
            }
            else if (biometriaDisponivel && usuarioEncontrado.BiometriaAtivada)
            {
                Preferences.Default.Set("BIOMETRIA_USUARIO", usuarioEncontrado.Nome);
                Preferences.Default.Set("BIOMETRIA_EMPRESA", usuarioEncontrado.EmpresaId);
                _usuarioBiometria = usuarioEncontrado;
            }
            await AtualizarBotaoBiometria();
            EntrarNoSistema();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Erro",
                $"Não foi possível realizar o login.\n\n{ex.Message}",
                "OK");
        }
        finally
        {
            UsuarioAtualizando(false);
        }
    }

    // =========================================================
    // CARREGAR BIOMETRIA
    // =========================================================

    private async Task CarregarBiometria()
    {
        try
        {
            BiometriaButton.IsVisible = false;
            _usuarioBiometria = null;

            if (string.IsNullOrWhiteSpace(_usuarioLembrado) || _empresaLembrada <= 0)
                return;

            string nomeUsuario =
                Preferences.Default.Get(
                    "BIOMETRIA_USUARIO",
                    "");

            long empresaBiometria = Preferences.Default.Get(
                "BIOMETRIA_EMPRESA",
                ApiEndpointSettings.GetEmpresaId());

            if (string.IsNullOrWhiteSpace(nomeUsuario) ||
                !string.Equals(nomeUsuario, _usuarioLembrado, StringComparison.OrdinalIgnoreCase) ||
                empresaBiometria != _empresaLembrada)
                return;

            var usuario =
                await _database.ObterUsuarioPorNome(
                    nomeUsuario,
                    empresaBiometria);

            if (usuario == null)
            {
                Preferences.Default.Remove(
                    "BIOMETRIA_USUARIO");

                return;
            }

            if (!usuario.BiometriaAtivada)
                return;

            bool disponivel =
                await _biometria.EstaDisponivel();

            if (!disponivel)
                return;

            _usuarioBiometria = usuario;
            BiometriaButton.IsVisible = true;
        }
        catch
        {
            // Não impede o login por senha.
        }
    }

    private void CarregarUsuarioLembrado()
    {
        _usuarioLembrado = Preferences.Default.Get(ChaveUltimoUsuario, "").Trim();
        _empresaLembrada = Preferences.Default.Get(ChaveUltimaEmpresa, 0L);
        bool temUsuarioSalvo = !string.IsNullOrWhiteSpace(_usuarioLembrado);

        UsernameFrame.IsVisible = !temUsuarioSalvo;
        UsuarioSalvoFrame.IsVisible = temUsuarioSalvo;
        AlterarUsuarioLabel.IsVisible = temUsuarioSalvo;
        UsuarioSalvoLabel.Text = temUsuarioSalvo ? _usuarioLembrado : "";
        if (!temUsuarioSalvo)
            UsernameEntry.Focus();
    }

    private void SalvarUsuarioLembrado(string nome, long empresaId)
    {
        _usuarioLembrado = nome.Trim();
        _empresaLembrada = empresaId;
        Preferences.Default.Set(ChaveUltimoUsuario, _usuarioLembrado);
        Preferences.Default.Set(ChaveUltimaEmpresa, _empresaLembrada);

        UsernameFrame.IsVisible = false;
        UsuarioSalvoFrame.IsVisible = true;
        AlterarUsuarioLabel.IsVisible = true;
        UsuarioSalvoLabel.Text = _usuarioLembrado;
    }

    private async Task AtualizarBotaoBiometria()
    {
        BiometriaButton.IsVisible = false;
        if (_usuarioBiometria is null ||
            !string.Equals(_usuarioBiometria.Nome, _usuarioLembrado, StringComparison.OrdinalIgnoreCase) ||
            _usuarioBiometria.EmpresaId != _empresaLembrada ||
            !_usuarioBiometria.BiometriaAtivada)
            return;

        BiometriaButton.IsVisible = await _biometria.EstaDisponivel();
    }

    private async void AlterarUsuario_Clicked(object sender, EventArgs e)
    {
        Preferences.Default.Remove(ChaveUltimoUsuario);
        Preferences.Default.Remove(ChaveUltimaEmpresa);
        _usuarioLembrado = null;
        _empresaLembrada = 0;
        _usuarioBiometria = null;
        PasswordEntry.Text = "";
        UsernameEntry.Text = "";
        BiometriaButton.IsVisible = false;
        CarregarUsuarioLembrado();
        await Task.Yield();
        UsernameEntry.Focus();
    }

    // =========================================================
    // ENTRAR COM BIOMETRIA
    // =========================================================

    private async void Biometria_Clicked(
        object sender,
        EventArgs e)
    {
        try
        {
            if (_usuarioBiometria == null)
            {
                await CarregarBiometria();
            }

            if (_usuarioBiometria == null)
            {
                await DisplayAlert(
                    "Biometria",
                    "Nenhum usuário configurado para acesso biométrico.",
                    "OK");

                return;
            }

            bool sucesso =
                await _biometria.Autenticar();

            if (!sucesso)
                return;

            // Recarrega do banco
            var usuario =
                await _database.ObterUsuario(
                    _usuarioBiometria.Id,
                    _usuarioBiometria.EmpresaId);

            if (usuario == null)
            {
                await DisplayAlert(
                    "Biometria",
                    "Usuário não encontrado.",
                    "OK");

                return;
            }

            if (!usuario.BiometriaAtivada)
            {
                await DisplayAlert(
                    "Biometria",
                    "A biometria não está ativada para este usuário.",
                    "OK");

                return;
            }

            SessaoUsuario.UsuarioAtual =
                usuario;

            if (!usuario.EhMaster && !SessaoUsuario.EhDeus)
            {
                string? erroApi = await AutenticarNaApi(usuario);
                if (AcessoNegadoPelaApi(erroApi))
                {
                    SessaoUsuario.Encerrar();
                    await DisplayAlert("Acesso à empresa", erroApi!, "OK");
                    return;
                }
            }

            EntrarNoSistema();
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Biometria",
                $"Não foi possível autenticar.\n\n{ex.Message}",
                "OK");
        }
    }

    // =========================================================
    // CADASTRE-SE
    // =========================================================
    private async void CadastreSe_Clicked(object sender, EventArgs e)
    {
        try
        {
            await Launcher.Default.OpenAsync(new Uri("https://catchat-razor-test.onrender.com/solicitar-acesso"));
        }
        catch (Exception ex)
        {
            await DisplayAlert(
                "Erro",
                $"Não foi possível abrir o cadastro.\n\n{ex.Message}",
                "OK");
        }
    }

    private async void EsqueciSenha_Clicked(object sender, EventArgs e)
    {
        try
        {
            await Launcher.Default.OpenAsync(new Uri("https://catchat-razor-test.onrender.com/login?recovery=1"));
        }
        catch (Exception ex)
        {
            await DisplayAlert("Recuperar senha", $"Não foi possível abrir a recuperação de senha.\n\n{ex.Message}", "OK");
        }
    }

    // =========================================================
    // ENTRAR NO SISTEMA
    // =========================================================

    private void EntrarNoSistema()
    {
        PaletasAppService.AplicarPreferenciaAtual();
        if (SessaoUsuario.EhMaster || SessaoUsuario.EhDeus)
            SincronizacaoAutomaticaService.Parar();
        else
            SincronizacaoAutomaticaService.Iniciar();

        Application.Current!.Windows[0].Page =
            new AppShell();
    }

    private async Task<string?> AutenticarNaApi(Usuario usuario)
    {
        SessaoUsuario.ApiToken = null;
        try
        {
            var resposta = await _apiService.Autenticar(new LoginApiRequest
            {
                EmpresaId = usuario.EmpresaId,
                Nome = usuario.Nome,
                SenhaHash = usuario.SenhaHash
            });
            AplicarLoginApi(usuario, resposta);
            await _database.MarcarUsuarioSincronizado(usuario.Id, resposta.UsuarioId, resposta.TipoNegocio);
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"API indisponível ou usuário sem acesso à empresa: {ex.Message}");
            return ex.Message;
        }
    }

    private static void AplicarLoginApi(Usuario usuario, LoginApiResponse resposta)
    {
        usuario.EmpresaId = resposta.EmpresaId;
        usuario.IdServidor = resposta.UsuarioId;
        usuario.Funcao = resposta.Funcao;
        usuario.TipoNegocio = resposta.TipoNegocio == "chat" ? "chat" : "sucata";
        usuario.EhMaster = resposta.EhMaster;
        SessaoUsuario.ApiToken = resposta.Token;
        SessaoUsuario.ApiTokenExpiraEm = resposta.ExpiraEm;
        SessaoUsuario.DispositivoId = resposta.DispositivoId;
    }

    private static bool AcessoNegadoPelaApi(string? erro) =>
        erro?.Contains("(401)", StringComparison.OrdinalIgnoreCase) == true ||
        erro?.Contains("(403)", StringComparison.OrdinalIgnoreCase) == true ||
        erro?.Contains("(404)", StringComparison.OrdinalIgnoreCase) == true;

    // =========================================================
    // CONTROLE DOS CAMPOS
    // =========================================================

    private void UsuarioAtualizando(
        bool carregando)
    {
        UsernameEntry.IsEnabled =
            !carregando;

        PasswordEntry.IsEnabled =
            !carregando;
    }

    // =========================================================
    // MOSTRAR / OCULTAR SENHA
    // =========================================================

    private void MostrarSenha_Clicked(
        object sender,
        EventArgs e)
    {
        PasswordEntry.IsPassword =
            !PasswordEntry.IsPassword;
        MostrarSenhaButton.Text = PasswordEntry.IsPassword ? "👁" : "🙈";
        SemanticProperties.SetDescription(
            MostrarSenhaButton,
            PasswordEntry.IsPassword ? "Mostrar senha" : "Ocultar senha");
    }

    // =========================================================
    // USAR SENHA
    // =========================================================

    private void UsarSenha_Clicked(
        object sender,
        EventArgs e)
    {
        UsernameEntry.Focus();
    }

}
