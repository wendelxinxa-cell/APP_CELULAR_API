using APP_CELULAR_API.Models;
using APP_CELULAR_API.Services;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using NpgsqlTypes;

namespace APP_CELULAR_API.Controllers;

[ApiController]
[Route("api/conversas")]
public class ConversasController : ControllerBase
{
    private const int MaxFotoBytes = 8 * 1024 * 1024;
    private readonly IEmpresaDatabaseResolver _resolver;
    private readonly TenantSessionStore _sessions;

    public ConversasController(IEmpresaDatabaseResolver resolver, TenantSessionStore sessions)
    {
        _resolver = resolver;
        _sessions = sessions;
    }

    [HttpPost("contatos")]
    public async Task<IActionResult> Contatos([FromBody] IdentidadeConversaRequest request)
    {
        if (!ValidarIdentidadeInput(request)) return BadRequest(new { mensagem = "Identidade inválida." });
        await using var db = await AbrirEValidar(request);
        if (db is null) return Unauthorized(new { mensagem = "Usuário inválido para esta empresa." });

        const string sql = """
            SELECT u.id, u.nome,
                   COALESCE(m.texto, CASE WHEN m.foto IS NOT NULL THEN '[Foto]' ELSE '' END) AS ultima_mensagem,
                   m.enviada_em AS data_ultima_mensagem,
                   (SELECT COUNT(*)::int FROM app.mensagem_conversa n
                    WHERE n.empresa_id = @empresaId AND n.remetente_id = u.id
                      AND n.destinatario_id = @usuarioId AND n.lida_em IS NULL) AS nao_lidas
            FROM app.usuario u
            LEFT JOIN LATERAL (
                SELECT mc.texto, mc.foto, mc.enviada_em
                FROM app.mensagem_conversa mc
                WHERE mc.empresa_id = @empresaId
                  AND ((mc.remetente_id = @usuarioId AND mc.destinatario_id = u.id)
                    OR (mc.remetente_id = u.id AND mc.destinatario_id = @usuarioId))
                ORDER BY mc.enviada_em DESC, mc.id DESC LIMIT 1
            ) m ON TRUE
            WHERE u.empresa_id = @empresaId AND u.id <> @usuarioId AND COALESCE(u.excluido, FALSE) = FALSE
            ORDER BY m.enviada_em DESC NULLS LAST, u.nome;
            """;
        await using var cmd = new NpgsqlCommand(sql, db);
        AddIdentity(cmd, request);
        await using var reader = await cmd.ExecuteReaderAsync();
        var result = new List<object>();
        while (await reader.ReadAsync())
            result.Add(new { id = reader.GetInt64(0), nome = reader.GetString(1), ultimaMensagem = reader.GetString(2), dataUltimaMensagem = reader.IsDBNull(3) ? (DateTime?)null : reader.GetDateTime(3), naoLidas = reader.GetInt32(4) });
        return Ok(result);
    }

    [HttpPost("listar")]
    public async Task<IActionResult> Listar([FromBody] ConversaRequest request)
    {
        if (!ValidarRequest(request)) return BadRequest(new { mensagem = "Conversa inválida." });
        await using var db = await AbrirEValidar(request);
        if (db is null) return Unauthorized(new { mensagem = "Usuário inválido para esta empresa." });
        if (!await ContatoValido(db, request.EmpresaId, request.ContatoId)) return BadRequest(new { mensagem = "Contato inválido." });

        const string sql = """
            SELECT m.id, m.remetente_id, r.nome, m.destinatario_id, m.texto,
                   m.foto, m.foto_nome, m.foto_tipo, m.enviada_em, m.lida_em
            FROM app.mensagem_conversa m
            JOIN app.usuario r ON r.id = m.remetente_id AND r.empresa_id = m.empresa_id
            WHERE m.empresa_id = @empresaId
              AND ((m.remetente_id = @usuarioId AND m.destinatario_id = @contatoId)
                OR (m.remetente_id = @contatoId AND m.destinatario_id = @usuarioId))
            ORDER BY m.enviada_em, m.id;
            """;
        await using var cmd = new NpgsqlCommand(sql, db);
        AddIdentity(cmd, request); cmd.Parameters.AddWithValue("contatoId", request.ContatoId);
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<object>();
        while (await reader.ReadAsync())
            rows.Add(new { id = reader.GetInt64(0), remetenteId = reader.GetInt64(1), remetenteNome = reader.GetString(2), destinatarioId = reader.GetInt64(3), texto = reader.GetString(4), foto = reader.IsDBNull(5) ? null : (byte[])reader[5], fotoNome = reader.IsDBNull(6) ? null : reader.GetString(6), fotoTipo = reader.IsDBNull(7) ? null : reader.GetString(7), enviadaEm = reader.GetDateTime(8), lidaEm = reader.IsDBNull(9) ? (DateTime?)null : reader.GetDateTime(9) });
        return Ok(rows);
    }

    [HttpPost("enviar")]
    public async Task<IActionResult> Enviar([FromBody] EnviarMensagemRequest request)
    {
        if (!ValidarRequest(request)) return BadRequest(new { mensagem = "Conversa inválida." });
        var texto = request.Texto?.Trim() ?? "";
        if (texto.Length > 4000) return BadRequest(new { mensagem = "A mensagem pode ter até 4.000 caracteres." });
        if (string.IsNullOrEmpty(texto) && request.Foto is not { Length: > 0 }) return BadRequest(new { mensagem = "Informe uma mensagem ou foto." });
        if (request.Foto is { Length: > MaxFotoBytes }) return BadRequest(new { mensagem = "A foto deve ter no máximo 8 MB." });
        if (request.Foto is { Length: > 0 } && (request.FotoTipo?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true))
            return BadRequest(new { mensagem = "O anexo precisa ser uma imagem." });

        await using var db = await AbrirEValidar(request);
        if (db is null) return Unauthorized(new { mensagem = "Usuário inválido para esta empresa." });
        if (!await ContatoValido(db, request.EmpresaId, request.ContatoId)) return BadRequest(new { mensagem = "Contato inválido." });

        const string sql = """
            INSERT INTO app.mensagem_conversa (empresa_id, remetente_id, destinatario_id, texto, foto, foto_nome, foto_tipo)
            VALUES (@empresaId, @usuarioId, @contatoId, @texto, @foto, @fotoNome, @fotoTipo)
            RETURNING id, enviada_em;
            """;
        await using var cmd = new NpgsqlCommand(sql, db);
        AddIdentity(cmd, request); cmd.Parameters.AddWithValue("contatoId", request.ContatoId);
        cmd.Parameters.AddWithValue("texto", texto);
        cmd.Parameters.Add(new NpgsqlParameter("foto", NpgsqlDbType.Bytea) { Value = (object?)request.Foto ?? DBNull.Value });
        cmd.Parameters.AddWithValue("fotoNome", (object?)request.FotoNome ?? DBNull.Value);
        cmd.Parameters.AddWithValue("fotoTipo", (object?)request.FotoTipo ?? DBNull.Value);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return Ok(new { id = reader.GetInt64(0), remetenteId = request.UsuarioId, remetenteNome = "", destinatarioId = request.ContatoId, texto, foto = request.Foto, fotoNome = request.FotoNome, fotoTipo = request.FotoTipo, enviadaEm = reader.GetDateTime(1), lidaEm = (DateTime?)null });
    }

    [HttpPost("marcar-lidas")]
    public async Task<IActionResult> MarcarLidas([FromBody] ConversaRequest request)
    {
        if (!ValidarRequest(request)) return BadRequest(new { mensagem = "Conversa inválida." });
        await using var db = await AbrirEValidar(request);
        if (db is null) return Unauthorized(new { mensagem = "Usuário inválido para esta empresa." });
        const string sql = "UPDATE app.mensagem_conversa SET lida_em = now() WHERE empresa_id=@empresaId AND remetente_id=@contatoId AND destinatario_id=@usuarioId AND lida_em IS NULL;";
        await using var cmd = new NpgsqlCommand(sql, db);
        AddIdentity(cmd, request); cmd.Parameters.AddWithValue("contatoId", request.ContatoId);
        await cmd.ExecuteNonQueryAsync();
        return Ok(new { sucesso = true });
    }

    private async Task<NpgsqlConnection?> AbrirEValidar(IdentidadeConversaRequest request)
    {
        if (!TryGetSession(request, out _)) return null;
        var db = new NpgsqlConnection(_resolver.ObterConnectionString(request.EmpresaId));
        await db.OpenAsync();
        return db;
    }

    private bool TryGetSession(IdentidadeConversaRequest request, out TenantSession session)
    {
        if (_sessions.TryGet(Request.Headers.Authorization.ToString(), out session) &&
            session.EmpresaId == request.EmpresaId && session.UsuarioId == request.UsuarioId)
            return true;
        session = default!;
        return false;
    }

    private static bool ValidarIdentidadeInput(IdentidadeConversaRequest r) => r.EmpresaId > 0 && r.UsuarioId > 0;
    private static bool ValidarRequest(ConversaRequest r) => ValidarIdentidadeInput(r) && r.ContatoId > 0 && r.ContatoId != r.UsuarioId;
    private static void AddIdentity(NpgsqlCommand cmd, IdentidadeConversaRequest r)
    {
        cmd.Parameters.AddWithValue("empresaId", r.EmpresaId);
        cmd.Parameters.AddWithValue("usuarioId", r.UsuarioId);
    }

    private static async Task<bool> ContatoValido(NpgsqlConnection db, long empresaId, long contatoId)
    {
        const string sql = "SELECT EXISTS (SELECT 1 FROM app.usuario WHERE id=@id AND empresa_id=@empresaId AND COALESCE(excluido,FALSE)=FALSE);";
        await using var cmd = new NpgsqlCommand(sql, db);
        cmd.Parameters.AddWithValue("id", contatoId); cmd.Parameters.AddWithValue("empresaId", empresaId);
        return await cmd.ExecuteScalarAsync() is true;
    }
}
