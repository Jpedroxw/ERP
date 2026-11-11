using ProjetoJP.Controles;
using ProjetoJP.Gestao.Servicos;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;

namespace ProjetoJP.Gestao.Telas;

/// <summary>
/// Cria um usuário para entrar no caixa. É o mesmo cadastro de Usuários, só que mais direto:
/// nome, login, senha (duas vezes) e o que a pessoa pode fazer no caixa.
/// O caixa recebe o usuário na próxima sincronização.
/// </summary>
public class OperadorForm : Form
{
    private readonly ApiClient _api;
    private readonly TextBox _nome = new() { Font = Tema.Fonte(11), MaxLength = 120 };
    private readonly CampoLogin _login = new() { Font = Tema.Fonte(11) };
    private readonly TextBox _senha = new() { Font = Tema.Fonte(11), UseSystemPasswordChar = true, MaxLength = 64 };
    private readonly TextBox _confirmar = new() { Font = Tema.Fonte(11), UseSystemPasswordChar = true, MaxLength = 64 };
    private readonly RadioButton _operador = new() { Text = "Operador de caixa", Checked = true, AutoSize = true, Font = Tema.Semibold(10) };
    private readonly RadioButton _gerente = new() { Text = "Gerente", AutoSize = true, Font = Tema.Semibold(10) };
    private readonly BotaoTema _salvar = new() { Text = "Criar usuário", Icone = Icones.Salvar, Size = new Size(160, 40) };
    private bool _loginEditado;

    public UsuarioDto? Criado { get; private set; }
    /// <summary>Já foi enviado aos caixas junto com a criação.</summary>
    public bool Enviado { get; private set; }

    public OperadorForm(ApiClient api)
    {
        _api = api;
        Ui.Dialogo(this, "Novo usuário do caixa", 520, 560);

        // Faixa de título, como os cabeçalhos do caixa
        var topo = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = Tema.Barra };
        topo.Controls.Add(new Label { Text = Icones.Pessoa, Font = Tema.FonteIcone(18), ForeColor = Color.White, AutoSize = true, Location = new Point(22, 24) });
        topo.Controls.Add(new Label { Text = "Novo usuário do caixa", Font = Tema.Semibold(14), ForeColor = Color.White, AutoSize = true, Location = new Point(56, 12) });
        topo.Controls.Add(new Label { Text = "Ele entra no caixa com este login e senha.", Font = Tema.Fonte(9), ForeColor = Tema.BarraTextoSuave, AutoSize = true, Location = new Point(58, 40) });

        var y = 92;
        Label Rotulo(string texto, string? dica = null)
        {
            var l = Ui.Rotulo(texto, 9.5f);
            l.ForeColor = Tema.TextoSuave;
            l.Location = new Point(28, y);
            Controls.Add(l);
            if (dica is not null)
            {
                var d = new Label { Text = dica, Font = Tema.Fonte(8.5f), ForeColor = Tema.TextoSuave, AutoSize = true };
                d.Location = new Point(28 + l.PreferredWidth + 6, y + 1);
                Controls.Add(d);
            }
            y += 22;
            return l;
        }

        Rotulo("Nome *");
        _nome.SetBounds(28, y, 464, 30); y += 44;

        Rotulo("Login *", "(minúsculas, números, ponto ou hífen)");
        _login.SetBounds(28, y, 464, 30); y += 44;

        Rotulo("Senha *", "(de 4 a 64 caracteres)");
        _senha.SetBounds(28, y, 226, 30);
        var lConf = Ui.Rotulo("Repita a senha *", 9.5f);
        lConf.ForeColor = Tema.TextoSuave;
        lConf.Location = new Point(266, y - 22);
        Controls.Add(lConf);
        _confirmar.SetBounds(266, y, 226, 30); y += 48;

        Rotulo("O que ele pode fazer no caixa");
        _operador.Location = new Point(28, y);
        var dOp = new Label { Text = "Vende, recebe e fecha o próprio caixa.", Font = Tema.Fonte(9), ForeColor = Tema.TextoSuave, AutoSize = true, Location = new Point(46, y + 22) };
        y += 50;
        _gerente.Location = new Point(28, y);
        var dGe = new Label
        {
            Text = "Tudo do operador, e também autoriza cancelamentos, descontos e\nsangrias e abre as Opções do caixa. Na Gestão, vê os relatórios.",
            Font = Tema.Fonte(9), ForeColor = Tema.TextoSuave, AutoSize = true, Location = new Point(46, y + 22)
        };

        // Rodapé com os botões
        var rodape = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Tema.Fundo };
        rodape.Paint += (_, e) =>
        {
            using var linha = new Pen(Tema.Borda);
            e.Graphics.DrawLine(linha, 0, 0, rodape.Width, 0);
        };
        var cancelar = new BotaoTema { Text = "Cancelar", Estilo = EstiloBotao.Secundario, Size = new Size(110, 40), DialogResult = DialogResult.Cancel };
        _salvar.Location = new Point(520 - 28 - _salvar.Width, 12);
        cancelar.Location = new Point(_salvar.Left - 10 - cancelar.Width, 12);
        _salvar.Click += async (_, _) => await Salvar();
        rodape.Controls.AddRange([cancelar, _salvar]);

        Controls.AddRange([_nome, _login, _senha, _confirmar, _operador, dOp, _gerente, dGe]);
        Controls.Add(rodape);
        Controls.Add(topo);
        AcceptButton = _salvar;
        CancelButton = cancelar;

        // Sugere o login a partir do primeiro nome (maria silva -> maria), até a pessoa mexer no login.
        _nome.TextChanged += (_, _) =>
        {
            if (_loginEditado) return;
            var primeiro = _nome.Text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            var sugestao = new string(Formatos.RemoverAcentos(primeiro).ToLowerInvariant()
                .Where(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c)).ToArray());
            _login.Text = sugestao;
        };
        _login.KeyPress += (_, _) => _loginEditado = true;
        Shown += (_, _) => _nome.Focus();
    }

    private async Task Salvar()
    {
        var nome = _nome.Text.Trim();
        var login = _login.Text.Trim().ToLowerInvariant();

        if (nome.Length == 0) { Aviso("Informe o nome.", _nome); return; }
        if (!Formatos.LoginValido(login)) { Aviso("Login inválido: de 3 a 60 caracteres, só letras minúsculas, números, ponto, hífen ou sublinhado.", _login); return; }
        if (_senha.Text.Length is < 4 or > 64) { Aviso("A senha precisa ter de 4 a 64 caracteres.", _senha); return; }
        if (_senha.Text != _confirmar.Text) { Aviso("As duas senhas não são iguais.", _confirmar); return; }

        var dto = new UsuarioDto
        {
            Nome = nome,
            Login = login,
            Senha = _senha.Text,
            Perfil = _gerente.Checked ? Perfil.Gerente : Perfil.Operador,
            Ativo = true
        };

        _salvar.Enabled = false;
        try
        {
            Criado = await _api.PostAsync<UsuarioDto>("api/usuarios", dto);
            // "Novo usuário do caixa" existe para entrar no caixa: já envia aos caixas.
            try
            {
                await _api.PostAsync<PublicarProdutosResposta>("api/cargas/usuarios/publicar",
                    new PublicarProdutosRequest { Ids = [Criado.Id] });
                Enviado = true;
            }
            catch (ApiException) { Enviado = false; }
            DialogResult = DialogResult.OK;
        }
        catch (ApiException ex)
        {
            Ui.Aviso(this, ex.Message);
        }
        finally
        {
            _salvar.Enabled = true;
        }
    }

    private void Aviso(string msg, Control campo)
    {
        Ui.Aviso(this, msg);
        campo.Focus();
        if (campo is TextBox t) t.SelectAll();
    }

    /// <summary>Abre o formulário e, se criou, explica como o usuário chega ao caixa.</summary>
    public static bool Abrir(IWin32Window? dono, ApiClient api)
    {
        using var f = new OperadorForm(api);
        if (f.ShowDialog(dono) != DialogResult.OK || f.Criado is null) return false;

        Ui.Info(dono,
            $"Usuário \"{f.Criado.Nome}\" criado.\n\n" +
            $"Login: {f.Criado.Login}\n\n" +
            (f.Enviado
                ? "O caixa recebe esse usuário na próxima sincronização, que é automática " +
                  "(em até meio minuto, com o caixa ligado e com internet).\n" +
                  "Depois é só entrar no caixa com esse login e a senha cadastrada."
                : "Ele ainda não foi enviado aos caixas: envie em Caixas → Enviar usuários aos caixas."));
        return true;
    }
}
