using SQLite;

namespace APP_CELULAR.Models;

public class Usuario
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string Nome { get; set; } = "";

    public string Username { get; set; } = "";

    public string Email { get; set; } = "";

    public string SenhaHash { get; set; } = "";

    public string Funcao { get; set; } = "USUARIO";

    // Identifica a conta ADMINISTRADOR MASTER
    public bool EhMaster { get; set; } = false;
}
