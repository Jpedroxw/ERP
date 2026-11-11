# Segurança saiu da Retaguarda e foi para o ERP Master (04/11)

## Retaguarda (lojas)
Saíram do menu (todos os modos: lateral, compacto, simples e menu do usuário):
- Computadores lembrados
- Sessões de acesso
- Auditoria
- Confirmar senha

Também saiu a **janela "Confirme a sua senha"** que aparecia ao mexer em usuários, terminais, permissões ou estoque
negativo a cada 10 minutos. Agora, quem entrou com usuário e senha já pode fazer essas alterações (respeitando o perfil
e as permissões de cada um). A justificativa do estoque negativo continua obrigatória.

## ERP Master → Clientes → botão "Segurança da loja"
Escolha a empresa e clique em **Segurança da loja**:
- **Conectados agora:** quem está com a Retaguarda aberta, quando entrou e até quando vale.
  *Encerrar sessão* (uma pessoa) ou *Encerrar todas* (ex.: senha vazou, funcionário saiu). Os caixas continuam vendendo.
- **Histórico de alterações:** criou/alterou/desativou usuário ou terminal, mudou permissões, senha redefinida pelo Master,
  sessões encerradas… com data e quem fez. Senhas e chaves nunca aparecem.
- **Computadores lembrados:** de versões antigas da Retaguarda (que entravam sem senha). *Esquecer computador* apaga o acesso.

O histórico continua sendo gravado no banco de cada loja, como antes; só a tela mudou de lugar.

## Atualização
Atualize **API**, **Retaguarda** e **ERP Master**. Sem mudança de banco nesta etapa.
