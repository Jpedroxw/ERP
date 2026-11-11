using System.Globalization;
using ProjetoJP.Controles;
using ProjetoJP.Gestao.Servicos;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;

namespace ProjetoJP.Gestao.Telas;

/// <summary>
/// Painel inicial do modo completo: indicadores do dia e do mês, últimas vendas e estoque baixo.
/// Operadores (sem acesso a relatórios) veem atalhos no lugar dos números.
/// </summary>
public class InicioForm : Form
{
    private readonly ApiClient _api;
    private readonly Action<string> _abrir;

    private readonly Indicador _hoje = new("Vendas hoje", Icones.Carrinho);
    private readonly Indicador _ticket = new("Ticket médio hoje", Icones.Calculadora);
    private readonly Indicador _mes = new("Vendas no mês", Icones.Relatorios, Tema.Sucesso);
    private readonly Indicador _baixo = new("Estoque baixo", Icones.Aviso, Tema.Alerta);

    private readonly DataGridView _gradeVendas = Ui.Grade();
    private readonly DataGridView _gradeEstoque = Ui.Grade();
    private readonly Label _situacao = new() { AutoSize = true, Font = Tema.Fonte(9), ForeColor = Tema.TextoSuave };

    /// <param name="abrir">Abre outra tela pela chave (produtos, estoque...).</param>
    public InicioForm(ApiClient api, Action<string> abrir)
    {
        _api = api;
        _abrir = abrir;
        Text = "Início";
        BackColor = Tema.Fundo;
        Font = Tema.Fonte(10);
        Padding = new Padding(28, 20, 28, 24);

        var sessao = api.Sessao!;
        var br = new CultureInfo("pt-BR");

        // Cabeçalho
        var cabecalho = new Panel { Dock = DockStyle.Top, Height = 80 };
        var ola = new Label { Text = $"Olá, {sessao.Nome.Split(' ')[0]}", Font = Tema.Semibold(18), ForeColor = Tema.Texto, AutoSize = true, Location = new Point(0, 0) };
        var data = new Label
        {
            Text = br.TextInfo.ToTitleCase(DateTime.Today.ToString("dddd, d 'de' MMMM 'de' yyyy", br)),
            Font = Tema.Fonte(10), ForeColor = Tema.TextoSuave, AutoSize = true, Location = new Point(2, 36)
        };
        var atualizar = Ui.Botao("Atualizar", Ui.Neutro, 112, 36, Icones.Atualizar);
        atualizar.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        atualizar.Click += async (_, _) => await CarregarAsync();
        cabecalho.Controls.AddRange([ola, data, atualizar]);
        cabecalho.Resize += (_, _) =>
        {
            atualizar.Location = new Point(cabecalho.Width - atualizar.Width, 4);
            _situacao.Location = new Point(cabecalho.Width - atualizar.Width - _situacao.PreferredWidth - 12, 14);
        };
        cabecalho.Controls.Add(_situacao);

        if (!api.Pode(Permissoes.VerVendas))
        {
            atualizar.Visible = false;
            Controls.Add(Atalhos());
            Controls.Add(cabecalho);
            Aparencia.AplicarFundo(this);
            return;
        }

        // Indicadores
        var indicadores = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, 140),
            WrapContents = true, BackColor = Tema.Fundo, Padding = new Padding(0, 8, 0, 8)
        };
        indicadores.Controls.AddRange([_hoje, _ticket, _mes, _baixo]);
        // Tela estreita (1024): os 4 cartões encolhem até 180 para ficar numa linha; menos que isso, quebram linha.
        indicadores.Resize += (_, _) =>
        {
            var cartoes = indicadores.Controls.Cast<Control>().ToList();
            var livre = indicadores.ClientSize.Width - indicadores.Padding.Horizontal - cartoes.Sum(c => c.Margin.Horizontal) - 4;
            var largura = Math.Clamp(livre / Math.Max(1, cartoes.Count), 180, 240);
            foreach (var c in cartoes) if (c.Width != largura) c.Width = largura;
        };
        _baixo.Cursor = Cursors.Hand;
        _baixo.Click += (_, _) => _abrir("estoque");

        // Listas lado a lado
        var listas = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Tema.Fundo };
        listas.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        listas.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        listas.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var cartaoVendas = new CartaoPainel { Titulo = "Últimas vendas de hoje", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0) };
        _gradeVendas.Dock = DockStyle.Fill;
        _gradeVendas.DataBindingComplete += (_, _) =>
        {
            Ui.AplicarFormatos(_gradeVendas, typeof(VendaResumoDto));
            Esconder(_gradeVendas, "Cliente", "Pagamento"); // o caixa (Terminal) aparece: de onde saiu cada venda
        };
        cartaoVendas.Controls.Add(_gradeVendas);

        var cartaoEstoque = new CartaoPainel { Titulo = "Produtos abaixo do estoque mínimo", Dock = DockStyle.Fill, Margin = new Padding(8, 0, 0, 0) };
        _gradeEstoque.Dock = DockStyle.Fill;
        _gradeEstoque.DataBindingComplete += (_, _) =>
        {
            Ui.AplicarFormatos(_gradeEstoque, typeof(EstoqueBaixoDto));
            Esconder(_gradeEstoque, "Codigo", "Unidade");
        };
        cartaoEstoque.Controls.Add(_gradeEstoque);

        listas.Controls.Add(cartaoVendas, 0, 0);
        listas.Controls.Add(cartaoEstoque, 1, 0);

        Controls.Add(listas);
        Controls.Add(indicadores);
        Controls.Add(cabecalho);
        Aparencia.AplicarFundo(this); // plano de fundo (se tiver) em volta dos cartões

        Shown += async (_, _) =>
        {
            await CarregarAsync();
            _automatico.Start();
        };
        // Atualiza sozinho (as vendas chegam dos caixas logo depois de finalizadas).
        _automatico.Tick += async (_, _) =>
        {
            if (!Visible || _atualizando) return;
            await CarregarAsync();
        };
        FormClosed += (_, _) => _automatico.Dispose();
    }

    private readonly System.Windows.Forms.Timer _automatico = new() { Interval = 30_000 };
    private bool _atualizando;

    private static void Esconder(DataGridView grade, params string[] colunas)
    {
        foreach (DataGridViewColumn c in grade.Columns)
            if (colunas.Contains(c.DataPropertyName)) c.Visible = false;
    }

    /// <summary>Para quem não vê relatórios: blocos de atalho.</summary>
    private Control Atalhos()
    {
        var area = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Tema.Fundo, Padding = new Padding(0, 8, 0, 0) };
        void Bloco(string chave, string icone, string titulo, string descricao)
        {
            var b = new BlocoAtalho(icone, titulo, descricao);
            b.Click += (_, _) => _abrir(chave);
            area.Controls.Add(b);
        }
        Bloco("produtos", Icones.Produtos, "Produtos", "Preços, códigos e estoque mínimo");
        Bloco("clientes", Icones.Clientes, "Clientes", "CPF/CNPJ, telefone e endereço");
        Bloco("estoque", Icones.Estoque, "Estoque", "Entradas, saídas e inventário");
        return area;
    }

    private async Task CarregarAsync()
    {
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var inicioMes = new DateOnly(hoje.Year, hoje.Month, 1);
        var qHoje = $"?de={hoje:yyyy-MM-dd}&ate={hoje:yyyy-MM-dd}";
        var qMes = $"?de={inicioMes:yyyy-MM-dd}&ate={hoje:yyyy-MM-dd}";

        if (_atualizando || IsDisposed) return;
        _atualizando = true;
        _situacao.ForeColor = Tema.TextoSuave;
        _situacao.Text = "Atualizando...";
        try
        {
            var diaTask = _api.GetAsync<List<VendasPorDiaDto>>("api/relatorios/vendas-por-dia" + qHoje);
            var mesTask = _api.GetAsync<List<VendasPorDiaDto>>("api/relatorios/vendas-por-dia" + qMes);
            var vendasTask = _api.GetAsync<List<VendaResumoDto>>("api/relatorios/vendas" + qHoje);
            var baixoTask = _api.GetAsync<List<EstoqueBaixoDto>>("api/relatorios/estoque-baixo");
            await Task.WhenAll(diaTask, mesTask, vendasTask, baixoTask);

            var dia = diaTask.Result;
            var totalHoje = dia.Sum(d => d.Total);
            var qtdHoje = dia.Sum(d => d.Quantidade);
            _hoje.Definir(Formatos.Moeda(totalHoje), qtdHoje == 1 ? "1 venda" : $"{qtdHoje} vendas");
            _ticket.Definir(Formatos.Moeda(qtdHoje > 0 ? Math.Round(totalHoje / qtdHoje, 2) : 0), "valor médio por venda");

            var mes = mesTask.Result;
            _mes.Definir(Formatos.Moeda(mes.Sum(d => d.Total)), $"{mes.Sum(d => d.Quantidade)} vendas desde dia 1º");

            var baixo = baixoTask.Result;
            _baixo.Definir(baixo.Count.ToString(), baixo.Count == 0 ? "tudo acima do mínimo" : "clique para ver o estoque");

            _gradeVendas.DataSource = vendasTask.Result.Take(20).ToList();
            _gradeEstoque.DataSource = baixo;

            _situacao.Text = $"Atualizado às {DateTime.Now:HH:mm}";
        }
        catch (ApiException ex)
        {
            if (IsDisposed) return;
            _situacao.ForeColor = Tema.Perigo;
            _situacao.Text = ex.Message;
        }
        finally
        {
            _atualizando = false;
        }
        if (IsDisposed || _situacao.Parent is null) return;
        _situacao.Location = new Point(_situacao.Parent!.Width - 112 - _situacao.PreferredWidth - 12, 14);
    }
}
