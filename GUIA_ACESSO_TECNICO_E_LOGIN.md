# ERP — acesso técnico por empresa e ajustes de usabilidade

Base usada: `ErpNaoFiscal_Reinicio_Apos_Aparencia.zip` (contém correções anteriores de visual, cargas/promoções e aprovação de pedido). Sem alterações fiscais.

## Alterações implementadas

1. **Master → Clientes → Acesso técnico:** selecione a empresa e clique em **Acesso técnico**. A tela mostra o login e se a credencial está ativa. Permite gerar uma nova senha de pelo menos 12 caracteres, mudar o login junto com a senha e bloquear/reativar o acesso. A senha antiga nunca é exibida; a nova só aparece ao redefinir.
2. **Caixa → Opções do Caixa → Autorização do gerente → Acesso técnico da empresa (online):** pede login e senha técnicos gerenciados pelo Master para a empresa para a qual o terminal está configurado. A API valida também a chave de um terminal ativo. Essa autenticação serve **somente** para abrir configurações, **não** para autorizar descontos, cancelamentos ou sangrias.
3. **Retaguarda → Cadastros → Terminais:** novo botão **Copiar chave**, que copia a chave do terminal selecionado novamente sem precisar criar outro.
4. **Retaguarda → Login: "Lembrar este computador por 30 dias"** (desmarcado por padrão). **A senha não fica guardada em lugar nenhum.** Ao entrar com a senha e marcar a opção, a Retaguarda cria uma chave de assinatura própria do computador — no chip TPM 2.0 quando existe (a chave não sai do chip: copiar arquivos ou o perfil do Windows para outro PC não leva a chave); sem TPM, numa chave do Windows não exportável. Só a parte pública vai para o servidor (tabela `Dispositivos`, criada sozinha). Nas próximas vezes, o login vem preenchido e a senha pode ficar em branco: a Retaguarda assina "computador + login + hora do servidor" e o servidor confere. Vale 30 dias; um pedido não pode ser repetido; trocar a senha do usuário (ou desativá-lo) invalida na hora; desmarcar e entrar com a senha apaga o registro no servidor. O arquivo antigo que guardava a senha (`retaguarda-acesso.dat`) é apagado automaticamente.
   **PC sem TPM (comum em computador antigo/simples):** ao lembrar, a Retaguarda pede para criar um **PIN de 4 a 8 números** (não aceita repetido nem sequência). Na entrada, o campo pede o PIN no lugar da senha (link "Usar a senha" para entrar do jeito normal). O PIN é conferido no servidor: 5 erros e o computador é esquecido. Assim, mesmo que alguém consiga copiar a chave do disco, sem o PIN ela não entra.
5. Mensagem de aprovação da empresa na Retaguarda mais direta: “Entre com o login e a senha informados no cadastro”.
6. Ícones originais, interface personalizável, promoções, cargas e componentes existentes preservados.

   Logo abaixo continua o link **Senha técnica deste computador (sem internet)**: é a saída quando o caixa não fala com o servidor (servidor ou chave errados, sem internet) — o caso em que é preciso entrar nas Opções para corrigir, e em que o acesso online não funciona. Caixa no servidor padrão (`nuvem`) recebe a explicação de que o acesso por empresa só existe para empresas do Master.
7. Ao gerar a senha técnica no Master, login e senha também são copiados para a área de transferência.

## Segurança e limites desta etapa

- **Acesso técnico remoto exige internet e API atualizada.** Vendas do Caixa continuam offline; apenas a autorização técnica por empresa fica indisponível sem conexão.
- A senha técnica *local* já existente em `Erp.Controles/ConfigTecnica.cs` e guardada em `servidores.dat` continua **separada** e serve para a configuração local da lista de servidores. Sua senha de fábrica deve ser substituída em todos os equipamentos antes da produção. **O Master não administra essa senha local nesta versão.** Não confunda as duas proteções.
- Credenciais técnicas por empresa são guardadas como hash PBKDF2-SHA256 no banco `erp_master`. Uma tabela `SuportesTecnicos` é criada automaticamente no início da API; o instalador Debian não precisa ser executado outra vez. O Master exige a `X-Master-Key`, e o Caixa precisa da chave de um terminal ativo. O endpoint de validação tem limite de tentativas.
- "Lembrar este computador" não guarda senha. Com TPM, a chave não pode ser levada para outro computador; ainda assim, quem usar este mesmo computador (com o seu usuário do Windows) entra sem senha enquanto estiver lembrado — em computador compartilhado, deixe desmarcado. Sem TPM, a proteção é menor (chave do Windows), mas a senha continua fora do disco e o acesso vence em 30 dias.
- A chave de terminal copiada para a área de transferência deve ser tratada como segredo e apagada depois do uso.
- Um usuário técnico bloqueado no Master não conseguirá nova autorização técnica **online**. O bloqueio não substitui controles de acesso do Windows, nem impede pessoas com privilégios administrativos locais de manipular arquivos do equipamento.
- O login técnico por empresa não é o usuário normal de caixa e não aparece no cadastro de operadores.

## Instalação no ambiente de testes (Debian 13, systemd)

1. Descompacte a nova solução no Windows. Não substitua as pastas de dados do Caixa.
2. Compile/publicar: `Set-ExecutionPolicy -Scope Process Bypass -Force` e `./deploy/publicar.ps1` no PowerShell.
3. Verifique **quatro projetos publicados sem erros**. A compilação ainda precisa ser confirmada no Windows; neste ambiente não há SDK .NET/WinForms.
4. Atualize a API com `publicar/api` pelo procedimento que você já usa em `/opt/erp-nf/api`, preservando `/etc/erp-nf/api.env`, sem reiniciar o instalador e sem apagar bancos. Faça a substituição com o serviço parado e reinicie `erp-nf-api`.
5. Verifique: `systemctl status erp-nf-api --no-pager` e `curl http://127.0.0.1:5080/health`.
6. Atualize **ErpMaster.exe, ErpPdv.exe e ErpRetaguarda.exe**, mantendo `C:\ProgramData\ErpNaoFiscal` (principalmente `pdv.db`, `pdv.json` e `retaguarda.json`).

## Testes importantes

1. Crie/seleciona uma empresa fictícia e confirme no Master → **Acesso técnico** que está inicialmente “Não configurado”.
2. Gere nova senha técnica para essa empresa; teste acesso ao **Opções do Caixa** de um terminal dessa empresa. Confirme que um operador comum continua sem poder autorizar cancelamentos usando a senha técnica.
3. Tente usar a mesma credencial em Caixa de outra empresa — deve ser recusada.
4. Bloqueie o acesso técnico no Master e teste novamente — deve ser recusado. Libere e confira que volta a funcionar.
5. Desconecte a internet do Caixa: operações de venda permanecem possíveis conforme os recursos offline já existentes; o **acesso técnico remoto** deve informar indisponibilidade.
6. Na Retaguarda, marque “Lembrar usuário e senha”, entre, feche e reabra. Verifique se os campos são preenchidos. Desmarque e entre: a credencial não deve permanecer salva.
7. Selecione um terminal em Cadastros → Terminais e clique **Copiar chave**. Confira a chave no Caixa de teste, não em mensagens ou logs.
8. Aprove um pedido de empresa de testes e confira o texto resumido na janela da Retaguarda.

**Atenção:** alterações ainda precisam de compilação, execução WinForms e teste com PostgreSQL reais. Não instale diretamente em clientes de produção sem essa validação.
