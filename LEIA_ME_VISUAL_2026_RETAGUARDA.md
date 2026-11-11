# Visual 2026 — Retaguarda (etapa 1)

## O que mudou
- **Cores**: azul mais vivo nos botões e na seleção; menu azul-marinho (vale para Retaguarda, caixa e Master).
- **Menu lateral novo** (modo completo, menu lateral): grupos que abrem e fecham (Vendas, Estoque, Configurações),
  item ativo em bloco azul, ondas decorativas no rodapé.
- **Barra superior**: "Empresa:" (nome da Aparência ou o nome do servidor, nunca o site), pesquisa geral
  (**Ctrl + F**: acha telas e produtos), situação "Conectado/Sem conexão", menu do usuário (modo simples,
  computadores, sessões, sair da conta) e a data.
- **Produtos e Estoque** (substitui a lista de Produtos no modo completo):
  - resumo: produtos cadastrados (+ novos no mês), estoque baixo, vendas do dia (vs. ontem), pendentes de carga;
  - lista com filtros (busca por nome/código/código de barras, categoria, status), ordenação pelos títulos
    e páginas (10/20/50/100);
  - painel de detalhes: código, categoria, unidade, código de barras, estoque/custo/preço,
    últimas movimentações e gráfico de vendas × estoque dos últimos 30 dias.
- **Fornecedores**: cadastro novo (gerente/admin). Na Entrada de produtos o campo Fornecedor sugere os cadastrados.
- **Erros ao abrir**: Master e Retaguarda mostram a mensagem e gravam
  `%LOCALAPPDATA%\ErpNaoFiscal\logs\<programa>-inicializacao.log`.

## Servidor (API)
- Migração **versão 3**: coluna `Produtos.CriadoEm`; tabela `Fornecedores`.
- Endpoints novos: `GET /api/produtos/resumo`, `GET /api/produtos/pagina`,
  `GET /api/produtos/{id}/serie?dias=30`, CRUD `/api/fornecedores`.
- Faça backup dos bancos antes de atualizar (a migração roda ao iniciar a API).

## Como testar
1. `deploy\publicar.cmd`, depois `testes\executar-validacoes.cmd`.
2. Teste de banco (só em banco descartável): deve passar com a versão 3.
3. Retaguarda em modo completo + menu lateral: abrir grupos, Ctrl + F (tela e produto), Produtos e Estoque
   (filtros, páginas, ordenar, ajustar estoque, exportar), Fornecedores, Entrada com sugestão de fornecedor.
4. Modo simples e menus Superior/Compacto continuam com o visual anterior (só as cores mudam).

## Próximas etapas
- Dashboard, Vendas e Financeiro no mesmo visual.
- Caixa (PDV) no layout do modelo com F7–F12 para formas de pagamento.
- Master (Clientes e Ambientes com painel de detalhes; mostra o nome do servidor, não o site).
