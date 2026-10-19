-- Padroniza o cadastro no banco APP-CELULAR sem apagar histórico.
-- Execute uma vez no banco de cada empresa antes de publicar a API nova.

BEGIN;

ALTER TABLE app.usuario
    ADD COLUMN IF NOT EXISTS username TEXT NULL,
    ADD COLUMN IF NOT EXISTS email TEXT NULL;

-- Mantém a conta master existente e libera o restante para novo cadastro.
-- Os perfis antigos ficam preservados para manter referências e histórico.
UPDATE app.usuario
SET username = 'zeus',
    email = 'wendelxinxa@gmail.com',
    nome = 'ADMINISTRADOR MASTER',
    funcao = 'ADMINISTRADOR MASTER',
    eh_master = TRUE,
    aprovado = TRUE,
    excluido = FALSE,
    bloqueado_por_master = FALSE,
    tipo_negocio = 'chat',
    data_alteracao = NOW()
WHERE id = 1 AND empresa_id = 1 AND eh_master = TRUE;

UPDATE app.usuario
SET username = NULL,
    email = NULL,
    aprovado = FALSE,
    excluido = TRUE,
    bloqueado_por_master = TRUE,
    data_alteracao = NOW()
WHERE COALESCE(eh_master, FALSE) = FALSE
  AND COALESCE(excluido, FALSE) = FALSE;

UPDATE app.usuario
SET tipo_negocio = CASE WHEN lower(trim(tipo_negocio)) = 'chat' THEN 'chat' ELSE 'comercial' END
WHERE COALESCE(eh_master, FALSE) = FALSE;

CREATE UNIQUE INDEX IF NOT EXISTS ux_usuario_empresa_username_ativo
    ON app.usuario (empresa_id, lower(trim(username)))
    WHERE username IS NOT NULL AND trim(username) <> '' AND COALESCE(excluido, FALSE) = FALSE;

CREATE UNIQUE INDEX IF NOT EXISTS ux_usuario_empresa_email_ativo
    ON app.usuario (empresa_id, lower(trim(email)))
    WHERE email IS NOT NULL AND trim(email) <> '' AND COALESCE(excluido, FALSE) = FALSE;

COMMIT;
