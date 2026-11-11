using System.ComponentModel;
using System.Drawing.Drawing2D;
using ProjetoJP.Controles;

namespace ProjetoJP.Instalador;

/// <summary>
/// Assistente de instalação no visual do sistema: coluna azul-marinho com os passos à esquerda e o conteúdo à direita.
/// Passos: Boas-vindas → Programas → Local → Instalar → Pronto.
/// </summary>
public sealed class InstaladorForm : Form
{
    private static readonly string[] NomesPassos = ["Boas-vindas", "Programas", "Local", "Instalar", "Pronto"];

    private readonly ColunaPassos _coluna = new() { Dock = DockStyle.Left, Width = 240 };
    private readonly Panel _conteudo = new() { Dock = DockStyle.Fill, BackColor = Tema.Superficie, Padding = new Padding(30, 26, 30, 0) };
    private readonly Panel _rodape = new() { Dock = DockStyle.Bottom, Height = 66, BackColor = Tema.Superficie };
    private readonly BotaoTema _cancelar = new() { Text = "Cancelar", Estilo = EstiloBotao.Secundario, Size = new Size(110, 38) };
    private readonly BotaoTema _voltar = new() { Text = "Voltar", Estilo = EstiloBotao.Secundario, Size = new Size(100, 38) };
    private readonly BotaoTema _avancar = new() { Text = "Avançar", Size = new Size(120, 38) };

    private readonly CartaoPrograma _cartaoCaixa = new(ProgramaInstalavel.Caixa) { Selecionado = true };
    private readonly CartaoPrograma _cartaoRetaguarda = new(ProgramaInstalavel.Retaguarda);
    private readonly Label _espaco = new() { AutoSize = true, Font = Tema.Fonte(9.5f), ForeColor = Tema.TextoSuave };

    private readonly TextBox _pasta = new() { Font = Tema.Fonte(10.5f) };
    private readonly CheckBox _atalhoArea = new() { Text = "Criar atalho na Área de Trabalho", Checked = true, AutoSize = true };
    private readonly CheckBox _atalhoMenu = new() { Text = "Criar atalho no Menu Iniciar", Checked = true, AutoSize = true };
    private readonly CheckBox _caixaAoIniciar = new() { Text = "Abrir o PDV quando o Windows iniciar", Checked = false, AutoSize = true };

    private readonly BarraAndamento _barra = new() { Height = 14 };
    private readonly Label _andamento = new() { AutoSize = true, Font = Tema.Fonte(10), ForeColor = Tema.TextoSuave };

    private readonly Label _concluidoTexto = new() { AutoSize = true, MaximumSize = new Size(480, 0), Font = Tema.Fonte(10), ForeColor = Tema.TextoSuave };
    private readonly CheckBox _abrirCaixa = new() { Text = "Abrir o PDV agora", Checked = true, AutoSize = true };
    private readonly CheckBox _abrirRetaguarda = new() { Text = "Abrir a Gestão agora", Checked = true, AutoSize = true };

    private readonly Panel[] _paginas;
    private readonly bool _temPacote;
    private int _passo;
    private bool _instalando;
    private OpcoesInstalacao? _feita;

    public InstaladorForm()
    {
        Text = "Projeto JP Instalador";
        Font = Tema.Fonte(10);
        BackColor = Tema.Superficie;
        ForeColor = Tema.Texto;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(790, 480);
        Icon = IconeDoRecurso("caixa.ico") ?? Icon;

        var data = Instalacao.DataDoPacote();
        _temPacote = data is not null;
        _coluna.Versao = data is { } d ? $"Instalação · versão {d:dd/MM/yyyy}" : "Instalação";
        _coluna.Logo = IconeDoRecurso("caixa.ico")?.ToBitmap();
        _coluna.Passos = NomesPassos;

        _paginas = [PaginaBoasVindas(), PaginaProgramas(), PaginaLocal(), PaginaInstalando(), PaginaPronto()];
        foreach (var p in _paginas) { p.Dock = DockStyle.Fill; p.Visible = false; _conteudo.Controls.Add(p); }

        // Rodapé: linha fina em cima, Cancelar à esquerda, Voltar e Avançar à direita.
        _rodape.Paint += (_, e) =>
        {
            using var linha = new Pen(Tema.Borda);
            e.Graphics.DrawLine(linha, 30, 0, _rodape.Width - 30, 0);
        };
        _rodape.Resize += (_, _) => PosicionarBotoes();
        _rodape.Controls.AddRange([_cancelar, _voltar, _avancar]);
        _cancelar.Click += (_, _) => Close();
        _voltar.Click += (_, _) => IrPara(_passo - 1);
        _avancar.Click += async (_, _) => await Avancar();

        var direita = new Panel { Dock = DockStyle.Fill, BackColor = Tema.Superficie };
        direita.Controls.Add(_conteudo);
        direita.Controls.Add(_rodape);
        Controls.Add(direita);
        Controls.Add(_coluna);

        _cartaoCaixa.Click += (_, _) => AtualizarEscolha();
        _cartaoRetaguarda.Click += (_, _) => AtualizarEscolha();
        AcceptButton = _avancar;
        IrPara(0);
    }

    // ---------- Páginas ----------

    private static Label Titulo(string texto) => new() { Text = texto, AutoSize = true, Font = Tema.Semibold(16), ForeColor = Tema.Texto, Location = new Point(0, 0) };

    private static Label Subtitulo(string texto, int y = 40) => new()
    {
        Text = texto, AutoSize = true, MaximumSize = new Size(480, 0), Font = Tema.Fonte(10), ForeColor = Tema.TextoSuave, Location = new Point(0, y)
    };

    // A altura cresce se o texto precisar (Windows com letras maiores: 125%, 150%...).
    private static CaixaInfo Caixinha(string texto, int y, int altura)
    {
        var caixa = new CaixaInfo(texto) { Location = new Point(0, y), Size = new Size(488, altura) };
        caixa.Height = Math.Max(altura, caixa.AlturaNecessaria());
        return caixa;
    }

    private Panel PaginaBoasVindas()
    {
        var p = new Panel();
        p.Controls.Add(Titulo("Bem-vindo à instalação"));
        p.Controls.Add(Subtitulo("Este assistente instala o sistema neste computador."));
        p.Controls.Add(_temPacote
            ? Caixinha("•  Instala o PDV e/ou a Gestão (você escolhe).\n" +
                       "•  Detecta sozinho se o Windows é 64 ou 32 bits.\n" +
                       "•  Não precisa instalar o .NET nem nada antes.\n" +
                       "•  Se já houver uma versão instalada, atualiza só o programa: configurações e vendas guardadas continuam.", 84, 128)
            : Caixinha("Este instalador está vazio: os programas não foram incluídos.\n\nGere de novo com o deploy\\publicar.cmd e use o " +
                       "Instalar-ProjetoJP.exe da pasta publicar\\instalador.", 84, 100));
        return p;
    }

    private Panel PaginaProgramas()
    {
        var p = new Panel();
        p.Controls.Add(Titulo("O que instalar neste computador?"));
        p.Controls.Add(Subtitulo("Marque um ou os dois."));
        _cartaoCaixa.SetBounds(0, 80, 488, 64);
        _cartaoRetaguarda.SetBounds(0, 154, 488, 64);
        _espaco.Location = new Point(0, 234);
        p.Controls.AddRange([_cartaoCaixa, _cartaoRetaguarda, _espaco]);
        return p;
    }

    private Panel PaginaLocal()
    {
        var p = new Panel();
        p.Controls.Add(Titulo("Onde instalar?"));
        p.Controls.Add(Subtitulo("A pasta dos programas. As configurações e o banco do caixa ficam em C:\\ProgramData\\ErpNaoFiscal."));
        var rotulo = new Label { Text = "Pasta de instalação", AutoSize = true, Font = Tema.Fonte(9.5f), ForeColor = Tema.TextoSuave, Location = new Point(0, 100) };
        _pasta.SetBounds(0, 124, 370, 32);
        var procurar = new BotaoTema { Text = "Procurar...", Estilo = EstiloBotao.Secundario, Size = new Size(108, 32), Location = new Point(380, 122) };
        procurar.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog { Description = "Onde instalar o Projeto JP", UseDescriptionForTitle = true, ShowNewFolderButton = true };
            var atual = _pasta.Text.Trim();
            if (Directory.Exists(atual)) d.InitialDirectory = atual;
            if (d.ShowDialog(this) != DialogResult.OK) return;
            // Escolheu uma pasta qualquer (ex.: D:\Programas): instala numa subpasta "Projeto JP" dentro dela.
            var escolhida = d.SelectedPath;
            var nome = Path.GetFileName(escolhida.TrimEnd('\\'));
            _pasta.Text = nome.Equals(Instalacao.PastaPadrao, StringComparison.OrdinalIgnoreCase) || nome.Equals(Instalacao.PastaPadraoAntiga, StringComparison.OrdinalIgnoreCase)
                ? escolhida : Path.Combine(escolhida, Instalacao.PastaPadrao);
            AtualizarEspaco();
        };
        _pasta.TextChanged += (_, _) => AtualizarEspaco();
        var atalhos = new Label { Text = "Atalhos", AutoSize = true, Font = Tema.Fonte(9.5f), ForeColor = Tema.TextoSuave, Location = new Point(0, 176) };
        _atalhoArea.Location = new Point(0, 200);
        _atalhoMenu.Location = new Point(0, 228);
        _caixaAoIniciar.Location = new Point(0, 256);
        foreach (var c in new[] { _atalhoArea, _atalhoMenu, _caixaAoIniciar }) c.Font = Tema.Fonte(10);
        p.Controls.AddRange([rotulo, _pasta, procurar, atalhos, _atalhoArea, _atalhoMenu, _caixaAoIniciar]);
        return p;
    }

    private Panel PaginaInstalando()
    {
        var p = new Panel();
        p.Controls.Add(Titulo("Instalando..."));
        p.Controls.Add(Subtitulo("Isso leva menos de um minuto."));
        _barra.SetBounds(0, 96, 488, 14);
        _andamento.Location = new Point(0, 122);
        p.Controls.AddRange([_barra, _andamento]);
        return p;
    }

    private Label? _tituloPronto;
    private CaixaInfo? _dicaPronto;

    private Panel PaginaPronto()
    {
        var p = new Panel();
        var ok = new IconeConcluido { Location = new Point(0, 0), Size = new Size(60, 60) };
        _tituloPronto = Titulo("Instalação concluída");
        _tituloPronto.Location = new Point(0, 74);
        _concluidoTexto.Location = new Point(0, 112);
        _abrirCaixa.Location = new Point(0, 158);
        _abrirRetaguarda.Location = new Point(0, 186);
        _abrirCaixa.Font = _abrirRetaguarda.Font = Tema.Fonte(10);
        _dicaPronto = Caixinha("Na primeira vez, o programa pede o nome do servidor (o mesmo da Gestão) e, no PDV, a chave " +
                               "do caixa — gerada na Gestão, em Caixas > Caixas (terminais).", 222, 64);
        p.Controls.AddRange([ok, _tituloPronto, _concluidoTexto, _abrirCaixa, _abrirRetaguarda, _dicaPronto]);
        return p;
    }

    /// <summary>
    /// Arruma a tela "Pronto" pelo tamanho real de cada item (um embaixo do outro), para não encavalar quando o
    /// Windows está com letras maiores (125%, 150%) ou quando só um programa foi instalado.
    /// </summary>
    private void OrganizarPronto()
    {
        if (_tituloPronto is null || _dicaPronto is null) return;
        var y = _tituloPronto.Bottom + 8;
        _concluidoTexto.Top = y;
        y = _concluidoTexto.Bottom + 18;
        foreach (var marcar in new[] { _abrirCaixa, _abrirRetaguarda })
        {
            if (!marcar.Visible) continue;
            marcar.Top = y;
            y = marcar.Bottom + 8;
        }
        _dicaPronto.Top = y + 14;
        _dicaPronto.Height = Math.Max(64, _dicaPronto.AlturaNecessaria());
    }

    // ---------- Navegação ----------

    private void IrPara(int passo)
    {
        passo = Math.Clamp(passo, 0, _paginas.Length - 1);
        _passo = passo;
        for (var i = 0; i < _paginas.Length; i++) _paginas[i].Visible = i == passo;
        _coluna.Atual = passo;

        _voltar.Visible = passo is 1 or 2;
        _cancelar.Visible = passo < 4;
        _cancelar.Enabled = !_instalando;
        _avancar.Enabled = passo switch { 0 => _temPacote, 3 => false, _ => true };
        _avancar.Text = passo switch { 2 => "Instalar", 3 => "Aguarde", 4 => "Concluir", _ => "Avançar" };
        if (passo == 1) AtualizarEscolha();
        if (passo == 2)
        {
            if (_pasta.Text.Length == 0) _pasta.Text = Instalacao.PastaSugerida();
            _caixaAoIniciar.Enabled = _cartaoCaixa.Selecionado;
            if (!_cartaoCaixa.Selecionado) _caixaAoIniciar.Checked = false;
            AtualizarEspaco();
        }
        PosicionarBotoes();
        _avancar.Focus();
    }

    private void PosicionarBotoes()
    {
        var y = 14;
        _cancelar.Location = new Point(30, y);
        _avancar.Location = new Point(_rodape.Width - 30 - _avancar.Width, y);
        _voltar.Location = new Point(_avancar.Left - 10 - _voltar.Width, y);
    }

    private List<ProgramaInstalavel> Escolhidos()
    {
        var lista = new List<ProgramaInstalavel>();
        if (_cartaoCaixa.Selecionado) lista.Add(ProgramaInstalavel.Caixa);
        if (_cartaoRetaguarda.Selecionado) lista.Add(ProgramaInstalavel.Retaguarda);
        return lista;
    }

    private void AtualizarEscolha()
    {
        var pasta = _pasta.Text.Length > 0 ? _pasta.Text.Trim() : Instalacao.PastaSugerida();
        _cartaoCaixa.JaInstalado = Instalacao.JaInstalado(ProgramaInstalavel.Caixa, pasta);
        _cartaoRetaguarda.JaInstalado = Instalacao.JaInstalado(ProgramaInstalavel.Retaguarda, pasta);
        if (_passo == 1) _avancar.Enabled = Escolhidos().Count > 0;
        AtualizarEspaco();
    }

    private void AtualizarEspaco()
    {
        var mb = Escolhidos().Sum(Instalacao.TamanhoNoPacote) / (1024 * 1024);
        var texto = $"Espaço necessário: {mb:N0} MB";
        try
        {
            var raiz = Path.GetPathRoot(Path.GetFullPath(_pasta.Text.Length > 0 ? _pasta.Text.Trim() : Instalacao.PastaSugerida()));
            if (!string.IsNullOrEmpty(raiz))
            {
                var disco = new DriveInfo(raiz);
                texto += $"  ·  disponível em {raiz.TrimEnd('\\')}: {disco.AvailableFreeSpace / (1024L * 1024 * 1024):N0} GB";
            }
        }
        catch (Exception) { /* pasta ainda incompleta */ }
        _espaco.Text = texto;
    }

    private async Task Avancar()
    {
        switch (_passo)
        {
            case 0:
            case 1:
                if (_passo == 1 && Escolhidos().Count == 0) return;
                IrPara(_passo + 1);
                break;
            case 2:
                await Instalar();
                break;
            case 4:
                AbrirEscolhidos();
                Close();
                break;
        }
    }

    // ---------- Instalar ----------

    private string? ConferirPasta()
    {
        var pasta = _pasta.Text.Trim();
        if (pasta.Length == 0) return "Escolha a pasta de instalação.";
        try
        {
            pasta = Path.GetFullPath(pasta);
            if (!Path.IsPathRooted(pasta) || pasta.TrimEnd('\\').Length <= 3) return "Escolha uma pasta, não a raiz do disco (ex.: C:\\Program Files\\Projeto JP).";
            if (pasta.StartsWith(@"\\", StringComparison.Ordinal)) return "Instale numa pasta deste computador, não numa pasta da rede.";
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (pasta.StartsWith(windows, StringComparison.OrdinalIgnoreCase)) return "Não instale dentro da pasta do Windows.";
        }
        catch (Exception) { return "Esse caminho de pasta não é válido."; }
        _pasta.Text = pasta;
        return null;
    }

    private async Task Instalar()
    {
        var erro = ConferirPasta();
        if (erro is not null) { MessageBox.Show(this, erro, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        var escolhidos = Escolhidos();
        // Programa aberto não deixa trocar o .exe: pede para fechar.
        while (Instalacao.Abertos(escolhidos) is { Count: > 0 } abertos)
        {
            var nomes = string.Join(" e ", abertos.Select(a => a.ComArtigo));
            if (MessageBox.Show(this, $"Feche {nomes} (aberto neste computador) e clique em Tentar novamente.",
                    Text, MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning) != DialogResult.Retry) return;
        }

        var opcoes = new OpcoesInstalacao
        {
            Programas = escolhidos,
            Pasta = _pasta.Text,
            AtalhoAreaDeTrabalho = _atalhoArea.Checked,
            AtalhoMenuIniciar = _atalhoMenu.Checked,
            CaixaAoIniciarWindows = _caixaAoIniciar.Checked && _cartaoCaixa.Selecionado
        };

        _instalando = true;
        IrPara(3);
        var andamento = new Progress<(int percentual, string texto)>(a =>
        {
            _barra.Valor = a.percentual;
            _andamento.Text = a.texto;
        });
        try
        {
            await Task.Run(() => Instalacao.Instalar(opcoes, andamento));
        }
        catch (Exception ex)
        {
            _instalando = false;
            var motivo = ex is UnauthorizedAccessException
                ? "sem permissão para gravar nessa pasta. Escolha outra pasta ou abra o instalador como administrador."
                : ex.Message;
            MessageBox.Show(this, "A instalação não terminou: " + motivo + "\n\nNada do que já funcionava foi apagado.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            IrPara(2);
            return;
        }
        _instalando = false;
        _feita = opcoes;

        var nomesInstalados = string.Join(" e ", escolhidos.Select(p => p.ComArtigo));
        nomesInstalados = char.ToUpper(nomesInstalados[0]) + nomesInstalados[1..];
        var verbo = escolhidos.Count > 1 ? "foram instalados" : escolhidos[0] == ProgramaInstalavel.Retaguarda ? "foi instalada" : "foi instalado";
        _concluidoTexto.Text = $"{nomesInstalados} {verbo} em {opcoes.Pasta}.";
        _abrirCaixa.Visible = escolhidos.Contains(ProgramaInstalavel.Caixa);
        _abrirRetaguarda.Visible = escolhidos.Contains(ProgramaInstalavel.Retaguarda);
        _abrirRetaguarda.Checked = !_abrirCaixa.Visible; // os dois: abre só o PDV (dá para marcar a Gestão)
        IrPara(4);
        OrganizarPronto(); // depois de aparecer: os tamanhos já são os reais
    }

    /// <summary>Abre o que foi marcado, como o usuário comum (o instalador roda como administrador).</summary>
    private void AbrirEscolhidos()
    {
        if (_feita is null) return;
        var abrir = new List<ProgramaInstalavel>();
        if (_abrirCaixa.Visible && _abrirCaixa.Checked) abrir.Add(ProgramaInstalavel.Caixa);
        if (_abrirRetaguarda.Visible && _abrirRetaguarda.Checked) abrir.Add(ProgramaInstalavel.Retaguarda);
        foreach (var p in abrir)
        {
            try
            {
                // Pelo Explorer o programa abre sem os direitos de administrador do instalador.
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe",
                    "\"" + Path.Combine(_feita.Pasta, p.Pasta, p.Exe) + "\"") { UseShellExecute = false })?.Dispose();
            }
            catch (Exception) { /* abre pelo atalho */ }
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_instalando)
        {
            e.Cancel = true; // não para no meio da cópia
            return;
        }
        if (_passo is > 0 and < 3 && e.CloseReason == CloseReason.UserClosing &&
            MessageBox.Show(this, "Sair da instalação?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            e.Cancel = true;
        base.OnFormClosing(e);
    }

    private static Icon? IconeDoRecurso(string nome)
    {
        try
        {
            using var s = typeof(InstaladorForm).Assembly.GetManifestResourceStream(nome);
            return s is null ? null : new Icon(s);
        }
        catch (Exception) { return null; }
    }

    // ---------- Controles desenhados ----------

    /// <summary>Coluna azul-marinho: logo, nome do sistema, versão e a lista de passos (feitos, atual e próximos).</summary>
    private sealed class ColunaPassos : Control
    {
        private int _atual;

        public ColunaPassos()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Tema.Barra;
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Versao { get; set; } = "";

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Image? Logo { get; set; }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string[] Passos { get; set; } = [];

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Atual
        {
            get => _atual;
            set { _atual = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Tema.Barra);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            const int x = 22;

            var quadro = new Rectangle(x, 26, 56, 56);
            using (var caminho = Tema.Arredondado(quadro, 12))
            using (var fundo = new SolidBrush(Tema.Primaria))
                g.FillPath(fundo, caminho);
            if (Logo is not null) g.DrawImage(Logo, new Rectangle(quadro.X + 10, quadro.Y + 10, 36, 36));

            TextRenderer.DrawText(g, "Projeto JP", Tema.Semibold(14), new Point(x - 3, 94), Color.White, TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, Versao, Tema.Fonte(8.5f), new Point(x - 2, 122), Tema.BarraTextoSuave, TextFormatFlags.NoPrefix);

            var y = 162;
            for (var i = 0; i < Passos.Length; i++)
            {
                var feito = i < _atual;
                var atual = i == _atual;
                var circulo = new Rectangle(x, y, 22, 22);
                var corCirculo = feito ? Tema.Sucesso : atual ? Tema.Primaria : Tema.Barra;
                using (var pincel = new SolidBrush(corCirculo)) g.FillEllipse(pincel, circulo);
                if (!feito && !atual)
                    using (var borda = new Pen(Color.FromArgb(75, 93, 122), 1.5f)) g.DrawEllipse(borda, circulo);
                TextRenderer.DrawText(g, feito ? "✓" : (i + 1).ToString(), Tema.Semibold(8.5f), circulo,
                    feito || atual ? Color.White : Color.FromArgb(127, 143, 168), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(g, Passos[i], atual ? Tema.Semibold(10) : Tema.Fonte(10), new Point(x + 32, y + 2),
                    atual ? Color.White : feito ? Color.FromArgb(201, 213, 230) : Color.FromArgb(127, 143, 168), TextFormatFlags.NoPrefix);
                y += 36;
            }

            TextRenderer.DrawText(g, "Windows 10 e 11 · 64 e 32 bits", Tema.Fonte(8), new Point(x - 2, Height - 30),
                Color.FromArgb(111, 129, 157), TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Cartão clicável de um programa (ícone, nome, descrição e marcação), como na prévia.</summary>
    private sealed class CartaoPrograma : Control
    {
        private readonly ProgramaInstalavel _programa;
        private readonly Image? _icone;
        private bool _selecionado;
        private bool _jaInstalado;
        private bool _sobre;

        public CartaoPrograma(ProgramaInstalavel programa)
        {
            _programa = programa;
            _icone = IconeDoRecurso(programa.Icone)?.ToBitmap();
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            BackColor = Tema.Superficie;
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Selecionado
        {
            get => _selecionado;
            set { _selecionado = value; Invalidate(); }
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool JaInstalado
        {
            get => _jaInstalado;
            set { _jaInstalado = value; Invalidate(); }
        }

        protected override void OnClick(EventArgs e)
        {
            Selecionado = !Selecionado;
            base.OnClick(e);
        }

        protected override void OnMouseEnter(EventArgs e) { _sobre = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _sobre = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Tema.Superficie);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var area = new Rectangle(1, 1, Width - 3, Height - 3);
            using (var caminho = Tema.Arredondado(area, 10))
            {
                using (var fundo = new SolidBrush(_selecionado ? Tema.PrimariaClara : Tema.Superficie)) g.FillPath(fundo, caminho);
                using var borda = new Pen(_selecionado ? Tema.Primaria : _sobre ? Tema.TextoSuave : Tema.Borda, 1.5f);
                g.DrawPath(borda, caminho);
            }
            if (_icone is not null) g.DrawImage(_icone, new Rectangle(16, (Height - 36) / 2, 36, 36));
            TextRenderer.DrawText(g, _programa.Nome, Tema.Semibold(11), new Point(64, 12), Tema.Texto, TextFormatFlags.NoPrefix);
            if (_jaInstalado)
            {
                var largura = TextRenderer.MeasureText(_programa.Nome, Tema.Semibold(11)).Width;
                TextRenderer.DrawText(g, "já instalado: será atualizado", Tema.Fonte(8.5f), new Point(64 + largura + 4, 15), Tema.Sucesso, TextFormatFlags.NoPrefix);
            }
            TextRenderer.DrawText(g, _programa.Descricao, Tema.Fonte(9), new Point(65, 36), Tema.TextoSuave, TextFormatFlags.NoPrefix);

            var marca = new Rectangle(Width - 40, (Height - 22) / 2, 22, 22);
            using (var caminho = Tema.Arredondado(marca, 5))
            {
                if (_selecionado)
                {
                    using var cheio = new SolidBrush(Tema.Primaria);
                    g.FillPath(cheio, caminho);
                    TextRenderer.DrawText(g, "✓", Tema.Semibold(10), marca, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                else
                {
                    using var borda = new Pen(Tema.TextoSuave, 1.5f);
                    g.DrawPath(borda, caminho);
                }
            }
        }
    }

    /// <summary>Quadro cinza claro com texto (informações e dicas).</summary>
    private sealed class CaixaInfo : Panel
    {
        private readonly string _texto;

        public CaixaInfo(string texto)
        {
            _texto = texto;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        /// <summary>Altura para o texto caber inteiro na largura atual (com as margens do desenho).</summary>
        public int AlturaNecessaria() =>
            TextRenderer.MeasureText(_texto, Tema.Fonte(9.5f), new Size(Math.Max(50, Width - 28), int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + 24;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Tema.Superficie);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var caminho = Tema.Arredondado(new Rectangle(0, 0, Width - 1, Height - 1), 8))
            using (var fundo = new SolidBrush(Tema.Fundo))
                g.FillPath(fundo, caminho);
            TextRenderer.DrawText(g, _texto, Tema.Fonte(9.5f), new Rectangle(14, 10, Width - 28, Height - 20), Tema.Texto,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Barra de andamento arredondada, na cor do sistema.</summary>
    private sealed class BarraAndamento : Control
    {
        private int _valor;

        public BarraAndamento()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Valor
        {
            get => _valor;
            set { _valor = Math.Clamp(value, 0, 100); Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Tema.Superficie);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var raio = Height / 2;
            using (var trilho = Tema.Arredondado(new Rectangle(0, 0, Width - 1, Height - 1), raio))
            using (var fundo = new SolidBrush(Tema.Fundo))
                g.FillPath(fundo, trilho);
            var largura = (int)((Width - 1) * (_valor / 100.0));
            if (largura < Height) return;
            using var cheio = Tema.Arredondado(new Rectangle(0, 0, largura, Height - 1), raio);
            using var cor = new SolidBrush(Tema.Primaria);
            g.FillPath(cor, cheio);
        }
    }

    /// <summary>Círculo verde com o "✓" da tela final.</summary>
    private sealed class IconeConcluido : Control
    {
        public IconeConcluido()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Tema.Superficie);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var fundo = new SolidBrush(Tema.SucessoClaro)) g.FillEllipse(fundo, 0, 0, Width - 1, Height - 1);
            using var traco = new Pen(Tema.Sucesso, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(traco, [new PointF(Width * 0.30f, Height * 0.52f), new PointF(Width * 0.44f, Height * 0.66f), new PointF(Width * 0.71f, Height * 0.37f)]);
        }
    }
}
