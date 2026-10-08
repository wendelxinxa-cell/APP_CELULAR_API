-- Migração aditiva para clientes APP CELULAR com Supabase Auth/RLS diretos.
-- Pré-requisito: um único projeto Supabase central contém o schema chat e os
-- dados de conversa. NÃO execute em produção antes de provisionar os usuários
-- Auth, preencher chat.usuario_auth e validar as políticas no projeto de teste.

CREATE TABLE IF NOT EXISTS chat.usuario_auth
(
    auth_uid UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    empresa_id BIGINT NOT NULL CHECK (empresa_id > 0),
    usuario_id BIGINT NOT NULL CHECK (usuario_id > 0),
    nome TEXT NOT NULL,
    ativo BOOLEAN NOT NULL DEFAULT TRUE,
    criado_em TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (auth_uid),
    UNIQUE (empresa_id, usuario_id)
);

ALTER TABLE chat.mensagem ADD COLUMN IF NOT EXISTS destinatario_id BIGINT NULL;
ALTER TABLE chat.mensagem
    ADD COLUMN IF NOT EXISTS midia_chave TEXT NULL,
    ADD COLUMN IF NOT EXISTS midia_nome TEXT NULL,
    ADD COLUMN IF NOT EXISTS midia_tipo TEXT NULL,
    ADD COLUMN IF NOT EXISTS midia_tamanho_bytes BIGINT NULL;

ALTER TABLE chat.mensagem DROP CONSTRAINT IF EXISTS ck_chat_mensagem_conteudo;
ALTER TABLE chat.mensagem ADD CONSTRAINT ck_chat_mensagem_conteudo
    CHECK (length(texto) > 0 OR foto IS NOT NULL OR midia_chave IS NOT NULL) NOT VALID;

CREATE OR REPLACE FUNCTION chat.definir_destinatario_mensagem()
RETURNS TRIGGER LANGUAGE plpgsql SET search_path = '' AS $$
BEGIN
    IF NEW.destinatario_id IS NULL THEN
        SELECT p.usuario_id INTO NEW.destinatario_id FROM chat.participante p
        WHERE p.empresa_id=NEW.empresa_id AND p.conversa_id=NEW.conversa_id
          AND p.usuario_id<>NEW.remetente_id AND p.saiu_em IS NULL
        ORDER BY p.entrou_em LIMIT 1;
    END IF;
    RETURN NEW;
END
$$;
DROP TRIGGER IF EXISTS tr_chat_destinatario_mensagem ON chat.mensagem;
CREATE TRIGGER tr_chat_destinatario_mensagem BEFORE INSERT ON chat.mensagem
    FOR EACH ROW EXECUTE FUNCTION chat.definir_destinatario_mensagem();

CREATE TABLE IF NOT EXISTS chat.acao_visual
(
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    empresa_id BIGINT NOT NULL,
    remetente_id BIGINT NOT NULL,
    destinatario_id BIGINT NOT NULL,
    tipo TEXT NOT NULL CHECK (tipo IN ('WINK','ATTENTION')),
    simbolo TEXT NOT NULL,
    criada_em TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    lida_em TIMESTAMPTZ NULL
);
CREATE INDEX IF NOT EXISTS ix_chat_acao_visual_destino
    ON chat.acao_visual (empresa_id, destinatario_id, remetente_id, id);

CREATE OR REPLACE FUNCTION chat.obter_identidade(p_empresa_id BIGINT)
RETURNS TABLE (empresa_id BIGINT, usuario_id BIGINT, nome TEXT)
LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $$
    SELECT u.empresa_id, u.usuario_id, u.nome
    FROM chat.usuario_auth AS u
    WHERE u.auth_uid = (SELECT auth.uid()) AND u.empresa_id = p_empresa_id AND u.ativo
$$;

CREATE OR REPLACE FUNCTION chat.pode_acessar_conversa(p_empresa_id BIGINT, p_conversa_id UUID)
RETURNS BOOLEAN LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $$
    SELECT EXISTS (
        SELECT 1 FROM chat.usuario_auth AS u
        JOIN chat.participante AS p ON p.empresa_id = u.empresa_id AND p.usuario_id = u.usuario_id
        WHERE u.auth_uid = (SELECT auth.uid()) AND u.ativo AND u.empresa_id = p_empresa_id
          AND p.conversa_id = p_conversa_id AND p.saiu_em IS NULL
    )
$$;

CREATE OR REPLACE FUNCTION chat.caminho_midia_autorizado(p_caminho TEXT)
RETURNS BOOLEAN LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = '' AS $$
DECLARE v_empresa BIGINT; v_conversa UUID;
BEGIN
    v_empresa := split_part(p_caminho, '/', 1)::BIGINT;
    v_conversa := split_part(p_caminho, '/', 2)::UUID;
    IF array_length(string_to_array(p_caminho, '/'), 1) <> 3 OR split_part(p_caminho, '/', 3) = '' THEN
        RETURN FALSE;
    END IF;
    RETURN chat.pode_acessar_conversa(v_empresa, v_conversa);
EXCEPTION WHEN OTHERS THEN RETURN FALSE;
END
$$;

CREATE OR REPLACE FUNCTION chat.obter_conversa_direta(p_contato_id BIGINT)
RETURNS TABLE (conversa_id UUID)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $$
DECLARE v_empresa BIGINT; v_usuario BIGINT; v_conversa UUID; v_menor BIGINT; v_maior BIGINT;
BEGIN
    SELECT u.empresa_id, u.usuario_id INTO v_empresa, v_usuario FROM chat.usuario_auth u
      WHERE u.auth_uid=(SELECT auth.uid()) AND u.ativo ORDER BY u.empresa_id LIMIT 1;
    IF v_usuario IS NULL OR p_contato_id = v_usuario OR NOT EXISTS (
        SELECT 1 FROM chat.usuario_auth u WHERE u.empresa_id=v_empresa AND u.usuario_id=p_contato_id AND u.ativo
    ) THEN RAISE EXCEPTION 'Contato indisponível nesta empresa'; END IF;
    v_menor := LEAST(v_usuario, p_contato_id); v_maior := GREATEST(v_usuario, p_contato_id);
    INSERT INTO chat.conversa (empresa_id,tipo,usuario_menor_id,usuario_maior_id,criada_por_usuario_id)
      VALUES (v_empresa,'DIRETA',v_menor,v_maior,v_usuario)
      ON CONFLICT (empresa_id,usuario_menor_id,usuario_maior_id) WHERE tipo='DIRETA' DO NOTHING;
    SELECT c.id INTO v_conversa FROM chat.conversa c
      WHERE c.empresa_id=v_empresa AND c.tipo='DIRETA' AND c.usuario_menor_id=v_menor AND c.usuario_maior_id=v_maior;
    INSERT INTO chat.participante (empresa_id,conversa_id,usuario_id)
      VALUES (v_empresa,v_conversa,v_menor),(v_empresa,v_conversa,v_maior) ON CONFLICT DO NOTHING;
    RETURN QUERY SELECT v_conversa;
END
$$;

CREATE OR REPLACE FUNCTION chat.listar_contatos()
RETURNS TABLE (id BIGINT, nome TEXT, nome_original TEXT, ultima_mensagem TEXT,
               data_ultima_mensagem TIMESTAMPTZ, nao_lidas BIGINT)
LANGUAGE sql STABLE SECURITY DEFINER SET search_path = '' AS $$
    WITH eu AS (
        SELECT empresa_id, usuario_id FROM chat.usuario_auth
        WHERE auth_uid=(SELECT auth.uid()) AND ativo
    )
    SELECT contato.usuario_id, contato.nome, contato.nome,
           COALESCE(ultima.texto, CASE WHEN ultima.midia_chave IS NOT NULL THEN '[Mídia]' ELSE '' END),
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
    ORDER BY ultima.enviada_em DESC NULLS LAST, contato.nome
$$;

CREATE OR REPLACE FUNCTION chat.listar_mensagens(p_contato_id BIGINT, p_depois_do_id BIGINT DEFAULT 0)
RETURNS TABLE (id BIGINT, cliente_mensagem_id UUID, remetente_id BIGINT, remetente_nome TEXT,
               destinatario_id BIGINT, texto TEXT, midia_chave TEXT, midia_nome TEXT,
               midia_tipo TEXT, midia_tamanho_bytes BIGINT, enviada_em TIMESTAMPTZ, lida_em TIMESTAMPTZ)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $$
DECLARE v_empresa BIGINT; v_usuario BIGINT; v_conversa UUID;
BEGIN
    SELECT x.empresa_id,x.usuario_id INTO v_empresa,v_usuario FROM chat.usuario_auth x
      WHERE x.auth_uid=(SELECT auth.uid()) AND x.ativo ORDER BY x.empresa_id LIMIT 1;
    SELECT c.conversa_id INTO v_conversa FROM chat.obter_conversa_direta(p_contato_id) c;
    RETURN QUERY SELECT m.id,m.cliente_mensagem_id,m.remetente_id,m.remetente_nome,
        CASE WHEN m.remetente_id=v_usuario THEN p_contato_id ELSE v_usuario END,
        m.texto,m.midia_chave,m.midia_nome,m.midia_tipo,m.midia_tamanho_bytes,m.enviada_em,r.lida_em
      FROM chat.mensagem m LEFT JOIN chat.recibo_mensagem r
        ON r.empresa_id=m.empresa_id AND r.mensagem_id=m.id AND r.usuario_id=v_usuario
      WHERE m.empresa_id=v_empresa AND m.conversa_id=v_conversa AND m.id>p_depois_do_id AND m.excluida_em IS NULL
      ORDER BY m.id;
END
$$;

CREATE OR REPLACE FUNCTION chat.enviar_mensagem(p_contato_id BIGINT, p_cliente_mensagem_id UUID,
    p_texto TEXT, p_midia_chave TEXT DEFAULT NULL, p_midia_nome TEXT DEFAULT NULL,
    p_midia_tipo TEXT DEFAULT NULL, p_midia_tamanho_bytes BIGINT DEFAULT NULL)
RETURNS TABLE (id BIGINT, cliente_mensagem_id UUID, remetente_id BIGINT, remetente_nome TEXT,
    destinatario_id BIGINT, texto TEXT, midia_chave TEXT, midia_nome TEXT, midia_tipo TEXT,
    midia_tamanho_bytes BIGINT, enviada_em TIMESTAMPTZ, lida_em TIMESTAMPTZ)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $$
DECLARE v_empresa BIGINT; v_usuario BIGINT; v_nome TEXT; v_conversa UUID; v_id BIGINT;
BEGIN
    SELECT empresa_id,usuario_id,nome INTO v_empresa,v_usuario,v_nome FROM chat.usuario_auth
      WHERE auth_uid=(SELECT auth.uid()) AND ativo ORDER BY empresa_id LIMIT 1;
    SELECT c.conversa_id INTO v_conversa FROM chat.obter_conversa_direta(p_contato_id) c;
    IF length(trim(COALESCE(p_texto,'')))=0 AND p_midia_chave IS NULL THEN RAISE EXCEPTION 'Mensagem vazia'; END IF;
    IF p_midia_chave IS NOT NULL AND p_midia_chave NOT LIKE v_empresa::TEXT || '/' || v_conversa::TEXT || '/%' THEN
        RAISE EXCEPTION 'Caminho de mídia inválido';
    END IF;
    INSERT INTO chat.mensagem (empresa_id,conversa_id,remetente_id,destinatario_id,cliente_mensagem_id,
        remetente_nome,destinatario_nome,texto,midia_chave,midia_nome,midia_tipo,midia_tamanho_bytes)
      VALUES (v_empresa,v_conversa,v_usuario,p_contato_id,p_cliente_mensagem_id,v_nome,'',COALESCE(p_texto,''),
        p_midia_chave,p_midia_nome,p_midia_tipo,p_midia_tamanho_bytes)
      ON CONFLICT (empresa_id,remetente_id,cliente_mensagem_id) WHERE cliente_mensagem_id IS NOT NULL DO NOTHING
      RETURNING id INTO v_id;
    IF v_id IS NULL THEN SELECT m.id INTO v_id FROM chat.mensagem m
      WHERE m.empresa_id=v_empresa AND m.remetente_id=v_usuario AND m.cliente_mensagem_id=p_cliente_mensagem_id; END IF;
    RETURN QUERY SELECT m.id,m.cliente_mensagem_id,m.remetente_id,m.remetente_nome,m.destinatario_id,
      m.texto,m.midia_chave,m.midia_nome,m.midia_tipo,m.midia_tamanho_bytes,m.enviada_em,r.lida_em
      FROM chat.mensagem m LEFT JOIN chat.recibo_mensagem r ON r.empresa_id=m.empresa_id
        AND r.mensagem_id=m.id AND r.usuario_id=v_usuario WHERE m.id=v_id;
END
$$;

CREATE OR REPLACE FUNCTION chat.marcar_como_lida(p_contato_id BIGINT)
RETURNS VOID LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $$
DECLARE v_empresa BIGINT; v_usuario BIGINT; v_conversa UUID; v_agora TIMESTAMPTZ := NOW();
BEGIN
    SELECT x.empresa_id,x.usuario_id INTO v_empresa,v_usuario FROM chat.usuario_auth x
      WHERE x.auth_uid=(SELECT auth.uid()) AND x.ativo ORDER BY x.empresa_id LIMIT 1;
    SELECT c.conversa_id INTO v_conversa FROM chat.obter_conversa_direta(p_contato_id) c;
    UPDATE chat.participante SET ultima_leitura_em=v_agora
      WHERE empresa_id=v_empresa AND conversa_id=v_conversa AND usuario_id=v_usuario;
    INSERT INTO chat.recibo_mensagem (empresa_id,mensagem_id,usuario_id,entregue_em,lida_em)
      SELECT v_empresa,m.id,v_usuario,v_agora,v_agora FROM chat.mensagem m
      WHERE m.empresa_id=v_empresa AND m.conversa_id=v_conversa AND m.remetente_id<>v_usuario AND m.excluida_em IS NULL
      ON CONFLICT (empresa_id,mensagem_id,usuario_id) DO UPDATE SET lida_em=EXCLUDED.lida_em,entregue_em=COALESCE(chat.recibo_mensagem.entregue_em,EXCLUDED.entregue_em);
END
$$;

CREATE OR REPLACE FUNCTION chat.enviar_acao_visual(p_contato_id BIGINT,p_tipo TEXT,p_simbolo TEXT)
RETURNS VOID LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $$
DECLARE v_empresa BIGINT; v_usuario BIGINT;
BEGIN
    SELECT empresa_id,usuario_id INTO v_empresa,v_usuario FROM chat.usuario_auth
      WHERE auth_uid=(SELECT auth.uid()) AND ativo ORDER BY empresa_id LIMIT 1;
    IF p_tipo NOT IN ('WINK','ATTENTION') OR NOT EXISTS (
      SELECT 1 FROM chat.usuario_auth WHERE empresa_id=v_empresa AND usuario_id=p_contato_id AND ativo
    ) THEN RAISE EXCEPTION 'Ação ou contato inválido'; END IF;
    INSERT INTO chat.acao_visual (empresa_id,remetente_id,destinatario_id,tipo,simbolo)
      VALUES (v_empresa,v_usuario,p_contato_id,p_tipo,left(p_simbolo,16));
END
$$;

CREATE OR REPLACE FUNCTION chat.listar_acoes_visuais(p_contato_id BIGINT)
RETURNS TABLE (tipo TEXT,simbolo TEXT)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $$
DECLARE v_empresa BIGINT; v_usuario BIGINT;
BEGIN
    SELECT empresa_id,usuario_id INTO v_empresa,v_usuario FROM chat.usuario_auth
      WHERE auth_uid=(SELECT auth.uid()) AND ativo ORDER BY empresa_id LIMIT 1;
    RETURN QUERY UPDATE chat.acao_visual a SET lida_em=NOW()
      WHERE a.empresa_id=v_empresa AND a.remetente_id=p_contato_id AND a.destinatario_id=v_usuario AND a.lida_em IS NULL
      RETURNING a.tipo,a.simbolo;
END
$$;

-- O app não consulta os dados de identidade diretamente; a identidade vem da RPC.
ALTER TABLE chat.usuario_auth ENABLE ROW LEVEL SECURITY;
CREATE POLICY usuario_auth_proprio ON chat.usuario_auth FOR SELECT TO authenticated USING (auth_uid=(SELECT auth.uid()));
REVOKE ALL ON SCHEMA chat FROM PUBLIC, anon, authenticated;
GRANT USAGE ON SCHEMA chat TO authenticated;
REVOKE ALL ON ALL TABLES IN SCHEMA chat FROM PUBLIC, anon, authenticated;
REVOKE ALL ON ALL SEQUENCES IN SCHEMA chat FROM PUBLIC, anon, authenticated;
GRANT SELECT ON chat.mensagem,chat.conversa,chat.participante TO authenticated;

ALTER TABLE chat.conversa ENABLE ROW LEVEL SECURITY;
ALTER TABLE chat.participante ENABLE ROW LEVEL SECURITY;
ALTER TABLE chat.mensagem ENABLE ROW LEVEL SECURITY;
ALTER TABLE chat.recibo_mensagem ENABLE ROW LEVEL SECURITY;
ALTER TABLE chat.reacao_mensagem ENABLE ROW LEVEL SECURITY;
ALTER TABLE chat.acao_visual ENABLE ROW LEVEL SECURITY;
ALTER TABLE chat.usuario_apelido ENABLE ROW LEVEL SECURITY;

CREATE POLICY conversa_participante_le ON chat.conversa FOR SELECT TO authenticated
  USING (chat.pode_acessar_conversa(empresa_id,id));
CREATE POLICY participante_proprio_le ON chat.participante FOR SELECT TO authenticated
  USING (chat.pode_acessar_conversa(empresa_id,conversa_id));
CREATE POLICY mensagem_participante_le ON chat.mensagem FOR SELECT TO authenticated
  USING (chat.pode_acessar_conversa(empresa_id,conversa_id));
CREATE POLICY recibo_proprio_le ON chat.recibo_mensagem FOR SELECT TO authenticated
  USING (usuario_id IN (SELECT u.usuario_id FROM chat.usuario_auth u WHERE u.auth_uid=(SELECT auth.uid()) AND u.ativo AND u.empresa_id=recibo_mensagem.empresa_id));
CREATE POLICY acao_participante_le ON chat.acao_visual FOR SELECT TO authenticated
  USING (destinatario_id IN (SELECT u.usuario_id FROM chat.usuario_auth u WHERE u.auth_uid=(SELECT auth.uid()) AND u.ativo AND u.empresa_id=acao_visual.empresa_id));

GRANT EXECUTE ON FUNCTION chat.obter_identidade(BIGINT), chat.obter_conversa_direta(BIGINT), chat.listar_contatos(),
  chat.listar_mensagens(BIGINT,BIGINT), chat.enviar_mensagem(BIGINT,UUID,TEXT,TEXT,TEXT,TEXT,BIGINT),
  chat.marcar_como_lida(BIGINT), chat.enviar_acao_visual(BIGINT,TEXT,TEXT), chat.listar_acoes_visuais(BIGINT)
  TO authenticated;
GRANT EXECUTE ON FUNCTION chat.pode_acessar_conversa(BIGINT,UUID), chat.caminho_midia_autorizado(TEXT)
  TO authenticated;
REVOKE ALL ON FUNCTION chat.obter_identidade(BIGINT), chat.obter_conversa_direta(BIGINT), chat.listar_contatos(),
  chat.listar_mensagens(BIGINT,BIGINT), chat.enviar_mensagem(BIGINT,UUID,TEXT,TEXT,TEXT,TEXT,BIGINT),
  chat.marcar_como_lida(BIGINT), chat.enviar_acao_visual(BIGINT,TEXT,TEXT), chat.listar_acoes_visuais(BIGINT),
  chat.pode_acessar_conversa(BIGINT,UUID), chat.caminho_midia_autorizado(TEXT)
  FROM PUBLIC, anon;
GRANT EXECUTE ON FUNCTION chat.obter_identidade(BIGINT), chat.obter_conversa_direta(BIGINT), chat.listar_contatos(),
  chat.listar_mensagens(BIGINT,BIGINT), chat.enviar_mensagem(BIGINT,UUID,TEXT,TEXT,TEXT,TEXT,BIGINT),
  chat.marcar_como_lida(BIGINT), chat.enviar_acao_visual(BIGINT,TEXT,TEXT), chat.listar_acoes_visuais(BIGINT),
  chat.pode_acessar_conversa(BIGINT,UUID), chat.caminho_midia_autorizado(TEXT)
  TO authenticated;

INSERT INTO storage.buckets (id,name,public,file_size_limit,allowed_mime_types)
VALUES ('chat-media','chat-media',FALSE,104857600,ARRAY['image/jpeg','image/png','image/gif','image/webp','image/heic','video/mp4','video/quicktime','video/webm'])
ON CONFLICT (id) DO UPDATE SET public=FALSE,file_size_limit=EXCLUDED.file_size_limit,allowed_mime_types=EXCLUDED.allowed_mime_types;

CREATE POLICY chat_media_ler_participante ON storage.objects FOR SELECT TO authenticated
  USING (bucket_id='chat-media' AND chat.caminho_midia_autorizado(name));
CREATE POLICY chat_media_enviar_participante ON storage.objects FOR INSERT TO authenticated
  WITH CHECK (bucket_id='chat-media' AND chat.caminho_midia_autorizado(name));

DO $publication$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_publication_tables
        WHERE pubname='supabase_realtime' AND schemaname='chat' AND tablename='mensagem'
    ) THEN
        ALTER PUBLICATION supabase_realtime ADD TABLE chat.mensagem;
    END IF;
END
$publication$;

-- No Supabase Dashboard > API, exponha o schema chat ao PostgREST.
