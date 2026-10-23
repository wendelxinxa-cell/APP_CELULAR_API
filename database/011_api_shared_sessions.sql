-- Required only for deployments using SessionStore:UsePersistentDatabase=true.
-- Session tokens are never stored directly: the API stores only their SHA-256 hashes.
CREATE SCHEMA IF NOT EXISTS platform;

CREATE TABLE IF NOT EXISTS platform.api_tenant_session (
    token_hash BYTEA PRIMARY KEY CHECK (octet_length(token_hash) = 32),
    empresa_id BIGINT NOT NULL,
    usuario_id BIGINT NOT NULL,
    eh_administrador BOOLEAN NOT NULL,
    dispositivo_id BIGINT NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_api_tenant_session_expiration
    ON platform.api_tenant_session (expires_at);
CREATE INDEX IF NOT EXISTS ix_api_tenant_session_device
    ON platform.api_tenant_session (empresa_id, dispositivo_id);

CREATE TABLE IF NOT EXISTS platform.api_master_session (
    token_hash BYTEA PRIMARY KEY CHECK (octet_length(token_hash) = 32),
    expires_at TIMESTAMPTZ NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_api_master_session_expiration
    ON platform.api_master_session (expires_at);
