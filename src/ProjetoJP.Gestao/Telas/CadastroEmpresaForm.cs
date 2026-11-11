using ProjetoJP.Controles;
using ProjetoJP.Gestao.Servicos;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;

namespace ProjetoJP.Gestao.Telas;

/// <summary>
/// "Criar cadastro da empresa": a própria loja pede acesso. O pedido vai para o ERP Master;
/// quando for aprovado, a Retaguarda se configura sozinha (veja LoginForm.ConferirPedido).
/// </summary>
public class CadastroEmpresaForm : Form
{
    private readonly CampoTextoSeguro _empresa = new() { Font = Tema.Fonte(10.5f), MaxLength = 120 };
    private readonly Control _documento = Campos.Criar(TipoCampo.CpfCnpj);
    private readonly CampoTextoSeguro _responsavel = new() { Font = Tema.Fonte(10.5f), MaxLength = 120 };
    private readonly Control _telefone = Campos.Criar(TipoCampo.Telefone);
    private readonly Control _email = Campos.Criar(TipoCampo.Email, 120);
    private readonly CampoTextoSeguro _cidade = new() { Font = Tema.Fonte(10.5f), MaxLength = 80 };
    private readonly ComboBox _uf = new() { Font = Tema.Fonte(10.5f), DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CampoNomeServidor _codigo = new() { Font = Tema.Fonte(10.5f) };
    private readonly Label _situacaoCodigo = new() { AutoSize = true, Font = Tema.Fonte(9) };
    private readonly CampoLogin _login = new() { Font = Tema.Fonte(10.5f), Text = "admin" };
    private readonly TextBox _senha = new() { Font = Tema.Fonte(10.5f), UseSystemPasswordChar = true, MaxLength = 64 };
    private readonly TextBox _repetir = new() { Font = Tema.Fonte(10.5f), UseSystemPasswordChar = true, MaxLength = 64 };
    private readonly BotaoTema _enviar = new() { Text = "Enviar cadastro", Icone = Icones.Ok, Size = new Size(170, 40) };
    private bool _codigoEditado;
    private CancellationTokenSource? _verificacao;

    public PedidoCadastroResposta? Resposta { get; private set; }

    public CadastroEmpresaForm()
    {
        Ui.Dialogo(this, "Criar cadastro da empresa", 640, 640);

        var topo = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = Tema.Barra };
        topo.Controls.Add(new Label { Text = Icones.Loja, Font = Tema.FonteIcone(18), ForeColor = Color.White, AutoSize = true, Location = new Point(22, 24) });
        topo.Controls.Add(new Label { Text = "Criar cadastro da empresa", Font = Tema.Semibold(14), ForeColor = Color.White, AutoSize = true, Location = new Point(56, 12) });
        topo.Controls.Add(new Label { Text = "Depois de aprovado, a Gestão se configura sozinha.", Font = Tema.Fonte(9), ForeColor = Tema.BarraTextoSuave, AutoSize = true, Location = new Point(58, 40) });

        foreach (var c in new[] { _documento, _telefone, _email }) c.Font = Tema.Fonte(10.5f);

        const int x1 = 28, x2 = 328, w = 284;
        var y = 88;
        void Rotulo(string texto, int x, int top)
        {
            var l = Ui.Rotulo(texto, 9.5f);
            l.ForeColor = Tema.TextoSuave;
            l.Location = new Point(x, top);
            Controls.Add(l);
        }
        void Secao(string texto)
        {
            Controls.Add(new Label { Text = texto, AutoSize = true, Font = Tema.Semibold(10.5f), ForeColor = Tema.Texto, Location = new Point(x1, y) });
            y += 28;
        }

        Secao("Empresa");
        Rotulo("Nome da empresa *", x1, y); Rotulo("CPF ou CNPJ", x2, y);
        _empresa.SetBounds(x1, y + 20, w, 30); _documento.SetBounds(x2, y + 20, w, 30);
        y += 60;
        Rotulo("Responsável *", x1, y); Rotulo("Telefone / WhatsApp *", x2, y);
        _responsavel.SetBounds(x1, y + 20, w, 30); _telefone.SetBounds(x2, y + 20, w, 30);
        y += 60;
        Rotulo("E-mail", x1, y); Rotulo("Cidade", x2, y); Rotulo("UF", x2 + 202, y);
        _email.SetBounds(x1, y + 20, w, 30); _cidade.SetBounds(x2, y + 20, 190, 30); _uf.SetBounds(x2 + 202, y + 20, w - 202, 30);
        _uf.Items.Add("");
        foreach (var u in Formatos.Ufs) _uf.Items.Add(u);
        _uf.SelectedIndex = 0;
        y += 70;

        Secao("Acesso");
        Rotulo("Nome do servidor * (o que se digita nos caixas)", x1, y);
        _codigo.SetBounds(x1, y + 20, w, 30);
        _situacaoCodigo.Location = new Point(x2, y + 26);
        y += 60;
        Rotulo("Login do administrador *", x1, y);
        _login.SetBounds(x1, y + 20, w, 30);
        y += 60;
        Rotulo("Senha * (mínimo 6)", x1, y); Rotulo("Repita a senha *", x2, y);
        _senha.SetBounds(x1, y + 20, w, 30); _repetir.SetBounds(x2, y + 20, w, 30);

        var rodape = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Tema.Fundo };
        rodape.Paint += (_, e) =>
        {
            using var linha = new Pen(Tema.Borda);
            e.Graphics.DrawLine(linha, 0, 0, rodape.Width, 0);
        };
        var cancelar = new BotaoTema { Text = "Cancelar", Estilo = EstiloBotao.Secundario, Size = new Size(110, 40), DialogResult = DialogResult.Cancel };
        _enviar.Location = new Point(640 - 28 - _enviar.Width, 12);
        cancelar.Location = new Point(_enviar.Left - 10 - cancelar.Width, 12);
        _enviar.Click += async (_, _) => await Enviar();
        rodape.Controls.AddRange([cancelar, _enviar]);

        Controls.AddRange([_empresa, _documento, _responsavel, _telefone, _email, _cidade, _uf, _codigo, _situacaoCodigo, _login, _senha, _repetir]);
        Controls.Add(rodape);
        Controls.Add(topo);
        AcceptButton = _enviar;
        CancelButton = cancelar;

        // Sugere o nome do servidor a partir do nome da empresa, até a pessoa mexer nele.
        _empresa.TextChanged += (_, _) =>
        {
            if (_codigoEditado) return;
            _codigo.Codigo = CodigoCliente.Sugerir(_empresa.Text);
        };
        _codigo.KeyPress += (_, e) =>
        {
            _codigoEditado = true;
            var c = char.ToLowerInvariant(e.KeyChar);
            if (!char.IsControl(c) && !(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-')) e.Handled = true;
        };
        _codigo.TextChanged += async (_, _) => await VerificarCodigo();
        FormClosed += (_, _) => _verificacao?.Cancel();
        Shown += (_, _) => _empresa.Focus();
    }

    /// <summary>Confere no servidor se o nome está livre (meio segundo depois de parar de digitar).</summary>
    private async Task VerificarCodigo()
    {
        _verificacao?.Cancel();
        var cts = _verificacao = new CancellationTokenSource();
        var codigo = _codigo.Codigo;
        if (codigo.Length == 0) { _situacaoCodigo.Text = ""; return; }
        if (!CodigoCliente.Valido(codigo))
        {
            Situacao(false, "Letras ou números (pode ter espaço); começa por letra.");
            return;
        }
        Situacao(null, "Conferindo...");
        try
        {
            await Task.Delay(500, cts.Token);
            var r = await CadastroEmpresa.DisponivelAsync(codigo);
            if (!cts.IsCancellationRequested && !IsDisposed) Situacao(r.Disponivel, r.Disponivel ? "Disponível" : r.Mensagem ?? "Indisponível");
        }
        catch (TaskCanceledException) { }
        catch (ApiException ex)
        {
            if (!IsDisposed) Situacao(null, ex.Message);
        }
    }

    private void Situacao(bool? ok, string texto)
    {
        _situacaoCodigo.Text = texto;
        _situacaoCodigo.Font = ok is null ? Tema.Fonte(9) : Tema.Semibold(9);
        _situacaoCodigo.ForeColor = ok switch { true => Tema.Sucesso, false => Tema.Perigo, _ => Tema.TextoSuave };
        _situacaoCodigo.MaximumSize = new Size(290, 0);
    }

    private async Task Enviar()
    {
        string Texto(Control c) => c.Text.Trim();
        if (Texto(_empresa).Length == 0) { Aviso("Informe o nome da empresa.", _empresa); return; }
        if (Texto(_documento).Length > 0 && !Formatos.DocumentoValido(Formatos.LimparDocumento(Texto(_documento)))) { Aviso("CPF/CNPJ inválido.", _documento); return; }
        if (Texto(_responsavel).Length == 0) { Aviso("Informe o nome do responsável.", _responsavel); return; }
        if (!Formatos.TelefoneValido(Texto(_telefone))) { Aviso("Telefone inválido: informe o DDD e o número.", _telefone); return; }
        if (Texto(_email).Length > 0 && !Formatos.EmailValido(Texto(_email))) { Aviso("E-mail inválido.", _email); return; }
        if (!CodigoCliente.Valido(_codigo.Codigo)) { Aviso("Nome do servidor inválido: de 2 a 30 letras ou números (pode ter espaço), começando por letra.", _codigo); return; }
        if (!Formatos.LoginValido(Texto(_login))) { Aviso("Login inválido: de 3 a 60 caracteres, letras minúsculas, números, ponto ou hífen.", _login); return; }
        if (_senha.Text.Length < 6) { Aviso("A senha precisa ter pelo menos 6 caracteres.", _senha); return; }
        if (_senha.Text != _repetir.Text) { Aviso("As duas senhas não são iguais.", _repetir); return; }

        _enviar.Enabled = false;
        try
        {
            Resposta = await CadastroEmpresa.EnviarAsync(new PedidoCadastroRequest
            {
                Empresa = Texto(_empresa),
                Documento = Texto(_documento).Length > 0 ? Texto(_documento) : null,
                Responsavel = Texto(_responsavel),
                Telefone = Texto(_telefone),
                Email = Texto(_email).Length > 0 ? Texto(_email) : null,
                Cidade = Texto(_cidade).Length > 0 ? Texto(_cidade) : null,
                Uf = _uf.SelectedItem as string is { Length: 2 } uf ? uf : null,
                CodigoDesejado = _codigo.Codigo,
                AdminLogin = Texto(_login),
                AdminSenha = _senha.Text
            });

            var cfg = RetaguardaConfig.Carregar();
            cfg.PedidoId = Resposta.Id;
            cfg.PedidoToken = Resposta.Token;
            cfg.Salvar();

            Ui.Info(this, "Cadastro enviado!\n\n" +
                          "Assim que for aprovado, esta Gestão se configura sozinha: é só abrir de novo e entrar com " +
                          $"o login \"{Texto(_login)}\" e a senha que você escolheu.\n\n" +
                          $"Nos caixas, o nome do servidor será: {CodigoCliente.ParaTela(_codigo.Codigo)}");
            DialogResult = DialogResult.OK;
        }
        catch (ApiException ex)
        {
            Ui.Aviso(this, ex.Message);
        }
        finally
        {
            _enviar.Enabled = true;
        }
    }

    private void Aviso(string msg, Control campo)
    {
        Ui.Aviso(this, msg);
        campo.Focus();
        if (campo is TextBox t) t.SelectAll();
    }
}
