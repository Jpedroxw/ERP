# ERP V6 — correções antes dos testes

Base: `ErpNaoFiscal(5).zip` enviado pelo usuário. Este pacote NÃO contém emissão fiscal nem altera as interfaces/ícones.

## Melhorias implementadas

1. **Entradas:** retirado `MAX(Numero) + 1` e adicionada sequência PostgreSQL por banco, com índice único para `Entradas.Numero`. O número é reservado em `nextval` e não volta após cancelamento. Pode haver lacunas quando uma operação falhar — isso é normal em sequências.
2. **Importação:** conflitos de códigos após normalização geram aviso e a linha é ignorada; a importação também evita sobrescrever um produto existente apenas porque seu código normalizado ou GTIN coincide. Linhas repetidas dentro do lote são ignoradas. Códigos somente com símbolos são rejeitados.
3. **Atualização do banco:** nova tabela `__ErpSchemaVersoes` em cada banco e `__ErpMasterSchemaVersoes` no Master; as alterações conhecidas são transacionais, registradas com versão e não reaplicadas em cada inicialização. O serviço verifica algumas colunas essenciais e falha explicitamente se faltar estrutura necessária. Essas são **migrações incrementais do ERP**, não uma conversão completa para migrations do Entity Framework.
4. **Conflito de estoque nas entradas:** alterações concorrentes retornam HTTP 409 com mensagem apropriada para a Retaguarda; o lançamento não é parcialmente confirmado.
5. **Testes adicionados:** `Erp.TestesImportacao` (regras puras) e `Erp.TestesBanco` (migrações/numeração, opcional, apenas em banco de teste).

## Ajustes da revisão

- **Teste de banco (`Erp.TestesBanco`)**: a consulta do `nextval` levava barras invertidas para dentro do SQL
  (`'\"Entradas_Numero_seq\"'`) e falharia no PostgreSQL; corrigido nas duas chamadas.
- **Reimportar a planilha volta a atualizar produtos com código ajustado**: antes, um produto importado como
  `REF@1` (gravado `REF1`) nunca mais era atualizado por importação. Agora atualiza quando a descrição é a mesma
  (é o mesmo produto); com descrição diferente continua bloqueado, como no teste da V6.
- **Código de barras**: GTIN comum (ex.: 789…) é único no mundo — a linha atualiza o produto que já tem esse código
  de barras, com aviso mostrando o código usado no ERP. Só os de uso interno (EAN-13 começando por 2, UPC-A por 2 ou 4,
  EAN-8 por 0 ou 2: balança/etiqueta da loja) continuam bloqueados quando o código é diferente.
- Aviso de código só com símbolos não diz mais "será gerado um automático" (a linha é rejeitada, como a V6 decidiu).
- `Erp.TestesImportacao`: contagem automática e 5 testes novos (reimportação e GTIN interno/comum).

## O que NÃO foi alterado

- WinForms do Caixa, da Retaguarda e do Master, ícones `.ico`, visual e preferências.
- `deploy/instalar-servidor.sh`, `deploy/publicar.ps1`, PostgreSQL instalado, nem dados de usuários.
- Cargas e promoções existentes; integrações fiscais continuam ausentes.

## Como compilar no Windows

Abra PowerShell na pasta da solução:

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
.\deploy\publicar.ps1
```

Só depois de `PUBLICACAO CONCLUIDA` e dos testes na empresa fictícia publique a API da pasta `publicar\api` na VPS. Este ZIP é código-fonte, não um executável já compilado.

### Testes automáticos sem alterar banco

```powershell
dotnet run -c Release --project .\testes\Erp.TestesValidacao\Erp.TestesValidacao.csproj
dotnet run -c Release --project .\testes\Erp.TestesImportacao\Erp.TestesImportacao.csproj
```

### Teste de numeração PostgreSQL (opcional, avançado)

O projeto `Erp.TestesBanco` requer `ERP_TEST_PG_CONN` (string de conexão de um banco de testes cujo nome comece por `erp_c_bot` ou `erp_nf_teste`) e `ERP_TESTE_BANCO_CONFIRMAR=SIM_TESTE`. Ele cria/aplica a estrutura de teste e **consome 21 números da sequência**, sem registrar entradas. NÃO executar em bases reais. Veja `testes/CENARIOS_REGRESSAO_V6.md`.

## Atualização da VPS Debian 13

- **Não executar `instalar-servidor.sh` novamente.**
- Somente API precisa ser substituída, depois da compilação e validação na empresa de testes.
- Não substituir `/etc/erp-nf/api.env` e não apagar os bancos.
- Uma alteração de esquema de dados sempre pode falhar por dados históricos inesperados: recomenda-se ter ponto de restauração do banco de testes antes de mudar a API.
- Pare o serviço antes de substituir os arquivos. Após atualizar, reinicie `erp-nf-api` e verifique `/health`, logs e acesso do Master, da Retaguarda e dos Caixas.
- Se houver números repetidos em `Entradas`, a migração vai FALHAR e solicitar correção manual. Nunca resolve sozinha apagando ou renumerando entradas.

## Limites da validação

Executados: integridade e estrutura do ZIP, XML dos projetos/solução, resolução das referências, comparação dos `.ico` e scripts, inspeção estática de sintaxe dos arquivos C# alterados (checagem aproximada de delimitadores), e verificação do fluxo de patches.

**Não executados neste ambiente:** `dotnet build`, PostgreSQL/Npgsql, Windows Forms, autenticação real, stress simultâneo, nem a execução dos novos testes .NET. Não declarar a versão homologada até concluir essas etapas no Windows e no banco de testes. Consulte o checklist em `testes/CENARIOS_REGRESSAO_V6.md`.
