using SQLite;

namespace APP_CELULAR.Models;

public class ItemCompra
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public int CompraId { get; set; }

    public int MaterialId { get; set; }

    public string MaterialNome { get; set; } = "";

    public string Unidade { get; set; } = "KG";

    // Quantidade comprada / peso bruto
    public decimal Quantidade { get; set; }

    public decimal PrecoUnitario { get; set; }

    public decimal ValorTotal { get; set; }

    // Quantidade perdida durante limpeza/beneficiamento
    public decimal QuantidadePerda { get; set; } = 0;

    // Quantidade que efetivamente entrará no estoque
    public decimal QuantidadeLiquida { get; set; } = 0;

    // Impede lançar a mesma entrada no estoque duas vezes
    public bool EstoqueLancado { get; set; } = false;

    public int? IdServidor { get; set; }

    public long EmpresaId { get; set; } = 1;

    public long DispositivoId { get; set; } = 1;

    public DateTime DataAlteracao { get; set; } = DateTime.Now;

    public string StatusSincronizacao { get; set; } = "PENDENTE";

    public string MensagemErro { get; set; } = "";

    public bool Excluido { get; set; } = false;
}