using APP_CELULAR.Models;

namespace APP_CELULAR;

public static class SessaoUsuario
{
    public static Usuario? UsuarioAtual { get; set; }

    public static bool EhMaster
    {
        get
        {
            return UsuarioAtual?.EhMaster == true;
        }
    }

    public static bool EhAdministrador
    {
        get
        {
            if (EhMaster)
                return true;

            return UsuarioAtual?.Funcao
                ?.Trim()
                .ToUpperInvariant() == "ADMINISTRADOR";
        }
    }

    public static bool EhUsuario
    {
        get
        {
            return !EhAdministrador;
        }
    }

    public static void Encerrar()
    {
        UsuarioAtual = null;
    }
}