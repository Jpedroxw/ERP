# ERP Não Fiscal — Novo Visual Aplicado

Base: ErpNaoFiscal_Aprovacao_Cadastro_Corrigida.zip. Esta entrega muda o código WinForms e não é apenas uma imagem ou mockup.

## Melhorias de apresentação

- Todos os módulos: paleta mais discreta (azul-marinho, branco e cinza), navegação lateral com ícones, estilo consistente e modo de menu lateral, superior ou compacto.
- Master: navegação redesenhada; no modo Completo há indicadores de empresas cadastradas, ativas e pedidos aguardando; as tabelas e ações originais continuam presentes.
- Retaguarda: menu superior agrupado por área no modo Completo, evitando excesso de botões; modo Simples com 4 ações principais no painel inicial, mantendo Cargas para Caixas, Promoções e outras ferramentas pelo menu ou modo Completo.
- Caixa: campo de código de barras na região superior da venda; painel de total mais discreto; barra lateral com ícones e atalhos. O modo Completo mantém os dados do item, cliente, pagamento e atalhos de teclado; a venda abre diretamente como padrão.
- As preferências ficam dentro de Configurações: Simples/Completo, Lateral/Superior/Compacta, Compacto/Normal/Grande e exibir painel inicial.
- Para uma instalação nova, a navegação padrão é lateral e o modo padrão é Completo. As preferências que o usuário já salvou são preservadas. O visual é aplicado ao reabrir o aplicativo.
- Os ícones originais master.ico, retaguarda.ico e caixa.ico não foram modificados.

## Arquivos alterados

- src/Erp.Controles/Tema.cs
- src/Erp.Controles/PreferenciasInterface.cs
- src/Erp.Controles/NavegacaoERP.cs (novo)
- src/Erp.Master/Telas/MasterForm.cs
- src/Erp.Retaguarda/Telas/PrincipalForm.cs
- src/Erp.Pdv/Telas/VendaForm.cs

A correção de aprovação de cadastro da SuperMax, API, dados de promoções/cargas, deploy/publicar.ps1 e deploy/instalar-servidor.sh foram mantidos inalterados em relação à base.

## Publicar no Windows

No PowerShell, na pasta raiz da solução:

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
.\deploy\publicar.ps1
```

Se a publicação terminar com sucesso, atualizar as pastas `publicar\master`, `publicar\retaguarda` e `publicar\pdv` nos PCs de testes. Para esta mudança de interface não é necessário reinstalar ou atualizar a API caso ela já tenha sido atualizada com a correção anterior.

Não delete `C:\ProgramData\ErpNaoFiscal\pdv.db`, `pdv.json` ou arquivos de configuração existentes. Feche os programas antes de substituir seus executáveis.

## Como testar

1. Master: Configurações > Aparência e interface, escolha Completo + Lateral; reinicie. Veja os indicadores de empresas no cadastro. Teste também Superior e Compacta.
2. Retaguarda: Configurações > Interface e navegação, escolha Simples; reinicie e confira os quatro atalhos no painel inicial. Volte para Completo e confira todos os módulos e seus menus agrupados.
3. Caixa: Opções > Interface (autorização de gerente/suporte conforme as regras existentes). Escolha Completo + Lateral. Reinicie, confira o campo de código de barras no topo e teste F2/F5/F9/F10/F12.
4. Em todos: teste os tamanhos Compacto/Normal/Grande, a opção de exibir painel inicial e a navegação por teclado.
5. Faça testes em resolução baixa (como 1366×768) e no hardware básico pretendido antes de usar com clientes.

## Validação e limites

Os arquivos originais `.ico`, os arquivos de API e os scripts de deploy foram comparados byte a byte; a integridade dos arquivos e a estrutura SQLite foram verificadas. Ainda é necessário compilar no Windows com .NET 10 e executar as telas WinForms: este ambiente não dispõe do SDK .NET/ambiente Windows para validar execução visual.

Não houve alteração fiscal.
