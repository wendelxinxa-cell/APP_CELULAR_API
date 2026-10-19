# APP CELULAR: WebPrototype com Supabase direto

## Arquitetura

O cliente Blazor WebAssembly executa no navegador. Auth, RPC de chat, Realtime e Storage comunicam-se diretamente com o Supabase. O host ASP.NET entrega os arquivos e o endpoint público `supabase-config.js`; ele não encaminha mensagens nem arquivos. `app-celular-api` e a APK não foram alterados.

## Configuração do host

A configuração de homologação está preenchida em `appsettings.json`. Para outro host, configure estas variáveis; elas substituem os valores do arquivo:

- `Supabase__Url`: `https://<ref>.supabase.co`
- `Supabase__PublishableKey`: chave publishable do projeto
- `Supabase__Bucket`: `chat-media`
- `Supabase__EmpresaId`: `1` (confirmado nos dados de homologação)

A URL e a publishable key são entregues ao navegador; nunca use chave `secret` ou `service_role` no site, APK ou endpoint de configuração. Os valores atuais são do projeto de homologação.

## Banco de homologação

Em 08/10/2026, a migração `database/010_supabase_direct_chat.sql` foi aplicada ao projeto `catalogo-central-empresas` (`zuqwlupzxfjqswfseuat`). A validação confirmou:

- RLS habilitado nas 8 tabelas de chat relevantes e 8 políticas de acesso;
- bucket `chat-media` privado, sem arquivos migrados;
- `chat.mensagem` na publicação `supabase_realtime`;
- schema `chat` exposto à Data API e 8 RPCs usados pelo cliente expostos;
- nenhuma tabela exposta diretamente via PostgREST;
- exposição automática de tabelas novas desativada.

A migração é aditiva e mantém a coluna antiga `chat.mensagem.foto`; uma mídia existente ainda está em bytes e não foi transferida ao Storage. Não removemos dados legados.

## Acesso Zeus e teste

As migrações `020_supabase_access_requests.sql` e `030_supabase_master_account.sql` foram aplicadas em 08/10/2026 ao projeto de homologação. Zeus é uma função global separada dos usuários comuns. O usuário `wendelxinxa@gmail.com` foi ativado como `chat.usuario_master`; seu vínculo anterior com `chat.usuario_auth` / usuário APP CELULAR ID 5 foi removido. A conta comum de suporte permanece associada ao ID 2.

A confirmação da migração 030 validou a estrutura, mas encontrou uma incompatibilidade no tipo de retorno da função `listar_solicitacoes_acesso`; a versão local foi corrigida para remover a função anterior antes de recriá-la. A aplicação final da versão corrigida retornou sucesso.

Em 12/10/2026, a migração `database/032_master_user_management.sql` foi aplicada ao mesmo projeto. A primeira validação detectou divergência entre tipos numéricos/textuais legados e o contrato da RPC; os retornos foram convertidos explicitamente para os tipos declarados. A tela `/usuarios` confirmou a listagem de Zeus e das contas vinculadas.

O teste de estado após a migração confirmou Zeus ativo, nenhum vínculo normal para a conta master e **0 solicitações pendentes**. Os dois usuários já existentes foram vinculados antes da instalação do fluxo de cadastro; portanto, eles não geraram pedidos pendentes retroativamente. Para testar a fila, cadastre contas de teste pelo formulário, que serão inseridas como `PENDENTE`. Senhas antigas não são importadas.

A migração `031_master_chat_readonly.sql` foi aplicada em 08/10/2026. Zeus consegue listar contatos com conversas existentes e ler o histórico da empresa configurada em `Supabase__EmpresaId` (homologação: `1`); a interface apresenta o chat master em modo somente leitura. Não dê escrita direta nas tabelas ao navegador.

## Compatibilidade e mídia antiga

O APK ainda usa seu fluxo atual. Ele não foi migrado nem publicado nesta etapa. A coluna `foto` ainda contém uma mídia legada que precisa ser transferida por um processo separado antes de passar a exibi-la pelo novo bucket. O cliente novo envia uploads diretamente ao bucket e guarda a chave do objeto na mensagem; o download usa URL assinada curta e cache local no aparelho/navegador.

## Hospedagem

O binário publicado do Blazor ainda não foi atualizado. Ainda é necessário publicar a versão web para validar o login e a inspeção das conversas com Zeus. O serviço atual `app-celular-api` permanece intacto.

> O painel Supabase avisou que a organização excedeu a cota no ciclo anterior. Os projetos poderão ser restringidos a partir de 06/11/2026 se o excesso continuar.

