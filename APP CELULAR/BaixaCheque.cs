using SQLite;

namespace APP_CELULAR.Models;

public class BaixaCheque
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public int ChequeId { get; set; }

    public DateTime DataBaixa { get; set; }

    public decimal Valor { get; set; }

    public string Observacao { get; set; } = "";
}
