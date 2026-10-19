(() => {
    const state = { client: null, empresaId: 0, bucket: "chat-media", apiBaseUrl: "https://app-celular-api-test.onrender.com", masterApiToken: null, masterApiTokenExpiresAt: 0, channel: null, passwordRecovery: false };
    const api = {};
    const fail = (error) => { if (error) throw new Error(error.message || String(error)); };
    const row = (data) => Array.isArray(data) ? data[0] : data;
    const normalizeContact = (value) => ({
        id: value.id, nome: value.nome, nomeOriginal: value.nome_original ?? value.nome,
        ultimaMensagem: value.ultima_mensagem ?? "", dataUltimaMensagem: value.data_ultima_mensagem,
        naoLidas: value.nao_lidas ?? 0
    });
    const normalizeMessage = (value) => ({
        id: value.id, clienteMensagemId: value.cliente_mensagem_id,
        remetenteId: value.remetente_id, remetenteNome: value.remetente_nome ?? "",
        destinatarioId: value.destinatario_id, texto: value.texto ?? "",
        midiaChave: value.midia_chave, midiaNome: value.midia_nome,
        midiaTipo: value.midia_tipo, midiaTamanhoBytes: value.midia_tamanho_bytes,
        enviadaEm: value.enviada_em, lidaEm: value.lida_em, fotoUrl: null
    });
    const toSession = (authSession, identity) => {
        if (!authSession?.user || !identity) return null;
        return {
            empresaId: identity.empresa_id,
            usuarioId: identity.usuario_id,
            usuario: identity.nome,
            supabaseUserId: authSession.user.id,
            expiraEm: new Date((authSession.expires_at || 0) * 1000).toISOString()
        };
    };
    const getClient = () => {
        if (!state.client) throw new Error("O Supabase ainda não foi inicializado.");
        return state.client;
    };
    const getMasterApiToken = async () => {
        if (state.masterApiToken && Date.now() < state.masterApiTokenExpiresAt - 60000) return state.masterApiToken;
        const { data, error } = await getClient().auth.getSession();
        fail(error);
        const supabaseToken = data.session?.access_token;
        if (!supabaseToken) throw new Error("Entre novamente como Zeus para carregar os usuários.");
        const response = await fetch(`${state.apiBaseUrl}/api/administracao/master/sessao-supabase`, {
            method: "POST", headers: { Authorization: `Bearer ${supabaseToken}` }
        });
        const result = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(result.mensagem || "Não foi possível validar o acesso Zeus na API.");
        state.masterApiToken = result.token;
        state.masterApiTokenExpiresAt = result.expiraEm ? new Date(result.expiraEm).getTime() : Date.now() + 3600000;
        return state.masterApiToken;
    };
    const masterApiFetch = async path => {
        const token = await getMasterApiToken();
        const response = await fetch(`${state.apiBaseUrl}${path}`, { headers: { Authorization: `Bearer ${token}` } });
        const result = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(result.mensagem || `A API recusou a operação (${response.status}).`);
        return result;
    };
    const getIdentity = async () => {
        const client = getClient();
        const { data, error } = await client.schema("chat").rpc("obter_identidade", { p_empresa_id: state.empresaId });
        fail(error);
        const identity = row(data);
        if (!identity) {
            const { data: request, error: requestError } = await client.schema("chat").rpc("status_solicitacao_acesso");
            fail(requestError);
            const accessRequest = row(request);
            if (accessRequest?.status === "PENDENTE") throw new Error("Sua solicitação está aguardando aprovação de Zeus.");
            if (accessRequest?.status === "RECUSADO") throw new Error("Sua solicitação de acesso foi recusada. Fale com Zeus.");
            throw new Error("Esta conta ainda não foi vinculada a um usuário da empresa no Supabase.");
        }
        return identity;
    };

    api.configureFromPage = () => {
        const config = window.chatcatSupabaseConfig;
        if (!config) throw new Error("O serviço ainda não publicou a configuração pública do Supabase.");
        api.configure(config.url, config.publishableKey, config.bucket, config.empresaId);
    };

    api.configure = (url, publishableKey, bucket, empresaId) => {
        if (!window.supabase?.createClient) throw new Error("A biblioteca Supabase não carregou. Recarregue a página.");
        if (!url || !publishableKey || !empresaId) throw new Error("Configuração Supabase incompleta.");
        state.empresaId = Number(empresaId);
        state.bucket = bucket || "chat-media";
        if (!state.client) {
            state.passwordRecovery = new URLSearchParams(window.location.hash.slice(1)).get("type") === "recovery";
            state.client = window.supabase.createClient(url, publishableKey, {
                auth: { autoRefreshToken: true, persistSession: true, detectSessionInUrl: true, storageKey: "chatcat-supabase-auth" }
            });
            state.client.auth.onAuthStateChange(event => {
                if (event === "PASSWORD_RECOVERY") state.passwordRecovery = true;
            });
        }
    };

    api.listCompanyUsers = async () => {
        const users = await masterApiFetch("/api/administracao/acessos/usuarios");
        return (users || []).map(user => ({ id: user.id, empresaId: user.empresaId, empresa: user.empresa, nome: user.nome, funcao: user.funcao }))
            .filter(user => Number.isInteger(Number(user.id)) && Number(user.id) > 0);
    };

    api.restoreSession = async () => {
        const client = getClient();
        const { data, error } = await client.auth.getSession();
        fail(error);
        if (state.passwordRecovery) return null;
        if (!data.session) return null;
        try { return toSession(data.session, await getIdentity()); }
        catch (error) { await client.auth.signOut({ scope: "local" }); throw error; }
    };

    api.signIn = async (email, password) => {
        const client = getClient();
        const { data, error } = await client.auth.signInWithPassword({ email, password });
        fail(error);
        try { return toSession(data.session, await getIdentity()); }
        catch (error) { await client.auth.signOut({ scope: "local" }); throw error; }
    };

    api.isPasswordRecovery = async () => {
        const { data, error } = await getClient().auth.getSession();
        fail(error);
        return state.passwordRecovery && !!data.session;
    };

    api.requestPasswordRecovery = async email => {
        const { error } = await getClient().auth.resetPasswordForEmail(email.trim(), {
            redirectTo: `${window.location.origin}/redefinir-senha`
        });
        fail(error);
    };

    api.updatePassword = async password => {
        const client = getClient();
        const { error } = await client.auth.updateUser({ password });
        fail(error);
        state.passwordRecovery = false;
        await client.auth.signOut({ scope: "local" });
    };

    api.signUp = async (email, password, name) => {
        const { data, error } = await getClient().auth.signUp({
            email: email.trim(), password,
            options: { data: { nome: name.trim() }, emailRedirectTo: window.location.origin }
        });
        fail(error);
        // The database trigger records the pending request even when Supabase
        // requires email confirmation and therefore returns no active session.
        if (data.session) await getClient().auth.signOut({ scope: "local" });
        return { confirmationRequired: !data.session };
    };

    api.isZeus = async () => {
        const { data, error } = await getClient().schema("chat").rpc("eh_zeus");
        fail(error);
        return data === true;
    };

    api.listAccessRequests = async () => {
        const { data, error } = await getClient().schema("chat").rpc("listar_solicitacoes_acesso");
        fail(error);
        return (data || []).map(item => ({
            authUid: item.auth_uid, email: item.email, nome: item.nome,
            criadaEm: item.criada_em, status: item.status
        }));
    };

    api.approveAccessRequest = async (authUid, userId) => {
        const { error } = await getClient().schema("chat").rpc("aprovar_solicitacao_acesso", {
            p_auth_uid: authUid, p_usuario_id: userId
        });
        fail(error);
    };

    api.rejectAccessRequest = async (authUid) => {
        const { error } = await getClient().schema("chat").rpc("recusar_solicitacao_acesso", { p_auth_uid: authUid });
        fail(error);
    };

    api.listManagedUsers = async () => {
        const { data, error } = await getClient().schema("chat").rpc("listar_usuarios_gerenciaveis");
        fail(error);
        return (data || []).map(item => ({
            authUid: item.auth_uid, email: item.email, nome: item.nome,
            empresaId: item.empresa_id, usuarioId: item.usuario_id,
            ativo: item.ativo, master: item.master, criadoEm: item.criado_em
        }));
    };

    api.setManagedUserMaster = async (authUid, active) => {
        const { error } = await getClient().schema("chat").rpc("definir_usuario_master", {
            p_auth_uid: authUid, p_ativo: active
        });
        fail(error);
    };

    api.setManagedUserActive = async (authUid, active) => {
        const { error } = await getClient().schema("chat").rpc("definir_usuario_ativo", {
            p_auth_uid: authUid, p_ativo: active
        });
        fail(error);
    };

    api.inviteManagedUser = async (email, name, isMaster, empresaId, usuarioId) => {
        const { data, error } = await getClient().functions.invoke("master-user-invite", {
            body: { email, nome: name, master: isMaster, empresaId, usuarioId }
        });
        fail(error);
        return data;
    };

    api.signOut = async () => {
        try {
            const { error } = await getClient().auth.signOut({ scope: "local" });
            fail(error);
        } finally {
            localStorage.removeItem("chatcat-session");
            localStorage.removeItem("chatcat-supabase-auth");
        }
    };

    api.listContacts = async () => {
        const { data, error } = await getClient().schema("chat").rpc("listar_contatos", { p_empresa_id: state.empresaId });
        fail(error);
        return (data || []).map(normalizeContact);
    };

    api.listMessages = async (contactId, afterId) => {
        const { data, error } = await getClient().schema("chat").rpc("listar_mensagens", {
            p_contato_id: contactId, p_depois_do_id: afterId || 0, p_empresa_id: state.empresaId
        });
        fail(error);
        return (data || []).map(normalizeMessage);
    };

    api.uploadMessageMedia = async (contactId, clientMessageId, inputId, fileName, contentType) => {
        const client = getClient();
        const file = document.getElementById(inputId)?.files?.[0];
        if (!file) throw new Error("Selecione novamente a mídia para enviar.");
        const { data: conversation, error: conversationError } = await client.schema("chat").rpc("obter_conversa_direta", { p_contato_id: contactId });
        fail(conversationError);
        const extension = (fileName.split(".").pop() || "bin").toLowerCase().replace(/[^a-z0-9]/g, "").slice(0, 8) || "bin";
        const path = `${state.empresaId}/${row(conversation).conversa_id}/${clientMessageId}.${extension}`;
        const { error } = await client.storage.from(state.bucket).upload(path, file, {
            contentType: contentType || file.type || "application/octet-stream", upsert: false, cacheControl: "3600"
        });
        fail(error);
        return path;
    };

    api.createSignedMediaUrl = async (path, expiresIn) => {
        const { data, error } = await getClient().storage.from(state.bucket).createSignedUrl(path, expiresIn || 3600);
        fail(error);
        return data.signedUrl;
    };

    api.sendMessage = async (contactId, text, clientMessageId, mediaPath, mediaName, mediaType, mediaSize) => {
        const { data, error } = await getClient().schema("chat").rpc("enviar_mensagem", {
            p_contato_id: contactId, p_cliente_mensagem_id: clientMessageId,
            p_texto: text || "", p_midia_chave: mediaPath || null, p_midia_nome: mediaName || null,
            p_midia_tipo: mediaType || null, p_midia_tamanho_bytes: mediaSize || null
        });
        fail(error);
        return normalizeMessage(row(data));
    };

    api.markRead = async (contactId) => {
        const { error } = await getClient().schema("chat").rpc("marcar_como_lida", { p_contato_id: contactId });
        fail(error);
    };

    api.sendVisualAction = async (contactId, type, symbol) => {
        const { error } = await getClient().schema("chat").rpc("enviar_acao_visual", {
            p_contato_id: contactId, p_tipo: type, p_simbolo: symbol
        });
        fail(error);
    };

    api.getVisualActions = async (contactId) => {
        const { data, error } = await getClient().schema("chat").rpc("listar_acoes_visuais", { p_contato_id: contactId });
        fail(error);
        return data || [];
    };

    api.subscribeMessages = async (empresaId, dotNetReference) => {
        const client = getClient();
        if (state.channel) await client.removeChannel(state.channel);
        state.channel = client.channel(`chat-mensagens-${empresaId}-${crypto.randomUUID()}`)
            .on("postgres_changes", {
                event: "INSERT", schema: "chat", table: "mensagem", filter: `empresa_id=eq.${empresaId}`
            }, payload => dotNetReference.invokeMethodAsync("AoReceberMensagemSupabase", normalizeMessage(payload.new)))
            .subscribe();
    };

    api.unsubscribeMessages = async () => {
        if (state.channel) await getClient().removeChannel(state.channel);
        state.channel = null;
    };

    window.appCelular = window.appCelular || {};
    window.appCelular.supabase = api;
})();

