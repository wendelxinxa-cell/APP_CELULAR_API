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
}