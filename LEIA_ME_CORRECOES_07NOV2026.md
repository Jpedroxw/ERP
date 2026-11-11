# Correções de 07/11/2026

## 10/11/2026 — Lucro, Pix com QR Code e botões rápidos

- **Relatório "Lucro por produto"** (Gestão → Relatórios): vendido, custo, lucro e margem % de cada produto no
  período, do maior lucro para o menor. Usa o custo gravado em cada venda; o desconto dado no total da venda é
  repartido entre os itens e as devoluções saem da conta. Venda antiga sem custo gravado usa o custo atual do
  cadastro (aparece na coluna Observação); produto sem custo nenhum fica sem margem e é avisado no rodapé.
  Só aparece para quem tem "Ver vendas" **e** "Ver preço de custo". Precisa atualizar a API.
- **Pix com QR Code no PDV** (Opções do caixa → Pix): tipo da chave, chave, nome de quem recebe e cidade. Ao pagar
  com a forma que tem "PIX" no nome, o Enter mostra o QR Code com o valor da venda (Pix estático, padrão do Banco
  Central: o dinheiro cai direto na conta da loja, sem taxa de intermediário). O operador confere no app do banco e
  clica em **Recebido**. Também funciona no pagamento dividido (o QR sai com o valor daquela parte) e tem
  **Copiar código** (Pix copia e cola). O botão "Ver QR Code de teste" mostra um QR de R$ 1,00 para conferir nome e
  chave com o celular. Chave em branco = Pix como antes, sem QR. Usa o pacote NuGet QRCoder (baixado pelo publicar).
- **Botões rápidos no PDV** (tecla **Insert**, "Rápidos" no rodapé ou Mais opções): até 16 produtos em botões
  grandes, para tocar ou apertar o número (1 a 9). Os produtos são escolhidos em Opções do caixa → Botões rápidos;
  sem escolha, aparecem sozinhos os produtos **sem código de barras** mais vendidos naquele caixa nos últimos 60
  dias. Produto por peso: lê a balança ou abre para digitar o peso.
- **Telas 1024×768:** na Gestão, o menu lateral do modo simples também vira só ícones em tela estreita (o do modo
  completo já virava); as barras de filtros e botões (Relatórios, Etiquetas, Fiado e devolução, Orçamentos) descem
  para uma segunda linha em vez de esconder botões; os cartões do Início encolhem para caber; o campo de busca dos
  cadastros encolhe para não cobrir os botões; telas abertas nas abas não têm mais tamanho mínimo fixo (encolhem e
  rolam em vez de ficar cortadas). No PDV, a lista de Orçamentos passou a ter 944 de largura e cabe em 1024.
- **Tela pequena:** janelas mais altas que a tela (a prévia de impressão tinha 980 de altura) agora encolhem para a
  área livre e ganham rolagem, em vez de esconder os botões de baixo atrás da barra de tarefas (1366×768).

## 10/11/2026 — Novo nome: Projeto JP

- **Nomes na tela:** Projeto JP PDV (antigo Caixa), Projeto JP Gestão (antiga Retaguarda), Projeto JP Master e
  Projeto JP Instalador. Site e painel online com a marca Projeto JP e ícone "JP".
- **Arquivos:** `ProjetoJP-PDV.exe`, `ProjetoJP-Gestao.exe`, `ProjetoJP-Master.exe` e `Instalar-ProjetoJP.exe`.
  O publicar gera a Gestão em `publicar\gestao` e `publicar\gestao-32bits`.
- **Código:** projetos e namespaces `ProjetoJP.*` (`ProjetoJP.Gestao` no lugar de `Erp.Retaguarda`), solução
  `ProjetoJP.slnx`. Rode uma vez `deploy\apagar-pastas-antigas.cmd` para tirar as pastas `src\Erp.*` antigas.
- **Lojas já instaladas:** a atualização automática continua funcionando (o pacote leva o programa com o nome antigo,
  que as versões anteriores procuram). O `Instalar-ProjetoJP.exe` rodado por cima troca `ErpPdv.exe` /
  `ErpRetaguarda.exe` pelos novos, apaga os atalhos "Caixa - ERP" / "Retaguarda - ERP" e a pasta "ERP Não Fiscal" do
  Menu Iniciar. Instalações novas vão para `C:\Program Files\Projeto JP`; quem já tinha continua na pasta antiga.
- **Não mudou:** o arquivo da API na VPS (`Erp.Api`) e o serviço `erp-nf-api`; as pastas de dados
  `C:\ProgramData\ErpNaoFiscal`; o site aceita o instalador com o nome antigo (`Instalar-ERP.exe`) enquanto o novo
  não for enviado para `/opt/erp-nf/downloads/`.

Base: a versão com fiado, devolução, balança, etiqueta de gôndola e envio de usuários aos caixas.
Esta pasta tem **só o sistema** (sem o robô de testes).

## Antes de atualizar

1. Backup na VPS: `sudo -u postgres /usr/local/bin/erp-nf-backup`
2. Gere os pacotes **só** com `powershell -ExecutionPolicy Bypass -File deploy\publicar.ps1`.
   O "Publicar" do Visual Studio gera pacotes que dependem do .NET instalado: a API não sobe na VPS
   sem o .NET e os programas pedem o ".NET Desktop Runtime" em cada computador.
3. Ordem: API → Gestão → caixas. Não crie migration: a API aplica sozinha a atualização **v11** do banco.

## O que foi corrigido

| # | Problema | Correção |
|---|---|---|
| 1 | Usuário criado na Gestão não chegava ao caixa; senha trocada ou usuário desativado continuava entrando no caixa | "Novo usuário do caixa" já envia aos caixas. Troca de senha e desativação valem nos caixas na próxima sincronização, sem precisar enviar. O admin inicial e os usuários que já existiam são enviados na atualização v11 |
| 2 | Estoque do caixa baixava de novo a cada sincronização enquanto havia venda não enviada | A venda pendente é descontada só dos produtos que chegaram naquela carga |
| 3 | Caixa deixava finalizar venda no fiado sem cliente (o servidor recusava depois) | O caixa avisa "Fiado precisa do cliente na venda" e não finaliza. Caixas antigos baixam de novo as formas de pagamento para saber qual é fiado |
| 4 | Cancelar no caixa uma venda que já teve devolução devolvia o estoque duas vezes | O cancelamento devolve só o que ainda não tinha voltado |
| 5 | Devolução escolhia "o primeiro item com quantidade" e o número da venda se repete entre caixas | Tela nova: busca por número e dia, mostra caixa/data/cliente/total, e você escolhe a venda e quanto devolver de cada item |
| 6 | Devolução não abatia o fiado nem saía dos relatórios | Venda no fiado: a devolução abate a dívida dessa venda. Relatórios descontam as devoluções (coluna "Devolvido" na lista de vendas; linha "(devoluções)" no por forma de pagamento) |
| 7 | Saldo do fiado podia perder lançamento com dois caixas ao mesmo tempo; venda fiado de cliente inexistente sumia com a dívida | Saldo somado direto no banco; venda fiado de cliente que não existe fica no caixa (recusada com o motivo) até o cliente existir |
| 8 | Devolução aceitava 0,5 de produto vendido por UN | Só inteiro em UN, CX, PC...; fração em KG, L, M... |
| 9 | Balança: produto em G recebia o peso em kg; etiqueta de preço em produto UN dava quantidade quebrada; "GS" da balança era lido como gramas | Peso convertido para a unidade do produto; etiqueta de preço em UN precisa dar quantidade inteira; a unidade é a que vem logo depois do número |
| 10 | Etiqueta de gôndola usava o preço em edição (não enviado) e ignorava a promoção | Usa o preço que o caixa cobra hoje e a promoção em vigor; avisa quando há preço novo não enviado |
| 11 | "Fiado e devolução" e "Etiqueta de gôndola" não apareciam no menu do modo completo | Estão em Vendas e em Estoque |

## Novo: atacado (preço por quantidade)

- **Produto:** campos *Preço de atacado* e *Atacado a partir de* (ex.: R$ 4,50 a partir de 6). Vai aos caixas com
  "Enviar produtos aos caixas", como o preço normal.
- **Promoções:** tipo **Varejo** (qualquer quantidade) ou **Atacado** (a partir de uma quantidade), com datas. Pode haver
  uma de cada ao mesmo tempo no mesmo produto.
- **PDV:** soma as linhas do mesmo produto; chegando no mínimo, todas saem no atacado (menor preço entre o atacado fixo e
  a promoção de atacado). Mostra ★ ATACADO, avisa a quantidade que falta e imprime "ATACADO (de R$ …)" no cupom.
- **Etiqueta de gôndola:** mostra a linha do atacado.
- **Cadastro de produto novo (janela própria):** blocos Identificação, Preços e Estoque; lucro e margem aparecem enquanto
  digita (no varejo e no atacado; só para quem pode ver o custo) e o atacado fica atrás de "Vende no atacado". Cabe inteira
  em 1366×768. Os outros cadastros usam duas colunas para os campos curtos.
- Banco: atualização **v12** automática (colunas novas). O caixa baixa os produtos de novo uma vez, sozinho.

## Novo: teclas do caixa simplificadas e configuráveis

- **Enter finaliza a venda** (com a busca vazia), como já era: o mais simples para quem não está acostumado.
- O resto em uma tecla (sem Ctrl): F1 ajuda, F2 quantidade, F3 buscar, F4 cliente, **F5 alterar item**, F6 desconto,
  F7–F12 formas de pagamento, Del remover.
- Mudou para quem já usava: só o F5 (antes "próxima forma") agora altera o item (antes Ctrl+E). "Próxima forma" fica sem
  tecla e pode ganhar uma. Dá para escolher também uma "outra tecla para finalizar" (ex.: a TOTAL do teclado de automação).
- **Opções do caixa → Teclado**: troca a tecla de cada ação apertando a tecla desejada (serve para teclado de automação).
  Letras, números, Enter e Esc não podem virar atalho. Vale só para aquele caixa.

## Novo: caixa com visual novo

- **Topo:** logo grande, nome e endereço da loja (do cabeçalho do cupom), terminal, operador, Online e hora.
- **Coluna do total à direita:** total grande, subtotal, desconto, itens, cliente e **"Você economizou"** (promoção,
  atacado e desconto).
- **Pagamento na própria coluna** (Enter): formas F7 a F12 (já vem a primeira), valor recebido, **notas rápidas**
  (Exato, R$ 95, R$ 100...), troco, **dividir em mais de uma forma** (mostra o que já foi pago e quanto falta) e aviso do
  fiado ("vai para a conta de..."). Esc volta para a venda. Enter conclui.
- **Venda concluída:** a coluna mostra o troco grande, total, como pagou, nº da venda e **Reimprimir (Ctrl+P)**; o meio
  mostra "Venda concluída — Obrigado pela preferência!" e a economia, **sem o nome do cliente**. Some ao passar o
  próximo produto.
- **Caixa livre:** o meio mostra as **promoções da loja** trocando sozinhas (as promoções e o atacado já enviados aos
  caixas; sem promoção, a logo grande).
- **Caixa fechado:** o programa sempre abre no **login**. Depois que o operador sai da conta ou fecha o caixa, a tela
  mostra **"CAIXA FECHADO — procure o próximo caixa"** com as promoções; Enter abre o login. "Sair do programa" fica
  embaixo da coluna.
- **F1 = Mais opções:** todas as ações em botões com a tecla de cada uma (sangria, suprimento, orçamento, vendas,
  reimprimir, sincronizar, opções, sair da conta, fechar caixa...). O menu lateral e os botões de baixo saíram.
- **Desconto (F6):** janela com R$ ou %, atalhos 2%, 5%, 10%, "arredondar o total" e o total novo na hora.
- **Alterar item (F5):** mostra o total do item e tem **Remover item**.
- **Telas menores:** se ajusta de 1024×768 a 1920×1080 (coluna mais estreita; somem Código/Unitário, hora e algumas
  teclas do rodapé).
- Nova tecla **Ctrl+P** (reimprimir), trocável em Opções → Teclado.

## Novo: cores (sai o verde)

- Cor padrão agora é **azul-marinho** (fundo cinza-azulado claro e menu azul-noite), no lugar do verde/bege.
  O tema escuro também ficou neutro (sem o tom marrom).
- **Caixa e Gestão:** a cor continua sendo escolhida em Aparência (9 tons prontos ou "Outra cor..."); sem escolha,
  azul-marinho. Quem já tinha escolhido uma cor continua com ela.
- **Projeto JP Master:** botão **Cor** no topo, com tema e cor só dele (não muda os da loja naquele computador).
- Corrigido: escolher o tom "Azul" mostrava o verde.

## Novo: orçamento em folha A4

- **PDV:** em *Imprimir ao salvar* escolha Não imprimir, Cupom (bobina) ou **Folha A4** (o caixa lembra). Em Ctrl+R →
  **Folha A4** mostra a folha antes e deixa escolher a impressora — *Microsoft Print to PDF* salva em PDF para mandar ao cliente.
  A impressora A4 fica em **Opções → Impressora** (em branco = a padrão do Windows).
- **Gestão:** **Vendas → Orçamentos → Imprimir A4** (ou duplo clique no orçamento).
- A folha traz logo e nome da loja (no caixa, também o cabeçalho do cupom), número, data, cliente, validade, atendente,
  caixa, itens (quebra de página sozinha), subtotal, desconto, total e observação.

Também: o guia (`GUIA-INSTALACAO.pdf`) e o `README.md` foram atualizados com as telas novas e os caminhos certos do menu.

## Banco (v11, automático)

- Envia aos caixas os usuários que ainda não tinham sido enviados (como estão hoje).
- Nova coluna `Vendas.ValorDevolvido`.

## Testes de regra

`testes\executar-validacoes.cmd` inclui verificações novas: leitura da balança sem unidade ("GS"), gramas, etiqueta por preço,
valor da devolução com desconto e quantidade inteira em UN.

## Fica para depois (sabido)

- A leitura da balança no caixa espera a resposta da porta (até cerca de 1 segundo) com a tela parada.
- Os executáveis são 64 bits; o `publicar.ps1` também gera Caixa e Gestão 32 bits.

## Caixa: servidor não encontrado e acesso técnico (08/11)

- **Acesso técnico da empresa:** quando o nome em Opções do caixa → Servidor não existe no servidor, o aviso agora diz isso
  (antes dizia "Atualize a API", o que estava errado). Sem internet, explica o motivo e indica a
  "Senha técnica deste computador (sem internet)" para entrar nas Opções e corrigir.
- **Opções do caixa → Salvar:** ao trocar o Servidor ou a chave, o caixa confere antes de gravar. Nome que não existe ou chave
  não reconhecida: avisa e pergunta se quer salvar mesmo assim. Sem internet, salva normalmente (o caixa trabalha offline).
- Lembrete: nomes criados na Configuração técnica valem só no computador onde foram criados. Ao levar o caixa para outra
  máquina, use o mesmo nome da Gestão (ex.: nuvem, ou o código da empresa no Projeto JP Master) ou crie o nome lá também.
- Robô de teste pela tela do caixa: fica em `Z:\ERPRobo` (fora do sistema). O caixa ganhou só nomes internos nos campos
  da tela de venda para o robô achá-los; nada muda para o operador.

## Publicar sem o .NET instalado (08/11)

- `deploy\publicar.cmd` (e o `gerar-robo.cmd` do robô): se o computador não tiver o SDK .NET 10, baixa e instala sozinho da
  Microsoft em `%LOCALAPPDATA%\Microsoft\dotnet` (só na primeira vez, precisa de internet, não precisa de administrador).
- Os programas gerados (Caixa, Gestão, Master, robô) já levam tudo dentro do exe: nos computadores das lojas não se instala nada.

## Caixa: cópia de segurança na rede da loja e rodapé (08/11)

- **Cópia de segurança na rede (opcional):** em Opções do caixa → Sincronização → "Cópia de segurança na rede da loja",
  informe uma pasta compartilhada de outro computador da loja (ex.: `\\RETAGUARDA\CopiasCaixa`) e use "Testar cópia".
  - O caixa guarda ali uma cópia do banco ao abrir, depois de cada venda, sangria/suprimento, orçamento, cancelamento,
    fechamento, no Ctrl+S e ao sair. Funciona sem internet (só a rede da loja).
  - Arquivo: `caixa-NOMEDOPC.db` (e a cópia anterior em `caixa-NOMEDOPC.anterior.db`). Vários caixas podem usar a mesma pasta.
  - A cópia é feita pelo backup do próprio SQLite: o arquivo é sempre íntegro, mesmo com vendas acontecendo.
  - Se a cópia falhar (outro computador desligado, sem permissão), o rodapé do caixa avisa. A venda nunca é bloqueada.
- **Recuperar as vendas de um caixa que estragou:**
  1. No computador novo (ou consertado), instale o caixa e abra uma vez para criar a pasta `C:\ProgramData\ErpNaoFiscal`; feche.
  2. Copie `caixa-NOMEDOPC.db` da pasta da rede para `C:\ProgramData\ErpNaoFiscal\pdv.db` (substitua; apague `pdv.db-wal` e `pdv.db-shm` se existirem).
  3. Abra o caixa e, em Opções, use o **mesmo Servidor e a mesma chave do terminal** do caixa que estragou.
  4. As vendas que não tinham subido vão para o servidor sozinhas.
- **Rodapé da tela de venda:** as teclas (F1 Mais opções, F2 Quantidade, F3 Buscar, F4 Cliente, F5, F6, Del) agora são
  desenhadas num controle só e mostram as que cabem inteiras. A mensagem do status fica numa linha só (o texto
  inteiro aparece passando o mouse).

## Rodapé com etiquetas, vendas recusadas e caixa aberto com outra chave (08/11)

- **Rodapé do caixa:** teclas com fundo (parecem teclas) e a situação em etiquetas coloridas: verde "Sincronizado às …",
  amarelo "N aguardando envio", vermelho "Sem conexão com o servidor" e "N recusadas — ver motivo", mais a impressora.
  Clique na etiqueta: envia na hora, confere a impressora ou abre a lista de recusadas (nº, data, total e motivo, com
  "Tentar enviar agora").
- **API (precisa atualizar a VPS):** venda, sangria/suprimento e fechamento de uma abertura de caixa registrada por
  OUTRO terminal da mesma empresa agora entram (antes: "o caixa (abertura) não pertence a este terminal"). Acontecia ao
  trocar a chave/servidor com o caixa aberto ou ao levar os dados do caixa para outro computador. A venda fica com o
  terminal que a enviou; o log da API registra cada caso. Abertura que não existe no servidor continua esperando.
- **Opções do caixa → Salvar:** ao trocar o Servidor ou a chave com o caixa aberto ou com registros não enviados, avisa
  e pergunta antes (o recomendado é fechar o caixa e esperar a etiqueta verde).

## Configurações por computador (08/11)

- Caixa e Gestão reconhecem o computador (identificação do Windows e da placa-mãe, guardadas só como resumo em
  `C:\ProgramData\ErpNaoFiscal\computador.id`). Se a pasta de dados chegar a outro computador (copiada ou com o
  computador clonado, ex.: máquina virtual copiada), o programa avisa, guarda o que veio em
  `C:\ProgramData\ErpNaoFiscal\de-outro-computador-AAAAMMDD-HHMMSS` e abre como na primeira vez. Nada é apagado.
- O `pdv.json` / `retaguarda.json` antigo ao lado do .exe não é mais lido (ia junto ao copiar a pasta do programa).
- Na primeira abertura desta versão, cada computador só é marcado (não zera nada). Para zerar um computador que já foi
  copiado antes desta versão: com o programa fechado e sem vendas aguardando envio, renomeie a pasta
  `C:\ProgramData\ErpNaoFiscal` (ex.: `ErpNaoFiscal-antigo`).

## Nome do servidor em maiúsculas (08/11)

- O nome do servidor (empresa) aparece e é digitado em MAIÚSCULAS COM ESPAÇO (ex.: `MERCADO BOM PRECO`, `NUVEM`) no
  Projeto JP Master (novo cliente, lista, pedidos, segurança, excluir), na Gestão (tela Servidor, criar cadastro da empresa,
  avisos) e no caixa (Opções → Servidor e avisos). Hífen digitado vira espaço.
- Por dentro nada muda: continua `mercado-bom-preco` no endereço da API, no banco e no que fica gravado. Empresas, caixas
  e Gestãos que já existem continuam funcionando; digitar em minúsculas ou com hífen também vale.
- O texto que o Master copia para mandar à loja já sai em maiúsculas. Caixas e Gestãos de versão anterior só aceitam o
  nome com hífen (ex.: `mercado-bom-preco`): atualize-os junto.

## Instalador do Caixa e da Gestão (08/11)

- Projeto novo `src\ProjetoJP.Instalador` (na solução). O `deploy\publicar.cmd` gera `publicar\instalador\Instalar-ProjetoJP.exe`
  com o Caixa e a Gestão dentro (64 e 32 bits; o Projeto JP Master fica de fora).
- No visual do sistema: Boas-vindas → Programas (Caixa, Gestão ou os dois) → Local (padrão
  `C:\Program Files\ErpNaoFiscal`, com Procurar) e atalhos → Instalando → Pronto (abrir agora).
- Escolhe sozinho 64 ou 32 bits; pede administrador; avisa se o programa estiver aberto; atualiza por cima sem mexer
  em `C:\ProgramData\ErpNaoFiscal`; registra em Aplicativos do Windows com desinstalador (que também não apaga os dados).
- O instalador fica grande (leva os quatro programas compactados): cerca de 250 a 300 MB.

## Site de cadastro e painel online pelo celular (08/11)

- O site fica dentro da própria API (sem programa novo na VPS e sem mudar o Nginx), na pasta
  `src\ProjetoJP.Api\wwwroot\site`. Endereços depois do `/nf/`: `/` (apresentação, cadastro e download), `/acompanhar`
  (situação do cadastro), `/painel` (painel da loja), `/privacidade` (aviso LGPD) e `/baixar` (instalador).
- **Cadastro:** mesmos campos e mesma regra do cadastro da Gestão; o pedido cai em Pedidos de cadastro no Projeto JP Master.
  Nome do servidor em maiúsculas com "Disponível" na hora. Campo escondido contra robô (`Site` no pedido; preenchido =
  descartado em silêncio), além do limite por IP que já existia. Depois de enviar, a pessoa recebe o link
  `acompanhar#<id>.<código>` (o código fica depois do `#` e não vai para os logs).
- **Painel (só consulta):** entra quem tem a permissão Ver vendas, com servidor, usuário e senha da Gestão. Telas
  Início, Vendas, Mais vendidos e Estoque baixo; períodos Hoje, Ontem, 7 dias e Mês (datas da loja). Sair encerra a
  sessão no servidor. O acesso fica só na aba (sessionStorage); servidor e usuário podem ficar lembrados.
- API: consulta nova `GET /api/relatorios/painel?periodo=hoje|ontem|7dias|mes` (permissão Ver vendas) com o resumo do
  Início; `mais-vendidos` passou a mandar a unidade (escondida na Gestão); `GET /site/info` (data e tamanho do
  instalador e contato); cabeçalhos de segurança só nas páginas do site (CSP só com arquivos do próprio site).
- Nomes reservados novos (não podem ser nome de servidor): `site`, `painel`, `acompanhar`, `privacidade`, `baixar`.
- `appsettings.json` → `Site`: `PastaDownloads` (padrão `/opt/erp-nf/downloads`), `WhatsApp` e `Email` do suporte
  (vazio = não aparece). O `Instalar-ProjetoJP.exe` vai à mão para `/opt/erp-nf/downloads/` (veja o guia).
- A imagem do caixa na página inicial é `wwwroot\site\caixa.jpg`: troque por um print real do seu caixa quando quiser
  (mesmo nome).

## Painel conforme a tela e download só para loja liberada (08/11)

- **Painel online se ajusta à resolução:** computador (1024 px ou mais) com menu lateral, cartões lado a lado,
  gráfico largo e tabelas (Início mostra também Por caixa, Últimas vendas e Estoque baixo); tablet com duas colunas;
  celular como antes (abas embaixo). Nova opção **Instalador** no painel.
- **Download só para loja liberada:** o endereço `/baixar` só entrega o arquivo com um link assinado (bilhete HMAC
  derivado da `Jwt:Chave`, vale 30 minutos) que confere se a loja está ativa. O link é gerado em
  `POST /cadastro/{id}/instalador?token=...` (página de cadastro aprovado; exige pedido aprovado) e em
  `POST /api/instalador/link` (Painel online, exige login da loja). A página inicial explica que o instalador é
  liberado após a aprovação e leva ao painel.

## Atualização automática, aprovação ao vivo, cadastro único, exportar e pagamento (08/11)

- **Atualização automática do Caixa e da Gestão** (`Erp.Controles\AtualizacaoAutomatica.cs`): o `publicar.ps1`
  grava a versão (`-p:Version=aaaa.m.d.HHmm`) e gera `publicar\atualizacao` (`versao.json` + `caixa-x64.zip`,
  `caixa-x86.zip`, `retaguarda-x64.zip`, `retaguarda-x86.zip`), que vai para `/opt/erp-nf/downloads/atualizacao`.
  O Caixa pergunta ao abrir (antes do login; chave do terminal em `/api/sync/atualizacao`) e a Gestão logo depois
  de entrar (login em `/api/atualizacao`). Sim = baixa, confere tamanho e SHA-256, renomeia o .exe aberto para
  `.antigo`, coloca o novo e abre de novo; o `.antigo` é apagado na próxima abertura. Programas compilados sem o
  publicar (versão 0.1.0) não se atualizam.
- **Instalador:** libera a escrita da pasta do sistema para o grupo Usuários (`icacls ... *S-1-5-32-545:(OI)(CI)M`),
  para a atualização não pedir administrador. Lojas já instaladas: rodar o instalador novo uma vez.
- **Aprovação ao vivo:** a página de acompanhamento confere a cada 10 segundos (e ao voltar para a aba) e muda para
  "Cadastro aprovado" sozinha, sem recarregar; depois de aprovada para de consultar.
- **Cadastro único:** o link "Criar cadastro da empresa" da Gestão abre o site (`#cadastro`). A tela antiga
  (`CadastroEmpresaForm.cs`) não é mais usada e pode ser apagada; pedidos antigos pendentes continuam sendo conferidos.
- **Painel online:** "Exportar planilha" (CSV com `;`, vírgula decimal e BOM, para o Excel) em Vendas e Mais vendidos
  no computador; forma de pagamento de cada venda (campo `Pagamento` novo em `VendaResumoDto`, também na Lista de
  vendas da Gestão; escondido no Início).
