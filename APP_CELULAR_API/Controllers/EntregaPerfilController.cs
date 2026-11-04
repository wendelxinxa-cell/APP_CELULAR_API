using APP_CELULAR_API.Services;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace APP_CELULAR_API.Controllers;

[ApiController]
[Route("api/entrega/perfil")]
public sealed class EntregaPerfilController(
    IEmpresaDatabaseResolver resolver,
    TenantSessionStore sessions,
    MasterSessionStore masterSessions,
    ILogger<EntregaPerfilController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Obter(CancellationToken cancellationToken)
    {
        if (!sessions.TryGet(Request.Headers.Authorization.ToString(), out var session)) return Unauthorized();
        try
        {
            await using var db = await AbrirBancoAsync(session.EmpresaId, cancellationToken);
            await GarantirTabelaAsync(db, cancellationToken);
            const string sql = "SELECT perfil, telefone, veiculo_modelo, veiculo_cor, veiculo_placa FROM app.entrega_perfil_usuario WHERE empresa_id=@empresa AND usuario_id=@usuario LIMIT 1";
            await using var command = new NpgsqlCommand(sql, db);
            command.Parameters.AddWithValue("empresa", session.EmpresaId);
            command.Parameters.AddWithValue("usuario", session.UsuarioId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return NoContent();
            return Ok(new DeliveryProfileResponse(
                reader.GetString(0), reader.GetString(1),
                reader.IsDBNull(2) ? "" : reader.GetString(2),
                reader.IsDBNull(3) ? "" : reader.GetString(3),
                reader.IsDBNull(4) ? "" : reader.GetString(4)));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao consultar perfil APPENTREGA do usuário {UsuarioId}.", session.UsuarioId);
            return StatusCode(503, new { mensagem = "Não foi possível consultar seu perfil de entrega." });
        }
    }

    [HttpGet("corrida/{corridaId:guid}")]
    public async Task<IActionResult> ObterMotoristaDaCorrida(Guid corridaId, CancellationToken cancellationToken)
    {
        if (!TrySession(out var session)) return Unauthorized();
        try
        {
            await using var db = await AbrirBancoAsync(session.EmpresaId, cancellationToken);
            await GarantirTabelaAsync(db, cancellationToken);
            const string sql = """
                SELECT u.nome, COALESCE(p.telefone,''), COALESCE(p.veiculo_modelo,''), COALESCE(p.veiculo_cor,''), COALESCE(p.veiculo_placa,'')
                FROM app.entrega_corrida c
                JOIN app.usuario u ON u.empresa_id=c.empresa_id AND u.id=c.motorista_id
                LEFT JOIN app.entrega_perfil_usuario p ON p.empresa_id=c.empresa_id AND p.usuario_id=c.motorista_id
                WHERE c.id=@corrida AND c.empresa_id=@empresa AND c.motorista_id IS NOT NULL
                  AND (@administrador OR c.cliente_id=@usuario OR c.motorista_id=@usuario)
                LIMIT 1
                """;
            await using var command = new NpgsqlCommand(sql, db);
            command.Parameters.AddWithValue("corrida", corridaId);
            command.Parameters.AddWithValue("empresa", session.EmpresaId);
            command.Parameters.AddWithValue("administrador", session.EhAdministrador);
            command.Parameters.AddWithValue("usuario", session.UsuarioId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return NoContent();
            return Ok(new CorridaMotoristaPerfilResponse(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao consultar dados do entregador da corrida {CorridaId}.", corridaId);
            return StatusCode(503, new { mensagem = "Não foi possível carregar os dados do entregador." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Salvar([FromBody] SalvarPerfilEntregaRequest request, CancellationToken cancellationToken)
    {
        if (!sessions.TryGet(Request.Headers.Authorization.ToString(), out var session)) return Unauthorized();
        var perfil = (request.Perfil ?? "").Trim().ToLowerInvariant();
        var telefone = new string((request.Telefone ?? "").Where(char.IsDigit).ToArray());
        var modelo = (request.VeiculoModelo ?? "").Trim();
        var cor = (request.VeiculoCor ?? "").Trim();
        var placa = new string((request.VeiculoPlaca ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (perfil is not ("cliente" or "motorista") || telefone.Length is < 8 or > 15)
            return BadRequest(new { mensagem = "Escolha cliente ou entregador e informe um telefone válido." });
        if (perfil == "motorista" && (modelo.Length is < 2 or > 100 || cor.Length is < 2 or > 40 || placa.Length != 7))
            return BadRequest(new { mensagem = "Para o entregador, informe modelo, cor e placa válida do veículo." });

        try
        {
            await using var db = await AbrirBancoAsync(session.EmpresaId, cancellationToken);
            await GarantirTabelaAsync(db, cancellationToken);
            const string sql = """
                INSERT INTO app.entrega_perfil_usuario
                    (empresa_id, usuario_id, perfil, telefone, veiculo_modelo, veiculo_cor, veiculo_placa, atualizado_em)
                VALUES (@empresa, @usuario, @perfil, @telefone, @modelo, @cor, @placa, now())
                ON CONFLICT (empresa_id, usuario_id) DO UPDATE SET
                    perfil=EXCLUDED.perfil, telefone=EXCLUDED.telefone,
                    veiculo_modelo=EXCLUDED.veiculo_modelo, veiculo_cor=EXCLUDED.veiculo_cor,
                    veiculo_placa=EXCLUDED.veiculo_placa, atualizado_em=now()
                """;
            await using var command = new NpgsqlCommand(sql, db);
            command.Parameters.AddWithValue("empresa", session.EmpresaId);
            command.Parameters.AddWithValue("usuario", session.UsuarioId);
            command.Parameters.AddWithValue("perfil", perfil);
            command.Parameters.AddWithValue("telefone", telefone);
            command.Parameters.AddWithValue("modelo", perfil == "motorista" ? modelo : "");
            command.Parameters.AddWithValue("cor", perfil == "motorista" ? cor : "");
            command.Parameters.AddWithValue("placa", perfil == "motorista" ? placa : "");
            await command.ExecuteNonQueryAsync(cancellationToken);
            return Ok(new DeliveryProfileResponse(perfil, telefone, modelo, cor, placa));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao salvar perfil APPENTREGA do usuário {UsuarioId}.", session.UsuarioId);
            return StatusCode(503, new { mensagem = "Não foi possível salvar seu perfil de entrega." });
        }
    }

    private bool TrySession(out TenantSession session)
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (sessions.TryGet(authorization, out session)) return true;
        if (!masterSessions.Validar(authorization) ||
            !long.TryParse(Request.Headers["X-Empresa-Id"].ToString(), out var empresaId) || empresaId <= 0)
        {
            session = default!;
            return false;
        }
        session = new TenantSession(empresaId, 0, true, 0, DateTimeOffset.UtcNow.AddMinutes(5));
        return true;
    }

    private async Task<NpgsqlConnection> AbrirBancoAsync(long empresaId, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(await resolver.ObterConnectionString(empresaId));
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task GarantirTabelaAsync(NpgsqlConnection db, CancellationToken cancellationToken)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS app.entrega_perfil_usuario (
                empresa_id bigint NOT NULL,
                usuario_id bigint NOT NULL,
                perfil text NOT NULL CHECK (perfil IN ('cliente','motorista')),
                telefone text NOT NULL,
                veiculo_modelo text NOT NULL DEFAULT '',
                veiculo_cor text NOT NULL DEFAULT '',
                veiculo_placa text NOT NULL DEFAULT '',
                atualizado_em timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY (empresa_id, usuario_id)
            );
            """;
        await using var command = new NpgsqlCommand(sql, db);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed class SalvarPerfilEntregaRequest
{
    public string? Perfil { get; set; }
    public string? Telefone { get; set; }
    public string? VeiculoModelo { get; set; }
    public string? VeiculoCor { get; set; }
    public string? VeiculoPlaca { get; set; }
}

public sealed record DeliveryProfileResponse(
    string Perfil,
    string Telefone,
    string VeiculoModelo,
    string VeiculoCor,
    string VeiculoPlaca);

public sealed record CorridaMotoristaPerfilResponse(
    string Nome,
    string Telefone,
    string VeiculoModelo,
    string VeiculoCor,
    string VeiculoPlaca);
