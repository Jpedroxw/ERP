# ERP Não Fiscal — Carga de Produtos e Promoções

Versão de testes, baseada em **ErpNaoFiscal_Interface_Configuravel**.

## O que foi adicionado

### Retaguarda > Produtos

- **Salvar** grava o produto como rascunho, sem alterar o que os caixas estão vendendo.
- Um produto novo pode ter preço R$ 0,00 durante a preparação; a carga exige preço maior que zero.
- Uma edição de preço, código, descrição, unidade ou ativação não chega ao PDV até ser publicada.
- **Desativar** também fica pendente de carga.
- O estoque NÃO fica pendente: continua sendo movimentado pelo fluxo existente para não parar vendas, entradas e ajustes.

### Retaguarda > Cargas para caixas

- Lista todos os produtos e diferencia **Novo, não publicado**, **Alterado, pendente** e **Publicado**.
- Permite **Publicar selecionados** (Ctrl para múltiplas linhas) e **Publicar pendentes** (até 1000 por operação).
- O PDV baixa as versões publicadas na próxima sincronização. Caixas offline mantêm a última versão recebida.
- **Publicado** quer dizer liberado na VPS, não confirmado por cada terminal. A confirmação individual ainda não está implementada.

### Retaguarda > Promoções

- Uma promoção por cadastro, vinculada a um produto **já publicado**.
- Nome, preço especial fixo e datas de início/fim (inclusive).
- Validação de valor menor que o preço normal publicado, datas e conflitos de promoções para o mesmo produto.
- Alterações de período/valor exigem cancelar a promoção anterior e criar outra.
- O cancelamento é sincronizado aos caixas; um caixa offline não consegue receber cancelamentos novos enquanto permanecer desconectado.
- O Caixa recebe futuras promoções por sincronização, ativa no início do dia e desativa após o último dia automaticamente, mesmo offline.
- O dia considerado vem do **relógio confiável do caixa** (hora da internet e do servidor; veja LEIA-ME-REVISAO.md), não só do relógio do Windows.
- As promoções são independentes da carga: uma promoção salva passa a estar disponível para sincronização imediatamente.

## Na primeira atualização da API

O novo catálogo publicado é uma tabela separada. Na primeira inicialização, a API faz uma **publicação inicial dos produtos já existentes**, preservando os preços atuais dos caixas. Uma marca de inicialização impede que, em reinicializações futuras, produtos novos ainda em rascunho sejam publicados automaticamente.

As novas tabelas no PostgreSQL são:

- `ProdutosPublicados` (último snapshot liberado ao Caixa);
- `ControlesPublicacao` (marca de inicialização única);
- `Promocoes` (preço e datas das ofertas).

No SQLite do Caixa, é criada a tabela `promocoes` sem apagar as vendas, os clientes ou os produtos locais.

**Essa alteração modifica a API e o esquema PostgreSQL**. Diferente da atualização anterior apenas visual, agora é necessário publicar a API e atualizar os executáveis da Retaguarda e do PDV em conjunto. O Master e `instalar-servidor.sh` não precisaram de alterações.

## Publicar no computador de desenvolvimento

Abra PowerShell na pasta onde está `ErpNaoFiscal.slnx`, execute:

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
.\deploy\publicar.ps1
```

O script gera `publicar/api`, `publicar/pdv`, `publicar/retaguarda` e `publicar/master`.

Não envie para o Linux o ZIP inteiro. Na VPS, preserve `/etc/erp-nf/api.env`, as configurações do Nginx, o systemd e os bancos PostgreSQL. Atualize somente a publicação da API em `/opt/erp-nf/api` e reinicie `erp-nf-api` depois de confirmar a publicação e as configurações. Verifique `curl http://127.0.0.1:5080/health` e `journalctl -u erp-nf-api -n 80 --no-pager`.

**Não execute o `instalar-servidor.sh` de novo.**

## Testes manuais recomendados (empresa fictícia)

1. Após atualizar a API, consulte a Retaguarda > Cargas. Produtos antigos devem aparecer como publicados.
2. Edite o preço de um produto antigo na Retaguarda, salve e sincronize o Caixa com F12. Ele deve continuar usando o preço antigo.
3. Volte à Retaguarda > Cargas e publique esse produto. Sincronize o Caixa com F12. Agora deve usar o novo preço.
4. Crie um produto novo, salve com preço zero. Confira que não aparece no Caixa. Ajuste o preço positivo, publique, sincronize e pesquise no Caixa.
5. Crie uma promoção para um produto publicado com datas cobrindo o dia atual. Sincronize o Caixa e confira que o preço promocional aparece na pesquisa e na venda.
6. Crie outra promoção futura, sincronize antes da data e confira que ela ainda não aplica desconto. Depois valide o calendário em ambiente controlado, sem alterar relógio de um caixa operacional.
7. Desligue a conexão externa somente em uma loja de testes depois da sincronização; o PDV deve conseguir aplicar promoção já recebida.
8. Cancele uma promoção online, sincronize o Caixa e confirme o retorno ao preço normal.
9. Verifique que a entrada de produtos e a venda continuam movimentando estoque mesmo quando um produto está em rascunho.
10. Reinicie a API e confira que novos rascunhos não são publicados automaticamente.

## Limitações e próximos testes

- Esta entrega não foi compilada em .NET nem executada no Windows/na VPS deste ambiente. **É um pacote de código para testes**, não versão homologada para clientes.
- Não há confirmação de carga recebida por terminal nem controle de versão por filial.
- Não há promoções por grupo, quantidade mínima, fidelidade ou limites por CPF. A primeira versão faz preço especial por produto.
- Antes de uso comercial, validar concorrência de publicação/promoções, carga com catálogos grandes, migrações e relógios offline.
- Se você usa os bots V7, eles precisam de ajuste para publicar produtos antes de esperar que cheguem ao PDV: o cadastro agora começa em rascunho.
