# Publicar a API no Render

O arquivo `render.yaml`, na raiz do repositório, define o serviço web gratuito, o contêiner .NET, a porta HTTPS pública, a verificação `/health`, publicação automática a cada commit e os segredos das conexões Supabase. O processo da API usa a variável `PORT` do Render; localmente continua usando a configuração existente.

## Criar o serviço

1. No Render, conecte o GitHub e autorize acesso somente ao repositório `APP_CELULAR`.
2. Crie um **Blueprint** e escolha esse repositório e a branch principal.
3. O Render encontrará o `render.yaml`. Na tela inicial do Blueprint, informe os valores secretos de `ConnectionStrings__SupabaseEmpresa1` e `ConnectionStrings__SupabaseEmpresa2` diretamente no painel Render. Não os coloque no repositório, no aplicativo móvel ou em mensagens.

4. Confirme a criação do Web Service no plano Free. O endereço HTTPS aparecerá no serviço criado.
5. Execute as migrações necessárias em cada banco Supabase de empresa antes de publicar a versão correspondente da API.
6. No app, acesse **Configurar endereço da API**, informe a URL HTTPS do Render e o código da empresa.

## Catálogo central e aprovações de empresas

O cadastro pelo aplicativo usa um projeto Supabase separado para o catálogo da plataforma. Crie esse projeto, execute `database/002_catalogo_empresas.sql` nele e configure `ConnectionStrings__CadastroCentral` no Render com a conexão desse projeto. Não use o banco de uma empresa como catálogo central.

No Render, configure também `MasterAdmin__Username`, `MasterAdmin__PasswordHash`, `MasterAdmin__EncryptionKey` e `MasterAdmin__Email`. Para ativar a conta de configuração Zeus solicitada no aplicativo, defina `MasterAdmin__Username=Zeus` e configure `MasterAdmin__PasswordHash` como SHA-256 hexadecimal da senha Zeus nos segredos do Render. A senha nunca é validada no código da API; a conta Zeus do aplicativo autentica no servidor usando essa configuração. Gere também uma chave AES aleatória de 32 bytes em Base64; guarde todos os segredos somente no Render. O e-mail é opcional: sem provedor configurado, o Master acompanha e aprova a solicitação pela tela **Empresas**. O Render Free bloqueia SMTP de saída nas portas 25, 465 e 587; para ativar avisos por e-mail nessa hospedagem será necessário integrar um provedor por API HTTPS e configurar um domínio remetente verificado.

O Administrador Master cadastra nome, e-mail do responsável e conexão do Supabase da empresa. A API testa o banco, cifra a conexão no catálogo e deixa a empresa pendente. O Master aprova na tela **Empresas** do app; só então aquele código de empresa pode autenticar e acessar conversas. Quando não há provedor de e-mail, a aprovação continua disponível no app, mas os avisos não são enviados.

O banco da empresa precisa estar previamente preparado com o esquema do aplicativo, incluindo `app.usuario`. O cadastro associa um banco Supabase existente; ele não cria um projeto Supabase automaticamente.

Para gerar os valores do Render no PowerShell, use uma senha Master forte e substitua o texto de exemplo localmente:

```powershell
[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes('SENHA-MASTER-FORTE')))
[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

O primeiro resultado vai em `MasterAdmin__PasswordHash`; o segundo, em `MasterAdmin__EncryptionKey`. Configure esses segredos, o catálogo central e SMTP antes de publicar a API atualizada. Nunca envie a senha, chave ou conexões de banco por e-mail ou chat.

## Atualização do banco para retentativas do chat

Antes de publicar uma versão da API que contenha o envio idempotente do chat, execute o arquivo `database/001_conversas.sql` em cada banco Supabase de empresa. Ele cria a tabela de mensagens (se ainda não existir) e adiciona uma chave para impedir mensagens duplicadas quando o aplicativo tentar reenviar após uma falha de rede.

Empresas cadastradas pelo Master usam conexões criptografadas no catálogo central; não é necessário acrescentar variáveis de conexão individuais no Render. A primeira instalação da empresa pode vincular-se ao registro de dispositivo legado ativo; instalações posteriores ficam pendentes até um administrador da empresa aprová-las em **Usuários → Aprovar usuários e aparelhos**.

Antes de publicar o fluxo de aprovação de usuários/aparelhos, execute `database/003_aprovacoes_acesso.sql` e depois `database/004_apelidos_e_vinculo_dispositivo.sql` uma vez em cada banco Supabase de empresa. Para habilitar o bloqueio global de usuários por Zeus, execute também `database/005_bloqueio_master.sql` em cada banco de empresa ativo. Usuários que já existiam são considerados aprovados; novos usuários sincronizados ficam pendentes. A migração 004 guarda apelidos individuais e o vínculo auditável entre usuário e instalação sem alterar ou apagar mensagens anteriores.

O Administrador Master aprova e ativa empresas no catálogo da plataforma. Dentro de uma empresa ativa, o administrador da própria empresa aprova novos usuários, aparelhos e pedidos para trocar o usuário vinculado a um aparelho. Após a aprovação, o usuário acessa o chat e o histórico já existente daquela empresa. A conta Zeus pode consultar os chats das empresas ativas na tela administrativa; os usuários comuns continuam restritos à própria empresa.

As variáveis de conexão aceitam o formato URI copiado de **Supabase → Connect**, por exemplo `postgresql://...`, ou o formato Npgsql `Host=...;Port=5432;Database=postgres;Username=...;Password=...;SSL Mode=Require`. A API converte as URLs do Supabase para o formato Npgsql antes de abrir o banco.

## Limites do plano gratuito

O serviço gratuito suspende a instância após 15 minutos sem tráfego e pode levar cerca de um minuto para acordar no próximo acesso. Reinicializações perdem o estado mantido só em memória; os tokens do app são renovados automaticamente. O plano Free executa uma instância, que também evita compartilhar as sessões entre instâncias.

O Render publica automaticamente as próximas alterações do serviço conectado. A API fica acessível pela internet com HTTPS; o acesso aos bancos continua separado por empresa e protegido pelo login/token da API.
