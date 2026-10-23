CREATE SCHEMA IF NOT EXISTS app;

CREATE TABLE IF NOT EXISTS app.financeiro_tipo (
    empresa_id BIGINT NOT NULL,
    chave UUID NOT NULL,
    nome TEXT NOT NULL,
    cor TEXT NOT NULL DEFAULT '#6C63FF',
    data_alteracao TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    excluido BOOLEAN NOT NULL DEFAULT FALSE,
    PRIMARY KEY (empresa_id, chave)
);

CREATE TABLE IF NOT EXISTS app.financeiro_recorrencia (
    empresa_id BIGINT NOT NULL,
    chave UUID NOT NULL,
    tipo_chave UUID NOT NULL,
    tipo_nome TEXT NOT NULL DEFAULT '',
    natureza TEXT NOT NULL CHECK (natureza IN ('PAGAR','RECEBER')),
    descricao TEXT NOT NULL,
    observacao TEXT NOT NULL DEFAULT '',
    valor NUMERIC(14,2) NOT NULL CHECK (valor > 0),
    dia_vencimento SMALLINT NOT NULL CHECK (dia_vencimento BETWEEN 1 AND 31),
    intervalo_meses SMALLINT NOT NULL DEFAULT 1 CHECK (intervalo_meses BETWEEN 1 AND 12),
    data_inicio DATE NOT NULL,
    data_fim DATE NULL,
    ativa BOOLEAN NOT NULL DEFAULT TRUE,
    data_alteracao TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    excluido BOOLEAN NOT NULL DEFAULT FALSE,
    PRIMARY KEY (empresa_id, chave)
);

CREATE TABLE IF NOT EXISTS app.financeiro_lancamento (
    empresa_id BIGINT NOT NULL,
    chave UUID NOT NULL,
    recorrencia_chave UUID NULL,
    tipo_chave UUID NOT NULL,
    tipo_nome TEXT NOT NULL DEFAULT '',
    natureza TEXT NOT NULL CHECK (natureza IN ('PAGAR','RECEBER')),
    tipo_lancamento TEXT NOT NULL CHECK (tipo_lancamento IN ('FIXO','VARIAVEL')),
    descricao TEXT NOT NULL,
    observacao TEXT NOT NULL DEFAULT '',
    valor NUMERIC(14,2) NOT NULL CHECK (valor > 0),
    data_vencimento DATE NOT NULL,
    data_liquidacao DATE NULL,
    status TEXT NOT NULL CHECK (status IN ('PENDENTE','PAGO','RECEBIDO','CANCELADO')),
    data_alteracao TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    excluido BOOLEAN NOT NULL DEFAULT FALSE,
    PRIMARY KEY (empresa_id, chave)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_financeiro_lancamento_recorrencia_data
    ON app.financeiro_lancamento (empresa_id, recorrencia_chave, data_vencimento)
    WHERE recorrencia_chave IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_financeiro_lancamento_periodo
    ON app.financeiro_lancamento (empresa_id, data_vencimento)
    WHERE excluido = FALSE;
