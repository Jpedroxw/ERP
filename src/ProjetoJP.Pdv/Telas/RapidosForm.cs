using System.Drawing.Drawing2D;
using ProjetoJP.Controles;
using ProjetoJP.Pdv.Dados;
using ProjetoJP.Shared;

namespace ProjetoJP.Pdv.Telas;

/// <summary>
/// Botões rápidos: produtos sem código de barras (pão, salgados, hortifrúti) em botões grandes, para tocar ou
/// apertar o número (1 a 9) e vender. A lista vem das Opções do caixa ou, sem escolha, dos mais vendidos sem código.
/// </summary>
public class RapidosForm : Form
{
    private const int Colunas = 4, Largura = 196, Altura = 84, Espaco = 12, Margem = 20;

    public ProdutoLocal? Escolhido { get; private set; }

    public RapidosForm(List<ProdutoLocal> produtos, bool automaticos)
    {
        var linhas = Math.Max(1, (produtos.Count + Colunas - 1) / Colunas);
        var alturaGrade = linhas * Altura + (linhas - 1) * Espaco;
        Ui.Dialogo(this, "Botões rápidos", Margem * 2 + Colunas * Largura + (Colunas - 1) * Espaco, Margem + alturaGrade + 84);

        for (var i = 0; i < produtos.Count; i++)
        {
            var p = produtos[i];
            var b = new BotaoRapido(i < 9 ? (i + 1).ToString() : "", p);
            b.SetBounds(Margem + i % Colunas * (Largura + Espaco), Margem + i / Colunas * (Altura + Espaco), Largura, Altura);
            b.Click += (_, _) => Escolher(p);
            Controls.Add(b);
        }

        var dica = new Label
        {
            Text = automaticos
                ? "Produtos sem código de barras mais vendidos neste caixa. Para escolher os botões: Opções do caixa → Botões rápidos."
                : "Toque no produto ou aperte o número. Os botões são escolhidos em Opções do caixa → Botões rápidos.",
            Font = Tema.Fonte(9), ForeColor = Tema.TextoSuave, AutoSize = false
        };
        dica.SetBounds(Margem, Margem + alturaGrade + 16, ClientSize.Width - Margem * 2 - 160, 40);
        var fechar = Ui.Botao("Fechar   (Esc)", Ui.Neutro, 150);
        fechar.Location = new Point(ClientSize.Width - Margem - 150, Margem + alturaGrade + 20);
        fechar.DialogResult = DialogResult.Cancel;
        CancelButton = fechar;
        Controls.AddRange([dica, fechar]);

        // 1 a 9 (teclado de cima ou numérico) escolhem o botão com esse número.
        KeyDown += (_, e) =>
        {
            var n = e.KeyCode is >= Keys.D1 and <= Keys.D9 ? e.KeyCode - Keys.D1
                  : e.KeyCode is >= Keys.NumPad1 and <= Keys.NumPad9 ? e.KeyCode - Keys.NumPad1 : -1;
            if (n < 0 || n >= produtos.Count) return;
            e.SuppressKeyPress = true;
            Escolher(produtos[n]);
        };
    }

    private void Escolher(ProdutoLocal p)
    {
        Escolhido = p;
        DialogResult = DialogResult.OK;
    }

    /// <summary>Botão grande: número, nome (até duas linhas) e preço por unidade.</summary>
    private sealed class BotaoRapido : Control
    {
        private readonly string _numero;
        private readonly ProdutoLocal _produto;
        private bool _hover;

        public BotaoRapido(string numero, ProdutoLocal produto)
        {
            _numero = numero;
            _produto = produto;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Cursor = Cursors.Hand;
            TabStop = false;
            AccessibleName = produto.Descricao;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Tema.Superficie);
            var quadro = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var caminho = Tema.Arredondado(quadro, 10))
            {
                using var fundo = new SolidBrush(_hover ? Tema.PrimariaClara : Tema.Fundo);
                using var borda = new Pen(_hover ? Tema.Primaria : Tema.Borda, _hover ? 2 : 1);
                g.FillPath(fundo, caminho);
                g.DrawPath(borda, caminho);
            }

            var x = 12;
            if (_numero.Length > 0)
            {
                var fonteNumero = Tema.Semibold(10); // fontes do tema ficam em cache: não descartar
                var circulo = new Rectangle(10, 10, 22, 22);
                using (var cor = new SolidBrush(Tema.Primaria)) g.FillEllipse(cor, circulo);
                TextRenderer.DrawText(g, _numero, fonteNumero, circulo, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                x = 40;
            }
            var fonteNome = Tema.Semibold(10.5f);
            TextRenderer.DrawText(g, _produto.Descricao, fonteNome, new Rectangle(x, 8, Width - x - 10, 42), Tema.Texto,
                TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);
            var fontePreco = Tema.Semibold(12);
            var preco = Formatos.Moeda(_produto.PrecoVenda) + (Formatos.UnidadeFracionada(_produto.Unidade) ? " / " + _produto.Unidade : "");
            TextRenderer.DrawText(g, preco, fontePreco, new Rectangle(12, Height - 32, Width - 24, 24),
                _produto.EmPromocao ? Tema.Sucesso : Tema.Primaria, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }
}
