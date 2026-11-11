using ProjetoJP.Controles;
using ProjetoJP.Gestao.Servicos;
using ProjetoJP.Shared;

namespace ProjetoJP.Gestao.Telas;

/// <summary>Servidor: a pessoa digita só o NOME (ex.: nuvem); o site fica dentro do programa e não aparece.</summary>
public class ServidorForm : Form
{
    private readonly TextBox _url = new() { Font = Ui.Fonte(11) };
    private readonly Label _resultado = Ui.Rotulo("", 9.5f);
    private readonly Button _testar = Ui.Botao("Testar", Ui.Escuro, 110, 32);

    public string ApiUrl { get; private set; } = "";

    /// <summary>Servidor salvo (nome, ou endereço de versões antigas — este fica escondido).</summary>
    private readonly string _atual;
    private bool _digitando;

    // Nome digitado em maiúsculas com espaço; gravado como sempre (minúsculas com hífen: "MERCADO BOM PRECO" → mercado-bom-preco).
    private string ServidorEscolhido => !_digitando ? _atual
        : EnderecoServidor.PareceEndereco(_url.Text) ? _url.Text.Trim() : EnderecoServidor.NormalizarNome(_url.Text);

    /// <summary>O que vale agora: o endereço salvo ou o que a pessoa acabou de digitar.</summary>
    private string UrlEscolhida => RetaguardaConfig.Normalizar(ServidorEscolhido);

    public ServidorForm()
    {
        Ui.Dialogo(this, "Servidor do " + Aparencia.NomePadrao, 520, 230);
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;

        var l = Ui.Rotulo("Servidor", 10, FontStyle.Bold);
        l.Location = new Point(24, 20);
        var dica = Ui.Rotulo($"Digite o nome do servidor (ex.: {EnderecoServidor.ParaTela(EnderecoServidor.NomePadrao)}).", 9);
        dica.ForeColor = Ui.Neutro;
        dica.Location = new Point(24, 44);

        _url.SetBounds(24, 70, 350, 30);
        _atual = RetaguardaConfig.Carregar().Servidor;
        // Nome (nuvem) pode aparecer; endereço de site, não.
        _digitando = _atual.Length == 0 || !EnderecoServidor.PareceEndereco(_atual);
        _url.Text = EnderecoServidor.ParaTela(_atual.Length == 0 ? EnderecoServidor.NomePadrao : _digitando ? _atual : "");
        _url.KeyPress += (_, e) => { if (!_url.ReadOnly) e.KeyChar = char.ToUpperInvariant(e.KeyChar); }; // nome em maiúsculas
        if (!_digitando)
        {
            // Já configurado: não mostra o endereço. Ao clicar no campo, começa um endereço novo.
            _url.Text = EnderecoServidor.TextoConfigurado;
            _url.ForeColor = Ui.Neutro;
            _url.ReadOnly = true;
            _url.Cursor = Cursors.IBeam;
            _url.Click += (_, _) => ComecarNovo();
            _url.KeyPress += (_, _) => ComecarNovo();
            new ToolTip().SetToolTip(_url, "Clique para digitar outro servidor");
        }
        _testar.Location = new Point(386, 68);
        _testar.Click += async (_, _) => await TestarAsync();

        _resultado.Location = new Point(24, 112);
        _resultado.MaximumSize = new Size(470, 0);

        var salvar = Ui.Botao("Salvar", largura: 140, altura: 38);
        salvar.Location = new Point(24, 166);
        salvar.Click += async (_, _) => await SalvarAsync();
        var cancelar = Ui.Botao("Cancelar", Ui.Neutro, 140, 38);
        cancelar.Location = new Point(174, 166);
        cancelar.DialogResult = DialogResult.Cancel;

        // Só o suporte: cadastra/troca os servidores (nome e site) com a senha técnica.
        var tecnica = new LinkLabel
        {
            Text = "Configuração técnica", AutoSize = true, Location = new Point(334, 178), Font = Tema.Fonte(9),
            LinkColor = Ui.Neutro, ActiveLinkColor = Tema.Primaria, LinkBehavior = LinkBehavior.HoverUnderline
        };
        tecnica.LinkClicked += (_, _) =>
        {
            if (!ConfigTecnica.Abrir(this)) return;
            if (_digitando && !EnderecoServidor.EhNome(_url.Text)) _url.Text = EnderecoServidor.ParaTela(EnderecoServidor.NomePadrao);
            Mostrar(null, "Lista de servidores atualizada.");
        };

        Controls.AddRange([l, dica, _url, _testar, _resultado, salvar, cancelar, tecnica]);
        AcceptButton = salvar;
        CancelButton = cancelar;
    }

    private void ComecarNovo()
    {
        if (_digitando) return;
        _digitando = true;
        _url.ReadOnly = false;
        _url.Text = "";
        _url.ForeColor = Tema.Texto;
        _url.Focus();
    }

    private void Mostrar(bool? ok, string texto)
    {
        _resultado.Text = texto;
        _resultado.ForeColor = ok switch { true => Ui.Sucesso, false => Ui.Perigo, _ => Ui.Neutro };
    }

    /// <summary>Chama /health. Devolve true se o servidor respondeu.</summary>
    private async Task<bool> TestarAsync()
    {
        if (_digitando && EnderecoServidor.PareceEndereco(_url.Text))
        {
            Mostrar(false, "Digite o NOME do servidor, não o site. Sites são cadastrados na Configuração técnica.");
            return false;
        }
        var url = UrlEscolhida;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            Mostrar(false, $"Servidor \"{EnderecoServidor.ParaTela(ServidorEscolhido)}\" não encontrado. Confira o nome.");
            return false;
        }

        _testar.Enabled = false;
        Mostrar(null, "Testando...");
        try
        {
            using var http = new HttpClient { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(15) };
            var resp = await http.GetAsync("health");
            if (resp.IsSuccessStatusCode)
            {
                Mostrar(true, "Servidor respondendo.");
                return true;
            }
            Mostrar(false, (int)resp.StatusCode switch
            {
                404 => $"Servidor \"{EnderecoServidor.ParaTela(ServidorEscolhido)}\" não encontrado. Confira o nome.",
                403 => "Acesso suspenso. Fale com o suporte.",
                var c => $"O servidor respondeu com erro {c}."
            });
            return false;
        }
        catch (TaskCanceledException)
        {
            Mostrar(false, "O servidor não respondeu a tempo.");
            return false;
        }
        catch (HttpRequestException ex)
        {
            Mostrar(false, "Não foi possível conectar: " + EnderecoServidor.ErroConexao(ex) + ".");
            return false;
        }
        finally
        {
            _testar.Enabled = true;
        }
    }

    private async Task SalvarAsync()
    {
        if (!await TestarAsync() &&
            !Ui.Confirmar(this, "O servidor não respondeu. Salvar mesmo assim?"))
            return;

        var cfg = RetaguardaConfig.Carregar(); // mantém as outras preferências (modo da tela)
        if (UrlEscolhida.Length == 0 || (_digitando && EnderecoServidor.PareceEndereco(_url.Text))) return;
        cfg.Servidor = ServidorEscolhido;
        try
        {
            cfg.Salvar();
        }
        catch (Exception ex)
        {
            Ui.Erro(this, "Não foi possível salvar: " + ex.Message);
            return;
        }
        ApiUrl = cfg.ApiUrl;
        DialogResult = DialogResult.OK;
    }
}
