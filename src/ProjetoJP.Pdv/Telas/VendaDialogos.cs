using ProjetoJP.Controles;
using ProjetoJP.Pdv.Dados;
using ProjetoJP.Pdv.Servicos;
using ProjetoJP.Shared;

namespace ProjetoJP.Pdv.Telas;

/// <summary>
/// Alterar quantidade e preço de um item da venda (a troca de preço é autorizada por quem chamou), com o total do
/// item na hora. "Remover item" fecha com DialogResult.Abort.
/// </summary>
public class EditarItemForm : Form
{
    private readonly CampoNumero _quantidade = new() { Font = Tema.Semibold(18) };
    private readonly CampoMoeda _preco = new() { Font = Tema.Semibold(18) };
    private readonly Label _totalItem = new() { AutoSize = false, Font = Tema.Semibold(20), ForeColor = Tema.Texto, TextAlign = ContentAlignment.MiddleRight };
    private readonly string _unidade;

    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public decimal Quantidade { get; private set; }
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public decimal Preco { get; private set; }

    public EditarItemForm(string descricao, string unidade, decimal quantidade, decimal preco, string teclaRemover = "")
    {
        _unidade = unidade;
        Ui.Dialogo(this, "Alterar item", 500, 340);

        var titulo = new Label
        {
            Text = descricao, Font = Tema.Semibold(14), ForeColor = Tema.Texto, AutoSize = false, AutoEllipsis = true,
            Location = new Point(24, 18), Size = new Size(452, 30)
        };
        var lq = Ui.Rotulo($"Quantidade ({unidade})", 10);
        lq.ForeColor = Tema.TextoSuave;
        lq.Location = new Point(24, 64);
        _quantidade.CasasDecimais = Formatos.CasasQuantidade(unidade);
        _quantidade.Maximo = 9999;
        _quantidade.Valor = quantidade;
        _quantidade.SetBounds(24, 88, 210, 44);

        var lp = Ui.Rotulo("Preço unitário", 10);
        lp.ForeColor = Tema.TextoSuave;
        lp.Location = new Point(254, 64);
        _preco.Valor = preco;
        _preco.SetBounds(254, 88, 222, 44);

        var lt = Ui.Rotulo("Total do item", 12);
        lt.Location = new Point(24, 158);
        _totalItem.SetBounds(200, 148, 276, 40);

        var dica = Ui.Rotulo("Mudar o preço pede a autorização de quem tem permissão para alterar preço.", 9);
        dica.ForeColor = Tema.TextoSuave;
        dica.MaximumSize = new Size(452, 0);
        dica.Location = new Point(24, 202);

        var remover = Ui.Botao(teclaRemover.Length > 0 ? $"Remover item ({teclaRemover})" : "Remover item", Ui.Neutro, 190, 46);
        remover.ForeColor = Tema.Perigo;
        remover.Location = new Point(24, 270);
        remover.Click += (_, _) => DialogResult = DialogResult.Abort;
        var cancelar = Ui.Botao("Cancelar", Ui.Neutro, 120, 46);
        cancelar.Location = new Point(224, 270);
        cancelar.DialogResult = DialogResult.Cancel;
        var ok = Ui.Botao("Salvar (Enter)", largura: 122, altura: 46);
        ok.Location = new Point(354, 270);
        ok.Click += (_, _) => Confirmar();

        Controls.AddRange([titulo, lq, _quantidade, lp, _preco, lt, _totalItem, dica, remover, cancelar, ok]);
        AcceptButton = ok;
        CancelButton = cancelar;
        _quantidade.TextChanged += (_, _) => AtualizarTotal();
        _preco.TextChanged += (_, _) => AtualizarTotal();
        AtualizarTotal();
        Shown += (_, _) => { _quantidade.Focus(); _quantidade.SelectAll(); };
    }

    private void AtualizarTotal() => _totalItem.Text = Ui.Dinheiro(Math.Round(_quantidade.Valor * _preco.Valor, 2));

    private void Confirmar()
    {
        var q = _quantidade.Valor;
        if (q <= 0) { Ui.Aviso(this, "Informe a quantidade."); _quantidade.Focus(); return; }
        if (!Formatos.UnidadeFracionada(_unidade) && q != decimal.Truncate(q))
        {
            Ui.Aviso(this, $"Produto vendido em {_unidade}: a quantidade precisa ser inteira.");
            _quantidade.Focus();
            return;
        }
        if (_preco.Valor <= 0) { Ui.Aviso(this, "Informe o preço."); _preco.Focus(); return; }
        Quantidade = Math.Round(q, Formatos.CasasQuantidade(_unidade));
        Preco = _preco.Valor;
        DialogResult = DialogResult.OK;
    }
}

/// <summary>Formas de pagamento que não couberam nos botões F7 a F11 ("Outros").</summary>
public class EscolherFormaForm : Form
{
    private readonly ListBox _lista = new() { Font = Ui.Fonte(13), IntegralHeight = false };

    public FormaLocal? Escolhida { get; private set; }

    public EscolherFormaForm(List<FormaLocal> formas)
    {
        Ui.Dialogo(this, "Outras formas de pagamento", 360, 340);
        _lista.SetBounds(20, 20, 320, 240);
        foreach (var f in formas) _lista.Items.Add(f);
        if (formas.Count > 0) _lista.SelectedIndex = 0;
        var ok = Ui.Botao("Escolher", largura: 150);
        ok.Location = new Point(20, 276);
        ok.Click += (_, _) => Escolher();
        var cancelar = Ui.Botao("Cancelar", Ui.Neutro, 150);
        cancelar.Location = new Point(190, 276);
        cancelar.DialogResult = DialogResult.Cancel;
        _lista.DoubleClick += (_, _) => Escolher();
        Controls.AddRange([_lista, ok, cancelar]);
        AcceptButton = ok;
        CancelButton = cancelar;
    }

    private void Escolher()
    {
        if (_lista.SelectedItem is not FormaLocal f) return;
        Escolhida = f;
        DialogResult = DialogResult.OK;
    }
}

/// <summary>Orçamentos salvos neste caixa: vender, alterar, imprimir ou cancelar.</summary>
public class OrcamentosCaixaForm : Form
{
    private readonly PdvConfig _cfg;
    private readonly UsuarioLocal _operador;
    private readonly string _terminal;
    private readonly TextBox _busca = Ui.Caixa(12);
    private readonly DataGridView _grade = Ui.Grade();
    private List<OrcamentoLocal> _lista = [];

    public OrcamentoCompleto? Escolhido { get; private set; }
    /// <summary>true = colocar na venda; false = abrir para alterar o orçamento.</summary>
    public bool ParaVenda { get; private set; }

    public OrcamentosCaixaForm(PdvConfig cfg, UsuarioLocal operador, string terminal)
    {
        _cfg = cfg;
        _operador = operador;
        _terminal = terminal;
        // 944 de largura: cabe numa tela 1024×768 com as bordas da janela.
        Ui.Dialogo(this, "Orçamentos deste caixa", 944, 560);
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(960, 460);

        var lb = Ui.Rotulo("Pesquisar por número, cliente ou observação");
        lb.Location = new Point(20, 14);
        _busca.SetBounds(20, 38, 400, 30);
        _busca.TextChanged += (_, _) => Carregar();

        _grade.SetBounds(20, 80, 904, 390);
        _grade.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _grade.Columns.Add(Ui.Coluna("Nº", 8, true));
        _grade.Columns.Add(Ui.Coluna("Data", 17));
        _grade.Columns.Add(Ui.Coluna("Cliente", 33));
        _grade.Columns.Add(Ui.Coluna("Total", 15, true));
        _grade.Columns.Add(Ui.Coluna("Validade", 13));
        _grade.Columns.Add(Ui.Coluna("Situação", 14));
        _grade.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Usar(paraVenda: true); };

        var botoes = new FlowLayoutPanel
        {
            Location = new Point(20, 484), Size = new Size(904, 50), WrapContents = false,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        var vender = Ui.Botao("Vender", largura: 130, icone: Icones.Carrinho);
        vender.Click += (_, _) => Usar(paraVenda: true);
        var alterar = Ui.Botao("Alterar", Ui.Neutro, 120, icone: Icones.Editar);
        alterar.Click += (_, _) => Usar(paraVenda: false);
        var imprimir = Ui.Botao("Imprimir cupom", Ui.Neutro, 160, icone: Icones.Imprimir);
        imprimir.Click += (_, _) => Imprimir();
        var folha = Ui.Botao("Folha A4", Ui.Neutro, 120, icone: Icones.Documento);
        folha.Click += (_, _) => ImprimirA4();
        var cancelarOrc = Ui.Botao("Cancelar orçamento", Ui.Neutro, 190, icone: Icones.Cancelar);
        cancelarOrc.Click += (_, _) => CancelarOrcamento();
        var fechar = Ui.Botao("Fechar", Ui.Neutro, 110);
        fechar.DialogResult = DialogResult.Cancel;
        foreach (var b in new Control[] { vender, alterar, imprimir, folha, cancelarOrc, fechar }) b.Margin = new Padding(0, 0, 10, 0);
        botoes.Controls.AddRange([vender, alterar, imprimir, folha, cancelarOrc, fechar]);

        Controls.AddRange([lb, _busca, _grade, botoes]);
        CancelButton = fechar;
        Resize += (_, _) =>
        {
            _grade.Size = new Size(ClientSize.Width - 40, ClientSize.Height - 80 - 76);
            botoes.Location = new Point(20, ClientSize.Height - 66);
        };
        Shown += (_, _) => { Carregar(); _busca.Focus(); };
    }

    private static string Situacao(int status) => (StatusOrcamento)status switch
    {
        StatusOrcamento.Aberto => "Aberto",
        StatusOrcamento.Convertido => "Vendido",
        StatusOrcamento.Cancelado => "Cancelado",
        _ => ""
    };

    private void Carregar()
    {
        try
        {
            _lista = Repositorio.ListarOrcamentos(_busca.Text);
        }
        catch (Exception ex)
        {
            Ui.Erro(this, "Não foi possível ler os orçamentos: " + ex.Message);
            return;
        }
        _grade.Rows.Clear();
        foreach (var o in _lista)
        {
            var validade = DateOnly.TryParseExact(o.Validade, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var v)
                ? v.ToString("dd/MM/yyyy") + (o.Status == (int)StatusOrcamento.Aberto && v < Relogio.Hoje ? " (vencido)" : "")
                : "—";
            var i = _grade.Rows.Add(o.Numero, Db.ParaData(o.Data).ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                o.ClienteNome ?? "Consumidor final", Ui.Dinheiro(o.Total), validade, Situacao(o.Status));
            _grade.Rows[i].Tag = o;
            if (o.Status != (int)StatusOrcamento.Aberto) _grade.Rows[i].DefaultCellStyle.ForeColor = Tema.TextoSuave;
        }
    }

    private OrcamentoLocal? Selecionado => _grade.CurrentRow?.Tag as OrcamentoLocal;

    private void Usar(bool paraVenda)
    {
        if (Selecionado is not { } o) return;
        if (o.Status != (int)StatusOrcamento.Aberto)
        {
            Ui.Aviso(this, $"O orçamento nº {o.Numero} está {Situacao(o.Status).ToLowerInvariant()}: só orçamento aberto pode ser vendido ou alterado.");
            return;
        }
        var completo = Repositorio.CarregarOrcamento(o.Id);
        if (completo is null) return;
        Escolhido = completo;
        ParaVenda = paraVenda;
        DialogResult = DialogResult.OK;
    }

    private void Imprimir()
    {
        if (Selecionado is not { } o || Repositorio.CarregarOrcamento(o.Id) is not { } completo) return;
        var operador = Repositorio.UsuarioPorId(o.UsuarioId)?.Nome ?? _operador.Nome;
        var erro = Cupom.ImprimirOrcamento(_cfg, completo, operador, _terminal);
        if (erro is not null) Ui.Aviso(this, "A impressão falhou:\n" + erro);
    }

    /// <summary>Folha A4: mostra antes e deixa escolher a impressora (ou "Microsoft Print to PDF" para mandar por e-mail/WhatsApp).</summary>
    private void ImprimirA4()
    {
        if (Selecionado is not { } o || Repositorio.CarregarOrcamento(o.Id) is not { } completo) return;
        var operador = Repositorio.UsuarioPorId(o.UsuarioId)?.Nome ?? _operador.Nome;
        OrcamentoFolha.Visualizar(this, _cfg, completo, operador, _terminal);
    }

    private void CancelarOrcamento()
    {
        if (Selecionado is not { } o) return;
        if (o.Status != (int)StatusOrcamento.Aberto) { Ui.Aviso(this, "Só orçamento aberto pode ser cancelado."); return; }
        if (!Ui.Confirmar(this, $"Cancelar o orçamento nº {o.Numero}?")) return;
        Repositorio.AlterarStatusOrcamento(o.Id, StatusOrcamento.Cancelado, null);
        Carregar();
    }
}

/// <summary>
/// Desconto na venda toda, em % ou em R$, com atalhos (2%, 5%, 10%, arredondar o total) e o total novo na hora.
/// Zero tira o desconto. A autorização é pedida por quem chamou.
/// </summary>
public class DescontoForm : Form
{
    private readonly decimal _subtotal;
    private readonly BotaoTema _emReais = new() { Text = "R$ (valor)", Font = Tema.Semibold(11), TabStop = false };
    private readonly BotaoTema _emPercentual = new() { Text = "% (porcentagem)", Font = Tema.Semibold(11), TabStop = false };
    private readonly Label _rotuloValor = new() { AutoSize = true, Font = Tema.Fonte(10), ForeColor = Tema.TextoSuave };
    private readonly CampoNumero _valor = new() { CasasDecimais = 2, Font = Tema.Semibold(20) };
    private readonly Label _novoTotal = new() { AutoSize = false, Font = Tema.Semibold(20), ForeColor = Tema.Texto, TextAlign = ContentAlignment.MiddleRight };
    private readonly Label _antes = new() { AutoSize = false, Font = Tema.Fonte(12, FontStyle.Strikeout), ForeColor = Tema.TextoSuave, TextAlign = ContentAlignment.MiddleRight };
    private bool _percentual = true;

    /// <summary>Porcentagem escolhida (null = em reais).</summary>
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public decimal? Percentual { get; private set; }
    /// <summary>O desconto em reais (0 = sem desconto).</summary>
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public decimal EmReais { get; private set; }

    public DescontoForm(decimal subtotal, decimal descontoAtual, decimal? percentualAtual)
    {
        _subtotal = subtotal;
        Ui.Dialogo(this, "Desconto", 540, 420);

        var titulo = new Label { Text = "Desconto na venda toda", Font = Tema.Semibold(15), ForeColor = Tema.Texto, AutoSize = true, Location = new Point(24, 18) };
        _emReais.SetBounds(24, 62, 240, 42);
        _emPercentual.SetBounds(276, 62, 240, 42);
        _emReais.Click += (_, _) => TrocarTipo(false, limpar: true);
        _emPercentual.Click += (_, _) => TrocarTipo(true, limpar: true);

        _rotuloValor.Location = new Point(24, 120);
        _valor.SetBounds(24, 144, 220, 46);
        _valor.TextChanged += (_, _) => AtualizarTotal();

        // Atalhos
        var atalhos = new FlowLayoutPanel { Location = new Point(258, 144), Size = new Size(270, 50), WrapContents = false, BackColor = Tema.Superficie };
        foreach (var p in new[] { 2m, 5m, 10m })
        {
            var b = new BotaoTema { Text = p.ToString("0", Ui.Br) + "%", Estilo = EstiloBotao.Secundario, Size = new Size(70, 46), Font = Tema.Semibold(11), TabStop = false, Margin = new Padding(0, 0, 8, 0) };
            var valor = p;
            b.Click += (_, _) => { TrocarTipo(true, limpar: false); _valor.Valor = valor; _valor.Focus(); _valor.SelectAll(); };
            atalhos.Controls.Add(b);
        }
        Controls.Add(atalhos);
        var centavos = subtotal - decimal.Floor(subtotal);
        if (centavos > 0 && centavos < subtotal)
        {
            var arredondar = new BotaoTema
            {
                Text = $"Arredondar o total para {Ui.Dinheiro(decimal.Floor(subtotal))}", Estilo = EstiloBotao.Secundario,
                Font = Tema.Fonte(10.5f), TabStop = false, Location = new Point(24, 204), Size = new Size(300, 38)
            };
            arredondar.Click += (_, _) => { TrocarTipo(false, limpar: false); _valor.Valor = centavos; _valor.Focus(); _valor.SelectAll(); };
            Controls.Add(arredondar);
        }
        var tirar = new BotaoTema { Text = "Sem desconto", Estilo = EstiloBotao.Secundario, Font = Tema.Fonte(10.5f), TabStop = false, Location = new Point(334, 204), Size = new Size(182, 38) };
        tirar.Click += (_, _) => { _valor.Valor = 0; _valor.Focus(); _valor.SelectAll(); };

        var lTotal = Ui.Rotulo("Total com desconto", 12);
        lTotal.Location = new Point(24, 270);
        _antes.SetBounds(200, 252, 316, 22);
        _novoTotal.SetBounds(200, 272, 316, 40);

        var aviso = Ui.Rotulo("O desconto pede a autorização de quem pode dar desconto.", 9);
        aviso.ForeColor = Tema.TextoSuave;
        aviso.Location = new Point(24, 318);

        var cancelar = Ui.Botao("Cancelar", Ui.Neutro, 150, 46);
        cancelar.Location = new Point(220, 356);
        cancelar.DialogResult = DialogResult.Cancel;
        var aplicar = Ui.Botao("Aplicar (Enter)", largura: 146, altura: 46);
        aplicar.Location = new Point(370, 356);
        aplicar.Click += (_, _) => Aplicar();

        Controls.AddRange([titulo, _emReais, _emPercentual, _rotuloValor, _valor, tirar, lTotal, _antes, _novoTotal, aviso, cancelar, aplicar]);
        AcceptButton = aplicar;
        CancelButton = cancelar;

        // Abre como estava: percentual, ou valor em reais.
        TrocarTipo(percentualAtual is not null || descontoAtual == 0, limpar: false);
        _valor.Valor = percentualAtual ?? descontoAtual;
        AtualizarTotal();
        Shown += (_, _) => { _valor.Focus(); _valor.SelectAll(); };
    }

    private void TrocarTipo(bool percentual, bool limpar)
    {
        _percentual = percentual;
        _emPercentual.Estilo = percentual ? EstiloBotao.Primario : EstiloBotao.Secundario;
        _emReais.Estilo = percentual ? EstiloBotao.Secundario : EstiloBotao.Primario;
        _rotuloValor.Text = percentual ? "Porcentagem (%)" : "Valor do desconto (R$)";
        _valor.Maximo = percentual ? 99.99m : Math.Max(0, _subtotal - 0.01m);
        if (limpar) _valor.Valor = 0;
        AtualizarTotal();
        _valor.Focus();
        _valor.SelectAll();
    }

    private decimal Calcular() => _percentual ? Math.Round(_subtotal * _valor.Valor / 100m, 2) : Math.Round(_valor.Valor, 2);

    private void AtualizarTotal()
    {
        var desconto = Calcular();
        _antes.Text = desconto > 0 ? Ui.Dinheiro(_subtotal) : "";
        _novoTotal.Text = Ui.Dinheiro(Math.Max(0, _subtotal - desconto));
        _novoTotal.ForeColor = desconto >= _subtotal && desconto > 0 ? Tema.Perigo : Tema.Texto;
    }

    private void Aplicar()
    {
        var desconto = Calcular();
        if (desconto >= _subtotal && desconto > 0) { Ui.Aviso(this, "O desconto precisa ser menor que o subtotal."); _valor.Focus(); return; }
        EmReais = desconto;
        Percentual = _percentual && desconto > 0 ? _valor.Valor : null;
        DialogResult = DialogResult.OK;
    }
}

/// <summary>
/// "Mais opções" (F1): todas as ações do caixa em botões grandes, cada um com a sua tecla. Clique ou aperte a tecla;
/// a ação é feita por quem abriu, depois que a janela fecha.
/// </summary>
public class MaisOpcoesForm : Form
{
    /// <param name="TeclaTexto">Texto da tecla quando não é uma tecla configurável (ex.: "Enter").</param>
    public sealed record Item(Keys Tecla, string Titulo, string Descricao, Action Acao, string? TeclaTexto = null, bool Perigo = false);

    private readonly List<Item> _itens;
    private readonly string _teclaFechar;

    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Item? Escolhido { get; private set; }

    public MaisOpcoesForm(List<Item> itens, string teclaFechar)
    {
        _itens = itens;
        _teclaFechar = teclaFechar;
        const int colunas = 3;
        var linhas = (itens.Count + colunas - 1) / colunas;
        const int alturaLinha = 64;
        Ui.Dialogo(this, "Mais opções", 980, 96 + linhas * alturaLinha + 70);

        var titulo = new Label { Text = "Mais opções", Font = Tema.Semibold(16), ForeColor = Tema.Texto, AutoSize = true, Location = new Point(24, 16) };
        var sub = new Label
        {
            Text = "Clique ou aperte a tecla. Esc fecha." + (teclaFechar.Length > 0 ? $" ({teclaFechar} também)" : ""),
            Font = Tema.Fonte(10), ForeColor = Tema.TextoSuave, AutoSize = true, Location = new Point(26, 50)
        };
        var grade = new TableLayoutPanel
        {
            Location = new Point(24, 84), Size = new Size(932, linhas * alturaLinha), ColumnCount = colunas, RowCount = linhas, BackColor = Tema.Superficie
        };
        for (var c = 0; c < colunas; c++) grade.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / colunas));
        for (var r = 0; r < linhas; r++) grade.RowStyles.Add(new RowStyle(SizeType.Absolute, alturaLinha));
        for (var i = 0; i < itens.Count; i++)
        {
            var item = itens[i];
            var tecla = item.TeclaTexto ?? TeclasCaixa.Texto(item.Tecla);
            var b = new BotaoOpcao(tecla, item.Titulo, item.Descricao, item.Perigo) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, i % colunas < colunas - 1 ? 10 : 0, 10) };
            b.Click += (_, _) => Escolher(item);
            grade.Controls.Add(b, i % colunas, i / colunas);
        }
        var dica = new Label
        {
            Text = "Dica: 3*código passa 3 unidades de uma vez. As teclas podem ser trocadas em Opções do caixa → Teclado.",
            Font = Tema.Fonte(9.5f), ForeColor = Tema.TextoSuave, AutoSize = true, Location = new Point(26, grade.Bottom + 10)
        };
        var fechar = Ui.Botao("Fechar (Esc)", Ui.Neutro, 150, 40);
        fechar.Location = new Point(806, grade.Bottom + 6);
        fechar.DialogResult = DialogResult.Cancel;
        Controls.AddRange([titulo, sub, grade, dica, fechar]);
        CancelButton = fechar;
    }

    private void Escolher(Item item)
    {
        Escolhido = item;
        DialogResult = DialogResult.OK;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_teclaFechar.Length > 0 && TeclasCaixa.Texto(keyData) == _teclaFechar)
        {
            DialogResult = DialogResult.Cancel;
            return true;
        }
        var item = _itens.FirstOrDefault(i => i.Tecla != Keys.None && i.Tecla == keyData);
        if (item is not null)
        {
            Escolher(item);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>Botão grande: a tecla desenhada como teclinha, o nome da ação e uma linha explicando.</summary>
    private sealed class BotaoOpcao : Button
    {
        private readonly string _tecla;
        private readonly string _descricao;
        private readonly bool _perigo;
        private bool _hover;

        public BotaoOpcao(string tecla, string titulo, string descricao, bool perigo)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            TabStop = false;
            _tecla = tecla;
            _descricao = descricao;
            _perigo = perigo;
            Text = titulo;
        }

        protected override bool ShowFocusCues => false;
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            var g = pevent.Graphics;
            g.Clear(Parent?.BackColor ?? Tema.Superficie);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var caminho = Tema.Arredondado(r, 10))
            {
                using (var fundo = new SolidBrush(_hover ? Tema.PrimariaClara : Tema.Superficie)) g.FillPath(fundo, caminho);
                using (var borda = new Pen(_hover ? Tema.Primaria : Tema.Borda)) g.DrawPath(borda, caminho);
            }
            var x = 14;
            if (_tecla.Length > 0)
            {
                var ft = Tema.Semibold(9);
                var wt = Math.Max(40, TextRenderer.MeasureText(g, _tecla, ft).Width + 12);
                var rt = new Rectangle(x, (Height - 28) / 2, wt, 26);
                using (var caminho = Tema.Arredondado(rt, 5))
                {
                    using (var fundo = new SolidBrush(Tema.Superficie)) g.FillPath(fundo, caminho);
                    using (var borda = new Pen(Tema.Borda)) g.DrawPath(borda, caminho);
                }
                using (var baixo = new Pen(Tema.Borda, 2f)) g.DrawLine(baixo, rt.X + 3, rt.Bottom + 1, rt.Right - 3, rt.Bottom + 1);
                TextRenderer.DrawText(g, _tecla, ft, rt, Tema.Texto, TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                x += wt + 12;
            }
            const TextFormatFlags linha = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            TextRenderer.DrawText(g, Text, Tema.Semibold(11.5f), new Rectangle(x, 9, Width - x - 10, 24), _perigo ? Tema.Perigo : Tema.Texto, linha);
            TextRenderer.DrawText(g, _descricao, Tema.Fonte(9), new Rectangle(x, 33, Width - x - 10, 20), Tema.TextoSuave, linha);
        }
    }
}
