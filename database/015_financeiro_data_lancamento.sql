ALTER TABLE app.financeiro_recorrencia
    ADD COLUMN IF NOT EXISTS data_lancamento DATE NOT NULL DEFAULT CURRENT_DATE;

ALTER TABLE app.financeiro_lancamento
    ADD COLUMN IF NOT EXISTS data_lancamento DATE NOT NULL DEFAULT CURRENT_DATE;
