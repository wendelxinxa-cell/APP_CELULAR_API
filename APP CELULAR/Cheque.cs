using SQLite;

namespace APP_CELULAR.Models;

public class Cheque
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public int PessoaId { get; set; }

    public string ClienteNome { get; set; } = "";

    public string NumeroCheque { get; set; } = "";

    public DateTime DataEmissao { get; set; }

    public DateTime DataVencimento { get; set; }

    public decimal ValorOriginal { get; set; }

    public decimal SaldoAtual { get; set; }

    public string Status { get; set; } = "ABERTO";
}
