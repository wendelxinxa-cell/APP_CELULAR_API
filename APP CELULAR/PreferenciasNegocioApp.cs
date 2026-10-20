namespace APP_CELULAR;

public static class PreferenciasNegocioApp
{
    public static string Atual => SessaoUsuario.UsuarioAtual?.TipoNegocio == "chat" ? "chat" : "sucata";
}
