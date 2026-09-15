using SQLite;

namespace APP_CELULAR.Models;

public class Pessoa
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public int? IdServidor { get; set; }

    public string Nome { get; set; } = "";

    public string Documento { get; set; } = "";

    public string Telefone { get; set; } = "";

    public string Email { get; set; } = "";

    public string Endereco { get; set; } = "";

    public string Cidade { get; set; } = "";

    public bool Cliente { get; set; }

    public bool Fornecedor { get; set; }

    public DateTime DataCadastro { get; set; } = DateTime.Now;

    public DateTime DataAlteracao { get; set; } = DateTime.Now;

    public string StatusSincronizacao { get; set; } = "PENDENTE";

    public string MensagemErro { get; set; } = "";
}