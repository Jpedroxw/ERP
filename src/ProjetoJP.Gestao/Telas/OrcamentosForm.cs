using ProjetoJP.Controles;
using ProjetoJP.Gestao.Servicos;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;

namespace ProjetoJP.Gestao.Telas;

/// <summary>Orçamentos feitos nos caixas (chegam na sincronização): consulta por período, situação e itens.</summary>
public sealed class OrcamentosForm : Form
{
    private readonly ApiClient _api;
    private readonly DateTimePicker _de = new() { Format = DateTimePickerFormat.Short, Width = 130, Font = Tema.Fonte(10.5f) };
    private readonly DateTimePicker _ate = new() { Format = DateTimePickerFormat.Short, Width = 130, Font = Tema.Fonte(10.5f) };
    private readonly ComboBox _situacao = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, Font = Tema.Fonte(10.5f) };
    private readonly DataGridView _grade = Ui.Grade();
    private readonly DataGridView _itens = Ui.Grade();
    private readonly Label _resumo = new() { AutoSize = true, Font = Tema.Fonte(9.5f), ForeColor = Tema.TextoSuave };
    private readonly System.Windows.Forms.Timer _automatico = new() { Interval = 60_000 };
    private List<OrcamentoDto> _lista = [];
    private bool _carregando;

    public OrcamentosForm(ApiClient api)
    {
        _api = api;
        Text = "Orçamentos";
        BackColor = Tema.Fundo;
        Font = Tema.Fonte(10);
        Padding = new Padding(24, 12, 24, 16);

        var cabecalho = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Tema.Fundo };
        cabecalho.Controls.Add(new Label { Text = "Orçamentos", Font = Tema.Semibold(19), ForeColor = Tema.Texto, AutoSize = true, Location = new Point(0, 0) });
        cabecalho.Controls.Add(new Label { Text = "Feitos nos caixas. Chegam aqui na sincronização; virar venda é feito no próprio caixa.", Font = Tema.Fonte(10), ForeColor = Tema.TextoSuave, AutoSize = true, Location = new Point(2, 36) });

        var filtros = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, 52), WrapContents = true, BackColor = Tema.Fundo, Padding = new Padding(0, 8, 0, 0) };
        _de.Value = DateTime.Today.AddDays(-30);
        _ate.Value = DateTime.Today;
        _situacao.Items.AddRange(["Todas", "Abertos", "Vendidos", "Cancelados"]);
        _situacao.SelectedIndex = 0;
        Label Rot(string t) => new() { Text = t, AutoSize = true, Font = Tema.Fonte(10), ForeColor = Tema.Texto, Margin = new Padding(0, 8, 6, 0) };
        var atualizar = Ui.Botao("Atualizar", Ui.Neutro, 120, 34, Icones.Atualizar);
        atualizar.Click += async (_, _) => await CarregarAsync();
        var exportar = Ui.Botao("Exportar", Ui.Neutro, 120, 34, Icones.Exportar);
        exportar.Click += (_, _) => Ui.ExportarCsv(this, _grade, "orcamentos");
        var folha = Ui.Botao("Imprimir A4", null, 140, 34, Icones.Imprimir);
        folha.Click += (_, _) => ImprimirA4();
        _de.Margin = _ate.Margin = new Padding(0, 4, 16, 0);
        _situacao.Margin = new Padding(0, 4, 16, 0);
        filtros.Controls.AddRange([Rot("De"), _de, Rot("até"), _ate, Rot("Situação"), _situacao, atualizar, exportar, folha]);
        _de.ValueChanged += async (_, _) => await CarregarAsync();
        _ate.ValueChanged += async (_, _) => await CarregarAsync();
        _situacao.SelectedIndexChanged += async (_, _) => await CarregarAsync();

        var divisao = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, BackColor = Tema.Fundo, SplitterWidth = 10 };
        var cartaoLista = new CartaoPainel { Dock = DockStyle.Fill, Padding = new Padding(1, 6, 1, 6) };
        _grade.Dock = DockStyle.Fill;
        _grade.Columns.AddRange(
            Ui.ColunaTexto("Nº", 7, true), Ui.ColunaTexto("Data", 14), Ui.ColunaTexto("Caixa", 12), Ui.ColunaTexto("Atendente", 14),
            Ui.ColunaTexto("Cliente", 22), Ui.ColunaTexto("Total", 12, true), Ui.ColunaTexto("Validade", 11), Ui.ColunaTexto("Situação", 10));
        _grade.SelectionChanged += (_, _) => MostrarItens();
        _grade.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) ImprimirA4(); };
        cartaoLista.Controls.Add(_grade);
        var cartaoItens = new CartaoPainel { Titulo = "Itens do orçamento", Dock = DockStyle.Fill, Padding = new Padding(1, 48, 1, 6) };
        _itens.Dock = DockStyle.Fill;
        _itens.Columns.AddRange(
            Ui.ColunaTexto("Código", 12), Ui.ColunaTexto("Produto", 44), Ui.ColunaTexto("Qtd", 10, true),
            Ui.ColunaTexto("Unitário", 14, true), Ui.ColunaTexto("Total", 14, true));
        cartaoItens.Controls.Add(_itens);
        divisao.Panel1.Controls.Add(cartaoLista);
        divisao.Panel2.Controls.Add(cartaoItens);

        var rodape = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = Tema.Fundo };
        _resumo.Location = new Point(2, 8);
        rodape.Controls.Add(_resumo);

        Controls.Add(divisao);
        Controls.Add(rodape);
        Controls.Add(filtros);
        Controls.Add(cabecalho);

        Shown += async (_, _) =>
        {
            try { divisao.SplitterDistance = Math.Max(120, (int)(divisao.Height * 0.58)); } catch (InvalidOperationException) { }
            await CarregarAsync();
            _automatico.Start();
        };
        _automatico.Tick += async (_, _) => { if (Visible) await CarregarAsync(); };
        FormClosed += (_, _) => _automatico.Dispose();
    }

    private static string Situacao(StatusOrcamento s) => s switch
    {
        StatusOrcamento.Aberto => "Aberto",
        StatusOrcamento.Convertido => "Vendido",
        _ => "Cancelado"
    };

    private async Task CarregarAsync()
    {
        if (_carregando || IsDisposed) return;
        _carregando = true;
        try
        {
            var de = DateOnly.FromDateTime(_de.Value.Date);
            var ate = DateOnly.FromDateTime(_ate.Value.Date);
            if (ate < de) (de, ate) = (ate, de);
            var status = _situacao.SelectedIndex switch { 1 => "&status=Aberto", 2 => "&status=Convertido", 3 => "&status=Cancelado", _ => "" };
            var lista = await _api.GetAsync<List<OrcamentoDto>>($"api/orcamentos?de={de:yyyy-MM-dd}&ate={ate:yyyy-MM-dd}{status}");
            if (IsDisposed) return;
            var selecionado = (_grade.CurrentRow?.Tag as OrcamentoDto)?.Id;
            _lista = lista;
            _grade.Rows.Clear();
            foreach (var o in lista)
            {
                var vencido = o.Status == StatusOrcamento.Aberto && o.Validade is { } v && v < DateOnly.FromDateTime(DateTime.Today);
                var i = _grade.Rows.Add(o.Numero, o.Data.ToString("dd/MM/yyyy HH:mm"), o.Terminal, o.Usuario,
                    o.ClienteNome ?? "Consumidor final", Formatos.Moeda(o.Total),
                    o.Validade?.ToString("dd/MM/yyyy") ?? "—", vencido ? "Vencido" : Situacao(o.Status));
                var linha = _grade.Rows[i];
                linha.Tag = o;
                if (o.Status == StatusOrcamento.Cancelado || vencido) linha.DefaultCellStyle.ForeColor = Tema.TextoSuave;
                else if (o.Status == StatusOrcamento.Convertido) linha.DefaultCellStyle.ForeColor = Tema.Sucesso;
                if (o.Id == selecionado) _grade.CurrentCell = linha.Cells[0];
            }
            var abertos = lista.Where(o => o.Status == StatusOrcamento.Aberto).ToList();
            var vendidos = lista.Count(o => o.Status == StatusOrcamento.Convertido);
            _resumo.Text = $"{lista.Count} orçamento(s)  ·  {abertos.Count} aberto(s) somando {Formatos.Moeda(abertos.Sum(o => o.Total))}" +
                           $"  ·  {vendidos} viraram venda  ·  atualizado às {DateTime.Now:HH:mm}";
            MostrarItens();
        }
        catch (ApiException ex)
        {
            if (!IsDisposed) _resumo.Text = ex.Message;
        }
        finally
        {
            _carregando = false;
        }
    }

    /// <summary>Orçamento selecionado em folha A4: mostra antes e deixa escolher a impressora (ou salvar em PDF).</summary>
    private void ImprimirA4()
    {
        if (_grade.CurrentRow?.Tag is not OrcamentoDto o)
        {
            Ui.Aviso(this, "Escolha um orçamento na lista.");
            return;
        }
        var doc = new OrcamentoImpresso
        {
            // Nome da empresa da Aparência; o endereço do servidor nunca vai para o papel.
            Loja = Aparencia.NomePersonalizado ? Aparencia.NomeSistema : "",
            Numero = o.Numero,
            Data = o.Data,
            Caixa = o.Terminal,
            Atendente = o.Usuario,
            Cliente = o.ClienteNome,
            Validade = o.Validade,
            Observacao = o.Observacao,
            Subtotal = o.Subtotal,
            Desconto = o.Desconto,
            Total = o.Total,
            Itens = o.Itens.Select(i => new OrcamentoImpresso.Item(i.Codigo, i.Descricao, i.Unidade, i.Quantidade, i.PrecoUnitario, i.Total)).ToList()
        };
        using var impressao = ImpressaoA4.Orcamento(doc, null);
        VisualizarImpressaoForm.Abrir(this, impressao, $"Orçamento nº {o.Numero} — folha A4");
    }

    private void MostrarItens()
    {
        _itens.Rows.Clear();
        if (_grade.CurrentRow?.Tag is not OrcamentoDto o) return;
        foreach (var i in o.Itens)
            _itens.Rows.Add(i.Codigo, i.Descricao, Formatos.Quantidade(i.Quantidade, i.Unidade), Formatos.Moeda(i.PrecoUnitario), Formatos.Moeda(i.Total));
    }
}
