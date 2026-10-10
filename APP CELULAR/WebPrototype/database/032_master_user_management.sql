-- Gestão de usuários pelo painel Zeus do CATCHAT.
-- Aplicar depois das migrações 020 e 030.

CREATE OR REPLACE FUNCTION chat.listar_usuarios_gerenciaveis()
RETURNS TABLE
(
    auth_uid UUID,
    email TEXT,
    nome TEXT,
    empresa_id BIGINT,
    usuario_id BIGINT,
    ativo BOOLEAN,
    master BOOLEAN,
    criado_em TIMESTAMPTZ
)
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = '' AS $$
BEGIN
    IF NOT chat.eh_zeus() THEN RAISE EXCEPTION 'Acesso restrito ao usuário Zeus'; END IF;
    RETURN QUERY
    SELECT au.id,
           COALESCE(au.email, ''),
           COALESCE(NULLIF(ua.nome, ''), NULLIF(um.nome, ''), NULLIF(au.raw_user_meta_data->>'nome', ''), 'Usuário'),
           COALESCE(ua.empresa_id, sa.empresa_id),
           ua.usuario_id,
           CASE WHEN um.ativo THEN TRUE ELSE COALESCE(ua.ativo, sa.status = 'PENDENTE', FALSE) END,
           COALESCE(um.ativo, FALSE),
           au.created_at
    FROM auth.users au
    LEFT JOIN chat.usuario_auth ua ON ua.auth_uid = au.id
    LEFT JOIN chat.usuario_master um ON um.auth_uid = au.id
    LEFT JOIN chat.solicitacao_acesso sa ON sa.auth_uid = au.id
    WHERE ua.auth_uid IS NOT NULL OR um.auth_uid IS NOT NULL OR sa.auth_uid IS NOT NULL
    ORDER BY au.created_at DESC;
END
$$;

CREATE OR REPLACE FUNCTION chat.definir_usuario_master(p_auth_uid UUID, p_ativo BOOLEAN)
RETURNS VOID
LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $$
DECLARE v_nome TEXT;
BEGIN
    IF NOT chat.eh_zeus() THEN RAISE EXCEPTION 'Acesso restrito ao usuário Zeus'; END IF;
    IF p_auth_uid IS NULL OR p_ativo IS NULL THEN RAISE EXCEPTION 'Informe conta e estado válidos'; END IF;
    IF p_auth_uid = (SELECT auth.uid()) AND NOT p_ativo THEN
      RAISE EXCEPTION 'Não é permitido remover o próprio acesso master';
    END IF;

    SELECT COALESCE(NULLIF(raw_user_meta_data->>'nome', ''), 'Zeus') INTO v_nome
      FROM auth.users WHERE id = p_auth_uid;
    IF NOT FOUND THEN RAISE EXCEPTION 'Conta Auth não encontrada neste projeto'; END IF;

    IF p_ativo THEN
      INSERT INTO chat.usuario_master (auth_uid, nome, ativo)
      VALUES (p_auth_uid, v_nome, TRUE)
      ON CONFLICT (auth_uid) DO UPDATE SET nome = EXCLUDED.nome, ativo = TRUE;
    ELSE
      UPDATE chat.usuario_master SET ativo = FALSE WHERE auth_uid = p_auth_uid;
    END IF;
END
$$;

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
    IF NOT FOUND THEN RAISE EXCEPTION 'Usuário vinculado não encontrado'; END IF;
END
$$;

REVOKE ALL ON FUNCTION chat.listar_usuarios_gerenciaveis(),
  chat.definir_usuario_master(UUID, BOOLEAN), chat.definir_usuario_ativo(UUID, BOOLEAN)
  FROM PUBLIC, anon;
GRANT EXECUTE ON FUNCTION chat.listar_usuarios_gerenciaveis(),
  chat.definir_usuario_master(UUID, BOOLEAN), chat.definir_usuario_ativo(UUID, BOOLEAN)
  TO authenticated;
