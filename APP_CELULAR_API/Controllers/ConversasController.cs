using APP_CELULAR_API.Models;
using APP_CELULAR_API.Services;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using NpgsqlTypes;
using System.Collections.Concurrent;

namespace APP_CELULAR_API.Controllers;

[ApiController]
[Route("api/conversas")]
public class ConversasController : ControllerBase
{
    private const int MaxFotoBytes = 8 * 1024 * 1024;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ActionSchemaLocks = new();
    private static readonly ConcurrentDictionary<string, byte> ActionSchemasReady = new();
    private readonly IEmpresaDatabaseResolver _resolver;
    private readonly TenantSessionStore _sessions;
    private readonly IConfiguration _configuration;

    public ConversasController(IEmpresaDatabaseResolver resolver, TenantSessionStore sessions, IConfiguration configuration)
    {
        _resolver = resolver;
        _sessions = sessions;
        _configuration = configuration;
    }

    [HttpPost("contatos")]
    public async Task<IActionResult> Contatos([FromBody] IdentidadeConversaRequest request)
    {
        if (!ValidarIdentidadeInput(request)) return BadRequest(new { mensagem = "Identidade inválida." });
        await using var db = await AbrirEValidar(request);
        if (db is null) return Unauthorized(new { mensagem = "Usuário inválido para esta empresa." });
        if (await BancoChatCentral.Pronto(_configuration)) return await ContatosCentrais(db, request);

        const string sql = """
            SELECT u.id, COALESCE(a.apelido, u.nome), u.nome,
                   COALESCE(m.texto, CASE WHEN m.foto IS NOT NULL THEN '[Foto]' ELSE '' END) AS ultima_mensagem,
                   m.enviada_em AS data_ultima_mensagem,
                   (SELECT COUNT(*)::int FROM app.mensagem_conversa n
                    WHERE n.empresa_id = @empresaId AND n.remetente_id = u.id
                      AND n.destinatario_id = @usuarioId AND n.lida_em IS NULL) AS nao_lidas
            FROM app.usuario u
            LEFT JOIN app.usuario_apelido a
              ON a.empresa_id = u.empresa_id AND a.usuario_id = @usuarioId AND a.contato_id = u.id
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
            result.Add(new { id = reader.GetInt64(0), nome = reader.GetString(1), nomeOriginal = reader.GetString(2), ultimaMensagem = reader.GetString(3), dataUltimaMensagem = reader.IsDBNull(4) ? (DateTime?)null : reader.GetDateTime(4), naoLidas = reader.GetInt32(5) });
        return Ok(result);
    }

    [HttpPost("apelido")]
    public async Task<IActionResult> DefinirApelido([FromBody] ApelidoConversaRequest request)
    {
        if (!ValidarRequest(request)) return BadRequest(new { mensagem = "Identidade ou contato inválido." });
        var apelido = request.Apelido?.Trim() ?? "";
        if (apelido.Length > 60) return BadRequest(new { mensagem = "O apelido pode ter até 60 caracteres." });

        await using var db = await AbrirEValidar(request);
        if (db is null) return Unauthorized(new { mensagem = "Usuário inválido para esta empresa." });
        if (!await ContatoValido(db, request.EmpresaId, request.ContatoId)) return BadRequest(new { mensagem = "Contato inválido para esta empresa." });

        if (await BancoChatCentral.Pronto(_configuration))
        {
            await using var central = await BancoChatCentral.Abrir(_configuration);
            if (apelido.Length == 0)
            {
                const string excluirCentral = "DELETE FROM chat.usuario_apelido WHERE empresa_id=@empresaId AND usuario_id=@usuarioId AND contato_id=@contatoId;";
                await using var apagar = new NpgsqlCommand(excluirCentral, central);
                AddIdentity(apagar, request); apagar.Parameters.AddWithValue("contatoId", request.ContatoId);
                await apagar.ExecuteNonQueryAsync();
                return Ok(new { sucesso = true, apelido = (string?)null });
            }

            const string salvarCentralSql = """
                INSERT INTO chat.usuario_apelido (empresa_id, usuario_id, contato_id, apelido)
                VALUES (@empresaId, @usuarioId, @contatoId, @apelido)
                ON CONFLICT (empresa_id, usuario_id, contato_id)
                DO UPDATE SET apelido=EXCLUDED.apelido, atualizado_em=NOW();
                """;
            await using var salvarCmdCentral = new NpgsqlCommand(salvarCentralSql, central);
            AddIdentity(salvarCmdCentral, request); salvarCmdCentral.Parameters.AddWithValue("contatoId", request.ContatoId);
            salvarCmdCentral.Parameters.AddWithValue("apelido", apelido);
            await salvarCmdCentral.ExecuteNonQueryAsync();
            return Ok(new { sucesso = true, apelido });
        }

        if (apelido.Length == 0)
        {
            const string excluir = "DELETE FROM app.usuario_apelido WHERE empresa_id=@empresaId AND usuario_id=@usuarioId AND contato_id=@contatoId;";
            await using var cmd = new NpgsqlCommand(excluir, db);
            AddIdentity(cmd, request); cmd.Parameters.AddWithValue("contatoId", request.ContatoId);
            await cmd.ExecuteNonQueryAsync();
            return Ok(new { sucesso = true, apelido = (string?)null });
        }

        const string salvar = """
            INSERT INTO app.usuario_apelido (empresa_id, usuario_id, contato_id, apelido)
            VALUES (@empresaId, @usuarioId, @contatoId, @apelido)
            ON CONFLICT (empresa_id, usuario_id, contato_id)
            DO UPDATE SET apelido=EXCLUDED.apelido, atualizado_em=NOW();
            """;
        await using var salvarCmd = new NpgsqlCommand(salvar, db);
        AddIdentity(salvarCmd, request);
        salvarCmd.Parameters.AddWithValue("contatoId", request.ContatoId);
        salvarCmd.Parameters.AddWithValue("apelido", apelido);
        await salvarCmd.ExecuteNonQueryAsync();
        return Ok(new { sucesso = true, apelido });
    }

    [HttpPost("listar")]
    public async Task<IActionResult> Listar([FromBody] ConversaRequest request)
    {
        if (!ValidarRequest(request)) return BadRequest(new { mensagem = "Conversa inválida." });
        await using var db = await AbrirEValidar(request);
        if (db is null) return Unauthorized(new { mensagem = "Usuário inválido para esta empresa." });
        if (!await ContatoValido(db, request.EmpresaId, request.ContatoId)) return BadRequest(new { mensagem = "Contato inválido." });
        if (await BancoChatCentral.Pronto(_configuration)) return await ListarCentral(db, request);

        const string sql = """
            SELECT m.id, m.cliente_mensagem_id, m.remetente_id, COALESCE(a.apelido, r.nome), m.destinatario_id, m.texto,
                   m.foto, m.foto_nome, m.foto_tipo, m.enviada_em, m.lida_em
            FROM app.mensagem_conversa m
            JOIN app.usuario r ON r.id = m.remetente_id AND r.empresa_id = m.empresa_id
            LEFT JOIN app.usuario_apelido a
              ON a.empresa_id = m.empresa_id AND a.usuario_id = @usuarioId AND a.contato_id = r.id
            WHERE m.empresa_id = @empresaId
              AND ((m.remetente_id = @usuarioId AND m.destinatario_id = @contatoId)
                OR (m.remetente_id = @contatoId AND m.destinatario_id = @usuarioId))
              AND m.id > @depoisDoId
            ORDER BY m.enviada_em, m.id;
            """;
        await using var cmd = new NpgsqlCommand(sql, db);
        AddIdentity(cmd, request); cmd.Parameters.AddWithValue("contatoId", request.ContatoId);
        cmd.Parameters.AddWithValue("depoisDoId", Math.Max(0, request.DepoisDoId));
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<object>();
        while (await reader.ReadAsync())
            rows.Add(new { id = reader.GetInt64(0), clienteMensagemId = reader.IsDBNull(1) ? (Guid?)null : reader.GetGuid(1), remetenteId = reader.GetInt64(2), remetenteNome = reader.GetString(3), destinatarioId = reader.GetInt64(4), texto = reader.GetString(5), foto = reader.IsDBNull(6) ? null : (byte[])reader[6], fotoNome = reader.IsDBNull(7) ? null : reader.GetString(7), fotoTipo = reader.IsDBNull(8) ? null : reader.GetString(8), enviadaEm = reader.GetDateTime(9), lidaEm = reader.IsDBNull(10) ? (DateTime?)null : reader.GetDateTime(10) });
        return Ok(rows);
    }

    [HttpPost("acao-visual")]
    public async Task<IActionResult> EnviarAcaoVisual([FromBody] EnviarAcaoVisualRequest request)
    {
        if (!ValidarRequest(request)) return BadRequest(new { mensagem = "Conversa inválida." });
        if (request.Tipo is not ("WINK" or "ATTENTION"))
            return BadRequest(new { mensagem = "Tipo de ação inválido." });
        if (request.Tipo == "WINK" && (string.IsNullOrWhiteSpace(request.Simbolo) || request.Simbolo.Length > 32))
            return BadRequest(new { mensagem = "O wink precisa de um símbolo válido." });

        await using var tenantDb = await AbrirEValidar(request);
        if (tenantDb is null) return Unauthorized(new { mensagem = "Usuário ou aparelho inválido para esta empresa." });
        if (!await ContatoValido(tenantDb, request.EmpresaId, request.ContatoId))
            return BadRequest(new { mensagem = "Contato inválido para esta empresa." });

        bool central = await BancoChatCentral.Pronto(_configuration);
        if (central)
        {
            await using var centralDb = await BancoChatCentral.Abrir(_configuration);
            await GarantirTabelaAcoesVisuais(centralDb, central: true, request.EmpresaId);
            await ExpirarAcoesVisuais(centralDb, central: true, request.EmpresaId);
            await using var command = new NpgsqlCommand("INSERT INTO chat.acao_visual (empresa_id,remetente_id,destinatario_id,tipo,simbolo) VALUES (@empresaId,@usuarioId,@contatoId,@tipo,@simbolo);", centralDb);
            AddIdentity(command, request);
            command.Parameters.AddWithValue("contatoId", request.ContatoId);
            command.Parameters.AddWithValue("tipo", request.Tipo);
            command.Parameters.AddWithValue("simbolo", request.Tipo == "ATTENTION" ? "⚡" : request.Simbolo.Trim());
            await command.ExecuteNonQueryAsync();
        }
        else
        {
            await GarantirTabelaAcoesVisuais(tenantDb, central: false, request.EmpresaId);
            await ExpirarAcoesVisuais(tenantDb, central: false, request.EmpresaId);
            await using var command = new NpgsqlCommand("INSERT INTO app.acao_visual (remetente_id,destinatario_id,tipo,simbolo) VALUES (@usuarioId,@contatoId,@tipo,@simbolo);", tenantDb);
            AddIdentity(command, request);
            command.Parameters.AddWithValue("contatoId", request.ContatoId);
            command.Parameters.AddWithValue("tipo", request.Tipo);
            command.Parameters.AddWithValue("simbolo", request.Tipo == "ATTENTION" ? "⚡" : request.Simbolo.Trim());
            await command.ExecuteNonQueryAsync();
        }

        return Accepted(new { enviada = true, registradaNoHistorico = false });
    }

    [HttpPost("acoes-visuais/pendentes")]
    public async Task<IActionResult> ObterAcoesVisuaisPendentes([FromBody] ConversaRequest request)
    {
        if (!ValidarRequest(request)) return BadRequest(new { mensagem = "Conversa inválida." });
        await using var tenantDb = await AbrirEValidar(request);
        if (tenantDb is null) return Unauthorized(new { mensagem = "Usuário ou aparelho inválido para esta empresa." });
        if (!await ContatoValido(tenantDb, request.EmpresaId, request.ContatoId))
            return BadRequest(new { mensagem = "Contato inválido para esta empresa." });

        bool central = await BancoChatCentral.Pronto(_configuration);
        if (central)
        {
            await using var centralDb = await BancoChatCentral.Abrir(_configuration);
            await GarantirTabelaAcoesVisuais(centralDb, central: true, request.EmpresaId);
            await ExpirarAcoesVisuais(centralDb, central: true, request.EmpresaId);
            return Ok(await ConsumirAcoesVisuais(centralDb, central: true, request));
        }

        await GarantirTabelaAcoesVisuais(tenantDb, central: false, request.EmpresaId);
        await ExpirarAcoesVisuais(tenantDb, central: false, request.EmpresaId);
        return Ok(await ConsumirAcoesVisuais(tenantDb, central: false, request));
    }

    private static async Task GarantirTabelaAcoesVisuais(NpgsqlConnection db, bool central, long empresaId)
    {
        string chave = central ? "central" : $"empresa:{empresaId}";
        if (ActionSchemasReady.ContainsKey(chave)) return;
        var gate = ActionSchemaLocks.GetOrAdd(chave, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            if (ActionSchemasReady.ContainsKey(chave)) return;
            string sql = central
                ? "CREATE TABLE IF NOT EXISTS chat.acao_visual (id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY, empresa_id BIGINT NOT NULL, remetente_id BIGINT NOT NULL, destinatario_id BIGINT NOT NULL, tipo TEXT NOT NULL CHECK (tipo IN ('WINK','ATTENTION')), simbolo TEXT NOT NULL, criada_em TIMESTAMPTZ NOT NULL DEFAULT NOW()); CREATE INDEX IF NOT EXISTS ix_chat_acao_visual_destino ON chat.acao_visual (empresa_id,destinatario_id,remetente_id,id);"
                : "CREATE TABLE IF NOT EXISTS app.acao_visual (id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY, remetente_id BIGINT NOT NULL, destinatario_id BIGINT NOT NULL, tipo TEXT NOT NULL CHECK (tipo IN ('WINK','ATTENTION')), simbolo TEXT NOT NULL, criada_em TIMESTAMPTZ NOT NULL DEFAULT NOW()); CREATE INDEX IF NOT EXISTS ix_app_acao_visual_destino ON app.acao_visual (destinatario_id,remetente_id,id);";
            await using var command = new NpgsqlCommand(sql, db);
            await command.ExecuteNonQueryAsync();
            ActionSchemasReady.TryAdd(chave, 0);
        }
        finally { gate.Release(); }
    }

    private static async Task ExpirarAcoesVisuais(NpgsqlConnection db, bool central, long empresaId)
    {
        string sql = central
            ? "DELETE FROM chat.acao_visual WHERE empresa_id=@empresaId AND criada_em < NOW() - INTERVAL '30 seconds';"
            : "DELETE FROM app.acao_visual WHERE criada_em < NOW() - INTERVAL '30 seconds';";
        await using var command = new NpgsqlCommand(sql, db);
        if (central) command.Parameters.AddWithValue("empresaId", empresaId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<AcaoVisualConversaResponse>> ConsumirAcoesVisuais(NpgsqlConnection db, bool central, ConversaRequest request)
    {
        string sql = central
            ? "WITH fila AS (SELECT id FROM chat.acao_visual WHERE empresa_id=@empresaId AND remetente_id=@contatoId AND destinatario_id=@usuarioId AND criada_em >= NOW() - INTERVAL '30 seconds' ORDER BY id LIMIT 10), removidas AS (DELETE FROM chat.acao_visual a USING fila f WHERE a.id=f.id RETURNING a.id,a.tipo,a.simbolo) SELECT tipo,simbolo FROM removidas ORDER BY id;"
            : "WITH fila AS (SELECT id FROM app.acao_visual WHERE remetente_id=@contatoId AND destinatario_id=@usuarioId AND criada_em >= NOW() - INTERVAL '30 seconds' ORDER BY id LIMIT 10), removidas AS (DELETE FROM app.acao_visual a USING fila f WHERE a.id=f.id RETURNING a.id,a.tipo,a.simbolo) SELECT tipo,simbolo FROM removidas ORDER BY id;";
        await using var command = new NpgsqlCommand(sql, db);
        command.Parameters.AddWithValue("usuarioId", request.UsuarioId);
        command.Parameters.AddWithValue("contatoId", request.ContatoId);
        if (central) command.Parameters.AddWithValue("empresaId", request.EmpresaId);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<AcaoVisualConversaResponse>();
        while (await reader.ReadAsync()) rows.Add(new() { Tipo = reader.GetString(0), Simbolo = reader.GetString(1) });
        return rows;
    }

    [HttpPost("enviar")]
    public async Task<IActionResult> Enviar([FromBody] EnviarMensagemRequest request)
    {
        if (!ValidarRequest(request)) return BadRequest(new { mensagem = "Conversa inválida." });
        if (request.ClienteMensagemId == Guid.Empty) return BadRequest(new { mensagem = "Identificador da mensagem inválido." });
        var texto = request.Texto?.Trim() ?? "";
        if (texto.Length > 4000) return BadRequest(new { mensagem = "A mensagem pode ter até 4.000 caracteres." });
        if (string.IsNullOrEmpty(texto) && request.Foto is not { Length: > 0 }) return BadRequest(new { mensagem = "Informe uma mensagem ou foto." });
        if (request.Foto is { Length: > MaxFotoBytes }) return BadRequest(new { mensagem = "A foto deve ter no máximo 8 MB." });
        if (request.Foto is { Length: > 0 } && (request.FotoTipo?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true))
            return BadRequest(new { mensagem = "O anexo precisa ser uma imagem." });

        await using var db = await AbrirEValidar(request);
        if (db is null) return Unauthorized(new { mensagem = "Usuário inválido para esta empresa." });
        if (!await ContatoValido(db, request.EmpresaId, request.ContatoId)) return BadRequest(new { mensagem = "Contato inválido." });

        if (await BancoChatCentral.Pronto(_configuration)) return await EnviarCentral(db, request);

        const string sql = """
            INSERT INTO app.mensagem_conversa (empresa_id, remetente_id, destinatario_id, texto, foto, foto_nome, foto_tipo, cliente_mensagem_id)
            VALUES (@empresaId, @usuarioId, @contatoId, @texto, @foto, @fotoNome, @fotoTipo, @clienteMensagemId)
            ON CONFLICT (empresa_id, remetente_id, cliente_mensagem_id) WHERE cliente_mensagem_id IS NOT NULL
            DO UPDATE SET cliente_mensagem_id = EXCLUDED.cliente_mensagem_id
            RETURNING id, enviada_em;
            """;
        await using var cmd = new NpgsqlCommand(sql, db);
        AddIdentity(cmd, request); cmd.Parameters.AddWithValue("contatoId", request.ContatoId);
        cmd.Parameters.AddWithValue("texto", texto);
        cmd.Parameters.AddWithValue("clienteMensagemId", request.ClienteMensagemId);
        cmd.Parameters.Add(new NpgsqlParameter("foto", NpgsqlDbType.Bytea) { Value = (object?)request.Foto ?? DBNull.Value });
        cmd.Parameters.AddWithValue("fotoNome", (object?)request.FotoNome ?? DBNull.Value);
        cmd.Parameters.AddWithValue("fotoTipo", (object?)request.FotoTipo ?? DBNull.Value);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return Ok(new { id = reader.GetInt64(0), clienteMensagemId = request.ClienteMensagemId, remetenteId = request.UsuarioId, remetenteNome = "", destinatarioId = request.ContatoId, texto, foto = request.Foto, fotoNome = request.FotoNome, fotoTipo = request.FotoTipo, enviadaEm = reader.GetDateTime(1), lidaEm = (DateTime?)null });
    }

    [HttpPost("marcar-lidas")]
    public async Task<IActionResult> MarcarLidas([FromBody] ConversaRequest request)
    {
        if (!ValidarRequest(request)) return BadRequest(new { mensagem = "Conversa inválida." });
        await using var db = await AbrirEValidar(request);
        if (db is null) return Unauthorized(new { mensagem = "Usuário inválido para esta empresa." });
        if (await BancoChatCentral.Pronto(_configuration))
        {
            await using var central = await BancoChatCentral.Abrir(_configuration);
            var conversaId = await ObterConversaCentral(central, request.EmpresaId, request.UsuarioId, request.ContatoId);
            if (conversaId is null) return Ok(new { sucesso = true });
            await using var transaction = await central.BeginTransactionAsync();
            const string lidas = """
                INSERT INTO chat.recibo_mensagem (empresa_id, mensagem_id, usuario_id, entregue_em, lida_em)
                SELECT @empresaId, m.id, @usuarioId, NOW(), NOW()
                FROM chat.mensagem m
                WHERE m.empresa_id=@empresaId AND m.conversa_id=@conversaId AND m.remetente_id=@contatoId
                ON CONFLICT (empresa_id, mensagem_id, usuario_id)
                DO UPDATE SET entregue_em=COALESCE(chat.recibo_mensagem.entregue_em, EXCLUDED.entregue_em),
                              lida_em=COALESCE(chat.recibo_mensagem.lida_em, EXCLUDED.lida_em);
                """;
            await using var cmdLidas = new NpgsqlCommand(lidas, central, transaction);
            cmdLidas.Parameters.AddWithValue("empresaId", request.EmpresaId);
            cmdLidas.Parameters.AddWithValue("usuarioId", request.UsuarioId);
            cmdLidas.Parameters.AddWithValue("contatoId", request.ContatoId);
            cmdLidas.Parameters.AddWithValue("conversaId", conversaId.Value);
            await cmdLidas.ExecuteNonQueryAsync();
            const string atualizarLeitura = "UPDATE chat.participante SET ultima_leitura_em=NOW() WHERE empresa_id=@empresaId AND conversa_id=@conversaId AND usuario_id=@usuarioId;";
            await using var cmdLeitura = new NpgsqlCommand(atualizarLeitura, central, transaction);
            cmdLeitura.Parameters.AddWithValue("empresaId", request.EmpresaId);
            cmdLeitura.Parameters.AddWithValue("conversaId", conversaId.Value);
            cmdLeitura.Parameters.AddWithValue("usuarioId", request.UsuarioId);
            await cmdLeitura.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
            return Ok(new { sucesso = true });
        }
        const string sql = "UPDATE app.mensagem_conversa SET lida_em = now() WHERE empresa_id=@empresaId AND remetente_id=@contatoId AND destinatario_id=@usuarioId AND lida_em IS NULL;";
        await using var cmd = new NpgsqlCommand(sql, db);
        AddIdentity(cmd, request); cmd.Parameters.AddWithValue("contatoId", request.ContatoId);
        await cmd.ExecuteNonQueryAsync();
        return Ok(new { sucesso = true });
    }

    private async Task<NpgsqlConnection?> AbrirEValidar(IdentidadeConversaRequest request)
    {
        if (!TryGetSession(request, out var session)) return null;
        var db = new NpgsqlConnection(await _resolver.ObterConnectionString(request.EmpresaId));
        await db.OpenAsync();
        const string validarDispositivo = "SELECT EXISTS (SELECT 1 FROM app.dispositivo WHERE id=@dispositivoId AND empresa_id=@empresaId AND usuario_id=@usuarioId AND ativo=TRUE);";
        await using var cmd = new NpgsqlCommand(validarDispositivo, db);
        cmd.Parameters.AddWithValue("dispositivoId", session.DispositivoId);
        cmd.Parameters.AddWithValue("empresaId", session.EmpresaId);
        cmd.Parameters.AddWithValue("usuarioId", session.UsuarioId);
        if (await cmd.ExecuteScalarAsync() is not true)
        {
            await db.DisposeAsync();
            return null;
        }
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

    private async Task<IActionResult> ContatosCentrais(NpgsqlConnection empresaDb, IdentidadeConversaRequest request)
    {
        var usuarios = new List<(long Id, string Nome)>();
        const string usuariosSql = "SELECT id,nome FROM app.usuario WHERE empresa_id=@empresaId AND id<>@usuarioId AND COALESCE(excluido,FALSE)=FALSE ORDER BY nome;";
        await using (var cmdUsuarios = new NpgsqlCommand(usuariosSql, empresaDb))
        {
            AddIdentity(cmdUsuarios, request);
            await using var reader = await cmdUsuarios.ExecuteReaderAsync();
            while (await reader.ReadAsync()) usuarios.Add((reader.GetInt64(0), reader.GetString(1)));
        }

        var resumo = new Dictionary<long, (string? Apelido, string UltimaMensagem, DateTime? Data, int NaoLidas)>();
        await using (var central = await BancoChatCentral.Abrir(_configuration))
        {
            const string sql = """
                SELECT p.usuario_id, a.apelido,
                       COALESCE(NULLIF(ultima.texto,''), CASE WHEN ultima.foto IS NOT NULL THEN '[Foto]' ELSE '' END),
                       ultima.enviada_em,
                       (SELECT COUNT(*)::int
                        FROM chat.mensagem n
                        LEFT JOIN chat.recibo_mensagem r
                          ON r.empresa_id=n.empresa_id AND r.mensagem_id=n.id AND r.usuario_id=@usuarioId
                        WHERE n.empresa_id=@empresaId AND n.conversa_id=c.id
                          AND n.remetente_id<>@usuarioId AND r.lida_em IS NULL)
                FROM chat.conversa c
                JOIN chat.participante p ON p.empresa_id=c.empresa_id AND p.conversa_id=c.id
                LEFT JOIN chat.usuario_apelido a
                  ON a.empresa_id=c.empresa_id AND a.usuario_id=@usuarioId AND a.contato_id=p.usuario_id
                LEFT JOIN LATERAL (
                    SELECT m.texto,m.foto,m.enviada_em
                    FROM chat.mensagem m
                    WHERE m.empresa_id=c.empresa_id AND m.conversa_id=c.id
                    ORDER BY m.enviada_em DESC,m.id DESC LIMIT 1
                ) ultima ON TRUE
                WHERE c.empresa_id=@empresaId AND c.tipo='DIRETA'
                  AND EXISTS (SELECT 1 FROM chat.participante eu
                              WHERE eu.empresa_id=c.empresa_id AND eu.conversa_id=c.id
                                AND eu.usuario_id=@usuarioId AND eu.saiu_em IS NULL)
                  AND p.usuario_id<>@usuarioId AND p.saiu_em IS NULL;
                """;
            await using var cmdResumo = new NpgsqlCommand(sql, central);
            AddIdentity(cmdResumo, request);
            await using var reader = await cmdResumo.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                resumo[reader.GetInt64(0)] = (reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(2),
                    reader.IsDBNull(3) ? (DateTime?)null : reader.GetDateTime(3), reader.GetInt32(4));
        }

        var resultado = usuarios.Select(usuario =>
        {
            resumo.TryGetValue(usuario.Id, out var item);
            var linha = new
            {
                id = usuario.Id,
                nome = string.IsNullOrWhiteSpace(item.Apelido) ? usuario.Nome : item.Apelido,
                nomeOriginal = usuario.Nome,
                ultimaMensagem = item.UltimaMensagem ?? "",
                dataUltimaMensagem = item.Data,
                naoLidas = item.NaoLidas
            };
            return (Data: item.Data, Linha: linha);
        }).OrderByDescending(x => x.Data ?? DateTime.MinValue).Select(x => x.Linha).ToList();
        return Ok(resultado);
    }

    private async Task<IActionResult> ListarCentral(NpgsqlConnection empresaDb, ConversaRequest request)
    {
        await using var central = await BancoChatCentral.Abrir(_configuration);
        var conversaId = await ObterConversaCentral(central, request.EmpresaId, request.UsuarioId, request.ContatoId);
        if (conversaId is null) return Ok(Array.Empty<object>());

        const string entregar = """
            INSERT INTO chat.recibo_mensagem (empresa_id,mensagem_id,usuario_id,entregue_em)
            SELECT @empresaId,m.id,@usuarioId,NOW()
            FROM chat.mensagem m
            WHERE m.empresa_id=@empresaId AND m.conversa_id=@conversaId AND m.remetente_id=@contatoId
              AND m.id > @depoisDoId
            ON CONFLICT (empresa_id,mensagem_id,usuario_id)
            DO UPDATE SET entregue_em=COALESCE(chat.recibo_mensagem.entregue_em,EXCLUDED.entregue_em);
            """;
        await using (var cmdEntrega = new NpgsqlCommand(entregar, central))
        {
            cmdEntrega.Parameters.AddWithValue("empresaId", request.EmpresaId);
            cmdEntrega.Parameters.AddWithValue("usuarioId", request.UsuarioId);
            cmdEntrega.Parameters.AddWithValue("contatoId", request.ContatoId);
            cmdEntrega.Parameters.AddWithValue("conversaId", conversaId.Value);
            cmdEntrega.Parameters.AddWithValue("depoisDoId", Math.Max(0, request.DepoisDoId));
            await cmdEntrega.ExecuteNonQueryAsync();
        }

        const string sql = """
            SELECT m.id,m.cliente_mensagem_id,m.remetente_id,
                   CASE WHEN m.remetente_id=@usuarioId THEN m.remetente_nome ELSE COALESCE(a.apelido,m.remetente_nome) END,
                   CASE WHEN m.remetente_id=@usuarioId THEN @contatoId ELSE @usuarioId END,
                   m.texto,m.foto,m.foto_nome,m.foto_tipo,m.enviada_em,rr.lida_em
            FROM chat.mensagem m
            LEFT JOIN chat.usuario_apelido a
              ON a.empresa_id=m.empresa_id AND a.usuario_id=@usuarioId AND a.contato_id=m.remetente_id
            LEFT JOIN chat.recibo_mensagem rr
              ON rr.empresa_id=m.empresa_id AND rr.mensagem_id=m.id
             AND rr.usuario_id=CASE WHEN m.remetente_id=@usuarioId THEN @contatoId ELSE @usuarioId END
            WHERE m.empresa_id=@empresaId AND m.conversa_id=@conversaId
              AND m.id > @depoisDoId
            ORDER BY m.enviada_em,m.id;
            """;
        await using var cmd = new NpgsqlCommand(sql, central);
        cmd.Parameters.AddWithValue("empresaId", request.EmpresaId);
        cmd.Parameters.AddWithValue("usuarioId", request.UsuarioId);
        cmd.Parameters.AddWithValue("contatoId", request.ContatoId);
        cmd.Parameters.AddWithValue("conversaId", conversaId.Value);
        cmd.Parameters.AddWithValue("depoisDoId", Math.Max(0, request.DepoisDoId));
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<object>();
        while (await reader.ReadAsync())
            rows.Add(new { id = reader.GetInt64(0), clienteMensagemId = reader.IsDBNull(1) ? (Guid?)null : reader.GetGuid(1), remetenteId = reader.GetInt64(2), remetenteNome = reader.GetString(3), destinatarioId = reader.GetInt64(4), texto = reader.GetString(5), foto = reader.IsDBNull(6) ? null : (byte[])reader[6], fotoNome = reader.IsDBNull(7) ? null : reader.GetString(7), fotoTipo = reader.IsDBNull(8) ? null : reader.GetString(8), enviadaEm = reader.GetDateTime(9), lidaEm = reader.IsDBNull(10) ? (DateTime?)null : reader.GetDateTime(10) });
        return Ok(rows);
    }

    private async Task<IActionResult> EnviarCentral(NpgsqlConnection empresaDb, EnviarMensagemRequest request)
    {
        string remetenteNome = await ObterNomeUsuario(empresaDb, request.EmpresaId, request.UsuarioId);
        string destinatarioNome = await ObterNomeUsuario(empresaDb, request.EmpresaId, request.ContatoId);
        await using var central = await BancoChatCentral.Abrir(_configuration);
        await using var transaction = await central.BeginTransactionAsync();
        var conversaId = await GarantirConversaCentral(central, transaction, request.EmpresaId,
            request.UsuarioId, request.ContatoId);

        const string participantes = """
            INSERT INTO chat.participante (empresa_id,conversa_id,usuario_id)
            VALUES (@empresaId,@conversaId,@usuarioId),(@empresaId,@conversaId,@contatoId)
            ON CONFLICT (empresa_id,conversa_id,usuario_id)
            DO UPDATE SET saiu_em=NULL;
            """;
        await using (var cmdParticipantes = new NpgsqlCommand(participantes, central, transaction))
        {
            cmdParticipantes.Parameters.AddWithValue("empresaId", request.EmpresaId);
            cmdParticipantes.Parameters.AddWithValue("conversaId", conversaId);
            cmdParticipantes.Parameters.AddWithValue("usuarioId", request.UsuarioId);
            cmdParticipantes.Parameters.AddWithValue("contatoId", request.ContatoId);
            await cmdParticipantes.ExecuteNonQueryAsync();
        }

        const string inserir = """
            INSERT INTO chat.mensagem
                (empresa_id,conversa_id,remetente_id,cliente_mensagem_id,remetente_nome,destinatario_nome,texto,foto,foto_nome,foto_tipo)
            VALUES (@empresaId,@conversaId,@usuarioId,@clienteMensagemId,@remetenteNome,@destinatarioNome,@texto,@foto,@fotoNome,@fotoTipo)
            ON CONFLICT (empresa_id,remetente_id,cliente_mensagem_id) WHERE cliente_mensagem_id IS NOT NULL
            DO UPDATE SET cliente_mensagem_id=EXCLUDED.cliente_mensagem_id
            RETURNING id,enviada_em;
            """;
        long mensagemId;
        DateTime enviadaEm;
        await using (var cmdMensagem = new NpgsqlCommand(inserir, central, transaction))
        {
            cmdMensagem.Parameters.AddWithValue("empresaId", request.EmpresaId);
            cmdMensagem.Parameters.AddWithValue("conversaId", conversaId);
            cmdMensagem.Parameters.AddWithValue("usuarioId", request.UsuarioId);
            cmdMensagem.Parameters.AddWithValue("clienteMensagemId", request.ClienteMensagemId);
            cmdMensagem.Parameters.AddWithValue("remetenteNome", remetenteNome);
            cmdMensagem.Parameters.AddWithValue("destinatarioNome", destinatarioNome);
            cmdMensagem.Parameters.AddWithValue("texto", request.Texto?.Trim() ?? "");
            cmdMensagem.Parameters.Add(new NpgsqlParameter("foto", NpgsqlDbType.Bytea) { Value = (object?)request.Foto ?? DBNull.Value });
            cmdMensagem.Parameters.AddWithValue("fotoNome", (object?)request.FotoNome ?? DBNull.Value);
            cmdMensagem.Parameters.AddWithValue("fotoTipo", (object?)request.FotoTipo ?? DBNull.Value);
            await using var reader = await cmdMensagem.ExecuteReaderAsync();
            await reader.ReadAsync();
            mensagemId = reader.GetInt64(0);
            enviadaEm = reader.GetDateTime(1);
        }

        const string recibo = """
            INSERT INTO chat.recibo_mensagem (empresa_id,mensagem_id,usuario_id)
            VALUES (@empresaId,@mensagemId,@contatoId)
            ON CONFLICT (empresa_id,mensagem_id,usuario_id) DO NOTHING;
            """;
        await using (var cmdRecibo = new NpgsqlCommand(recibo, central, transaction))
        {
            cmdRecibo.Parameters.AddWithValue("empresaId", request.EmpresaId);
            cmdRecibo.Parameters.AddWithValue("mensagemId", mensagemId);
            cmdRecibo.Parameters.AddWithValue("contatoId", request.ContatoId);
            await cmdRecibo.ExecuteNonQueryAsync();
        }
        const string atualizarConversa = "UPDATE chat.conversa SET atualizada_em=GREATEST(atualizada_em,@enviadaEm) WHERE empresa_id=@empresaId AND id=@conversaId;";
        await using (var cmdAtualizarConversa = new NpgsqlCommand(atualizarConversa, central, transaction))
        {
            cmdAtualizarConversa.Parameters.AddWithValue("empresaId", request.EmpresaId);
            cmdAtualizarConversa.Parameters.AddWithValue("conversaId", conversaId);
            cmdAtualizarConversa.Parameters.AddWithValue("enviadaEm", enviadaEm);
            await cmdAtualizarConversa.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        return Ok(new { id = mensagemId, clienteMensagemId = request.ClienteMensagemId, remetenteId = request.UsuarioId, remetenteNome, destinatarioId = request.ContatoId, texto = request.Texto?.Trim() ?? "", foto = request.Foto, fotoNome = request.FotoNome, fotoTipo = request.FotoTipo, enviadaEm, lidaEm = (DateTime?)null });
    }

    private static async Task<string> ObterNomeUsuario(NpgsqlConnection empresaDb, long empresaId, long usuarioId)
    {
        const string sql = "SELECT nome FROM app.usuario WHERE empresa_id=@empresaId AND id=@usuarioId AND COALESCE(excluido,FALSE)=FALSE;";
        await using var cmd = new NpgsqlCommand(sql, empresaDb);
        cmd.Parameters.AddWithValue("empresaId", empresaId);
        cmd.Parameters.AddWithValue("usuarioId", usuarioId);
        return (string?)await cmd.ExecuteScalarAsync() ?? $"Usuário {usuarioId}";
    }

    private static async Task<Guid?> ObterConversaCentral(NpgsqlConnection central, long empresaId, long usuarioId, long contatoId)
    {
        const string sql = "SELECT id FROM chat.conversa WHERE empresa_id=@empresaId AND tipo='DIRETA' AND usuario_menor_id=LEAST(@usuarioId,@contatoId) AND usuario_maior_id=GREATEST(@usuarioId,@contatoId);";
        await using var cmd = new NpgsqlCommand(sql, central);
        cmd.Parameters.AddWithValue("empresaId", empresaId);
        cmd.Parameters.AddWithValue("usuarioId", usuarioId);
        cmd.Parameters.AddWithValue("contatoId", contatoId);
        var value = await cmd.ExecuteScalarAsync();
        return value is Guid id ? id : null;
    }

    private static async Task<Guid> GarantirConversaCentral(NpgsqlConnection central, NpgsqlTransaction transaction,
        long empresaId, long usuarioId, long contatoId)
    {
        const string sql = """
            INSERT INTO chat.conversa (empresa_id,tipo,usuario_menor_id,usuario_maior_id,criada_por_usuario_id)
            VALUES (@empresaId,'DIRETA',LEAST(@usuarioId,@contatoId),GREATEST(@usuarioId,@contatoId),@usuarioId)
            ON CONFLICT (empresa_id,usuario_menor_id,usuario_maior_id) WHERE tipo='DIRETA'
            DO UPDATE SET atualizada_em=GREATEST(chat.conversa.atualizada_em,NOW())
            RETURNING id;
            """;
        await using var cmd = new NpgsqlCommand(sql, central, transaction);
        cmd.Parameters.AddWithValue("empresaId", empresaId);
        cmd.Parameters.AddWithValue("usuarioId", usuarioId);
        cmd.Parameters.AddWithValue("contatoId", contatoId);
        return (Guid)(await cmd.ExecuteScalarAsync() ?? throw new InvalidOperationException("Não foi possível criar a conversa."));
    }
}
