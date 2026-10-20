using SQLite;

namespace APP_CELULAR.Models;

public class Usuario
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string Nome { get; set; } = "";

    public string SenhaHash { get; set; } = "";

    public string Funcao { get; set; } = "USUARIO";

    public string TipoNegocio { get; set; } = "sucata";

    public bool EhMaster { get; set; } = false;

    public bool BiometriaAtivada { get; set; } = false;

    public long? IdServidor { get; set; }

    public string StatusSincronizacao { get; set; } = "PENDENTE";

    public string MensagemErro { get; set; } = "";

    public DateTime DataCadastro { get; set; } = DateTime.Now;

    public DateTime DataAlteracao { get; set; } = DateTime.Now;

    public bool Excluido { get; set; } = false;

    public long EmpresaId { get; set; } = 1;

    [Ignore]
    public string StatusAcesso { get; set; } = "PENDENTE";

    [Ignore]
    public string CorStatusAcesso => StatusAcesso switch
    {
        "LIBERADO" => "#238636",
        "BLOQUEADO" => "#D1242F",
        _ => "#F28C28"
    };
}
