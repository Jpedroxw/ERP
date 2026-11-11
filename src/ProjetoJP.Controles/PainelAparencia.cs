using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace ProjetoJP.Controles;

/// <summary>
/// Opções de aparência (tema, cor, nome da empresa, logo, plano de fundo). O mesmo painel é usado
/// numa página das Opções do caixa e na janela Aparência da Retaguarda. No ERP Master mostra só tema e cor.
/// </summary>
public class PainelAparencia : Panel
{
    private readonly ComboBox _tema = new() { DropDownStyle = ComboBoxStyle.DropDownList, Font = Tema.Fonte(10.5f) };
    private readonly FlowLayoutPanel _cores = new() { AutoSize = false, WrapContents = false, BackColor = Color.Transparent };
    private readonly TextBox _nome = new() { Font = Tema.Fonte(10.5f), MaxLength = 30, PlaceholderText = Aparencia.NomePadrao };
    private readonly PictureBox _logo = new() { SizeMode = PictureBoxSizeMode.Zoom, BackColor = Tema.Barra, Padding = new Padding(8) };
    private readonly PictureBox _fundo = new() { SizeMode = PictureBoxSizeMode.Zoom, BackColor = Tema.Fundo, BorderStyle = BorderStyle.FixedSingle };
    private readonly TrackBar _intensidade = new() { Minimum = 5, Maximum = 80, TickFrequency = 15, SmallChange = 5, LargeChange = 15, AutoSize = false };
    private readonly Label _valorIntensidade = new() { AutoSize = true, ForeColor = Tema.TextoSuave, Font = Tema.Fonte(9) };
    private readonly List<AmostraCor> _amostras = [];

    private Color _cor;
    private string? _novoLogo;   // caminho escolhido, "" = remover, null = manter
    private string? _novoFundo;

    public PainelAparencia()
    {
        BackColor = Tema.Superficie;
        AutoScroll = true;
        var atual = Aparencia.Atual;
        var y = 0;

        Label Titulo(string texto, string? dica = null)
        {
            var l = new Label { Text = texto, AutoSize = true, Font = Tema.Semibold(10), ForeColor = Tema.Texto, Location = new Point(0, y) };
            Controls.Add(l);
            if (dica is not null)
                Controls.Add(new Label { Text = dica, AutoSize = true, Font = Tema.Fonte(8.5f), ForeColor = Tema.TextoSuave, Location = new Point(l.PreferredWidth + 6, y + 2) });
            y += 24;
            return l;
        }

        // Tema
        Titulo("Tema");
        _tema.Items.AddRange(new object[] { Aparencia.Nome(OpcaoTema.Claro), Aparencia.Nome(OpcaoTema.Escuro), Aparencia.Nome(OpcaoTema.Windows) });
        _tema.SelectedIndex = (int)atual.Tema;
        _tema.SetBounds(0, y, 220, 30);
        Controls.Add(_tema);
        y += 44;

        // Cor de destaque
        Titulo("Cor de destaque", Aparencia.Master ? "(botões e barras do Projeto JP Master)" : "(botões, total do caixa, barras)");
        _cor = Aparencia.LerCor(atual.CorDestaque) ?? Aparencia.Cores[0].Cor;
        _cores.SetBounds(0, y, 540, 40);
        foreach (var (nome, cor) in Aparencia.Cores)
        {
            var a = new AmostraCor(cor, nome);
            a.Click += (_, _) => Escolher(cor);
            _amostras.Add(a);
            _cores.Controls.Add(a);
        }
        var outra = new BotaoTema { Text = "Outra cor...", Estilo = EstiloBotao.Secundario, Size = new Size(116, 34), Margin = new Padding(6, 3, 0, 0) };
        outra.Click += (_, _) =>
        {
            using var dlg = new ColorDialog { Color = _cor, FullOpen = true, AnyColor = true };
            if (dlg.ShowDialog(FindForm()) == DialogResult.OK) Escolher(dlg.Color);
        };
        _cores.Controls.Add(outra);
        Controls.Add(_cores);
        Escolher(_cor);
        y += 52;

        if (Aparencia.Master)
        {
            Controls.Add(new Label
            {
                Text = "Vale só para o Projeto JP Master deste computador (o caixa e a Gestão têm a sua). " +
                       "Aparece quando o Master abrir de novo (ao salvar, ele oferece reabrir agora).",
                AutoSize = false, Size = new Size(540, 40), Location = new Point(0, y), ForeColor = Tema.TextoSuave, Font = Tema.Fonte(8.5f)
            });
            return;
        }

        // Nome da empresa
        Titulo("Nome da empresa", "(aparece no lugar de \"" + Aparencia.NomePadrao + "\" no login e no topo)");
        _nome.Text = atual.NomeEmpresa ?? "";
        _nome.SetBounds(0, y, 320, 30);
        Controls.Add(_nome);
        y += 46;

        // Logo
        Titulo("Logo", "(PNG com fundo transparente fica melhor)");
        _logo.SetBounds(0, y, 84, 84);
        _logo.Image = Aparencia.Logo;
        var escolherLogo = new BotaoTema { Text = "Escolher...", Estilo = EstiloBotao.Secundario, Size = new Size(120, 34), Location = new Point(100, y + 4) };
        var removerLogo = new BotaoTema { Text = "Remover", Estilo = EstiloBotao.Secundario, Size = new Size(120, 34), Location = new Point(100, y + 46) };
        escolherLogo.Click += (_, _) => EscolherImagem(_logo, v => _novoLogo = v);
        removerLogo.Click += (_, _) => { _logo.Image = null; _novoLogo = ""; };
        Controls.AddRange([_logo, escolherLogo, removerLogo]);
        Controls.Add(new Label
        {
            Text = "Aparece no login, no topo do caixa e no menu da Gestão.",
            AutoSize = false, Size = new Size(300, 40), Location = new Point(236, y + 10), ForeColor = Tema.TextoSuave, Font = Tema.Fonte(8.5f)
        });
        y += 100;

        // Plano de fundo
        Titulo("Plano de fundo", "(imagem atrás das telas principais)");
        _fundo.SetBounds(0, y, 160, 90);
        _fundo.Image = Aparencia.Fundo;
        var escolherFundo = new BotaoTema { Text = "Escolher...", Estilo = EstiloBotao.Secundario, Size = new Size(120, 34), Location = new Point(176, y) };
        var removerFundo = new BotaoTema { Text = "Remover", Estilo = EstiloBotao.Secundario, Size = new Size(120, 34), Location = new Point(176, y + 42) };
        escolherFundo.Click += (_, _) => EscolherImagem(_fundo, v => _novoFundo = v);
        removerFundo.Click += (_, _) => { _fundo.Image = null; _novoFundo = ""; };
        var lInt = new Label { Text = "Intensidade da imagem", AutoSize = true, ForeColor = Tema.TextoSuave, Font = Tema.Fonte(9), Location = new Point(312, y) };
        _intensidade.SetBounds(306, y + 20, 200, 36);
        _intensidade.Value = Math.Clamp(atual.IntensidadeFundo, _intensidade.Minimum, _intensidade.Maximum);
        _valorIntensidade.Location = new Point(510, y + 26);
        _intensidade.ValueChanged += (_, _) => _valorIntensidade.Text = _intensidade.Value + "%";
        _valorIntensidade.Text = _intensidade.Value + "%";
        Controls.AddRange([_fundo, escolherFundo, removerFundo, lInt, _intensidade, _valorIntensidade]);
        y += 104;

        Controls.Add(new Label
        {
            Text = "As mudanças aparecem quando o programa abrir de novo (ao salvar, ele oferece reabrir agora). " +
                   "Valem para o caixa e a Gestão deste computador.",
            AutoSize = false, Size = new Size(540, 40), Location = new Point(0, y), ForeColor = Tema.TextoSuave, Font = Tema.Fonte(8.5f)
        });
    }

    private void Escolher(Color cor)
    {
        _cor = cor;
        foreach (var a in _amostras) a.Marcar(a.Cor.ToArgb() == cor.ToArgb());
    }

    private bool EscolherImagem(PictureBox previa, Action<string> guardar)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Escolher imagem",
            Filter = "Imagens (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif"
        };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return false;
        var img = Aparencia.CarregarImagem(dlg.FileName);
        if (img is null)
        {
            MessageBox.Show(FindForm(), "Não foi possível abrir essa imagem.", "Atenção", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        previa.Image = img;
        guardar(dlg.FileName);
        return true;
    }

    /// <summary>Grava as escolhas. Devolve true se algo mudou (aí vale reabrir o programa).</summary>
    public bool Salvar()
    {
        var padrao = _cor.ToArgb() == Tema.DestaquePadrao.ToArgb();
        var novos = new Aparencia.Dados
        {
            Tema = (OpcaoTema)_tema.SelectedIndex,
            CorDestaque = padrao ? null : Aparencia.ParaHex(_cor),
            NomeEmpresa = Aparencia.Master || string.IsNullOrWhiteSpace(_nome.Text) ? null : _nome.Text.Trim(),
            IntensidadeFundo = Aparencia.Master ? Aparencia.Atual.IntensidadeFundo : _intensidade.Value
        };
        return Aparencia.Salvar(novos, _novoLogo, _novoFundo);
    }

    /// <summary>Bolinha de cor clicável.</summary>
    private sealed class AmostraCor : Control
    {
        private bool _marcada;
        private bool _hover;

        public Color Cor { get; }

        public AmostraCor(Color cor, string nome)
        {
            Cor = cor;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(38, 38);
            Margin = new Padding(0, 1, 6, 0);
            Cursor = Cursors.Hand;
            new ToolTip().SetToolTip(this, nome);
        }

        public void Marcar(bool sim)
        {
            _marcada = sim;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Tema.PintarFundoDoPai(g, this);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var margem = _marcada || _hover ? 2 : 5;
            using (var b = new SolidBrush(Cor)) g.FillEllipse(b, margem, margem, Width - 2 * margem - 1, Height - 2 * margem - 1);
            if (_marcada)
            {
                using var p = new Pen(Tema.Texto, 2);
                g.DrawEllipse(p, 1, 1, Width - 3, Height - 3);
                TextRenderer.DrawText(g, Icones.Ok, Tema.FonteIcone(11), ClientRectangle, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }
}
