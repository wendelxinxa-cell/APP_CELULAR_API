namespace APP_CELULAR_API.Models;

public class LoginApiRequest
{
    public long EmpresaId { get; set; }
    public string Nome { get; set; } = "";
    public string SenhaHash { get; set; } = "";
    public Guid ChaveInstalacao { get; set; }
    public string NomeDispositivo { get; set; } = "";
}

public class LoginSupabaseApiRequest
{
    public long EmpresaId { get; set; }
    public string Identificador { get; set; } = "";
    public string Senha { get; set; } = "";
    public Guid ChaveInstalacao { get; set; }
    public string NomeDispositivo { get; set; } = "";
}

public class LoginApiResponse
{
    public string Nome { get; set; } = "";
    public string Token { get; set; } = "";
    public long EmpresaId { get; set; }
    public long UsuarioId { get; set; }
    public long DispositivoId { get; set; }
    public string Funcao { get; set; } = "USUARIO";
    public string TipoNegocio { get; set; } = "sucata";
    public bool EhMaster { get; set; }
    public DateTimeOffset ExpiraEm { get; set; }
}
