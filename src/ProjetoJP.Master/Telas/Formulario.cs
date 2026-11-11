using System.ComponentModel;
using System.Security.Cryptography;
using ProjetoJP.Controles;

namespace ProjetoJP.Master.Telas;

/// <summary>Ajudantes visuais do ERP Master (mesmo tema do caixa e da Retaguarda).</summary>
internal static class Ui
{
    private static Icon? _icone;

    public static Icon? IconeApp
    {
        get
        {
            if (_icone is null)
            {
                try
                {
                    using var s = typeof(Ui).Assembly.GetManifestResourceStream("ProjetoJP.Master.master.ico");
                    _icone = s is not null ? new Icon(s) : Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                }
                catch (Exception) { /* ícone padrão */ }
            }
            return _icone;
        }
    }

    /// <summary>Escala do Windows (1 = 100%, 1,25 = 125%...). As letras já crescem sozinhas; margens e botões usam Px.</summary>
    public static float Escala { get; } = LerEscala();

    private static float LerEscala()
    {
        try
        {
            using var g = Graphics.FromHwnd(IntPtr.Zero);
            return Math.Max(1f, g.DpiX / 96f);
        }
        catch (Exception) { return 1f; }
    }

    /// <summary>Pixels de 100% → pixels na escala atual do Windows.</summary>
    public static int Px(int valor) => (int)Math.Round(valor * Escala);

    public static void Aviso(IWin32Window? dono, string msg) =>
        MessageBox.Show(dono, msg, "Atenção", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    public static void Info(IWin32Window? dono, string msg) =>
        MessageBox.Show(dono, msg, Aparencia.NomePadrao + " Master", MessageBoxButtons.OK, MessageBoxIcon.Information);

    public static bool Confirmar(IWin32Window? dono, string msg) =>
        MessageBox.Show(dono, msg, "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    /// <summary>Senha aleatória fácil de ditar por telefone (sem 0/O, 1/l).</summary>
    public static string GerarSenha(int tamanho = 10)
    {
        const string letras = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKMNPQRSTUVWXYZ23456789";
        return new string(Enumerable.Range(0, tamanho).Select(_ => letras[RandomNumberGenerator.GetInt32(letras.Length)]).ToArray());
    }

    /// <summary>
    /// Janela principal: ajusta à escala do Windows (posições e tamanhos crescem junto com as letras),
    /// limita à área livre da tela e centraliza.
    /// </summary>
    public static void Encaixar(Form f)
    {
        var escala = f.DeviceDpi / 96f;
        if (escala > 1.01f) f.Scale(new SizeF(escala, escala));
        Centralizar(f, f.Width, f.Height);
    }

    /// <summary>Limita o tamanho à área livre da tela e centraliza (no dono, se houver).</summary>
    public static void Centralizar(Form f, int largura, int altura)
    {
        var area = Screen.FromControl(f.Owner ?? f).WorkingArea;
        largura = Math.Min(largura, area.Width - 20);
        altura = Math.Min(altura, area.Height - 20);
        f.Size = new Size(largura, altura);

        var centro = f.Owner is { } dono && f.StartPosition == FormStartPosition.CenterParent
            ? new Point(dono.Left + (dono.Width - largura) / 2, dono.Top + (dono.Height - altura) / 2)
            : new Point(area.Left + (area.Width - largura) / 2, area.Top + (area.Height - altura) / 2);
        f.StartPosition = FormStartPosition.Manual;
        f.Location = new Point(
            Math.Clamp(centro.X, area.Left, Math.Max(area.Left, area.Right - largura)),
            Math.Clamp(centro.Y, area.Top, Math.Max(area.Top, area.Bottom - altura)));
    }

    /// <summary>Faixa azul do topo das janelas: ícone, título e subtítulo. A altura acompanha o tamanho das letras.</summary>
    public static Control Topo(string icone, string titulo, string subtitulo)
    {
        var topo = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Tema.Barra,
            ColumnCount = 2, RowCount = 2, Padding = new Padding(Px(20), Px(12), Px(20), Px(12))
        };
        topo.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        topo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        topo.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        topo.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var simbolo = new Label
        {
            Text = icone, Font = Tema.FonteIcone(18), ForeColor = Color.White, AutoSize = true,
            Margin = new Padding(0, Px(6), Px(12), 0), Anchor = AnchorStyles.Left | AnchorStyles.Top
        };
        topo.Controls.Add(simbolo, 0, 0);
        topo.SetRowSpan(simbolo, 2);
        var l1 = new Label
        {
            Text = titulo, Font = Tema.Semibold(14), ForeColor = Color.White, AutoSize = true, Margin = Padding.Empty
        };
        var l2 = new Label
        {
            Text = subtitulo, Font = Tema.Fonte(9), ForeColor = Tema.BarraTextoSuave, AutoSize = true,
            Margin = new Padding(Px(2), Px(2), 0, 0)
        };
        topo.Controls.Add(l1, 1, 0);
        topo.Controls.Add(l2, 1, 1);
        // Título ou subtítulo comprido (ex.: nome grande da empresa) quebra linha em vez de ser cortado.
        topo.SizeChanged += (_, _) =>
        {
            var livre = Math.Max(Px(100), topo.ClientSize.Width - topo.Padding.Horizontal - simbolo.Width - Px(20));
            if (l1.MaximumSize.Width != livre) l1.MaximumSize = new Size(livre, 0);
            if (l2.MaximumSize.Width != livre) l2.MaximumSize = new Size(livre, 0);
        };
        return topo;
    }
}

/// <summary>
/// Janela de formulário montada por campos: faixa de título, rótulos e campos um embaixo do outro,
/// rodapé com Cancelar / botão principal. A validação e a gravação ficam no "aoConfirmar".
///
/// Nada fica em posição fixa: os campos se arrumam pelo tamanho das letras (que acompanha a escala do
/// Windows), textos longos quebram linha, a janela abre do tamanho do conteúdo e, se não couber na tela,
/// aparece a barra de rolagem. Também dá para redimensionar a janela.
/// </summary>
public class Formulario : Form
{
    private readonly Control _topo;
    private readonly FlowLayoutPanel _rodape;
    private readonly Panel _corpo;
    private readonly TableLayoutPanel _pilha;
    private readonly BotaoTema _ok;
    private readonly List<Label> _quebraveis = [];
    private readonly List<CheckBox> _marcas = [];
    private int _larguraAjustada = -1;

    /// <summary>Largura do conteúdo em 100% (cresce com a escala do Windows).</summary>
    private const int Largura = 460;

    /// <summary>Chamado ao clicar no botão principal. Devolve true para fechar a janela.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<Task<bool>>? AoConfirmar { get; init; }

    public Formulario(string icone, string titulo, string subtitulo, string textoOk = "Salvar")
    {
        Text = titulo;
        Icon = Ui.IconeApp ?? Icon;
        AutoScaleMode = AutoScaleMode.None; // a escala é feita pelos painéis de layout (e por Ui.Px)
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = Tema.Superficie;
        ForeColor = Tema.Texto;
        Font = Tema.Fonte(10);

        _pilha = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Tema.Superficie
        };
        _pilha.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _pilha.SizeChanged += (_, _) => Ajustar();

        _corpo = new Panel
        {
            Dock = DockStyle.Fill, AutoScroll = true, BackColor = Tema.Superficie,
            Padding = new Padding(Ui.Px(28), Ui.Px(16), Ui.Px(28), Ui.Px(8))
        };
        _corpo.Controls.Add(_pilha);

        _rodape = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = Tema.Fundo,
            Padding = new Padding(Ui.Px(20), Ui.Px(12), Ui.Px(20), Ui.Px(12))
        };
        _rodape.Paint += (_, e) =>
        {
            using var linha = new Pen(Tema.Borda);
            e.Graphics.DrawLine(linha, 0, 0, _rodape.Width, 0);
        };
        _ok = new BotaoTema { Text = textoOk, Icone = Icones.Ok, Margin = new Padding(Ui.Px(10), 0, 0, 0) };
        _ok.Size = new Size(Math.Max(Ui.Px(150), TextRenderer.MeasureText(textoOk, _ok.Font).Width + Ui.Px(64)), Ui.Px(40));
        var cancelar = new BotaoTema
        {
            Text = "Cancelar", Estilo = EstiloBotao.Secundario, DialogResult = DialogResult.Cancel,
            Size = new Size(Ui.Px(110), Ui.Px(40)), Margin = Padding.Empty
        };
        _ok.Click += async (_, _) => await Confirmar();
        _rodape.Controls.Add(_ok);      // da direita para a esquerda: botão principal no canto
        _rodape.Controls.Add(cancelar);

        _topo = Ui.Topo(icone, titulo, subtitulo);
        Controls.Add(_corpo);
        Controls.Add(_rodape);
        Controls.Add(_topo);
        AcceptButton = _ok;
        CancelButton = cancelar;
    }

    // ---------- Montagem dos campos ----------

    private void Adicionar(Control c)
    {
        _pilha.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _pilha.RowCount = _pilha.RowStyles.Count;
        _pilha.Controls.Add(c, 0, _pilha.RowCount - 1);
    }

    /// <summary>Label que quebra linha quando não cabe na largura da janela.</summary>
    private Label Quebravel(Label l)
    {
        l.AutoSize = true;
        _quebraveis.Add(l);
        if (_larguraAjustada > 0) l.MaximumSize = new Size(Math.Max(50, _larguraAjustada - Ui.Px(8)), 0);
        return l;
    }

    /// <summary>Rótulo do campo, com a dica ao lado (desce para a linha de baixo se não couber).</summary>
    private void Rotulo(string rotulo, string? dica)
    {
        var linha = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = Padding.Empty, Padding = Padding.Empty,
            BackColor = Tema.Superficie
        };
        linha.Controls.Add(Quebravel(new Label
        {
            Text = rotulo, ForeColor = Tema.TextoSuave, Font = Tema.Fonte(9.5f), Margin = new Padding(0, 0, Ui.Px(6), 0)
        }));
        if (dica is not null)
            linha.Controls.Add(Quebravel(new Label
            {
                Text = dica, ForeColor = Tema.TextoSuave, Font = Tema.Fonte(8.5f), Margin = new Padding(0, Ui.Px(1), 0, 0)
            }));
        Adicionar(linha);
    }

    /// <summary>Acrescenta um campo com rótulo (e dica opcional ao lado do rótulo).</summary>
    /// <param name="altura">Para caixas de várias linhas (observação). As de uma linha têm a altura da letra.</param>
    /// <param name="largura">Campo estreito (ex.: número), em pixels de 100%. 0 = largura toda.</param>
    public T Campo<T>(string rotulo, T controle, string? dica = null, int altura = 30, int largura = 0) where T : Control
    {
        Rotulo(rotulo, dica);
        controle.Font = Tema.Fonte(10.5f);
        if (altura > 30) controle.Height = Ui.Px(altura);
        if (largura > 0)
        {
            controle.Width = Ui.Px(largura);
            controle.Anchor = AnchorStyles.Left;
        }
        else
        {
            controle.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        }
        controle.Margin = new Padding(0, Ui.Px(4), 0, Ui.Px(14));
        Adicionar(controle);
        return controle;
    }

    /// <summary>Campo com um botão à direita (ex.: "Gerar" senha).</summary>
    public TextBox CampoComBotao(string rotulo, TextBox caixa, string textoBotao, Action<TextBox> acao, string? dica = null)
    {
        Rotulo(rotulo, dica);
        var linha = new TableLayoutPanel
        {
            ColumnCount = 2, RowCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, Ui.Px(4), 0, Ui.Px(14)), Padding = Padding.Empty
        };
        linha.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        linha.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        caixa.Font = Tema.Fonte(10.5f);
        caixa.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        caixa.Margin = new Padding(0, 0, Ui.Px(8), 0);
        var b = new BotaoTema { Text = textoBotao, Estilo = EstiloBotao.Secundario, Margin = Padding.Empty };
        b.Size = new Size(Math.Max(Ui.Px(110), TextRenderer.MeasureText(textoBotao, b.Font).Width + Ui.Px(32)), Math.Max(Ui.Px(32), caixa.PreferredHeight));
        b.Click += (_, _) => acao(caixa);
        linha.Controls.Add(caixa, 0, 0);
        linha.Controls.Add(b, 1, 0);
        Adicionar(linha);
        return caixa;
    }

    /// <summary>Título de seção (agrupa os campos: Empresa, Contato, Acesso...).</summary>
    public void Secao(string titulo)
    {
        var primeiro = _pilha.Controls.Count == 0;
        Adicionar(new Label
        {
            Text = titulo, AutoSize = true, Font = Tema.Semibold(10.5f), ForeColor = Tema.Texto,
            Margin = new Padding(0, primeiro ? 0 : Ui.Px(8), 0, Ui.Px(4))
        });
        Adicionar(new Panel
        {
            Height = 1, BackColor = Tema.Borda, Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(0, 0, 0, Ui.Px(12))
        });
    }

    /// <summary>Dois campos lado a lado na mesma linha (ex.: Cidade e UF). A divisão é proporcional.</summary>
    public void Lado(string rotulo1, Control c1, int largura1, string rotulo2, Control c2)
    {
        var t = new TableLayoutPanel
        {
            ColumnCount = 2, RowCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 0, 0, Ui.Px(14)), Padding = Padding.Empty
        };
        var p1 = Math.Clamp(largura1 * 100f / Largura, 15f, 85f);
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, p1));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f - p1));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        Label Rot(string texto, int esquerda) => new()
        {
            Text = texto, AutoSize = true, ForeColor = Tema.TextoSuave, Font = Tema.Fonte(9.5f),
            Margin = new Padding(esquerda, 0, 0, 0)
        };
        t.Controls.Add(Rot(rotulo1, 0), 0, 0);
        t.Controls.Add(Rot(rotulo2, Ui.Px(6)), 1, 0);
        c1.Font = c2.Font = Tema.Fonte(10.5f);
        c1.Anchor = c2.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        c1.Margin = new Padding(0, Ui.Px(4), Ui.Px(6), 0);
        c2.Margin = new Padding(Ui.Px(6), Ui.Px(4), 0, 0);
        t.Controls.Add(c1, 0, 1);
        t.Controls.Add(c2, 1, 1);
        Adicionar(t);
    }

    public CheckBox Marcar(string texto, bool marcado)
    {
        var c = new CheckBox
        {
            Text = texto, Checked = marcado, AutoSize = false, Font = Tema.Fonte(10),
            CheckAlign = ContentAlignment.TopLeft, TextAlign = ContentAlignment.TopLeft,
            Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, Ui.Px(2), 0, Ui.Px(12)),
            Height = Ui.Px(26)
        };
        _marcas.Add(c);
        Adicionar(c);
        return c;
    }

    public Label Texto(string texto, bool destaque = false)
    {
        var l = Quebravel(new Label
        {
            Text = texto, Anchor = AnchorStyles.Left,
            Font = destaque ? Tema.Semibold(10) : Tema.Fonte(9.5f), ForeColor = destaque ? Tema.Texto : Tema.TextoSuave,
            Margin = new Padding(0, 0, 0, Ui.Px(10))
        });
        Adicionar(l);
        return l;
    }

    /// <summary>Faz os textos quebrarem na largura atual e as caixas de marcar crescerem com o texto.</summary>
    private void Ajustar()
    {
        var largura = _pilha.ClientSize.Width;
        if (largura <= 0 || largura == _larguraAjustada) return;
        _larguraAjustada = largura;

        foreach (var l in _quebraveis) l.MaximumSize = new Size(Math.Max(50, largura - Ui.Px(8)), 0);
        foreach (var c in _marcas)
        {
            var texto = TextRenderer.MeasureText(c.Text, c.Font, new Size(Math.Max(50, largura - Ui.Px(24)), 0),
                TextFormatFlags.WordBreak);
            c.Height = texto.Height + Ui.Px(6);
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        // Largura: a do desenho (na escala do Windows), sem passar da tela.
        var area = Screen.FromControl(Owner ?? this).WorkingArea;
        var bordaX = Width - ClientSize.Width;
        var bordaY = Height - ClientSize.Height;
        var largura = Math.Min(Ui.Px(Largura) + _corpo.Padding.Horizontal + SystemInformation.VerticalScrollBarWidth, area.Width - 20 - bordaX);
        ClientSize = new Size(largura, ClientSize.Height);
        PerformLayout();
        Ajustar();
        _corpo.PerformLayout();

        // Altura: o conteúdo inteiro; se não couber na tela, fica com rolagem.
        var altura = _topo.Height + _rodape.Height + _corpo.Padding.Vertical + _pilha.Height + Ui.Px(4);
        Ui.Centralizar(this, largura + bordaX, altura + bordaY);
        MinimumSize = new Size(Math.Min(Width, Ui.Px(380)), Math.Min(Height, Ui.Px(260)));
        base.OnLoad(e);
    }

    private async Task Confirmar()
    {
        if (AoConfirmar is null) { DialogResult = DialogResult.OK; return; }
        _ok.Enabled = false;
        UseWaitCursor = true;
        try
        {
            if (await AoConfirmar()) DialogResult = DialogResult.OK;
        }
        finally
        {
            UseWaitCursor = false;
            if (!IsDisposed) _ok.Enabled = true;
        }
    }
}
