namespace APP_CELULAR_API.Models;

public sealed class AgendamentoEstadoResponse
{
    public List<AgendamentoCliente> Clientes { get; set; } = [];
    public List<AgendamentoPet> Pets { get; set; } = [];
    public List<AgendamentoProfissional> Profissionais { get; set; } = [];
    public List<AgendamentoServico> Servicos { get; set; } = [];
    public List<AgendamentoMaterial> Materiais { get; set; } = [];
    public List<AgendamentoItem> Agendamentos { get; set; } = [];
}

public sealed class AgendamentoCliente
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = "";
    public string Whatsapp { get; set; } = "";
    public string Observacoes { get; set; } = "";
    public DateTimeOffset CriadoEm { get; set; }
}

public sealed class AgendamentoPet
{
    public Guid Id { get; set; }
    public Guid ClienteId { get; set; }
    public string NomePet { get; set; } = "";
    public string Raca { get; set; } = "";
    public string Porte { get; set; } = "";
    public string ObservacoesAlergias { get; set; } = "";
}

public sealed class AgendamentoProfissional
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = "";
    public string Nicho { get; set; } = "todos";
    public string FotoUrl { get; set; } = "";
    public bool Ativo { get; set; } = true;
}

public sealed class AgendamentoServico
{
    public Guid Id { get; set; }
    public string Nicho { get; set; } = "barbearia";
    public string Nome { get; set; } = "";
    public int DuracaoMinutos { get; set; } = 30;
    public decimal PrecoBrl { get; set; }
    public bool Ativo { get; set; } = true;
}

public sealed class AgendamentoMaterial
{
    public Guid Id { get; set; }
    public string Nicho { get; set; } = "barbearia";
    public string NomeItem { get; set; } = "";
    public decimal Quantidade { get; set; }
    public string Unidade { get; set; } = "unidades";
    public decimal EstoqueMinimo { get; set; } = 5;
    public DateTimeOffset AtualizadoEm { get; set; }
}

public sealed class AgendamentoItem
{
    public Guid Id { get; set; }
    public Guid ClienteId { get; set; }
    public string ClienteNome { get; set; } = "";
    public Guid? PetId { get; set; }
    public string PetNome { get; set; } = "";
    public Guid ServicoId { get; set; }
    public string ServicoNome { get; set; } = "";
    public int DuracaoMinutos { get; set; }
    public Guid ProfissionalId { get; set; }
    public string ProfissionalNome { get; set; } = "";
    public string Whatsapp { get; set; } = "";
    public DateTimeOffset DataHorario { get; set; }
    public string Status { get; set; } = "confirmado";
    public decimal ValorTotal { get; set; }
    public bool MensagemWhatsappEnviada { get; set; }
}

public sealed class AgendamentoClienteRequest
{
    public string Nome { get; set; } = "";
    public string Whatsapp { get; set; } = "";
    public string? Observacoes { get; set; }
}

public sealed class AgendamentoPetRequest
{
    public Guid ClienteId { get; set; }
    public string NomePet { get; set; } = "";
    public string? Raca { get; set; }
    public string? Porte { get; set; }
    public string? ObservacoesAlergias { get; set; }
}

public sealed class AgendamentoProfissionalRequest
{
    public string Nome { get; set; } = "";
    public string Nicho { get; set; } = "todos";
    public string? FotoUrl { get; set; }
}

public sealed class AgendamentoServicoRequest
{
    public string Nicho { get; set; } = "barbearia";
    public string Nome { get; set; } = "";
    public int DuracaoMinutos { get; set; }
    public decimal PrecoBrl { get; set; }
}

public sealed class AgendamentoMaterialRequest
{
    public string Nicho { get; set; } = "barbearia";
    public string NomeItem { get; set; } = "";
    public decimal Quantidade { get; set; }
    public string Unidade { get; set; } = "unidades";
    public decimal EstoqueMinimo { get; set; } = 5;
}

public sealed class CriarAgendamentoRequest
{
    public Guid ClienteId { get; set; }
    public Guid? PetId { get; set; }
    public Guid ServicoId { get; set; }
    public Guid ProfissionalId { get; set; }
    public DateTimeOffset DataHorario { get; set; }
    public string Status { get; set; } = "confirmado";
    public decimal? ValorTotal { get; set; }
}

public sealed class AtualizarStatusAgendamentoRequest
{
    public string Status { get; set; } = "";
}
