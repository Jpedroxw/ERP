# ERP Não Fiscal — preferências de interface

Esta versão parte do código enviado em `ErpNaoFiscal(3).zip`. Não introduz NFC-e e não modifica a API, o PostgreSQL nem os scripts da VPS.

## Novas opções em cada aplicativo Windows

- **Modo de exibição:** Simples ou Completo.
- **Posição do menu:** barra lateral, barra superior ou barra lateral compacta (apenas ícones).
- **Tamanho:** Compacto, Normal ou Grande (densidade das tabelas e menus).
- **Exibir painel inicial:** ligado/desligado. No Caixa, o padrão permanece **desligado**, para começar diretamente na venda.

As opções ficam dentro de Configurações/Aparência/Interface, e não expostas como seletores permanentes no cabeçalho.

## Onde encontrar

- **Master:** botão `Configurações` no topo, dentro dos controles administrativos; ou `Configurações` no menu de navegação.
- **Retaguarda:** grupo `Configurações` > `Interface e navegação` (há também `Aparência`, que já existia).
- **Caixa:** `Opções` (`Ctrl+O`) > `Interface`. A autorização existente para abrir Opções continua obrigatória.

Após salvar, feche e abra novamente o programa para aplicar a preferência. O programa não modifica senhas ou certificados.

## Arquivos locais

As preferências são independentes para cada módulo e usuário do **Windows**, e ficam em:

```
%LOCALAPPDATA%\ErpNaoFiscal\interface\master.json
%LOCALAPPDATA%\ErpNaoFiscal\interface\retaguarda.json
%LOCALAPPDATA%\ErpNaoFiscal\interface\pdv.json
```

Esses arquivos não precisam ser enviados à VPS. As opções não afetam os bancos PostgreSQL ou SQLite.

## Padrão para não atrapalhar o uso anterior

- Master: Completo, menu superior, sem painel inicial novo.
- Retaguarda: respeita o modo anterior salvo ou o perfil, menu lateral, painel inicial habilitado.
- Caixa: Completo, menu superior, abre diretamente na venda.

**Ícones:** são utilizados os `.ico` originais da solução para a marca dos aplicativos. Os arquivos originais `master.ico`, `retaguarda.ico` e `caixa.ico` não foram alterados.

## Publicação

Na pasta raiz do ERP, pelo PowerShell:

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
.\deploy\publicar.ps1
```

O script prepara `publicar\master`, `publicar\retaguarda` e `publicar\pdv`. As bibliotecas compartilhadas saem incluídas nessas publicações. **Não é necessário atualizar a VPS** por estas mudanças exclusivamente de interface.

Antes de substituir programas em lojas, valide no Windows 11/10 compatível com o seu build, inclusive o caso de monitor pequeno e a autorização de configurações do Caixa.

## Verificações realizadas

- Estrutura do projeto e nomes dos ícones confrontados com a solução original.
- Leitura estática das alterações e integridade do ZIP.
- Não houve compilação com `dotnet` neste ambiente (o SDK não está disponível aqui).
- Não houve teste real das janelas WinForms; o teste funcional deverá ser feito no Windows.

### Atenção

O menu Completo apresenta todas as opções existentes; a implementação não cria módulos ou recursos administrativos que ainda não existem na versão V3. O ajuste de tamanho muda principalmente tabelas/menus: não altera escala do Windows nem resolução da tela.
