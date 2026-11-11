using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace ProjetoJP.Controles;

// Componentes do visual 2026 (menu azul-marinho com grupos, cartões de resumo, campo de busca e gráfico).
// Tudo desenhado com GDI+ do próprio Windows: nada de navegador embutido, leve para PC de 4 GB.

/// <summary>Campo de busca arredondado com lupa (o texto fica em <see cref="Caixa"/>).</summary>
public sealed class CampoBusca : Panel
{
    public TextBox Caixa { get; } = new() { BorderStyle = BorderStyle.None, MaxLength = 150 };

    public CampoBusca()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Tema.Superficie;
        Height = 38;
        Cursor = Cursors.IBeam;
        Caixa.Font = Tema.Fonte(10.5f);
        Caixa.BackColor = Tema.Superficie;
        Caixa.ForeColor = Tema.Texto;
        Caixa.GotFocus += (_, _) => Invalidate();
        Caixa.LostFocus += (_, _) => Invalidate();
        Controls.Add(Caixa);
    }

    /// <summary>Texto cinza quando vazio.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Dica
    {
        get => Caixa.PlaceholderText;
        set => Caixa.PlaceholderText = value;
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        Caixa.SetBounds(38, Math.Max(0, (Height - Caixa.PreferredHeight) / 2), Math.Max(20, Width - 48), Caixa.PreferredHeight);
    }

    protected override void OnClick(EventArgs e)
    {
        Caixa.Focus();
        base.OnClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Tema.PintarFundoDoPai(g, this);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var caminho = Tema.Arredondado(r, 7))
        {
            using (var fundo = new SolidBrush(Tema.Superficie)) g.FillPath(fundo, caminho);
            using (var borda = new Pen(Caixa.Focused ? Tema.Primaria : Tema.Borda, Caixa.Focused ? 1.5f : 1f)) g.DrawPath(borda, caminho);
        }
        TextRenderer.DrawText(g, Icones.Buscar, Tema.FonteIcone(11), new Rectangle(10, 0, 22, Height), Tema.TextoSuave,
            TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
    }
}

/// <summary>Cartão de resumo (KPI): ícone colorido, título, valor grande e uma linha de detalhe colorida.</summary>
public sealed class CartaoResumo : Control
{
    private readonly string _rotulo;
    private readonly string _icone;
    private readonly Color _cor;
    private string _valor = "—";
    private string _detalhe = "";
    private Color _corDetalhe = Tema.TextoSuave;
    private bool _hover;

    public CartaoResumo(string rotulo, string icone, Color cor)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        _rotulo = rotulo;
        _icone = icone;
        _cor = cor;
        Height = 100;
        BackColor = Tema.Fundo;
    }

    /// <summary>
    /// Versão pequena (painel de detalhes): ícone e título na primeira linha, valor embaixo ocupando a largura toda,
    /// fundo branco com borda.
    /// </summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Compacto { get; set; }

    /// <summary>true = o cartão abre algo ao clicar (cursor de mão e realce).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Clicavel
    {
        get => Cursor == Cursors.Hand;
        set => Cursor = value ? Cursors.Hand : Cursors.Default;
    }

    public void Definir(string valor, string? detalhe = null, Color? corDetalhe = null)
    {
        _valor = valor;
        _detalhe = detalhe ?? "";
        _corDetalhe = corDetalhe ?? Tema.TextoSuave;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Tema.PintarFundoDoPai(g, this);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        var fundo = Compacto ? Tema.Superficie : Tema.Misturar(_cor, Tema.Superficie, Tema.ModoEscuro ? 0.16f : 0.07f);
        var borda = Compacto ? Tema.Borda : Tema.Misturar(_cor, Tema.Superficie, _hover && Clicavel ? 0.45f : 0.18f);
        using (var caminho = Tema.Arredondado(r, Compacto ? 8 : 10))
        {
            using (var b = new SolidBrush(fundo)) g.FillPath(b, caminho);
            using (var p = new Pen(borda)) g.DrawPath(p, caminho);
        }

        var escala = DeviceDpi / 96f;
        int Px(int v) => (int)(v * escala);
        if (Compacto)
        {
            const TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
            var fTitulo = Tema.Fonte(8.5f);
            var fValorC = Tema.Semibold(12.5f);
            var linha1 = new Rectangle(Px(10), Px(8), Width - Px(20), fTitulo.Height + Px(4));
            TextRenderer.DrawText(g, _icone, Tema.FonteIcone(12), new Rectangle(linha1.X, linha1.Y, Px(20), linha1.Height), _cor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, _rotulo, fTitulo, new Rectangle(linha1.X + Px(24), linha1.Y, linha1.Width - Px(24), linha1.Height),
                Tema.TextoSuave, flags);
            TextRenderer.DrawText(g, _valor, fValorC, new Rectangle(Px(10), linha1.Bottom + Px(2), Width - Px(20), fValorC.Height + Px(6)),
                Tema.Texto, flags);
            return;
        }
        // Cartão estreito (tela pequena): ícone menor e letras um pouco menores, para nada ser cortado.
        var estreito = Width < Px(210);
        var tamanhoIcone = estreito ? Px(28) : Px(40);
        TextRenderer.DrawText(g, _icone, Tema.FonteIcone(estreito ? 14 : 20), new Rectangle(Px(10), Px(12), tamanhoIcone, tamanhoIcone), _cor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        var x = Px(14) + tamanhoIcone;
        var largura = Math.Max(10, Width - x - Px(8));
        const TextFormatFlags linha = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
        var fRotulo = Tema.Fonte(estreito ? 8.5f : 9.5f);
        var fDetalhe = Tema.Fonte(estreito ? 8f : 9f);
        // Valor grande, mas cabendo na altura: rótulo + valor + detalhe.
        var fValor = Tema.Semibold(18);
        foreach (var tamanho in new[] { 18f, 16f, 14f, 12f })
        {
            fValor = Tema.Semibold(estreito ? Math.Min(tamanho, 15f) : tamanho);
            if (Px(12) + fRotulo.Height + fValor.Height + fDetalhe.Height + Px(10) <= Height) break;
        }
        var y = Px(10);
        TextRenderer.DrawText(g, _rotulo, fRotulo, new Rectangle(x, y, largura, fRotulo.Height + 2), Tema.Texto, linha);
        y += fRotulo.Height + Px(2);
        TextRenderer.DrawText(g, _valor, fValor, new Rectangle(x, y, largura, fValor.Height + 4), Tema.Texto, linha);
        y += fValor.Height + Px(3);
        if (_detalhe.Length > 0)
            TextRenderer.DrawText(g, _detalhe, fDetalhe, new Rectangle(x, y, largura, fDetalhe.Height + 2), _corDetalhe, linha);
    }
}

/// <summary>
/// Gráfico de barras (vendas, eixo da esquerda) com linha (estoque, eixo da direita), um ponto por dia.
/// </summary>
public sealed class GraficoVendasEstoque : Control
{
    public sealed record Ponto(DateOnly Dia, decimal Vendido, decimal Estoque);

    private List<Ponto> _pontos = [];
    private string _mensagem = "Carregando...";

    public GraficoVendasEstoque()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Tema.Superficie;
        Height = 200;
    }

    /// <summary>Dados do gráfico (null/vazio = mostra a mensagem).</summary>
    public void Definir(IEnumerable<Ponto>? pontos, string? mensagem = null)
    {
        _pontos = pontos?.ToList() ?? [];
        _mensagem = mensagem ?? "Sem dados no período.";
        Invalidate();
    }

    /// <summary>Teto "redondo" do eixo (5, 10, 20, 50, 100, 200, 500...).</summary>
    private static decimal Teto(decimal v)
    {
        if (v <= 0) return 5;
        var potencia = (decimal)Math.Pow(10, Math.Floor(Math.Log10((double)v)));
        foreach (var m in new[] { 1m, 2m, 2.5m, 5m, 10m })
            if (v <= m * potencia) return m * potencia;
        return 10 * potencia;
    }

    private static string Numero(decimal v) =>
        v >= 1000 ? (v / 1000m).ToString("0.#", CultureInfo.GetCultureInfo("pt-BR")) + " mil" : v.ToString("0.##", CultureInfo.GetCultureInfo("pt-BR"));

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var fonte = Tema.Fonte(8);
        var corEixo = Tema.TextoSuave;
        var corBarra = Tema.Primaria;
        var corLinha = Tema.ModoEscuro ? Color.FromArgb(120, 180, 255) : Color.FromArgb(56, 132, 245);

        // Legenda
        var legendaY = 2;
        var xLegenda = Width - 10;
        void Legenda(string texto, Color cor, bool linha)
        {
            var w = TextRenderer.MeasureText(texto, fonte).Width;
            xLegenda -= w;
            TextRenderer.DrawText(g, texto, fonte, new Point(xLegenda, legendaY), Tema.Texto, TextFormatFlags.NoPrefix);
            xLegenda -= 16;
            if (linha)
            {
                using var p = new Pen(cor, 2);
                g.DrawLine(p, xLegenda, legendaY + 8, xLegenda + 12, legendaY + 8);
            }
            else
            {
                using var b = new SolidBrush(cor);
                g.FillRectangle(b, xLegenda + 1, legendaY + 3, 10, 10);
            }
            xLegenda -= 14;
        }
        Legenda("Estoque (un)", corLinha, true);
        Legenda("Vendas (un)", corBarra, false);

        if (_pontos.Count == 0)
        {
            TextRenderer.DrawText(g, _mensagem, Tema.Fonte(9.5f), ClientRectangle, Tema.TextoSuave,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            return;
        }

        var maxV = Teto(_pontos.Max(p => p.Vendido));
        var maxE = Teto(_pontos.Max(p => Math.Max(0, p.Estoque)));
        var area = new Rectangle(38, 26, Math.Max(40, Width - 38 - 40), Math.Max(30, Height - 26 - 24));

        // Grade e eixos (5 divisões)
        using (var grade = new Pen(Tema.Borda))
        {
            for (var i = 0; i <= 5; i++)
            {
                var y = area.Bottom - area.Height * i / 5;
                g.DrawLine(grade, area.Left, y, area.Right, y);
                TextRenderer.DrawText(g, Numero(maxV * i / 5), fonte, new Rectangle(0, y - 8, area.Left - 4, 16), corEixo,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, Numero(maxE * i / 5), fonte, new Rectangle(area.Right + 4, y - 8, 40, 16), corEixo,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }

        var n = _pontos.Count;
        var passo = area.Width / (float)n;
        var larguraBarra = Math.Max(2f, passo * 0.62f);
        using (var b = new SolidBrush(corBarra))
        {
            for (var i = 0; i < n; i++)
            {
                var h = (float)(_pontos[i].Vendido / maxV) * area.Height;
                if (h <= 0) continue;
                var x = area.Left + passo * i + (passo - larguraBarra) / 2;
                g.FillRectangle(b, x, area.Bottom - h, larguraBarra, h);
            }
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        var pontos = new PointF[n];
        for (var i = 0; i < n; i++)
        {
            var est = Math.Max(0, _pontos[i].Estoque);
            pontos[i] = new PointF(area.Left + passo * i + passo / 2, area.Bottom - (float)(est / maxE) * area.Height);
        }
        if (n > 1)
            using (var p = new Pen(corLinha, 2f)) g.DrawLines(p, pontos);
        using (var b = new SolidBrush(corLinha))
        {
            // Bolinhas só no primeiro e no último ponto (fica limpo com 30 dias).
            g.FillEllipse(b, pontos[0].X - 3, pontos[0].Y - 3, 6, 6);
            g.FillEllipse(b, pontos[^1].X - 3, pontos[^1].Y - 3, 6, 6);
        }
        g.SmoothingMode = SmoothingMode.None;

        // Datas: no máximo 7 rótulos.
        var intervalo = Math.Max(1, (int)Math.Ceiling(n / 7.0));
        for (var i = 0; i < n; i += intervalo)
        {
            var x = (int)(area.Left + passo * i + passo / 2);
            TextRenderer.DrawText(g, _pontos[i].Dia.ToString("dd/MM"), fonte, new Rectangle(x - 24, area.Bottom + 4, 48, 16), corEixo,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
        }
    }
}

/// <summary>
/// Menu lateral azul-marinho do visual 2026: marca no topo, itens com ícone e grupos que abrem e fecham.
/// O item ativo fica num bloco azul arredondado. Desenhado num único controle (leve e sem piscar).
/// </summary>
public sealed class MenuLateralERP : Panel
{
    public abstract record Entrada;
    public sealed record Item(string Chave, string Texto, string Icone, Action Executar) : Entrada;
    public sealed record Grupo(string Texto, string Icone, IReadOnlyList<Item> Itens) : Entrada;

    private sealed record Linha(int Y, int Altura, Item? Item, Grupo? Grupo, bool Sub);

    private readonly IReadOnlyList<Entrada> _entradas;
    private readonly HashSet<string> _abertos = new(StringComparer.Ordinal);
    private readonly AreaItens _area;
    private readonly List<Linha> _linhas = [];
    private string? _ativo;
    private Linha? _hover;

    public MenuLateralERP(string titulo, string subtitulo, Image? icone, IEnumerable<Entrada> entradas, int largura = 248)
    {
        _entradas = entradas.Where(e => e is Item || (e is Grupo gr && gr.Itens.Count > 0)).ToList();
        Dock = DockStyle.Left;
        Width = largura;
        BackColor = Tema.Barra;

        // Marca: ícone grande e o nome em duas linhas.
        // Altura pela fonte real: com o zoom do Windows em 125%/150% o nome não fica cortado.
        var fonteMarca = Tema.Semibold(14);
        var alturaLinha = fonteMarca.Height;
        var escalaMarca = DeviceDpi / 96f;
        var tamanhoIcone = (int)(48 * escalaMarca);
        var alturaMarca = Math.Max(tamanhoIcone, alturaLinha * 2) + (int)(40 * escalaMarca);
        var marca = new Panel { Dock = DockStyle.Top, Height = alturaMarca, BackColor = Tema.Barra };
        var topo = (alturaMarca - Math.Max(tamanhoIcone, alturaLinha * 2)) / 2;
        if (icone is not null)
            marca.Controls.Add(new PictureBox
            {
                Image = icone, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent,
                Size = new Size(tamanhoIcone, tamanhoIcone), Location = new Point(18, topo + (Math.Max(tamanhoIcone, alturaLinha * 2) - tamanhoIcone) / 2)
            });
        var x = icone is null ? 22 : 18 + tamanhoIcone + 12;
        var yTexto = topo + (Math.Max(tamanhoIcone, alturaLinha * 2) - alturaLinha * 2) / 2;
        marca.Controls.Add(new Label
        {
            Text = titulo, Font = fonteMarca, ForeColor = Color.White, AutoSize = false, AutoEllipsis = true,
            Size = new Size(Math.Max(40, largura - x - 8), alturaLinha), Location = new Point(x, yTexto), BackColor = Color.Transparent
        });
        marca.Controls.Add(new Label
        {
            Text = subtitulo, Font = fonteMarca, ForeColor = Color.White, AutoSize = false, AutoEllipsis = true,
            Size = new Size(Math.Max(40, largura - x - 8), alturaLinha), Location = new Point(x, yTexto + alturaLinha), BackColor = Color.Transparent
        });

        _area = new AreaItens(this) { Dock = DockStyle.Fill };
        Controls.Add(_area);
        Controls.Add(marca);
        Montar();
    }

    /// <summary>248 com os nomes, ou 68 só com ícones quando a janela é estreita.</summary>
    public void DefinirLargura(bool soIcones)
    {
        Width = soIcones ? 68 : 248;
        foreach (Control c in Controls)
        {
            if (c is not Panel marca || marca.Dock != DockStyle.Top) continue;
            foreach (Control filho in marca.Controls)
                if (filho is Label) filho.Visible = !soIcones;
        }
        Invalidate(true);
    }

    /// <summary>Marca o item ativo (abre o grupo dele).</summary>
    public void Marcar(string chave)
    {
        _ativo = chave;
        foreach (var g in _entradas.OfType<Grupo>())
            if (g.Itens.Any(i => i.Chave == chave)) _abertos.Add(g.Texto);
        Montar();
    }

    private void Montar()
    {
        _linhas.Clear();
        var escala = DeviceDpi / 96f;
        int Px(int v) => (int)(v * escala);
        var y = Px(6);
        foreach (var e in _entradas)
        {
            switch (e)
            {
                case Item item:
                    _linhas.Add(new Linha(y, Px(46), item, null, false));
                    y += Px(46);
                    break;
                case Grupo grupo:
                    _linhas.Add(new Linha(y, Px(46), null, grupo, false));
                    y += Px(46);
                    if (_abertos.Contains(grupo.Texto))
                        foreach (var sub in grupo.Itens)
                        {
                            _linhas.Add(new Linha(y, Px(38), sub, null, true));
                            y += Px(38);
                        }
                    break;
            }
        }
        _area.AutoScrollMinSize = new Size(0, y + Px(8));
        _area.Invalidate();
    }

    private Linha? LinhaEm(Point p)
    {
        var y = p.Y - _area.AutoScrollPosition.Y;
        return _linhas.FirstOrDefault(l => y >= l.Y && y < l.Y + l.Altura);
    }

    private void Clicar(Linha l)
    {
        if (l.Grupo is { } g)
        {
            if (!_abertos.Remove(g.Texto)) _abertos.Add(g.Texto);
            Montar();
            return;
        }
        if (l.Item is { } item)
        {
            item.Executar();
        }
    }

    private void Desenhar(Graphics g, Size tamanho)
    {
        g.Clear(Tema.Barra);
        var w = tamanho.Width;
        var h = tamanho.Height;

        // Ondas claras no rodapé (decoração).
        if (h > 260)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var onda = Tema.Clarear(Tema.Primaria, 0.25f);
            using (var p1 = new GraphicsPath())
            {
                p1.AddBezier(0, h - 150, w * 0.35f, h - 210, w * 0.6f, h - 60, w, h - 140);
                p1.AddLine(w, h - 140, w, h);
                p1.AddLine(w, h, 0, h);
                p1.CloseFigure();
                using var b1 = new SolidBrush(Color.FromArgb(34, onda));
                g.FillPath(b1, p1);
            }
            using (var p2 = new GraphicsPath())
            {
                p2.AddBezier(0, h - 80, w * 0.4f, h - 150, w * 0.7f, h - 10, w, h - 95);
                p2.AddLine(w, h - 95, w, h);
                p2.AddLine(w, h, 0, h);
                p2.CloseFigure();
                using var b2 = new SolidBrush(Color.FromArgb(48, onda));
                g.FillPath(b2, p2);
            }
        }

        var escala = DeviceDpi / 96f;
        int Px(int v) => (int)(v * escala);
        var deslocamento = _area.AutoScrollPosition.Y;
        foreach (var l in _linhas)
        {
            var y = l.Y + deslocamento;
            if (y + l.Altura < 0 || y > h) continue;
            var ativo = l.Item is not null && l.Item.Chave == _ativo;
            var grupoComAtivo = l.Grupo is not null && l.Grupo.Itens.Any(i => i.Chave == _ativo);
            var caixa = new Rectangle(Px(10), y + Px(3), w - Px(20), l.Altura - Px(6));

            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (ativo || l == _hover)
            {
                using var caminho = Tema.Arredondado(caixa, Px(8));
                using var fundo = new SolidBrush(ativo ? Tema.Primaria : Tema.BarraHover);
                g.FillPath(fundo, caminho);
            }
            g.SmoothingMode = SmoothingMode.None;

            var cor = ativo || grupoComAtivo ? Color.White : Tema.BarraTexto;
            const TextFormatFlags centro = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
            var iconesSo = w < Px(96);
            if (l.Sub)
            {
                if (iconesSo) continue;
                TextRenderer.DrawText(g, l.Item!.Texto, ativo ? Tema.Semibold(9.5f) : Tema.Fonte(9.5f),
                    new Rectangle(Px(60), y, w - Px(72), l.Altura), cor, centro);
                continue;
            }
            var icone = l.Item?.Icone ?? l.Grupo!.Icone;
            var texto = l.Item?.Texto ?? l.Grupo!.Texto;
            TextRenderer.DrawText(g, icone, Tema.FonteIcone(14), new Rectangle(iconesSo ? Px(8) : Px(22), y, iconesSo ? w - Px(16) : Px(28), l.Altura), cor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            if (iconesSo) continue;
            TextRenderer.DrawText(g, texto, ativo || grupoComAtivo ? Tema.Semibold(11) : Tema.Fonte(11),
                new Rectangle(Px(60), y, w - Px(60) - Px(l.Grupo is null ? 14 : 40), l.Altura), cor, centro);
            if (l.Grupo is not null)
                TextRenderer.DrawText(g, _abertos.Contains(l.Grupo.Texto) ? Icones.ChevronCima : Icones.ChevronBaixo,
                    Tema.FonteIcone(8), new Rectangle(w - Px(42), y, Px(24), l.Altura), Tema.BarraTexto,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        }
    }

    /// <summary>Área rolável onde os itens são desenhados.</summary>
    private sealed class AreaItens : Panel
    {
        private readonly MenuLateralERP _menu;

        public AreaItens(MenuLateralERP menu)
        {
            _menu = menu;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AutoScroll = true;
            BackColor = Tema.Barra;
        }

        protected override void OnPaint(PaintEventArgs e) => _menu.Desenhar(e.Graphics, ClientSize);

        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var l = _menu.LinhaEm(e.Location);
            if (l == _menu._hover) return;
            _menu._hover = l;
            Cursor = l is null ? Cursors.Default : Cursors.Hand;
            // Sem dica (tooltip): o texto já aparece inteiro no menu e a dica cobria os itens de baixo.
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _menu._hover = null;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Left && _menu.LinhaEm(e.Location) is { } l) _menu.Clicar(l);
        }
    }
}
