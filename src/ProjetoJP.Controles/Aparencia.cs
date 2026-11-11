using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;
using ProjetoJP.Shared;
using Microsoft.Win32;

namespace ProjetoJP.Controles;

public enum OpcaoTema
{
    Claro,
    Escuro,
    /// <summary>Segue o Windows (Configurações > Personalização > Cores).</summary>
    Windows
}

/// <summary>
/// Personalização do caixa e da Retaguarda: tema claro/escuro, cor de destaque, nome da empresa,
/// logo e plano de fundo. Fica em C:\ProgramData\ErpNaoFiscal\aparencia (aparencia.json + imagens)
/// e vale para os dois programas deste computador. É aplicada ao abrir o programa.
/// Cor: a escolhida; sem escolha, azul-marinho.
/// O ERP Master tem a sua (só tema e cor, em master.json): não usa a da loja.
/// </summary>
public static class Aparencia
{
    public sealed class Dados
    {
        public OpcaoTema Tema { get; set; } = OpcaoTema.Claro;
        /// <summary>Cor de destaque em #RRGGBB. Vazio = azul-marinho.</summary>
        public string? CorDestaque { get; set; }
        /// <summary>Aparece no lugar de "Projeto JP" (login, menu, topo). Vazio = "Projeto JP".</summary>
        public string? NomeEmpresa { get; set; }
        /// <summary>Quanto o plano de fundo aparece (0 a 100). Mais alto = imagem mais forte.</summary>
        public int IntensidadeFundo { get; set; } = 35;
    }

    /// <summary>Tons prontos para a cor de destaque.</summary>
    public static readonly (string Nome, Color Cor)[] Cores =
    [
        ("Azul-marinho", Tema.DestaquePadrao),
        ("Azul", Color.FromArgb(29, 95, 191)),
        ("Verde", Color.FromArgb(22, 128, 82)),
        ("Turquesa", Color.FromArgb(8, 124, 138)),
        ("Roxo", Color.FromArgb(108, 66, 190)),
        ("Rosa", Color.FromArgb(186, 44, 118)),
        ("Vermelho", Color.FromArgb(188, 42, 56)),
        ("Laranja", Color.FromArgb(198, 92, 18)),
        ("Grafite", Color.FromArgb(68, 78, 94))
    ];

    private static string Pasta => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ErpNaoFiscal", "aparencia");
    private static string CaminhoDados => Path.Combine(Pasta, Master ? "master.json" : "aparencia.json");
    private static string CaminhoLogo => Path.Combine(Pasta, "logo.png");
    private static string CaminhoFundo => Path.Combine(Pasta, "fundo.jpg");

    /// <summary>true = ERP Master: só tema e cor, gravados à parte (master.json); sem logo, fundo e nome.</summary>
    public static bool Master { get; private set; }

    /// <summary>Escolhas gravadas (as que estão em uso desde que o programa abriu).</summary>
    public static Dados Atual { get; private set; } = new();

    public static Image? Logo { get; private set; }
    public static Image? Fundo { get; private set; }

    /// <summary>Nome do sistema (a marca). Os módulos vêm depois: "Projeto JP PDV", "Projeto JP Gestão", "Projeto JP Master".</summary>
    public const string NomePadrao = "Projeto JP";

    /// <summary>A empresa pôs o nome dela em Aparência (aí ele aparece no lugar de "Projeto JP").</summary>
    public static bool NomePersonalizado => !string.IsNullOrWhiteSpace(Atual.NomeEmpresa);

    /// <summary>Nome para os títulos: o da empresa, ou "Projeto JP".</summary>
    public static string NomeSistema => NomePersonalizado ? Atual.NomeEmpresa!.Trim() : NomePadrao;

    /// <summary>Título de janela: "Projeto JP PDV", ou "Mercado Bom Preço — PDV" com o nome da empresa.</summary>
    public static string Titulo(string modulo) => NomePersonalizado ? $"{NomeSistema} — {modulo}" : $"{NomePadrao} {modulo}";

    public static string Nome(OpcaoTema o) => o switch
    {
        OpcaoTema.Escuro => "Escuro",
        OpcaoTema.Windows => "Igual ao Windows",
        _ => "Claro"
    };

    // ---------- Início ----------

    /// <summary>
    /// Lê a personalização e aplica: cores do sistema (Tema) e dos controles do Windows (caixas de texto,
    /// listas, barras de rolagem, barra de título). Chamar no início do Main, antes de abrir qualquer janela.
    /// </summary>
    public static void Iniciar(bool master = false)
    {
        Master = master;
        Atual = LerDados();
        if (master)
            Atual.NomeEmpresa = null;
        else
        {
            Logo = LerImagem(CaminhoLogo);
            Fundo = LerImagem(CaminhoFundo);
        }

        var escuro = Atual.Tema == OpcaoTema.Escuro || (Atual.Tema == OpcaoTema.Windows && WindowsEstaEscuro());
        Tema.Definir(escuro, LerCor(Atual.CorDestaque) ?? Tema.DestaquePadrao);
#pragma warning disable WFO5001 // modo escuro do WinForms (.NET 9+)
        Application.SetColorMode(escuro ? SystemColorMode.Dark : SystemColorMode.Classic);
#pragma warning restore WFO5001
    }

    // ---------- Gravação ----------

    /// <summary>
    /// Grava as escolhas. logo/fundo: caminho de uma imagem nova, "" para remover, null para manter.
    /// As imagens são copiadas (e reduzidas) para a pasta do sistema. Devolve true se algo mudou.
    /// </summary>
    public static bool Salvar(Dados novos, string? logo, string? fundo)
    {
        if (Master) { logo = null; fundo = null; novos.NomeEmpresa = null; }
        Directory.CreateDirectory(Pasta);
        var mudou = JsonSerializer.Serialize(novos) != JsonSerializer.Serialize(Atual) || logo is not null || fundo is not null;
        if (!mudou) return false;

        if (logo is not null) GravarImagem(logo, CaminhoLogo, 512, ImageFormat.Png);
        if (fundo is not null) GravarImagem(fundo, CaminhoFundo, 1920, ImageFormat.Jpeg);
        GravacaoAtomica.Texto(CaminhoDados, JsonSerializer.Serialize(novos, new JsonSerializerOptions { WriteIndented = true }));
        Atual = novos;
        return true;
    }

    private static void GravarImagem(string origem, string destino, int maximo, ImageFormat formato)
    {
        if (origem.Length == 0)
        {
            if (File.Exists(destino)) File.Delete(destino);
            return;
        }
        using var img = CarregarImagem(origem) ?? throw new InvalidOperationException("Não foi possível abrir a imagem.");
        var escala = Math.Min(1.0, (double)maximo / Math.Max(img.Width, img.Height));
        var w = Math.Max(1, (int)(img.Width * escala));
        var h = Math.Max(1, (int)(img.Height * escala));
        using var bmp = new Bitmap(w, h, formato == ImageFormat.Png ? PixelFormat.Format32bppArgb : PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            if (formato != ImageFormat.Png) g.Clear(Color.White);
            g.DrawImage(img, 0, 0, w, h);
        }
        var temp = destino + ".tmp";
        bmp.Save(temp, formato);
        File.Move(temp, destino, overwrite: true);
    }

    /// <summary>Reabre o mesmo executável; encerra esta instância apenas se a nova iniciou.</summary>
    public static void Reiniciar()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            throw new InvalidOperationException("Não foi possível localizar o executável atual.");
        var novo = Process.Start(new ProcessStartInfo(exe)
        {
            UseShellExecute = true,
            WorkingDirectory = AppContext.BaseDirectory
        });
        if (novo is null)
            throw new InvalidOperationException("O Windows não conseguiu iniciar uma nova instância.");
        Environment.Exit(0);
    }

    // ---------- Uso nas telas ----------

    /// <summary>
    /// Marca para os cantos das telas: o logo da empresa (se houver) ou o ícone padrão em branco.
    /// </summary>
    public static Control Marca(string iconePadrao, int tamanho, Point local)
    {
        if (Logo is not null)
        {
            return new PictureBox
            {
                Image = Logo, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent,
                Size = new Size(tamanho, tamanho), Location = local
            };
        }
        return new Label
        {
            Text = iconePadrao, Font = Tema.FonteIcone(tamanho * 0.55f), ForeColor = Color.White,
            BackColor = Color.Transparent, AutoSize = true, Location = new Point(local.X, local.Y + tamanho / 8)
        };
    }

    /// <summary>
    /// Põe o plano de fundo no controle (se houver), coberto por uma camada da cor de fundo para o texto
    /// continuar legível. Os painéis de dentro que devem deixar a imagem aparecer precisam de BackColor transparente.
    /// </summary>
    public static void AplicarFundo(Control alvo, float? intensidade = null)
    {
        if (Fundo is null) return;
        var forca = Math.Clamp(intensidade ?? Atual.IntensidadeFundo / 100f, 0.05f, 1f);
        var corBase = alvo.BackColor.A == 255 ? alvo.BackColor : Tema.Fundo;
        Size ultimo = Size.Empty;
        DeixarVerFundo(alvo, corBase);

        void Atualizar()
        {
            var tamanho = alvo.ClientSize;
            if (tamanho.Width < 10 || tamanho.Height < 10 || tamanho == ultimo) return;
            ultimo = tamanho;
            var antigo = alvo.BackgroundImage;
            alvo.BackgroundImage = Cobrir(Fundo!, tamanho, corBase, forca);
            alvo.BackgroundImageLayout = ImageLayout.None;
            antigo?.Dispose();
        }

        // Redesenha a imagem só quando o tamanho para de mudar (evita travar ao redimensionar).
        var espera = new System.Windows.Forms.Timer { Interval = 120 };
        espera.Tick += (_, _) => { espera.Stop(); Atualizar(); };
        alvo.Resize += (_, _) => { espera.Stop(); espera.Start(); };
        alvo.HandleCreated += (_, _) => Atualizar();
        alvo.Disposed += (_, _) => espera.Dispose();
        if (alvo.IsHandleCreated) Atualizar();
    }

    /// <summary>
    /// Painéis e textos de dentro com a mesma cor do fundo ficam transparentes, para a imagem aparecer.
    /// Cartões (CartaoPainel), grades e campos continuam sólidos, para tudo continuar legível.
    /// </summary>
    private static void DeixarVerFundo(Control raiz, Color cor)
    {
        foreach (Control c in raiz.Controls)
        {
            if (c is CartaoPainel or DataGridView or TextBoxBase or ComboBox or ListBox or BotaoTema) continue;
            var mesmaCor = c.BackColor.ToArgb() == cor.ToArgb();
            if (!mesmaCor) continue;
            if (c is Panel or Label or CheckBox or RadioButton or PictureBox)
            {
                c.BackColor = Color.Transparent;
                DeixarVerFundo(c, cor);
            }
        }
    }

    /// <summary>Imagem cortada para cobrir o tamanho (sem distorcer) com a camada da cor por cima.</summary>
    private static Bitmap Cobrir(Image img, Size tamanho, Color camada, float forcaImagem)
    {
        var bmp = new Bitmap(tamanho.Width, tamanho.Height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        var escala = Math.Max((double)tamanho.Width / img.Width, (double)tamanho.Height / img.Height);
        var w = (int)Math.Ceiling(img.Width * escala);
        var h = (int)Math.Ceiling(img.Height * escala);
        g.DrawImage(img, (tamanho.Width - w) / 2, (tamanho.Height - h) / 2, w, h);
        using var pincel = new SolidBrush(Color.FromArgb((int)(255 * (1 - forcaImagem)), camada));
        g.FillRectangle(pincel, 0, 0, tamanho.Width, tamanho.Height);
        return bmp;
    }

    // ---------- Leitura ----------

    public static Color? LerCor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try { return ColorTranslator.FromHtml(hex.Trim()); } catch (Exception) { return null; }
    }

    public static string ParaHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>Abre uma imagem sem deixar o arquivo preso (dá para trocar/apagar depois).</summary>
    public static Image? CarregarImagem(string caminho)
    {
        try
        {
            using var fs = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var img = Image.FromStream(fs);
            return new Bitmap(img);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Image? LerImagem(string caminho) => File.Exists(caminho) ? CarregarImagem(caminho) : null;

    private static Dados LerDados()
    {
        try
        {
            if (File.Exists(CaminhoDados))
                return JsonSerializer.Deserialize<Dados>(File.ReadAllText(CaminhoDados)) ?? new Dados();
        }
        catch (Exception)
        {
            // arquivo estranho: volta ao padrão
        }
        return new Dados();
    }

    private static bool WindowsEstaEscuro()
    {
        try
        {
            using var chave = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return chave?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Caminhos das imagens gravadas (para a prévia na tela de Aparência).</summary>
    internal static string ArquivoLogo => CaminhoLogo;
    internal static string ArquivoFundo => CaminhoFundo;
}
