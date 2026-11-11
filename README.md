# Projeto JP — C# / .NET 10

> **Novo nome (10/11/2026):** o sistema agora se chama **Projeto JP**: **Projeto JP PDV** (antigo Caixa,
> `ProjetoJP-PDV.exe`), **Projeto JP Gestão** (antiga Retaguarda, `ProjetoJP-Gestao.exe`), **Projeto JP Master**
> (`ProjetoJP-Master.exe`) e o instalador `Instalar-ProjetoJP.exe`. Código em `src/ProjetoJP.*`, solução `ProjetoJP.slnx`.
> Ficaram com o nome antigo, de propósito: o arquivo da API na VPS (`Erp.Api`, que o serviço `erp-nf-api` executa) e as
> pastas de dados `C:\ProgramData\ErpNaoFiscal` e `%LOCALAPPDATA%\ErpNaoFiscal` (as lojas já instaladas guardam tudo ali).

> **Versão atual (correções de 07/11/2026):** leia primeiro `LEIA_ME_CORRECOES_07NOV2026.md` e o `GUIA-INSTALACAO.pdf`
> (instalação na VPS e uso do sistema). Antes de atualizar a VPS ou o Caixa, faça backup. Gere os pacotes **só** com
> `deploy\publicar.ps1`. Não execute `instalar-servidor.sh` para atualizar nem crie migration (`dotnet ef migrations add`).

Caixa (PDV), estoque, clientes, usuários e relatórios. Banco PostgreSQL na VPS Linux;
cada caixa tem um SQLite local e **continua vendendo sem internet** — as vendas sobem sozinhas quando a conexão volta.

```
 Loja (Windows 10+)                               VPS Debian
┌───────────────────────┐   HTTPS (Nginx)   ┌──────────────────────────┐
│ ProjetoJP-PDV.exe     │ ── /api/sync ───▶ │ Erp.Api (ASP.NET Core)   │
│  └ SQLite local       │                   │  └ PostgreSQL (erp_nf)   │
│ ProjetoJP-Gestao.exe  │ ── /api/... ────▶ │                          │
└───────────────────────┘   (login JWT)     └──────────────────────────┘
```

| Projeto | O que é |
|---|---|
| `src/ProjetoJP.Shared` | DTOs, enums, hash de senha e regras de formato/validação usados por todos |
| `src/ProjetoJP.Controles` | Tema visual (cores, botões, cartões, abas, menu) e campos formatados (R$, quantidade, CPF/CNPJ, telefone) usados pelo caixa e pela Gestão |
| `src/ProjetoJP.Api` | API REST + EF Core/PostgreSQL. Roda na VPS atrás do Nginx |
| `src/ProjetoJP.Pdv` | Caixa WinForms. SQLite local, sincronização, cupom ESC/POS |
| `src/ProjetoJP.Gestao` | WinForms para cadastros, estoque e relatórios |
| `deploy/` | Script de instalação da VPS, site do Nginx e publicação |

> Tudo é **documento sem valor fiscal**. A parte fiscal (NFC-e/SEFAZ) entra depois sem mudar essa base:
> a venda já tem Id próprio, itens e pagamentos separados.

---

## 1. Rodar no seu computador (desenvolvimento)

Precisa de: **Visual Studio 2026** (já vem com o .NET 10 SDK; Rider ou VS Code + SDK também servem) e um PostgreSQL local
(ou aponte para o da VPS por túnel SSH).

1. Crie o banco local:
   ```sql
   CREATE USER erp_nf WITH PASSWORD 'erp_nf';
   CREATE DATABASE erp_nf OWNER erp_nf;
   ```
2. Em `src/ProjetoJP.Api/appsettings.json`, ajuste a senha na `ConnectionStrings:Erp`.
3. **Não crie uma migration inicial manualmente em um banco já existente.** Esta V6 já inclui
   as atualizações versionadas necessárias em `MigracoesBanco.cs` e `MigracoesMaster.cs`.
   Confira sempre o backup e a compatibilidade antes de uma futura migração.
4. Antes da primeira inicialização de um banco novo, defina uma senha forte para
   `Seed__AdminSenha` na configuração segura da API. A API cria o usuário `admin` com essa
   senha e as formas de pagamento padrão. **Não existe senha inicial `admin/admin`.**
   Teste: http://localhost:5080/health
5. Rode a **Gestão**. Na primeira vez ela pede o endereço do servidor: `http://localhost:5080/`.
   Entre como admin e:
   - **Caixas > Caixas (terminais) > Novo** → a chave do caixa é copiada para a área de transferência;
   - cadastre alguns produtos, dê entrada em **Estoque > Entrada de produtos** e envie em
     **Caixas > Enviar produtos aos caixas** (e os usuários em **Caixas > Enviar usuários aos caixas**).
6. Rode o **PDV**. Na primeira vez abre a tela de configurações: endereço, chave (botão Colar),
   impressora e papel escolhidos em listas. Depois ele baixa usuários, produtos e formas de pagamento.

Para iniciar vários projetos juntos: botão direito na solução > *Configurar Projetos de Inicialização* > Vários.

## 2. Colocar a API na VPS

Na VPS (root):

```bash
# somente para a PRIMEIRA INSTALAÇÃO numa VPS nova, nunca para atualizar uma instalação em uso:
bash instalar-servidor.sh
```

O script instala PostgreSQL/Nginx se faltar, cria o banco **erp_nf** (separado do que já existe lá),
gera senha do banco, chave JWT e a senha inicial do admin em `/etc/erp-nf/api.env`, e registra o serviço
`erp-nf-api`. Anote a senha do admin que ele mostra no final.

No Windows, gere os pacotes (ou dois cliques em `deploy\publicar.cmd`). Se o computador não tiver o .NET 10 SDK, o script baixa e instala sozinho da Microsoft na primeira vez (cerca de 250 MB, sem precisar de administrador):

```powershell
powershell -ExecutionPolicy Bypass -File deploy\publicar.ps1
scp -r publicar\api\* root@IP_DA_VPS:/opt/erp-nf/api/
```

De volta na VPS:

```bash
chmod +x /opt/erp-nf/api/Erp.Api && chown -R erpnf:erpnf /opt/erp-nf/api
systemctl restart erp-nf-api
journalctl -u erp-nf-api -f        # acompanhar o log
curl http://127.0.0.1:5080/health  # deve responder {"ok":true,...}
```

**Nginx + HTTPS** (detalhado no Passo 4 do GUIA-INSTALACAO.pdf):

```bash
cp deploy/nginx-erp-nf.conf /etc/nginx/sites-available/erp-nf
ln -s /etc/nginx/sites-available/erp-nf /etc/nginx/sites-enabled/
nginx -t && systemctl reload nginx
apt install -y certbot python3-certbot-nginx
certbot --nginx -d erp.f84568e4.vps.hospedeaqui.cloud
```

Teste: `https://erp.f84568e4.vps.hospedeaqui.cloud/nf/health` → `ApiUrl = https://erp.f84568e4.vps.hospedeaqui.cloud/nf/`

Atualizar versão depois: rode `publicar.ps1`, envie a pasta `api` de novo e `systemctl restart erp-nf-api`.
As migrations novas são aplicadas sozinhas na inicialização.

## 3. Instalar nas lojas

Copie `publicar\pdv` e `publicar\gestao` para os computadores (não precisa instalar .NET).
Não há arquivo para editar: tudo é configurado pelas telas.

- **Gestão:** na primeira vez pede o servidor (com botão Testar). Para trocar depois: link
  *Alterar servidor* na tela de login.
- **Servidor:** no caixa e na Gestão digita-se só o **nome** do servidor (`nuvem`, que já vem preenchido). O site
  para onde ele aponta não aparece em tela, mensagem nem no arquivo de configuração — para o usuário é só "Servidor"
  (Online/Offline).
- **Configuração técnica (suporte):** link na tela de servidor do caixa (Opções → Servidor) e da Gestão
  (Alterar servidor). Pede a **senha técnica** e abre a lista *nome → site*: acrescentar, trocar o site, renomear,
  remover, testar e trocar a senha técnica. Senha de fábrica: `suporte@erp` — no primeiro acesso o sistema obriga a
  criar outra. A lista e a senha ficam em `C:\ProgramData\ErpNaoFiscal\servidores.dat`, criptografadas pelo Windows
  (copiado para outro computador, o arquivo não abre). Cada computador guarda a sua; o de fábrica (`nuvem`) está em
  `src/ProjetoJP.Shared/EnderecoServidor.cs`.
- **PDV:** na primeira vez abre a tela de opções. Para mudar depois: **F1 > Opções do caixa** na tela de venda
  (ou Ctrl+O), ou o link *Opções do caixa* na tela de login (Enter na tela de caixa fechado). Pede senha de gerente/admin; ao salvar, o caixa
  recarrega e volta direto para a venda.

### Aparência (tema escuro, cores, logo, fundo)

PDV: **Opções → Aparência**. Gestão (gerente/admin): **Configurações → Aparência**. As duas mexem na mesma
configuração (vale para o caixa e a Gestão daquele computador):

- **Tema:** Claro, Escuro ou Igual ao Windows (caixas de texto, listas e barra de título também ficam escuras).
- **Cor de destaque:** muda botões, total do caixa, seleção e barras escuras. Padrão: **azul-marinho**.
  Dá para escolher um dos 9 tons prontos ou qualquer cor ("Outra cor...").
  Cores muito claras são escurecidas sozinhas para o texto branco continuar legível.
- **Nome da empresa:** aparece no lugar de "Projeto JP" no login, no menu, no topo e no título das janelas.
- **Logo:** aparece no login, no topo do caixa e no menu da Gestão (PNG com fundo transparente fica melhor).
- **Plano de fundo:** imagem atrás da tela de venda, do início da Gestão e da coluna do login, com intensidade
  ajustável; os cartões continuam sólidos para tudo ficar legível.

Fica em `C:\ProgramData\ErpNaoFiscal\aparencia\`. Vale ao reabrir o programa (ao salvar ele oferece reabrir).

**Projeto JP Master:** botão **Cor** no topo. Tem tema e cor próprios (`master.json`, na mesma pasta), separados dos da
loja: mudar no Master não muda o caixa nem a Gestão, e vice-versa.

### Usuários do caixa

Na Gestão (login de admin), menu **Caixas → Novo usuário do caixa**: nome, login, senha (duas vezes) e se é
**Operador** (vende, recebe, fecha o caixa) ou **Gerente** (também autoriza cancelamento, desconto, sangria e abre as
Opções do caixa). Essa tela já envia o usuário aos caixas: ele chega na próxima sincronização (a cada 30 s, com internet).
Para trocar perfil ou permissões: **Caixas → Usuários e permissões** e depois **Caixas → Enviar usuários aos caixas**.
Trocar a senha ou desativar um usuário vale nos caixas na próxima sincronização, sem precisar enviar.

### Entrada de produtos (compras)

Menu **Estoque → Entrada de produtos** (gerente ou admin): fornecedor, nº da nota e observação; depois os produtos,
um embaixo do outro — digite o código, o código de barras (o leitor funciona direto) ou parte do nome, Enter,
quantidade, Enter, custo, Enter. Duplo clique num item corrige; Delete remove. **Finalizar entrada** soma tudo ao
estoque de uma vez e, se marcado, grava o custo como novo preço de custo. Cada item aparece no histórico do produto
(tela Estoque) como "Entrada nº X · NF ... · fornecedor".
Em **Entradas lançadas** dá para ver os itens de cada entrada e **cancelar** uma lançada errada (as quantidades saem
do estoque de novo; o preço de custo não volta). Saídas (perda, quebra, uso interno...) ficam em
**Estoque → Saída de produtos**; inventário (ajuste) em **Estoque → Movimentação de estoque**.

As tabelas novas (`Entradas`, `EntradaItens`) são criadas sozinhas, em todos os bancos, quando a API nova inicia.

### Importar dados de outro sistema

Gestão (gerente/admin), menu **Configurações → Importar dados**. Traz **produtos** ou **clientes** de:

| Origem | O que precisa |
|---|---|
| Planilha Excel (.xlsx/.xls) ou CSV/TXT | primeira linha com os nomes das colunas; separador e acentos são descobertos sozinhos |
| Firebird (.FDB/.GDB) | Firebird rodando no computador/servidor do banco; usuário (SYSDBA) e senha |
| SQLite (.db) | só o arquivo |
| PostgreSQL, MySQL/MariaDB | servidor, porta, banco, usuário e senha |
| SQL Server | servidor e banco; usuário vazio = login do Windows (usa o driver ODBC que vem no Windows) |
| Outros (Access, Oracle...) | driver ODBC 64 bits instalado + string de conexão |

Depois de conectar, olha as colunas de todas as tabelas e escolhe sozinho a que parece ser a de produtos (ou
clientes); as outras candidatas ficam com ★ na lista.

Passos: escolha o que importar → conecte → escolha a tabela (ou escreva um `SELECT` para juntar tabelas, por exemplo
produto + grupo + estoque) → confira qual coluna vai para cada campo (sugerido pelo nome; as escolhidas ficam azuis na
prévia) → **Testar sem gravar** → **Importar**.

- A origem **só é lida** (consultas que não começam com SELECT/WITH são recusadas).
- Produtos existentes são atualizados apenas quando a correspondência é segura. Código de barras igual
  com código interno diferente, ou códigos normalizados que colidem, exigem revisão manual e não
  sobrescrevem outro cadastro. Clientes existentes seguem as validações próprias.
- Unidades de outros sistemas são convertidas (UND→UN, LT→L, PÇ→PC, KILO→KG...). Grupos/categorias que não existem
  são criados. O estoque vira um **ajuste de inventário** no histórico.
- Linhas com problema não param a importação: código de barras com dígito errado, CPF inválido ou telefone sem DDD
  são importados sem esse campo; produto sem descrição ou sem preço é pulado. Tudo aparece em **Avisos**.
- Exemplos para testar em `exemplos\` (SQLite `loja-antiga.db`, `produtos-planilha.csv`, `cadastros.xlsx`).
  No SQLite, para trazer o nome do grupo use a consulta:
  `SELECT p.*, g.DESCRICAO AS GRUPO FROM PRODUTOS p LEFT JOIN GRUPOS g ON g.COD_GRUPO = p.COD_GRUPO`

### Visual

Tema claro com azul-marinho (ou a cor escolhida), compartilhado pelos dois programas (`src/ProjetoJP.Controles/Tema.cs`): botões, cartões, abas,
menu lateral e ícones (fonte Segoe MDL2 Assets, do Windows 10/11).

A Gestão tem dois modos, alternados por um botão e lembrados por computador:

| | Modo simples | Modo completo |
|---|---|---|
| Início | blocos grandes de atalho | painel com vendas do dia/mês, ticket médio, estoque baixo, últimas vendas |
| Navegação | uma tela por vez, botão Início | menu lateral + várias telas em abas |
| Listas | colunas principais | todas as colunas, Exportar, Mostrar inativos |

No caixa: em cima, a logo grande, o nome e o endereço da loja (do cabeçalho do cupom), terminal, operador,
Online/Offline, hora e **Mais opções (F1)**. No meio, a busca com a quantidade ao lado, o **último item** em destaque e a
lista. À direita, uma **coluna escura com o total grande**, subtotal, desconto, itens, cliente e quanto o cliente
**economizou** (promoção, atacado e desconto); Enter transforma a coluna no **pagamento**: formas (F7 a F12), valor
recebido com **notas rápidas** (Exato, R$ 95, R$ 100...), troco, **dividir em mais de uma forma** e o aviso do fiado
(para quem vai a conta). Ao concluir, a coluna mostra o **troco** grande e o meio mostra **"Venda concluída"** com a
economia (sem o nome do cliente), até passar o próximo produto. Sem venda, o meio mostra as **promoções da loja**
passando sozinhas (as promoções e o atacado já enviados aos caixas). O programa sempre abre no **login**; depois que o
operador sai da conta ou fecha o caixa, fica na tela **"Caixa fechado — procure o próximo caixa"**, também com as
promoções, e Enter abre o login. Embaixo, as teclas (clicáveis), a
impressora e as mensagens. A tela se ajusta de 1024×768 a 1920×1080: em telas menores a coluna fica mais estreita e
somem a coluna Código/Unitário, a hora e algumas teclas do rodapé.

| Configuração do caixa | Como escolhe |
|---|---|
| Endereço do servidor | digitado, botão **Testar conexão** confere servidor e chave |
| Chave do terminal | botão **Colar** (gerada em Caixas > Caixas (terminais)) |
| Impressora | lista das impressoras instaladas no Windows, ou "nenhuma" (cupom em .txt) |
| Largura do papel | 80 mm / 48 colunas, 80 mm / 42 colunas, 58 mm / 32 colunas |
| Acentuação | PC850, PC860 ou sem acentos |
| Cortar papel, abrir gaveta | caixas de marcar; botão **Imprimir teste** |
| Sincronização | 15 s, 30 s, 1 min, 2 min |

Configurações e banco local ficam em `C:\ProgramData\ErpNaoFiscal\` (`pdv.json`, `pdv.db`,
`retaguarda.json`), então trocar o .exe numa atualização não apaga nada.

A chave do terminal fica criptografada no `pdv.json` (DPAPI do Windows) e só abre naquele computador. Caixas antigos,
com a chave aberta no arquivo, são convertidos sozinhos na primeira vez que abrem. Se o `pdv.json` for copiado para
outro computador (ou o Windows for reinstalado), o caixa abre a configuração pedindo a chave de novo: copie-a na
Gestão em Caixas → Caixas (terminais).

### Formato dos campos

| Campo | Na digitação | Validação |
|---|---|---|
| Preços, valores em R$ | digitado como calculadora: 1 2 3 4 → R$ 12,34 | até R$ 9.999.999,99 |
| Quantidade / estoque | KG, G, L, ML, M, M2 com 3 casas; UN, CX, PC... só inteiros | caixa e servidor recusam fração em UN |
| CPF/CNPJ | máscara automática, aceita CNPJ alfanumérico | dígitos verificadores |
| Telefone | (00) 0000-0000 ou (00) 00000-0000 | DDD + 8 ou 9 dígitos |
| Código de barras | só números | EAN/GTIN 8, 12, 13 ou 14 com dígito verificador |
| Unidade | lista (UN, KG, L, CX...) | só as da lista |
| Descrição, código | maiúsculas | tamanho máximo do banco |
| Login | minúsculas, números, ponto, hífen | 3 a 60 caracteres |

### Atalhos do caixa

Padrão: **Enter paga** (com a busca vazia; no pagamento, Enter conclui) e o resto fica em uma tecla, sem Ctrl (bom para teclado de automação). Cada caixa pode trocar qualquer tecla
em **Opções do caixa → Teclado** (capturando a tecla, inclusive a do teclado de automação).

| Tecla | Ação | Tecla | Ação |
|---|---|---|---|
| Enter | **pagar** (busca vazia) / concluir no pagamento / salvar orçamento | Esc | no pagamento, voltar; na busca, limpar / cancelar |
| F1 | **mais opções** (todas as ações: sangria, suprimento, orçamento, fechar caixa...) | F7 a F12 | formas de pagamento |
| F2 | quantidade | Ctrl+P | reimprimir o cupom da última venda |
| F3 | buscar produto | Del | remover item* |
| F4 | cliente da venda | Ctrl+B | ler o peso da balança |
| F5 | alterar quantidade/preço do item* (também remove) | Ctrl+L / Ctrl+R | vendas do caixa / orçamentos |
| F6 | desconto na venda* (R$ ou %, atalhos 2/5/10% e arredondar) | Ctrl+S / Ctrl+O | sincronizar / opções do caixa* |

\* pede senha de Gerente/Admin quando o operador é perfil Operador.

### Fiado, devolução, balança e etiqueta

- **Fiado:** em **Caixas → Formas de pagamento**, marque *É fiado* na forma (ex.: "Fiado"). No caixa, a venda no
  fiado exige o cliente (F4). Quando a venda sobe, a dívida entra na conta do cliente; cancelar a venda tira.
  Receber: Gestão **Vendas → Fiado e devolução → Receber fiado**.
- **Devolução:** **Vendas → Fiado e devolução → Devolver item**: busque pelo nº da venda (e o dia, se houver
  mais de um caixa — cada caixa conta do 1), escolha a venda certa, digite quanto volta de cada item. O estoque volta,
  o valor sai dos relatórios e, se a venda foi no fiado, abate a dívida.
- **Balança:** etiqueta EAN-13 que começa com 2 (peso ou preço) e balança ligada no caixa (USB/serial). Configure
  em Opções do caixa. Produto em **KG** ou **G**.
- **Atacado:** no produto, *Preço de atacado* e *Atacado a partir de*; em Promoções, tipo Varejo ou Atacado (com quantidade
  mínima e datas). O caixa soma o mesmo produto no cupom e aplica o atacado sozinho quando chega no mínimo.
- **Etiqueta de gôndola:** **Estoque → Etiqueta de gôndola**. Sai com o preço que o caixa cobra hoje (o último
  enviado aos caixas, ou a promoção em vigor); avisa quando há preço novo ainda não enviado.

## Vários clientes e Projeto JP Master

Cada empresa cliente tem **um banco só dela** na VPS (`erp_c_<código>`). A instalação antiga continua como
"cliente padrão" (banco `erp_nf`). Quem cuida disso é o **Projeto JP Master** (`ProjetoJP-Master.exe`), um programa só seu.

- **Nome do servidor = código do cliente.** Nos caixas e na Gestão da loja, em Servidor, digita-se o código
  (ex.: `lojaabc`). O sistema monta sozinho o endereço `https://<servidor padrão>/nf/lojaabc/` — nada a cadastrar
  por computador. Na API, o primeiro pedaço do caminho escolhe o banco; o login de um cliente não abre outro.
- **Projeto JP Master:** clientes (criar, editar, suspender/liberar, trocar a senha do admin, copiar o nome do servidor,
  tamanho do banco, usuários, caixas, última venda) e **pedidos de cadastro** (aprovar/recusar). Conecta com o
  endereço da VPS + a **chave master** (`Master__Chave` no `/etc/erp-nf/api.env`, mostrada pelo
  `instalar-servidor.sh`), guardada criptografada para o seu usuário do Windows.
- **Pedido de cadastro pela própria loja:** na Gestão, link *Criar cadastro da empresa* no login (empresa,
  CPF/CNPJ, responsável, telefone, e-mail, cidade, nome do servidor desejado — conferido na hora — e login/senha do
  admin). O pedido aparece no Master; aprovado, o banco é criado com esse admin e a Gestão da loja se configura
  sozinha na próxima abertura (recusado, ela mostra o motivo). Limite de 5 pedidos por hora por internet.
- **Suspender** um cliente bloqueia login e sincronização dele (os caixas continuam vendendo offline); os dados
  ficam guardados.
- **Excluir** um cliente apaga o banco dele e o cadastro no master — pede o nome do servidor digitado de novo e,
  antes, guarda uma cópia (`pg_dump`) em `/opt/erp-nf/excluidos` (se a cópia falhar, nada é apagado).
  **Excluir pedido** apaga um pedido de cadastro e os dados pessoais dele.
- **Limite de caixas**: no Novo cliente, Editar e Aprovar, defina quantos caixas ativos a empresa pode ter
  (0 = sem limite). A coluna *Caixas* mostra "2 de 3". Ao chegar no limite, a Gestão da loja não consegue criar
  nem reativar outro caixa (aparece a mensagem do plano). Se você baixar o limite, os caixas que já existem continuam
  funcionando até a loja desativar algum.
- **Caixa e Gestão precisam usar o mesmo Servidor** (o código da empresa): a chave do terminal só existe no
  banco da empresa onde ele foi criado. A tela de terminal criado já diz o nome a digitar no caixa.
- **CNPJ com letras:** desde jul/2026 a Receita emite CNPJ alfanumérico (letras nas 12 primeiras posições; os
  2 dígitos verificadores são sempre números). Os campos aceitam letra só nessas posições; CPF nunca tem letra.
- **Servidor:** o usuário do banco precisa de `CREATEDB` (o `instalar-servidor.sh` já faz:
  `ALTER ROLE erp_nf CREATEDB`). Ao iniciar, a API cria o `erp_master` se faltar e atualiza a estrutura do banco de
  todos os clientes ativos.
- **Backup:** `/usr/local/bin/erp-nf-backup` (todo dia às 2h) gera um `.dump` por banco — padrão, master e cada
  cliente — em `/opt/erp-nf/backups`, guardando 14 dias.
- **Próxima etapa:** filiais dentro de cada cliente (estoque, caixas e relatórios por filial).

## 4. Como a sincronização funciona

- **Desce (VPS → caixa)**: usuários e produtos **enviados aos caixas** (Caixas → Enviar ...), promoções, clientes e
  formas de pagamento alterados desde a última sincronização. Roda a cada 30 s e logo após cada venda.
- **Sobe (caixa → VPS)**: caixas, sangrias/suprimentos e vendas com `sincronizado = 0`.
  Cada registro tem Guid gerado no caixa, então reenviar nunca duplica.
- **Estoque**: o servidor baixa o estoque quando a venda chega; cancelamento devolve (menos o que já voltou por
  devolução na Gestão). No caixa, a venda ainda não enviada é descontada só dos produtos que chegaram na carga.
  Dois caixas vendendo o mesmo produto ao mesmo tempo não perdem saldo (controle por `xmin` no PostgreSQL;
  em caso de conflito o caixa simplesmente reenvia na próxima rodada).
- Senhas são PBKDF2; o hash desce para o caixa para permitir login offline.

## 5. Perfis

| Perfil | Pode |
|---|---|
| Operador | começa com: cadastrar clientes, remover item da venda e fazer orçamento |
| Gerente | começa com todas as permissões; fiado, devolução, etiqueta, formas de pagamento |
| Admin | tudo, inclusive usuários e terminais (caixas) |

O admin marca o que cada um pode em **Caixas → Usuários e permissões → Permissões**.

## 6. Endpoints da API

| Método | Rota | Acesso |
|---|---|---|
| POST | `/api/auth/login` | público |
| GET/POST/PUT/DELETE | `/api/produtos`, `/api/categorias`, `/api/clientes` | logado |
| GET/POST/PUT/DELETE | `/api/formas-pagamento` | Gerente |
| GET/POST/PUT/DELETE | `/api/usuarios`, `/api/terminais` | Admin |
| GET/POST | `/api/estoque/movimentos` | logado |
| GET/POST | `/api/estoque/entradas`, GET `/api/estoque/entradas/{id}`, POST `/api/estoque/entradas/{id}/cancelar` | Gerente |
| GET | `/api/relatorios/{vendas-por-dia,mais-vendidos,por-forma,vendas,estoque-baixo}?de=AAAA-MM-DD&ate=AAAA-MM-DD` | Gerente |
| GET/POST/PUT | `/master/clientes`, `/master/pedidos` (aprovar/recusar) | cabeçalho `X-Master-Key` |
| POST/GET | `/cadastro` (pedido), `/cadastro/disponivel`, `/cadastro/{id}?token=` | público, com limite por IP |
| POST | `/api/importacao/produtos`, `/api/importacao/clientes` (até 1000 linhas por chamada) | Gerente |
| GET | `/api/sync/receber?desde=` · POST `/api/sync/enviar` | cabeçalho `X-Terminal-Key` |
| GET | `/api/vendas?numero=&data=&terminal=` · POST `/api/vendas/{id}/devolver` | Gerente |
| GET | `/api/clientes/fiado?devendo=true` · GET/POST `/api/clientes/{id}/fiado` | Gerente |
| GET | `/api/etiquetas` (preço do caixa + promoção em vigor) | Gerente |
| GET/POST | `/api/cargas/produtos`, `/api/cargas/usuarios` (+ `/publicar`), `/api/cargas/terminais` | permissão Enviar aos caixas |
| GET/POST | `/api/promocoes` | permissão Promoções |

DELETE apenas desativa (vendas antigas continuam apontando para o registro).

## 7. Próximos passos sugeridos

- Guardar o código em Git (uma pasta oficial, histórico de cada mudança)
- Módulo fiscal NFC-e (certificado A1, CSC, SEFAZ-SP)
- Filiais dentro de cada cliente
- Copiar os backups da VPS para fora dela automaticamente
