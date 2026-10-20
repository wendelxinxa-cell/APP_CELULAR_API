using SQLite;

namespace APP_CELULAR.Models;

public class Configuracao
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string Segmento { get; set; } = "";

    public bool Configurado { get; set; }

    public long EmpresaId { get; set; } = 1;

    public DateTime DataAlteracao { get; set; } = DateTime.Now;

    public string StatusSincronizacao { get; set; } = "PENDENTE";

    public string MensagemErro { get; set; } = "";

    public bool Excluido { get; set; } = false;
}