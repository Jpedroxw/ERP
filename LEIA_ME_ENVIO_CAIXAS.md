# Envio aos caixas, movimentações e interface (04/11)

## Enviar produtos aos caixas (antes "Cargas para caixas")
Revisão do funcionamento:
- **Corrigido:** produto inativo que nunca foi aos caixas aparecia como pendente para sempre
  (e a tela de Produtos contava diferente). Agora as duas contam igual.
- Datas ("Último envio", "Última sincronização") formatadas no horário local direto na tela (a grade já convertia; ficou explícito).
- **Novo:** coluna "O que muda no caixa" (ex.: "preço R$ 5,00 → R$ 5,50 · descrição", "produto novo", "desativado").
- **Novo:** filtro por código/descrição; duplo clique envia um produto.
- **Novo:** a lista "Caixas — já receberam o último envio?" atualiza sozinha a cada 20 segundos.
- **Novo:** ao salvar um produto em Produtos e Estoque, pergunta "Enviar para os caixas agora?". Responder Sim envia na hora,
  sem precisar abrir a tela de envio. Quem não tem a permissão de enviar só recebe o aviso.
- **Caixa:** a pesquisa de produtos do caixa passa a enxergar o que chegou logo depois de cada sincronização
  (antes podia levar até 1 minuto a mais).
- A tela mudou de nome: **Caixas → Enviar produtos aos caixas**.

Como funciona: salvar na Retaguarda não muda o caixa; só o envio muda. Estoque não depende do envio.
Cada caixa baixa na sincronização automática (intervalo em Opções do caixa) ou na hora com Ctrl+S.

## Atualização
Atualize a **API**, a **Retaguarda** e o **Caixa**. Não há mudança no banco além da v5 (Saídas).

## Modo simples (Retaguarda)
O modo simples continua existindo. Agora ele também aparece em:
- **Configurações → Aparência**: novo campo "Modo de exibição" (Simples / Completo). Troca na hora, sem reabrir.
- **Configurações → Mudar para o modo simples**: atalho direto no menu lateral.
Continua também no menu do usuário (nome no canto superior direito) e em "Interface e navegação".
Para voltar, use o botão "Modo completo" no topo do modo simples.

## Estoque: movimentações de todos os produtos (04/11)
Estoque → **Movimentação de estoque** agora abre mostrando as movimentações de **todos os produtos**:
- vendas de cada caixa (com o nome do caixa em "Origem"), vendas canceladas, entradas, saídas e ajustes;
- filtros: **De / até** (padrão: últimos 30 dias), **Tipo** e **Todos os produtos / Só o produto escolhido**;
- até 3.000 linhas mais recentes (se passar, avisa para diminuir o período); **Exportar** para CSV (Excel);
- ao escolher um produto lá em cima, a lista passa a mostrar só ele (volte em "Todos os produtos");
- duplo clique numa linha escolhe aquele produto; atualiza sozinho a cada 30 segundos.

## Caixa: aviso de impressora (04/11)
- No menu lateral do caixa aparece a situação da impressora (conferida a cada 30 s):
  "● Impressora pronta", "▲ IMPRESSORA COM PROBLEMA (clique)" ou "Sem impressora (cupom salvo em arquivo)".
  Clicando, mostra o motivo: não encontrada, desligada/desconectada (offline), sem papel, papel preso, tampa aberta, pausada, com erro,
  e quantas impressões estão paradas na fila do Windows.
- **Ao finalizar a venda** (antes de receber o pagamento), se a impressora estiver com problema, pergunta:
  "Finalizar a venda mesmo assim, SEM imprimir o cupom?" — Não volta para a venda; Sim grava sem imprimir.
- Se a impressão falhar depois de gravar a venda, oferece **Tentar imprimir de novo** (ex.: depois de ligar a impressora).
  A 2ª via continua em Vendas do caixa (Ctrl+L).
- Algumas impressoras térmicas USB não avisam "sem papel" ao Windows; desconectada, o Windows normalmente marca como offline.

## Revisão geral (04/11)
Conferido em todo o código (sem compilador disponível aqui: conferência estática):
chaves/parênteses de todos os arquivos, referências a Tema/Icones/Ui/Formatos/Permissoes/Repositorio/ApiClient,
campos usados x declarados, métodos duplicados, nomes de telas e mensagens antigas.
Corrigido nesta revisão:
- **Retaguarda**: conexão que cai no meio da resposta, resposta que não é da API (página de erro do provedor) ou demora
  na leitura viravam "erro inesperado"; agora aparecem como aviso normal com a explicação.
