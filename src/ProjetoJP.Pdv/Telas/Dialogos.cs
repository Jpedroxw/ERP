using ProjetoJP.Controles;
using ProjetoJP.Pdv.Dados;
using ProjetoJP.Pdv.Servicos;
using ProjetoJP.Shared;

namespace ProjetoJP.Pdv.Telas;

/// <summary>Login do operador, validado no banco local (funciona sem internet).</summary>
public class LoginForm : Form
{
    private readonly CampoLogin _login = new() { Font = Tema.Fonte(13) };
    private readonly TextBox _senha = new() { Font = Tema.Fonte(13), UseSystemPasswordChar = true, MaxLength = 64 };
    private readonly Label _status = new() { AutoSize = false, Font = Tema.Fonte(8.5f), ForeColor = Tema.BarraTextoSuave };
    private Label? _terminal;
    private readonly Pilula _pilula = new() { Text = "Online" };
    private readonly Sincronizador _sync;
    private readonly PdvConfig _cfg;

    public UsuarioLocal? Usuario { get; private set; }

    /// <summary>Fecha com DialogResult.Retry quando as configurações foram alteradas (o caixa recarrega tudo).</summary>
    public LoginForm(PdvConfig cfg, Sincronizador sync)
    {
        _sync = sync;
        _cfg = cfg;
        Ui.Dialogo(this, Aparencia.Titulo("PDV"), 760, 440);
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;

        // Coluna da marca: loja, terminal e situação da conexão
        var marca = new Panel { Dock = DockStyle.Left, Width = 300, BackColor = Tema.Barra };
        Control icone = Aparencia.Logo is null && Ui.IconeApp is { } original
            ? new PictureBox { Image = original.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent, Size = new Size(52, 52), Location = new Point(36, 48) }
            : Aparencia.Marca(Icones.Carrinho, 52, new Point(36, 48));
        var loja = new Label
        {
            Text = cfg.NomeLoja, Font = Tema.Semibold(18), ForeColor = Color.White,
            AutoSize = false, Size = new Size(240, 64), Location = new Point(34, 110)
        };
        var terminal = new Label
        {
            Text = TextoTerminal(),
            Font = Tema.Fonte(10.5f), ForeColor = Tema.BarraTexto, AutoSize = true, Location = new Point(37, 178)
        };
        _terminal = terminal;
        _pilula.Location = new Point(36, 222);
        _status.SetBounds(37, 260, 240, 120);
        _status.Text = sync.UltimaMensagem;
        AplicarSituacao(sync.Online);
        marca.Controls.AddRange([icone, loja, terminal, _pilula, _status]);

        // Coluna do formulário
        var titulo = new Label { Text = "Abrir o caixa", Font = Tema.Semibold(20), ForeColor = Tema.Texto, AutoSize = true, Location = new Point(338, 52) };
        var subtitulo = new Label { Text = "Entre com seu usuário de operador.", Font = Tema.Fonte(10), ForeColor = Tema.TextoSuave, AutoSize = true, Location = new Point(341, 96) };

        var lLogin = Ui.Rotulo("Usuário", 9.5f);
        lLogin.ForeColor = Tema.TextoSuave;
        lLogin.Location = new Point(340, 140);
        _login.SetBounds(340, 164, 380, 32);

        var lSenha = Ui.Rotulo("Senha", 9.5f);
        lSenha.ForeColor = Tema.TextoSuave;
        lSenha.Location = new Point(340, 212);
        _senha.SetBounds(340, 236, 380, 32);

        var entrar = Ui.Botao("Entrar", largura: 380, altura: 44, icone: Icones.Cadeado);
        entrar.Location = new Point(340, 296);
        entrar.Click += async (_, _) => await Entrar(entrar);

        var configuracoes = new LinkLabel
        {
            Text = "Opções do caixa", AutoSize = true, Location = new Point(340, 360), Font = Tema.Fonte(9.5f),
            LinkColor = Tema.Primaria, ActiveLinkColor = Tema.PrimariaEscura, LinkBehavior = LinkBehavior.HoverUnderline
        };
        configuracoes.LinkClicked += (_, _) => AbrirConfiguracoes();

        Controls.AddRange([titulo, subtitulo, lLogin, _login, lSenha, _senha, entrar, configuracoes]);
        Controls.Add(marca);
        Aparencia.AplicarFundo(marca, 0.30f); // plano de fundo por trás da marca, se tiver
        AcceptButton = entrar;
        Shown += (_, _) => _login.Focus();

        _sync.StatusMudou += AtualizarStatus;
        FormClosed += (_, _) => _sync.StatusMudou -= AtualizarStatus;
    }

    private static string TextoTerminal() =>
        "Terminal: " + (Db.LerConfig("nome_terminal") ?? "aguardando sincronização");

    private void AplicarSituacao(bool online)
    {
        _pilula.Text = online ? "Online" : "Offline";
        _pilula.Cor = online ? Tema.Sucesso : Tema.Alerta;
    }

    private void AtualizarStatus(bool online, string msg)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(() =>
        {
            _status.Text = msg;
            AplicarSituacao(online);
            if (_terminal is not null) _terminal.Text = TextoTerminal();
        });
    }

    private void AbrirConfiguracoes()
    {
        // Com usuários já baixados, só gerente/admin mexe nas configurações.
        if (Repositorio.ContarUsuarios() > 0)
        {
            using var aut = new AutorizacaoForm("Alterar as configurações do caixa", permitirSuporte: true);
            if (aut.ShowDialog(this) != DialogResult.OK) return;
        }

        using var f = new ConfiguracaoForm(_cfg, primeiraVez: false);
        if (f.ShowDialog(this) == DialogResult.OK)
            DialogResult = DialogResult.Retry;
    }

    private async Task Entrar(Control botao)
    {
        var u = Repositorio.BuscarUsuario(_login.Text);

        // Usuário criado agora na Retaguarda ainda não chegou neste caixa: sincroniza na hora e procura de novo.
        // Senha trocada agora também: se não bater, tenta sincronizar antes de recusar.
        if (u is null || !Senha.Verificar(_senha.Text, u.SenhaHash))
        {
            botao.Enabled = false;
            UseWaitCursor = true;
            try { await _sync.SincronizarAsync(); }
            catch (Exception) { /* sem internet: segue com o que já tem */ }
            finally
            {
                UseWaitCursor = false;
                if (!IsDisposed) botao.Enabled = true;
            }
            if (IsDisposed) return;
            u = Repositorio.BuscarUsuario(_login.Text);
        }

        if (u is null)
        {
            var msg = Repositorio.ContarUsuarios() == 0
                ? "Este caixa ainda não recebeu nenhum usuário do servidor.\n\n"
                : $"O usuário \"{_login.Text.Trim().ToLowerInvariant()}\" não existe neste caixa.\n\n";
            msg += _sync.Online
                ? "A sincronização deste caixa já ocorreu. Use o login cadastrado na Gestão (não o nome da pessoa) e a senha de lá."
                : "O caixa está sem comunicação com o servidor (veja o aviso à esquerda). " +
                  "Usuários novos só chegam aqui depois que ele sincronizar.";
            Ui.Aviso(this, msg);
            _login.Focus();
            return;
        }
        if (!Senha.Verificar(_senha.Text, u.SenhaHash))
        {
            Ui.Aviso(this, "Senha incorreta.");
            _senha.SelectAll();
            _senha.Focus();
            return;
        }
        Usuario = u;
        DialogResult = DialogResult.OK;
    }
}

/// <summary>Pede um valor (abertura de caixa, desconto, sangria...). Opcionalmente pede motivo.</summary>
public class ValorForm : Form
{
    private readonly CampoMoeda _valor = new() { Font = Ui.Fonte(18) };
    private readonly TextBox? _motivo;
    private readonly bool _permiteZero;

    public decimal Valor { get; private set; }
    public string? Motivo => _motivo?.Text.Trim();

    public ValorForm(string titulo, string pergunta, decimal sugestao = 0, bool pedirMotivo = false, bool permiteZero = true)
    {
        _permiteZero = permiteZero;
        Ui.Dialogo(this, titulo, 380, pedirMotivo ? 280 : 200);

        var l = Ui.Rotulo(pergunta, 11);
        l.Location = new Point(24, 20);
        _valor.SetBounds(24, 50, 330, 40);
        _valor.Valor = sugestao;
        Controls.AddRange([l, _valor]);

        var y = 110;
        if (pedirMotivo)
        {
            var lm = Ui.Rotulo("Motivo");
            lm.Location = new Point(24, y);
            _motivo = Ui.Caixa(11);
            _motivo.MaxLength = 100;
            _motivo.SetBounds(24, y + 24, 330, 30);
            Controls.AddRange([lm, _motivo]);
            y += 80;
        }

        var ok = Ui.Botao("Confirmar", largura: 160);
        ok.Location = new Point(24, y);
        ok.Click += (_, _) => Confirmar();
        var cancelar = Ui.Botao("Cancelar", Ui.Neutro, 160);
        cancelar.Location = new Point(194, y);
        cancelar.DialogResult = DialogResult.Cancel;

        Controls.AddRange([ok, cancelar]);
        AcceptButton = ok;
        CancelButton = cancelar;
        Shown += (_, _) => { _valor.Focus(); _valor.SelectAll(); };
    }

    private void Confirmar()
    {
        if (!_permiteZero && _valor.Valor == 0)
        {
            Ui.Aviso(this, "Informe um valor maior que zero.");
            _valor.Focus();
            return;
        }
        Valor = _valor.Valor;
        DialogResult = DialogResult.OK;
    }
}

/// <summary>Senha de gerente/administrador para operações sensíveis (cancelamento etc.).</summary>
public class AutorizacaoForm : Form
{
    private readonly CampoLogin _login = new() { Font = Ui.Fonte(12) };
    private readonly TextBox _senha = Ui.Caixa(12);

    public UsuarioLocal? Autorizador { get; private set; }

    /// <summary>Permissão que quem autoriza precisa ter (null = qualquer Gerente/Admin, como antes).</summary>
    private readonly string? _permissao;

    /// <param name="permitirSuporte">Mostra "Usar a senha técnica": saída para quando nenhum gerente consegue autorizar
    /// (ex.: caixa apontando para o servidor errado e precisando entrar nas Opções para corrigir).</param>
    public AutorizacaoForm(string motivo, bool permitirSuporte = false, string? permissao = null)
    {
        _permissao = permissao;
        Ui.Dialogo(this, "Autorização do gerente", 360, permitirSuporte ? 328 : 270);
        var l = Ui.Rotulo(motivo, 10, FontStyle.Bold);
        l.Location = new Point(24, 16);
        l.MaximumSize = new Size(310, 0);

        var l1 = Ui.Rotulo("Usuário do gerente");
        l1.Location = new Point(24, 56);
        _login.SetBounds(24, 80, 310, 30);
        var l2 = Ui.Rotulo("Senha");
        l2.Location = new Point(24, 118);
        _senha.SetBounds(24, 142, 310, 30);
        _senha.UseSystemPasswordChar = true;
        _senha.MaxLength = 64;

        var ok = Ui.Botao("Autorizar", largura: 150);
        ok.Location = new Point(24, 198);
        ok.Click += (_, _) =>
        {
            var login = _login.Text.Trim().ToLowerInvariant();
            var u = Repositorio.BuscarUsuario(login);
            var erro =
                login.Length == 0 ? "Informe o usuário do gerente." :
                u is null ? UsuarioNaoEncontrado(login) :
                _permissao is null && !u.PodeAutorizar ? $"O usuário \"{login}\" é Operador. Só um usuário Gerente ou Admin pode autorizar." :
                _permissao is not null && !u.Pode(_permissao) ? $"O usuário \"{login}\" não tem permissão para: {Permissoes.Descricao(_permissao).ToLowerInvariant()}." :
                !Senha.Verificar(_senha.Text, u.SenhaHash) ? "Senha incorreta." :
                null;
            if (erro is not null)
            {
                Ui.Aviso(this, erro);
                (u is null ? (Control)_login : _senha).Focus();
                _senha.SelectAll();
                return;
            }
            Autorizador = u;
            DialogResult = DialogResult.OK;
        };
        var cancelar = Ui.Botao("Cancelar", Ui.Neutro, 150);
        cancelar.Location = new Point(184, 198);
        cancelar.DialogResult = DialogResult.Cancel;

        Controls.AddRange([l, l1, _login, l2, _senha, ok, cancelar]);
        if (permitirSuporte)
        {
            LinkLabel Link(string texto, int y) => new()
            {
                Text = texto, AutoSize = true, Location = new Point(24, y), Font = Ui.Fonte(9.5f),
                LinkColor = Tema.Primaria, ActiveLinkColor = Tema.PrimariaEscura, LinkBehavior = LinkBehavior.HoverUnderline
            };
            // 1) Online: login técnico da empresa, controlado pelo ERP Master (precisa do servidor respondendo).
            var empresa = Link("Acesso técnico da empresa (online)", 252);
            empresa.LinkClicked += (_, _) =>
            {
                using var acesso = new SuporteClienteForm();
                if (acesso.ShowDialog(this) == DialogResult.OK) DialogResult = DialogResult.OK;
            };
            // 2) Sem internet: a senha técnica deste computador. É a saída quando o caixa não fala com o servidor
            //    (servidor ou chave errados) — justamente o caso em que é preciso entrar nas Opções para corrigir.
            var local = Link("Senha técnica deste computador (sem internet)", 280);
            local.LinkClicked += (_, _) =>
            {
                if (ConfigTecnica.PedirSenha(this)) DialogResult = DialogResult.OK;
            };
            Controls.AddRange([empresa, local]);
        }
        AcceptButton = ok;
        CancelButton = cancelar;
    }

    /// <summary>Explica por que o login não está neste caixa (os usuários vêm do servidor configurado).</summary>
    private static string UsuarioNaoEncontrado(string login)
    {
        var servidor = PdvConfig.Carregar().Servidor;
        var nome = string.IsNullOrWhiteSpace(servidor) || EnderecoServidor.PareceEndereco(servidor) ? "configurado" : $"\"{EnderecoServidor.ParaTela(servidor)}\"";
        var gerentes = Repositorio.ContarAutorizadores();
        return $"O usuário \"{login}\" não existe neste caixa.\n\n" +
               $"Os usuários vêm da Gestão do servidor {nome} (o que está em Opções do caixa → Servidor). " +
               (gerentes == 0
                   ? "Esse servidor ainda não mandou nenhum Gerente ou Admin para este caixa."
                   : $"Este caixa tem {gerentes} usuário(s) Gerente/Admin desse servidor: use o login de um deles.") +
               "\n\nSe o usuário foi criado agora, espere a sincronização (uns 30 segundos) e tente de novo.";
    }

    /// <summary>
    /// Se o operador já tem a permissão (ou, sem permissão informada, é gerente/admin), autoriza direto;
    /// senão pede o login e a senha de quem tem.
    /// </summary>
    public static bool Pedir(IWin32Window dono, UsuarioLocal operador, string motivo, string? permissao = null)
    {
        if (permissao is null ? operador.PodeAutorizar : operador.Pode(permissao)) return true;
        using var f = new AutorizacaoForm(motivo, permissao: permissao);
        return f.ShowDialog(dono) == DialogResult.OK;
    }
}

/// <summary>Pesquisa com filtro por texto (produtos, clientes).</summary>
public class PesquisaForm<T> : Form where T : class
{
    private readonly TextBox _busca = Ui.Caixa(13);
    private readonly DataGridView _grade = Ui.Grade();
    private readonly Func<string, List<T>> _pesquisar;
    private readonly (string titulo, Func<T, string> valor, float peso, bool direita)[] _colunas;

    public T? Selecionado { get; private set; }

    public PesquisaForm(string titulo, string textoInicial, Func<string, List<T>> pesquisar,
        params (string titulo, Func<T, string> valor, float peso, bool direita)[] colunas)
    {
        _pesquisar = pesquisar;
        _colunas = colunas;
        Ui.Dialogo(this, titulo, 760, 500);

        var dica = Ui.Rotulo("Digite para filtrar — Enter seleciona — Esc fecha", 9);
        dica.ForeColor = Ui.Neutro;
        dica.Location = new Point(16, 12);
        _busca.SetBounds(16, 36, 728, 34);
        _busca.Text = textoInicial;
        _grade.SetBounds(16, 80, 728, 404);
        foreach (var c in colunas) _grade.Columns.Add(Ui.Coluna(c.titulo, c.peso, c.direita));

        Controls.AddRange([dica, _busca, _grade]);

        _busca.TextChanged += (_, _) => Carregar();
        _busca.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Down && _grade.Rows.Count > 0) { _grade.Focus(); e.Handled = true; }
            if (e.KeyCode == Keys.Enter) { Selecionar(); e.SuppressKeyPress = true; }
        };
        _grade.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { Selecionar(); e.SuppressKeyPress = true; }
        };
        _grade.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Selecionar(); };
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) DialogResult = DialogResult.Cancel; };

        Shown += (_, _) => { Carregar(); _busca.Focus(); _busca.SelectionStart = _busca.TextLength; };
    }

    private void Carregar()
    {
        _grade.Rows.Clear();
        foreach (var item in _pesquisar(_busca.Text))
        {
            var i = _grade.Rows.Add(_colunas.Select(c => (object)c.valor(item)).ToArray());
            _grade.Rows[i].Tag = item;
        }
    }

    private void Selecionar()
    {
        var linha = _grade.CurrentRow ?? (_grade.Rows.Count > 0 ? _grade.Rows[0] : null);
        if (linha?.Tag is not T item) return;
        Selecionado = item;
        DialogResult = DialogResult.OK;
    }
}

/// <summary>Sangria (retirada) ou suprimento (reforço de troco).</summary>
public class MovimentoCaixaForm : Form
{
    private readonly RadioButton _sangria = new() { Text = "Sangria (retirada)", Checked = true, AutoSize = true };
    private readonly RadioButton _suprimento = new() { Text = "Suprimento (reforço)", AutoSize = true };
    private readonly CampoMoeda _valor = new() { Font = Ui.Fonte(16) };
    private readonly TextBox _motivo = Ui.Caixa(11);

    public TipoMovimentoCaixa Tipo => _sangria.Checked ? TipoMovimentoCaixa.Sangria : TipoMovimentoCaixa.Suprimento;
    public decimal Valor { get; private set; }
    public string Motivo => _motivo.Text.Trim();

    /// <param name="suprimento">Abre com "Suprimento" marcado (botão Suprimento da tela de venda).</param>
    public MovimentoCaixaForm(bool suprimento = false)
    {
        Ui.Dialogo(this, "Sangria / Suprimento", 380, 300);
        _suprimento.Checked = suprimento;
        _sangria.Checked = !suprimento;
        _sangria.Location = new Point(24, 18);
        _suprimento.Location = new Point(200, 18);

        var lv = Ui.Rotulo("Valor");
        lv.Location = new Point(24, 56);
        _valor.SetBounds(24, 80, 330, 36);

        var lm = Ui.Rotulo("Motivo");
        lm.Location = new Point(24, 126);
        _motivo.SetBounds(24, 150, 330, 30);
        _motivo.MaxLength = 100;

        var ok = Ui.Botao("Confirmar", largura: 160);
        ok.Location = new Point(24, 220);
        ok.Click += (_, _) =>
        {
            if (_valor.Valor <= 0)
            {
                Ui.Aviso(this, "Informe um valor maior que zero.");
                _valor.Focus();
                return;
            }
            Valor = _valor.Valor;
            DialogResult = DialogResult.OK;
        };
        var cancelar = Ui.Botao("Cancelar", Ui.Neutro, 160);
        cancelar.Location = new Point(194, 220);
        cancelar.DialogResult = DialogResult.Cancel;

        Controls.AddRange([_sangria, _suprimento, lv, _valor, lm, _motivo, ok, cancelar]);
        AcceptButton = ok;
        CancelButton = cancelar;
        Shown += (_, _) => _valor.Focus();
    }
}
