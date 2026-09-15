using SQLite;

namespace APP_CELULAR.Models;

public class Compra
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public int? IdServidor { get; set; }

    public int PessoaId { get; set; }

    public string PessoaNome { get; set; } = "";

    public DateTime DataCompra { get; set; } = DateTime.Now;

    public decimal ValorTotal { get; set; }

    public string Observacao { get; set; } = "";

    public string Status { get; set; } = "ABERTA";

    public string StatusSincronizacao { get; set; } = "PENDENTE";

    public string MensagemErro { get; set; } = "";

  
}