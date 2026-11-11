using System.Drawing.Drawing2D;
using ProjetoJP.Controles;
using ProjetoJP.Pdv.Dados;
using ProjetoJP.Pdv.Servicos;
using ProjetoJP.Shared;

namespace ProjetoJP.Pdv.Telas;

/// <summary>Como a tela de venda foi fechada. Reconfigurado = opções salvas: o caixa recarrega e volta à venda.</summary>
public enum SaidaVenda { Sair, TrocarOperador, CaixaFechado, Reconfigurado }

/// <summary>
/// Tela principal do caixa. Em cima: logo grande e nome da loja. No meio: busca, último item e a lista (sem venda,
/// as promoções da loja). À direita, a coluna escura do total, que vira o pagamento ao apertar Enter (formas F7 a F12,
/// valor recebido, troco, pagamento dividido, fiado) e mostra o troco quando a venda acaba. Embaixo, as teclas.
/// F1 abre "Mais opções" (sangria, orçamento, fechar caixa...).
/// </summary>
public class VendaForm : Form
{
    private sealed class ItemCarrinho
    {
        public string ProdutoId { get; init; } = "";
        public string Codigo { get; init; } = "";
        public string Descricao { get; init; } = "";
        public string Unidade { get; init; } = "";
        public decimal Quantidade { get; set; }
        public decimal PrecoUnitario { get; set; }
        /// <summary>Preço normal, quando o item entrou em promoção (0 = sem promoção).</summary>
        public decimal PrecoNormal { get; set; }
        /// <summary>Preço alterado no caixa (com autorização).</summary>
        public bool PrecoAlterado { get; set; }
        public decimal Total => Math.Round(Quantidade * PrecoUnitario, 2);

        // Atacado: o preço de varejo do item (já com promoção de varejo) e as faixas por quantidade.
        // Somando as linhas do mesmo produto, se chegar no mínimo de uma faixa, todas saem no atacado.
        public decimal PrecoVarejo { get; init; }
        public decimal PrecoNormalVarejo { get; init; }
        public IReadOnlyList<Atacado.Faixa> Faixas { get; init; } = [];
        /// <summary>A partir de quanto valeu o atacado (null = saiu no varejo).</summary>
        public decimal? AtacadoMinimo { get; set; }
    }

    /// <summary>O que a coluna escura da direita mostra.</summary>
    private enum EstadoColuna { Resumo, Pagamento, Concluida, Orcamento }

    private readonly PdvConfig _cfg;
    /// <summary>Tecla → ação da venda (Opções → Teclado; padrão F1 a F12 e Del).</summary>
    private readonly Dictionary<Keys, AcaoCaixa> _teclas;
    private readonly PreferenciasInterface _prefs = PreferenciasInterface.Carregar("pdv");
    private readonly Sincronizador _sync;
    private readonly UsuarioLocal _operador;
    private readonly CaixaLocal _caixa;
    private readonly string _terminal;

    private readonly List<ItemCarrinho> _itens = [];
    private decimal _desconto;
    private decimal? _descontoPercentual;
    private ClienteLocal? _cliente;
    private bool _modoOrcamento;
    /// <summary>Orçamento aberto na tela (editar no modo orçamento, ou virando venda no modo venda).</summary>
    private OrcamentoLocal? _orcamentoAtual;
    private FormaLocal? _formaEscolhida;
    private List<FormaLocal> _formas = [];
    private EstadoColuna _estado = EstadoColuna.Resumo;

    // Pagamento na coluna
    private readonly List<(FormaLocal forma, decimal valor)> _parciais = [];
    private bool _dividindo;
    private bool _imprimirCupom = true;
    /// <summary>A última venda feita neste caixa (reimprimir com Ctrl+P).</summary>
    private VendaCompleta? _ultimaVenda;
    private string? _ultimoCliente;
    /// <summary>"Venda concluída" no meio da tela, até passar o próximo produto (ou alguns segundos).</summary>
    private bool _mostrandoConcluido;
    private readonly System.Windows.Forms.Timer _timerConcluido = new() { Interval = 12_000 };

    // Busca e sugestões
    private readonly CampoBusca _busca = new() { Dica = "Passe o código de barras ou digite o nome", Height = 54 };
    private readonly Panel _sugestoes = new() { Visible = false, BackColor = Tema.Superficie, Padding = new Padding(1) };
    private readonly ListBox _listaSugestoes = new()
    {
        Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 50,
        IntegralHeight = false, BackColor = Tema.Superficie, TabStop = false
    };
    private readonly System.Windows.Forms.Timer _atrasoBusca = new() { Interval = 160 };
    private List<ProdutoLocal>? _catalogo;
    private DateTime _catalogoEm;

    // Quantidade (ao lado da busca) e produto escolhido pelo nome (confere quantidade e preço antes de entrar)
    private readonly CampoNumero _quantidade = new() { CasasDecimais = 3, Maximo = 9999, Font = Tema.Semibold(15) };
    private ProdutoLocal? _pendente;
    private readonly Panel _faixaPendente = new() { Dock = DockStyle.Top, Height = 92, Visible = false, BackColor = Tema.Fundo, Padding = new Padding(0, 0, 0, 10) };
    private readonly Label _nomePendente = new() { AutoSize = false, AutoEllipsis = true, Font = Tema.Semibold(13), ForeColor = Tema.Texto, BackColor = Color.Transparent };
    private readonly Label _infoPendente = new() { AutoSize = false, AutoEllipsis = true, Font = Tema.Fonte(9.5f), ForeColor = Tema.TextoSuave, BackColor = Color.Transparent };
    private readonly Label _totalPendente = new() { AutoSize = false, Font = Tema.Semibold(14), ForeColor = Tema.Primaria, TextAlign = ContentAlignment.MiddleRight, BackColor = Color.Transparent };
    private readonly CampoNumero _precoPendente = new() { CasasDecimais = 2, Maximo = 9_999_999, Font = Tema.Semibold(14) };

    // Meio: último item, lista, promoções (caixa livre) e venda concluída
    private readonly CartaoUltimoItem _ultimo = new() { Visible = false };
    private readonly CartaoPainel _cartaoItens = new() { Dock = DockStyle.Fill, Padding = new Padding(1, 44, 1, 6) };
    private readonly DataGridView _grade = Ui.Grade();
    private readonly Label _tituloItens = new() { AutoSize = true, Font = Tema.Semibold(11), ForeColor = Tema.Texto };
    private readonly PainelPromocoes _livre = new() { Dock = DockStyle.Fill };
    private readonly PainelConcluido _concluido = new() { Dock = DockStyle.Fill, Visible = false };

    // Coluna: resumo
    private readonly PainelColuna _coluna = new() { Dock = DockStyle.Right, Width = 380, Padding = new Padding(26, 22, 26, 20) };
    private readonly Panel _pResumo = PainelDaColuna();
    private readonly Label _rotuloTotal = RotuloColuna("TOTAL A PAGAR");
    private readonly Label _total = ValorColuna(84);
    private readonly LinhaValor _lSubtotal = new("Subtotal");
    private readonly LinhaValor _lDesconto = new("Desconto");
    private readonly LinhaValor _lItens = new("Itens");
    private readonly LinhaValor _lCliente = new("Cliente");
    private readonly FaixaValor _economia = new() { Height = 64, ComIcone = true, RotuloEmCima = true, CorFundo = CoresCaixa.EconomiaFundo, CorTexto = Color.White, Rotulo = "VOCÊ ECONOMIZOU", Visible = false };
    private readonly BotaoTema _pagar = new() { Estilo = EstiloBotao.Sucesso, Font = Tema.Semibold(16), Dock = DockStyle.Bottom, Height = 64, TabStop = false };

    // Coluna: pagamento
    private readonly Panel _pPagamento = PainelDaColuna();
    private readonly Label _rotuloPag = RotuloColuna("TOTAL A PAGAR");
    private readonly Label _totalPag = ValorColuna(56);
    private readonly Panel _parciaisBox = new() { Dock = DockStyle.Top, Height = 0, BackColor = Tema.Barra };
    private readonly FaixaValor _falta = new() { Height = 48, Rotulo = "Falta", CorFundo = Color.FromArgb(36, 248, 194, 92), CorTexto = CoresCaixa.Destaque, Visible = false };
    private readonly Label _rotuloForma = RotuloPequeno("Forma de pagamento");
    private readonly TableLayoutPanel _gradeFormas = new() { Dock = DockStyle.Top, ColumnCount = 2, BackColor = Tema.Barra, Margin = new Padding(0) };
    private readonly List<BotaoColuna> _botoesForma = [];
    private readonly Label _rotuloValor = RotuloPequeno("Valor recebido");
    private readonly Panel _boxRecebido = new() { Dock = DockStyle.Top, Height = 46, BackColor = Tema.Barra };
    private readonly CampoMoeda _recebido = new() { Font = Tema.Semibold(18), Dock = DockStyle.Top };
    private readonly TableLayoutPanel _notas = new() { Dock = DockStyle.Top, Height = 40, ColumnCount = 4, RowCount = 1, BackColor = Tema.Barra, Margin = new Padding(0) };
    private readonly List<BotaoColuna> _botoesNota = [];
    private readonly FaixaValor _troco = new() { Height = 56, Rotulo = "Troco" };
    private readonly Label _avisoPag = new() { Dock = DockStyle.Top, Height = 46, Font = Tema.Fonte(10), ForeColor = CoresCaixa.Destaque, BackColor = Tema.Barra, TextAlign = ContentAlignment.MiddleLeft };
    private readonly LinkLabel _dividir = new() { Dock = DockStyle.Bottom, Height = 30, Font = Tema.Fonte(10), TextAlign = ContentAlignment.MiddleCenter, LinkBehavior = LinkBehavior.HoverUnderline, BackColor = Tema.Barra, TabStop = false };
    private readonly BotaoTema _voltarPag = new() { Text = "Voltar (Esc)", Estilo = EstiloBotao.NaBarra, Font = Tema.Semibold(11), Dock = DockStyle.Left, Width = 124, TabStop = false };
    private readonly BotaoTema _concluir = new() { Estilo = EstiloBotao.Sucesso, Font = Tema.Semibold(15), Dock = DockStyle.Fill };

    // Coluna: venda concluída (troco)
    private readonly Panel _pConcluida = PainelDaColuna();
    private readonly Label _rotuloConc = RotuloColuna("TROCO");
    private readonly Label _valorConc = ValorColuna(84);
    private readonly LinhaValor _cTotal = new("Total");
    private readonly LinhaValor _cPagamento = new("Pagamento");
    private readonly LinhaValor _cVenda = new("Venda");
    private readonly BotaoColuna _reimprimir = new() { Dock = DockStyle.Bottom, Height = 60, Font = Tema.Semibold(13) };

    // Coluna: orçamento
    private readonly Panel _pOrcamento = PainelDaColuna();
    private readonly Label _totalOrc = ValorColuna(64);
    private readonly CampoNumero _validadeDias = new() { CasasDecimais = 0, Maximo = 365, Font = Tema.Fonte(12), Valor = 7, Width = 90 };
    private readonly TextBox _observacaoOrcamento = new() { Font = Tema.Fonte(11), MaxLength = 300, Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Top, Height = 84 };
    // O que sai ao salvar o orçamento: nada, cupom na térmica ou folha A4. Fica lembrado neste caixa.
    private readonly ComboBox _imprimirOrcamento = new() { DropDownStyle = ComboBoxStyle.DropDownList, Font = Tema.Fonte(10.5f), TabStop = false, Dock = DockStyle.Top };
    private readonly Label _orcamentoInfo = new() { Dock = DockStyle.Top, Height = 46, Font = Tema.Fonte(9.5f), ForeColor = Tema.BarraTextoSuave, BackColor = Tema.Barra };

    // Barra de cima e rodapé
    private readonly PilulaTexto _pTerminal = new();
    private readonly PilulaTexto _pOperador = new();
    private readonly PilulaTexto _pHora = new();
    private readonly PilulaTexto _pModo = new() { Visible = false };
    private readonly Pilula _pilula = new() { Font = Tema.Semibold(10.5f), Height = 32, Margin = new Padding(8, 0, 0, 0), Text = "Online" };
    private readonly FaixaTeclas _dicas = new() { Dock = DockStyle.Left, Width = 700 };
    private readonly RotuloUmaLinha _status = new() { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 0, 0), Font = Tema.Fonte(9.5f), ForeColor = Tema.TextoSuave, BackColor = Tema.Superficie, TextAlign = ContentAlignment.MiddleRight };
    // Situação da impressora do cupom: conferida a cada 30 s e antes de cada venda.
    private readonly EtiquetasSituacao _etiquetas = new() { Dock = DockStyle.Right, Width = 300 };
    private readonly System.Windows.Forms.Timer _timerImpressora = new() { Interval = 30_000 };
    private bool _conferindoImpressora;
    private DateTime _manterStatusAte;
    private readonly ToolTip _dica = new();
    private readonly System.Windows.Forms.Timer _relogio = new() { Interval = 1000 };

    private static Panel PainelDaColuna() => new() { Dock = DockStyle.Fill, BackColor = Tema.Barra, Visible = false };

    private static Label RotuloColuna(string texto) => new()
    {
        Text = texto, Dock = DockStyle.Top, Height = 28, Font = Tema.Semibold(11), ForeColor = CoresCaixa.Destaque,
        BackColor = Tema.Barra, TextAlign = ContentAlignment.MiddleLeft, AutoSize = false
    };

    private static Label RotuloPequeno(string texto) => new()
    {
        Text = texto, Dock = DockStyle.Top, Height = 28, Font = Tema.Semibold(9.5f), ForeColor = Tema.BarraTextoSuave,
        BackColor = Tema.Barra, TextAlign = ContentAlignment.BottomLeft, AutoSize = false, Padding = new Padding(0, 0, 0, 4)
    };

    private static Label ValorColuna(int altura) => new()
    {
        Dock = DockStyle.Top, Height = altura, Font = Tema.Semibold(40), ForeColor = Color.White, BackColor = Tema.Barra,
        TextAlign = ContentAlignment.MiddleLeft, AutoSize = false
    };

    private static Panel Espaco(int altura) => new() { Dock = DockStyle.Top, Height = altura, BackColor = Tema.Barra };

    /// <summary>Põe controles com Dock = Top na ordem de cima para baixo (no WinForms, o último adicionado fica em cima).</summary>
    private static void Empilhar(Control pai, params Control[] deCimaParaBaixo)
    {
        for (var i = deCimaParaBaixo.Length - 1; i >= 0; i--) pai.Controls.Add(deCimaParaBaixo[i]);
    }

    public SaidaVenda Saida { get; private set; } = SaidaVenda.Sair;

    public VendaForm(PdvConfig cfg, Sincronizador sync, UsuarioLocal operador, CaixaLocal caixa)
    {
        _cfg = cfg;
        _teclas = TeclasCaixa.Mapa(cfg);
        _sync = sync;
        _operador = operador;
        _caixa = caixa;
        _terminal = Db.LerConfig("nome_terminal") ?? "CAIXA";

        Text = $"{cfg.NomeLoja} — {_terminal}";
        Icon = Ui.IconeApp ?? Icon;
        FormBorderStyle = FormBorderStyle.None;
        WindowState = FormWindowState.Normal;
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(800, 560);
        BackColor = Tema.Fundo;
        Font = Tema.Fonte(10);
        KeyPreview = true;

        _busca.Dica = T("Passe o código de barras ou digite o nome", AcaoCaixa.Buscar);
        NomesParaAutomacao();
        MontarTela();
        CarregarFormas();
        Limpar();
        MudarModo(orcamento: false, perguntar: false);

        _sync.StatusMudou += AtualizarStatus;
        AtualizarStatus(_sync.Online, _sync.UltimaMensagem);
        CopiaSeguranca.SituacaoMudou += AvisoCopia;
        FormClosing += AoFechar;
        FormClosed += (_, _) =>
        {
            _sync.StatusMudou -= AtualizarStatus;
            CopiaSeguranca.SituacaoMudou -= AvisoCopia;
            _relogio.Dispose();
            _atrasoBusca.Dispose();
            _timerImpressora.Dispose();
            _timerConcluido.Dispose();
            _dica.Dispose();
        };
        _relogio.Tick += (_, _) => AtualizarHora();
        _relogio.Start();
        _timerImpressora.Tick += async (_, _) => await ConferirImpressoraAsync();
        _timerImpressora.Start();
        _timerConcluido.Tick += (_, _) => { _timerConcluido.Stop(); EsconderConcluido(); };
        Shown += async (_, _) => await ConferirImpressoraAsync();
        AtualizarHora();
        Shown += (_, _) => Focar();
    }

    /// <summary>
    /// Nomes fixos nos campos principais: o Windows mostra como "AutomationId" e o robô de testes (ErpRoboTela)
    /// acha cada campo por eles, sem depender do texto na tela. Não muda nada para quem usa o caixa.
    /// </summary>
    private void NomesParaAutomacao()
    {
        _busca.Caixa.Name = "buscaProduto";
        _quantidade.Name = "quantidade";
        _precoPendente.Name = "precoProduto";
        _tituloItens.Name = "tituloItens";
        _total.Name = "totalVenda";
        _pagar.Name = "pagar";
        _totalPag.Name = "totalPagamento";
        _recebido.Name = "valorRecebido";
        _concluir.Name = "concluirPagamento";
        _dividir.Name = "dividirPagamento";
        _rotuloConc.Name = "rotuloConcluido";
        _valorConc.Name = "valorConcluido";
        _cVenda.Name = "numeroVenda";
    }

    // ---------- Montagem da tela ----------

    private DataGridViewColumn? _colCodigo;
    private DataGridViewColumn? _colUnitario;
    private Label? _dicaItens;

    private void MontarTela()
    {
        // ----- Barra de cima: logo, loja e etiquetas (terminal, operador, conexão, hora) -----
        var topo = TopoCaixa.Montar(_cfg, out var direita);
        _pTerminal.Text = _terminal;
        _pOperador.Text = _operador.Nome;
        _pModo.CorFundo = CoresCaixa.Destaque;
        _pModo.CorTexto = Tema.Barra;
        _pModo.Font = Tema.Semibold(10.5f);
        _pModo.Text = "ORÇAMENTO";
        var menu = new BotaoTema { Text = T("Mais opções", AcaoCaixa.Ajuda), Estilo = EstiloBotao.NaBarra, Icone = Icones.Grade, Font = Tema.Fonte(10.5f), Height = 32, TabStop = false, Margin = new Padding(8, 0, 0, 0) };
        menu.Width = TextRenderer.MeasureText(menu.Text, menu.Font).Width + 52;
        menu.Click += (_, _) => MaisOpcoes();
        // RightToLeft: o primeiro fica mais à direita.
        direita.Controls.AddRange([_pHora, _pilula, _pOperador, _pTerminal, _pModo, menu]);

        // ----- Rodapé: teclas (clicáveis), impressora e mensagens -----
        var rodape = new Panel { Dock = DockStyle.Bottom, Height = 42, BackColor = Tema.Superficie, Padding = new Padding(0, 0, 16, 0) };
        rodape.Paint += (_, e) =>
        {
            using var linha = new Pen(Tema.Borda);
            e.Graphics.DrawLine(linha, 0, 0, rodape.Width, 0);
        };
        foreach (var (acao, texto) in new[]
                 {
                     (AcaoCaixa.Ajuda, "Mais opções"), (AcaoCaixa.Quantidade, "Quantidade"), (AcaoCaixa.Buscar, "Buscar"),
                     (AcaoCaixa.Rapidos, "Rápidos"),
                     (AcaoCaixa.Cliente, "Cliente"), (AcaoCaixa.AlterarItem, "Alterar item"), (AcaoCaixa.Desconto, "Desconto"),
                     (AcaoCaixa.RemoverItem, "Remover")
                 })
        {
            var tecla = TeclasCaixa.Texto(TeclasCaixa.Tecla(_cfg, acao));
            if (tecla.Length == 0) continue;
            var a = acao;
            _dicas.Adicionar(tecla, texto, () => Executar(a));
        }
        // Ordem do encaixe: o Fill (mensagens rápidas) entra primeiro para ficar com o espaço entre as teclas e as etiquetas.
        rodape.Controls.Add(_status);
        rodape.Controls.Add(_dicas);
        rodape.Controls.Add(_etiquetas);
        _etiquetas.SizeChanged += (_, _) => { if (IsHandleCreated) AjustarLayout(); }; // etiquetas mudaram: as teclas se ajustam
        _status.TextChanged += (_, _) => _dica.SetToolTip(_status, _status.Text);

        // ----- Coluna do total -----
        MontarColuna();

        // ----- Meio -----
        var centro = MontarCentro();

        Controls.Add(centro);
        Controls.Add(_coluna);
        Controls.Add(rodape);
        Controls.Add(topo);

        // Sugestões da busca: por cima de tudo, logo abaixo do campo.
        _sugestoes.Paint += (_, e) =>
        {
            using var borda = new Pen(Tema.Borda);
            e.Graphics.DrawRectangle(borda, 0, 0, _sugestoes.Width - 1, _sugestoes.Height - 1);
        };
        _sugestoes.Controls.Add(_listaSugestoes);
        Controls.Add(_sugestoes);
        _listaSugestoes.DrawItem += DesenharSugestao;
        _listaSugestoes.MouseClick += (_, e) =>
        {
            var i = _listaSugestoes.IndexFromPoint(e.Location);
            if (i >= 0 && _listaSugestoes.Items[i] is ProdutoLocal p) EscolherProduto(p);
        };

        PreferenciasInterface.AplicarDensidade(this, _prefs.Tamanho, fonteNormal: 11f); // caixa: letra maior
        Resize += (_, _) => AjustarLayout();
        Shown += (_, _) =>
        {
            CobrirTela();
            AjustarLayout();
        };
    }

    /// <summary>
    /// Ocupa o monitor inteiro, por cima do lugar da barra do Windows.
    /// A barra não é desligada: a tecla Windows ou Alt+Tab continuam trazendo ela.
    /// </summary>
    private void CobrirTela()
    {
        var tela = Screen.FromHandle(Handle).Bounds;
        WindowState = FormWindowState.Normal;
        Bounds = tela;
    }

    /// <summary>
    /// Se ajusta ao tamanho da tela: em telas menores a coluna fica mais estreita, o total diminui um pouco (continua
    /// o maior da tela) e somem as informações menos usadas (coluna Código/Unitário, hora, algumas teclas do rodapé).
    /// </summary>
    private void AjustarLayout()
    {
        var w = ClientSize.Width;
        var h = ClientSize.Height;
        if (w < 80 || h < 80) return;

        _coluna.Width = w >= 1500 ? 420 : w >= 1250 ? 380 : 320;
        _coluna.Padding = w >= 1250 ? new Padding(26, 22, 26, 20) : new Padding(18, 18, 18, 16);
        if (_colCodigo is not null) _colCodigo.Visible = w >= 1366;
        if (_colUnitario is not null) _colUnitario.Visible = w >= 1100;
        _pHora.Visible = w >= 1180;
        _pTerminal.Visible = w >= 980;
        // Rodapé: etiquetas (situação) à direita, teclas com prioridade; as mensagens rápidas ficam com o resto.
        _etiquetas.Compacto = w < 1366;
        var livreTeclas = w - 16 - _etiquetas.Width - 160;
        _dicas.Width = Math.Max(120, Math.Min(_dicas.LarguraPreferida, livreTeclas));
        if (_dicaItens is not null) _dicaItens.Visible = w >= 1250;

        // Tela baixa (768 px ou menos): um pouco mais compacto, para tudo caber sem rolar.
        var baixo = h < 740;
        _total.Height = baixo ? 70 : 84;
        _valorConc.Height = baixo ? 70 : 84;
        _ultimo.Height = baixo ? 76 : 84;
        foreach (var l in new[] { _lSubtotal, _lDesconto, _lItens, _lCliente, _cTotal, _cPagamento, _cVenda }) l.Height = baixo ? 40 : 44;
        AjustarAlturaFormas();
        AjustarTotais();
    }

    private Control MontarCentro()
    {
        var centro = new Panel { Dock = DockStyle.Fill, BackColor = Tema.Fundo, Padding = new Padding(20, 16, 18, 14) };

        // Busca + quantidade
        var linhaBusca = new Panel { Dock = DockStyle.Top, Height = 66, BackColor = Tema.Fundo };
        var lQtd = new Label { Text = T("Quantidade", AcaoCaixa.Quantidade), Font = Tema.Fonte(8.5f), ForeColor = Tema.TextoSuave, AutoSize = false, TextAlign = ContentAlignment.BottomLeft };
        _busca.Caixa.Font = Tema.Fonte(15);
        _quantidade.TextAlign = HorizontalAlignment.Center;
        linhaBusca.Controls.AddRange([_busca, lQtd, _quantidade]);
        linhaBusca.Resize += (_, _) =>
        {
            var w = linhaBusca.ClientSize.Width;
            const int larguraQtd = 132;
            _busca.SetBounds(0, 0, Math.Max(160, w - larguraQtd - 14), 54);
            lQtd.SetBounds(w - larguraQtd, 0, larguraQtd, 18);
            _quantidade.SetBounds(w - larguraQtd, 20, larguraQtd, 34);
        };

        // Produto escolhido pelo nome: nome, preço editável e total do item.
        var cartaoPendente = new PainelArredondado { Cor = Tema.PrimariaClara, Dock = DockStyle.Fill };
        var lPreco = new Label { Text = "Preço unit.", Font = Tema.Semibold(10), ForeColor = Tema.Texto, AutoSize = true, BackColor = Color.Transparent };
        var cancelarPendente = new BotaoTema { Text = "Cancelar (Esc)", Estilo = EstiloBotao.Secundario, Font = Tema.Fonte(10), Size = new Size(130, 36), TabStop = false };
        cancelarPendente.Click += (_, _) => CancelarPendente();
        _precoPendente.TextAlign = HorizontalAlignment.Right;
        cartaoPendente.Controls.AddRange([_nomePendente, _infoPendente, lPreco, _precoPendente, _totalPendente, cancelarPendente]);
        cartaoPendente.Resize += (_, _) =>
        {
            var w = cartaoPendente.ClientSize.Width;
            cancelarPendente.Location = new Point(w - cancelarPendente.Width - 12, 20);
            _totalPendente.SetBounds(cancelarPendente.Left - 150, 18, 140, 40);
            _precoPendente.SetBounds(_totalPendente.Left - 140, 20, 130, 36);
            lPreco.Location = new Point(_precoPendente.Left, 2);
            var larguraNome = Math.Max(120, _precoPendente.Left - 30);
            _nomePendente.SetBounds(14, 10, larguraNome, 28);
            _infoPendente.SetBounds(15, 42, larguraNome, 22);
        };
        _faixaPendente.Controls.Add(cartaoPendente);
        _precoPendente.TextChanged += (_, _) => AtualizarTotalPendente();
        _quantidade.TextChanged += (_, _) => AtualizarTotalPendente();
        // Enter na quantidade vai para o preço; Enter no preço coloca o item na venda. Esc desiste.
        _quantidade.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape && _pendente is not null) { e.SuppressKeyPress = true; CancelarPendente(); return; }
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            if (_pendente is not null) { _precoPendente.Focus(); _precoPendente.SelectAll(); }
            else Focar();
        };
        _precoPendente.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; CancelarPendente(); return; }
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            ConfirmarPendente();
        };

        // Itens
        _tituloItens.Location = new Point(16, 12);
        _cartaoItens.Controls.Add(_tituloItens);
        var dicaItens = new Label { Text = DicaItens(), Font = Tema.Fonte(9), ForeColor = Tema.TextoSuave, AutoSize = true };
        _dicaItens = dicaItens;
        _cartaoItens.Controls.Add(dicaItens);
        _cartaoItens.Resize += (_, _) => dicaItens.Location = new Point(Math.Max(260, _cartaoItens.Width - dicaItens.Width - 16), 15);
        _grade.Dock = DockStyle.Fill;
        _grade.Columns.Add(Ui.Coluna("#", 6));
        _colCodigo = Ui.Coluna("Código", 15);
        _grade.Columns.Add(_colCodigo);
        _grade.Columns.Add(Ui.Coluna("Produto", 45));
        _grade.Columns.Add(Ui.Coluna("Qtd", 11, true));
        _colUnitario = Ui.Coluna("Unitário", 13, true);
        _grade.Columns.Add(_colUnitario);
        _grade.Columns.Add(Ui.Coluna("Total", 14, true));
        _grade.TabStop = false;
        _grade.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) EditarItem(); };
        var menuItem = new ContextMenuStrip();
        menuItem.Items.Add(T("Alterar quantidade / preço", AcaoCaixa.AlterarItem), null, (_, _) => EditarItem());
        menuItem.Items.Add(T("Remover item", AcaoCaixa.RemoverItem), null, (_, _) => RemoverItem());
        _grade.ContextMenuStrip = menuItem;
        _grade.CellMouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0) _grade.CurrentCell = _grade.Rows[e.RowIndex].Cells[0];
        };
        _cartaoItens.Controls.Add(_grade);

        // Área do meio: lista, promoções (caixa livre) ou venda concluída — um de cada vez.
        var area = new Panel { Dock = DockStyle.Fill, BackColor = Tema.Fundo };
        area.Controls.AddRange([_cartaoItens, _livre, _concluido]);

        centro.Controls.Add(area);
        Empilhar(centro, linhaBusca, _faixaPendente, _ultimo);

        // Eventos da busca
        _busca.Caixa.TextChanged += (_, _) =>
        {
            if (_busca.Caixa.TextLength > 0) EsconderConcluido();
            _atrasoBusca.Stop();
            _atrasoBusca.Start();
        };
        _atrasoBusca.Tick += (_, _) => { _atrasoBusca.Stop(); AtualizarSugestoes(); };
        _busca.Caixa.KeyDown += BuscaKeyDown;
        _busca.Caixa.Leave += (_, _) =>
        {
            if (!IsDisposed && IsHandleCreated)
                BeginInvoke(new Action(() => { if (!_listaSugestoes.Focused) EsconderSugestoes(); }));
        };
        return centro;
    }

    private void MontarColuna()
    {
        // Resumo: total grande, subtotal, desconto, itens, cliente, economia e Pagar.
        _pagar.Text = "Pagar   (Enter)";
        _pagar.Click += (_, _) => AbrirPagamento(null);
        _pResumo.Controls.Add(_pagar);
        Empilhar(_pResumo, _rotuloTotal, _total, Espaco(8), _lSubtotal, _lDesconto, _lItens, _lCliente, Espaco(14), _economia);

        // Pagamento: formas (F7 a F12), valor recebido, notas rápidas, troco; dividir em mais de uma forma.
        _gradeFormas.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _gradeFormas.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (var i = 0; i < 4; i++) _notas.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        _notas.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        for (var i = 0; i < 4; i++)
        {
            var nota = new BotaoColuna { Dock = DockStyle.Fill, Margin = new Padding(0, 0, i < 3 ? 6 : 0, 0), Font = Tema.Semibold(10.5f), CorSelecionado = CoresCaixa.Destaque, Visible = false };
            nota.Click += (_, _) =>
            {
                if (nota.Dado is decimal v) { _recebido.Valor = v; AtualizarTroco(); _recebido.Focus(); _recebido.SelectAll(); }
            };
            _botoesNota.Add(nota);
            _notas.Controls.Add(nota, i, 0);
        }
        _boxRecebido.Controls.Add(_recebido);
        _recebido.TextChanged += (_, _) => AtualizarTroco();
        _recebido.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            ConcluirPagamento();
        };
        _dividir.LinkColor = Tema.BarraTexto;
        _dividir.ActiveLinkColor = Color.White;
        _dividir.LinkClicked += (_, _) => TrocarDividir(!_dividindo);
        _voltarPag.Click += (_, _) => VoltarDoPagamento();
        _concluir.Click += (_, _) => ConcluirPagamento();
        var botoesPag = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Tema.Barra };
        botoesPag.Controls.Add(_concluir);
        botoesPag.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 10, BackColor = Tema.Barra });
        botoesPag.Controls.Add(_voltarPag);
        _pPagamento.Controls.Add(_dividir);
        _pPagamento.Controls.Add(botoesPag);
        Empilhar(_pPagamento, _rotuloPag, _totalPag, _parciaisBox, _falta, _rotuloForma, _gradeFormas, _rotuloValor, _boxRecebido,
            Espaco(8), _notas, Espaco(10), _troco, _avisoPag);

        // Venda concluída: troco grande, total, como pagou, número e reimprimir.
        _valorConc.ForeColor = CoresCaixa.TrocoTexto;
        _reimprimir.Text = T("Reimprimir cupom", AcaoCaixa.Reimprimir);
        _reimprimir.Click += (_, _) => Reimprimir();
        var proxima = new Label
        {
            Text = "Passe o próximo produto para começar outra venda", Dock = DockStyle.Bottom, Height = 44, Font = Tema.Fonte(9.5f),
            ForeColor = Tema.BarraTextoSuave, BackColor = Tema.Barra, TextAlign = ContentAlignment.MiddleCenter
        };
        _pConcluida.Controls.Add(proxima);
        _pConcluida.Controls.Add(_reimprimir);
        Empilhar(_pConcluida, _rotuloConc, _valorConc, Espaco(8), _cTotal, _cPagamento, _cVenda);

        // Orçamento: validade, observação, impressão, salvar.
        _imprimirOrcamento.Items.AddRange(["Não imprimir", "Cupom (bobina)", "Folha A4"]);
        _imprimirOrcamento.SelectedIndex = Math.Clamp(_cfg.ImpressaoOrcamento, 0, 2);
        _imprimirOrcamento.SelectedIndexChanged += (_, _) =>
        {
            if (_imprimirOrcamento.SelectedIndex < 0 || _cfg.ImpressaoOrcamento == _imprimirOrcamento.SelectedIndex) return;
            _cfg.ImpressaoOrcamento = _imprimirOrcamento.SelectedIndex;
            try { _cfg.Salvar(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        };
        _imprimirOrcamento.DropDownClosed += (_, _) => Focar();
        var linhaValidade = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = Tema.Barra };
        linhaValidade.Controls.Add(new Label { Text = "Validade (dias)", Font = Tema.Fonte(11), ForeColor = Tema.BarraTexto, AutoSize = true, Location = new Point(0, 8), BackColor = Tema.Barra });
        _validadeDias.Location = new Point(150, 2);
        linhaValidade.Controls.Add(_validadeDias);
        var salvarOrc = new BotaoTema { Text = "Salvar orçamento   (Enter)", Estilo = EstiloBotao.Sucesso, Font = Tema.Semibold(14), Dock = DockStyle.Bottom, Height = 60, TabStop = false };
        salvarOrc.Click += (_, _) => SalvarOrcamento();
        var abrirLista = new BotaoColuna { Text = T("Abrir orçamento salvo", AcaoCaixa.Orcamentos), Dock = DockStyle.Bottom, Height = 46, Font = Tema.Semibold(10.5f) };
        abrirLista.Click += (_, _) => AbrirOrcamentos();
        var voltarVenda = new LinkLabel
        {
            Text = "Voltar para a venda", Dock = DockStyle.Bottom, Height = 34, Font = Tema.Fonte(10), TextAlign = ContentAlignment.MiddleCenter,
            LinkColor = Tema.BarraTexto, ActiveLinkColor = Color.White, LinkBehavior = LinkBehavior.HoverUnderline, BackColor = Tema.Barra, TabStop = false
        };
        voltarVenda.LinkClicked += (_, _) => MudarModo(false);
        _pOrcamento.Controls.Add(voltarVenda);
        _pOrcamento.Controls.Add(Espaco(8).ComDock(DockStyle.Bottom));
        _pOrcamento.Controls.Add(abrirLista);
        _pOrcamento.Controls.Add(Espaco(10).ComDock(DockStyle.Bottom));
        _pOrcamento.Controls.Add(salvarOrc);
        Empilhar(_pOrcamento, RotuloColuna("ORÇAMENTO"), _totalOrc, Espaco(6), linhaValidade, RotuloPequeno("Observação (sai impressa)"),
            _observacaoOrcamento, RotuloPequeno("Imprimir ao salvar"), _imprimirOrcamento, Espaco(8), _orcamentoInfo);

        _coluna.Controls.AddRange([_pResumo, _pPagamento, _pConcluida, _pOrcamento]);
        _coluna.Resize += (_, _) => AjustarTotais();
    }

    /// <summary>Mostra na coluna o resumo, o pagamento, o troco da venda que acabou ou o orçamento.</summary>
    private void MostrarColuna(EstadoColuna estado)
    {
        _estado = estado;
        _pResumo.Visible = estado == EstadoColuna.Resumo;
        _pPagamento.Visible = estado == EstadoColuna.Pagamento;
        _pConcluida.Visible = estado == EstadoColuna.Concluida;
        _pOrcamento.Visible = estado == EstadoColuna.Orcamento;
        if (estado != EstadoColuna.Pagamento)
        {
            _parciais.Clear();
            _dividindo = false;
            _formaEscolhida = null;
            foreach (var b in _botoesForma) b.Selecionado = false;
        }
        AtualizarTela();
    }

    /// <summary>Monta os botões de forma de pagamento: até 6, cada um com a tecla das Opções (padrão F7 a F12).</summary>
    private void CarregarFormas()
    {
        _formas = Repositorio.Formas();
        _gradeFormas.SuspendLayout();
        foreach (var b in _botoesForma)
        {
            _gradeFormas.Controls.Remove(b);
            b.Dispose();
        }
        _botoesForma.Clear();
        _gradeFormas.RowStyles.Clear();
        var acoesForma = new[] { AcaoCaixa.Forma1, AcaoCaixa.Forma2, AcaoCaixa.Forma3, AcaoCaixa.Forma4, AcaoCaixa.Forma5, AcaoCaixa.Forma6 };
        var mostrar = _formas.Count <= 6 ? _formas.Cast<FormaLocal?>().ToList() : [.. _formas.Take(5), null];
        var linhas = (mostrar.Count + 1) / 2;
        _gradeFormas.RowCount = Math.Max(1, linhas);
        for (var r = 0; r < linhas; r++) _gradeFormas.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / linhas));
        for (var i = 0; i < mostrar.Count; i++)
        {
            var forma = mostrar[i];
            var b = new BotaoColuna
            {
                Text = forma?.Descricao ?? "Outros", Tecla = TeclasCaixa.Texto(TeclasCaixa.Tecla(_cfg, acoesForma[i])), Dado = forma,
                Dock = DockStyle.Fill, Margin = new Padding(0, 0, i % 2 == 0 ? 8 : 0, 8)
            };
            var indice = i;
            b.Click += (_, _) => EscolherForma(indice);
            _gradeFormas.Controls.Add(b, i % 2, i / 2);
            _botoesForma.Add(b);
        }
        _gradeFormas.ResumeLayout(true);
        AjustarAlturaFormas();
    }

    private void AjustarAlturaFormas()
    {
        var linhas = Math.Max(1, (_botoesForma.Count + 1) / 2);
        var alturaLinha = ClientSize.Height < 740 || _dividindo ? 46 : 54;
        _gradeFormas.Height = linhas * alturaLinha;
    }

    /// <summary>O total sempre cabe na coluna: a letra diminui só o necessário.</summary>
    private void AjustarTotais()
    {
        static void Caber(Label l, float maximo, float minimo)
        {
            if (l.Width < 40) return;
            l.Font = ControlePintado.FonteQueCabe(null, l.Text, l.Width - 6, maximo, minimo);
        }
        Caber(_total, _total.Height >= 80 ? 46 : 38, 18);
        Caber(_totalPag, _dividindo ? 26 : 32, 16);
        Caber(_valorConc, _valorConc.Height >= 80 ? 46 : 38, 18);
        Caber(_totalOrc, 36, 18);
    }

    // ---------- Teclado ----------

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Esc no pagamento volta para a venda (os itens continuam).
        if (keyData == Keys.Escape && _estado == EstadoColuna.Pagamento && !_sugestoes.Visible)
        {
            VoltarDoPagamento();
            return true;
        }
        if (_teclas.TryGetValue(keyData, out var acao) && Executar(acao)) return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>Faz a ação da tecla. false = a tecla segue o caminho normal (ex.: Del apagando texto num campo).</summary>
    private bool Executar(AcaoCaixa acao)
    {
        switch (acao)
        {
            case AcaoCaixa.Ajuda: MaisOpcoes(); return true;
            case AcaoCaixa.Quantidade: _quantidade.Focus(); _quantidade.SelectAll(); return true;
            case AcaoCaixa.Buscar: Focar(); return true;
            case AcaoCaixa.Cliente: EscolherCliente(); return true;
            case AcaoCaixa.AlterarItem: EditarItem(); return true;
            case AcaoCaixa.Desconto: AbrirDesconto(); return true;
            case AcaoCaixa.Forma1: EscolherForma(0); return true;
            case AcaoCaixa.Forma2: EscolherForma(1); return true;
            case AcaoCaixa.Forma3: EscolherForma(2); return true;
            case AcaoCaixa.Forma4: EscolherForma(3); return true;
            case AcaoCaixa.Forma5: EscolherForma(4); return true;
            case AcaoCaixa.Forma6: EscolherForma(5); return true;
            case AcaoCaixa.ProximaForma:
                if (!_modoOrcamento && _botoesForma.Count > 0)
                    EscolherForma(_formaEscolhida is null ? 0 : (IndiceForma() + 1) % _botoesForma.Count);
                return true;
            case AcaoCaixa.Finalizar:
                EnterSemBusca();
                return true;
            case AcaoCaixa.RemoverItem:
                // Del/Backspace dentro de um campo com texto apagam o texto, não o item.
                if (_busca.Caixa.Focused && _busca.Caixa.TextLength > 0) return false;
                if (_quantidade.Focused || _precoPendente.Focused || _recebido.Focused || _observacaoOrcamento.Focused || _validadeDias.Focused) return false;
                RemoverItem();
                return true;
            case AcaoCaixa.Vendas: AbrirVendas(); return true;
            case AcaoCaixa.Orcamentos: AbrirOrcamentos(); return true;
            case AcaoCaixa.Sincronizar: _ = SincronizarAgora(); return true;
            case AcaoCaixa.Opcoes: AbrirOpcoes(); return true;
            case AcaoCaixa.PesoBalanca: LerPesoNaQuantidade(); return true;
            case AcaoCaixa.Reimprimir: Reimprimir(); return true;
            case AcaoCaixa.Rapidos: AbrirRapidos(); return true;
        }
        return false;
    }

    /// <summary>
    /// Enter com a busca vazia (ou a "outra tecla para finalizar"): confirma o produto em destaque; na venda, abre o
    /// pagamento e, no pagamento, conclui; no orçamento, salva.
    /// </summary>
    private void EnterSemBusca()
    {
        if (_pendente is not null) { ConfirmarPendente(); return; }
        if (_modoOrcamento) { if (_itens.Count > 0) SalvarOrcamento(); return; }
        if (_itens.Count == 0) { Focar(); return; }
        if (_estado == EstadoColuna.Pagamento) ConcluirPagamento();
        else AbrirPagamento(null);
    }

    /// <summary>"Cliente (F4)", com a tecla configurada neste caixa (sem tecla: só o texto).</summary>
    private string T(string texto, AcaoCaixa acao) => TeclasCaixa.ComTecla(_cfg, texto, acao);

    private Keys Tecla(AcaoCaixa acao) => TeclasCaixa.Tecla(_cfg, acao);

    private string DicaItens()
    {
        var alterar = TeclasCaixa.Texto(Tecla(AcaoCaixa.AlterarItem));
        var remover = TeclasCaixa.Texto(Tecla(AcaoCaixa.RemoverItem));
        return $"Duplo clique{(alterar.Length > 0 ? " ou " + alterar : "")}: alterar quantidade/preço" +
               (remover.Length > 0 ? $"   ·   {remover}: remover item" : "");
    }

    private void BuscaKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Down when _sugestoes.Visible && _listaSugestoes.Items.Count > 0:
                _listaSugestoes.SelectedIndex = Math.Min(_listaSugestoes.Items.Count - 1, _listaSugestoes.SelectedIndex + 1);
                e.Handled = e.SuppressKeyPress = true;
                break;
            case Keys.Up when _sugestoes.Visible && _listaSugestoes.Items.Count > 0:
                _listaSugestoes.SelectedIndex = Math.Max(0, _listaSugestoes.SelectedIndex - 1);
                e.Handled = e.SuppressKeyPress = true;
                break;
            case Keys.Enter:
                e.Handled = e.SuppressKeyPress = true;
                if (_busca.Caixa.TextLength == 0)
                {
                    // Produto escolhido esperando: Enter coloca na venda. Senão, campo vazio + Enter paga.
                    EnterSemBusca();
                    return;
                }
                AdicionarDaBusca();
                break;
            case Keys.Escape:
                e.Handled = e.SuppressKeyPress = true;
                if (_sugestoes.Visible) EsconderSugestoes();
                else if (_busca.Caixa.TextLength == 0 && _pendente is not null) CancelarPendente();
                else _busca.Caixa.Clear();
                break;
        }
    }

    /// <summary>F1: todas as ações do caixa num lugar só (com a tecla de cada uma).</summary>
    private void MaisOpcoes()
    {
        EsconderSugestoes();
        var itens = new List<MaisOpcoesForm.Item>
        {
            new(Tecla(AcaoCaixa.Quantidade), "Quantidade", "Do próximo produto", () => Executar(AcaoCaixa.Quantidade)),
            new(Tecla(AcaoCaixa.Buscar), "Buscar produto", "Pelo nome ou código", () => Executar(AcaoCaixa.Buscar)),
            new(Tecla(AcaoCaixa.Rapidos), "Botões rápidos", "Pão, salgados, itens sem código", () => Executar(AcaoCaixa.Rapidos)),
            new(Tecla(AcaoCaixa.Cliente), "Cliente", "Escolher ou tirar o cliente", () => Executar(AcaoCaixa.Cliente)),
            new(Tecla(AcaoCaixa.AlterarItem), "Alterar item", "Quantidade ou preço", () => Executar(AcaoCaixa.AlterarItem)),
            new(Tecla(AcaoCaixa.Desconto), "Desconto", "Na venda, em R$ ou %", () => Executar(AcaoCaixa.Desconto)),
            new(Tecla(AcaoCaixa.RemoverItem), "Remover item", "O item marcado na lista", () => Executar(AcaoCaixa.RemoverItem)),
            new(Keys.None, "Pagar", "Formas, troco, dividir", () => EnterSemBusca(), "Enter"),
            new(Tecla(AcaoCaixa.Vendas), "Vendas do caixa", "Reimprimir ou cancelar", () => Executar(AcaoCaixa.Vendas)),
            new(Tecla(AcaoCaixa.Reimprimir), "Reimprimir cupom", "Da última venda", () => Executar(AcaoCaixa.Reimprimir)),
            new(Tecla(AcaoCaixa.Orcamentos), "Orçamentos salvos", "Abrir, vender ou imprimir", () => Executar(AcaoCaixa.Orcamentos)),
            new(Keys.None, _modoOrcamento ? "Voltar para a venda" : "Fazer orçamento", _modoOrcamento ? "Sai do modo orçamento" : "Monta, salva e imprime", () => MudarModo(!_modoOrcamento)),
            new(Tecla(AcaoCaixa.PesoBalanca), "Ler o peso", "Balança ligada no caixa", () => Executar(AcaoCaixa.PesoBalanca)),
            new(Tecla(AcaoCaixa.Sincronizar), "Sincronizar agora", "Enviar e receber dados", () => Executar(AcaoCaixa.Sincronizar)),
            new(Keys.None, "Sangria", "Retirar dinheiro da gaveta", () => SangriaSuprimento(suprimento: false)),
            new(Keys.None, "Suprimento", "Colocar troco na gaveta", () => SangriaSuprimento(suprimento: true)),
            new(Keys.None, _modoOrcamento ? "Limpar orçamento" : "Cancelar venda", "A que está na tela", CancelarVendaAtual, Perigo: true),
            new(Tecla(AcaoCaixa.Opcoes), "Opções do caixa", "Impressora, teclado, aparência", () => Executar(AcaoCaixa.Opcoes)),
            new(Keys.None, "Como usar o caixa", "Busca, balança, teclas", MostrarAjuda),
            new(Keys.None, "Sair da conta", "Trocar de operador", TrocarOperador),
            new(Keys.None, "Fechar caixa", "Conferir e fechar o dia", FecharCaixa, Perigo: true)
        };
        Action? escolhida;
        using (var f = new MaisOpcoesForm(itens, TeclasCaixa.Texto(Tecla(AcaoCaixa.Ajuda))))
        {
            escolhida = f.ShowDialog(this) == DialogResult.OK ? f.Escolhido?.Acao : null;
        }
        if (escolhida is not null) escolhida();
        else Focar(limpar: false);
    }

    private void MostrarAjuda()
    {
        var teclas = new System.Text.StringBuilder();
        foreach (var d in TeclasCaixa.Todas)
        {
            var t = TeclasCaixa.Texto(Tecla(d.Acao));
            if (t.Length > 0) teclas.Append(t.PadRight(10)).Append("  ").Append(d.Nome).Append('\n');
        }
        MessageBox.Show(this,
            "Busca: digite o código, o código de barras ou parte do nome. Aparecem sugestões: use ↑ ↓ e Enter, ou clique.\n" +
            "Escolhendo pelo nome, o produto aparece em destaque: confira a quantidade (Enter), o preço (Enter) e ele entra na venda. Esc cancela.\n" +
            "Código de barras (leitor) ou código exato entram direto. Quantidade antes do código também funciona: 3*7891234567890\n" +
            "Etiqueta de balança (13 dígitos começando com 2) entra com o peso ou o preço da etiqueta.\n\n" +
            "Enter     PAGAR (com o campo de busca vazio); no pagamento, Enter conclui a venda\n" +
            "Esc       No pagamento, volta para a venda; na busca, fecha as sugestões / limpa / cancela o produto em destaque\n" +
            teclas + "\n" +
            "As teclas podem ser trocadas em Opções do caixa → Teclado (útil para teclado de automação).",
            "Como usar o caixa", MessageBoxButtons.OK, MessageBoxIcon.Information);
        Focar(limpar: false);
    }

    // ---------- Busca com sugestões ----------

    private List<ProdutoLocal> Catalogo()
    {
        if (_catalogo is null || DateTime.Now - _catalogoEm > TimeSpan.FromSeconds(60))
        {
            try
            {
                _catalogo = Repositorio.ProdutosAtivos();
                _catalogoEm = DateTime.Now;
            }
            catch (Exception)
            {
                _catalogo ??= [];
            }
        }
        return _catalogo;
    }

    private static string TextoSemQuantidade(string texto)
    {
        var asterisco = texto.IndexOf('*');
        return asterisco > 0 ? texto[(asterisco + 1)..].Trim() : texto.Trim();
    }

    private List<ProdutoLocal> Sugestoes(string texto, int maximo)
    {
        var termo = TextoSemQuantidade(texto);
        if (termo.Length == 0) return [];
        return Catalogo()
            .Select(p => (p, pontos: PesquisaProdutos.Pontuar(termo, p.Descricao, p.Codigo, p.CodigoBarras)))
            .Where(x => x.pontos >= 0)
            .OrderByDescending(x => x.pontos).ThenBy(x => x.p.Descricao, StringComparer.CurrentCultureIgnoreCase)
            .Take(maximo).Select(x => x.p).ToList();
    }

    private void AtualizarSugestoes()
    {
        var texto = _busca.Caixa.Text.Trim();
        var termo = TextoSemQuantidade(texto);
        // Só números (leitor de código de barras): não abre lista enquanto o código chega.
        if (termo.Length < 2 || (termo.All(char.IsAsciiDigit) && termo.Length >= 8))
        {
            EsconderSugestoes();
            return;
        }
        var lista = Sugestoes(texto, 8);
        if (lista.Count == 0)
        {
            EsconderSugestoes();
            return;
        }
        _listaSugestoes.BeginUpdate();
        _listaSugestoes.Items.Clear();
        foreach (var p in lista) _listaSugestoes.Items.Add(p);
        _listaSugestoes.SelectedIndex = 0;
        _listaSugestoes.EndUpdate();

        var inicio = PointToClient(_busca.PointToScreen(new Point(0, _busca.Height + 2)));
        _sugestoes.SetBounds(inicio.X, inicio.Y, Math.Max(_busca.Width, 520), lista.Count * _listaSugestoes.ItemHeight + 2);
        _sugestoes.Visible = true;
        _sugestoes.BringToFront();
    }

    private void EsconderSugestoes()
    {
        if (!_sugestoes.IsDisposed) _sugestoes.Visible = false;
    }

    private void DesenharSugestao(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || _listaSugestoes.Items[e.Index] is not ProdutoLocal p) return;
        var selecionado = (e.State & DrawItemState.Selected) != 0;
        var b = e.Bounds;
        using (var fundo = new SolidBrush(selecionado ? Tema.PrimariaClara : Tema.Superficie)) e.Graphics.FillRectangle(fundo, b);
        const TextFormatFlags linha = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
        var preco = Ui.Dinheiro(p.PrecoVenda) + (p.EmPromocao ? "  ★" : "");
        var larguraPreco = TextRenderer.MeasureText(preco, Tema.Semibold(12)).Width + 16;
        TextRenderer.DrawText(e.Graphics, p.Descricao, Tema.Semibold(11.5f), new Rectangle(b.X + 14, b.Y + 6, b.Width - larguraPreco - 24, 22), Tema.Texto, linha);
        TextRenderer.DrawText(e.Graphics, $"Código {p.Codigo}" + (string.IsNullOrEmpty(p.CodigoBarras) ? "" : $"  ·  {p.CodigoBarras}") +
                              $"  ·  estoque {Cupom.Qtd(p.Estoque)} {p.Unidade}", Tema.Fonte(9), new Rectangle(b.X + 14, b.Y + 28, b.Width - larguraPreco - 24, 18), Tema.TextoSuave, linha);
        TextRenderer.DrawText(e.Graphics, preco, Tema.Semibold(12), new Rectangle(b.Right - larguraPreco - 8, b.Y, larguraPreco, b.Height),
            p.EmPromocao ? Tema.Sucesso : Tema.Texto, TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPrefix);
        using var separador = new Pen(Tema.Borda);
        e.Graphics.DrawLine(separador, b.X, b.Bottom - 1, b.Right, b.Bottom - 1);
    }

    // ---------- Carrinho ----------

    private decimal Subtotal => _itens.Sum(i => i.Total);
    private decimal Total => Math.Max(0, Subtotal - _desconto);

    /// <summary>Quanto o cliente deixou de pagar: promoção, atacado (contra o preço normal) e desconto.</summary>
    private decimal Economia => _itens.Sum(i => i.PrecoNormal > i.PrecoUnitario ? Math.Round((i.PrecoNormal - i.PrecoUnitario) * i.Quantidade, 2) : 0) + _desconto;

    private string DetalheEconomia()
    {
        var partes = new List<string>();
        if (_itens.Any(i => i.AtacadoMinimo is not null)) partes.Add("atacado");
        if (_itens.Any(i => i.AtacadoMinimo is null && i.PrecoNormal > i.PrecoUnitario)) partes.Add("promoção");
        if (_desconto > 0) partes.Add("desconto");
        return string.Join(" + ", partes);
    }

    /// <summary>Enter na busca: código exato, sugestão escolhida ou pesquisa completa.</summary>
    private void AdicionarDaBusca()
    {
        var texto = _busca.Caixa.Text.Trim();
        if (texto.Length == 0) { Focar(); return; }
        FecharPendente(); // buscou outro produto: o escolhido antes (sem confirmar) sai da tela

        decimal? quantidade = null;
        var codigo = texto;
        var asterisco = texto.IndexOf('*');
        if (asterisco > 0)
        {
            if (!Ui.LerDecimal(texto[..asterisco], out var q) || q <= 0 || q > 9999)
            {
                Ui.Aviso(this, "Quantidade inválida. Use, por exemplo, 3*código ou 1,250*código.");
                return;
            }
            quantidade = q;
            codigo = texto[(asterisco + 1)..].Trim();
        }

        // 1) código interno ou de barras exato (leitor de código de barras): entra direto, como em todo caixa.
        var exato = codigo.Length > 0 ? Repositorio.BuscarProduto(codigo) : null;
        if (exato is not null)
        {
            AdicionarProduto(exato, quantidade);
            return;
        }
        if (quantidade is null && _cfg.BalancaEtiqueta &&
            EtiquetaBalanca.TentarLer(codigo, _cfg.BalancaDigitosCodigo, _cfg.BalancaEtiquetaPreco, out var etiqueta))
        {
            var daBalanca = Repositorio.BuscarProdutoBalanca(etiqueta.CodigoProduto);
            if (daBalanca is null)
            {
                Ui.Aviso(this, $"Etiqueta de balança: o produto {etiqueta.CodigoProduto.TrimStart('0')} não está no caixa.");
                Focar();
                return;
            }
            if (etiqueta.PesoKg is { } peso)
            {
                // A etiqueta traz kg; produto cadastrado em G vende em gramas.
                if (!Formatos.UnidadePesavel(daBalanca.Unidade))
                {
                    Ui.Aviso(this, $"Etiqueta de peso, mas {daBalanca.Descricao} é vendido em {daBalanca.Unidade}.\n\nConfira a unidade do produto na Gestão (KG ou G).");
                    Focar();
                    return;
                }
                AdicionarProduto(daBalanca, QuantidadeNaUnidade(daBalanca.Unidade, peso));
                return;
            }
            if (etiqueta.PrecoTotal is { } precoTotal && daBalanca.PrecoVenda > 0)
            {
                var qtd = precoTotal / daBalanca.PrecoVenda;
                if (!Formatos.UnidadeFracionada(daBalanca.Unidade))
                {
                    // UN, PC...: o preço da etiqueta tem de dar uma quantidade inteira.
                    if (qtd != decimal.Truncate(qtd) || qtd <= 0)
                    {
                        Ui.Aviso(this, $"A etiqueta ({Formatos.Moeda(precoTotal)}) não bate com o preço de {daBalanca.Descricao} " +
                                       $"({Formatos.Moeda(daBalanca.PrecoVenda)} por {daBalanca.Unidade}).\n\nConfira o preço enviado aos caixas.");
                        Focar();
                        return;
                    }
                    AdicionarProduto(daBalanca, qtd);
                    return;
                }
                AdicionarProduto(daBalanca, Math.Round(qtd, 3));
                return;
            }
            Ui.Aviso(this, "Etiqueta de balança sem peso nem preço utilizável.");
            Focar();
            return;
        }
        // 2) sugestão marcada na lista
        ProdutoLocal? produto = _sugestoes.Visible && _listaSugestoes.SelectedItem is ProdutoLocal marcado ? marcado : null;
        // 3) um único resultado
        if (produto is null)
        {
            var achados = Sugestoes(codigo, 2);
            if (achados.Count == 1) produto = achados[0];
        }
        // 4) vários ou nenhum: pesquisa completa
        produto ??= PesquisarProduto(codigo);
        if (produto is null) { Focar(); return; }
        // Escolhido pelo nome: com "3*nome" já tem a quantidade e entra direto; senão confere quantidade e preço antes.
        if (quantidade is not null) AdicionarProduto(produto, quantidade);
        else EscolherProduto(produto);
    }

    /// <summary>Produto escolhido na lista: mostra nome e preço, vai para a quantidade (Enter → preço → Enter adiciona).</summary>
    private void EscolherProduto(ProdutoLocal produto)
    {
        EsconderSugestoes();
        if (produto.PrecoVenda <= 0)
        {
            Ui.Aviso(this, $"{produto.Descricao} está sem preço de venda. Corrija na Gestão.");
            Focar();
            return;
        }
        EsconderConcluido();
        if (_estado is EstadoColuna.Pagamento or EstadoColuna.Concluida) MostrarColuna(EstadoColuna.Resumo);
        _pendente = produto;
        _busca.Caixa.Clear();
        EsconderSugestoes();
        _nomePendente.Text = produto.Descricao;
        _infoPendente.Text = $"Código {produto.Codigo}  ·  estoque {Cupom.Qtd(produto.Estoque)} {produto.Unidade}" +
                             (produto.EmPromocao ? $"  ·  EM PROMOÇÃO (normal {Ui.Dinheiro(produto.PrecoNormal)})" : "") +
                             TextoAtacado(produto) +
                             "  ·  Enter: próximo  ·  Esc: cancelar";
        _precoPendente.Valor = produto.PrecoVenda;
        if (_quantidade.Valor <= 0) _quantidade.Valor = 1;
        if (Formatos.UnidadePesavel(produto.Unidade))
            AplicarPesoDaBalanca(produto.Unidade);
        _faixaPendente.Visible = true;
        AtualizarTotalPendente();
        AtualizarCentro();
        _quantidade.Focus();
        _quantidade.SelectAll();
    }

    private void AtualizarTotalPendente()
    {
        if (_pendente is null) return;
        var qtd = _quantidade.Valor > 0 ? _quantidade.Valor : 1;
        _totalPendente.Text = Ui.Dinheiro(Math.Round(qtd * _precoPendente.Valor, 2));
    }

    private void LerPesoNaQuantidade()
    {
        if (_pendente is null || !Formatos.UnidadePesavel(_pendente.Unidade))
        {
            _status.Text = "A balança só é lida em produto por peso (KG ou G).";
            return;
        }
        if (!LeitorBalanca.Configurada(_cfg))
        {
            _status.Text = "Balança USB não configurada. Use Opções do caixa.";
            return;
        }
        if (!LeitorBalanca.TentarLerPeso(_cfg, out var kg, out var erro))
        {
            _status.Text = erro ?? "A balança não enviou o peso.";
            return;
        }
        _quantidade.Valor = QuantidadeNaUnidade(_pendente.Unidade, kg);
        _status.Text = $"Peso estável: {Cupom.Qtd(kg)} kg";
        AtualizarTotalPendente();
    }

    private void AplicarPesoDaBalanca(string unidade)
    {
        if (!LeitorBalanca.Configurada(_cfg)) return;
        if (LeitorBalanca.TentarLerPeso(_cfg, out var kg, out var erro) && kg > 0)
        {
            _quantidade.Valor = QuantidadeNaUnidade(unidade, kg);
            _status.Text = $"Peso estável: {Cupom.Qtd(kg)} kg";
        }
        else if (erro is not null)
            _status.Text = erro;
    }

    private static decimal QuantidadeNaUnidade(string unidade, decimal kg) =>
        unidade.Equals("G", StringComparison.OrdinalIgnoreCase) ? Math.Round(kg * 1000m, 0) : kg;

    /// <summary>Coloca o produto escolhido na venda com a quantidade e o preço informados.</summary>
    private void ConfirmarPendente()
    {
        if (_pendente is not { } p) return;
        var preco = Math.Round(_precoPendente.Valor, 2);
        if (preco <= 0)
        {
            Ui.Aviso(this, "Informe o preço do item.");
            _precoPendente.Focus();
            _precoPendente.SelectAll();
            return;
        }
        var alterado = preco != p.PrecoVenda;
        if (alterado && !AutorizacaoForm.Pedir(this, _operador, $"Alterar o preço de {p.Descricao}", Permissoes.CaixaAlterarPreco))
        {
            _precoPendente.Valor = p.PrecoVenda;
            _precoPendente.Focus();
            _precoPendente.SelectAll();
            return;
        }
        var qtdInformada = _quantidade.Valor > 0 ? _quantidade.Valor : 1;
        if (Formatos.UnidadePesavel(p.Unidade) && LeitorBalanca.Configurada(_cfg) && qtdInformada == 1)
        {
            if (!LeitorBalanca.TentarLerPeso(_cfg, out var kg, out var erro) || kg <= 0)
            {
                _status.Text = erro ?? "Balança sem peso.";
                return;
            }
            qtdInformada = QuantidadeNaUnidade(p.Unidade, kg);
        }
        FecharPendente();
        AdicionarProduto(p, qtdInformada, alterado ? preco : null);
    }

    private void CancelarPendente()
    {
        FecharPendente();
        _quantidade.Valor = 1;
        AtualizarCentro();
        Focar();
    }

    private void FecharPendente()
    {
        _pendente = null;
        _faixaPendente.Visible = false;
    }

    private ProdutoLocal? PesquisarProduto(string texto)
    {
        EsconderSugestoes();
        using var f = new PesquisaForm<ProdutoLocal>("Pesquisar produto", texto, Repositorio.PesquisarProdutos,
            ("Código", p => p.Codigo, 18, false),
            ("Descrição", p => p.Descricao, 52, false),
            ("Preço", p => Ui.Dinheiro(p.PrecoVenda), 15, true),
            ("Estoque", p => Cupom.Qtd(p.Estoque), 15, true));
        return f.ShowDialog(this) == DialogResult.OK ? f.Selecionado : null;
    }

    /// <param name="precoInformado">Preço combinado no caixa (já autorizado); null = preço do produto.</param>
    private void AdicionarProduto(ProdutoLocal produto, decimal? quantidadeInformada = null, decimal? precoInformado = null)
    {
        EsconderSugestoes();
        if (produto.PrecoVenda <= 0)
        {
            Ui.Aviso(this, $"{produto.Descricao} está sem preço de venda. Corrija na Gestão.");
            Focar();
            return;
        }
        decimal quantidade;
        if (quantidadeInformada is not null)
            quantidade = quantidadeInformada.Value;
        else if (Formatos.UnidadePesavel(produto.Unidade) && LeitorBalanca.Configurada(_cfg))
        {
            if (!LeitorBalanca.TentarLerPeso(_cfg, out var kg, out var erro) || kg <= 0)
            {
                _status.Text = erro ?? "Balança sem peso.";
                Focar();
                return;
            }
            quantidade = QuantidadeNaUnidade(produto.Unidade, kg);
            _status.Text = $"Peso estável: {Cupom.Qtd(kg)} kg";
        }
        else
            quantidade = _quantidade.Valor > 0 ? _quantidade.Valor : 1;
        // UN, CX, PC... só vendem quantidade inteira; KG, L, M... aceitam fração.
        if (!Formatos.UnidadeFracionada(produto.Unidade) && quantidade != decimal.Truncate(quantidade))
        {
            Ui.Aviso(this, $"{produto.Descricao} é vendido em {produto.Unidade}: a quantidade precisa ser inteira.");
            Focar();
            return;
        }
        quantidade = Math.Round(quantidade, Formatos.CasasQuantidade(produto.Unidade));

        // Passou um produto: some a tela de venda concluída e, se estava pagando, volta para a venda.
        EsconderConcluido();
        if (_estado is EstadoColuna.Pagamento or EstadoColuna.Concluida) MostrarColuna(EstadoColuna.Resumo);

        _itens.Add(new ItemCarrinho
        {
            ProdutoId = produto.Id,
            Codigo = produto.Codigo,
            Descricao = produto.Descricao,
            Unidade = produto.Unidade,
            Quantidade = quantidade,
            PrecoUnitario = precoInformado ?? produto.PrecoVenda,
            PrecoNormal = precoInformado is null && produto.EmPromocao ? produto.PrecoNormal : 0,
            PrecoAlterado = precoInformado is not null,
            PrecoVarejo = precoInformado ?? produto.PrecoVenda,
            PrecoNormalVarejo = precoInformado is null && produto.EmPromocao ? produto.PrecoNormal : 0,
            Faixas = precoInformado is null ? produto.Faixas : []
        });
        var noCarrinho = _itens.Where(i => i.ProdutoId == produto.Id).Sum(i => i.Quantidade);
        if (!_modoOrcamento && produto.Estoque - noCarrinho < 0)
            _status.Text = $"Atenção: {produto.Descricao} ficou com estoque negativo no caixa.";
        else if (precoInformado is null && produto.Faixas.Count > 0)
        {
            var faixas = produto.Faixas;
            var (_, usada) = Atacado.Calcular(produto.PrecoVenda, faixas, noCarrinho);
            var proxima = Atacado.Proxima(produto.PrecoVenda, faixas, noCarrinho);
            if (usada is { } u)
                _status.Text = $"{produto.Descricao}: preço de atacado {Ui.Dinheiro(u.Preco)} (a partir de {Cupom.Qtd(u.Minimo)} {produto.Unidade}).";
            else if (proxima is { } px)
                _status.Text = $"{produto.Descricao}: levando {Cupom.Qtd(px.Minimo)} {produto.Unidade} ou mais, sai {Ui.Dinheiro(px.Preco)} cada (atacado).";
        }

        _quantidade.Valor = 1;
        AtualizarTela();
        Focar();
    }

    private ItemCarrinho? ItemSelecionado(out int indice)
    {
        indice = _grade.CurrentRow?.Index ?? -1;
        return indice >= 0 && indice < _itens.Count ? _itens[indice] : null;
    }

    /// <summary>Alterar quantidade (livre) e preço (pede autorização de quem pode alterar preço). Também remove o item.</summary>
    private void EditarItem()
    {
        if (ItemSelecionado(out var indice) is not { } item) { Focar(); return; }
        DialogResult resultado;
        decimal novaQtd, novoPreco;
        using (var f = new EditarItemForm(item.Descricao, item.Unidade, item.Quantidade, item.PrecoUnitario, TeclasCaixa.Texto(Tecla(AcaoCaixa.RemoverItem))))
        {
            resultado = f.ShowDialog(this);
            novaQtd = f.Quantidade;
            novoPreco = f.Preco;
        }
        if (resultado == DialogResult.Abort) { RemoverItem(); return; }
        if (resultado != DialogResult.OK) { Focar(); return; }
        if (novoPreco != item.PrecoUnitario &&
            !AutorizacaoForm.Pedir(this, _operador, $"Alterar o preço de {item.Descricao}", Permissoes.CaixaAlterarPreco))
        {
            Focar();
            return;
        }
        item.Quantidade = novaQtd;
        if (novoPreco != item.PrecoUnitario)
        {
            item.PrecoUnitario = novoPreco;
            item.PrecoAlterado = true;
            item.PrecoNormal = 0; // preço combinado no caixa: não mostra "promoção"
        }
        if (_estado == EstadoColuna.Pagamento) MostrarColuna(EstadoColuna.Resumo);
        else AtualizarTela();
        if (indice < _grade.Rows.Count) _grade.CurrentCell = _grade.Rows[indice].Cells[0];
        Focar(limpar: false);
    }

    private void RemoverItem()
    {
        if (ItemSelecionado(out var indice) is not { } item) return;
        if (!Ui.Confirmar(this, $"Remover o item {indice + 1} — {item.Descricao}?")) { Focar(); return; }
        if (!AutorizacaoForm.Pedir(this, _operador, $"Remover {item.Descricao} da venda", Permissoes.CaixaRemoverItem)) { Focar(); return; }
        _itens.RemoveAt(indice);
        // Mexeu nos itens durante o pagamento: volta para a venda (o total mudou).
        if (_estado == EstadoColuna.Pagamento) MostrarColuna(EstadoColuna.Resumo);
        else AtualizarTela();
        Focar();
    }

    private void CancelarVendaAtual()
    {
        if (_itens.Count == 0 && _orcamentoAtual is null) { Focar(); return; }
        if (Ui.Confirmar(this, _modoOrcamento ? "Limpar o orçamento em andamento?" : "Cancelar a venda em andamento?"))
        {
            Limpar();
            if (!_modoOrcamento) MostrarColuna(EstadoColuna.Resumo);
        }
        Focar();
    }

    private void EscolherCliente()
    {
        if (_cliente is not null && Ui.Confirmar(this, $"Remover o cliente {_cliente.Nome}?"))
        {
            _cliente = null;
            AtualizarTela();
            Focar();
            return;
        }
        using (var f = new PesquisaForm<ClienteLocal>("Cliente da venda (nome, CPF ou telefone)", "", Repositorio.PesquisarClientes,
                   ("Nome", c => c.Nome, 55, false),
                   ("CPF/CNPJ", c => Formatos.FormatarCpfCnpj(c.CpfCnpj), 25, false),
                   ("Telefone", c => Formatos.FormatarTelefone(c.Telefone), 20, false)))
        {
            if (f.ShowDialog(this) == DialogResult.OK) _cliente = f.Selecionado;
        }
        AtualizarTela();
        if (_estado == EstadoColuna.Pagamento) FocarPagamento();
        else Focar();
    }

    /// <summary>F6: desconto na venda (% ou R$). Zero tira o desconto. Pede autorização de quem pode dar desconto.</summary>
    private void AbrirDesconto()
    {
        if (_itens.Count == 0) { Ui.Aviso(this, "Passe os produtos antes do desconto."); Focar(); return; }
        decimal? percentual;
        decimal emReais;
        using (var f = new DescontoForm(Subtotal, _desconto, _descontoPercentual))
        {
            if (f.ShowDialog(this) != DialogResult.OK) { Focar(limpar: false); return; }
            percentual = f.Percentual;
            emReais = f.EmReais;
        }
        if (emReais >= Subtotal && emReais > 0) { Ui.Aviso(this, "O desconto precisa ser menor que o subtotal."); Focar(); return; }
        if (emReais > 0 && emReais != _desconto &&
            !AutorizacaoForm.Pedir(this, _operador, "Desconto na venda", Permissoes.CaixaDesconto))
        {
            Focar();
            return;
        }
        _descontoPercentual = emReais > 0 ? percentual : null;
        _desconto = emReais;
        AtualizarTela();
        if (_estado == EstadoColuna.Pagamento)
        {
            if (_formaEscolhida is not null) _recebido.Valor = _dividindo ? FaltaDividido : Total;
            AtualizarPagamento();
            FocarPagamento();
        }
        else Focar();
    }

    // ---------- Pagamento (na coluna) ----------

    private int IndiceForma() => _botoesForma.FindIndex(b => b.Dado is FormaLocal f && f.Id == _formaEscolhida?.Id);

    private decimal PagoDividido => _parciais.Sum(x => x.valor);
    private decimal FaltaDividido => Math.Max(0, Total - PagoDividido);

    /// <summary>Enter na venda (ou F7 a F12): a coluna vira o pagamento. Sem forma escolhida, já vem a primeira (dinheiro).</summary>
    private void AbrirPagamento(int? indiceForma)
    {
        if (_modoOrcamento) { SalvarOrcamento(); return; }
        if (_itens.Count == 0) { Focar(); return; }
        if (_formas.Count == 0)
        {
            CarregarFormas();
            if (_formas.Count == 0)
            {
                Ui.Aviso(this, $"Nenhuma forma de pagamento cadastrada. {T("Sincronize com o servidor", AcaoCaixa.Sincronizar)}.");
                return;
            }
        }
        if (_estado != EstadoColuna.Pagamento)
        {
            // Impressora: avisa ANTES de receber o pagamento, para dar tempo de ligar/conectar.
            _imprimirCupom = true;
            var impressora = SituacaoAgora();
            if (impressora is { Ok: false })
            {
                var resposta = MessageBox.Show(this,
                    impressora.Mensagem + "\n\nFinalizar a venda mesmo assim, SEM imprimir o cupom?\n\n" +
                    $"Sim: a venda é gravada; depois imprima a 2ª via em {T("Vendas do caixa", AcaoCaixa.Vendas)}.\n" +
                    "Não: volta para a venda (ligue/conecte a impressora e pague de novo).",
                    "Impressora com problema", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (resposta != DialogResult.Yes) { Focar(); return; }
                _imprimirCupom = false;
            }
            EsconderConcluido();
            MostrarColuna(EstadoColuna.Pagamento);
        }
        EscolherForma(indiceForma ?? (IndiceForma() >= 0 ? IndiceForma() : 0));
    }

    private void EscolherForma(int indice)
    {
        if (_modoOrcamento || indice < 0 || indice >= _botoesForma.Count) return;
        if (_estado != EstadoColuna.Pagamento)
        {
            AbrirPagamento(indice);
            return;
        }
        var forma = _botoesForma[indice].Dado as FormaLocal;
        if (forma is null)
        {
            // "Outros": escolhe numa lista as formas que não couberam nos botões.
            var restantes = _formas.Skip(5).ToList();
            using var f = new EscolherFormaForm(restantes);
            if (f.ShowDialog(this) != DialogResult.OK || f.Escolhida is null) { FocarPagamento(); return; }
            forma = f.Escolhida;
            _botoesForma[indice].Text = forma.Descricao;
        }
        _formaEscolhida = forma;
        for (var i = 0; i < _botoesForma.Count; i++) _botoesForma[i].Selecionado = i == indice;
        _recebido.Valor = _dividindo ? FaltaDividido : Total;
        AtualizarPagamento();
        FocarPagamento();
    }

    private void FocarPagamento()
    {
        if (_recebido.Enabled && _recebido.Visible)
        {
            _recebido.Focus();
            _recebido.SelectAll();
        }
        else
        {
            _concluir.Focus();
        }
    }

    /// <summary>Atualiza a coluna de pagamento: forma escolhida, campo do valor, notas, troco e avisos.</summary>
    private void AtualizarPagamento()
    {
        var forma = _formaEscolhida;
        _rotuloPag.Text = _dividindo ? "PAGAMENTO DIVIDIDO" : "TOTAL A PAGAR";
        _totalPag.Height = _dividindo ? 44 : 56;
        _totalPag.Text = Ui.Dinheiro(Total);
        _rotuloForma.Text = _dividindo && _parciais.Count > 0 ? "Próxima forma" : "Forma de pagamento";
        _dividir.Text = _dividindo ? "Voltar para uma forma só" : "Dividir em mais de uma forma";

        // Pagamentos já lançados (dividido): cada linha remove com um clique.
        _parciaisBox.SuspendLayout();
        foreach (Control c in _parciaisBox.Controls.Cast<Control>().ToList()) { _parciaisBox.Controls.Remove(c); c.Dispose(); }
        if (_dividindo)
        {
            for (var i = _parciais.Count - 1; i >= 0; i--)
            {
                var (f, v) = _parciais[i];
                var linha = new LinhaValor(f.Descricao) { Height = 34, Valor = Ui.Dinheiro(v) + "   ✕", Cursor = Cursors.Hand };
                var indice = i;
                // Depois do clique (BeginInvoke): a linha clicada é recriada, não pode sumir dentro do próprio evento.
                linha.Click += (_, _) => BeginInvoke(new Action(() =>
                {
                    if (IsDisposed || _estado != EstadoColuna.Pagamento) return;
                    if (indice < _parciais.Count) _parciais.RemoveAt(indice);
                    if (_formaEscolhida is not null) _recebido.Valor = FaltaDividido;
                    AtualizarPagamento();
                    FocarPagamento();
                }));
                _parciaisBox.Controls.Add(linha);
            }
        }
        _parciaisBox.Height = _dividindo ? _parciais.Count * 34 : 0;
        _parciaisBox.ResumeLayout(true);
        _falta.Visible = _dividindo && _parciais.Count > 0;
        _falta.Valor = Ui.Dinheiro(FaltaDividido);

        // Campo do valor: dividido aceita valor em qualquer forma; numa forma só, só dinheiro (troco) é digitado.
        _recebido.Enabled = forma is not null && (_dividindo || forma.PermiteTroco);
        _rotuloValor.Text = forma is null ? "Valor recebido"
            : _dividindo ? $"Valor no {forma.Descricao}"
            : forma.PermiteTroco ? "Valor recebido" : $"Valor no {forma.Descricao}";
        if (forma is not null && !_recebido.Enabled) _recebido.Valor = Total;

        // Notas rápidas: o valor exato e as próximas notas redondas (só em forma com troco e numa forma só).
        var comNotas = forma is { PermiteTroco: true } && !_dividindo;
        _notas.Visible = comNotas;
        if (comNotas)
        {
            var sugestoes = SugestoesDeNotas(Total);
            for (var i = 0; i < _botoesNota.Count; i++)
            {
                var b = _botoesNota[i];
                if (i < sugestoes.Count)
                {
                    b.Visible = true;
                    b.Dado = sugestoes[i];
                    b.Text = i == 0 ? "Exato" : "R$ " + sugestoes[i].ToString("N0", Ui.Br);
                }
                else
                {
                    b.Visible = false;
                }
            }
        }

        _concluir.Text = _dividindo && (forma is null || _recebido.Valor < FaltaDividido) ? "Adicionar   (Enter)" : "Concluir   (Enter)";
        _concluir.Estilo = _concluir.Text.StartsWith("Adicionar", StringComparison.Ordinal) ? EstiloBotao.Primario : EstiloBotao.Sucesso;

        // Aviso embaixo: fiado sem cliente, para quem vai a conta, ou confirmar o cartão/PIX.
        if (forma is null) _avisoPag.Text = "Escolha a forma de pagamento.";
        else if (forma.EhFiado && _cliente is null) _avisoPag.Text = $"Fiado precisa do cliente: {T("aperte para escolher", AcaoCaixa.Cliente)}.";
        else if (forma.EhFiado) _avisoPag.Text = $"Vai para a conta de {_cliente!.Nome}.";
        else if (UsaQrPix(forma)) _avisoPag.Text = "Enter mostra o QR Code do Pix com o valor.";
        else if (!forma.PermiteTroco) _avisoPag.Text = "Confirme só depois de ver o pagamento aprovado.";
        else _avisoPag.Text = "";
        _avisoPag.Visible = _avisoPag.Text.Length > 0;
        AjustarAlturaFormas();
        AtualizarTroco();
        AjustarTotais();
    }

    /// <summary>Exato, e as próximas notas redondas acima do total (ex.: 90,00 → Exato, 95, 100, 150).</summary>
    private static List<decimal> SugestoesDeNotas(decimal total)
    {
        var lista = new List<decimal> { total };
        foreach (var nota in new[] { 2m, 5m, 10m, 20m, 50m, 100m, 200m })
        {
            var v = Math.Ceiling(total / nota) * nota;
            if (v > total && !lista.Contains(v)) lista.Add(v);
        }
        return [lista[0], .. lista.Skip(1).OrderBy(v => v).Take(3)];
    }

    private void AtualizarTroco()
    {
        var forma = _formaEscolhida;
        var baseTroco = _dividindo ? FaltaDividido : Total;
        // Dividindo, o troco só aparece quando o valor passa do que falta (economiza espaço na coluna).
        var mostrar = forma is { PermiteTroco: true } && (!_dividindo || _recebido.Valor > baseTroco);
        _troco.Visible = mostrar;
        _troco.Valor = Ui.Dinheiro(mostrar ? Math.Max(0, _recebido.Valor - baseTroco) : 0);
        foreach (var b in _botoesNota) b.Selecionado = b.Visible && b.Dado is decimal v && v == _recebido.Valor;
        if (_estado == EstadoColuna.Pagamento && _dividindo)
        {
            var adicionar = forma is null || _recebido.Valor < FaltaDividido;
            _concluir.Text = adicionar ? "Adicionar   (Enter)" : "Concluir   (Enter)";
            _concluir.Estilo = adicionar ? EstiloBotao.Primario : EstiloBotao.Sucesso;
        }
    }

    private void TrocarDividir(bool dividir)
    {
        _dividindo = dividir;
        _parciais.Clear();
        if (_formaEscolhida is not null) _recebido.Valor = dividir ? FaltaDividido : Total;
        AtualizarPagamento();
        FocarPagamento();
    }

    private void VoltarDoPagamento()
    {
        if (_parciais.Count > 0 && !Ui.Confirmar(this, "Voltar para a venda? Os pagamentos já lançados nesta divisão serão desfeitos.")) { FocarPagamento(); return; }
        MostrarColuna(EstadoColuna.Resumo);
        Focar();
    }

    /// <summary>Enter no pagamento: numa forma só, conclui; dividindo, lança este valor e conclui quando fechar o total.</summary>
    private void ConcluirPagamento()
    {
        if (_estado != EstadoColuna.Pagamento || _itens.Count == 0) return;
        if (_formaEscolhida is null)
        {
            if (_botoesForma.Count > 0) EscolherForma(0);
            return;
        }
        var forma = _formaEscolhida;
        var total = Total;

        if (_dividindo)
        {
            var falta = FaltaDividido;
            var v = _recebido.Valor;
            if (v <= 0) { Ui.Aviso(this, "Informe o valor."); FocarPagamento(); return; }
            if (!forma.PermiteTroco && v > falta)
            {
                Ui.Aviso(this, $"{forma.Descricao} não permite troco. O valor máximo é {Ui.Dinheiro(falta)}.");
                _recebido.Valor = falta;
                FocarPagamento();
                return;
            }
            if (UsaQrPix(forma) && !PixRecebido(Math.Min(v, falta))) return;
            _parciais.Add((forma, v));
            if (PagoDividido < total)
            {
                _recebido.Valor = FaltaDividido;
                AtualizarPagamento();
                FocarPagamento();
                return;
            }
            // Fechou o total: o troco sai das formas que aceitam troco, da última para a primeira.
            var troco = PagoDividido - total;
            var aceitaTroco = _parciais.Where(x => x.forma.PermiteTroco).Sum(x => x.valor);
            if (troco > aceitaTroco)
            {
                _parciais.RemoveAt(_parciais.Count - 1);
                Ui.Aviso(this, "O troco é maior que o valor recebido em dinheiro. Revise os pagamentos.");
                AtualizarPagamento();
                FocarPagamento();
                return;
            }
            var restante = troco;
            var liquidos = _parciais.Select(x => (x.forma, x.valor)).ToList();
            for (var i = liquidos.Count - 1; i >= 0 && restante > 0; i--)
            {
                if (!liquidos[i].forma.PermiteTroco) continue;
                var abate = Math.Min(restante, liquidos[i].valor);
                liquidos[i] = (liquidos[i].forma, liquidos[i].valor - abate);
                restante -= abate;
            }
            if (!GravarVenda(liquidos.Where(x => x.valor > 0).ToList(), troco, PagoDividido))
            {
                // Não gravou (ex.: fiado sem cliente): o último valor lançado volta para o campo.
                _parciais.RemoveAt(_parciais.Count - 1);
                _recebido.Valor = FaltaDividido;
                AtualizarPagamento();
                FocarPagamento();
            }
            return;
        }

        var recebido = forma.PermiteTroco ? _recebido.Valor : total;
        if (recebido <= 0) recebido = total;
        if (recebido < total)
        {
            Ui.Aviso(this, $"O valor recebido ({Ui.Dinheiro(recebido)}) é menor que o total ({Ui.Dinheiro(total)}).\n\n" +
                           "Corrija o valor ou use \"Dividir em mais de uma forma\".");
            FocarPagamento();
            return;
        }
        if (UsaQrPix(forma) && !PixRecebido(total)) return;
        GravarVenda([(forma, total)], recebido - total, recebido);
    }

    /// <summary>Botões rápidos: escolhe um produto sem código de barras (ou dos escolhidos nas Opções) e põe na venda.</summary>
    private void AbrirRapidos()
    {
        if (_estado == EstadoColuna.Pagamento && _parciais.Count > 0)
        {
            Ui.Aviso(this, "Termine o pagamento dividido, ou volte para a venda, antes de colocar mais produtos.");
            FocarPagamento();
            return;
        }
        var produtos = Repositorio.ProdutosRapidos(_cfg.BotoesRapidos, 16);
        if (produtos.Count == 0)
        {
            Ui.Aviso(this, "Ainda não há produtos para os botões rápidos.\n\n" +
                           "Escolha os produtos em Opções do caixa → Botões rápidos, ou cadastre na Gestão os produtos " +
                           "sem código de barras (pão, salgados...) e envie aos caixas.");
            Focar(limpar: false);
            return;
        }
        ProdutoLocal? escolhido;
        using (var f = new RapidosForm(produtos, automaticos: _cfg.BotoesRapidos.Count == 0))
            escolhido = f.ShowDialog(this) == DialogResult.OK ? f.Escolhido : null;
        if (escolhido is null) { Focar(limpar: false); return; }

        // Vendido por peso e sem balança ligada: abre o produto para digitar o peso (como na busca pelo nome).
        if (Formatos.UnidadePesavel(escolhido.Unidade) && !LeitorBalanca.Configurada(_cfg))
        {
            EscolherProduto(escolhido);
            return;
        }
        EsconderConcluido();
        if (_estado is EstadoColuna.Pagamento or EstadoColuna.Concluida) MostrarColuna(EstadoColuna.Resumo);
        AdicionarProduto(escolhido);
    }

    /// <summary>Forma com "PIX" no nome e chave Pix configurada nas Opções: o pagamento passa pelo QR Code.</summary>
    private bool UsaQrPix(FormaLocal forma) =>
        _cfg.PixConfigurado && !forma.EhFiado && forma.Descricao.Contains("PIX", StringComparison.OrdinalIgnoreCase);

    /// <summary>Mostra o QR Code com o valor e espera o operador confirmar que o Pix chegou. false = cancelou.</summary>
    private bool PixRecebido(decimal valor)
    {
        using var f = new PixQrForm(_cfg, valor);
        if (f.ShowDialog(this) == DialogResult.OK) return true;
        FocarPagamento();
        return false;
    }

    /// <summary>Grava a venda, imprime o cupom e mostra o troco. false = não gravou (continua no pagamento).</summary>
    private bool GravarVenda(List<(FormaLocal forma, decimal valor)> pagamentos, decimal troco, decimal recebido)
    {
        var total = Total;
        // Fiado fica na conta do cliente: sem cliente o servidor recusaria a venda depois, com o cliente já fora da loja.
        if (_cliente is null && pagamentos.Any(p => p.forma.EhFiado && p.valor > 0))
        {
            Ui.Aviso(this, $"Fiado precisa do cliente na venda.\n\n{T("Aperte para escolher o cliente", AcaoCaixa.Cliente)} e conclua de novo.");
            FocarPagamento();
            return false;
        }

        var vendaId = Guid.NewGuid().ToString();
        var vc = new VendaCompleta
        {
            Venda = new VendaLocal
            {
                Id = vendaId,
                Numero = 0, // atribuído atomicamente por SalvarVenda()
                CaixaId = _caixa.Id,
                UsuarioId = _operador.Id,
                ClienteId = _cliente?.Id,
                Data = Db.Agora(),
                Subtotal = Subtotal,
                Desconto = _desconto,
                Total = total,
                Troco = troco,
                Status = (int)StatusVenda.Concluida
            },
            Itens = _itens.Select(i => new VendaItemLocal
            {
                Id = Guid.NewGuid().ToString(),
                VendaId = vendaId,
                ProdutoId = i.ProdutoId,
                Descricao = i.Descricao,
                Quantidade = i.Quantidade,
                PrecoUnitario = i.PrecoUnitario,
                Desconto = 0,
                Total = i.Total,
                PrecoNormal = i.PrecoNormal,
                Atacado = i.AtacadoMinimo is not null
            }).ToList(),
            Pagamentos = pagamentos.Select(p => new VendaPagamentoLocal
            {
                Id = Guid.NewGuid().ToString(),
                VendaId = vendaId,
                FormaId = p.forma.Id,
                Valor = p.valor
            }).ToList()
        };

        try
        {
            Repositorio.SalvarVenda(vc);
            if (_orcamentoAtual is not null)
                Repositorio.AlterarStatusOrcamento(_orcamentoAtual.Id, StatusOrcamento.Convertido, vendaId);
        }
        catch (Exception ex)
        {
            Ui.Erro(this, "Não foi possível gravar a venda: " + ex.Message);
            return false;
        }

        var nomeCliente = _cliente?.Nome;
        var economia = Economia;
        var formasTexto = string.Join(" + ", pagamentos.Select(p => p.forma.Descricao).Distinct());
        var erro = _imprimirCupom ? Cupom.ImprimirVenda(_cfg, vc, _operador.Nome, nomeCliente, _terminal) : null;
        _ultimaVenda = vc;
        _ultimoCliente = nomeCliente;
        Limpar();

        // Coluna: troco grande (ou "PAGO" sem troco), total, como pagou e o número da venda.
        _rotuloConc.Text = troco > 0 ? "TROCO" : "VENDA PAGA";
        _valorConc.Text = Ui.Dinheiro(troco > 0 ? troco : total);
        _valorConc.ForeColor = troco > 0 ? CoresCaixa.TrocoTexto : Color.White;
        _cTotal.Valor = Ui.Dinheiro(total);
        _cPagamento.Text = pagamentos.Count == 1 && pagamentos[0].forma.PermiteTroco ? $"Recebido ({formasTexto})" : "Pagamento";
        _cPagamento.Valor = pagamentos.Count == 1 && pagamentos[0].forma.PermiteTroco ? Ui.Dinheiro(recebido) : formasTexto;
        _cVenda.Valor = $"nº {vc.Venda.Numero}";
        MostrarColuna(EstadoColuna.Concluida);

        // Meio: "Venda concluída" com a economia do cliente (sem o nome), até passar o próximo produto.
        _concluido.Mensagem = !_imprimirCupom ? "Venda gravada. Cupom NÃO impresso (imprima a 2ª via depois)."
            : erro is null ? "Obrigado pela preferência! Cupom impresso." : "Venda gravada, mas o cupom não saiu.";
        _concluido.Alerta = !_imprimirCupom || erro is not null;
        _concluido.Economia = economia > 0 ? $"Você economizou {Ui.Dinheiro(economia)} hoje" : null;
        _mostrandoConcluido = true;
        _timerConcluido.Stop();
        _timerConcluido.Start();
        AtualizarCentro();

        _status.Text = $"Venda nº {vc.Venda.Numero} concluída" + (troco > 0 ? $" — TROCO {Ui.Dinheiro(troco)}" : "");
        _manterStatusAte = DateTime.Now.AddSeconds(15); // a sincronização logo em seguida não apaga a confirmação

        // Não imprimiu: a venda já está gravada; oferece tentar de novo (ex.: depois de ligar a impressora).
        while (erro is not null && MessageBox.Show(this,
                   $"Venda nº {vc.Venda.Numero} gravada, mas o cupom NÃO foi impresso:\n{erro}\n\n" +
                   $"Tentar imprimir de novo?\n(Não: dá para imprimir a 2ª via depois em {T("Vendas do caixa", AcaoCaixa.Vendas)}.)",
                   "Impressão do cupom", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
        {
            erro = Cupom.ImprimirVenda(_cfg, vc, _operador.Nome, nomeCliente, _terminal);
            if (erro is null)
            {
                _concluido.Mensagem = "Obrigado pela preferência! Cupom impresso.";
                _concluido.Alerta = false;
            }
        }
        if (!_imprimirCupom || erro is not null) _ = ConferirImpressoraAsync();
        SincronizarEGuardarCopia(); // a Retaguarda recebe a venda em segundos
        _catalogo = null;            // estoque mudou
        Focar();
        return true;
    }

    /// <summary>Reimprime o cupom da última venda deste caixa (2ª via). Sem venda ainda: abre a lista de vendas.</summary>
    private void Reimprimir()
    {
        if (_ultimaVenda is null) { AbrirVendas(); return; }
        var erro = Cupom.ImprimirVenda(_cfg, _ultimaVenda, _operador.Nome, _ultimoCliente, _terminal, segundaVia: true);
        if (erro is not null) Ui.Erro(this, "Falha na impressão: " + erro);
        else _status.Text = $"2ª via da venda nº {_ultimaVenda.Venda.Numero} impressa.";
        Focar(limpar: false);
    }

    private void EsconderConcluido()
    {
        if (!_mostrandoConcluido) return;
        _mostrandoConcluido = false;
        _timerConcluido.Stop();
        AtualizarCentro();
    }

    // ---------- Orçamento ----------

    private void MudarModo(bool orcamento, bool perguntar = true)
    {
        if (orcamento && perguntar && !_modoOrcamento &&
            !AutorizacaoForm.Pedir(this, _operador, "Fazer orçamento", Permissoes.CaixaOrcamento))
        {
            Focar();
            return;
        }
        if (!orcamento && perguntar && _modoOrcamento && _orcamentoAtual is not null && _itens.Count > 0 &&
            !Ui.Confirmar(this, $"Transformar o orçamento nº {_orcamentoAtual.Numero} em venda?\n\nOs itens continuam na tela; ao pagar, o orçamento fica marcado como vendido."))
            return;

        _modoOrcamento = orcamento;
        _pModo.Visible = orcamento;
        EsconderConcluido();
        MostrarColuna(orcamento ? EstadoColuna.Orcamento : EstadoColuna.Resumo);
        Focar(limpar: false);
    }

    private void SalvarOrcamento()
    {
        if (_itens.Count == 0) { Ui.Aviso(this, "Adicione os produtos do orçamento."); Focar(); return; }
        var id = _orcamentoAtual?.Id ?? Guid.NewGuid().ToString();
        var dias = (int)_validadeDias.Valor;
        var oc = new OrcamentoCompleto
        {
            Orcamento = new OrcamentoLocal
            {
                Id = id,
                UsuarioId = _operador.Id,
                ClienteId = _cliente?.Id,
                ClienteNome = _cliente?.Nome,
                Data = Db.Agora(),
                Validade = dias > 0 ? Relogio.Hoje.AddDays(dias).ToString("yyyy-MM-dd") : null,
                Subtotal = Subtotal,
                Desconto = _desconto,
                Total = Total,
                Observacao = string.IsNullOrWhiteSpace(_observacaoOrcamento.Text) ? null : _observacaoOrcamento.Text.Trim(),
                Status = (int)StatusOrcamento.Aberto
            },
            Itens = _itens.Select(i => new OrcamentoItemLocal
            {
                ProdutoId = i.ProdutoId, Codigo = i.Codigo, Descricao = i.Descricao, Unidade = i.Unidade,
                Quantidade = i.Quantidade, PrecoUnitario = i.PrecoUnitario, Total = i.Total
            }).ToList()
        };
        if (oc.Orcamento.Observacao is { } obs && !Formatos.TextoSeguro(obs, multilinha: true))
        {
            Ui.Aviso(this, "A observação contém caracteres inválidos.");
            return;
        }
        try
        {
            Repositorio.SalvarOrcamento(oc);
        }
        catch (Exception ex)
        {
            Ui.Erro(this, "Não foi possível gravar o orçamento: " + ex.Message);
            return;
        }
        string? erro = null;
        if (_imprimirOrcamento.SelectedIndex == 1) erro = Cupom.ImprimirOrcamento(_cfg, oc, _operador.Nome, _terminal);
        else if (_imprimirOrcamento.SelectedIndex == 2) erro = OrcamentoFolha.Imprimir(_cfg, oc, _operador.Nome, _terminal);
        Limpar();
        _status.Text = $"Orçamento nº {oc.Orcamento.Numero} salvo.";
        MessageBox.Show(this, $"Orçamento nº {oc.Orcamento.Numero} salvo ({Ui.Dinheiro(oc.Orcamento.Total)})." +
                              (erro is null ? "" : "\n\nA impressão falhou: " + erro),
            "Orçamento", MessageBoxButtons.OK, erro is null ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        SincronizarEGuardarCopia();
        Focar();
    }

    private void AbrirOrcamentos()
    {
        if (_itens.Count > 0 && !Ui.Confirmar(this, "Há itens na tela. Abrir um orçamento substitui esses itens. Continuar?")) { Focar(); return; }
        OrcamentoCompleto? escolhido;
        bool paraVenda;
        using (var f = new OrcamentosCaixaForm(_cfg, _operador, _terminal))
        {
            if (f.ShowDialog(this) != DialogResult.OK || f.Escolhido is null) { Focar(); return; }
            escolhido = f.Escolhido;
            paraVenda = f.ParaVenda;
        }
        CarregarOrcamento(escolhido, paraVenda);
    }

    /// <summary>Coloca o orçamento na tela: para vender (modo venda) ou para alterar (modo orçamento).</summary>
    private void CarregarOrcamento(OrcamentoCompleto oc, bool paraVenda)
    {
        var o = oc.Orcamento;
        var usarPrecoAtual = false;
        if (paraVenda && DateOnly.TryParseExact(o.Validade, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var validade) &&
            validade < Relogio.Hoje)
        {
            var r = MessageBox.Show(this, $"O orçamento nº {o.Numero} venceu em {validade:dd/MM/yyyy}.\n\n" +
                                          "Sim = vender com os preços de hoje\nNão = manter os preços do orçamento",
                "Orçamento vencido", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) { Focar(); return; }
            usarPrecoAtual = r == DialogResult.Yes;
        }

        Limpar();
        var faltando = new List<string>();
        foreach (var i in oc.Itens)
        {
            var produto = Repositorio.BuscarProduto(i.Codigo);
            if (produto is null || produto.Id != i.ProdutoId)
            {
                faltando.Add(i.Descricao);
                continue;
            }
            _itens.Add(new ItemCarrinho
            {
                ProdutoId = produto.Id, Codigo = produto.Codigo, Descricao = produto.Descricao, Unidade = produto.Unidade,
                Quantidade = i.Quantidade,
                PrecoUnitario = usarPrecoAtual ? produto.PrecoVenda : i.PrecoUnitario,
                PrecoNormal = usarPrecoAtual && produto.EmPromocao ? produto.PrecoNormal : 0,
                // Com o preço do orçamento, fica o preço combinado; com o de hoje, o atacado vale de novo.
                PrecoVarejo = usarPrecoAtual ? produto.PrecoVenda : i.PrecoUnitario,
                PrecoNormalVarejo = usarPrecoAtual && produto.EmPromocao ? produto.PrecoNormal : 0,
                Faixas = usarPrecoAtual ? produto.Faixas : []
            });
        }
        _cliente = string.IsNullOrEmpty(o.ClienteId) ? null : Repositorio.ClientePorId(o.ClienteId);
        _desconto = Math.Min(o.Desconto, Math.Max(0, Subtotal - 0.01m));
        _orcamentoAtual = o;
        _observacaoOrcamento.Text = o.Observacao ?? "";
        EsconderConcluido();
        MudarModo(orcamento: !paraVenda, perguntar: false);
        if (faltando.Count > 0)
            Ui.Aviso(this, "Estes produtos não estão mais disponíveis neste caixa e ficaram de fora:\n\n• " + string.Join("\n• ", faltando.Take(15)));
        AtualizarTela();
    }

    // ---------- Caixa ----------

    private void SangriaSuprimento(bool suprimento)
    {
        using var f = new MovimentoCaixaForm(suprimento);
        if (f.ShowDialog(this) != DialogResult.OK) { Focar(); return; }
        if (f.Tipo == TipoMovimentoCaixa.Sangria && !AutorizacaoForm.Pedir(this, _operador, "Sangria de caixa", Permissoes.CaixaSangria)) { Focar(); return; }

        var m = Repositorio.RegistrarMovimentoCaixa(_caixa.Id, f.Tipo, f.Valor, f.Motivo, _operador.Id);
        var erro = Cupom.ImprimirMovimento(_cfg, m, _operador.Nome);
        if (erro is not null) Ui.Aviso(this, "Registrado, mas a impressão falhou:\n" + erro);
        SincronizarEGuardarCopia();
        Focar();
    }

    private void AbrirVendas()
    {
        using var f = new VendasCaixaForm(_cfg, _caixa, _operador, _terminal);
        f.ShowDialog(this);
        if (f.HouveCancelamento)
        {
            SincronizarEGuardarCopia();
            _catalogo = null;
        }
        Focar();
    }

    private void FecharCaixa()
    {
        if (_itens.Count > 0) { Ui.Aviso(this, "Finalize ou cancele a venda em andamento antes de fechar o caixa."); return; }

        var resumo = Repositorio.Resumo(_caixa);
        using var f = new FechamentoForm(resumo);
        if (f.ShowDialog(this) != DialogResult.OK) { Focar(); return; }

        Repositorio.FecharCaixa(_caixa.Id, _operador.Id, f.ValorContado);
        var erro = Cupom.ImprimirFechamento(_cfg, _caixa, resumo, f.ValorContado, _operador.Nome, _terminal);
        if (erro is not null) Ui.Aviso(this, "Caixa fechado, mas a impressão falhou:\n" + erro);

        SincronizarEGuardarCopia();
        Saida = SaidaVenda.CaixaFechado;
        Close();
    }

    private void TrocarOperador()
    {
        if (_itens.Count > 0) { Ui.Aviso(this, "Finalize ou cancele a venda em andamento antes de sair da conta."); return; }
        Saida = SaidaVenda.TrocarOperador;
        Close();
    }

    /// <summary>Opções do caixa (servidor, impressora, papel...). Pede autorização se o operador não puder.</summary>
    private void AbrirOpcoes()
    {
        if (_itens.Count > 0)
        {
            Ui.Aviso(this, "Finalize ou cancele a venda em andamento antes de abrir as opções.");
            Focar();
            return;
        }
        if (!AutorizacaoForm.Pedir(this, _operador, "Abrir as opções do caixa", Permissoes.CaixaOpcoes)) { Focar(); return; }

        using var f = new ConfiguracaoForm(_cfg, primeiraVez: false);
        if (f.ShowDialog(this) != DialogResult.OK) { Focar(); return; }

        // O caixa recarrega as configurações e volta direto para esta tela, com o mesmo operador.
        Saida = SaidaVenda.Reconfigurado;
        Close();
    }

    private async Task SincronizarAgora()
    {
        _status.Text = "Sincronizando...";
        CopiaSeguranca.Pedir(_cfg.PastaCopiaSeguranca);
        await _sync.SincronizarAsync();
        if (IsDisposed) return;
        if (_estado != EstadoColuna.Pagamento) CarregarFormas(); // formas novas chegam na sincronização
        _catalogo = null;
        _livre.Recarregar(forcar: true);
    }

    private void AoFechar(object? sender, FormClosingEventArgs e)
    {
        if (Saida == SaidaVenda.Sair && _itens.Count > 0 &&
            !Ui.Confirmar(this, "Há itens na tela. Sair mesmo assim? (eles serão perdidos)"))
        {
            e.Cancel = true;
        }
    }

    // ---------- Tela ----------

    private void AtualizarHora() => _pHora.Text = Relogio.Agora.ToString("HH:mm");

    /// <summary>Confere a impressora agora (no máximo 3 s: impressora de rede desligada pode demorar).</summary>
    private SituacaoImpressora? SituacaoAgora()
    {
        var tarefa = Task.Run(() => Cupom.Situacao(_cfg));
        try
        {
            if (!tarefa.Wait(3000)) return null; // não respondeu a tempo: tenta imprimir normalmente
        }
        catch (AggregateException) { return null; } // erro ao consultar a impressora: segue a venda e tenta imprimir
        if (!tarefa.IsCompletedSuccessfully) return null;
        MostrarImpressora(tarefa.Result);
        return tarefa.Result;
    }

    /// <summary>Confere em segundo plano e atualiza o aviso no rodapé.</summary>
    private async Task<SituacaoImpressora?> ConferirImpressoraAsync()
    {
        if (_conferindoImpressora || IsDisposed) return null;
        _conferindoImpressora = true;
        try
        {
            var s = await Task.Run(() => Cupom.Situacao(_cfg));
            if (!IsDisposed) MostrarImpressora(s);
            return s;
        }
        catch (Exception) { return null; }
        finally { _conferindoImpressora = false; }
    }

    private void MostrarImpressora(SituacaoImpressora s)
    {
        var etiqueta = !s.Configurada
            ? new Etiqueta("Sem impressora", TipoEtiqueta.Neutro, "Sem impressora: o cupom é salvo em arquivo. " + s.Mensagem,
                ClicarImpressora, Impressora: true, TextoCurto: "")
            : s.Ok
                ? new Etiqueta("Impressora pronta", TipoEtiqueta.Ok, s.Mensagem, ClicarImpressora, Impressora: true, TextoCurto: "")
                : new Etiqueta("Impressora com problema", TipoEtiqueta.Erro, s.Mensagem + " (clique para conferir)",
                    ClicarImpressora, Impressora: true, TextoCurto: "Impressora");
        _etiquetas.DefinirImpressora(etiqueta);
    }

    private async void ClicarImpressora()
    {
        var s = await ConferirImpressoraAsync();
        if (!IsDisposed && s is { Ok: false }) Ui.Aviso(this, s.Mensagem);
    }

    /// <summary>
    /// Etiquetas da sincronização: verde "Sincronizado às …", amarelo "N aguardando envio", vermelho "Sem conexão"
    /// ou "N recusadas — ver motivo" (abre a lista). A mensagem completa fica na dica do mouse.
    /// </summary>
    private void MostrarSincronizacao(bool online, string msg)
    {
        int pendentes, recusadas;
        try
        {
            pendentes = Repositorio.ContarPendencias();
            recusadas = Repositorio.VendasRecusadas().Count;
        }
        catch (Exception)
        {
            pendentes = 0;
            recusadas = 0;
        }
        var aguardando = Math.Max(0, pendentes - recusadas);
        var lista = new List<Etiqueta>();
        if (!online)
            lista.Add(new Etiqueta("Sem conexão com o servidor", TipoEtiqueta.Erro, msg + " (clique para tentar de novo)",
                () => _ = SincronizarAgora(), TextoCurto: "Offline"));
        if (aguardando > 0)
            lista.Add(new Etiqueta($"{aguardando} aguardando envio", TipoEtiqueta.Alerta, msg + " (clique para enviar agora)",
                () => _ = SincronizarAgora(), TextoCurto: $"{aguardando} aguardando"));
        if (recusadas > 0)
            lista.Add(new Etiqueta(recusadas == 1 ? "1 recusada — ver motivo" : $"{recusadas} recusadas — ver motivo", TipoEtiqueta.Erro,
                "Vendas que o servidor recusou: clique para ver o motivo de cada uma", MostrarRecusadas,
                TextoCurto: recusadas == 1 ? "1 recusada" : $"{recusadas} recusadas"));
        if (online && aguardando == 0 && recusadas == 0)
            lista.Add(new Etiqueta($"Sincronizado às {Relogio.Agora:HH:mm}", TipoEtiqueta.Ok, msg, TextoCurto: "Sincronizado"));
        _etiquetas.DefinirSincronizacao(lista);
    }

    private void MostrarRecusadas()
    {
        bool tentar;
        using (var f = new VendasRecusadasForm())
        {
            f.ShowDialog(this);
            tentar = f.TentarAgora;
        }
        if (IsDisposed) return;
        if (tentar) _ = SincronizarAgora();
        Focar();
    }

    /// <summary>Envia ao servidor e guarda a cópia de segurança na rede da loja (se configurada nas Opções).</summary>
    private void SincronizarEGuardarCopia()
    {
        _ = _sync.SincronizarAsync();
        CopiaSeguranca.Pedir(_cfg.PastaCopiaSeguranca);
    }

    /// <summary>A cópia de segurança falhou (ou voltou a funcionar): avisa no rodapé.</summary>
    private void AvisoCopia(bool ok, string msg)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(() =>
        {
            if (IsDisposed) return;
            _status.Text = msg;
            _dica.SetToolTip(_status, msg);
            _manterStatusAte = DateTime.Now.AddSeconds(ok ? 6 : 15);
        });
    }

    private void AtualizarStatus(bool online, string msg)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            MostrarSincronizacao(online, msg);
            return;
        }
        BeginInvoke(() =>
        {
            if (IsDisposed) return;
            if (online) _catalogo = null; // produtos/preços enviados pela Retaguarda já valem na pesquisa
            // A situação vai para as etiquetas; a mensagem rápida (venda concluída, balança...) fica uns segundos e some.
            MostrarSincronizacao(online, msg);
            if (DateTime.Now >= _manterStatusAte) _status.Text = "";
            _pilula.Text = online ? "Online" : "Offline";
            _pilula.Cor = online ? Tema.Sucesso : Tema.Alerta;
        });
    }

    /// <summary>Recalcula o atacado: soma as linhas de cada produto e aplica a faixa que a quantidade alcança.</summary>
    private void AplicarAtacado()
    {
        foreach (var grupo in _itens.Where(i => !i.PrecoAlterado && i.Faixas.Count > 0).GroupBy(i => i.ProdutoId))
        {
            var quantidade = grupo.Sum(i => i.Quantidade);
            foreach (var i in grupo)
            {
                var (preco, faixa) = Atacado.Calcular(i.PrecoVarejo, i.Faixas, quantidade);
                i.PrecoUnitario = preco;
                i.AtacadoMinimo = faixa?.Minimo;
                // "de R$ X" no cupom: o preço normal (antes de promoção e atacado).
                i.PrecoNormal = faixa is null ? i.PrecoNormalVarejo : Math.Max(i.PrecoNormalVarejo, i.PrecoVarejo);
            }
        }
    }

    private static string TextoAtacado(ProdutoLocal produto)
    {
        var faixa = produto.Faixas.Where(f => f.Preco < produto.PrecoVenda).OrderBy(f => f.Minimo).Cast<Atacado.Faixa?>().FirstOrDefault();
        return faixa is { } f ? $"  ·  ATACADO: a partir de {Cupom.Qtd(f.Minimo)} {produto.Unidade}, {Ui.Dinheiro(f.Preco)} cada" : "";
    }

    /// <summary>O meio da tela: a lista (com venda), as promoções (caixa livre) ou a venda concluída.</summary>
    private void AtualizarCentro()
    {
        var concluido = _mostrandoConcluido && _itens.Count == 0 && _pendente is null;
        var livre = !concluido && _itens.Count == 0 && _pendente is null && !_modoOrcamento;
        _concluido.Visible = concluido;
        _livre.Visible = livre;
        _cartaoItens.Visible = !concluido && !livre;
        _ultimo.Visible = _itens.Count > 0;
    }

    private void AtualizarTela()
    {
        AplicarAtacado();
        if (_descontoPercentual is { } p) _desconto = Math.Round(Subtotal * p / 100m, 2);
        if (_desconto >= Subtotal && _desconto > 0) { _desconto = 0; _descontoPercentual = null; }
        AtualizarCentro(); // antes da lista: com o primeiro item, a lista aparece no lugar das promoções

        _grade.Rows.Clear();
        var n = 1;
        foreach (var i in _itens)
        {
            var descricao = i.AtacadoMinimo is not null ? $"{i.Descricao}  ★ ATACADO"
                : i.PrecoNormal > 0 ? $"{i.Descricao}  ★ PROMOÇÃO"
                : i.PrecoAlterado ? $"{i.Descricao}  (preço alterado)" : i.Descricao;
            var r = _grade.Rows.Add(n++, i.Codigo, descricao, Cupom.Qtd(i.Quantidade), Ui.Dinheiro(i.PrecoUnitario), Ui.Dinheiro(i.Total));
            if (i.AtacadoMinimo is not null || i.PrecoNormal > 0) _grade.Rows[r].DefaultCellStyle.ForeColor = Tema.Sucesso;
            else if (i.PrecoAlterado) _grade.Rows[r].DefaultCellStyle.ForeColor = Tema.Alerta;
        }
        if (_grade.Rows.Count > 0 && _grade.Visible)
        {
            var ultima = _grade.Rows[_grade.Rows.Count - 1];
            try
            {
                _grade.CurrentCell = ultima.Cells[0];
                _grade.FirstDisplayedScrollingRowIndex = ultima.Index;
            }
            catch (InvalidOperationException) { /* lista sem espaço para rolar (tela minimizada): só não marca a linha */ }
        }

        // Último item passado, grande
        if (_itens.Count > 0)
        {
            var u = _itens[^1];
            var unidade = Formatos.UnidadeFracionada(u.Unidade) ? " " + u.Unidade.Trim().ToLowerInvariant() : "";
            _ultimo.Text = u.Descricao;
            _ultimo.Detalhe = $"{Cupom.Qtd(u.Quantidade)}{unidade} × {Ui.Dinheiro(u.PrecoUnitario)}";
            _ultimo.Valor = Ui.Dinheiro(u.Total);
            _ultimo.Etiqueta = u.AtacadoMinimo is not null ? "ATACADO" : u.PrecoNormal > 0 ? "PROMOÇÃO" : null;
        }

        var quantidadeItens = _itens.Count;
        _tituloItens.Text = (_modoOrcamento ? "Itens do orçamento" : "Itens da venda") + $" ({quantidadeItens})" +
                            (_orcamentoAtual is null ? "" : $"  ·  orçamento nº {_orcamentoAtual.Numero}");

        // Coluna: resumo
        _rotuloTotal.Text = "TOTAL A PAGAR";
        _total.Text = Ui.Dinheiro(Total);
        _lSubtotal.Valor = Ui.Dinheiro(Subtotal);
        _lDesconto.Valor = _desconto > 0
            ? "− " + Ui.Dinheiro(_desconto) + (_descontoPercentual is { } pc ? $" ({pc.ToString("0.##", Ui.Br)}%)" : "")
            : "—";
        _lItens.Valor = quantidadeItens.ToString(Ui.Br);
        _lCliente.Valor = _cliente?.Nome ?? "Consumidor final";
        var economia = Economia;
        _economia.Visible = economia > 0 && _itens.Count > 0;
        _economia.Valor = Ui.Dinheiro(economia);
        _economia.Detalhe = DetalheEconomia();
        _pagar.Enabled = _itens.Count > 0;

        // Coluna: orçamento
        _totalOrc.Text = Ui.Dinheiro(Total);
        _orcamentoInfo.Text = _orcamentoAtual is null
            ? "Orçamento novo. Ao salvar, ele recebe um número e vai para a Gestão."
            : $"Alterando o orçamento nº {_orcamentoAtual.Numero}.";

        if (_estado == EstadoColuna.Pagamento)
        {
            if (_formaEscolhida is not null && !_dividindo && _recebido.Valor < Total) _recebido.Valor = Total;
            AtualizarPagamento();
        }
        AtualizarCentro();
        AjustarTotais();
    }

    private void Limpar()
    {
        FecharPendente();
        _itens.Clear();
        _desconto = 0;
        _descontoPercentual = null;
        _cliente = null;
        _orcamentoAtual = null;
        _formaEscolhida = null;
        _parciais.Clear();
        _dividindo = false;
        foreach (var b in _botoesForma) b.Selecionado = false;
        _recebido.Valor = 0;
        _quantidade.Valor = 1;
        _observacaoOrcamento.Clear();
        _validadeDias.Valor = 7;
        AtualizarTela();
    }

    private void Focar(bool limpar = true)
    {
        if (limpar) _busca.Caixa.Clear();
        EsconderSugestoes();
        _busca.Caixa.Focus();
    }
}

/// <summary>Painel com cantos arredondados e cor própria (produto escolhido pelo nome).</summary>
internal sealed class PainelArredondado : Panel
{
    public PainelArredondado()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color Cor { get; set; } = Tema.PrimariaClara;

    protected override void OnPaint(PaintEventArgs e)
    {
        Tema.PintarFundoDoPai(e.Graphics, this);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var caminho = Tema.Arredondado(new Rectangle(0, 0, Width - 1, Height - 1), 10);
        using var fundo = new SolidBrush(Cor);
        e.Graphics.FillPath(fundo, caminho);
        base.OnPaint(e);
    }
}

internal static class ExtensoesControle
{
    /// <summary>Troca o Dock e devolve o próprio controle (para montar em uma linha).</summary>
    public static T ComDock<T>(this T controle, DockStyle dock) where T : Control
    {
        controle.Dock = dock;
        return controle;
    }
}
