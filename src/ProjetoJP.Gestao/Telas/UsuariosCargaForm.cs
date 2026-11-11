using ProjetoJP.Controles;
using ProjetoJP.Gestao.Servicos;
using ProjetoJP.Shared.Dtos;

namespace ProjetoJP.Gestao.Telas;

/// <summary>
/// Envio manual de usuários aos caixas. Salvar ou trocar a senha na Retaguarda não chega no caixa até publicar aqui.
/// As vendas continuam subindo sozinhas.
/// </summary>
public sealed class UsuariosCargaForm : Form
{
    private readonly ApiClient _api;
    private readonly DataGridView _grade = Ui.Grade();
    private readonly DataGridView _gradeCaixas = Ui.Grade();
    private readonly Label _resumo = Ui.Rotulo("Carregando...");
    private readonly CheckBox _soPendentes = new() { Text = "Mostrar só os pendentes", AutoSize = true, Font = Tema.Fonte(10), Checked = true };
    private readonly System.Windows.Forms.Timer _automatico = new() { Interval = 20_000 };
    private List<CargaUsuarioDto> _usuarios = [];
    private bool _carregando;

    public UsuariosCargaForm(ApiClient api)
    {
        _api = api;
        Text = "Enviar usuários aos caixas";
        Icon = Ui.IconeApp ?? Icon;
        BackColor = Tema.Fundo;
        Font = Tema.Fonte(10);
        Size = new Size(980, 680);
        Padding = new Padding(16, 12, 16, 12);

        var cabecalho = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, BackColor = Tema.Superficie, Padding = new Padding(16, 12, 16, 10)
        };
        cabecalho.Controls.Add(Ui.Rotulo("Enviar usuários aos caixas", 16, FontStyle.Bold));
        var explicacao = Ui.Rotulo("Usuário novo, senha, perfil ou permissão só chega no caixa depois deste envio. " +
                                   "Não é automático. As vendas dos caixas continuam subindo sozinhas.", 10);
        explicacao.ForeColor = Tema.TextoSuave;
        cabecalho.Controls.Add(explicacao);
        cabecalho.SizeChanged += (_, _) => explicacao.MaximumSize = new Size(Math.Max(200, cabecalho.ClientSize.Width - 40), 0);

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
        _soPendentes.Margin = new Padding(12, 9, 0, 0);
        _soPendentes.CheckedChanged += (_, _) => Mostrar();
        botoes.Controls.AddRange([todos, selecionados, atualizar, _soPendentes]);

        _grade.Dock = DockStyle.Fill;
        _grade.MultiSelect = true;
        _grade.Columns.AddRange(
            Ui.ColunaTexto("Nome", 24), Ui.ColunaTexto("Login", 16), Ui.ColunaTexto("Perfil", 12),
            Ui.ColunaTexto("Situação", 16), Ui.ColunaTexto("O que muda no caixa", 28), Ui.ColunaTexto("Último envio", 14));
        _grade.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex < 0 || _grade.Rows[e.RowIndex].Tag is not CargaUsuarioDto u || !u.Pendente) return;
            _grade.ClearSelection();
            _grade.Rows[e.RowIndex].Selected = true;
            await PublicarAsync(todos: false);
        };

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
            _usuarios = await _api.GetAsync<List<CargaUsuarioDto>>("api/cargas/usuarios");
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
        _grade.Rows.Clear();
        foreach (var u in _usuarios.Where(u => !_soPendentes.Checked || u.Pendente))
        {
            var i = _grade.Rows.Add(u.Nome, u.Login, u.Perfil, u.Situacao, u.Mudancas, Hora(u.PublicadoEm));
            var linha = _grade.Rows[i];
            linha.Tag = u;
            if (u.Pendente) linha.DefaultCellStyle.ForeColor = Tema.Alerta;
        }
        var pendentes = _usuarios.Count(x => x.Pendente);
        _resumo.Text = (pendentes == 0 ? "Tudo enviado: os caixas estão com os usuários atuais" : $"{pendentes} pendente(s)") +
                       $"  ·  {_usuarios.Count(x => x.Publicado)} já enviados" +
                       "  ·  Duplo clique envia um usuário; Ctrl ou Shift seleciona vários";
    }

    private async Task PublicarAsync(bool todos)
    {
        var ids = todos
            ? _usuarios.Where(x => x.Pendente).Select(x => x.Id).ToList()
            : _grade.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag).OfType<CargaUsuarioDto>()
                .Where(x => x.Pendente).Select(x => x.Id).Distinct().ToList();
        if (ids.Count == 0)
        {
            Ui.Aviso(this, todos ? "Não há usuários pendentes." : "Selecione um ou mais usuários pendentes na lista.");
            return;
        }
        if (!Ui.Confirmar(this, $"Enviar {ids.Count} usuário(s) para os caixas?\n\nO login passa a funcionar na próxima sincronização de cada caixa. As vendas não dependem disso.")) return;

        try
        {
            UseWaitCursor = true;
            var publicados = 0;
            foreach (var lote in ids.Chunk(1000))
            {
                var r = await _api.PostAsync<PublicarProdutosResposta>("api/cargas/usuarios/publicar",
                    new PublicarProdutosRequest { Ids = [.. lote] });
                publicados += r.Publicados;
            }
            await CarregarAsync();
            Ui.Aviso(this, $"{publicados} usuário(s) enviado(s). Os caixas recebem na próxima sincronização.");
        }
        catch (ApiException ex) { if (!IsDisposed) Ui.Erro(this, ex.Message); }
        finally { if (!IsDisposed) UseWaitCursor = false; }
    }
}
