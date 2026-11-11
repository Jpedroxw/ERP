using System.Globalization;
using ProjetoJP.Controles;
using ProjetoJP.Gestao.Servicos;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;

namespace ProjetoJP.Gestao.Telas;

/// <summary>Login em duas colunas, igual ao do caixa: marca à esquerda (azul-marinho), formulário à direita.</summary>
public class LoginForm : Form
{
    private readonly ApiClient _api;
    private readonly CampoLogin _login = new() { Font = Tema.Fonte(13) };
    private readonly TextBox _senha = new() { Font = Tema.Fonte(13), UseSystemPasswordChar = true, MaxLength = 64 };
    private readonly BotaoTema _entrar = new() { Text = "Entrar", Size = new Size(380, 44), Font = Tema.Semibold(10.5f) };
    // Só o nome do usuário fica salvo; a senha é digitada sempre.
    private readonly CheckBox _lembrar = new() { Text = "Lembrar meu usuário", AutoSize = true, Checked = true,
        Font = Tema.Fonte(9.5f), ForeColor = Tema.TextoSuave };
    private readonly Pilula _pilula = new() { Text = "Verificando...", Cor = Tema.BarraTextoSuave };
    private readonly Label _status = new() { AutoSize = false, Font = Tema.Fonte(8.5f), ForeColor = Tema.BarraTextoSuave };

    public LoginForm(ApiClient api, string apiUrl)
    {
        _api = api;
        Ui.Dialogo(this, Aparencia.Titulo("Gestão"), 760, 470);
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;

        // Coluna da marca: nome do sistema, servidor e situação da conexão (mesmas posições do caixa)
        var marca = new Panel { Dock = DockStyle.Left, Width = 300, BackColor = Tema.Barra };
        Control icone = Aparencia.Logo is null && Ui.IconeApp is { } original
            ? new PictureBox { Image = original.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent, Size = new Size(52, 52), Location = new Point(36, 48) }
            : Aparencia.Marca(Icones.Relatorios, 52, new Point(36, 48));
        var nome = new Label
        {
            Text = TituloMarca, Font = Tema.Semibold(18), ForeColor = Color.White,
            AutoSize = false, Size = new Size(240, 64), Location = new Point(34, 110)
        };
        var descricao = new Label
        {
            Text = Aparencia.NomePersonalizado ? "Gestão · cadastros, estoque e relatórios" : "Cadastros, estoque e relatórios",
            Font = Tema.Fonte(10.5f), ForeColor = Tema.BarraTexto,
            AutoSize = true, Location = new Point(37, 178)
        };
        _pilula.Location = new Point(36, 222);
        // O endereço do servidor não aparece para o usuário: só "Servidor" e a situação.
        _status.SetBounds(37, 260, 240, 120);
        _status.Text = "Servidor";
        marca.Controls.AddRange([icone, nome, descricao, _pilula, _status]);

        // Coluna do formulário
        var titulo = new Label { Text = "Acessar a retaguarda", Font = Tema.Semibold(20), ForeColor = Tema.Texto, AutoSize = true, Location = new Point(338, 52) };
        var subtitulo = new Label { Text = "Entre com seu usuário e senha.", Font = Tema.Fonte(10), ForeColor = Tema.TextoSuave, AutoSize = true, Location = new Point(341, 96) };

        var l1 = Ui.Rotulo("Usuário", 9.5f);
        l1.ForeColor = Tema.TextoSuave;
        l1.Location = new Point(340, 140);
        _login.SetBounds(340, 164, 380, 32);

        var l2 = Ui.Rotulo("Senha", 9.5f);
        l2.ForeColor = Tema.TextoSuave;
        l2.Location = new Point(340, 212);
        _senha.SetBounds(340, 236, 380, 32);

        _lembrar.Location = new Point(340, 280);
        _entrar.Location = new Point(340, 314);
        _entrar.Icone = Icones.Cadeado;
        _entrar.Click += async (_, _) => await Entrar();

        var alterarServidor = new LinkLabel
        {
            Text = "Alterar servidor", AutoSize = true, Location = new Point(340, 376), Font = Tema.Fonte(9.5f),
            LinkColor = Tema.Primaria, ActiveLinkColor = Tema.PrimariaEscura, LinkBehavior = LinkBehavior.HoverUnderline
        };
        alterarServidor.LinkClicked += (_, _) => DialogResult = DialogResult.Retry;

        // Empresa nova: o cadastro é feito pelo site (um cadastro só; o pedido vai para o ERP Master aprovar).
        var criarCadastro = new LinkLabel
        {
            Text = "Criar cadastro da empresa", AutoSize = true, Location = new Point(340, 405), Font = Tema.Fonte(9.5f),
            LinkColor = Tema.Primaria, ActiveLinkColor = Tema.PrimariaEscura, LinkBehavior = LinkBehavior.HoverUnderline,
            Visible = CadastroEmpresa.ServidorConfigurado
        };
        criarCadastro.LinkClicked += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(EnderecoServidor.UrlServidorPadrao.TrimEnd('/') + "/#cadastro")
                {
                    UseShellExecute = true
                });
                _status.Text = "O cadastro da empresa abriu no navegador. Depois da aprovação, use \"Alterar servidor\" e " +
                               "digite o nome do servidor informado no site.";
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                Ui.Aviso(this, "Não foi possível abrir o navegador para o cadastro da empresa.\n\n" + ex.Message);
            }
        };

        Controls.AddRange([titulo, subtitulo, l1, _login, l2, _senha, _lembrar, _entrar, alterarServidor, criarCadastro]);
        // Usuário lembrado: vem preenchido e o cursor já vai para a senha.
        var lembrado = UltimoUsuario.Carregar(RetaguardaConfig.Carregar().Servidor);
        if (lembrado.Length > 0) _login.Text = lembrado;
        Controls.Add(marca);
        Aparencia.AplicarFundo(marca, 0.30f); // plano de fundo por trás da marca, se tiver
        AcceptButton = _entrar;
        Shown += async (_, _) =>
        {
            if (_login.Text.Length > 0) _senha.Focus(); else _login.Focus();
            await VerificarServidor();
            await ConferirPedido();
        };
    }

    /// <summary>
    /// Tem pedido de cadastro aguardando? Pergunta ao servidor. Aprovado → passa a usar o servidor da
    /// empresa e reabre o login (DialogResult.Yes); recusado → mostra o motivo.
    /// </summary>
    private async Task ConferirPedido()
    {
        var cfg = RetaguardaConfig.Carregar();
        if (cfg.PedidoId is not { } id || string.IsNullOrEmpty(cfg.PedidoToken)) return;

        SituacaoCadastroDto? s;
        try
        {
            s = await CadastroEmpresa.SituacaoAsync(id, cfg.PedidoToken);
        }
        catch (ApiException)
        {
            return; // sem internet: confere na próxima vez
        }
        if (IsDisposed) return;

        void Limpar()
        {
            cfg.PedidoId = null;
            cfg.PedidoToken = null;
        }

        switch (s?.Situacao)
        {
            case null:
                Limpar();
                cfg.Salvar();
                break;
            case "Pendente":
                _status.Text = "Cadastro da empresa em análise. Quando for aprovado, é só abrir a Gestão de novo.";
                break;
            case "Aprovado" when !string.IsNullOrEmpty(s.Codigo):
                Limpar();
                cfg.Servidor = s.Codigo;
                cfg.Salvar();
                Ui.Info(this, "Cadastro aprovado!\n\n" +
                              "Entre com o login e a senha informados no cadastro.\n\n" +
                              $"Nos caixas, em Opções → Servidor, digite: {CodigoCliente.ParaTela(s.Codigo)}");
                DialogResult = DialogResult.Yes; // o Program reabre o login já no servidor novo
                break;
            case "Recusado":
                Limpar();
                cfg.Salvar();
                Ui.Aviso(this, "O cadastro da empresa não foi aprovado." +
                               (string.IsNullOrWhiteSpace(s.Motivo) ? "" : "\n\nMotivo: " + s.Motivo));
                break;
        }
    }

    /// <summary>"Projeto JP Gestão", ou o nome da empresa (Aparência).</summary>
    internal static string TituloMarca => Aparencia.NomePersonalizado ? Aparencia.NomeSistema : Aparencia.NomePadrao + " Gestão";

    private async Task VerificarServidor()
    {
        var online = await _api.ServidorOnlineAsync();
        if (IsDisposed) return;
        _pilula.Text = online ? "Online" : "Offline";
        _pilula.Cor = online ? Tema.Sucesso : Tema.Alerta;
        _status.Text = online
            ? "Servidor conectado"
            : "Servidor sem resposta.\n\nVerifique a internet ou use \"Alterar servidor\".";
    }

    private async Task Entrar()
    {
        _entrar.Enabled = false;
        _entrar.Text = "Entrando...";
        try
        {
            var login = _login.Text.Trim().ToLowerInvariant();

            await _api.LoginAsync(login, _senha.Text);

            var servidor = RetaguardaConfig.Carregar().Servidor;
            if (_lembrar.Checked) UltimoUsuario.Salvar(servidor, login);
            else UltimoUsuario.Esquecer();

            // Versões anteriores tinham "Lembrar este computador" (chave TPM/Windows): desfaz, se existir.
            try { await AcessoLembrado.EsquecerAsync(_api); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException) { }
            DialogResult = DialogResult.OK;
        }
        catch (ApiException ex)
        {
            var msg = ex.Message;
            // Login recusado: o motivo mais comum é estar no servidor de outra empresa (ex.: "nuvem" em vez de "lojaabc").
            if (msg.StartsWith("Login ou senha inválidos", StringComparison.OrdinalIgnoreCase))
            {
                var servidor = RetaguardaConfig.Carregar().Servidor;
                if (!string.IsNullOrWhiteSpace(servidor) && !EnderecoServidor.PareceEndereco(servidor))
                    msg += $"\n\nVocê está entrando no servidor \"{EnderecoServidor.ParaTela(servidor)}\". Se a empresa foi criada com outro nome de " +
                           "servidor (o que aparece no Projeto JP Master, ex.: LOJA ABC), clique em \"Alterar servidor\" e digite esse nome.";
            }
            Ui.Aviso(this, msg);
            _senha.SelectAll();
            _senha.Focus();
        }
        finally
        {
            _entrar.Enabled = true;
            _entrar.Text = "Entrar";
        }
    }
}

/// <summary>
/// Janela principal da Retaguarda, em dois modos:
/// simples (blocos grandes, uma tela por vez, colunas essenciais) e
/// completo (menu lateral, várias telas em abas, painel com os números do dia).
/// </summary>
public class PrincipalForm : Form
{
    /// <summary>Uma tela do menu. Com Acao, o clique executa a ação (abre um diálogo) em vez de abrir uma tela.</summary>
    private sealed record Tela(string Chave, string Titulo, string Icone, string Descricao, string Grupo, bool Permitida,
        Func<bool, Form>? Criar, Action? Acao = null);

    private readonly ApiClient _api;
    private readonly List<Tela> _telas;
    private readonly System.Windows.Forms.Timer _relogio = new() { Interval = 20_000 };
    private bool _completo;
    private readonly PreferenciasInterface _prefs;
    private bool _online = true;
    private bool _saindo;
    public bool LogoutSolicitado { get; private set; }

    // Situação do servidor: bolinha da barra de status e pílula do topo (modo simples)
    private Label? _pontoStatus;
    private Label? _textoStatus;
    private Pilula? _pilulaTopo;

    // modo completo
    private AreaAbas? _abas;
    // visual 2026 (modo completo com menu lateral): menu com grupos, barra superior e pesquisa
    private MenuLateralERP? _menuNovo;
    private CampoBusca? _buscaTopo;
    private PesquisaGlobal? _pesquisa;
    private bool NovoLayout => _completo && _prefs.Menu == NavegacaoInterface.Lateral;
    private readonly List<(string chave, ItemMenu item)> _itensMenu = [];

    // modo simples
    private Panel? _corpo;

    public PrincipalForm(ApiClient api, string apiUrl, bool completo)
    {
        _api = api;
        // Alteração de usuários/terminais pedindo senha: abre a confirmação aqui mesmo e repete o pedido.
        _api.ConfirmarSenha = () =>
        {
            using var f = new ConfirmarSenhaForm(_api);
            return Task.FromResult(f.ShowDialog(this) == DialogResult.OK);
        };
        _prefs = PreferenciasInterface.Carregar("retaguarda", completo);
        _completo = _prefs.Modo == ModoInterface.Completo;

        var sessao = api.Sessao!;
        Text = Aparencia.Titulo("Gestão");
        Icon = Ui.IconeApp ?? Icon;
        Size = new Size(1280, 800);
        MinimumSize = new Size(800, 520);
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        BackColor = Tema.Fundo;
        Font = Tema.Fonte(10);

        _telas =
        [
            new Tela("produtos", "Produtos", Icones.Produtos, "Preços, códigos e estoque", "Cadastros", true,
                c => c ? new ProdutosEstoqueForm(_api, AbrirPorChave) : (Form)new CadastroForm<ProdutoDto>(_api, "Produtos", "api/produtos", c)),
            new Tela("cargas", "Enviar produtos aos caixas", Icones.Sincronizar, "Produtos novos e alterações de preço para os caixas", "Caixa", api.Pode(Permissoes.PublicarCargas),
                _ => new CargasForm(_api)),
            new Tela("carga-usuarios", "Enviar usuários aos caixas", Icones.Pessoa, "Usuários e senhas só chegam no caixa quando você enviar", "Caixa", api.Pode(Permissoes.PublicarCargas),
                _ => new UsuariosCargaForm(_api)),
            new Tela("promocoes", "Promoções", Icones.Pagamento, "Preços especiais com início e fim", "Cadastros", api.Pode(Permissoes.Promocoes),
                _ => new PromocoesForm(_api)),
            new Tela("clientes", "Clientes", Icones.Clientes, "CPF/CNPJ, telefone e endereço", "Cadastros", true,
                c => new CadastroForm<ClienteDto>(_api, "Clientes", "api/clientes", c, api.Pode(Permissoes.EditarClientes))),
            new Tela("categorias", "Categorias", Icones.Categoria, "Grupos de produtos", "Cadastros", true,
                c => new CadastroForm<CategoriaDto>(_api, "Categorias", "api/categorias", c, api.Pode(Permissoes.EditarProdutos))),
            new Tela("fornecedores", "Fornecedores", Icones.Fornecedor, "Empresas de quem a loja compra", "Cadastros", api.Pode(Permissoes.Fornecedores),
                c => new CadastroForm<FornecedorDto>(_api, "Fornecedores", "api/fornecedores", c)),
            new Tela("entrada", "Entrada de produtos", Icones.Importar, "Compras e notas de fornecedor, vários itens", "Estoque", api.Pode(Permissoes.EntradaProdutos),
                _ => new EntradaForm(_api)),
            new Tela("saida", "Saída de produtos", Icones.Exportar, "Perdas, vencidos, uso interno, devolução — vários itens", "Estoque", api.Pode(Permissoes.MovimentarEstoque),
                _ => new SaidaForm(_api)),
            new Tela("estoque", "Estoque", Icones.Estoque, "Movimentações de todos os produtos, ajustes e histórico", "Estoque", true,
                _ => new EstoqueForm(_api)),
            new Tela("etiquetas", "Etiqueta de gôndola", Icones.Etiqueta, "Nome, código e preço para a prateleira", "Estoque", api.EhGerente,
                _ => new EtiquetaGondolaForm(_api)),
            new Tela("fiado", "Fiado e devolução", Icones.Clientes, "Receber fiado e devolver um item", "Vendas", api.EhGerente,
                _ => new FiadoDevolucaoForm(_api)),
            new Tela("relatorios", "Relatórios", Icones.Relatorios, "Vendas, lucro, mais vendidos, estoque baixo", "Relatórios", api.Pode(Permissoes.VerVendas),
                _ => new RelatoriosForm(_api)),
            new Tela("orcamentos", "Orçamentos", Icones.Documento, "Orçamentos feitos nos caixas", "Vendas", api.Pode(Permissoes.VerOrcamentos),
                _ => new OrcamentosForm(_api)),
            new Tela("novo-operador", "Novo usuário do caixa", Icones.Novo, "Login e senha para entrar no caixa", "Caixa", api.EhAdmin,
                null, () => OperadorForm.Abrir(this, _api)),
            new Tela("usuarios", "Usuários", Icones.Pessoa, "Alterar senha, perfil ou desativar", "Caixa", api.EhAdmin,
                c => new CadastroForm<UsuarioDto>(_api, "Usuários", "api/usuarios", c)),
            new Tela("terminais", "Terminais (caixas)", Icones.Terminal, "Chave de cada caixa", "Caixa", api.EhAdmin,
                CriarTerminais),
            new Tela("formas", "Formas de pagamento", Icones.Pagamento, "Dinheiro, PIX, cartões", "Caixa", api.EhGerente,
                c => new CadastroForm<FormaPagamentoDto>(_api, "Formas de pagamento", "api/formas-pagamento", c)),
            new Tela("aparencia", "Aparência", Icones.Lua, "Tema escuro, cores, logo e plano de fundo", "Configurações", api.EhGerente,
                null, () => AparenciaForm.Abrir(this, "a Gestão", _completo, completo => { if (completo != _completo) BeginInvoke(new Action(TrocarModo)); })),
            new Tela("interface", "Interface e navegação", Icones.Configuracoes, "Modo, posição do menu e tamanho", "Configurações", true,
                null, () => PreferenciasInterfaceForm.Abrir(this, "retaguarda", _completo)),
            // Computadores lembrados, sessões de acesso, auditoria e "confirmar senha" saíram da Retaguarda:
            // agora ficam no ERP Master (botão "Segurança da loja"), para o dono do sistema ver cada loja.
            new Tela("importar", "Importar dados", Icones.Importar, "Produtos e clientes de outro sistema", "Administração", api.Pode(Permissoes.ImportarDados),
                _ => new ImportacaoForm(_api))
        ];

        Montar();

        _relogio.Tick += async (_, _) =>
        {
            AtualizarRelogio?.Invoke();
            await VerificarServidor();
        };
        _relogio.Start();
        Shown += async (_, _) => await VerificarServidor();
        FormClosed += (_, _) => _relogio.Dispose();
    }

    private Action? AtualizarRelogio;

    /// <summary>A cada 20 s confere se o servidor responde e atualiza Online/Offline, como no caixa.</summary>
    private async Task VerificarServidor()
    {
        var online = await _api.ServidorOnlineAsync();
        if (IsDisposed) return;
        _online = online;
        AplicarSituacao();
    }

    private void AplicarSituacao()
    {
        var sessao = _api.Sessao!;
        if (_pontoStatus is not null) _pontoStatus.ForeColor = _online ? Tema.Sucesso : Tema.Alerta;
        if (_textoStatus is not null)
            _textoStatus.Text = (_online ? "Servidor conectado" : "Servidor sem resposta — verifique a internet") +
                                $"    •    {sessao.Nome} ({sessao.Perfil})    •    Modo {(_completo ? "completo" : "simples")}";
        if (_pilulaTopo is not null)
        {
            _pilulaTopo.Text = NovoLayout ? (_online ? "Conectado" : "Sem conexão") : _online ? "Online" : "Offline";
            _pilulaTopo.Cor = _online ? Tema.Sucesso : Tema.Alerta;
        }
    }

    // ---------- Montagem ----------

    private void Montar()
    {
        SuspendLayout();
        foreach (var c in Controls.Cast<Control>().ToList())
        {
            Controls.Remove(c);
            c.Dispose();
        }
        _abas = null;
        _corpo = null;
        _pilulaTopo = null;
        _menuNovo = null;
        _menuSimples = null;
        _buscaTopo = null;
        _pesquisa?.Dispose();
        _pesquisa = null;
        _pontoStatus = null;
        _textoStatus = null;
        _itensMenu.Clear();

        if (_completo) MontarCompleto();
        else MontarSimples();

        // No visual novo a situação e a data ficam na barra superior.
        if (!NovoLayout) Controls.Add(BarraStatus()); // por último = fica embaixo de tudo
        AplicarSituacao();
        PreferenciasInterface.AplicarDensidade(this, _prefs.Tamanho);
        Resize -= AjustarParaTela;
        Resize += AjustarParaTela;
        AjustarParaTela(this, EventArgs.Empty);
        ResumeLayout(true);
    }

    private bool _menuEstreito = true;

    // Modo simples com menu lateral: o menu da vez e se ele está só com ícones.
    private Control? _menuSimples;
    private bool _menuSimplesEstreito;

    private void AjustarParaTela(object? sender, EventArgs e)
    {
        var estreito = ClientSize.Width < 1100;

        // Modo simples: em tela estreita (ex.: 1024×768) o menu lateral vira só ícones, como no modo completo.
        // Troca só o menu, sem fechar a tela aberta.
        if (_menuSimples is { } antigo && estreito != _menuSimplesEstreito && antigo.Parent == this)
        {
            var indice = Controls.GetChildIndex(antigo);
            var novo = MenuObjetivoLateral(estreito);
            SuspendLayout();
            Controls.Remove(antigo);
            antigo.Dispose();
            Controls.Add(novo);
            Controls.SetChildIndex(novo, indice);
            PreferenciasInterface.AplicarDensidade(novo, _prefs.Tamanho);
            ResumeLayout(true);
            _menuSimples = novo;
            _menuSimplesEstreito = estreito;
        }

        if (estreito == _menuEstreito && sender is not PrincipalForm) return;
        _menuEstreito = estreito;
        _menuNovo?.DefinirLargura(estreito);
    }

    private Control BarraStatus()
    {
        // Faixa clara no rodapé, como a mensagem de sincronização do caixa
        var barra = new Panel { Dock = DockStyle.Bottom, Height = 28, BackColor = Tema.Fundo };
        barra.Paint += (_, e) =>
        {
            using var linha = new Pen(Tema.Borda);
            e.Graphics.DrawLine(linha, 0, 0, barra.Width, 0);
        };

        _pontoStatus = new Label { Text = "●", ForeColor = Tema.Sucesso, AutoSize = true, Location = new Point(16, 5), Font = Tema.Fonte(9) };
        _textoStatus = new Label { ForeColor = Tema.TextoSuave, AutoSize = true, Location = new Point(32, 6), Font = Tema.Fonte(9) };
        var relogio = new Label { ForeColor = Tema.TextoSuave, AutoSize = true, Font = Tema.Fonte(9), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        void Atualizar()
        {
            relogio.Text = DateTime.Now.ToString("dddd, dd/MM/yyyy  HH:mm", new CultureInfo("pt-BR"));
            relogio.Location = new Point(barra.Width - relogio.PreferredWidth - 16, 6);
        }
        barra.Resize += (_, _) => Atualizar();
        AtualizarRelogio = Atualizar;
        Atualizar();

        barra.Controls.AddRange([_pontoStatus, _textoStatus, relogio]);
        return barra;
    }

    private void TrocarModo()
    {
        _completo = !_completo;
        _prefs.Modo = _completo ? ModoInterface.Completo : ModoInterface.Simples;
        try
        {
            _prefs.Salvar("retaguarda");
            var cfg = RetaguardaConfig.Carregar();
            cfg.ModoCompleto = _completo;
            cfg.Salvar();
        }
        catch (Exception)
        {
            // não conseguir gravar a preferência não impede a troca
        }
        _abas?.FecharTodas();
        Montar();
    }

    // Sair da conta volta à tela de login. O botão X continua fechando o aplicativo.
    private async Task SairAsync()
    {
        if (_saindo) return;
        _saindo = true;
        try { await _api.EncerrarSessaoAsync(); }
        finally
        {
            LogoutSolicitado = true;
            Close();
        }
    }

    // ---------- Modo completo ----------

    private void MontarCompleto()
    {
        _abas = new AreaAbas { Dock = DockStyle.Fill };
        Controls.Add(_abas);
        if (NovoLayout)
        {
            // Ordem importa: o último adicionado encaixa primeiro (menu na altura toda, barra no topo do resto).
            Controls.Add(BarraSuperior());
            Controls.Add(MenuNovo());
        }
        else
        {
            Controls.Add(_prefs.Menu switch
            {
                NavegacaoInterface.Superior => MenuSuperior(),
                NavegacaoInterface.Compacta => MenuCompacto(),
                _ => MenuLateral()
            });
        }

        if (_prefs.MostrarPainelInicial)
            _abas.Abrir("inicio", "Início", Icones.Inicio, () => new InicioForm(_api, AbrirPorChave), fixa: true);
        else
            AbrirPorChave("produtos");
        MarcarMenu(_prefs.MostrarPainelInicial ? "inicio" : "produtos");
    }

    private static readonly string[] ChavesObjetivas = ["produtos", "cargas", "promocoes", "clientes", "entrada", "estoque", "relatorios", "orcamentos"];

    private Control MenuSuperior(bool simples = false)
    {
        // Grupos no modo completo: evita uma barra longa com dezenas de itens.
        var menu = new MenuStrip
        {
            Dock = DockStyle.Top, Height = 42, BackColor = Tema.Superficie,
            Font = Tema.Fonte(10), GripStyle = ToolStripGripStyle.Hidden,
            Padding = new Padding(12, 3, 12, 3)
        };
        var inicio = new ToolStripMenuItem("Início");
        inicio.Click += (_, _) => AbrirPorChave("inicio");
        menu.Items.Add(inicio);
        var telas = _telas.Where(t => t.Permitida && t.Grupo != "Configurações"
            && (!simples || ChavesObjetivas.Contains(t.Chave)));
        if (simples)
        {
            foreach (var tela in telas)
            {
                var item = new ToolStripMenuItem(tela.Titulo);
                item.Click += (_, _) => AbrirPorChave(tela.Chave);
                menu.Items.Add(item);
            }
        }
        else
        {
            foreach (var grupo in telas.GroupBy(t => t.Grupo))
            {
                var menuGrupo = new ToolStripMenuItem(grupo.Key);
                foreach (var tela in grupo)
                {
                    var item = new ToolStripMenuItem(tela.Titulo);
                    item.Click += (_, _) => AbrirPorChave(tela.Chave);
                    menuGrupo.DropDownItems.Add(item);
                }
                menu.Items.Add(menuGrupo);
            }
        }
        var configuracoes = new ToolStripMenuItem("Configurações");
        foreach (var tela in _telas.Where(t => t.Permitida && t.Grupo == "Configurações"))
        {
            var item = new ToolStripMenuItem(tela.Titulo);
            item.Click += (_, _) => AbrirPorChave(tela.Chave);
            configuracoes.DropDownItems.Add(item);
        }
        configuracoes.DropDownItems.Add(new ToolStripSeparator());
        var trocar = new ToolStripMenuItem(_completo ? "Mudar para o modo simples" : "Mudar para o modo completo");
        trocar.Click += (_, _) => BeginInvoke(new Action(TrocarModo)); // depois do menu fechar (a tela é remontada)
        configuracoes.DropDownItems.Add(trocar);
        menu.Items.Add(configuracoes);
        var sairConta = new ToolStripMenuItem("Sair da conta");
        sairConta.Click += async (_, _) => await SairAsync();
        menu.Items.Add(sairConta);
        return menu;
    }

    private Control MenuCompacto(bool simples = false)
    {
        var acoes = new List<NavegacaoERP.Acao> { new("inicio", "Início", Icones.Inicio, () => AbrirPorChave("inicio")) };
        foreach (var tela in _telas.Where(t => t.Permitida && (!simples || ChavesObjetivas.Contains(t.Chave))))
            acoes.Add(new(tela.Chave, tela.Titulo, tela.Icone, () => AbrirPorChave(tela.Chave)));
        if (simples)
        {
            acoes.Add(new("interface", "Configurações", Icones.Configuracoes, () => AbrirPorChave("interface")));
        }
        acoes.Add(new("modo", simples ? "Modo completo" : "Modo simples", Icones.Alternar, () => BeginInvoke(new Action(TrocarModo))));
        acoes.Add(new("logout", "Sair da conta", Icones.Sair, () => { _ = SairAsync(); }));
        return NavegacaoERP.Lateral("Gestão", Ui.IconeApp, true, acoes, "inicio");
    }

    private Control MenuObjetivoLateral(bool soIcones = false)
    {
        var acoes = new List<NavegacaoERP.Acao> { new("inicio", "Início", Icones.Inicio, () => AbrirPorChave("inicio")) };
        foreach (var tela in _telas.Where(t => t.Permitida && ChavesObjetivas.Contains(t.Chave)))
            acoes.Add(new(tela.Chave, tela.Titulo, tela.Icone, () => AbrirPorChave(tela.Chave)));
        acoes.Add(new("interface", "Configurações", Icones.Configuracoes, () => AbrirPorChave("interface")));
        acoes.Add(new("logout", "Sair da conta", Icones.Sair, () => { _ = SairAsync(); }));
        return NavegacaoERP.Lateral("Gestão", Ui.IconeApp, soIcones, acoes, "inicio");
    }

    private static Control MarcaOriginal(int tamanho, Point local)
    {
        if (Aparencia.Logo is not null || Ui.IconeApp is null)
            return Aparencia.Marca(Icones.Relatorios, tamanho, local);
        return new PictureBox
        {
            Image = Ui.IconeApp.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent, Size = new Size(tamanho, tamanho), Location = local
        };
    }

    private Control MenuLateral()
    {
        var sessao = _api.Sessao!;
        const int largura = 236;
        var menu = new Panel { Dock = DockStyle.Left, Width = largura, BackColor = Tema.Barra };

        // Marca
        var marca = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = Tema.Barra };
        marca.Controls.Add(MarcaOriginal(32, new Point(14, 20)));
        var tituloMenu = new Label { Text = Aparencia.NomeSistema, Font = Tema.Semibold(14), ForeColor = Color.White, AutoSize = false, AutoEllipsis = true, Size = new Size(largura - 60, 26), Location = new Point(52, 16) };
        marca.Controls.Add(tituloMenu);
        marca.Controls.Add(new Label { Text = "Gestão", Font = Tema.Fonte(8.5f), ForeColor = Tema.BarraTextoSuave, AutoSize = true, Location = new Point(54, 42) });

        // Itens
        var lista = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Tema.Barra };
        var yMenu = 4;
        void Adicionar(Control c)
        {
            c.Dock = DockStyle.None;
            c.Location = new Point(0, yMenu);
            c.Width = largura - SystemInformation.VerticalScrollBarWidth - 2;
            lista.Controls.Add(c);
            yMenu += c.Height;
        }

        var inicio = new ItemMenu(Icones.Inicio, "Início");
        inicio.Click += (_, _) => AbrirPorChave("inicio");
        _itensMenu.Add(("inicio", inicio));
        Adicionar(inicio);

        foreach (var grupo in _telas.Where(t => t.Permitida).GroupBy(t => t.Grupo))
        {
            Adicionar(new GrupoMenu(grupo.Key));
            foreach (var tela in grupo)
            {
                var item = new ItemMenu(tela.Icone, tela.Titulo);
                item.Click += (_, _) => AbrirPorChave(tela.Chave);
                _itensMenu.Add((tela.Chave, item));
                Adicionar(item);
            }
        }

        lista.AutoScrollMinSize = new Size(0, yMenu + 4);
        void AjustarMenu()
        {
            var larguraItem = Math.Max(100, lista.ClientSize.Width -
                (lista.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0) - 2);
            foreach (Control item in lista.Controls) item.Width = larguraItem;
        }
        lista.Resize += (_, _) => AjustarMenu();
        AjustarMenu();

        // Rodapé: usuário, trocar modo, sair
        var rodape = new Panel { Dock = DockStyle.Bottom, Height = 138, BackColor = Tema.Barra, Padding = new Padding(12, 10, 12, 12) };
        rodape.Paint += (_, e) =>
        {
            using var linha = new Pen(Tema.BarraHover);
            e.Graphics.DrawLine(linha, 12, 0, rodape.Width - 12, 0);
        };
        rodape.Controls.Add(new Label
        {
            Text = sessao.Nome, Font = Tema.Semibold(10), ForeColor = Color.White,
            AutoSize = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(largura - 24, 25), Location = new Point(12, 8)
        });
        rodape.Controls.Add(new Label
        {
            Text = sessao.Perfil.ToString(), Font = Tema.Fonte(8.5f), ForeColor = Tema.BarraTextoSuave,
            AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(largura - 24, 20), Location = new Point(12, 34)
        });
        // Troca rápida de modo (sem reiniciar): continua aqui, além de Configurações → Interface.
        var modo = new BotaoTema { Text = "Modo simples", Estilo = EstiloBotao.NaBarra, Icone = Icones.Alternar, Size = new Size(largura - 24, 32), Location = new Point(12, 58), Font = Tema.Fonte(9.5f) };
        modo.Click += (_, _) => TrocarModo();
        new ToolTip().SetToolTip(modo, "Blocos grandes e só as informações principais");
        var sair = new BotaoTema { Text = "Sair da conta", Estilo = EstiloBotao.NaBarra, Icone = Icones.Sair, Size = new Size(largura - 24, 32), Location = new Point(12, 96), Font = Tema.Fonte(9.5f) };
        sair.Click += async (_, _) => await SairAsync();
        rodape.Controls.AddRange([modo, sair]);

        menu.Controls.Add(lista);
        menu.Controls.Add(rodape);
        menu.Controls.Add(marca);
        return menu;
    }

    private void MarcarMenu(string chave)
    {
        foreach (var (c, item) in _itensMenu) item.Ativo = c == chave;
        _menuNovo?.Marcar(chave);
    }

    // ---------- Visual 2026: menu com grupos e barra superior ----------

    private Control MenuNovo()
    {
        MenuLateralERP.Item? ItemDe(string chave, string? texto = null)
        {
            var t = _telas.FirstOrDefault(x => x.Chave == chave && x.Permitida);
            return t is null ? null : new MenuLateralERP.Item(chave, texto ?? t.Titulo, t.Icone, () => AbrirPorChave(chave));
        }
        MenuLateralERP.Grupo? GrupoDe(string texto, string icone, params MenuLateralERP.Item?[] itens)
        {
            var lista = itens.OfType<MenuLateralERP.Item>().ToList();
            return lista.Count == 0 ? null : new MenuLateralERP.Grupo(texto, icone, lista);
        }

        var entradas = new List<MenuLateralERP.Entrada?>
        {
            new MenuLateralERP.Item("inicio", "Dashboard", Icones.Inicio, () => AbrirPorChave("inicio")),
            GrupoDe("Vendas", Icones.Carrinho, ItemDe("fiado"), ItemDe("orcamentos"), ItemDe("promocoes")),
            // Tudo que é dos caixas num lugar só (antes ficava espalhado em Vendas e Configurações).
            GrupoDe("Caixas", Icones.Terminal, ItemDe("terminais", "Caixas (terminais)"), ItemDe("cargas"),
                ItemDe("carga-usuarios"), ItemDe("formas"), ItemDe("novo-operador"), ItemDe("usuarios", "Usuários e permissões")),
            ItemDe("produtos"),
            GrupoDe("Estoque", Icones.Estoque, ItemDe("estoque", "Movimentação de estoque"), ItemDe("entrada"), ItemDe("saida"),
                ItemDe("etiquetas"), ItemDe("categorias")),
            ItemDe("clientes"),
            ItemDe("fornecedores"),
            ItemDe("relatorios"),
            GrupoDe("Configurações", Icones.Configuracoes,
                new MenuLateralERP.Item("modo-simples", "Mudar para o modo simples", Icones.Alternar, () => BeginInvoke(new Action(TrocarModo))),
                ItemDe("importar"), ItemDe("aparencia"), ItemDe("interface"))
        };
        var nome = Aparencia.NomeSistema;
        var icone = Aparencia.Logo ?? Ui.IconeApp?.ToBitmap();
        _menuNovo = new MenuLateralERP(nome, "Gestão", icone, entradas.OfType<MenuLateralERP.Entrada>());
        return _menuNovo;
    }

    /// <summary>Nome mostrado em "Empresa:": o da Aparência ou o nome do servidor (nunca o endereço do site).</summary>
    private static string NomeEmpresa()
    {
        if (Aparencia.NomePersonalizado) return Aparencia.NomeSistema;
        var servidor = RetaguardaConfig.Carregar().Servidor;
        return string.IsNullOrWhiteSpace(servidor) || EnderecoServidor.PareceEndereco(servidor) ? "Minha empresa" : EnderecoServidor.ParaTela(servidor);
    }

    private Control BarraSuperior()
    {
        var sessao = _api.Sessao!;
        var br = new CultureInfo("pt-BR");
        var topo = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Tema.Superficie };
        topo.Paint += (_, e) =>
        {
            using var linha = new Pen(Tema.Borda);
            e.Graphics.DrawLine(linha, 0, topo.Height - 1, topo.Width, topo.Height - 1);
        };

        var tabela = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Tema.Superficie,
            Padding = new Padding(20, 0, 16, 1), Margin = Padding.Empty
        };
        tabela.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tabela.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tabela.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tabela.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // Empresa
        var esquerda = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left, Margin = Padding.Empty, BackColor = Tema.Superficie };
        var iconeEmpresa = new Label { Text = Icones.Loja, Font = Tema.FonteIcone(13), ForeColor = Tema.Texto, AutoSize = true, Margin = new Padding(0, 10, 8, 0) };
        var rotuloEmpresa = new Label { Text = "Empresa:", Font = Tema.Semibold(10), ForeColor = Tema.Texto, AutoSize = true, Margin = new Padding(0, 10, 8, 0) };
        var nomeEmpresa = NomeEmpresa();
        var empresa = new BotaoTema
        {
            Text = nomeEmpresa, Estilo = EstiloBotao.Secundario, IconeDireita = Icones.ChevronBaixo, Font = Tema.Fonte(10),
            Size = new Size(Math.Min(280, TextRenderer.MeasureText(nomeEmpresa, Tema.Fonte(10)).Width + 52), 36), Margin = Padding.Empty
        };
        var menuEmpresa = new ContextMenuStrip();
        menuEmpresa.Items.Add(new ToolStripMenuItem(nomeEmpresa) { Enabled = false });
        menuEmpresa.Items.Add(new ToolStripSeparator());
        menuEmpresa.Items.Add("Aparência (nome, cores e logo)", null, (_, _) => AbrirPorChave("aparencia")).Enabled = _api.EhGerente;
        menuEmpresa.Items.Add("Entrar em outra empresa (sair da conta)", null, (_, _) => { _ = SairAsync(); });
        empresa.Click += (_, _) => menuEmpresa.Show(empresa, new Point(0, empresa.Height));
        esquerda.Controls.AddRange([iconeEmpresa, rotuloEmpresa, empresa]);

        // Pesquisa
        var busca = new CampoBusca { Dica = "Pesquisar no sistema... (Ctrl + F)", Anchor = AnchorStyles.Left, Margin = new Padding(28, 0, 16, 0) };
        _buscaTopo = busca;

        // Situação, usuário e data
        var direita = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = Padding.Empty, BackColor = Tema.Superficie };
        _pilulaTopo = new Pilula { Text = "Conectado", Cor = Tema.Sucesso, Margin = new Padding(0, 6, 14, 0) };
        Control Separador() => new Panel { Size = new Size(1, 28), BackColor = Tema.Borda, Margin = new Padding(0, 5, 10, 0) };
        var usuario = new BotaoTema
        {
            Text = sessao.Nome, Estilo = EstiloBotao.Transparente, Icone = Icones.Pessoa, IconeDireita = Icones.ChevronBaixo,
            Font = Tema.Semibold(10), Margin = new Padding(0, 0, 10, 0)
        };
        usuario.Size = new Size(Math.Min(240, TextRenderer.MeasureText(sessao.Nome, usuario.Font).Width + 70), 38);
        var menuUsuario = new ContextMenuStrip();
        menuUsuario.Items.Add(new ToolStripMenuItem($"{sessao.Nome} ({sessao.Perfil})") { Enabled = false });
        menuUsuario.Items.Add(new ToolStripSeparator());
        menuUsuario.Items.Add("Mudar para o modo simples", null, (_, _) => BeginInvoke(new Action(TrocarModo)));
        menuUsuario.Items.Add("Interface e navegação", null, (_, _) => AbrirPorChave("interface"));
        menuUsuario.Items.Add(new ToolStripSeparator());
        menuUsuario.Items.Add("Sair da conta", null, (_, _) => { _ = SairAsync(); });
        usuario.Click += (_, _) => menuUsuario.Show(usuario, new Point(0, usuario.Height));
        var iconeData = new Label { Text = Icones.Calendario, Font = Tema.FonteIcone(12), ForeColor = Tema.Texto, AutoSize = true, Margin = new Padding(0, 11, 6, 0) };
        var data = new Label { Font = Tema.Semibold(10), ForeColor = Tema.Texto, AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
        AtualizarRelogio = () =>
        {
            var agora = DateTime.Now;
            var dia = agora.ToString("ddd", br).TrimEnd('.');
            data.Text = $"{agora:dd/MM/yyyy} ({(dia.Length > 0 ? char.ToUpper(dia[0], br) + dia[1..] : dia)})";
        };
        AtualizarRelogio();
        direita.Controls.AddRange([_pilulaTopo, Separador(), usuario, Separador(), iconeData, data]);

        tabela.Controls.Add(esquerda, 0, 0);
        tabela.Controls.Add(busca, 1, 0);
        tabela.Controls.Add(direita, 2, 0);
        void AjustarBusca()
        {
            var colunas = tabela.GetColumnWidths();
            if (colunas.Length < 2) return;
            if (!busca.IsDisposed) busca.Size = new Size(Math.Clamp(colunas[1] - 44, 120, 520), 38);
        }
        tabela.Resize += (_, _) => AjustarBusca();
        tabela.Layout += (_, _) => AjustarBusca();
        topo.Controls.Add(tabela);

        var destinos = new List<PesquisaGlobal.Destino> { new("inicio", "Dashboard", "Resumo do dia e atalhos", Icones.Inicio) };
        destinos.AddRange(_telas.Where(t => t.Permitida).Select(t => new PesquisaGlobal.Destino(t.Chave, t.Titulo, t.Descricao, t.Icone)));
        _pesquisa = new PesquisaGlobal(this, busca, _api, destinos, AbrirPorChave, AbrirProduto);
        return topo;
    }

    /// <summary>Abre Produtos e Estoque já no produto escolhido (pesquisa do topo).</summary>
    private void AbrirProduto(Guid id)
    {
        AbrirPorChave("produtos");
        if (_abas?.Tela("produtos") is ProdutosEstoqueForm tela) tela.SelecionarProduto(id);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Ctrl + F: pesquisa do topo (telas e produtos), de qualquer tela.
        if (keyData == (Keys.Control | Keys.F) && _buscaTopo is { IsDisposed: false } busca)
        {
            busca.Caixa.Focus();
            busca.Caixa.SelectAll();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>Abre a tela pela chave (menu lateral, atalhos do painel, blocos do modo simples).</summary>
    private void AbrirPorChave(string chave)
    {
        if (chave == "inicio")
        {
            if (_completo) _abas?.Abrir("inicio", "Início", Icones.Inicio, () => new InicioForm(_api, AbrirPorChave), fixa: true);
            else MostrarInicioSimples();
            MarcarMenu("inicio");
            return;
        }

        var tela = _telas.FirstOrDefault(t => t.Chave == chave && t.Permitida);
        if (tela is null) return;
        if (tela.Acao is not null)
        {
            tela.Acao();
            return;
        }

        if (_completo)
        {
            _abas?.Abrir(tela.Chave, tela.Titulo, tela.Icone, () => tela.Criar!(true));
            PreferenciasInterface.AplicarDensidade(this, _prefs.Tamanho);
            MarcarMenu(chave);
        }
        else
        {
            AbrirSimples(tela);
        }
    }

    // ---------- Modo simples ----------

    private void MontarSimples()
    {
        _corpo = new Panel { Dock = DockStyle.Fill, BackColor = Tema.Fundo };
        Controls.Add(_corpo);
        Controls.Add(_prefs.Menu switch
        {
            NavegacaoInterface.Superior => MenuSuperior(simples: true),
            NavegacaoInterface.Compacta => MenuCompacto(simples: true),
            _ => _menuSimples = MenuObjetivoLateral(_menuSimplesEstreito = ClientSize.Width < 1100)
        });
        Controls.Add(BarraTopoSimples());
        if (_prefs.MostrarPainelInicial) MostrarInicioSimples();
        else AbrirPorChave("produtos");
    }

    private Control BarraTopoSimples()
    {
        // Mesmo topo do caixa: ícone, título, linha de informações e a pílula Online/Offline à direita.
        var sessao = _api.Sessao!;
        var topo = new Panel { Dock = DockStyle.Top, Height = 62, BackColor = Tema.Barra };
        topo.Controls.Add(MarcaOriginal(32, new Point(14, 15)));
        topo.Controls.Add(new Label { Text = TituloMarcaTopo, Font = Tema.Semibold(14), ForeColor = Color.White, AutoSize = true, Location = new Point(54, 9) });
        var usuarioTopo = new Label
        {
            Text = $"{sessao.Nome} ({sessao.Perfil})",
            Font = Tema.Fonte(9), ForeColor = Tema.BarraTextoSuave,
            AutoSize = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(56, 34), Size = new Size(400, 24)
        };
        topo.Controls.Add(usuarioTopo);

        var direita = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.RightToLeft,
            BackColor = Tema.Barra, Padding = new Padding(0, 13, 12, 0)
        };
        var sair = new BotaoTema { Text = "Sair da conta", Estilo = EstiloBotao.NaBarra, Icone = Icones.Sair, Size = new Size(142, 36), Font = Tema.Fonte(10) };
        sair.Click += async (_, _) => await SairAsync();
        var modo = new BotaoTema { Text = "Modo completo", Estilo = EstiloBotao.NaBarra, Icone = Icones.Alternar, Size = new Size(150, 36), Font = Tema.Fonte(10) };
        modo.Click += (_, _) => TrocarModo();
        new ToolTip().SetToolTip(modo, "Menu, várias telas em abas e todas as colunas");
        _pilulaTopo = new Pilula { Text = "Online", Margin = new Padding(0, 5, 12, 0) };
        direita.Controls.AddRange([sair, modo, _pilulaTopo]);
        topo.Controls.Add(direita);
        void AjustarNome() => usuarioTopo.Width = Math.Max(120, topo.ClientSize.Width - direita.Width - 70);
        topo.Resize += (_, _) => AjustarNome();
        AjustarNome();
        return topo;
    }

    private static string TituloMarcaTopo =>
        Aparencia.NomePersonalizado ? $"{Aparencia.NomeSistema}  ·  Gestão" : Aparencia.NomePadrao + " Gestão";

    private void LimparCorpo()
    {
        if (_corpo is null) return;
        foreach (var c in _corpo.Controls.Cast<Control>().ToList())
        {
            _corpo.Controls.Remove(c);
            c.Dispose();
        }
    }

    private void MostrarInicioSimples()
    {
        if (_corpo is null) return;
        LimparCorpo();
        var sessao = _api.Sessao!;

        var area = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoScroll = true, BackColor = Tema.Fundo,
            Padding = new Padding(48, 36, 48, 24)
        };

        var ola = new Label { Text = "Acessos rápidos", Font = Tema.Semibold(22), ForeColor = Tema.Texto, AutoSize = true, Margin = new Padding(0, 0, 0, 2) };
        var sub = new Label { Text = "Cadastros, entradas e relatórios em poucos cliques.", Font = Tema.Fonte(11), ForeColor = Tema.TextoSuave, AutoSize = true, Margin = new Padding(2, 0, 0, 24) };
        area.Controls.Add(ola);
        area.SetFlowBreak(ola, true);
        area.Controls.Add(sub);
        area.SetFlowBreak(sub, true);

        void Bloco(Tela tela)
        {
            var b = new BlocoAtalho(tela.Icone, tela.Titulo, tela.Descricao);
            b.Click += (_, _) => AbrirSimples(tela);
            area.Controls.Add(b);
        }

        var principais = new[] { "produtos", "clientes", "entrada", "relatorios" };
        Control? ultimo = null;
        foreach (var t in _telas.Where(t => t.Permitida && principais.Contains(t.Chave)))
        {
            Bloco(t);
            ultimo = area.Controls[^1];
        }
        if (ultimo is not null) area.SetFlowBreak(ultimo, true);

        // No modo simples, os recursos continuam no menu, mas o painel inicial fica objetivo.
        var ajuda = new Label
        {
            Text = "Para consultar todas as ferramentas, altere para Modo Completo em Configurações > Interface e navegação.",
            Font = Tema.Fonte(9), ForeColor = Tema.TextoSuave, AutoSize = false,
            Width = 730, Height = 45, Margin = new Padding(3, 18, 0, 0)
        };
        area.Controls.Add(ajuda);

        // Textos soltos ficam transparentes para o plano de fundo (se tiver) aparecer; os blocos continuam sólidos.
        _corpo.Controls.Add(area);
        Aparencia.AplicarFundo(area);
    }

    private void AbrirSimples(Tela tela)
    {
        if (tela.Acao is not null)
        {
            tela.Acao();
            return;
        }
        if (_corpo is null || tela.Criar is null) return;
        LimparCorpo();

        var barra = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Tema.Superficie, Padding = new Padding(16, 10, 16, 10) };
        barra.Paint += (_, e) =>
        {
            using var linha = new Pen(Tema.Borda);
            e.Graphics.DrawLine(linha, 0, barra.Height - 1, barra.Width, barra.Height - 1);
        };
        var voltar = new BotaoTema { Text = "Início", Estilo = EstiloBotao.Secundario, Icone = Icones.Voltar, Size = new Size(100, 36), Location = new Point(16, 10) };
        voltar.Click += (_, _) => MostrarInicioSimples();
        var caminho = new Label { Text = "Início  ›  " + tela.Titulo, Font = Tema.Fonte(10), ForeColor = Tema.TextoSuave, AutoSize = true, Location = new Point(132, 19) };
        barra.Controls.AddRange([voltar, caminho]);

        var form = tela.Criar(false);
        form.TopLevel = false;
        form.FormBorderStyle = FormBorderStyle.None;
        // Sem o tamanho mínimo da janela: em monitor pequeno a tela encolhe junto (e rola) em vez de ficar cortada.
        form.MinimumSize = Size.Empty;
        form.AutoScroll = true;
        form.Dock = DockStyle.Fill;
        form.FormClosed += (_, _) => BeginInvoke(new Action(MostrarInicioSimples));

        _corpo.Controls.Add(form);
        _corpo.Controls.Add(barra);
        form.Show();
        PreferenciasInterface.AplicarDensidade(form, _prefs.Tamanho);
        form.Focus();
    }

    // ---------- Telas com comportamento próprio ----------

    private Form CriarTerminais(bool completo) => new CadastroForm<TerminalDto>(_api, "Terminais (caixas)", "api/terminais", completo)
    {
        AposSalvar = (t, novo) =>
        {
            if (!novo || string.IsNullOrEmpty(t.Chave)) return;
            Clipboard.SetText(t.Chave);
            // O caixa precisa usar o MESMO servidor desta Retaguarda (a chave só existe no banco desta empresa).
            var servidor = RetaguardaConfig.Carregar().Servidor;
            Ui.Info(null,
                $"Terminal \"{t.Nome}\" criado.\n\nChave (já copiada para a área de transferência):\n{t.Chave}\n\n" +
                "No caixa, abra Opções → Servidor:\n" +
                (EnderecoServidor.PareceEndereco(servidor) ? "" : $"  • em Servidor, digite: {EnderecoServidor.ParaTela(servidor)}\n") +
                "  • em Chave do terminal, clique em Colar\n  • clique em Testar conexão e depois em Salvar.");
        }
    };
}
