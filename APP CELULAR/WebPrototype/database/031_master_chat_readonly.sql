-- Permite ao Zeus master inspecionar as conversas existentes da empresa selecionada.
-- Zeus global é somente leitura no chat; os demais usuários preservam o fluxo atual.

DROP FUNCTION IF EXISTS chat.listar_contatos();
CREATE OR REPLACE FUNCTION chat.listar_contatos(p_empresa_id BIGINT DEFAULT 1)
RETURNS TABLE (id BIGINT,nome TEXT,nome_original TEXT,ultima_mensagem TEXT,
  data_ultima_mensagem TIMESTAMPTZ,nao_lidas BIGINT)
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = '' AS $$
BEGIN
  IF chat.eh_master() THEN
    RETURN QUERY
    SELECT contato.usuario_id,contato.nome,contato.nome,
      COALESCE(ultima.texto,CASE WHEN ultima.midia_chave IS NOT NULL THEN '[Mídia]' ELSE '' END),
      ultima.enviada_em,0::BIGINT
    FROM chat.usuario_auth contato
    JOIN LATERAL (
      SELECT m.texto,m.midia_chave,m.enviada_em
      FROM chat.conversa cv JOIN chat.mensagem m
        ON m.empresa_id=cv.empresa_id AND m.conversa_id=cv.id
      WHERE cv.empresa_id=p_empresa_id AND cv.tipo='DIRETA'
        AND (cv.usuario_menor_id=contato.usuario_id OR cv.usuario_maior_id=contato.usuario_id)
        AND m.excluida_em IS NULL
      ORDER BY m.enviada_em DESC,m.id DESC LIMIT 1
    ) ultima ON TRUE
    WHERE contato.empresa_id=p_empresa_id AND contato.ativo
    ORDER BY ultima.enviada_em DESC NULLS LAST,contato.nome;
  ELSE
    RETURN QUERY
    WITH eu AS (
      SELECT u.empresa_id,u.usuario_id FROM chat.usuario_auth u
      WHERE u.auth_uid=(SELECT auth.uid()) AND u.ativo AND u.empresa_id=p_empresa_id
    )
    SELECT contato.usuario_id,contato.nome,contato.nome,
      COALESCE(ultima.texto,CASE WHEN ultima.midia_chave IS NOT NULL THEN '[Mídia]' ELSE '' END),
      ultima.enviada_em,
      COALESCE((SELECT count(*) FROM chat.mensagem m
        WHERE m.empresa_id=eu.empresa_id AND m.conversa_id=cv.id AND m.remetente_id<>eu.usuario_id
          AND m.excluida_em IS NULL AND m.enviada_em>COALESCE(leitura.ultima_leitura_em,'-infinity'::timestamptz)),0)
    FROM eu
    JOIN chat.usuario_auth contato ON contato.empresa_id=eu.empresa_id AND contato.usuario_id<>eu.usuario_id AND contato.ativo
    LEFT JOIN chat.conversa cv ON cv.empresa_id=eu.empresa_id AND cv.tipo='DIRETA'
      AND cv.usuario_menor_id=LEAST(eu.usuario_id,contato.usuario_id)
      AND cv.usuario_maior_id=GREATEST(eu.usuario_id,contato.usuario_id)
    LEFT JOIN chat.participante leitura ON leitura.empresa_id=cv.empresa_id AND leitura.conversa_id=cv.id AND leitura.usuario_id=eu.usuario_id
    LEFT JOIN LATERAL (SELECT m.texto,m.midia_chave,m.enviada_em FROM chat.mensagem m
      WHERE m.empresa_id=cv.empresa_id AND m.conversa_id=cv.id AND m.excluida_em IS NULL
      ORDER BY m.enviada_em DESC,m.id DESC LIMIT 1) ultima ON TRUE
    ORDER BY ultima.enviada_em DESC NULLS LAST,contato.nome;
  END IF;
END
$$;

DROP FUNCTION IF EXISTS chat.listar_mensagens(BIGINT,BIGINT);
CREATE OR REPLACE FUNCTION chat.listar_mensagens(
  p_contato_id BIGINT,p_depois_do_id BIGINT DEFAULT 0,p_empresa_id BIGINT DEFAULT 1)
RETURNS TABLE (id BIGINT,cliente_mensagem_id UUID,remetente_id BIGINT,remetente_nome TEXT,
  destinatario_id BIGINT,texto TEXT,midia_chave TEXT,midia_nome TEXT,midia_tipo TEXT,
  midia_tamanho_bytes BIGINT,enviada_em TIMESTAMPTZ,lida_em TIMESTAMPTZ)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $$
DECLARE v_empresa BIGINT; v_usuario BIGINT; v_conversa UUID;
BEGIN
  IF chat.eh_master() THEN
    RETURN QUERY SELECT m.id,m.cliente_mensagem_id,m.remetente_id,m.remetente_nome,
      m.destinatario_id,m.texto,m.midia_chave,m.midia_nome,m.midia_tipo,
      m.midia_tamanho_bytes,m.enviada_em,r.lida_em
    FROM chat.mensagem m JOIN chat.conversa cv
      ON cv.empresa_id=m.empresa_id AND cv.id=m.conversa_id
    LEFT JOIN chat.recibo_mensagem r ON r.empresa_id=m.empresa_id AND r.mensagem_id=m.id
      AND r.usuario_id=0
    WHERE m.empresa_id=p_empresa_id AND cv.tipo='DIRETA'
      AND (cv.usuario_menor_id=p_contato_id OR cv.usuario_maior_id=p_contato_id)
      AND m.id>p_depois_do_id AND m.excluida_em IS NULL
    ORDER BY m.enviada_em,m.id;
    RETURN;
  END IF;
  SELECT x.empresa_id,x.usuario_id INTO v_empresa,v_usuario FROM chat.usuario_auth x
    WHERE x.auth_uid=(SELECT auth.uid()) AND x.ativo AND x.empresa_id=p_empresa_id;
  SELECT c.conversa_id INTO v_conversa FROM chat.obter_conversa_direta(p_contato_id) c;
  RETURN QUERY SELECT m.id,m.cliente_mensagem_id,m.remetente_id,m.remetente_nome,
    CASE WHEN m.remetente_id=v_usuario THEN p_contato_id ELSE v_usuario END,
    m.texto,m.midia_chave,m.midia_nome,m.midia_tipo,m.midia_tamanho_bytes,m.enviada_em,r.lida_em
  FROM chat.mensagem m LEFT JOIN chat.recibo_mensagem r
    ON r.empresa_id=m.empresa_id AND r.mensagem_id=m.id AND r.usuario_id=v_usuario
  WHERE m.empresa_id=v_empresa AND m.conversa_id=v_conversa
    AND m.id>p_depois_do_id AND m.excluida_em IS NULL ORDER BY m.id;
END
$$;

REVOKE ALL ON FUNCTION chat.listar_contatos(BIGINT),chat.listar_mensagens(BIGINT,BIGINT,BIGINT) FROM PUBLIC,anon;
GRANT EXECUTE ON FUNCTION chat.listar_contatos(BIGINT),chat.listar_mensagens(BIGINT,BIGINT,BIGINT) TO authenticated;
