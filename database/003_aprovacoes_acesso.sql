-- Execute uma vez em cada banco de dados de empresa antes de publicar esta versao da API.
ALTER TABLE app.usuario
    ADD COLUMN IF NOT EXISTS aprovado BOOLEAN NOT NULL DEFAULT TRUE;

-- Campos usados pela sincronização e pelo painel Zeus. Eles ficam aqui também
-- para que bancos antigos preparados apenas com a migração 003 não quebrem as
-- consultas de usuários; IF NOT EXISTS torna a atualização segura para repetir.
ALTER TABLE app.usuario
    ADD COLUMN IF NOT EXISTS id_local INTEGER NULL;

ALTER TABLE app.usuario
    ADD COLUMN IF NOT EXISTS status_sincronizacao TEXT NOT NULL DEFAULT 'SINCRONIZADO';

ALTER TABLE app.usuario
    ADD COLUMN IF NOT EXISTS data_alteracao TIMESTAMPTZ NOT NULL DEFAULT NOW();

ALTER TABLE app.usuario
    ADD COLUMN IF NOT EXISTS bloqueado_por_master BOOLEAN NOT NULL DEFAULT FALSE;

ALTER TABLE app.dispositivo
    ADD COLUMN IF NOT EXISTS chave_instalacao UUID NULL;

ALTER TABLE app.dispositivo
    ADD COLUMN IF NOT EXISTS nome_dispositivo TEXT NOT NULL DEFAULT '';

ALTER TABLE app.dispositivo
    ADD COLUMN IF NOT EXISTS criado_em TIMESTAMPTZ NOT NULL DEFAULT NOW();

CREATE UNIQUE INDEX IF NOT EXISTS ux_dispositivo_empresa_chave_instalacao
    ON app.dispositivo (empresa_id, chave_instalacao)
    WHERE chave_instalacao IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_usuario_empresa_aprovado
    ON app.usuario (empresa_id, aprovado)
    WHERE COALESCE(excluido, FALSE) = FALSE;
