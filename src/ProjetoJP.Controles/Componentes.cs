using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace ProjetoJP.Controles;

public enum EstiloBotao
{
    /// <summary>Azul cheio: a ação principal da tela (Salvar, Entrar, Pagamento).</summary>
    Primario,
    /// <summary>Branco com borda: ações comuns (Editar, Atualizar, Cancelar).</summary>
    Secundario,
    /// <summary>Azul-marinho cheio.</summary>
    Escuro,
    /// <summary>Vermelho: ações que desfazem ou apagam.</summary>
    Perigo,
    Sucesso,
    /// <summary>Sem fundo, para barras escuras (topo do caixa, menu lateral).</summary>
    NaBarra,
    /// <summary>Sem fundo e sem borda em superfície clara (barra superior: usuário, data); realça ao passar o mouse.</summary>
    Transparente
}

/// <summary>Botão plano com cantos arredondados, ícone opcional e realce ao passar o mouse.</summary>
public class BotaoTema : Button
{
    private bool _hover;
    private bool _pressionado;
    private EstiloBotao _estilo = EstiloBotao.Primario;
    private string? _icone;

    public BotaoTema()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
        Font = Tema.Semibold(10);
        Size = new Size(120, 36);
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public EstiloBotao Estilo
    {
        get => _estilo;
        set { _estilo = value; Invalidate(); }
    }

    /// <summary>Um caractere de <see cref="Icones"/>.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? Icone
    {
        get => _icone;
        set { _icone = value; Invalidate(); }
    }

    private string? _iconeDireita;

    /// <summary>Ícone depois do texto (ex.: <see cref="Icones.ChevronBaixo"/> em botão que abre um menu).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? IconeDireita
    {
        get => _iconeDireita;
        set { _iconeDireita = value; Invalidate(); }
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = _pressionado = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs mevent) { _pressionado = true; Invalidate(); base.OnMouseDown(mevent); }
    protected override void OnMouseUp(MouseEventArgs mevent) { _pressionado = false; Invalidate(); base.OnMouseUp(mevent); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    private (Color fundo, Color texto, Color borda) Cores()
    {
        var fundoPai = Parent?.BackColor ?? Tema.Fundo;
        if (!Enabled)
            return _estilo == EstiloBotao.NaBarra
                ? (fundoPai, Tema.BarraTextoSuave, Color.Empty)
                : (Tema.Borda, Tema.TextoSuave, Color.Empty);

        Color Ativo(Color normal) => _pressionado ? Tema.Escurecer(normal, 0.2f) : _hover ? Tema.Escurecer(normal) : normal;

        return _estilo switch
        {
            EstiloBotao.Secundario => (_hover || _pressionado ? Tema.Realce(Tema.Superficie) : Tema.Superficie, Tema.Texto, _hover ? Tema.Primaria : Tema.Borda),
            EstiloBotao.Escuro => (Ativo(Tema.Barra), Color.White, Color.Empty),
            EstiloBotao.Perigo => (Ativo(Tema.Perigo), Color.White, Color.Empty),
            EstiloBotao.Sucesso => (Ativo(Tema.Sucesso), Color.White, Color.Empty),
            EstiloBotao.NaBarra => (_hover || _pressionado ? Tema.BarraHover : fundoPai, Color.White, Color.Empty),
            EstiloBotao.Transparente => (_hover || _pressionado ? Tema.Realce(fundoPai, 0.05f) : fundoPai, Tema.Texto, Color.Empty),
            _ => (_pressionado ? Tema.Escurecer(Tema.PrimariaEscura) : _hover ? Tema.PrimariaEscura : Tema.Primaria, Color.White, Color.Empty)
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Tema.PintarFundoDoPai(g, this);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var (fundo, texto, borda) = Cores();
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var caminho = Tema.Arredondado(r, 6))
        {
            using (var pincel = new SolidBrush(fundo)) g.FillPath(pincel, caminho);
            if (borda != Color.Empty)
                using (var caneta = new Pen(borda)) g.DrawPath(caneta, caminho);
        }

        if (Focused && ShowFocusCues)
        {
            var foco = Rectangle.Inflate(r, -3, -3);
            using var caneta = new Pen(_estilo == EstiloBotao.Secundario ? Tema.Primaria : Color.FromArgb(160, Color.White)) { DashStyle = DashStyle.Dot };
            using var caminho = Tema.Arredondado(foco, 4);
            g.DrawPath(caneta, caminho);
        }

        const TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        var tamTexto = TextRenderer.MeasureText(g, Text, Font, Size.Empty, flags);
        var fonteIcone = Tema.FonteIcone(Font.Size + 1);
        var larguraIcone = string.IsNullOrEmpty(_icone) ? 0 : (int)(Font.Size * 1.9f);
        var espaco = larguraIcone > 0 && Text.Length > 0 ? 6 : 0;
        var larguraDireita = string.IsNullOrEmpty(_iconeDireita) ? 0 : (int)(Font.Size * 1.6f) + 4;
        var total = larguraIcone + espaco + tamTexto.Width + larguraDireita;
        var x = Math.Max(4, (Width - total) / 2);

        if (larguraIcone > 0)
            TextRenderer.DrawText(g, _icone, fonteIcone, new Rectangle(x, 0, larguraIcone, Height), texto,
                flags | TextFormatFlags.HorizontalCenter);
        var larguraTexto = Math.Max(0, Width - x - larguraIcone - espaco - larguraDireita - 4);
        if (Text.Length > 0)
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x + larguraIcone + espaco, 0, larguraTexto, Height),
                texto, flags | TextFormatFlags.EndEllipsis);
        if (larguraDireita > 0)
            TextRenderer.DrawText(g, _iconeDireita, Tema.FonteIcone(Math.Max(7f, Font.Size - 2)),
                new Rectangle(x + larguraIcone + espaco + Math.Min(tamTexto.Width, larguraTexto) + 4, 0, larguraDireita, Height),
                texto, flags | TextFormatFlags.HorizontalCenter);
    }
}

/// <summary>Bloco grande de atalho (modo simples da Retaguarda): ícone, título e uma linha de descrição.</summary>
public class BlocoAtalho : Button
{
    private bool _hover;

    public BlocoAtalho(string icone, string titulo, string descricao)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        IconeBloco = icone;
        Text = titulo;
        Descricao = descricao;
        Size = new Size(250, 132);
        Margin = new Padding(0, 0, 16, 16);
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string IconeBloco { get; set; }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Descricao { get; set; }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Tema.PintarFundoDoPai(g, this);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var caminho = Tema.Arredondado(r, 10))
        {
            using (var fundo = new SolidBrush(_hover && Enabled ? Tema.PrimariaClara : Tema.Superficie)) g.FillPath(fundo, caminho);
            using (var borda = new Pen(_hover && Enabled ? Tema.Primaria : Tema.Borda, _hover ? 1.5f : 1f)) g.DrawPath(borda, caminho);
        }

        // ícone num círculo azul-claro
        var circulo = new Rectangle(20, 20, 44, 44);
        using (var fundoIcone = new SolidBrush(_hover ? Tema.Superficie : Tema.PrimariaClara)) g.FillEllipse(fundoIcone, circulo);
        TextRenderer.DrawText(g, IconeBloco, Tema.FonteIcone(17), circulo, Enabled ? Tema.Primaria : Tema.TextoSuave,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        TextRenderer.DrawText(g, Text, Tema.Semibold(13), new Rectangle(20, 74, Width - 36, 26),
            Enabled ? Tema.Texto : Tema.TextoSuave, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, Descricao, Tema.Fonte(9), new Rectangle(20, 100, Width - 36, 22),
            Tema.TextoSuave, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

/// <summary>Painel branco com cantos arredondados e borda fina, com título opcional no topo.</summary>
public class CartaoPainel : Panel
{
    private string? _titulo;

    public CartaoPainel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Tema.Superficie;
        Padding = new Padding(16);
    }

    /// <summary>Com título, o conteúdo começa 48 px abaixo do topo (Padding.Top).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? Titulo
    {
        get => _titulo;
        set
        {
            _titulo = value;
            Padding = new Padding(16, string.IsNullOrEmpty(value) ? 16 : 48, 16, 16);
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Tema.PintarFundoDoPai(g, this);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var caminho = Tema.Arredondado(r, 8))
        {
            using (var fundo = new SolidBrush(BackColor)) g.FillPath(fundo, caminho);
            using (var borda = new Pen(Tema.Borda)) g.DrawPath(borda, caminho);
        }
        if (!string.IsNullOrEmpty(_titulo))
            TextRenderer.DrawText(g, _titulo, Tema.Semibold(11), new Point(16, 14), Tema.Texto, TextFormatFlags.NoPrefix);
        base.OnPaint(e);
    }
}

/// <summary>Cartão de indicador (painel inicial): título pequeno, valor grande e uma linha de detalhe.</summary>
public class Indicador : CartaoPainel
{
    private readonly string _icone;
    private readonly Color _cor;
    private readonly Label _valor = new() { AutoSize = true, Font = Tema.Semibold(20), ForeColor = Tema.Texto, Location = new Point(16, 38), Text = "—" };
    private readonly Label _detalhe = new() { AutoSize = true, Font = Tema.Fonte(9), ForeColor = Tema.TextoSuave, Location = new Point(18, 80) };
    private readonly string _rotulo;

    public Indicador(string rotulo, string icone, Color? cor = null)
    {
        _rotulo = rotulo;
        _icone = icone;
        _cor = cor ?? Tema.Primaria;
        Size = new Size(240, 112);
        Margin = new Padding(0, 0, 16, 16);
        Controls.Add(_valor);
        Controls.Add(_detalhe);
    }

    public void Definir(string valor, string? detalhe = null)
    {
        _valor.Text = valor;
        _detalhe.Text = detalhe ?? "";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        TextRenderer.DrawText(g, _rotulo, Tema.Fonte(9.5f), new Point(17, 16), Tema.TextoSuave, TextFormatFlags.NoPrefix);
        var circulo = new Rectangle(Width - 52, 14, 36, 36);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var fundo = new SolidBrush(Color.FromArgb(28, _cor))) g.FillEllipse(fundo, circulo);
        TextRenderer.DrawText(g, _icone, Tema.FonteIcone(14), circulo, _cor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>
/// Item de menu lateral: ícone + texto, com marca azul quando ativo.
/// Escuro (padrão) para o menu da Retaguarda; claro para a navegação de telas de opções.
/// </summary>
public class ItemMenu : Control
{
    private bool _hover;
    private bool _ativo;
    private readonly bool _claro;

    public ItemMenu(string icone, string texto, bool claro = false)
    {
        _claro = claro;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        IconeItem = icone;
        Text = texto;
        Height = 40;
        Dock = DockStyle.Top;
        Cursor = Cursors.Hand;
        BackColor = claro ? Tema.Fundo : Tema.Barra;
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string IconeItem { get; set; }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Ativo
    {
        get => _ativo;
        set { _ativo = value; Invalidate(); }
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space) OnClick(EventArgs.Empty);
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (_claro)
            g.Clear(_ativo ? Tema.PrimariaClara : _hover ? Tema.Realce(Tema.Fundo, 0.05f) : Tema.Fundo);
        else
            g.Clear(_ativo || _hover ? Tema.BarraHover : Tema.Barra);
        if (_ativo)
            using (var marca = new SolidBrush(Tema.Primaria)) g.FillRectangle(marca, 0, 6, 4, Height - 12);

        var cor = _claro
            ? (_ativo ? Tema.Primaria : Tema.Texto)
            : (_ativo ? Color.White : Tema.BarraTexto);
        TextRenderer.DrawText(g, IconeItem, Tema.FonteIcone(12), new Rectangle(18, 0, 24, Height), cor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(g, Text, _ativo ? Tema.Semibold(10) : Tema.Fonte(10), new Rectangle(52, 0, Width - 60, Height), cor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

/// <summary>Título de grupo no menu lateral (CADASTROS, ESTOQUE...).</summary>
public class GrupoMenu : Label
{
    public GrupoMenu(string texto)
    {
        Text = texto.ToUpperInvariant();
        Dock = DockStyle.Top;
        Height = 34;
        Padding = new Padding(20, 12, 0, 0);
        Font = Tema.Semibold(8);
        ForeColor = Tema.BarraTextoSuave;
        BackColor = Tema.Barra;
    }
}

/// <summary>Etiqueta arredondada de situação (Online, Offline...).</summary>
public class Pilula : Control
{
    private Color _cor = Tema.Sucesso;

    public Pilula()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        Font = Tema.Semibold(9);
        Height = 26;
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Cor
    {
        get => _cor;
        set { _cor = value; Invalidate(); }
    }

    protected override void OnTextChanged(EventArgs e)
    {
        Width = TextRenderer.MeasureText(Text, Font).Width + 34;
        Invalidate();
        base.OnTextChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Tema.PintarFundoDoPai(g, this);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var caminho = Tema.Arredondado(r, Height / 2))
        using (var fundo = new SolidBrush(Color.FromArgb(40, _cor)))
            g.FillPath(fundo, caminho);
        using (var ponto = new SolidBrush(_cor)) g.FillEllipse(ponto, 11, Height / 2 - 4, 8, 8);
        var corTexto = Parent?.BackColor.GetBrightness() < 0.4f ? Color.White : Tema.Texto;
        TextRenderer.DrawText(g, Text, Font, new Rectangle(24, 0, Width - 28, Height), corTexto,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

/// <summary>Cabeçalho de uma aba: título, ícone e botão de fechar.</summary>
public class CabecalhoAba : Control
{
    private bool _hover;
    private bool _hoverFechar;
    private bool _ativa;
    private readonly string? _icone;
    private readonly bool _fixa;

    public event EventHandler? Selecionada;
    public event EventHandler? FecharClicado;

    public CabecalhoAba(string titulo, string? icone, bool fixa)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Text = titulo;
        _icone = icone;
        _fixa = fixa;
        Font = Tema.Fonte(9.5f);
        Height = 34;
        Margin = new Padding(0, 0, 4, 0);
        Cursor = Cursors.Hand;
        Width = (_icone is null ? 16 : 40) + TextRenderer.MeasureText(titulo, Tema.Semibold(9.5f)).Width + (fixa ? 16 : 38);
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Ativa
    {
        get => _ativa;
        set { _ativa = value; Invalidate(); }
    }

    private Rectangle AreaFechar => new(Width - 28, (Height - 20) / 2, 20, 20);

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = _hoverFechar = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var sobre = !_fixa && AreaFechar.Contains(e.Location);
        if (sobre != _hoverFechar) { _hoverFechar = sobre; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_fixa && (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && AreaFechar.Contains(e.Location))))
            FecharClicado?.Invoke(this, EventArgs.Empty);
        else if (e.Button == MouseButtons.Left)
            Selecionada?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Tema.PintarFundoDoPai(g, this);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (_ativa || _hover)
        {
            var r = new Rectangle(0, 0, Width - 1, Height + 8); // cantos de baixo escondidos
            using var caminho = Tema.Arredondado(r, 6);
            using var fundo = new SolidBrush(_ativa ? Tema.Superficie : Tema.Realce(Tema.Fundo, 0.05f));
            g.FillPath(fundo, caminho);
            if (_ativa)
            {
                using var borda = new Pen(Tema.Borda);
                g.DrawPath(borda, caminho);
                using var topo = new SolidBrush(Tema.Primaria);
                g.FillRectangle(topo, 6, 0, Width - 12, 2);
            }
        }

        var cor = _ativa ? Tema.Texto : Tema.TextoSuave;
        var x = 12;
        if (_icone is not null)
        {
            TextRenderer.DrawText(g, _icone, Tema.FonteIcone(10), new Rectangle(x, 0, 20, Height), _ativa ? Tema.Primaria : cor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            x += 24;
        }
        TextRenderer.DrawText(g, Text, _ativa ? Tema.Semibold(9.5f) : Font, new Rectangle(x, 0, Width - x - (_fixa ? 8 : 30), Height), cor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        if (!_fixa)
        {
            var f = AreaFechar;
            if (_hoverFechar)
                using (var fundoX = new SolidBrush(Tema.Borda)) g.FillEllipse(fundoX, f);
            TextRenderer.DrawText(g, Icones.Cancelar, Tema.FonteIcone(7), f, _hoverFechar ? Tema.Texto : Tema.TextoSuave,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}

/// <summary>
/// Área com abas: cada tela (um Form) abre numa aba dentro da janela principal.
/// Abrir uma tela que já está aberta só muda para a aba dela.
/// </summary>
public class AreaAbas : Panel
{
    private sealed class Aba(string chave, CabecalhoAba cabecalho, Form tela)
    {
        public string Chave { get; } = chave;
        public CabecalhoAba Cabecalho { get; } = cabecalho;
        public Form Tela { get; } = tela;
    }

    private readonly FlowLayoutPanel _faixa = new()
    {
        Dock = DockStyle.Top,
        Height = 42,
        WrapContents = false,
        Padding = new Padding(16, 8, 0, 0),
        BackColor = Tema.Fundo
    };

    private readonly Panel _conteudo = new() { Dock = DockStyle.Fill, BackColor = Tema.Fundo };
    private readonly List<Aba> _abas = [];

    public AreaAbas()
    {
        BackColor = Tema.Fundo;
        Controls.Add(_conteudo);
        Controls.Add(_faixa);
        _faixa.Paint += (_, e) =>
        {
            using var linha = new Pen(Tema.Borda);
            e.Graphics.DrawLine(linha, 0, _faixa.Height - 1, _faixa.Width, _faixa.Height - 1);
        };
    }

    public void Abrir(string chave, string titulo, string? icone, Func<Form> criar, bool fixa = false)
    {
        var existente = _abas.FirstOrDefault(a => a.Chave == chave);
        if (existente is not null)
        {
            Ativar(existente);
            return;
        }

        var tela = criar();
        tela.TopLevel = false;
        tela.FormBorderStyle = FormBorderStyle.None;
        // Dentro da aba, o tamanho mínimo da janela cortaria a tela em monitores pequenos (1024×768): ela encolhe junto
        // e, o que for de posição fixa, ganha rolagem.
        tela.MinimumSize = Size.Empty;
        tela.AutoScroll = true;
        tela.Dock = DockStyle.Fill;

        var cabecalho = new CabecalhoAba(titulo, icone, fixa);
        var aba = new Aba(chave, cabecalho, tela);
        cabecalho.Selecionada += (_, _) => Ativar(aba);
        cabecalho.FecharClicado += (_, _) => tela.Close();
        tela.FormClosed += (_, _) => Remover(aba);

        _abas.Add(aba);
        _faixa.Controls.Add(cabecalho);
        _conteudo.Controls.Add(tela);
        tela.Show();
        Ativar(aba);
    }

    private void Ativar(Aba aba)
    {
        foreach (var a in _abas)
        {
            a.Cabecalho.Ativa = a == aba;
            a.Tela.Visible = a == aba;
        }
        aba.Tela.BringToFront();
        aba.Tela.Focus();
    }

    private void Remover(Aba aba)
    {
        var indice = _abas.IndexOf(aba);
        if (indice < 0) return;
        var eraAtiva = aba.Cabecalho.Ativa;
        _abas.RemoveAt(indice);
        _faixa.Controls.Remove(aba.Cabecalho);
        aba.Cabecalho.Dispose();
        _conteudo.Controls.Remove(aba.Tela);
        if (eraAtiva && _abas.Count > 0)
            Ativar(_abas[Math.Min(indice, _abas.Count - 1)]);
    }

    /// <summary>A tela já aberta com essa chave (null se não estiver aberta).</summary>
    public Form? Tela(string chave) => _abas.FirstOrDefault(a => a.Chave == chave)?.Tela;

    /// <summary>Fecha todas as abas (ao trocar de modo ou sair).</summary>
    public void FecharTodas()
    {
        foreach (var a in _abas.ToList()) a.Tela.Close();
    }
}
