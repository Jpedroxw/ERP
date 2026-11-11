using System.Drawing.Drawing2D;
using ProjetoJP.Controles;
using ProjetoJP.Pdv.Dados;
using ProjetoJP.Pdv.Servicos;

namespace ProjetoJP.Pdv.Telas;

/// <summary>
/// Tela do caixa sem operador (fechado): para o cliente, "CAIXA FECHADO — procure o próximo caixa" e as promoções da
/// loja passando; para o operador, Enter abre o login. Fecha com OK (operador entrou, ver <see cref="Usuario"/>),
/// Retry (opções alteradas no login: o caixa recarrega) ou Cancel (sair do programa).
/// </summary>
public class CaixaFechadoForm : Form
{
    private readonly PdvConfig _cfg;
    private readonly Sincronizador _sync;
    private readonly Pilula _pilula = new() { Font = Tema.Semibold(10.5f), Height = 32, Margin = new Padding(8, 0, 0, 0), Text = "Online" };
    // Logo da loja na maior parte do tempo; de vez em quando, algumas promoções.
    private readonly PainelPromocoes _promocoes = new()
    {
        Dock = DockStyle.Fill, TituloVazio = "CAIXA FECHADO", SubtituloVazio = "Por favor, dirija-se ao próximo caixa", LogoNaMaiorParte = true
    };
    private bool _saindo;

    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public UsuarioLocal? Usuario { get; private set; }

    public CaixaFechadoForm(PdvConfig cfg, Sincronizador sync)
    {
        _cfg = cfg;
        _sync = sync;
        var terminal = Db.LerConfig("nome_terminal") ?? "CAIXA";
        Text = $"{cfg.NomeLoja} — {terminal}";
        Icon = Ui.IconeApp ?? Icon;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        BackColor = Tema.Fundo;
        Font = Tema.Fonte(10);
        KeyPreview = true;
        ShowInTaskbar = true;

        // Barra de cima (a mesma da venda)
        var topo = TopoCaixa.Montar(cfg, out var direita);
        var pTerminal = new PilulaTexto { Text = terminal };
        direita.Controls.AddRange([_pilula, pTerminal]);

        // Coluna: cadeado, aviso, situação e "Abrir o caixa"
        var coluna = new PainelColuna { Dock = DockStyle.Right, Width = 380, Padding = new Padding(26, 24, 26, 20) };
        var cadeado = new IconeRedondo { Dock = DockStyle.Top, Height = 120 };
        var rotulo = new Label
        {
            Text = "CAIXA FECHADO", Dock = DockStyle.Top, Height = 34, Font = Tema.Semibold(12), ForeColor = CoresCaixa.Destaque,
            BackColor = Tema.Barra, TextAlign = ContentAlignment.MiddleCenter
        };
        var aviso = new Label
        {
            Text = "Procure o\npróximo caixa", Dock = DockStyle.Top, Height = 120, Font = Tema.Semibold(26), ForeColor = Color.White,
            BackColor = Tema.Barra, TextAlign = ContentAlignment.MiddleCenter
        };
        var aberto = Repositorio.CaixaAberto() is not null;
        var lCaixa = new LinhaValor("Caixa") { Valor = terminal };
        var lSituacao = new LinhaValor("Situação") { Valor = aberto ? "sem operador" : "fechado" };
        var lDesde = new LinhaValor("Desde") { Valor = Relogio.Agora.ToString("HH:mm") };
        var abrir = new BotaoColuna { Text = "Abrir o caixa   (Enter)", Dock = DockStyle.Bottom, Height = 64, Font = Tema.Semibold(15) };
        abrir.Click += (_, _) => Entrar();
        var sair = new LinkLabel
        {
            Text = "Sair do programa", Dock = DockStyle.Bottom, Height = 34, Font = Tema.Fonte(9.5f), TextAlign = ContentAlignment.MiddleCenter,
            LinkColor = Tema.BarraTextoSuave, ActiveLinkColor = Color.White, LinkBehavior = LinkBehavior.HoverUnderline, BackColor = Tema.Barra, TabStop = false
        };
        sair.LinkClicked += (_, _) => Sair();
        coluna.Controls.Add(abrir);
        coluna.Controls.Add(sair); // o último fica mais embaixo
        foreach (var c in new Control[] { lDesde, lSituacao, lCaixa, new Panel { Dock = DockStyle.Top, Height = 24, BackColor = Tema.Barra }, aviso, rotulo, cadeado })
            coluna.Controls.Add(c);

        // Meio: faixa vermelha de aviso e as promoções
        var centro = new Panel { Dock = DockStyle.Fill, BackColor = Tema.Fundo, Padding = new Padding(20, 16, 18, 14) };
        var faixa = new FaixaFechado { Dock = DockStyle.Top, Height = 70 };
        centro.Controls.Add(_promocoes);
        centro.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 12, BackColor = Tema.Fundo });
        centro.Controls.Add(faixa);

        // Rodapé: como abrir
        var rodape = new Label
        {
            Text = "Operador: aperte Enter e entre com seu usuário e senha para abrir o caixa", Dock = DockStyle.Bottom, Height = 42,
            Font = Tema.Fonte(10), ForeColor = Tema.TextoSuave, BackColor = Tema.Superficie, TextAlign = ContentAlignment.MiddleCenter
        };
        rodape.Paint += (_, e) =>
        {
            using var linha = new Pen(Tema.Borda);
            e.Graphics.DrawLine(linha, 0, 0, rodape.Width, 0);
        };

        Controls.Add(centro);
        Controls.Add(coluna);
        Controls.Add(rodape);
        Controls.Add(topo);

        Resize += (_, _) => coluna.Width = ClientSize.Width >= 1250 ? 380 : 320;
        Shown += (_, _) =>
        {
            Bounds = Screen.FromHandle(Handle).Bounds; // ocupa o monitor inteiro, como a tela de venda
            abrir.Focus();
        };
        AplicarSituacao(sync.Online);
        _sync.StatusMudou += AtualizarStatus;
        FormClosed += (_, _) => _sync.StatusMudou -= AtualizarStatus;
        FormClosing += (_, e) =>
        {
            // Alt+F4 e afins: confirma antes de fechar o programa.
            if (_saindo || DialogResult is DialogResult.OK or DialogResult.Retry || e.CloseReason != CloseReason.UserClosing) return;
            if (!Ui.Confirmar(this, "Fechar o programa do caixa?")) e.Cancel = true;
            else DialogResult = DialogResult.Cancel;
        };
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Enter)
        {
            Entrar();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void Entrar()
    {
        DialogResult resultado;
        UsuarioLocal? usuario;
        using (var login = new LoginForm(_cfg, _sync))
        {
            resultado = login.ShowDialog(this);
            usuario = login.Usuario;
        }
        if (resultado == DialogResult.OK && usuario is not null)
        {
            Usuario = usuario;
            _saindo = true;
            DialogResult = DialogResult.OK;
        }
        else if (resultado == DialogResult.Retry)
        {
            _saindo = true;
            DialogResult = DialogResult.Retry;
        }
    }

    private void Sair()
    {
        if (!Ui.Confirmar(this, "Fechar o programa do caixa?")) return;
        _saindo = true;
        DialogResult = DialogResult.Cancel;
    }

    private void AplicarSituacao(bool online)
    {
        _pilula.Text = online ? "Online" : "Offline";
        _pilula.Cor = online ? Tema.Sucesso : Tema.Alerta;
    }

    private void AtualizarStatus(bool online, string msg)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(() =>
        {
            if (IsDisposed) return;
            AplicarSituacao(online);
            if (online) _promocoes.Recarregar(forcar: true); // promoções novas chegam na sincronização
        });
    }

    /// <summary>Cadeado num círculo claro, no topo da coluna.</summary>
    private sealed class IconeRedondo : ControlePintado
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Tema.PintarFundoDoPai(g, this);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var d = Math.Min(Height - 16, 100);
            var c = new Rectangle((Width - d) / 2, (Height - d) / 2, d, d);
            using (var bola = new SolidBrush(Color.FromArgb(26, 255, 255, 255))) g.FillEllipse(bola, c);
            TextRenderer.DrawText(g, Icones.Cadeado, Tema.FonteIcone(d * 0.36f), c, CoresCaixa.Destaque, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>Faixa vermelha "CAIXA FECHADO — Por favor, dirija-se ao próximo caixa".</summary>
    private sealed class FaixaFechado : ControlePintado
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Tema.PintarFundoDoPai(g, this);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(1, 1, Width - 3, Height - 3);
            using (var caminho = Tema.Arredondado(r, 16))
            {
                using (var fundo = new SolidBrush(Tema.PerigoClaro)) g.FillPath(fundo, caminho);
                using (var borda = new Pen(Tema.Perigo, 2f)) g.DrawPath(borda, caminho);
            }
            TextRenderer.DrawText(g, Icones.Cadeado, Tema.FonteIcone(20), new Rectangle(18, 0, 40, Height), Tema.Perigo, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            TextRenderer.DrawText(g, "CAIXA FECHADO", Tema.Semibold(17), new Rectangle(70, 8, Width - 90, 30), Tema.Perigo, Linha);
            TextRenderer.DrawText(g, "Por favor, dirija-se ao próximo caixa", Tema.Fonte(11), new Rectangle(71, 38, Width - 90, 24), Tema.Texto, Linha);
        }
    }
}
