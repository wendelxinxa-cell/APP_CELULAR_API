-- Execute uma vez em cada banco de empresa antes de publicar a API atualizada.
-- Os apelidos são privados para o usuário que os definiu; mensagens e usuários
-- permanecem intactos para preservar o histórico e os nomes oficiais.

CREATE TABLE IF NOT EXISTS app.usuario_apelido
(
    empresa_id BIGINT NOT NULL,
    usuario_id BIGINT NOT NULL REFERENCES app.usuario(id),
    contato_id BIGINT NOT NULL REFERENCES app.usuario(id),
    apelido TEXT NOT NULL,
    criado_em TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    atualizado_em TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT pk_usuario_apelido PRIMARY KEY (empresa_id, usuario_id, contato_id),
    CONSTRAINT ck_usuario_apelido_usuarios_diferentes CHECK (usuario_id <> contato_id),
    CONSTRAINT ck_usuario_apelido_nao_vazio CHECK (length(trim(apelido)) BETWEEN 1 AND 60)
);

CREATE INDEX IF NOT EXISTS ix_usuario_apelido_contato
    ON app.usuario_apelido (empresa_id, contato_id);

ALTER TABLE app.dispositivo
    ADD COLUMN IF NOT EXISTS usuario_id BIGINT NULL REFERENCES app.usuario(id);

ALTER TABLE app.dispositivo
    ADD COLUMN IF NOT EXISTS usuario_id_solicitado BIGINT NULL REFERENCES app.usuario(id);

ALTER TABLE app.dispositivo
    ADD COLUMN IF NOT EXISTS solicitado_em TIMESTAMPTZ NULL;

ALTER TABLE app.dispositivo
    ADD COLUMN IF NOT EXISTS aprovado_por_usuario_id BIGINT NULL REFERENCES app.usuario(id);

ALTER TABLE app.dispositivo
    ADD COLUMN IF NOT EXISTS aprovado_em TIMESTAMPTZ NULL;

CREATE INDEX IF NOT EXISTS ix_dispositivo_empresa_usuario
    ON app.dispositivo (empresa_id, usuario_id);

CREATE INDEX IF NOT EXISTS ix_dispositivo_empresa_solicitacao
    ON app.dispositivo (empresa_id, usuario_id_solicitado)
    WHERE usuario_id_solicitado IS NOT NULL OR ativo = FALSE;
