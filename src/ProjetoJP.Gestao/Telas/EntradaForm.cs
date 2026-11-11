using ProjetoJP.Controles;
using ProjetoJP.Gestao.Servicos;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;

namespace ProjetoJP.Gestao.Telas;

/// <summary>
/// Entrada de produtos: vários itens de uma vez (compra, nota do fornecedor). Soma ao estoque e, se marcado,
/// atualiza o preço de custo. Em "Entradas lançadas" dá para ver os itens de cada uma e cancelar.
/// Leitor de código de barras funciona direto no campo Produto (ele manda Enter no fim).
/// </summary>
public class EntradaForm : Form
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
        public decimal Custo { get; set; }
        public decimal Total => Math.Round(Quantidade * Custo, 2);
    }

    private readonly ApiClient _api;
    private readonly List<Linha> _itens = [];

    // Nova entrada
    private readonly TextBox _fornecedor = new() { Font = Ui.Fonte(11), MaxLength = 120 };
    private readonly TextBox _documento = new() { Font = Ui.Fonte(11), MaxLength = 60 };
    private readonly TextBox _obs = new() { Font = Ui.Fonte(11), MaxLength = 200 };
    private readonly ComboBox _produto = new()
    {
        Font = Ui.Fonte(11), DropDownStyle = ComboBoxStyle.DropDown,
        AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems
    };
    private readonly CampoNumero _quantidade = new() { Font = Ui.Fonte(11), CasasDecimais = 0 };
    private readonly CampoMoeda _custo = new() { Font = Ui.Fonte(11) }; // R$ como no cadastro de produtos
    private readonly Label _unidade = Ui.Rotulo("", 10);
    private readonly Label _infoProduto = Ui.Rotulo("", 9);
    private readonly CheckBox _atualizarCusto = new()
    {
        Text = "Atualizar o preço de custo dos produtos", Checked = true, AutoSize = true, Font = Ui.Fonte(10)
    };
    private readonly Label _totais = Ui.Rotulo("", 12, FontStyle.Bold);
    private readonly DataGridView _grade = Ui.Grade();
    private readonly BotaoTema _finalizar = Ui.Botao("Finalizar entrada", Ui.Sucesso, largura: 190, altura: 40, icone: Icones.Ok);

    // Entradas lançadas
    private readonly DataGridView _gradeLancadas = Ui.Grade();

    private readonly Panel _nova = new() { Dock = DockStyle.Fill };
    private readonly Panel _lancadas = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly BotaoTema _abaNova = Ui.Botao("Nova entrada", largura: 160, altura: 36, icone: Icones.Novo);
    private readonly BotaoTema _abaLancadas = Ui.Botao("Entradas lançadas", Ui.Neutro, largura: 190, altura: 36, icone: Icones.Documento);

    public EntradaForm(ApiClient api)
    {
        _api = api;
        Text = "Entrada de produtos";
        Size = new Size(1100, 720);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Ui.Fundo;
        Font = Ui.Fonte(10);
        Padding = new Padding(20, 12, 20, 16);

        // Alterna entre lançar uma entrada nova e consultar as que já foram lançadas.
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
            _fornecedor.Focus();
        };
    }

    private static Panel Espaco(DockStyle lado) => new() { Dock = lado, Height = 10, BackColor = Color.Transparent };

    private void MontarNova()
    {
        // De quem veio
        var cabecalho = new Panel { Dock = DockStyle.Top, Height = 80, BackColor = Tema.Superficie };
        var lf = Ui.Rotulo("Fornecedor");
        lf.Location = new Point(16, 10);
        _fornecedor.SetBounds(16, 32, 360, 30);
        var ld = Ui.Rotulo("Nº da nota / documento");
        ld.Location = new Point(392, 10);
        _documento.SetBounds(392, 32, 190, 30);
        var lo = Ui.Rotulo("Observação");
        lo.Location = new Point(598, 10);
        _obs.SetBounds(598, 32, 420, 30);
        cabecalho.Controls.AddRange([lf, _fornecedor, ld, _documento, lo, _obs]);

        // Produto a adicionar
        var item = new Panel { Dock = DockStyle.Top, Height = 100, BackColor = Tema.Superficie };
        var lp = Ui.Rotulo("Produto (código, código de barras ou nome)");
        lp.Location = new Point(16, 8);
        _produto.SetBounds(16, 30, 520, 30);
        var lq = Ui.Rotulo("Quantidade");
        lq.Location = new Point(552, 8);
        _quantidade.SetBounds(552, 30, 110, 30);
        _unidade.ForeColor = Ui.Neutro;
        _unidade.Location = new Point(666, 35);
        var lc = Ui.Rotulo("Custo unitário (R$)");
        lc.Location = new Point(712, 8);
        _custo.SetBounds(712, 30, 130, 30);
        var adicionar = Ui.Botao("Adicionar", largura: 140, altura: 34, icone: Icones.Novo);
        adicionar.Location = new Point(858, 28);
        adicionar.Click += (_, _) => Adicionar();
        _infoProduto.ForeColor = Ui.Neutro;
        _infoProduto.Location = new Point(16, 70);
        item.Controls.AddRange([lp, _produto, lq, _quantidade, _unidade, lc, _custo, adicionar, _infoProduto]);

        // Enter vai passando: produto → quantidade → custo → adiciona.
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
            _custo.Focus();
            _custo.SelectAll();
        };
        _custo.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            Adicionar();
        };
        _produto.SelectedIndexChanged += (_, _) => ProdutoMudou();

        // Itens da entrada
        _grade.Dock = DockStyle.Fill;
        _grade.Columns.AddRange(
            Ui.ColunaTexto("Código", 12), Ui.ColunaTexto("Produto", 40), Ui.ColunaTexto("Un", 6),
            Ui.ColunaTexto("Quantidade", 12, true), Ui.ColunaTexto("Custo unit.", 12, true), Ui.ColunaTexto("Total", 12, true));
        _grade.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) EditarLinha(e.RowIndex); };
        _grade.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Delete) return;
            e.Handled = true;
            RemoverSelecionado();
        };
        var dica = Ui.Rotulo("Duplo clique num item para corrigir  ·  Delete para remover", 9);
        dica.ForeColor = Ui.Neutro;
        dica.Dock = DockStyle.Top;
        dica.Padding = new Padding(0, 0, 0, 6);
        var cartao = new Panel { Dock = DockStyle.Fill, BackColor = Tema.Superficie, Padding = new Padding(16, 10, 16, 12) };
        cartao.Controls.Add(_grade);
        cartao.Controls.Add(dica);

        // Rodapé: total e botões
        var rodape = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Tema.Superficie };
        _atualizarCusto.Location = new Point(16, 22);
        _totais.Location = new Point(360, 20);
        var remover = Ui.Botao("Remover item", Ui.Neutro, largura: 150, altura: 40, icone: Icones.Remover);
        remover.Click += (_, _) => RemoverSelecionado();
        var limpar = Ui.Botao("Limpar", Ui.Neutro, largura: 110, altura: 40, icone: Icones.Cancelar);
        limpar.Click += (_, _) =>
        {
            if (_itens.Count == 0 || Ui.Confirmar(this, "Descartar os itens desta entrada?")) Limpar();
        };
        _finalizar.Click += async (_, _) => await Finalizar();
        rodape.Controls.AddRange([_atualizarCusto, _totais, remover, limpar, _finalizar]);
        rodape.Resize += (_, _) =>
        {
            _finalizar.Location = new Point(rodape.Width - 16 - _finalizar.Width, 12);
            limpar.Location = new Point(_finalizar.Left - 10 - limpar.Width, 12);
            remover.Location = new Point(limpar.Left - 10 - remover.Width, 12);
        };

        // Ordem do Dock: o último adicionado fica na borda (Fill sempre primeiro).
        _nova.Controls.Add(cartao);
        _nova.Controls.Add(Espaco(DockStyle.Top));
        _nova.Controls.Add(item);
        _nova.Controls.Add(Espaco(DockStyle.Top));
        _nova.Controls.Add(cabecalho);
        _nova.Controls.Add(Espaco(DockStyle.Bottom));
        _nova.Controls.Add(rodape);
    }

    private void MontarLancadas()
    {
        _gradeLancadas.Dock = DockStyle.Fill;
        _gradeLancadas.Columns.AddRange(
            Ui.ColunaTexto("Nº", 7, true), Ui.ColunaTexto("Data", 14), Ui.ColunaTexto("Fornecedor", 26),
            Ui.ColunaTexto("Nota / documento", 14), Ui.ColunaTexto("Itens", 7, true), Ui.ColunaTexto("Total", 12, true),
            Ui.ColunaTexto("Usuário", 14), Ui.ColunaTexto("Situação", 10));
        _gradeLancadas.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0) await VerItens(); };
        var cartao = new Panel { Dock = DockStyle.Fill, BackColor = Tema.Superficie, Padding = new Padding(16) };
        cartao.Controls.Add(_gradeLancadas);

        var rodape = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Tema.Superficie };
        var ver = Ui.Botao("Ver itens", largura: 140, altura: 40, icone: Icones.Documento);
        ver.Click += async (_, _) => await VerItens();
        var cancelar = Ui.Botao("Cancelar entrada", Ui.Perigo, largura: 180, altura: 40, icone: Icones.Cancelar);
        cancelar.Click += async (_, _) => await CancelarEntrada();
        var atualizar = Ui.Botao("Atualizar", Ui.Neutro, largura: 130, altura: 40, icone: Icones.Atualizar);
        atualizar.Click += async (_, _) => await CarregarLancadas();
        ver.Location = new Point(16, 12);
        cancelar.Location = new Point(166, 12);
        atualizar.Location = new Point(356, 12);
        rodape.Controls.AddRange([ver, cancelar, atualizar]);

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
        _abaNova.Invalidate();
        _abaLancadas.Invalidate();
    }

    // ---------- Nova entrada ----------

    private ProdutoDto? ProdutoAtual => (_produto.SelectedItem as ItemProduto)?.Produto;

    /// <summary>Sugere os fornecedores cadastrados ao digitar (o campo continua livre).</summary>
    private async Task CarregarFornecedoresAsync()
    {
        try
        {
            var fornecedores = await _api.GetAsync<List<FornecedorDto>>("api/fornecedores");
            if (IsDisposed) return;
            var nomes = new AutoCompleteStringCollection();
            nomes.AddRange(fornecedores.Where(f => f.Ativo).Select(f => f.Nome).Distinct().ToArray());
            _fornecedor.AutoCompleteCustomSource = nomes;
            _fornecedor.AutoCompleteSource = AutoCompleteSource.CustomSource;
            _fornecedor.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        }
        catch (ApiException) { /* sem a lista: digita o nome normalmente */ }
    }

    private async Task CarregarProdutos()
    {
        try
        {
            var produtos = await _api.GetAsync<List<ProdutoDto>>("api/produtos");
            _produto.BeginUpdate();
            _produto.Items.Clear();
            foreach (var p in produtos.Where(p => p.Ativo).OrderBy(p => p.Descricao))
                _produto.Items.Add(new ItemProduto(p));
            _produto.EndUpdate();
        }
        catch (ApiException ex) { Ui.Erro(this, ex.Message); }
    }

    /// <summary>Acha o produto pelo que foi digitado: código, código de barras ou parte do nome.</summary>
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
            itens.FirstOrDefault(i => i.Produto.Descricao.Contains(texto, StringComparison.OrdinalIgnoreCase));
        if (achado is null)
        {
            Ui.Aviso(this, $"Nenhum produto encontrado para \"{texto}\".\n\nSe for um produto novo, cadastre-o antes em Produtos.");
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
        // KG/L/M... aceitam 3 casas; UN/CX/PC... só inteiros
        _quantidade.CasasDecimais = Formatos.CasasQuantidade(p?.Unidade);
        if (p is null)
        {
            _infoProduto.Text = "";
            return;
        }
        _custo.Valor = p.PrecoCusto;
        _infoProduto.Text = $"Estoque atual: {Formatos.Quantidade(p.EstoqueAtual, p.Unidade)} {p.Unidade}   ·   " +
                            $"Custo atual: {Formatos.Moeda(p.PrecoCusto)}   ·   Venda: {Formatos.Moeda(p.PrecoVenda)}";
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
        var custo = _custo.Valor;

        // O mesmo produto duas vezes vira uma linha só (soma a quantidade; fica o custo mais recente).
        var existente = _itens.FirstOrDefault(l => l.Produto.Id == p.Id);
        if (existente is not null)
        {
            existente.Quantidade += qtd;
            existente.Custo = custo;
        }
        else
        {
            _itens.Add(new Linha { Produto = p, Quantidade = qtd, Custo = custo });
        }
        AtualizarGrade(p.Id);

        _produto.SelectedIndex = -1;
        _produto.Text = "";
        _quantidade.Valor = 0;
        _custo.Valor = 0;
        _infoProduto.Text = existente is null ? "" : $"{p.Descricao}: quantidade somada ao item que já estava na lista.";
        _produto.Focus();
    }

    private void AtualizarGrade(Guid? selecionar = null)
    {
        _grade.Rows.Clear();
        foreach (var l in _itens)
        {
            var i = _grade.Rows.Add(l.Produto.Codigo, l.Produto.Descricao, l.Produto.Unidade,
                Formatos.Quantidade(l.Quantidade, l.Produto.Unidade), Formatos.Moeda(l.Custo), Formatos.Moeda(l.Total));
            _grade.Rows[i].Tag = l;
            if (l.Produto.Id == selecionar)
            {
                _grade.ClearSelection();
                _grade.Rows[i].Selected = true;
                try { _grade.FirstDisplayedScrollingRowIndex = i; }
                catch (InvalidOperationException) { /* grade ainda sem tamanho */ }
            }
        }
        _totais.Text = _itens.Count == 0
            ? "Nenhum item ainda"
            : $"{_itens.Count} item(ns)   ·   Total: {Formatos.Moeda(_itens.Sum(l => l.Total))}";
        _finalizar.Enabled = _itens.Count > 0;
    }

    /// <summary>Tira a linha da lista e devolve para os campos de cima, para corrigir e adicionar de novo.</summary>
    private void EditarLinha(int indice)
    {
        if (_grade.Rows[indice].Tag is not Linha l) return;
        var item = _produto.Items.Cast<ItemProduto>().FirstOrDefault(i => i.Produto.Id == l.Produto.Id);
        if (item is null) return;
        _itens.Remove(l);
        AtualizarGrade();
        _produto.SelectedItem = item;
        _quantidade.Valor = l.Quantidade;
        _custo.Valor = l.Custo;
        _infoProduto.Text = "Corrija a quantidade ou o custo e clique em Adicionar.";
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
        _fornecedor.Clear();
        _documento.Clear();
        _obs.Clear();
        _produto.SelectedIndex = -1;
        _produto.Text = "";
        _quantidade.Valor = 0;
        _custo.Valor = 0;
        AtualizarGrade();
        _fornecedor.Focus();
    }

    private async Task Finalizar()
    {
        if (_itens.Count == 0)
        {
            Ui.Aviso(this, "Adicione pelo menos um produto.");
            return;
        }
        var total = _itens.Sum(l => l.Total);
        if (!Ui.Confirmar(this, $"Lançar a entrada com {_itens.Count} item(ns), total de {Formatos.Moeda(total)}?\n\n" +
                                "As quantidades serão somadas ao estoque" +
                                (_atualizarCusto.Checked ? " e o preço de custo dos produtos será atualizado." : ".")))
            return;

        _finalizar.Enabled = false;
        try
        {
            var r = await _api.PostAsync<EntradaDto>("api/estoque/entradas", new EntradaRequest
            {
                Fornecedor = _fornecedor.Text.Trim(),
                Documento = _documento.Text.Trim(),
                Observacao = _obs.Text.Trim(),
                AtualizarCusto = _atualizarCusto.Checked,
                Itens = _itens.Select(l => new EntradaItemRequest
                {
                    ProdutoId = l.Produto.Id, Quantidade = l.Quantidade, CustoUnitario = l.Custo
                }).ToList()
            });
            Ui.Info(this, $"Entrada nº {r.Numero} lançada: {r.QuantidadeItens} item(ns), {Formatos.Moeda(r.Total)}.");
            Limpar();
            await CarregarProdutos(); // estoque e custo já atualizados
        }
        catch (ApiException ex) { Ui.Erro(this, ex.Message); }
        finally
        {
            _finalizar.Enabled = _itens.Count > 0;
        }
    }

    // ---------- Entradas lançadas ----------

    private async Task CarregarLancadas()
    {
        try
        {
            var entradas = await _api.GetAsync<List<EntradaDto>>("api/estoque/entradas");
            _gradeLancadas.Rows.Clear();
            foreach (var e in entradas)
            {
                var i = _gradeLancadas.Rows.Add(e.Numero, e.Data, e.Fornecedor ?? "", e.Documento ?? "",
                    e.QuantidadeItens, Formatos.Moeda(e.Total), e.Usuario ?? "", e.Cancelada ? "Cancelada" : "Lançada");
                var linha = _gradeLancadas.Rows[i];
                linha.Tag = e;
                if (e.Cancelada) linha.DefaultCellStyle.ForeColor = Ui.Perigo;
            }
        }
        catch (ApiException ex) { Ui.Erro(this, ex.Message); }
    }

    private async Task VerItens()
    {
        if (_gradeLancadas.CurrentRow?.Tag is not EntradaDto escolhida)
        {
            Ui.Aviso(this, "Escolha uma entrada na lista.");
            return;
        }
        EntradaDto e;
        try { e = await _api.GetAsync<EntradaDto>($"api/estoque/entradas/{escolhida.Id}"); }
        catch (ApiException ex)
        {
            Ui.Erro(this, ex.Message);
            return;
        }

        using var f = new Form();
        Ui.Dialogo(f, $"Entrada nº {e.Numero}", 820, 520);

        var titulo = Ui.Rotulo($"Entrada nº {e.Numero}" + (e.Cancelada ? "  (CANCELADA)" : ""), 13, FontStyle.Bold);
        titulo.Location = new Point(20, 16);
        if (e.Cancelada) titulo.ForeColor = Ui.Perigo;

        var data = e.Data.Kind == DateTimeKind.Utc ? e.Data.ToLocalTime() : e.Data;
        var partes = new List<string> { data.ToString("dd/MM/yyyy HH:mm", Ui.Br) };
        if (e.Fornecedor is not null) partes.Add("Fornecedor: " + e.Fornecedor);
        if (e.Documento is not null) partes.Add("Nota: " + e.Documento);
        if (e.Usuario is not null) partes.Add("Por: " + e.Usuario);
        var detalhes = Ui.Rotulo(string.Join("   ·   ", partes), 9.5f);
        detalhes.ForeColor = Ui.Neutro;
        detalhes.Location = new Point(22, 48);

        var grade = Ui.Grade();
        grade.Location = new Point(20, 78);
        grade.Size = new Size(780, 340);
        grade.Columns.AddRange(
            Ui.ColunaTexto("Código", 12), Ui.ColunaTexto("Produto", 40), Ui.ColunaTexto("Un", 6),
            Ui.ColunaTexto("Quantidade", 12, true), Ui.ColunaTexto("Custo unit.", 12, true), Ui.ColunaTexto("Total", 12, true));
        foreach (var i in e.Itens)
            grade.Rows.Add(i.Codigo, i.Descricao, i.Unidade, Formatos.Quantidade(i.Quantidade, i.Unidade),
                Formatos.Moeda(i.CustoUnitario), Formatos.Moeda(i.Total));

        var total = Ui.Rotulo($"Total: {Formatos.Moeda(e.Total)}", 12, FontStyle.Bold);
        total.Location = new Point(20, 432);
        var fechar = Ui.Botao("Fechar", Ui.Neutro, largura: 110, altura: 38);
        fechar.Location = new Point(820 - 20 - 110, 466);
        fechar.DialogResult = DialogResult.OK;
        f.Controls.AddRange([titulo, detalhes, grade, total, fechar]);
        if (e.Observacao is not null)
        {
            var obs = Ui.Rotulo("Observação: " + e.Observacao, 9.5f);
            obs.ForeColor = Ui.Neutro;
            obs.MaximumSize = new Size(660, 0);
            obs.Location = new Point(20, 466);
            f.Controls.Add(obs);
        }
        f.AcceptButton = fechar;
        f.CancelButton = fechar;
        f.ShowDialog(this);
    }

    private async Task CancelarEntrada()
    {
        if (_gradeLancadas.CurrentRow?.Tag is not EntradaDto e)
        {
            Ui.Aviso(this, "Escolha uma entrada na lista.");
            return;
        }
        if (e.Cancelada)
        {
            Ui.Aviso(this, "Essa entrada já foi cancelada.");
            return;
        }
        if (!Ui.Confirmar(this, $"Cancelar a entrada nº {e.Numero}" + (e.Fornecedor is null ? "" : $" ({e.Fornecedor})") + "?\n\n" +
                                "As quantidades dela saem do estoque de novo. O preço de custo não volta ao valor anterior."))
            return;
        try
        {
            await _api.PostAsync<EntradaDto>($"api/estoque/entradas/{e.Id}/cancelar", new { });
            await CarregarLancadas();
            await CarregarProdutos();
        }
        catch (ApiException ex) { Ui.Erro(this, ex.Message); }
    }
}
