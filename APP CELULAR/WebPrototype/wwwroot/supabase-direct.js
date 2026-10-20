(() => {
    const state = { client: null, empresaId: 0, bucket: "chat-media", apiBaseUrl: "", masterApiToken: null, masterApiTokenExpiresAt: 0, channel: null, passwordRecovery: false };
    const api = {};
    const fail = (error) => { if (error) throw new Error(error.message || String(error)); };
    const row = (data) => Array.isArray(data) ? data[0] : data;
    const normalizeContact = (value) => ({
        id: value.id, nome: value.nome, nomeOriginal: value.nome_original ?? value.nomeOriginal ?? value.nome,
        ultimaMensagem: value.ultima_mensagem ?? value.ultimaMensagem ?? "", dataUltimaMensagem: value.data_ultima_mensagem ?? value.dataUltimaMensagem,
        naoLidas: value.nao_lidas ?? value.naoLidas ?? 0
    });
    const normalizeMessage = (value) => ({
        id: value.id ?? value.Id, clienteMensagemId: value.cliente_mensagem_id ?? value.clienteMensagemId ?? value.ClienteMensagemId,
        remetenteId: value.remetente_id ?? value.remetenteId ?? value.RemetenteId, remetenteNome: value.remetente_nome ?? value.remetenteNome ?? value.RemetenteNome ?? "",
        destinatarioId: value.destinatario_id ?? value.destinatarioId ?? value.DestinatarioId, texto: value.texto ?? value.Texto ?? "",
        midiaChave: value.midia_chave ?? value.midiaChave ?? value.MidiaChave, midiaNome: value.midia_nome ?? value.midiaNome ?? value.MidiaNome,
        midiaTipo: value.midia_tipo ?? value.midiaTipo ?? value.MidiaTipo, midiaTamanhoBytes: value.midia_tamanho_bytes ?? value.midiaTamanhoBytes ?? value.MidiaTamanhoBytes,
        foto: value.foto ?? value.Foto, fotoNome: value.foto_nome ?? value.fotoNome ?? value.FotoNome,
        fotoTipo: value.foto_tipo ?? value.fotoTipo ?? value.FotoTipo,
        enviadaEm: value.enviada_em ?? value.enviadaEm ?? value.EnviadaEm, lidaEm: value.lida_em ?? value.lidaEm ?? value.LidaEm, fotoUrl: null
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
        api.configure(config.url, config.publishableKey, config.bucket, config.empresaId, config.apiBaseUrl);
    };

    api.configure = (url, publishableKey, bucket, empresaId, apiBaseUrl) => {
        if (!window.supabase?.createClient) throw new Error("A biblioteca Supabase não carregou. Recarregue a página.");
        if (!url || !publishableKey || !empresaId) throw new Error("Configuração Supabase incompleta.");
        state.empresaId = Number(empresaId);
        state.bucket = bucket || "chat-media";
        state.apiBaseUrl = (apiBaseUrl || "https://app-celular-api-test.onrender.com").replace(/\/$/, "");
        if (!state.client) {
            const linkType = new URLSearchParams(window.location.hash.slice(1)).get("type");
            state.passwordRecovery = linkType === "recovery" || linkType === "invite";
            state.client = window.supabase.createClient(url, publishableKey, {
                auth: { autoRefreshToken: true, persistSession: true, detectSessionInUrl: true, storageKey: "chatcat-supabase-auth" }
            });
            state.client.auth.onAuthStateChange(event => {
                if (event === "PASSWORD_RECOVERY" || (event === "SIGNED_IN" && new URLSearchParams(window.location.hash.slice(1)).get("type") === "invite")) state.passwordRecovery = true;
            });
        }
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
        let data;
        if (email.includes("@")) {
            const { data: result, error } = await client.auth.signInWithPassword({ email, password });
            fail(error);
            data = result;
        } else {
            const result = await invokeMasterInvite({ action: "signIn", identificador: email.trim(), senha: password, empresaId: state.empresaId });
            if (!result.session?.access_token || !result.session?.refresh_token) throw new Error("Não foi possível iniciar a sessão com esse nome.");
            const { data: sessionData, error } = await client.auth.setSession({ access_token: result.session.access_token, refresh_token: result.session.refresh_token });
            fail(error);
            data = sessionData;
        }
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

    const invokeMasterInvite = async body => {
        const { data, error } = await getClient().functions.invoke("master-user-invite", { body });
        if (error) {
            let detail = error.message || "Falha na função de cadastro.";
            try { const response = error.context; if (response?.json) { const payload = await response.json(); if (payload?.message) detail = payload.message; } } catch { }
            throw new Error(detail);
        }
        if (data?.message && !data.ok) throw new Error(data.message);
        return data;
    };

    api.requestSignup = async (email, name, password) => await invokeMasterInvite({ action: "requestSignup", email, nome: name, senha: password });
    api.confirmSignupEmail = async () => {
        const client = getClient();
        const { data, error } = await client.auth.getSession();
        fail(error);
        if (!data.session) throw new Error("O link de confirmação expirou ou já foi usado. Solicite outro e-mail de confirmação.");
        await invokeMasterInvite({ action: "confirmSignupEmail" });
        await client.auth.signOut({ scope: "local" });
        return true;
    };
    api.listSignupRequests = async () => (await invokeMasterInvite({ action: "listSignupRequests" })).requests || [];
    api.approveSignupRequest = async (requestId, userId, companyId, businessType) => { await invokeMasterInvite({ action: "approveSignupRequest", requestId, usuarioId: userId, empresaId: companyId, tipoNegocio: businessType }); };
    api.rejectSignupRequest = async requestId => { await invokeMasterInvite({ action: "rejectSignupRequest", requestId }); };

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
            criadaEm: item.criada_em, status: item.status, empresaId: item.empresa_id,
            dispositivoId: item.dispositivo_id, nomeDispositivo: item.nome_dispositivo
        }));
    };

    const getMasterApiToken = async () => {
        if (state.masterApiToken && Date.now() < state.masterApiTokenExpiresAt - 60000) return state.masterApiToken;
        const { data, error } = await getClient().auth.getSession();
        fail(error);
        const supabaseToken = data.session?.access_token;
        if (!supabaseToken) throw new Error("Entre novamente como Zeus para carregar as aprovações.");
        const response = await fetch(`${state.apiBaseUrl}/api/administracao/master/sessao-supabase`, {
            method: "POST", headers: { Authorization: `Bearer ${supabaseToken}` }
        });
        const result = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(result.mensagem || "Não foi possível validar o acesso Zeus na API.");
        state.masterApiToken = result.token;
        state.masterApiTokenExpiresAt = result.expiraEm ? new Date(result.expiraEm).getTime() : Date.now() + 3600000;
        return state.masterApiToken;
    };

    const masterApiFetch = async (path, method = "GET", body = null) => {
        const token = await getMasterApiToken();
        const headers = { Authorization: `Bearer ${token}` };
        const options = { method, headers };
        if (body !== null) {
            headers["Content-Type"] = "application/json";
            options.body = JSON.stringify(body);
        }
        const response = await fetch(`${state.apiBaseUrl}${path}`, options);
        const result = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(result.mensagem || `A API recusou a operação (${response.status}).`);
        return result;
    };

    api.deleteConversationForZeus = async (empresaId, usuarioId, contatoId) => {
        const path = `/api/administracao/chat/empresas/${encodeURIComponent(empresaId)}/usuarios/${encodeURIComponent(usuarioId)}/contatos/${encodeURIComponent(contatoId)}`;
        return await masterApiFetch(path, "DELETE");
    };

    api.listPendingUserDevices = async () => {
        const data = await masterApiFetch("/api/administracao/acessos/pendentes");
        const pendingUsers = new Map((data.usuarios || []).map(user => [`${user.empresaId}:${user.id}`, user]));
        const rows = (data.dispositivos || []).filter(device => device.usuarioId != null).map(device => {
            const user = pendingUsers.get(`${device.empresaId}:${device.usuarioId}`);
            return {
                empresaId: device.empresaId, empresa: device.empresa, dispositivoId: device.id,
                usuarioId: device.usuarioId, usuarioPendenteId: user?.id ?? null,
                usuario: device.usuario, dispositivo: device.nome,
                tipo: device.descricao === "Novo aparelho" ? "Novo vínculo pendente" : "Troca de aparelho pendente",
                criadoEm: device.criadoEm
            };
        });
        const pairedUsers = new Set(rows.filter(row => row.usuarioPendenteId != null).map(row => `${row.empresaId}:${row.usuarioPendenteId}`));
        for (const user of (data.usuarios || [])) {
            if (pairedUsers.has(`${user.empresaId}:${user.id}`)) continue;
            rows.push({ empresaId: user.empresaId, empresa: user.empresa, usuarioPendenteId: user.id,
                usuarioId: user.id, usuario: user.nome, dispositivo: "Aparelho ainda não solicitado",
                tipo: "Usuário pendente · aguardando aparelho", criadoEm: user.criadoEm, semDispositivo: true });
        }
        return rows.sort((a, b) => new Date(a.criadoEm || 0) - new Date(b.criadoEm || 0));
    };

    api.listCompanyUsers = async () => {
        const users = await masterApiFetch("/api/administracao/acessos/usuarios");
        return (users || []).map(user => ({ id: user.id, empresaId: user.empresaId, empresa: user.empresa, nome: user.nome, username: user.username || "", funcao: user.funcao, email: user.email || "", aprovado: !!user.aprovado, bloqueado: !!user.bloqueado, aparelhoPendente: !!user.aparelhoPendente, tipoNegocio: user.tipoNegocio || "comercial" }))
            .filter(user => Number.isInteger(Number(user.id)) && Number(user.id) > 0);
    };

    api.updateCompanyUser = async user => {
        await masterApiFetch(`/api/administracao/acessos/empresas/${user.empresaId}/usuarios/${user.id}/identidade`, "PUT", {
            username: user.username, email: user.email, nome: user.nome, tipoNegocio: user.tipoNegocio
        });
    };

    api.setCompanyUserActive = async (user, active) => {
        const result = await masterApiFetch(`/api/administracao/acessos/empresas/${user.empresaId}/usuarios/${user.id}/bloqueio`, "POST", { bloqueado: !active });
        if (Boolean(result.bloqueado) === active) throw new Error(result.mensagem || "A API não confirmou o novo estado de acesso do usuário.");
    };

    api.approvePendingUserDevice = async request => {
        if (request.usuarioPendenteId != null) {
            await masterApiFetch(`/api/administracao/acessos/empresas/${request.empresaId}/usuarios/${request.usuarioPendenteId}/aprovar`, "POST");
        }
        if (request.dispositivoId != null) {
            await masterApiFetch(`/api/administracao/acessos/empresas/${request.empresaId}/dispositivos/${request.dispositivoId}/aprovar`, "POST");
        }
    };

    api.approveAccessRequest = async request => {
        const { error } = await getClient().schema("chat").rpc("aprovar_solicitacao_acesso", {
            p_auth_uid: request.authUid, p_empresa_id: request.empresaId, p_tipo_negocio: request.tipoNegocio || "comercial"
        });
        fail(error);
    };

    api.rejectAccessRequest = async (authUid) => {
        const { error } = await getClient().schema("chat").rpc("recusar_solicitacao_acesso", { p_auth_uid: authUid });
        fail(error);
    };

    api.listManagedUsers = async () => {
        const { data, error } = await getClient().schema("chat").rpc("listar_usuarios_gerenciaveis");
        if (error?.code === "PGRST202" || /Could not find the function chat\.listar_usuarios_gerenciaveis/i.test(error?.message || "")) {
            throw new Error("A gestão de usuários ainda não foi habilitada neste Supabase. Aplique a migração database/032_master_user_management.sql e tente novamente.");
        }
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

    api.setCompanyUserBusinessType = async (companyId, userId, businessType) => {
        await masterApiFetch(`/api/administracao/acessos/empresas/${encodeURIComponent(companyId)}/usuarios/${encodeURIComponent(userId)}/tipo-negocio`, "PUT", { tipoNegocio: businessType });
    };

    api.inviteManagedUser = async (email, name, isMaster, empresaId, usuarioId) => {
        return await invokeMasterInvite({ email, nome: name, master: isMaster, empresaId, usuarioId });
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
        const { data: sessionData, error: sessionError } = await getClient().auth.getSession();
        fail(sessionError);
        const accessToken = sessionData.session?.access_token;
        if (!accessToken) throw new Error("Sua sessão expirou. Entre novamente.");
        const response = await fetch("/api/chat/contacts", {
            method: "POST",
            headers: { "Content-Type": "application/json", Authorization: `Bearer ${accessToken}` },
            body: JSON.stringify({ empresaId: state.empresaId })
        });
        const result = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(result.mensagem || `Não foi possível carregar os contatos (${response.status}).`);
        // O APK mostra conversas com mensagens já registradas. Mantém a mesma regra no Razor.
        return (result || []).filter(contact => contact.dataUltimaMensagem ?? contact.data_ultima_mensagem).map(normalizeContact);
    };

    api.listMessages = async (contactId, afterId) => {
        const { data: sessionData, error: sessionError } = await getClient().auth.getSession();
        fail(sessionError);
        const accessToken = sessionData.session?.access_token;
        if (!accessToken) throw new Error("Sua sessão expirou. Entre novamente.");
        const response = await fetch(`/api/chat/messages?contatoId=${encodeURIComponent(contactId)}&depoisDoId=${encodeURIComponent(afterId || 0)}`, {
            method: "POST",
            headers: { "Content-Type": "application/json", Authorization: `Bearer ${accessToken}` },
            body: JSON.stringify({ empresaId: state.empresaId })
        });
        const result = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(result.mensagem || `Não foi possível carregar as mensagens (${response.status}).`);
        return (result || []).map(normalizeMessage);
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
        if (!mediaPath) {
            const { data: sessionData, error: sessionError } = await getClient().auth.getSession();
            fail(sessionError);
            const accessToken = sessionData.session?.access_token;
            if (!accessToken) throw new Error("Sua sessão expirou. Entre novamente.");
            const response = await fetch("/api/chat/send", {
                method: "POST",
                headers: { "Content-Type": "application/json", Authorization: `Bearer ${accessToken}` },
                body: JSON.stringify({ empresaId: state.empresaId, contatoId: contactId, clienteMensagemId: clientMessageId, texto: text || "" })
            });
            const result = await response.json().catch(() => ({}));
            if (!response.ok) throw new Error(result.mensagem || `Não foi possível enviar a mensagem (${response.status}).`);
            return normalizeMessage(result);
        }
        const { data, error } = await getClient().schema("chat").rpc("enviar_mensagem", {
            p_contato_id: contactId, p_cliente_mensagem_id: clientMessageId,
            p_texto: text || "", p_midia_chave: mediaPath || null, p_midia_nome: mediaName || null,
            p_midia_tipo: mediaType || null, p_midia_tamanho_bytes: mediaSize || null
        });
        fail(error);
        return normalizeMessage(row(data));
    };

    api.markRead = async (contactId) => {
        const { data: sessionData, error: sessionError } = await getClient().auth.getSession();
        fail(sessionError);
        const accessToken = sessionData.session?.access_token;
        if (!accessToken) throw new Error("Sua sessão expirou. Entre novamente.");
        const response = await fetch("/api/chat/read", {
            method: "POST", headers: { "Content-Type": "application/json", Authorization: `Bearer ${accessToken}` },
            body: JSON.stringify({ empresaId: state.empresaId, contatoId })
        });
        const result = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(result.mensagem || `Não foi possível atualizar a leitura (${response.status}).`);
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

