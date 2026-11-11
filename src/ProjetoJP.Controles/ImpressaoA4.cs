using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Drawing.Text;
using ProjetoJP.Shared;

namespace ProjetoJP.Controles;

/// <summary>Dados do orçamento para imprimir em folha A4 (o caixa e a Retaguarda montam a partir do que têm).</summary>
public sealed class OrcamentoImpresso
{
    public string Loja { get; init; } = "";
    /// <summary>Endereço, telefone... (as linhas do cabeçalho do cupom).</summary>
    public IReadOnlyList<string> Cabecalho { get; init; } = [];
    public int Numero { get; init; }
    public DateTime Data { get; init; }
    public string? Caixa { get; init; }
    public string? Atendente { get; init; }
    public string? Cliente { get; init; }
    public DateOnly? Validade { get; init; }
    public string? Observacao { get; init; }
    public decimal Subtotal { get; init; }
    public decimal Desconto { get; init; }
    public decimal Total { get; init; }
    public IReadOnlyList<Item> Itens { get; init; } = [];

    public sealed record Item(string Codigo, string Descricao, string Unidade, decimal Quantidade, decimal PrecoUnitario, decimal Total);
}

/// <summary>
/// Orçamento em folha A4 pela impressora comum do Windows (jato de tinta, laser ou "Microsoft Print to PDF").
/// Cabeçalho com logo e dados da loja, tabela de itens (quebra de página sozinha), totais e observação.
/// </summary>
public static class ImpressaoA4
{
    private static readonly Color CinzaTexto = Color.FromArgb(90, 90, 90);
    private static readonly Color CinzaLinha = Color.FromArgb(200, 200, 200);
    private static readonly Color CinzaFundo = Color.FromArgb(242, 242, 242);

    // Colunas da tabela (peso de cada uma na largura útil).
    private static readonly (string Titulo, float Peso, bool Direita)[] Colunas =
    [
        ("Item", 0.055f, true), ("Código", 0.13f, false), ("Descrição", 0.395f, false),
        ("Qtd", 0.09f, true), ("Un", 0.06f, false), ("Unitário", 0.135f, true), ("Total", 0.135f, true)
    ];

    /// <summary>Documento pronto para imprimir ou visualizar. <paramref name="impressora"/> vazio = padrão do Windows.</summary>
    public static PrintDocument Orcamento(OrcamentoImpresso o, string? impressora)
    {
        var doc = new PrintDocument { DocumentName = $"Orçamento {o.Numero}" };
        if (!string.IsNullOrWhiteSpace(impressora)) doc.PrinterSettings.PrinterName = impressora;
        AjustarA4(doc);

        var proximo = 0;
        var pagina = 0;
        doc.BeginPrint += (_, _) => { proximo = 0; pagina = 0; };
        doc.QueryPageSettings += (_, e) => e.PageSettings.Landscape = false;
        doc.PrintPage += (_, e) =>
        {
            pagina++;
            e.HasMorePages = DesenharPagina(e.Graphics!, e.MarginBounds, o, pagina, ref proximo);
        };
        return doc;
    }

    /// <summary>Imprime direto, sem perguntar nada. Devolve o erro em português, ou null se foi para a impressora.</summary>
    public static string? ImprimirDireto(PrintDocument doc)
    {
        try
        {
            if (!doc.PrinterSettings.IsValid)
                return string.IsNullOrWhiteSpace(doc.PrinterSettings.PrinterName)
                    ? "Não há impressora padrão no Windows."
                    : $"A impressora \"{doc.PrinterSettings.PrinterName}\" não foi encontrada neste computador.";
            doc.Print();
            return null;
        }
        catch (Exception ex) when (ex is InvalidPrinterException or Win32Exception or InvalidOperationException)
        {
            return ex.Message;
        }
    }

    internal static void AjustarA4(PrintDocument doc)
    {
        try
        {
            if (doc.PrinterSettings.IsValid)
            {
                var a4 = doc.PrinterSettings.PaperSizes.Cast<PaperSize>().FirstOrDefault(p => p.Kind == PaperKind.A4);
                if (a4 is not null) doc.DefaultPageSettings.PaperSize = a4;
            }
        }
        catch (Exception ex) when (ex is InvalidPrinterException or Win32Exception)
        {
            // impressora sumiu: a mensagem aparece na hora de imprimir
        }
        doc.DefaultPageSettings.Landscape = false;
        doc.DefaultPageSettings.Margins = new Margins(45, 45, 40, 40); // centésimos de polegada (~1,1 cm)
    }

    /// <returns>true se ainda há página para imprimir.</returns>
    private static bool DesenharPagina(Graphics g, Rectangle area, OrcamentoImpresso o, int pagina, ref int proximo)
    {
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var fonte = new Font("Segoe UI", 9.5f);
        using var negrito = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        using var pequena = new Font("Segoe UI", 8f);

        float x = area.Left, largura = area.Width;
        var limite = area.Bottom - 28f; // espaço do rodapé
        var y = pagina == 1 ? Cabecalho(g, area, o) : CabecalhoContinuacao(g, area, o);

        if (proximo < o.Itens.Count)
        {
            var larguras = Colunas.Select(c => c.Peso * largura).ToArray();
            y = CabecalhoTabela(g, x, y, larguras, negrito);
            var linha = 0;
            while (proximo < o.Itens.Count)
            {
                var item = o.Itens[proximo];
                var alturaDescricao = Math.Min(g.MeasureString(item.Descricao, fonte, (int)(larguras[2] - 8)).Height, fonte.GetHeight(g) * 2 + 1);
                var altura = Math.Max(22f, alturaDescricao + 8);
                if (y + altura > limite) break;
                if (linha % 2 == 1)
                    using (var fundo = new SolidBrush(CinzaFundo)) g.FillRectangle(fundo, x, y, largura, altura);
                string[] valores =
                [
                    (proximo + 1).ToString(), item.Codigo, item.Descricao, Formatos.Quantidade(item.Quantidade, item.Unidade),
                    item.Unidade, Formatos.Moeda(item.PrecoUnitario), Formatos.Moeda(item.Total)
                ];
                var cx = x;
                for (var c = 0; c < Colunas.Length; c++)
                {
                    var r = new RectangleF(cx + 4, y + 4, larguras[c] - 8, altura - 6);
                    using var formato = new StringFormat
                    {
                        Alignment = Colunas[c].Direita ? StringAlignment.Far : StringAlignment.Near,
                        LineAlignment = c == 2 ? StringAlignment.Near : StringAlignment.Center,
                        Trimming = StringTrimming.EllipsisWord,
                        FormatFlags = c == 2 ? (StringFormatFlags)0 : StringFormatFlags.NoWrap
                    };
                    g.DrawString(valores[c], c == 6 ? negrito : fonte, Brushes.Black, r, formato);
                    cx += larguras[c];
                }
                y += altura;
                using (var caneta = new Pen(CinzaLinha, 0.5f)) g.DrawLine(caneta, x, y, x + largura, y);
                proximo++;
                linha++;
            }
            if (proximo < o.Itens.Count)
            {
                Rodape(g, area, o, pagina, continua: true);
                return true;
            }
            y += 10;
        }

        // Totais e observação juntos: se não couberem, vão para a página seguinte (sem repetir itens).
        var alturaFinal = AlturaFinal(g, o, largura, fonte);
        var paginaVazia = y <= area.Top + 140;
        if (y + alturaFinal > limite && !paginaVazia)
        {
            Rodape(g, area, o, pagina, continua: true);
            return true;
        }
        y = Totais(g, x, y, largura, o, fonte);
        Fechamento(g, x, y + 14, largura, o, fonte, negrito, pequena);
        Rodape(g, area, o, pagina, continua: false);
        return false;
    }

    private static float Cabecalho(Graphics g, Rectangle area, OrcamentoImpresso o)
    {
        using var nomeLoja = new Font("Segoe UI", 15f, FontStyle.Bold);
        using var linhaLoja = new Font("Segoe UI", 9f);
        using var titulo = new Font("Segoe UI", 20f, FontStyle.Bold);
        using var numero = new Font("Segoe UI", 11f, FontStyle.Bold);
        using var pequena = new Font("Segoe UI", 8.5f);
        using var cinza = new SolidBrush(CinzaTexto);

        float x = area.Left, y = area.Top;
        var larguraDireita = 230f;

        // Logo da Aparência, sem esticar: cabe numa caixa de até 150 x 70.
        var logo = Aparencia.Logo;
        if (logo is not null && logo.Width > 0 && logo.Height > 0)
        {
            var escala = Math.Min(150f / logo.Width, 70f / logo.Height);
            var w = logo.Width * escala;
            var h = logo.Height * escala;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(logo, x, y, w, h);
            x += w + 14;
        }

        var larguraTexto = area.Right - larguraDireita - x - 10;
        var yEsq = y;
        if (!string.IsNullOrWhiteSpace(o.Loja))
        {
            var r = new RectangleF(x, yEsq, larguraTexto, 50);
            g.DrawString(o.Loja, nomeLoja, Brushes.Black, r, new StringFormat { Trimming = StringTrimming.EllipsisWord });
            yEsq += Math.Min(g.MeasureString(o.Loja, nomeLoja, (int)larguraTexto).Height, 50) + 2;
        }
        foreach (var l in o.Cabecalho.Where(l => !string.IsNullOrWhiteSpace(l)).Take(4))
        {
            g.DrawString(l, linhaLoja, cinza, new RectangleF(x, yEsq, larguraTexto, 18), new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter });
            yEsq += 16;
        }
        if (logo is not null) yEsq = Math.Max(yEsq, y + 72);

        // Lado direito: ORÇAMENTO, número e data
        using var direita = new StringFormat { Alignment = StringAlignment.Far };
        var xd = area.Right - larguraDireita;
        var yDir = y;
        g.DrawString("ORÇAMENTO", titulo, Brushes.Black, new RectangleF(xd, yDir, larguraDireita, 40), direita);
        yDir += 38;
        g.DrawString($"Nº {o.Numero:000000}", numero, Brushes.Black, new RectangleF(xd, yDir, larguraDireita, 22), direita);
        yDir += 22;
        g.DrawString($"Emitido em {o.Data:dd/MM/yyyy HH:mm}", pequena, cinza, new RectangleF(xd, yDir, larguraDireita, 18), direita);
        yDir += 18;

        var yLinha = Math.Max(yEsq, yDir) + 8;
        using (var grossa = new Pen(Color.Black, 1.6f)) g.DrawLine(grossa, area.Left, yLinha, area.Right, yLinha);

        // Quadro com cliente, validade, atendente e caixa
        var yQuadro = yLinha + 10;
        var alturaQuadro = 48f;
        using (var fundo = new SolidBrush(CinzaFundo)) g.FillRectangle(fundo, area.Left, yQuadro, area.Width, alturaQuadro);
        var metade = area.Width / 2f;
        Campo(g, area.Left + 10, yQuadro + 6, metade - 20, "Cliente", string.IsNullOrWhiteSpace(o.Cliente) ? "Consumidor final" : o.Cliente!);
        Campo(g, area.Left + metade + 10, yQuadro + 6, metade - 20, "Validade",
            o.Validade is { } v ? v.ToString("dd/MM/yyyy") : "—");
        Campo(g, area.Left + 10, yQuadro + 26, metade - 20, "Atendente", string.IsNullOrWhiteSpace(o.Atendente) ? "—" : o.Atendente!);
        Campo(g, area.Left + metade + 10, yQuadro + 26, metade - 20, "Caixa", string.IsNullOrWhiteSpace(o.Caixa) ? "—" : o.Caixa!);
        return yQuadro + alturaQuadro + 14;
    }

    private static void Campo(Graphics g, float x, float y, float largura, string rotulo, string valor)
    {
        using var fRotulo = new Font("Segoe UI", 9f, FontStyle.Bold);
        using var fValor = new Font("Segoe UI", 9.5f);
        var texto = rotulo + ": ";
        var w = g.MeasureString(texto, fRotulo).Width;
        g.DrawString(texto, fRotulo, Brushes.Black, x, y);
        g.DrawString(valor, fValor, Brushes.Black, new RectangleF(x + w, y, Math.Max(10, largura - w), 18),
            new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter });
    }

    private static float CabecalhoContinuacao(Graphics g, Rectangle area, OrcamentoImpresso o)
    {
        using var f = new Font("Segoe UI", 11f, FontStyle.Bold);
        g.DrawString($"Orçamento nº {o.Numero:000000} — continuação", f, Brushes.Black, area.Left, area.Top);
        var y = area.Top + 24f;
        using (var p = new Pen(Color.Black, 1.2f)) g.DrawLine(p, area.Left, y, area.Right, y);
        return y + 10;
    }

    private static float CabecalhoTabela(Graphics g, float x, float y, float[] larguras, Font negrito)
    {
        var altura = 24f;
        using (var fundo = new SolidBrush(Color.FromArgb(225, 225, 225))) g.FillRectangle(fundo, x, y, larguras.Sum(), altura);
        var cx = x;
        for (var c = 0; c < Colunas.Length; c++)
        {
            using var formato = new StringFormat
            {
                Alignment = Colunas[c].Direita ? StringAlignment.Far : StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap
            };
            g.DrawString(Colunas[c].Titulo, negrito, Brushes.Black, new RectangleF(cx + 4, y, larguras[c] - 8, altura), formato);
            cx += larguras[c];
        }
        return y + altura;
    }

    private static float AlturaFinal(Graphics g, OrcamentoImpresso o, float largura, Font fonte)
    {
        var altura = (o.Desconto > 0 ? 3 : 2) * 22f + 30;
        if (!string.IsNullOrWhiteSpace(o.Observacao))
            altura += g.MeasureString(o.Observacao, fonte, (int)largura - 20).Height + 40;
        return altura + 60;
    }

    private static float Totais(Graphics g, float x, float y, float largura, OrcamentoImpresso o, Font fonte)
    {
        using var total = new Font("Segoe UI", 14f, FontStyle.Bold);
        using var direita = new StringFormat { Alignment = StringAlignment.Far };
        var lb = 300f;
        var xb = x + largura - lb;
        void Linha(string rotulo, string valor)
        {
            g.DrawString(rotulo, fonte, Brushes.Black, xb, y);
            g.DrawString(valor, fonte, Brushes.Black, new RectangleF(xb, y, lb, 20), direita);
            y += 22;
        }
        Linha("Subtotal", Formatos.Moeda(o.Subtotal));
        if (o.Desconto > 0) Linha("Desconto", "- " + Formatos.Moeda(o.Desconto));
        using (var p = new Pen(Color.Black, 1.2f)) g.DrawLine(p, xb, y + 2, xb + lb, y + 2);
        y += 6;
        g.DrawString("TOTAL", total, Brushes.Black, xb, y);
        g.DrawString(Formatos.Moeda(o.Total), total, Brushes.Black, new RectangleF(xb, y, lb, 30), direita);
        return y + 32;
    }

    private static void Fechamento(Graphics g, float x, float y, float largura, OrcamentoImpresso o, Font fonte, Font negrito, Font pequena)
    {
        if (!string.IsNullOrWhiteSpace(o.Observacao))
        {
            g.DrawString("Observações", negrito, Brushes.Black, x, y);
            y += 20;
            var altura = g.MeasureString(o.Observacao, fonte, (int)largura - 20).Height + 12;
            using (var p = new Pen(CinzaLinha)) g.DrawRectangle(p, x, y, largura, altura);
            g.DrawString(o.Observacao, fonte, Brushes.Black, new RectangleF(x + 10, y + 6, largura - 20, altura));
            y += altura + 14;
        }
        using var cinza = new SolidBrush(CinzaTexto);
        if (o.Validade is { } v)
        {
            g.DrawString($"Orçamento válido até {v:dd/MM/yyyy}.", negrito, Brushes.Black, x, y);
            y += 18;
        }
        g.DrawString("Preços sujeitos a alteração e à disponibilidade de estoque. Documento sem valor fiscal.", pequena, cinza, x, y);
    }

    private static void Rodape(Graphics g, Rectangle area, OrcamentoImpresso o, int pagina, bool continua)
    {
        using var pequena = new Font("Segoe UI", 8f);
        using var cinza = new SolidBrush(CinzaTexto);
        var y = area.Bottom - 18f;
        using (var p = new Pen(CinzaLinha)) g.DrawLine(p, area.Left, y - 4, area.Right, y - 4);
        var esquerda = (string.IsNullOrWhiteSpace(o.Loja) ? "" : o.Loja + "  ·  ") + $"Orçamento nº {o.Numero:000000}";
        g.DrawString(esquerda, pequena, cinza, area.Left, y);
        using var direita = new StringFormat { Alignment = StringAlignment.Far };
        g.DrawString($"Página {pagina}" + (continua ? " — continua" : ""), pequena, cinza, new RectangleF(area.Left, y, area.Width, 16), direita);
    }
}

/// <summary>Mostra como vai ficar a folha antes de imprimir; "Imprimir" deixa escolher a impressora (ou salvar em PDF).</summary>
public sealed class VisualizarImpressaoForm : Form
{
    private readonly PrintDocument _doc;
    private readonly PrintPreviewControl _previa;
    private readonly Label _pagina = new() { AutoSize = true, Font = Tema.Fonte(10), ForeColor = Tema.Texto, Margin = new Padding(8, 10, 8, 0) };

    /// <summary>Impressora usada, quando imprimiu (para lembrar na próxima vez).</summary>
    public string? ImpressoraUsada { get; private set; }

    /// <summary>
    /// Abre a prévia. Impressora escolhida que sumiu: usa a padrão do Windows. Sem nenhuma impressora instalada
    /// a prévia não tem como medir a folha, então avisa em vez de abrir. Devolve a impressora usada (ou null).
    /// </summary>
    public static string? Abrir(IWin32Window? dono, PrintDocument doc, string titulo)
    {
        try
        {
            if (!doc.PrinterSettings.IsValid)
            {
                doc.PrinterSettings.PrinterName = new PrinterSettings().PrinterName; // a padrão do Windows
                ImpressaoA4.AjustarA4(doc);
            }
            if (!doc.PrinterSettings.IsValid)
            {
                MessageBox.Show(dono, "Não há impressora instalada neste computador.\n\n" +
                                      "Para salvar em PDF, instale a impressora \"Microsoft Print to PDF\" " +
                                      "(Configurações do Windows → Impressoras e scanners).",
                    titulo, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
            using var f = new VisualizarImpressaoForm(doc, titulo);
            f.ShowDialog(dono);
            return f.ImpressoraUsada;
        }
        catch (Exception ex) when (ex is InvalidPrinterException or Win32Exception)
        {
            MessageBox.Show(dono, "A impressora não respondeu:\n" + ex.Message, titulo, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
    }

    public VisualizarImpressaoForm(PrintDocument doc, string titulo)
    {
        _doc = doc;
        Text = titulo;
        Font = Tema.Fonte(10);
        BackColor = Tema.Fundo;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(900, 980);
        MinimumSize = new Size(620, 480);
        JanelaNaTela.Limitar(this); // 980 de altura não cabe em telas de 768: encolhe para a área livre
        ShowInTaskbar = false;
        MinimizeBox = false;
        KeyPreview = true;

        _previa = new PrintPreviewControl
        {
            Dock = DockStyle.Fill, Document = doc, AutoZoom = true, UseAntiAlias = true, Columns = 1, Rows = 1,
            BackColor = Tema.Fundo
        };

        var barra = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 54, WrapContents = false, BackColor = Tema.Superficie, Padding = new Padding(10, 9, 10, 0)
        };
        BotaoTema Botao(string texto, string? icone, int largura, EstiloBotao estilo = EstiloBotao.Secundario) =>
            new() { Text = texto, Icone = icone, Estilo = estilo, Size = new Size(largura, 36), Margin = new Padding(0, 0, 8, 0) };
        var imprimir = Botao("Imprimir...", Icones.Imprimir, 140, EstiloBotao.Primario);
        imprimir.Click += (_, _) => Imprimir();
        var anterior = Botao("◀", null, 44);
        anterior.Click += (_, _) => { _previa.StartPage = Math.Max(0, _previa.StartPage - 1); AtualizarPagina(); };
        var proxima = Botao("▶", null, 44);
        proxima.Click += (_, _) => { _previa.StartPage++; AtualizarPagina(); };
        var menos = Botao("−", null, 44);
        menos.Click += (_, _) => Zoom(1 / 1.25);
        var mais = Botao("+", null, 44);
        mais.Click += (_, _) => Zoom(1.25);
        var inteira = Botao("Página inteira", null, 140);
        inteira.Click += (_, _) => _previa.AutoZoom = true;
        var fechar = Botao("Fechar", Icones.Cancelar, 110);
        fechar.Click += (_, _) => Close();
        barra.Controls.AddRange([imprimir, anterior, _pagina, proxima, menos, mais, inteira, fechar]);

        Controls.Add(_previa);
        Controls.Add(barra);
        CancelButton = null;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) Close();
            else if (e.Control && e.KeyCode == Keys.P) Imprimir();
        };
        Shown += (_, _) => AtualizarPagina();
    }

    private void Zoom(double fator)
    {
        _previa.AutoZoom = false;
        _previa.Zoom = Math.Clamp(_previa.Zoom * fator, 0.2, 4);
    }

    private void AtualizarPagina() => _pagina.Text = $"Página {_previa.StartPage + 1}";

    private void Imprimir()
    {
        using var dlg = new PrintDialog { Document = _doc, UseEXDialog = true, AllowSomePages = false, AllowSelection = false };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var erro = ImpressaoA4.ImprimirDireto(_doc);
        if (erro is not null)
        {
            MessageBox.Show(this, "A impressão falhou:\n" + erro, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        ImpressoraUsada = _doc.PrinterSettings.PrinterName;
        DialogResult = DialogResult.OK;
        Close();
    }
}
