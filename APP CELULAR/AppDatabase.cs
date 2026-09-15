using SQLite;
using APP_CELULAR.Models;

namespace APP_CELULAR.Data;

public class AppDatabase
{
    private SQLiteAsyncConnection? _database;

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

    // =========================
    // CONFIGURAÇÃO
    // =========================

    public async Task<Configuracao?> ObterConfiguracao()
    {
        await Init();

        return await _database!
            .Table<Configuracao>()
            .FirstOrDefaultAsync();
    }

    public async Task<int> SalvarConfiguracao(
        Configuracao configuracao)
    {
        await Init();

        return await _database!.InsertAsync(configuracao);
    }

    // =========================
    // PESSOAS
    // =========================

    // =========================
    // PESSOAS
    // =========================

    public async Task<List<Pessoa>> ObterPessoas()
    {
        await Init();

        return await _database!
            .Table<Pessoa>()
            .OrderBy(x => x.Nome)
            .ToListAsync();
    }

    public async Task<List<Pessoa>> ObterClientes()
    {
        await Init();

        return await _database!
            .Table<Pessoa>()
            .Where(x => x.Cliente)
            .OrderBy(x => x.Nome)
            .ToListAsync();
    }

    public async Task<List<Pessoa>> ObterFornecedores()
    {
        await Init();

        return await _database!
            .Table<Pessoa>()
            .Where(x => x.Fornecedor)
            .OrderBy(x => x.Nome)
            .ToListAsync();
    }

    public async Task<int> SalvarPessoa(Pessoa pessoa)
    {
        await Init();

        pessoa.DataCadastro = DateTime.Now;
        pessoa.DataAlteracao = DateTime.Now;
        pessoa.StatusSincronizacao = "PENDENTE";

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
            .OrderBy(x => x.Nome)
            .ToListAsync();
    }

    public async Task<int> SalvarMaterial(Material material)
    {
        await Init();

        material.DataAlteracao = DateTime.Now;

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

        return await _database!.DeleteAsync(material);
    }

    // =========================
    // COMPRAS
    // =========================

    public async Task<int> SalvarCompra(Compra compra)
    {
        await Init();

        await _database!.InsertAsync(compra);

        return compra.Id;
    }

    public async Task<int> SalvarItemCompra(ItemCompra item)
    {
        await Init();

        return await _database!.InsertAsync(item);
    }

    public async Task<List<Compra>> ObterCompras()
    {
        await Init();

        return await _database!
            .Table<Compra>()
            .OrderByDescending(x => x.DataCompra)
            .ToListAsync();
    }

    public async Task<List<ItemCompra>> ObterItensCompra(
        int compraId)
    {
        await Init();

        return await _database!
            .Table<ItemCompra>()
            .Where(x => x.CompraId == compraId)
            .ToListAsync();
    }

    public async Task<Compra?> ObterCompra(int compraId)
    {
        await Init();

        return await _database!
            .Table<Compra>()
            .Where(x => x.Id == compraId)
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
            // COMPRA PENDENTE
            //
            // Não entrou estoque.
            // Portanto simplesmente cancela.
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
            // RETIRAR A QUANTIDADE LÍQUIDA
            // --------------------------------------------------------

            if (!pendente)
            {
                foreach (var item in itens)
                {
                    // Só retira se realmente foi lançado.
                    if (!item.EstoqueLancado)
                        continue;


                    if (item.QuantidadeLiquida <= 0)
                        continue;


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


                    // Impede novo estorno
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
            .Where(x => x.Id == vendaId)
            .FirstOrDefaultAsync();
    }

    public async Task<List<Venda>> ObterVendas()
    {
        await Init();

        return await _database!
            .Table<Venda>()
            .OrderByDescending(x => x.DataVenda)
            .ToListAsync();
    }

    public async Task<int> SalvarItemVenda(ItemVenda item)
    {
        await Init();

        return await _database!.InsertAsync(item);
    }

    public async Task<List<ItemVenda>> ObterItensVenda(
        int vendaId)
    {
        await Init();

        return await _database!
            .Table<ItemVenda>()
            .Where(x => x.VendaId == vendaId)
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

}