namespace APP_CELULAR_API.Models;

public class LoginApiRequest
{
    public long EmpresaId { get; set; }
    public string Nome { get; set; } = "";
    public string SenhaHash { get; set; } = "";
    public Guid ChaveInstalacao { get; set; }
    public string NomeDispositivo { get; set; } = "";
}

public class LoginApiResponse
{
    public string Token { get; set; } = "";
    public long EmpresaId { get; set; }
    public long UsuarioId { get; set; }
    public long DispositivoId { get; set; }
    public string Funcao { get; set; } = "USUARIO";
    public bool EhMaster { get; set; }
    public DateTimeOffset ExpiraEm { get; set; }
}
