-- Cadastro de teste: usuário escolhe sua senha; Zeus define empresa e tipo ao aprovar.
ALTER TABLE chat.solicitacao_cadastro ALTER COLUMN empresa_id DROP NOT NULL;
ALTER TABLE chat.solicitacao_cadastro ADD COLUMN IF NOT EXISTS tipo_negocio TEXT NULL;

CREATE OR REPLACE FUNCTION chat.aprovar_cadastro_pendente(
    p_request_id BIGINT,
    p_auth_uid UUID,
    p_usuario_id BIGINT,
    p_admin_uid UUID,
    p_empresa_id BIGINT,
    p_tipo_negocio TEXT
)
RETURNS VOID LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $$
DECLARE v_req chat.solicitacao_cadastro%ROWTYPE;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM chat.usuario_auth WHERE auth_uid=p_admin_uid AND ativo AND eh_zeus) THEN
        RAISE EXCEPTION 'Acesso restrito ao usuário Zeus';
    END IF;
    IF p_usuario_id IS NULL OR p_usuario_id <= 0 OR p_empresa_id IS NULL OR p_empresa_id <= 0 THEN
        RAISE EXCEPTION 'Selecione uma empresa e um usuário CATCHAT';
    END IF;
    IF p_tipo_negocio IS NULL OR p_tipo_negocio NOT IN ('chat','comercial') THEN
        RAISE EXCEPTION 'Tipo de negócio inválido';
    END IF;
    SELECT * INTO v_req FROM chat.solicitacao_cadastro WHERE id=p_request_id AND status='PENDENTE' FOR UPDATE;
    IF NOT FOUND THEN RAISE EXCEPTION 'Pedido pendente não encontrado'; END IF;
    IF v_req.email_verificado_em IS NULL OR v_req.auth_uid IS DISTINCT FROM p_auth_uid THEN
        RAISE EXCEPTION 'O solicitante ainda não confirmou o e-mail';
    END IF;
    INSERT INTO chat.usuario_auth (auth_uid,empresa_id,usuario_id,nome,email,tipo_negocio,ativo)
        VALUES (p_auth_uid,p_empresa_id,p_usuario_id,v_req.nome,v_req.email,p_tipo_negocio,TRUE);
    UPDATE chat.solicitacao_cadastro SET status='APROVADO',auth_uid=p_auth_uid,usuario_id=p_usuario_id,
        empresa_id=p_empresa_id,tipo_negocio=p_tipo_negocio,
        analisada_em=NOW(),analisada_por=p_admin_uid WHERE id=p_request_id;
END $$;

REVOKE ALL ON FUNCTION chat.aprovar_cadastro_pendente(BIGINT,UUID,BIGINT,UUID,BIGINT,TEXT)
    FROM PUBLIC,anon,authenticated;
GRANT EXECUTE ON FUNCTION chat.aprovar_cadastro_pendente(BIGINT,UUID,BIGINT,UUID,BIGINT,TEXT)
    TO service_role;

NOTIFY pgrst, 'reload schema';
