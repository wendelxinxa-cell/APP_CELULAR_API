-- Execute no Supabase central para habilitar empresas APPENTREGA.
ALTER TABLE platform.empresa_catalogo
    DROP CONSTRAINT IF EXISTS ck_empresa_catalogo_tipo_negocio;

ALTER TABLE platform.empresa_catalogo
    ADD CONSTRAINT ck_empresa_catalogo_tipo_negocio
    CHECK (tipo_negocio IN ('comercial','chat','agendamento','appentrega'));
