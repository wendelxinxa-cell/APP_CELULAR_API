namespace APP_CELULAR_API.Models;

public sealed class LoginMasterRequest
{
    public string Usuario { get; set; } = "";
    public string Senha { get; set; } = "";
}

public sealed class CadastroEmpresaRequest
{
    public string Nome { get; set; } = "";
    public string EmailResponsavel { get; set; } = "";
    public string ConnectionString { get; set; } = "";
}

public sealed class EmpresaCatalogoResponse
{
    public long Id { get; set; }
    public string Nome { get; set; } = "";
    public string EmailResponsavel { get; set; } = "";
    public string Status { get; set; } = "PENDENTE";
    public DateTimeOffset CriadaEm { get; set; }
}
