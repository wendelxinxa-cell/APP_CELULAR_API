namespace APP_CELULAR_API.Models;

public sealed class FinanceiroSyncRequest
{
    public long EmpresaId { get; set; }
    public List<FinanceiroTipoDto> Tipos { get; set; } = [];
    public List<FinanceiroRecorrenciaDto> Recorrencias { get; set; } = [];
    public List<FinanceiroLancamentoDto> Lancamentos { get; set; } = [];
}

public sealed class FinanceiroSyncResponse
{
    public List<FinanceiroTipoDto> Tipos { get; set; } = [];
    public List<FinanceiroRecorrenciaDto> Recorrencias { get; set; } = [];
    public List<FinanceiroLancamentoDto> Lancamentos { get; set; } = [];
}

public sealed class FinanceiroTipoDto
{
    public string Chave { get; set; } = "";
    public string Nome { get; set; } = "";
    public string Cor { get; set; } = "#6C63FF";
    public DateTime DataAlteracao { get; set; }
    public bool Excluido { get; set; }
}

public sealed class FinanceiroRecorrenciaDto
{
    public string Chave { get; set; } = "";
    public string TipoChave { get; set; } = "";
    public string TipoNome { get; set; } = "";
    public string Natureza { get; set; } = "PAGAR";
    public string Descricao { get; set; } = "";
    public string Observacao { get; set; } = "";
    public decimal Valor { get; set; }
    public int DiaVencimento { get; set; }
    public int IntervaloMeses { get; set; }
    public DateTime DataInicio { get; set; }
    public DateTime? DataFim { get; set; }
    public bool Ativa { get; set; }
    public DateTime DataAlteracao { get; set; }
    public bool Excluido { get; set; }
}

public sealed class FinanceiroLancamentoDto
{
    public string Chave { get; set; } = "";
    public string? RecorrenciaChave { get; set; }
    public string TipoChave { get; set; } = "";
    public string TipoNome { get; set; } = "";
    public string Natureza { get; set; } = "PAGAR";
    public string TipoLancamento { get; set; } = "VARIAVEL";
    public string Descricao { get; set; } = "";
    public string Observacao { get; set; } = "";
    public decimal Valor { get; set; }
    public DateTime DataVencimento { get; set; }
    public DateTime? DataLiquidacao { get; set; }
    public string Status { get; set; } = "PENDENTE";
    public DateTime DataAlteracao { get; set; }
    public bool Excluido { get; set; }
}
