using System.Globalization;
using ProjetoJP.Controles;
using ProjetoJP.Master.Servicos;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;

namespace ProjetoJP.Master.Telas;

/// <summary>
/// ERP Master: clientes (empresas, cada uma com o seu banco) e pedidos de cadastro feitos pelas lojas.
/// </summary>
public class MasterForm : Form
{
    private static readonly CultureInfo Br = new("pt-BR");

    private MasterConfig _cfg;
    private readonly PreferenciasInterface _prefs = PreferenciasInterface.Carregar("master");
    private readonly Panel _paginaInicio = new() { Dock = DockStyle.Fill, BackColor = Tema.Fundo };
    private MasterApi _api;

    private readonly DataGridView _gradeClientes = NovaGrade();
    private readonly DataGridView _gradePedidos = NovaGrade();
    private readonly Panel _paginaClientes = new() { Dock = DockStyle.Fill, BackColor = Tema.Fundo };
    private readonly Panel _paginaPedidos = new() { Dock = DockStyle.Fill, BackColor = Tema.Fundo };
    private readonly BotaoTema _abaClientes = new() { Text = "Clientes", Icone = Icones.Loja, Size = new Size(150, 38) };
    private readonly BotaoTema _abaPedidos = new() { Text = "Pedidos de cadastro", Icone = Icones.Documento, Size = new Size(230, 38) };
    private readonly ComboBox _filtroPedidos = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, Font = Tema.Fonte(10) };
    private readonly Pilula _pilula = new() { Text = "Conectando...", Cor = Tema.BarraTextoSuave };
    private readonly Label _servidor = new() { AutoSize = true, Font = Tema.Fonte(9), ForeColor = Tema.BarraTextoSuave, Location = new Point(56, 38) };
    private readonly Label _rodape = new() { Dock = DockStyle.Fill, ForeColor = Tema.TextoSuave, Font = Tema.Fonte(9), TextAlign = ContentAlignment.MiddleLeft };
    private readonly System.Windows.Forms.Timer _relogio = new() { Interval = 60_000 };

    private List<ClienteMasterDto> _clientes = [];
    private List<PedidoCadastroDto> _pedidos = [];
    private int _pendentes;
    private readonly Label _totalClientesVisual = ResumoValor();
    private readonly Label _ativosVisual = ResumoValor();
    private readonly Label _pedidosVisual = ResumoValor();

    private static Label ResumoValor() => new()
    {
        Text = "—", Font = Tema.Semibold(18), ForeColor = Tema.Texto,
        AutoSize = false, Size = new Size(170, 35), TextAlign = ContentAlignment.MiddleLeft
    };

    private static Panel BlocoResumo(string titulo, Label valor)
    {
        var painel = new Panel
        {
            BackColor = Tema.Superficie, Width = 194, Height = 70,
            BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 0, 10, 0)
        };
        painel.Controls.Add(new Label
        {
            Text = titulo, ForeColor = Tema.TextoSuave,
            Font = Tema.Fonte(9), Location = new Point(13, 8), Size = new Size(175, 20)
        });
        valor.Location = new Point(12, 30);
        painel.Controls.Add(valor);
        return painel;
    }

    private Control CriarResumo()
    {
        var faixa = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 90, BackColor = Tema.Fundo,
            WrapContents = false, Padding = new Padding(3, 7, 0, 5)
        };
        faixa.Controls.AddRange([
            BlocoResumo("Empresas cadastradas", _totalClientesVisual),
            BlocoResumo("Empresas ativas", _ativosVisual),
            BlocoResumo("Pedidos pendentes", _pedidosVisual)
        ]);
        faixa.Visible = _prefs.Modo == ModoInterface.Completo;
        return faixa;
    }

    private void AtualizarResumo()
    {
        _totalClientesVisual.Text = _clientes.Count.ToString();
        _ativosVisual.Text = _clientes.Count(c => c.Ativo).ToString();
        _pedidosVisual.Text = _pendentes.ToString();
    }


    public MasterForm(MasterConfig cfg)
    {
        _cfg = cfg;
        _api = new MasterApi(cfg);

        Text = Aparencia.NomePadrao + " Master";
        Icon = Ui.IconeApp ?? Icon;
        Size = new Size(1240, 760);
        MinimumSize = new Size(900, 560);
        StartPosition = FormStartPosition.CenterScreen;
        Load += (_, _) =>
        {
            Ui.Encaixar(this);  // escala do Windows + cabe na tela
            if (Width >= Screen.FromControl(this).WorkingArea.Width - 40) WindowState = FormWindowState.Maximized;
        };
        BackColor = Tema.Fundo;
        Font = Tema.Fonte(10);

        // ----- Topo -----
        var topo = new Panel { Dock = DockStyle.Top, Height = 62, BackColor = Tema.Barra };
        if (Ui.IconeApp is { } iconeOriginal)
            topo.Controls.Add(new PictureBox { Image = iconeOriginal.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent,
                Size = new Size(32, 32), Location = new Point(14, 15) });
        topo.Controls.Add(new Label { Text = Aparencia.NomePadrao + " Master", Font = Tema.Semibold(14), ForeColor = Color.White, AutoSize = true, Location = new Point(54, 9) });
        topo.Controls.Add(_servidor);
        var direita = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.RightToLeft,
            BackColor = Tema.Barra, Padding = new Padding(0, 13, 12, 0)
        };
        var conexao = new BotaoTema { Text = "Conexão", Estilo = EstiloBotao.NaBarra, Icone = Icones.Servidor, Size = new Size(120, 36), Font = Tema.Fonte(10) };
        conexao.Click += async (_, _) => await TrocarConexao();
        var atualizar = new BotaoTema { Text = "Atualizar", Estilo = EstiloBotao.NaBarra, Icone = Icones.Atualizar, Size = new Size(120, 36), Font = Tema.Fonte(10) };
        atualizar.Click += async (_, _) => await CarregarTudo();
        var aparencia = new BotaoTema { Text = "Cor", Estilo = EstiloBotao.NaBarra, Icone = Icones.Lua, Size = new Size(90, 36), Font = Tema.Fonte(10) };
        aparencia.Click += (_, _) => AparenciaForm.Abrir(this, "o Projeto JP Master");
        _pilula.Margin = new Padding(0, 5, 12, 0);
        // Configurações fica no menu de navegação (não repete aqui no topo).
        direita.Controls.AddRange([aparencia, conexao, atualizar, _pilula]);
        topo.Controls.Add(direita);

        // ----- Abas -----
        var abas = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 57, BackColor = Tema.Fundo, Padding = new Padding(18, 11, 18, 0), WrapContents = false };
        _abaClientes.Margin = new Padding(0, 0, 8, 0);
        _abaClientes.Click += (_, _) => MostrarAba(true);
        _abaPedidos.Click += (_, _) => MostrarAba(false);
        abas.Controls.AddRange([_abaClientes, _abaPedidos]);

        // ----- Páginas -----
        MontarClientes();
        MontarPedidos();
        var corpo = new Panel { Dock = DockStyle.Fill, BackColor = Tema.Fundo, Padding = new Padding(18, 4, 18, 8) };
        corpo.Controls.Add(_paginaPedidos);
        corpo.Controls.Add(_paginaClientes);
        CriarInicio();
        corpo.Controls.Add(_paginaInicio);

        var faixa = new Panel { Dock = DockStyle.Bottom, Height = 28, BackColor = Tema.Fundo, Padding = new Padding(20, 0, 0, 0) };
        faixa.Controls.Add(_rodape);

        Controls.Add(corpo);
        Controls.Add(faixa);
        Controls.Add(CriarNavegacao());
        // A faixa de abas (Clientes / Pedidos) repetia o menu de navegação: fica fora da tela.
        // O número de pedidos aguardando aparece no rodapé e no resumo.
        Controls.Add(topo);

        MostrarAba(true);
        if (_prefs.MostrarPainelInicial) MostrarInicio();
        AplicarModoEGrade();
        AtualizarServidor();
        Shown += async (_, _) =>
        {
            await CarregarTudo();
            AplicarModoEGrade();
        };
        _relogio.Tick += async (_, _) => await CarregarPedidos(silencioso: true);
        _relogio.Start();
        FormClosed += (_, _) => _relogio.Dispose();
    }

    private void CriarInicio()
    {
        var area = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = Tema.Fundo, AutoScroll = true,
            Padding = new Padding(22, 24, 14, 0), FlowDirection = FlowDirection.LeftToRight
        };
        var titulo = new Label { Text = "Painel administrativo", Font = Tema.Semibold(20), ForeColor = Tema.Texto,
            AutoSize = true, Margin = new Padding(0, 0, 0, 28) };
        area.Controls.Add(titulo);
        area.SetFlowBreak(titulo, true);
        void Acao(string texto, string icone, Action abrir)
        {
            var b = new BotaoTema { Text = texto, Icone = icone, Estilo = EstiloBotao.Secundario,
                Size = new Size(210, 72), Font = Tema.Semibold(11), Margin = new Padding(0, 0, 12, 12) };
            b.Click += (_, _) => abrir();
            area.Controls.Add(b);
        }
        Acao("Gerenciar clientes", Icones.Loja, () => MostrarAba(true));
        Acao("Pedidos de cadastro", Icones.Documento, () => MostrarAba(false));
        Acao("Configurações", Icones.Configuracoes, () => PreferenciasInterfaceForm.Abrir(this, "master"));
        _paginaInicio.Controls.Add(area);
    }

    private Control CriarNavegacao()
    {
        var acoes = new List<NavegacaoERP.Acao>
        {
            new("inicio", "Início", Icones.Inicio, MostrarInicio),
            new("clientes", "Clientes", Icones.Loja, () => MostrarAba(true)),
            new("pedidos", "Pedidos", Icones.Documento, () => MostrarAba(false)),
            new("config", "Configurações", Icones.Configuracoes, () => PreferenciasInterfaceForm.Abrir(this, "master"))
        };
        return _prefs.Menu switch
        {
            NavegacaoInterface.Superior => NavegacaoERP.Superior(acoes),
            NavegacaoInterface.Compacta => NavegacaoERP.Lateral("Master", Ui.IconeApp, true, acoes, "clientes"),
            _ => NavegacaoERP.Lateral("Master", Ui.IconeApp, false, acoes, "clientes")
        };
    }

    private void MostrarInicio()
    {
        _paginaClientes.Visible = false;
        _paginaPedidos.Visible = false;
        _paginaInicio.Visible = true;
    }

    private void AplicarModoEGrade()
    {
        var simples = _prefs.Modo == ModoInterface.Simples;
        // As colunas secundarias continuam disponiveis no modo Completo.
        int[] secundarias = [2, 3, 4, 6, 8, 10];
        foreach (var indice in secundarias)
            if (indice < _gradeClientes.Columns.Count) _gradeClientes.Columns[indice].Visible = !simples;
        PreferenciasInterface.AplicarDensidade(this, _prefs.Tamanho);
    }

    // ---------- Montagem ----------

    private static DataGridView NovaGrade()
    {
        var g = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        Tema.EstilizarGrade(g, 10);
        return g;
    }

    private static void Colunas(DataGridView g, params (string titulo, int peso, bool direita)[] colunas)
    {
        foreach (var (titulo, peso, direita) in colunas)
        {
            var c = new DataGridViewTextBoxColumn { HeaderText = titulo, FillWeight = peso, SortMode = DataGridViewColumnSortMode.Automatic };
            if (direita) c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            g.Columns.Add(c);
        }
    }

    private static (CartaoPainel cartao, FlowLayoutPanel barra) Cartao(DataGridView grade)
    {
        var cartao = new CartaoPainel { Dock = DockStyle.Fill, Padding = new Padding(1, 1, 1, 6) };
        // Barra de botões: se a janela ficar estreita, os botões descem para uma segunda linha em vez de sumir.
        var barra = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true,
            BackColor = Tema.Superficie, Padding = new Padding(11, 11, 11, 6)
        };
        cartao.Controls.Add(grade);  // Fill primeiro; a barra (Top) fica por cima
        cartao.Controls.Add(barra);
        return (cartao, barra);
    }

    private static BotaoTema Botao(string texto, string icone, EstiloBotao estilo = EstiloBotao.Secundario, int largura = 0)
    {
        var b = new BotaoTema { Text = texto, Icone = icone, Estilo = estilo, Height = 36, Margin = new Padding(0, 0, 8, 6) };
        b.Width = largura > 0 ? largura : TextRenderer.MeasureText(texto, b.Font).Width + 58;
        return b;
    }

    private void MontarClientes()
    {
        Colunas(_gradeClientes,
            ("Nome do servidor", 12, false), ("Empresa", 20, false), ("Responsável", 13, false), ("Telefone", 11, false),
            ("Cidade", 11, false), ("Situação", 8, false), ("Usuários", 6, true), ("Caixas", 7, true),
            ("Última venda", 11, false), ("Banco", 7, true), ("Cliente desde", 9, false));
        var (cartao, barra) = Cartao(_gradeClientes);

        var novo = Botao("Novo cliente", Icones.Novo, EstiloBotao.Primario);
        novo.Click += async (_, _) => await NovoCliente();
        var editar = Botao("Editar", Icones.Editar);
        editar.Click += async (_, _) => await EditarCliente();
        var senha = Botao("Senha do admin", Icones.Cadeado);
        senha.Click += async (_, _) => await SenhaAdmin();
        var suporte = Botao("Acesso técnico", Icones.Cadeado);
        suporte.Click += async (_, _) => await GerenciarSuporteTecnico();
        var seguranca = Botao("Segurança da loja", Icones.Cadeado);
        seguranca.Click += (_, _) => AbrirSeguranca();
        var suspender = Botao("Suspender / liberar", Icones.Alternar);
        suspender.Click += async (_, _) => await AlternarAcesso();
        var copiar = Botao("Copiar nome do servidor", Icones.Colar);
        copiar.Click += (_, _) => CopiarNome();
        var excluir = Botao("Excluir", Icones.Excluir, EstiloBotao.Perigo);
        excluir.Click += async (_, _) => await ExcluirCliente();
        barra.Controls.AddRange([novo, editar, senha, suporte, seguranca, suspender, copiar, excluir]);

        _gradeClientes.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0) await EditarCliente(); };
        _paginaClientes.Controls.Add(cartao);
        _paginaClientes.Controls.Add(CriarResumo());
    }

    private void MontarPedidos()
    {
        Colunas(_gradePedidos,
            ("Data", 11, false), ("Empresa", 20, false), ("Responsável", 14, false), ("Telefone", 11, false),
            ("E-mail", 15, false), ("Cidade", 11, false), ("CPF/CNPJ", 12, false), ("Nome pedido", 11, false),
            ("Login", 8, false), ("Situação", 9, false));
        var (cartao, barra) = Cartao(_gradePedidos);

        var aprovar = Botao("Aprovar", Icones.Ok, EstiloBotao.Sucesso);
        aprovar.Click += async (_, _) => await Aprovar();
        var recusar = Botao("Recusar", Icones.Cancelar, EstiloBotao.Perigo);
        recusar.Click += async (_, _) => await Recusar();
        var excluirPedido = Botao("Excluir pedido", Icones.Excluir);
        excluirPedido.Click += async (_, _) => await ExcluirPedido();
        var lMostrar = new Label { Text = "Mostrar:", AutoSize = true, ForeColor = Tema.TextoSuave, Margin = new Padding(16, 10, 6, 0) };
        _filtroPedidos.Items.AddRange(new object[] { "Pendentes", "Aprovados", "Recusados", "Todos" });
        _filtroPedidos.SelectedIndex = 0;
        _filtroPedidos.Margin = new Padding(0, 5, 0, 0);
        _filtroPedidos.SelectedIndexChanged += async (_, _) => await CarregarPedidos();
        barra.Controls.AddRange([aprovar, recusar, excluirPedido, lMostrar, _filtroPedidos]);

        _gradePedidos.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0) await Aprovar(); };
        _paginaPedidos.Controls.Add(cartao);
    }

    private void MostrarAba(bool clientes)
    {
        _paginaInicio.Visible = false;
        _paginaClientes.Visible = clientes;
        _paginaPedidos.Visible = !clientes;
        _abaClientes.Estilo = clientes ? EstiloBotao.Primario : EstiloBotao.Secundario;
        _abaPedidos.Estilo = clientes ? EstiloBotao.Secundario : EstiloBotao.Primario;
    }

    private void AtualizarServidor() => _servidor.Text = "Servidor: " + _api.Endereco;

    // ---------- Dados ----------

    private async Task CarregarTudo()
    {
        await CarregarClientes();
        await CarregarPedidos();
    }

    private async Task CarregarClientes()
    {
        try
        {
            _clientes = await _api.ClientesAsync();
            Online(true);
        }
        catch (MasterException ex)
        {
            Online(false, ex.Message);
            return;
        }
        var selecionado = Selecionado(_gradeClientes) is ClienteMasterDto atual ? atual.Id : (Guid?)null;
        _gradeClientes.Rows.Clear();
        foreach (var c in _clientes)
        {
            var i = _gradeClientes.Rows.Add(
                CodigoCliente.ParaTela(c.Codigo), c.Nome, c.Responsavel ?? "", c.Telefone is null ? "" : Formatos.FormatarTelefone(c.Telefone),
                CidadeUf(c.Cidade, c.Uf), c.Ativo ? "Liberado" : "Suspenso", c.Usuarios,
                c.LimiteCaixas is { } lim ? $"{c.Terminais} de {lim}" : c.Terminais.ToString(),
                c.UltimaVenda?.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Br) ?? "—",
                Tamanho(c.TamanhoBanco), c.CriadoEm.ToLocalTime().ToString("dd/MM/yyyy", Br));
            var linha = _gradeClientes.Rows[i];
            linha.Tag = c;
            if (!c.Ativo) linha.DefaultCellStyle.ForeColor = Tema.Perigo;
            if (c.Id == selecionado) linha.Selected = true;
        }
        AtualizarResumo();
        AtualizarRodape();
    }

    private async Task CarregarPedidos(bool silencioso = false)
    {
        int? situacao = _filtroPedidos.SelectedIndex is >= 0 and <= 2 ? _filtroPedidos.SelectedIndex : null;
        try
        {
            _pedidos = await _api.PedidosAsync(situacao);
            _pendentes = situacao == 0 ? _pedidos.Count : (await _api.PedidosAsync(0)).Count;
            Online(true);
        }
        catch (MasterException ex)
        {
            if (!silencioso) Online(false, ex.Message);
            return;
        }
        if (IsDisposed) return;

        _gradePedidos.Rows.Clear();
        foreach (var p in _pedidos)
        {
            var i = _gradePedidos.Rows.Add(
                p.CriadoEm.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Br), p.Empresa, p.Responsavel,
                Formatos.FormatarTelefone(p.Telefone), p.Email ?? "", CidadeUf(p.Cidade, p.Uf),
                p.Documento is null ? "" : Formatos.FormatarCpfCnpj(p.Documento), CodigoCliente.ParaTela(p.CodigoAprovado ?? p.CodigoDesejado),
                p.AdminLogin, p.Situacao);
            var linha = _gradePedidos.Rows[i];
            linha.Tag = p;
            if (p.Situacao == "Recusado") linha.DefaultCellStyle.ForeColor = Tema.TextoSuave;
        }
        _abaPedidos.Text = _pendentes > 0 ? $"Pedidos de cadastro ({_pendentes})" : "Pedidos de cadastro";
        _abaPedidos.Width = TextRenderer.MeasureText(_abaPedidos.Text, _abaPedidos.Font).Width + 60;
        AtualizarResumo();
        AtualizarRodape();
    }

    private void Online(bool ok, string? erro = null)
    {
        _pilula.Text = ok ? "Conectado" : "Sem conexão";
        _pilula.Cor = ok ? Tema.Sucesso : Tema.Alerta;
        if (!ok) _rodape.Text = erro ?? "";
    }

    private void AtualizarRodape() =>
        _rodape.Text = $"{_clientes.Count} cliente(s), {_clientes.Count(c => c.Ativo)} liberado(s)   •   " +
                       $"{_pendentes} pedido(s) de cadastro aguardando   •   atualizado às {DateTime.Now:HH:mm}";

    private static object? Selecionado(DataGridView g) => g.CurrentRow?.Tag;

    private static string CidadeUf(string? cidade, string? uf) =>
        string.Join("/", new[] { cidade, uf }.Where(x => !string.IsNullOrWhiteSpace(x)));

    private static string Tamanho(long bytes) => bytes switch
    {
        <= 0 => "—",
        >= 1L << 30 => $"{bytes / (double)(1L << 30):N1} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):N1} MB",
        _ => $"{Math.Max(1, bytes / 1024)} KB"
    };

    // ---------- Ações: clientes ----------

    private async Task NovoCliente()
    {
        var empresa = new TextBox { MaxLength = 120 };
        var dados = new CamposEmpresa();
        var codigo = new CampoNomeServidor();
        var adminNome = new TextBox { MaxLength = 120, Text = "Administrador" };
        var adminLogin = new CampoLogin { Text = "admin" };
        var senha = new TextBox { MaxLength = 64, Text = Ui.GerarSenha() };
        var obs = new TextBox { MaxLength = 500, Multiline = true, ScrollBars = ScrollBars.Vertical };
        var limite = CampoLimite(1);
        var editouCodigo = false;
        empresa.TextChanged += (_, _) => { if (!editouCodigo) codigo.Codigo = CodigoCliente.Sugerir(empresa.Text); };
        codigo.KeyPress += (_, _) => editouCodigo = true;

        ClienteMasterDto? criado = null;
        using var f = new Formulario(Icones.Novo, "Novo cliente", "Cria o banco da empresa e o primeiro administrador.", "Criar cliente")
        {
            AoConfirmar = async () =>
            {
                if (empresa.Text.Trim().Length == 0) { Ui.Aviso(null, "Informe o nome da empresa."); empresa.Focus(); return false; }
                if (!dados.Conferir()) return false;
                if (!CodigoCliente.Valido(codigo.Codigo)) { Ui.Aviso(null, "Nome do servidor inválido: de 2 a 30 letras ou números (pode ter espaço), começando por letra."); codigo.Focus(); return false; }
                if (!Formatos.LoginValido(adminLogin.Text.Trim())) { Ui.Aviso(null, "Login do administrador inválido: de 3 a 60 caracteres, letras minúsculas, números, ponto ou hífen."); adminLogin.Focus(); return false; }
                if (senha.Text.Length < 6) { Ui.Aviso(null, "A senha precisa ter pelo menos 6 caracteres."); senha.Focus(); return false; }
                try
                {
                    var req = new NovoClienteRequest
                    {
                        Nome = empresa.Text.Trim(), Codigo = codigo.Codigo, Observacao = obs.Text.Trim(),
                        AdminNome = adminNome.Text.Trim(), AdminLogin = adminLogin.Text.Trim(), AdminSenha = senha.Text,
                        LimiteCaixas = (int)limite.Valor
                    };
                    dados.Copiar(req);
                    criado = await _api.CriarClienteAsync(req);
                    return true;
                }
                catch (MasterException ex) { Ui.Aviso(null, ex.Message); return false; }
            }
        };
        f.Secao("Empresa");
        f.Campo("Nome da empresa *", empresa);
        dados.Adicionar(f);
        f.Secao("Acesso");
        f.Campo("Nome do servidor *", codigo, "(o que a loja digita nos caixas)");
        f.Lado("Nome do administrador", adminNome, 220, "Login do administrador *", adminLogin);
        f.CampoComBotao("Senha do administrador *", senha, "Gerar", t => t.Text = Ui.GerarSenha(), "(mínimo 6)");
        f.Secao("Plano");
        f.Campo("Limite de caixas", limite, "(quantos caixas ativos a loja pode ter — 0 = sem limite)", largura: 120);
        f.Campo("Observação", obs, "(plano, combinados, o que quiser)", altura: 60);
        if (f.ShowDialog(this) != DialogResult.OK || criado is null) return;

        await CarregarClientes();
        Entregar(criado.Nome, criado.Codigo, adminLogin.Text.Trim(), senha.Text);
    }

    /// <summary>Mostra (e copia) o que mandar para a loja: nome do servidor, login e senha.</summary>
    private void Entregar(string empresa, string codigo, string login, string? senha)
    {
        var texto = $"Acesso ao sistema — {empresa}\r\n\r\n" +
                    $"Nome do servidor: {CodigoCliente.ParaTela(codigo)}\r\n" +
                    $"Login: {login}\r\n" +
                    (senha is null ? "" : $"Senha: {senha}\r\n") +
                    "\r\nNa Gestão e nos caixas, em Servidor, digite o nome do servidor acima.";
        try { Clipboard.SetText(texto); } catch (Exception) { /* área de transferência ocupada */ }
        Ui.Info(this, texto + "\n\n(Copiado para a área de transferência — é só colar no WhatsApp ou e-mail.)");
    }

    private async Task EditarCliente()
    {
        if (Selecionado(_gradeClientes) is not ClienteMasterDto c) { Ui.Aviso(this, "Escolha um cliente na lista."); return; }
        var nome = new TextBox { MaxLength = 120, Text = c.Nome };
        var dados = new CamposEmpresa();
        var obs = new TextBox { MaxLength = 500, Multiline = true, ScrollBars = ScrollBars.Vertical, Text = c.Observacao ?? "" };
        var limite = CampoLimite(c.LimiteCaixas ?? 0);
        CheckBox? ativo = null;
        using var f = new Formulario(Icones.Editar, "Editar cliente", $"Nome do servidor: {CodigoCliente.ParaTela(c.Codigo)}")
        {
            AoConfirmar = async () =>
            {
                if (nome.Text.Trim().Length == 0) { Ui.Aviso(null, "Informe o nome da empresa."); nome.Focus(); return false; }
                if (!dados.Conferir()) return false;
                try
                {
                    var req = new AtualizarClienteRequest
                    {
                        Nome = nome.Text.Trim(), Ativo = ativo!.Checked, Observacao = obs.Text, LimiteCaixas = (int)limite.Valor
                    };
                    dados.Copiar(req);
                    await _api.AtualizarClienteAsync(c.Id, req);
                    if (req.LimiteCaixas is int novo && novo > 0 && c.Terminais > novo)
                    {
                        Ui.Info(null,
                            $"Essa loja já tem {c.Terminais} caixas ativos, acima do novo limite ({novo}).\n\n" +
                            "Os caixas que já existem continuam funcionando; ela só não consegue criar nem reativar outro " +
                            "até ficar abaixo do limite (desativando caixas na Gestão).");
                    }
                    return true;
                }
                catch (MasterException ex) { Ui.Aviso(null, ex.Message); return false; }
            }
        };
        f.Secao("Empresa");
        f.Campo("Nome da empresa *", nome);
        dados.Adicionar(f);
        dados.Preencher(c.Documento, c.Responsavel, c.Telefone, c.Email, c.Cidade, c.Uf);
        f.Secao("Situação");
        ativo = f.Marcar("Acesso liberado (desmarcado = suspenso: a loja não consegue entrar nem sincronizar)", c.Ativo);
        f.Campo("Limite de caixas", limite, $"(hoje tem {c.Terminais} ativo(s) — 0 = sem limite)", largura: 120);
        f.Campo("Observação", obs, altura: 70);
        if (f.ShowDialog(this) == DialogResult.OK) await CarregarClientes();
    }

    private async Task SenhaAdmin()
    {
        if (Selecionado(_gradeClientes) is not ClienteMasterDto c) { Ui.Aviso(this, "Escolha um cliente na lista."); return; }
        var login = new CampoLogin { Text = "admin" };
        var senha = new TextBox { MaxLength = 64, Text = Ui.GerarSenha() };
        using var f = new Formulario(Icones.Cadeado, "Senha do administrador", $"{c.Nome} ({CodigoCliente.ParaTela(c.Codigo)})", "Trocar senha")
        {
            AoConfirmar = async () =>
            {
                if (!Formatos.LoginValido(login.Text.Trim())) { Ui.Aviso(null, "Login inválido."); return false; }
                if (senha.Text.Length < 6) { Ui.Aviso(null, "A senha precisa ter pelo menos 6 caracteres."); return false; }
                try { await _api.SenhaAdminAsync(c.Id, new SenhaAdminRequest { Login = login.Text.Trim(), Senha = senha.Text }); return true; }
                catch (MasterException ex) { Ui.Aviso(null, ex.Message); return false; }
            }
        };
        f.Texto("Troca a senha desse login no banco da empresa (se o login não existir, cria um administrador com ele). " +
                "Use quando a loja esquecer a senha.");
        f.Campo("Login", login);
        f.CampoComBotao("Nova senha", senha, "Gerar", t => t.Text = Ui.GerarSenha(), "(mínimo 6)");
        if (f.ShowDialog(this) == DialogResult.OK) Entregar(c.Nome, c.Codigo, login.Text.Trim(), senha.Text);
    }

    /// <summary>Sessões abertas, computadores lembrados e histórico de alterações da loja escolhida.</summary>
    private void AbrirSeguranca()
    {
        if (Selecionado(_gradeClientes) is not ClienteMasterDto c)
        {
            Ui.Aviso(this, "Selecione uma empresa.");
            return;
        }
        using var f = new SegurancaLojaForm(_api, c);
        f.ShowDialog(this);
    }

    private async Task GerenciarSuporteTecnico()
    {
        if (Selecionado(_gradeClientes) is not ClienteMasterDto c)
        {
            Ui.Aviso(this, "Selecione uma empresa.");
            return;
        }
        SuporteTecnicoStatusDto status;
        try { status = await _api.SuporteTecnicoAsync(c.Id); }
        catch (MasterException ex) { Ui.Aviso(this, ex.Message); return; }

        var login = new CampoLogin { Text = status.Login, Width = 280 };
        var senha = new TextBox { MaxLength = 128, Width = 280, UseSystemPasswordChar = false };
        var ativo = new CheckBox { Text = "Acesso técnico liberado", Checked = status.Ativo, AutoSize = true };
        var atual = !status.Configurado ? "Ainda não configurado" :
            status.Ativo ? "Ativo" : "Bloqueado";
        var trocouSenha = false;
        using var f = new Formulario(Icones.Cadeado, "Acesso técnico", c.Nome + " (" + CodigoCliente.ParaTela(c.Codigo) + ")", "Salvar acesso")
        {
            AoConfirmar = async () =>
            {
                try
                {
                    if (senha.Text.Length > 0)
                    {
                        if (senha.Text.Length < 12)
                        {
                            Ui.Aviso(null, "A senha técnica precisa ter pelo menos 12 caracteres.");
                            return false;
                        }
                        await _api.DefinirSenhaSuporteAsync(c.Id,
                            new SuporteTecnicoSenhaRequest { Login = login.Text.Trim(), NovaSenha = senha.Text });
                        trocouSenha = true;
                    }
                    else if (!status.Configurado)
                    {
                        Ui.Aviso(null, "Defina uma senha técnica para esta empresa.");
                        return false;
                    }
                    else if (!string.Equals(login.Text.Trim(), status.Login, StringComparison.OrdinalIgnoreCase))
                    {
                        Ui.Aviso(null, "Para alterar o login técnico, gere uma nova senha.");
                        return false;
                    }
                    // Definir a senha libera o acesso; o bloqueio é aplicado depois, se necessário.
                    if (trocouSenha ? !ativo.Checked : status.Ativo != ativo.Checked)
                        await _api.HabilitarSuporteAsync(c.Id, ativo.Checked);
                    return true;
                }
                catch (MasterException ex) { Ui.Aviso(null, ex.Message); return false; }
            }
        };
        f.Texto("Acesso exclusivo às configurações técnicas do PDV desta empresa. " +
                "Não dá permissão de gerente nem autoriza vendas e cancelamentos.");
        f.Texto("Situação atual: " + atual +
                (status.AtualizadoEm is { } d ? " · atualizado em " + d.ToLocalTime().ToString("dd/MM/yyyy HH:mm") : ""));
        f.Campo("Login técnico", login);
        f.CampoComBotao("Nova senha (vazio = manter)", senha, "Gerar", t =>
        {
            t.Text = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        }, "(mínimo 12 caracteres)");
        f.Campo("Situação", ativo);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        if (trocouSenha)
        {
            var mensagem = "Senha técnica atualizada para " + c.Nome + ".\r\n\r\n" +
                           "Login: " + login.Text.Trim() + "\r\nNova senha: " + senha.Text +
                           "\r\n\r\nGuarde a senha em local seguro; ela não poderá ser consultada depois.";
            var copiou = false;
            try { Clipboard.SetText($"Acesso técnico — {c.Nome} ({CodigoCliente.ParaTela(c.Codigo)})\r\nLogin: {login.Text.Trim()}\r\nSenha: {senha.Text}"); copiou = true; }
            catch (Exception) { /* área de transferência ocupada */ }
            Ui.Info(this, mensagem + (copiou ? "\r\n\r\n(Copiado para a área de transferência.)" : ""));
        }
        else Ui.Info(this, "Configuração de acesso técnico atualizada.");
    }

    private async Task AlternarAcesso()
    {
        if (Selecionado(_gradeClientes) is not ClienteMasterDto c) { Ui.Aviso(this, "Escolha um cliente na lista."); return; }
        var msg = c.Ativo
            ? $"Suspender o acesso de \"{c.Nome}\"?\n\nA Gestão e os caixas dessa empresa deixam de entrar e sincronizar " +
              "(os caixas continuam vendendo offline). Os dados ficam guardados."
            : $"Liberar de novo o acesso de \"{c.Nome}\"?";
        if (!Ui.Confirmar(this, msg)) return;
        try
        {
            // Manda tudo de volta como está; só a situação muda.
            await _api.AtualizarClienteAsync(c.Id, new AtualizarClienteRequest
            {
                Nome = c.Nome, Ativo = !c.Ativo, Observacao = c.Observacao, LimiteCaixas = c.LimiteCaixas,
                Documento = c.Documento, Responsavel = c.Responsavel, Telefone = c.Telefone,
                Email = c.Email, Cidade = c.Cidade, Uf = c.Uf
            });
            await CarregarClientes();
        }
        catch (MasterException ex) { Ui.Aviso(this, ex.Message); }
    }

    private async Task ExcluirCliente()
    {
        if (Selecionado(_gradeClientes) is not ClienteMasterDto c) { Ui.Aviso(this, "Escolha um cliente na lista."); return; }
        var confirmar = new CampoNomeServidor();
        string? copia = null;
        using var f = new Formulario(Icones.Excluir, "Excluir cliente", $"{c.Nome} ({CodigoCliente.ParaTela(c.Codigo)})", "Excluir para sempre")
        {
            AoConfirmar = async () =>
            {
                if (!string.Equals(confirmar.Codigo, c.Codigo, StringComparison.OrdinalIgnoreCase))
                {
                    Ui.Aviso(null, $"Digite exatamente o nome do servidor: {CodigoCliente.ParaTela(c.Codigo)}");
                    confirmar.Focus();
                    return false;
                }
                try { copia = await _api.ExcluirClienteAsync(c.Id, confirmar.Codigo); return true; }
                catch (MasterException ex) { Ui.Aviso(null, ex.Message); return false; }
            }
        };
        f.Texto("Isto APAGA o banco dessa empresa — produtos, clientes, estoque, vendas, caixas e usuários — e o cadastro dela aqui. " +
                "A Gestão e os caixas dela param de funcionar.", destaque: true);
        f.Texto("Antes de apagar, o servidor guarda uma cópia do banco em /opt/erp-nf/excluidos (se a cópia falhar, nada é apagado). " +
                "Para só bloquear o acesso sem apagar nada, use \"Suspender / liberar\".");
        f.Campo("Para confirmar, digite o nome do servidor", confirmar, $"({CodigoCliente.ParaTela(c.Codigo)})");
        if (f.ShowDialog(this) != DialogResult.OK) return;

        await CarregarTudo();
        Ui.Info(this, $"\"{c.Nome}\" foi excluído." + (string.IsNullOrEmpty(copia) ? "" : $"\n\nCópia de segurança na VPS:\n{copia}"));
    }

    private async Task ExcluirPedido()
    {
        if (Selecionado(_gradePedidos) is not PedidoCadastroDto p) { Ui.Aviso(this, "Escolha um pedido na lista."); return; }
        if (!Ui.Confirmar(this, $"Excluir o pedido de \"{p.Empresa}\"?\n\nApaga os dados do pedido (responsável, telefone, e-mail, documento). " +
                                (p.Situacao == "Aprovado" ? "A empresa já criada não é afetada." : "Se a loja ainda não recebeu resposta, ela não vai receber.")))
            return;
        try
        {
            await _api.ExcluirPedidoAsync(p.Id);
            await CarregarPedidos();
        }
        catch (MasterException ex) { Ui.Aviso(this, ex.Message); }
    }

    private void CopiarNome()
    {
        if (Selecionado(_gradeClientes) is not ClienteMasterDto c) { Ui.Aviso(this, "Escolha um cliente na lista."); return; }
        try { Clipboard.SetText(CodigoCliente.ParaTela(c.Codigo)); } catch (Exception) { }
        Ui.Info(this, $"Nome do servidor de {c.Nome}: {CodigoCliente.ParaTela(c.Codigo)}\n\n(Copiado.) Nos caixas e na Gestão, em Servidor, é só digitar esse nome.");
    }

    /// <summary>Campo do limite de caixas: número inteiro de 0 (sem limite) a 999.</summary>
    private static CampoNumero CampoLimite(int valor) =>
        new() { CasasDecimais = 0, Maximo = 999, Valor = valor };

    // ---------- Ações: pedidos ----------

    private async Task Aprovar()
    {
        if (Selecionado(_gradePedidos) is not PedidoCadastroDto p) { Ui.Aviso(this, "Escolha um pedido na lista."); return; }
        if (p.Situacao != "Pendente") { Ui.Aviso(this, "Esse pedido já foi " + p.Situacao.ToLowerInvariant() + "."); return; }

        var nome = new TextBox { MaxLength = 120, Text = p.Empresa };
        var codigo = new CampoNomeServidor { Codigo = p.CodigoDesejado };
        var limite = CampoLimite(1);
        ClienteMasterDto? criado = null;
        using var f = new Formulario(Icones.Ok, "Aprovar cadastro", "Cria o banco da empresa com o administrador que ela escolheu.", "Aprovar")
        {
            AoConfirmar = async () =>
            {
                if (!CodigoCliente.Valido(codigo.Codigo)) { Ui.Aviso(null, "Nome do servidor inválido."); return false; }
                try
                {
                    criado = await _api.AprovarAsync(p.Id, new AprovarPedidoRequest
                    {
                        Codigo = codigo.Codigo, Nome = nome.Text.Trim(), LimiteCaixas = (int)limite.Valor
                    });
                    return true;
                }
                catch (MasterException ex) { Ui.Aviso(null, ex.Message); return false; }
            }
        };
        f.Texto($"{p.Empresa}", destaque: true);
        f.Texto($"Responsável: {p.Responsavel}   •   Telefone: {Formatos.FormatarTelefone(p.Telefone)}" +
                (p.Email is null ? "" : $"\nE-mail: {p.Email}") +
                (p.Documento is null ? "" : $"\nCPF/CNPJ: {Formatos.FormatarCpfCnpj(p.Documento)}") +
                (p.Cidade is null && p.Uf is null ? "" : $"\nCidade: {CidadeUf(p.Cidade, p.Uf)}") +
                $"\nPedido em {p.CriadoEm.ToLocalTime():dd/MM/yyyy HH:mm}   •   login do admin: {p.AdminLogin}");
        f.Campo("Nome da empresa", nome);
        f.Campo("Nome do servidor", codigo, "(pode trocar, se quiser)");
        f.Campo("Limite de caixas", limite, "(0 = sem limite)", largura: 120);
        if (f.ShowDialog(this) != DialogResult.OK || criado is null) return;

        await CarregarTudo();
        Ui.Info(this, $"Cadastro de \"{criado.Nome}\" aprovado.\n\n" +
                      $"A Gestão da loja se configura sozinha na próxima vez que abrir; ela entra com o login \"{p.AdminLogin}\" " +
                      $"e a senha que escolheu.\n\nNome do servidor para os caixas: {CodigoCliente.ParaTela(criado.Codigo)}");
    }

    private async Task Recusar()
    {
        if (Selecionado(_gradePedidos) is not PedidoCadastroDto p) { Ui.Aviso(this, "Escolha um pedido na lista."); return; }
        if (p.Situacao != "Pendente") { Ui.Aviso(this, "Esse pedido já foi " + p.Situacao.ToLowerInvariant() + "."); return; }
        var motivo = new TextBox { MaxLength = 300, Multiline = true, ScrollBars = ScrollBars.Vertical };
        using var f = new Formulario(Icones.Cancelar, "Recusar cadastro", p.Empresa, "Recusar")
        {
            AoConfirmar = async () =>
            {
                try { await _api.RecusarAsync(p.Id, motivo.Text.Trim()); return true; }
                catch (MasterException ex) { Ui.Aviso(null, ex.Message); return false; }
            }
        };
        f.Texto("A loja vê esse motivo na próxima vez que abrir a Gestão.");
        f.Campo("Motivo (opcional)", motivo, altura: 80);
        if (f.ShowDialog(this) == DialogResult.OK) await CarregarPedidos();
    }

    // ---------- Conexão ----------

    private async Task TrocarConexao()
    {
        if (!ConexaoForm.Abrir(this, _cfg)) return;
        _cfg = MasterConfig.Carregar();
        _api = new MasterApi(_cfg);
        AtualizarServidor();
        await CarregarTudo();
    }
}

/// <summary>
/// Campos da empresa com o formato de cada um: CPF/CNPJ e telefone com máscara (pontos, traço, DDD),
/// e-mail em minúsculas, UF escolhida na lista. Usado no Novo cliente e no Editar cliente.
/// </summary>
internal sealed class CamposEmpresa
{
    private readonly Control _documento = Campos.Criar(TipoCampo.CpfCnpj);
    private readonly TextBox _responsavel = new() { MaxLength = 120 };
    private readonly Control _telefone = Campos.Criar(TipoCampo.Telefone);
    private readonly Control _email = Campos.Criar(TipoCampo.Email, 120);
    private readonly TextBox _cidade = new() { MaxLength = 80 };
    private readonly ComboBox _uf = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    public CamposEmpresa()
    {
        _uf.Items.Add("");
        foreach (var u in Formatos.Ufs) _uf.Items.Add(u);
        _uf.SelectedIndex = 0;
    }

    public void Adicionar(Formulario f)
    {
        f.Lado("CPF ou CNPJ", _documento, 200, "Responsável", _responsavel);
        f.Lado("Telefone / WhatsApp", _telefone, 200, "E-mail", _email);
        f.Lado("Cidade", _cidade, 360, "UF", _uf);
    }

    public void Preencher(string? documento, string? responsavel, string? telefone, string? email, string? cidade, string? uf)
    {
        _documento.Text = documento is null ? "" : Formatos.FormatarCpfCnpj(documento);
        _responsavel.Text = responsavel ?? "";
        _telefone.Text = telefone is null ? "" : Formatos.FormatarTelefone(telefone);
        _email.Text = email ?? "";
        _cidade.Text = cidade ?? "";
        _uf.SelectedItem = uf is not null && Formatos.UfValida(uf) ? uf.ToUpperInvariant() : "";
    }

    /// <summary>Confere o formato antes de mandar (o servidor confere de novo).</summary>
    public bool Conferir()
    {
        var doc = Formatos.LimparDocumento(_documento.Text);
        if (doc.Length > 0 && (doc.Length is not (11 or 14) || !Formatos.DocumentoValido(doc)))
            return Erro(_documento, doc.Length == 11 ? "CPF inválido." : "CNPJ inválido.");
        if (_telefone.Text.Trim().Length > 0 && !Formatos.TelefoneValido(_telefone.Text))
            return Erro(_telefone, "Telefone inválido: informe o DDD e o número (10 ou 11 dígitos).");
        if (_email.Text.Trim().Length > 0 && !Formatos.EmailValido(_email.Text.Trim()))
            return Erro(_email, "E-mail inválido.");
        return true;
    }

    private static bool Erro(Control campo, string msg)
    {
        Ui.Aviso(null, msg);
        campo.Focus();
        return false;
    }

    private string? Valor(Control c) => string.IsNullOrWhiteSpace(c.Text) ? null : c.Text.Trim();

    public void Copiar(NovoClienteRequest r) =>
        (r.Documento, r.Responsavel, r.Telefone, r.Email, r.Cidade, r.Uf) = Valores();

    public void Copiar(AtualizarClienteRequest r) =>
        (r.Documento, r.Responsavel, r.Telefone, r.Email, r.Cidade, r.Uf) = Valores();

    private (string?, string?, string?, string?, string?, string?) Valores() => (
        Valor(_documento) is { } d ? Formatos.LimparDocumento(d) : null,
        Valor(_responsavel),
        Valor(_telefone) is { } t ? Formatos.SoDigitos(t) : null,
        Valor(_email)?.ToLowerInvariant(),
        Valor(_cidade),
        _uf.SelectedItem as string is { Length: 2 } uf ? uf : null);
}

/// <summary>Endereço do servidor e chave master (a do /etc/erp-nf/api.env, linha Master__Chave).</summary>
public static class ConexaoForm
{
    public static bool Abrir(IWin32Window? dono, MasterConfig atual)
    {
        var servidor = new TextBox { Text = atual.Servidor };
        var chave = new TextBox { UseSystemPasswordChar = true, Text = atual.Chave };
        using var f = new Formulario(Icones.Servidor, "Conexão com o servidor", "Endereço da VPS e a chave master.", "Testar e salvar")
        {
            AoConfirmar = async () =>
            {
                var cfg = new MasterConfig { Servidor = EnderecoServidor.ParaUrlDeEndereco(servidor.Text), Chave = chave.Text.Trim() };
                if (!Uri.TryCreate(cfg.Servidor, UriKind.Absolute, out _)) { Ui.Aviso(null, "Endereço inválido."); return false; }
                if (cfg.Chave.Length < 32) { Ui.Aviso(null, "A chave master tem pelo menos 32 caracteres (copie do api.env)."); return false; }
                try
                {
                    await new MasterApi(cfg).TestarAsync();
                    cfg.Salvar();
                    return true;
                }
                catch (MasterException ex) { Ui.Aviso(null, ex.Message); return false; }
            }
        };
        f.Campo("Endereço do servidor", servidor, "(ex.: erp.minhaempresa.com.br)");
        f.Campo("Chave master", chave, "(linha Master__Chave do /etc/erp-nf/api.env)");
        f.Texto("Guardada criptografada só para o seu usuário do Windows. Quem tem a chave cria e suspende clientes: não passe para ninguém.");
        return f.ShowDialog(dono) == DialogResult.OK;
    }
}
