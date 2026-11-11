using System.ComponentModel;
using System.Drawing.Drawing2D;
using ProjetoJP.Controles;

namespace ProjetoJP.Pdv.Telas;

/// <summary>Cores da coluna escura do caixa (total, pagamento, troco). A coluna usa a cor das barras do tema.</summary>
internal static class CoresCaixa
{
    /// <summary>Rótulos em destaque na coluna escura ("TOTAL A PAGAR", "FALTA").</summary>
    public static readonly Color Destaque = Color.FromArgb(248, 194, 92);
    public static readonly Color TrocoTexto = Color.FromArgb(134, 239, 172);
    public static readonly Color TrocoFundo = Color.FromArgb(40, 74, 222, 128);
    public static readonly Color EconomiaFundo = Color.FromArgb(31, 111, 74);
    public static readonly Color Linha = Color.FromArgb(38, 255, 255, 255);
    public static readonly Color BotaoFundo = Color.FromArgb(22, 255, 255, 255);
    public static readonly Color BotaoHover = Color.FromArgb(40, 255, 255, 255);
    public static readonly Color BotaoBorda = Color.FromArgb(44, 255, 255, 255);
}

/// <summary>Base dos controles desenhados do caixa: sem foco, sem piscar, fundo igual ao do pai.</summary>
internal abstract class ControlePintado : Control
{
    protected ControlePintado()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
    }

    protected override void OnTextChanged(EventArgs e)
    {
        Invalidate();
        base.OnTextChanged(e);
    }

    protected const TextFormatFlags Linha = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;

    /// <summary>A maior fonte (entre <paramref name="maximo"/> e <paramref name="minimo"/>) em que o texto cabe na largura.</summary>
    public static Font FonteQueCabe(Graphics? g, string texto, int largura, float maximo, float minimo, bool negrito = true)
    {
        for (var t = maximo; t > minimo; t -= 2f)
        {
            var f = negrito ? Tema.Semibold(t) : Tema.Fonte(t);
            var w = g is null ? TextRenderer.MeasureText(texto, f).Width : TextRenderer.MeasureText(g, texto, f, Size.Empty, TextFormatFlags.NoPadding).Width;
            if (w <= largura) return f;
        }
        return negrito ? Tema.Semibold(minimo) : Tema.Fonte(minimo);
    }
}

/// <summary>Coluna escura da direita, com o canto de cima arredondado (total, pagamento, troco).</summary>
internal sealed class PainelColuna : Panel
{
    public PainelColuna()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Tema.Barra;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Tema.Fundo);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        const int d = 52;
        using var p = new GraphicsPath();
        p.AddArc(0, 0, d, d, 180, 90);
        p.AddLine(d / 2, 0, Width, 0);
        p.AddLine(Width, 0, Width, Height);
        p.AddLine(Width, Height, 0, Height);
        p.CloseFigure();
        using var b = new SolidBrush(Tema.Barra);
        g.FillPath(b, p);
    }
}

/// <summary>Etiqueta arredondada da barra de cima (CAIXA 01, operador, hora, ORÇAMENTO).</summary>
internal sealed class PilulaTexto : ControlePintado
{
    private Color _fundo = Color.FromArgb(30, 255, 255, 255);
    private Color _cor = Color.White;

    public PilulaTexto()
    {
        Font = Tema.Fonte(10.5f);
        Height = 32;
        Margin = new Padding(8, 0, 0, 0);
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color CorFundo { get => _fundo; set { _fundo = value; Invalidate(); } }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color CorTexto { get => _cor; set { _cor = value; Invalidate(); } }

    protected override void OnTextChanged(EventArgs e)
    {
        Width = TextRenderer.MeasureText(Text, Font).Width + 26;
        base.OnTextChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Tema.PintarFundoDoPai(g, this);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var caminho = Tema.Arredondado(new Rectangle(0, 0, Width - 1, Height - 1), (Height - 1) / 2))
        using (var fundo = new SolidBrush(_fundo))
            g.FillPath(fundo, caminho);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, _cor, Linha | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>Atalho do rodapé: a tecla desenhada como uma teclinha e o nome da ação. Clicável.</summary>
internal sealed class DicaTecla : ControlePintado
{
    private readonly string _tecla;

    public DicaTecla(string tecla, string texto)
    {
        _tecla = tecla;
        Text = texto;
        Font = Tema.Fonte(10);
        Cursor = Cursors.Hand;
        Height = 30;
        Margin = new Padding(0, 5, 10, 0);
        Width = LarguraTecla() + 8 + TextRenderer.MeasureText(texto, Font).Width + 6;
    }

    private int LarguraTecla() => Math.Max(30, TextRenderer.MeasureText(_tecla, Tema.Semibold(8.5f)).Width + 12);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Tema.PintarFundoDoPai(g, this);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var wTecla = LarguraTecla();
        var r = new Rectangle(0, 4, wTecla, Height - 9);
        using (var caminho = Tema.Arredondado(r, 4))
        {
            using (var fundo = new SolidBrush(Tema.Superficie)) g.FillPath(fundo, caminho);
            using (var borda = new Pen(Tema.Borda)) g.DrawPath(borda, caminho);
        }
        using (var baixo = new Pen(Tema.Borda, 2f)) g.DrawLine(baixo, r.X + 3, r.Bottom, r.Right - 3, r.Bottom);
        TextRenderer.DrawText(g, _tecla, Tema.Semibold(8.5f), r, Tema.Texto, Linha | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(wTecla + 8, 0, Width - wTecla - 8, Height), Tema.TextoSuave, Linha | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>
/// Teclas do rodapé ("F1 Mais opções", "F2 Quantidade"...) desenhadas num controle só e clicáveis. Mostra da
/// esquerda para a direita só as que cabem inteiras (nunca corta uma no meio).
/// </summary>
internal sealed class FaixaTeclas : ControlePintado
{
    private readonly List<(string tecla, string texto, Action acao)> _itens = [];
    private readonly List<(Rectangle area, Action acao)> _areas = [];
    private int _maximo = int.MaxValue;

    public FaixaTeclas()
    {
        Font = Tema.Fonte(10);
        BackColor = Tema.Superficie;
    }

    public void Adicionar(string tecla, string texto, Action acao)
    {
        _itens.Add((tecla, texto, acao));
        Invalidate();
    }

    /// <summary>Quantas teclas mostrar no máximo (telas menores mostram só as primeiras).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int MaximoVisiveis
    {
        get => _maximo;
        set
        {
            if (_maximo == value) return;
            _maximo = value;
            Invalidate();
        }
    }

    private static Font FonteTecla => Tema.Semibold(8.5f);

    /// <summary>Largura para mostrar todas as teclas (até o máximo) sem cortar nenhuma.</summary>
    public int LarguraPreferida
    {
        get
        {
            var x = 14;
            for (var i = 0; i < _itens.Count && i < _maximo; i++)
            {
                var (tecla, texto, _) = _itens[i];
                var wTecla = Math.Max(30, TextRenderer.MeasureText(tecla, FonteTecla).Width + 12);
                x += wTecla + 8 + TextRenderer.MeasureText(texto, Font).Width + 18;
            }
            return x + 4;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        _areas.Clear();
        const int altura = 30;
        var y = Math.Max(0, (Height - altura) / 2);
        var x = 14;
        for (var i = 0; i < _itens.Count && i < _maximo; i++)
        {
            var (tecla, texto, acao) = _itens[i];
            var wTecla = Math.Max(30, TextRenderer.MeasureText(g, tecla, FonteTecla).Width + 12);
            var wTexto = TextRenderer.MeasureText(g, texto, Font).Width;
            var largura = wTecla + 8 + wTexto;
            if (x + largura > Width - 4) break; // não cabe inteira: para aqui
            var r = new Rectangle(x, y + 4, wTecla, altura - 9);
            // Tecla com fundo (parece uma tecla de verdade) e o nome da ação em cor de texto normal.
            using (var caminho = Tema.Arredondado(r, 4))
            {
                using (var fundo = new SolidBrush(Tema.Fundo)) g.FillPath(fundo, caminho);
                using (var borda = new Pen(Tema.Borda)) g.DrawPath(borda, caminho);
            }
            using (var baixo = new Pen(Tema.Borda, 2f)) g.DrawLine(baixo, r.X + 2, r.Bottom, r.Right - 2, r.Bottom);
            TextRenderer.DrawText(g, tecla, FonteTecla, r, Tema.Texto, Linha | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, texto, Font, new Rectangle(x + wTecla + 8, y, wTexto + 4, altura), Tema.Texto,
                Linha | TextFormatFlags.VerticalCenter);
            _areas.Add((new Rectangle(x, y, largura, altura), acao));
            x += largura + 18;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var sobre = _areas.Any(a => a.area.Contains(e.Location));
        var cursor = sobre ? Cursors.Hand : Cursors.Default;
        if (Cursor != cursor) Cursor = cursor;
        base.OnMouseMove(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) return;
        foreach (var (area, acao) in _areas.ToArray())
        {
            if (!area.Contains(e.Location)) continue;
            acao();
            return;
        }
    }
}

internal enum TipoEtiqueta { Ok, Alerta, Erro, Neutro }

/// <summary>Uma etiqueta do rodapé: texto (e um curto para telas menores), cor, dica do mouse e o que faz ao clicar.</summary>
internal sealed record Etiqueta(string Texto, TipoEtiqueta Tipo, string? Dica = null, Action? Clique = null,
    bool Impressora = false, string? TextoCurto = null);

/// <summary>
/// Situação do caixa em etiquetas coloridas no rodapé: sincronização (verde = tudo enviado, amarelo = aguardando
/// envio, vermelho = sem conexão ou vendas recusadas) e a impressora. A largura acompanha o conteúdo.
/// </summary>
internal sealed class EtiquetasSituacao : ControlePintado
{
    private List<Etiqueta> _sincronizacao = [];
    private Etiqueta? _impressora;
    private bool _compacto;
    private readonly List<(Rectangle area, Etiqueta etiqueta)> _areas = [];
    private readonly ToolTip _dica = new() { InitialDelay = 300 };
    private string? _dicaAtual;

    public EtiquetasSituacao()
    {
        Font = Tema.Fonte(9);
        BackColor = Tema.Superficie;
    }

    public void DefinirSincronizacao(IEnumerable<Etiqueta> etiquetas)
    {
        _sincronizacao = etiquetas.ToList();
        Atualizar();
    }

    public void DefinirImpressora(Etiqueta? etiqueta)
    {
        _impressora = etiqueta;
        Atualizar();
    }

    /// <summary>Telas menores: textos curtos (a impressora fica só com o ícone).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Compacto
    {
        get => _compacto;
        set
        {
            if (_compacto == value) return;
            _compacto = value;
            Atualizar();
        }
    }

    private IEnumerable<Etiqueta> Todas => _impressora is null ? _sincronizacao : _sincronizacao.Append(_impressora);

    private string TextoDe(Etiqueta e) => _compacto && e.TextoCurto is not null ? e.TextoCurto : e.Texto;

    private int LarguraDe(Etiqueta e)
    {
        var texto = TextoDe(e);
        var marca = e.Impressora ? 15 : 8; // ícone da impressora ou bolinha
        var miolo = texto.Length == 0 ? marca : marca + 6 + TextRenderer.MeasureText(texto, Font).Width;
        return 10 + miolo + 10;
    }

    public int LarguraPreferida => Todas.Sum(e => LarguraDe(e) + 8) + 8;

    private void Atualizar()
    {
        var largura = LarguraPreferida;
        if (Width != largura) Width = largura; // o rodapé se reorganiza sozinho
        Invalidate();
    }

    private (Color fundo, Color texto) Cores(TipoEtiqueta tipo) => tipo switch
    {
        TipoEtiqueta.Ok => (Tema.SucessoClaro, Tema.Sucesso),
        TipoEtiqueta.Alerta => (Tema.AlertaClaro, ControlPaint.Dark(Tema.Alerta, 0.25f)),
        TipoEtiqueta.Erro => (Tema.PerigoClaro, Tema.Perigo),
        _ => (Tema.Fundo, Tema.TextoSuave)
    };

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        _areas.Clear();
        const int altura = 26;
        var y = Math.Max(0, (Height - altura) / 2);
        var x = 8;
        foreach (var etiqueta in Todas)
        {
            var largura = LarguraDe(etiqueta);
            var area = new Rectangle(x, y, largura, altura);
            var (fundo, cor) = Cores(etiqueta.Tipo);
            using (var caminho = Tema.Arredondado(area, altura / 2))
            using (var pincel = new SolidBrush(fundo))
                g.FillPath(pincel, caminho);

            var cx = x + 10;
            if (etiqueta.Impressora)
            {
                using var caneta = new Pen(cor, 1.4f);
                var topo = y + 6;
                g.DrawRectangle(caneta, cx + 3, topo, 9, 4);          // papel
                g.DrawRectangle(caneta, cx, topo + 4, 15, 6);         // corpo
                g.DrawRectangle(caneta, cx + 3, topo + 8, 9, 5);      // saída
                cx += 15;
            }
            else
            {
                using var bolinha = new SolidBrush(cor);
                g.FillEllipse(bolinha, cx, y + altura / 2 - 4, 8, 8);
                cx += 8;
            }
            var texto = TextoDe(etiqueta);
            if (texto.Length > 0)
                TextRenderer.DrawText(g, texto, Font, new Rectangle(cx + 6, y, area.Right - cx - 6, altura), cor,
                    Linha | TextFormatFlags.VerticalCenter);
            _areas.Add((area, etiqueta));
            x += largura + 8;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var sobre = _areas.FirstOrDefault(a => a.area.Contains(e.Location)).etiqueta;
        var cursor = sobre?.Clique is not null ? Cursors.Hand : Cursors.Default;
        if (Cursor != cursor) Cursor = cursor;
        var dica = sobre is null ? null : sobre.Dica ?? TextoDe(sobre);
        if (dica != _dicaAtual)
        {
            _dicaAtual = dica;
            _dica.SetToolTip(this, dica ?? "");
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _dicaAtual = null;
        _dica.SetToolTip(this, "");
        base.OnMouseLeave(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) return;
        var alvo = _areas.ToArray().FirstOrDefault(a => a.area.Contains(e.Location)).etiqueta;
        alvo?.Clique?.Invoke();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _dica.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Rótulo de uma linha só: texto comprido termina em "…" (o texto inteiro fica na dica do mouse).</summary>
internal sealed class RotuloUmaLinha : Label
{
    public RotuloUmaLinha()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        Invalidate();
        base.OnTextChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        var direita = TextAlign is ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight;
        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
                    TextFormatFlags.VerticalCenter | (direita ? TextFormatFlags.Right : TextFormatFlags.Left);
        var area = new Rectangle(Padding.Left, Padding.Top, Math.Max(0, Width - Padding.Horizontal), Math.Max(0, Height - Padding.Vertical));
        TextRenderer.DrawText(e.Graphics, Text, Font, area, ForeColor, flags);
    }
}

/// <summary>Linha "rótulo .......... valor" com um traço fino em cima (subtotal, desconto, itens, cliente).</summary>
internal sealed class LinhaValor : ControlePintado
{
    private string _valor = "";
    private Color _corValor = Color.White;

    public LinhaValor(string rotulo)
    {
        Text = rotulo;
        Height = 44;
        Dock = DockStyle.Top;
        Font = Tema.Fonte(12);
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Valor { get => _valor; set { _valor = value; Invalidate(); } }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color CorValor { get => _corValor; set { _corValor = value; Invalidate(); } }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Tema.PintarFundoDoPai(g, this);
        using (var traco = new Pen(CoresCaixa.Linha)) g.DrawLine(traco, 0, 0, Width, 0);
        var wRotulo = Math.Min(Width / 2, TextRenderer.MeasureText(g, Text, Font).Width + 8);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, wRotulo, Height), Tema.BarraTexto, Linha | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, _valor, Tema.Semibold(12), new Rectangle(wRotulo, 0, Width - wRotulo, Height), _corValor,
            Linha | TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
    }
}

/// <summary>Faixa arredondada com rótulo e valor (troco, economia, falta).</summary>
internal sealed class FaixaValor : ControlePintado
{
    private string _rotulo = "";
    private string _valor = "";
    private string _detalhe = "";
    private Color _fundo = CoresCaixa.TrocoFundo;
    private Color _cor = CoresCaixa.TrocoTexto;
    private bool _icone;
    private bool _rotuloEmCima;

    public FaixaValor()
    {
        Dock = DockStyle.Top;
        Height = 58;
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Rotulo { get => _rotulo; set { _rotulo = value; Invalidate(); } }
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Valor { get => _valor; set { _valor = value; Invalidate(); } }
    /// <summary>Texto pequeno à direita (ex.: "atacado + promoção").</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Detalhe { get => _detalhe; set { _detalhe = value; Invalidate(); } }
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color CorFundo { get => _fundo; set { _fundo = value; Invalidate(); } }
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color CorTexto { get => _cor; set { _cor = value; Invalidate(); } }
    /// <summary>Bolinha com ✓ à esquerda.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ComIcone { get => _icone; set { _icone = value; Invalidate(); } }
    /// <summary>Rótulo pequeno em cima do valor (em vez de rótulo à esquerda e valor à direita).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool RotuloEmCima { get => _rotuloEmCima; set { _rotuloEmCima = value; Invalidate(); } }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Tema.PintarFundoDoPai(g, this);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var caminho = Tema.Arredondado(r, 12))
        using (var fundo = new SolidBrush(_fundo))
            g.FillPath(fundo, caminho);

        var x = 14;
        if (_icone)
        {
            var d = Math.Min(34, Height - 16);
            var c = new Rectangle(x, (Height - d) / 2, d, d);
            using (var bola = new SolidBrush(Color.FromArgb(74, 222, 128))) g.FillEllipse(bola, c);
            TextRenderer.DrawText(g, Icones.Ok, Tema.FonteIcone(d * 0.36f), c, Color.FromArgb(20, 83, 45),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            x += d + 10;
        }
        var wDetalhe = _detalhe.Length == 0 ? 0 : Math.Min((Width - x) / 2, TextRenderer.MeasureText(g, _detalhe, Tema.Fonte(8.5f)).Width + 10);
        var area = new Rectangle(x, 0, Width - x - 12 - wDetalhe, Height);
        if (_rotuloEmCima)
        {
            TextRenderer.DrawText(g, _rotulo, Tema.Semibold(8.5f), new Rectangle(area.X, area.Y + 8, area.Width, 18), Color.FromArgb(200, _cor), Linha);
            var f = FonteQueCabe(g, _valor, area.Width, 20, 11);
            TextRenderer.DrawText(g, _valor, f, new Rectangle(area.X, area.Y + 24, area.Width, Height - 28), _cor, Linha | TextFormatFlags.VerticalCenter);
        }
        else
        {
            var wRotulo = Math.Min(area.Width / 2, TextRenderer.MeasureText(g, _rotulo, Tema.Semibold(12)).Width + 8);
            TextRenderer.DrawText(g, _rotulo, Tema.Semibold(12), new Rectangle(area.X, 0, wRotulo, Height), _cor, Linha | TextFormatFlags.VerticalCenter);
            var f = FonteQueCabe(g, _valor, area.Width - wRotulo, 24, 11);
            TextRenderer.DrawText(g, _valor, f, new Rectangle(area.X + wRotulo, 0, area.Width - wRotulo, Height), _cor,
                Linha | TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }
        if (wDetalhe > 0)
            TextRenderer.DrawText(g, _detalhe, Tema.Fonte(8.5f), new Rectangle(Width - 12 - wDetalhe, 0, wDetalhe, Height), Color.FromArgb(210, _cor),
                TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>Botão da coluna escura (forma de pagamento, nota rápida): fundo translúcido, branco quando escolhido, tecla à direita.</summary>
internal sealed class BotaoColuna : Button
{
    private bool _hover;
    private bool _selecionado;
    private string _tecla = "";
    private Color _corSelecionado = Color.White;

    public BotaoColuna()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        TabStop = false;
        Cursor = Cursors.Hand;
        Font = Tema.Semibold(11);
        Margin = new Padding(0);
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selecionado { get => _selecionado; set { _selecionado = value; Invalidate(); } }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Tecla { get => _tecla; set { _tecla = value; Invalidate(); } }

    /// <summary>Fundo quando escolhido (branco nas formas; amarelo nas notas rápidas).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color CorSelecionado { get => _corSelecionado; set { _corSelecionado = value; Invalidate(); } }

    /// <summary>Forma de pagamento ligada ao botão (null = "Outros").</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public object? Dado { get; set; }

    protected override bool ShowFocusCues => false;
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        g.Clear(Parent?.BackColor ?? Tema.Barra);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        var fundo = _selecionado ? _corSelecionado : _hover ? CoresCaixa.BotaoHover : CoresCaixa.BotaoFundo;
        var texto = _selecionado ? Tema.Barra : Color.White;
        using (var caminho = Tema.Arredondado(r, 10))
        {
            using (var b = new SolidBrush(fundo)) g.FillPath(b, caminho);
            if (!_selecionado) using (var p = new Pen(CoresCaixa.BotaoBorda)) g.DrawPath(p, caminho);
        }
        var wTecla = 0;
        if (_tecla.Length > 0)
        {
            var ft = Tema.Semibold(8f);
            wTecla = TextRenderer.MeasureText(g, _tecla, ft).Width + 8;
            var rt = new Rectangle(Width - wTecla - 10, (Height - 22) / 2, wTecla, 22);
            using (var caminho = Tema.Arredondado(rt, 4))
            using (var p = new Pen(_selecionado ? Tema.Borda : Color.FromArgb(90, 255, 255, 255)))
                g.DrawPath(p, caminho);
            TextRenderer.DrawText(g, _tecla, ft, rt, _selecionado ? Tema.TextoSuave : Tema.BarraTexto,
                TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            wTecla += 14;
        }
        var alinhamento = _tecla.Length > 0 ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter;
        TextRenderer.DrawText(g, Text, Font, new Rectangle(14, 0, Width - 18 - wTecla, Height), texto,
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | alinhamento);
    }
}

/// <summary>Cartão "ÚLTIMO ITEM": o produto que acabou de passar, grande, com quantidade × preço e o total.</summary>
internal sealed class CartaoUltimoItem : ControlePintado
{
    private string _detalhe = "";
    private string _valor = "";
    private string? _etiqueta;

    public CartaoUltimoItem()
    {
        Dock = DockStyle.Top;
        Height = 84;
        BackColor = Tema.Fundo;
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Detalhe { get => _detalhe; set { _detalhe = value; Invalidate(); } }
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Valor { get => _valor; set { _valor = value; Invalidate(); } }
    /// <summary>"ATACADO", "PROMOÇÃO" ou null.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? Etiqueta { get => _etiqueta; set { _etiqueta = value; Invalidate(); } }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Tema.PintarFundoDoPai(g, this);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 10);
        using (var caminho = Tema.Arredondado(r, 14))
        {
            using (var fundo = new SolidBrush(Tema.PrimariaClara)) g.FillPath(fundo, caminho);
            var clip = g.Clip;
            g.SetClip(caminho);
            using (var barra = new SolidBrush(Tema.Primaria)) g.FillRectangle(barra, 0, 0, 6, r.Height + 1);
            g.Clip = clip;
        }
        var fValor = Tema.Semibold(20);
        var wValor = Math.Max(TextRenderer.MeasureText(g, _valor, fValor).Width, TextRenderer.MeasureText(g, _detalhe, Tema.Fonte(11)).Width) + 10;
        wValor = Math.Min(wValor, r.Width / 2);
        TextRenderer.DrawText(g, _detalhe, Tema.Fonte(11), new Rectangle(r.Right - wValor - 16, 8, wValor, 24), Tema.TextoSuave, Linha | TextFormatFlags.Right);
        TextRenderer.DrawText(g, _valor, fValor, new Rectangle(r.Right - wValor - 16, 30, wValor, r.Height - 34), Tema.Texto, Linha | TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

        var xTexto = 24;
        var areaNome = r.Width - wValor - 24 - xTexto;
        TextRenderer.DrawText(g, "ÚLTIMO ITEM", Tema.Semibold(8.5f), new Point(xTexto, 14), Tema.Primaria, TextFormatFlags.NoPrefix);
        if (!string.IsNullOrEmpty(_etiqueta))
        {
            var wRot = TextRenderer.MeasureText(g, "ÚLTIMO ITEM", Tema.Semibold(8.5f)).Width;
            var fe = Tema.Semibold(8f);
            var we = TextRenderer.MeasureText(g, _etiqueta, fe).Width + 14;
            var re = new Rectangle(xTexto + wRot + 8, 11, we, 20);
            var cor = _etiqueta == "ATACADO" ? Tema.Sucesso : Tema.Alerta;
            using (var caminho = Tema.Arredondado(re, 9))
            using (var fundo = new SolidBrush(Color.FromArgb(40, cor)))
                g.FillPath(fundo, caminho);
            TextRenderer.DrawText(g, _etiqueta, fe, re, cor, TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        var fNome = FonteQueCabe(g, Text, areaNome, 17, 11);
        TextRenderer.DrawText(g, Text, fNome, new Rectangle(xTexto, 32, Math.Max(10, areaNome), r.Height - 36), Tema.Texto, Linha | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>Tela de "Venda concluída" no meio do caixa (fica até passar o próximo produto).</summary>
internal sealed class PainelConcluido : ControlePintado
{
    private string _mensagem = "";
    private string? _economia;
    private bool _alerta;

    public PainelConcluido()
    {
        BackColor = Tema.Fundo;
        Text = "Venda concluída";
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Mensagem { get => _mensagem; set { _mensagem = value; Invalidate(); } }
    /// <summary>"Você economizou R$ 7,90 hoje" (null = sem economia).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? Economia { get => _economia; set { _economia = value; Invalidate(); } }
    /// <summary>Cupom não saiu: mensagem em laranja.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Alerta { get => _alerta; set { _alerta = value; Invalidate(); } }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Tema.PintarFundoDoPai(g, this);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var caminho = Tema.Arredondado(r, 18))
        {
            using (var fundo = new SolidBrush(Tema.Superficie)) g.FillPath(fundo, caminho);
            using (var borda = new Pen(Tema.Borda)) g.DrawPath(borda, caminho);
        }
        var d = Math.Clamp(Height / 5, 60, 110);
        var alturaTotal = d + 20 + 46 + 32 + (_economia is null ? 0 : 66) + 34;
        var y = Math.Max(10, (Height - alturaTotal) / 2);
        var c = new Rectangle((Width - d) / 2, y, d, d);
        using (var bola = new SolidBrush(Tema.SucessoClaro)) g.FillEllipse(bola, c);
        TextRenderer.DrawText(g, Icones.Ok, Tema.FonteIcone(d * 0.4f), c, Tema.Sucesso, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        y += d + 20;
        const TextFormatFlags centro = TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        TextRenderer.DrawText(g, Text, Tema.Semibold(24), new Rectangle(10, y, Width - 20, 46), Tema.Texto, centro | TextFormatFlags.VerticalCenter);
        y += 46;
        TextRenderer.DrawText(g, _mensagem, Tema.Fonte(13), new Rectangle(10, y, Width - 20, 32), _alerta ? Tema.Alerta : Tema.TextoSuave, centro | TextFormatFlags.VerticalCenter);
        y += 40;
        if (_economia is not null)
        {
            var f = Tema.Semibold(16);
            var w = Math.Min(Width - 40, TextRenderer.MeasureText(g, _economia, f).Width + 48);
            var re = new Rectangle((Width - w) / 2, y, w, 52);
            using (var caminho = Tema.Arredondado(re, 14))
            using (var fundo = new SolidBrush(Tema.SucessoClaro))
                g.FillPath(fundo, caminho);
            TextRenderer.DrawText(g, _economia, f, re, Tema.Sucesso, centro | TextFormatFlags.VerticalCenter);
            y += 66;
        }
        TextRenderer.DrawText(g, "Passe o próximo produto para começar outra venda", Tema.Fonte(10.5f), new Rectangle(10, y, Width - 20, 26),
            Tema.TextoSuave, centro | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>Barra de cima do caixa: logo grande, nome da loja e endereço (do cabeçalho do cupom); à direita, as etiquetas.</summary>
internal static class TopoCaixa
{
    public static Panel Montar(PdvConfig cfg, out FlowLayoutPanel direita)
    {
        var topo = new Panel { Dock = DockStyle.Top, Height = 88, BackColor = Tema.Barra };
        Control marca;
        if (Aparencia.Logo is not null)
            marca = new PictureBox { Image = Aparencia.Logo, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent, Size = new Size(64, 64), Location = new Point(20, 12) };
        else if (Ui.IconeApp is { } icone)
            marca = new PictureBox { Image = icone.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent, Size = new Size(56, 56), Location = new Point(24, 16) };
        else
            marca = new Label { Text = Icones.Carrinho, Font = Tema.FonteIcone(30), ForeColor = Color.White, BackColor = Color.Transparent, AutoSize = false, Size = new Size(64, 64), Location = new Point(20, 12), TextAlign = ContentAlignment.MiddleCenter };
        var endereco = LinhaEndereco(cfg);
        var nome = new Label
        {
            Text = cfg.NomeLoja.ToUpper(Ui.Br), Font = Tema.Semibold(19), ForeColor = Color.White, BackColor = Color.Transparent,
            AutoSize = false, AutoEllipsis = true, Location = new Point(98, endereco.Length > 0 ? 12 : 24), Size = new Size(500, 38)
        };
        var linha = new Label
        {
            Text = endereco, Font = Tema.Fonte(10.5f), ForeColor = Tema.BarraTextoSuave, BackColor = Color.Transparent,
            AutoSize = false, AutoEllipsis = true, Location = new Point(100, 50), Size = new Size(500, 24), Visible = endereco.Length > 0
        };
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
            FlowDirection = FlowDirection.RightToLeft, BackColor = Tema.Barra, Padding = new Padding(0, 28, 18, 0)
        };
        direita = flow;
        topo.Controls.AddRange([marca, nome, linha, flow]);
        void Ajustar()
        {
            var largura = Math.Max(80, flow.Left - nome.Left - 16);
            nome.Width = largura;
            linha.Width = largura;
        }
        topo.Resize += (_, _) => Ajustar();
        flow.SizeChanged += (_, _) => Ajustar();
        return topo;
    }

    /// <summary>Até duas linhas do cabeçalho do cupom (endereço, telefone) numa linha só, sem repetir o nome da loja.</summary>
    private static string LinhaEndereco(PdvConfig cfg) =>
        string.Join("  ·  ", cfg.CabecalhoCupom
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.Equals(cfg.NomeLoja.Trim(), StringComparison.OrdinalIgnoreCase))
            .Take(2));
}
