namespace ProjetoJP.Controles;

/// <summary>
/// Navegacao padronizada para os tres aplicativos desktop. Totalmente WinForms,
/// sem WebView, imagens externas ou bibliotecas adicionais.
/// </summary>
public static class NavegacaoERP
{
    public readonly record struct Acao(string Chave, string Texto, string Icone, Action Executar);

    public static Control Lateral(string modulo, Icon? iconeOriginal, bool compacto,
        IEnumerable<Acao> acoes, string inicial, int alturaLinha = 46)
    {
        var largura = compacto ? 70 : 204;
        var barra = new Panel { Dock = DockStyle.Left, Width = largura, BackColor = Tema.Barra };
        var cabecalho = new Panel { Dock = DockStyle.Top, Height = compacto ? 84 : 92, BackColor = Tema.Barra };
        if (iconeOriginal is not null)
            cabecalho.Controls.Add(new PictureBox
            {
                Image = iconeOriginal.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent, Size = new Size(34, 34),
                Location = new Point(compacto ? 18 : 16, 17)
            });
        if (!compacto)
        {
            cabecalho.Controls.Add(new Label
            {
                Text = Aparencia.NomePadrao + " " + modulo, ForeColor = Color.White, Font = Tema.Semibold(12.5f),
                AutoSize = false, Size = new Size(largura - 62, 48),
                Location = new Point(60, 20), AutoEllipsis = true
            });
            cabecalho.Controls.Add(new Label
            {
                Text = "NAVEGAÇÃO", ForeColor = Tema.BarraTextoSuave,
                Font = Tema.Fonte(8), AutoSize = true, Location = new Point(18, 70)
            });
        }
        // Panel com posicionamento vertical: evita a barra horizontal gerada pelo FlowLayoutPanel
        // quando aparece a barra de rolagem vertical ou muda a escala do Windows.
        var lista = new Panel
        {
            Dock = DockStyle.Fill, AutoScroll = true, BackColor = Tema.Barra
        };
        var posicaoY = 7;
        var botoes = new Dictionary<string, Panel>(StringComparer.OrdinalIgnoreCase);
        var dicas = new ToolTip();
        barra.Disposed += (_, _) => dicas.Dispose();
        void Marcar(string chave)
        {
            foreach (var (id, painel) in botoes)
            {
                painel.BackColor = id == chave ? Tema.BarraHover : Tema.Barra;
                foreach (Control filho in painel.Controls)
                    filho.BackColor = painel.BackColor;
            }
        }
        foreach (var acao in acoes)
        {
            var painel = new Panel
            {
                BackColor = Tema.Barra, Size = new Size(largura - 20, alturaLinha),
                Location = new Point(5, posicaoY), Cursor = Cursors.Hand,
                AccessibleName = acao.Texto
            };
            var icone = new Label
            {
                Text = acao.Icone, Font = Tema.FonteIcone(16), ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter, BackColor = Tema.Barra,
                Location = new Point(compacto ? 0 : 9, 0), Size = new Size(compacto ? painel.Width : 34, alturaLinha),
                Cursor = Cursors.Hand
            };
            painel.Controls.Add(icone);
            Label? textoItem = null;
            if (!compacto)
            {
                textoItem = new Label
                {
                    Text = acao.Texto, Font = Tema.Fonte(10), ForeColor = Color.White,
                    BackColor = Tema.Barra, TextAlign = ContentAlignment.MiddleLeft,
                    Location = new Point(46, 0), Size = new Size(painel.Width - 49, alturaLinha),
                    AutoEllipsis = true, Cursor = Cursors.Hand
                };
                painel.Controls.Add(textoItem);
            }
            painel.Resize += (_, _) =>
            {
                if (compacto) icone.Width = painel.Width;
                else if (textoItem is not null) textoItem.Width = Math.Max(12, painel.Width - 49);
            };
            void Ativar(object? _, EventArgs __) { Marcar(acao.Chave); acao.Executar(); }
            painel.Click += Ativar;
            foreach (Control filho in painel.Controls) filho.Click += Ativar;
            dicas.SetToolTip(painel, acao.Texto);
            foreach (Control filho in painel.Controls) dicas.SetToolTip(filho, acao.Texto);
            botoes[acao.Chave] = painel;
            lista.Controls.Add(painel);
            posicaoY += alturaLinha + 3;
        }
        lista.AutoScrollMinSize = new Size(0, posicaoY + 5);
        void AjustarLargura()
        {
            var disponivel = Math.Max(44, lista.ClientSize.Width - 10 -
                (lista.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0));
            foreach (var painel in botoes.Values) painel.Width = disponivel;
        }
        lista.Resize += (_, _) => AjustarLargura();
        AjustarLargura();
        barra.Controls.Add(lista);
        barra.Controls.Add(cabecalho);
        Marcar(inicial);
        return barra;
    }

    public static Control Superior(IEnumerable<Acao> acoes)
    {
        var barra = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 50, BackColor = Tema.Superficie,
            WrapContents = false, AutoScroll = true,
            Padding = new Padding(12, 7, 8, 0)
        };
        foreach (var acao in acoes)
        {
            var botao = new BotaoTema
            {
                Text = acao.Texto, Estilo = EstiloBotao.Secundario,
                Icone = acao.Icone, Height = 34,
                Width = Math.Max(104, TextRenderer.MeasureText(acao.Texto, Tema.Fonte(9.5f)).Width + 55),
                Font = Tema.Fonte(9.5f), Margin = new Padding(0, 0, 7, 0), TabStop = false
            };
            botao.Click += (_, _) => acao.Executar();
            barra.Controls.Add(botao);
        }
        return barra;
    }
}
