using SQLite;

namespace APP_CELULAR.Models;

public class HistoricoCheque
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public int ChequeId { get; set; }

    public DateTime DataMovimento { get; set; }

    public string Tipo { get; set; } = "";

    public string Descricao { get; set; } = "";
}