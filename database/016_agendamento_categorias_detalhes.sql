-- Detalhes específicos por categoria para a agenda operacional.
-- Aplicar no projeto Supabase APP-CELULAR (banco operacional, por empresa).
BEGIN;

ALTER TABLE public.agendamento_agendamentos
    ADD COLUMN IF NOT EXISTS categoria_servico VARCHAR(40) NOT NULL DEFAULT 'servico',
    ADD COLUMN IF NOT EXISTS data_fim TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS detalhes JSONB NOT NULL DEFAULT '{}'::JSONB;

ALTER TABLE public.agendamento_servicos
    ADD COLUMN IF NOT EXISTS categoria_atendimento VARCHAR(40) NOT NULL DEFAULT 'servico';

UPDATE public.agendamento_servicos
SET categoria_atendimento = CASE
    WHEN nome ILIKE '%veterin%' OR nome ILIKE '%consulta%' OR nome ILIKE '%vacina%' OR nome ILIKE '%exame%' THEN 'veterinario'
    WHEN nome ILIKE '%hotel%' OR nome ILIKE '%hosped%' OR nome ILIKE '%diária%' OR nome ILIKE '%diaria%' THEN 'hotel'
    WHEN nome ILIKE '%creche%' OR nome ILIKE '%daycare%' THEN 'creche'
    WHEN nome ILIKE '%banho%' OR nome ILIKE '%tosa%' OR nome ILIKE '%hidrata%' THEN 'banho_tosa'
    ELSE 'servico'
END
WHERE categoria_atendimento = 'servico';

ALTER TABLE public.agendamento_agendamentos
    DROP CONSTRAINT IF EXISTS ck_agendamento_data_fim;
ALTER TABLE public.agendamento_agendamentos
    ADD CONSTRAINT ck_agendamento_data_fim
    CHECK (data_fim IS NULL OR data_fim > data_horario);

COMMENT ON COLUMN public.agendamento_agendamentos.categoria_servico IS
    'Categoria: banho_tosa, veterinario, hotel, creche ou servico.';
COMMENT ON COLUMN public.agendamento_agendamentos.data_fim IS
    'Fim da hospedagem/reserva; nulo para atendimentos de horário simples.';
COMMENT ON COLUMN public.agendamento_agendamentos.detalhes IS
    'Campos específicos por categoria, como pacote, táxi dog, recorrência e observações.';

COMMIT;
