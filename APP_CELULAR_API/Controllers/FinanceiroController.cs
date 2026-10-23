using APP_CELULAR_API.Models;
using APP_CELULAR_API.Services;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using NpgsqlTypes;

namespace APP_CELULAR_API.Controllers;

[ApiController]
[Route("api/financeiro")]
public sealed class FinanceiroController : ControllerBase
{
    private readonly IEmpresaDatabaseResolver _resolver;
    private readonly TenantSessionStore _sessions;

    public FinanceiroController(IEmpresaDatabaseResolver resolver, TenantSessionStore sessions)
    {
        _resolver = resolver;
        _sessions = sessions;
    }

    [HttpPost("sincronizar")]
    public async Task<IActionResult> Sincronizar([FromBody] FinanceiroSyncRequest request)
    {
        if (!_sessions.TryGet(Request.Headers.Authorization.ToString(), out var session))
            return Unauthorized(new { mensagem = "Sessão expirada. Entre novamente para sincronizar o financeiro." });
        if (request.EmpresaId != session.EmpresaId)
            return StatusCode(403, new { mensagem = "A sessão não pertence à empresa informada." });
        if (request.Tipos.Count > 300 || request.Recorrencias.Count > 2000 || request.Lancamentos.Count > 5000)
            return BadRequest(new { mensagem = "O lote financeiro excedeu o limite permitido." });

        string connectionString;
        try { connectionString = await _resolver.ObterConnectionString(session.EmpresaId); }
        catch { return StatusCode(503, new { mensagem = "O banco financeiro da empresa não está configurado." }); }

        await using var db = new NpgsqlConnection(connectionString);
        await db.OpenAsync();
        await GarantirEsquemaAsync(db);
        await using var transaction = await db.BeginTransactionAsync();
        try
        {
            foreach (var item in request.Tipos)
            {
                if (!Guid.TryParse(item.Chave, out var chave) || item.Nome.Trim().Length is < 2 or > 50 || item.Cor.Length > 16)
                    return BadRequest(new { mensagem = "Há um tipo financeiro inválido." });
                await using var cmd = new NpgsqlCommand("""
                    INSERT INTO app.financeiro_tipo(empresa_id,chave,nome,cor,data_alteracao,excluido)
                    VALUES(@empresa,@chave,@nome,@cor,@alterado,@excluido)
                    ON CONFLICT(empresa_id,chave) DO UPDATE SET nome=EXCLUDED.nome,cor=EXCLUDED.cor,data_alteracao=EXCLUDED.data_alteracao,excluido=EXCLUDED.excluido
                    WHERE EXCLUDED.data_alteracao >= app.financeiro_tipo.data_alteracao;
                    """, db, transaction);
                cmd.Parameters.AddWithValue("empresa", session.EmpresaId);
                cmd.Parameters.AddWithValue("chave", chave);
                cmd.Parameters.AddWithValue("nome", item.Nome.Trim());
                cmd.Parameters.AddWithValue("cor", item.Cor);
                AddTimestamp(cmd, "alterado", item.DataAlteracao);
                cmd.Parameters.AddWithValue("excluido", item.Excluido);
                await cmd.ExecuteNonQueryAsync();
            }

            foreach (var item in request.Recorrencias)
            {
                if (!Guid.TryParse(item.Chave, out var chave) || !Guid.TryParse(item.TipoChave, out var tipoChave) ||
                    item.Descricao.Trim().Length is < 2 or > 100 || item.Valor <= 0 || item.DiaVencimento is < 1 or > 31 || item.IntervaloMeses is < 1 or > 12 ||
                    !NaturezaValida(item.Natureza) || item.DataFim?.Date < item.DataInicio.Date)
                    return BadRequest(new { mensagem = "Há uma recorrência financeira inválida." });
                await using var cmd = new NpgsqlCommand("""
                    INSERT INTO app.financeiro_recorrencia(empresa_id,chave,tipo_chave,tipo_nome,natureza,descricao,observacao,valor,dia_vencimento,intervalo_meses,data_inicio,data_fim,ativa,data_alteracao,excluido)
                    VALUES(@empresa,@chave,@tipo,@tipoNome,@natureza,@descricao,@observacao,@valor,@dia,@intervalo,@inicio,@fim,@ativa,@alterado,@excluido)
                    ON CONFLICT(empresa_id,chave) DO UPDATE SET tipo_chave=EXCLUDED.tipo_chave,tipo_nome=EXCLUDED.tipo_nome,natureza=EXCLUDED.natureza,descricao=EXCLUDED.descricao,observacao=EXCLUDED.observacao,valor=EXCLUDED.valor,dia_vencimento=EXCLUDED.dia_vencimento,intervalo_meses=EXCLUDED.intervalo_meses,data_inicio=EXCLUDED.data_inicio,data_fim=EXCLUDED.data_fim,ativa=EXCLUDED.ativa,data_alteracao=EXCLUDED.data_alteracao,excluido=EXCLUDED.excluido
                    WHERE EXCLUDED.data_alteracao >= app.financeiro_recorrencia.data_alteracao;
                    """, db, transaction);
                cmd.Parameters.AddWithValue("empresa", session.EmpresaId);
                cmd.Parameters.AddWithValue("chave", chave);
                cmd.Parameters.AddWithValue("tipo", tipoChave);
                cmd.Parameters.AddWithValue("tipoNome", item.TipoNome);
                cmd.Parameters.AddWithValue("natureza", item.Natureza);
                cmd.Parameters.AddWithValue("descricao", item.Descricao.Trim());
                cmd.Parameters.AddWithValue("observacao", item.Observacao ?? "");
                cmd.Parameters.AddWithValue("valor", item.Valor);
                cmd.Parameters.AddWithValue("dia", item.DiaVencimento);
                cmd.Parameters.AddWithValue("intervalo", item.IntervaloMeses);
                AddDate(cmd, "inicio", item.DataInicio);
                AddNullableDate(cmd, "fim", item.DataFim);
                cmd.Parameters.AddWithValue("ativa", item.Ativa);
                AddTimestamp(cmd, "alterado", item.DataAlteracao);
                cmd.Parameters.AddWithValue("excluido", item.Excluido);
                await cmd.ExecuteNonQueryAsync();
            }

            foreach (var item in request.Lancamentos)
            {
                if (!Guid.TryParse(item.Chave, out var chave) || !Guid.TryParse(item.TipoChave, out var tipoChave) ||
                    (!string.IsNullOrWhiteSpace(item.RecorrenciaChave) && !Guid.TryParse(item.RecorrenciaChave, out _)) ||
                    item.Descricao.Trim().Length is < 2 or > 100 || item.Valor <= 0 || !NaturezaValida(item.Natureza) ||
                    item.TipoLancamento is not ("FIXO" or "VARIAVEL") || item.Status is not ("PENDENTE" or "PAGO" or "RECEBIDO" or "CANCELADO"))
                    return BadRequest(new { mensagem = "Há um lançamento financeiro inválido." });
                await using var cmd = new NpgsqlCommand("""
                    INSERT INTO app.financeiro_lancamento(empresa_id,chave,recorrencia_chave,tipo_chave,tipo_nome,natureza,tipo_lancamento,descricao,observacao,valor,data_vencimento,data_liquidacao,status,data_alteracao,excluido)
                    VALUES(@empresa,@chave,@recorrencia,@tipo,@tipoNome,@natureza,@tipoLancamento,@descricao,@observacao,@valor,@vencimento,@liquidacao,@status,@alterado,@excluido)
                    ON CONFLICT(empresa_id,chave) DO UPDATE SET recorrencia_chave=EXCLUDED.recorrencia_chave,tipo_chave=EXCLUDED.tipo_chave,tipo_nome=EXCLUDED.tipo_nome,natureza=EXCLUDED.natureza,tipo_lancamento=EXCLUDED.tipo_lancamento,descricao=EXCLUDED.descricao,observacao=EXCLUDED.observacao,valor=EXCLUDED.valor,data_vencimento=EXCLUDED.data_vencimento,data_liquidacao=EXCLUDED.data_liquidacao,status=EXCLUDED.status,data_alteracao=EXCLUDED.data_alteracao,excluido=EXCLUDED.excluido
                    WHERE EXCLUDED.data_alteracao >= app.financeiro_lancamento.data_alteracao;
                    """, db, transaction);
                cmd.Parameters.AddWithValue("empresa", session.EmpresaId);
                cmd.Parameters.AddWithValue("chave", chave);
                AddNullableGuid(cmd, "recorrencia", item.RecorrenciaChave);
                cmd.Parameters.AddWithValue("tipo", tipoChave);
                cmd.Parameters.AddWithValue("tipoNome", item.TipoNome);
                cmd.Parameters.AddWithValue("natureza", item.Natureza);
                cmd.Parameters.AddWithValue("tipoLancamento", item.TipoLancamento);
                cmd.Parameters.AddWithValue("descricao", item.Descricao.Trim());
                cmd.Parameters.AddWithValue("observacao", item.Observacao ?? "");
                cmd.Parameters.AddWithValue("valor", item.Valor);
                AddDate(cmd, "vencimento", item.DataVencimento);
                AddNullableDate(cmd, "liquidacao", item.DataLiquidacao);
                cmd.Parameters.AddWithValue("status", item.Status);
                AddTimestamp(cmd, "alterado", item.DataAlteracao);
                cmd.Parameters.AddWithValue("excluido", item.Excluido);
                await cmd.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
            return Ok(await LerEstadoAsync(db, session.EmpresaId));
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return StatusCode(500, new { mensagem = "Não foi possível sincronizar o financeiro.", detalhe = ex.Message });
        }
    }

    private static async Task<FinanceiroSyncResponse> LerEstadoAsync(NpgsqlConnection db, long empresaId)
    {
        var estado = new FinanceiroSyncResponse();
        await using (var cmd = new NpgsqlCommand("SELECT replace(chave::text,'-',''),nome,cor,data_alteracao,excluido FROM app.financeiro_tipo WHERE empresa_id=@empresa ORDER BY nome", db))
        {
            cmd.Parameters.AddWithValue("empresa", empresaId);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) estado.Tipos.Add(new FinanceiroTipoDto
            {
                Chave = reader.GetString(0), Nome = reader.GetString(1), Cor = reader.GetString(2), DataAlteracao = reader.GetDateTime(3), Excluido = reader.GetBoolean(4)
            });
        }
        await using (var cmd = new NpgsqlCommand("SELECT replace(chave::text,'-',''),replace(tipo_chave::text,'-',''),tipo_nome,natureza,descricao,observacao,valor,dia_vencimento,intervalo_meses,data_inicio,data_fim,ativa,data_alteracao,excluido FROM app.financeiro_recorrencia WHERE empresa_id=@empresa ORDER BY data_inicio", db))
        {
            cmd.Parameters.AddWithValue("empresa", empresaId);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) estado.Recorrencias.Add(new FinanceiroRecorrenciaDto
            {
                Chave = reader.GetString(0), TipoChave = reader.GetString(1), TipoNome = reader.GetString(2), Natureza = reader.GetString(3),
                Descricao = reader.GetString(4), Observacao = reader.GetString(5), Valor = reader.GetDecimal(6), DiaVencimento = reader.GetInt32(7),
                IntervaloMeses = reader.GetInt32(8), DataInicio = reader.GetDateTime(9), DataFim = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                Ativa = reader.GetBoolean(11), DataAlteracao = reader.GetDateTime(12), Excluido = reader.GetBoolean(13)
            });
        }
        await using (var cmd = new NpgsqlCommand("SELECT replace(chave::text,'-',''),CASE WHEN recorrencia_chave IS NULL THEN NULL ELSE replace(recorrencia_chave::text,'-','') END,replace(tipo_chave::text,'-',''),tipo_nome,natureza,tipo_lancamento,descricao,observacao,valor,data_vencimento,data_liquidacao,status,data_alteracao,excluido FROM app.financeiro_lancamento WHERE empresa_id=@empresa ORDER BY data_vencimento", db))
        {
            cmd.Parameters.AddWithValue("empresa", empresaId);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) estado.Lancamentos.Add(new FinanceiroLancamentoDto
            {
                Chave = reader.GetString(0), RecorrenciaChave = reader.IsDBNull(1) ? null : reader.GetString(1), TipoChave = reader.GetString(2),
                TipoNome = reader.GetString(3), Natureza = reader.GetString(4), TipoLancamento = reader.GetString(5), Descricao = reader.GetString(6),
                Observacao = reader.GetString(7), Valor = reader.GetDecimal(8), DataVencimento = reader.GetDateTime(9),
                DataLiquidacao = reader.IsDBNull(10) ? null : reader.GetDateTime(10), Status = reader.GetString(11),
                DataAlteracao = reader.GetDateTime(12), Excluido = reader.GetBoolean(13)
            });
        }
        return estado;
    }

    private static async Task GarantirEsquemaAsync(NpgsqlConnection db)
    {
        const string sql = """
            CREATE SCHEMA IF NOT EXISTS app;
            CREATE TABLE IF NOT EXISTS app.financeiro_tipo(
              empresa_id BIGINT NOT NULL, chave UUID NOT NULL, nome TEXT NOT NULL, cor TEXT NOT NULL DEFAULT '#6C63FF',
              data_alteracao TIMESTAMPTZ NOT NULL DEFAULT NOW(), excluido BOOLEAN NOT NULL DEFAULT FALSE,
              PRIMARY KEY(empresa_id,chave));
            CREATE TABLE IF NOT EXISTS app.financeiro_recorrencia(
              empresa_id BIGINT NOT NULL, chave UUID NOT NULL, tipo_chave UUID NOT NULL, tipo_nome TEXT NOT NULL DEFAULT '',
              natureza TEXT NOT NULL CHECK(natureza IN ('PAGAR','RECEBER')), descricao TEXT NOT NULL, observacao TEXT NOT NULL DEFAULT '',
              valor NUMERIC(14,2) NOT NULL CHECK(valor > 0), dia_vencimento SMALLINT NOT NULL CHECK(dia_vencimento BETWEEN 1 AND 31),
              intervalo_meses SMALLINT NOT NULL DEFAULT 1 CHECK(intervalo_meses BETWEEN 1 AND 12), data_inicio DATE NOT NULL,
              data_fim DATE NULL, ativa BOOLEAN NOT NULL DEFAULT TRUE, data_alteracao TIMESTAMPTZ NOT NULL DEFAULT NOW(),
              excluido BOOLEAN NOT NULL DEFAULT FALSE, PRIMARY KEY(empresa_id,chave));
            CREATE TABLE IF NOT EXISTS app.financeiro_lancamento(
              empresa_id BIGINT NOT NULL, chave UUID NOT NULL, recorrencia_chave UUID NULL, tipo_chave UUID NOT NULL,
              tipo_nome TEXT NOT NULL DEFAULT '', natureza TEXT NOT NULL CHECK(natureza IN ('PAGAR','RECEBER')),
              tipo_lancamento TEXT NOT NULL CHECK(tipo_lancamento IN ('FIXO','VARIAVEL')), descricao TEXT NOT NULL,
              observacao TEXT NOT NULL DEFAULT '', valor NUMERIC(14,2) NOT NULL CHECK(valor > 0), data_vencimento DATE NOT NULL,
              data_liquidacao DATE NULL, status TEXT NOT NULL CHECK(status IN ('PENDENTE','PAGO','RECEBIDO','CANCELADO')),
              data_alteracao TIMESTAMPTZ NOT NULL DEFAULT NOW(), excluido BOOLEAN NOT NULL DEFAULT FALSE,
              PRIMARY KEY(empresa_id,chave));
            CREATE UNIQUE INDEX IF NOT EXISTS ux_financeiro_lancamento_recorrencia_data
              ON app.financeiro_lancamento(empresa_id,recorrencia_chave,data_vencimento) WHERE recorrencia_chave IS NOT NULL;
            CREATE INDEX IF NOT EXISTS ix_financeiro_lancamento_periodo
              ON app.financeiro_lancamento(empresa_id,data_vencimento) WHERE excluido=FALSE;
            """;
        await using var command = new NpgsqlCommand(sql, db);
        await command.ExecuteNonQueryAsync();
    }

    private static bool NaturezaValida(string value) => value is "PAGAR" or "RECEBER";

    private static void AddTimestamp(NpgsqlCommand command, string name, DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        command.Parameters.Add(name, NpgsqlDbType.TimestampTz).Value = utc;
    }

    private static void AddDate(NpgsqlCommand command, string name, DateTime value) =>
        command.Parameters.Add(name, NpgsqlDbType.Date).Value = value.Date;

    private static void AddNullableDate(NpgsqlCommand command, string name, DateTime? value) =>
        command.Parameters.Add(name, NpgsqlDbType.Date).Value = value.HasValue ? value.Value.Date : DBNull.Value;

    private static void AddNullableGuid(NpgsqlCommand command, string name, string? value) =>
        command.Parameters.Add(name, NpgsqlDbType.Uuid).Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : Guid.Parse(value);
}
