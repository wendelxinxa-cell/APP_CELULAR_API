-- Execute uma vez em cada banco de dados de empresa antes de publicar esta versao da API.
ALTER TABLE app.usuario
    ADD COLUMN IF NOT EXISTS aprovado BOOLEAN NOT NULL DEFAULT TRUE;

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
