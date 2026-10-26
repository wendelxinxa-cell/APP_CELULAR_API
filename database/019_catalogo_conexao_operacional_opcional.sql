-- Execute no projeto Supabase do catálogo central.
-- As novas empresas compartilham o banco operacional da plataforma; o catálogo
-- guarda o ID e os metadados da empresa, sem exigir uma conexão por registro.
ALTER TABLE platform.empresa_catalogo
    ALTER COLUMN conexao_criptografada DROP NOT NULL;
