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
      const { data: listed, error: listError } = await admin.auth.admin.listUsers({ page: 1, perPage: 1000 });
      if (listError) return json({ message: "Não foi possível localizar a conta. Tente entrar pelo e-mail." }, 400);
      const companyId = Number(body.empresaId);
      const matches = (listed?.users || []).filter(user => identificador.includes("@")
        ? user.email?.toLowerCase() === identificador.toLowerCase()
        : String(user.user_metadata?.nome ?? "").trim().toLowerCase() === identificador.toLowerCase()
          && (user.user_metadata?.empresa_id == null || Number(user.user_metadata.empresa_id) === companyId));
      if (matches.length !== 1 || !matches[0].email) return json({ message: matches.length > 1 ? "Esse apelido está associado a mais de uma conta. Entre com seu e-mail." : "Apelido não encontrado. Confira o cadastro ou entre com o e-mail." }, 401);
      const { data: login, error: loginError } = await caller.auth.signInWithPassword({ email: matches[0].email, password: senha });
      if (loginError || !login.session) return json({ message: "Apelido ou senha incorretos." }, 401);
      return json({ session: { access_token: login.session.access_token, refresh_token: login.session.refresh_token } });
    }
    if (action === "requestSignup") {
      const empresaId = Number(body.empresaId);
      if (!email.includes("@") || email.length > 254 || !nome || !Number.isInteger(empresaId) || empresaId <= 0) return json({ message: "Informe apelido, e-mail e ID da empresa válidos." }, 400);
      const { data: existing } = await admin.auth.admin.listUsers({ page: 1, perPage: 1000 });
      if (existing?.users?.some(user => user.email?.toLowerCase() === email)) return json({ message: "Este e-mail já possui uma conta. Use a opção de entrar ou recuperar senha." }, 409);
      const { error } = await admin.schema("chat").from("solicitacao_cadastro").insert({ email, nome, empresa_id: empresaId });
      if (error?.code === "23505") return json({ message: "Já existe um pedido pendente para este e-mail." }, 409);
      if (error) return json({ message: `Não foi possível registrar o pedido: ${error.message}` }, 400);
      return json({ ok: true });
    }

    if (!authorization?.startsWith("Bearer ")) return json({ message: "Sessão necessária para esta operação." }, 401);
    const { data: authResult, error: authError } = await caller.auth.getUser();
    if (authError || !authResult.user) return json({ message: "Sessão inválida." }, 401);

    const { data: isZeus, error: roleError } = await caller.schema("chat").rpc("eh_zeus");
    if (roleError) return json({ message: "Não foi possível validar a função Zeus." }, 403);
    if (isZeus !== true) return json({ message: "Apenas Zeus pode convidar usuários." }, 403);

    if (action === "listSignupRequests") {
      const { data, error } = await admin.schema("chat").from("solicitacao_cadastro").select("id,email,nome,empresa_id,criada_em").eq("status", "PENDENTE").order("criada_em");
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
      const { data: signup, error: requestError } = await admin.schema("chat").from("solicitacao_cadastro").select("id,email,nome,empresa_id").eq("id", requestId).eq("status", "PENDENTE").single();
      if (requestError || !signup) return json({ message: "Pedido pendente não encontrado." }, 404);
      const { data: listedUsers } = await admin.auth.admin.listUsers({ page: 1, perPage: 1000 });
      let invited = listedUsers?.users?.find(user => user.email?.toLowerCase() === signup.email.toLowerCase() && String(user.user_metadata?.pending_signup_request_id ?? "") === String(requestId));
      if (!invited && listedUsers?.users?.some(user => user.email?.toLowerCase() === signup.email.toLowerCase())) return json({ message: "Este e-mail já tem uma conta Auth que não pertence a este pedido. Confira a conta antes de aprovar." }, 409);
      if (!invited) {
        const redirectTo = Deno.env.get("APP_PUBLIC_URL");
        const invite = await admin.auth.admin.inviteUserByEmail(signup.email, { data: { nome: signup.nome, empresa_id: signup.empresa_id, pending_signup_request_id: requestId }, ...(redirectTo ? { redirectTo: `${redirectTo.replace(/\/$/, "")}/redefinir-senha` } : {}) });
        if (invite.error || !invite.data.user) return json({ message: invite.error?.message ?? "O Supabase não criou o convite." }, 400);
        invited = invite.data.user;
      }
      const { error: linkError } = await admin.schema("chat").rpc("aprovar_cadastro_pendente", { p_request_id: requestId, p_auth_uid: invited.id, p_usuario_id: selectedUserId, p_admin_uid: authResult.user.id });
      if (linkError) return json({ message: `Convite criado, mas o vínculo falhou: ${linkError.message}. O pedido continua pendente; verifique a conta antes de tentar novamente.`, authUid: invited.id }, 500);
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
