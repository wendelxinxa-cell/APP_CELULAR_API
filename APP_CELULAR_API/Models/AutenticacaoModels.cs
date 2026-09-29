namespace APP_CELULAR_API.Models;

public class LoginApiRequest
{
    public long EmpresaId { get; set; }
    public string Nome { get; set; } = "";
    public string SenhaHash { get; set; } = "";
}

public class LoginApiResponse
{
    public string Token { get; set; } = "";
    public long EmpresaId { get; set; }
    public long UsuarioId { get; set; }
    public DateTimeOffset ExpiraEm { get; set; }
}
