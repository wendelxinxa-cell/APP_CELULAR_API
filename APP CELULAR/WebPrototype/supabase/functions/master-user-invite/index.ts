import { createClient } from "https://esm.sh/@supabase/supabase-js@2";

const corsHeaders = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers": "authorization, x-client-info, apikey, content-type",
  "Access-Control-Allow-Methods": "POST, OPTIONS",
};

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), {
  status,
  headers: { ...corsHeaders, "Content-Type": "application/json" },
});

type SignupRequestRow = {
  id: number;
  email: string;
  nome: string;
  empresa_id: number;
  auth_uid: string | null;
  email_verificado_em: string | null;
};

Deno.serve(async (request: Request) => {
  if (request.method === "OPTIONS") return new Response("ok", { headers: corsHeaders });
  if (request.method !== "POST") return json({ message: "Método não permitido." }, 405);

  const authorization = request.headers.get("Authorization");

  const supabaseUrl = Deno.env.get("SUPABASE_URL");
  const anonKey = Deno.env.get("SUPABASE_ANON_KEY");
  const serviceRoleKey = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY");
  if (!supabaseUrl || !anonKey || !serviceRoleKey) return json({ message: "Função de convite sem configuração." }, 500);

  const caller = createClient(supabaseUrl, anonKey, {
    global: { headers: authorization ? { Authorization: authorization } : {} },
    auth: { persistSession: false, autoRefreshToken: false },
  });
  const admin = createClient(supabaseUrl, serviceRoleKey, {
    auth: { persistSession: false, autoRefreshToken: false },
  });

  try {
    const body = await request.json();
    const action = String(body.action ?? "invite");
    const email = String(body.email ?? "").trim().toLowerCase();
    const nome = String(body.nome ?? "").trim().slice(0, 120);

    if (action === "signIn") {
      const identificador = String(body.identificador ?? "").trim();
      const senha = String(body.senha ?? "");
      if (!identificador || !senha) return json({ message: "Informe apelido e senha." }, 400);
      const companyId = Number(body.empresaId);
      if (!Number.isInteger(companyId) || companyId <= 0) return json({ message: "Empresa não configurada para este acesso." }, 400);
      let matches: Array<{ email: string }> = [];
      if (identificador.includes("@")) {
        const { data: listed, error: listError } = await admin.auth.admin.listUsers({ page: 1, perPage: 1000 });
        if (listError) return json({ message: "Não foi possível localizar a conta. Tente novamente." }, 400);
        matches = (listed?.users || []).filter(user => user.email?.toLowerCase() === identificador.toLowerCase())
          .flatMap(user => user.email ? [{ email: user.email }] : []);
      } else {
        // Apelidos aprovados ficam no vínculo ativo da empresa, não necessariamente em user_metadata.
        const normalizedIdentifier = identificador.normalize("NFKC").trim().toLocaleLowerCase("pt-BR");
        const { data: linkedUsers, error: linkedError } = await admin.schema("chat").from("usuario_auth")
          .select("auth_uid,nome").eq("empresa_id", companyId).eq("ativo", true);
        if (linkedError) return json({ message: "Não foi possível localizar o apelido vinculado à empresa." }, 400);
        const authUids = (linkedUsers || [])
          .filter(user => String(user.nome ?? "").normalize("NFKC").trim().toLocaleLowerCase("pt-BR") === normalizedIdentifier)
          .map(user => user.auth_uid);
        for (const authUid of authUids) {
          const { data: authUser, error: authUserError } = await admin.auth.admin.getUserById(authUid);
          if (authUserError) return json({ message: "Não foi possível validar o e-mail associado ao apelido." }, 400);
          if (authUser.user?.email) matches.push({ email: authUser.user.email });
        }
        // Mantém compatibilidade com contas master antigas vinculadas apenas por metadados.
        if (matches.length === 0) {
          const { data: listed, error: listError } = await admin.auth.admin.listUsers({ page: 1, perPage: 1000 });
          if (listError) return json({ message: "Não foi possível localizar a conta. Tente entrar pelo e-mail." }, 400);
          matches = (listed?.users || []).filter(user =>
            String(user.user_metadata?.nome ?? "").normalize("NFKC").trim().toLocaleLowerCase("pt-BR") === normalizedIdentifier
            && (user.user_metadata?.empresa_id == null || Number(user.user_metadata.empresa_id) === companyId))
            .flatMap(user => user.email ? [{ email: user.email }] : []);
        }
      }
      matches = [...new Map(matches.map(user => [user.email.toLowerCase(), user])).values()];
      if (matches.length !== 1 || !matches[0].email) return json({ message: matches.length > 1 ? "Esse apelido está associado a mais de uma conta. Entre com seu e-mail." : "Apelido não encontrado. Confira o cadastro ou entre com o e-mail." }, 401);
      const { data: login, error: loginError } = await caller.auth.signInWithPassword({ email: matches[0].email, password: senha });
      if (loginError || !login.session) return json({ message: "Apelido ou senha incorretos." }, 401);
      return json({ session: { access_token: login.session.access_token, refresh_token: login.session.refresh_token } });
    }
    if (action === "requestSignup") {
      const empresaId = Number(body.empresaId);
      if (!email.includes("@") || email.length > 254 || !nome || !Number.isInteger(empresaId) || empresaId <= 0) return json({ message: "Informe apelido, e-mail e ID da empresa válidos." }, 400);
      const { data: existing, error: existingError } = await admin.auth.admin.listUsers({ page: 1, perPage: 1000 });
      if (existingError) return json({ message: `Não foi possível validar o e-mail: ${existingError.message}` }, 500);
      let signup: SignupRequestRow | null = null;
      const { data: pending, error: pendingError } = await admin.schema("chat").from("solicitacao_cadastro")
        .select("id,email,nome,empresa_id,auth_uid,email_verificado_em")
        .eq("email", email).eq("status", "PENDENTE").maybeSingle();
      if (pendingError) return json({ message: `Não foi possível consultar pedidos pendentes: ${pendingError.message}` }, 500);
      if (pending) {
        if (pending.nome.toLowerCase() !== nome.toLowerCase() || Number(pending.empresa_id) !== empresaId) {
          return json({ message: "Já existe uma solicitação pendente para este e-mail. Confira seus dados ou aguarde a análise." }, 409);
        }
        signup = pending;
      } else {
        const { data, error } = await admin.schema("chat").from("solicitacao_cadastro")
          .insert({ email, nome, empresa_id: empresaId }).select("id,email,nome,empresa_id,auth_uid,email_verificado_em").single();
        if (error?.code === "23505") return json({ message: "Já existe um pedido pendente para este e-mail." }, 409);
        if (error || !data) return json({ message: `Não foi possível registrar o pedido: ${error?.message ?? "erro desconhecido"}` }, 400);
        signup = data;
      }
      if (!signup) return json({ message: "Não foi possível preparar a solicitação." }, 500);

      let authUser = existing?.users?.find(user => user.email?.toLowerCase() === email);
      if (authUser && String(authUser.user_metadata?.pending_signup_request_id ?? "") !== String(signup.id)) {
        return json({ message: "Este e-mail já está cadastrado. Entre ou recupere a senha da conta existente." }, 409);
      }
      if (!authUser) {
        const randomBytes = new Uint8Array(32);
        crypto.getRandomValues(randomBytes);
        const temporaryPassword = Array.from(randomBytes, byte => byte.toString(16).padStart(2, "0")).join("") + "Aa1!";
        const created = await admin.auth.admin.createUser({
          email,
          password: temporaryPassword,
          email_confirm: false,
          user_metadata: { nome, empresa_id: empresaId, pending_signup_request_id: signup.id },
        });
        if (created.error || !created.data.user) {
          return json({ message: `Não foi possível preparar a confirmação do e-mail: ${created.error?.message ?? "erro desconhecido"}` }, 400);
        }
        authUser = created.data.user;
        const { error: linkError } = await admin.schema("chat").from("solicitacao_cadastro")
          .update({ auth_uid: authUser.id }).eq("id", signup.id).eq("status", "PENDENTE");
        if (linkError) return json({ message: `Solicitação criada, mas não foi possível vinculá-la à confirmação: ${linkError.message}` }, 500);
      }
      if (signup.auth_uid !== authUser.id) {
        const { error: linkError } = await admin.schema("chat").from("solicitacao_cadastro")
          .update({ auth_uid: authUser.id }).eq("id", signup.id).eq("status", "PENDENTE");
        if (linkError) return json({ message: `Solicitação criada, mas não foi possível vinculá-la à confirmação: ${linkError.message}` }, 500);
      }

      if (authUser.email_confirmed_at || signup.email_verificado_em) {
        if (!signup.email_verificado_em) {
          await admin.schema("chat").from("solicitacao_cadastro").update({ email_verificado_em: authUser.email_confirmed_at })
            .eq("id", signup.id).eq("status", "PENDENTE");
        }
        return json({ ok: true, emailJaVerificado: true });
      }

      const appUrl = (Deno.env.get("APP_PUBLIC_URL") || "https://catchat-razor-test.onrender.com").replace(/\/$/, "");
      const publicAuth = createClient(supabaseUrl, anonKey, { auth: { persistSession: false, autoRefreshToken: false } });
      const { error: resendError } = await publicAuth.auth.resend({
        type: "signup",
        email,
        options: { emailRedirectTo: `${appUrl}/redefinir-senha?confirmarEmail=1` },
      });
      if (resendError) return json({ message: `Pedido registrado, mas não foi possível enviar a confirmação: ${resendError.message}` }, 502);
      return json({ ok: true, emailJaVerificado: false });
    }

    if (!authorization?.startsWith("Bearer ")) return json({ message: "Sessão necessária para esta operação." }, 401);
    const { data: authResult, error: authError } = await caller.auth.getUser();
    if (authError || !authResult.user) return json({ message: "Sessão inválida." }, 401);

    if (action === "confirmSignupEmail") {
      const user = authResult.user;
      if (!user.email_confirmed_at || !user.email) return json({ message: "O Supabase ainda não confirmou este e-mail. Abra o link recebido e tente novamente." }, 400);
      const { data: request, error: requestError } = await admin.schema("chat").from("solicitacao_cadastro")
        .select("id,email,auth_uid,status").eq("auth_uid", user.id).eq("status", "PENDENTE").maybeSingle();
      if (requestError || !request || request.email.toLowerCase() !== user.email.toLowerCase()) {
        return json({ message: "Não encontrei uma solicitação pendente associada a este link." }, 404);
      }
      const { error } = await admin.schema("chat").from("solicitacao_cadastro")
        .update({ email_verificado_em: user.email_confirmed_at }).eq("id", request.id).eq("status", "PENDENTE");
      if (error) return json({ message: `E-mail confirmado, mas não foi possível atualizar o pedido: ${error.message}` }, 500);
      return json({ ok: true });
    }

    const { data: isZeus, error: roleError } = await caller.schema("chat").rpc("eh_zeus");
    if (roleError) return json({ message: "Não foi possível validar a função Zeus." }, 403);
    if (isZeus !== true) return json({ message: "Apenas Zeus pode convidar usuários." }, 403);

    if (action === "listSignupRequests") {
      const { data, error } = await admin.schema("chat").from("solicitacao_cadastro").select("id,email,nome,empresa_id,criada_em,email_verificado_em").eq("status", "PENDENTE").not("email_verificado_em", "is", null).order("criada_em");
      if (error) return json({ message: `Não foi possível listar pedidos: ${error.message}` }, 400);
      return json({ requests: (data || []).map(row => ({ id: row.id, email: row.email, nome: row.nome, empresaId: row.empresa_id, criadaEm: row.criada_em })) });
    }
    if (action === "rejectSignupRequest") {
      const { error } = await admin.schema("chat").rpc("recusar_cadastro_pendente", { p_request_id: Number(body.requestId), p_admin_uid: authResult.user.id });
      if (error) return json({ message: error.message }, 400);
      return json({ ok: true });
    }
    if (action === "approveSignupRequest") {
      const requestId = Number(body.requestId), selectedUserId = Number(body.usuarioId);
      if (!Number.isInteger(requestId) || requestId <= 0 || !Number.isInteger(selectedUserId) || selectedUserId <= 0) return json({ message: "Selecione um usuário CATCHAT válido." }, 400);
      const { data: signup, error: requestError } = await admin.schema("chat").from("solicitacao_cadastro").select("id,email,nome,empresa_id,auth_uid,email_verificado_em").eq("id", requestId).eq("status", "PENDENTE").not("email_verificado_em", "is", null).single();
      if (requestError || !signup) return json({ message: "Pedido pendente não encontrado." }, 404);
      if (!signup.auth_uid) return json({ message: "O pedido confirmado não tem conta de autenticação vinculada. Solicite novo cadastro." }, 409);
      const { data: existingUser, error: userError } = await admin.auth.admin.getUserById(signup.auth_uid);
      const invited = existingUser?.user;
      if (userError || !invited || invited.email?.toLowerCase() !== signup.email.toLowerCase() || !invited.email_confirmed_at) {
        return json({ message: "A conta Auth não confirmou este e-mail. Peça ao usuário para reenviar a confirmação." }, 409);
      }
      const appUrl = (Deno.env.get("APP_PUBLIC_URL") || "https://catchat-razor-test.onrender.com").replace(/\/$/, "");
      const publicAuth = createClient(supabaseUrl, anonKey, { auth: { persistSession: false, autoRefreshToken: false } });
      const { error: emailError } = await publicAuth.auth.resetPasswordForEmail(signup.email, { redirectTo: `${appUrl}/redefinir-senha` });
      if (emailError) return json({ message: `Não foi possível enviar o e-mail para criar a senha: ${emailError.message}` }, 502);
      const { error: linkError } = await admin.schema("chat").rpc("aprovar_cadastro_pendente", { p_request_id: requestId, p_auth_uid: invited.id, p_usuario_id: selectedUserId, p_admin_uid: authResult.user.id });
      if (linkError) return json({ message: `E-mail para criar a senha enviado, mas o vínculo falhou: ${linkError.message}. O pedido continua pendente.`, authUid: invited.id }, 500);
      return json({ ok: true, email: signup.email, authUid: invited.id });
    }
    const master = body.master === true;
    const empresaId = Number(body.empresaId ?? 1);
    const usuarioId = body.usuarioId == null ? null : Number(body.usuarioId);
    if (!email.includes("@") || email.length > 254 || nome.length === 0) {
      return json({ message: "Informe um nome e um e-mail válidos." }, 400);
    }
    if (!master && (!Number.isInteger(empresaId) || empresaId <= 0 || !Number.isInteger(usuarioId) || usuarioId! <= 0)) {
      return json({ message: "Selecione um usuário CATCHAT existente." }, 400);
    }

    const { data: invite, error: inviteError } = await admin.auth.admin.inviteUserByEmail(email, {
      data: { nome, empresa_id: master ? null : empresaId },
      ...(Deno.env.get("APP_PUBLIC_URL") ? { redirectTo: `${Deno.env.get("APP_PUBLIC_URL")!.replace(/\/$/, "")}/redefinir-senha` } : {}),
    });
    if (inviteError || !invite.user) {
      return json({ message: inviteError?.message ?? "O Supabase não criou o convite." }, 400);
    }

    if (master) {
      const { error } = await caller.schema("chat").rpc("definir_usuario_master", {
        p_auth_uid: invite.user.id, p_ativo: true,
      });
      if (error) {
        return json({ message: `Convite enviado, mas não foi possível ativar Zeus master: ${error.message}`, authUid: invite.user.id }, 500);
      }
    } else {
      const { error } = await caller.schema("chat").rpc("aprovar_solicitacao_acesso", {
        p_auth_uid: invite.user.id,
        p_usuario_id: usuarioId,
        p_empresa_id: empresaId,
      });
      if (error) {
        return json({ message: `Convite enviado, mas o vínculo CATCHAT não foi concluído: ${error.message}`, authUid: invite.user.id }, 500);
      }
    }

    return json({ email, authUid: invite.user.id, master });
  } catch (error) {
    return json({ message: error instanceof Error ? error.message : "Falha ao enviar o convite." }, 500);
  }
});
