# Publicar a API no Render

O arquivo `render.yaml`, na raiz do repositório, define o serviço web gratuito, o contêiner .NET, a porta HTTPS pública, a verificação `/health`, publicação automática a cada commit e os segredos das conexões Supabase. O processo da API usa a variável `PORT` do Render; localmente continua usando a configuração existente.

## Criar o serviço

1. No Render, conecte o GitHub e autorize acesso somente ao repositório `APP_CELULAR`.
2. Crie um **Blueprint** e escolha esse repositório e a branch principal.
3. O Render encontrará o `render.yaml`. Na tela inicial do Blueprint, informe os valores secretos de `ConnectionStrings__SupabaseEmpresa1` e `ConnectionStrings__SupabaseEmpresa2` diretamente no painel Render. Não os coloque no repositório, no aplicativo móvel ou em mensagens.
4. Confirme a criação do Web Service no plano Free. O endereço HTTPS aparecerá no serviço criado.
5. Execute `database/001_conversas.sql` uma vez em cada banco Supabase configurado.
6. No app, acesse **Configurar endereço da API**, informe a URL HTTPS do Render e o código da empresa.

Para adicionar uma empresa, inclua em `render.yaml` um novo par `Empresas__ID` / `ConnectionStrings__SupabaseEmpresaN` e cadastre o segredo no painel Render.

As variáveis de conexão aceitam o formato URI copiado de **Supabase → Connect**, por exemplo `postgresql://...`, ou o formato Npgsql `Host=...;Port=5432;Database=postgres;Username=...;Password=...;SSL Mode=Require`. A API converte as URLs do Supabase para o formato Npgsql antes de abrir o banco.

## Limites do plano gratuito

O serviço gratuito suspende a instância após 15 minutos sem tráfego e pode levar cerca de um minuto para acordar no próximo acesso. Reinicializações perdem o estado mantido só em memória; os tokens do app são renovados automaticamente. O plano Free executa uma instância, que também evita compartilhar as sessões entre instâncias.

O Render publica automaticamente as próximas alterações do serviço conectado. A API fica acessível pela internet com HTTPS; o acesso aos bancos continua separado por empresa e protegido pelo login/token da API.
