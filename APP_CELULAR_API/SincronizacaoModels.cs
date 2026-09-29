namespace APP_CELULAR_API.Models;

public class SincronizacaoRequest
{
    public long EmpresaId { get; set; }

    public long DispositivoId { get; set; }

    public List<PessoaSync> Pessoas { get; set; } = new();

    public List<MaterialSync> Materiais { get; set; } = new();

    public List<CompraSync> Compras { get; set; } = new();

    public List<ItemCompraSync> ItensCompra { get; set; } = new();

    public List<VendaSync> Vendas { get; set; } = new();

    public List<ItemVendaSync> ItensVenda { get; set; } = new();

    public List<UsuarioSync> Usuarios { get; set; } = new();

    public List<ConfiguracaoSync> Configuracoes { get; set; } = new();
}

public class PessoaSync
{
    public int? IdLocal { get; set; }
    public long? IdServidor { get; set; }

    public string Nome { get; set; } = "";
    public string? Documento { get; set; }
    public string? Telefone { get; set; }
    public string? Email { get; set; }
    public string? Endereco { get; set; }
    public string? Cidade { get; set; }

    public bool Cliente { get; set; }
    public bool Fornecedor { get; set; }

    public DateTime DataCadastro { get; set; }
    public DateTime DataAlteracao { get; set; }

    public string StatusSincronizacao { get; set; } = "PENDENTE";
    public string? MensagemErro { get; set; }

    public bool Excluido { get; set; }
}

public class MaterialSync
{
    public int? IdLocal { get; set; }
    public long? IdServidor { get; set; }

    public string Nome { get; set; } = "";
    public string Unidade { get; set; } = "KG";

    public decimal PrecoCompra { get; set; }
    public decimal PrecoVenda { get; set; }
    public decimal EstoqueAtual { get; set; }

    public DateTime DataCadastro { get; set; }
    public DateTime DataAlteracao { get; set; }

    public string StatusSincronizacao { get; set; } = "PENDENTE";
    public string? MensagemErro { get; set; }

    public bool Excluido { get; set; }
}

public class CompraSync
{
    public int? IdLocal { get; set; }

    public long? IdServidor { get; set; }

    public int PessoaId { get; set; }

    public long? PessoaIdServidor { get; set; }

    public string PessoaNome { get; set; } = "";

    public DateTime DataCompra { get; set; }

    public decimal ValorTotal { get; set; }

    public string? Observacao { get; set; }

    public string Status { get; set; } = "";

    public string StatusSincronizacao { get; set; } = "PENDENTE";

    public string? MensagemErro { get; set; }

    public DateTime DataAlteracao { get; set; }

    public bool Excluido { get; set; }
}
public class ItemCompraSync
{
    public int? IdLocal { get; set; }

    public long? IdServidor { get; set; }

    public int CompraId { get; set; }

    public long? CompraIdServidor { get; set; }

    public int MaterialId { get; set; }

    public long? MaterialIdServidor { get; set; }

    public string MaterialNome { get; set; } = "";

    public string Unidade { get; set; } = "KG";

    public decimal Quantidade { get; set; }

    public decimal PrecoUnitario { get; set; }

    public decimal ValorTotal { get; set; }

    public decimal QuantidadePerda { get; set; }

    public decimal QuantidadeLiquida { get; set; }

    public bool EstoqueLancado { get; set; }

    public string StatusSincronizacao { get; set; } = "PENDENTE";

    public string MensagemErro { get; set; } = "";

    public DateTime DataAlteracao { get; set; }

    public long EmpresaId { get; set; }

    public long DispositivoId { get; set; }

    public bool Excluido { get; set; }
}
public class VendaSync
{
    public int? IdLocal { get; set; }
    public long? IdServidor { get; set; }

    public int PessoaId { get; set; }
    public long? PessoaIdServidor { get; set; }

    public string PessoaNome { get; set; } = "";

    public DateTime DataVenda { get; set; }

    public decimal ValorTotal { get; set; }

    public string? Observacao { get; set; }

    public string Status { get; set; } = "";

    public string StatusSincronizacao { get; set; } = "PENDENTE";

    public string? MensagemErro { get; set; }

    public DateTime DataAlteracao { get; set; }

    public bool Excluido { get; set; }
}

public class ItemVendaSync
{
    public int? IdLocal { get; set; }
    public long? IdServidor { get; set; }

    public int VendaId { get; set; }
    public long? VendaIdServidor { get; set; }

    public int MaterialId { get; set; }
    public long? MaterialIdServidor { get; set; }

    public string MaterialNome { get; set; } = "";
    public string Unidade { get; set; } = "KG";

    public decimal Quantidade { get; set; }
    public decimal PrecoUnitario { get; set; }
    public decimal ValorTotal { get; set; }

    public string StatusSincronizacao { get; set; } = "PENDENTE";

    public string? MensagemErro { get; set; }

    public DateTime DataAlteracao { get; set; }

    public bool Excluido { get; set; }
}

public class UsuarioSync
{
    public int? IdLocal { get; set; }
    public long? IdServidor { get; set; }

    public string Nome { get; set; } = "";

    public string SenhaHash { get; set; } = "";

    public string Funcao { get; set; } = "USUARIO";

    public bool EhMaster { get; set; }

    public string StatusSincronizacao { get; set; } = "PENDENTE";

    public string? MensagemErro { get; set; }

    public DateTime DataCadastro { get; set; }

    public DateTime DataAlteracao { get; set; }

    public bool Excluido { get; set; }
}

public class ConfiguracaoSync
{
    public string Chave { get; set; } = "";

    public string Valor { get; set; } = "";

    public string StatusSincronizacao { get; set; } = "PENDENTE";

    public string? MensagemErro { get; set; }

    public DateTime DataAlteracao { get; set; }

    public bool Excluido { get; set; }
}

public class SincronizacaoResponse
{
    public bool Sucesso { get; set; }

    public string Mensagem { get; set; } = "";

    public List<IdSincronizado> Registros { get; set; } = new();

    public List<ErroSincronizacao> Erros { get; set; } = new();
}

public class IdSincronizado
{
    public string Tabela { get; set; } = "";

    public int? IdLocal { get; set; }

    public long IdServidor { get; set; }
}

public class ErroSincronizacao
{
    public string Tabela { get; set; } = "";

    public int? IdLocal { get; set; }

    public string Mensagem { get; set; } = "";
}