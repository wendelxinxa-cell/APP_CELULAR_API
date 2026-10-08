-- Solicitação de acesso por Supabase Auth e aprovação de Zeus.
-- Execute somente depois de 010_supabase_direct_chat.sql.

ALTER TABLE chat.usuario_auth ADD COLUMN IF NOT EXISTS eh_zeus BOOLEAN NOT NULL DEFAULT FALSE;

CREATE TABLE IF NOT EXISTS chat.solicitacao_acesso
(
    auth_uid UUID PRIMARY KEY REFERENCES auth.users(id) ON DELETE CASCADE,
    email TEXT NOT NULL,
    nome TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'PENDENTE' CHECK (status IN ('PENDENTE','APROVADO','RECUSADO')),
    usuario_id BIGINT NULL,
    criada_em TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    analisada_em TIMESTAMPTZ NULL,
    analisada_por UUID NULL REFERENCES auth.users(id) ON DELETE SET NULL
);

CREATE OR REPLACE FUNCTION chat.registrar_solicitacao_acesso()
RETURNS TRIGGER LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $$
BEGIN
    INSERT INTO chat.solicitacao_acesso (auth_uid,email,nome)
    VALUES (NEW.id,COALESCE(NEW.email,''),left(COALESCE(NEW.raw_user_meta_data->>'nome',''),120))
    ON CONFLICT (auth_uid) DO NOTHING;
    RETURN NEW;
END
$$;
DROP TRIGGER IF EXISTS tr_chat_registrar_solicitacao_acesso ON auth.users;
CREATE TRIGGER tr_chat_registrar_solicitacao_acesso AFTER INSERT ON auth.users
    FOR EACH ROW EXECUTE FUNCTION chat.registrar_solicitacao_acesso();

CREATE OR REPLACE FUNCTION chat.eh_zeus()
RETURNS BOOLEAN LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $$
    SELECT EXISTS (SELECT 1 FROM chat.usuario_auth
      WHERE auth_uid=(SELECT auth.uid()) AND ativo AND eh_zeus)
$$;

CREATE OR REPLACE FUNCTION chat.status_solicitacao_acesso()
RETURNS TABLE (status TEXT,nome TEXT)
LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $$
    SELECT s.status,s.nome FROM chat.solicitacao_acesso s
    WHERE s.auth_uid=(SELECT auth.uid())
$$;

CREATE OR REPLACE FUNCTION chat.listar_solicitacoes_acesso()
RETURNS TABLE (auth_uid UUID,email TEXT,nome TEXT,criada_em TIMESTAMPTZ,status TEXT)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $$
BEGIN
    IF NOT chat.eh_zeus() THEN RAISE EXCEPTION 'Acesso restrito ao usuário Zeus'; END IF;
    RETURN QUERY SELECT s.auth_uid,s.email,s.nome,s.criada_em,s.status
      FROM chat.solicitacao_acesso s WHERE s.status='PENDENTE' ORDER BY s.criada_em;
END
$$;

CREATE OR REPLACE FUNCTION chat.aprovar_solicitacao_acesso(p_auth_uid UUID,p_usuario_id BIGINT)
RETURNS VOID LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $$
DECLARE v_empresa BIGINT; v_nome TEXT;
BEGIN
    SELECT empresa_id INTO v_empresa FROM chat.usuario_auth
      WHERE auth_uid=(SELECT auth.uid()) AND ativo AND eh_zeus LIMIT 1;
    IF v_empresa IS NULL THEN RAISE EXCEPTION 'Acesso restrito ao usuário Zeus'; END IF;
    IF p_usuario_id IS NULL OR p_usuario_id<=0 THEN RAISE EXCEPTION 'Informe um ID válido do APP CELULAR'; END IF;
    SELECT s.nome INTO v_nome FROM chat.solicitacao_acesso s
      WHERE s.auth_uid=p_auth_uid AND s.status='PENDENTE' FOR UPDATE;
    IF v_nome IS NULL THEN RAISE EXCEPTION 'Solicitação pendente não encontrada'; END IF;
    INSERT INTO chat.usuario_auth (auth_uid,empresa_id,usuario_id,nome,ativo)
      VALUES (p_auth_uid,v_empresa,p_usuario_id,v_nome,TRUE);
    UPDATE chat.solicitacao_acesso SET status='APROVADO',usuario_id=p_usuario_id,
      analisada_em=NOW(),analisada_por=(SELECT auth.uid()) WHERE auth_uid=p_auth_uid;
END
$$;

CREATE OR REPLACE FUNCTION chat.recusar_solicitacao_acesso(p_auth_uid UUID)
RETURNS VOID LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $$
BEGIN
    IF NOT chat.eh_zeus() THEN RAISE EXCEPTION 'Acesso restrito ao usuário Zeus'; END IF;
    UPDATE chat.solicitacao_acesso SET status='RECUSADO',analisada_em=NOW(),
      analisada_por=(SELECT auth.uid()) WHERE auth_uid=p_auth_uid AND status='PENDENTE';
    IF NOT FOUND THEN RAISE EXCEPTION 'Solicitação pendente não encontrada'; END IF;
END
$$;

ALTER TABLE chat.solicitacao_acesso ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS solicitacao_acesso_propria ON chat.solicitacao_acesso;
CREATE POLICY solicitacao_acesso_propria ON chat.solicitacao_acesso FOR SELECT TO authenticated
  USING (auth_uid=(SELECT auth.uid()));

REVOKE ALL ON chat.solicitacao_acesso FROM PUBLIC,anon,authenticated;
GRANT SELECT ON chat.solicitacao_acesso TO authenticated;
REVOKE ALL ON FUNCTION chat.registrar_solicitacao_acesso(),chat.eh_zeus(),
  chat.status_solicitacao_acesso(),chat.listar_solicitacoes_acesso(),
  chat.aprovar_solicitacao_acesso(UUID,BIGINT),chat.recusar_solicitacao_acesso(UUID)
  FROM PUBLIC,anon;
GRANT EXECUTE ON FUNCTION chat.eh_zeus(),chat.status_solicitacao_acesso(),
  chat.listar_solicitacoes_acesso(),chat.aprovar_solicitacao_acesso(UUID,BIGINT),
  chat.recusar_solicitacao_acesso(UUID) TO authenticated;
