using APP_CELULAR_API.Models;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using APP_CELULAR_API.Services;

namespace APP_CELULAR_API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SincronizacaoController : ControllerBase
{
    private readonly IEmpresaDatabaseResolver _databaseResolver;
    private readonly TenantSessionStore _sessions;

    public SincronizacaoController(
        IEmpresaDatabaseResolver databaseResolver,
        TenantSessionStore sessions)
    {
        _databaseResolver = databaseResolver;
        _sessions = sessions;
    }
    [HttpPost("enviar")]
    public async Task<IActionResult> Enviar(
        [FromBody] SincronizacaoRequest request)
    {
        if (request == null)
        {
            return BadRequest(new
            {
                sucesso = false,
                mensagem = "Dados de sincronização não informados."
            });
        }

        if (request.EmpresaId <= 0)
        {
            return BadRequest(new
            {
                sucesso = false,
                mensagem = "EmpresaId inválido."
            });
        }

        if (request.DispositivoId <= 0)
        {
            return BadRequest(new
            {
                sucesso = false,
                mensagem = "DispositivoId inválido."
            });
        }

        if (!_sessions.TryGet(Request.Headers.Authorization.ToString(), out var session))
            return Unauthorized(new { sucesso = false, mensagem = "Sessão da API expirada. Entre novamente." });
        if (session.EmpresaId != request.EmpresaId)
            return StatusCode(403, new { sucesso = false, mensagem = "A sessão não pertence à empresa informada." });
        if (session.DispositivoId != request.DispositivoId)
            return StatusCode(403, new { sucesso = false, mensagem = "A sessão não pertence a este aparelho." });
        if (request.Usuarios.Count > 0 && !session.EhAdministrador)
            return StatusCode(403, new { sucesso = false, mensagem = "Somente administradores podem sincronizar usuários." });

        string connectionString;

        try
        {
            connectionString =
                await _databaseResolver.ObterConnectionString(
                    request.EmpresaId);
        }
        catch (Exception ex)
        {
            return StatusCode(
                500,
                new
                {
                    sucesso = false,
                    mensagem =
                        "Não foi possível localizar o banco da empresa.",
                    erro = ex.Message
                });
        }

        var resposta = new SincronizacaoResponse();

        await using var conexao =
            new NpgsqlConnection(connectionString);

        await conexao.OpenAsync();

        await using var transacao =
            await conexao.BeginTransactionAsync();

        try
        {
            await ValidarDispositivo(
                conexao,
                transacao,
                request.EmpresaId,
                session.UsuarioId,
                request.DispositivoId);

            // =====================================================
            // PESSOAS
            // =====================================================

            foreach (var item in request.Pessoas)
            {
                try
                {
                    long idServidor =
                        await SincronizarPessoa(
                            conexao,
                            transacao,
                            request.EmpresaId,
                            request.DispositivoId,
                            item);

                    resposta.Registros.Add(
                        new IdSincronizado
                        {
                            Tabela = "pessoa",
                            IdLocal = item.IdLocal,
                            IdServidor = idServidor
                        });
                }
                catch (Exception ex)
                {
                    resposta.Erros.Add(
                        new ErroSincronizacao
                        {
                            Tabela = "pessoa",
                            IdLocal = item.IdLocal,
                            Mensagem = ex.Message
                        });
                }
            }

            // =====================================================
            // MATERIAIS
            // =====================================================

            foreach (var item in request.Materiais)
            {
                try
                {
                    long idServidor =
                        await SincronizarMaterial(
                            conexao,
                            transacao,
                            request.EmpresaId,
                            request.DispositivoId,
                            item);

                    resposta.Registros.Add(
                        new IdSincronizado
                        {
                            Tabela = "material",
                            IdLocal = item.IdLocal,
                            IdServidor = idServidor
                        });
                }
                catch (Exception ex)
                {
                    resposta.Erros.Add(
                        new ErroSincronizacao
                        {
                            Tabela = "material",
                            IdLocal = item.IdLocal,
                            Mensagem = ex.Message
                        });
                }
            }

            // =====================================================
            // COMPRAS
            // =====================================================

            foreach (var item in request.Compras)
            {
                try
                {
                    long idServidor =
                        await SincronizarCompra(
                            conexao,
                            transacao,
                            request.EmpresaId,
                            request.DispositivoId,
                            item);

                    resposta.Registros.Add(
                        new IdSincronizado
                        {
                            Tabela = "compra",
                            IdLocal = item.IdLocal,
                            IdServidor = idServidor
                        });
                }
                catch (Exception ex)
                {
                    resposta.Erros.Add(
                        new ErroSincronizacao
                        {
                            Tabela = "compra",
                            IdLocal = item.IdLocal,
                            Mensagem = ex.Message
                        });
                }
            }

            // =====================================================
            // ITENS DE COMPRA
            // =====================================================

            foreach (var item in request.ItensCompra)
            {
                try
                {
                    long idServidor =
                        await SincronizarItemCompra(
                            conexao,
                            transacao,
                            request.EmpresaId,
                            request.DispositivoId,
                            item);

                    resposta.Registros.Add(
                        new IdSincronizado
                        {
                            Tabela = "item_compra",
                            IdLocal = item.IdLocal,
                            IdServidor = idServidor
                        });
                }
                catch (Exception ex)
                {
                    resposta.Erros.Add(
                        new ErroSincronizacao
                        {
                            Tabela = "item_compra",
                            IdLocal = item.IdLocal,
                            Mensagem = ex.Message
                        });
                }
            }

            // =====================================================
            // VENDAS
            // =====================================================

            foreach (var item in request.Vendas)
            {
                try
                {
                    long idServidor =
                        await SincronizarVenda(
                            conexao,
                            transacao,
                            request.EmpresaId,
                            request.DispositivoId,
                            item);

                    resposta.Registros.Add(
                        new IdSincronizado
                        {
                            Tabela = "venda",
                            IdLocal = item.IdLocal,
                            IdServidor = idServidor
                        });
                }
                catch (Exception ex)
                {
                    resposta.Erros.Add(
                        new ErroSincronizacao
                        {
                            Tabela = "venda",
                            IdLocal = item.IdLocal,
                            Mensagem = ex.Message
                        });
                }
            }

            // =====================================================
            // ITENS DE VENDA
            // =====================================================

            foreach (var item in request.ItensVenda)
            {
                try
                {
                    long idServidor =
                        await SincronizarItemVenda(
                            conexao,
                            transacao,
                            request.EmpresaId,
                            request.DispositivoId,
                            item);

                    resposta.Registros.Add(
                        new IdSincronizado
                        {
                            Tabela = "item_venda",
                            IdLocal = item.IdLocal,
                            IdServidor = idServidor
                        });
                }
                catch (Exception ex)
                {
                    resposta.Erros.Add(
                        new ErroSincronizacao
                        {
                            Tabela = "item_venda",
                            IdLocal = item.IdLocal,
                            Mensagem = ex.Message
                        });
                }
            }

            // =====================================================
            // USUÁRIOS
            // =====================================================

            foreach (var item in request.Usuarios)
            {
                try
                {
                    long idServidor =
                        await SincronizarUsuario(
                            conexao,
                            transacao,
                            request.EmpresaId,
                            item);

                    resposta.Registros.Add(
                        new IdSincronizado
                        {
                            Tabela = "usuario",
                            IdLocal = item.IdLocal,
                            IdServidor = idServidor
                        });
                }
                catch (Exception ex)
                {
                    resposta.Erros.Add(
                        new ErroSincronizacao
                        {
                            Tabela = "usuario",
                            IdLocal = item.IdLocal,
                            Mensagem = ex.Message
                        });
                }
            }

            // =====================================================
            // CONFIGURAÇÃO
            // =====================================================

            foreach (var item in request.Configuracoes)
            {
                try
                {
                    await SincronizarConfiguracao(
                        conexao,
                        transacao,
                        request.EmpresaId,
                        item);
                }
                catch (Exception ex)
                {
                    resposta.Erros.Add(
                        new ErroSincronizacao
                        {
                            Tabela = "configuracao",
                            IdLocal = null,
                            Mensagem = ex.Message
                        });
                }
            }

            // Se houver algum erro, vamos manter a transação.
            // Os registros que deram certo serão confirmados.
            await transacao.CommitAsync();

            resposta.Sucesso =
                resposta.Erros.Count == 0;

            resposta.Mensagem =
                resposta.Sucesso
                    ? "Sincronização concluída com sucesso."
                    : "Sincronização concluída com alguns erros.";

            return Ok(resposta);
        }
        catch (Exception ex)
        {
            await transacao.RollbackAsync();

            return StatusCode(
                500,
                new
                {
                    sucesso = false,
                    mensagem =
                        "Erro geral na sincronização.",
                    erro = ex.Message
                });
        }
    }

    // =============================================================
    // VALIDAR DISPOSITIVO
    // =============================================================

    private static async Task ValidarDispositivo(
        NpgsqlConnection conexao,
        NpgsqlTransaction transacao,
        long empresaId,
        long usuarioId,
        long dispositivoId)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM app.dispositivo
            WHERE id = @dispositivoId
              AND empresa_id = @empresaId
              AND usuario_id = @usuarioId
              AND ativo = TRUE;
            """;

        await using var cmd =
            new NpgsqlCommand(
                sql,
                conexao,
                transacao);

        cmd.Parameters.AddWithValue(
            "dispositivoId",
            dispositivoId);

        cmd.Parameters.AddWithValue(
            "empresaId",
            empresaId);

        cmd.Parameters.AddWithValue(
            "usuarioId",
            usuarioId);

        long quantidade =
            Convert.ToInt64(
                await cmd.ExecuteScalarAsync());

        if (quantidade == 0)
        {
            throw new Exception(
                "Dispositivo não autorizado para esta empresa.");
        }
    }

    // =============================================================
    // PESSOA
    // =============================================================

    private static async Task<long> SincronizarPessoa(
        NpgsqlConnection conexao,
        NpgsqlTransaction transacao,
        long empresaId,
        long dispositivoId,
        PessoaSync item)
    {
        const string procura = """
            SELECT id
            FROM app.pessoa
            WHERE empresa_id = @empresaId
              AND dispositivo_id = @dispositivoId
              AND id_local = @idLocal
            LIMIT 1;
            """;

        await using var cmdProcura =
            new NpgsqlCommand(
                procura,
                conexao,
                transacao);

        cmdProcura.Parameters.AddWithValue(
            "empresaId",
            empresaId);

        cmdProcura.Parameters.AddWithValue(
            "dispositivoId",
            dispositivoId);

        cmdProcura.Parameters.AddWithValue(
            "idLocal",
            (object?)item.IdLocal ??
            DBNull.Value);

        object? encontrado =
            await cmdProcura.ExecuteScalarAsync();

        if (encontrado != null)
        {
            long id = Convert.ToInt64(encontrado);

            const string update = """
                UPDATE app.pessoa
                SET
                    nome = @nome,
                    documento = @documento,
                    telefone = @telefone,
                    email = @email,
                    endereco = @endereco,
                    cidade = @cidade,
                    cliente = @cliente,
                    fornecedor = @fornecedor,
                    data_alteracao = @dataAlteracao,
                    status_sincronizacao = 'SINCRONIZADO',
                    mensagem_erro = NULL,
                    excluido = @excluido
                WHERE id = @id;
                """;

            await using var cmd =
                new NpgsqlCommand(
                    update,
                    conexao,
                    transacao);

            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("nome", item.Nome);
            cmd.Parameters.AddWithValue("documento", (object?)item.Documento ?? DBNull.Value);
            cmd.Parameters.AddWithValue("telefone", (object?)item.Telefone ?? DBNull.Value);
            cmd.Parameters.AddWithValue("email", (object?)item.Email ?? DBNull.Value);
            cmd.Parameters.AddWithValue("endereco", (object?)item.Endereco ?? DBNull.Value);
            cmd.Parameters.AddWithValue("cidade", (object?)item.Cidade ?? DBNull.Value);
            cmd.Parameters.AddWithValue("cliente", item.Cliente);
            cmd.Parameters.AddWithValue("fornecedor", item.Fornecedor);
            cmd.Parameters.AddWithValue("dataAlteracao", item.DataAlteracao);
            cmd.Parameters.AddWithValue("excluido", item.Excluido);

            await cmd.ExecuteNonQueryAsync();

            return id;
        }

        const string insert = """
            INSERT INTO app.pessoa
            (
                id_local,
                nome,
                documento,
                telefone,
                email,
                endereco,
                cidade,
                cliente,
                fornecedor,
                data_cadastro,
                data_alteracao,
                status_sincronizacao,
                mensagem_erro,
                empresa_id,
                dispositivo_id,
                excluido
            )
            VALUES
            (
                @idLocal,
                @nome,
                @documento,
                @telefone,
                @email,
                @endereco,
                @cidade,
                @cliente,
                @fornecedor,
                @dataCadastro,
                @dataAlteracao,
                'SINCRONIZADO',
                NULL,
                @empresaId,
                @dispositivoId,
                @excluido
            )
            RETURNING id;
            """;

        await using var cmdInsert =
            new NpgsqlCommand(
                insert,
                conexao,
                transacao);

        cmdInsert.Parameters.AddWithValue("idLocal", (object?)item.IdLocal ?? DBNull.Value);
        cmdInsert.Parameters.AddWithValue("nome", item.Nome);
        cmdInsert.Parameters.AddWithValue("documento", (object?)item.Documento ?? DBNull.Value);
        cmdInsert.Parameters.AddWithValue("telefone", (object?)item.Telefone ?? DBNull.Value);
        cmdInsert.Parameters.AddWithValue("email", (object?)item.Email ?? DBNull.Value);
        cmdInsert.Parameters.AddWithValue("endereco", (object?)item.Endereco ?? DBNull.Value);
        cmdInsert.Parameters.AddWithValue("cidade", (object?)item.Cidade ?? DBNull.Value);
        cmdInsert.Parameters.AddWithValue("cliente", item.Cliente);
        cmdInsert.Parameters.AddWithValue("fornecedor", item.Fornecedor);
        cmdInsert.Parameters.AddWithValue("dataCadastro", item.DataCadastro);
        cmdInsert.Parameters.AddWithValue("dataAlteracao", item.DataAlteracao);
        cmdInsert.Parameters.AddWithValue("empresaId", empresaId);
        cmdInsert.Parameters.AddWithValue("dispositivoId", dispositivoId);
        cmdInsert.Parameters.AddWithValue("excluido", item.Excluido);

        return Convert.ToInt64(
            await cmdInsert.ExecuteScalarAsync());
    }

    // =============================================================
    // MATERIAL
    // =============================================================

    private static async Task<long> SincronizarMaterial(
        NpgsqlConnection conexao,
        NpgsqlTransaction transacao,
        long empresaId,
        long dispositivoId,
        MaterialSync item)
    {
        const string procura = """
            SELECT id
            FROM app.material
            WHERE empresa_id = @empresaId
              AND dispositivo_id = @dispositivoId
              AND id_local = @idLocal
            LIMIT 1;
            """;

        await using var cmdProcura =
            new NpgsqlCommand(
                procura,
                conexao,
                transacao);

        cmdProcura.Parameters.AddWithValue("empresaId", empresaId);
        cmdProcura.Parameters.AddWithValue("dispositivoId", dispositivoId);
        cmdProcura.Parameters.AddWithValue("idLocal", (object?)item.IdLocal ?? DBNull.Value);

        object? encontrado =
            await cmdProcura.ExecuteScalarAsync();

        if (encontrado != null)
        {
            long id = Convert.ToInt64(encontrado);

            const string update = """
                UPDATE app.material
                SET
                    nome = @nome,
                    unidade = @unidade,
                    preco_compra = @precoCompra,
                    preco_venda = @precoVenda,
                    estoque_atual = @estoqueAtual,
                    data_alteracao = @dataAlteracao,
                    status_sincronizacao = 'SINCRONIZADO',
                    mensagem_erro = NULL,
                    excluido = @excluido
                WHERE id = @id;
                """;

            await using var cmd =
                new NpgsqlCommand(
                    update,
                    conexao,
                    transacao);

            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("nome", item.Nome);
            cmd.Parameters.AddWithValue("unidade", item.Unidade);
            cmd.Parameters.AddWithValue("precoCompra", item.PrecoCompra);
            cmd.Parameters.AddWithValue("precoVenda", item.PrecoVenda);
            cmd.Parameters.AddWithValue("estoqueAtual", item.EstoqueAtual);
            cmd.Parameters.AddWithValue("dataAlteracao", item.DataAlteracao);
            cmd.Parameters.AddWithValue("excluido", item.Excluido);

            await cmd.ExecuteNonQueryAsync();

            return id;
        }

        const string insert = """
            INSERT INTO app.material
            (
                id_local,
                nome,
                unidade,
                preco_compra,
                preco_venda,
                estoque_atual,
                data_cadastro,
                data_alteracao,
                status_sincronizacao,
                mensagem_erro,
                empresa_id,
                dispositivo_id,
                excluido
            )
            VALUES
            (
                @idLocal,
                @nome,
                @unidade,
                @precoCompra,
                @precoVenda,
                @estoqueAtual,
                @dataCadastro,
                @dataAlteracao,
                'SINCRONIZADO',
                NULL,
                @empresaId,
                @dispositivoId,
                @excluido
            )
            RETURNING id;
            """;

        await using var cmdInsert =
            new NpgsqlCommand(
                insert,
                conexao,
                transacao);

        cmdInsert.Parameters.AddWithValue("idLocal", (object?)item.IdLocal ?? DBNull.Value);
        cmdInsert.Parameters.AddWithValue("nome", item.Nome);
        cmdInsert.Parameters.AddWithValue("unidade", item.Unidade);
        cmdInsert.Parameters.AddWithValue("precoCompra", item.PrecoCompra);
        cmdInsert.Parameters.AddWithValue("precoVenda", item.PrecoVenda);
        cmdInsert.Parameters.AddWithValue("estoqueAtual", item.EstoqueAtual);
        cmdInsert.Parameters.AddWithValue("dataCadastro", item.DataCadastro);
        cmdInsert.Parameters.AddWithValue("dataAlteracao", item.DataAlteracao);
        cmdInsert.Parameters.AddWithValue("empresaId", empresaId);
        cmdInsert.Parameters.AddWithValue("dispositivoId", dispositivoId);
        cmdInsert.Parameters.AddWithValue("excluido", item.Excluido);

        return Convert.ToInt64(
            await cmdInsert.ExecuteScalarAsync());
    }

    // =============================================================
    // COMPRA
    // =============================================================

    private static async Task<long> SincronizarCompra(
        NpgsqlConnection conexao,
        NpgsqlTransaction transacao,
        long empresaId,
        long dispositivoId,
        CompraSync item)
    {
        long pessoaIdServidor =
            await ResolverPessoaServidor(
                conexao,
                transacao,
                empresaId,
                dispositivoId,
                item.PessoaId,
                item.PessoaIdServidor);

        const string procura = """
            SELECT id
            FROM app.compra
            WHERE empresa_id = @empresaId
              AND dispositivo_id = @dispositivoId
              AND id_local = @idLocal
            LIMIT 1;
            """;

        await using var cmdProcura =
            new NpgsqlCommand(procura, conexao, transacao);

        cmdProcura.Parameters.AddWithValue("empresaId", empresaId);
        cmdProcura.Parameters.AddWithValue("dispositivoId", dispositivoId);
        cmdProcura.Parameters.AddWithValue("idLocal", (object?)item.IdLocal ?? DBNull.Value);

        object? encontrado =
            await cmdProcura.ExecuteScalarAsync();

        if (encontrado != null)
        {
            long id = Convert.ToInt64(encontrado);

            const string update = """
                UPDATE app.compra
                SET
                    pessoa_id = @pessoaId,
                    pessoa_nome = @pessoaNome,
                    data_compra = @dataCompra,
                    valor_total = @valorTotal,
                    observacao = @observacao,
                    status = @status,
                    status_sincronizacao = 'SINCRONIZADO',
                    mensagem_erro = NULL,
                    data_alteracao = @dataAlteracao,
                    excluido = @excluido
                WHERE id = @id;
                """;

            await using var cmd =
                new NpgsqlCommand(update, conexao, transacao);

            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("pessoaId", pessoaIdServidor);
            cmd.Parameters.AddWithValue("pessoaNome", item.PessoaNome);
            cmd.Parameters.AddWithValue("dataCompra", item.DataCompra);
            cmd.Parameters.AddWithValue("valorTotal", item.ValorTotal);
            cmd.Parameters.AddWithValue("observacao", (object?)item.Observacao ?? DBNull.Value);
            cmd.Parameters.AddWithValue("status", item.Status);
            cmd.Parameters.AddWithValue("dataAlteracao", item.DataAlteracao);
            cmd.Parameters.AddWithValue("excluido", item.Excluido);

            await cmd.ExecuteNonQueryAsync();

            return id;
        }

        const string insert = """
            INSERT INTO app.compra
            (
                id_local,
                pessoa_id,
                pessoa_nome,
                data_compra,
                valor_total,
                observacao,
                status,
                status_sincronizacao,
                mensagem_erro,
                empresa_id,
                dispositivo_id,
                data_alteracao,
                excluido
            )
            VALUES
            (
                @idLocal,
                @pessoaId,
                @pessoaNome,
                @dataCompra,
                @valorTotal,
                @observacao,
                @status,
                'SINCRONIZADO',
                NULL,
                @empresaId,
                @dispositivoId,
                @dataAlteracao,
                @excluido
            )
            RETURNING id;
            """;

        await using var cmdInsert =
            new NpgsqlCommand(insert, conexao, transacao);

        cmdInsert.Parameters.AddWithValue("idLocal", (object?)item.IdLocal ?? DBNull.Value);
        cmdInsert.Parameters.AddWithValue("pessoaId", pessoaIdServidor);
        cmdInsert.Parameters.AddWithValue("pessoaNome", item.PessoaNome);
        cmdInsert.Parameters.AddWithValue("dataCompra", item.DataCompra);
        cmdInsert.Parameters.AddWithValue("valorTotal", item.ValorTotal);
        cmdInsert.Parameters.AddWithValue("observacao", (object?)item.Observacao ?? DBNull.Value);
        cmdInsert.Parameters.AddWithValue("status", item.Status);
        cmdInsert.Parameters.AddWithValue("empresaId", empresaId);
        cmdInsert.Parameters.AddWithValue("dispositivoId", dispositivoId);
        cmdInsert.Parameters.AddWithValue("dataAlteracao", item.DataAlteracao);
        cmdInsert.Parameters.AddWithValue("excluido", item.Excluido);

        return Convert.ToInt64(
            await cmdInsert.ExecuteScalarAsync());
    }

    // =============================================================
    // ITEM COMPRA
    // =============================================================

    private static async Task<long> SincronizarItemCompra(
        NpgsqlConnection conexao,
        NpgsqlTransaction transacao,
        long empresaId,
        long dispositivoId,
        ItemCompraSync item)
    {
        // =========================================================
        // RESOLVER COMPRA
        // =========================================================

        long compraIdServidor =
            item.CompraIdServidor ??
            await ResolverCompraServidor(
                conexao,
                transacao,
                empresaId,
                dispositivoId,
                item.CompraId);

        // =========================================================
        // RESOLVER MATERIAL
        // =========================================================

        long materialIdServidor =
            item.MaterialIdServidor ??
            await ResolverMaterialServidor(
                conexao,
                transacao,
                empresaId,
                dispositivoId,
                item.MaterialId);

        // =========================================================
        // PROCURAR ITEM JÁ EXISTENTE
        // =========================================================

        const string procura = """
        SELECT id
        FROM app.item_compra
        WHERE empresa_id = @empresaId
          AND dispositivo_id = @dispositivoId
          AND id_local = @idLocal
        LIMIT 1;
        """;

        await using var cmdProcura =
            new NpgsqlCommand(
                procura,
                conexao,
                transacao);

        cmdProcura.Parameters.AddWithValue(
            "empresaId",
            empresaId);

        cmdProcura.Parameters.AddWithValue(
            "dispositivoId",
            dispositivoId);

        cmdProcura.Parameters.AddWithValue(
            "idLocal",
            (object?)item.IdLocal ??
            DBNull.Value);

        object? encontrado =
            await cmdProcura.ExecuteScalarAsync();

        // =========================================================
        // ATUALIZAR
        // =========================================================

        if (encontrado != null)
        {
            long id =
                Convert.ToInt64(encontrado);

            const string update = """
            UPDATE app.item_compra
            SET
                compra_id = @compraId,
                material_id = @materialId,
                material_nome = @materialNome,
                unidade = @unidade,
                quantidade = @quantidade,
                preco_unitario = @precoUnitario,
                valor_total = @valorTotal,
                quantidade_perda = @quantidadePerda,
                quantidade_liquida = @quantidadeLiquida,
                estoque_lancado = @estoqueLancado,
                status_sincronizacao = 'SINCRONIZADO',
                mensagem_erro = NULL,
                data_alteracao = @dataAlteracao,
                excluido = @excluido
            WHERE id = @id;
            """;

            await using var cmd =
                new NpgsqlCommand(
                    update,
                    conexao,
                    transacao);

            cmd.Parameters.AddWithValue(
                "id",
                id);

            cmd.Parameters.AddWithValue(
                "compraId",
                compraIdServidor);

            cmd.Parameters.AddWithValue(
                "materialId",
                materialIdServidor);

            cmd.Parameters.AddWithValue(
                "materialNome",
                item.MaterialNome);

            cmd.Parameters.AddWithValue(
                "unidade",
                item.Unidade);

            cmd.Parameters.AddWithValue(
                "quantidade",
                item.Quantidade);

            cmd.Parameters.AddWithValue(
                "precoUnitario",
                item.PrecoUnitario);

            cmd.Parameters.AddWithValue(
                "valorTotal",
                item.ValorTotal);

            cmd.Parameters.AddWithValue(
                "quantidadePerda",
                item.QuantidadePerda);

            cmd.Parameters.AddWithValue(
                "quantidadeLiquida",
                item.QuantidadeLiquida);

            cmd.Parameters.AddWithValue(
                "estoqueLancado",
                item.EstoqueLancado);

            cmd.Parameters.AddWithValue(
                "dataAlteracao",
                item.DataAlteracao);

            cmd.Parameters.AddWithValue(
                "excluido",
                item.Excluido);

            await cmd.ExecuteNonQueryAsync();

            return id;
        }

        // =========================================================
        // INSERIR
        // =========================================================

        const string insert = """
        INSERT INTO app.item_compra
        (
            id_local,
            compra_id,
            material_id,
            material_nome,
            unidade,
            quantidade,
            preco_unitario,
            valor_total,
            quantidade_perda,
            quantidade_liquida,
            estoque_lancado,
            empresa_id,
            dispositivo_id,
            status_sincronizacao,
            mensagem_erro,
            data_alteracao,
            excluido
        )
        VALUES
        (
            @idLocal,
            @compraId,
            @materialId,
            @materialNome,
            @unidade,
            @quantidade,
            @precoUnitario,
            @valorTotal,
            @quantidadePerda,
            @quantidadeLiquida,
            @estoqueLancado,
            @empresaId,
            @dispositivoId,
            'SINCRONIZADO',
            NULL,
            @dataAlteracao,
            @excluido
        )
        RETURNING id;
        """;

        await using var cmdInsert =
            new NpgsqlCommand(
                insert,
                conexao,
                transacao);

        cmdInsert.Parameters.AddWithValue(
            "idLocal",
            (object?)item.IdLocal ??
            DBNull.Value);

        cmdInsert.Parameters.AddWithValue(
            "compraId",
            compraIdServidor);

        cmdInsert.Parameters.AddWithValue(
            "materialId",
            materialIdServidor);

        cmdInsert.Parameters.AddWithValue(
            "materialNome",
            item.MaterialNome);

        cmdInsert.Parameters.AddWithValue(
            "unidade",
            item.Unidade);

        cmdInsert.Parameters.AddWithValue(
            "quantidade",
            item.Quantidade);

        cmdInsert.Parameters.AddWithValue(
            "precoUnitario",
            item.PrecoUnitario);

        cmdInsert.Parameters.AddWithValue(
            "valorTotal",
            item.ValorTotal);

        cmdInsert.Parameters.AddWithValue(
            "quantidadePerda",
            item.QuantidadePerda);

        cmdInsert.Parameters.AddWithValue(
            "quantidadeLiquida",
            item.QuantidadeLiquida);

        cmdInsert.Parameters.AddWithValue(
            "estoqueLancado",
            item.EstoqueLancado);

        cmdInsert.Parameters.AddWithValue(
            "empresaId",
            empresaId);

        cmdInsert.Parameters.AddWithValue(
            "dispositivoId",
            dispositivoId);

        cmdInsert.Parameters.AddWithValue(
            "dataAlteracao",
            item.DataAlteracao);

        cmdInsert.Parameters.AddWithValue(
            "excluido",
            item.Excluido);

        return Convert.ToInt64(
            await cmdInsert.ExecuteScalarAsync());
    }
    // =============================================================
    // VENDA
    // =============================================================

    private static async Task<long> SincronizarVenda(
     NpgsqlConnection conexao,
     NpgsqlTransaction transacao,
     long empresaId,
     long dispositivoId,
     VendaSync item)
    {
        long pessoaIdServidor =
            await ResolverPessoaServidor(
                conexao,
                transacao,
                empresaId,
                dispositivoId,
                item.PessoaId,
                item.PessoaIdServidor);

        const string procura = """
        SELECT id
        FROM app.venda
        WHERE empresa_id = @empresaId
          AND dispositivo_id = @dispositivoId
          AND id_local = @idLocal
        LIMIT 1;
        """;

        await using var cmdProcura =
            new NpgsqlCommand(
                procura,
                conexao,
                transacao);

        cmdProcura.Parameters.AddWithValue(
            "empresaId",
            empresaId);

        cmdProcura.Parameters.AddWithValue(
            "dispositivoId",
            dispositivoId);

        cmdProcura.Parameters.AddWithValue(
            "idLocal",
            (object?)item.IdLocal ??
            DBNull.Value);

        object? encontrado =
            await cmdProcura.ExecuteScalarAsync();

        if (encontrado != null)
        {
            long id =
                Convert.ToInt64(encontrado);

            const string update = """
            UPDATE app.venda
            SET
                pessoa_id = @pessoaId,
                pessoa_nome = @pessoaNome,
                data_venda = @dataVenda,
                valor_total = @valorTotal,
                observacao = @observacao,
                status = @status,
                status_sincronizacao = 'SINCRONIZADO',
                mensagem_erro = NULL,
                data_alteracao = @dataAlteracao,
                excluido = @excluido
            WHERE id = @id;
            """;

            await using var cmd =
                new NpgsqlCommand(
                    update,
                    conexao,
                    transacao);

            cmd.Parameters.AddWithValue(
                "id",
                id);

            cmd.Parameters.AddWithValue(
                "pessoaId",
                pessoaIdServidor);

            cmd.Parameters.AddWithValue(
                "pessoaNome",
                item.PessoaNome);

            cmd.Parameters.AddWithValue(
                "dataVenda",
                item.DataVenda);

            cmd.Parameters.AddWithValue(
                "valorTotal",
                item.ValorTotal);

            cmd.Parameters.AddWithValue(
                "observacao",
                (object?)item.Observacao ??
                DBNull.Value);

            cmd.Parameters.AddWithValue(
                "status",
                item.Status);

            cmd.Parameters.AddWithValue(
                "dataAlteracao",
                item.DataAlteracao);

            cmd.Parameters.AddWithValue(
                "excluido",
                item.Excluido);

            await cmd.ExecuteNonQueryAsync();

            return id;
        }

        const string insert = """
        INSERT INTO app.venda
        (
            id_local,
            pessoa_id,
            pessoa_nome,
            data_venda,
            valor_total,
            observacao,
            status,
            status_sincronizacao,
            mensagem_erro,
            empresa_id,
            dispositivo_id,
            data_alteracao,
            excluido
        )
        VALUES
        (
            @idLocal,
            @pessoaId,
            @pessoaNome,
            @dataVenda,
            @valorTotal,
            @observacao,
            @status,
            'SINCRONIZADO',
            NULL,
            @empresaId,
            @dispositivoId,
            @dataAlteracao,
            @excluido
        )
        RETURNING id;
        """;

        await using var cmdInsert =
            new NpgsqlCommand(
                insert,
                conexao,
                transacao);

        cmdInsert.Parameters.AddWithValue(
            "idLocal",
            (object?)item.IdLocal ??
            DBNull.Value);

        cmdInsert.Parameters.AddWithValue(
            "pessoaId",
            pessoaIdServidor);

        cmdInsert.Parameters.AddWithValue(
            "pessoaNome",
            item.PessoaNome);

        cmdInsert.Parameters.AddWithValue(
            "dataVenda",
            item.DataVenda);

        cmdInsert.Parameters.AddWithValue(
            "valorTotal",
            item.ValorTotal);

        cmdInsert.Parameters.AddWithValue(
            "observacao",
            (object?)item.Observacao ??
            DBNull.Value);

        cmdInsert.Parameters.AddWithValue(
            "status",
            item.Status);

        cmdInsert.Parameters.AddWithValue(
            "empresaId",
            empresaId);

        cmdInsert.Parameters.AddWithValue(
            "dispositivoId",
            dispositivoId);

        cmdInsert.Parameters.AddWithValue(
            "dataAlteracao",
            item.DataAlteracao);

        cmdInsert.Parameters.AddWithValue(
            "excluido",
            item.Excluido);

        return Convert.ToInt64(
            await cmdInsert.ExecuteScalarAsync());
    }

    // =============================================================
    // ITEM VENDA
    // =============================================================

    private static async Task<long> SincronizarItemVenda(
        NpgsqlConnection conexao,
        NpgsqlTransaction transacao,
        long empresaId,
        long dispositivoId,
        ItemVendaSync item)
    {
        long vendaIdServidor =
     item.VendaIdServidor ??
     await ResolverVendaServidor(
         conexao,
         transacao,
         empresaId,
         item.VendaId);

        long materialIdServidor =
            item.MaterialIdServidor ??
            await ResolverMaterialServidor(
                conexao,
                transacao,
                empresaId,
                dispositivoId,
                item.MaterialId);

        const string procura = """
            SELECT id
            FROM app.item_venda
            WHERE empresa_id = @empresaId
              AND dispositivo_id = @dispositivoId
              AND id_local = @idLocal
            LIMIT 1;
            """;

        await using var cmdProcura =
            new NpgsqlCommand(procura, conexao, transacao);

        cmdProcura.Parameters.AddWithValue("empresaId", empresaId);
        cmdProcura.Parameters.AddWithValue("dispositivoId", dispositivoId);
        cmdProcura.Parameters.AddWithValue("idLocal", (object?)item.IdLocal ?? DBNull.Value);

        object? encontrado =
            await cmdProcura.ExecuteScalarAsync();

        if (encontrado != null)
        {
            long id = Convert.ToInt64(encontrado);

            const string update = """
                UPDATE app.item_venda
                SET
                    venda_id = @vendaId,
                    material_id = @materialId,
                    material_nome = @materialNome,
                    unidade = @unidade,
                    quantidade = @quantidade,
                    preco_unitario = @precoUnitario,
                    valor_total = @valorTotal,
                    status_sincronizacao = 'SINCRONIZADO',
                    mensagem_erro = NULL,
                    data_alteracao = @dataAlteracao,
                    excluido = @excluido
                WHERE id = @id;
                """;

            await using var cmd =
                new NpgsqlCommand(update, conexao, transacao);

            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("vendaId", vendaIdServidor);
            cmd.Parameters.AddWithValue("materialId", materialIdServidor);
            cmd.Parameters.AddWithValue("materialNome", item.MaterialNome);
            cmd.Parameters.AddWithValue("unidade", item.Unidade);
            cmd.Parameters.AddWithValue("quantidade", item.Quantidade);
            cmd.Parameters.AddWithValue("precoUnitario", item.PrecoUnitario);
            cmd.Parameters.AddWithValue("valorTotal", item.ValorTotal);
            cmd.Parameters.AddWithValue("dataAlteracao", item.DataAlteracao);
            cmd.Parameters.AddWithValue("excluido", item.Excluido);

            await cmd.ExecuteNonQueryAsync();

            return id;
        }

        const string insert = """
            INSERT INTO app.item_venda
            (
                id_local,
                venda_id,
                material_id,
                material_nome,
                unidade,
                quantidade,
                preco_unitario,
                valor_total,
                empresa_id,
                dispositivo_id,
                status_sincronizacao,
                mensagem_erro,
                data_alteracao,
                excluido
            )
            VALUES
            (
                @idLocal,
                @vendaId,
                @materialId,
                @materialNome,
                @unidade,
                @quantidade,
                @precoUnitario,
                @valorTotal,
                @empresaId,
                @dispositivoId,
                'SINCRONIZADO',
                NULL,
                @dataAlteracao,
                @excluido
            )
            RETURNING id;
            """;

        await using var cmdInsert =
            new NpgsqlCommand(insert, conexao, transacao);

        cmdInsert.Parameters.AddWithValue("idLocal", (object?)item.IdLocal ?? DBNull.Value);
        cmdInsert.Parameters.AddWithValue("vendaId", vendaIdServidor);
        cmdInsert.Parameters.AddWithValue("materialId", materialIdServidor);
        cmdInsert.Parameters.AddWithValue("materialNome", item.MaterialNome);
        cmdInsert.Parameters.AddWithValue("unidade", item.Unidade);
        cmdInsert.Parameters.AddWithValue("quantidade", item.Quantidade);
        cmdInsert.Parameters.AddWithValue("precoUnitario", item.PrecoUnitario);
        cmdInsert.Parameters.AddWithValue("valorTotal", item.ValorTotal);
        cmdInsert.Parameters.AddWithValue("empresaId", empresaId);
        cmdInsert.Parameters.AddWithValue("dispositivoId", dispositivoId);
        cmdInsert.Parameters.AddWithValue("dataAlteracao", item.DataAlteracao);
        cmdInsert.Parameters.AddWithValue("excluido", item.Excluido);

        return Convert.ToInt64(
            await cmdInsert.ExecuteScalarAsync());
    }

    // =============================================================
    // USUARIO
    // =============================================================

    private static async Task<long> SincronizarUsuario(
        NpgsqlConnection conexao,
        NpgsqlTransaction transacao,
        long empresaId,
        UsuarioSync item)
    {
        long? idEncontrado = null;
        string? hashEncontrado = null;

        if (item.IdServidor is > 0)
        {
            const string porIdServidor = "SELECT id FROM app.usuario WHERE id=@id AND empresa_id=@empresaId LIMIT 1;";
            await using var cmdPorId = new NpgsqlCommand(porIdServidor, conexao, transacao);
            cmdPorId.Parameters.AddWithValue("id", item.IdServidor.Value);
            cmdPorId.Parameters.AddWithValue("empresaId", empresaId);
            var encontrado = await cmdPorId.ExecuteScalarAsync();
            if (encontrado is null)
                throw new InvalidOperationException("O usuário vinculado no servidor não pertence à empresa desta sessão.");
            idEncontrado = Convert.ToInt64(encontrado);
        }
        else
        {
            // IDs SQLite são locais ao aparelho. Nunca use id_local como identidade
            // global: dois celulares normalmente terão o mesmo valor.
            const string porNome = "SELECT id, senha_hash FROM app.usuario WHERE empresa_id=@empresaId AND lower(trim(nome))=lower(trim(@nome)) ORDER BY id LIMIT 1;";
            await using var cmdPorNome = new NpgsqlCommand(porNome, conexao, transacao);
            cmdPorNome.Parameters.AddWithValue("empresaId", empresaId);
            cmdPorNome.Parameters.AddWithValue("nome", item.Nome);
            await using var leitor = await cmdPorNome.ExecuteReaderAsync();
            if (await leitor.ReadAsync())
            {
                idEncontrado = leitor.GetInt64(0);
                hashEncontrado = leitor.GetString(1);
            }
        }

        if (idEncontrado is long idExistente)
        {
            if (hashEncontrado is not null && !string.Equals(hashEncontrado, item.SenhaHash, StringComparison.Ordinal))
                throw new InvalidOperationException("Já existe um usuário com esse nome nesta empresa e a senha não confere. O perfil não foi alterado.");

            const string update = """
                UPDATE app.usuario
                SET
                    nome = @nome,
                    senha_hash = @senhaHash,
                    funcao = @funcao,
                    eh_master = @ehMaster,
                    data_alteracao = @dataAlteracao,
                    status_sincronizacao = 'SINCRONIZADO',
                    mensagem_erro = NULL,
                    excluido = @excluido
                WHERE id = @id AND empresa_id = @empresaId;
                """;

            await using var cmd =
                new NpgsqlCommand(update, conexao, transacao);

            cmd.Parameters.AddWithValue("id", idExistente);
            cmd.Parameters.AddWithValue("empresaId", empresaId);
            cmd.Parameters.AddWithValue("nome", item.Nome);
            cmd.Parameters.AddWithValue("senhaHash", item.SenhaHash);
            cmd.Parameters.AddWithValue("funcao", item.Funcao);
            cmd.Parameters.AddWithValue("ehMaster", item.EhMaster);
            cmd.Parameters.AddWithValue("dataAlteracao", item.DataAlteracao);
            cmd.Parameters.AddWithValue("excluido", item.Excluido);

            await cmd.ExecuteNonQueryAsync();

            return idExistente;
        }

        const string insert = """
            INSERT INTO app.usuario
            (
                id_local,
                nome,
                senha_hash,
                funcao,
                eh_master,
                aprovado,
                empresa_id,
                status_sincronizacao,
                mensagem_erro,
                data_cadastro,
                data_alteracao,
                excluido
            )
            VALUES
            (
                @idLocal,
                @nome,
                @senhaHash,
                @funcao,
                @ehMaster,
                FALSE,
                @empresaId,
                'SINCRONIZADO',
                NULL,
                @dataCadastro,
                @dataAlteracao,
                @excluido
            )
            RETURNING id;
            """;

        await using var cmdInsert =
            new NpgsqlCommand(insert, conexao, transacao);

        cmdInsert.Parameters.AddWithValue("idLocal", (object?)item.IdLocal ?? DBNull.Value);
        cmdInsert.Parameters.AddWithValue("nome", item.Nome);
        cmdInsert.Parameters.AddWithValue("senhaHash", item.SenhaHash);
        cmdInsert.Parameters.AddWithValue("funcao", item.Funcao);
        cmdInsert.Parameters.AddWithValue("ehMaster", item.EhMaster);
        cmdInsert.Parameters.AddWithValue("empresaId", empresaId);
        cmdInsert.Parameters.AddWithValue("dataCadastro", item.DataCadastro);
        cmdInsert.Parameters.AddWithValue("dataAlteracao", item.DataAlteracao);
        cmdInsert.Parameters.AddWithValue("excluido", item.Excluido);

        return Convert.ToInt64(
            await cmdInsert.ExecuteScalarAsync());
    }

    // =============================================================
    // CONFIGURAÇÃO
    // =============================================================

    private static async Task SincronizarConfiguracao(
        NpgsqlConnection conexao,
        NpgsqlTransaction transacao,
        long empresaId,
        ConfiguracaoSync item)
    {
        const string sql = """
            INSERT INTO app.configuracao
            (
                empresa_id,
                chave,
                valor,
                status_sincronizacao,
                mensagem_erro,
                data_alteracao,
                excluido
            )
            VALUES
            (
                @empresaId,
                @chave,
                @valor,
                'SINCRONIZADO',
                NULL,
                @dataAlteracao,
                @excluido
            )
            ON CONFLICT (empresa_id, chave)
            DO UPDATE SET
                valor = EXCLUDED.valor,
                status_sincronizacao = 'SINCRONIZADO',
                mensagem_erro = NULL,
                data_alteracao = EXCLUDED.data_alteracao,
                excluido = EXCLUDED.excluido;
            """;

        await using var cmd =
            new NpgsqlCommand(
                sql,
                conexao,
                transacao);

        cmd.Parameters.AddWithValue("empresaId", empresaId);
        cmd.Parameters.AddWithValue("chave", item.Chave);
        cmd.Parameters.AddWithValue("valor", item.Valor);
        cmd.Parameters.AddWithValue("dataAlteracao", item.DataAlteracao);
        cmd.Parameters.AddWithValue("excluido", item.Excluido);

        await cmd.ExecuteNonQueryAsync();
    }

    // =============================================================
    // RESOLVER PESSOA
    // =============================================================

    private static async Task<long> ResolverPessoaServidor(
        NpgsqlConnection conexao,
        NpgsqlTransaction transacao,
        long empresaId,
        long dispositivoId,
        int idLocal,
        long? idServidor)
    {
        if (idServidor.HasValue &&
            idServidor.Value > 0)
        {
            return idServidor.Value;
        }

        const string sql = """
            SELECT id
            FROM app.pessoa
            WHERE empresa_id = @empresaId
              AND dispositivo_id = @dispositivoId
              AND id_local = @idLocal
            LIMIT 1;
            """;

        await using var cmd =
            new NpgsqlCommand(sql, conexao, transacao);

        cmd.Parameters.AddWithValue("empresaId", empresaId);
        cmd.Parameters.AddWithValue("dispositivoId", dispositivoId);
        cmd.Parameters.AddWithValue("idLocal", idLocal);

        object? resultado =
            await cmd.ExecuteScalarAsync();

        if (resultado == null)
        {
            throw new Exception(
                $"Pessoa local {idLocal} não encontrada no servidor.");
        }

        return Convert.ToInt64(resultado);
    }

    // =============================================================
    // RESOLVER MATERIAL
    // =============================================================

    private static async Task<long> ResolverMaterialServidor(
        NpgsqlConnection conexao,
        NpgsqlTransaction transacao,
        long empresaId,
        long dispositivoId,
        int idLocal)
    {
        const string sql = """
            SELECT id
            FROM app.material
            WHERE empresa_id = @empresaId
              AND dispositivo_id = @dispositivoId
              AND id_local = @idLocal
            LIMIT 1;
            """;

        await using var cmd =
            new NpgsqlCommand(sql, conexao, transacao);

        cmd.Parameters.AddWithValue("empresaId", empresaId);
        cmd.Parameters.AddWithValue("dispositivoId", dispositivoId);
        cmd.Parameters.AddWithValue("idLocal", idLocal);

        object? resultado =
            await cmd.ExecuteScalarAsync();

        if (resultado == null)
        {
            throw new Exception(
                $"Material local {idLocal} não encontrado no servidor.");
        }

        return Convert.ToInt64(resultado);
    }

    // =============================================================
    // RESOLVER COMPRA
    // =============================================================

    private static async Task<long> ResolverCompraServidor(
        NpgsqlConnection conexao,
        NpgsqlTransaction transacao,
        long empresaId,
        long dispositivoId,
        int idLocal)
    {
        const string sql = """
            SELECT id
            FROM app.compra
            WHERE empresa_id = @empresaId
              AND dispositivo_id = @dispositivoId
              AND id_local = @idLocal
            LIMIT 1;
            """;

        await using var cmd =
            new NpgsqlCommand(sql, conexao, transacao);

        cmd.Parameters.AddWithValue("empresaId", empresaId);
        cmd.Parameters.AddWithValue("dispositivoId", dispositivoId);
        cmd.Parameters.AddWithValue("idLocal", idLocal);

        object? resultado =
            await cmd.ExecuteScalarAsync();

        if (resultado == null)
        {
            throw new Exception(
                $"Compra local {idLocal} não encontrada no servidor.");
        }

        return Convert.ToInt64(resultado);
    }

    // =============================================================
    // RESOLVER VENDA
    // =============================================================

    private static async Task<long> ResolverVendaServidor(
        NpgsqlConnection conexao,
        NpgsqlTransaction transacao,
        long empresaId,
        int idLocal)
    {
        const string sql = """
            SELECT id
            FROM app.venda
            WHERE empresa_id = @empresaId
              AND id_local = @idLocal
            LIMIT 1;
            """;

        await using var cmd =
            new NpgsqlCommand(sql, conexao, transacao);

        cmd.Parameters.AddWithValue("empresaId", empresaId);
        cmd.Parameters.AddWithValue("idLocal", idLocal);

        object? resultado =
            await cmd.ExecuteScalarAsync();

        if (resultado == null)
        {
            throw new Exception(
                $"Venda local {idLocal} não encontrada no servidor.");
        }

        return Convert.ToInt64(resultado);
    }
}
