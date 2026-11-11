# Correcao de Aparencia e publicacao de aplicativos em um EXE

## Problema de botoes cortados
A janela de Aparencia/Interface em Master e Retaguarda foi corrigida para usar tres areas distintas: cabecalho, conteudo com rolagem e rodape fixo com Salvar/Cancelar. A janela agora pode ser redimensionada para lidar melhor com escala alta do Windows.

## Um EXE para cada modulo Windows
`deploy/publicar.ps1` ja publicava Windows como single-file e self-contained. Nesta revisao, a opcao `DebugType=embedded` foi adicionada para incorporar simbolos PDB no binario em vez de produzir arquivos `.pdb` separados.

As pastas continuarao sendo:
- `publicar/pdv/ErpPdv.exe`
- `publicar/retaguarda/ErpRetaguarda.exe`
- `publicar/master/ErpMaster.exe`

O .NET pode necessitar extrair bibliotecas nativas automaticamente na pasta temporaria do Windows. A pasta `C:\ProgramData\ErpNaoFiscal` continua armazenando configuracoes e banco SQLite do PDV e **nao deve ser apagada**, sobretudo se houver vendas pendentes.

Se forem gerados arquivos auxiliares adicionais (como arquivos de configuracao ou conteudo), nao exclua sem verificar que o software continua funcionando. A publicacao self-contained resulta em EXEs maiores que o modelo dependente do runtime. Nao habilitamos trimming por motivos de compatibilidade WinForms/reflexao.

A API Linux continua separada e publicada para `/opt/erp-nf/api`; o `instalar-servidor.sh` e o codigo de API nao foram alterados nesta correcao.

## Validacao
Os arquivos foram editados e conferidos sintaticamente por inspecao. Execute o publicador no Windows com o SDK .NET 10; nao houve compilacao .NET nem teste real da interface neste ambiente.
