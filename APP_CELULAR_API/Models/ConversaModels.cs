namespace APP_CELULAR_API.Models;

public class IdentidadeConversaRequest
{
    public long EmpresaId { get; set; }
    public long UsuarioId { get; set; }
}

public class ConversaRequest : IdentidadeConversaRequest
{
    public long ContatoId { get; set; }
    public long DepoisDoId { get; set; }
}

public sealed class ApelidoConversaRequest : ConversaRequest
{
    public string? Apelido { get; set; }
}

public class EnviarMensagemRequest : ConversaRequest
{
    public Guid ClienteMensagemId { get; set; }
    public string Texto { get; set; } = "";
    public string? FotoNome { get; set; }
    public string? FotoTipo { get; set; }
    public byte[]? Foto { get; set; }
}

public sealed class EnviarAcaoVisualRequest : ConversaRequest
{
    public string Tipo { get; set; } = "WINK";
    public string Simbolo { get; set; } = "⭐";
}

public sealed class AcaoVisualConversaResponse
{
    public string Tipo { get; set; } = "WINK";
    public string Simbolo { get; set; } = "⭐";
}
