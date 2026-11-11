# Saída de produtos (Retaguarda)

Onde fica: menu **Estoque → Saída de produtos**, ou o botão **Saída de produtos** na tela Produtos e Estoque.
Quem vê: quem tem a permissão "Lançar saídas e ajustes de estoque" (movimentar_estoque).

## Como lançar
1. Escolha o **Motivo**: Perda / quebra, Produto vencido, Uso / consumo interno, Devolução ao fornecedor,
   Doação, Transferência para outra loja, Brinde / degustação ou Outro. Com "Outro", a Observação é obrigatória.
2. Se quiser, preencha **Para quem / para onde** (sugere os fornecedores cadastrados), **Documento** e **Observação**.
3. Bipe ou digite o produto e aperte Enter, informe a quantidade e aperte Enter. Pode colocar vários itens.
   - Duplo clique corrige um item. Delete remove.
   - Em vermelho: o item ficaria com estoque negativo.
4. Clique em **Lançar saída**. Por padrão o sistema não deixa o estoque ficar negativo.
   Marque "Permitir estoque negativo" só quando o saldo do sistema estiver errado.

O valor da saída é calculado pelo **preço de custo** do produto na hora (quanto se perdeu).
Quem não tem a permissão "ver custo" não vê esse valor.

## Saídas lançadas
- Lista as saídas com número, motivo, destino, valor e usuário.
- **Ver itens** abre o detalhe da saída.
- **Cancelar saída** devolve as quantidades ao estoque. Nada é apagado: a saída fica marcada como "Cancelada".
- No histórico do produto, cada saída aparece como "Saída nº X · Motivo · Destino", com origem "Retaguarda".

## Atualização do servidor
A API cria sozinha as tabelas `Saidas` e `SaidaItens` e a sequência de numeração (migração **v5**).
Faça backup do banco antes, publique a API e reinicie o serviço `erp-nf-api`.

# Login da Retaguarda: só o usuário fica salvo (04/11)
- Saiu o "Lembrar este computador por 7 dias" (chave no TPM/Windows, entrar sem senha).
- No lugar dele, a opção **Lembrar meu usuário** (vem marcada): guarda só o nome de login em
  `%LOCALAPPDATA%\ErpNaoFiscal\retaguarda-usuario.txt`. A **senha é digitada sempre**.
- Com o usuário preenchido, o cursor já abre no campo Senha.
- Se o computador tinha o acesso lembrado da versão anterior, no primeiro login com senha ele é apagado (no PC e no servidor).
- A tela "Computadores lembrados" saiu do menu.
