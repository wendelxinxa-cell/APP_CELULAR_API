-- Opcional: execute em cada banco Supabase de empresa somente depois de
-- configurar ChatCentral:Ativo=true no Render e confirmar o chat compartilhado.
-- Apaga apenas mensagens e apelidos de teste antigos; usuarios, empresas,
-- dispositivos, aprovacoes e cadastros do aplicativo permanecem intactos.

DROP TABLE IF EXISTS app.mensagem_conversa;
DROP TABLE IF EXISTS app.usuario_apelido;
