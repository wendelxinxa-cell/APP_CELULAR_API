-- Lets Zeus reactivate an unlinked Auth account by reopening its access request.
-- Linked accounts continue to be controlled through chat.usuario_auth.

CREATE OR REPLACE FUNCTION chat.definir_usuario_ativo(p_auth_uid UUID, p_ativo BOOLEAN)
RETURNS VOID
LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $$
BEGIN
    IF NOT chat.eh_zeus() THEN RAISE EXCEPTION 'Acesso restrito ao usuário Zeus'; END IF;
    IF p_auth_uid IS NULL OR p_ativo IS NULL THEN RAISE EXCEPTION 'Informe conta e estado válidos'; END IF;
    IF EXISTS (SELECT 1 FROM chat.usuario_master WHERE auth_uid = p_auth_uid AND ativo) THEN
        RAISE EXCEPTION 'Use a ação específica para contas master';
    END IF;

    UPDATE chat.usuario_auth SET ativo = p_ativo WHERE auth_uid = p_auth_uid;
    IF FOUND THEN RETURN; END IF;

    UPDATE chat.solicitacao_acesso
       SET status = CASE WHEN p_ativo THEN 'PENDENTE' ELSE 'RECUSADO' END,
           analisada_em = CASE WHEN p_ativo THEN NULL ELSE NOW() END,
           analisada_por = CASE WHEN p_ativo THEN NULL ELSE (SELECT auth.uid()) END
     WHERE auth_uid = p_auth_uid;
    IF NOT FOUND THEN RAISE EXCEPTION 'Solicitação de acesso não encontrada para esta conta'; END IF;
END
$$;

REVOKE ALL ON FUNCTION chat.definir_usuario_ativo(UUID, BOOLEAN) FROM PUBLIC, anon;
GRANT EXECUTE ON FUNCTION chat.definir_usuario_ativo(UUID, BOOLEAN) TO authenticated;

NOTIFY pgrst, 'reload schema';
