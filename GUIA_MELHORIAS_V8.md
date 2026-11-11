# ERP V8 — Segurança, sincronização e desempenho

Esta versão parte do arquivo `ErpNaoFiscal(8).zip`, mantendo os executáveis/projetos existentes e os ícones originais. Trata-se de uma **versão de testes**: a revisão estática e a integridade do ZIP foram conferidas, mas é necessário compilar e validar em Windows/.NET 10, PostgreSQL e SQLite antes de atualizar clientes.

## Mudanças incluídas

1. **Permissões:** perfil Operador novo/sem regras personalizadas não recebe, por padrão, permissão de ver custos, editar produtos ou movimentar estoque. Admin e Gerente preservam a configuração padrão ampla; permissões explicitamente salvas não são substituídas. Corrigido o caso em que um conjunto de permissões intencionalmente vazio era interpretado como permissão padrão no cliente.
2. **Orçamentos offline:** inclusão da coluna `Revisao` (PostgreSQL e SQLite). Ao alterar ou converter orçamento, a revisão cresce; o servidor rejeita versões antigas e não reabre orçamento convertido ou cancelado. Conflitos permanecem pendentes e geram aviso, para revisão manual.
3. **Vendas sincronizadas:** servidor verifica identificadores, produto/forma de pagamento, terminal, situação, quantidade, preço, subtotais, descontos e soma dos pagamentos. Reenvios incompatíveis com venda já recebida geram conflito em vez de alterarem totais antigos.
4. **Banco:** nova migração 6 para a revisão dos orçamentos e migração 7 para registrar bootstrap de estruturas antigas dentro de transação. Não reinstalar o Debian e não apagar bancos. Migrações antigas são mantidas. Backup recuperável é indispensável.
5. **Produtos/Estoque:** API oferece `GET /api/produtos/pagina`, com filtros, ordenação e página de até 100 produtos. A tela da Retaguarda passa a consultar somente a página em uso. Exportar tudo continua sendo uma consulta completa por solicitação expressa. A busca aproximada limitada é aplicada apenas quando não há resultados literais.
6. **Estoque negativo:** permissão própria `permitir_estoque_negativo` + confirmação recente da senha + justificativa de 10 a 150 caracteres; tela inclui o campo somente quando necessário. Justificativa é anexada à observação da saída.
7. **Testes adicionais:** `testes/Erp.TestesRegrasV8` com testes de regras de orçamentos, vendas e permissões; projeto incluído na solução.

## Ordem de atualização no ambiente de testes

1. Faça backup **recuperável** do banco Master, dos bancos das empresas e dos bancos locais SQLite dos caixas, principalmente se houver vendas/orçamentos offline pendentes.
2. Extraia a pasta `ErpNaoFiscal` em diretório novo no Windows.
3. Execute, na raiz do projeto:
   ```powershell
   dotnet restore .\ErpNaoFiscal.slnx --force-evaluate
   dotnet build .\ErpNaoFiscal.slnx -c Release --no-restore
   dotnet run -c Release --project .\testes\Erp.TestesRegrasV8\Erp.TestesRegrasV8.csproj
   ```
4. Somente se compilar sem erros, execute `Set-ExecutionPolicy -Scope Process Bypass -Force; .\deploy\publicar.ps1`.
5. Atualize **a API no servidor de testes primeiro** (novos contratos e rotas) e confirme o log de migrações. Depois atualize Retaguarda e PDV de testes. **Não execute `instalar-servidor.sh` novamente.**
6. Valide os fluxos abaixo com uma empresa fictícia e dados descartáveis.

## Testes de regressão recomendados

- Operador sem permissões customizadas não vê custo, não edita produto, não lança saída; Admin pode conceder ou revogar permissões.
- Salvar e sincronizar orçamento, alterá-lo offline, reenviar pacotes na ordem inversa; orçamento convertido/cancelado nunca deve voltar a "aberto".
- Enviar uma venda com soma de pagamentos diferente do total; API deve recusá-la sem gravar parte do lote. Reenvio idêntico deve ser idempotente.
- Fazer venda offline, reiniciar o PDV, sincronizar e conferir estoque e pagamentos. Cancelar a venda e conferir estorno único.
- Tentar deixar estoque negativo sem permissão; depois com permissão, senha recente e justificativa; conferir texto salvo no histórico.
- Pesquisar e paginar 1.000+ produtos, com filtros/códigos, exportar planilha.
- Simular falha de migração em banco **descartável**; serviço deve interromper a inicialização e não aplicar atualização parcial.

## Limitações verificadas

- **Compilação/execução não validadas neste ambiente:** não há SDK .NET 10 nem PostgreSQL/Windows configurados aqui. A inspeção de código e a integridade dos arquivos **não substituem** os testes executados.
- A consulta paginada exige que API e Retaguarda estejam ambas atualizadas.
- Se um orçamento entra em conflito, a API mantém o registro local pendente; revisar manualmente e reconciliar os dados antes de tentar eliminá-lo ou reenviá-lo.
- A busca aproximada procura apenas um conjunto limitado de candidatos no servidor. A exportação em lote ainda busca catálogo inteiro por ação explícita.
- Uma alteração de esquema demanda backup dos bancos, plano de reversão e teste de migração sobre cópia.
