using System.Drawing.Drawing2D;

namespace ProjetoJP.Controles;

/// <summary>
/// Cores, fontes e ícones usados pela Retaguarda e pelo caixa, com tema claro e escuro.
/// Mudar uma cor aqui muda os dois programas. O tema é escolhido no início (veja <see cref="Aparencia"/>):
/// as telas leem as cores na hora em que são montadas.
/// </summary>
public static class Tema
{
    /// <summary>true = tema escuro em uso.</summary>
    public static bool ModoEscuro { get; private set; }

    // Fundos
    public static Color Fundo { get; private set; }
    public static Color Superficie { get; private set; }
    public static Color SuperficieAlternada { get; private set; }
    public static Color CabecalhoGrade { get; private set; }
    public static Color Borda { get; private set; }

    // Texto
    public static Color Texto { get; private set; }
    public static Color TextoSuave { get; private set; }

    // Cor principal (botões, destaques, total do caixa): azul-marinho ou a cor escolhida
    public static Color Primaria { get; private set; }
    public static Color PrimariaEscura { get; private set; }
    public static Color PrimariaClara { get; private set; }

    // Barras escuras (menu lateral, topo do caixa, login)
    public static Color Barra { get; private set; }
    public static Color BarraHover { get; private set; }
    public static Color BarraTexto { get; private set; }
    public static Color BarraTextoSuave { get; private set; }

    // Situações
    public static Color Sucesso { get; private set; }
    public static Color SucessoClaro { get; private set; }
    public static Color Perigo { get; private set; }
    public static Color PerigoClaro { get; private set; }
    public static Color Alerta { get; private set; }
    public static Color AlertaClaro { get; private set; }

    /// <summary>Cor de destaque padrão (azul-marinho): usada quando não há cor escolhida nem logo colorida.</summary>
    public static readonly Color DestaquePadrao = Color.FromArgb(30, 78, 140);

    static Tema() => Definir(escuro: false);

    /// <summary>
    /// Troca a paleta. Chamar antes de abrir as janelas (as já abertas não mudam).
    /// Com cor de destaque, o azul (botões, total do caixa, seleção) e as barras escuras passam a usar essa cor.
    /// </summary>
    public static void Definir(bool escuro, Color? destaque = null)
    {
        ModoEscuro = escuro;
        if (!escuro)
        {
            // Padrão: fundo cinza-azulado claro, botões azul-marinho e menu azul-noite.
            Fundo = Color.FromArgb(244, 246, 249);
            Superficie = Color.FromArgb(255, 255, 255);
            SuperficieAlternada = Color.FromArgb(247, 249, 252);
            CabecalhoGrade = Color.FromArgb(238, 242, 247);
            Borda = Color.FromArgb(220, 226, 234);
            Texto = Color.FromArgb(27, 36, 48);
            TextoSuave = Color.FromArgb(95, 107, 122);
            Primaria = DestaquePadrao;
            PrimariaEscura = Color.FromArgb(22, 59, 107);
            PrimariaClara = Color.FromArgb(231, 238, 248);
            Barra = Color.FromArgb(20, 33, 61);
            BarraHover = Color.FromArgb(34, 50, 90);
            BarraTexto = Color.FromArgb(230, 235, 243);
            BarraTextoSuave = Color.FromArgb(154, 168, 189);
            Sucesso = Color.FromArgb(30, 127, 79);
            SucessoClaro = Color.FromArgb(228, 244, 235);
            Perigo = Color.FromArgb(180, 35, 24);
            PerigoClaro = Color.FromArgb(252, 233, 231);
            Alerta = Color.FromArgb(161, 92, 7);
            AlertaClaro = Color.FromArgb(255, 243, 224);
        }
        else
        {
            Fundo = Color.FromArgb(17, 21, 28);
            Superficie = Color.FromArgb(26, 32, 42);
            SuperficieAlternada = Color.FromArgb(31, 38, 50);
            CabecalhoGrade = Color.FromArgb(35, 43, 56);
            Borda = Color.FromArgb(52, 62, 78);
            Texto = Color.FromArgb(232, 236, 242);
            TextoSuave = Color.FromArgb(150, 162, 180);
            Primaria = Color.FromArgb(74, 124, 224);
            PrimariaEscura = Color.FromArgb(56, 100, 196);
            PrimariaClara = Color.FromArgb(30, 44, 72);
            Barra = Color.FromArgb(12, 16, 24);
            BarraHover = Color.FromArgb(32, 40, 56);
            BarraTexto = Color.FromArgb(226, 232, 240);
            BarraTextoSuave = Color.FromArgb(140, 152, 172);
            Sucesso = Color.FromArgb(74, 184, 124);
            SucessoClaro = Color.FromArgb(24, 48, 36);
            Perigo = Color.FromArgb(240, 113, 103);
            PerigoClaro = Color.FromArgb(66, 30, 28);
            Alerta = Color.FromArgb(230, 170, 70);
            AlertaClaro = Color.FromArgb(60, 45, 20);
        }

        if (destaque is { } d && d.ToArgb() != DestaquePadrao.ToArgb())
        {
            // Texto branco precisa ser legível em cima da cor: escurece cores muito claras (amarelo, verde-limão...)
            var cor = Color.FromArgb(255, d);
            while (Luminancia(cor) > 0.45) cor = Escurecer(cor, 0.1f);

            Primaria = escuro ? Clarear(cor, 0.12f) : cor;
            PrimariaEscura = Escurecer(Primaria, 0.22f);
            PrimariaClara = escuro ? Misturar(cor, Superficie, 0.30f) : Misturar(cor, Color.White, 0.11f);
            Barra = escuro ? Misturar(cor, Color.FromArgb(8, 10, 14), 0.14f) : Misturar(cor, Color.FromArgb(10, 15, 24), 0.24f);
            BarraHover = Clarear(Barra, 0.09f);
        }
    }

    /// <summary>Mistura "parte" da cor a sobre a cor b (0 = só b, 1 = só a).</summary>
    public static Color Misturar(Color a, Color b, float parte) => Color.FromArgb(
        (int)(a.R * parte + b.R * (1 - parte)), (int)(a.G * parte + b.G * (1 - parte)), (int)(a.B * parte + b.B * (1 - parte)));

    private static double Luminancia(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    /// <summary>
    /// Pinta o fundo do controle igual ao do pai — inclusive quando o pai tem imagem de fundo
    /// (plano de fundo personalizado) ou é transparente. Usado pelos controles com cantos arredondados.
    /// </summary>
    public static void PintarFundoDoPai(Graphics g, Control c)
    {
        var pai = c.Parent;
        if (pai is not null && (pai.BackColor.A < 255 || pai.BackgroundImage is not null))
            ButtonRenderer.DrawParentBackground(g, c.ClientRectangle, c);
        else
            g.Clear(pai?.BackColor ?? Fundo);
    }

    private static readonly Dictionary<(string, float, FontStyle), Font> Fontes = new();

    /// <summary>Fontes guardadas em cache: o Windows tem limite de objetos gráficos por programa.</summary>
    private static Font Obter(string familia, float tamanho, FontStyle estilo)
    {
        var chave = (familia, tamanho, estilo);
        if (!Fontes.TryGetValue(chave, out var f))
        {
            f = new Font(familia, tamanho, estilo);
            Fontes[chave] = f;
        }
        return f;
    }

    public static Font Fonte(float tamanho, FontStyle estilo = FontStyle.Regular) => Obter("Segoe UI", tamanho, estilo);

    /// <summary>Meio-negrito, para títulos e valores.</summary>
    public static Font Semibold(float tamanho) => Obter("Segoe UI Semibold", tamanho, FontStyle.Regular);

    /// <summary>Fonte de ícones do Windows 10/11 (Segoe MDL2 Assets).</summary>
    public static Font FonteIcone(float tamanho) => Obter("Segoe MDL2 Assets", tamanho, FontStyle.Regular);

    public static GraphicsPath Arredondado(Rectangle r, int raio)
    {
        var p = new GraphicsPath();
        if (raio <= 0 || r.Width < raio * 2 || r.Height < raio * 2)
        {
            p.AddRectangle(r);
            return p;
        }
        var d = raio * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static Color Escurecer(Color c, float fator = 0.12f) =>
        Color.FromArgb(c.A, (int)(c.R * (1 - fator)), (int)(c.G * (1 - fator)), (int)(c.B * (1 - fator)));

    public static Color Clarear(Color c, float fator = 0.12f) =>
        Color.FromArgb(c.A, c.R + (int)((255 - c.R) * fator), c.G + (int)((255 - c.G) * fator), c.B + (int)((255 - c.B) * fator));

    /// <summary>Cor de "passar o mouse" sobre um fundo: escurece no tema claro e clareia no escuro.</summary>
    public static Color Realce(Color c, float fator = 0.06f) => ModoEscuro ? Clarear(c, fator) : Escurecer(c, fator);

    /// <summary>
    /// Etiqueta de situação (Ativo, Estoque baixo, Saída...): fundo claro da cor, bolinha e texto na cor.
    /// Desenha centralizada verticalmente em "area", alinhada à esquerda. Devolve a largura usada.
    /// </summary>
    public static int DesenharEtiqueta(Graphics g, Rectangle area, string texto, Color cor, Font? fonte = null, bool bolinha = true)
    {
        fonte ??= Semibold(8.5f);
        var tamanho = TextRenderer.MeasureText(g, texto, fonte, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        var altura = Math.Min(area.Height - 4, tamanho.Height + 8);
        var largura = Math.Min(area.Width, tamanho.Width + (bolinha ? 26 : 16));
        var r = new Rectangle(area.X, area.Y + (area.Height - altura) / 2, largura, altura);
        var anterior = g.SmoothingMode;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var caminho = Arredondado(r, altura / 2))
        using (var fundo = new SolidBrush(Color.FromArgb(ModoEscuro ? 70 : 34, cor)))
            g.FillPath(fundo, caminho);
        var corTexto = ModoEscuro ? Clarear(cor, 0.25f) : Escurecer(cor, 0.12f);
        var x = r.X + 8;
        if (bolinha)
        {
            using var ponto = new SolidBrush(cor);
            g.FillEllipse(ponto, x, r.Y + r.Height / 2 - 3, 6, 6);
            x += 11;
        }
        g.SmoothingMode = anterior;
        TextRenderer.DrawText(g, texto, fonte, new Rectangle(x, r.Y, r.Right - x, r.Height), corTexto,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        return largura;
    }

    /// <summary>Visual padrão das grades: linhas finas, cabeçalho cinza-claro, seleção azul-clara.</summary>
    public static void EstilizarGrade(DataGridView g, float tamanhoFonte = 10)
    {
        g.BackgroundColor = Superficie;
        g.BorderStyle = BorderStyle.None;
        g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        g.GridColor = Borda;
        g.RowHeadersVisible = false;
        g.EnableHeadersVisualStyles = false;
        g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        g.ColumnHeadersHeight = 36;
        g.Font = Fonte(tamanhoFonte);

        var cab = g.ColumnHeadersDefaultCellStyle;
        cab.BackColor = CabecalhoGrade;
        cab.ForeColor = TextoSuave;
        cab.SelectionBackColor = CabecalhoGrade;
        cab.SelectionForeColor = TextoSuave;
        cab.Font = Semibold(tamanhoFonte - 0.5f);
        cab.Padding = new Padding(8, 0, 8, 0);

        var cel = g.DefaultCellStyle;
        cel.BackColor = Superficie;
        cel.ForeColor = Texto;
        cel.SelectionBackColor = PrimariaClara;
        cel.SelectionForeColor = Texto;
        cel.Padding = new Padding(8, 0, 8, 0);

        g.AlternatingRowsDefaultCellStyle.BackColor = SuperficieAlternada;
        g.RowTemplate.Height = (int)(tamanhoFonte * 3.1f);
    }
}

/// <summary>Ícones da fonte Segoe MDL2 Assets (vem com o Windows 10 e 11).</summary>
public static class Icones
{
    public const string Inicio = "";
    public const string Novo = "";
    public const string Editar = "";
    public const string Excluir = "";
    public const string Remover = "";
    public const string Atualizar = "";
    public const string Buscar = "";
    public const string Salvar = "";
    public const string Cancelar = "";
    public const string Configuracoes = "";
    public const string Produtos = "";
    public const string Clientes = "";
    public const string Pessoa = "";
    public const string Estoque = "";
    public const string Relatorios = "";
    public const string Pagamento = "";
    public const string Categoria = "";
    public const string Terminal = "";
    public const string Imprimir = "";
    public const string Carrinho = "";
    public const string Sincronizar = "";
    public const string Sair = "";
    public const string Exportar = "";
    public const string Voltar = "";
    public const string Servidor = "";
    public const string Aviso = "";
    public const string Ok = "";
    public const string Cadeado = "";
    public const string Calculadora = "";
    public const string Documento = "";
    public const string Relogio = "";
    public const string Grade = "";
    public const string Alternar = "";
    public const string Colar = "\uE77F";
    public const string Loja = "\uE719";
    public const string Importar = "\uE8B5";
    public const string Banco = "\uE1D3";
    public const string Lua = "\uE708";
    public const string Sol = "\uE706";
    public const string ChevronBaixo = "\uE70D";
    public const string ChevronCima = "\uE70E";
    public const string Calendario = "\uE787";
    public const string Camera = "\uE722";
    public const string Etiqueta = "\uE8EC";
    public const string Fornecedor = "\uE821";
    public const string Financeiro = "\uE8C7";
    public const string Mais = "\uE712";
}
