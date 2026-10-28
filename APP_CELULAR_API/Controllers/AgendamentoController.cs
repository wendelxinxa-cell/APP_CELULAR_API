using APP_CELULAR_API.Models;
using APP_CELULAR_API.Services;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace APP_CELULAR_API.Controllers;

[ApiController]
[Route("api/agendamento")]
public sealed class AgendamentoController(
    IEmpresaDatabaseResolver resolver,
    TenantSessionStore sessions,
    MasterSessionStore masterSessions) : ControllerBase
{
    private static readonly string[] Nichos = ["barbearia", "manicure", "petshop"];
    private static readonly string[] Status = ["confirmado", "pendente", "em_atendimento", "concluido", "cancelado"];

    [HttpGet("estado")]
    public async Task<IActionResult> ObterEstado()
    {
        var tenant = await AbrirTenant();
        if (tenant.Erro is not null) return tenant.Erro;
        long empresaId = tenant.EmpresaId;
        await using var db = tenant.Db!;
        {
            var estado = new AgendamentoEstadoResponse();
            await using (var cmd = new NpgsqlCommand("SELECT id,nome,whatsapp,COALESCE(observacoes,''),criado_em FROM public.agendamento_clientes WHERE empresa_id=@empresa ORDER BY nome", db))
            {
                cmd.Parameters.AddWithValue("empresa", empresaId);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) estado.Clientes.Add(new() { Id = reader.GetGuid(0), Nome = reader.GetString(1), Whatsapp = reader.GetString(2), Observacoes = reader.GetString(3), CriadoEm = reader.GetFieldValue<DateTimeOffset>(4) });
            }
            await using (var cmd = new NpgsqlCommand("SELECT id,cliente_id,nome_pet,COALESCE(raca,''),COALESCE(porte,''),COALESCE(observacoes_alergias,'') FROM public.agendamento_pets WHERE empresa_id=@empresa ORDER BY nome_pet", db))
            {
                cmd.Parameters.AddWithValue("empresa", empresaId);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) estado.Pets.Add(new() { Id = reader.GetGuid(0), ClienteId = reader.GetGuid(1), NomePet = reader.GetString(2), Raca = reader.GetString(3), Porte = reader.GetString(4), ObservacoesAlergias = reader.GetString(5) });
            }
            await using (var cmd = new NpgsqlCommand("SELECT id,nome,nicho,COALESCE(foto_url,''),ativo FROM public.agendamento_profissionais WHERE empresa_id=@empresa ORDER BY nome", db))
            {
                cmd.Parameters.AddWithValue("empresa", empresaId);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) estado.Profissionais.Add(new() { Id = reader.GetGuid(0), Nome = reader.GetString(1), Nicho = reader.GetString(2), FotoUrl = reader.GetString(3), Ativo = reader.GetBoolean(4) });
            }
            await using (var cmd = new NpgsqlCommand("SELECT id,nicho,nome,duracao_minutos,preco_brl,ativo FROM public.agendamento_servicos WHERE empresa_id=@empresa ORDER BY nome", db))
            {
                cmd.Parameters.AddWithValue("empresa", empresaId);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) estado.Servicos.Add(new() { Id = reader.GetGuid(0), Nicho = reader.GetString(1), Nome = reader.GetString(2), DuracaoMinutos = reader.GetInt32(3), PrecoBrl = reader.GetDecimal(4), Ativo = reader.GetBoolean(5) });
            }
            await using (var cmd = new NpgsqlCommand("SELECT id,nicho,nome_item,quantidade,unidade,estoque_minimo,atualizado_em FROM public.agendamento_materiais WHERE empresa_id=@empresa ORDER BY nome_item", db))
            {
                cmd.Parameters.AddWithValue("empresa", empresaId);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) estado.Materiais.Add(new() { Id = reader.GetGuid(0), Nicho = reader.GetString(1), NomeItem = reader.GetString(2), Quantidade = reader.GetDecimal(3), Unidade = reader.GetString(4), EstoqueMinimo = reader.GetDecimal(5), AtualizadoEm = reader.GetFieldValue<DateTimeOffset>(6) });
            }
            await using (var cmd = new NpgsqlCommand("""
                SELECT a.id,a.cliente_id,c.nome, a.pet_id,COALESCE(p.nome_pet,''),a.servico_id,s.nome,s.duracao_minutos,
                       a.profissional_id,pr.nome,c.whatsapp,a.data_horario,a.status,a.valor_total,a.mensagem_whatsapp_enviada
                FROM public.agendamento_agendamentos a
                JOIN public.agendamento_clientes c ON c.empresa_id=a.empresa_id AND c.id=a.cliente_id
                LEFT JOIN public.agendamento_pets p ON p.empresa_id=a.empresa_id AND p.id=a.pet_id
                JOIN public.agendamento_servicos s ON s.empresa_id=a.empresa_id AND s.id=a.servico_id
                JOIN public.agendamento_profissionais pr ON pr.empresa_id=a.empresa_id AND pr.id=a.profissional_id
                WHERE a.empresa_id=@empresa ORDER BY a.data_horario DESC LIMIT 500
                """, db))
            {
                cmd.Parameters.AddWithValue("empresa", empresaId);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) estado.Agendamentos.Add(new() { Id = reader.GetGuid(0), ClienteId = reader.GetGuid(1), ClienteNome = reader.GetString(2), PetId = reader.IsDBNull(3) ? null : reader.GetGuid(3), PetNome = reader.GetString(4), ServicoId = reader.GetGuid(5), ServicoNome = reader.GetString(6), DuracaoMinutos = reader.GetInt32(7), ProfissionalId = reader.GetGuid(8), ProfissionalNome = reader.GetString(9), Whatsapp = reader.GetString(10), DataHorario = reader.GetFieldValue<DateTimeOffset>(11), Status = reader.GetString(12), ValorTotal = reader.GetDecimal(13), MensagemWhatsappEnviada = reader.GetBoolean(14) });
            }
            return Ok(estado);
        }
    }

    [HttpPost("clientes")]
    public async Task<IActionResult> CriarCliente([FromBody] AgendamentoClienteRequest request)
    {
        string nome = request.Nome.Trim();
        string whatsapp = new string(request.Whatsapp.Where(char.IsAsciiDigit).ToArray());
        if (nome.Length is < 2 or > 250 || whatsapp.Length is < 10 or > 15 || (request.Observacoes?.Length ?? 0) > 4000)
            return BadRequest(new { mensagem = "Informe o nome do cliente e um WhatsApp com DDI e DDD." });
        var tenant = await AbrirTenant();
        if (tenant.Erro is not null) return tenant.Erro;
        long empresaId = tenant.EmpresaId;
        await using var db = tenant.Db!;
        await using (var cmd = new NpgsqlCommand("""
            INSERT INTO public.agendamento_clientes(empresa_id,nome,whatsapp,observacoes)
            VALUES(@empresa,@nome,@whatsapp,@observacoes)
            ON CONFLICT(empresa_id,whatsapp) DO UPDATE SET nome=EXCLUDED.nome,observacoes=EXCLUDED.observacoes
            RETURNING id,nome,whatsapp,COALESCE(observacoes,''),criado_em
            """, db))
        {
            cmd.Parameters.AddWithValue("empresa", empresaId); cmd.Parameters.AddWithValue("nome", nome);
            cmd.Parameters.AddWithValue("whatsapp", whatsapp); cmd.Parameters.AddWithValue("observacoes", (object?)request.Observacoes ?? DBNull.Value);
            await using var reader = await cmd.ExecuteReaderAsync(); await reader.ReadAsync();
            return Ok(new AgendamentoCliente { Id = reader.GetGuid(0), Nome = reader.GetString(1), Whatsapp = reader.GetString(2), Observacoes = reader.GetString(3), CriadoEm = reader.GetFieldValue<DateTimeOffset>(4) });
        }
    }

    [HttpPost("pets")]
    public async Task<IActionResult> CriarPet([FromBody] AgendamentoPetRequest request)
    {
        if (request.ClienteId == Guid.Empty || request.NomePet.Trim().Length is < 1 or > 100 || (request.Porte is not null and not ("Pequeno" or "Médio" or "Grande")))
            return BadRequest(new { mensagem = "Confira os dados do pet e selecione um porte válido." });
        var tenant = await AbrirTenant();
        if (tenant.Erro is not null) return tenant.Erro;
        long empresaId = tenant.EmpresaId;
        await using var db = tenant.Db!;
        await using (var cmd = new NpgsqlCommand("INSERT INTO public.agendamento_pets(empresa_id,cliente_id,nome_pet,raca,porte,observacoes_alergias) VALUES(@empresa,@cliente,@nome,@raca,@porte,@alergias) RETURNING id", db))
        {
            cmd.Parameters.AddWithValue("empresa", empresaId); cmd.Parameters.AddWithValue("cliente", request.ClienteId);
            cmd.Parameters.AddWithValue("nome", request.NomePet.Trim()); cmd.Parameters.AddWithValue("raca", (object?)request.Raca ?? DBNull.Value);
            cmd.Parameters.AddWithValue("porte", (object?)request.Porte ?? DBNull.Value); cmd.Parameters.AddWithValue("alergias", (object?)request.ObservacoesAlergias ?? DBNull.Value);
            try { return Ok(new { id = await cmd.ExecuteScalarAsync() }); }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation) { return BadRequest(new { mensagem = "O cliente selecionado não existe nesta empresa." }); }
        }
    }

    [HttpPost("profissionais")]
    public async Task<IActionResult> CriarProfissional([FromBody] AgendamentoProfissionalRequest request)
    {
        if (request.Nome.Trim().Length is < 2 or > 200 || (request.Nicho != "todos" && !NichoValido(request.Nicho)) || (request.FotoUrl?.Length ?? 0) > 2048)
            return BadRequest(new { mensagem = "Confira o nome, nicho e endereço da foto do profissional." });
        var tenant = await AbrirTenant();
        if (tenant.Erro is not null) return tenant.Erro;
        long empresaId = tenant.EmpresaId;
        await using var db = tenant.Db!;
        await using (var cmd = new NpgsqlCommand("INSERT INTO public.agendamento_profissionais(empresa_id,nome,nicho,foto_url) VALUES(@empresa,@nome,@nicho,@foto) RETURNING id", db))
        {
            cmd.Parameters.AddWithValue("empresa", empresaId); cmd.Parameters.AddWithValue("nome", request.Nome.Trim()); cmd.Parameters.AddWithValue("nicho", request.Nicho); cmd.Parameters.AddWithValue("foto", (object?)request.FotoUrl ?? DBNull.Value);
            return Ok(new { id = await cmd.ExecuteScalarAsync() });
        }
    }

    [HttpPost("servicos")]
    public async Task<IActionResult> CriarServico([FromBody] AgendamentoServicoRequest request)
    {
        if (!NichoValido(request.Nicho) || request.Nome.Trim().Length is < 2 or > 200 || request.DuracaoMinutos is < 5 or > 1440 || request.PrecoBrl < 0)
            return BadRequest(new { mensagem = "Confira o nicho, nome, duração e preço do serviço." });
        var tenant = await AbrirTenant();
        if (tenant.Erro is not null) return tenant.Erro;
        long empresaId = tenant.EmpresaId;
        await using var db = tenant.Db!;
        await using (var cmd = new NpgsqlCommand("INSERT INTO public.agendamento_servicos(empresa_id,nicho,nome,duracao_minutos,preco_brl) VALUES(@empresa,@nicho,@nome,@duracao,@preco) RETURNING id", db))
        {
            cmd.Parameters.AddWithValue("empresa", empresaId); cmd.Parameters.AddWithValue("nicho", request.Nicho); cmd.Parameters.AddWithValue("nome", request.Nome.Trim()); cmd.Parameters.AddWithValue("duracao", request.DuracaoMinutos); cmd.Parameters.AddWithValue("preco", request.PrecoBrl);
            return Ok(new { id = await cmd.ExecuteScalarAsync() });
        }
    }

    [HttpPost("materiais")]
    public async Task<IActionResult> CriarMaterial([FromBody] AgendamentoMaterialRequest request)
    {
        if (!NichoValido(request.Nicho) || request.NomeItem.Trim().Length is < 2 or > 200 || request.Unidade.Trim().Length is < 1 or > 20 || request.Quantidade < 0 || request.EstoqueMinimo < 0)
            return BadRequest(new { mensagem = "Confira o material, nicho, quantidade, unidade e estoque mínimo." });
        var tenant = await AbrirTenant();
        if (tenant.Erro is not null) return tenant.Erro;
        long empresaId = tenant.EmpresaId;
        await using var db = tenant.Db!;
        await using (var cmd = new NpgsqlCommand("INSERT INTO public.agendamento_materiais(empresa_id,nicho,nome_item,quantidade,unidade,estoque_minimo,atualizado_em) VALUES(@empresa,@nicho,@nome,@quantidade,@unidade,@minimo,NOW()) RETURNING id", db))
        {
            cmd.Parameters.AddWithValue("empresa", empresaId); cmd.Parameters.AddWithValue("nicho", request.Nicho); cmd.Parameters.AddWithValue("nome", request.NomeItem.Trim()); cmd.Parameters.AddWithValue("quantidade", request.Quantidade); cmd.Parameters.AddWithValue("unidade", request.Unidade.Trim()); cmd.Parameters.AddWithValue("minimo", request.EstoqueMinimo);
            return Ok(new { id = await cmd.ExecuteScalarAsync() });
        }
    }

    [HttpPost("agendamentos")]
    public async Task<IActionResult> CriarAgendamento([FromBody] CriarAgendamentoRequest request)
    {
        if (request.ClienteId == Guid.Empty || request.ServicoId == Guid.Empty || request.ProfissionalId == Guid.Empty || request.DataHorario <= DateTimeOffset.UtcNow.AddMinutes(-5) || (request.ValorTotal is < 0) || (request.Status != "confirmado" && request.Status != "pendente"))
            return BadRequest(new { mensagem = "Confira cliente, serviço, profissional, data, horário e status do agendamento." });
        var tenant = await AbrirTenant();
        if (tenant.Erro is not null) return tenant.Erro;
        long empresaId = tenant.EmpresaId;
        await using var db = tenant.Db!;
        {
            const string durationSql = "SELECT duracao_minutos,preco_brl FROM public.agendamento_servicos WHERE empresa_id=@empresa AND id=@servico AND ativo";
            int duration; decimal price;
            await using (var service = new NpgsqlCommand(durationSql, db))
            {
                service.Parameters.AddWithValue("empresa", empresaId); service.Parameters.AddWithValue("servico", request.ServicoId);
                await using var reader = await service.ExecuteReaderAsync();
                if (!await reader.ReadAsync()) return BadRequest(new { mensagem = "O serviço selecionado está indisponível." });
                duration = reader.GetInt32(0); price = reader.GetDecimal(1);
            }
            if (request.PetId is Guid petId)
            {
                await using var pet = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM public.agendamento_pets WHERE empresa_id=@empresa AND id=@pet AND cliente_id=@cliente)", db);
                pet.Parameters.AddWithValue("empresa", empresaId); pet.Parameters.AddWithValue("pet", petId); pet.Parameters.AddWithValue("cliente", request.ClienteId);
                if (await pet.ExecuteScalarAsync() is not true) return BadRequest(new { mensagem = "O pet não pertence ao cliente selecionado." });
            }
            if (request.Status != "cancelado" && request.Status != "pendente")
            {
                const string overlapSql = """
                    SELECT EXISTS(SELECT 1 FROM public.agendamento_agendamentos a
                    JOIN public.agendamento_servicos s ON s.empresa_id=a.empresa_id AND s.id=a.servico_id
                    WHERE a.empresa_id=@empresa AND a.profissional_id=@profissional
                      AND a.status NOT IN ('cancelado','concluido')
                      AND a.data_horario < @inicio + (@duracao * INTERVAL '1 minute')
                      AND a.data_horario + (s.duracao_minutos * INTERVAL '1 minute') > @inicio)
                    """;
                await using var overlap = new NpgsqlCommand(overlapSql, db);
                overlap.Parameters.AddWithValue("empresa", empresaId); overlap.Parameters.AddWithValue("profissional", request.ProfissionalId); overlap.Parameters.AddWithValue("inicio", request.DataHorario); overlap.Parameters.AddWithValue("duracao", duration);
                if (await overlap.ExecuteScalarAsync() is true) return Conflict(new { mensagem = "Este profissional já possui um atendimento nesse horário." });
            }
            const string insert = "INSERT INTO public.agendamento_agendamentos(empresa_id,cliente_id,pet_id,servico_id,profissional_id,data_horario,status,valor_total) VALUES(@empresa,@cliente,@pet,@servico,@profissional,@data,@status,@valor) RETURNING id";
            await using var cmd = new NpgsqlCommand(insert, db);
            cmd.Parameters.AddWithValue("empresa", empresaId); cmd.Parameters.AddWithValue("cliente", request.ClienteId); cmd.Parameters.AddWithValue("pet", (object?)request.PetId ?? DBNull.Value); cmd.Parameters.AddWithValue("servico", request.ServicoId); cmd.Parameters.AddWithValue("profissional", request.ProfissionalId); cmd.Parameters.AddWithValue("data", request.DataHorario); cmd.Parameters.AddWithValue("status", request.Status); cmd.Parameters.AddWithValue("valor", request.ValorTotal ?? price);
            try { return Ok(new { id = await cmd.ExecuteScalarAsync() }); }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation) { return BadRequest(new { mensagem = "Um dos itens selecionados não pertence a esta empresa." }); }
        }
    }

    [HttpPost("agendamentos/{id:guid}/status")]
    public async Task<IActionResult> AtualizarStatus(Guid id, [FromBody] AtualizarStatusAgendamentoRequest request)
    {
        if (id == Guid.Empty || !Status.Contains(request.Status, StringComparer.Ordinal)) return BadRequest(new { mensagem = "Status de agendamento inválido." });
        var tenant = await AbrirTenant();
        if (tenant.Erro is not null) return tenant.Erro;
        long empresaId = tenant.EmpresaId;
        await using var db = tenant.Db!;
        await using (var cmd = new NpgsqlCommand("UPDATE public.agendamento_agendamentos SET status=@status WHERE empresa_id=@empresa AND id=@id RETURNING id", db))
        {
            cmd.Parameters.AddWithValue("status", request.Status); cmd.Parameters.AddWithValue("empresa", empresaId); cmd.Parameters.AddWithValue("id", id);
            return await cmd.ExecuteScalarAsync() is Guid ? Ok(new { sucesso = true }) : NotFound(new { mensagem = "Agendamento não encontrado." });
        }
    }

    private async Task<(long EmpresaId, NpgsqlConnection? Db, IActionResult? Erro)> AbrirTenant()
    {
        var authorization = Request.Headers.Authorization.ToString();
        long empresaId;
        NpgsqlConnection? db = null;
        if (sessions.TryGet(authorization, out var session))
        {
            empresaId = session.EmpresaId;
            var empresaSolicitada = Request.Headers["X-Empresa-Id"].ToString();
            if (!string.IsNullOrWhiteSpace(empresaSolicitada) &&
                (!long.TryParse(empresaSolicitada, out var empresaInformada) || empresaInformada != empresaId))
                return (0, null, Forbid());
        }
        else if (masterSessions.Validar(authorization))
        {
            if (!long.TryParse(Request.Headers["X-Empresa-Id"].ToString(), out empresaId) || empresaId <= 0)
                return (0, null, BadRequest(new { mensagem = "Selecione uma empresa válida para abrir a agenda." }));
        }
        else
        {
            return (0, null, Unauthorized(new { mensagem = "Sessão expirada. Entre novamente para acessar os agendamentos." }));
        }

        try
        {
            db = new NpgsqlConnection(await resolver.ObterConnectionString(empresaId));
            await db.OpenAsync();
            return (empresaId, db, null);
        }
        catch
        {
            if (db is not null) await db.DisposeAsync();
            db = null;
            return (0, null, StatusCode(503, new { mensagem = "O banco de agendamentos não está disponível. Aplique a migração 039_agendamento_saas.sql." }));
        }
    }

    private static bool NichoValido(string valor) => Nichos.Contains(valor, StringComparer.Ordinal);
}
