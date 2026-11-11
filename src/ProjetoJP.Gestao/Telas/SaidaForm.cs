using ProjetoJP.Controles;
using ProjetoJP.Gestao.Servicos;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;

namespace ProjetoJP.Gestao.Telas;

/// <summary>
/// Saída de produtos: vários itens de uma vez (perda, vencido, uso interno, devolução ao fornecedor...).
/// Tira do estoque e mostra quanto isso custou. Em "Saídas lançadas" dá para ver os itens e cancelar.
/// Leitor de código de barras funciona direto no campo Produto (ele manda Enter no fim).
/// </summary>
public class SaidaForm : Form
{
    private sealed class ItemProduto(ProdutoDto p)
    {
        public ProdutoDto Produto { get; } = p;
        public override string ToString() => $"{Produto.Codigo} - {Produto.Descricao}";
    }

    private sealed class Linha
    {
        public required ProdutoDto Produto { get; init; }
        public decimal Quantidade { get; set; }
        public decimal Total => Math.Round(Quantidade * Produto.PrecoCusto, 2);
    }

    private readonly ApiClient _api;
    private readonly bool _verCusto;
    private readonly List<Linha> _itens = [];

    // Nova saída
    private readonly ComboBox _motivo = new() { Font = Ui.Fonte(11), DropDownStyle = ComboBoxStyle.DropDown, MaxLength = 40 };
    private readonly TextBox _destino = new() { Font = Ui.Fonte(11), MaxLength = 120 };
    private readonly TextBox _documento = new() { Font = Ui.Fonte(11), MaxLength = 60 };
    private readonly TextBox _obs = new() { Font = Ui.Fonte(11), MaxLength = 200 };
    private readonly ComboBox _produto = new()
    {
        Font = Ui.Fonte(11), DropDownStyle = ComboBoxStyle.DropDown,
        AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems
    };
    private readonly CampoNumero _quantidade = new() { Font = Ui.Fonte(11), CasasDecimais = 0 };
    private readonly Label _unidade = Ui.Rotulo("", 10);
    private readonly Label _infoProduto = Ui.Rotulo("", 9);
    private readonly TextBox _justificativaNegativo = new() { MaxLength = 150, Font = Ui.Fonte(10), PlaceholderText = "Justifique o saldo negativo (mínimo 10 caracteres)" };
    private readonly CheckBox _negativo = new()
    {
        Text = "Permitir estoque negativo", AutoSize = true, Font = Ui.Fonte(10)
    };
    private readonly Label _totais = Ui.Rotulo("", 12, FontStyle.Bold);
    private readonly DataGridView _grade = Ui.Grade();
    private readonly BotaoTema _finalizar = Ui.Botao("Lançar saída", Ui.Perigo, largura: 180, altura: 40, icone: Icones.Ok);

    // Saídas lançadas
    private readonly DataGridView _gradeLancadas = Ui.Grade();

    private readonly Panel _nova = new() { Dock = DockStyle.Fill };
    private readonly Panel _lancadas = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly BotaoTema _abaNova = Ui.Botao("Nova saída", largura: 150, altura: 36, icone: Icones.Novo);
    private readonly BotaoTema _abaLancadas = Ui.Botao("Saídas lançadas", Ui.Neutro, largura: 180, altura: 36, icone: Icones.Documento);

    public SaidaForm(ApiClient api)
    {
        _api = api;
        _verCusto = api.Pode(Permissoes.VerCusto);
        Text = "Saída de produtos";
        Size = new Size(1100, 720);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Ui.Fundo;
        Font = Ui.Fonte(10);
        Padding = new Padding(20, 12, 20, 16);

        var abas = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, WrapContents = false, BackColor = Color.Transparent };
        _abaNova.Click += (_, _) => MostrarAba(nova: true);
        _abaLancadas.Click += async (_, _) =>
        {
            MostrarAba(nova: false);
            await CarregarLancadas();
        };
        abas.Controls.AddRange([_abaNova, _abaLancadas]);

        MontarNova();
        MontarLancadas();

        Controls.Add(_lancadas);
        Controls.Add(_nova);
        Controls.Add(abas);

        AtualizarGrade();
        Shown += async (_, _) =>
        {
            await CarregarProdutos();
            await CarregarFornecedoresAsync();
            _motivo.Focus();
        };
    }

    private static Panel Espaco(DockStyle lado) => new() { Dock = lado, Height = 10, BackColor = Color.Transparent };

    private void MontarNova()
    {
        // Por que saiu
        var cabecalho = new Panel { Dock = DockStyle.Top, Height = 80, BackColor = Tema.Superficie };
        var lm = Ui.Rotulo("Motivo *");
        lm.Location = new Point(16, 10);
        _motivo.Items.AddRange(MotivosSaida.Todos);
        _motivo.SetBounds(16, 32, 230, 30);
        var ld = Ui.Rotulo("Para quem / para onde (opcional)");
        ld.Location = new Point(262, 10);
        _destino.SetBounds(262, 32, 280, 30);
        var ldoc = Ui.Rotulo("Documento (opcional)");
        ldoc.Location = new Point(558, 10);
        _documento.SetBounds(558, 32, 150, 30);
        var lo = Ui.Rotulo("Observação");
        lo.Location = new Point(724, 10);
        _obs.SetBounds(724, 32, 300, 30);
        cabecalho.Controls.AddRange([lm, _motivo, ld, _destino, ldoc, _documento, lo, _obs]);
        cabecalho.Resize += (_, _) => _obs.Width = Math.Max(160, cabecalho.ClientSize.Width - 724 - 16);

        // Produto a tirar
        var item = new Panel { Dock = DockStyle.Top, Height = 100, BackColor = Tema.Superficie };
        var lp = Ui.Rotulo("Produto (código, código de barras ou nome)");
        lp.Location = new Point(16, 8);
        _produto.SetBounds(16, 30, 560, 30);
        var lq = Ui.Rotulo("Quantidade");
        lq.Location = new Point(592, 8);
        _quantidade.SetBounds(592, 30, 120, 30);
        _unidade.ForeColor = Ui.Neutro;
        _unidade.Location = new Point(716, 35);
        var adicionar = Ui.Botao("Adicionar", largura: 140, altura: 34, icone: Icones.Novo);
        adicionar.Location = new Point(770, 28);
        adicionar.Click += (_, _) => Adicionar();
        _infoProduto.ForeColor = Ui.Neutro;
        _infoProduto.Location = new Point(16, 70);
        item.Controls.AddRange([lp, _produto, lq, _quantidade, _unidade, adicionar, _infoProduto]);

        // Enter vai passando: produto → quantidade → adiciona.
        _produto.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            if (EscolherProduto())
            {
                _quantidade.Focus();
                _quantidade.SelectAll();
            }
        };
        _quantidade.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            Adicionar();
        };
        _produto.SelectedIndexChanged += (_, _) => ProdutoMudou();

        // Itens da saída
        _grade.Dock = DockStyle.Fill;
        _grade.Columns.AddRange(
            Ui.ColunaTexto("Código", 12), Ui.ColunaTexto("Produto", 40), Ui.ColunaTexto("Un", 6),
            Ui.ColunaTexto("Quantidade", 12, true), Ui.ColunaTexto("Estoque atual", 12, true),
            Ui.ColunaTexto("Custo unit.", 12, true), Ui.ColunaTexto("Total (custo)", 12, true));
        if (!_verCusto) _grade.Columns[5].Visible = _grade.Columns[6].Visible = false;
        _grade.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) EditarLinha(e.RowIndex); };
        _grade.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Delete) return;
            e.Handled = true;
            RemoverSelecionado();
        };
        var dica = Ui.Rotulo("Duplo clique num item para corrigir  ·  Delete para remover  ·  Em vermelho: ficaria com estoque negativo", 9);
        dica.ForeColor = Ui.Neutro;
        dica.Dock = DockStyle.Top;
        dica.Padding = new Padding(0, 0, 0, 6);
        var cartao = new Panel { Dock = DockStyle.Fill, BackColor = Tema.Superficie, Padding = new Padding(16, 10, 16, 12) };
        cartao.Controls.Add(_grade);
        cartao.Controls.Add(dica);

        // Rodapé: total e botões
        var rodape = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Tema.Superficie };
        _negativo.Location = new Point(16, 22);
        _totais.Location = new Point(260, 20);
        var remover = Ui.Botao("Remover item", Ui.Neutro, largura: 150, altura: 40, icone: Icones.Remover);
        remover.Click += (_, _) => RemoverSelecionado();
        var limpar = Ui.Botao("Limpar", Ui.Neutro, largura: 110, altura: 40, icone: Icones.Cancelar);
        limpar.Click += (_, _) =>
        {
            if (_itens.Count == 0 || Ui.Confirmar(this, "Descartar os itens desta saída?")) Limpar();
        };
        _finalizar.Click += async (_, _) => await Finalizar();
        _negativo.Enabled = _api.Pode(Permissoes.PermitirEstoqueNegativo);
        _negativo.CheckedChanged += (_, _) =>
        {
            _justificativaNegativo.Visible = _negativo.Checked;
            if (_negativo.Checked) _justificativaNegativo.Focus();
            else _justificativaNegativo.Clear();
        };
        rodape.Controls.AddRange([_negativo, _totais, remover, limpar, _finalizar]);
        rodape.Resize += (_, _) =>
        {
            _finalizar.Location = new Point(rodape.Width - 16 - _finalizar.Width, 12);
            limpar.Location = new Point(_finalizar.Left - 10 - limpar.Width, 12);
            remover.Location = new Point(limpar.Left - 10 - remover.Width, 12);
        };

        var faixaJustificativa = new Panel { Dock = DockStyle.Bottom, Height = 44, BackColor = Tema.Superficie };
        _justificativaNegativo.SetBounds(16, 6, 560, 30);
        _justificativaNegativo.Visible = false;
        _justificativaNegativo.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        faixaJustificativa.Controls.Add(_justificativaNegativo);
        _negativo.CheckedChanged += (_, _) => faixaJustificativa.Visible = _negativo.Checked;
        faixaJustificativa.Visible = false;

        _nova.Controls.Add(cartao);
        _nova.Controls.Add(Espaco(DockStyle.Top));
        _nova.Controls.Add(item);
        _nova.Controls.Add(Espaco(DockStyle.Top));
        _nova.Controls.Add(cabecalho);
        _nova.Controls.Add(Espaco(DockStyle.Bottom));
        _nova.Controls.Add(faixaJustificativa);
        _nova.Controls.Add(rodape);
    }

    private void MontarLancadas()
    {
        _gradeLancadas.Dock = DockStyle.Fill;
        _gradeLancadas.Columns.AddRange(
            Ui.ColunaTexto("Nº", 7, true), Ui.ColunaTexto("Data", 14), Ui.ColunaTexto("Motivo", 18),
            Ui.ColunaTexto("Para quem / onde", 20), Ui.ColunaTexto("Itens", 7, true), Ui.ColunaTexto("Valor (custo)", 12, true),
            Ui.ColunaTexto("Usuário", 14), Ui.ColunaTexto("Situação", 10));
        if (!_verCusto) _gradeLancadas.Columns[5].Visible = false;
        _gradeLancadas.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0) await VerItens(); };
        var cartao = new Panel { Dock = DockStyle.Fill, BackColor = Tema.Superficie, Padding = new Padding(16) };
        cartao.Controls.Add(_gradeLancadas);

        var rodape = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Tema.Superficie };
        var ver = Ui.Botao("Ver itens", largura: 140, altura: 40, icone: Icones.Documento);
        ver.Click += async (_, _) => await VerItens();
        var cancelar = Ui.Botao("Cancelar saída", Ui.Perigo, largura: 170, altura: 40, icone: Icones.Cancelar);
        cancelar.Click += async (_, _) => await CancelarSaida();
        var atualizar = Ui.Botao("Atualizar", Ui.Neutro, largura: 130, altura: 40, icone: Icones.Atualizar);
        atualizar.Click += async (_, _) => await CarregarLancadas();
        var exportar = Ui.Botao("Exportar", Ui.Neutro, largura: 120, altura: 40, icone: Icones.Exportar);
        exportar.Click += (_, _) => Ui.ExportarCsv(this, _gradeLancadas, "saidas");
        ver.Location = new Point(16, 12);
        cancelar.Location = new Point(166, 12);
        atualizar.Location = new Point(346, 12);
        exportar.Location = new Point(486, 12);
        rodape.Controls.AddRange([ver, cancelar, atualizar, exportar]);

        _lancadas.Controls.Add(cartao);
        _lancadas.Controls.Add(Espaco(DockStyle.Bottom));
        _lancadas.Controls.Add(rodape);
    }

    private void MostrarAba(bool nova)
    {
        _nova.Visible = nova;
        _lancadas.Visible = !nova;
        _abaNova.Estilo = nova ? EstiloBotao.Primario : EstiloBotao.Secundario;
        _abaLancadas.Estilo = nova ? EstiloBotao.Secundario : EstiloBotao.Primario;
    }

    // ---------- Nova saída ----------

    private ProdutoDto? ProdutoAtual => (_produto.SelectedItem as ItemProduto)?.Produto;

    /// <summary>Sugere os fornecedores cadastrados em "Para quem" (devolução ao fornecedor).</summary>
    private async Task CarregarFornecedoresAsync()
    {
        if (!_api.Pode(Permissoes.Fornecedores)) return;
        try
        {
            var fornecedores = await _api.GetAsync<List<FornecedorDto>>("api/fornecedores");
            if (IsDisposed) return;
            var nomes = new AutoCompleteStringCollection();
            nomes.AddRange(fornecedores.Where(f => f.Ativo).Select(f => f.Nome).Distinct().ToArray());
            _destino.AutoCompleteCustomSource = nomes;
            _destino.AutoCompleteSource = AutoCompleteSource.CustomSource;
            _destino.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        }
        catch (ApiException) { /* digita normalmente */ }
    }

    private async Task CarregarProdutos()
    {
        try
        {
            var produtos = await _api.GetAsync<List<ProdutoDto>>("api/produtos");
            if (IsDisposed) return;
            _produto.BeginUpdate();
            _produto.Items.Clear();
            foreach (var p in produtos.Where(p => p.Ativo).OrderBy(p => p.Descricao))
                _produto.Items.Add(new ItemProduto(p));
            _produto.EndUpdate();
            // Itens já na lista passam a usar o saldo e o custo atualizados.
            var porId = produtos.ToDictionary(p => p.Id);
            for (var i = 0; i < _itens.Count; i++)
                if (porId.TryGetValue(_itens[i].Produto.Id, out var novo))
                    _itens[i] = new Linha { Produto = novo, Quantidade = _itens[i].Quantidade };
            AtualizarGrade();
        }
        catch (ApiException ex) { if (!IsDisposed) Ui.Erro(this, ex.Message); }
    }

    /// <summary>Acha o produto pelo que foi digitado: código, código de barras ou parte do nome (aceita erro de digitação).</summary>
    private bool EscolherProduto()
    {
        if (_produto.SelectedItem is ItemProduto sel && _produto.Text == sel.ToString()) return true;

        var texto = _produto.Text.Trim();
        if (texto.Length == 0) return false;
        var itens = _produto.Items.Cast<ItemProduto>().ToList();
        var achado =
            itens.FirstOrDefault(i => string.Equals(i.Produto.Codigo, texto, StringComparison.OrdinalIgnoreCase)) ??
            itens.FirstOrDefault(i => string.Equals(i.Produto.CodigoBarras, texto, StringComparison.OrdinalIgnoreCase)) ??
            itens.FirstOrDefault(i => string.Equals(i.ToString(), texto, StringComparison.OrdinalIgnoreCase)) ??
            itens.Select(i => (i, pontos: PesquisaProdutos.Pontuar(texto, i.Produto.Descricao, i.Produto.Codigo, i.Produto.CodigoBarras)))
                .Where(x => x.pontos >= 0).OrderByDescending(x => x.pontos).Select(x => x.i).FirstOrDefault();
        if (achado is null)
        {
            Ui.Aviso(this, $"Nenhum produto encontrado para \"{texto}\".");
            _produto.Focus();
            _produto.SelectAll();
            return false;
        }
        _produto.SelectedItem = achado;
        return true;
    }

    private void ProdutoMudou()
    {
        var p = ProdutoAtual;
        _unidade.Text = p?.Unidade ?? "";
        _quantidade.CasasDecimais = Formatos.CasasQuantidade(p?.Unidade);
        if (p is null)
        {
            _infoProduto.Text = "";
            return;
        }
        var naLista = _itens.FirstOrDefault(l => l.Produto.Id == p.Id)?.Quantidade ?? 0;
        _infoProduto.Text = $"Estoque atual: {Formatos.Quantidade(p.EstoqueAtual, p.Unidade)} {p.Unidade}" +
                            (naLista > 0 ? $"   ·   já na lista: {Formatos.Quantidade(naLista, p.Unidade)}" : "") +
                            (_verCusto ? $"   ·   Custo: {Formatos.Moeda(p.PrecoCusto)}" : "");
    }

    private void Adicionar()
    {
        if (!EscolherProduto())
        {
            _produto.Focus();
            return;
        }
        var p = ProdutoAtual!;
        var qtd = _quantidade.Valor;
        if (qtd <= 0)
        {
            Ui.Aviso(this, "Informe a quantidade.");
            _quantidade.Focus();
            return;
        }
        var existente = _itens.FirstOrDefault(l => l.Produto.Id == p.Id);
        if (existente is not null) existente.Quantidade += qtd;
        else _itens.Add(new Linha { Produto = p, Quantidade = qtd });
        AtualizarGrade(p.Id);

        _produto.SelectedIndex = -1;
        _produto.Text = "";
        _quantidade.Valor = 0;
        _infoProduto.Text = existente is null ? "" : $"{p.Descricao}: quantidade somada ao item que já estava na lista.";
        _produto.Focus();
    }

    private void AtualizarGrade(Guid? selecionar = null)
    {
        _grade.Rows.Clear();
        foreach (var l in _itens)
        {
            var p = l.Produto;
            var i = _grade.Rows.Add(p.Codigo, p.Descricao, p.Unidade, Formatos.Quantidade(l.Quantidade, p.Unidade),
                Formatos.Quantidade(p.EstoqueAtual, p.Unidade), Formatos.Moeda(p.PrecoCusto), Formatos.Moeda(l.Total));
            _grade.Rows[i].Tag = l;
            if (l.Quantidade > p.EstoqueAtual) _grade.Rows[i].DefaultCellStyle.ForeColor = Ui.Perigo;
            if (p.Id == selecionar)
            {
                _grade.ClearSelection();
                _grade.Rows[i].Selected = true;
                try { _grade.FirstDisplayedScrollingRowIndex = i; }
                catch (InvalidOperationException) { /* grade ainda sem tamanho */ }
            }
        }
        _totais.Text = _itens.Count == 0
            ? "Nenhum item ainda"
            : $"{_itens.Count} item(ns)" + (_verCusto ? $"   ·   Valor de custo: {Formatos.Moeda(_itens.Sum(l => l.Total))}" : "");
        _finalizar.Enabled = _itens.Count > 0;
    }

    private void EditarLinha(int indice)
    {
        if (_grade.Rows[indice].Tag is not Linha l) return;
        var item = _produto.Items.Cast<ItemProduto>().FirstOrDefault(i => i.Produto.Id == l.Produto.Id);
        if (item is null) return;
        _itens.Remove(l);
        AtualizarGrade();
        _produto.SelectedItem = item;
        _quantidade.Valor = l.Quantidade;
        _infoProduto.Text = "Corrija a quantidade e clique em Adicionar.";
        _quantidade.Focus();
        _quantidade.SelectAll();
    }

    private void RemoverSelecionado()
    {
        if (_grade.CurrentRow?.Tag is not Linha l)
        {
            Ui.Aviso(this, "Escolha um item da lista.");
            return;
        }
        _itens.Remove(l);
        AtualizarGrade();
    }

    private void Limpar()
    {
        _itens.Clear();
        _motivo.Text = "";
        _destino.Clear();
        _documento.Clear();
        _obs.Clear();
        _negativo.Checked = false;
        _justificativaNegativo.Clear();
        _produto.SelectedIndex = -1;
        _produto.Text = "";
        _quantidade.Valor = 0;
        AtualizarGrade();
        _motivo.Focus();
    }

    private async Task Finalizar()
    {
        if (_itens.Count == 0)
        {
            Ui.Aviso(this, "Adicione pelo menos um produto.");
            return;
        }
        var motivo = _motivo.Text.Trim();
        if (motivo.Length == 0)
        {
            Ui.Aviso(this, "Escolha o motivo da saída (perda, vencido, uso interno...).");
            _motivo.Focus();
            return;
        }
        if (motivo == "Outro" && _obs.Text.Trim().Length == 0)
        {
            Ui.Aviso(this, "Motivo \"Outro\": explique na Observação.");
            _obs.Focus();
            return;
        }
        var negativos = _itens.Count(l => l.Quantidade > l.Produto.EstoqueAtual);
        if (negativos > 0 && !_negativo.Checked)
        {
            Ui.Aviso(this, $"{negativos} item(ns) ficariam com estoque negativo (em vermelho).\n\n" +
                           "Confira as quantidades ou marque \"Permitir estoque negativo\".");
            return;
        }
        if (_negativo.Checked && _justificativaNegativo.Text.Trim().Length < 10)
        {
            Ui.Aviso(this, "Explique por que o estoque ficará negativo (mínimo 10 caracteres).");
            _justificativaNegativo.Focus();
            return;
        }
        if (!Ui.Confirmar(this, $"Lançar a saída ({motivo}) com {_itens.Count} item(ns)?" +
                                (_verCusto ? $"\nValor de custo: {Formatos.Moeda(_itens.Sum(l => l.Total))}" : "") +
                                "\n\nAs quantidades saem do estoque agora."))
            return;

        _finalizar.Enabled = false;
        try
        {
            var r = await _api.PostAsync<SaidaDto>("api/estoque/saidas", new SaidaRequest
            {
                Motivo = motivo,
                Destino = _destino.Text.Trim(),
                Documento = _documento.Text.Trim(),
                Observacao = _obs.Text.Trim(),
                PermitirNegativo = _negativo.Checked,
                JustificativaNegativo = _negativo.Checked ? _justificativaNegativo.Text.Trim() : null,
                Itens = _itens.Select(l => new SaidaItemRequest { ProdutoId = l.Produto.Id, Quantidade = l.Quantidade }).ToList()
            });
            Ui.Info(this, $"Saída nº {r.Numero} lançada: {r.QuantidadeItens} item(ns)" +
                          (_verCusto ? $", valor de custo {Formatos.Moeda(r.Total)}." : "."));
            Limpar();
            await CarregarProdutos(); // saldo já atualizado
        }
        catch (ApiException ex) { Ui.Erro(this, ex.Message); }
        finally
        {
            if (!IsDisposed) _finalizar.Enabled = _itens.Count > 0;
        }
    }

    // ---------- Saídas lançadas ----------

    private async Task CarregarLancadas()
    {
        try
        {
            var saidas = await _api.GetAsync<List<SaidaDto>>("api/estoque/saidas");
            if (IsDisposed) return;
            _gradeLancadas.Rows.Clear();
            foreach (var s in saidas)
            {
                var data = s.Data.Kind == DateTimeKind.Utc ? s.Data.ToLocalTime() : s.Data;
                var i = _gradeLancadas.Rows.Add(s.Numero, data.ToString("dd/MM/yyyy HH:mm", Ui.Br), s.Motivo, s.Destino ?? "",
                    s.QuantidadeItens, Formatos.Moeda(s.Total), s.Usuario ?? "", s.Cancelada ? "Cancelada" : "Lançada");
                var linha = _gradeLancadas.Rows[i];
                linha.Tag = s;
                if (s.Cancelada) linha.DefaultCellStyle.ForeColor = Ui.Neutro;
            }
        }
        catch (ApiException ex) { if (!IsDisposed) Ui.Erro(this, ex.Message); }
    }

    private async Task VerItens()
    {
        if (_gradeLancadas.CurrentRow?.Tag is not SaidaDto escolhida)
        {
            Ui.Aviso(this, "Escolha uma saída na lista.");
            return;
        }
        SaidaDto s;
        try { s = await _api.GetAsync<SaidaDto>($"api/estoque/saidas/{escolhida.Id}"); }
        catch (ApiException ex)
        {
            Ui.Erro(this, ex.Message);
            return;
        }

        using var f = new Form();
        Ui.Dialogo(f, $"Saída nº {s.Numero}", 820, 520);
        var titulo = Ui.Rotulo($"Saída nº {s.Numero} — {s.Motivo}" + (s.Cancelada ? "  (CANCELADA)" : ""), 13, FontStyle.Bold);
        titulo.Location = new Point(20, 16);
        if (s.Cancelada) titulo.ForeColor = Ui.Perigo;

        var data = s.Data.Kind == DateTimeKind.Utc ? s.Data.ToLocalTime() : s.Data;
        var partes = new List<string> { data.ToString("dd/MM/yyyy HH:mm", Ui.Br) };
        if (s.Destino is not null) partes.Add("Para: " + s.Destino);
        if (s.Documento is not null) partes.Add("Doc.: " + s.Documento);
        if (s.Usuario is not null) partes.Add("Por: " + s.Usuario);
        var detalhes = Ui.Rotulo(string.Join("   ·   ", partes), 9.5f);
        detalhes.ForeColor = Ui.Neutro;
        detalhes.Location = new Point(22, 48);

        var grade = Ui.Grade();
        grade.Location = new Point(20, 78);
        grade.Size = new Size(780, 340);
        grade.Columns.AddRange(
            Ui.ColunaTexto("Código", 12), Ui.ColunaTexto("Produto", 40), Ui.ColunaTexto("Un", 6),
            Ui.ColunaTexto("Quantidade", 12, true), Ui.ColunaTexto("Custo unit.", 12, true), Ui.ColunaTexto("Total", 12, true));
        if (!_verCusto) grade.Columns[4].Visible = grade.Columns[5].Visible = false;
        foreach (var i in s.Itens)
            grade.Rows.Add(i.Codigo, i.Descricao, i.Unidade, Formatos.Quantidade(i.Quantidade, i.Unidade),
                Formatos.Moeda(i.CustoUnitario), Formatos.Moeda(i.Total));

        var fechar = Ui.Botao("Fechar", Ui.Neutro, largura: 110, altura: 38);
        fechar.Location = new Point(820 - 20 - 110, 466);
        fechar.DialogResult = DialogResult.OK;
        f.Controls.AddRange([titulo, detalhes, grade, fechar]);
        if (_verCusto)
        {
            var total = Ui.Rotulo($"Valor de custo: {Formatos.Moeda(s.Total)}", 12, FontStyle.Bold);
            total.Location = new Point(20, 432);
            f.Controls.Add(total);
        }
        if (s.Observacao is not null)
        {
            var obs = Ui.Rotulo("Observação: " + s.Observacao, 9.5f);
            obs.ForeColor = Ui.Neutro;
            obs.MaximumSize = new Size(660, 0);
            obs.Location = new Point(20, 466);
            f.Controls.Add(obs);
        }
        f.AcceptButton = fechar;
        f.CancelButton = fechar;
        f.ShowDialog(this);
    }

    private async Task CancelarSaida()
    {
        if (_gradeLancadas.CurrentRow?.Tag is not SaidaDto s)
        {
            Ui.Aviso(this, "Escolha uma saída na lista.");
            return;
        }
        if (s.Cancelada)
        {
            Ui.Aviso(this, "Essa saída já foi cancelada.");
            return;
        }
        if (!Ui.Confirmar(this, $"Cancelar a saída nº {s.Numero} ({s.Motivo})?\n\nAs quantidades dela voltam para o estoque."))
            return;
        try
        {
            await _api.PostAsync<SaidaDto>($"api/estoque/saidas/{s.Id}/cancelar", new { });
            await CarregarLancadas();
            await CarregarProdutos();
        }
        catch (ApiException ex) { Ui.Erro(this, ex.Message); }
    }
}
