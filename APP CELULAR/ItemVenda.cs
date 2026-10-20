using SQLite;

namespace APP_CELULAR.Models;

public class ItemVenda
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public int VendaId { get; set; }

    public int MaterialId { get; set; }

    public string MaterialNome { get; set; } = "";

    public string Unidade { get; set; } = "KG";

    public decimal Quantidade { get; set; }

    public decimal PrecoUnitario { get; set; }

    public decimal ValorTotal { get; set; }

    public int? IdServidor { get; set; }

    public long EmpresaId { get; set; } = 1;

    public long DispositivoId { get; set; } = 1;

    public DateTime DataAlteracao { get; set; } = DateTime.Now;

    public string StatusSincronizacao { get; set; } = "PENDENTE";

    public string MensagemErro { get; set; } = "";

    public bool Excluido { get; set; } = false;
}