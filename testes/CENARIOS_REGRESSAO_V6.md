# Cenários de regressão — ERP V6 (executar somente na empresa fictícia)

## Compilação e smoke tests

1. Execute `dotnet build .\ErpNaoFiscal.slnx -c Release` ou `deploy\publicar.ps1` na raiz da solução.
2. Execute `dotnet run -c Release --project testes/Erp.TestesValidacao/Erp.TestesValidacao.csproj`.
3. Suba a API no ambiente de testes. Verifique `/nf/health` e logs do serviço.
4. Execute o projeto `testes/Erp.TestesBanco` **somente com `ERP_TEST_PG_CONN` apontando para um banco `erp_c_bot*` ou `erp_nf_teste*`** e `ERP_TESTE_BANCO_CONFIRMAR=SIM_TESTE`. O teste reserva números, sem criar entradas.

## Entradas concorrentes

- Em duas sessões da Retaguarda, cadastre entradas quase simultaneamente e confira números diferentes.
- Confira `SELECT "Numero", COUNT(*) FROM "Entradas" GROUP BY "Numero" HAVING COUNT(*) > 1;` (nenhuma linha).
- Cancele uma entrada e cadastre outra: a numeração não pode voltar para trás.
- Execute novamente a preparação do banco: os números da sequência não podem ser reiniciados.
- Se o banco legado tiver números repetidos, a atualização deve falhar com mensagem clara, sem correção automática.

## Importação de códigos

- Importe linhas com `REF@1` e `REF#1`: ambas geram `REF1`; a segunda deve ser rejeitada como colisão.
- Tenha previamente `REF1` e importe `REF@1` com `AtualizarExistentes=true`: `REF1` não pode ser sobrescrito.
- Importe duas linhas com o mesmo código: a segunda deve ser ignorada com aviso.
- Código original apenas com símbolos (`@@@`) não deve gerar automaticamente produto.
- Importe um código legítimo que já exista com `AtualizarExistentes=true`: a atualização permitida deve continuar funcionando.
- Importe `REF@1` (vira `REF1`), mude o preço na planilha e importe de novo: o mesmo produto deve ser atualizado.
- Produto já cadastrado com GTIN 789… e código diferente: a importação atualiza esse produto (com aviso).
- GTIN interno (2…) igual ao de outro produto com código diferente: a linha é rejeitada.
- Repita em modo de simulação: nenhuma alteração deve ser persistida.

## Migrações e compatibilidade

- Novo banco de teste: todas as tabelas/índices e tabelas de versão devem existir.
- Banco de teste vindo da V5: a versão V6 deve criar sequência e índice único sem apagar dados.
- Após segundo reinício, não devem ser reaplicadas as migrações versionadas.
- O mesmo vale para `erp_master` e bancos ativos de clientes fictícios.
- Inspecione os logs da API para bancos que não receberam a atualização.

## Demais módulos

- Validar login e senha, dispositivos lembrados e revogação de sessão na Retaguarda.
- Verificar cadastro/edição de produtos e publicação por carga.
- Testar promoções antes, durante e depois do período, inclusive com PDV offline.
- Confirmar vendas e cancelamentos simultâneos com saldo coerente.
- Testar navegação nas duas disposições de menu, escala de 125% e encerramento/reabertura dos aplicativos.

**Importante:** os testes de interface, de carga e de PostgreSQL real não foram executados no ambiente de preparação do pacote. Só a execução no Windows/VPS de testes comprova esses comportamentos.
