-- Cadastro unificado no schema atual do CATCHAT.
-- auth.users continua sendo gerenciado pelo Supabase Auth; chat.usuario_auth é
-- o perfil usado pelas conversas. Zeus aprova a conta e o aparelho numa RPC.

BEGIN;

ALTER TABLE chat.usuario_auth
    ADD COLUMN IF NOT EXISTS username TEXT NULL,
    ADD COLUMN IF NOT EXISTS email TEXT NULL,
    ADD COLUMN IF NOT EXISTS tipo_negocio TEXT NOT NULL DEFAULT 'comercial';

UPDATE chat.usuario_auth SET tipo_negocio='comercial' WHERE tipo_negocio='sucata';

ALTER TABLE chat.solicitacao_acesso
    ADD COLUMN IF NOT EXISTS username TEXT NULL,
    ADD COLUMN IF NOT EXISTS tipo_negocio TEXT NULL;

UPDATE chat.solicitacao_acesso s
SET username=COALESCE(NULLIF(trim(u.raw_user_meta_data->>'username'),''),NULLIF(trim(u.raw_user_meta_data->>'nome'),''),split_part(s.email,'@',1))
FROM auth.users u WHERE u.id=s.auth_uid AND s.username IS NULL;
UPDATE chat.usuario_auth ua
SET email=COALESCE(ua.email,u.email),
    username=COALESCE(NULLIF(trim(u.raw_user_meta_data->>'username'),''),NULLIF(trim(u.raw_user_meta_data->>'nome'),''),split_part(COALESCE(u.email,''),'@',1))
FROM auth.users u WHERE u.id=ua.auth_uid AND ua.username IS NULL;

WITH duplicados AS
(
    SELECT auth_uid,empresa_id,username,
           row_number() OVER (PARTITION BY empresa_id,lower(trim(username)) ORDER BY criado_em,auth_uid) AS ordem
    FROM chat.usuario_auth WHERE username IS NOT NULL AND trim(username)<>''
)
UPDATE chat.usuario_auth ua
SET username=left(d.username,30)||'_'||right(replace(d.auth_uid::text,'-',''),8)
FROM duplicados d WHERE d.auth_uid=ua.auth_uid AND d.ordem>1;

WITH duplicados AS
(
    SELECT auth_uid,empresa_id,username,
           row_number() OVER (PARTITION BY empresa_id,lower(trim(username)) ORDER BY criada_em,auth_uid) AS ordem
    FROM chat.solicitacao_acesso WHERE username IS NOT NULL AND trim(username)<>'' AND status='PENDENTE'
)
UPDATE chat.solicitacao_acesso s
SET username=left(d.username,30)||'_'||right(replace(d.auth_uid::text,'-',''),8)
FROM duplicados d WHERE d.auth_uid=s.auth_uid AND d.ordem>1;

CREATE UNIQUE INDEX IF NOT EXISTS ux_chat_solicitacao_empresa_username
    ON chat.solicitacao_acesso (empresa_id,lower(trim(username)))
    WHERE username IS NOT NULL AND trim(username)<>'' AND status='PENDENTE';

CREATE SEQUENCE IF NOT EXISTS chat.usuario_id_seq;
SELECT setval('chat.usuario_id_seq', GREATEST(COALESCE((SELECT MAX(usuario_id) FROM chat.usuario_auth), 0) + 1, 1), FALSE);
ALTER TABLE chat.usuario_auth ALTER COLUMN usuario_id SET DEFAULT nextval('chat.usuario_id_seq');
ALTER SEQUENCE chat.usuario_id_seq OWNED BY chat.usuario_auth.usuario_id;

CREATE UNIQUE INDEX IF NOT EXISTS ux_chat_usuario_auth_empresa_username
    ON chat.usuario_auth (empresa_id, lower(trim(username)))
    WHERE username IS NOT NULL AND trim(username) <> '';

CREATE TABLE IF NOT EXISTS chat.dispositivo_solicitacao
(
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    auth_uid UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    empresa_id BIGINT NOT NULL CHECK (empresa_id > 0),
    usuario_id BIGINT NULL,
    chave_instalacao UUID NOT NULL,
    nome_dispositivo TEXT NOT NULL DEFAULT 'Navegador web',
    status TEXT NOT NULL DEFAULT 'PENDENTE' CHECK (status IN ('PENDENTE','APROVADO','RECUSADO')),
    criada_em TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    analisada_em TIMESTAMPTZ NULL,
    analisada_por UUID NULL REFERENCES auth.users(id) ON DELETE SET NULL,
    CONSTRAINT ux_chat_dispositivo_empresa_chave UNIQUE (empresa_id, chave_instalacao)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_chat_dispositivo_pendente_usuario
    ON chat.dispositivo_solicitacao (auth_uid, empresa_id)
    WHERE status='PENDENTE';
CREATE INDEX IF NOT EXISTS ix_chat_dispositivo_solicitacao_pendente
    ON chat.dispositivo_solicitacao (empresa_id, criada_em)
    WHERE status='PENDENTE';

ALTER TABLE chat.dispositivo_solicitacao ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON chat.dispositivo_solicitacao FROM PUBLIC, anon, authenticated;

CREATE OR REPLACE FUNCTION chat.registrar_solicitacao_acesso()
RETURNS TRIGGER LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $$
DECLARE
    v_empresa BIGINT;
    v_username TEXT;
    v_chave UUID;
BEGIN
    v_empresa := COALESCE(NULLIF(NEW.raw_user_meta_data->>'empresa_id','')::BIGINT, 1);
    v_username := left(regexp_replace(COALESCE(NULLIF(trim(NEW.raw_user_meta_data->>'username'),''), split_part(COALESCE(NEW.email,''),'@',1)), '[^A-Za-z0-9._-]', '', 'g'), 40);
    v_chave := COALESCE(NULLIF(NEW.raw_user_meta_data->>'chave_instalacao','')::UUID, gen_random_uuid());

    IF v_username !~ '^[A-Za-z0-9._-]{3,40}$' THEN
        RAISE EXCEPTION 'Username deve ter de 3 a 40 caracteres: letras, números, ponto, hífen ou sublinhado';
    END IF;
    PERFORM pg_advisory_xact_lock(hashtextextended(v_empresa::TEXT||':'||lower(v_username),0));
    IF EXISTS (SELECT 1 FROM chat.usuario_auth u WHERE u.empresa_id=v_empresa AND lower(trim(u.username))=lower(v_username))
       OR EXISTS (SELECT 1 FROM chat.solicitacao_acesso s WHERE s.empresa_id=v_empresa AND s.auth_uid<>NEW.id
                 AND s.status='PENDENTE' AND lower(trim(s.username))=lower(v_username)) THEN
        IF NULLIF(trim(NEW.raw_user_meta_data->>'username'),'') IS NOT NULL THEN
            RAISE EXCEPTION 'Username já está em uso nesta empresa';
        END IF;
        v_username := left(v_username,30)||'_'||right(replace(NEW.id::text,'-',''),8);
    END IF;

    INSERT INTO chat.solicitacao_acesso (auth_uid,email,nome,username,empresa_id,status)
    VALUES (NEW.id,COALESCE(NEW.email,''),v_username,v_username,v_empresa,'PENDENTE')
    ON CONFLICT (auth_uid) DO UPDATE SET
        email=EXCLUDED.email, username=EXCLUDED.username, nome=EXCLUDED.nome,
        empresa_id=EXCLUDED.empresa_id;

    INSERT INTO chat.dispositivo_solicitacao (auth_uid,empresa_id,chave_instalacao,nome_dispositivo)
    VALUES (NEW.id,v_empresa,v_chave,COALESCE(NULLIF(NEW.raw_user_meta_data->>'nome_dispositivo',''),'Navegador web'))
    ON CONFLICT (empresa_id,chave_instalacao) DO NOTHING;
    RETURN NEW;
END
$$;

DROP TRIGGER IF EXISTS tr_chat_registrar_solicitacao_acesso ON auth.users;
CREATE TRIGGER tr_chat_registrar_solicitacao_acesso AFTER INSERT ON auth.users
    FOR EACH ROW EXECUTE FUNCTION chat.registrar_solicitacao_acesso();

DROP FUNCTION IF EXISTS chat.listar_solicitacoes_acesso();
CREATE FUNCTION chat.listar_solicitacoes_acesso()
RETURNS TABLE
(
    auth_uid UUID, email TEXT, nome TEXT, criada_em TIMESTAMPTZ, status TEXT,
    empresa_id BIGINT, username TEXT, dispositivo_id BIGINT, nome_dispositivo TEXT
)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $$
DECLARE v_empresa BIGINT;
BEGIN
    IF NOT chat.eh_zeus() THEN RAISE EXCEPTION 'Acesso restrito ao usuário Zeus'; END IF;
    IF NOT chat.eh_master() THEN
        SELECT u.empresa_id INTO v_empresa FROM chat.usuario_auth u
        WHERE u.auth_uid=(SELECT auth.uid()) AND u.ativo AND u.eh_zeus LIMIT 1;
    END IF;
    RETURN QUERY
    SELECT s.auth_uid,s.email,s.nome,s.criada_em,s.status,s.empresa_id,s.username,d.id,COALESCE(d.nome_dispositivo,'Aparelho não informado')
    FROM chat.solicitacao_acesso s
    LEFT JOIN chat.dispositivo_solicitacao d ON d.auth_uid=s.auth_uid AND d.empresa_id=s.empresa_id AND d.status='PENDENTE'
    WHERE s.status='PENDENTE' AND (chat.eh_master() OR s.empresa_id=v_empresa)
    ORDER BY s.criada_em,d.criada_em;
END
$$;

DROP FUNCTION IF EXISTS chat.aprovar_solicitacao_acesso(UUID,BIGINT,BIGINT);
DROP FUNCTION IF EXISTS chat.aprovar_solicitacao_acesso(UUID,BIGINT);
CREATE FUNCTION chat.aprovar_solicitacao_acesso(p_auth_uid UUID,p_empresa_id BIGINT,p_tipo_negocio TEXT)
RETURNS BIGINT LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $$
DECLARE
    v_empresa BIGINT;
    v_nome TEXT;
    v_email TEXT;
    v_username TEXT;
    v_usuario_id BIGINT;
    v_dispositivo_id BIGINT;
BEGIN
    IF NOT chat.eh_zeus() THEN RAISE EXCEPTION 'Acesso restrito ao usuário Zeus'; END IF;
    IF p_tipo_negocio IS NULL OR p_tipo_negocio NOT IN ('chat','comercial') THEN RAISE EXCEPTION 'Tipo de negócio inválido'; END IF;
    IF chat.eh_master() THEN
        v_empresa := p_empresa_id;
    ELSE
        SELECT u.empresa_id INTO v_empresa FROM chat.usuario_auth u
        WHERE u.auth_uid=(SELECT auth.uid()) AND u.ativo AND u.eh_zeus LIMIT 1;
        IF v_empresa IS DISTINCT FROM p_empresa_id THEN RAISE EXCEPTION 'Empresa fora do escopo do Zeus'; END IF;
    END IF;

    SELECT s.nome,s.email,s.username INTO v_nome,v_email,v_username
    FROM chat.solicitacao_acesso s
    WHERE s.auth_uid=p_auth_uid AND s.empresa_id=v_empresa AND s.status='PENDENTE'
    FOR UPDATE;
    IF v_nome IS NULL THEN RAISE EXCEPTION 'Solicitação pendente não encontrada'; END IF;

    SELECT d.id INTO v_dispositivo_id FROM chat.dispositivo_solicitacao d
    WHERE d.auth_uid=p_auth_uid AND d.empresa_id=v_empresa AND d.status='PENDENTE'
    ORDER BY d.criada_em LIMIT 1 FOR UPDATE;
    IF v_dispositivo_id IS NULL THEN RAISE EXCEPTION 'A solicitação de aparelho não foi encontrada'; END IF;

    INSERT INTO chat.usuario_auth (auth_uid,empresa_id,usuario_id,nome,ativo,username,email,tipo_negocio)
    VALUES (p_auth_uid,v_empresa,DEFAULT,v_nome,TRUE,v_username,v_email,p_tipo_negocio)
    RETURNING usuario_id INTO v_usuario_id;

    UPDATE chat.solicitacao_acesso SET status='APROVADO',usuario_id=v_usuario_id,
        tipo_negocio=p_tipo_negocio,analisada_em=NOW(),analisada_por=(SELECT auth.uid())
    WHERE auth_uid=p_auth_uid;
    UPDATE chat.dispositivo_solicitacao SET status='APROVADO',usuario_id=v_usuario_id,
        analisada_em=NOW(),analisada_por=(SELECT auth.uid()) WHERE id=v_dispositivo_id;
    RETURN v_usuario_id;
END
$$;

-- Compatibilidade do convite administrativo, que vincula uma conta Auth a
-- um usuário CATCHAT já existente. O cadastro público acima gera o ID sozinho.
CREATE FUNCTION chat.aprovar_solicitacao_acesso(p_auth_uid UUID,p_usuario_id BIGINT,p_empresa_id BIGINT DEFAULT 1)
RETURNS VOID LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $$
DECLARE
    v_empresa BIGINT;
    v_nome TEXT;
    v_email TEXT;
    v_username TEXT;
    v_dispositivo_id BIGINT;
BEGIN
    IF NOT chat.eh_zeus() THEN RAISE EXCEPTION 'Acesso restrito ao usuário Zeus'; END IF;
    IF p_usuario_id IS NULL OR p_usuario_id<=0 OR p_empresa_id IS NULL OR p_empresa_id<=0 THEN
        RAISE EXCEPTION 'Informe empresa e usuário CATCHAT válidos';
    END IF;
    IF chat.eh_master() THEN
        v_empresa := p_empresa_id;
    ELSE
        SELECT u.empresa_id INTO v_empresa FROM chat.usuario_auth u
        WHERE u.auth_uid=(SELECT auth.uid()) AND u.ativo AND u.eh_zeus LIMIT 1;
        IF v_empresa IS DISTINCT FROM p_empresa_id THEN RAISE EXCEPTION 'Empresa fora do escopo do Zeus'; END IF;
    END IF;
    SELECT s.nome,s.email,s.username INTO v_nome,v_email,v_username
    FROM chat.solicitacao_acesso s
    WHERE s.auth_uid=p_auth_uid AND s.empresa_id=v_empresa AND s.status='PENDENTE'
    FOR UPDATE;
    IF v_nome IS NULL THEN RAISE EXCEPTION 'Solicitação pendente não encontrada'; END IF;
    SELECT d.id INTO v_dispositivo_id FROM chat.dispositivo_solicitacao d
    WHERE d.auth_uid=p_auth_uid AND d.empresa_id=v_empresa AND d.status='PENDENTE'
    ORDER BY d.criada_em LIMIT 1 FOR UPDATE;
    IF v_dispositivo_id IS NULL THEN RAISE EXCEPTION 'A solicitação de aparelho não foi encontrada'; END IF;
    INSERT INTO chat.usuario_auth (auth_uid,empresa_id,usuario_id,nome,ativo,username,email,tipo_negocio)
    VALUES (p_auth_uid,v_empresa,p_usuario_id,v_nome,TRUE,v_username,v_email,'comercial');
    UPDATE chat.solicitacao_acesso SET status='APROVADO',usuario_id=p_usuario_id,
        tipo_negocio='comercial',analisada_em=NOW(),analisada_por=(SELECT auth.uid())
    WHERE auth_uid=p_auth_uid AND empresa_id=v_empresa;
    UPDATE chat.dispositivo_solicitacao SET status='APROVADO',usuario_id=p_usuario_id,
        analisada_em=NOW(),analisada_por=(SELECT auth.uid()) WHERE id=v_dispositivo_id;
END
$$;

CREATE OR REPLACE FUNCTION chat.recusar_solicitacao_acesso(p_auth_uid UUID)
RETURNS VOID LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $$
DECLARE v_empresa BIGINT;
BEGIN
    IF NOT chat.eh_zeus() THEN RAISE EXCEPTION 'Acesso restrito ao usuário Zeus'; END IF;
    IF NOT chat.eh_master() THEN
        SELECT u.empresa_id INTO v_empresa FROM chat.usuario_auth u
        WHERE u.auth_uid=(SELECT auth.uid()) AND u.ativo AND u.eh_zeus LIMIT 1;
    END IF;
    UPDATE chat.solicitacao_acesso SET status='RECUSADO',analisada_em=NOW(),
        analisada_por=(SELECT auth.uid())
    WHERE auth_uid=p_auth_uid AND status='PENDENTE'
      AND (chat.eh_master() OR empresa_id=v_empresa);
    IF NOT FOUND THEN RAISE EXCEPTION 'Solicitação pendente não encontrada'; END IF;
    UPDATE chat.dispositivo_solicitacao SET status='RECUSADO',analisada_em=NOW(),
        analisada_por=(SELECT auth.uid())
    WHERE auth_uid=p_auth_uid AND status='PENDENTE'
      AND (chat.eh_master() OR empresa_id=v_empresa);
END
$$;

REVOKE ALL ON FUNCTION chat.listar_solicitacoes_acesso(),
    chat.aprovar_solicitacao_acesso(UUID,BIGINT,TEXT),
    chat.aprovar_solicitacao_acesso(UUID,BIGINT,BIGINT),
    chat.recusar_solicitacao_acesso(UUID) FROM PUBLIC,anon;
GRANT EXECUTE ON FUNCTION chat.listar_solicitacoes_acesso(),
    chat.aprovar_solicitacao_acesso(UUID,BIGINT,TEXT),
    chat.aprovar_solicitacao_acesso(UUID,BIGINT,BIGINT),
    chat.recusar_solicitacao_acesso(UUID) TO authenticated;

NOTIFY pgrst, 'reload schema';
COMMIT;
