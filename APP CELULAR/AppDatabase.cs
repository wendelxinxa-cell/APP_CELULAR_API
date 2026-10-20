using SQLite;
using APP_CELULAR.Models;
using APP_CELULAR.Services;

namespace APP_CELULAR.Data;

public class AppDatabase
{
    private SQLiteAsyncConnection? _database;
    private static long EmpresaAtual
    {
        get
        {
            var usuario = APP_CELULAR.SessaoUsuario.UsuarioAtual;
            if (usuario is null)
                return ApiEndpointSettings.GetEmpresaId();

            if (usuario.EmpresaId > 0)
                return usuario.EmpresaId;

            return ApiEndpointSettings.GetEmpresaId();
        }
    }

    private async Task Init()
    {
        if (_database != null)
            return;

        string caminhoBanco = Path.Combine(
            FileSystem.AppDataDirectory,
            "APP_CELULAR.db3");

        _database = new SQLiteAsyncConnection(caminhoBanco);

        await _database.CreateTableAsync<Configuracao>();
        await _database.CreateTableAsync<Pessoa>();
        await _database.CreateTableAsync<Material>();

        await _database.CreateTableAsync<Compra>();
        await _database.CreateTableAsync<ItemCompra>();

        await _database.CreateTableAsync<Venda>();
        await _database.CreateTableAsync<ItemVenda>();

        await _database.CreateTableAsync<Usuario>();
        await AdicionarColunaSeNaoExistir("Usuario", "TipoNegocio", "TEXT NOT NULL DEFAULT 'sucata'");

        // Remove a conta Zeus fictícia criada por versões antigas. O acesso da
        // plataforma agora depende de um token Master emitido pela API.
        var usuariosZeusLegados = await _database.Table<Usuario>()
            .Where(x => x.Nome == "Zeus" && x.Funcao == "Deus")
            .ToListAsync();
        foreach (var usuarioZeus in usuariosZeusLegados)
            await _database.DeleteAsync(usuarioZeus);

        await _database.CreateTableAsync<MensagemPendente>();
        await _database.CreateTableAsync<MensagemCacheada>();
        await _database.CreateTableAsync<MensagemHistoricoProtegida>();
        await AdicionarColunaSeNaoExistir("MensagemHistoricoProtegida", "SaltDerivacao", "TEXT NOT NULL DEFAULT ''");
        await AdicionarColunaSeNaoExistir("MensagemHistoricoProtegida", "Ocultar", "INTEGER NOT NULL DEFAULT 1");
        await _database.CreateTableAsync<PreferenciasConversa>();
        await AdicionarColunaSeNaoExistir("PreferenciasConversa", "SaltHistoricoProtegido", "TEXT NOT NULL DEFAULT ''");
        await _database.CreateTableAsync<WinkPersonalizado>();

        await _database.CreateTableAsync<Usuario>();

        // ============================================================
        // MIGRAÇÃO DE SINCRONIZAÇÃO
        // ============================================================

        await AdicionarColunaSeNaoExistir(
            "Pessoa",
            "IdServidor",
            "INTEGER");

        await AdicionarColunaSeNaoExistir(
            "Pessoa",
            "EmpresaId",
            "INTEGER NOT NULL DEFAULT 1");

        await AdicionarColunaSeNaoExistir(
            "Pessoa",
            "DispositivoId",
            "INTEGER NOT NULL DEFAULT 1");

        await AdicionarColunaSeNaoExistir(
            "Pessoa",
            "Excluido",
            "INTEGER NOT NULL DEFAULT 0");

        await AdicionarColunaSeNaoExistir(
            "Material",
            "IdServidor",
            "INTEGER");

        await AdicionarColunaSeNaoExistir(
            "Material",
            "EmpresaId",
            "INTEGER NOT NULL DEFAULT 1");

        await AdicionarColunaSeNaoExistir(
            "Material",
            "DispositivoId",
            "INTEGER NOT NULL DEFAULT 1");

        await AdicionarColunaSeNaoExistir(
            "Material",
            "Excluido",
            "INTEGER NOT NULL DEFAULT 0");

        await AdicionarColunaSeNaoExistir(
            "Compra",
            "DispositivoId",
            "INTEGER NOT NULL DEFAULT 1");

        await AdicionarColunaSeNaoExistir(
            "Compra",
            "DataAlteracao",
            "DATETIME");

        await AdicionarColunaSeNaoExistir(
            "Compra",
            "Excluido",
            "INTEGER NOT NULL DEFAULT 0");

        await AdicionarColunaSeNaoExistir(
            "ItemCompra",
            "IdServidor",
            "INTEGER");

        await AdicionarColunaSeNaoExistir(
            "ItemCompra",
            "EmpresaId",
            "INTEGER NOT NULL DEFAULT 1");

        await AdicionarColunaSeNaoExistir(
            "ItemCompra",
            "DispositivoId",
            "INTEGER NOT NULL DEFAULT 1");

        await AdicionarColunaSeNaoExistir(
            "ItemCompra",
            "DataAlteracao",
            "DATETIME");

        await AdicionarColunaSeNaoExistir(
            "ItemCompra",
            "StatusSincronizacao",
            "TEXT NOT NULL DEFAULT 'PENDENTE'");

        await AdicionarColunaSeNaoExistir(
            "ItemCompra",
            "MensagemErro",
            "TEXT");

        await AdicionarColunaSeNaoExistir(
            "ItemCompra",
            "Excluido",
            "INTEGER NOT NULL DEFAULT 0");

        await AdicionarColunaSeNaoExistir(
            "Venda",
            "DispositivoId",
            "INTEGER NOT NULL DEFAULT 1");

        await AdicionarColunaSeNaoExistir(
            "Venda",
            "DataAlteracao",
            "DATETIME");

        await AdicionarColunaSeNaoExistir(
            "Venda",
            "Excluido",
            "INTEGER NOT NULL DEFAULT 0");

        await AdicionarColunaSeNaoExistir(
            "ItemVenda",
            "IdServidor",
            "INTEGER");

        await AdicionarColunaSeNaoExistir(
            "ItemVenda",
            "EmpresaId",
            "INTEGER NOT NULL DEFAULT 1");

        await AdicionarColunaSeNaoExistir(
            "ItemVenda",
            "DispositivoId",
            "INTEGER NOT NULL DEFAULT 1");

        await AdicionarColunaSeNaoExistir(
            "ItemVenda",
            "DataAlteracao",
            "DATETIME");

        await AdicionarColunaSeNaoExistir(
            "ItemVenda",
            "StatusSincronizacao",
            "TEXT NOT NULL DEFAULT 'PENDENTE'");

        await AdicionarColunaSeNaoExistir(
            "ItemVenda",
            "MensagemErro",
            "TEXT");

        await AdicionarColunaSeNaoExistir(
            "ItemVenda",
            "Excluido",
            "INTEGER NOT NULL DEFAULT 0");

        await AdicionarColunaSeNaoExistir(
            "Usuario",
            "IdServidor",
            "INTEGER");

        await AdicionarColunaSeNaoExistir(
            "Usuario",
            "EmpresaId",
            "INTEGER NOT NULL DEFAULT 1");

        await AdicionarColunaSeNaoExistir(
            "Usuario",
            "StatusSincronizacao",
            "TEXT NOT NULL DEFAULT 'PENDENTE'");

        await AdicionarColunaSeNaoExistir(
            "Usuario",
            "MensagemErro",
            "TEXT");

        await AdicionarColunaSeNaoExistir(
            "Usuario",
            "DataCadastro",
            "DATETIME");

        await AdicionarColunaSeNaoExistir(
            "Usuario",
            "DataAlteracao",
            "DATETIME");

        await AdicionarColunaSeNaoExistir(
            "Usuario",
            "Excluido",
            "INTEGER NOT NULL DEFAULT 0");

        await AdicionarColunaSeNaoExistir(
            "Configuracao",
            "EmpresaId",
            "INTEGER NOT NULL DEFAULT 1");

        await AdicionarColunaSeNaoExistir(
            "Configuracao",
            "DataAlteracao",
            "DATETIME");

        await AdicionarColunaSeNaoExistir(
            "Configuracao",
            "StatusSincronizacao",
            "TEXT NOT NULL DEFAULT 'PENDENTE'");

        await AdicionarColunaSeNaoExistir(
            "Configuracao",
            "MensagemErro",
            "TEXT");

        await AdicionarColunaSeNaoExistir(
            "Configuracao",
            "Excluido",
            "INTEGER NOT NULL DEFAULT 0");

        // ============================================================
        // MIGRAÇÃO DA TABELA ITEMCOMPRA
        // ============================================================

        await AdicionarColunaSeNaoExistir(
            "ItemCompra",
            "QuantidadePerda",
            "DECIMAL NOT NULL DEFAULT 0");

        await AdicionarColunaSeNaoExistir(
            "ItemCompra",
            "QuantidadeLiquida",
            "DECIMAL NOT NULL DEFAULT 0");

        await AdicionarColunaSeNaoExistir(
            "ItemCompra",
            "EstoqueLancado",
            "INTEGER NOT NULL DEFAULT 0");

        // ============================================================
        // MIGRAÇÃO DA TABELA USUARIO
        // ============================================================

        await AdicionarColunaSeNaoExistir(
            "Usuario",
            "EhMaster",
            "INTEGER NOT NULL DEFAULT 0");

        // ============================================================
        // GARANTIR EXISTÊNCIA DO ADMINISTRADOR MASTER
        // ============================================================

        // Remove contas locais criadas com a senha padrão vulnerável das versões
        // antigas. O usuário Master da plataforma autentica-se no servidor.
        var mastersComSenhaPadrao = await _database.Table<Usuario>()
            .Where(x => x.Nome == "ADMINISTRADOR MASTER" && x.EhMaster)
            .ToListAsync();
        const string hashMasterPadraoLegado = "0E0DE5D9FF8B94AA61E0BF1F34AC0CAEFF0716C104B07E2B8BA9589A37D2DB94";
        foreach (var masterLegado in mastersComSenhaPadrao.Where(x => x.SenhaHash == hashMasterPadraoLegado))
            await _database.DeleteAsync(masterLegado);


        // ============================================================
        // GARANTIR EXISTÊNCIA DA BIOMETRIA SE ATIVA
        // ============================================================

        await AdicionarColunaSeNaoExistir(
                "Usuario",
                "BiometriaAtivada",
                "INTEGER NOT NULL DEFAULT 0");

        await AdicionarColunaSeNaoExistir(
            "Material",
            "EmpresaId",
            "INTEGER NOT NULL DEFAULT 1");

        await AdicionarColunaSeNaoExistir(
            "Material",
            "DispositivoId",
            "INTEGER NOT NULL DEFAULT 1");

        await AdicionarColunaSeNaoExistir(
            "Material",
            "Excluido",
            "INTEGER NOT NULL DEFAULT 0");

        //********************* criar tabelas cheque

        await _database.CreateTableAsync<Cheque>();
        await _database.CreateTableAsync<BaixaCheque>();
        await _database.CreateTableAsync<ProrrogacaoCheque>();
        await _database.CreateTableAsync<HistoricoCheque>();
    }

    private async Task AdicionarColunaSeNaoExistir(
    string tabela,
    string coluna,
    string definicao)
    {
        try
        {
            var colunas = await _database!.GetTableInfoAsync(tabela);

            bool existe = colunas.Any(
                x => x.Name.Equals(
                    coluna,
                    StringComparison.OrdinalIgnoreCase));

            if (!existe)
            {
                await _database.ExecuteAsync(
                    $"ALTER TABLE {tabela} ADD COLUMN {coluna} {definicao}");
            }
        }
        catch (Exception ex)
        {
            throw new Exception(
                $"Erro ao atualizar o banco de dados. " +
                $"Tabela: {tabela}, coluna: {coluna}.",
                ex);
        }
    }

    public async Task<bool> ObterBiometriaAtivada(int usuarioId)
    {
        await Init();

        var usuario =
            await _database!
                .Table<Usuario>()
                .Where(x => x.Id == usuarioId)
                .FirstOrDefaultAsync();

        return usuario?.BiometriaAtivada == true;
    }


    public async Task DefinirBiometria(
        int usuarioId,
        bool ativada)
    {
        await Init();

        var usuario =
            await _database!
                .Table<Usuario>()
                .Where(x => x.Id == usuarioId)
                .FirstOrDefaultAsync();

        if (usuario == null)
            return;

        usuario.BiometriaAtivada = ativada;

        await _database.UpdateAsync(usuario);
    }

    // =========================
    // CONFIGURAÇÃO
    // =========================

    public async Task<Configuracao?> ObterConfiguracao()
    {
        await Init();

        return await _database!
            .Table<Configuracao>()
            .Where(x => x.EmpresaId == EmpresaAtual)
            .FirstOrDefaultAsync();
    }

    public async Task<int> SalvarConfiguracao(
        Configuracao configuracao)
    {
        await Init();
        configuracao.EmpresaId = EmpresaAtual;

        return await _database!.InsertAsync(configuracao);
    }

    // =========================
    // PESSOAS
    // =========================
    public async Task<List<Pessoa>> ObterPessoas()
    {
        await Init();

        return await _database!
            .Table<Pessoa>()
            .Where(x => x.EmpresaId == EmpresaAtual)
            .OrderBy(x => x.Nome)
            .ToListAsync();
    }

    public async Task<List<Pessoa>> ObterClientes()
    {
        await Init();

        return await _database!
            .Table<Pessoa>()
            .Where(x => x.EmpresaId == EmpresaAtual && x.Cliente)
            .OrderBy(x => x.Nome)
            .ToListAsync();
    }

    public async Task<List<Pessoa>> ObterFornecedores()
    {
        await Init();

        return await _database!
            .Table<Pessoa>()
            .Where(x => x.EmpresaId == EmpresaAtual && x.Fornecedor)
            .OrderBy(x => x.Nome)
            .ToListAsync();
    }

    public async Task<int> SalvarPessoa(Pessoa pessoa)
    {
        await Init();

        pessoa.DataCadastro = DateTime.Now;
        pessoa.DataAlteracao = DateTime.Now;
        pessoa.StatusSincronizacao = "PENDENTE";
        pessoa.EmpresaId = EmpresaAtual;

        return await _database!.InsertAsync(pessoa);
    }

    public async Task<int> AtualizarPessoa(Pessoa pessoa)
    {
        await Init();

        pessoa.DataAlteracao = DateTime.Now;
        pessoa.StatusSincronizacao = "PENDENTE";

        return await _database!.UpdateAsync(pessoa);
    }

    public async Task<int> ExcluirPessoa(Pessoa pessoa)
    {
        await Init();

        return await _database!.DeleteAsync(pessoa);
    }

    // =========================
    // MATERIAIS
    // =========================

    public async Task<List<Material>> ObterMateriais()
    {
        await Init();

        return await _database!
            .Table<Material>()
            .Where(x => x.EmpresaId == EmpresaAtual)
            .OrderBy(x => x.Nome)
            .ToListAsync();
    }

    public async Task<int> SalvarMaterial(Material material)
    {
        await Init();

        material.DataCadastro = DateTime.Now;
        material.DataAlteracao = DateTime.Now;
        material.StatusSincronizacao = "PENDENTE";
        material.MensagemErro = "";
        material.Excluido = false;

        material.EmpresaId = EmpresaAtual;

        if (material.DispositivoId <= 0)
            material.DispositivoId = 1;

        return await _database!.InsertAsync(material);
    }

    public async Task<int> AtualizarMaterial(Material material)
    {
        await Init();

        material.DataAlteracao = DateTime.Now;
        material.StatusSincronizacao = "PENDENTE";

        return await _database!.UpdateAsync(material);
    }

    public async Task<int> ExcluirMaterial(Material material)
    {
        await Init();

        material.Excluido = true;
        material.DataAlteracao = DateTime.Now;
        material.StatusSincronizacao = "PENDENTE";
        material.MensagemErro = "";

        return await _database!.UpdateAsync(material);
    }

    // ============================================================
    // USUÁRIOS
    // ============================================================

    public async Task<int> SalvarUsuario(Usuario usuario)
    {
        await Init();

        usuario.DataCadastro = DateTime.Now;
        usuario.DataAlteracao = DateTime.Now;
        usuario.StatusSincronizacao = "PENDENTE";
        usuario.MensagemErro = "";
        usuario.Excluido = false;

        usuario.EmpresaId = EmpresaAtual;

        return await _database!.InsertAsync(usuario);
    }

    public async Task<Usuario> ImportarUsuarioAutenticado(Usuario usuario)
    {
        await Init();
        var existente = await _database!.Table<Usuario>()
            .Where(x => x.EmpresaId == usuario.EmpresaId && x.Nome == usuario.Nome)
            .FirstOrDefaultAsync();
        if (existente is null)
        {
            usuario.StatusSincronizacao = "SINCRONIZADO";
            usuario.MensagemErro = "";
            usuario.Excluido = false;
            usuario.BiometriaAtivada = false;
            await _database.InsertAsync(usuario);
            return usuario;
        }

        existente.SenhaHash = usuario.SenhaHash;
        existente.Funcao = usuario.Funcao;
        existente.TipoNegocio = usuario.TipoNegocio;
        existente.EhMaster = usuario.EhMaster;
        existente.IdServidor = usuario.IdServidor;
        existente.EmpresaId = usuario.EmpresaId;
        existente.StatusSincronizacao = "SINCRONIZADO";
        existente.MensagemErro = "";
        existente.Excluido = false;
        existente.DataAlteracao = DateTime.Now;
        await _database.UpdateAsync(existente);
        return existente;
    }
    public async Task<List<Usuario>> ObterUsuarios()
    {
        await Init();

        return await _database!
            .Table<Usuario>()
            .Where(x => x.EmpresaId == EmpresaAtual)
            .OrderBy(x => x.Nome)
            .ToListAsync();
    }


    public async Task<Usuario?> ObterUsuario(int id, long? empresaId = null)
    {
        await Init();

        long empresa = empresaId.GetValueOrDefault(EmpresaAtual);
        return await _database!
            .Table<Usuario>()
            .Where(x => x.Id == id && x.EmpresaId == empresa)
            .FirstOrDefaultAsync();
    }


    public async Task<Usuario?> ObterUsuarioPorNome(string nome, long? empresaId = null)
    {
        await Init();

        long empresa = empresaId.GetValueOrDefault(EmpresaAtual);
        return await _database!
            .Table<Usuario>()
            .Where(x => x.Nome == nome && x.EmpresaId == empresa)
            .FirstOrDefaultAsync();
    }


    public async Task<bool> ExisteUsuarioComNome(
        string nome,
        int idIgnorar = 0)
    {
        await Init();

        var usuarios = await _database!
            .Table<Usuario>()
            .Where(x => x.EmpresaId == EmpresaAtual)
            .ToListAsync();

        return usuarios.Any(x =>
            x.Id != idIgnorar &&
            string.Equals(
                x.Nome?.Trim(),
                nome?.Trim(),
                StringComparison.OrdinalIgnoreCase));
    }

    public async Task<int> AtualizarUsuario(
        Usuario usuario)
    {
        await Init();

        usuario.DataAlteracao = DateTime.Now;
        usuario.StatusSincronizacao = "PENDENTE";
        usuario.MensagemErro = "";

        return await _database!.UpdateAsync(usuario);
    }

    public async Task<int> ExcluirUsuario(int id)
    {
        await Init();

        var usuario = await ObterUsuario(id);

        if (usuario == null)
            return 0;

        // ========================================================
        // PROTEÇÃO DO MASTER
        // ========================================================

        if (usuario.EhMaster)
            throw new Exception(
                "O ADMINISTRADOR MASTER não pode ser excluído.");

        // ========================================================
        // NÃO PERMITIR FICAR SEM ADMINISTRADOR
        // ========================================================

        string funcao =
            usuario.Funcao?.Trim().ToUpperInvariant() ?? "";

        bool administrador =
            funcao == "ADMINISTRADOR";

        if (administrador)
        {
            var usuarios = await ObterUsuarios();

            int quantidadeAdministradores =
                usuarios.Count(x =>
                {
                    string f =
                        x.Funcao?.Trim().ToUpperInvariant() ?? "";

                    return x.EhMaster ||
                           f == "ADMINISTRADOR";
                });

            if (quantidadeAdministradores <= 1)
            {
                throw new Exception(
                    "Não é possível excluir o último administrador do sistema.");
            }
        }

        return await _database!
            .DeleteAsync<Usuario>(id);
    }


    // ============================================================
    // VALIDAR LOGIN
    // ============================================================

    public async Task<Usuario?> ValidarLogin(
        string nome,
        string senha,
        long? empresaId = null)
    {
        await Init();

        string senhaHash =
            GerarHashSenha(senha);

        // Avalia o fallback antes de construir a expressão LINQ:
        // sqlite-net não traduz o operador ?? (Coalesce) para SQL.
        long empresaIdConsulta = empresaId ?? EmpresaAtual;

        var usuarios = await _database!
            .Table<Usuario>()
            .ToListAsync();
        var usuarioEmpresa = usuarios.FirstOrDefault(x =>
            x.Nome == nome &&
            x.EmpresaId == empresaIdConsulta &&
            x.SenhaHash == senhaHash);

        if (usuarioEmpresa is not null)
            return usuarioEmpresa;

        return null;
    }


    // ============================================================
    // GERAR HASH DA SENHA
    // ============================================================

    private string GerarHashSenha(string senha)
    {
        using var sha256 =
            System.Security.Cryptography.SHA256.Create();

        byte[] bytes =
            System.Text.Encoding.UTF8.GetBytes(senha);

        byte[] hash =
            sha256.ComputeHash(bytes);

        return Convert.ToHexString(hash);
    }


    public string CriarHashSenha(string senha)
    {
        return GerarHashSenha(senha);
    }
    // =========================
    // COMPRAS
    // =========================

    public async Task<int> SalvarCompra(Compra compra)
    {
        await Init();
        compra.EmpresaId = EmpresaAtual;

        await _database!.InsertAsync(compra);

        return compra.Id;
    }

    public async Task<int> SalvarItemCompra(ItemCompra item)
    {
        await Init();
        item.EmpresaId = EmpresaAtual;

        return await _database!.InsertAsync(item);
    }

    public async Task<List<Compra>> ObterCompras()
    {
        await Init();

        return await _database!
            .Table<Compra>()
            .Where(x => x.EmpresaId == EmpresaAtual)
            .OrderByDescending(x => x.DataCompra)
            .ToListAsync();
    }

    public async Task<List<ItemCompra>> ObterItensCompra(
        int compraId)
    {
        await Init();

        return await _database!
            .Table<ItemCompra>()
            .Where(x => x.CompraId == compraId && x.EmpresaId == EmpresaAtual)
            .ToListAsync();
    }

    public async Task<Compra?> ObterCompra(int compraId)
    {
        await Init();

        return await _database!
            .Table<Compra>()
            .Where(x => x.Id == compraId && x.EmpresaId == EmpresaAtual)
            .FirstOrDefaultAsync();
    }

    public async Task<int> AtualizarCompra(Compra compra)
    {
        await Init();

        compra.StatusSincronizacao = "PENDENTE";

        return await _database!.UpdateAsync(compra);
    }

    public async Task<int> AtualizarItemCompra(ItemCompra item)
    {
        await Init();

        item.DataAlteracao = DateTime.Now;
        item.StatusSincronizacao = "PENDENTE";
        item.MensagemErro = "";

        return await _database!.UpdateAsync(item);
    }

    public async Task<bool> ConcluirCompra(
    int compraId)
    {
        await Init();

        await _database!.RunInTransactionAsync(conn =>
        {
            var compra = conn.Find<Compra>(compraId);

            if (compra == null)
            {
                throw new Exception(
                    "Compra não encontrada.");
            }


            string status =
                compra.Status?.Trim().ToUpperInvariant() ?? "";


            // --------------------------------------------------------
            // NÃO PODE CONCLUIR UMA COMPRA JÁ CANCELADA
            // --------------------------------------------------------

            if (status == "CANCELADA" ||
                status == "CANCELADO")
            {
                throw new Exception(
                    "Esta compra está cancelada.");
            }


            // --------------------------------------------------------
            // SE JÁ FOI FINALIZADA, NÃO LANÇA ESTOQUE NOVAMENTE
            // --------------------------------------------------------

            if (status == "FINALIZADA" ||
                status == "FINALIZADO" ||
                status == "CONCLUIDA" ||
                status == "CONCLUIDO")
            {
                return;
            }


            // --------------------------------------------------------
            // BUSCAR ITENS
            // --------------------------------------------------------

            var itens = conn.Table<ItemCompra>()
                .Where(x => x.CompraId == compraId)
                .ToList();


            if (itens.Count == 0)
            {
                throw new Exception(
                    "A compra não possui produtos.");
            }


            // --------------------------------------------------------
            // PROCESSAR CADA ITEM
            // --------------------------------------------------------

            foreach (var item in itens)
            {
                // Se por algum motivo esse item já foi lançado,
                // não lança novamente.
                if (item.EstoqueLancado)
                    continue;


                // ----------------------------------------------------
                // VALIDAR QUANTIDADE
                // ----------------------------------------------------

                if (item.Quantidade < 0)
                {
                    throw new Exception(
                        $"Quantidade inválida para " +
                        $"'{item.MaterialNome}'.");
                }


                // ----------------------------------------------------
                // VALIDAR PERDA
                // ----------------------------------------------------

                if (item.QuantidadePerda < 0)
                {
                    throw new Exception(
                        $"A perda não pode ser negativa " +
                        $"para '{item.MaterialNome}'.");
                }


                if (item.QuantidadePerda > item.Quantidade)
                {
                    throw new Exception(
                        $"A perda de '{item.MaterialNome}' " +
                        $"não pode ser maior que a quantidade " +
                        $"comprada.");
                }


                // ----------------------------------------------------
                // CALCULAR QUANTIDADE LÍQUIDA
                // ----------------------------------------------------

                item.QuantidadeLiquida =
                    item.Quantidade -
                    item.QuantidadePerda;


                // ----------------------------------------------------
                // BUSCAR MATERIAL
                // ----------------------------------------------------

                var material =
                    conn.Find<Material>(item.MaterialId);


                if (material == null)
                {
                    throw new Exception(
                        $"O material '{item.MaterialNome}' " +
                        $"não foi encontrado no estoque.");
                }


                // ----------------------------------------------------
                // DAR ENTRADA SOMENTE NO LÍQUIDO
                // ----------------------------------------------------

                if (item.QuantidadeLiquida > 0)
                {
                    material.EstoqueAtual +=
                        item.QuantidadeLiquida;

                    material.DataAlteracao =
                        DateTime.Now;

                    material.StatusSincronizacao =
                        "PENDENTE";

                    conn.Update(material);
                }


                // ----------------------------------------------------
                // MARCAR ITEM COMO LANÇADO
                // ----------------------------------------------------

                item.EstoqueLancado = true;

                conn.Update(item);
            }


            // --------------------------------------------------------
            // FINALIZAR COMPRA
            // --------------------------------------------------------

            compra.Status = "FINALIZADA";

            compra.StatusSincronizacao =
                "PENDENTE";

            compra.MensagemErro = "";

            conn.Update(compra);
        });


        return true;
    }

    public async Task<bool> CancelarCompraComDevolucaoEstoque(
     int compraId)
    {
        await Init();

        await _database!.RunInTransactionAsync(conn =>
        {
            var compra = conn.Find<Compra>(compraId);

            if (compra == null)
            {
                throw new Exception(
                    "Compra não encontrada.");
            }

            string status =
                compra.Status?.Trim().ToUpperInvariant() ?? "";

            // --------------------------------------------------------
            // JÁ CANCELADA
            // --------------------------------------------------------

            if (status == "CANCELADA" ||
                status == "CANCELADO")
            {
                return;
            }

            // --------------------------------------------------------
            // VERIFICAR SE ESTÁ PENDENTE
            // --------------------------------------------------------

            bool pendente =
                status == "ABERTA" ||
                status == "PENDENTE" ||
                status == "PENDENTES";

            // --------------------------------------------------------
            // BUSCAR ITENS
            // --------------------------------------------------------

            var itens = conn.Table<ItemCompra>()
                .Where(x => x.CompraId == compraId)
                .ToList();

            // --------------------------------------------------------
            // SE JÁ ENTROU NO ESTOQUE,
            // RETIRAR SOMENTE O QUE FOI LANÇADO
            // --------------------------------------------------------

            if (!pendente)
            {
                foreach (var item in itens)
                {
                    // Só estorna se realmente foi lançado.
                    if (item.EstoqueLancado)
                    {
                        if (item.QuantidadeLiquida > 0)
                        {
                            var material =
                                conn.Find<Material>(item.MaterialId);

                            if (material == null)
                            {
                                throw new Exception(
                                    $"O material '{item.MaterialNome}' " +
                                    $"não foi encontrado no estoque.");
                            }

                            material.EstoqueAtual -=
                                item.QuantidadeLiquida;

                            material.DataAlteracao =
                                DateTime.Now;

                            material.StatusSincronizacao =
                                "PENDENTE";

                            conn.Update(material);
                        }
                    }

                    // ------------------------------------------------
                    // APAGAR O REBATE/PERDA DA COMPRA
                    // ------------------------------------------------

                    item.QuantidadePerda = 0;
                    item.QuantidadeLiquida = 0;
                    item.EstoqueLancado = false;

                    conn.Update(item);
                }
            }
            else
            {
                // ----------------------------------------------------
                // COMPRA AINDA PENDENTE
                // Não entrou no estoque.
                // Apenas limpa qualquer perda que eventualmente
                // tenha sido informada e cancela.
                // ----------------------------------------------------

                foreach (var item in itens)
                {
                    item.QuantidadePerda = 0;
                    item.QuantidadeLiquida = 0;
                    item.EstoqueLancado = false;

                    conn.Update(item);
                }
            }

            // --------------------------------------------------------
            // CANCELAR COMPRA
            // --------------------------------------------------------

            compra.Status = "CANCELADA";

            compra.StatusSincronizacao =
                "PENDENTE";

            compra.MensagemErro = "";

            conn.Update(compra);
        });

        return true;
    }

    // =========================
    // VENDAS
    // =========================

    public async Task<int> SalvarVenda(Venda venda)
    {
        await Init();
        venda.EmpresaId = EmpresaAtual;

        await _database!.InsertAsync(venda);

        return venda.Id;
    }

    public async Task<int> AtualizarVenda(Venda venda)
    {
        await Init();

        venda.StatusSincronizacao = "PENDENTE";

        return await _database!.UpdateAsync(venda);
    }

    public async Task<Venda?> ObterVenda(int vendaId)
    {
        await Init();

        return await _database!
            .Table<Venda>()
            .Where(x => x.Id == vendaId && x.EmpresaId == EmpresaAtual)
            .FirstOrDefaultAsync();
    }

    public async Task<List<Venda>> ObterVendas()
    {
        await Init();

        return await _database!
            .Table<Venda>()
            .Where(x => x.EmpresaId == EmpresaAtual)
            .OrderByDescending(x => x.DataVenda)
            .ToListAsync();
    }

    public async Task<int> SalvarItemVenda(ItemVenda item)
    {
        await Init();
        item.EmpresaId = EmpresaAtual;

        return await _database!.InsertAsync(item);
    }

    public async Task<List<ItemVenda>> ObterItensVenda(
        int vendaId)
    {
        await Init();

        return await _database!
            .Table<ItemVenda>()
            .Where(x => x.VendaId == vendaId && x.EmpresaId == EmpresaAtual)
            .ToListAsync();
    }
    public async Task<bool> CancelarVendaComDevolucaoEstoque(int vendaId)
    {
        await Init();

        await _database!.RunInTransactionAsync(conn =>
        {
            var venda = conn.Find<Venda>(vendaId);

            if (venda == null)
            {
                throw new Exception("Venda não encontrada.");
            }

            string status =
                venda.Status?.Trim().ToUpperInvariant() ?? "";

            // Se já estiver cancelada, não devolve o estoque novamente.
            if (status == "CANCELADA" ||
                status == "CANCELADO")
            {
                return;
            }

            // Somente vendas finalizadas podem ser canceladas.
            bool finalizada =
                status == "FINALIZADA" ||
                status == "FINALIZADO" ||
                status == "CONCLUIDA" ||
                status == "CONCLUIDO";

            if (!finalizada)
            {
                throw new Exception(
                    "Somente uma venda finalizada pode ser cancelada.");
            }

            // Busca todos os produtos vendidos.
            var itens = conn.Table<ItemVenda>()
                .Where(x => x.VendaId == vendaId)
                .ToList();

            // Devolve cada produto ao estoque.
            foreach (var item in itens)
            {
                var material = conn.Find<Material>(item.MaterialId);

                if (material == null)
                {
                    throw new Exception(
                        $"O material '{item.MaterialNome}' " +
                        $"não foi encontrado no estoque.");
                }

                // DEVOLVE A QUANTIDADE VENDIDA
                material.EstoqueAtual += item.Quantidade;

                // Marca para sincronização
                material.DataAlteracao = DateTime.Now;
                material.StatusSincronizacao = "PENDENTE";

                conn.Update(material);
            }

            // Marca a venda como cancelada.
            venda.Status = "CANCELADA";
            venda.StatusSincronizacao = "PENDENTE";
            venda.MensagemErro = "";

            conn.Update(venda);
        });

        // Se chegou até aqui, a transação terminou com sucesso.
        return true;
    }

    // CLASSES API COMUNICAÇAO CELULAR NUVEM SUPABASE

    public async Task MarcarMaterialSincronizado(
    int idLocal,
    long idServidor)
    {
        await Init();

        var material =
            await _database!
                .Table<Material>()
                .Where(x => x.Id == idLocal)
                .FirstOrDefaultAsync();

        if (material == null)
            return;

        material.IdServidor =
            (int?)idServidor;

        material.StatusSincronizacao =
            "SINCRONIZADO";

        material.MensagemErro =
            "";

        material.DataAlteracao =
            DateTime.Now;

        await _database.UpdateAsync(material);
    }

    // ============================================================
    // SINCRONIZAÇÃO
    // ============================================================


    public async Task<List<Configuracao>> ObterConfiguracoes()
    {
        await Init();

        return await _database!
            .Table<Configuracao>()
            .Where(x => x.EmpresaId == EmpresaAtual)
            .ToListAsync();
    }

    public async Task<int> SalvarMensagemPendente(MensagemPendente mensagem)
    {
        await Init();
        return await _database!.InsertOrReplaceAsync(mensagem);
    }

    public async Task<List<MensagemPendente>> ObterMensagensPendentes(long empresaId, long usuarioId)
    {
        await Init();
        return await _database!.Table<MensagemPendente>()
            .Where(x => x.EmpresaId == empresaId && x.UsuarioId == usuarioId)
            .OrderBy(x => x.CriadaEm)
            .ToListAsync();
    }

    public async Task<int> RemoverMensagemPendente(string chave)
    {
        await Init();
        return await _database!.DeleteAsync<MensagemPendente>(chave);
    }

    public async Task SubstituirMensagensCache(long empresaId, long usuarioId, long contatoId, IEnumerable<MensagemConversa> mensagens)
    {
        await Init();
        var protegidas = await _database!.Table<MensagemHistoricoProtegida>()
            .Where(x => x.EmpresaId == empresaId && x.UsuarioId == usuarioId && x.ContatoId == contatoId && x.Ocultar)
            .ToListAsync();
        var chavesProtegidas = protegidas.Select(x => x.Chave).ToHashSet(StringComparer.Ordinal);
        var antigas = await _database!.Table<MensagemCacheada>()
            .Where(x => x.EmpresaId == empresaId && x.UsuarioId == usuarioId && x.ContatoId == contatoId)
            .ToListAsync();
        foreach (var antiga in antigas)
            await _database.DeleteAsync(antiga);

        foreach (var mensagem in mensagens)
        {
            var chaveMensagem = HistoricoCriptografia.ChaveMensagem(empresaId, usuarioId, contatoId,
                mensagem.ClienteMensagemId, mensagem.Id);
            var protegida = chavesProtegidas.Contains(chaveMensagem);
            var chave = mensagem.ClienteMensagemId?.ToString("N") ?? $"servidor-{mensagem.Id}";
            await _database.InsertOrReplaceAsync(new MensagemCacheada
            {
                Chave = $"{empresaId}:{usuarioId}:{contatoId}:{chave}",
                EmpresaId = empresaId, UsuarioId = usuarioId, ContatoId = contatoId,
                IdServidor = mensagem.Id, ClienteMensagemId = mensagem.ClienteMensagemId,
                RemetenteId = mensagem.RemetenteId, RemetenteNome = mensagem.RemetenteNome,
                DestinatarioId = mensagem.DestinatarioId,
                Texto = protegida ? "" : mensagem.Texto,
                Foto = protegida ? null : mensagem.Foto,
                FotoNome = protegida ? null : mensagem.FotoNome,
                FotoTipo = protegida ? null : mensagem.FotoTipo,
                EnviadaEm = mensagem.EnviadaEm, LidaEm = mensagem.LidaEm
            });
        }
    }

    public async Task<List<MensagemHistoricoProtegida>> ObterMensagensHistoricoProtegidas(long empresaId, long usuarioId, long contatoId)
    {
        await Init();
        return await _database!.Table<MensagemHistoricoProtegida>()
            .Where(x => x.EmpresaId == empresaId && x.UsuarioId == usuarioId && x.ContatoId == contatoId)
            .OrderBy(x => x.EnviadaEm)
            .ToListAsync();
    }

    public async Task SalvarMensagemHistoricoProtegida(MensagemHistoricoProtegida mensagem)
    {
        await Init();
        await _database!.InsertOrReplaceAsync(mensagem);
    }

    public async Task DefinirMensagensHistoricoOcultas(long empresaId, long usuarioId, long contatoId, bool ocultar)
    {
        await Init();
        var protegidas = await _database!.Table<MensagemHistoricoProtegida>()
            .Where(x => x.EmpresaId == empresaId && x.UsuarioId == usuarioId && x.ContatoId == contatoId)
            .ToListAsync();
        foreach (var mensagem in protegidas)
        {
            mensagem.Ocultar = ocultar;
            await _database.UpdateAsync(mensagem);
        }
    }

    public async Task RestaurarMensagemCacheada(MensagemHistoricoProtegida protegida, ConteudoMensagemPrivado conteudo)
    {
        await Init();
        var chaveMensagem = protegida.ClienteMensagemId?.ToString("N") ?? $"servidor-{protegida.IdServidor}";
        await _database!.InsertOrReplaceAsync(new MensagemCacheada
        {
            Chave = $"{protegida.EmpresaId}:{protegida.UsuarioId}:{protegida.ContatoId}:{chaveMensagem}",
            EmpresaId = protegida.EmpresaId, UsuarioId = protegida.UsuarioId, ContatoId = protegida.ContatoId,
            IdServidor = protegida.IdServidor, ClienteMensagemId = protegida.ClienteMensagemId,
            RemetenteId = protegida.RemetenteId, RemetenteNome = protegida.RemetenteNome,
            DestinatarioId = protegida.DestinatarioId, Texto = conteudo.Texto,
            Foto = conteudo.Foto, FotoNome = conteudo.FotoNome, FotoTipo = conteudo.FotoTipo,
            EnviadaEm = protegida.EnviadaEm, LidaEm = protegida.LidaEm
        });
    }

    public async Task ExcluirMensagensHistoricoProtegidas(long empresaId, long usuarioId, long contatoId)
    {
        await Init();
        var protegidas = await _database!.Table<MensagemHistoricoProtegida>()
            .Where(x => x.EmpresaId == empresaId && x.UsuarioId == usuarioId && x.ContatoId == contatoId && x.Ocultar)
            .ToListAsync();
        foreach (var mensagem in protegidas)
            await _database.DeleteAsync(mensagem);
    }

    public async Task<List<MensagemConversa>> ObterMensagensCache(long empresaId, long usuarioId, long contatoId)
    {
        await Init();
        var cache = await _database!.Table<MensagemCacheada>()
            .Where(x => x.EmpresaId == empresaId && x.UsuarioId == usuarioId && x.ContatoId == contatoId)
            .OrderBy(x => x.EnviadaEm)
            .ToListAsync();
        var protegidas = await _database.Table<MensagemHistoricoProtegida>()
            .Where(x => x.EmpresaId == empresaId && x.UsuarioId == usuarioId && x.ContatoId == contatoId && x.Ocultar)
            .ToListAsync();
        var chavesProtegidas = protegidas.Select(x => x.Chave).ToHashSet(StringComparer.Ordinal);
        foreach (var mensagem in cache)
        {
            var chave = HistoricoCriptografia.ChaveMensagem(empresaId, usuarioId, contatoId,
                mensagem.ClienteMensagemId, mensagem.IdServidor);
            if (!chavesProtegidas.Contains(chave) ||
                (string.IsNullOrEmpty(mensagem.Texto) && mensagem.Foto is null && mensagem.FotoNome is null && mensagem.FotoTipo is null))
                continue;
            mensagem.Texto = "";
            mensagem.Foto = null;
            mensagem.FotoNome = null;
            mensagem.FotoTipo = null;
            await _database.UpdateAsync(mensagem);
        }
        return cache.Select(x => new MensagemConversa
        {
            Id = x.IdServidor, ClienteMensagemId = x.ClienteMensagemId,
            RemetenteId = x.RemetenteId, RemetenteNome = x.RemetenteNome,
            DestinatarioId = x.DestinatarioId, Texto = x.Texto, Foto = x.Foto,
            FotoNome = x.FotoNome, FotoTipo = x.FotoTipo, EnviadaEm = x.EnviadaEm, LidaEm = x.LidaEm
        }).ToList();
    }

    public async Task<PreferenciasConversa> ObterPreferenciasConversa(long empresaId, long usuarioId, long contatoId)
    {
        await Init();
        var chave = $"{empresaId}:{usuarioId}:{contatoId}";
        var preferencias = await _database!.FindAsync<PreferenciasConversa>(chave);
        preferencias ??= await _database.Table<PreferenciasConversa>()
            .Where(x => x.EmpresaId == empresaId && x.UsuarioId == usuarioId && x.ContatoId == contatoId)
            .FirstOrDefaultAsync();
        return preferencias ?? new PreferenciasConversa
        {
            Chave = chave, EmpresaId = empresaId, UsuarioId = usuarioId, ContatoId = contatoId
        };
    }

    public async Task SalvarPreferenciasConversa(PreferenciasConversa preferencias)
    {
        await Init();
        await _database!.InsertOrReplaceAsync(preferencias);
    }

    public async Task<List<WinkPersonalizado>> ObterWinksPersonalizados(long usuarioId)
    {
        await Init();
        return await _database!.Table<WinkPersonalizado>()
            .Where(x => x.UsuarioId == usuarioId).OrderBy(x => x.Nome).ToListAsync();
    }

    public async Task SalvarWinkPersonalizado(WinkPersonalizado wink)
    {
        await Init();
        await _database!.InsertOrReplaceAsync(wink);
    }


    // ============================================================
    // MARCAR REGISTRO COMO SINCRONIZADO
    // ============================================================

    public async Task<int> MarcarPessoaSincronizada(
        int idLocal,
        long idServidor)
    {
        await Init();

        return await _database!.ExecuteAsync(
            @"UPDATE Pessoa
          SET IdServidor = ?,
              StatusSincronizacao = 'SINCRONIZADO',
              MensagemErro = ''
          WHERE Id = ?",
            idServidor,
            idLocal);
    }


    public async Task<int> MarcarCompraSincronizada(
        int idLocal,
        long idServidor)
    {
        await Init();

        return await _database!.ExecuteAsync(
            @"UPDATE Compra
          SET IdServidor = ?,
              StatusSincronizacao = 'SINCRONIZADO',
              MensagemErro = ''
          WHERE Id = ?",
            idServidor,
            idLocal);
    }

    public async Task<int> MarcarItemCompraSincronizado(
        int idLocal,
        long idServidor)
    {
        await Init();

        return await _database!.ExecuteAsync(
            @"UPDATE ItemCompra
          SET IdServidor = ?,
              StatusSincronizacao = 'SINCRONIZADO',
              MensagemErro = ''
          WHERE Id = ?",
            idServidor,
            idLocal);
    }

    public async Task<int> MarcarVendaSincronizada(
        int idLocal,
        long idServidor)
    {
        await Init();

        return await _database!.ExecuteAsync(
            @"UPDATE Venda
          SET IdServidor = ?,
              StatusSincronizacao = 'SINCRONIZADO',
              MensagemErro = ''
          WHERE Id = ?",
            idServidor,
            idLocal);
    }

    public async Task<int> MarcarItemVendaSincronizado(
        int idLocal,
        long idServidor)
    {
        await Init();

        return await _database!.ExecuteAsync(
            @"UPDATE ItemVenda
          SET IdServidor = ?,
              StatusSincronizacao = 'SINCRONIZADO',
              MensagemErro = ''
          WHERE Id = ?",
            idServidor,
            idLocal);
    }

    public async Task<int> MarcarUsuarioSincronizado(
        int idLocal,
        long idServidor,
        string? tipoNegocio = null)
    {
        await Init();

        return await _database!.ExecuteAsync(
            @"UPDATE Usuario
          SET IdServidor = ?,
              TipoNegocio = COALESCE(?, TipoNegocio),
              StatusSincronizacao = 'SINCRONIZADO',
              MensagemErro = ''
          WHERE Id = ?",
            idServidor,
            tipoNegocio,
            idLocal);
    }

    public async Task<int> MarcarUsuarioSincronizado(
        int idLocal,
        long idServidor,
        long empresaId)
    {
        await Init();
        return await _database!.ExecuteAsync(
            @"UPDATE Usuario
              SET IdServidor = ?,
                  EmpresaId = ?,
                  StatusSincronizacao = 'SINCRONIZADO',
                  MensagemErro = ''
              WHERE Id = ?",
            idServidor,
            empresaId,
            idLocal);
    }

    public async Task<int> MarcarConfiguracaoSincronizada(
        string chave)
    {
        await Init();

        return await _database!.ExecuteAsync(
            @"UPDATE Configuracao
          SET StatusSincronizacao = 'SINCRONIZADO',
              MensagemErro = ''
          WHERE Chave = ?",
            chave);
    }


    // ============================================================
    // MARCAR ERRO
    // ============================================================

    public async Task<int> MarcarPessoaErro(
        int idLocal,
        string mensagem)
    {
        await Init();

        return await _database!.ExecuteAsync(
            @"UPDATE Pessoa
          SET StatusSincronizacao = 'ERRO',
              MensagemErro = ?
          WHERE Id = ?",
            mensagem,
            idLocal);
    }

    public async Task<int> MarcarMaterialErro(
        int idLocal,
        string mensagem)
    {
        await Init();

        return await _database!.ExecuteAsync(
            @"UPDATE Material
          SET StatusSincronizacao = 'ERRO',
              MensagemErro = ?
          WHERE Id = ?",
            mensagem,
            idLocal);
    }

    public async Task<int> MarcarCompraErro(
        int idLocal,
        string mensagem)
    {
        await Init();

        return await _database!.ExecuteAsync(
            @"UPDATE Compra
          SET StatusSincronizacao = 'ERRO',
              MensagemErro = ?
          WHERE Id = ?",
            mensagem,
            idLocal);
    }

    public async Task<int> MarcarItemCompraErro(
        int idLocal,
        string mensagem)
    {
        await Init();

        return await _database!.ExecuteAsync(
            @"UPDATE ItemCompra
          SET StatusSincronizacao = 'ERRO',
              MensagemErro = ?
          WHERE Id = ?",
            mensagem,
            idLocal);
    }

    public async Task<int> MarcarConfiguracaoPendente()
    {
        await Init();

        return await _database!.ExecuteAsync(
            @"UPDATE Configuracao
          SET StatusSincronizacao = 'PENDENTE',
              MensagemErro = ''
          WHERE Id > 0");
    }

    public async Task<int> MarcarVendaErro(
        int idLocal,
        string mensagem)
    {
        await Init();

        return await _database!.ExecuteAsync(
            @"UPDATE Venda
          SET StatusSincronizacao = 'ERRO',
              MensagemErro = ?
          WHERE Id = ?",
            mensagem,
            idLocal);
    }

    public async Task<int> MarcarItemVendaErro(
        int idLocal,
        string mensagem)
    {
        await Init();

        return await _database!.ExecuteAsync(
            @"UPDATE ItemVenda
          SET StatusSincronizacao = 'ERRO',
              MensagemErro = ?
          WHERE Id = ?",
            mensagem,
            idLocal);
    }

    public async Task<int> MarcarUsuarioErro(
        int idLocal,
        string mensagem)
    {
        await Init();

        return await _database!.ExecuteAsync(
            @"UPDATE Usuario
          SET StatusSincronizacao = 'ERRO',
              MensagemErro = ?
          WHERE Id = ?",
            mensagem,
            idLocal);
    }
    public async Task<int> MarcarConfiguracaoErro(
        int idLocal,
        string mensagem)
    {
        await Init();

        return await _database!.ExecuteAsync(
            @"UPDATE Configuracao
          SET StatusSincronizacao = 'ERRO',
              MensagemErro = ?
          WHERE Id = ?",
            mensagem,
            idLocal);
    }

    // ============================================================
    // VOLTAR PARA PENDENTE
    // ============================================================

    public async Task<int> MarcarPessoaPendente(int idLocal)
    {
        await Init();

        return await _database!.ExecuteAsync(
            @"UPDATE Pessoa
          SET StatusSincronizacao = 'PENDENTE'
          WHERE Id = ?",
            idLocal);
    }

    public async Task<int> MarcarMaterialPendente(int idLocal)
    {
        await Init();

        return await _database!.ExecuteAsync(
            @"UPDATE Material
          SET StatusSincronizacao = 'PENDENTE'
          WHERE Id = ?",
            idLocal);
    }

    public async Task<int> MarcarCompraPendente(int idLocal)
    {
        await Init();

        return await _database!.ExecuteAsync(
            @"UPDATE Compra
          SET StatusSincronizacao = 'PENDENTE'
          WHERE Id = ?",
            idLocal);
    }

    public async Task<int> MarcarItemCompraPendente(int idLocal)
    {
        await Init();

        return await _database!.ExecuteAsync(
            @"UPDATE ItemCompra
          SET StatusSincronizacao = 'PENDENTE'
          WHERE Id = ?",
            idLocal);
    }

    public async Task<int> MarcarVendaPendente(int idLocal)
    {
        await Init();

        return await _database!.ExecuteAsync(
            @"UPDATE Venda
          SET StatusSincronizacao = 'PENDENTE'
          WHERE Id = ?",
            idLocal);
    }

    public async Task<int> MarcarItemVendaPendente(int idLocal)
    {
        await Init();

        return await _database!.ExecuteAsync(
            @"UPDATE ItemVenda
          SET StatusSincronizacao = 'PENDENTE'
          WHERE Id = ?",
            idLocal);
    }

    public async Task<int> MarcarUsuarioPendente(int idLocal)
    {
        await Init();

        return await _database!.ExecuteAsync(
            @"UPDATE Usuario
          SET StatusSincronizacao = 'PENDENTE'
          WHERE Id = ?",
            idLocal);
    }

    public async Task<int> MarcarConfiguracaoSincronizada(
        int idLocal)
    {
        await Init();

        return await _database!.ExecuteAsync(
            @"UPDATE Configuracao
          SET StatusSincronizacao = 'SINCRONIZADO',
              MensagemErro = ''
          WHERE Id = ?",
            idLocal);
    }

    public async Task<int> SalvarCheque(
        Cheque cheque)
    {
        await Init();

        if (cheque.Id == 0)
        {
            return await _database!
                .InsertAsync(cheque);
        }

        return await _database!
            .UpdateAsync(cheque);
    }

    public async Task<int> ExcluirCheque(
    int chequeId)
    {
        await Init();

        return await _database!
            .DeleteAsync<Cheque>(chequeId);
    }

    public async Task<Cheque?> ObterCheque(
    int chequeId)
    {
        await Init();

        return await _database!
            .Table<Cheque>()
            .FirstOrDefaultAsync(x =>
                x.Id == chequeId);
    }

    public async Task<List<Cheque>>
    ObterTodosCheques()
    {
        await Init();

        return await _database!
            .Table<Cheque>()
            .OrderBy(x => x.ClienteNome)
            .ToListAsync();
    }

    public async Task<List<Cheque>>
    ObterChequesCliente(
        int pessoaId)
    {
        await Init();

        return await _database!
            .Table<Cheque>()
            .Where(x =>
                x.PessoaId == pessoaId)
            .OrderBy(x =>
                x.DataVencimento)
            .ToListAsync();
    }

    public async Task<List<Cheque>>
    ObterChequesEmAberto()
    {
        await Init();

        return await _database!
            .Table<Cheque>()
            .Where(x =>
                x.SaldoAtual > 0)
            .OrderBy(x =>
                x.ClienteNome)
            .ToListAsync();
    }

    public async Task<int>
    AtualizarSaldoCheque(
        int chequeId,
        decimal saldoAtual)
    {
        await Init();

        return await _database!
            .ExecuteAsync(
                @"UPDATE Cheque
              SET SaldoAtual = ?
              WHERE Id = ?",
                saldoAtual,
                chequeId);
    }

    public async Task<int>
    AtualizarStatusCheque(
        int chequeId,
        string status)
    {
        await Init();

        return await _database!
            .ExecuteAsync(
                @"UPDATE Cheque
              SET Status = ?
              WHERE Id = ?",
                status,
                chequeId);
    }

    public async Task<int>
    AtualizarVencimentoCheque(
        int chequeId,
        DateTime novoVencimento)
    {
        await Init();

        return await _database!
            .ExecuteAsync(
                @"UPDATE Cheque
              SET DataVencimento = ?
              WHERE Id = ?",
                novoVencimento,
                chequeId);
    }

    public async Task<int>
    SalvarBaixaCheque(
        BaixaCheque baixa)
    {
        await Init();

        return await _database!
            .InsertAsync(baixa);
    }

    public async Task<List<BaixaCheque>>
    ObterBaixasCheque(
        int chequeId)
    {
        await Init();

        return await _database!
            .Table<BaixaCheque>()
            .Where(x =>
                x.ChequeId == chequeId)
            .OrderByDescending(x =>
                x.DataBaixa)
            .ToListAsync();
    }

    public async Task<int>
    ExcluirBaixaCheque(
        int baixaId)
    {
        await Init();

        return await _database!
            .DeleteAsync<BaixaCheque>(
                baixaId);
    }

    public async Task<decimal>
    ObterTotalPagoCheque(
        int chequeId)
    {
        await Init();

        var baixas = await _database!
            .Table<BaixaCheque>()
            .Where(x =>
                x.ChequeId == chequeId)
            .ToListAsync();

        return baixas.Sum(x =>
            x.Valor);
    }

    public async Task<int>
    SalvarProrrogacao(
        ProrrogacaoCheque prorrogacao)
    {
        await Init();

        return await _database!
            .InsertAsync(prorrogacao);
    }

    public async Task<List<ProrrogacaoCheque>>
    ObterProrrogacoesCheque(
        int chequeId)
    {
        await Init();

        return await _database!
            .Table<ProrrogacaoCheque>()
            .Where(x =>
                x.ChequeId == chequeId)
            .OrderByDescending(x =>
                x.NovoVencimento)
            .ToListAsync();
    }

    public async Task<int>
    ExcluirProrrogacao(
        int prorrogacaoId)
    {
        await Init();

        return await _database!
            .DeleteAsync<ProrrogacaoCheque>(
                prorrogacaoId);
    }

    public async Task<int>
    SalvarHistoricoCheque(
        HistoricoCheque historico)
    {
        await Init();

        return await _database!
            .InsertAsync(historico);
    }

    public async Task<List<HistoricoCheque>>
    ObterHistoricoCheque(
        int chequeId)
    {
        await Init();

        return await _database!
            .Table<HistoricoCheque>()
            .Where(x =>
                x.ChequeId == chequeId)
            .OrderByDescending(x =>
                x.DataMovimento)
            .ToListAsync();
    }

    public async Task<List<Cheque>>
    ObterClientesComCheques()
    {
        await Init();

        return await _database!
            .Table<Cheque>()
            .Where(x =>
                x.SaldoAtual > 0)
            .ToListAsync();
    }

    public async Task RegistrarBaixaCheque(
    int chequeId,
    decimal valorBaixa,
    string observacao)
    {
        await Init();

        var cheque = await ObterCheque(
            chequeId);

        if (cheque == null)
            return;

        var baixa = new BaixaCheque
        {
            ChequeId = chequeId,
            DataBaixa = DateTime.Now,
            Valor = valorBaixa,
            Observacao = observacao
        };

        await SalvarBaixaCheque(
            baixa);

        cheque.SaldoAtual -= valorBaixa;

        if (cheque.SaldoAtual < 0)
            cheque.SaldoAtual = 0;

        if (cheque.SaldoAtual == 0)
        {
            cheque.Status = "LIQUIDADO";
        }
        else
        {
            cheque.Status = "PARCIAL";
        }

        await SalvarCheque(cheque);

        await SalvarHistoricoCheque(
            new HistoricoCheque
            {
                ChequeId = cheque.Id,
                DataMovimento = DateTime.Now,
                Tipo = "BAIXA",
                Descricao =
                    $"Baixa de R$ {valorBaixa:N2}"
            });
    }



}
