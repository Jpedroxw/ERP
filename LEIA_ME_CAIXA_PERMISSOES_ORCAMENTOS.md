# Caixa novo, permissões, orçamentos e saídas por caixa

## Caixa (PDV) — visual novo
- Menu azul à esquerda: loja, terminal, operador, data, hora, Online/Offline e a mensagem da sincronização.
  Embaixo: Vendas do caixa (Ctrl+L), Orçamentos (Ctrl+R) e Sair da conta.
- **Busca com lupa e sugestões**: digite parte do nome, código ou código de barras; a lista aparece embaixo
  (aceita erro de digitação). ↑ ↓ e Enter, ou clique. Código de barras exato entra direto. `3*código` continua valendo.
- **Quantidade (F2)** com − e +; vale para o próximo produto.
- **Alterar item**: duplo clique ou Ctrl+E → quantidade (livre) e preço (pede autorização de quem pode alterar preço).
  Item com preço alterado aparece em laranja com "(preço alterado)".
- **Desconto (F6)** em % ou R$ (Enter aplica; 0 tira). Pede autorização de quem pode dar desconto.
- **Formas de pagamento F7 a F12** (as 5 primeiras + a 6ª ou "Outros"), valor recebido e troco na própria tela.
  "Dividir em mais de uma forma" abre a tela de pagamento de antes.
- **Finalizar**: botão, Enter no valor recebido ou Enter com a busca vazia. Sem forma escolhida, abre a tela de pagamento.
- Rodapé: Sincronizar (Ctrl+S), Sangria, Suprimento, Cancelar venda, Fechar caixa.
- **F1** mostra todos os atalhos. Del remove o item selecionado. Ctrl+O = opções do caixa.

## Orçamento (aba no caixa)
- Aba "Orçamento" no topo: monte os itens, validade (dias) e observação; "Salvar orçamento" grava, numera e imprime.
- Ctrl+R lista os orçamentos do caixa: Vender (coloca na venda), Alterar, Imprimir, Cancelar.
  Orçamento vencido pergunta se vende com os preços de hoje. Ao finalizar a venda ele fica "Vendido".
- Funciona sem internet; sobe na sincronização. Na Retaguarda: Vendas → Orçamentos.

## Permissões por usuário
- Retaguarda → Configurações → Usuários → botão **Permissões** (Admin, com senha confirmada).
- Retaguarda: ver custo, ver valores de vendas, cadastrar/alterar produtos, saídas/ajustes, entradas, cargas,
  promoções, clientes, fornecedores, orçamentos, importar.
- Caixa: alterar preço, desconto, remover item, cancelar venda, sangria, orçamento, opções.
  No caixa, quem não pode pede a senha de alguém que pode.
- Padrão = o comportamento de antes (Gerente tudo; Operador: ver custo, produtos, estoque, clientes,
  remover item e orçamento). Admin pode tudo sempre.
- Na Retaguarda vale na hora (o servidor confere em cada pedido; menus mudam no próximo login).
  No caixa vale depois da próxima sincronização.

## Saídas por caixa e atualização automática
- Movimentações mostram a **Origem**: "CAIXA 2 · Venda nº 15" ou "Retaguarda · motivo"
  (painel de detalhes do produto e histórico do Estoque). O painel inicial mostra o caixa de cada venda.
- O caixa envia a venda logo após finalizar; Produtos e Estoque, Estoque, Dashboard e Orçamentos
  **atualizam sozinhos** (30 s / 60 s), sem janela de erro se a internet cair.

## Servidor
- Migração **versão 4**: coluna `Usuarios.Permissoes`; tabelas novas `Orcamentos` e `OrcamentoItens`.
- Faça backup dos bancos antes de atualizar a API. Atualize API, Retaguarda e caixas juntos.
