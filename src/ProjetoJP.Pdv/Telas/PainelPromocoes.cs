using System.ComponentModel;
using System.Drawing.Drawing2D;
using ProjetoJP.Controles;
using ProjetoJP.Pdv.Dados;
using ProjetoJP.Pdv.Servicos;
using ProjetoJP.Shared;

namespace ProjetoJP.Pdv.Telas;

/// <summary>
/// Vitrine de promoções para o cliente ver enquanto o caixa está livre ou fechado: uma promoção grande que troca
/// sozinha a cada poucos segundos e, embaixo, as próximas. Usa as promoções e o atacado que a Retaguarda já envia
/// aos caixas (nada a cadastrar a mais). Sem promoção, mostra a logo grande e uma mensagem.
/// </summary>
internal sealed class PainelPromocoes : Control
{
    private sealed record Destaque(string Tipo, string Nome, string? De, string? DeValor, string? DeFim, string Preco, string? Detalhe);

    private readonly System.Windows.Forms.Timer _troca = new() { Interval = 6000 };
    private List<Destaque> _lista = [];
    private int _atual;
    private DateTime _carregadoEm = DateTime.MinValue;
    // Logo na maior parte do tempo (caixa fechado): fica na logo e, de vez em quando, passa algumas promoções.
    private bool _naLogo = true;
    private int _passos;
    private const int PassosNaLogo = 5;       // 5 × 6 s = 30 s na logo
    private const int PromocoesPorVez = 2;    // depois mostra 2 promoções (12 s) e volta para a logo

    public PainelPromocoes()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        BackColor = Tema.Fundo;
        _troca.Tick += (_, _) => Avancar();
    }

    /// <summary>Título quando não há promoção (ex.: "CAIXA LIVRE").</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string TituloVazio { get; set; } = "CAIXA LIVRE";

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string SubtituloVazio { get; set; } = "Passe o primeiro produto para começar";

    /// <summary>true = mostra a logo da loja na maior parte do tempo e as promoções só de vez em quando (caixa fechado).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool LogoNaMaiorParte { get; set; }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
        {
            Recarregar();
            _troca.Start();
        }
        else
        {
            _troca.Stop();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _troca.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>Lê de novo as promoções do banco do caixa (de tempos em tempos, ou na hora com <paramref name="forcar"/>).</summary>
    public void Recarregar(bool forcar = false)
    {
        if (!forcar && DateTime.Now - _carregadoEm < TimeSpan.FromMinutes(2)) return;
        try { _lista = Montar(Repositorio.ProdutosAtivos()); }
        catch (Exception) { _lista = []; }
        _carregadoEm = DateTime.Now;
        if (_atual >= _lista.Count) _atual = 0;
        Invalidate();
    }

    private void Avancar()
    {
        if (DateTime.Now - _carregadoEm > TimeSpan.FromMinutes(5)) Recarregar(forcar: true);
        if (LogoNaMaiorParte)
        {
            _passos++;
            if (_naLogo)
            {
                if (_passos < PassosNaLogo || _lista.Count == 0) return;
                _naLogo = false; // hora das promoções: começa pela próxima
                _passos = 0;
                if (_lista.Count > 0) _atual = (_atual + 1) % _lista.Count;
            }
            else if (_passos >= Math.Min(PromocoesPorVez, _lista.Count))
            {
                _naLogo = true;  // volta para a logo
                _passos = 0;
            }
            else
            {
                _atual = (_atual + 1) % _lista.Count;
            }
            Invalidate();
            return;
        }
        if (_lista.Count <= 1) return;
        _atual = (_atual + 1) % _lista.Count;
        Invalidate();
    }

    private static string PorUnidade(ProdutoLocal p) =>
        Formatos.UnidadeFracionada(p.Unidade) ? " / " + p.Unidade.Trim().ToLowerInvariant() : "";

    private static List<Destaque> Montar(List<ProdutoLocal> produtos)
    {
        var promocoes = produtos
            .Where(p => p.EmPromocao && p.PrecoNormal > 0)
            .OrderByDescending(p => (p.PrecoNormal - p.PrecoVenda) / p.PrecoNormal)
            .Take(8)
            .Select(p => new Destaque("PROMOÇÃO", p.Descricao, "de ", Ui.Dinheiro(p.PrecoNormal), " por",
                Ui.Dinheiro(p.PrecoVenda) + PorUnidade(p), null));
        var atacado = produtos
            .Select(p => (p, faixa: p.Faixas.Where(f => f.Preco < p.PrecoVenda).OrderBy(f => f.Minimo).Cast<Atacado.Faixa?>().FirstOrDefault()))
            .Where(x => x.faixa is not null)
            .Take(8)
            .Select(x => new Destaque("ATACADO", x.p.Descricao, $"levando {Cupom.Qtd(x.faixa!.Value.Minimo)} {x.p.Unidade} ou mais, cada", null, null,
                Ui.Dinheiro(x.faixa!.Value.Preco), $"normal {Ui.Dinheiro(x.p.PrecoVenda)}"));
        return promocoes.Concat(atacado).Take(12).ToList();
    }

    // ---------- Desenho ----------

    private const TextFormatFlags Linha = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (Width < 60 || Height < 60) return;
        if (_lista.Count == 0 || (LogoNaMaiorParte && _naLogo))
        {
            DesenharVazio(g);
            return;
        }

        var cartoes = _lista.Count > 1 && Height >= 400 ? 118 : 0;
        var pontos = _lista.Count > 1 ? 26 : 0;
        var grande = new Rectangle(0, 0, Width - 1, Height - 1 - cartoes - pontos - (cartoes > 0 ? 12 : 0));
        DesenharGrande(g, grande, _lista[_atual]);

        if (pontos > 0)
        {
            var n = _lista.Count;
            var larguraPontos = n * 14 + 14;
            var x = (Width - larguraPontos) / 2;
            var y = grande.Bottom + 9;
            for (var i = 0; i < n; i++)
            {
                var atual = i == _atual;
                var r = new Rectangle(x, y, atual ? 22 : 8, 8);
                using var b = new SolidBrush(atual ? Tema.Primaria : Tema.Borda);
                using var caminho = Tema.Arredondado(r, 4);
                g.FillPath(b, caminho);
                x += atual ? 28 : 14;
            }
        }

        if (cartoes > 0)
        {
            var proximos = Enumerable.Range(1, Math.Min(3, _lista.Count - 1)).Select(k => _lista[(_atual + k) % _lista.Count]).ToList();
            var gap = 12;
            var w = (Width - gap * 2) / 3;
            var y = Height - cartoes;
            for (var i = 0; i < proximos.Count; i++)
                DesenharPequeno(g, new Rectangle(i * (w + gap), y, w - 1, cartoes - 1), proximos[i]);
        }
    }

    private void DesenharVazio(Graphics g)
    {
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var caminho = Tema.Arredondado(r, 18))
        {
            using (var fundo = new SolidBrush(Tema.Superficie)) g.FillPath(fundo, caminho);
            using (var borda = new Pen(Tema.Borda)) g.DrawPath(borda, caminho);
        }
        var lado = Math.Clamp(Math.Min(Width, Height) * 2 / 5, 80, 240);
        var y = Math.Max(10, (Height - lado - 110) / 2);
        var area = new Rectangle((Width - lado) / 2, y, lado, lado);
        DesenharMarca(g, area, Tema.Primaria);
        y += lado + 22;
        const TextFormatFlags centro = Linha | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
        TextRenderer.DrawText(g, TituloVazio, Tema.Semibold(26), new Rectangle(10, y, Width - 20, 48), Tema.Texto, centro);
        TextRenderer.DrawText(g, SubtituloVazio, Tema.Fonte(13), new Rectangle(10, y + 50, Width - 20, 30), Tema.TextoSuave, centro);
    }

    /// <summary>Logo da loja (Aparência) ou um ícone de etiqueta, se não houver logo.</summary>
    private static void DesenharMarca(Graphics g, Rectangle area, Color corIcone)
    {
        if (Aparencia.Logo is { } logo)
        {
            var escala = Math.Min((double)area.Width / logo.Width, (double)area.Height / logo.Height);
            var w = (int)(logo.Width * escala);
            var h = (int)(logo.Height * escala);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(logo, area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
            return;
        }
        TextRenderer.DrawText(g, Icones.Etiqueta, Tema.FonteIcone(area.Height * 0.42f), area, corIcone,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private static void DesenharGrande(Graphics g, Rectangle r, Destaque d)
    {
        if (r.Width < 40 || r.Height < 40) return;
        using (var caminho = Tema.Arredondado(r, 18))
        using (var fundo = new LinearGradientBrush(r, Tema.Primaria, Tema.Barra, 35f))
            g.FillPath(fundo, caminho);

        // Logo à direita, num círculo claro (só se couber)
        var ladoImagem = r.Width >= 620 ? Math.Min(r.Height - 60, (int)(r.Width * 0.3)) : 0;
        if (ladoImagem >= 90)
        {
            var c = new Rectangle(r.Right - ladoImagem - 40, r.Y + (r.Height - ladoImagem) / 2, ladoImagem, ladoImagem);
            using (var bola = new SolidBrush(Color.FromArgb(Aparencia.Logo is null ? 26 : 235, Color.White))) g.FillEllipse(bola, c);
            var dentro = Rectangle.Inflate(c, -ladoImagem / 6, -ladoImagem / 6);
            DesenharMarca(g, dentro, Color.FromArgb(220, Color.White));
        }
        else
        {
            ladoImagem = 0;
        }

        var x = r.X + 36;
        var largura = r.Width - 72 - (ladoImagem > 0 ? ladoImagem + 30 : 0);
        var fNome = ControlePintado.FonteQueCabe(g, d.Nome, largura, Math.Clamp(r.Height / 9f, 18, 34), 13);
        var fPreco = ControlePintado.FonteQueCabe(g, d.Preco, largura, Math.Clamp(r.Height / 5f, 26, 64), 18);
        var hTag = 30;
        var hNome = fNome.Height + 6;
        var hDe = 30;
        var hPreco = fPreco.Height + 4;
        var hDetalhe = d.Detalhe is null ? 0 : 26;
        var total = hTag + 14 + hNome + hDe + hPreco + hDetalhe;
        var y = r.Y + Math.Max(16, (r.Height - total) / 2);

        // Etiqueta amarela (PROMOÇÃO / ATACADO)
        var fTag = Tema.Semibold(10);
        var tag = d.Tipo == "ATACADO" ? "ATACADO" : "PROMOÇÃO";
        var wTag = TextRenderer.MeasureText(g, tag, fTag).Width + 26;
        var rt = new Rectangle(x, y, wTag, hTag);
        using (var caminho = Tema.Arredondado(rt, hTag / 2))
        using (var b = new SolidBrush(CoresCaixa.Destaque))
            g.FillPath(b, caminho);
        TextRenderer.DrawText(g, tag, fTag, rt, Tema.Barra, Linha | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        y += hTag + 14;

        TextRenderer.DrawText(g, d.Nome, fNome, new Rectangle(x, y, largura, hNome), Color.White, Linha | TextFormatFlags.VerticalCenter);
        y += hNome;

        // "de R$ 21,90 por" com o preço antigo riscado
        var fDe = Tema.Fonte(14);
        var corDe = Tema.BarraTexto;
        if (d.DeValor is not null)
        {
            const TextFormatFlags justo = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            var px = x;
            foreach (var (texto, fonte) in new[] { (d.De ?? "", fDe), (d.DeValor, Tema.Fonte(14, FontStyle.Strikeout)), (d.DeFim ?? "", fDe) })
            {
                var w = TextRenderer.MeasureText(g, texto, fonte, Size.Empty, justo).Width;
                TextRenderer.DrawText(g, texto, fonte, new Rectangle(px, y + 4, w + 2, hDe), corDe, justo);
                px += w;
            }
        }
        else if (d.De is not null)
        {
            TextRenderer.DrawText(g, d.De, fDe, new Rectangle(x, y, largura, hDe), corDe, Linha | TextFormatFlags.VerticalCenter);
        }
        y += hDe;

        TextRenderer.DrawText(g, d.Preco, fPreco, new Rectangle(x - 4, y, largura + 4, hPreco), Color.FromArgb(252, 211, 77), Linha | TextFormatFlags.VerticalCenter);
        y += hPreco;
        if (d.Detalhe is not null)
            TextRenderer.DrawText(g, d.Detalhe, Tema.Fonte(11), new Rectangle(x, y, largura, hDetalhe), Tema.BarraTextoSuave, Linha | TextFormatFlags.VerticalCenter);
    }

    private static void DesenharPequeno(Graphics g, Rectangle r, Destaque d)
    {
        using (var caminho = Tema.Arredondado(r, 16))
        {
            using (var fundo = new SolidBrush(Tema.Superficie)) g.FillPath(fundo, caminho);
            using (var borda = new Pen(Tema.Borda)) g.DrawPath(borda, caminho);
        }
        var x = r.X + 18;
        var w = r.Width - 36;
        TextRenderer.DrawText(g, d.Tipo, Tema.Semibold(9), new Rectangle(x, r.Y + 14, w, 20), d.Tipo == "ATACADO" ? Tema.Sucesso : Tema.Alerta, Linha);
        TextRenderer.DrawText(g, d.Nome, Tema.Semibold(12), new Rectangle(x, r.Y + 36, w, 26), Tema.Texto, Linha);
        var preco = d.Tipo == "ATACADO" ? d.Preco + " cada" : d.Preco;
        var f = ControlePintado.FonteQueCabe(g, preco, w, 17, 10);
        TextRenderer.DrawText(g, preco, f, new Rectangle(x, r.Y + 64, w, r.Height - 72), Tema.Primaria, Linha | TextFormatFlags.VerticalCenter);
    }
}
