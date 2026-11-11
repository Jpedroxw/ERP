using System.Net;
using System.Net.Http.Json;
using ProjetoJP.Controles;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;

namespace ProjetoJP.Pdv.Telas;

/// <summary>Senha técnica por empresa validada exclusivamente na API, sem conceder direitos de gerente.</summary>
public sealed class SuporteClienteForm : Form
{
    private readonly CampoLogin _login = new() { Text = "suporte", Font = Tema.Fonte(11) };
    private readonly TextBox _senha = new() { UseSystemPasswordChar = true, MaxLength = 128, Font = Tema.Fonte(11) };
    private readonly BotaoTema _entrar = new() { Text = "Autorizar", Size = new Size(130, 39) };

    public SuporteClienteForm()
    {
        Ui.Dialogo(this, "Acesso técnico", 420, 286);
        Controls.Add(new Label { Text = "Suporte técnico da empresa", Font = Tema.Semibold(14),
            ForeColor = Tema.Texto, Location = new Point(24, 19), Size = new Size(365, 32) });
        Controls.Add(new Label { Text = "Este acesso exige conexão com o servidor da empresa.", Font = Tema.Fonte(9),
            ForeColor = Tema.TextoSuave, Location = new Point(24, 54), Size = new Size(372, 34) });
        Controls.Add(new Label { Text = "Usuário técnico", Font = Tema.Fonte(9), Location = new Point(24, 95), Size = new Size(350, 20) });
        _login.SetBounds(24, 117, 372, 31);
        Controls.Add(new Label { Text = "Senha", Font = Tema.Fonte(9), Location = new Point(24, 156), Size = new Size(350, 20) });
        _senha.SetBounds(24, 177, 372, 31);
        var cancelar = new BotaoTema { Text = "Cancelar", Estilo = EstiloBotao.Secundario,
            Size = new Size(105, 39), Location = new Point(154, 231), DialogResult = DialogResult.Cancel };
        _entrar.Location = new Point(266, 231);
        _entrar.Click += async (_, _) => await Autorizar();
        Controls.AddRange([_login, _senha, cancelar, _entrar]);
        AcceptButton = _entrar;
        CancelButton = cancelar;
        Shown += (_, _) => _senha.Focus();
    }

    private async Task Autorizar()
    {
        var cfg = PdvConfig.Carregar();
        if (!cfg.Configurado)
        {
            Ui.Aviso(this, "Este caixa ainda não tem Servidor e chave do terminal válidos (ou o nome do Servidor não é " +
                           "conhecido neste computador).\n\n" + ComoCorrigir);
            return;
        }
        if (_login.Text.Trim().Length == 0 || _senha.Text.Length == 0)
        {
            Ui.Aviso(this, "Informe o usuário e a senha do suporte.");
            return;
        }
        if (!Uri.TryCreate(cfg.ApiUrl.TrimEnd('/') + "/", UriKind.Absolute, out var endereco))
        {
            Ui.Aviso(this, "O Servidor configurado neste caixa não é válido.\n\n" + ComoCorrigir);
            return;
        }
        _entrar.Enabled = false;
        try
        {
            using var http = new HttpClient { BaseAddress = endereco, Timeout = TimeSpan.FromSeconds(12) };
            http.DefaultRequestHeaders.Add("X-Terminal-Key", cfg.TerminalKey);
            using var resposta = await http.PostAsJsonAsync("api/suporte/verificar", new SuporteTecnicoValidarRequest
            {
                Login = _login.Text.Trim(), Senha = _senha.Text
            });
            if (resposta.IsSuccessStatusCode) { DialogResult = DialogResult.OK; return; }
            var erro = await ErroDoServidor(resposta);
            var mensagem = resposta.StatusCode switch
            {
                // A API respondeu "não encontrado" com motivo: é o nome do Servidor deste caixa que não existe lá
                // (a sincronização recebe o mesmo erro e o caixa fica Offline). Sem motivo: a API é antiga, sem esta rota.
                HttpStatusCode.NotFound when erro is not null =>
                    "O Servidor configurado neste caixa não foi encontrado (o caixa também fica Offline por isso).\n\n" +
                    "O nome em Opções do caixa → Servidor não existe no servidor da empresa: confira se é o mesmo da " +
                    "Gestão (ex.: nuvem, ou o código da empresa no Projeto JP Master).\n\n" + ComoCorrigir,
                HttpStatusCode.NotFound =>
                    "O servidor ainda não oferece o acesso técnico. Atualize a API no servidor.",
                HttpStatusCode.Forbidden => erro ?? "Acesso suspenso. Fale com o suporte.",
                HttpStatusCode.TooManyRequests => "Muitas tentativas. Tente novamente mais tarde.",
                HttpStatusCode.Unauthorized =>
                    "Acesso técnico não autorizado. Confira o usuário e a senha do suporte.\n\n" +
                    "Se estiverem certos, a chave deste terminal pode não ser deste servidor (confira em Opções do caixa).",
                _ => $"O servidor respondeu com erro {(int)resposta.StatusCode}" + (erro is null ? "." : $": {erro}")
            };
            Ui.Aviso(this, mensagem);
        }
        catch (HttpRequestException ex)
        {
            Ui.Aviso(this, $"Não foi possível falar com o servidor ({EnderecoServidor.ErroConexao(ex)}).\n\n" +
                           "O acesso técnico da empresa precisa de internet. " + ComoCorrigir);
        }
        catch (TaskCanceledException)
        {
            Ui.Aviso(this, "O servidor não respondeu a tempo.\n\n" + ComoCorrigir);
        }
        finally
        {
            if (!IsDisposed)
            {
                _entrar.Enabled = true;
                _senha.Clear();
            }
        }
    }

    private const string ComoCorrigir =
        "Para entrar nas Opções e corrigir o Servidor sem depender dele, cancele e use " +
        "\"Senha técnica deste computador (sem internet)\".";

    /// <summary>Mensagem que o servidor manda junto com a recusa (ex.: caixa no servidor padrão).</summary>
    private static async Task<string?> ErroDoServidor(HttpResponseMessage resposta)
    {
        try
        {
            var json = await resposta.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            return json.TryGetProperty("erro", out var e) ? e.GetString() : null;
        }
        catch (Exception) { return null; }
    }
}
