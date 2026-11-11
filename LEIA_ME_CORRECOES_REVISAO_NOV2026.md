# Correções da revisão (novembro/2026)

## Sincronização do Caixa
- **Uma venda com problema não trava mais as outras.** O servidor grava as vendas boas do lote e devolve as
  recusadas em `VendasRecusadas` (com o motivo). O Caixa guarda o motivo (colunas `erro_sync` / `erro_sync_em`
  no SQLite, criadas sozinhas), continua enviando as vendas novas, mostra no status
  "Venda nº X recusada pelo servidor: motivo" e tenta a recusada de novo a cada 30 minutos.
  Cancelar a venda no Caixa limpa o erro e ela sobe na hora.
- Caixa já fechado enviado como aberto, sangria de outro terminal e orçamento inválido viram **aviso** e não
  derrubam o lote inteiro.
- **Conflito de estoque entre caixas** (dois caixas vendendo o mesmo produto ao mesmo tempo): o servidor tenta
  de novo sozinho (até 4 vezes) em vez de responder erro — o Caixa não mostra mais "OFFLINE" por isso.
- O preço de custo não é mais enviado aos caixas (o Caixa não usa).

## Estoque
- A saída pela tela **Movimentação de estoque** não deixa mais o estoque negativo. Para isso, use
  **Saída de produtos** com "Permitir estoque negativo" (que exige a permissão e uma justificativa).

## Relatórios
- **Lista de vendas**: o total e a quantidade do rodapé agora vêm do período inteiro
  (`/api/relatorios/vendas-totais`). Quando há mais de 2.000 vendas, aparece
  "N venda(s) no período — mostrando as 2.000 mais recentes".

## Outros
- Caixa: erro ao consultar a impressora na hora de finalizar não interrompe mais a venda.
- ERP Master (`/master`): limite de 600 pedidos a cada 10 minutos por IP.

## Compatibilidade
- Atualize **a API primeiro**. Caixa novo com API antiga continua funcionando (só não recebe a lista de
  recusadas). Caixa antigo com API nova também funciona.
- Robô de testes atualizado: "venda com pagamento errado é recusada" e o novo passo
  "venda com problema não trava as outras".
