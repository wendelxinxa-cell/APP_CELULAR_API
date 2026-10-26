-- Agenda compartilhada entre o app e o portal web.
-- Aplique no Supabase principal; todas as linhas são isoladas por empresa_id.
BEGIN;

-- O tipo e o nicho do portal vêm do perfil da empresa no banco tenant.
ALTER TABLE chat.usuario_auth
    ADD COLUMN IF NOT EXISTS tipo_negocio TEXT NOT NULL DEFAULT 'comercial',
    ADD COLUMN IF NOT EXISTS nicho_agendamento TEXT NULL;

CREATE OR REPLACE FUNCTION chat.obter_configuracao_agendamento(p_empresa_id BIGINT)
RETURNS TABLE (tipo_negocio TEXT, nicho_agendamento TEXT)
LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $$
    SELECT COALESCE(u.tipo_negocio, 'comercial'), u.nicho_agendamento
    FROM chat.usuario_auth u
    WHERE u.auth_uid=(SELECT auth.uid()) AND u.empresa_id=p_empresa_id AND u.ativo
    LIMIT 1
$$;
REVOKE ALL ON FUNCTION chat.obter_configuracao_agendamento(BIGINT) FROM PUBLIC, anon;
GRANT EXECUTE ON FUNCTION chat.obter_configuracao_agendamento(BIGINT) TO authenticated;

CREATE OR REPLACE FUNCTION public.agendamento_empresa_atual()
RETURNS BIGINT
LANGUAGE SQL STABLE SECURITY DEFINER SET search_path = '' AS $$
    SELECT u.empresa_id
    FROM chat.usuario_auth u
    WHERE u.auth_uid = (SELECT auth.uid()) AND u.ativo
    ORDER BY u.empresa_id
    LIMIT 1
$$;

REVOKE ALL ON FUNCTION public.agendamento_empresa_atual() FROM PUBLIC, anon;
GRANT EXECUTE ON FUNCTION public.agendamento_empresa_atual() TO authenticated;

CREATE TABLE IF NOT EXISTS public.agendamento_clientes (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    empresa_id BIGINT NOT NULL CHECK (empresa_id > 0),
    nome VARCHAR(250) NOT NULL,
    whatsapp VARCHAR(20) NOT NULL,
    observacoes TEXT,
    criado_em TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (empresa_id, id),
    UNIQUE (empresa_id, whatsapp)
);

CREATE TABLE IF NOT EXISTS public.agendamento_pets (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    empresa_id BIGINT NOT NULL CHECK (empresa_id > 0),
    cliente_id UUID NOT NULL,
    nome_pet VARCHAR(100) NOT NULL,
    raca VARCHAR(100),
    porte VARCHAR(20) CHECK (porte IN ('Pequeno', 'Médio', 'Grande')),
    observacoes_alergias TEXT,
    criado_em TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (empresa_id, id),
    FOREIGN KEY (empresa_id, cliente_id)
        REFERENCES public.agendamento_clientes(empresa_id, id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS public.agendamento_profissionais (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    empresa_id BIGINT NOT NULL CHECK (empresa_id > 0),
    nome VARCHAR(200) NOT NULL,
    nicho VARCHAR(30) NOT NULL CHECK (nicho IN ('barbearia', 'manicure', 'petshop', 'todos')),
    foto_url TEXT,
    ativo BOOLEAN NOT NULL DEFAULT TRUE,
    UNIQUE (empresa_id, id)
);

CREATE TABLE IF NOT EXISTS public.agendamento_servicos (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    empresa_id BIGINT NOT NULL CHECK (empresa_id > 0),
    nicho VARCHAR(30) NOT NULL CHECK (nicho IN ('barbearia', 'manicure', 'petshop')),
    nome VARCHAR(200) NOT NULL,
    duracao_minutos INT NOT NULL CHECK (duracao_minutos BETWEEN 5 AND 1440),
    preco_brl NUMERIC(10,2) NOT NULL CHECK (preco_brl >= 0),
    ativo BOOLEAN NOT NULL DEFAULT TRUE,
    UNIQUE (empresa_id, id)
);

CREATE TABLE IF NOT EXISTS public.agendamento_materiais (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    empresa_id BIGINT NOT NULL CHECK (empresa_id > 0),
    nicho VARCHAR(30) NOT NULL CHECK (nicho IN ('barbearia', 'manicure', 'petshop')),
    nome_item VARCHAR(200) NOT NULL,
    quantidade NUMERIC(10,2) NOT NULL DEFAULT 0 CHECK (quantidade >= 0),
    unidade VARCHAR(20) NOT NULL,
    estoque_minimo NUMERIC(10,2) NOT NULL DEFAULT 5 CHECK (estoque_minimo >= 0),
    atualizado_em TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (empresa_id, id)
);

CREATE TABLE IF NOT EXISTS public.agendamento_agendamentos (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    empresa_id BIGINT NOT NULL CHECK (empresa_id > 0),
    cliente_id UUID NOT NULL,
    pet_id UUID,
    servico_id UUID NOT NULL,
    profissional_id UUID NOT NULL,
    data_horario TIMESTAMPTZ NOT NULL,
    status VARCHAR(30) NOT NULL DEFAULT 'confirmado'
        CHECK (status IN ('confirmado', 'pendente', 'em_atendimento', 'concluido', 'cancelado')),
    valor_total NUMERIC(10,2) NOT NULL CHECK (valor_total >= 0),
    mensagem_whatsapp_enviada BOOLEAN NOT NULL DEFAULT FALSE,
    criado_em TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (empresa_id, id),
    FOREIGN KEY (empresa_id, cliente_id)
        REFERENCES public.agendamento_clientes(empresa_id, id) ON DELETE RESTRICT,
    FOREIGN KEY (empresa_id, pet_id)
        REFERENCES public.agendamento_pets(empresa_id, id) ON DELETE SET NULL (pet_id),
    FOREIGN KEY (empresa_id, servico_id)
        REFERENCES public.agendamento_servicos(empresa_id, id) ON DELETE RESTRICT,
    FOREIGN KEY (empresa_id, profissional_id)
        REFERENCES public.agendamento_profissionais(empresa_id, id) ON DELETE RESTRICT
);

CREATE INDEX IF NOT EXISTS ix_agendamento_agendamentos_empresa_data
    ON public.agendamento_agendamentos (empresa_id, data_horario)
    WHERE status <> 'cancelado';
CREATE INDEX IF NOT EXISTS ix_agendamento_agendamentos_profissional_data
    ON public.agendamento_agendamentos (empresa_id, profissional_id, data_horario)
    WHERE status NOT IN ('cancelado', 'concluido');
CREATE INDEX IF NOT EXISTS ix_agendamento_pets_cliente
    ON public.agendamento_pets (empresa_id, cliente_id);

DO $$
DECLARE tabela TEXT;
BEGIN
    FOREACH tabela IN ARRAY ARRAY[
        'agendamento_clientes', 'agendamento_pets', 'agendamento_profissionais',
        'agendamento_servicos', 'agendamento_materiais', 'agendamento_agendamentos'
    ] LOOP
        EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY', tabela);
        EXECUTE format('DROP POLICY IF EXISTS empresa_agendamento_isolada ON public.%I', tabela);
        EXECUTE format(
            'CREATE POLICY empresa_agendamento_isolada ON public.%I FOR ALL TO authenticated USING (empresa_id = (SELECT public.agendamento_empresa_atual())) WITH CHECK (empresa_id = (SELECT public.agendamento_empresa_atual()))',
            tabela
        );
        EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON public.%I TO authenticated', tabela);
    END LOOP;
END $$;

DO $$
DECLARE tabela REGCLASS;
BEGIN
    IF EXISTS (SELECT 1 FROM pg_publication WHERE pubname = 'supabase_realtime') THEN
        FOREACH tabela IN ARRAY ARRAY[
            'public.agendamento_agendamentos'::REGCLASS,
            'public.agendamento_profissionais'::REGCLASS,
            'public.agendamento_servicos'::REGCLASS
        ] LOOP
            IF NOT EXISTS (
                SELECT 1 FROM pg_publication_tables
                WHERE pubname = 'supabase_realtime' AND schemaname = 'public' AND tablename = split_part(tabela::TEXT, '.', 2)
            ) THEN
                EXECUTE format('ALTER PUBLICATION supabase_realtime ADD TABLE %s', tabela);
            END IF;
        END LOOP;
    END IF;
END $$;

COMMIT;
