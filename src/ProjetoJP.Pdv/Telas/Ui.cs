using System.Globalization;
using ProjetoJP.Controles;
using ProjetoJP.Shared;

namespace ProjetoJP.Pdv.Telas;

/// <summary>Cores, fontes e componentes padrão das telas do caixa.</summary>
internal static class Ui
{
    public static readonly CultureInfo Br = new("pt-BR");

    // Cores do tema (ProjetoJP.Controles.Tema). Os nomes antigos continuam valendo nas telas.
    public static Color Fundo => Tema.Fundo;
    public static Color Escuro => Tema.Barra;
    public static Color Destaque => Tema.Primaria;
    public static Color Sucesso => Tema.Sucesso;
    public static Color Perigo => Tema.Perigo;
    public static Color Neutro => Tema.TextoSuave;
    public static Color Alerta => Tema.Alerta;

    public static Font Fonte(float tamanho, FontStyle estilo = FontStyle.Regular) => Tema.Fonte(tamanho, estilo);

    public static Label Rotulo(string texto, float tamanho = 10, FontStyle estilo = FontStyle.Regular) =>
        new() { Text = texto, AutoSize = true, Font = Fonte(tamanho, estilo), ForeColor = Tema.Texto, BackColor = Color.Transparent };

    public static TextBox Caixa(float tamanho = 12) => new() { Font = Fonte(tamanho) };

    /// <summary>
    /// Botão do tema. A cor escolhe o estilo: nenhuma = azul (principal), Neutro = branco com borda,
    /// Escuro = azul-marinho, Perigo = vermelho.
    /// </summary>
    public static BotaoTema Botao(string texto, Color? cor = null, int largura = 150, int altura = 42, string? icone = null)
    {
        var estilo =
            cor == Perigo ? EstiloBotao.Perigo :
            cor == Neutro ? EstiloBotao.Secundario :
            cor == Escuro ? EstiloBotao.Escuro :
            cor == Sucesso ? EstiloBotao.Sucesso :
            EstiloBotao.Primario;
        return new BotaoTema
        {
            Text = texto,
            Estilo = estilo,
            Icone = icone,
            Size = new Size(largura, altura),
            Font = Tema.Semibold(10.5f)
        };
    }


    private static Icon? _iconeApp;

    /// <summary>Ícone do programa (o mesmo do .exe), usado na barra de título e na barra de tarefas.</summary>
    public static Icon? IconeApp
    {
        get
        {
            if (_iconeApp is null)
            {
                try
                {
                    // caixa.ico embutido no programa (todos os tamanhos: o Windows escolhe o certo
                    // para a barra de título e para a barra de tarefas).
                    using var s = typeof(Ui).Assembly.GetManifestResourceStream("ProjetoJP.Pdv.caixa.ico");
                    _iconeApp = s is not null ? new Icon(s) : Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                }
                catch (Exception) { /* sem ícone: o Windows usa o padrão */ }
            }
            return _iconeApp;
        }
    }

    public static void Dialogo(Form f, string titulo, int largura, int altura)
    {
        f.Text = titulo;
        f.Icon = IconeApp ?? f.Icon;
        f.FormBorderStyle = FormBorderStyle.FixedDialog;
        f.StartPosition = FormStartPosition.CenterParent;
        f.MaximizeBox = false;
        f.MinimizeBox = false;
        f.ShowInTaskbar = false;
        f.ClientSize = new Size(largura, altura);
        f.BackColor = Tema.Superficie;
        f.ForeColor = Tema.Texto;
        f.Font = Fonte(10);
        f.KeyPreview = true;
        JanelaNaTela.Limitar(f); // monitor pequeno: a janela não passa da tela
    }

    public static DataGridView Grade()
    {
        var g = new DataGridView
        {
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        Tema.EstilizarGrade(g, 11);
        return g;
    }

    public static DataGridViewTextBoxColumn Coluna(string titulo, float peso = 100, bool direita = false)
    {
        var c = new DataGridViewTextBoxColumn { HeaderText = titulo, FillWeight = peso, SortMode = DataGridViewColumnSortMode.NotSortable };
        if (direita) c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        return c;
    }

    /// <summary>Aceita "1,50", "1.50" e "1.234,50".</summary>
    public static bool LerDecimal(string? texto, out decimal valor) => Formatos.TryLerDecimal(texto, out valor);

    public static string Dinheiro(decimal v) => v.ToString("C2", Br);

    public static void Aviso(IWin32Window? dono, string msg) =>
        MessageBox.Show(dono, msg, "Atenção", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    public static void Erro(IWin32Window? dono, string msg) =>
        MessageBox.Show(dono, msg, "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);

    public static bool Confirmar(IWin32Window? dono, string msg) =>
        MessageBox.Show(dono, msg, "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
}
