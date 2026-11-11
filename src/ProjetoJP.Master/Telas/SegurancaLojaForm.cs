using System.Globalization;
using ProjetoJP.Controles;
using ProjetoJP.Master.Servicos;
using ProjetoJP.Shared.Dtos;

namespace ProjetoJP.Master.Telas;

/// <summary>
/// Segurança de uma loja (antes ficava na Retaguarda de cada cliente): quem está conectado agora,
/// computadores lembrados de versões antigas e o histórico de alterações administrativas
/// (usuários, terminais, permissões, senha redefinida pelo Master...).
/// </summary>
public sealed class SegurancaLojaForm : Form
{
    private static readonly CultureInfo Br = new("pt-BR");
    private readonly MasterApi _api;
    private readonly ClienteMasterDto _cliente;
    private readonly DataGridView _gradeSessoes = NovaGrade();
    private readonly DataGridView _gradeComputadores = NovaGrade();
    private readonly DataGridView _gradeAuditoria = NovaGrade();
    private readonly Panel _paginaSessoes = new() { Dock = DockStyle.Fill, BackColor = Tema.Fundo };
    private readonly Panel _paginaComputadores = new() { Dock = DockStyle.Fill, BackColor = Tema.Fundo, Visible = false };
    private readonly Panel _paginaAuditoria = new() { Dock = DockStyle.Fill, BackColor = Tema.Fundo, Visible = false };
    private readonly List<(BotaoTema aba, Panel pagina)> _abas = [];
    private readonly Label _resumo = new() { Dock = DockStyle.Fill, ForeColor = Tema.TextoSuave, Font = Tema.Fonte(9), TextAlign = ContentAlignment.MiddleLeft };

    public SegurancaLojaForm(MasterApi api, ClienteMasterDto cliente)
    {
        _api = api;
        _cliente = cliente;
        Text = $"Segurança — {cliente.Nome} ({CodigoCliente.ParaTela(cliente.Codigo)})";
        Icon = Ui.IconeApp ?? Icon;
        BackColor = Tema.Fundo;
        Font = Tema.Fonte(10);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Ui.Centralizar(this, 1000, 640);
        MinimumSize = new Size(Ui.Px(760), Ui.Px(480));

        var topo = Ui.Topo(Icones.Cadeado, "Segurança da loja", $"{cliente.Nome} · servidor {CodigoCliente.ParaTela(cliente.Codigo)} · quem está conectado e o que foi alterado");

        // Abas
        var barraAbas = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true,
            BackColor = Tema.Fundo, Padding = new Padding(Ui.Px(16), Ui.Px(10), Ui.Px(16), Ui.Px(4))
        };
        Aba(barraAbas, "Conectados agora", Icones.Pessoa, _paginaSessoes);
        Aba(barraAbas, "Histórico de alterações", Icones.Relatorios, _paginaAuditoria);
        Aba(barraAbas, "Computadores lembrados", Icones.Cadeado, _paginaComputadores);

        MontarSessoes();
        MontarComputadores();
        MontarAuditoria();

        var corpo = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Ui.Px(16), Ui.Px(4), Ui.Px(16), 0), BackColor = Tema.Fundo };
        corpo.Controls.Add(_paginaSessoes);
        corpo.Controls.Add(_paginaComputadores);
        corpo.Controls.Add(_paginaAuditoria);

        var rodape = new Panel { Dock = DockStyle.Bottom, Height = Ui.Px(52), BackColor = Tema.Fundo, Padding = new Padding(Ui.Px(18), 0, Ui.Px(16), 0) };
        var atualizar = Botao("Atualizar", Icones.Atualizar);
        atualizar.Click += async (_, _) => await CarregarAsync();
        var fechar = Botao("Fechar", Icones.Cancelar);
        fechar.DialogResult = DialogResult.Cancel;
        var direita = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, BackColor = Tema.Fundo, Padding = new Padding(0, Ui.Px(8), 0, 0) };
        direita.Controls.AddRange([atualizar, fechar]);
        rodape.Controls.Add(_resumo);
        rodape.Controls.Add(direita);

        Controls.Add(corpo);
        Controls.Add(rodape);
        Controls.Add(barraAbas);
        Controls.Add(topo);
        CancelButton = fechar;
        MostrarAba(_paginaSessoes);
        Shown += async (_, _) => await CarregarAsync();
    }

    private static DataGridView NovaGrade()
    {
        var g = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        Tema.EstilizarGrade(g, 10);
        return g;
    }

    private static void Colunas(DataGridView g, params (string titulo, int peso)[] colunas)
    {
        foreach (var (titulo, peso) in colunas)
            g.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = titulo, FillWeight = peso, SortMode = DataGridViewColumnSortMode.Automatic });
    }

    private static BotaoTema Botao(string texto, string icone, EstiloBotao estilo = EstiloBotao.Secundario)
    {
        var b = new BotaoTema { Text = texto, Icone = icone, Estilo = estilo, Height = Ui.Px(36), Margin = new Padding(0, 0, Ui.Px(8), Ui.Px(6)) };
        b.Width = TextRenderer.MeasureText(texto, b.Font).Width + Ui.Px(58);
        return b;
    }

    /// <summary>Cartão com a grade e uma barra de botões em cima (os botões descem de linha se faltar espaço).</summary>
    private static Control Cartao(DataGridView grade, string explicacao, params Control[] botoes)
    {
        var cartao = new CartaoPainel { Dock = DockStyle.Fill, Padding = new Padding(1, 1, 1, Ui.Px(6)) };
        var barra = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true,
            BackColor = Tema.Superficie, Padding = new Padding(Ui.Px(11), Ui.Px(11), Ui.Px(11), Ui.Px(4))
        };
        barra.Controls.AddRange(botoes);
        var texto = new Label
        {
            Text = explicacao, AutoSize = true, Font = Tema.Fonte(9), ForeColor = Tema.TextoSuave,
            Margin = new Padding(Ui.Px(4), Ui.Px(10), 0, 0)
        };
        barra.Controls.Add(texto);
        barra.SizeChanged += (_, _) => texto.MaximumSize = new Size(Math.Max(Ui.Px(200), barra.ClientSize.Width - Ui.Px(40)), 0);
        cartao.Controls.Add(grade);
        cartao.Controls.Add(barra);
        return cartao;
    }

    private void Aba(FlowLayoutPanel barra, string texto, string icone, Panel pagina)
    {
        var b = Botao(texto, icone);
        b.Click += (_, _) => MostrarAba(pagina);
        barra.Controls.Add(b);
        _abas.Add((b, pagina));
    }

    private void MostrarAba(Panel pagina)
    {
        foreach (var (aba, p) in _abas)
        {
            p.Visible = p == pagina;
            aba.Estilo = p == pagina ? EstiloBotao.Primario : EstiloBotao.Secundario;
        }
    }

    private void MontarSessoes()
    {
        Colunas(_gradeSessoes, ("Usuário", 22), ("Entrou em", 18), ("Vale até", 18), ("Como entrou", 22));
        var encerrar = Botao("Encerrar sessão", Icones.Sair);
        encerrar.Click += async (_, _) => await EncerrarSessaoAsync();
        var todas = Botao("Encerrar todas", Icones.Cancelar, EstiloBotao.Perigo);
        todas.Click += async (_, _) => await EncerrarTodasAsync();
        _paginaSessoes.Controls.Add(Cartao(_gradeSessoes,
            "Quem está com a Gestão aberta nesta loja. Encerrar obriga a entrar de novo com a senha (use se a senha vazou ou alguém saiu da empresa).",
            encerrar, todas));
    }

    private void MontarComputadores()
    {
        Colunas(_gradeComputadores, ("Computador", 20), ("Usuário", 16), ("Cadastrado em", 15), ("Último uso", 15), ("Vale até", 15), ("Situação", 14));
        var revogar = Botao("Esquecer computador", Icones.Excluir, EstiloBotao.Perigo);
        revogar.Click += async (_, _) => await RevogarAsync();
        _paginaComputadores.Controls.Add(Cartao(_gradeComputadores,
            "Versões antigas da Gestão podiam \"lembrar o computador\" e entrar sem senha. A versão atual não usa mais isso; esquecer apaga o acesso e encerra as sessões dele.",
            revogar));
    }

    private void MontarAuditoria()
    {
        Colunas(_gradeAuditoria, ("Data", 15), ("Quem", 15), ("O que fez", 18), ("Em", 15), ("Detalhe", 37));
        _paginaAuditoria.Controls.Add(Cartao(_gradeAuditoria,
            "Alterações em usuários, terminais (caixas), permissões, sessões e senhas, as mais recentes primeiro. Senhas e chaves nunca aparecem aqui."));
    }

    private static string Hora(DateTime? d) => d is { } v
        ? (v.Kind == DateTimeKind.Utc ? v.ToLocalTime() : v).ToString("dd/MM/yyyy HH:mm", Br)
        : "—";

    private static string Acao(string acao) => acao switch
    {
        "Criar" => "Criou",
        "Alterar" => "Alterou",
        "Desativar" => "Desativou",
        "RevogarDispositivo" => "Esqueceu computador",
        "EncerrarSessao" => "Encerrou sessão",
        "EncerrarSessoes" => "Encerrou todas as sessões",
        "RedefinirSenhaMaster" => "Senha redefinida pelo Master",
        "AlterarPermissoes" => "Mudou permissões",
        _ => acao
    };

    private static string Tipo(string tipo) => tipo switch
    {
        "Usuario" => "Usuário",
        "Terminal" => "Terminal (caixa)",
        "Dispositivo" => "Computador lembrado",
        "Sessao" => "Sessão",
        _ => tipo
    };

    private async Task CarregarAsync()
    {
        UseWaitCursor = true;
        try
        {
            var s = await _api.SegurancaAsync(_cliente.Id);
            if (IsDisposed) return;

            _gradeSessoes.Rows.Clear();
            foreach (var x in s.Sessoes)
            {
                var i = _gradeSessoes.Rows.Add(x.Usuario, Hora(x.CriadaEm), Hora(x.ExpiraEm),
                    x.ComputadorLembrado ? "Computador lembrado (sem senha)" : "Com senha");
                _gradeSessoes.Rows[i].Tag = x;
                if (x.ComputadorLembrado) _gradeSessoes.Rows[i].DefaultCellStyle.ForeColor = Tema.Alerta;
            }

            _gradeComputadores.Rows.Clear();
            foreach (var x in s.Computadores)
            {
                var situacao = x.Revogado ? "Esquecido" : x.ExpiraEm < DateTime.UtcNow ? "Vencido" : "Ativo";
                var i = _gradeComputadores.Rows.Add(x.Nome, x.Usuario, Hora(x.CriadoEm), Hora(x.UltimoUsoEm), Hora(x.ExpiraEm), situacao);
                _gradeComputadores.Rows[i].Tag = x;
                if (situacao != "Ativo") _gradeComputadores.Rows[i].DefaultCellStyle.ForeColor = Tema.TextoSuave;
            }

            _gradeAuditoria.Rows.Clear();
            foreach (var x in s.Auditoria)
                _gradeAuditoria.Rows.Add(Hora(x.Data), x.Usuario, Acao(x.Acao), Tipo(x.AlvoTipo), x.Resumo);

            var ativos = s.Computadores.Count(c => !c.Revogado && c.ExpiraEm >= DateTime.UtcNow);
            _resumo.Text = $"{s.Sessoes.Count} conectado(s) agora  ·  {s.Auditoria.Count} alteração(ões) no histórico" +
                           (ativos > 0 ? $"  ·  {ativos} computador(es) lembrado(s) ativo(s)" : "") +
                           $"  ·  atualizado às {DateTime.Now:HH:mm}";
        }
        catch (MasterException ex)
        {
            if (!IsDisposed) Ui.Aviso(this, ex.Message);
        }
        finally
        {
            if (!IsDisposed) UseWaitCursor = false;
        }
    }

    private async Task EncerrarSessaoAsync()
    {
        if (_gradeSessoes.CurrentRow?.Tag is not SessaoResumoDto s)
        {
            Ui.Aviso(this, "Escolha uma sessão na lista.");
            return;
        }
        if (!Ui.Confirmar(this, $"Encerrar a sessão de \"{s.Usuario}\"?\n\nA pessoa precisa entrar de novo com a senha.")) return;
        try
        {
            await _api.EncerrarSessaoAsync(_cliente.Id, s.Id);
            await CarregarAsync();
        }
        catch (MasterException ex) { Ui.Aviso(this, ex.Message); }
    }

    private async Task EncerrarTodasAsync()
    {
        if (!Ui.Confirmar(this, $"Encerrar TODAS as sessões abertas de {_cliente.Nome}?\n\n" +
                                "Todo mundo precisa entrar de novo na Gestão. Os caixas continuam vendendo normalmente.")) return;
        try
        {
            var n = await _api.EncerrarTodasSessoesAsync(_cliente.Id);
            await CarregarAsync();
            Ui.Info(this, $"{n} sessão(ões) encerrada(s).");
        }
        catch (MasterException ex) { Ui.Aviso(this, ex.Message); }
    }

    private async Task RevogarAsync()
    {
        if (_gradeComputadores.CurrentRow?.Tag is not DispositivoResumoDto d)
        {
            Ui.Aviso(this, "Escolha um computador na lista.");
            return;
        }
        if (d.Revogado)
        {
            Ui.Aviso(this, "Esse computador já foi esquecido.");
            return;
        }
        if (!Ui.Confirmar(this, $"Esquecer o computador \"{d.Nome}\" ({d.Usuario})?\n\nEle passa a precisar da senha e as sessões abertas por ele são encerradas.")) return;
        try
        {
            await _api.RevogarComputadorAsync(_cliente.Id, d.Id);
            await CarregarAsync();
        }
        catch (MasterException ex) { Ui.Aviso(this, ex.Message); }
    }
}
