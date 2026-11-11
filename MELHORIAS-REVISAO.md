# Projeto JP — revisão técnica
## Alterações implementadas
- Sincronização rejeita IDs duplicados também nas listas de caixas e movimentos, antes de gravar.
- Requisições simultâneas de sincronização do mesmo terminal são serializadas por trava transacional no PostgreSQL. O segundo envio consulta os dados após o primeiro commit.
- A atualização recusa bancos com versão superior a 12, evitando continuar com uma API que desconhece esse esquema.
- deploy/exportar-fonte.ps1 exporta código separado de binários, instaladores, bancos e configurações locais.

## Como fica no uso
| Situação | Comportamento acrescentado |
|---|---|
| Mesmo movimento repetido no lote | Lote rejeitado com mensagem de identificador repetido |
| Dois envios simultâneos do mesmo PDV | Segundo envio aguarda a transação anterior |
| Banco atualizado por uma API futura | Inicialização interrompida com explicação da incompatibilidade |
| Compartilhar código | ZIP separado dos executáveis e dados locais |

As telas permanecem iguais: estas mudanças são no processamento e na distribuição. O sistema continua não fiscal; integração SEFAZ não foi implementada.

## Validação e limites
Compilação da API tentada com SDK .NET 10.0.402: bloqueada por NU1301, sem resolução do host api.nuget.org. Não há confirmação de compilação.
Não foram executados testes de concorrência nem migrações em PostgreSQL. Esta entrega é código para revisão, não instalador homologado.
Antes de produção, validar em banco descartável: dois envios simultâneos da mesma venda devem gerar uma venda e uma baixa; lote com movimentos duplicados deve retornar 400 sem gravar; banco na versão 13 deve impedir inicialização; atualização de cópia de banco antigo deve preservar saldos.
O ZIP usa lista permitida de extensões para compartilhar código. Não substitui o pacote de instalação: configurações e recursos binários devem ser fornecidos separadamente.

Suite existente ProjetoJP.TestesValidacao: 49 verificações passaram, zero falhas. Essa suite valida regras compartilhadas; não comprova as novas proteções de concorrência e migração.

Validação na pasta Z:\Erpchat: compilação da API com --no-restore concluída com zero erros e dois avisos AD0001 do analisador de rotas ASP.NET. Esse resultado substitui a limitação de compilação descrita acima para a cópia anterior. Testes PostgreSQL seguem pendentes.
