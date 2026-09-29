namespace APP_CELULAR_API.Models;

public class IdentidadeConversaRequest
{
    public long EmpresaId { get; set; }
    public long UsuarioId { get; set; }
}

public class ConversaRequest : IdentidadeConversaRequest
{
    public long ContatoId { get; set; }
}

public class EnviarMensagemRequest : ConversaRequest
{
    public string Texto { get; set; } = "";
    public string? FotoNome { get; set; }
    public string? FotoTipo { get; set; }
    public byte[]? Foto { get; set; }
}
