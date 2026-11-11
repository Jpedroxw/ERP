# ERP V6 — ajustes de navegação, estoque, logout e diagnóstico do Caixa

Base: `ErpNaoFiscal(6).zip` enviado pelo usuário. **Pacote de testes, ainda não homologado.**

## 1. Pesquisa de estoque

Em Retaguarda > Estoque há um campo separado **Pesquisar produto**. Digite o nome inteiro, parte do nome, nome com ou sem acentos, código ou código de barras. O campo **Selecione o produto encontrado** mostra até 120 resultados por vez, ordenados por relevância. Para produtos fora da lista, digite mais palavras. Nomes parecidos com pequenos erros de digitação também aparecem (ex.: `aroz` encontra `ARROZ`).

**Segurança:** digitar não seleciona produto para movimentação. Selecione explicitamente o item da lista, confira o saldo e só então lance a quantidade. Após o lançamento, o saldo e o histórico são atualizados.

## 2. Menu, usuário e Logout

- Menu lateral do Master, Retaguarda e Caixa usa alinhamento vertical estável para evitar rolagem horizontal ao aparecer a barra vertical.
- No modo completo da Retaguarda, o nome/perfil aparecem centralizados no rodapé do menu; no modo simples, o nome acompanha a largura livre do cabeçalho.
- **Sair da conta** na Retaguarda retorna ao login e tenta revogar a sessão atual no servidor. A marcação de *computador lembrado* permanece, pois o usuário pode removê-la separadamente em Configurações.
- O Caixa ganhou **Sair da conta** no menu e na tecla F11. A ação preserva o caixa aberto e abre o login para outro operador; uma venda em andamento precisa ser finalizada ou cancelada antes de sair.
- No ERP Master não existe login individual ao entrar no aplicativo; portanto, nele não foi criado um Logout fictício. A opção de conexão continua disponível.

## 3. Caixa que não abre

No início de execução, o Caixa registra as etapas em:

`%LOCALAPPDATA%\ErpNaoFiscal\logs\caixa-inicializacao.log`

Se o aplicativo abrir e falhar durante a inicialização, uma mensagem aponta para esse arquivo. A primeira sincronização agora mostra uma janela de progresso sem congelar a interface, mas **a instalação inicial continua exigindo internet** para baixar usuários/produtos; depois, o SQLite permite vender offline.

Se **nenhuma mensagem aparecer e nenhum log for criado**, o processo pode nem ter iniciado no .NET. Confirme arquitetura do PC (`publicar\pdv` para Windows x64; `publicar\pdv-32bits` para x86), antivírus e permissões do Windows. Não apague nem renomeie `C:\ProgramData\ErpNaoFiscal\pdv.db` ou `pdv.json`.

## 4. Importação conservadora

Na V6 enviada havia uma mudança que autorizava atualizar um produto com código interno diferente em alguns casos de GTIN ou descrição iguais. Isso foi revertido: colisões de identificadores pedem revisão manual, sem sobrescrever produtos automaticamente. A proteção permanece na API. Planilhas com código inválido normalizado que coincide com outro produto também são bloqueadas.

## 5. Ordem recomendada de testes

1. Fazer backup dos bancos PostgreSQL e do `pdv.db` dos computadores de teste.
2. No Windows com .NET 10 SDK, executar `dotnet build .\ErpNaoFiscal.slnx -c Release` e `dotnet run -c Release --project .\testes\Erp.TestesPesquisaEstoque\Erp.TestesPesquisaEstoque.csproj`.
3. Rodar `dotnet run -c Release --project .\testes\Erp.TestesImportacao\Erp.TestesImportacao.csproj` e os testes de banco já existentes numa empresa fictícia.
4. Abrir a Retaguarda e pesquisar `arroz`, `aroz`, `branco`, `cafe` e um código de barras. Confirmar que um item **não é lançado** só por digitar.
5. Testar Logout da Retaguarda nos três layouts: lateral, compacto e superior; entrar novamente com e sem computador lembrado.
6. Testar rolagem do menu e nomes longos com escala do Windows de 100%, 125% e 150%.
7. Abrir o Caixa; se falhar, consultar o log de inicialização. Usar F11 ou menu **Sair da conta** sem venda em andamento e conferir volta ao login.
8. Testar uma venda offline e sincronizar depois, sem apagar dados locais.
9. Se a API for atualizada, checar `systemctl status erp-nf-api` e logs; não executar o instalador Debian numa atualização normal.

## 6. Limitações da verificação

A integridade do ZIP e a revisão estática do código podem ser conferidas neste ambiente, mas aqui não há SDK .NET 10 nem execução de WinForms/PostgreSQL reais. **A mudança não é uma garantia de que a falha original de abertura do PDV tenha sido resolvida**: a causa real será identificada pelo arquivo de log ou pela mensagem do Windows durante o teste.
