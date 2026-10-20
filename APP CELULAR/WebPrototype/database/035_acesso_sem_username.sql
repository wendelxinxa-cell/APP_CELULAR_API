-- Corrige o fluxo legado de solicitação: nome é nome de exibição, não username.
-- Mantém username legado nullable para compatibilidade, mas não o exige nem valida.

BEGIN;

CREATE OR REPLACE FUNCTION chat.registrar_solicitacao_acesso()
RETURNS TRIGGER
LANGUAGE plpgsql SECURITY DEFINER SET search_path = '' AS $$
DECLARE
    v_empresa BIGINT;
    v_nome TEXT;
    v_chave UUID;
    v_chave_metadata TEXT;
BEGIN
    v_empresa := COALESCE(NULLIF(NEW.raw_user_meta_data->>'empresa_id','')::BIGINT, 1);
    v_nome := left(COALESCE(NULLIF(trim(NEW.raw_user_meta_data->>'nome'),''), NULLIF(split_part(COALESCE(NEW.email,''),'@',1),''), 'Usuário'), 120);
    v_chave_metadata := NEW.raw_user_meta_data->>'chave_instalacao';
    v_chave := CASE WHEN v_chave_metadata ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
                    THEN v_chave_metadata::UUID ELSE gen_random_uuid() END;

    INSERT INTO chat.solicitacao_acesso (auth_uid,email,nome,username,empresa_id,status)
    VALUES (NEW.id,COALESCE(NEW.email,''),v_nome,NULL,v_empresa,'PENDENTE')
    ON CONFLICT (auth_uid) DO UPDATE SET
        email=EXCLUDED.email, nome=EXCLUDED.nome, empresa_id=EXCLUDED.empresa_id;

    INSERT INTO chat.dispositivo_solicitacao (auth_uid,empresa_id,chave_instalacao,nome_dispositivo)
    VALUES (NEW.id,v_empresa,v_chave,COALESCE(NULLIF(NEW.raw_user_meta_data->>'nome_dispositivo',''),'Navegador web'))
    ON CONFLICT (empresa_id,chave_instalacao) DO NOTHING;
    RETURN NEW;
END
$$;

DROP TRIGGER IF EXISTS tr_chat_registrar_solicitacao_acesso ON auth.users;
CREATE TRIGGER tr_chat_registrar_solicitacao_acesso AFTER INSERT ON auth.users
    FOR EACH ROW EXECUTE FUNCTION chat.registrar_solicitacao_acesso();

COMMIT;
