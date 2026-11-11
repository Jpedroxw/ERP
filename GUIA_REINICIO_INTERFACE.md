# Confirmação de reinício depois de salvar aparência

## Master
Abra **Configurações → Aparência e interface**, altere o modo, posição do menu, tamanho ou painel inicial e clique em **Salvar preferências**. Ao detectar alteração, o programa pergunta se deseja reiniciar agora. **Sim** reabre o Master; **Não** mantém o programa aberto com a preferência salva.

## Retaguarda
Em **Configurações → Interface e navegação**, o mesmo diálogo aparece quando alguma opção for alterada. Em **Configurações → Aparência** (tema, cores, logo), a confirmação de reinício que já existia permanece.

## Caixa
Abra **Opções do Caixa** (autorização de gerente, conforme sua configuração), e altere **Aparência** ou **Interface**. Ao salvar, se alguma dessas opções mudou, pergunta se deseja reiniciar agora. **Não** aplica a mudança na próxima abertura; **Sim** reabre o Caixa e exige novo login do operador. A venda em andamento precisa estar vazia para abrir Opções; caixa aberto e registros persistidos no SQLite não são apagados.

## Segurança / compatibilidade
- A opção **Não** nunca descarta as alterações já salvas.
- A escolha padrão na confirmação é **Não**.
- Se o Windows não conseguir abrir a nova instância, a sessão antiga permanece aberta e uma mensagem orienta a reabertura manual.
- Preferências de interface continuam em `%LOCALAPPDATA%\ErpNaoFiscal\interface`.
- Não há mudanças no banco PostgreSQL, na API Linux ou no script `instalar-servidor.sh`.

## Como compilar no Windows
No PowerShell, na pasta raiz:

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
.\deploy\publicar.ps1
```

Valide: salvar e escolher **Não**; reabrir manualmente; salvar outra alteração e escolher **Sim**; reinício no Caixa com caixa aberto e nenhuma venda em andamento. A compilação WinForms/.NET 10 não foi executada neste ambiente.
