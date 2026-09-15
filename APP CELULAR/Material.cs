using SQLite;

namespace APP_CELULAR.Models;

public class Material
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public int? IdServidor { get; set; }

    public string Nome { get; set; } = "";

    public string Unidade { get; set; } = "KG";

    public decimal PrecoCompra { get; set; }

    public decimal PrecoVenda { get; set; }

    public decimal EstoqueAtual { get; set; }

    public DateTime DataCadastro { get; set; } = DateTime.Now;

    public DateTime DataAlteracao { get; set; } = DateTime.Now;

    public string StatusSincronizacao { get; set; } = "PENDENTE";

    public string MensagemErro { get; set; } = "";
}