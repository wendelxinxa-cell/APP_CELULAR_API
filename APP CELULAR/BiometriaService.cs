using Plugin.Maui.Biometric;

namespace APP_CELULAR.Services;

public class BiometriaService
{
    private readonly IBiometric _biometric;

    public BiometriaService()
    {
        _biometric =
            BiometricAuthenticationService.Default;
    }

    public async Task<bool> EstaDisponivel()
    {
        try
        {
            var status =
                await _biometric.GetAuthenticationStatusAsync();

            return status == BiometricHwStatus.Success;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Erro ao verificar biometria: {ex}");

            return false;
        }
    }

    public async Task<bool> Autenticar()
    {
        try
        {
            var request = new AuthenticationRequest
            {
                Title = "Entrar no CATCHAT",

                Subtitle =
                    "Confirme sua identidade",

                Description =
                    "Use sua impressão digital ou reconhecimento facial para continuar.",

                NegativeText = "Cancelar",

                AllowPasswordAuth = false,

                AuthStrength =
                    AuthenticatorStrength.Strong
            };

            var resultado =
                await _biometric.AuthenticateAsync(
                    request,
                    CancellationToken.None);

            return
                resultado.Status ==
                BiometricResponseStatus.Success;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Erro na autenticação biométrica: {ex}");

            return false;
        }
    }
}
