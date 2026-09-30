-- Execute uma vez em cada banco de empresa ativo para habilitar bloqueio Zeus.
ALTER TABLE app.usuario
    ADD COLUMN IF NOT EXISTS bloqueado_por_master BOOLEAN NOT NULL DEFAULT FALSE;

CREATE INDEX IF NOT EXISTS ix_usuario_empresa_bloqueado_master
    ON app.usuario (empresa_id, bloqueado_por_master)
    WHERE bloqueado_por_master = TRUE;
