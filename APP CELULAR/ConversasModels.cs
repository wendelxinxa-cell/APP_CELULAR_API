using System.Text.Json.Serialization;

namespace APP_CELULAR.Services;

public class LoginApiRequest
{
    public long EmpresaId { get; set; }
    public string Nome { get; set; } = "";
    public string SenhaHash { get; set; } = "";
    public Guid ChaveInstalacao { get; set; }
    public string NomeDispositivo { get; set; } = "";
}

public class LoginSupabaseApiRequest
{
    public long EmpresaId { get; set; }
    public string Identificador { get; set; } = "";
    public string Senha { get; set; } = "";
    public Guid ChaveInstalacao { get; set; }
    public string NomeDispositivo { get; set; } = "";
}

public class LoginApiResponse
{
    public string Nome { get; set; } = "";
    public string Token { get; set; } = "";
    public long EmpresaId { get; set; }
    public long UsuarioId { get; set; }
    public long DispositivoId { get; set; }
    public string Funcao { get; set; } = "USUARIO";
    public string TipoNegocio { get; set; } = "sucata";
    public bool EhMaster { get; set; }
    public DateTimeOffset ExpiraEm { get; set; }
}

public sealed class AprovacoesAcessoResponse
{
    public List<AprovacaoAcessoItem> Usuarios { get; set; } = new();
    public List<AprovacaoAcessoItem> Dispositivos { get; set; } = new();
}

public sealed class StatusUsuarioAcesso
{
    public long Id { get; set; }
    public int? IdLocal { get; set; }
    public long EmpresaId { get; set; }
    public string Empresa { get; set; } = "";
    public string Nome { get; set; } = "";
    public string Funcao { get; set; } = "";
    public bool Aprovado { get; set; }
    public bool Bloqueado { get; set; }
    public bool AparelhoPendente { get; set; }
    public string TipoNegocio { get; set; } = "sucata";
    public string Email { get; set; } = "";
}

public sealed class MasterChatConversa
{
    public long EmpresaId { get; set; }
    public string Empresa { get; set; } = "";
    public long UsuarioId { get; set; }
    public string Usuario { get; set; } = "";
    public long ContatoId { get; set; }
    public string Contato { get; set; } = "";
    public string UltimaMensagem { get; set; } = "";
    public DateTime DataUltimaMensagem { get; set; }
    public int NaoLidas { get; set; }
}

public sealed class AprovacaoAcessoItem
{
    public long Id { get; set; }
    public long? UsuarioId { get; set; }
    public long EmpresaId { get; set; }
    public string Empresa { get; set; } = "";
    public string Nome { get; set; } = "";
    public string Usuario { get; set; } = "";
    public string Funcao { get; set; } = "";
    public DateTime? CriadoEm { get; set; }
    public string Descricao { get; set; } = "";
    public string FuncaoExibicao => string.Join(" · ", new[] { Empresa, Funcao }.Where(x => !string.IsNullOrWhiteSpace(x)));
}

public class IdentidadeConversa
{
    public long EmpresaId { get; set; }
    public long UsuarioId { get; set; }
}

public class ConversaRequest : IdentidadeConversa
{
    public long ContatoId { get; set; }
    public long DepoisDoId { get; set; }
}

public sealed class UrlUploadMidiaResponse
{
    public string Chave { get; set; } = "";
    public string Url { get; set; } = "";
}

public sealed class UrlMidiaResponse
{
    public string Url { get; set; } = "";
}

public class EnviarMensagemConversa : ConversaRequest
{
    public Guid ClienteMensagemId { get; set; }
    public string Texto { get; set; } = "";
    public string? FotoNome { get; set; }
    public string? FotoTipo { get; set; }
    public string? MidiaChave { get; set; }
    public string? MidiaNome { get; set; }
    public string? MidiaTipo { get; set; }
    public long? MidiaTamanhoBytes { get; set; }
    [JsonIgnore]
    public byte[]? Foto { get; set; }
}

public class MensagemPendente
{
    [SQLite.PrimaryKey]
    public string Chave { get; set; } = Guid.NewGuid().ToString("N");
    public long EmpresaId { get; set; }
    public long UsuarioId { get; set; }
    public long ContatoId { get; set; }
    public string Texto { get; set; } = "";
    public byte[]? Foto { get; set; }
    public string? FotoNome { get; set; }
    public string? FotoTipo { get; set; }
    public DateTime CriadaEm { get; set; } = DateTime.Now;
    public string MensagemErro { get; set; } = "";
}

public class MensagemCacheada
{
    [SQLite.PrimaryKey]
    public string Chave { get; set; } = "";
    public long EmpresaId { get; set; }
    public long UsuarioId { get; set; }
    public long ContatoId { get; set; }
    public long IdServidor { get; set; }
    public Guid? ClienteMensagemId { get; set; }
    public long RemetenteId { get; set; }
    public string RemetenteNome { get; set; } = "";
    public long DestinatarioId { get; set; }
    public string Texto { get; set; } = "";
    public byte[]? Foto { get; set; }
    public string? FotoNome { get; set; }
    public string? FotoTipo { get; set; }
    public DateTime EnviadaEm { get; set; }
    public DateTime? LidaEm { get; set; }
}

public sealed class MensagemHistoricoProtegida
{
    [SQLite.PrimaryKey]
    public string Chave { get; set; } = "";
    public long EmpresaId { get; set; }
    public long UsuarioId { get; set; }
    public long ContatoId { get; set; }
    public long IdServidor { get; set; }
    public Guid? ClienteMensagemId { get; set; }
    public long RemetenteId { get; set; }
    public string RemetenteNome { get; set; } = "";
    public long DestinatarioId { get; set; }
    public DateTime EnviadaEm { get; set; }
    public DateTime? LidaEm { get; set; }
    public string SaltDerivacao { get; set; } = "";
    public bool Ocultar { get; set; } = true;
    public byte[] PayloadCifrado { get; set; } = Array.Empty<byte>();
}

public class PreferenciasConversa
{
    [SQLite.PrimaryKey]
    public string Chave { get; set; } = "";
    public long EmpresaId { get; set; }
    public long UsuarioId { get; set; }
    public long ContatoId { get; set; }
    public string FamiliaFonte { get; set; } = "OpenSansRegular";
    public double TamanhoFonte { get; set; } = 15;
    public string CorMinhasMensagens { get; set; } = "#DCEBFF";
    public string CorMensagensRecebidas { get; set; } = "#F1F5F9";
    public string StatusLocal { get; set; } = "online";
    public string SaltHistoricoProtegido { get; set; } = "";
}

public class WinkPersonalizado
{
    [SQLite.PrimaryKey]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public long UsuarioId { get; set; }
    public string Nome { get; set; } = "Meu wink";
    public string Simbolo { get; set; } = "✨";
}

public class ContatoConversa
{
    public long Id { get; set; }
    public string Nome { get; set; } = "";
    public string NomeOriginal { get; set; } = "";
    public string UltimaMensagem { get; set; } = "";
    public DateTime? DataUltimaMensagem { get; set; }
    public int NaoLidas { get; set; }
}

public sealed class AtualizarApelidoConversa
{
    public long EmpresaId { get; set; }
    public long UsuarioId { get; set; }
    public long ContatoId { get; set; }
    public string? Apelido { get; set; }
}

public class MensagemConversa
{
    public long Id { get; set; }
    public Guid? ClienteMensagemId { get; set; }
    public bool PendenteLocal { get; set; }
    public long RemetenteId { get; set; }
    public string RemetenteNome { get; set; } = "";
    public long DestinatarioId { get; set; }
    public string Texto { get; set; } = "";
    public byte[]? Foto { get; set; }
    public string? FotoNome { get; set; }
    public string? FotoTipo { get; set; }
    public string? MidiaChave { get; set; }
    public string? MidiaNome { get; set; }
    public string? MidiaTipo { get; set; }
    public long? MidiaTamanhoBytes { get; set; }
    public string? FotoUrl { get; set; }
    public DateTime EnviadaEm { get; set; }
    public DateTime? LidaEm { get; set; }
}

public sealed class EnviarAcaoVisualConversa : ConversaRequest
{
    public string Tipo { get; set; } = "WINK";
    public string Simbolo { get; set; } = "⚡";
}

public sealed class AcaoVisualConversa
{
    public string Tipo { get; set; } = "WINK";
    public string Simbolo { get; set; } = "✨";
}
