-- Zeus global: conta administrativa separada dos usuários comuns.
-- Execute após 020_supabase_access_requests.sql.

CREATE TABLE IF NOT EXISTS chat.usuario_master
(
    auth_uid UUID PRIMARY KEY REFERENCES auth.users(id) ON DELETE CASCADE,
    nome TEXT NOT NULL DEFAULT 'Zeus',
    ativo BOOLEAN NOT NULL DEFAULT TRUE,
    criado_em TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
ALTER TABLE chat.usuario_master ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON chat.usuario_master FROM PUBLIC,anon,authenticated;

ALTER TABLE chat.solicitacao_acesso
  ADD COLUMN IF NOT EXISTS empresa_id BIGINT NOT NULL DEFAULT 1 CHECK (empresa_id > 0);
CREATE INDEX IF NOT EXISTS ix_chat_solicitacao_acesso_pendente
  ON chat.solicitacao_acesso (empresa_id,criada_em) WHERE status='PENDENTE';

CREATE OR REPLACE FUNCTION chat.eh_master()
RETURNS BOOLEAN LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $$
    SELECT EXISTS (SELECT 1 FROM chat.usuario_master
      WHERE auth_uid=(SELECT auth.uid()) AND ativo)
$$;

CREATE OR REPLACE FUNCTION chat.eh_zeus()
RETURNS BOOLEAN LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $$
    SELECT chat.eh_master() OR EXISTS (SELECT 1 FROM chat.usuario_auth
      WHERE auth_uid=(SELECT auth.uid()) AND ativo AND eh_zeus)
$$;

CREATE OR REPLACE FUNCTION chat.obter_identidade(p_empresa_id BIGINT)
RETURNS TABLE (empresa_id BIGINT, usuario_id BIGINT, nome TEXT)
LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $$
    SELECT u.empresa_id,u.usuario_id,u.nome FROM chat.usuario_auth u
      WHERE u.auth_uid=(SELECT auth.uid()) AND u.empresa_id=p_empresa_id AND u.ativo
    UNION ALL
    SELECT p_empresa_id,0::BIGINT,m.nome FROM chat.usuario_master m
      WHERE m.auth_uid=(SELECT auth.uid()) AND m.ativo AND p_empresa_id>0
        AND NOT EXISTS (SELECT 1 FROM chat.usuario_auth u
          WHERE u.auth_uid=m.auth_uid AND u.empresa_id=p_empresa_id AND u.ativo)
    LIMIT 1
$$;

CREATE OR REPLACE FUNCTION chat.pode_acessar_conversa(p_empresa_id BIGINT,p_conversa_id UUID)
RETURNS BOOLEAN LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $$
    SELECT chat.eh_master() OR EXISTS (
        SELECT 1 FROM chat.usuario_auth u
        JOIN chat.participante p ON p.empresa_id=u.empresa_id AND p.usuario_id=u.usuario_id
        WHERE u.auth_uid=(SELECT auth.uid()) AND u.ativo AND u.empresa_id=p_empresa_id
          AND p.conversa_id=p_conversa_id AND p.saiu_em IS NULL
    )
$$;

DROP FUNCTION IF EXISTS chat.listar_solicitacoes_acesso();
CREATE OR REPLACE FUNCTION chat.listar_solicitacoes_acesso()
RETURNS TABLE (auth_uid UUID,email TEXT,nome TEXT,criada_em TIMESTAMPTZ,status TEXT,empresa_id BIGINT)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $$
DECLARE v_empresa BIGINT;
BEGIN
    IF NOT chat.eh_zeus() THEN RAISE EXCEPTION 'Acesso restrito ao usuário Zeus'; END IF;
    IF NOT chat.eh_master() THEN
      SELECT u.empresa_id INTO v_empresa FROM chat.usuario_auth u
        WHERE u.auth_uid=(SELECT auth.uid()) AND u.ativo AND u.eh_zeus LIMIT 1;
    END IF;
    RETURN QUERY SELECT s.auth_uid,s.email,s.nome,s.criada_em,s.status,s.empresa_id
      FROM chat.solicitacao_acesso s
      WHERE s.status='PENDENTE' AND (chat.eh_master() OR s.empresa_id=v_empresa)
      ORDER BY s.criada_em;
END
$$;

DROP FUNCTION IF EXISTS chat.aprovar_solicitacao_acesso(UUID,BIGINT);
CREATE OR REPLACE FUNCTION chat.aprovar_solicitacao_acesso(p_auth_uid UUID,p_usuario_id BIGINT,p_empresa_id BIGINT DEFAULT 1)
RETURNS VOID LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $$
DECLARE v_empresa BIGINT; v_nome TEXT;
BEGIN
    IF NOT chat.eh_zeus() THEN RAISE EXCEPTION 'Acesso restrito ao usuário Zeus'; END IF;
    IF p_usuario_id IS NULL OR p_usuario_id<=0 OR p_empresa_id IS NULL OR p_empresa_id<=0 THEN
      RAISE EXCEPTION 'Informe empresa e ID válidos do APP CELULAR';
    END IF;
    IF chat.eh_master() THEN
      v_empresa := p_empresa_id;
    ELSE
      SELECT u.empresa_id INTO v_empresa FROM chat.usuario_auth u
        WHERE u.auth_uid=(SELECT auth.uid()) AND u.ativo AND u.eh_zeus LIMIT 1;
      IF v_empresa IS DISTINCT FROM p_empresa_id THEN RAISE EXCEPTION 'Empresa fora do escopo do Zeus'; END IF;
    END IF;
    SELECT s.nome INTO v_nome FROM chat.solicitacao_acesso s
      WHERE s.auth_uid=p_auth_uid AND s.empresa_id=v_empresa AND s.status='PENDENTE' FOR UPDATE;
    IF v_nome IS NULL THEN RAISE EXCEPTION 'Solicitação pendente não encontrada'; END IF;
    INSERT INTO chat.usuario_auth (auth_uid,empresa_id,usuario_id,nome,ativo)
      VALUES (p_auth_uid,v_empresa,p_usuario_id,v_nome,TRUE);
    UPDATE chat.solicitacao_acesso SET status='APROVADO',usuario_id=p_usuario_id,
      analisada_em=NOW(),analisada_por=(SELECT auth.uid()) WHERE auth_uid=p_auth_uid;
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
      analisada_por=(SELECT auth.uid()) WHERE auth_uid=p_auth_uid AND status='PENDENTE'
      AND (chat.eh_master() OR empresa_id=v_empresa);
    IF NOT FOUND THEN RAISE EXCEPTION 'Solicitação pendente não encontrada'; END IF;
END
$$;

DROP POLICY IF EXISTS usuario_auth_master_ler ON chat.usuario_auth;
CREATE POLICY usuario_auth_master_ler ON chat.usuario_auth FOR SELECT TO authenticated
  USING (chat.eh_master());
DROP POLICY IF EXISTS recibo_master_ler ON chat.recibo_mensagem;
CREATE POLICY recibo_master_ler ON chat.recibo_mensagem FOR SELECT TO authenticated
  USING (chat.eh_master());
DROP POLICY IF EXISTS acao_master_ler ON chat.acao_visual;
CREATE POLICY acao_master_ler ON chat.acao_visual FOR SELECT TO authenticated
  USING (chat.eh_master());
DROP POLICY IF EXISTS reacao_master_ler ON chat.reacao_mensagem;
CREATE POLICY reacao_master_ler ON chat.reacao_mensagem FOR SELECT TO authenticated
  USING (chat.eh_master());
DROP POLICY IF EXISTS apelido_master_ler ON chat.usuario_apelido;
CREATE POLICY apelido_master_ler ON chat.usuario_apelido FOR SELECT TO authenticated
  USING (chat.eh_master());
GRANT SELECT ON chat.usuario_auth,chat.recibo_mensagem,chat.acao_visual,
  chat.reacao_mensagem,chat.usuario_apelido TO authenticated;

REVOKE ALL ON FUNCTION chat.eh_master() FROM PUBLIC,anon;
GRANT EXECUTE ON FUNCTION chat.eh_master() TO authenticated;
REVOKE ALL ON FUNCTION chat.eh_zeus(),chat.obter_identidade(BIGINT),
  chat.pode_acessar_conversa(BIGINT,UUID),chat.listar_solicitacoes_acesso(),
  chat.aprovar_solicitacao_acesso(UUID,BIGINT,BIGINT),chat.recusar_solicitacao_acesso(UUID)
  FROM PUBLIC,anon;
GRANT EXECUTE ON FUNCTION chat.eh_zeus(),chat.obter_identidade(BIGINT),
  chat.pode_acessar_conversa(BIGINT,UUID),chat.listar_solicitacoes_acesso(),
  chat.aprovar_solicitacao_acesso(UUID,BIGINT,BIGINT),chat.recusar_solicitacao_acesso(UUID)
  TO authenticated;
