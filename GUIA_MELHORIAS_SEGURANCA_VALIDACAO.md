# ERP Não Fiscal — Revisão de segurança, auditoria e validação de campos

**Base:** `ErpNaoFiscal (1).zip` recebido em 03/11/2026. **Sem qualquer integração fiscal.**

## O que foi alterado

### 1. Auditoria de operações administrativas

- A API registra criação, alteração e desativação de **usuários e terminais**, com data, ID do operador e ID do registro.
- Registra a revogação de computador lembrado e o encerramento de sessão, e identifica a redefinição de senha via Master.
- Senhas, hashes, tokens e chaves **não são copiados para os registros de auditoria**.
- Novo endpoint restrito a Admin: `GET /api/administracao/auditoria?limite=200`.
- Na Retaguarda: **Configurações → Auditoria** (Admin).
- Os registros são separados por empresa, porque continuam no banco PostgreSQL do respectivo cliente.
- A redefinição via chave Master fica identificada como operação do Master; a chave Master não identifica um operador humano específico.

### 2. Sessões com revogação

- Login normal e login por dispositivo lembrado criam um registro `SessoesAcesso` associado ao token.
- Cada requisição autenticada verifica se sua sessão continua ativa.
- Na Retaguarda: **Configurações → Sessões de acesso**. O usuário vê as próprias sessões; o Admin pode listar as da empresa.
- A opção **Encerrar selecionada** invalida o token da sessão imediatamente para as próximas requisições.
- Encerrar sessão de outro usuário exige Admin e confirmação recente de senha.
- Redefinir senha ou desativar usuário revoga todas as sessões e dispositivos lembrados desse usuário.
- Revogar dispositivo lembrado também encerra sessões criadas por ele.
- As mudanças de usuário e a revogação são feitas na mesma transação PostgreSQL.

**Compatibilidade importante:** os tokens emitidos pela versão anterior não possuem identificador de sessão. Após instalar a nova API, **todos precisarão entrar novamente na Retaguarda**. O login local do PDV e as vendas offline não mudaram.

### 3. Formatos de campos

- Código do produto: vazio continua gerando o código automaticamente; quando informado, só permite letras ASCII, números, `.`, `_`, `-`, `/` (até 30 caracteres, começando por letra ou dígito).
- Nomes, descrição e observações: rejeitam caracteres de controle/invisíveis, sem bloquear acentos e pontuação normal.
- CPF/CNPJ: além do dígito verificador, rejeita caracteres estranhos que eram eliminados silenciosamente na limpeza.
- Telefone: confere máscara e comprimento, rejeitando letras e símbolos impróprios.
- E-mail: confere formato local/domínio e impede segmentos vazios no domínio.
- Login: mantém letras minúsculas, números, ponto, hífen e sublinhado.
- Entradas, importações e promoções: verificações adicionais também na API, para proteger contra dados enviados fora da tela.
- Retaguarda: novo campo de código de produto e tratamento seguro de colagem em textos livres. Cadastro de empresa também usa campo de texto seguro.

**Compatibilidade:** se o banco já tiver um código de produto com espaços ou símbolos fora da regra, ele continuará registrado, mas precisará ser ajustado se editar seu cadastro. Confira os códigos dos produtos existentes antes de usar em clientes.

### 4. Novas tabelas

`Auditorias` e `SessoesAcesso` são criadas pelo mecanismo já existente de inicialização da API. Caso não seja possível criá-las, a inicialização agora apresenta erro explícito, em vez de manter um controle de sessões parcialmente operacional. **Não execute `instalar-servidor.sh` para isso.**

> O projeto ainda utiliza `EnsureCreated` + criação incremental de tabelas na ausência de migrations. **A migração completa para migrations versionadas não foi feita nesta versão** e segue como trabalho separado, que exige testes com cópias de bancos existentes.

## Ajustes da revisão

- **Copiar/colar voltou a funcionar** nos campos de texto: o campo seguro bloqueava Ctrl+C, Ctrl+V, Ctrl+X, Ctrl+Z e
  Ctrl+A (eles chegam como caracteres de controle). Agora só bloqueia os invisíveis de formatação; o que é colado
  continua filtrado.
- **Código de produto antigo** (fora da regra nova, ex.: com espaço) pode ter preço/descrição alterados sem trocar o
  código; a regra vale para código novo ou alterado (na tela e na API).
- **Importação limpa em vez de pular**: tabulação/quebra de linha viram espaço e invisíveis saem (como antes); código
  de outro sistema é ajustado (espaço vira hífen, símbolos saem) com aviso, e o código original também é procurado
  para atualizar o produto já existente.
- **Auditoria legível**: o resumo diz quem foi alterado (ex.: "Alterado: usuário maria (Gerente) — senha redefinida")
  e a tela mostra as ações em português ("Criou", "Esqueceu computador"...).
- **Sessões antigas**: a API apaga ao iniciar as sessões vencidas há mais de 30 dias (cada login cria uma).
- Observação multilinha detectada pelo atributo do campo, não pelo texto do rótulo.

## Verificação e testes

### A. Compilar e publicar (Windows com .NET 10)

Na raiz `ErpNaoFiscal`:

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
.\deploy\publicar.ps1
```

Confirme que os **quatro projetos** compilam. A API e a Retaguarda precisam ser atualizadas juntas. Master e Caixa não tiveram mudanças de interface nesta revisão.

### B. Executar testes de formatos (sem API)

```powershell
dotnet run -c Release --project .\testes\Erp.TestesValidacao\Erp.TestesValidacao.csproj
```

Os testes conferem códigos, nomes, CPF, telefone, login, unidades e caracteres invisíveis. Não gravam dados.

### C. Testar autenticação e revogação (SOMENTE empresa fictícia)

```powershell
$env:ERP_TEST_URL = 'https://SEU-DOMINIO/nf/botretaguarda/'
$env:ERP_TEST_LOGIN = 'botadmin'
dotnet run -c Release --project .\testes\Erp.TestesIntegracao\Erp.TestesIntegracao.csproj
```

O programa solicita a senha no terminal sem exibi-la e realiza: senha errada, login certo, confirmação de senha errada/certa, segunda sessão, revogação da primeira e verificação de que a segunda continua ativa. **Esse teste cria e encerra sessões de verdade; não execute com loja real.**

O cenário de sessão expirada deve ser verificado adicionalmente com duração de token reduzida num ambiente isolado. O teste de revogação não substitui esse teste de expiração nem a validação visual das telas WinForms.

### D. Atualização da VPS — somente após testes

- Não reinstale Debian/PostgreSQL; preserve `/etc/erp-nf/api.env` e os bancos já existentes.
- Publique a API da versão nova e envie `publicar/api` para `/opt/erp-nf/api` conforme seu guia de implantação.
- Faça a substituição com o serviço parado e inicie `erp-nf-api` novamente.
- Confirme: `systemctl status erp-nf-api --no-pager` e `curl http://127.0.0.1:5080/health`.
- Atualize o EXE da Retaguarda e faça login novamente.
- Para uso fora de testes, recomenda-se backup e restauração ensaiada antes de qualquer alteração no esquema de dados.

## Limites desta entrega

Inspeção e edição de arquivos-fonte, checagem estática de estrutura/lexemas e verificação de integridade ZIP. **Não houve compilação .NET nem testes com WinForms/PostgreSQL reais neste ambiente.** Não distribua a clientes finais antes de compilar, executar os testes e conferir os logs da VPS.
