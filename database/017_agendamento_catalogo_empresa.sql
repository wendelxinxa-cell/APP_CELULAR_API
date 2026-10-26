-- Aplique no CadastroCentral para registrar o nicho selecionado pela empresa.
ALTER TABLE platform.empresa_catalogo
    ADD COLUMN IF NOT EXISTS tipo_negocio TEXT NOT NULL DEFAULT 'comercial',
    ADD COLUMN IF NOT EXISTS nicho_agendamento TEXT NULL;

ALTER TABLE platform.empresa_catalogo
    DROP CONSTRAINT IF EXISTS ck_empresa_catalogo_tipo_negocio;
ALTER TABLE platform.empresa_catalogo
    ADD CONSTRAINT ck_empresa_catalogo_tipo_negocio
    CHECK (tipo_negocio IN ('comercial', 'chat', 'agendamento'));

ALTER TABLE platform.empresa_catalogo
    DROP CONSTRAINT IF EXISTS ck_empresa_catalogo_nicho_agendamento;
ALTER TABLE platform.empresa_catalogo
    ADD CONSTRAINT ck_empresa_catalogo_nicho_agendamento
    CHECK (
        (tipo_negocio = 'agendamento' AND nicho_agendamento IN ('barbearia', 'manicure', 'petshop', 'todos'))
        OR (tipo_negocio <> 'agendamento' AND nicho_agendamento IS NULL)
    );
