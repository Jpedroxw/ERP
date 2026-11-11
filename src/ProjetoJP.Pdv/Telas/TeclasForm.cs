using ProjetoJP.Controles;
using ProjetoJP.Pdv.Servicos;

namespace ProjetoJP.Pdv.Telas;

/// <summary>
/// Captura uma tecla para uma ação do caixa: é só apertar a tecla do teclado comum ou a do teclado de automação.
/// Recusa as teclas usadas para digitar na busca (letras, números, Enter, Esc...).
/// </summary>
public sealed class CapturarTeclaForm : Form
{
    private readonly Label _mensagem;
    public Keys Tecla { get; private set; } = Keys.None;

    public CapturarTeclaForm(string acao)
    {
        Ui.Dialogo(this, "Trocar tecla", 460, 220);
        var titulo = new Label { Text = acao, Font = Tema.Semibold(13), ForeColor = Tema.Texto, AutoSize = true, Location = new Point(24, 20) };
        var instrucao = new Label
        {
            Text = "Aperte agora a tecla (ou a tecla do teclado de automação).\nPode ser F1 a F24, Del, Ins, Home, End, PgUp, PgDn,\n+ − / do teclado numérico, ou Ctrl/Alt com outra tecla.",
            Font = Tema.Fonte(10), ForeColor = Tema.TextoSuave, AutoSize = true, Location = new Point(24, 56)
        };
        _mensagem = new Label { Font = Tema.Fonte(10), ForeColor = Tema.Perigo, AutoSize = true, Location = new Point(24, 128) };
        var cancelar = Ui.Botao("Cancelar", Ui.Neutro, 120, 36);
        cancelar.Location = new Point(460 - 24 - 120, 220 - 24 - 36);
        cancelar.TabStop = false;
        cancelar.Click += (_, _) => { DialogResult = DialogResult.Cancel; };
        Controls.AddRange([titulo, instrucao, _mensagem, cancelar]);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var codigo = keyData & Keys.KeyCode;
        if (codigo is Keys.ControlKey or Keys.ShiftKey or Keys.Menu) return true; // esperando a outra tecla
        if (keyData == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            return true;
        }
        if (!TeclasCaixa.Permitida(keyData))
        {
            _mensagem.Text = $"\"{TeclasCaixa.Texto(keyData)}\" é usada para digitar ou navegar na venda. Escolha outra.";
            return true;
        }
        Tecla = keyData;
        DialogResult = DialogResult.OK;
        return true;
    }
}
