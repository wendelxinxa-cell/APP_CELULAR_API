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
  if (!authorization?.startsWith("Bearer ")) return json({ message: "Sessão necessária." }, 401);

  const supabaseUrl = Deno.env.get("SUPABASE_URL");
  const anonKey = Deno.env.get("SUPABASE_ANON_KEY");
  const serviceRoleKey = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY");
  if (!supabaseUrl || !anonKey || !serviceRoleKey) return json({ message: "Função de convite sem configuração." }, 500);

  const caller = createClient(supabaseUrl, anonKey, {
    global: { headers: { Authorization: authorization } },
    auth: { persistSession: false, autoRefreshToken: false },
  });
  const admin = createClient(supabaseUrl, serviceRoleKey, {
    auth: { persistSession: false, autoRefreshToken: false },
  });

  try {
    const { data: authResult, error: authError } = await caller.auth.getUser();
    if (authError || !authResult.user) return json({ message: "Sessão inválida." }, 401);

    const { data: isZeus, error: roleError } = await caller.schema("chat").rpc("eh_zeus");
    if (roleError) return json({ message: "Não foi possível validar a função Zeus." }, 403);
    if (isZeus !== true) return json({ message: "Apenas Zeus pode convidar usuários." }, 403);

    const body = await request.json();
    const email = String(body.email ?? "").trim().toLowerCase();
    const nome = String(body.nome ?? "").trim().slice(0, 120);
    const master = body.master === true;
    const empresaId = Number(body.empresaId ?? 1);
    const usuarioId = body.usuarioId == null ? null : Number(body.usuarioId);
    if (!email.includes("@") || email.length > 254 || nome.length === 0) {
      return json({ message: "Informe um nome e um e-mail válidos." }, 400);
    }
    if (!master && (!Number.isInteger(empresaId) || empresaId <= 0 || !Number.isInteger(usuarioId) || usuarioId! <= 0)) {
      return json({ message: "Para usuário comum, informe o ID CATCHAT existente." }, 400);
    }

    const { data: invite, error: inviteError } = await admin.auth.admin.inviteUserByEmail(email, {
      data: { nome },
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
