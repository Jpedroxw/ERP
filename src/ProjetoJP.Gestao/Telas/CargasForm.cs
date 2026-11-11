using ProjetoJP.Controles;
using ProjetoJP.Gestao.Servicos;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;

namespace ProjetoJP.Gestao.Telas;

/// <summary>
/// Enviar produtos aos caixas (carga): o que foi salvo na Retaguarda só chega aos caixas depois de publicado aqui.
/// Mostra o que muda em cada produto e, embaixo, se cada caixa já baixou a última carga (atualiza sozinho).
/// </summary>
public sealed class CargasForm : Form
{
    private readonly ApiClient _api;
    private readonly DataGridView _grade = Ui.Grade();
    private readonly DataGridView _gradeCaixas = Ui.Grade();
    private readonly Label _resumo = Ui.Rotulo("Carregando...");
    private readonly CampoBusca _busca = new() { Dica = "Filtrar por código ou descrição...", Width = 300, Height = 36 };
    private readonly CheckBox _soPendentes = new() { Text = "Mostrar só os pendentes", AutoSize = true, Font = Tema.Fonte(10), Checked = true };
    private readonly System.Windows.Forms.Timer _automatico = new() { Interval = 20_000 };
    private List<CargaProdutoDto> _produtos = [];
    private bool _carregando;

    public CargasForm(ApiClient api)
    {
        _api = api;
        Text = "Enviar produtos aos caixas";
        Icon = Ui.IconeApp ?? Icon;
        BackColor = Tema.Fundo;
        Font = Tema.Fonte(10);
        Size = new Size(1100, 700);
        Padding = new Padding(16, 12, 16, 12);

        // Título e explicação (quebra linha se a janela ficar estreita)
        var cabecalho = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, BackColor = Tema.Superficie, Padding = new Padding(16, 12, 16, 10)
        };
        cabecalho.Controls.Add(Ui.Rotulo("Enviar produtos aos caixas", 16, FontStyle.Bold));
        var explicacao = Ui.Rotulo("Produto novo e mudança de preço, código, código de barras, descrição, unidade ou desativação " +
                                   "ficam pendentes até serem enviados aqui. O estoque não depende disso. " +
                                   "Cada caixa recebe na próxima sincronização (ou na hora, com Ctrl+S no caixa).", 10);
        explicacao.ForeColor = Tema.TextoSuave;
        cabecalho.Controls.Add(explicacao);
        cabecalho.SizeChanged += (_, _) => explicacao.MaximumSize = new Size(Math.Max(200, cabecalho.ClientSize.Width - 40), 0);

        // Botões e filtros (descem de linha se não couberem)
        var botoes = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true,
            BackColor = Tema.Superficie, Padding = new Padding(16, 4, 16, 10)
        };
        var todos = Ui.Botao("Enviar todos os pendentes", Ui.Sucesso, largura: 240, altura: 36, icone: Icones.Sincronizar);
        var selecionados = Ui.Botao("Enviar selecionados", largura: 190, altura: 36, icone: Icones.Ok);
        var atualizar = Ui.Botao("Atualizar", Ui.Neutro, 120, 36, Icones.Atualizar);
        todos.Click += async (_, _) => await PublicarAsync(todos: true);
        selecionados.Click += async (_, _) => await PublicarAsync(todos: false);
        atualizar.Click += async (_, _) => await CarregarAsync();
        _busca.Margin = new Padding(12, 0, 0, 0);
        _busca.Caixa.TextChanged += (_, _) => Mostrar();
        _soPendentes.Margin = new Padding(12, 9, 0, 0);
        _soPendentes.CheckedChanged += (_, _) => Mostrar();
        botoes.Controls.AddRange([todos, selecionados, atualizar, _busca, _soPendentes]);

        // Produtos
        _grade.Dock = DockStyle.Fill;
        _grade.MultiSelect = true;
        _grade.Columns.AddRange(
            Ui.ColunaTexto("Código", 9), Ui.ColunaTexto("Descrição", 30), Ui.ColunaTexto("Situação", 14),
            Ui.ColunaTexto("O que muda no caixa", 30), Ui.ColunaTexto("Preço no caixa", 10, true), Ui.ColunaTexto("Último envio", 12));
        _grade.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex < 0 || _grade.Rows[e.RowIndex].Tag is not CargaProdutoDto p || !p.Pendente) return;
            _grade.ClearSelection();
            _grade.Rows[e.RowIndex].Selected = true;
            await PublicarAsync(todos: false);
        };

        // Caixas: já receberam a última carga?
        var lCaixas = Ui.Rotulo("Caixas — já receberam o último envio?  (atualiza sozinho)", 10.5f, FontStyle.Bold);
        lCaixas.Dock = DockStyle.Top;
        lCaixas.Padding = new Padding(0, 8, 0, 6);
        _gradeCaixas.Dock = DockStyle.Fill;
        _gradeCaixas.Columns.AddRange(
            Ui.ColunaTexto("Caixa", 30), Ui.ColunaTexto("Última sincronização", 30), Ui.ColunaTexto("Recebeu o último envio", 30));
        var painelCaixas = new Panel { Dock = DockStyle.Bottom, Height = 170, BackColor = Tema.Fundo, Padding = new Padding(0, 4, 0, 0) };
        painelCaixas.Controls.Add(_gradeCaixas);
        painelCaixas.Controls.Add(lCaixas);

        var rodape = new Panel { Dock = DockStyle.Bottom, Height = 34, BackColor = Tema.Fundo, Padding = new Padding(2, 8, 0, 0) };
        _resumo.Dock = DockStyle.Fill;
        _resumo.ForeColor = Tema.TextoSuave;
        rodape.Controls.Add(_resumo);

        Controls.Add(_grade);
        Controls.Add(rodape);
        Controls.Add(painelCaixas);
        Controls.Add(botoes);
        Controls.Add(cabecalho);
        Shown += async (_, _) =>
        {
            await CarregarAsync();
            _automatico.Start();
        };
        // Só a situação dos caixas: a lista de produtos não muda sozinha (e não perde a seleção).
        _automatico.Tick += async (_, _) => { if (Visible) await CarregarCaixasAsync(silencioso: true); };
        FormClosed += (_, _) => _automatico.Dispose();
    }

    private static string Hora(DateTime? d)
    {
        if (d is not { } v) return "—";
        var local = v.Kind == DateTimeKind.Utc ? v.ToLocalTime() : v;
        return local.ToString("dd/MM/yyyy HH:mm", Ui.Br);
    }

    private async Task CarregarAsync()
    {
        if (_carregando) return;
        _carregando = true;
        try
        {
            UseWaitCursor = true;
            _produtos = await _api.GetAsync<List<CargaProdutoDto>>("api/cargas/produtos");
            if (IsDisposed) return;
            Mostrar();
            await CarregarCaixasAsync(silencioso: false);
        }
        catch (ApiException ex) { if (!IsDisposed) Ui.Erro(this, ex.Message); }
        finally
        {
            _carregando = false;
            if (!IsDisposed) UseWaitCursor = false;
        }
    }

    private async Task CarregarCaixasAsync(bool silencioso)
    {
        try
        {
            var caixas = await _api.GetAsync<List<CargaTerminalDto>>("api/cargas/terminais");
            if (IsDisposed) return;
            _gradeCaixas.Rows.Clear();
            foreach (var c in caixas)
            {
                var i = _gradeCaixas.Rows.Add(c.Nome, Hora(c.UltimaSincronizacao), c.Situacao);
                _gradeCaixas.Rows[i].DefaultCellStyle.ForeColor = c.EmDia ? Tema.Sucesso : Tema.Alerta;
            }
            if (caixas.Count == 0) _gradeCaixas.Rows.Add("Nenhum caixa cadastrado", "", "");
        }
        catch (ApiException ex)
        {
            if (!silencioso && !IsDisposed) Ui.Erro(this, ex.Message);
        }
    }

    private void Mostrar()
    {
        var filtro = _busca.Caixa.Text.Trim();
        _grade.Rows.Clear();
        foreach (var p in _produtos.Where(p => (!_soPendentes.Checked || p.Pendente) &&
                                               (filtro.Length == 0 ||
                                                p.Codigo.Contains(filtro, StringComparison.OrdinalIgnoreCase) ||
                                                PesquisaProdutos.Pontuar(filtro, p.Descricao, p.Codigo, null) >= 0)))
        {
            var semPreco = p.Pendente && p.Ativo && p.PrecoEmEdicao <= 0;
            var i = _grade.Rows.Add(p.Codigo, p.Descricao, p.Situacao,
                semPreco ? "SEM PREÇO: não vai para os caixas" : p.Mudancas,
                p.Publicado ? Formatos.Moeda(p.PrecoPublicado) : "—",
                Hora(p.PublicadoEm));
            var linha = _grade.Rows[i];
            linha.Tag = p;
            if (semPreco) linha.DefaultCellStyle.ForeColor = Tema.Perigo;
            else if (p.Pendente) linha.DefaultCellStyle.ForeColor = Tema.Alerta;
        }
        var pendentes = _produtos.Count(x => x.Pendente);
        var semPrecoTotal = _produtos.Count(x => x.Pendente && x.Ativo && x.PrecoEmEdicao <= 0);
        _resumo.Text = (pendentes == 0 ? "Tudo enviado: os caixas estão com os dados atuais" : $"{pendentes} pendente(s)") +
                       $"  ·  {_produtos.Count(x => x.Publicado)} nos caixas" +
                       (semPrecoTotal > 0 ? $"  ·  {semPrecoTotal} sem preço (em vermelho) não vão" : "") +
                       "  ·  Duplo clique envia um produto; Ctrl ou Shift seleciona vários";
    }

    private async Task PublicarAsync(bool todos)
    {
        var ids = todos
            ? _produtos.Where(x => x.Pendente).Select(x => x.Id).ToList()
            : _grade.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag).OfType<CargaProdutoDto>()
                .Where(x => x.Pendente).Select(x => x.Id).Distinct().ToList();
        if (ids.Count == 0)
        {
            Ui.Aviso(this, todos ? "Não há produtos pendentes: os caixas já estão com tudo." : "Selecione um ou mais produtos pendentes na lista.");
            return;
        }
        if (!Ui.Confirmar(this, $"Enviar {ids.Count} produto(s) para os caixas?\n\nEles chegam na próxima sincronização de cada caixa.")) return;

        try
        {
            UseWaitCursor = true;
            var publicados = 0;
            var ignorados = new List<CargaIgnoradaDto>();
            // De 1000 em 1000 (limite por chamada).
            foreach (var lote in ids.Chunk(1000))
            {
                var r = await _api.PostAsync<PublicarProdutosResposta>("api/cargas/produtos/publicar",
                    new PublicarProdutosRequest { Ids = [.. lote] });
                publicados += r.Publicados;
                ignorados.AddRange(r.Ignorados);
            }
            await CarregarAsync();

            var msg = $"{publicados} produto(s) enviado(s). Os caixas recebem na próxima sincronização " +
                      "(os que estiverem sem internet, quando voltarem). Acompanhe embaixo, em \"Caixas\".";
            if (ignorados.Count > 0)
            {
                msg += $"\n\n{ignorados.Count} ficaram de fora:\n" +
                       string.Join("\n", ignorados.Take(15).Select(i => $"• {i.Codigo} {i.Descricao}: {i.Motivo}")) +
                       (ignorados.Count > 15 ? $"\n… e mais {ignorados.Count - 15}." : "") +
                       "\n\nCorrija em Produtos e envie de novo.";
                Ui.Aviso(this, msg);
            }
            else
            {
                Ui.Info(this, msg);
            }
        }
        catch (ApiException ex) { if (!IsDisposed) Ui.Erro(this, ex.Message); }
        finally { if (!IsDisposed) UseWaitCursor = false; }
    }
}
