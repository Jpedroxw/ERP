using ProjetoJP.Controles;
using ProjetoJP.Gestao.Servicos;
using ProjetoJP.Shared;

namespace ProjetoJP.Gestao.Telas;

/// <summary>
/// Devolução de itens de uma venda (sem cancelar o cupom inteiro) e recebimento do fiado.
/// O número da venda se repete entre caixas (cada caixa conta do 1), por isso a busca mostra
/// caixa, data, cliente e total, e o usuário escolhe a venda e o item certos.
/// </summary>
public sealed class FiadoDevolucaoForm : Form
{
    private readonly ApiClient _api;

    // Devolução
    private readonly CampoNumero _numero = new() { CasasDecimais = 0, Maximo = 999_999_999, Width = 110, Font = Tema.Fonte(10.5f) };
    private readonly DateTimePicker _data = new() { Format = DateTimePickerFormat.Short, Width = 140, Font = Tema.Fonte(10.5f), ShowCheckBox = true };
    private readonly DataGridView _vendas = Ui.Grade();
    private readonly DataGridView _itens = Ui.Grade();
    private readonly Label _valorDevolucao = new() { AutoSize = true, Font = Tema.Semibold(11), ForeColor = Tema.Texto, Margin = new Padding(0, 9, 16, 0) };
    private readonly BotaoTema _devolver = Ui.Botao("Devolver", Ui.Sucesso, 150, 36, Icones.Ok);
    private List<VendaApi> _listaVendas = [];

    // Fiado
    private readonly TextBox _buscaCliente = new() { Width = 260, Font = Tema.Fonte(10.5f), PlaceholderText = "Nome, CPF/CNPJ ou telefone" };
    private readonly CheckBox _soDevendo = new() { Text = "Só quem deve", AutoSize = true, Checked = true, Font = Tema.Fonte(10), Margin = new Padding(12, 8, 0, 0) };
    private readonly DataGridView _clientes = Ui.Grade();
    private readonly DataGridView _lancamentos = Ui.Grade();
    private readonly Label _saldo = new() { AutoSize = true, Font = Tema.Semibold(16), ForeColor = Tema.Texto };
    private readonly Label _clienteNome = new() { AutoSize = true, Font = Tema.Fonte(10), ForeColor = Tema.TextoSuave };
    private readonly CampoMoeda _valor = new() { Width = 140, Font = Tema.Fonte(12) };
    private readonly BotaoTema _receber = Ui.Botao("Receber", Ui.Sucesso, 130, 36, Icones.Pagamento);
    private readonly BotaoTema _lancar = Ui.Botao("Lançar dívida", Ui.Neutro, 150, 36, Icones.Novo);
    private List<ClienteApi> _listaClientes = [];

    private readonly Panel _painelDevolucao = new() { Dock = DockStyle.Fill, BackColor = Tema.Fundo };
    private readonly Panel _painelFiado = new() { Dock = DockStyle.Fill, BackColor = Tema.Fundo, Visible = false };
    private readonly BotaoTema _abaDevolucao = Ui.Botao("Devolver item", null, 170, 36, Icones.Voltar);
    private readonly BotaoTema _abaFiado = Ui.Botao("Receber fiado", Ui.Neutro, 170, 36, Icones.Clientes);
    private bool _fiadoCarregado;

    public FiadoDevolucaoForm(ApiClient api)
    {
        _api = api;
        Text = "Fiado e devolução";
        Icon = Ui.IconeApp ?? Icon;
        BackColor = Tema.Fundo;
        Font = Tema.Fonte(10);
        Size = new Size(1040, 700);
        MinimumSize = new Size(820, 560);
        Padding = new Padding(24, 12, 24, 16);

        var cabecalho = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Tema.Fundo };
        cabecalho.Controls.Add(new Label { Text = "Fiado e devolução", Font = Tema.Semibold(19), ForeColor = Tema.Texto, AutoSize = true, Location = new Point(0, 0) });
        cabecalho.Controls.Add(new Label
        {
            Text = "Devolva itens de uma venda sem cancelar o cupom, ou receba o que o cliente deve no fiado.",
            Font = Tema.Fonte(10), ForeColor = Tema.TextoSuave, AutoSize = true, Location = new Point(2, 36)
        });

        var abas = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 50, WrapContents = false, BackColor = Tema.Fundo, Padding = new Padding(0, 6, 0, 0) };
        abas.Controls.AddRange([_abaDevolucao, _abaFiado]);
        _abaDevolucao.Click += (_, _) => MostrarAba(fiado: false);
        _abaFiado.Click += async (_, _) =>
        {
            MostrarAba(fiado: true);
            if (!_fiadoCarregado) await CarregarClientesAsync();
        };

        MontarDevolucao();
        MontarFiado();

        Controls.Add(_painelDevolucao);
        Controls.Add(_painelFiado);
        Controls.Add(abas);
        Controls.Add(cabecalho);

        Shown += async (_, _) => await BuscarVendasAsync();
    }

    private void MostrarAba(bool fiado)
    {
        _painelFiado.Visible = fiado;
        _painelDevolucao.Visible = !fiado;
        _abaFiado.Estilo = fiado ? EstiloBotao.Primario : EstiloBotao.Secundario;
        _abaDevolucao.Estilo = fiado ? EstiloBotao.Secundario : EstiloBotao.Primario;
        _abaFiado.Invalidate();
        _abaDevolucao.Invalidate();
    }

    private static Label Rot(string t) => new() { Text = t, AutoSize = true, Font = Tema.Fonte(10), ForeColor = Tema.Texto, Margin = new Padding(0, 8, 6, 0) };

    // ------------------------------------------------------------------ Devolução

    private void MontarDevolucao()
    {
        var filtros = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, 50), WrapContents = true, BackColor = Tema.Fundo, Padding = new Padding(0, 6, 0, 0) };
        _data.Value = DateTime.Today;
        _data.Checked = false;
        _numero.Margin = _data.Margin = new Padding(0, 4, 16, 0);
        var buscar = Ui.Botao("Buscar", null, 120, 34, Icones.Buscar);
        buscar.Click += async (_, _) => await BuscarVendasAsync();
        _numero.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            await BuscarVendasAsync();
        };
        var dica = new Label
        {
            Text = "Sem número: mostra as últimas vendas.", AutoSize = true, Font = Tema.Fonte(9), ForeColor = Tema.TextoSuave,
            Margin = new Padding(8, 10, 0, 0)
        };
        filtros.Controls.AddRange([Rot("Nº da venda"), _numero, Rot("Dia"), _data, buscar, dica]);

        var divisao = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, BackColor = Tema.Fundo, SplitterWidth = 10 };
        var cartaoVendas = new CartaoPainel { Titulo = "1. Escolha a venda", Dock = DockStyle.Fill, Padding = new Padding(1, 48, 1, 6) };
        _vendas.Dock = DockStyle.Fill;
        _vendas.Columns.AddRange(
            Ui.ColunaTexto("Caixa", 14), Ui.ColunaTexto("Nº", 7, true), Ui.ColunaTexto("Data", 14), Ui.ColunaTexto("Cliente", 26),
            Ui.ColunaTexto("Total", 12, true), Ui.ColunaTexto("Já devolvido", 12, true), Ui.ColunaTexto("Fiado", 7));
        _vendas.SelectionChanged += (_, _) => MostrarItens();
        cartaoVendas.Controls.Add(_vendas);

        var cartaoItens = new CartaoPainel { Titulo = "2. Digite quanto devolver de cada item", Dock = DockStyle.Fill, Padding = new Padding(1, 48, 1, 6) };
        _itens.Dock = DockStyle.Fill;
        _itens.ReadOnly = false;
        _itens.EditMode = DataGridViewEditMode.EditOnEnter;
        _itens.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _itens.Columns.AddRange(
            Ui.ColunaTexto("Produto", 40), Ui.ColunaTexto("Un", 6), Ui.ColunaTexto("Vendido", 10, true),
            Ui.ColunaTexto("Já devolvido", 11, true), Ui.ColunaTexto("Pode devolver", 11, true), Ui.ColunaTexto("Valor un.", 11, true),
            Ui.ColunaTexto("Devolver", 11, true));
        foreach (DataGridViewColumn c in _itens.Columns) c.ReadOnly = true;
        var colDevolver = _itens.Columns[ColDevolver];
        colDevolver.ReadOnly = false;
        colDevolver.DefaultCellStyle.BackColor = Tema.Superficie;
        colDevolver.DefaultCellStyle.Font = Tema.Semibold(10.5f);
        colDevolver.HeaderText = "Devolver ✎";
        _itens.CellEndEdit += (_, e) => { ConferirQuantidade(e.RowIndex, e.ColumnIndex); AtualizarValor(); };
        _itens.DataError += (_, e) => e.ThrowException = false;
        cartaoItens.Controls.Add(_itens);

        divisao.Panel1.Controls.Add(cartaoVendas);
        divisao.Panel2.Controls.Add(cartaoItens);

        var rodape = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, 52), WrapContents = true, BackColor = Tema.Fundo, Padding = new Padding(0, 10, 0, 0), FlowDirection = FlowDirection.LeftToRight };
        _devolver.Click += async (_, _) => await DevolverAsync();
        var tudo = Ui.Botao("Devolver tudo desta venda", Ui.Neutro, 220, 36, Icones.Voltar);
        tudo.Click += (_, _) =>
        {
            foreach (DataGridViewRow r in _itens.Rows)
                if (r.Tag is ItemApi i) r.Cells[ColDevolver].Value = Formatos.Quantidade(i.Restante, i.Unidade);
            AtualizarValor();
        };
        rodape.Controls.AddRange([_valorDevolucao, _devolver, tudo]);
        AtualizarValor();

        _painelDevolucao.Controls.Add(divisao);
        _painelDevolucao.Controls.Add(rodape);
        _painelDevolucao.Controls.Add(filtros);
        _painelDevolucao.VisibleChanged += (_, _) =>
        {
            if (!_painelDevolucao.Visible) return;
            try { divisao.SplitterDistance = Math.Max(120, (int)(divisao.Height * 0.48)); } catch (InvalidOperationException) { }
        };
        Shown += (_, _) =>
        {
            try { divisao.SplitterDistance = Math.Max(120, (int)(divisao.Height * 0.48)); } catch (InvalidOperationException) { }
        };
    }

    private const int ColDevolver = 6;

    private async Task BuscarVendasAsync()
    {
        try
        {
            UseWaitCursor = true;
            var filtros = new List<string>();
            if (_numero.Valor > 0) filtros.Add($"numero={(int)_numero.Valor}");
            if (_data.Checked) filtros.Add($"data={_data.Value:yyyy-MM-dd}");
            var url = "api/vendas" + (filtros.Count > 0 ? "?" + string.Join("&", filtros) : "");
            _listaVendas = await _api.GetAsync<List<VendaApi>>(url);
            if (IsDisposed) return;
            _vendas.Rows.Clear();
            foreach (var v in _listaVendas)
            {
                var i = _vendas.Rows.Add(v.Terminal, v.Numero, v.Data.ToString("dd/MM/yyyy HH:mm", Ui.Br), v.Cliente ?? "Consumidor final",
                    Formatos.Moeda(v.Total), v.ValorDevolvido > 0 ? Formatos.Moeda(v.ValorDevolvido) : "—", v.TemFiado ? "Sim" : "");
                _vendas.Rows[i].Tag = v;
                if (v.Itens.All(x => x.Restante <= 0)) _vendas.Rows[i].DefaultCellStyle.ForeColor = Tema.TextoSuave;
            }
            if (_listaVendas.Count == 0)
            {
                _itens.Rows.Clear();
                AtualizarValor();
                if (_numero.Valor > 0 || _data.Checked)
                    Ui.Aviso(this, "Nenhuma venda concluída com esse número/dia.\n\nA venda só aparece aqui depois que o caixa sincroniza.");
            }
            else if (_listaVendas.Count > 1 && _numero.Valor > 0)
            {
                // Mesmo número em caixas diferentes: deixa o usuário escolher.
                _vendas.ClearSelection();
                _itens.Rows.Clear();
                AtualizarValor();
            }
        }
        catch (ApiException ex) { if (!IsDisposed) Ui.Erro(this, ex.Message); }
        finally { if (!IsDisposed) UseWaitCursor = false; }
    }

    private VendaApi? VendaSelecionada() =>
        _vendas.SelectedRows.Count == 1 ? _vendas.SelectedRows[0].Tag as VendaApi : null;

    private void MostrarItens()
    {
        _itens.Rows.Clear();
        var v = VendaSelecionada();
        if (v is not null)
        {
            foreach (var item in v.Itens)
            {
                var i = _itens.Rows.Add(item.Descricao, item.Unidade, Formatos.Quantidade(item.Quantidade, item.Unidade),
                    Formatos.Quantidade(item.JaDevolvida, item.Unidade), Formatos.Quantidade(item.Restante, item.Unidade),
                    Formatos.Moeda(item.Quantidade > 0 ? item.Total / item.Quantidade : 0), "");
                var linha = _itens.Rows[i];
                linha.Tag = item;
                if (item.Restante <= 0)
                {
                    linha.Cells[ColDevolver].ReadOnly = true;
                    linha.DefaultCellStyle.ForeColor = Tema.TextoSuave;
                }
            }
        }
        AtualizarValor();
    }

    /// <summary>Quantidade digitada errada: limpa a célula e avisa (depois de sair da edição, sem prender o foco).</summary>
    private void ConferirQuantidade(int linha, int coluna)
    {
        if (linha < 0 || coluna != ColDevolver || _itens.Rows[linha].Tag is not ItemApi item) return;
        var celula = _itens.Rows[linha].Cells[ColDevolver];
        var texto = Convert.ToString(celula.Value)?.Trim() ?? "";
        if (texto.Length == 0) return;
        string? erro = null;
        if (!Formatos.TryLerDecimal(texto, out var q) || q < 0) erro = "Digite uma quantidade válida.";
        else if (q > 0 && !Devolucao.QuantidadeValida(item.Unidade, q)) erro = $"\"{item.Descricao}\" é vendido por {item.Unidade}: use quantidade inteira.";
        else if (q > item.Restante) erro = $"De \"{item.Descricao}\" só dá para devolver {Formatos.Quantidade(item.Restante, item.Unidade)}.";
        if (erro is null)
        {
            celula.Value = q == 0 ? "" : Formatos.Quantidade(q, item.Unidade);
            return;
        }
        celula.Value = "";
        string aviso = erro;
        BeginInvoke(new Action(() => Ui.Aviso(this, aviso)));
    }

    private List<(ItemApi item, decimal quantidade)> Pedido()
    {
        var lista = new List<(ItemApi, decimal)>();
        foreach (DataGridViewRow r in _itens.Rows)
        {
            if (r.Tag is not ItemApi item) continue;
            var texto = Convert.ToString(r.Cells[ColDevolver].Value)?.Trim() ?? "";
            if (Formatos.TryLerDecimal(texto, out var q) && q > 0) lista.Add((item, q));
        }
        return lista;
    }

    private decimal ValorPedido(VendaApi v) =>
        Math.Min(v.Total - v.ValorDevolvido, Pedido().Sum(p => ValorItem(v, p.item, p.quantidade)));

    // Mesma conta do servidor: valor do item (com desconto dele) e o rateio do desconto da venda.
    private static decimal ValorItem(VendaApi v, ItemApi i, decimal quantidade) =>
        Devolucao.Valor(i.Total, i.Quantidade, quantidade, v.Itens.Sum(x => x.Total), v.Total);

    private void AtualizarValor()
    {
        var v = VendaSelecionada();
        var valor = v is null ? 0 : ValorPedido(v);
        _valorDevolucao.Text = v is null ? "Escolha uma venda acima" : $"Valor da devolução: {Formatos.Moeda(valor)}";
        _devolver.Enabled = valor > 0 || (v is not null && Pedido().Count > 0);
    }

    private async Task DevolverAsync()
    {
        _itens.EndEdit();
        var v = VendaSelecionada();
        var pedido = Pedido();
        if (v is null || pedido.Count == 0)
        {
            Ui.Aviso(this, "Escolha a venda e digite, na coluna \"Devolver\", a quantidade de cada item que volta.");
            return;
        }
        var valor = ValorPedido(v);
        var linhas = string.Join("\n", pedido.Select(p => $"  • {Formatos.Quantidade(p.quantidade, p.item.Unidade)} {p.item.Unidade} — {p.item.Descricao}"));
        if (!Ui.Confirmar(this,
                $"Devolver da venda nº {v.Numero} ({v.Terminal}, {v.Data:dd/MM/yyyy HH:mm}):\n\n{linhas}\n\n" +
                $"Valor: {Formatos.Moeda(valor)}\n\nOs itens voltam para o estoque." +
                (v.TemFiado ? "\nComo a venda foi no fiado, o valor abate a dívida do cliente." : "")))
            return;

        try
        {
            UseWaitCursor = true;
            _devolver.Enabled = false;
            var r = await _api.PostAsync<DevolucaoApi>($"api/vendas/{v.Id}/devolver",
                new { Itens = pedido.Select(p => new { ItemId = p.item.Id, Quantidade = p.quantidade }).ToArray() });
            var msg = $"Devolução lançada. Os itens voltaram para o estoque.\n\nValor: {Formatos.Moeda(r.Valor)}";
            if (r.ValorNoFiado > 0) msg += $"\nAbatido do fiado: {Formatos.Moeda(r.ValorNoFiado)}";
            if (r.ValorEmDinheiro > 0) msg += $"\nDevolver ao cliente: {Formatos.Moeda(r.ValorEmDinheiro)}";
            Ui.Info(this, msg);
            var id = v.Id;
            await BuscarVendasAsync();
            foreach (DataGridViewRow linha in _vendas.Rows)
                if (linha.Tag is VendaApi x && x.Id == id) { _vendas.ClearSelection(); linha.Selected = true; _vendas.CurrentCell = linha.Cells[0]; break; }
            _fiadoCarregado = false;
        }
        catch (ApiException ex) { if (!IsDisposed) Ui.Erro(this, ex.Message); }
        finally
        {
            if (!IsDisposed)
            {
                UseWaitCursor = false;
                AtualizarValor();
            }
        }
    }

    // ------------------------------------------------------------------ Fiado

    private void MontarFiado()
    {
        var filtros = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, 50), WrapContents = true, BackColor = Tema.Fundo, Padding = new Padding(0, 6, 0, 0) };
        _buscaCliente.Margin = new Padding(0, 4, 8, 0);
        _buscaCliente.TextChanged += (_, _) => MostrarClientes();
        _soDevendo.CheckedChanged += (_, _) => MostrarClientes();
        var atualizar = Ui.Botao("Atualizar", Ui.Neutro, 120, 34, Icones.Atualizar);
        atualizar.Margin = new Padding(16, 0, 0, 0);
        atualizar.Click += async (_, _) => await CarregarClientesAsync();
        filtros.Controls.AddRange([Rot("Cliente"), _buscaCliente, _soDevendo, atualizar]);

        var divisao = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, BackColor = Tema.Fundo, SplitterWidth = 12 };
        var cartaoClientes = new CartaoPainel { Titulo = "Clientes", Dock = DockStyle.Fill, Padding = new Padding(1, 48, 1, 6) };
        _clientes.Dock = DockStyle.Fill;
        _clientes.Columns.AddRange(Ui.ColunaTexto("Nome", 46), Ui.ColunaTexto("Telefone", 22), Ui.ColunaTexto("Deve", 20, true));
        _clientes.SelectionChanged += async (_, _) => await MostrarClienteAsync();
        cartaoClientes.Controls.Add(_clientes);

        var cartaoConta = new CartaoPainel { Titulo = "Conta do cliente", Dock = DockStyle.Fill, Padding = new Padding(16, 48, 16, 10) };
        var topoConta = new Panel { Dock = DockStyle.Top, Height = 130, BackColor = Color.Transparent };
        _clienteNome.Location = new Point(0, 0);
        _saldo.Location = new Point(0, 22);
        var lValor = new Label { Text = "Valor", AutoSize = true, Font = Tema.Fonte(10), ForeColor = Tema.TextoSuave, Location = new Point(0, 66) };
        _valor.Location = new Point(0, 88);
        _receber.Location = new Point(152, 86);
        _lancar.Location = new Point(292, 86);
        _receber.Click += async (_, _) => await LancarAsync(receber: true);
        _lancar.Click += async (_, _) => await LancarAsync(receber: false);
        topoConta.Controls.AddRange([_clienteNome, _saldo, lValor, _valor, _receber, _lancar]);
        var lHist = new Label { Text = "Últimos lançamentos", Dock = DockStyle.Top, Height = 28, Font = Tema.Semibold(10), ForeColor = Tema.Texto, TextAlign = ContentAlignment.BottomLeft };
        _lancamentos.Dock = DockStyle.Fill;
        _lancamentos.Columns.AddRange(Ui.ColunaTexto("Data", 26), Ui.ColunaTexto("O quê", 46), Ui.ColunaTexto("Valor", 22, true));
        cartaoConta.Controls.Add(_lancamentos);
        cartaoConta.Controls.Add(lHist);
        cartaoConta.Controls.Add(topoConta);

        divisao.Panel1.Controls.Add(cartaoClientes);
        divisao.Panel2.Controls.Add(cartaoConta);

        _painelFiado.Controls.Add(divisao);
        _painelFiado.Controls.Add(filtros);
        _painelFiado.VisibleChanged += (_, _) =>
        {
            if (!_painelFiado.Visible) return;
            try { divisao.SplitterDistance = Math.Max(260, (int)(divisao.Width * 0.45)); } catch (InvalidOperationException) { }
        };
        MostrarConta(null, null);
    }

    private async Task CarregarClientesAsync()
    {
        try
        {
            UseWaitCursor = true;
            _listaClientes = await _api.GetAsync<List<ClienteApi>>("api/clientes/fiado");
            if (IsDisposed) return;
            _fiadoCarregado = true;
            MostrarClientes();
        }
        catch (ApiException ex) { if (!IsDisposed) Ui.Erro(this, ex.Message); }
        finally { if (!IsDisposed) UseWaitCursor = false; }
    }

    private void MostrarClientes()
    {
        var selecionado = (_clientes.CurrentRow?.Tag as ClienteApi)?.Id;
        var busca = Formatos.RemoverAcentos(_buscaCliente.Text.Trim()).ToLowerInvariant();
        var digitos = Formatos.SoDigitos(busca);
        _clientes.Rows.Clear();
        foreach (var c in _listaClientes)
        {
            if (_soDevendo.Checked && c.Saldo <= 0) continue;
            if (busca.Length > 0 &&
                !Formatos.RemoverAcentos(c.Nome).ToLowerInvariant().Contains(busca) &&
                !(digitos.Length >= 3 && (Formatos.SoDigitos(c.CpfCnpj).Contains(digitos) || Formatos.SoDigitos(c.Telefone).Contains(digitos))))
                continue;
            var i = _clientes.Rows.Add(c.Nome, Formatos.FormatarTelefone(c.Telefone), c.Saldo > 0 ? Formatos.Moeda(c.Saldo) : "—");
            _clientes.Rows[i].Tag = c;
            if (c.Saldo > 0) _clientes.Rows[i].Cells[2].Style.ForeColor = Tema.Alerta;
            if (c.Id == selecionado) _clientes.CurrentCell = _clientes.Rows[i].Cells[0];
        }
        if (_clientes.Rows.Count == 0) MostrarConta(null, null);
    }

    private ClienteApi? ClienteSelecionado() =>
        _clientes.SelectedRows.Count == 1 ? _clientes.SelectedRows[0].Tag as ClienteApi : null;

    private int _versaoConta;

    private async Task MostrarClienteAsync()
    {
        var c = ClienteSelecionado();
        var versao = ++_versaoConta;
        if (c is null) { MostrarConta(null, null); return; }
        try
        {
            var r = await _api.GetAsync<FiadoApi>($"api/clientes/{c.Id}/fiado");
            // Trocou de cliente enquanto carregava: ignora a resposta velha.
            if (IsDisposed || versao != _versaoConta) return;
            c.Saldo = r.Saldo;
            MostrarConta(c, r);
        }
        catch (ApiException ex) { if (!IsDisposed) Ui.Erro(this, ex.Message); }
    }

    private void MostrarConta(ClienteApi? c, FiadoApi? conta)
    {
        _lancamentos.Rows.Clear();
        _receber.Enabled = _lancar.Enabled = _valor.Enabled = c is not null;
        if (c is null || conta is null)
        {
            _clienteNome.Text = "Escolha um cliente na lista";
            _saldo.Text = "—";
            _saldo.ForeColor = Tema.TextoSuave;
            return;
        }
        _clienteNome.Text = c.Nome + (string.IsNullOrEmpty(c.CpfCnpj) ? "" : "  ·  " + Formatos.FormatarCpfCnpj(c.CpfCnpj));
        _saldo.Text = conta.Saldo > 0 ? $"Deve {Formatos.Moeda(conta.Saldo)}" : "Não deve nada";
        _saldo.ForeColor = conta.Saldo > 0 ? Tema.Alerta : Tema.Sucesso;
        foreach (var l in conta.Lancamentos)
        {
            var i = _lancamentos.Rows.Add(l.Data.ToString("dd/MM/yyyy HH:mm", Ui.Br), l.Observacao ?? "",
                (l.Valor < 0 ? "− " : "+ ") + Formatos.Moeda(Math.Abs(l.Valor)));
            _lancamentos.Rows[i].Cells[2].Style.ForeColor = l.Valor < 0 ? Tema.Sucesso : Tema.Alerta;
        }
    }

    private async Task LancarAsync(bool receber)
    {
        var c = ClienteSelecionado();
        if (c is null) return;
        var valor = _valor.Valor;
        if (valor <= 0)
        {
            Ui.Aviso(this, receber ? "Digite o valor recebido." : "Digite o valor da dívida.");
            _valor.Focus();
            return;
        }
        if (receber && valor > c.Saldo)
        {
            Ui.Aviso(this, $"{c.Nome} deve {Formatos.Moeda(c.Saldo)}. O recebimento não pode passar disso.");
            return;
        }
        var pergunta = receber
            ? $"Receber {Formatos.Moeda(valor)} de {c.Nome}?\n\nDeve agora: {Formatos.Moeda(c.Saldo)}\nVai dever: {Formatos.Moeda(c.Saldo - valor)}"
            : $"Lançar {Formatos.Moeda(valor)} de dívida para {c.Nome}?\n\nUse só para acertos: o fiado das vendas do caixa entra sozinho.";
        if (!Ui.Confirmar(this, pergunta)) return;

        try
        {
            UseWaitCursor = true;
            _receber.Enabled = _lancar.Enabled = false;
            await _api.PostAsync<object>($"api/clientes/{c.Id}/fiado",
                new { Valor = receber ? -valor : valor, Observacao = receber ? "Recebimento" : "Lançamento manual" });
            _valor.Valor = 0;
            await CarregarClientesAsync();
            await MostrarClienteAsync();
        }
        catch (ApiException ex) { if (!IsDisposed) Ui.Erro(this, ex.Message); }
        finally
        {
            if (!IsDisposed)
            {
                UseWaitCursor = false;
                _receber.Enabled = _lancar.Enabled = ClienteSelecionado() is not null;
            }
        }
    }

    // ------------------------------------------------------------------ Respostas da API

    private sealed class VendaApi
    {
        public Guid Id { get; set; }
        public int Numero { get; set; }
        public string Terminal { get; set; } = "";
        public DateTime Data { get; set; }
        public string? Cliente { get; set; }
        public decimal Total { get; set; }
        public decimal ValorDevolvido { get; set; }
        public bool TemFiado { get; set; }
        public List<ItemApi> Itens { get; set; } = [];
    }

    private sealed class ItemApi
    {
        public Guid Id { get; set; }
        public string Descricao { get; set; } = "";
        public string Unidade { get; set; } = "UN";
        public decimal Quantidade { get; set; }
        public decimal JaDevolvida { get; set; }
        public decimal PrecoUnitario { get; set; }
        public decimal Total { get; set; }
        public decimal Restante => Quantidade - JaDevolvida;
    }

    private sealed class DevolucaoApi
    {
        public decimal Valor { get; set; }
        public decimal ValorNoFiado { get; set; }
        public decimal ValorEmDinheiro { get; set; }
    }

    private sealed class ClienteApi
    {
        public Guid Id { get; set; }
        public string Nome { get; set; } = "";
        public string? CpfCnpj { get; set; }
        public string? Telefone { get; set; }
        public decimal Saldo { get; set; }
    }

    private sealed class FiadoApi
    {
        public decimal Saldo { get; set; }
        public List<LinhaApi> Lancamentos { get; set; } = [];
    }

    private sealed class LinhaApi
    {
        public DateTime Data { get; set; }
        public decimal Valor { get; set; }
        public string? Observacao { get; set; }
    }
}
