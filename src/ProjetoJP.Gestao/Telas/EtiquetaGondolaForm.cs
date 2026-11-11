using System.Drawing.Printing;
using ProjetoJP.Controles;
using ProjetoJP.Gestao.Servicos;
using ProjetoJP.Shared;

namespace ProjetoJP.Gestao.Telas;

/// <summary>
/// Etiqueta de gôndola com o preço que o CAIXA cobra hoje: o do catálogo enviado aos caixas e, se houver,
/// a promoção em vigor. O preço em edição na Retaguarda (ainda não enviado) não vai para a prateleira.
/// </summary>
public sealed class EtiquetaGondolaForm : Form
{
    private readonly ApiClient _api;
    private readonly TextBox _busca = new() { Width = 280, Font = Tema.Fonte(10.5f), PlaceholderText = "Nome, código ou código de barras" };
    private readonly CheckBox _soPromocao = new() { Text = "Só em promoção", AutoSize = true, Font = Tema.Fonte(10), Margin = new Padding(12, 8, 0, 0) };
    private readonly CampoNumero _copias = new() { CasasDecimais = 0, Maximo = 50, Width = 60, Font = Tema.Fonte(10.5f) };
    private readonly DataGridView _grade = Ui.Grade();
    private readonly Label _resumo = new() { AutoSize = true, Font = Tema.Fonte(9.5f), ForeColor = Tema.TextoSuave };
    private readonly HashSet<Guid> _marcados = [];
    private List<EtiquetaApi> _produtos = [];

    public EtiquetaGondolaForm(ApiClient api)
    {
        _api = api;
        Text = "Etiqueta de gôndola";
        Icon = Ui.IconeApp ?? Icon;
        BackColor = Tema.Fundo;
        Font = Tema.Fonte(10);
        Size = new Size(980, 660);
        MinimumSize = new Size(760, 480);
        Padding = new Padding(24, 12, 24, 16);

        var cabecalho = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Tema.Fundo };
        cabecalho.Controls.Add(new Label { Text = "Etiqueta de gôndola", Font = Tema.Semibold(19), ForeColor = Tema.Texto, AutoSize = true, Location = new Point(0, 0) });
        cabecalho.Controls.Add(new Label
        {
            Text = "Sai com o preço que o caixa cobra hoje (o último enviado aos caixas, ou a promoção em vigor).",
            Font = Tema.Fonte(10), ForeColor = Tema.TextoSuave, AutoSize = true, Location = new Point(2, 36)
        });

        Label Rot(string t) => new() { Text = t, AutoSize = true, Font = Tema.Fonte(10), ForeColor = Tema.Texto, Margin = new Padding(0, 8, 6, 0) };
        var filtros = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, 50), WrapContents = true, BackColor = Tema.Fundo, Padding = new Padding(0, 6, 0, 0) };
        _busca.Margin = new Padding(0, 4, 8, 0);
        _busca.TextChanged += (_, _) => Mostrar();
        _soPromocao.CheckedChanged += (_, _) => Mostrar();
        var atualizar = Ui.Botao("Atualizar", Ui.Neutro, 120, 34, Icones.Atualizar);
        atualizar.Margin = new Padding(16, 0, 0, 0);
        atualizar.Click += async (_, _) => await CarregarAsync();
        filtros.Controls.AddRange([Rot("Buscar"), _busca, _soPromocao, atualizar]);

        var cartao = new CartaoPainel { Dock = DockStyle.Fill, Padding = new Padding(1, 6, 1, 6) };
        _grade.Dock = DockStyle.Fill;
        _grade.ReadOnly = false;
        var marcar = new DataGridViewCheckBoxColumn { HeaderText = "", FillWeight = 5, MinimumWidth = 40, SortMode = DataGridViewColumnSortMode.NotSortable };
        _grade.Columns.Add(marcar);
        _grade.Columns.AddRange(
            Ui.ColunaTexto("Código", 12), Ui.ColunaTexto("Produto", 42), Ui.ColunaTexto("Un", 6),
            Ui.ColunaTexto("Preço no caixa", 14, true), Ui.ColunaTexto("Observação", 26));
        foreach (DataGridViewColumn c in _grade.Columns) c.ReadOnly = c != marcar;
        _grade.CellContentClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 0) return;
            _grade.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grade.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 0 || _grade.Rows[e.RowIndex].Tag is not EtiquetaApi p) return;
            if (_grade.Rows[e.RowIndex].Cells[0].Value is true) _marcados.Add(p.Id); else _marcados.Remove(p.Id);
            AtualizarResumo();
        };
        _grade.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Space || _grade.CurrentRow is null) return;
            e.Handled = true;
            var celula = _grade.CurrentRow.Cells[0];
            celula.Value = celula.Value is not true;
        };
        cartao.Controls.Add(_grade);

        var rodape = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, 54), WrapContents = true, BackColor = Tema.Fundo, Padding = new Padding(0, 10, 0, 0) };
        _copias.Valor = 1;
        _copias.Margin = new Padding(0, 4, 16, 0);
        var todos = Ui.Botao("Marcar visíveis", Ui.Neutro, 150, 36, Icones.Ok);
        todos.Click += (_, _) => MarcarVisiveis(true);
        var nenhum = Ui.Botao("Desmarcar", Ui.Neutro, 120, 36, Icones.Cancelar);
        nenhum.Click += (_, _) => MarcarVisiveis(false);
        var visualizar = Ui.Botao("Visualizar", Ui.Neutro, 130, 36, Icones.Buscar);
        visualizar.Click += (_, _) => Imprimir(previa: true);
        var imprimir = Ui.Botao("Imprimir", null, 130, 36, Icones.Imprimir);
        imprimir.Click += (_, _) => Imprimir(previa: false);
        _resumo.Margin = new Padding(16, 10, 0, 0);
        rodape.Controls.AddRange([Rot("Cópias de cada"), _copias, todos, nenhum, visualizar, imprimir, _resumo]);

        Controls.Add(cartao);
        Controls.Add(rodape);
        Controls.Add(filtros);
        Controls.Add(cabecalho);
        Shown += async (_, _) => await CarregarAsync();
    }

    private async Task CarregarAsync()
    {
        try
        {
            UseWaitCursor = true;
            _produtos = await _api.GetAsync<List<EtiquetaApi>>("api/etiquetas");
            if (IsDisposed) return;
            _marcados.IntersectWith(_produtos.Select(p => p.Id));
            Mostrar();
        }
        catch (ApiException ex) { if (!IsDisposed) Ui.Erro(this, ex.Message); }
        finally { if (!IsDisposed) UseWaitCursor = false; }
    }

    private IEnumerable<EtiquetaApi> Filtrados()
    {
        var busca = Formatos.RemoverAcentos(_busca.Text.Trim()).ToLowerInvariant();
        return _produtos.Where(p =>
            (!_soPromocao.Checked || p.PrecoPromocional is not null) &&
            (busca.Length == 0 ||
             Formatos.RemoverAcentos(p.Descricao).ToLowerInvariant().Contains(busca) ||
             p.Codigo.Contains(busca, StringComparison.OrdinalIgnoreCase) ||
             (p.CodigoBarras ?? "").Contains(busca, StringComparison.Ordinal)));
    }

    private void Mostrar()
    {
        _grade.Rows.Clear();
        foreach (var p in Filtrados())
        {
            var preco = p.PrecoPromocional is { } promo
                ? $"{Formatos.Moeda(promo)}{Sufixo(p.Unidade)}"
                : $"{Formatos.Moeda(p.Preco)}{Sufixo(p.Unidade)}";
            var obs = new List<string>();
            if (p.PrecoPromocional is not null) obs.Add($"Promoção até {p.PromocaoAte:dd/MM}");
            if (p.PrecoAtacado is { } pa && p.AtacadoAPartirDe is { } min)
                obs.Add($"Atacado: a partir de {Formatos.Quantidade(min, p.Unidade)}, {Formatos.Moeda(pa)}" +
                        (p.AtacadoAte is { } ate ? $" (até {ate:dd/MM})" : ""));
            if (p.PrecoNovoNaoEnviado) obs.Add("preço novo ainda não enviado aos caixas");
            var i = _grade.Rows.Add(_marcados.Contains(p.Id), p.Codigo, p.Descricao, p.Unidade, preco, string.Join(" · ", obs));
            var linha = _grade.Rows[i];
            linha.Tag = p;
            if (p.PrecoPromocional is not null) linha.Cells[4].Style.ForeColor = Tema.Sucesso;
            if (p.PrecoNovoNaoEnviado) linha.Cells[5].Style.ForeColor = Tema.Alerta;
        }
        AtualizarResumo();
    }

    private void MarcarVisiveis(bool marcar)
    {
        foreach (DataGridViewRow r in _grade.Rows) r.Cells[0].Value = marcar;
        AtualizarResumo();
    }

    private void AtualizarResumo()
    {
        var pendentes = _produtos.Count(p => p.PrecoNovoNaoEnviado);
        _resumo.Text = $"{_marcados.Count} marcado(s) de {_produtos.Count}" +
                       (pendentes > 0 ? $"  ·  {pendentes} com preço novo não enviado (envie em \"Enviar produtos aos caixas\")" : "");
    }

    /// <summary>"/kg", "/L"... para quem é vendido por peso ou medida; nada para unidade.</summary>
    public static string Sufixo(string? unidade) =>
        Formatos.UnidadeFracionada(unidade) ? "/" + (unidade!.Trim().ToUpperInvariant() switch { "KG" => "kg", "G" => "g", "ML" => "ml", "M2" => "m²", var u => u }) : "";

    private void Imprimir(bool previa)
    {
        _grade.EndEdit();
        var copias = Math.Max(1, (int)_copias.Valor);
        var escolhidos = _produtos.Where(p => _marcados.Contains(p.Id))
            .SelectMany(p => Enumerable.Repeat(p, copias)).ToList();
        if (escolhidos.Count == 0)
        {
            Ui.Aviso(this, "Marque ao menos um produto na lista.");
            return;
        }
        var pendentes = escolhidos.Where(p => p.PrecoNovoNaoEnviado).Select(p => p.Descricao).Distinct().ToList();
        if (pendentes.Count > 0 && !Ui.Confirmar(this,
                $"{pendentes.Count} produto(s) marcado(s) têm preço novo que ainda não foi enviado aos caixas.\n\n" +
                "A etiqueta sai com o preço que o caixa cobra HOJE. Imprimir assim mesmo?"))
            return;

        using var doc = new PrintDocument { DocumentName = "Etiquetas de gôndola" };
        var indice = 0;
        doc.BeginPrint += (_, _) => indice = 0;
        doc.PrintPage += (_, e) =>
        {
            var g = e.Graphics!;
            var area = e.MarginBounds;
            const int colunas = 2, altura = 118, espaco = 12;
            var largura = (area.Width - espaco * (colunas - 1)) / colunas;
            var y = area.Top;
            while (indice < escolhidos.Count && y + altura <= area.Bottom)
            {
                for (var c = 0; c < colunas && indice < escolhidos.Count; c++)
                    DesenharEtiqueta(g, new Rectangle(area.Left + c * (largura + espaco), y, largura, altura), escolhidos[indice++]);
                y += altura + espaco;
            }
            e.HasMorePages = indice < escolhidos.Count;
        };

        if (!doc.PrinterSettings.IsValid)
        {
            Ui.Erro(this, "Não há impressora instalada neste computador.");
            return;
        }
        try
        {
            if (previa)
            {
                using var prev = new PrintPreviewDialog { Document = doc, Width = 900, Height = 700, UseAntiAlias = true };
                prev.ShowDialog(this);
            }
            else
            {
                using var dlg = new PrintDialog { Document = doc, UseEXDialog = true };
                if (dlg.ShowDialog(this) == DialogResult.OK) doc.Print();
            }
        }
        catch (InvalidPrinterException) { Ui.Erro(this, "Nenhuma impressora instalada neste computador."); }
    }

    private static void DesenharEtiqueta(Graphics g, Rectangle r, EtiquetaApi p)
    {
        using var nome = new Font("Segoe UI", 11, FontStyle.Bold);
        using var miudo = new Font("Segoe UI", 8);
        using var de = new Font("Segoe UI", 9, FontStyle.Strikeout);
        using var preco = new Font("Segoe UI", 24, FontStyle.Bold);
        using var unidade = new Font("Segoe UI", 11, FontStyle.Bold);
        using var borda = new Pen(Color.Black, 1.5f);
        g.DrawRectangle(borda, r);

        var texto = new RectangleF(r.Left + 8, r.Top + 6, r.Width - 16, 40);
        using (var formato = new StringFormat { Trimming = StringTrimming.EllipsisWord })
            g.DrawString(p.Descricao, nome, Brushes.Black, texto, formato);
        g.DrawString(string.IsNullOrEmpty(p.CodigoBarras) ? $"Cód. {p.Codigo}" : $"Cód. {p.Codigo}   {p.CodigoBarras}",
            miudo, Brushes.Black, r.Left + 8, r.Bottom - 20);

        var valor = p.PrecoPromocional ?? p.Preco;
        var sufixo = Sufixo(p.Unidade);
        var textoPreco = Formatos.Moeda(valor);
        var tamPreco = g.MeasureString(textoPreco, preco);
        var tamSufixo = sufixo.Length > 0 ? g.MeasureString(sufixo, unidade) : SizeF.Empty;
        var xPreco = r.Right - 8 - tamSufixo.Width - tamPreco.Width;
        var yPreco = r.Bottom - 8 - tamPreco.Height;
        g.DrawString(textoPreco, preco, Brushes.Black, xPreco, yPreco);
        if (sufixo.Length > 0)
            g.DrawString(sufixo, unidade, Brushes.Black, r.Right - 8 - tamSufixo.Width, yPreco + tamPreco.Height - tamSufixo.Height - 6);

        if (p.PrecoAtacado is { } pa && p.AtacadoAPartirDe is { } min)
        {
            // Atacado logo abaixo do nome, à esquerda (o preço de varejo fica embaixo à direita).
            using var atacado = new Font("Segoe UI", 9, FontStyle.Bold);
            var linha = $"ATACADO: a partir de {Formatos.Quantidade(min, p.Unidade)} {p.Unidade.ToLowerInvariant()} — {Formatos.Moeda(pa)}{sufixo}";
            using var corte = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter };
            g.DrawString(linha, atacado, Brushes.Black, new RectangleF(r.Left + 8, r.Top + 46, r.Width * 0.62f, 18), corte);
        }

        if (p.PrecoPromocional is not null)
        {
            g.DrawString($"PROMOÇÃO até {p.PromocaoAte:dd/MM}", miudo, Brushes.Black, r.Left + 8, r.Bottom - 36);
            if (p.PrecoPromocional < p.Preco)
            {
                var antes = $"De {Formatos.Moeda(p.Preco)}{sufixo}";
                var tam = g.MeasureString(antes, de);
                g.DrawString(antes, de, Brushes.Black, r.Right - 8 - tam.Width, yPreco - tam.Height + 6);
            }
        }
    }

    private sealed class EtiquetaApi
    {
        public Guid Id { get; set; }
        public string Codigo { get; set; } = "";
        public string? CodigoBarras { get; set; }
        public string Descricao { get; set; } = "";
        public string Unidade { get; set; } = "UN";
        public decimal Preco { get; set; }
        public decimal? PrecoPromocional { get; set; }
        public DateOnly? PromocaoAte { get; set; }
        public decimal? PrecoAtacado { get; set; }
        public decimal? AtacadoAPartirDe { get; set; }
        public DateOnly? AtacadoAte { get; set; }
        public bool PrecoNovoNaoEnviado { get; set; }
    }
}
