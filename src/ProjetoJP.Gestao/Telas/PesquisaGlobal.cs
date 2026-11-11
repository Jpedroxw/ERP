using ProjetoJP.Controles;
using ProjetoJP.Gestao.Servicos;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;

namespace ProjetoJP.Gestao.Telas;

/// <summary>
/// Pesquisa da barra superior (Ctrl + F): acha telas e produtos enquanto digita.
/// Os resultados aparecem numa lista logo abaixo do campo (dentro da própria janela, sem roubar o foco).
/// </summary>
internal sealed class PesquisaGlobal : IDisposable
{
    public sealed record Destino(string Chave, string Titulo, string Descricao, string Icone);
    private sealed record Resultado(string Titulo, string Detalhe, string Icone, Action? Abrir);

    private const int AlturaItem = 48;
    private const int MaxItens = 9;

    private readonly Form _dono;
    private readonly CampoBusca _campo;
    private readonly ApiClient _api;
    private readonly IReadOnlyList<Destino> _telas;
    private readonly Action<string> _abrirTela;
    private readonly Action<Guid> _abrirProduto;
    private readonly Panel _painel = new() { Visible = false, BackColor = Tema.Superficie, Padding = new Padding(1) };
    private readonly ListBox _lista = new()
    {
        Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed,
        ItemHeight = AlturaItem, IntegralHeight = false, BackColor = Tema.Superficie, TabStop = false
    };
    private readonly System.Windows.Forms.Timer _atraso = new() { Interval = 200 };
    private List<ProdutoDto>? _produtos;
    private DateTime _produtosEm;
    private bool _carregando;

    public PesquisaGlobal(Form dono, CampoBusca campo, ApiClient api, IReadOnlyList<Destino> telas,
        Action<string> abrirTela, Action<Guid> abrirProduto)
    {
        _dono = dono;
        _campo = campo;
        _api = api;
        _telas = telas;
        _abrirTela = abrirTela;
        _abrirProduto = abrirProduto;

        _painel.Paint += (_, e) =>
        {
            using var borda = new Pen(Tema.Borda);
            e.Graphics.DrawRectangle(borda, 0, 0, _painel.Width - 1, _painel.Height - 1);
        };
        _painel.Controls.Add(_lista);
        _dono.Controls.Add(_painel);

        _lista.DrawItem += Desenhar;
        _lista.MouseClick += (_, e) =>
        {
            var i = _lista.IndexFromPoint(e.Location);
            if (i >= 0) Abrir(i);
        };
        _lista.Leave += (_, _) => Depois(() => { if (!_campo.Caixa.Focused) Fechar(); });

        _campo.Caixa.TextChanged += (_, _) => { _atraso.Stop(); _atraso.Start(); };
        _atraso.Tick += (_, _) => { _atraso.Stop(); Atualizar(); };
        _campo.Caixa.KeyDown += (_, e) =>
        {
            switch (e.KeyCode)
            {
                case Keys.Down when _painel.Visible && _lista.Items.Count > 0:
                    _lista.SelectedIndex = Math.Min(_lista.Items.Count - 1, _lista.SelectedIndex + 1);
                    e.Handled = e.SuppressKeyPress = true;
                    break;
                case Keys.Up when _painel.Visible && _lista.Items.Count > 0:
                    _lista.SelectedIndex = Math.Max(0, _lista.SelectedIndex - 1);
                    e.Handled = e.SuppressKeyPress = true;
                    break;
                case Keys.Enter:
                    _atraso.Stop();
                    if (!_painel.Visible) Atualizar();
                    if (_painel.Visible) Abrir(Math.Max(0, _lista.SelectedIndex));
                    e.Handled = e.SuppressKeyPress = true;
                    break;
                case Keys.Escape:
                    Fechar();
                    _campo.Caixa.Clear();
                    e.Handled = e.SuppressKeyPress = true;
                    break;
            }
        };
        _campo.Caixa.Leave += (_, _) => Depois(() => { if (!_lista.Focused) Fechar(); });
        _campo.Caixa.GotFocus += (_, _) => { if (_campo.Caixa.TextLength >= 2) Atualizar(); };
    }

    /// <summary>Roda depois que o Windows terminar de trocar o foco (e só se a janela ainda existir).</summary>
    private void Depois(Action acao)
    {
        if (_dono.IsDisposed || _dono.Disposing || !_dono.IsHandleCreated) return;
        _dono.BeginInvoke(new Action(() =>
        {
            if (!_dono.IsDisposed && !_painel.IsDisposed) acao();
        }));
    }

    private static readonly HashSet<string> PalavrasVazias = new(["DE", "DA", "DO", "DAS", "DOS", "E", "A", "O", "PARA", "COM"], StringComparer.OrdinalIgnoreCase);

    private static bool TelaCombina(string termo, Destino t)
    {
        var alvo = PesquisaProdutos.Normalizar(t.Titulo + " " + t.Descricao);
        if (alvo.Contains(termo, StringComparison.Ordinal)) return true;
        var palavras = termo.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !PalavrasVazias.Contains(w)).ToList();
        if (palavras.Count == 0) return false;
        var doAlvo = alvo.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // Cada palavra digitada precisa começar igual a alguma palavra da tela (até 6 letras: "movimentacao" ~ "movimentacoes").
        return palavras.All(w =>
        {
            var inicio = w.Length > 6 ? w[..6] : w;
            return doAlvo.Any(a => a.StartsWith(inicio, StringComparison.Ordinal));
        });
    }

    private void Atualizar()
    {
        if (_dono.IsDisposed) return;
        var texto = _campo.Caixa.Text.Trim();
        if (texto.Length < 2)
        {
            Fechar();
            return;
        }

        var resultados = new List<Resultado>();
        var termo = PesquisaProdutos.Normalizar(texto);
        // Por palavras (em qualquer ordem, plural/singular): "movimentação de estoque" acha a tela Estoque,
        // cuja descrição fala em "movimentações". Antes só achava o texto inteiro, do jeito exato.
        foreach (var t in _telas.Where(t => TelaCombina(termo, t)).Take(4))
            resultados.Add(new Resultado(t.Titulo, "Tela  ·  " + t.Descricao, t.Icone, () => _abrirTela(t.Chave)));

        if (_produtos is null || DateTime.Now - _produtosEm > TimeSpan.FromMinutes(2)) _ = CarregarProdutosAsync();
        if (_produtos is not null)
        {
            var achados = _produtos
                .Select(p => (p, pontos: PesquisaProdutos.Pontuar(texto, p.Descricao, p.Codigo, p.CodigoBarras)))
                .Where(x => x.pontos >= 0)
                .OrderByDescending(x => x.pontos).ThenBy(x => x.p.Descricao, StringComparer.CurrentCultureIgnoreCase)
                .Take(MaxItens - resultados.Count)
                .Select(x => x.p);
            foreach (var p in achados)
            {
                var id = p.Id;
                resultados.Add(new Resultado(p.Descricao + (p.Ativo ? "" : "  (inativo)"),
                    $"Produto  ·  código {p.Codigo}  ·  {Formatos.Moeda(p.PrecoVenda)}  ·  estoque {Formatos.Quantidade(p.EstoqueAtual, p.Unidade)} {p.Unidade}",
                    Icones.Produtos, () => _abrirProduto(id)));
            }
        }
        else if (_carregando)
        {
            resultados.Add(new Resultado("Buscando produtos...", "", Icones.Relogio, null));
        }
        if (resultados.Count == 0)
            resultados.Add(new Resultado("Nada encontrado", "Tente outra parte do nome ou o código", Icones.Buscar, null));

        _lista.BeginUpdate();
        _lista.Items.Clear();
        foreach (var r in resultados) _lista.Items.Add(r);
        _lista.SelectedIndex = resultados[0].Abrir is null ? -1 : 0;
        _lista.EndUpdate();
        Mostrar(resultados.Count);
    }

    private async Task CarregarProdutosAsync()
    {
        if (_carregando) return;
        _carregando = true;
        try
        {
            _produtos = await _api.GetAsync<List<ProdutoDto>>("api/produtos");
            _produtosEm = DateTime.Now;
        }
        catch (ApiException) { /* sem conexão: pesquisa só as telas */ }
        finally { _carregando = false; }
        if (!_dono.IsDisposed && _campo.Caixa.Focused && _campo.Caixa.TextLength >= 2) Atualizar();
    }

    private void Mostrar(int quantidade)
    {
        var inicio = _dono.PointToClient(_campo.PointToScreen(new Point(0, _campo.Height + 4)));
        var largura = Math.Max(_campo.Width, 420);
        largura = Math.Min(largura, Math.Max(200, _dono.ClientSize.Width - inicio.X - 12));
        _painel.SetBounds(inicio.X, inicio.Y, largura, Math.Min(quantidade, MaxItens) * AlturaItem + 2);
        _painel.Visible = true;
        _painel.BringToFront();
    }

    private void Fechar()
    {
        if (!_painel.IsDisposed) _painel.Visible = false;
    }

    private void Abrir(int indice)
    {
        if (indice < 0 || indice >= _lista.Items.Count || _lista.Items[indice] is not Resultado { Abrir: { } abrir }) return;
        Fechar();
        _campo.Caixa.Clear();
        abrir();
    }

    private void Desenhar(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || _lista.Items[e.Index] is not Resultado r) return;
        var selecionado = (e.State & DrawItemState.Selected) != 0;
        using (var fundo = new SolidBrush(selecionado ? Tema.PrimariaClara : Tema.Superficie)) e.Graphics.FillRectangle(fundo, e.Bounds);
        var b = e.Bounds;
        TextRenderer.DrawText(e.Graphics, r.Icone, Tema.FonteIcone(13), new Rectangle(b.X + 10, b.Y, 30, b.Height),
            r.Abrir is null ? Tema.TextoSuave : Tema.Primaria, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        const TextFormatFlags linha = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
        TextRenderer.DrawText(e.Graphics, r.Titulo, Tema.Semibold(10), new Rectangle(b.X + 48, b.Y + 6, b.Width - 58, 20), Tema.Texto, linha);
        if (r.Detalhe.Length > 0)
            TextRenderer.DrawText(e.Graphics, r.Detalhe, Tema.Fonte(8.5f), new Rectangle(b.X + 48, b.Y + 26, b.Width - 58, 18), Tema.TextoSuave, linha);
    }

    public void Dispose() => _atraso.Dispose();
}
