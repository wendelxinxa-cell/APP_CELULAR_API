using SQLite;

namespace APP_CELULAR.Models;

public class ProrrogacaoCheque
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public int ChequeId { get; set; }

    public DateTime VencimentoAnterior { get; set; }

    public DateTime NovoVencimento { get; set; }

    public string Observacao { get; set; } = "";
}
