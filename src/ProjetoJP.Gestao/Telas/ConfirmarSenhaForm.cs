using ProjetoJP.Controles;
using ProjetoJP.Gestao.Servicos;

namespace ProjetoJP.Gestao.Telas;

/// <summary>
/// Pede a senha de novo antes de alterar usuários ou terminais (vale 10 minutos). Abre sozinha quando o servidor
/// pede (veja ApiClient.ConfirmarSenha) e também pelo menu Configurações → Confirmar senha.
/// Montada com painel de layout: não corta em escala 125%/150%.
/// </summary>
public sealed class ConfirmarSenhaForm : Form
{
    private readonly ApiClient _api;
    private readonly TextBox _senha = new() { UseSystemPasswordChar = true, MaxLength = 64, Font = Tema.Fonte(12), Width = 360 };
    private readonly BotaoTema _confirmar = Ui.Botao("Confirmar", largura: 140, altura: 38, icone: Icones.Cadeado);

    public ConfirmarSenhaForm(ApiClient api)
    {
        _api = api;
        Ui.Dialogo(this, "Confirmar senha", 420, 240);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var tabela = new TableLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1,
            Padding = new Padding(24, 18, 24, 16), BackColor = Tema.Superficie
        };
        var titulo = Ui.Rotulo("Confirme a sua senha", 13, FontStyle.Bold);
        var texto = Ui.Rotulo($"Para alterar usuários ou terminais, digite a senha de {api.Sessao?.Nome ?? "quem está conectado"}. " +
                              "A confirmação vale por 10 minutos.", 9.5f);
        texto.ForeColor = Tema.TextoSuave;
        texto.MaximumSize = new Size(380, 0);
        texto.Margin = new Padding(0, 6, 0, 12);
        var rotulo = Ui.Rotulo("Senha", 9.5f);

        var botoes = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill, Margin = new Padding(0, 16, 0, 0)
        };
        var cancelar = Ui.Botao("Cancelar", Ui.Neutro, 120, 38);
        cancelar.DialogResult = DialogResult.Cancel;
        _confirmar.Click += async (_, _) => await Validar();
        botoes.Controls.AddRange([_confirmar, cancelar]);

        tabela.Controls.AddRange([titulo, texto, rotulo, _senha, botoes]);
        Controls.Add(tabela);
        AcceptButton = _confirmar;
        CancelButton = cancelar;
        Shown += (_, _) => _senha.Focus();
    }

    private async Task Validar()
    {
        if (_senha.Text.Length == 0)
        {
            Ui.Aviso(this, "Digite a sua senha.");
            _senha.Focus();
            return;
        }
        _confirmar.Enabled = false;
        try
        {
            await _api.ConfirmarSenhaAsync(_senha.Text);
            DialogResult = DialogResult.OK;
        }
        catch (ApiException ex)
        {
            Ui.Aviso(this, ex.Message);
            _senha.SelectAll();
            _senha.Focus();
        }
        finally
        {
            if (!IsDisposed) _confirmar.Enabled = true;
        }
    }
}
