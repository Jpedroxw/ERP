using ProjetoJP.Controles;
using ProjetoJP.Gestao.Servicos;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;

namespace ProjetoJP.Gestao.Telas;

/// <summary>
/// Promoções com preço especial por período. Os caixas recebem antes e ligam/desligam sozinhos na data,
/// mesmo sem internet (usando a hora confiável do caixa, não só o relógio do Windows).
/// </summary>
public sealed class PromocoesForm : Form
{
    private readonly ApiClient _api;
    private readonly DataGridView _grade = Ui.Grade();
    private readonly Label _resumo = Ui.Rotulo("Carregando...");
    private readonly CheckBox _todas = new() { Text = "Mostrar encerradas e canceladas", AutoSize = true, Font = Tema.Fonte(10) };
    private List<PromocaoDto> _lista = [];

    public PromocoesForm(ApiClient api)
    {
        _api = api;
        Text = "Promoções";
        Icon = Ui.IconeApp ?? Icon;
        BackColor = Tema.Fundo;
        Font = Tema.Fonte(10);
        Size = new Size(1100, 690);
        Padding = new Padding(16, 12, 16, 12);

        var cabecalho = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, BackColor = Tema.Superficie, Padding = new Padding(16, 12, 16, 10)
        };
        cabecalho.Controls.Add(Ui.Rotulo("Promoções", 16, FontStyle.Bold));
        var explicacao = Ui.Rotulo("Preço especial com data de início e de término. De varejo (qualquer quantidade) ou de " +
                                   "atacado (a partir de uma quantidade no mesmo cupom). Os caixas recebem antes e ligam/desligam " +
                                   "a promoção sozinhos no dia certo, mesmo sem internet.", 10);
        explicacao.ForeColor = Tema.TextoSuave;
        cabecalho.Controls.Add(explicacao);
        cabecalho.SizeChanged += (_, _) => explicacao.MaximumSize = new Size(Math.Max(200, cabecalho.ClientSize.Width - 40), 0);

        var botoes = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true,
            BackColor = Tema.Superficie, Padding = new Padding(16, 4, 16, 10)
        };
        var nova = Ui.Botao("Nova promoção", largura: 170, altura: 36, icone: Icones.Novo);
        var cancelar = Ui.Botao("Cancelar promoção", Ui.Perigo, largura: 190, altura: 36, icone: Icones.Cancelar);
        var atualizar = Ui.Botao("Atualizar", Ui.Neutro, 120, 36, Icones.Atualizar);
        nova.Click += async (_, _) => await NovaAsync();
        cancelar.Click += async (_, _) => await CancelarAsync();
        atualizar.Click += async (_, _) => await CarregarAsync();
        _todas.Margin = new Padding(12, 9, 0, 0);
        _todas.CheckedChanged += (_, _) => Mostrar();
        botoes.Controls.AddRange([nova, cancelar, atualizar, _todas]);

        _grade.Dock = DockStyle.Fill;
        _grade.Columns.AddRange(
            Ui.ColunaTexto("Produto", 26), Ui.ColunaTexto("Promoção", 15), Ui.ColunaTexto("Tipo", 15), Ui.ColunaTexto("Início", 9),
            Ui.ColunaTexto("Término", 10), Ui.ColunaTexto("Preço normal", 11, true), Ui.ColunaTexto("Preço promocional", 12, true),
            Ui.ColunaTexto("Desconto", 8, true), Ui.ColunaTexto("Situação", 10));
        _grade.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0) await CancelarAsync(); };

        var rodape = new Panel { Dock = DockStyle.Bottom, Height = 34, BackColor = Tema.Fundo, Padding = new Padding(2, 8, 0, 0) };
        _resumo.Dock = DockStyle.Fill;
        _resumo.ForeColor = Tema.TextoSuave;
        rodape.Controls.Add(_resumo);

        Controls.Add(_grade);
        Controls.Add(rodape);
        Controls.Add(botoes);
        Controls.Add(cabecalho);
        Shown += async (_, _) => await CarregarAsync();
    }

    private async Task CarregarAsync()
    {
        try
        {
            UseWaitCursor = true;
            _lista = await _api.GetAsync<List<PromocaoDto>>("api/promocoes");
            Mostrar();
        }
        catch (ApiException ex) { Ui.Erro(this, ex.Message); }
        finally { UseWaitCursor = false; }
    }

    private void Mostrar()
    {
        _grade.Rows.Clear();
        foreach (var p in _lista.Where(p => _todas.Checked || p.Situacao is "Em vigor" or "Agendada"))
        {
            var desconto = p.PrecoNormal > 0 ? $"{(1 - p.PrecoPromocional / p.PrecoNormal) * 100:0}%" : "";
            var i = _grade.Rows.Add(p.Produto, p.Nome, p.Tipo, p.Inicio, p.Fim,
                p.PrecoNormal > 0 ? Formatos.Moeda(p.PrecoNormal) : "—", Formatos.Moeda(p.PrecoPromocional), desconto, p.Situacao);
            var linha = _grade.Rows[i];
            linha.Tag = p;
            linha.DefaultCellStyle.ForeColor = p.Situacao switch
            {
                "Em vigor" => Tema.Sucesso,
                "Cancelada" or "Encerrada" => Tema.TextoSuave,
                _ => Tema.Texto
            };
        }
        _resumo.Text = $"{_lista.Count(x => x.Situacao == "Em vigor")} em vigor  ·  {_lista.Count(x => x.Situacao == "Agendada")} agendada(s)" +
                       $"  ·  {_lista.Count} no total";
    }

    private async Task NovaAsync()
    {
        List<CargaProdutoDto> produtos;
        try
        {
            produtos = (await _api.GetAsync<List<CargaProdutoDto>>("api/cargas/produtos"))
                .Where(x => x.Publicado && x.AtivoPublicado).OrderBy(x => x.DescricaoPublicada).ToList();
        }
        catch (ApiException ex)
        {
            Ui.Erro(this, ex.Message);
            return;
        }
        if (produtos.Count == 0)
        {
            Ui.Aviso(this, "Nenhum produto publicado ainda. Envie os produtos em Caixas → Enviar produtos aos caixas antes de criar promoções.");
            return;
        }

        using var f = new NovaPromocaoForm(_api, produtos);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        await CarregarAsync();
        Ui.Info(this, "Promoção salva. Os caixas recebem na próxima sincronização.");
    }

    private async Task CancelarAsync()
    {
        if (_grade.CurrentRow?.Tag is not PromocaoDto p)
        {
            Ui.Aviso(this, "Escolha uma promoção na lista.");
            return;
        }
        if (!p.Ativa)
        {
            Ui.Aviso(this, "Essa promoção já foi cancelada.");
            return;
        }
        if (!Ui.Confirmar(this, $"Cancelar a promoção \"{p.Nome}\" de {p.Produto}?\n\n" +
                                "Os caixas voltam ao preço normal na próxima sincronização. Para mudar o preço ou as datas, " +
                                "cancele e crie outra."))
            return;
        try
        {
            await _api.DeleteAsync($"api/promocoes/{p.Id}");
            await CarregarAsync();
        }
        catch (ApiException ex) { Ui.Erro(this, ex.Message); }
    }
}

/// <summary>
/// Janela "Nova promoção": produto com busca, preço em R$ como no resto do sistema, mostra o preço normal e o
/// desconto. Grava pela API antes de fechar: se o servidor recusar, a janela continua aberta com o que foi digitado.
/// Montada com painéis de layout (sem posições fixas), então não corta em escala 125%/150%.
/// </summary>
internal sealed class NovaPromocaoForm : Form
{
    private sealed class Item(CargaProdutoDto p)
    {
        public CargaProdutoDto Produto { get; } = p;
        public override string ToString() => $"{Produto.Codigo} - {Produto.DescricaoPublicada}";
    }

    private readonly ApiClient _api;
    private readonly ComboBox _produto = new()
    {
        DropDownStyle = ComboBoxStyle.DropDown, AutoCompleteMode = AutoCompleteMode.SuggestAppend,
        AutoCompleteSource = AutoCompleteSource.ListItems, Font = Tema.Fonte(10.5f), Anchor = AnchorStyles.Left | AnchorStyles.Right
    };
    private readonly Label _precoNormal = new() { AutoSize = true, ForeColor = Tema.TextoSuave, Font = Tema.Fonte(9.5f) };
    private readonly TextBox _nome = new() { MaxLength = 100, Text = "Promoção", Font = Tema.Fonte(10.5f), Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly DateTimePicker _inicio = new() { Format = DateTimePickerFormat.Short, Value = DateTime.Today, Font = Tema.Fonte(10.5f), Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly DateTimePicker _fim = new() { Format = DateTimePickerFormat.Short, Value = DateTime.Today.AddDays(7), Font = Tema.Fonte(10.5f), Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly CampoMoeda _preco = new() { Font = Tema.Fonte(12), Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly RadioButton _varejo = new() { Text = "Varejo (qualquer quantidade)", Checked = true, AutoSize = true, Font = Tema.Fonte(10) };
    private readonly RadioButton _atacado = new() { Text = "Atacado (a partir de uma quantidade)", AutoSize = true, Font = Tema.Fonte(10) };
    private readonly CampoNumero _minimo = new() { CasasDecimais = 0, Font = Tema.Fonte(12), Anchor = AnchorStyles.Left | AnchorStyles.Right, Enabled = false };
    private readonly Label _atacadoFixo = new() { AutoSize = true, ForeColor = Tema.TextoSuave, Font = Tema.Fonte(9), Anchor = AnchorStyles.Left };
    private readonly Label _desconto = new() { AutoSize = true, ForeColor = Tema.Sucesso, Font = Tema.Semibold(10), Anchor = AnchorStyles.Left };
    private readonly TableLayoutPanel _tabela;
    private readonly FlowLayoutPanel _rodape;
    private readonly BotaoTema _salvar;

    public NovaPromocaoForm(ApiClient api, List<CargaProdutoDto> produtos)
    {
        _api = api;
        Ui.Dialogo(this, "Nova promoção", 560, 440);
        FormBorderStyle = FormBorderStyle.Sizable;
        AutoScaleMode = AutoScaleMode.None;

        foreach (var p in produtos) _produto.Items.Add(new Item(p));

        _tabela = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2,
            Padding = new Padding(24, 18, 24, 6), BackColor = Tema.Superficie
        };
        _tabela.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _tabela.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        var linha = 0;
        Label Rot(string texto) => new()
        {
            Text = texto, AutoSize = true, ForeColor = Tema.TextoSuave, Font = Tema.Fonte(9.5f), Margin = new Padding(0, 6, 8, 2)
        };
        void Inteira(Control c)
        {
            _tabela.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _tabela.RowCount = linha + 1;
            _tabela.Controls.Add(c, 0, linha);
            _tabela.SetColumnSpan(c, 2);
            linha++;
        }
        void Duas(Control a, Control b)
        {
            _tabela.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _tabela.RowCount = linha + 1;
            _tabela.Controls.Add(a, 0, linha);
            _tabela.Controls.Add(b, 1, linha);
            linha++;
        }

        Inteira(Rot("Produto (digite o código ou parte do nome)"));
        _produto.Margin = new Padding(0, 0, 0, 2);
        Inteira(_produto);
        _precoNormal.Margin = new Padding(0, 0, 0, 8);
        Inteira(_precoNormal);
        Inteira(Rot("Nome da promoção (aparece só aqui na Gestão)"));
        _nome.Margin = new Padding(0, 0, 0, 8);
        Inteira(_nome);
        Duas(Rot("Início"), Rot("Término (último dia, inclusive)"));
        _inicio.Margin = new Padding(0, 0, 8, 8);
        _fim.Margin = new Padding(8, 0, 0, 8);
        Duas(_inicio, _fim);
        Duas(_varejo, _atacado);
        _varejo.Margin = new Padding(0, 4, 8, 6);
        _atacado.Margin = new Padding(8, 4, 0, 6);
        Duas(Rot("Vale a partir de (quantidade no mesmo cupom)"), new Label());
        _minimo.Margin = new Padding(0, 0, 8, 8);
        _atacadoFixo.Margin = new Padding(8, 8, 0, 8);
        Duas(_minimo, _atacadoFixo);
        Inteira(Rot("Preço promocional (R$ por unidade)"));
        _preco.Margin = new Padding(0, 0, 8, 8);
        _desconto.Margin = new Padding(8, 8, 0, 8);
        Duas(_preco, _desconto);
        var dica = new Label
        {
            Text = "O caixa liga a promoção no primeiro dia e desliga depois do último, mesmo sem internet. " +
                   "Para mudar preço ou datas depois, cancele e crie outra.",
            AutoSize = true, ForeColor = Tema.TextoSuave, Font = Tema.Fonte(9), Margin = new Padding(0, 4, 0, 4)
        };
        Inteira(dica);
        _tabela.SizeChanged += (_, _) => dica.MaximumSize = new Size(Math.Max(150, _tabela.ClientSize.Width - _tabela.Padding.Horizontal), 0);

        _rodape = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, BackColor = Tema.Fundo, Padding = new Padding(20, 12, 20, 12)
        };
        _salvar = Ui.Botao("Salvar promoção", largura: 180, altura: 38, icone: Icones.Ok);
        _salvar.Margin = new Padding(10, 0, 0, 0);
        var cancelar = Ui.Botao("Cancelar", Ui.Neutro, 110, 38);
        cancelar.Margin = Padding.Empty;
        cancelar.DialogResult = DialogResult.Cancel;
        _salvar.Click += async (_, _) => await SalvarAsync();
        _rodape.Controls.Add(_salvar);
        _rodape.Controls.Add(cancelar);

        Controls.Add(_tabela);
        Controls.Add(_rodape);
        AcceptButton = _salvar;
        CancelButton = cancelar;

        _produto.SelectedIndexChanged += (_, _) => Atualizar();
        _atacado.CheckedChanged += (_, _) =>
        {
            _minimo.Enabled = _atacado.Checked;
            if (_atacado.Checked && _minimo.Valor <= 0) _minimo.Valor = 2;
            if (_nome.Text is "Promoção" or "Atacado") _nome.Text = _atacado.Checked ? "Atacado" : "Promoção";
            Atualizar();
        };
        _minimo.TextChanged += (_, _) => Atualizar();
        _produto.Leave += (_, _) => { Escolher(); Atualizar(); };
        _preco.TextChanged += (_, _) => Atualizar();
        Atualizar();
        Shown += (_, _) => _produto.Focus();
    }

    protected override void OnLoad(EventArgs e)
    {
        // Tamanho pelo conteúdo, na escala do Windows (as letras já crescem; a largura acompanha).
        var escala = DeviceDpi / 96f;
        var area = Screen.FromControl(Owner ?? this).WorkingArea;
        ClientSize = new Size(Math.Min((int)(560 * escala), area.Width - 40), ClientSize.Height);
        PerformLayout();
        var altura = _tabela.Height + _rodape.Height;
        ClientSize = new Size(ClientSize.Width, Math.Min(altura, area.Height - 60));
        MinimumSize = new Size(Math.Min(Width, (int)(420 * escala)), Math.Min(Height, (int)(380 * escala)));
        base.OnLoad(e);
    }

    private CargaProdutoDto? Produto => (_produto.SelectedItem as Item)?.Produto;

    /// <summary>Acha o produto pelo texto digitado (código ou parte do nome).</summary>
    private void Escolher()
    {
        if (_produto.SelectedItem is Item sel && _produto.Text == sel.ToString()) return;
        var texto = _produto.Text.Trim();
        if (texto.Length == 0) return;
        var itens = _produto.Items.Cast<Item>().ToList();
        var achado = itens.FirstOrDefault(i => string.Equals(i.Produto.Codigo, texto, StringComparison.OrdinalIgnoreCase)) ??
                     itens.FirstOrDefault(i => i.ToString().Equals(texto, StringComparison.OrdinalIgnoreCase)) ??
                     itens.FirstOrDefault(i => i.Produto.DescricaoPublicada.Contains(texto, StringComparison.OrdinalIgnoreCase));
        if (achado is not null) _produto.SelectedItem = achado;
    }

    private void Atualizar()
    {
        var p = Produto;
        _precoNormal.Text = p is null ? "Escolha um produto publicado." : $"Preço normal no caixa: {Formatos.Moeda(p.PrecoPublicado)} por {p.UnidadePublicada}";
        if (p is not null && _minimo.CasasDecimais != Formatos.CasasQuantidade(p.UnidadePublicada))
            _minimo.CasasDecimais = Formatos.CasasQuantidade(p.UnidadePublicada);
        _atacadoFixo.Text = p is not null && p.PrecoAtacadoPublicado > 0
            ? $"Atacado fixo do produto: {Formatos.Moeda(p.PrecoAtacadoPublicado)} a partir de {Formatos.Quantidade(p.QtdAtacadoPublicado, p.UnidadePublicada)}"
            : "";
        if (p is null || _preco.Valor <= 0)
        {
            _desconto.Text = "";
            return;
        }
        if (_preco.Valor >= p.PrecoPublicado)
        {
            _desconto.Text = "precisa ser menor que o preço normal";
            _desconto.ForeColor = Tema.Perigo;
            return;
        }
        _desconto.Text = $"{(1 - _preco.Valor / p.PrecoPublicado) * 100:0}% de desconto";
        _desconto.ForeColor = Tema.Sucesso;
    }

    private async Task SalvarAsync()
    {
        Escolher();
        var p = Produto;
        if (p is null) { Aviso("Escolha o produto na lista.", _produto); return; }
        if (_preco.Valor <= 0) { Aviso("Informe o preço promocional.", _preco); return; }
        if (_preco.Valor >= p.PrecoPublicado) { Aviso($"O preço promocional precisa ser menor que o normal ({Formatos.Moeda(p.PrecoPublicado)}).", _preco); return; }
        if (_fim.Value.Date < _inicio.Value.Date) { Aviso("O término não pode ser antes do início.", _fim); return; }
        if (_atacado.Checked && Atacado.ValidarQuantidade(p.UnidadePublicada, _minimo.Valor) is { } erroQtd) { Aviso(erroQtd, _minimo); return; }

        _salvar.Enabled = false;
        UseWaitCursor = true;
        try
        {
            await _api.PostAsync<PromocaoDto>("api/promocoes", new PromocaoCadastroRequest
            {
                ProdutoId = p.Id,
                Nome = _nome.Text.Trim(),
                Inicio = DateOnly.FromDateTime(_inicio.Value.Date),
                Fim = DateOnly.FromDateTime(_fim.Value.Date),
                PrecoPromocional = _preco.Valor,
                QuantidadeMinima = _atacado.Checked ? _minimo.Valor : 0
            });
            DialogResult = DialogResult.OK; // só fecha depois que o servidor aceitou
        }
        catch (ApiException ex) { Ui.Aviso(this, ex.Message); }
        finally
        {
            UseWaitCursor = false;
            if (!IsDisposed) _salvar.Enabled = true;
        }
    }

    private void Aviso(string msg, Control campo)
    {
        Ui.Aviso(this, msg);
        campo.Focus();
    }
}
