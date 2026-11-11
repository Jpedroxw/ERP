using ProjetoJP.Controles;
using ProjetoJP.Gestao.Servicos;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;

namespace ProjetoJP.Gestao.Telas;

/// <summary>
/// Entradas, saídas e ajustes de estoque com pesquisa por nome, código e nomes aproximados.
/// Embaixo, as movimentações de TODOS os produtos (vendas de cada caixa, entradas, saídas, ajustes), com período e
/// tipo — ou só as do produto escolhido.
/// </summary>
public class EstoqueForm : Form
{
    private class ItemProduto(ProdutoDto p)
    {
        public ProdutoDto Produto { get; } = p;
        public override string ToString() => $"{Produto.Descricao}  ·  {Produto.Codigo}";
    }

    private readonly ApiClient _api;
    private readonly TextBox _busca = new() { Font = Ui.Fonte(11), MaxLength = 150,
        PlaceholderText = "Digite o nome, parte do nome ou código do produto" };
    private readonly ComboBox _produto = new() { Font = Ui.Fonte(11), DropDownStyle = ComboBoxStyle.DropDownList,
        IntegralHeight = false, DropDownHeight = 280 };
    private readonly Label _resultados = Ui.Rotulo("", 9);
    private readonly Label _saldo = Ui.Rotulo("", 11, FontStyle.Bold);
    private readonly ComboBox _tipo = new() { Font = Ui.Fonte(11), DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CampoNumero _quantidade = new() { Font = Ui.Fonte(11), CasasDecimais = 0 };
    private readonly TextBox _obs = new() { Font = Ui.Fonte(11), MaxLength = 200 };
    private readonly Label _unidade = Ui.Rotulo("", 10);
    private readonly Label _dicaTipo = Ui.Rotulo("", 9);
    private readonly DataGridView _grade = Ui.Grade();
    private readonly System.Windows.Forms.Timer _atrasoBusca = new() { Interval = 180 };
    private readonly ComboBox _mostrar = new() { DropDownStyle = ComboBoxStyle.DropDownList, Font = Ui.Fonte(10), Width = 200 };
    private readonly DateTimePicker _de = new() { Format = DateTimePickerFormat.Short, Width = 118, Font = Ui.Fonte(10) };
    private readonly DateTimePicker _ate = new() { Format = DateTimePickerFormat.Short, Width = 118, Font = Ui.Fonte(10) };
    private readonly ComboBox _filtroTipo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Font = Ui.Fonte(10), Width = 170 };
    private readonly Label _resumoMov = Ui.Rotulo("", 9);
    private bool _carregandoMov;
    private const int LimiteTodos = 3000;
    private List<ItemProduto> _todos = [];
    private bool _atualizandoItens;

    /// <param name="produtoInicial">Abre com esse produto já escolhido (ex.: "Ajustar estoque" na tela Produtos).</param>
    public EstoqueForm(ApiClient api, Guid? produtoInicial = null)
    {
        _api = api;
        Text = "Movimentação de estoque";
        Size = new Size(1060, 720);
        MinimumSize = new Size(930, 620);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Ui.Fundo;
        Font = Ui.Fonte(10);
        Padding = new Padding(16);

        var topo = new Panel { Dock = DockStyle.Top, Height = 256, BackColor = Tema.Superficie, Padding = new Padding(16) };
        var lb = Ui.Rotulo("Pesquisar produto por nome, código ou código de barras");
        lb.Location = new Point(16, 10);
        _busca.SetBounds(16, 32, 566, 32);

        var lp = Ui.Rotulo("Selecione o produto encontrado");
        lp.Location = new Point(16, 75);
        _produto.SetBounds(16, 98, 566, 32);
        _resultados.ForeColor = Ui.Neutro;
        _resultados.Location = new Point(600, 35);
        _saldo.AutoSize = false;
        _saldo.SetBounds(600, 101, 360, 48);
        _saldo.AutoEllipsis = true;

        var lt = Ui.Rotulo("Tipo");
        lt.Location = new Point(16, 151);
        _tipo.SetBounds(16, 176, 180, 30);
        _tipo.Items.AddRange([TipoMovimentoEstoque.Entrada, TipoMovimentoEstoque.Saida, TipoMovimentoEstoque.Ajuste]);
        _tipo.SelectedIndex = 0;

        var lq = Ui.Rotulo("Quantidade");
        lq.Location = new Point(212, 151);
        _quantidade.SetBounds(212, 176, 120, 30);
        _unidade.ForeColor = Ui.Neutro;
        _unidade.Location = new Point(336, 181);

        var lo = Ui.Rotulo("Observação / motivo");
        lo.Location = new Point(396, 151);
        _obs.SetBounds(396, 176, 260, 30);
        _dicaTipo.ForeColor = Ui.Neutro;
        _dicaTipo.AutoSize = false;
        _dicaTipo.SetBounds(16, 219, 900, 34);

        var lancar = Ui.Botao("Lançar", largura: 150, altura: 36, icone: Icones.Ok);
        lancar.Location = new Point(812, 174);
        lancar.Click += async (_, _) => await Lancar();
        if (!api.Pode(Permissoes.MovimentarEstoque))
        {
            lancar.Enabled = false;
            new ToolTip().SetToolTip(lancar, "Seu usuário pode consultar, mas não lançar movimentações de estoque.");
        }
        void AjustarLarguras()
        {
            var direita = topo.ClientSize.Width - 16;
            var larguraBusca = Math.Max(320, Math.Min(566, direita - 370));
            _busca.Width = larguraBusca;
            _produto.Width = larguraBusca;
            _resultados.SetBounds(larguraBusca + 30, 35, Math.Max(120, direita - larguraBusca - 30), 24);
            _saldo.SetBounds(larguraBusca + 30, 101, Math.Max(120, direita - larguraBusca - 30), 48);
            lancar.Left = direita - lancar.Width;
            _obs.Width = Math.Max(130, lancar.Left - 408);
            _dicaTipo.Width = direita - 16;
        }
        topo.Resize += (_, _) => AjustarLarguras();
        topo.Controls.AddRange([lb, _busca, lp, _produto, _resultados, _saldo, lt, _tipo, lq,
            _quantidade, _unidade, lo, _obs, _dicaTipo, lancar]);
        AjustarLarguras();

        // Movimentações: de todos os produtos (padrão) ou só do escolhido, com período e tipo.
        var filtros = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true,
            Padding = new Padding(16, 10, 16, 2), BackColor = Ui.Fundo
        };
        Label Rot(string t, bool negrito = false) => new()
        {
            Text = t, AutoSize = true, Font = negrito ? Tema.Semibold(10.5f) : Ui.Fonte(10), ForeColor = Tema.Texto, Margin = new Padding(0, 7, 6, 0)
        };
        _mostrar.Items.AddRange(["Todos os produtos", "Só o produto escolhido"]);
        _mostrar.SelectedIndex = 0;
        _de.Value = DateTime.Today.AddDays(-30);
        _ate.Value = DateTime.Today;
        _filtroTipo.Items.AddRange(["Todos os tipos", "Entradas", "Saídas (manuais)", "Vendas (caixas)", "Vendas canceladas", "Ajustes"]);
        _filtroTipo.SelectedIndex = 0;
        foreach (Control c in new Control[] { _mostrar, _de, _ate, _filtroTipo }) c.Margin = new Padding(0, 3, 14, 0);
        var atualizar = Ui.Botao("Atualizar", Ui.Neutro, 120, 32, Icones.Atualizar);
        atualizar.Margin = new Padding(0, 1, 8, 0);
        atualizar.Click += async (_, _) => await CarregarMovimentosAsync();
        var exportar = Ui.Botao("Exportar", Ui.Neutro, 120, 32, Icones.Exportar);
        exportar.Margin = new Padding(0, 1, 8, 0);
        exportar.Click += (_, _) => Ui.ExportarCsv(this, _grade, "movimentacoes-estoque");
        filtros.Controls.AddRange([Rot("Movimentações:", negrito: true), _mostrar, Rot("De"), _de, Rot("até"), _ate,
            Rot("Tipo"), _filtroTipo, atualizar, exportar]);
        _mostrar.SelectedIndexChanged += async (_, _) => await CarregarMovimentosAsync();
        _de.ValueChanged += async (_, _) => await CarregarMovimentosAsync();
        _ate.ValueChanged += async (_, _) => await CarregarMovimentosAsync();
        _filtroTipo.SelectedIndexChanged += async (_, _) => await CarregarMovimentosAsync();

        var meio = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 4, 16, 6) };
        _grade.Dock = DockStyle.Fill;
        _grade.Columns.AddRange(
            Ui.ColunaTexto("Data", 12), Ui.ColunaTexto("Código", 8), Ui.ColunaTexto("Produto", 24), Ui.ColunaTexto("Tipo", 11),
            Ui.ColunaTexto("Origem", 10), Ui.ColunaTexto("Quantidade", 9, true), Ui.ColunaTexto("Saldo após", 9, true),
            Ui.ColunaTexto("Observação", 20), Ui.ColunaTexto("Usuário", 10));
        _grade.CellDoubleClick += async (_, e) =>
        {
            // Duplo clique numa linha: escolhe aquele produto lá em cima (para lançar ou ver só o histórico dele).
            if (e.RowIndex < 0 || _grade.Rows[e.RowIndex].Tag is not MovimentoEstoqueDto m || m.Codigo is null) return;
            var item = _todos.FirstOrDefault(i => i.Produto.Codigo == m.Codigo);
            if (item is null) return;
            _busca.Text = item.Produto.Codigo;
            _atrasoBusca.Stop();
            Filtrar(item.Produto.Id);
            await ProdutoMudou();
        };
        meio.Controls.Add(_grade);
        var rodape = new Panel { Dock = DockStyle.Bottom, Height = 30, Padding = new Padding(18, 4, 16, 0), BackColor = Ui.Fundo };
        _resumoMov.ForeColor = Ui.Neutro;
        _resumoMov.Dock = DockStyle.Fill;
        rodape.Controls.Add(_resumoMov);

        Controls.Add(meio);
        Controls.Add(rodape);
        Controls.Add(filtros);
        Controls.Add(topo);

        _tipo.SelectedIndexChanged += (_, _) => AtualizarDica();
        _produto.SelectedIndexChanged += async (_, _) => { if (!_atualizandoItens) await ProdutoMudou(); };
        _atrasoBusca.Tick += (_, _) => { _atrasoBusca.Stop(); Filtrar(); };
        _busca.TextChanged += (_, _) => { _atrasoBusca.Stop(); _atrasoBusca.Start(); };
        _busca.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            _atrasoBusca.Stop();
            Filtrar();
            if (_produto.Items.Count == 1) _produto.SelectedIndex = 0;
            else { _produto.Focus(); _produto.DroppedDown = _produto.Items.Count > 0; }
            e.SuppressKeyPress = true;
        };
        AtualizarDica();
        Shown += async (_, _) =>
        {
            await CarregarProdutos(produtoInicial);
            if (produtoInicial is not null && ProdutoAtual is not null) _quantidade.Focus();
            else
            {
                await CarregarMovimentosAsync(); // abre mostrando as movimentações de todos os produtos
                _busca.Focus();
            }
        };
        // Atualiza sozinho o saldo e o histórico do produto escolhido (vendas dos caixas chegam em segundos).
        var automatico = new System.Windows.Forms.Timer { Interval = 30_000 };
        automatico.Tick += async (_, _) =>
        {
            if (!Visible || ContainsFocus && (_quantidade.Focused || _obs.Focused || _busca.Focused) || _produto.DroppedDown) return;
            _automatico = true;
            try
            {
                if (ProdutoAtual is { } atual) await CarregarProdutos(atual.Id); // saldo + histórico
                else await CarregarMovimentosAsync();
            }
            finally { _automatico = false; }
        };
        Shown += (_, _) => automatico.Start();
        FormClosed += (_, _) =>
        {
            automatico.Dispose();
            _atrasoBusca.Dispose();
        };
    }

    private ProdutoDto? ProdutoAtual => (_produto.SelectedItem as ItemProduto)?.Produto;

    private void AtualizarDica()
    {
        _dicaTipo.Text = (TipoMovimentoEstoque)_tipo.SelectedItem! switch
        {
            TipoMovimentoEstoque.Entrada => "Entrada: soma a quantidade ao estoque (compra, devolução).",
            TipoMovimentoEstoque.Saida => "Saída: subtrai a quantidade (perda, quebra, uso interno).",
            _ => "Ajuste: define o saldo EXATAMENTE como a quantidade informada (inventário)."
        };
    }

    private void Filtrar(Guid? selecionar = null)
    {
        var pesquisa = _busca.Text.Trim();
        var itens = _todos.Select(i => new
        {
            Item = i,
            Pontos = PesquisaProdutos.Pontuar(pesquisa, i.Produto.Descricao, i.Produto.Codigo, i.Produto.CodigoBarras)
        }).Where(x => x.Pontos >= 0)
          .OrderByDescending(x => x.Pontos)
          .ThenBy(x => x.Item.Produto.Descricao, StringComparer.CurrentCultureIgnoreCase)
          .Take(120).Select(x => x.Item).ToList();
        // Produto pedido (após lançar, ou vindo da tela Produtos) aparece mesmo fora dos 120 primeiros.
        if (selecionar is { } pedido && itens.All(i => i.Produto.Id != pedido) &&
            _todos.FirstOrDefault(i => i.Produto.Id == pedido) is { } extra)
            itens.Insert(0, extra);
        _atualizandoItens = true;
        try
        {
            _produto.BeginUpdate();
            _produto.Items.Clear();
            foreach (var item in itens) _produto.Items.Add(item);
            if (selecionar is not null)
            {
                var encontrado = itens.FirstOrDefault(i => i.Produto.Id == selecionar);
                if (encontrado is not null) _produto.SelectedItem = encontrado;
            }
            _produto.EndUpdate();
        }
        finally { _atualizandoItens = false; }
        _resultados.Text = itens.Count == 0 ? "Nenhum produto encontrado" :
            $"{itens.Count} resultado(s)" + (itens.Count == 120 ? " (refine a busca)" : "");
        if (ProdutoAtual is null)
        {
            _saldo.Text = "";
            _unidade.Text = "";
            if (_mostrar.SelectedIndex == 1) MostrarMovimentos([], "Escolha um produto lá em cima, ou mude para \"Todos os produtos\".");
        }
    }

    private async Task CarregarProdutos(Guid? selecionar = null)
    {
        try
        {
            var produtos = await _api.GetAsync<List<ProdutoDto>>("api/produtos");
            if (IsDisposed) return;
            _todos = produtos.Where(p => p.Ativo).Select(p => new ItemProduto(p)).ToList();
            Filtrar(selecionar);
            if (selecionar is not null && ProdutoAtual is not null) await ProdutoMudou();
        }
        catch (ApiException ex) { if (!IsDisposed && !_automatico) Ui.Erro(this, ex.Message); }
    }

    /// <summary>true durante a atualização automática: erro de conexão não abre janela.</summary>
    private bool _automatico;

    private async Task ProdutoMudou()
    {
        var p = ProdutoAtual;
        _saldo.Text = p is null ? "" : $"Estoque atual: {Formatos.Quantidade(p.EstoqueAtual, p.Unidade)} {p.Unidade}";
        _unidade.Text = p?.Unidade ?? "";
        _quantidade.CasasDecimais = Formatos.CasasQuantidade(p?.Unidade);
        if (p is null) { await CarregarMovimentosAsync(); return; }
        // Escolheu um produto: a lista passa a mostrar só ele (dá para voltar a "Todos os produtos").
        if (_mostrar.SelectedIndex != 1 && !_automatico)
        {
            _mostrar.SelectedIndex = 1; // dispara CarregarMovimentosAsync
            return;
        }
        await CarregarMovimentosAsync();
    }

    private static (string texto, Color cor) TextoTipo(MovimentoEstoqueDto m) => m.Tipo switch
    {
        TipoMovimentoEstoque.Venda => ("Saída (venda)", Tema.Perigo),
        TipoMovimentoEstoque.Saida => ("Saída", Tema.Perigo),
        TipoMovimentoEstoque.Entrada => ("Entrada", Tema.Sucesso),
        TipoMovimentoEstoque.Ajuste => (m.Quantidade >= 0 ? "Ajuste (+)" : "Ajuste (−)", Tema.Primaria),
        TipoMovimentoEstoque.CancelamentoVenda => ("Venda cancelada", Tema.Sucesso),
        TipoMovimentoEstoque.DevolucaoCliente => ("Devolução", Tema.Sucesso),
        _ => (m.Tipo.ToString(), Tema.Texto)
    };

    /// <summary>Busca as movimentações conforme os filtros: todos os produtos ou só o escolhido.</summary>
    private async Task CarregarMovimentosAsync()
    {
        if (_carregandoMov || IsDisposed) return;
        var soProduto = _mostrar.SelectedIndex == 1;
        var p = ProdutoAtual;
        if (soProduto && p is null)
        {
            MostrarMovimentos([], "Escolha um produto lá em cima, ou mude para \"Todos os produtos\".");
            return;
        }
        var de = DateOnly.FromDateTime(_de.Value.Date);
        var ate = DateOnly.FromDateTime(_ate.Value.Date);
        if (ate < de) (de, ate) = (ate, de);
        var tipo = _filtroTipo.SelectedIndex switch
        {
            1 => "&tipo=Entrada", 2 => "&tipo=Saida", 3 => "&tipo=Venda", 4 => "&tipo=CancelamentoVenda", 5 => "&tipo=Ajuste", _ => ""
        };
        var url = soProduto
            ? $"api/estoque/movimentos?produtoId={p!.Id}&de={de:yyyy-MM-dd}&ate={ate:yyyy-MM-dd}{tipo}&limite=1000"
            : $"api/estoque/movimentos?de={de:yyyy-MM-dd}&ate={ate:yyyy-MM-dd}{tipo}&limite={LimiteTodos}";
        _carregandoMov = true;
        try
        {
            var lista = await _api.GetAsync<List<MovimentoEstoqueDto>>(url);
            if (IsDisposed) return;
            if (soProduto && ProdutoAtual?.Id != p!.Id) return; // trocou de produto enquanto carregava
            var limite = soProduto ? 1000 : LimiteTodos;
            var entradas = lista.Count(m => m.Quantidade > 0);
            var saidas = lista.Count(m => m.Quantidade < 0);
            MostrarMovimentos(lista,
                $"{lista.Count} movimentação(ões){(soProduto ? $" de {p!.Descricao}" : " de todos os produtos")}" +
                $"  ·  {entradas} entrada(s)  ·  {saidas} saída(s)" +
                (lista.Count >= limite ? $"  ·  mostrando as {limite} mais recentes: diminua o período para ver o resto" : "") +
                $"  ·  atualizado às {DateTime.Now:HH:mm}" +
                (soProduto ? "" : "  ·  duplo clique numa linha escolhe o produto"));
        }
        catch (ApiException ex)
        {
            if (!IsDisposed && !_automatico) Ui.Erro(this, ex.Message);
        }
        finally { _carregandoMov = false; }
    }

    private void MostrarMovimentos(List<MovimentoEstoqueDto> lista, string resumo)
    {
        var topoAntes = _grade.Rows.Count > 0 ? _grade.FirstDisplayedScrollingRowIndex : 0;
        _grade.SuspendLayout();
        _grade.Rows.Clear();
        foreach (var m in lista)
        {
            var (texto, cor) = TextoTipo(m);
            var data = m.Data.Kind == DateTimeKind.Utc ? m.Data.ToLocalTime() : m.Data;
            var i = _grade.Rows.Add(data.ToString("dd/MM/yyyy HH:mm", Ui.Br), m.Codigo ?? "", m.Produto, texto, m.Origem ?? "",
                (m.Quantidade > 0 ? "+" : "") + Formatos.Quantidade(m.Quantidade), Formatos.Quantidade(m.SaldoApos),
                m.Observacao ?? "", m.Usuario ?? "");
            var linha = _grade.Rows[i];
            linha.Tag = m;
            linha.Cells[3].Style.ForeColor = cor;
            linha.Cells[5].Style.ForeColor = m.Quantidade < 0 ? Tema.Perigo : Tema.Sucesso;
        }
        _grade.ResumeLayout();
        // Atualização automática: não pula para o topo enquanto a pessoa lê lá embaixo.
        if (_automatico && topoAntes > 0 && topoAntes < _grade.Rows.Count)
        {
            try { _grade.FirstDisplayedScrollingRowIndex = topoAntes; }
            catch (InvalidOperationException) { }
        }
        _resumoMov.Text = resumo;
    }

    private async Task Lancar()
    {
        var p = ProdutoAtual;
        if (p is null) { Ui.Aviso(this, "Pesquise e selecione um produto antes de lançar."); return; }
        var tipo = (TipoMovimentoEstoque)_tipo.SelectedItem!;
        if (tipo != TipoMovimentoEstoque.Ajuste && _quantidade.Valor <= 0) { Ui.Aviso(this, "Informe a quantidade."); return; }
        if (tipo == TipoMovimentoEstoque.Ajuste &&
            !Ui.Confirmar(this, $"O estoque de {p.Descricao} passará de {Formatos.Quantidade(p.EstoqueAtual, p.Unidade)} para {Formatos.Quantidade(_quantidade.Valor, p.Unidade)} {p.Unidade}. Confirma?")) return;

        try
        {
            await _api.PostAsync<object>("api/estoque/movimentos", new MovimentoEstoqueRequest
            {
                ProdutoId = p.Id,
                Tipo = tipo,
                Quantidade = _quantidade.Valor,
                Observacao = string.IsNullOrWhiteSpace(_obs.Text) ? null : _obs.Text.Trim()
            });
            _quantidade.Valor = 0;
            _obs.Clear();
            await CarregarProdutos(p.Id);
        }
        catch (ApiException ex) { Ui.Erro(this, ex.Message); }
    }
}
