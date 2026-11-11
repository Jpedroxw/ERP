using ProjetoJP.Controles;
using ProjetoJP.Gestao.Servicos;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;

namespace ProjetoJP.Gestao.Telas;

/// <summary>
/// Cadastro de produto feito sob medida (os outros cadastros usam o formulário automático).
/// Três blocos — Identificação, Preços e Estoque —, margem de lucro calculada enquanto digita e o atacado
/// escondido atrás de "Vende no atacado" (a maioria dos produtos não usa). Montado com painéis de layout,
/// então a janela tem a altura do conteúdo e não corta em escala 125%/150%.
/// </summary>
public sealed class ProdutoEdicaoForm : Form
{
    private readonly ProdutoDto _dto;
    private readonly ApiClient _api;
    private readonly bool _verCusto;
    private readonly float _escala;

    private readonly CampoCodigoProduto _codigo = new();
    private readonly CampoFormatado _barras = new(null, 14);
    private readonly CampoTextoSeguro _descricao = new() { CharacterCasing = CharacterCasing.Upper, MaxLength = 150 };
    private readonly ComboBox _unidade = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _categoria = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CampoMoeda _custo = new();
    private readonly CampoMoeda _venda = new();
    private readonly Label _margem = Dica("");
    private readonly CheckBox _temAtacado = new() { Text = "Vende no atacado (preço menor a partir de uma quantidade)", AutoSize = true };
    private readonly CampoMoeda _precoAtacado = new();
    private readonly CampoNumero _qtdAtacado = new() { CasasDecimais = 0 };
    private readonly Label _margemAtacado = Dica("");
    private readonly Label _rotuloQtdAtacado = Rotulo("A partir de (quantidade no cupom)");
    private readonly TextBox _estoqueAtual = new() { ReadOnly = true, TabStop = false, TextAlign = HorizontalAlignment.Right };
    private readonly CampoNumero _estoqueMinimo = new() { CasasDecimais = 0 };
    private readonly CheckBox _ativo = new() { Text = "Ativo (aparece no caixa depois de enviado)", AutoSize = true };

    private readonly TableLayoutPanel _tabela;
    private readonly FlowLayoutPanel _rodape;
    private readonly Control _linhaAtacado;

    public ProdutoEdicaoForm(string titulo, ProdutoDto dto, ApiClient api)
    {
        _dto = dto;
        _api = api;
        _verCusto = api.Pode(Permissoes.VerCusto);
        Ui.Dialogo(this, titulo, 640, 600);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        AutoScaleMode = AutoScaleMode.None;
        _escala = DeviceDpi / 96f;

        foreach (var u in Formatos.Unidades) _unidade.Items.Add(u);
        var fonte = Tema.Fonte(10.5f);
        foreach (var c in new Control[] { _codigo, _barras, _descricao, _unidade, _categoria, _custo, _venda, _precoAtacado, _qtdAtacado, _estoqueAtual, _estoqueMinimo })
            c.Font = fonte;
        _temAtacado.Font = _ativo.Font = Tema.Fonte(10);
        _temAtacado.ForeColor = _ativo.ForeColor = Tema.Texto;

        _tabela = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2,
            Padding = new Padding(Px(24), Px(10), Px(24), Px(8)), BackColor = Tema.Superficie
        };
        _tabela.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _tabela.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        // ---- Identificação
        Secao("Identificação", primeira: true);
        Duas(Bloco("Código", _codigo, Dica("em branco = gera automático")),
             Bloco("Código de barras", _barras, Dica("EAN de 8, 12, 13 ou 14 dígitos")));
        Inteira(Bloco("Descrição *", _descricao, cheia: true));
        Duas(Bloco("Unidade", _unidade, Dica("KG, G, L, ML, M, M2 aceitam fração")),
             Bloco("Categoria", _categoria));

        // ---- Preços
        Secao("Preços");
        if (_verCusto)
            Duas(Bloco("Preço de custo", _custo), Bloco("Preço de venda *", _venda, _margem));
        else
            Duas(Bloco("Preço de venda *", _venda, _margem), new Panel { Size = Size.Empty, Margin = Padding.Empty });
        _temAtacado.Margin = new Padding(0, Px(6), 0, Px(4));
        Inteira(_temAtacado);
        var atacado = new TableLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, Px(4)), Padding = new Padding(Px(18), Px(2), 0, Px(4)), BackColor = Tema.SuperficieAlternada
        };
        // Faixa verde à esquerda: deixa claro que os dois campos pertencem a "Vende no atacado".
        atacado.Paint += (_, e) =>
        {
            using var faixa = new SolidBrush(Tema.Primaria);
            e.Graphics.FillRectangle(faixa, 0, 0, Px(3), atacado.Height);
        };
        atacado.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        atacado.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        atacado.Controls.Add(Bloco("Preço de atacado", _precoAtacado, _margemAtacado, largura: 250), 0, 0);
        atacado.Controls.Add(Bloco(_rotuloQtdAtacado, _qtdAtacado, null, largura: 250), 1, 0);
        _linhaAtacado = atacado;
        Inteira(atacado);

        // ---- Estoque
        Secao("Estoque");
        Duas(Bloco("Estoque atual", _estoqueAtual, Dica("muda pelo menu Estoque")),
             Bloco("Estoque mínimo", _estoqueMinimo, Dica("avisa quando chegar nele")));
        _ativo.Margin = new Padding(0, Px(6), 0, Px(4));
        Inteira(_ativo);

        // ---- Rodapé
        _rodape = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, BackColor = Tema.Fundo, Padding = new Padding(Px(20), Px(12), Px(20), Px(12))
        };
        var salvar = Ui.Botao("Salvar", largura: Px(140), altura: Px(38), icone: Icones.Salvar);
        salvar.Margin = new Padding(Px(10), 0, 0, 0);
        salvar.Click += (_, _) => Salvar();
        var cancelar = Ui.Botao("Cancelar", Ui.Neutro, Px(120), Px(38));
        cancelar.Margin = Padding.Empty;
        cancelar.DialogResult = DialogResult.Cancel;
        _rodape.Controls.Add(salvar);
        _rodape.Controls.Add(cancelar);

        Controls.Add(_tabela);
        Controls.Add(_rodape);
        AcceptButton = salvar;
        CancelButton = cancelar;

        Preencher();

        _unidade.SelectedIndexChanged += (_, _) => { AplicarUnidade(); AtualizarMargens(); };
        _custo.TextChanged += (_, _) => AtualizarMargens();
        _venda.TextChanged += (_, _) => AtualizarMargens();
        _precoAtacado.TextChanged += (_, _) => AtualizarMargens();
        _qtdAtacado.TextChanged += (_, _) => AtualizarMargens();
        _temAtacado.CheckedChanged += (_, _) =>
        {
            _linhaAtacado.Visible = _temAtacado.Checked;
            if (_temAtacado.Checked && _qtdAtacado.Valor <= 0)
                _qtdAtacado.Valor = Formatos.UnidadeFracionada(_unidade.SelectedItem as string) ? 1 : 6;
            AtualizarMargens();
            AjustarAltura();
            if (_temAtacado.Checked) _precoAtacado.Focus();
        };
        Shown += async (_, _) =>
        {
            (_dto.Id == Guid.Empty ? (Control)_descricao : _venda).Focus();
            await CarregarCategoriasAsync();
        };
    }

    private int Px(int v) => (int)Math.Round(v * _escala);

    protected override void OnLoad(EventArgs e)
    {
        AjustarAltura();
        base.OnLoad(e);
    }

    /// <summary>Altura pelo conteúdo (o atacado aparece e some), sem passar da área útil da tela.</summary>
    private void AjustarAltura()
    {
        var area = Screen.FromControl(Owner ?? this).WorkingArea;
        ClientSize = new Size(Math.Min(Px(640), area.Width - 40), ClientSize.Height);
        _tabela.PerformLayout();
        var altura = _tabela.PreferredSize.Height + _rodape.PreferredSize.Height;
        ClientSize = new Size(ClientSize.Width, Math.Min(altura, area.Height - 60));
        AutoScroll = altura > area.Height - 60;
    }

    // ---------- Montagem ----------

    private static Label Rotulo(string texto) => new()
    {
        Text = texto, AutoSize = true, Font = Tema.Fonte(9.5f), ForeColor = Tema.TextoSuave, Margin = new Padding(0, 4, 0, 2)
    };

    private static Label Dica(string texto) => new()
    {
        Text = texto, AutoSize = true, Font = Tema.Fonte(8.5f), ForeColor = Tema.TextoSuave, Margin = new Padding(0, 2, 0, 0)
    };

    private Control Bloco(string rotulo, Control controle, Label? dica = null, bool cheia = false, int largura = 270) =>
        Bloco(Rotulo(rotulo), controle, dica, cheia ? 560 : largura);

    private Control Bloco(Label rotulo, Control controle, Label? dica, int largura)
    {
        var p = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, Px(10), Px(6)), BackColor = Color.Transparent
        };
        controle.Width = Px(largura);
        controle.Margin = Padding.Empty;
        p.Controls.Add(rotulo);
        p.Controls.Add(controle);
        if (dica is not null)
        {
            dica.MaximumSize = new Size(Px(largura), 0);
            p.Controls.Add(dica);
        }
        return p;
    }

    private void Secao(string titulo, bool primeira = false)
    {
        var l = new Label
        {
            Text = titulo, AutoSize = true, Font = Tema.Semibold(11.5f), ForeColor = Tema.Primaria,
            Margin = new Padding(0, primeira ? Px(4) : Px(14), 0, Px(2))
        };
        Inteira(l);
        Inteira(new Panel { Height = 1, Anchor = AnchorStyles.Left | AnchorStyles.Right, BackColor = Tema.Borda, Margin = new Padding(0, 0, 0, Px(6)) });
    }

    private void Inteira(Control c)
    {
        var linha = _tabela.RowCount;
        _tabela.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _tabela.RowCount = linha + 1;
        _tabela.Controls.Add(c, 0, linha);
        _tabela.SetColumnSpan(c, 2);
    }

    private void Duas(Control a, Control b)
    {
        var linha = _tabela.RowCount;
        _tabela.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _tabela.RowCount = linha + 1;
        _tabela.Controls.Add(a, 0, linha);
        _tabela.Controls.Add(b, 1, linha);
    }

    // ---------- Dados ----------

    private void Preencher()
    {
        _codigo.Text = _dto.Codigo ?? "";
        _barras.Text = _dto.CodigoBarras ?? "";
        _descricao.Text = _dto.Descricao ?? "";
        var unidade = (_dto.Unidade ?? "UN").Trim().ToUpperInvariant();
        _unidade.SelectedItem = _unidade.Items.Contains(unidade) ? unidade : "UN";
        _custo.Valor = _dto.PrecoCusto;
        _venda.Valor = _dto.PrecoVenda;
        AplicarUnidade();
        _temAtacado.Checked = _dto.PrecoAtacado > 0 && _dto.QtdMinimaAtacado > 0;
        _precoAtacado.Valor = _dto.PrecoAtacado;
        _qtdAtacado.Valor = _dto.QtdMinimaAtacado;
        _linhaAtacado.Visible = _temAtacado.Checked;
        _estoqueAtual.Text = Formatos.Quantidade(_dto.EstoqueAtual, unidade);
        _estoqueMinimo.Valor = _dto.EstoqueMinimo;
        _ativo.Checked = _dto.Id == Guid.Empty || _dto.Ativo;
        _categoria.Items.Add(new LookupItem { Id = _dto.CategoriaId ?? Guid.Empty, Nome = _dto.CategoriaNome ?? "(nenhuma)" });
        _categoria.SelectedIndex = 0;
        AtualizarMargens();
    }

    private async Task CarregarCategoriasAsync()
    {
        try
        {
            var itens = await _api.GetAsync<List<LookupItem>>("api/categorias");
            if (IsDisposed) return;
            var atual = (_categoria.SelectedItem as LookupItem)?.Id ?? Guid.Empty;
            _categoria.Items.Clear();
            _categoria.Items.Add(new LookupItem { Id = Guid.Empty, Nome = "(nenhuma)" });
            foreach (var i in itens.OrderBy(i => i.Nome)) _categoria.Items.Add(i);
            _categoria.SelectedItem = _categoria.Items.Cast<LookupItem>().FirstOrDefault(i => i.Id == atual) ?? _categoria.Items[0];
        }
        catch (ApiException ex) { if (!IsDisposed) Ui.Erro(this, ex.Message); }
    }

    /// <summary>KG, L, M... com 3 casas; UN, CX, PC... inteiros.</summary>
    private void AplicarUnidade()
    {
        var unidade = _unidade.SelectedItem as string ?? "UN";
        var casas = Formatos.CasasQuantidade(unidade);
        _qtdAtacado.CasasDecimais = casas;
        _estoqueMinimo.CasasDecimais = casas;
        _rotuloQtdAtacado.Text = $"A partir de quantos {unidade} no cupom";
    }

    /// <summary>Lucro e margem enquanto digita: no varejo e no atacado (quem não vê custo vê só o desconto do atacado).</summary>
    private void AtualizarMargens()
    {
        var unidade = _unidade.SelectedItem as string ?? "UN";
        var venda = _venda.Valor;
        var custo = _custo.Valor;

        if (venda <= 0)
            Pintar(_margem, "precisa de preço para ir ao caixa", Tema.Alerta);
        else if (!_verCusto || custo <= 0)
            Pintar(_margem, _verCusto ? "informe o custo para ver a margem" : "", Tema.TextoSuave);
        else
            Pintar(_margem, TextoMargem(venda, custo, unidade), venda < custo ? Tema.Perigo : Tema.Sucesso);

        var atacado = _precoAtacado.Valor;
        if (!_temAtacado.Checked || atacado <= 0)
        {
            Pintar(_margemAtacado, "", Tema.TextoSuave);
            return;
        }
        if (venda > 0 && atacado >= venda)
        {
            Pintar(_margemAtacado, "precisa ser menor que o preço de venda", Tema.Perigo);
            return;
        }
        var economia = venda > 0 ? $"cliente economiza {(1 - atacado / venda) * 100:0}%" : "";
        if (_verCusto && custo > 0)
            Pintar(_margemAtacado, TextoMargem(atacado, custo, unidade) + (economia.Length > 0 ? $" · {economia}" : ""),
                atacado < custo ? Tema.Perigo : Tema.Sucesso);
        else
            Pintar(_margemAtacado, economia, Tema.Sucesso);
    }

    private static string TextoMargem(decimal preco, decimal custo, string unidade)
    {
        var lucro = preco - custo;
        if (lucro < 0) return $"abaixo do custo: prejuízo de {Formatos.Moeda(-lucro)} por {unidade}";
        return $"lucro {Formatos.Moeda(lucro)} por {unidade} · margem {lucro / preco * 100:0.#}%";
    }

    private static void Pintar(Label l, string texto, Color cor)
    {
        l.Text = texto;
        l.ForeColor = cor;
    }

    private void Salvar()
    {
        var descricao = _descricao.Text.Trim();
        if (descricao.Length == 0) { Aviso("Preencha a descrição do produto.", _descricao); return; }
        if (!Formatos.TextoSeguro(descricao)) { Aviso("A descrição contém caracteres inválidos.", _descricao); return; }

        var codigo = _codigo.Text.Trim().ToUpperInvariant();
        // Código antigo fora da regra nova continua valendo enquanto não for trocado (pode estar em etiqueta/balança).
        if (codigo.Length > 0 && codigo != (_dto.Codigo ?? "") && !Formatos.CodigoProdutoValido(codigo))
        { Aviso("Código inválido: use letras, números, ponto, hífen, barra ou sublinhado (até 30).", _codigo); return; }

        var barras = _barras.Limpo;
        if (barras.Length > 0 && !Formatos.GtinValido(barras))
        { Aviso("Código de barras inválido: precisa ter 8, 12, 13 ou 14 dígitos e o dígito verificador correto.", _barras); return; }

        var unidade = _unidade.SelectedItem as string ?? "UN";
        decimal precoAtacado = 0, qtdAtacado = 0;
        if (_temAtacado.Checked)
        {
            precoAtacado = _precoAtacado.Valor;
            qtdAtacado = _qtdAtacado.Valor;
            if (precoAtacado <= 0) { Aviso("Informe o preço de atacado (ou desmarque \"Vende no atacado\").", _precoAtacado); return; }
            if (Atacado.Validar(unidade, _venda.Valor, precoAtacado, qtdAtacado) is { } erro)
            { Aviso(erro, qtdAtacado <= 0 || erro.Contains("quantidade") || erro.Contains("unidades") ? _qtdAtacado : _precoAtacado); return; }
        }

        _dto.Codigo = codigo;
        _dto.CodigoBarras = barras.Length == 0 ? null : barras;
        _dto.Descricao = descricao;
        _dto.Unidade = unidade;
        var categoria = _categoria.SelectedItem as LookupItem;
        _dto.CategoriaId = categoria is null || categoria.Id == Guid.Empty ? null : categoria.Id;
        _dto.CategoriaNome = categoria is null || categoria.Id == Guid.Empty ? null : categoria.Nome;
        if (_verCusto) _dto.PrecoCusto = _custo.Valor; // sem permissão, o servidor mantém o custo gravado
        _dto.PrecoVenda = _venda.Valor;
        _dto.PrecoAtacado = precoAtacado;
        _dto.QtdMinimaAtacado = qtdAtacado;
        _dto.EstoqueMinimo = _estoqueMinimo.Valor;
        _dto.Ativo = _ativo.Checked;
        DialogResult = DialogResult.OK;
    }

    private void Aviso(string msg, Control campo)
    {
        Ui.Aviso(this, msg);
        campo.Focus();
    }
}
