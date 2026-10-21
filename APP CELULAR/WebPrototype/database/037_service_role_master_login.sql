-- A Edge Function master-user-invite usa service_role para localizar nomes.
-- As migrations 030 e 032 revogam acesso público às tabelas, mas não concedem
-- SELECT ao papel interno service_role usado pelo backend.
BEGIN;

GRANT USAGE ON SCHEMA chat TO service_role;
GRANT SELECT ON chat.usuario_master, chat.usuario_auth TO service_role;

NOTIFY pgrst, 'reload schema';
COMMIT;
