using SQLite;

namespace APP_CELULAR.Models;

public class Venda
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public int? IdServidor { get; set; }

    public int PessoaId { get; set; }

    public string PessoaNome { get; set; } = "";

    public DateTime DataVenda { get; set; } = DateTime.Now;

    public decimal ValorTotal { get; set; }

    public string Observacao { get; set; } = "";

    public string Status { get; set; } = "ABERTA";

    public string StatusSincronizacao { get; set; } = "PENDENTE";

    public string MensagemErro { get; set; } = "";

    public DateTime DataAlteracao { get; set; } = DateTime.Now;

    public long EmpresaId { get; set; } = 1;

    public long DispositivoId { get; set; } = 1;

    public bool Excluido { get; set; } = false;
}