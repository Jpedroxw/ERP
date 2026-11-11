# ERP V8 — Remoção definitiva das fotos dos produtos

A funcionalidade foi retirada **por completo**, a pedido do usuário, no ambiente de teste.

## O que foi removido

- Botões, miniaturas e opções de fotos no cadastro e na pesquisa de produtos da Retaguarda.
- Seleção manual e busca automática pela internet, incluindo telas, downloads, integrações e métodos de API.
- DTO e entidade `ProdutoFoto`, e o mapeamento `ProdutoFotos` do Entity Framework.
- Tabela PostgreSQL `ProdutoFotos` **e seus registros**, usando a **migração de banco versão 8**.
- Chamadas de fotos após cadastro e importação, e os textos que sugeriam essa função.

A migração executa `DROP TABLE IF EXISTS "ProdutoFotos"` **somente no banco de cada empresa** durante sua preparação pela API. É uma exclusão definitiva dos registros de fotos dessa tabela. Não remove produtos, preços, estoques, vendas, orçamentos nem personalização do visual (ícones, logo e plano de fundo). O banco Master não é alterado por essa migração.

## Publicação e testes

1. Em uma cópia/ambiente de testes, compile `ErpNaoFiscal.slnx` com .NET 10 e depois execute `deploy/publicar.cmd`.
2. **Faça backup antes de instalar a API:** fotos antigas serão apagadas definitivamente na inicialização do banco da empresa.
3. Atualize a API na VPS e a Retaguarda, pois a versão antiga ainda utiliza rotas removidas.
4. Verifique Produtos e Estoque, cadastrar/editar/importar/exportar produtos, movimentação e gráfico.
5. Confira, no banco de teste, a versão 8 registrada em `__ErpSchemaVersoes` e a inexistência da tabela `ProdutoFotos`.
6. As antigas rotas `/api/produtos/fotos` e `/api/produtos/{id}/foto` devem responder 404 após atualização da API.
7. Configurações de fotos existentes em `/etc/erp-nf/api.env` (`Fotos__Contato`, `Fotos__PixabayChave`, `Fotos__CosmosToken`) não são mais usadas; podem ser removidas opcionalmente. Não é necessário adicionar nenhuma linha ao Linux.

**Validação efetuada aqui:** integridade e checagens estáticas. A compilação real .NET 10 e os testes em PostgreSQL/Windows ficam pendentes no computador de testes.
