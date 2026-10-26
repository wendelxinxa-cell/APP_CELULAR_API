-- Aplique no banco tenant da empresa. Os novos logins também garantem a coluna.
ALTER TABLE app.usuario
    ADD COLUMN IF NOT EXISTS icones_permitidos TEXT NOT NULL
    DEFAULT '["chat","pessoas","produtos","vendas","compras","financeiro","outros","usuarios","configuracoes"]';
