using APP_CELULAR_API.Services;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace APP_CELULAR_API.Controllers;

[ApiController]
[Route("api/entrega/corridas")]
public sealed class EntregasController(
    IEmpresaDatabaseResolver resolver,
    TenantSessionStore sessions,
    ILogger<EntregasController> logger) : ControllerBase
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> SchemaLocks = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> SchemaReady = new();

    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] CriarCorridaRequest request, CancellationToken cancellationToken)
    {
        if (!TrySession(out var session)) return Unauthorized(new { mensagem = "Entre novamente no aplicativo." });
        if (request.OrigemLatitude is < -90 or > 90 || request.OrigemLongitude is < -180 or > 180 ||
            request.DestinoLatitude is < -90 or > 90 || request.DestinoLongitude is < -180 or > 180 ||
            string.IsNullOrWhiteSpace(request.Origem) || string.IsNullOrWhiteSpace(request.Destino) ||
            request.Origem.Length > 300 || request.Destino.Length > 300 ||
            request.Servico is not ("delivery" or "passenger") || request.Veiculo is not ("moto" or "carro"))
            return BadRequest(new { mensagem = "Informe coleta, destino e modalidade válidos." });

        try
        {
            await using var db = await AbrirBancoAsync(session.EmpresaId, cancellationToken);
            await GarantirTabelaAsync(db, cancellationToken);
            var id = Guid.NewGuid();
            var code = System.Security.Cryptography.RandomNumberGenerator.GetInt32(1000, 10000)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
            var codeHash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(code));
            const string sql = """
                INSERT INTO app.entrega_corrida
                    (id, empresa_id, cliente_id, origem, origem_latitude, origem_longitude,
                     destino, destino_latitude, destino_longitude, servico, veiculo,
                     codigo_recebimento_hash, status, criada_em, atualizada_em)
                VALUES (@id, @empresa, @cliente, @origem, @olat, @olon, @destino, @dlat, @dlon,
                        @servico, @veiculo, @codigo, 'aguardando_motorista', now(), now())
                """;
            await using var command = new NpgsqlCommand(sql, db);
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("empresa", session.EmpresaId);
            command.Parameters.AddWithValue("cliente", session.UsuarioId);
            command.Parameters.AddWithValue("origem", request.Origem.Trim());
            command.Parameters.AddWithValue("olat", request.OrigemLatitude);
            command.Parameters.AddWithValue("olon", request.OrigemLongitude);
            command.Parameters.AddWithValue("destino", request.Destino.Trim());
            command.Parameters.AddWithValue("dlat", request.DestinoLatitude);
            command.Parameters.AddWithValue("dlon", request.DestinoLongitude);
            command.Parameters.AddWithValue("servico", request.Servico);
            command.Parameters.AddWithValue("veiculo", request.Veiculo);
            command.Parameters.AddWithValue("codigo", codeHash);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return Ok(new { id, codigoRecebimento = code, status = "aguardando_motorista" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Não foi possível criar uma corrida APPENTREGA para a empresa {EmpresaId}.", session.EmpresaId);
            return StatusCode(503, new { mensagem = "Não foi possível registrar a solicitação agora." });
        }
    }

    [HttpGet("pendentes")]
    public async Task<IActionResult> Pendentes(CancellationToken cancellationToken)
    {
        if (!TrySession(out var session)) return Unauthorized();
        try
        {
            await using var db = await AbrirBancoAsync(session.EmpresaId, cancellationToken);
            await GarantirTabelaAsync(db, cancellationToken);
            const string sql = """
                SELECT id, cliente_id, motorista_id, origem, origem_latitude, origem_longitude,
                       destino, destino_latitude, destino_longitude, servico, veiculo, status,
                       motorista_latitude, motorista_longitude, atualizada_em
                FROM app.entrega_corrida
                WHERE empresa_id=@empresa AND status='aguardando_motorista'
                ORDER BY criada_em LIMIT 30
                """;
            await using var command = new NpgsqlCommand(sql, db);
            command.Parameters.AddWithValue("empresa", session.EmpresaId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var result = new List<CorridaResponse>();
            while (await reader.ReadAsync(cancellationToken)) result.Add(LerCorrida(reader));
            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao consultar solicitações APPENTREGA.");
            return StatusCode(503, new { mensagem = "As solicitações não estão disponíveis agora." });
        }
    }

    [HttpGet("atual")]
    public async Task<IActionResult> Atual(CancellationToken cancellationToken)
    {
        if (!TrySession(out var session)) return Unauthorized();
        try
        {
            await using var db = await AbrirBancoAsync(session.EmpresaId, cancellationToken);
            await GarantirTabelaAsync(db, cancellationToken);
            const string sql = """
                SELECT id, cliente_id, motorista_id, origem, origem_latitude, origem_longitude,
                       destino, destino_latitude, destino_longitude, servico, veiculo, status,
                       motorista_latitude, motorista_longitude, atualizada_em
                FROM app.entrega_corrida
                WHERE empresa_id=@empresa AND ((cliente_id=@usuario AND status<>'aguardando_motorista') OR motorista_id=@usuario)
                  AND status NOT IN ('concluida','cancelada')
                ORDER BY criada_em DESC LIMIT 1
                """;
            await using var command = new NpgsqlCommand(sql, db);
            command.Parameters.AddWithValue("empresa", session.EmpresaId);
            command.Parameters.AddWithValue("usuario", session.UsuarioId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? Ok(LerCorrida(reader)) : NoContent();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao carregar corrida atual para o usuário {UsuarioId}.", session.UsuarioId);
            return StatusCode(503, new { mensagem = "Não foi possível recuperar a corrida atual." });
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Obter(Guid id, CancellationToken cancellationToken)
    {
        if (!TrySession(out var session)) return Unauthorized();
        try
        {
            await using var db = await AbrirBancoAsync(session.EmpresaId, cancellationToken);
            await GarantirTabelaAsync(db, cancellationToken);
            const string sql = """
                SELECT id, cliente_id, motorista_id, origem, origem_latitude, origem_longitude,
                       destino, destino_latitude, destino_longitude, servico, veiculo, status,
                       motorista_latitude, motorista_longitude, atualizada_em
                FROM app.entrega_corrida
                WHERE id=@id AND empresa_id=@empresa AND (cliente_id=@usuario OR motorista_id=@usuario)
                """;
            await using var command = new NpgsqlCommand(sql, db);
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("empresa", session.EmpresaId);
            command.Parameters.AddWithValue("usuario", session.UsuarioId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return NotFound();
            return Ok(LerCorrida(reader));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao consultar corrida {CorridaId}.", id);
            return StatusCode(503, new { mensagem = "A corrida não está disponível agora." });
        }
    }

    [HttpPost("{id:guid}/aceitar")]
    public async Task<IActionResult> Aceitar(Guid id, CancellationToken cancellationToken)
    {
        if (!TrySession(out var session)) return Unauthorized();
        try
        {
            await using var db = await AbrirBancoAsync(session.EmpresaId, cancellationToken);
            await GarantirTabelaAsync(db, cancellationToken);
            const string sql = """
                UPDATE app.entrega_corrida SET motorista_id=@motorista, status='motorista_a_caminho', atualizada_em=now()
                WHERE id=@id AND empresa_id=@empresa AND status='aguardando_motorista'
                RETURNING id, cliente_id, motorista_id, origem, origem_latitude, origem_longitude,
                          destino, destino_latitude, destino_longitude, servico, veiculo, status,
                          motorista_latitude, motorista_longitude, atualizada_em
                """;
            await using var command = new NpgsqlCommand(sql, db);
            command.Parameters.AddWithValue("motorista", session.UsuarioId);
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("empresa", session.EmpresaId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return Conflict(new { mensagem = "Esta solicitação já foi aceita ou não está disponível." });
            return Ok(LerCorrida(reader));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao aceitar corrida {CorridaId}.", id);
            return StatusCode(503, new { mensagem = "Não foi possível aceitar a solicitação agora." });
        }
    }

    [HttpPost("{id:guid}/localizacao")]
    public async Task<IActionResult> AtualizarLocalizacao(Guid id, [FromBody] PosicaoMotoristaRequest request, CancellationToken cancellationToken)
    {
        if (!TrySession(out var session)) return Unauthorized();
        if (request.Latitude is < -90 or > 90 || request.Longitude is < -180 or > 180) return BadRequest();
        try
        {
            await using var db = await AbrirBancoAsync(session.EmpresaId, cancellationToken);
            await GarantirTabelaAsync(db, cancellationToken);
            const string sql = """
                UPDATE app.entrega_corrida SET motorista_latitude=@lat, motorista_longitude=@lon, atualizada_em=now()
                WHERE id=@id AND empresa_id=@empresa AND motorista_id=@motorista
                  AND status IN ('motorista_a_caminho','no_local_coleta','em_rota')
                """;
            await using var command = new NpgsqlCommand(sql, db);
            command.Parameters.AddWithValue("lat", request.Latitude);
            command.Parameters.AddWithValue("lon", request.Longitude);
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("empresa", session.EmpresaId);
            command.Parameters.AddWithValue("motorista", session.UsuarioId);
            return await command.ExecuteNonQueryAsync(cancellationToken) == 1 ? NoContent() : NotFound();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao atualizar localização da corrida {CorridaId}.", id);
            return StatusCode(503);
        }
    }

    [HttpPost("{id:guid}/status")]
    public async Task<IActionResult> AtualizarStatus(Guid id, [FromBody] AtualizarCorridaStatusRequest request, CancellationToken cancellationToken)
    {
        if (!TrySession(out var session)) return Unauthorized();
        var allowed = request.Status is "no_local_coleta" or "em_rota" or "no_destino";
        if (!allowed) return BadRequest(new { mensagem = "Etapa inválida." });
        try
        {
            await using var db = await AbrirBancoAsync(session.EmpresaId, cancellationToken);
            await GarantirTabelaAsync(db, cancellationToken);
            const string sql = """
                UPDATE app.entrega_corrida SET status=@status, atualizada_em=now()
                WHERE id=@id AND empresa_id=@empresa AND motorista_id=@motorista
                  AND ((status='motorista_a_caminho' AND @status='no_local_coleta')
                    OR (status='em_rota' AND @status='no_destino'))
                """;
            await using var command = new NpgsqlCommand(sql, db);
            command.Parameters.AddWithValue("status", request.Status);
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("empresa", session.EmpresaId);
            command.Parameters.AddWithValue("motorista", session.UsuarioId);
            return await command.ExecuteNonQueryAsync(cancellationToken) == 1 ? NoContent() : Conflict(new { mensagem = "A corrida não pode avançar para esta etapa." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao atualizar etapa da corrida {CorridaId}.", id);
            return StatusCode(503);
        }
    }

    [HttpPost("{id:guid}/iniciar-rota")]
    public async Task<IActionResult> IniciarRota(Guid id, CancellationToken cancellationToken)
    {
        if (!TrySession(out var session)) return Unauthorized();
        try
        {
            await using var db = await AbrirBancoAsync(session.EmpresaId, cancellationToken);
            await GarantirTabelaAsync(db, cancellationToken);
            const string sql = "UPDATE app.entrega_corrida SET status='em_rota', atualizada_em=now() WHERE id=@id AND empresa_id=@empresa AND cliente_id=@cliente AND status='no_local_coleta'";
            await using var command = new NpgsqlCommand(sql, db);
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("empresa", session.EmpresaId);
            command.Parameters.AddWithValue("cliente", session.UsuarioId);
            return await command.ExecuteNonQueryAsync(cancellationToken) == 1 ? NoContent() : Conflict(new { mensagem = "A saída da mercadoria ainda não pode ser confirmada." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao confirmar saída da corrida {CorridaId}.", id);
            return StatusCode(503);
        }
    }

    [HttpPost("{id:guid}/cancelar")]
    public async Task<IActionResult> Cancelar(Guid id, CancellationToken cancellationToken)
    {
        if (!TrySession(out var session)) return Unauthorized();
        try
        {
            await using var db = await AbrirBancoAsync(session.EmpresaId, cancellationToken);
            await GarantirTabelaAsync(db, cancellationToken);
            const string sql = """
                UPDATE app.entrega_corrida SET status='cancelada', atualizada_em=now()
                WHERE id=@id AND empresa_id=@empresa AND cliente_id=@cliente
                  AND status IN ('aguardando_motorista','motorista_a_caminho','no_local_coleta')
                """;
            await using var command = new NpgsqlCommand(sql, db);
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("empresa", session.EmpresaId);
            command.Parameters.AddWithValue("cliente", session.UsuarioId);
            return await command.ExecuteNonQueryAsync(cancellationToken) == 1 ? NoContent() : Conflict(new { mensagem = "A corrida já iniciou a rota e não pode ser cancelada por esta tela." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao cancelar corrida {CorridaId}.", id);
            return StatusCode(503);
        }
    }

    [HttpPost("{id:guid}/concluir")]
    public async Task<IActionResult> Concluir(Guid id, [FromBody] ConfirmarRecebimentoRequest request, CancellationToken cancellationToken)
    {
        if (!TrySession(out var session)) return Unauthorized();
        if (request.Codigo is null || request.Codigo.Length != 4 || !request.Codigo.All(char.IsDigit)) return BadRequest(new { mensagem = "Informe o código de quatro dígitos fornecido pelo recebedor." });
        try
        {
            await using var db = await AbrirBancoAsync(session.EmpresaId, cancellationToken);
            await GarantirTabelaAsync(db, cancellationToken);
            var codeHash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(request.Codigo));
            const string sql = """
                UPDATE app.entrega_corrida SET status='concluida', concluida_em=now(), atualizada_em=now()
                WHERE id=@id AND empresa_id=@empresa AND motorista_id=@motorista AND status='no_destino'
                  AND tentativas_codigo < 5
                  AND codigo_recebimento_hash=@codigo
                """;
            await using var command = new NpgsqlCommand(sql, db);
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("empresa", session.EmpresaId);
            command.Parameters.AddWithValue("motorista", session.UsuarioId);
            command.Parameters.AddWithValue("codigo", codeHash);
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await using var attempt = new NpgsqlCommand("UPDATE app.entrega_corrida SET tentativas_codigo=tentativas_codigo+1, atualizada_em=now() WHERE id=@id AND empresa_id=@empresa AND motorista_id=@motorista AND status='no_destino' AND tentativas_codigo < 5", db);
                attempt.Parameters.AddWithValue("id", id);
                attempt.Parameters.AddWithValue("empresa", session.EmpresaId);
                attempt.Parameters.AddWithValue("motorista", session.UsuarioId);
                await attempt.ExecuteNonQueryAsync(cancellationToken);
                return Conflict(new { mensagem = "O código não confere ou a corrida ainda não chegou ao destino." });
            }
            return NoContent();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao validar recebimento da corrida {CorridaId}.", id);
            return StatusCode(503);
        }
    }

    private bool TrySession(out TenantSession session) => sessions.TryGet(Request.Headers.Authorization.ToString(), out session);

    private async Task<NpgsqlConnection> AbrirBancoAsync(long empresaId, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(await resolver.ObterConnectionString(empresaId));
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task GarantirTabelaAsync(NpgsqlConnection db, CancellationToken cancellationToken)
    {
        var key = db.ConnectionString;
        if (SchemaReady.ContainsKey(key)) return;
        var schemaLock = SchemaLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await schemaLock.WaitAsync(cancellationToken);
        try
        {
            if (SchemaReady.ContainsKey(key)) return;
            await using var command = new NpgsqlCommand("""
                CREATE SCHEMA IF NOT EXISTS app;
                CREATE TABLE IF NOT EXISTS app.entrega_corrida (
                    id uuid PRIMARY KEY,
                    empresa_id bigint NOT NULL,
                    cliente_id bigint NOT NULL,
                    motorista_id bigint NULL,
                    origem text NOT NULL,
                    origem_latitude double precision NOT NULL,
                    origem_longitude double precision NOT NULL,
                    destino text NOT NULL,
                    destino_latitude double precision NOT NULL,
                    destino_longitude double precision NOT NULL,
                    servico varchar(20) NOT NULL,
                    veiculo varchar(20) NOT NULL,
                    codigo_recebimento_hash bytea NOT NULL,
                    tentativas_codigo integer NOT NULL DEFAULT 0,
                    status varchar(30) NOT NULL,
                    motorista_latitude double precision NULL,
                    motorista_longitude double precision NULL,
                    criada_em timestamptz NOT NULL DEFAULT now(),
                    atualizada_em timestamptz NOT NULL DEFAULT now(),
                    concluida_em timestamptz NULL
                );
                ALTER TABLE app.entrega_corrida ADD COLUMN IF NOT EXISTS tentativas_codigo integer NOT NULL DEFAULT 0;
                CREATE INDEX IF NOT EXISTS ix_entrega_corrida_fila ON app.entrega_corrida (empresa_id, status, criada_em);
                CREATE INDEX IF NOT EXISTS ix_entrega_corrida_cliente ON app.entrega_corrida (empresa_id, cliente_id, criada_em DESC);
                """, db);
            await command.ExecuteNonQueryAsync(cancellationToken);
            SchemaReady.TryAdd(key, 0);
        }
        finally { schemaLock.Release(); }
    }

    private static CorridaResponse LerCorrida(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        ClienteId = reader.GetInt64(1),
        MotoristaId = reader.IsDBNull(2) ? null : reader.GetInt64(2),
        Origem = reader.GetString(3),
        OrigemLatitude = reader.GetDouble(4),
        OrigemLongitude = reader.GetDouble(5),
        Destino = reader.GetString(6),
        DestinoLatitude = reader.GetDouble(7),
        DestinoLongitude = reader.GetDouble(8),
        Servico = reader.GetString(9),
        Veiculo = reader.GetString(10),
        Status = reader.GetString(11),
        MotoristaLatitude = reader.IsDBNull(12) ? null : reader.GetDouble(12),
        MotoristaLongitude = reader.IsDBNull(13) ? null : reader.GetDouble(13),
        AtualizadaEm = reader.GetFieldValue<DateTimeOffset>(14)
    };
}

public sealed class CriarCorridaRequest
{
    public string Origem { get; set; } = "";
    public double OrigemLatitude { get; set; }
    public double OrigemLongitude { get; set; }
    public string Destino { get; set; } = "";
    public double DestinoLatitude { get; set; }
    public double DestinoLongitude { get; set; }
    public string Servico { get; set; } = "delivery";
    public string Veiculo { get; set; } = "moto";
}

public sealed class PosicaoMotoristaRequest { public double Latitude { get; set; } public double Longitude { get; set; } }
public sealed class AtualizarCorridaStatusRequest { public string Status { get; set; } = ""; }
public sealed class ConfirmarRecebimentoRequest { public string Codigo { get; set; } = ""; }

public sealed class CorridaResponse
{
    public Guid Id { get; set; }
    public long ClienteId { get; set; }
    public long? MotoristaId { get; set; }
    public string Origem { get; set; } = "";
    public double OrigemLatitude { get; set; }
    public double OrigemLongitude { get; set; }
    public string Destino { get; set; } = "";
    public double DestinoLatitude { get; set; }
    public double DestinoLongitude { get; set; }
    public string Servico { get; set; } = "";
    public string Veiculo { get; set; } = "";
    public string Status { get; set; } = "";
    public double? MotoristaLatitude { get; set; }
    public double? MotoristaLongitude { get; set; }
    public DateTimeOffset AtualizadaEm { get; set; }
}
