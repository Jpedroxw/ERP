# ERP — Acesso lembrado sem PIN e sem TPM obrigatório

**Base:** `ErpNaoFiscal(4).zip` enviado pelo usuário. Esta entrega altera a **Retaguarda**, a **API** e os contratos compartilhados. Não altera os programas Caixa/Master, a parte fiscal nem `deploy/instalar-servidor.sh`.

## Comportamento novo

- No primeiro acesso, informe o login e a senha da empresa e marque **Lembrar por 7 dias** (somente em computador pessoal/confiável).
- O ERP cria uma chave privada de assinatura ECDSA P-256 no armazenamento criptográfico do **usuário Windows**: tenta TPM (quando disponível); em máquina sem TPM usa Microsoft Software Key Storage Provider, com chave marcada como não exportável. O arquivo de identificação é protegido com DPAPI `CurrentUser`.
- **Não existe PIN adicional do ERP.** A senha do ERP não fica gravada para o acesso lembrado; a API recebe somente a chave pública e o identificador do dispositivo.
- Nas próximas entradas, na mesma conta do Windows, o campo de senha pode ficar em branco. O programa assina uma solicitação e a API valida a assinatura.
- O registro expira em **7 dias**, sem renovação silenciosa. Após expirar, informe a senha novamente e marque lembrar para cadastrar uma nova chave.
- Credenciais V4 que utilizavam PIN **não são automaticamente convertidas para acesso sem PIN**: é preciso entrar novamente com senha e cadastrar outra chave. Isso evita reduzir a proteção da credencial anterior sem autenticação.

## Gerenciar computadores

Retaguarda → **Configurações → Computadores lembrados**:

- Usuários: consultam e revogam os próprios dispositivos.
- Admin: consulta todos os dispositivos cadastrados **da mesma empresa**. Para revogar dispositivos de outros usuários precisa antes abrir **Configurações → Confirmar senha** e confirmar a senha atual.
- Lista mostra login do usuário, nome do computador, proteção (TPM ou Windows), data de cadastro, vencimento, último uso, status de revogação.
- Dispositivo revogado deixa de conseguir entrar; sessões criadas pela chave do dispositivo também são recusadas nas chamadas seguintes.

## Operações sensíveis

> **Revisão:** não é mais preciso ir antes em Configurações → Confirmar senha. Quando o servidor pede a senha
> (resposta 403 com `codigo: "confirmar_senha"`), a Retaguarda abre a janela de confirmação sozinha e repete a
> alteração depois de confirmar. O menu continua existindo para quem quiser confirmar antes.

A API exige senha confirmada nos últimos **10 minutos** para **criar/editar/desativar usuários e terminais**. Uma sessão aberta por chave lembrada permite operações comuns, mas não estas alterações administrativas.

Na Retaguarda, abra **Configurações → Confirmar senha** e digite a senha da conta conectada. Isso libera essas ações por 10 minutos sem sair do programa. As operações de leitura (GET) continuam disponíveis sem reautenticação.

## Proteções no servidor

- Validação por banco da empresa (tenant). Chaves de uma empresa não autenticam em outra.
- Atualização **atômica** do último carimbo aceito, impedindo que a mesma assinatura seja reutilizada por solicitações concorrentes.
- Validade curta e limitação de **até 10 dispositivos lembrados por usuário**.
- Alteração da senha ou desativação de usuário invalida o acesso lembrado; a API também revoga esses dispositivos ao alterar o cadastro de usuário ou redefinir o administrador pelo Master.
- Registros de cadastros, logins por dispositivo, falhas de assinatura, revogações e falhas de senha são escritos no **journal do serviço** (sem registrar senhas nem chaves privadas).
- Os endpoints de login, registro e confirmação de senha usam controle de taxa de requisições por IP.

### Limitações de segurança a considerar

- **Sem TPM e sem PIN, a segurança depende da conta Windows.** Se alguém acessar uma sessão Windows desbloqueada ou comprometer esse perfil, poderá utilizar a chave de autenticação. Não marque "Lembrar" em computadores públicos ou compartilhados. Configure senha de login do Windows e bloqueio automático de tela.
- Software-backed CNG não oferece a mesma proteção física contra extração que TPM. "Não exportável" é uma política do provedor de software, não garantia contra administrador malicioso/malware.
- O registro `Tpm` é informado pelo aplicativo e exibido para diagnóstico; não é comprovação remota de hardware (não há atestação TPM).
- Os tokens criados após **confirmação de senha** são tokens normais de login, não associados ao computador. A política de alteração de senha pode exigir invalidação adicional dos tokens de senha ainda válidos para cenários de maior risco. As sessões lembradas, por sua vez, são verificadas contra o dispositivo a cada chamada.
- Revogação local durante falta de internet remove a chave local, mas a confirmação de revogação no servidor depende da volta da conexão. Para revogar remotamente, use a tela de dispositivos quando a VPS estiver acessível.
- Limitação de tentativas por IP não substitui MFA em operações de alto risco. O sistema não envia códigos por e-mail nesta versão; isso exigiria integração de serviço de e-mail e configuração adicional.
- O acesso lembrado depende da API para login: não foi adicionado login offline à Retaguarda (o PDV permanece como já funcionava).

## Como compilar

1. Extraia o ZIP novo em pasta separada no Windows.
2. Abra o PowerShell na raiz da solução e execute:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
.\deploy\publicar.ps1
```

3. **Não publique na VPS se houver erros**. O script precisa finalizar os quatro projetos.
4. Quando a compilação passar, atualize a **API** em `/opt/erp-nf/api` na VPS usando seu procedimento habitual e reinicie `erp-nf-api`. Preserve `/etc/erp-nf/api.env` e os bancos PostgreSQL. **Não execute** `instalar-servidor.sh` novamente.
5. Atualize o executável da Retaguarda com `publicar\retaguarda`, mantendo as configurações existentes. Caixa e Master não mudaram no código desta entrega.

## Testes de aceite (empresa fictícia)

1. **Sem TPM:** Windows 10/11 com usuário Windows padrão, registrar como lembrado sem PIN, sair e entrar novamente com senha em branco. Conferir que a API autentica.
2. **Com TPM:** repetir e conferir que o tipo de proteção aparece como TPM.
3. **Senha normal:** deixar a caixa desmarcada e comprovar login com senha, sem memorizar computador.
4. **Troca de senha:** editar a senha do usuário e verificar que login lembrado anterior é recusado.
5. **Revogação:** revogar dispositivo na tela e comprovar que novo login é recusado e sessão existente perde acesso na chamada seguinte.
6. **Outro usuário:** Admin lista todos os dispositivos da empresa, confirma a senha e revoga um dispositivo de outro usuário.
7. **Cross-tenant:** dispositivo criado em empresa A não autentica no endereço da empresa B.
8. **Administração:** login por dispositivo não edita usuários nem terminais; depois de confirmar a senha em Configurações, edita por 10 minutos; depois desse período exige nova confirmação.
9. **Reenvio:** duas solicitações de login com o mesmo carimbo e assinatura não podem gerar dois tokens.
10. **Compatibilidade:** arquivo lembrado antigo com PIN pede senha e novo registro. Não perder configurações comerciais nem vendas.

## Validação executada nesta entrega

Foi feita inspeção estática dos arquivos, comparação dos scripts/ícones com a V4 e verificação da integridade do ZIP. **Não foi possível compilar com .NET 10, executar o WinForms ou conectar ao PostgreSQL/VPS nesta sessão**, portanto estes testes de aceite ainda precisam ser realizados no Windows antes de qualquer uso em produção.
