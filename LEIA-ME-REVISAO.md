# ERP revisado - sem alteracoes fiscais

Versao preparada a partir de `ErpNaoFiscal_Melhorado.zip` enviado nesta conversa.
Inclui os quatro programas: **API**, **PDV (Caixa)**, **Retaguarda** e **Master**.

## Correcoes e melhorias desta entrega

1. `deploy/publicar.ps1` foi reescrito com **somente ASCII** para evitar o erro de codificacao/aspas no Windows PowerShell 5.1.
2. Publicador confere a existencia do SDK `dotnet` e dos quatro projetos, para ao primeiro erro e verifica os executaveis gerados.
3. Os quatro pacotes sao gerados em uma pasta temporaria. O script so substitui `publicar` depois que todos terminarem com sucesso. Se a publicacao falhar, preserva a pasta `publicar` anterior.
4. Foi criada a funcao `Erp.Shared.GravacaoAtomica` para escrever arquivos de configuracao via arquivo temporario e substituicao. Aplicada a PDV, Retaguarda, Master, Configuracao Tecnica e Aparencia. Objetivo: reduzir risco de configuracoes truncadas ao salvar.
5. `Erp.Pdv/Servicos/Sincronizador.cs`: agora descarta corretamente as respostas HTTP de envio e recebimento com `using var`.
6. Preservadas as melhorias anteriores no PDV (numeracao comercial dentro da transacao, indices de vendas), senha administrativa inicial sem fallback `admin` e mensagem de falha no timer de sincronizacao.

## Publicar no Windows

Extraia o ZIP em uma pasta nova. Abra o PowerShell DENTRO da pasta `ErpNaoFiscal` (onde fica `ErpNaoFiscal.slnx`).

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
.\deploy\publicar.ps1
```

Se abrir com duplo clique e a janela fechar, use:

```powershell
powershell.exe -NoExit -ExecutionPolicy Bypass -File .\deploy\publicar.ps1
```

A saida sera:

- `publicar\api`: API Linux para a VPS Debian 13.
- `publicar\pdv`: PDV Windows.
- `publicar\retaguarda`: Retaguarda Windows.
- `publicar\master`: ERP Master Windows.

**Nao envie o ZIP do codigo-fonte inteiro para a VPS.** A API usa o servico `erp-nf-api` e a pasta `/opt/erp-nf/api` conforme seu guia. O arquivo `/etc/erp-nf/api.env` contem as configuracoes do servidor e nao deve ser substituido.

O script **nao** envia nenhum arquivo para a VPS e **nao** modifica bancos de dados.

## Validacao e limites

- Inspecionada a arvore dos projetos e seus caminhos, o script e as alteracoes pontuais no codigo.
- Arquivo `instalar-servidor.sh`: sintaxe Bash validada (`bash -n`).
- Esquema SQLite do PDV: comandos `CREATE TABLE` e `CREATE INDEX` executados em teste local SQLite.
- Arquivo `.ps1`: verificado como ASCII; **nao foi possivel executar um parser real de PowerShell neste ambiente**.
- **Nao foi possivel compilar ou executar o ERP**, pois o SDK .NET/PowerShell e as dependencias Windows nao estao disponiveis aqui. A compilacao precisa ser realizada no seu Windows com SDK .NET 10.

## Login recusado (correcao de 03/11)

- Caixa: ao trocar o Servidor ou a chave, o caixa continuava pedindo so o que mudou depois da ultima sincronizacao com o servidor ANTIGO, entao usuarios e produtos mais antigos do servidor novo nunca chegavam ("usuario ou senha invalidos"). Agora ele guarda de onde vieram os cadastros (`origem_sync`) e, se mudou, baixa tudo de novo. Na primeira abertura com o .exe novo, todo caixa faz um download completo uma vez.
- Caixa: se o usuario nao existe ou a senha nao bate, sincroniza na hora e tenta de novo; se ainda falhar, explica o motivo (nenhum usuario recebido, usuario de outra empresa, sem comunicacao, senha incorreta).
- Retaguarda: "Login ou senha invalidos" agora mostra em qual servidor voce esta entrando e lembra de usar o nome do servidor da empresa.

## Entrada de produtos (03/11)

- Retaguarda: nova tela Estoque -> Entrada de produtos (varios itens, fornecedor, nota, custo; historico e cancelamento).
- API: tabelas `Entradas` e `EntradaItens` + rotas `/api/estoque/entradas`. A API passou a criar sozinha, ao iniciar, as tabelas novas que faltarem nos bancos que ja existiam (mesmo script do EF com IF NOT EXISTS). Exige atualizar a API na VPS e reiniciar o servico.

## Revisão das cargas e promoções (03/11, noite)

Mantida a carga manual (Cargas para caixas). Corrigido e acrescentado:

- **Carga parcial**: publicar não trava mais por causa de um produto. Os válidos vão; os que têm problema (sem preço,
  preço normal abaixo de promoção em vigor/agendada) voltam numa lista com o motivo. "Publicar todos os pendentes"
  manda de 1000 em 1000.
- **Promoção encerrada não bloqueia mais baixar o preço** (só contam promoções em vigor ou agendadas, pela data da loja).
- **Status por caixa** em Cargas para caixas: cada caixa mostra se já baixou a última carga (produtos e promoções).
- **Importação** tem a opção "Já publicar nos caixas" (marcada): os produtos importados não ficam presos como rascunho.
- **Relógio confiável no caixa** (`Erp.Pdv/Servicos/Relogio.cs`): hora pela internet (NTP.br; se a rede bloquear NTP,
  o cabeçalho Date de google/cloudflare/microsoft) e pela hora do servidor em cada sincronização. Durante a sessão a
  hora anda pelo contador interno do Windows, então mudar o relógio do Windows não liga/desliga promoção nem muda a
  hora das vendas. A diferença fica gravada para quando abrir sem internet, e a hora nunca volta para antes da maior
  hora certa já vista. Se o relógio do Windows estiver errado (2 min ou mais), a barra de status avisa.
  Promoções, data/hora das vendas, caixas, movimentos e do cupom usam esse relógio.
- **Promoção na venda e no cupom**: o item aparece como "★ PROMOÇÃO" (em verde) e o cupom mostra "PROMOÇÃO (de R$ X)"
  com o desconto do item e "Você economizou R$ Y" no fim.
- Logo da empresa voltou ao topo do caixa; a letra da lista de itens do caixa voltou ao tamanho 11 no modo Normal.
- Menu "Caixa / movimentos": o menu é descartado depois do clique (antes podia engolir a opção escolhida).
- Retaguarda: telas de Cargas e Promoções com acentos, valores em R$ e layout que não corta em 125%/150%.
  "Nova promoção" com busca do produto, preço normal e % de desconto, e só fecha depois que o servidor aceitar.
  O aviso de carga ao salvar produto aparece uma vez por sessão (não a cada salvamento). Voltou o botão rápido
  "Modo simples / Modo completo" (também no menu Configurações e no menu compacto).
- Master: tirada a navegação repetida (faixa de abas e o botão Configurações do topo; fica o menu de navegação).

Exige atualizar a API na VPS (rotas novas/alteradas de cargas, promoções e importação) e os três programas Windows.

## Acesso técnico por empresa e login lembrado (03/11, revisão)

Juntadas as mudanças do pacote "Acesso_Tecnico_Por_Empresa" (acesso técnico por empresa no Master, autorização online no
caixa, "Copiar chave" em Terminais, "Lembrar usuário e senha" na Retaguarda) com a revisão das cargas/promoções e o
relógio confiável. Ajustes: o caixa mantém também a senha técnica local (sem internet) ao lado do acesso online;
mensagem clara para caixa no servidor padrão; login técnico com o campo de login (só caracteres válidos); a senha
técnica gerada no Master é copiada para a área de transferência.

## "Lembrar este computador" sem guardar senha (03/11)

Trocado o "Lembrar usuário e senha" (que guardava a senha protegida pelo Windows) por uma chave do computador:
ECDSA P-256 não exportável, no TPM quando existe. O servidor guarda só a chave pública (`Dispositivos`), aceita a
entrada assinada por 30 dias, recusa pedido repetido ou fora do horário (a hora vem do próprio servidor), e invalida ao
trocar a senha ou desativar o usuário. Rotas: POST `/api/auth/dispositivos`, POST `/api/auth/dispositivo/login`
(limite de tentativas), DELETE `/api/auth/dispositivos/{id}`. Exige API atualizada.

PCs simples/antigos: sem TPM, lembrar exige um PIN (4–8 números, conferido no servidor, 5 erros = esquecido).
O publicador agora gera também `publicar\pdv-32bits` e `publicar\retaguarda-32bits` para Windows 10 de 32 bits
(comum em PC com 4 GB). O .NET 10 roda em Windows 10 e 11; Windows 7/8.1 não rodam.

## Revisão do pacote V4 (segurança sem PIN)

Mantido o desenho do V4 (sem PIN, 7 dias, computadores lembrados, senha recente para usuários/terminais, carimbo
atômico, revogação ao trocar senha, logs). Ajustes: a confirmação de senha abre sozinha quando o servidor pede e a
alteração é repetida (antes dava erro e era preciso achar o menu); telas "Confirmar senha" e "Computadores lembrados"
com o tema, acentos, botão Cancelar e layout que não corta; mensagens da API com acentos.

## Pontos importantes para uma segunda etapa (nao alterados por seguranca)

- ~~A chave do terminal do PDV continua no `pdv.json` em texto claro.~~ Feito: agora fica criptografada (DPAPI, so abre no mesmo computador). Caixas antigos sao convertidos sozinhos ao abrir. Testar no Windows: abrir um caixa ja configurado, conferir que sincroniza e que o `pdv.json` passou a ter `TerminalKeyProtegida` no lugar de `TerminalKey`.
- Fluxos de estoque e sincronizacao entre diversos caixas precisam de testes de concorrencia e reconciliacao com PostgreSQL real.
- A API ainda requer planejamento de migracoes EF Core para bancos que usam `EnsureCreated`.
- Nao foram aplicadas mudancas de banco, de API publica, de emissao NFC-e nem de gateway fiscal.

**Antes de trocar a API da VPS:** publicar com sucesso, conferir os logs de compilacao e testar `GET /health` no ambiente de testes.
