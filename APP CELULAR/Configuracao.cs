using SQLite;

namespace APP_CELULAR.Models;

public class Configuracao
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string Segmento { get; set; } = "";

    public bool Configurado { get; set; }
}