using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ProjetoJP.Controles;
using ProjetoJP.Gestao.Servicos;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;

namespace ProjetoJP.Gestao.Telas;

/// <summary>
/// Produtos e Estoque (visual 2026): resumo no topo, lista paginada com filtros e, à direita,
/// os detalhes do produto escolhido (preços, últimas movimentações e gráfico de 30 dias).
/// Salvar aqui continua só no rascunho: os caixas recebem depois da publicação em Cargas.
/// </summary>
public sealed class ProdutosEstoqueForm : Form
{
    private const int ColStatus = 7;
    private static bool _lembrouCarga;

    private readonly ApiClient _api;
    private readonly Action<string> _abrir;

    // Resumo
    private readonly CartaoResumo _kCadastrados = new("Produtos cadastrados", Icones.Produtos, Tema.Primaria);
    private readonly CartaoResumo _kBaixo = new("Estoque baixo", Icones.Aviso, Tema.Alerta);
    private readonly CartaoResumo _kVendas = new("Vendas do dia", Icones.Carrinho, Tema.Sucesso);
    private readonly CartaoResumo _kPendentes = new("Pendentes de carga", Icones.Sincronizar, Color.FromArgb(124, 77, 230));

    // Lista
    private readonly CampoBusca _busca = new() { Dica = "Pesquisar produto por código, descrição ou código de barras..." };
    private readonly ComboBox _categoria = new() { DropDownStyle = ComboBoxStyle.DropDownList, Font = Tema.Fonte(10.5f) };
    private readonly ComboBox _status = new() { DropDownStyle = ComboBoxStyle.DropDownList, Font = Tema.Fonte(10.5f) };
    private readonly DataGridView _grade = Ui.Grade();
    private readonly Label _exibindo = new() { AutoSize = true, Font = Tema.Fonte(9.5f), ForeColor = Tema.TextoSuave };
    private readonly FlowLayoutPanel _paginas = new()
    {
        AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
        BackColor = Tema.Superficie, Margin = Padding.Empty, Padding = Padding.Empty, Size = new Size(10, 10)
    };
    private readonly ComboBox _porPagina = new() { DropDownStyle = ComboBoxStyle.DropDownList, Font = Tema.Fonte(9.5f), Width = 64 };
    private readonly Label _rotuloPorPagina = new() { AutoSize = true, Text = "Itens por página:", Font = Tema.Fonte(9.5f), ForeColor = Tema.TextoSuave };

    // Detalhes
    private readonly CartaoPainel _detalhe = new() { Dock = DockStyle.Right, Width = 430, Padding = new Padding(6) };
    private readonly Panel _separador = new() { Dock = DockStyle.Right, Width = 16 };
    private readonly Panel _conteudo = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Tema.Superficie };
    private readonly Label _tituloDetalhe = new() { Text = "Detalhes do produto", AutoSize = true, Font = Tema.Semibold(12), ForeColor = Tema.Texto };
    private readonly BotaoTema _editarDetalhe = new() { Estilo = EstiloBotao.Secundario, Icone = Icones.Editar, Size = new Size(38, 34) };
    private readonly Label _nome = new() { AutoSize = false, AutoEllipsis = true, Font = Tema.Semibold(13), ForeColor = Tema.Texto };
    private readonly (Label rotulo, Label valor)[] _campos;
    private readonly CartaoResumo _tEstoque = new("Estoque atual", Icones.Produtos, Tema.Primaria) { Compacto = true };
    private readonly CartaoResumo _tCusto = new("Custo", Icones.Etiqueta, Tema.Primaria) { Compacto = true };
    private readonly CartaoResumo _tPreco = new("Preço de venda", Icones.Pagamento, Tema.Primaria) { Compacto = true };
    private readonly Label _tituloMov = new() { Text = "Últimas movimentações de estoque", AutoSize = true, Font = Tema.Semibold(11), ForeColor = Tema.Texto };
    private readonly LinkLabel _verTodas = new() { Text = "Ver todas", AutoSize = true, Font = Tema.Semibold(9.5f), LinkBehavior = LinkBehavior.HoverUnderline };
    private readonly ListaMovimentos _movimentos = new();
    private readonly Label _tituloGrafico = new() { Text = "Vendas e estoque (últimos 30 dias)", AutoSize = true, Font = Tema.Semibold(11), ForeColor = Tema.Texto };
    private readonly GraficoVendasEstoque _grafico = new();

    private readonly System.Windows.Forms.Timer _atrasoBusca = new() { Interval = 220 };
    private readonly System.Windows.Forms.Timer _atrasoDetalhe = new() { Interval = 160 };
    private readonly ContextMenuStrip _menuLinha = new();

    private List<ProdutoDto> _todos = [];
    private List<ProdutoDto> _filtrados = [];
    private int _pagina;
    private int _totalFiltrados;
    private int _versaoConsulta;
    private int _colunaOrdem = 1;
    private bool _ordemDesc;
    private bool _ordemManual;
    private bool _montando;
    private bool _carregado;
    private Guid? _pendente;
    private Guid? _manter;
    private int _versaoDetalhe;

    /// <param name="abrir">Abre outra tela da Retaguarda pela chave (entrada, cargas...).</param>
    public ProdutosEstoqueForm(ApiClient api, Action<string> abrir)
    {
        _api = api;
        _abrir = abrir;
        Text = "Produtos e Estoque";
        BackColor = Tema.Fundo;
        Font = Tema.Fonte(10);
        Padding = new Padding(24, 12, 24, 16);
        KeyPreview = true;

        var principal = new Panel { Dock = DockStyle.Fill, BackColor = Tema.Fundo };

        // ----- Cabeçalho -----
        // Posições pela altura real das letras: não se sobrepõem com o zoom do Windows em 125% ou 150%.
        var fTrilha = Tema.Fonte(9.5f);
        var fTitulo = Tema.Semibold(18);
        var fSub = Tema.Fonte(10);
        var yTitulo = fTrilha.Height + Px(4);
        var cabecalho = new Panel { Dock = DockStyle.Top, Height = yTitulo + fTitulo.Height + fSub.Height + Px(10), BackColor = Tema.Fundo };
        cabecalho.Controls.Add(new Label
        {
            Text = "Gestão   ›   Produtos e Estoque", AutoSize = true, Font = fTrilha,
            ForeColor = Tema.TextoSuave, Location = new Point(2, 0)
        });
        cabecalho.Controls.Add(new Label
        {
            Text = Icones.Produtos, Font = Tema.FonteIcone(20), ForeColor = Tema.Texto, AutoSize = false,
            Size = new Size(Px(40), fTitulo.Height + fSub.Height), TextAlign = ContentAlignment.MiddleCenter, Location = new Point(0, yTitulo)
        });
        cabecalho.Controls.Add(new Label
        {
            Text = "Produtos e Estoque", AutoSize = true, Font = fTitulo, ForeColor = Tema.Texto, Location = new Point(Px(44), yTitulo)
        });
        cabecalho.Controls.Add(new Label
        {
            Text = "Cadastro de produtos, controle de estoque e movimentações.", AutoSize = true,
            Font = fSub, ForeColor = Tema.TextoSuave, Location = new Point(Px(46), yTitulo + fTitulo.Height)
        });
        var atualizar = new BotaoTema { Text = "Atualizar", Estilo = EstiloBotao.Transparente, Icone = Icones.Atualizar, Font = Tema.Fonte(10) };
        atualizar.Size = new Size(LarguraBotao("Atualizar", atualizar.Font), Px(36));
        atualizar.Click += async (_, _) => await CarregarAsync();
        _botaoDetalhes.Font = Tema.Fonte(10);
        _botaoDetalhes.Size = new Size(LarguraBotao("Ocultar detalhes", _botaoDetalhes.Font), Px(36));
        _botaoDetalhes.Click += (_, _) =>
        {
            _detalhesPedidos = !_detalhe.Visible;
            AjustarDetalhe();
        };
        cabecalho.Controls.AddRange([atualizar, _botaoDetalhes]);
        cabecalho.Resize += (_, _) =>
        {
            atualizar.Location = new Point(cabecalho.Width - atualizar.Width, yTitulo);
            _botaoDetalhes.Location = new Point(atualizar.Left - _botaoDetalhes.Width - Px(6), yTitulo);
        };
        new ToolTip().SetToolTip(atualizar, "Recarregar do servidor (F5)");

        // ----- Resumo -----
        var resumo = new TableLayoutPanel { Dock = DockStyle.Top, Height = Px(96), ColumnCount = 4, RowCount = 1, BackColor = Tema.Fundo, Margin = Padding.Empty };
        for (var i = 0; i < 4; i++) resumo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        resumo.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var cartoes = new[] { _kCadastrados, _kBaixo, _kVendas, _kPendentes };
        for (var i = 0; i < cartoes.Length; i++)
        {
            cartoes[i].Dock = DockStyle.Fill;
            cartoes[i].Margin = new Padding(0, 0, i < cartoes.Length - 1 ? 14 : 0, 0);
            resumo.Controls.Add(cartoes[i], i, 0);
        }
        _kCadastrados.Clicavel = true;
        _kCadastrados.Click += (_, _) => _status.SelectedIndex = 0;
        _kBaixo.Clicavel = true;
        _kBaixo.Click += (_, _) => _status.SelectedIndex = 3;
        if (_api.Pode(Permissoes.PublicarCargas))
        {
            _kPendentes.Clicavel = true;
            _kPendentes.Click += (_, _) => _abrir("cargas");
        }
        var dicas = new ToolTip();
        dicas.SetToolTip(_kBaixo, "Mostrar só os produtos com estoque baixo");
        dicas.SetToolTip(_kPendentes, "Produtos novos ou alterados que ainda não foram publicados para os caixas");

        // ----- Ações -----
        var acoes = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true,
            BackColor = Tema.Fundo, Padding = new Padding(0, Px(14), 0, Px(6))
        };
        BotaoTema Acao(string texto, Color? cor, string icone, bool menu = false) =>
            Ui.Botao(texto, cor, LarguraBotao(texto, Tema.Semibold(10)) + (menu ? Px(22) : 0), Px(40), icone);
        var novo = Acao("Novo produto", null, Icones.Novo);
        novo.Click += async (_, _) => await NovoAsync();
        novo.Enabled = PodeEditar;
        var editar = Acao("Editar", Ui.Neutro, Icones.Editar);
        editar.Click += async (_, _) => await EditarAsync();
        editar.Enabled = PodeEditar;
        var entrada = Acao("Entrada de produtos", Ui.Neutro, Icones.Importar);
        entrada.Enabled = _api.Pode(Permissoes.EntradaProdutos);
        entrada.Click += (_, _) => _abrir("entrada");
        var saida = Acao("Saída de produtos", Ui.Neutro, Icones.Exportar);
        saida.Enabled = _api.Pode(Permissoes.MovimentarEstoque);
        saida.Click += (_, _) => _abrir("saida");
        var ajustar = Acao("Ajustar estoque", Ui.Neutro, Icones.Alternar);
        ajustar.Enabled = _api.Pode(Permissoes.MovimentarEstoque);
        ajustar.Click += async (_, _) => await AjustarEstoqueAsync();
        var exportar = Acao("Exportar", Ui.Neutro, Icones.Exportar, menu: true);
        exportar.IconeDireita = Icones.ChevronBaixo;
        var menuExportar = new ContextMenuStrip();
        menuExportar.Items.Add("Salvar a lista filtrada em CSV (Excel)...", null, (_, _) => ExportarCsv());
        menuExportar.Items.Add("Copiar a lista filtrada", null, (_, _) => CopiarLista());
        exportar.Click += (_, _) => menuExportar.Show(exportar, new Point(0, exportar.Height));
        var mais = Acao("Mais", Ui.Neutro, Icones.Mais, menu: true);
        mais.IconeDireita = Icones.ChevronBaixo;
        mais.Click += (_, _) => { AtualizarMenuLinha(); _menuLinha.Show(mais, new Point(0, mais.Height)); };
        acoes.Controls.AddRange([novo, editar, entrada, saida, ajustar, exportar, mais]);
        dicas.SetToolTip(novo, "Novo produto (Insert)");
        dicas.SetToolTip(editar, "Editar o selecionado (Enter ou duplo clique)");
        dicas.SetToolTip(saida, "Perda, vencido, uso interno, devolução ao fornecedor — vários itens de uma vez");

        // ----- Filtros -----
        var filtros = new Panel { Dock = DockStyle.Top, BackColor = Tema.Fundo };
        var lCategoria = new Label { Text = "Categoria:", AutoSize = true, Font = Tema.Semibold(9.5f), ForeColor = Tema.Texto };
        var lStatus = new Label { Text = "Status:", AutoSize = true, Font = Tema.Semibold(9.5f), ForeColor = Tema.Texto };
        _categoria.Items.Add(new LookupItem { Id = Guid.Empty, Nome = "Todas" });
        _categoria.SelectedIndex = 0;
        _status.Items.AddRange(["Todos", "Ativos", "Inativos", "Estoque baixo", "Sem estoque"]);
        _status.SelectedIndex = 1;
        filtros.Controls.AddRange([_busca, lCategoria, _categoria, lStatus, _status]);
        var yCombo = lStatus.PreferredHeight + Px(4);
        var alturaBusca = Math.Max(Px(36), _status.PreferredHeight + Px(6));
        filtros.Height = yCombo + alturaBusca + Px(10);
        filtros.Resize += (_, _) =>
        {
            var w = filtros.ClientSize.Width;
            var larguraCombo = Math.Clamp(w / 5, Px(130), Px(180));
            var meioBusca = yCombo + alturaBusca / 2;
            _status.SetBounds(w - larguraCombo, meioBusca - _status.Height / 2, larguraCombo, _status.Height);
            _categoria.SetBounds(w - 2 * larguraCombo - Px(14), meioBusca - _categoria.Height / 2, larguraCombo, _categoria.Height);
            lStatus.Location = new Point(_status.Left, 0);
            lCategoria.Location = new Point(_categoria.Left, 0);
            _busca.SetBounds(0, yCombo, Math.Max(Px(140), _categoria.Left - Px(16)), alturaBusca);
        };

        // ----- Lista -----
        var cartaoGrade = new CartaoPainel { Dock = DockStyle.Fill, Padding = new Padding(1, 4, 1, 1) };
        _grade.Dock = DockStyle.Fill;
        _grade.Columns.AddRange(
            Ui.ColunaTexto("Código", 11), Ui.ColunaTexto("Produto", 32), Ui.ColunaTexto("Categoria", 14),
            Ui.ColunaTexto("Un", 6), Ui.ColunaTexto("Estoque", 10, true), Ui.ColunaTexto("Custo", 11, true),
            Ui.ColunaTexto("Preço", 11, true), Ui.ColunaTexto("Status", 15));
        foreach (var i in new[] { 0, 1, 2, 4, 5, 6 }) _grade.Columns[i].SortMode = DataGridViewColumnSortMode.Programmatic;
        foreach (var i in new[] { 4, 5, 6 }) _grade.Columns[i].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
        _grade.Columns[3].MinimumWidth = 44;
        // Sem o pontilhado de foco na célula (a linha inteira já fica destacada).
        _grade.RowPrePaint += (_, e) => e.PaintParts &= ~DataGridViewPaintParts.Focus;
        // Listas no tema (no escuro, as caixas de escolha do Windows ficavam brancas).
        foreach (var combo in new[] { _categoria, _status, _porPagina })
        {
            combo.FlatStyle = FlatStyle.Flat;
            combo.BackColor = Tema.Superficie;
            combo.ForeColor = Tema.Texto;
        }
        _grade.ContextMenuStrip = _menuLinha;
        var rodape = new Panel { Dock = DockStyle.Bottom, Height = Px(52), BackColor = Tema.Superficie };
        // Tela estreita: a coluna Categoria sai (continua no painel de detalhes e no filtro).
        _grade.Resize += (_, _) => _grade.Columns[2].Visible = _grade.Width >= Px(760);
        rodape.Paint += (_, e) =>
        {
            using var linha = new Pen(Tema.Borda);
            e.Graphics.DrawLine(linha, 0, 0, rodape.Width, 0);
        };
        foreach (var n in new[] { "10", "20", "50", "100" }) _porPagina.Items.Add(n);
        _porPagina.SelectedIndex = 1;
        rodape.Controls.AddRange([_exibindo, _paginas, _rotuloPorPagina, _porPagina]);
        rodape.Resize += (_, _) => PosicionarRodape(rodape);
        _paginas.SizeChanged += (_, _) => PosicionarRodape(rodape);
        cartaoGrade.Controls.Add(_grade);
        cartaoGrade.Controls.Add(rodape);

        principal.Controls.Add(cartaoGrade);
        principal.Controls.Add(filtros);
        principal.Controls.Add(acoes);
        principal.Controls.Add(resumo);
        principal.Controls.Add(cabecalho);

        // ----- Detalhes -----
        _campos =
        [
            (Rotulo("Código:"), Valor()), (Rotulo("Categoria:"), Valor()),
            (Rotulo("Unidade:"), Valor()), (Rotulo("Cód. de barras:"), Valor())
        ];
        _verTodas.LinkColor = Tema.Primaria;
        _verTodas.ActiveLinkColor = Tema.PrimariaEscura;
        _verTodas.LinkClicked += async (_, _) => await AjustarEstoqueAsync();
        _editarDetalhe.Click += async (_, _) => await EditarAsync();
        dicas.SetToolTip(_editarDetalhe, "Editar produto");
        _conteudo.Controls.AddRange([_tituloDetalhe, _editarDetalhe, _nome]);
        foreach (var (r, v) in _campos) _conteudo.Controls.AddRange([r, v]);
        _conteudo.Controls.AddRange([_tEstoque, _tCusto, _tPreco, _tituloMov, _verTodas, _movimentos, _tituloGrafico, _grafico]);
        _conteudo.Resize += (_, _) => PosicionarDetalhe();
        _detalhe.Controls.Add(_conteudo);

        Controls.Add(principal);
        Controls.Add(_separador);
        Controls.Add(_detalhe);
        Resize += (_, _) => AjustarDetalhe();

        // ----- Menus -----
        _menuLinha.Opening += (_, _) => AtualizarMenuLinha();

        // ----- Eventos -----
        _busca.Caixa.TextChanged += (_, _) => { _atrasoBusca.Stop(); _atrasoBusca.Start(); };
        _atrasoBusca.Tick += (_, _) =>
        {
            _atrasoBusca.Stop();
            _ordemManual = false;
            LimparSetas();
            Filtrar();
        };
        _categoria.SelectedIndexChanged += (_, _) => Filtrar();
        _status.SelectedIndexChanged += (_, _) => Filtrar();
        _porPagina.SelectedIndexChanged += (_, _) => Filtrar();
        _grade.SelectionChanged += (_, _) => { if (!_montando) AgendarDetalhe(); };
        _grade.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0) await EditarAsync(); };
        _grade.CellMouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && e.ColumnIndex >= 0)
                _grade.CurrentCell = _grade.Rows[e.RowIndex].Cells[e.ColumnIndex];
        };
        _grade.ColumnHeaderMouseClick += (_, e) => Ordenar(e.ColumnIndex);
        _grade.CellPainting += PintarStatus;
        _atrasoDetalhe.Tick += async (_, _) =>
        {
            _atrasoDetalhe.Stop();
            await CarregarDetalheAsync();
        };
        KeyDown += async (_, e) =>
        {
            switch (e.KeyCode)
            {
                case Keys.F5: e.Handled = true; await CarregarAsync(); break;
                case Keys.Insert: e.Handled = true; await NovoAsync(); break;
                case Keys.Enter when _grade.Focused: e.Handled = true; e.SuppressKeyPress = true; await EditarAsync(); break;
            }
        };

        // Quem não pode ver custo: coluna e quadro de custo somem (o servidor já manda R$ 0,00).
        if (!_api.Pode(Permissoes.VerCusto))
        {
            _grade.Columns[5].Visible = false;
            _tCusto.Visible = false;
        }

        Shown += async (_, _) =>
        {
            AjustarDetalhe();
            PosicionarDetalhe();
            await CarregarAsync();
            _automatico.Start();
        };
        // Atualiza sozinho: as vendas dos caixas chegam em segundos e o estoque muda.
        _automatico.Tick += async (_, _) =>
        {
            if (!Visible || _carregando || _grade.IsCurrentCellInEditMode) return;
            await CarregarAsync(silencioso: true);
        };
        FormClosed += (_, _) =>
        {
            _automatico.Dispose();
            _atrasoBusca.Dispose();
            _atrasoDetalhe.Dispose();
        };
        MostrarDetalhe(null);
    }

    private static Label Rotulo(string texto) => new()
    {
        Text = texto, AutoSize = false, Font = Tema.Fonte(9.5f), ForeColor = Tema.TextoSuave, Height = 21
    };

    private static Label Valor() => new()
    {
        AutoSize = false, AutoEllipsis = true, Font = Tema.Fonte(9.5f), ForeColor = Tema.Texto, Height = 21
    };

    private ProdutoDto? Selecionado => _grade.CurrentRow?.Tag as ProdutoDto;

    // ---------- Abertura por outra tela (pesquisa do topo) ----------

    /// <summary>Quando vem de outra tela, busca o código para encontrá-lo na página do servidor.</summary>
    public void SelecionarProduto(Guid id)
    {
        if (!_carregado) { _pendente = id; return; }
        _ = SelecionarProdutoAsync(id);
    }

    private async Task SelecionarProdutoAsync(Guid id)
    {
        try
        {
            var encontrado = _filtrados.FirstOrDefault(p => p.Id == id);
            if (encontrado is null)
            {
                var p = await _api.GetAsync<ProdutoDto>($"api/produtos/{id}");
                if (IsDisposed) return;
                _montando = true;
                _busca.Caixa.Text = p.Codigo;
                _categoria.SelectedIndex = 0;
                _status.SelectedIndex = 0;
                _montando = false;
                _atrasoBusca.Stop();
                _pagina = 0;
                _manter = id;
                await CarregarPaginaAsync();
            }
            else
            {
                _manter = id;
                MostrarPagina();
            }
            if (!IsDisposed) _grade.Focus();
        }
        catch (ApiException ex) { if (!IsDisposed) Ui.Erro(this, ex.Message); }
    }

    // ---------- Dados ----------

    private readonly System.Windows.Forms.Timer _automatico = new() { Interval = 30_000 };
    private bool _carregando;
    private bool PodeEditar => _api.Pode(Permissoes.EditarProdutos);

    /// <param name="silencioso">Atualização automática: sem cursor de espera e sem janela de erro.</param>
    private async Task CarregarAsync(bool silencioso = false)
    {
        if (_carregando) return;
        _carregando = true;
        try
        {
            if (!silencioso) UseWaitCursor = true;
            _manter = Selecionado?.Id ?? _manter;
            await CarregarCategoriasAsync();
            if (IsDisposed) return;
            _carregado = true;
            await CarregarPaginaAsync(manterPagina: true, silencioso: silencioso);
            if (_pendente is { } id)
            {
                _pendente = null;
                SelecionarProduto(id);
            }
            await CarregarResumoAsync();
        }
        catch (ApiException ex)
        {
            if (!IsDisposed && !silencioso) Ui.Erro(this, ex.Message);
        }
        finally
        {
            _carregando = false;
            if (!IsDisposed) UseWaitCursor = false;
        }
    }

    private async Task CarregarCategoriasAsync()
    {
        try
        {
            var categorias = await _api.GetAsync<List<LookupItem>>("api/categorias");
            if (IsDisposed) return;
            var atual = (_categoria.SelectedItem as LookupItem)?.Id ?? Guid.Empty;
            _montando = true;
            _categoria.BeginUpdate();
            _categoria.Items.Clear();
            _categoria.Items.Add(new LookupItem { Id = Guid.Empty, Nome = "Todas" });
            foreach (var c in categorias.OrderBy(c => c.Nome, StringComparer.CurrentCultureIgnoreCase)) _categoria.Items.Add(c);
            _categoria.SelectedItem = _categoria.Items.Cast<LookupItem>().FirstOrDefault(c => c.Id == atual) ?? _categoria.Items[0];
            _categoria.EndUpdate();
        }
        catch (ApiException) { /* sem categorias: filtro fica só com "Todas" */ }
        finally { _montando = false; }
    }

    private async Task CarregarResumoAsync()
    {
        try
        {
            var r = await _api.GetAsync<ResumoProdutosDto>("api/produtos/resumo");
            if (!IsDisposed) AplicarResumo(r);
        }
        catch (ApiException)
        {
            // API anterior (sem o resumo): calcula o que dá com a lista.
            if (IsDisposed) return;
            AplicarResumo(new ResumoProdutosDto
            {
                Cadastrados = _todos.Count,
                Ativos = _todos.Count(p => p.Ativo),
                EstoqueBaixo = _todos.Count(EstoqueBaixo),
                SemEstoque = _todos.Count(p => p.Ativo && p.EstoqueAtual <= 0),
                PendentesCarga = -1
            });
        }
    }

    private void AplicarResumo(ResumoProdutosDto r)
    {
        _kCadastrados.Definir(r.Cadastrados.ToString("N0", Ui.Br),
            r.NovosNoMes > 0 ? $"▲ +{r.NovosNoMes:N0} novo(s) no mês" : $"{r.Ativos:N0} ativo(s)",
            r.NovosNoMes > 0 ? Tema.Sucesso : null);
        _kBaixo.Definir(r.EstoqueBaixo.ToString("N0", Ui.Br),
            r.SemEstoque > 0 ? $"{r.SemEstoque:N0} sem estoque" : "abaixo do estoque mínimo",
            r.SemEstoque > 0 ? Tema.Perigo : null);
        if (r.VendasHoje is not { } hoje)
        {
            _kVendas.Definir("—", "disponível para gerente");
        }
        else if (r.VendasOntem is { } ontem && ontem > 0)
        {
            var variacao = (hoje - ontem) / ontem * 100;
            _kVendas.Definir(Formatos.Moeda(hoje),
                $"{(variacao >= 0 ? "▲ +" : "▼ ")}{variacao.ToString("N1", Ui.Br)}% vs. ontem",
                variacao >= 0 ? Tema.Sucesso : Tema.Perigo);
        }
        else
        {
            _kVendas.Definir(Formatos.Moeda(hoje), "ontem: " + Formatos.Moeda(r.VendasOntem ?? 0));
        }
        _kPendentes.Definir(r.PendentesCarga < 0 ? "—" : r.PendentesCarga.ToString("N0", Ui.Br),
            r.PendentesCarga > 0 ? "aguardando publicação" : r.PendentesCarga == 0 ? "caixas em dia" : "");
    }

    private static bool EstoqueBaixo(ProdutoDto p) => p.Ativo && p.EstoqueMinimo > 0 && p.EstoqueAtual <= p.EstoqueMinimo;

    private static string Situacao(ProdutoDto p) =>
        !p.Ativo ? "Inativo" : p.EstoqueAtual <= 0 ? "Sem estoque" : EstoqueBaixo(p) ? "Estoque baixo" : "Ativo";

    private static Color CorSituacao(string s) => s switch
    {
        "Ativo" => Tema.Sucesso,
        "Estoque baixo" => Tema.Alerta,
        "Sem estoque" => Tema.Perigo,
        _ => Tema.TextoSuave
    };

    // ---------- Filtro, ordem e páginas ----------

    private int PorPagina => int.TryParse(_porPagina.SelectedItem as string, out var n) ? n : 20;

    private void Filtrar(bool manterPagina = false)
    {
        if (_montando || !_carregado) return;
        if (!manterPagina) _pagina = 0;
        _ = CarregarPaginaAsync(manterPagina);
    }

    /// <summary>Somente os itens da página atual são carregados; nenhuma consulta periódica baixa o catálogo todo.</summary>
    private async Task CarregarPaginaAsync(bool manterPagina = false, bool silencioso = false)
    {
        if (!manterPagina) _pagina = 0;
        var versao = ++_versaoConsulta;
        var busca = Uri.EscapeDataString(_busca.Caixa.Text.Trim());
        var categoria = (_categoria.SelectedItem as LookupItem)?.Id ?? Guid.Empty;
        var url = $"api/produtos/pagina?busca={busca}&categoria={categoria}&status={_status.SelectedIndex}" +
                  $"&pagina={_pagina}&porPagina={PorPagina}&ordenar={_colunaOrdem}&desc={_ordemDesc.ToString().ToLowerInvariant()}";
        try
        {
            var dados = await _api.GetAsync<PaginaProdutosDto>(url);
            if (IsDisposed || versao != _versaoConsulta) return;
            _totalFiltrados = dados.Total;
            _pagina = dados.Pagina;
            _todos = dados.Itens;
            _filtrados = dados.Itens;
            MostrarPagina();
        }
        catch (ApiException ex)
        {
            if (!IsDisposed && versao == _versaoConsulta && !silencioso)
                Ui.Erro(this, "Não foi possível carregar a página de produtos: " + ex.Message);
        }
    }

    /// <summary>Busca completa somente sob pedido explícito de exportação.</summary>
    private async Task<List<ProdutoDto>> ObterProdutosCompletosAsync() =>
        await _api.GetAsync<List<ProdutoDto>>("api/produtos");

    private async Task<List<ProdutoDto>> ObterProdutosFiltradosCompletosAsync()
    {
        var produtos = await ObterProdutosCompletosAsync();
        var texto = _busca.Caixa.Text.Trim();
        var categoria = (_categoria.SelectedItem as LookupItem)?.Id ?? Guid.Empty;
        var status = _status.SelectedIndex;
        var itens = produtos.Select(p => (p, pontos: texto.Length == 0 ? 0 : PesquisaProdutos.Pontuar(texto, p.Descricao, p.Codigo, p.CodigoBarras)))
            .Where(x => x.pontos >= 0)
            .Where(x => categoria == Guid.Empty || x.p.CategoriaId == categoria)
            .Where(x => status switch
            {
                1 => x.p.Ativo,
                2 => !x.p.Ativo,
                3 => EstoqueBaixo(x.p),
                4 => x.p.Ativo && x.p.EstoqueAtual <= 0,
                _ => true
            }).ToList();
        return texto.Length > 0 && !_ordemManual
            ? itens.OrderByDescending(x => x.pontos).ThenBy(x => x.p.Descricao, StringComparer.CurrentCultureIgnoreCase).Select(x => x.p).ToList()
            : OrdenarLista(itens.Select(x => x.p));
    }

    private List<ProdutoDto> OrdenarLista(IEnumerable<ProdutoDto> lista)
    {
        var texto = StringComparer.CurrentCultureIgnoreCase;
        IOrderedEnumerable<ProdutoDto> o = _colunaOrdem switch
        {
            0 => _ordemDesc
                ? lista.OrderByDescending(p => long.TryParse(p.Codigo, out var n) ? n : long.MaxValue).ThenByDescending(p => p.Codigo, texto)
                : lista.OrderBy(p => long.TryParse(p.Codigo, out var n) ? n : long.MaxValue).ThenBy(p => p.Codigo, texto),
            2 => _ordemDesc ? lista.OrderByDescending(p => p.CategoriaNome ?? "", texto) : lista.OrderBy(p => p.CategoriaNome ?? "", texto),
            4 => _ordemDesc ? lista.OrderByDescending(p => p.EstoqueAtual) : lista.OrderBy(p => p.EstoqueAtual),
            5 => _ordemDesc ? lista.OrderByDescending(p => p.PrecoCusto) : lista.OrderBy(p => p.PrecoCusto),
            6 => _ordemDesc ? lista.OrderByDescending(p => p.PrecoVenda) : lista.OrderBy(p => p.PrecoVenda),
            _ => _ordemDesc ? lista.OrderByDescending(p => p.Descricao, texto) : lista.OrderBy(p => p.Descricao, texto)
        };
        return o.ThenBy(p => p.Descricao, texto).ToList();
    }

    private void Ordenar(int coluna)
    {
        if (coluna < 0 || _grade.Columns[coluna].SortMode != DataGridViewColumnSortMode.Programmatic) return;
        _ordemDesc = _colunaOrdem == coluna && !_ordemDesc;
        _colunaOrdem = coluna;
        _ordemManual = true;
        LimparSetas();
        _grade.Columns[coluna].HeaderCell.SortGlyphDirection = _ordemDesc ? SortOrder.Descending : SortOrder.Ascending;
        _manter = Selecionado?.Id;
        Filtrar();
    }

    private void LimparSetas()
    {
        foreach (DataGridViewColumn c in _grade.Columns)
            if (c.SortMode == DataGridViewColumnSortMode.Programmatic) c.HeaderCell.SortGlyphDirection = SortOrder.None;
    }

    private void MostrarPagina()
    {
        var porPagina = PorPagina;
        var total = _totalFiltrados;
        var paginas = Math.Max(1, (total + porPagina - 1) / porPagina);
        _pagina = Math.Clamp(_pagina, 0, paginas - 1);
        var itens = _filtrados;
        var manter = _manter ?? Selecionado?.Id;
        _manter = null;

        _montando = true;
        try
        {
            _grade.Rows.Clear();
            foreach (var p in itens)
            {
                var i = _grade.Rows.Add(p.Codigo, p.Descricao, p.CategoriaNome ?? "—", p.Unidade,
                    Formatos.Quantidade(p.EstoqueAtual, p.Unidade), Formatos.Moeda(p.PrecoCusto),
                    Formatos.Moeda(p.PrecoVenda), Situacao(p));
                var linha = _grade.Rows[i];
                linha.Tag = p;
                if (!p.Ativo) linha.DefaultCellStyle.ForeColor = Tema.TextoSuave;
            }
            if (_grade.Rows.Count > 0)
            {
                var alvo = _grade.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => (r.Tag as ProdutoDto)?.Id == manter)
                           ?? _grade.Rows[0];
                _grade.CurrentCell = alvo.Cells[1];
            }
        }
        finally
        {
            _montando = false;
        }

        _exibindo.Text = total == 0
            ? "Nenhum produto encontrado"
            : $"Exibindo {(_pagina * porPagina + 1).ToString("N0", Ui.Br)} a {(_pagina * porPagina + itens.Count).ToString("N0", Ui.Br)} de {total.ToString("N0", Ui.Br)} produto(s)";
        MontarPaginacao(paginas);
        AgendarDetalhe();
    }

    private void MontarPaginacao(int paginas)
    {
        _paginas.SuspendLayout();
        foreach (var c in _paginas.Controls.Cast<Control>().ToList())
        {
            _paginas.Controls.Remove(c);
            c.Dispose();
        }

        void Botao(string texto, int destino, bool atual = false, bool habilitado = true)
        {
            var b = new BotaoTema
            {
                Text = texto, Estilo = atual ? EstiloBotao.Primario : EstiloBotao.Secundario, Font = Tema.Fonte(9.5f),
                Size = new Size(Math.Max(34, TextRenderer.MeasureText(texto, Tema.Fonte(9.5f)).Width + 18), 32),
                Margin = new Padding(0, 0, 6, 0), Enabled = habilitado, TabStop = false
            };
            b.Click += (_, _) =>
            {
                _pagina = destino;
                _ = CarregarPaginaAsync(manterPagina: true);
            };
            _paginas.Controls.Add(b);
        }
        void Reticencias() => _paginas.Controls.Add(new Label
        {
            Text = "…", AutoSize = false, Size = new Size(22, 32), TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Tema.TextoSuave, Margin = new Padding(0, 0, 6, 0)
        });

        Botao("«", 0, habilitado: _pagina > 0);
        Botao("‹", _pagina - 1, habilitado: _pagina > 0);
        var inicio = Math.Max(0, _pagina - 2);
        var fim = Math.Min(paginas - 1, inicio + 4);
        inicio = Math.Max(0, fim - 4);
        if (inicio > 0)
        {
            Botao("1", 0);
            if (inicio > 1) Reticencias();
        }
        for (var i = inicio; i <= fim; i++) Botao((i + 1).ToString("N0", Ui.Br), i, atual: i == _pagina);
        if (fim < paginas - 1)
        {
            if (fim < paginas - 2) Reticencias();
            Botao(paginas.ToString("N0", Ui.Br), paginas - 1);
        }
        Botao("›", _pagina + 1, habilitado: _pagina < paginas - 1);
        Botao("»", paginas - 1, habilitado: _pagina < paginas - 1);
        _paginas.ResumeLayout(true);
    }

    private void PosicionarRodape(Control rodape)
    {
        var h = rodape.ClientSize.Height;
        var w = rodape.ClientSize.Width;
        _exibindo.Location = new Point(14, (h - _exibindo.Height) / 2);
        _porPagina.Location = new Point(w - _porPagina.Width - 14, (h - _porPagina.Height) / 2);
        _rotuloPorPagina.Location = new Point(_porPagina.Left - _rotuloPorPagina.Width - 6, (h - _rotuloPorPagina.Height) / 2);
        var livreInicio = _exibindo.Right + 16;
        var livreFim = _rotuloPorPagina.Left - 16;
        _rotuloPorPagina.Visible = livreFim - livreInicio >= _paginas.Width;
        if (!_rotuloPorPagina.Visible) livreFim = _porPagina.Left - 16;
        var x = Math.Max(livreInicio, (w - _paginas.Width) / 2);
        if (x + _paginas.Width > livreFim) x = Math.Max(livreInicio, livreFim - _paginas.Width);
        _paginas.Location = new Point(x, (h - _paginas.Height) / 2);
    }

    private void PintarStatus(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != ColStatus || e.Graphics is null) return;
        e.PaintBackground(e.CellBounds, true);
        var texto = e.Value as string ?? "";
        Tema.DesenharEtiqueta(e.Graphics, new Rectangle(e.CellBounds.X + 8, e.CellBounds.Y, e.CellBounds.Width - 12, e.CellBounds.Height),
            texto, CorSituacao(texto));
        e.Handled = true;
    }

    // ---------- Detalhes ----------

    /// <summary>Pixels de 100% → pixels na escala do Windows (125%, 150%...).</summary>
    private int Px(int valor) => (int)Math.Round(valor * DeviceDpi / 96f);

    private int LarguraBotao(string texto, Font fonte) => TextRenderer.MeasureText(texto, fonte).Width + Px(58);

    private readonly BotaoTema _botaoDetalhes = new() { Estilo = EstiloBotao.Transparente, Icone = Icones.Grade };
    /// <summary>null = automático pela largura; true/false = escolhido no botão.</summary>
    private bool? _detalhesPedidos;

    private void AjustarDetalhe()
    {
        // Automático: o painel aparece quando sobra espaço para a lista (tela de 1366 px ou mais com menu).
        var cabe = ClientSize.Width >= Px(1080);
        var mostrar = _detalhesPedidos ?? cabe;
        var apareceu = mostrar && !_detalheMostrado;
        _detalheMostrado = mostrar;
        _detalhe.Visible = mostrar;
        _separador.Visible = mostrar;
        if (mostrar) _detalhe.Width = Math.Clamp((int)(ClientSize.Width * (cabe ? 0.33 : 0.42)), Px(360), Px(450));
        _botaoDetalhes.Text = mostrar ? "Ocultar detalhes" : "Mostrar detalhes";
        if (apareceu && _carregado) AgendarDetalhe(); // estava escondido: carrega movimentos e gráfico
    }

    private bool _detalheMostrado;

    private void PosicionarDetalhe()
    {
        var w = _conteudo.ClientSize.Width;
        var dy = _conteudo.AutoScrollPosition.Y;
        var m = Px(14);
        var linha = Math.Max(Px(21), _campos[0].valor.Font.Height + Px(3));
        _editarDetalhe.Size = new Size(Px(38), Px(34));
        _tituloDetalhe.Location = new Point(m, Px(12) + dy);
        _editarDetalhe.Location = new Point(w - m - _editarDetalhe.Width, Px(8) + dy);
        // Sem foto: nome ocupa toda a largura e os dados começam abaixo dele.
        var yNome = Math.Max(_tituloDetalhe.Bottom - dy, _editarDetalhe.Bottom - dy) + Px(12);
        var larguraTexto = Math.Max(Px(60), w - 2 * m);
        var alturaNome = _nome.Font.Height * 2 + Px(4);
        _nome.SetBounds(m, yNome + dy, larguraTexto, alturaNome);
        var larguraRotulo = Math.Min(Px(125), larguraTexto / 2);
        var yCampos = yNome + alturaNome + Px(8);
        for (var i = 0; i < _campos.Length; i++)
        {
            var y = yCampos + i * linha + dy;
            _campos[i].rotulo.SetBounds(m, y, larguraRotulo, linha);
            _campos[i].valor.SetBounds(m + larguraRotulo, y, Math.Max(Px(20), larguraTexto - larguraRotulo), linha);
        }
        var yCartoes = yCampos + _campos.Length * linha + Px(14);
        var mostrarCusto = _api.Pode(Permissoes.VerCusto);
        var visiveis = mostrarCusto ? 3 : 2;
        var espaco = Px(10);
        var larguraCartao = (w - 2 * m - (visiveis - 1) * espaco) / visiveis;
        var alturaCartao = Tema.Fonte(8.5f).Height + Tema.Semibold(12.5f).Height + Px(26);
        var x = m;
        foreach (var cartao in new Control[] { _tEstoque, _tCusto, _tPreco })
        {
            if (cartao == _tCusto && !mostrarCusto) continue;
            cartao.SetBounds(x, yCartoes + dy, larguraCartao, alturaCartao);
            x += larguraCartao + espaco;
        }
        var yMov = yCartoes + alturaCartao + Px(18);
        _tituloMov.Location = new Point(m, yMov + dy);
        _verTodas.Location = new Point(w - m - _verTodas.Width, yMov + Px(2) + dy);
        var yLista = yMov + _tituloMov.Height + Px(8);
        _movimentos.SetBounds(m, yLista + dy, w - 2 * m, ListaMovimentos.AlturaPara(5, DeviceDpi));
        var yGrafico = yLista + _movimentos.Height + Px(18);
        _tituloGrafico.Location = new Point(m, yGrafico + dy);
        var yG = yGrafico + _tituloGrafico.Height + Px(8);
        _grafico.SetBounds(m, yG + dy, w - 2 * m, Px(200));
        _conteudo.AutoScrollMinSize = new Size(0, yG + Px(200) + Px(12));
    }

    private void AgendarDetalhe()
    {
        if (IsDisposed || Disposing) return;
        _atrasoDetalhe.Stop();
        _atrasoDetalhe.Start();
    }

    /// <summary>Dados que já estão na lista (aparecem na hora); movimentos e gráfico vêm depois.</summary>
    private void MostrarDetalhe(ProdutoDto? p)
    {
        _editarDetalhe.Enabled = p is not null;
        _verTodas.Enabled = p is not null;
        _nome.Text = p?.Descricao ?? "Selecione um produto na lista";
        _campos[0].valor.Text = p?.Codigo ?? "";
        _campos[1].valor.Text = p is null ? "" : p.CategoriaNome ?? "—";
        _campos[2].valor.Text = p?.Unidade ?? "";
        _campos[3].valor.Text = p is null ? "" : string.IsNullOrWhiteSpace(p.CodigoBarras) ? "—" : p.CodigoBarras;
        _tEstoque.Definir(p is null ? "—" : $"{Formatos.Quantidade(p.EstoqueAtual, p.Unidade)} {p.Unidade.ToLowerInvariant()}");
        _tCusto.Definir(p is null ? "—" : Formatos.Moeda(p.PrecoCusto));
        _tPreco.Definir(p is null ? "—" : Formatos.Moeda(p.PrecoVenda));
        if (p is null)
        {
            _movimentos.Definir(null, "");
            _grafico.Definir(null, "");
        }
    }

    private async Task CarregarDetalheAsync()
    {
        var p = Selecionado;
        var versao = ++_versaoDetalhe;
        MostrarDetalhe(p);
        if (p is null || !_detalhe.Visible || IsDisposed) return;

        _movimentos.Definir(null, "Carregando...");
        try
        {
            var movs = await _api.GetAsync<List<MovimentoEstoqueDto>>($"api/estoque/movimentos?produtoId={p.Id}&limite=5");
            if (versao != _versaoDetalhe || IsDisposed) return;
            _movimentos.Unidade = p.Unidade;
            _movimentos.Definir(movs, "Nenhuma movimentação registrada.");
        }
        catch (ApiException ex)
        {
            if (versao == _versaoDetalhe && !IsDisposed) _movimentos.Definir(null, ex.Message);
        }

        _grafico.Definir(null, "Carregando...");
        try
        {
            var serie = await _api.GetAsync<List<SerieProdutoDto>>($"api/produtos/{p.Id}/serie?dias=30");
            if (versao != _versaoDetalhe || IsDisposed) return;
            _grafico.Definir(serie.Select(s => new GraficoVendasEstoque.Ponto(s.Dia, s.Vendido, s.Estoque)));
        }
        catch (ApiException)
        {
            if (versao == _versaoDetalhe && !IsDisposed)
                _grafico.Definir(null, "Gráfico indisponível (atualize a API do servidor).");
        }
    }

    // ---------- Ações ----------

    private void AtualizarMenuLinha()
    {
        var p = Selecionado;
        foreach (var antigo in _menuLinha.Items.Cast<ToolStripItem>().ToList()) antigo.Dispose();
        _menuLinha.Items.Clear();
        _menuLinha.Items.Add("Editar", null, async (_, _) => await EditarAsync()).Enabled = p is not null && PodeEditar;
        _menuLinha.Items.Add("Ajustar estoque...", null, async (_, _) => await AjustarEstoqueAsync()).Enabled = p is not null;
        _menuLinha.Items.Add(p?.Ativo == false ? "Reativar produto" : "Desativar produto", null,
            async (_, _) => await AlternarAtivoAsync()).Enabled = p is not null && PodeEditar;
        _menuLinha.Items.Add(new ToolStripSeparator());
        _menuLinha.Items.Add("Atualizar lista (F5)", null, async (_, _) => await CarregarAsync());
    }

    private async Task NovoAsync()
    {
        var item = new ProdutoDto();
        using var f = new ProdutoEdicaoForm("Novo — Produtos", item, _api);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var salvo = await _api.PostAsync<ProdutoDto>("api/produtos", item);
            _manter = salvo.Id;
            await CarregarAsync();
            SelecionarProduto(salvo.Id);
            await OferecerEnvioAsync(salvo.Id, salvo.Descricao);
        }
        catch (ApiException ex) { Ui.Erro(this, ex.Message); }
    }

    private async Task EditarAsync()
    {
        if (Selecionado is not { } atual) return;
        var copia = JsonSerializer.Deserialize<ProdutoDto>(JsonSerializer.Serialize(atual))!;
        using var f = new ProdutoEdicaoForm("Editar — Produtos", copia, _api);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            await _api.PutAsync<ProdutoDto>($"api/produtos/{copia.Id}", copia);
            _manter = copia.Id;
            await CarregarAsync();
            await OferecerEnvioAsync(copia.Id, copia.Descricao);
        }
        catch (ApiException ex) { Ui.Erro(this, ex.Message); }
    }

    private async Task AlternarAtivoAsync()
    {
        if (Selecionado is not { } p) return;
        try
        {
            if (p.Ativo)
            {
                if (!Ui.Confirmar(this, $"Desativar {p.Descricao}?\n\nNos caixas ele só some depois de enviado aos caixas.")) return;
                await _api.DeleteAsync($"api/produtos/{p.Id}");
            }
            else
            {
                var copia = JsonSerializer.Deserialize<ProdutoDto>(JsonSerializer.Serialize(p))!;
                copia.Ativo = true;
                await _api.PutAsync<ProdutoDto>($"api/produtos/{p.Id}", copia);
            }
            _manter = p.Id;
            await CarregarAsync();
            await OferecerEnvioAsync(p.Id, p.Descricao);
        }
        catch (ApiException ex) { Ui.Erro(this, ex.Message); }
    }

    private async Task AjustarEstoqueAsync()
    {
        var id = Selecionado?.Id;
        using (var f = new EstoqueForm(_api, id))
            f.ShowDialog(this);
        _manter = id;
        await CarregarAsync();
    }

    /// <summary>
    /// Depois de salvar: pergunta se já envia esse produto aos caixas (em vez de só lembrar da tela de carga).
    /// Sem a permissão de enviar, só avisa uma vez por sessão.
    /// </summary>
    private async Task OferecerEnvioAsync(Guid id, string descricao)
    {
        if (!_api.Pode(Permissoes.PublicarCargas))
        {
            if (_lembrouCarga) return;
            _lembrouCarga = true;
            Ui.Info(this, "Salvo na Gestão.\n\nOs caixas só recebem esse produto (ou a alteração) depois que alguém " +
                          "com permissão enviar em Caixas → Enviar produtos aos caixas.\n\n(Este lembrete aparece só uma vez por sessão.)");
            return;
        }
        if (!Ui.Confirmar(this, $"Salvo na Gestão.\n\nEnviar \"{descricao}\" para os caixas agora?\n\n" +
                                "Sim: os caixas recebem na próxima sincronização.\n" +
                                "Não: fica pendente em Caixas → Enviar produtos aos caixas."))
            return;
        try
        {
            var r = await _api.PostAsync<PublicarProdutosResposta>("api/cargas/produtos/publicar",
                new PublicarProdutosRequest { Ids = [id] });
            if (IsDisposed) return;
            if (r.Ignorados.Count > 0)
                Ui.Aviso(this, $"Não foi enviado aos caixas: {r.Ignorados[0].Motivo}.\n\nCorrija o produto e salve de novo.");
            await CarregarAsync(silencioso: true);
        }
        catch (ApiException ex) { if (!IsDisposed) Ui.Erro(this, ex.Message); }
    }

    private IEnumerable<string[]> LinhasExportacao(IEnumerable<ProdutoDto> produtos)
    {
        yield return new[] { "Código", "Produto", "Código de barras", "Categoria", "Unidade", "Estoque", "Estoque mínimo", "Custo", "Preço", "Situação" };
        foreach (var p in produtos)
            yield return new[]
            {
                p.Codigo, p.Descricao, p.CodigoBarras ?? "", p.CategoriaNome ?? "", p.Unidade,
                Formatos.Quantidade(p.EstoqueAtual, p.Unidade), Formatos.Quantidade(p.EstoqueMinimo, p.Unidade),
                Formatos.Moeda(p.PrecoCusto), Formatos.Moeda(p.PrecoVenda), Situacao(p)
            };
    }

    private async void ExportarCsv()
    {
        using var dlg = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = $"produtos-{DateTime.Now:yyyy-MM-dd}.csv" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        static string Csv(string s) =>
            s.Contains(';') || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        try
        {
            var dados = await ObterProdutosFiltradosCompletosAsync();
            var sb = new StringBuilder();
            foreach (var linha in LinhasExportacao(dados)) sb.AppendLine(string.Join(";", linha.Select(Csv)));
            File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
            Ui.Info(this, $"{dados.Count:N0} produto(s) exportado(s).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ApiException)
        {
            Ui.Erro(this, "Não foi possível salvar o arquivo: " + ex.Message);
        }
    }

    private async void CopiarLista()
    {
        try
        {
            var dados = await ObterProdutosFiltradosCompletosAsync();
            var sb = new StringBuilder();
            foreach (var linha in LinhasExportacao(dados)) sb.AppendLine(string.Join("\t", linha.Select(c => c.Replace('\t', ' '))));
            Clipboard.SetText(sb.ToString());
            Ui.Info(this, $"{dados.Count:N0} produto(s) copiado(s). Cole no Excel ou em outro programa.");
        }
        catch (ApiException ex) { Ui.Erro(this, ex.Message); }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            Ui.Aviso(this, "A área de transferência está ocupada. Tente novamente.");
        }
    }
}

/// <summary>Últimas movimentações de um produto: data, tipo (etiqueta colorida), quantidade e origem.</summary>
internal sealed class ListaMovimentos : Control
{
    private int AlturaCabecalho => AlturaCab(DeviceDpi);
    private int AlturaLinha => AlturaLin(DeviceDpi);
    private static int AlturaCab(int dpi) => (int)Math.Round(30 * dpi / 96f);
    private static int AlturaLin(int dpi) => (int)Math.Round(32 * dpi / 96f);
    private List<MovimentoEstoqueDto> _itens = [];
    private string _mensagem = "";

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? Unidade { get; set; }

    public static int AlturaPara(int linhas, int dpi = 96) => AlturaCab(dpi) + linhas * AlturaLin(dpi) + 2;

    public ListaMovimentos()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Tema.Superficie;
    }

    public void Definir(IEnumerable<MovimentoEstoqueDto>? itens, string mensagem)
    {
        _itens = itens?.Take(5).ToList() ?? [];
        _mensagem = mensagem;
        Invalidate();
    }

    private static (string texto, Color cor) Tipo(MovimentoEstoqueDto m) => m.Tipo switch
    {
        TipoMovimentoEstoque.Venda => ("Saída (venda)", Tema.Perigo),
        TipoMovimentoEstoque.Saida => ("Saída", Tema.Perigo),
        TipoMovimentoEstoque.Entrada => ("Entrada", Tema.Sucesso),
        TipoMovimentoEstoque.Ajuste => (m.Quantidade >= 0 ? "Ajuste (+)" : "Ajuste (−)", Tema.Primaria),
        TipoMovimentoEstoque.CancelamentoVenda => ("Venda cancelada", Tema.Sucesso),
        TipoMovimentoEstoque.DevolucaoCliente => ("Devolução", Tema.Sucesso),
        _ => (m.Tipo.ToString(), Tema.TextoSuave)
    };

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var w = Width;
        int[] x = [0, (int)(w * 0.30), (int)(w * 0.58), (int)(w * 0.76), w];
        const TextFormatFlags esquerda = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;

        using (var fundo = new SolidBrush(Tema.CabecalhoGrade)) g.FillRectangle(fundo, 0, 0, w, AlturaCabecalho);
        var fCab = Tema.Semibold(8.5f);
        TextRenderer.DrawText(g, "Data/Hora", fCab, new Rectangle(x[0] + 8, 0, x[1] - x[0] - 8, AlturaCabecalho), Tema.TextoSuave, esquerda);
        TextRenderer.DrawText(g, "Tipo", fCab, new Rectangle(x[1] + 4, 0, x[2] - x[1] - 4, AlturaCabecalho), Tema.TextoSuave, esquerda);
        TextRenderer.DrawText(g, "Quantidade", fCab, new Rectangle(x[2], 0, x[3] - x[2] - 8, AlturaCabecalho), Tema.TextoSuave, esquerda | TextFormatFlags.Right);
        TextRenderer.DrawText(g, "Origem", fCab, new Rectangle(x[3] + 6, 0, x[4] - x[3] - 6, AlturaCabecalho), Tema.TextoSuave, esquerda);

        if (_itens.Count == 0)
        {
            TextRenderer.DrawText(g, _mensagem, Tema.Fonte(9), new Rectangle(0, AlturaCabecalho, w, Height - AlturaCabecalho),
                Tema.TextoSuave, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
            return;
        }

        var fonte = Tema.Fonte(8.5f);
        using var linha = new Pen(Tema.Borda);
        for (var i = 0; i < _itens.Count; i++)
        {
            var m = _itens[i];
            var y = AlturaCabecalho + i * AlturaLinha;
            var data = m.Data.Kind == DateTimeKind.Utc ? m.Data.ToLocalTime() : m.Data;
            TextRenderer.DrawText(g, data.ToString("dd/MM/yyyy HH:mm"), fonte, new Rectangle(x[0] + 8, y, x[1] - x[0] - 8, AlturaLinha), Tema.Texto, esquerda);
            var (texto, cor) = Tipo(m);
            Tema.DesenharEtiqueta(g, new Rectangle(x[1] + 2, y, x[2] - x[1] - 6, AlturaLinha), texto, cor, Tema.Semibold(7.5f));
            var quantidade = (m.Quantidade > 0 ? "+" : m.Quantidade < 0 ? "−" : "") + Formatos.Quantidade(Math.Abs(m.Quantidade), Unidade);
            TextRenderer.DrawText(g, quantidade, Tema.Semibold(9), new Rectangle(x[2], y, x[3] - x[2] - 8, AlturaLinha),
                m.Quantidade < 0 ? Tema.Perigo : m.Quantidade > 0 ? Tema.Sucesso : Tema.Texto, esquerda | TextFormatFlags.Right);
            // Saídas por caixa: "CAIXA 2 · Venda nº 15"; lançamento manual: "Retaguarda · motivo".
            var detalhe = !string.IsNullOrWhiteSpace(m.Observacao) ? m.Observacao! : m.Usuario ?? "";
            var origem = string.IsNullOrWhiteSpace(m.Origem) ? detalhe
                : detalhe.Length == 0 ? m.Origem! : $"{m.Origem} · {detalhe}";
            TextRenderer.DrawText(g, origem, fonte, new Rectangle(x[3] + 6, y, x[4] - x[3] - 8, AlturaLinha), Tema.Texto, esquerda);
            g.DrawLine(linha, 0, y + AlturaLinha - 1, w, y + AlturaLinha - 1);
        }
    }
}
