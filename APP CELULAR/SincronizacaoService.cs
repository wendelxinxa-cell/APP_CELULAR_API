using APP_CELULAR.Data;
using APP_CELULAR.Models;

namespace APP_CELULAR.Services;

public class SincronizacaoService
{
    private static readonly SemaphoreSlim TravaSincronizacao = new(1, 1);
    private readonly AppDatabase _database;
    private readonly ApiService _api;

    private const long EmpresaPadrao = 1;
    private static long DispositivoPadrao => APP_CELULAR.SessaoUsuario.DispositivoId > 0 ? APP_CELULAR.SessaoUsuario.DispositivoId : 1;


    public SincronizacaoService()
    {
        _database = new AppDatabase();
        _api = new ApiService();
    }

    // ============================================================
    // SINCRONIZAÇÃO PRINCIPAL
    // ============================================================

    public async Task<SincronizacaoResultado> SincronizarTudo()
    {
        if (!await TravaSincronizacao.WaitAsync(0))
            return new SincronizacaoResultado { Sucesso = false, Mensagem = "Já existe uma sincronização em andamento." };

        try
        {
            long empresaIdAtual = APP_CELULAR.SessaoUsuario.UsuarioAtual?.EmpresaId ?? 0;
            if (empresaIdAtual <= 0)
                return new SincronizacaoResultado { Sucesso = false, Mensagem = "Não foi possível identificar a empresa do usuário conectado." };

            var pessoas =
                (await _database.ObterPessoas())
                .Where(x =>
                    x.EmpresaId == empresaIdAtual &&
                    (x.StatusSincronizacao == "PENDENTE" || x.StatusSincronizacao == "ERRO"))
                .ToList();

            var materiais =
                (await _database.ObterMateriais())
                .Where(x =>
                    x.EmpresaId == empresaIdAtual &&
                    (x.StatusSincronizacao == "PENDENTE" || x.StatusSincronizacao == "ERRO"))
                .ToList();

            var compras =
          (await _database.ObterCompras())
          .Where(x =>
              x.EmpresaId == empresaIdAtual &&
              (x.StatusSincronizacao == "PENDENTE" ||
              x.StatusSincronizacao == "ERRO" ||
              (
                  x.Status == "AGUARDANDO" &&
                  !x.IdServidor.HasValue
              )))
          .ToList();
            var todasItensCompra = new List<ItemCompra>();

            foreach (var compra in compras)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"COMPRA ID={compra.Id} PESSOA_ID={compra.PessoaId} PESSOA_NOME={compra.PessoaNome}");
            }

            foreach (var compra in await _database.ObterCompras())
            {
                var itens =
                    await _database.ObterItensCompra(compra.Id);

                todasItensCompra.AddRange(itens);
            }

            var itensCompra =
                todasItensCompra
                    .Where(x =>
                        x.EmpresaId == empresaIdAtual &&
                        (x.StatusSincronizacao == "PENDENTE" || x.StatusSincronizacao == "ERRO"))
                    .ToList();

            var vendas =
                (await _database.ObterVendas())
                .Where(x =>
                    x.EmpresaId == empresaIdAtual &&
                    (x.StatusSincronizacao == "PENDENTE" || x.StatusSincronizacao == "ERRO"))
                .ToList();

            foreach (var venda in vendas)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"VENDA ID={venda.Id} PESSOA_ID={venda.PessoaId} PESSOA_NOME={venda.PessoaNome}");
            }

            var todasItensVenda = new List<ItemVenda>();

            foreach (var venda in await _database.ObterVendas())
            {
                var itens =
                    await _database.ObterItensVenda(venda.Id);

                todasItensVenda.AddRange(itens);
            }

            var itensVenda =
                todasItensVenda
                    .Where(x =>
                        x.EmpresaId == empresaIdAtual &&
                        (x.StatusSincronizacao == "PENDENTE" || x.StatusSincronizacao == "ERRO"))
                    .ToList();

            var usuarios = APP_CELULAR.SessaoUsuario.EhAdministrador
                ? (await _database.ObterUsuarios())
                    .Where(x => x.EmpresaId == empresaIdAtual &&
                        (x.StatusSincronizacao == "PENDENTE" || x.StatusSincronizacao == "ERRO"))
                    .ToList()
                : new List<Usuario>();

            var configuracoes =
                (await _database.ObterConfiguracoes())
                .Where(x =>
                    x.EmpresaId == empresaIdAtual &&
                    (x.StatusSincronizacao == "PENDENTE" || x.StatusSincronizacao == "ERRO"))
                .ToList();

            long usuarioServidorAtual = APP_CELULAR.SessaoUsuario.UsuarioAtual?.IdServidor ?? 0;
            var mensagensPendentes = usuarioServidorAtual > 0
                ? await _database.ObterMensagensPendentes(empresaIdAtual, usuarioServidorAtual)
                : new List<MensagemPendente>();

            int totalPendentes =
                pessoas.Count +
                materiais.Count +
                compras.Count +
                itensCompra.Count +
                vendas.Count +
                itensVenda.Count +
                usuarios.Count +
                configuracoes.Count +
                mensagensPendentes.Count;

            if (totalPendentes == 0)
            {
                return new SincronizacaoResultado
                {
                    Sucesso = true,
                    Mensagem = "Não existem registros pendentes."
                };
            }

            // ========================================================
            // MONTAR REQUEST
            // ========================================================

            var request = new SincronizacaoRequest
            {
                EmpresaId = empresaIdAtual,
                DispositivoId = DispositivoPadrao
            };

            foreach (var pessoa in pessoas)
            {
                request.Pessoas.Add(new PessoaSync
                {
                    IdLocal = pessoa.Id,
                    IdServidor = pessoa.IdServidor,
                    EmpresaId = GarantirEmpresa(pessoa.EmpresaId),
                    DispositivoId = GarantirDispositivo(pessoa.DispositivoId),
                    Nome = pessoa.Nome,
                    Documento = pessoa.Documento,
                    Telefone = pessoa.Telefone,
                    Email = pessoa.Email,
                    Endereco = pessoa.Endereco,
                    Cidade = pessoa.Cidade,
                    Cliente = pessoa.Cliente,
                    Fornecedor = pessoa.Fornecedor,
                    DataCadastro = pessoa.DataCadastro,
                    DataAlteracao = pessoa.DataAlteracao,
                    StatusSincronizacao = "SINCRONIZANDO",
                    MensagemErro = "",
                    Excluido = pessoa.Excluido
                });

                await _database.MarcarPessoaPendente(pessoa.Id);
            }

            foreach (var material in materiais)
            {
                request.Materiais.Add(new MaterialSync
                {
                    IdLocal = material.Id,
                    IdServidor = material.IdServidor,
                    EmpresaId = GarantirEmpresa(material.EmpresaId),
                    DispositivoId = GarantirDispositivo(material.DispositivoId),
                    Nome = material.Nome,
                    Unidade = material.Unidade,
                    PrecoCompra = material.PrecoCompra,
                    PrecoVenda = material.PrecoVenda,
                    EstoqueAtual = material.EstoqueAtual,
                    DataCadastro = material.DataCadastro,
                    DataAlteracao = material.DataAlteracao,
                    StatusSincronizacao = "SINCRONIZANDO",
                    MensagemErro = "",
                    Excluido = material.Excluido
                });

                await _database.MarcarMaterialPendente(material.Id);
            }

            foreach (var compra in compras)
            {
                request.Compras.Add(new CompraSync
                {
                    IdLocal = compra.Id,
                    IdServidor = compra.IdServidor,
                    EmpresaId = GarantirEmpresa(compra.EmpresaId),
                    DispositivoId = GarantirDispositivo(compra.DispositivoId),
                    PessoaId = compra.PessoaId,
                    PessoaNome = compra.PessoaNome,
                    DataCompra = compra.DataCompra,
                    ValorTotal = compra.ValorTotal,
                    Observacao = compra.Observacao,
                    Status = compra.Status,
                    StatusSincronizacao = "SINCRONIZANDO",
                    MensagemErro = "",
                    DataAlteracao = compra.DataAlteracao,
                    Excluido = compra.Excluido
                });

                await _database.MarcarCompraPendente(compra.Id);
            }

            foreach (var item in itensCompra)
            {
                request.ItensCompra.Add(new ItemCompraSync
                {
                    IdLocal = item.Id,
                    IdServidor = item.IdServidor,
                    CompraId = item.CompraId,
                    MaterialId = item.MaterialId,
                    MaterialNome = item.MaterialNome,
                    Unidade = item.Unidade,
                    Quantidade = item.Quantidade,
                    PrecoUnitario = item.PrecoUnitario,
                    ValorTotal = item.ValorTotal,
                    QuantidadePerda = item.QuantidadePerda,
                    QuantidadeLiquida = item.QuantidadeLiquida,
                    EstoqueLancado = item.EstoqueLancado,
                    StatusSincronizacao = "SINCRONIZANDO",
                    MensagemErro = "",
                    DataAlteracao = item.DataAlteracao,
                    EmpresaId = GarantirEmpresa(item.EmpresaId),
                    DispositivoId = GarantirDispositivo(item.DispositivoId),
                    Excluido = item.Excluido
                });

                await _database.MarcarItemCompraPendente(item.Id);
            }

            foreach (var venda in vendas)
            {
                request.Vendas.Add(new VendaSync
                {
                    IdLocal = venda.Id,
                    IdServidor = venda.IdServidor,
                    EmpresaId = GarantirEmpresa(venda.EmpresaId),
                    DispositivoId = GarantirDispositivo(venda.DispositivoId),
                    PessoaId = venda.PessoaId,
                    PessoaNome = venda.PessoaNome,
                    DataVenda = venda.DataVenda,
                    ValorTotal = venda.ValorTotal,
                    Observacao = venda.Observacao,
                    Status = venda.Status,
                    StatusSincronizacao = "SINCRONIZANDO",
                    MensagemErro = "",
                    DataAlteracao = venda.DataAlteracao,
                    Excluido = venda.Excluido
                });

                await _database.MarcarVendaPendente(venda.Id);
            }

            foreach (var item in itensVenda)
            {
                request.ItensVenda.Add(new ItemVendaSync
                {
                    IdLocal = item.Id,
                    IdServidor = item.IdServidor,
                    VendaId = item.VendaId,
                    MaterialId = item.MaterialId,
                    MaterialNome = item.MaterialNome,
                    Unidade = item.Unidade,
                    Quantidade = item.Quantidade,
                    PrecoUnitario = item.PrecoUnitario,
                    ValorTotal = item.ValorTotal,
                    StatusSincronizacao = "SINCRONIZANDO",
                    MensagemErro = "",
                    DataAlteracao = item.DataAlteracao,
                    EmpresaId = GarantirEmpresa(item.EmpresaId),
                    DispositivoId = GarantirDispositivo(item.DispositivoId),
                    Excluido = item.Excluido
                });

                await _database.MarcarItemVendaPendente(item.Id);
            }

            foreach (var usuario in usuarios)
            {
                request.Usuarios.Add(new UsuarioSync
                {
                    IdLocal = usuario.Id,
                    IdServidor = usuario.IdServidor,
                    Nome = usuario.Nome,
                    SenhaHash = usuario.SenhaHash,
                    Funcao = usuario.Funcao,
                    EhMaster = usuario.EhMaster,
                    EmpresaId = GarantirEmpresa(usuario.EmpresaId),
                    StatusSincronizacao = "SINCRONIZANDO",
                    MensagemErro = "",
                    DataCadastro = usuario.DataCadastro,
                    DataAlteracao = usuario.DataAlteracao,
                    Excluido = usuario.Excluido
                });

                await _database.MarcarUsuarioPendente(usuario.Id);
            }
            foreach (var configuracao in configuracoes)
            {
                request.Configuracoes.Add(new ConfiguracaoSync
                {
                    IdLocal = configuracao.Id,
                    Chave = "SEGMENTO",
                    Valor = configuracao.Segmento,
                    EmpresaId = GarantirEmpresa(configuracao.EmpresaId),
                    StatusSincronizacao = "SINCRONIZANDO",
                    MensagemErro = "",
                    DataAlteracao = configuracao.DataAlteracao,
                    Excluido = configuracao.Excluido
                });

                request.Configuracoes.Add(new ConfiguracaoSync
                {
                    IdLocal = configuracao.Id,
                    Chave = "CONFIGURADO",
                    Valor = configuracao.Configurado ? "1" : "0",
                    EmpresaId = GarantirEmpresa(configuracao.EmpresaId),
                    StatusSincronizacao = "SINCRONIZANDO",
                    MensagemErro = "",
                    DataAlteracao = configuracao.DataAlteracao,
                    Excluido = configuracao.Excluido
                });

                await _database.MarcarConfiguracaoPendente();
            }

            // ========================================================
            // ENVIAR PARA API
            // ========================================================

            var temDadosGeraisPendentes =
                pessoas.Count + materiais.Count + compras.Count + itensCompra.Count +
                vendas.Count + itensVenda.Count + usuarios.Count + configuracoes.Count > 0;
            var resposta = temDadosGeraisPendentes
                ? await _api.Sincronizar(request)
                : new SincronizacaoResponse { Sucesso = true, Mensagem = "Sem dados gerais pendentes." };

            // ========================================================
            // API NÃO RESPONDEU
            // ========================================================

            if (!resposta.Sucesso &&
                resposta.Registros.Count == 0 &&
                resposta.Erros.Count == 0)
            {
                return new SincronizacaoResultado
                {
                    Sucesso = false,
                    Mensagem = resposta.Mensagem
                };
            }
            // ========================================================
            // PROCESSAR REGISTROS SINCRONIZADOS
            // ========================================================

            foreach (var registro in resposta.Registros)
            {
                if (!registro.IdLocal.HasValue)
                    continue;

                int idLocal =
                    registro.IdLocal.Value;

                long idServidor =
                    registro.IdServidor;

                string tabela =
                    registro.Tabela?.Trim().ToLowerInvariant() ?? "";

                switch (tabela)
                {
                    case "pessoa":

                        await _database.MarcarPessoaSincronizada(
                            idLocal,
                            idServidor);

                        break;


                    case "material":

                        await _database.MarcarMaterialSincronizado(
                            idLocal,
                            idServidor);

                        break;


                    case "compra":

                        await _database.MarcarCompraSincronizada(
                            idLocal,
                            idServidor);

                        break;


                    case "item_compra":

                        await _database.MarcarItemCompraSincronizado(
                            idLocal,
                            idServidor);

                        break;


                    case "venda":

                        await _database.MarcarVendaSincronizada(
                            idLocal,
                            idServidor);

                        break;


                    case "item_venda":

                        await _database.MarcarItemVendaSincronizado(
                            idLocal,
                            idServidor);

                        break;


                    case "usuario":

                        await _database.MarcarUsuarioSincronizado(
                            idLocal,
                            idServidor);

                        break;


                    case "configuracao":

                        await _database.MarcarConfiguracaoSincronizada(
                            idLocal);

                        break;
                }
            }

            // ========================================================
            // PROCESSAR ERROS
            // ========================================================

            foreach (var erro in resposta.Erros)
            {
                if (!erro.IdLocal.HasValue)
                    continue;

                int idLocal =
                    erro.IdLocal.Value;

                string tabela =
                    erro.Tabela?.Trim().ToLowerInvariant() ?? "";

                switch (tabela)
                {
                    case "pessoa":

                        await _database.MarcarPessoaErro(
                            idLocal,
                            erro.Mensagem);

                        break;


                    case "material":

                        await _database.MarcarMaterialErro(
                            idLocal,
                            erro.Mensagem);

                        break;


                    case "compra":

                        await _database.MarcarCompraErro(
                            idLocal,
                            erro.Mensagem);

                        break;


                    case "item_compra":

                        await _database.MarcarItemCompraErro(
                            idLocal,
                            erro.Mensagem);

                        break;


                    case "venda":

                        await _database.MarcarVendaErro(
                            idLocal,
                            erro.Mensagem);

                        break;


                    case "item_venda":

                        await _database.MarcarItemVendaErro(
                            idLocal,
                            erro.Mensagem);

                        break;


                    case "usuario":

                        await _database.MarcarUsuarioErro(
                            idLocal,
                            erro.Mensagem);

                        break;


                    case "configuracao":

                        await _database.MarcarConfiguracaoErro(
                            idLocal,
                            erro.Mensagem);

                        break;
                }
            }

            int sincronizados =
                resposta.Registros.Count;

            int erros =
                resposta.Erros.Count;
            string detalhesErros = "";

            int mensagensSincronizadas = 0;
            var errosMensagens = new List<string>();
            foreach (var pendente in mensagensPendentes)
            {
                try
                {
                    await _api.EnviarMensagem(new EnviarMensagemConversa
                    {
                        EmpresaId = pendente.EmpresaId,
                        UsuarioId = pendente.UsuarioId,
                        ContatoId = pendente.ContatoId,
                        ClienteMensagemId = Guid.Parse(pendente.Chave),
                        Texto = pendente.Texto,
                        Foto = pendente.Foto,
                        FotoNome = pendente.FotoNome,
                        FotoTipo = pendente.FotoTipo
                    });
                    await _database.RemoverMensagemPendente(pendente.Chave);
                    mensagensSincronizadas++;
                }
                catch (Exception ex)
                {
                    pendente.MensagemErro = ex.Message;
                    await _database.SalvarMensagemPendente(pendente);
                    errosMensagens.Add($"Chat para usuário {pendente.ContatoId}: {ex.Message}");
                }
            }
            sincronizados += mensagensSincronizadas;
            erros += errosMensagens.Count;

            if (resposta.Erros.Count > 0)
            {
                detalhesErros =
                    "\n\nERROS:\n\n" +
                    string.Join(
                        "\n",
                        resposta.Erros.Select(x =>
                            $"{x.Tabela} | ID local: {x.IdLocal} | {x.Mensagem}"));
            }
            if (errosMensagens.Count > 0)
                detalhesErros += "\n\nCHAT PENDENTE:\n" + string.Join("\n", errosMensagens);

            return new SincronizacaoResultado
            {
                Sucesso = erros == 0,
                Mensagem =
                    $"Sincronização concluída.\n\n" +
                    $"{sincronizados} registro(s) sincronizado(s).\n" +
                    $"{erros} registro(s) com erro." +
                    detalhesErros,
                RegistrosSincronizados = sincronizados,
                RegistrosComErro = erros
            };
        }
        catch (Exception ex)
        {
            return new SincronizacaoResultado
            {
                Sucesso = false,
                Mensagem =
                    $"Erro na sincronização: {ex.Message}"
            };
        }
        finally
        {
            TravaSincronizacao.Release();
        }
    }

    public async Task<SincronizacaoResultado> SincronizarUsuario(Usuario usuario)
    {
        if (usuario.Id <= 0 || usuario.EmpresaId <= 0)
        {
            return new SincronizacaoResultado { Sucesso = false, Mensagem = "Os dados do usuário ou da empresa são inválidos." };
        }

        var request = new SincronizacaoRequest
        {
            EmpresaId = usuario.EmpresaId,
            DispositivoId = DispositivoPadrao
        };
        request.Usuarios.Add(new UsuarioSync
        {
            IdLocal = usuario.Id,
            IdServidor = usuario.IdServidor,
            Nome = usuario.Nome,
            SenhaHash = usuario.SenhaHash,
            Funcao = usuario.Funcao,
            TipoNegocio = usuario.TipoNegocio,
            EhMaster = usuario.EhMaster,
            EmpresaId = usuario.EmpresaId,
            StatusSincronizacao = "SINCRONIZANDO",
            DataCadastro = usuario.DataCadastro,
            DataAlteracao = DateTime.Now,
            Excluido = usuario.Excluido
        });

        var resposta = await _api.Sincronizar(request);
        var erro = resposta.Erros.FirstOrDefault(x =>
            string.Equals(x.Tabela, "usuario", StringComparison.OrdinalIgnoreCase) &&
            x.IdLocal == usuario.Id);
        if (erro is not null)
        {
            await _database.MarcarUsuarioErro(usuario.Id, erro.Mensagem);
            return new SincronizacaoResultado { Sucesso = false, Mensagem = erro.Mensagem };
        }

        var registro = resposta.Registros.FirstOrDefault(x =>
            string.Equals(x.Tabela, "usuario", StringComparison.OrdinalIgnoreCase) &&
            x.IdLocal == usuario.Id);
        if (registro is null || registro.IdServidor <= 0)
        {
            return new SincronizacaoResultado
            {
                Sucesso = false,
                Mensagem = resposta.Mensagem ?? "A API não retornou o identificador do usuário."
            };
        }

        await _database.MarcarUsuarioSincronizado(usuario.Id, registro.IdServidor);
        usuario.IdServidor = registro.IdServidor;
        usuario.StatusSincronizacao = "SINCRONIZADO";
        if (APP_CELULAR.SessaoUsuario.UsuarioAtual?.Id == usuario.Id)
            APP_CELULAR.SessaoUsuario.UsuarioAtual = usuario;

        return new SincronizacaoResultado
        {
            Sucesso = true,
            Mensagem = "Usuário sincronizado com a API."
        };
    }

    // ============================================================
    // UTILITÁRIOS
    // ============================================================

    private static long GarantirEmpresa(long valor)
    {
        return valor > 0
            ? valor
            : EmpresaPadrao;
    }

    private static long GarantirDispositivo(long valor)
    {
        return DispositivoPadrao > 0 ? DispositivoPadrao : (valor > 0 ? valor : 1);
    }
}


// ================================================================
// RESULTADO
// ================================================================

public class SincronizacaoResultado
{
    public bool Sucesso { get; set; }

    public string Mensagem { get; set; } = "";

    public int RegistrosSincronizados { get; set; }

    public int RegistrosComErro { get; set; }
}
