using System.Drawing.Printing;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using ProjetoJP.Controles;
using ProjetoJP.Pdv.Dados;
using ProjetoJP.Pdv.Servicos;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;

namespace ProjetoJP.Pdv.Telas;

/// <summary>
/// Configurações do caixa escolhidas na tela: servidor, chave do terminal, impressora,
/// largura do papel, acentuação, corte, gaveta e intervalo de sincronização.
/// </summary>
public class ConfiguracaoForm : Form
{
    private sealed record Opcao<T>(string Texto, T Valor)
    {
        public override string ToString() => Texto;
    }

    private const string SemImpressora = "(nenhuma — salvar o cupom em arquivo .txt)";

    private readonly PdvConfig _original;

    private readonly TextBox _url = Ui.Caixa(11);
    /// <summary>Servidor salvo: nome (nuvem) aparece; endereço de site (versões antigas) fica escondido.</summary>
    private string _servidorAtual = "";
    private bool _digitandoUrl = true;
    // Nome digitado em maiúsculas com espaço; gravado como sempre (minúsculas com hífen: "MERCADO BOM PRECO" → mercado-bom-preco).
    private string ServidorEscolhido => !_digitandoUrl ? _servidorAtual
        : EnderecoServidor.PareceEndereco(_url.Text) ? _url.Text.Trim() : EnderecoServidor.NormalizarNome(_url.Text);
    private readonly TextBox _chave = Ui.Caixa(11);
    private readonly Button _testar = Ui.Botao("Testar conexão", Ui.Neutro, 140, 32, Icones.Servidor);
    private readonly Label _resultado = Ui.Rotulo("", 9.5f);

    private readonly TextBox _loja = Ui.Caixa(11);
    private readonly TextBox _cabecalho = Ui.Caixa(10.5f);

    private readonly ComboBox _impressora = Combo();
    private readonly ComboBox _impressoraA4 = Combo();
    private readonly DataGridView _gradeTeclas = Ui.Grade();
    private readonly Dictionary<AcaoCaixa, Keys> _teclas = new();
    private const string PadraoWindows = "(a impressora padrão do Windows)";
    private readonly ComboBox _largura = Combo();
    private readonly ComboBox _acentos = Combo();
    private readonly CheckBox _cortar = new() { Text = "Cortar o papel no fim do cupom", AutoSize = true };
    private readonly CheckBox _gaveta = new() { Text = "Abrir a gaveta de dinheiro ao finalizar a venda", AutoSize = true };

    private readonly ComboBox _intervalo = Combo();
    private readonly TextBox _pastaCopia = Ui.Caixa(10.5f);
    private readonly Label _resultadoCopia = new() { AutoSize = true, MaximumSize = new Size(520, 0), Font = Tema.Fonte(9.5f) };
    private readonly CheckBox _balancaEtiqueta = new() { Text = "Ler etiqueta (código de 13 dígitos começando com 2)", AutoSize = true };
    private readonly ComboBox _balancaDigitos = Combo();
    private readonly ComboBox _balancaConteudo = Combo();
    private readonly ComboBox _balancaPorta = new() { DropDownStyle = ComboBoxStyle.DropDown, Font = Ui.Fonte(10.5f) };
    private readonly TextBox _balancaBaud = Ui.Caixa(11);
    private readonly TextBox _balancaSimulado = Ui.Caixa(11);
    private sealed record ItemRapido(string Codigo, string Texto)
    {
        public override string ToString() => Texto;
    }

    private const int MaximoRapidos = 16;
    private readonly TextBox _rapidoBusca = Ui.Caixa(10.5f);
    private readonly ListBox _rapidosLista = new() { Font = Ui.Fonte(10.5f), IntegralHeight = false };

    private readonly ComboBox _pixTipo = Combo();
    private readonly TextBox _pixChave = Ui.Caixa(10.5f);
    private readonly TextBox _pixRecebedor = Ui.Caixa(10.5f);
    private readonly TextBox _pixCidade = Ui.Caixa(10.5f);
    private readonly PainelAparencia _aparencia = new();
    private readonly PainelPreferenciasInterface _interface = new("pdv");

    private readonly List<(ItemMenu item, Panel pagina)> _paginas = [];

    private static ComboBox Combo() => new() { DropDownStyle = ComboBoxStyle.DropDownList, Font = Ui.Fonte(10.5f) };

    private const int PaginaServidor = 0, PaginaLoja = 1, PaginaPix = 2;

    public ConfiguracaoForm(PdvConfig cfg, bool primeiraVez)
    {
        _original = cfg;
        Ui.Dialogo(this, primeiraVez ? "Opções do caixa — primeira configuração" : "Opções do caixa", 820, 560);
        if (primeiraVez)
        {
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
        }

        var conteudo = new Panel { Dock = DockStyle.Fill, BackColor = Tema.Superficie, Padding = new Padding(28, 22, 28, 16) };
        var navegacao = MontarNavegacao(primeiraVez);

        AdicionarPagina(conteudo, Icones.Servidor, "Servidor", PaginaDoServidor());
        AdicionarPagina(conteudo, Icones.Loja, "Loja e cupom", PaginaDaLoja());
        AdicionarPagina(conteudo, Icones.Pagamento, "Pix", PaginaDoPix());
        AdicionarPagina(conteudo, Icones.Grade, "Botões rápidos", PaginaDosRapidos());
        AdicionarPagina(conteudo, Icones.Imprimir, "Impressora", PaginaDaImpressora());
        AdicionarPagina(conteudo, Icones.Sincronizar, "Sincronização", PaginaDaSincronizacao());
        AdicionarPagina(conteudo, Icones.Calculadora, "Balança", PaginaDaBalanca());
        AdicionarPagina(conteudo, Icones.Grade, "Teclado", PaginaDoTeclado());
        AdicionarPagina(conteudo, Icones.Lua, "Aparência", PaginaDaAparencia());
        AdicionarPagina(conteudo, Icones.Configuracoes, "Interface", PaginaDaInterface());
        foreach (var (item, _) in _paginas.AsEnumerable().Reverse())
            navegacao.Controls.Add(item); // Dock Top: o último adicionado fica em cima

        Controls.Add(conteudo);
        Controls.Add(navegacao);
        Controls.Add(MontarRodape());

        Preencher(cfg);
        MostrarPagina(PaginaServidor);
    }

    // ---------- Montagem ----------

    private Panel MontarNavegacao(bool primeiraVez)
    {
        var nav = new Panel { Dock = DockStyle.Left, Width = 210, BackColor = Tema.Fundo, Padding = new Padding(0, 72, 0, 0) };
        nav.Paint += (_, e) =>
        {
            using var linha = new Pen(Tema.Borda);
            e.Graphics.DrawLine(linha, nav.Width - 1, 0, nav.Width - 1, nav.Height);
        };
        nav.Controls.Add(new Label { Text = "Opções", Font = Tema.Semibold(15), ForeColor = Tema.Texto, AutoSize = true, Location = new Point(20, 18) });
        nav.Controls.Add(new Label
        {
            Text = primeiraVez ? "Primeira configuração" : "Configuração deste caixa",
            Font = Tema.Fonte(9), ForeColor = Tema.TextoSuave, AutoSize = true, Location = new Point(21, 46)
        });
        return nav;
    }

    private void AdicionarPagina(Panel conteudo, string icone, string titulo, Panel pagina)
    {
        var indice = _paginas.Count;
        var item = new ItemMenu(icone, titulo, claro: true) { Dock = DockStyle.Top };
        item.Click += (_, _) => MostrarPagina(indice);
        pagina.Dock = DockStyle.Fill;
        pagina.Visible = false;
        conteudo.Controls.Add(pagina);
        _paginas.Add((item, pagina));
    }

    private void MostrarPagina(int indice)
    {
        for (var i = 0; i < _paginas.Count; i++)
        {
            _paginas[i].item.Ativo = i == indice;
            _paginas[i].pagina.Visible = i == indice;
        }
    }

    private static Panel NovaPagina(string titulo, string descricao)
    {
        var p = new Panel { BackColor = Tema.Superficie };
        p.Controls.Add(new Label { Text = titulo, Font = Tema.Semibold(15), ForeColor = Tema.Texto, AutoSize = true, Location = new Point(0, 0) });
        p.Controls.Add(new Label { Text = descricao, Font = Tema.Fonte(10), ForeColor = Tema.TextoSuave, AutoSize = true, Location = new Point(1, 32) });
        return p;
    }

    private static Label Rotulo(string texto, int x, int y)
    {
        var l = Ui.Rotulo(texto, 9.5f);
        l.ForeColor = Tema.TextoSuave;
        l.Location = new Point(x, y);
        return l;
    }

    private static Label Dica(string texto, int x, int y) =>
        new() { Text = texto, Font = Tema.Fonte(8.5f), ForeColor = Tema.TextoSuave, AutoSize = true, Location = new Point(x, y) };

    private Panel PaginaDoServidor()
    {
        var p = NovaPagina("Servidor", "Nome do servidor e a chave que identifica este caixa.");

        _url.SetBounds(0, 98, 400, 28);
        _url.KeyPress += (_, e) => { if (!_url.ReadOnly) e.KeyChar = char.ToUpperInvariant(e.KeyChar); }; // nome em maiúsculas
        _testar.Location = new Point(412, 96);
        _testar.Click += async (_, _) => await TestarAsync();

        _chave.SetBounds(0, 188, 400, 28);
        _chave.Font = new Font("Consolas", 11);
        _chave.CharacterCasing = CharacterCasing.Upper;
        _chave.MaxLength = 64;
        // A chave é hexadecimal (0-9 e A-F), como a Retaguarda gera.
        _chave.KeyPress += (_, e) => { if (!char.IsControl(e.KeyChar) && !Uri.IsHexDigit(e.KeyChar)) e.Handled = true; };
        var colar = Ui.Botao("Colar", Ui.Neutro, 140, 32, Icones.Colar);
        colar.Location = new Point(412, 186);
        colar.Click += (_, _) =>
        {
            if (Clipboard.ContainsText())
                _chave.Text = new string(Clipboard.GetText().ToUpperInvariant().Where(Uri.IsHexDigit).ToArray());
        };

        _resultado.Location = new Point(0, 258);
        _resultado.MaximumSize = new Size(552, 0);

        // Só o suporte: cadastra/troca os servidores (nome e site) com a senha técnica.
        var tecnica = new LinkLabel
        {
            Text = "Configuração técnica (suporte)", AutoSize = true, Location = new Point(0, 344), Font = Tema.Fonte(9),
            LinkColor = Tema.TextoSuave, ActiveLinkColor = Tema.Primaria, LinkBehavior = LinkBehavior.HoverUnderline
        };
        tecnica.LinkClicked += (_, _) =>
        {
            if (!ConfigTecnica.Abrir(this)) return;
            if (_digitandoUrl && !EnderecoServidor.EhNome(_url.Text)) _url.Text = EnderecoServidor.ParaTela(EnderecoServidor.NomePadrao);
            Mostrar(null, "Lista de servidores atualizada.");
        };
        p.Controls.Add(tecnica);

        p.Controls.AddRange([
            Rotulo("Servidor", 0, 76), _url, _testar,
            Dica($"Digite o nome do servidor (ex.: {EnderecoServidor.ParaTela(EnderecoServidor.NomePadrao)}).", 0, 130),
            Rotulo("Chave do terminal", 0, 166), _chave, colar,
            Dica("Gerada na Gestão, em Cadastros > Terminais.", 0, 220),
            _resultado
        ]);
        return p;
    }

    private Panel PaginaDaLoja()
    {
        var p = NovaPagina("Loja e cupom", "O que aparece no topo de cada cupom impresso.");

        _loja.SetBounds(0, 98, 552, 28);
        _loja.CharacterCasing = CharacterCasing.Upper;
        _loja.MaxLength = 48;

        _cabecalho.SetBounds(0, 162, 552, 104);
        _cabecalho.Multiline = true;
        _cabecalho.AcceptsReturn = true;
        _cabecalho.MaxLength = 200;

        p.Controls.AddRange([
            Rotulo("Nome da loja", 0, 76), _loja,
            Rotulo("Cabeçalho do cupom (até 4 linhas: endereço, telefone...)", 0, 140), _cabecalho
        ]);
        return p;
    }

    private Panel PaginaDoPix()
    {
        var p = NovaPagina("Pix", "Chave da loja para o QR Code com o valor de cada venda.");

        _pixTipo.SetBounds(0, 98, 200, 30);
        _pixChave.SetBounds(220, 98, 332, 28);
        _pixChave.MaxLength = 77;
        _pixRecebedor.SetBounds(0, 162, 332, 28);
        _pixRecebedor.MaxLength = 25;
        _pixCidade.SetBounds(352, 162, 200, 28);
        _pixCidade.MaxLength = 15;

        var testar = Ui.Botao("Ver QR Code de teste", Ui.Neutro, 190, 32);
        testar.Location = new Point(0, 214);
        testar.Click += (_, _) =>
        {
            var cfg = LerTela();
            var erro = ProblemaNoPix(cfg);
            if (erro is not null) { Ui.Aviso(this, erro); return; }
            using var f = new PixQrForm(cfg, 1m, teste: true);
            f.ShowDialog(this);
        };

        p.Controls.AddRange([
            Rotulo("Tipo da chave", 0, 76), _pixTipo,
            Rotulo("Chave Pix", 220, 76), _pixChave,
            Rotulo("Nome de quem recebe (como no banco)", 0, 140), _pixRecebedor,
            Rotulo("Cidade", 352, 140), _pixCidade,
            testar,
            Dica("Com a chave preenchida, ao pagar em Pix o caixa mostra o QR Code com o valor da venda. O cliente lê com o app", 0, 264),
            Dica("do banco e o dinheiro cai direto na conta da loja. Confira no app do banco que o Pix chegou antes de concluir.", 0, 282),
            Dica("Vale para a forma de pagamento que tem \"PIX\" no nome. Chave vazia = Pix sem QR Code, como antes.", 0, 300),
            Dica("O QR de teste é de R$ 1,00: leia com o celular para conferir o nome e a chave, sem precisar pagar.", 0, 326)
        ]);
        return p;
    }

    private Panel PaginaDosRapidos()
    {
        var p = NovaPagina("Botões rápidos", "Produtos sem código de barras (pão, salgados...) em botões grandes na venda.");

        _rapidoBusca.SetBounds(0, 98, 380, 28);
        _rapidoBusca.PlaceholderText = "Código ou nome do produto";
        var adicionar = Ui.Botao("Adicionar", largura: 150, altura: 32);
        adicionar.Location = new Point(400, 96);
        adicionar.Click += (_, _) => AdicionarRapido();
        _rapidoBusca.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            AdicionarRapido();
        };

        _rapidosLista.SetBounds(0, 140, 380, 210);
        var subir = Ui.Botao("Subir", Ui.Neutro, 150, 32);
        subir.Location = new Point(400, 140);
        subir.Click += (_, _) => MoverRapido(-1);
        var descer = Ui.Botao("Descer", Ui.Neutro, 150, 32);
        descer.Location = new Point(400, 180);
        descer.Click += (_, _) => MoverRapido(1);
        var remover = Ui.Botao("Remover", Ui.Neutro, 150, 32);
        remover.Location = new Point(400, 220);
        remover.Click += (_, _) =>
        {
            var i = _rapidosLista.SelectedIndex;
            if (i < 0) return;
            _rapidosLista.Items.RemoveAt(i);
            if (_rapidosLista.Items.Count > 0) _rapidosLista.SelectedIndex = Math.Min(i, _rapidosLista.Items.Count - 1);
        };
        var limpar = Ui.Botao("Limpar tudo", Ui.Neutro, 150, 32);
        limpar.Location = new Point(400, 260);
        limpar.Click += (_, _) => _rapidosLista.Items.Clear();

        p.Controls.AddRange([
            Rotulo("Produto", 0, 76), _rapidoBusca, adicionar,
            _rapidosLista, subir, descer, remover, limpar,
            Dica($"Até {MaximoRapidos} botões, nesta ordem (os 9 primeiros também pelo número). Na venda: tecla Insert ou \"Rápidos\" no rodapé.", 0, 360),
            Dica("Lista vazia = automático: os produtos sem código de barras mais vendidos neste caixa.", 0, 378)
        ]);
        return p;
    }

    private void AdicionarRapido()
    {
        var texto = _rapidoBusca.Text.Trim();
        if (texto.Length == 0) { _rapidoBusca.Focus(); return; }
        if (_rapidosLista.Items.Count >= MaximoRapidos)
        {
            Ui.Aviso(this, $"São no máximo {MaximoRapidos} botões. Remova um para colocar outro.");
            return;
        }
        ProdutoLocal? produto;
        try
        {
            produto = Repositorio.BuscarProduto(texto);
            if (produto is null)
            {
                var achados = Repositorio.PesquisarProdutos(texto);
                produto = achados.Count switch
                {
                    0 => null,
                    1 => achados[0],
                    _ => EscolherNaLista(achados)
                };
                if (produto is null && achados.Count > 1) return; // desistiu de escolher
            }
        }
        catch (Exception ex)
        {
            Ui.Aviso(this, "Não foi possível consultar os produtos deste caixa: " + ex.Message);
            return;
        }
        if (produto is null)
        {
            Ui.Aviso(this, $"Nenhum produto encontrado com \"{texto}\".\n\nO produto precisa estar ativo e já ter chegado a este caixa.");
            _rapidoBusca.SelectAll();
            _rapidoBusca.Focus();
            return;
        }
        if (_rapidosLista.Items.Cast<ItemRapido>().Any(i => i.Codigo == produto.Codigo))
        {
            Ui.Aviso(this, $"{produto.Descricao} já está nos botões.");
            return;
        }
        _rapidosLista.Items.Add(new ItemRapido(produto.Codigo, $"{produto.Codigo} — {produto.Descricao}"));
        _rapidosLista.SelectedIndex = _rapidosLista.Items.Count - 1;
        _rapidoBusca.Clear();
        _rapidoBusca.Focus();
    }

    /// <summary>Vários produtos com o texto digitado: escolhe um numa lista.</summary>
    private ProdutoLocal? EscolherNaLista(List<ProdutoLocal> produtos)
    {
        using var f = new Form();
        Ui.Dialogo(f, "Escolha o produto", 520, 380);
        var lista = new ListBox { Font = Ui.Fonte(11), IntegralHeight = false };
        lista.SetBounds(20, 20, 480, 280);
        foreach (var p in produtos.Take(200)) lista.Items.Add(new ItemRapido(p.Codigo, $"{p.Codigo} — {p.Descricao}"));
        lista.SelectedIndex = 0;
        var ok = Ui.Botao("Escolher", largura: 150);
        ok.Location = new Point(20, 316);
        ok.DialogResult = DialogResult.OK;
        var cancelar = Ui.Botao("Cancelar", Ui.Neutro, 150);
        cancelar.Location = new Point(190, 316);
        cancelar.DialogResult = DialogResult.Cancel;
        lista.DoubleClick += (_, _) => f.DialogResult = DialogResult.OK;
        f.Controls.AddRange([lista, ok, cancelar]);
        f.AcceptButton = ok;
        f.CancelButton = cancelar;
        if (f.ShowDialog(this) != DialogResult.OK || lista.SelectedItem is not ItemRapido escolhido) return null;
        return produtos.FirstOrDefault(p => p.Codigo == escolhido.Codigo);
    }

    private void MoverRapido(int direcao)
    {
        var i = _rapidosLista.SelectedIndex;
        var j = i + direcao;
        if (i < 0 || j < 0 || j >= _rapidosLista.Items.Count) return;
        var item = _rapidosLista.Items[i];
        _rapidosLista.Items.RemoveAt(i);
        _rapidosLista.Items.Insert(j, item);
        _rapidosLista.SelectedIndex = j;
    }

    /// <summary>Chave preenchida mas com problema (null = tudo certo, ou sem Pix configurado).</summary>
    private static string? ProblemaNoPix(PdvConfig cfg)
    {
        if (cfg.PixChave.Trim().Length == 0)
            return cfg.PixRecebedor.Trim().Length > 0 || cfg.PixCidade.Trim().Length > 0 ? "Informe a chave Pix." : null;
        if (Pix.NormalizarChave(cfg.PixTipo, cfg.PixChave, out var erro) is null) return erro;
        if (Pix.Limpar(cfg.PixRecebedor, 25).Length == 0) return "Informe o nome de quem recebe o Pix, como aparece no banco.";
        if (Pix.Limpar(cfg.PixCidade, 15).Length == 0) return "Informe a cidade da loja.";
        return null;
    }

    private Panel PaginaDaImpressora()
    {
        var p = NovaPagina("Impressora", "Impressora térmica (ESC/POS) instalada no Windows.");

        _impressora.SetBounds(0, 98, 390, 30);
        var teste = Ui.Botao("Imprimir teste", Ui.Neutro, 150, 32, Icones.Imprimir);
        teste.Location = new Point(402, 96);
        teste.Click += (_, _) => ImprimirTeste();

        _largura.SetBounds(0, 166, 265, 30);
        _acentos.SetBounds(285, 166, 265, 30);
        _cortar.Location = new Point(0, 214);
        _cortar.Font = Ui.Fonte(10);
        _gaveta.Location = new Point(0, 242);
        _gaveta.Font = Ui.Fonte(10);

        p.Controls.AddRange([
            Rotulo("Impressora", 0, 76), _impressora, teste,
            Rotulo("Largura do papel", 0, 144), _largura,
            Rotulo("Acentuação", 285, 144), _acentos,
            _cortar, _gaveta,
            Dica("Use Imprimir teste: a linha de números deve caber inteira e os acentos devem sair certos.", 0, 282),
            Rotulo("Impressora para orçamento em folha A4 (jato de tinta, laser ou PDF)", 0, 318), _impressoraA4
        ]);
        _impressoraA4.SetBounds(0, 340, 390, 30);
        p.AutoScroll = true;
        return p;
    }

    private Panel PaginaDaSincronizacao()
    {
        var p = NovaPagina("Sincronização", "Com que frequência o caixa envia vendas e recebe cadastros.");
        _intervalo.SetBounds(0, 98, 200, 30);

        // Cópia de segurança na rede da loja: protege as vendas ainda não enviadas se este computador estragar.
        _pastaCopia.SetBounds(0, 232, 400, 28);
        var procurar = Ui.Botao("Procurar...", Ui.Neutro, 110, 32);
        procurar.Location = new Point(410, 230);
        procurar.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog
            {
                Description = "Pasta da rede da loja para a cópia de segurança do caixa", UseDescriptionForTitle = true
            };
            if (Directory.Exists(_pastaCopia.Text.Trim())) d.InitialDirectory = _pastaCopia.Text.Trim();
            if (d.ShowDialog(this) == DialogResult.OK) _pastaCopia.Text = d.SelectedPath;
        };
        var testarCopia = Ui.Botao("Testar cópia", Ui.Neutro, 130, 32);
        testarCopia.Location = new Point(0, 270);
        _resultadoCopia.Location = new Point(142, 277);
        testarCopia.Click += async (_, _) =>
        {
            var pasta = _pastaCopia.Text.Trim();
            if (pasta.Length == 0)
            {
                MostrarCopia(false, "Informe a pasta da rede (ex.: \\\\RETAGUARDA\\CopiasCaixa).");
                return;
            }
            testarCopia.Enabled = false;
            MostrarCopia(null, "Copiando...");
            var resultado = await Task.Run(() =>
            {
                var ok = CopiaSeguranca.Fazer(pasta, out var mensagem);
                return (ok, mensagem);
            });
            if (IsDisposed) return;
            testarCopia.Enabled = true;
            MostrarCopia(resultado.ok, resultado.mensagem);
        };

        p.Controls.AddRange([
            Rotulo("Sincronizar a cada", 0, 76), _intervalo,
            Dica("Sem internet o caixa continua vendendo; as vendas sobem sozinhas quando a conexão volta.", 0, 142),
            Dica($"Configuração e banco local ficam em {Db.Pasta}", 0, 166),
            Rotulo("Cópia de segurança na rede da loja (opcional)", 0, 210), _pastaCopia, procurar,
            testarCopia, _resultadoCopia,
            Dica("Uma pasta compartilhada em outro computador da loja (ex.: \\\\RETAGUARDA\\CopiasCaixa). O caixa guarda ali uma cópia", 0, 318),
            Dica("do banco depois de cada venda, mesmo sem internet. Se este computador estragar, as vendas que ainda não", 0, 336),
            Dica("subiram podem ser recuperadas a partir dela (veja o guia). Vazio = sem cópia.", 0, 354)
        ]);
        p.AutoScroll = true;
        return p;
    }

    private Panel PaginaDaBalanca()
    {
        var p = NovaPagina("Balança", "Etiqueta com peso no código, ou peso ao vivo pela USB.");
        _balancaEtiqueta.Location = new Point(0, 72);
        _balancaEtiqueta.Font = Ui.Fonte(10);
        _balancaDigitos.SetBounds(0, 124, 200, 30);
        _balancaConteudo.SetBounds(220, 124, 250, 30);
        _balancaPorta.SetBounds(0, 196, 200, 30);
        _balancaBaud.SetBounds(220, 196, 90, 30);
        _balancaSimulado.SetBounds(330, 196, 120, 30);
        var testar = Ui.Botao("Testar peso", Ui.Neutro, 130, 32);
        testar.Location = new Point(0, 246);
        testar.Click += (_, _) =>
        {
            var cfg = LerTela();
            if (ProjetoJP.Pdv.Servicos.LeitorBalanca.TentarLerPeso(cfg, out var kg, out var erro))
                Ui.Aviso(this, $"Peso lido: {kg:0.000} kg");
            else
                Ui.Aviso(this, erro ?? "Não foi possível ler a balança.");
        };
        p.Controls.AddRange([
            _balancaEtiqueta,
            Rotulo("Dígitos do código", 0, 102), _balancaDigitos,
            Rotulo("O restante da etiqueta é", 220, 102), _balancaConteudo,
            Rotulo("Porta USB", 0, 174), _balancaPorta,
            Rotulo("Velocidade", 220, 174), _balancaBaud,
            Rotulo("Peso simulado", 330, 174), _balancaSimulado,
            testar,
            Dica("Exemplo de etiqueta, código 123 e 1,250 kg: 2000123012506. Porta SIMULADA usa o peso simulado, sem aparelho.", 0, 292)
        ]);
        return p;
    }

    private Panel PaginaDoTeclado()
    {
        var p = NovaPagina("Teclado", "Tecla de cada ação na venda. Para teclado de automação, aponte cada tecla dele para uma ação.");
        // Posições fixas (sem Anchor): a página ainda não tem o tamanho final quando é montada.
        _gradeTeclas.SetBounds(0, 66, 554, 300);
        _gradeTeclas.Columns.AddRange(Ui.Coluna("Ação", 70), Ui.Coluna("Tecla", 30));
        _gradeTeclas.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) TrocarTecla(); };

        var trocar = Ui.Botao("Trocar tecla", largura: 140, altura: 34, icone: Icones.Editar);
        trocar.Click += (_, _) => TrocarTecla();
        var semTecla = Ui.Botao("Sem tecla", Ui.Neutro, 120, 34);
        semTecla.Click += (_, _) =>
        {
            if (AcaoSelecionada() is not { } a) return;
            _teclas[a] = Keys.None;
            MostrarTeclas();
        };
        var padrao = Ui.Botao("Voltar ao padrão", Ui.Neutro, 160, 34);
        padrao.Click += (_, _) =>
        {
            if (!Ui.Confirmar(this, "Voltar todas as teclas ao padrão (F1 a F12 e Del; Enter paga)?")) return;
            foreach (var d in TeclasCaixa.Todas) _teclas[d.Acao] = d.Padrao;
            MostrarTeclas();
        };
        var botoes = new FlowLayoutPanel { Location = new Point(0, 374), Size = new Size(554, 40), WrapContents = false, BackColor = Color.Transparent };
        foreach (var b in new Control[] { trocar, semTecla, padrao }) b.Margin = new Padding(0, 0, 10, 0);
        botoes.Controls.AddRange([trocar, semTecla, padrao]);
        var dica = Dica("Enter paga (e conclui no pagamento) e Esc volta: esses são fixos. Duplo clique numa linha troca a tecla.", 0, 418);
        p.Controls.AddRange([_gradeTeclas, botoes, dica]);
        return p;
    }

    private AcaoCaixa? AcaoSelecionada() =>
        _gradeTeclas.CurrentRow?.Tag is TeclasCaixa.Definicao d ? d.Acao : null;

    private void MostrarTeclas()
    {
        var atual = AcaoSelecionada();
        _gradeTeclas.Rows.Clear();
        foreach (var d in TeclasCaixa.Todas)
        {
            var tecla = _teclas.GetValueOrDefault(d.Acao, d.Padrao);
            var i = _gradeTeclas.Rows.Add(d.Nome, tecla == Keys.None ? "—" : TeclasCaixa.Texto(tecla));
            _gradeTeclas.Rows[i].Tag = d;
            if (tecla != d.Padrao) _gradeTeclas.Rows[i].Cells[1].Style.ForeColor = Tema.Primaria;
            if (d.Acao == atual) _gradeTeclas.CurrentCell = _gradeTeclas.Rows[i].Cells[0];
        }
    }

    private void TrocarTecla()
    {
        if (_gradeTeclas.CurrentRow?.Tag is not TeclasCaixa.Definicao d) return;
        using var f = new CapturarTeclaForm(d.Nome);
        if (f.ShowDialog(this) != DialogResult.OK || f.Tecla == Keys.None) return;
        // Tecla já usada por outra ação: passa para esta, e a outra fica sem tecla.
        var dona = TeclasCaixa.Todas.FirstOrDefault(x => x.Acao != d.Acao && _teclas.GetValueOrDefault(x.Acao, x.Padrao) == f.Tecla);
        if (dona is not null)
        {
            if (!Ui.Confirmar(this, $"{TeclasCaixa.Texto(f.Tecla)} já é de \"{dona.Nome}\".\n\nPassar para \"{d.Nome}\"? A outra ação fica sem tecla.")) return;
            _teclas[dona.Acao] = Keys.None;
        }
        _teclas[d.Acao] = f.Tecla;
        MostrarTeclas();
    }

    private Panel PaginaDaAparencia()
    {
        var p = NovaPagina("Aparência", "Tema claro ou escuro, cor, nome da empresa, logo e plano de fundo.");
        _aparencia.SetBounds(0, 72, 560, 360);
        _aparencia.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        p.Controls.Add(_aparencia);
        return p;
    }

    private Panel PaginaDaInterface()
    {
        var p = NovaPagina("Interface", "Escolha o visual, menu, tamanho e painel inicial deste Caixa.");
        _interface.SetBounds(0, 70, 535, 340);
        p.Controls.Add(_interface);
        return p;
    }

    private Panel MontarRodape()
    {
        var rodape = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Tema.Superficie };
        rodape.Paint += (_, e) =>
        {
            using var linha = new Pen(Tema.Borda);
            e.Graphics.DrawLine(linha, 0, 0, rodape.Width, 0);
        };
        var salvar = Ui.Botao("Salvar", largura: 130, altura: 40, icone: Icones.Salvar);
        salvar.Location = new Point(ClientSize.Width - 290, 12);
        salvar.Click += async (_, _) =>
        {
            if (!salvar.Enabled) return;
            salvar.Enabled = false;
            try { await SalvarAsync(); }
            finally { if (!salvar.IsDisposed) salvar.Enabled = true; }
        };
        var cancelar = Ui.Botao("Cancelar", Ui.Neutro, 130, 40);
        cancelar.Location = new Point(ClientSize.Width - 150, 12);
        cancelar.DialogResult = DialogResult.Cancel;
        rodape.Controls.AddRange([Dica("As alterações passam a valer ao salvar.", 20, 24), salvar, cancelar]);
        CancelButton = cancelar;
        return rodape;
    }

    private void Preencher(PdvConfig cfg)
    {
        foreach (var d in TeclasCaixa.Todas) _teclas[d.Acao] = TeclasCaixa.Tecla(cfg, d.Acao);
        MostrarTeclas();
        _servidorAtual = cfg.Servidor;
        _digitandoUrl = _servidorAtual.Length == 0 || !EnderecoServidor.PareceEndereco(_servidorAtual);
        _url.Text = EnderecoServidor.ParaTela(_servidorAtual.Length == 0 ? EnderecoServidor.NomePadrao : _digitandoUrl ? _servidorAtual : "");
        if (!_digitandoUrl)
        {
            // Não mostra o endereço do servidor. Clicou ou digitou no campo: começa um endereço novo.
            _url.Text = EnderecoServidor.TextoConfigurado;
            _url.ForeColor = Tema.TextoSuave;
            _url.ReadOnly = true;
            _url.Click += (_, _) => ComecarNovaUrl();
            _url.KeyPress += (_, _) => ComecarNovaUrl();
        }
        _chave.Text = cfg.TerminalKey.StartsWith("COLE_AQUI", StringComparison.OrdinalIgnoreCase) ? "" : cfg.TerminalKey;
        _loja.Text = cfg.NomeLoja;
        _cabecalho.Lines = cfg.CabecalhoCupom.ToArray();

        _impressora.Items.Add(SemImpressora);
        try
        {
            foreach (string nome in PrinterSettings.InstalledPrinters) _impressora.Items.Add(nome);
        }
        catch (Exception)
        {
            // spooler parado: a lista fica só com "nenhuma"
        }
        if (!string.IsNullOrWhiteSpace(cfg.Impressora) && !_impressora.Items.Contains(cfg.Impressora))
            _impressora.Items.Add(cfg.Impressora); // configurada antes, não está instalada agora
        _impressora.SelectedItem = string.IsNullOrWhiteSpace(cfg.Impressora) ? SemImpressora : cfg.Impressora;

        _impressoraA4.Items.Add(PadraoWindows);
        foreach (var nome in _impressora.Items.OfType<string>().Where(n => n != SemImpressora)) _impressoraA4.Items.Add(nome);
        if (!string.IsNullOrWhiteSpace(cfg.ImpressoraA4) && !_impressoraA4.Items.Contains(cfg.ImpressoraA4))
            _impressoraA4.Items.Add(cfg.ImpressoraA4);
        _impressoraA4.SelectedItem = string.IsNullOrWhiteSpace(cfg.ImpressoraA4) ? PadraoWindows : cfg.ImpressoraA4;

        _largura.Items.AddRange(new object[]
        {
            new Opcao<int>("80 mm — 48 colunas (padrão)", 48),
            new Opcao<int>("80 mm — 42 colunas", 42),
            new Opcao<int>("58 mm — 32 colunas", 32)
        });
        Selecionar(_largura, (Opcao<int> o) => o.Valor == cfg.ColunasCupom);

        _acentos.Items.AddRange(new object[]
        {
            new Opcao<(int Pagina, byte Tabela, bool Sem)>("Padrão (PC850)", (850, 2, false)),
            new Opcao<(int Pagina, byte Tabela, bool Sem)>("Português (PC860)", (860, 3, false)),
            new Opcao<(int Pagina, byte Tabela, bool Sem)>("Sem acentos", (850, 2, true))
        });
        Selecionar(_acentos, (Opcao<(int Pagina, byte Tabela, bool Sem)> o) =>
            cfg.SemAcentos ? o.Valor.Sem : !o.Valor.Sem && o.Valor.Pagina == cfg.PaginaCodigo);

        _cortar.Checked = cfg.CortarPapel;
        _gaveta.Checked = cfg.AbrirGaveta;

        _intervalo.Items.AddRange(new object[]
        {
            new Opcao<int>("15 segundos", 15),
            new Opcao<int>("30 segundos", 30),
            new Opcao<int>("1 minuto", 60),
            new Opcao<int>("2 minutos", 120)
        });
        Selecionar(_intervalo, (Opcao<int> o) => o.Valor == cfg.IntervaloSyncSegundos, padrao: 1);
        _pastaCopia.Text = cfg.PastaCopiaSeguranca ?? "";

        _balancaEtiqueta.Checked = cfg.BalancaEtiqueta;
        _balancaDigitos.Items.AddRange(new object[]
        {
            new Opcao<int>("4 dígitos", 4),
            new Opcao<int>("5 dígitos", 5),
            new Opcao<int>("6 dígitos", 6)
        });
        var digitos = cfg.BalancaDigitosCodigo is 4 or 5 or 6 ? cfg.BalancaDigitosCodigo : 6;
        Selecionar(_balancaDigitos, (Opcao<int> o) => o.Valor == digitos, padrao: 2);
        _balancaConteudo.Items.AddRange(new object[]
        {
            new Opcao<bool>("Peso (kg)", false),
            new Opcao<bool>("Preço total", true)
        });
        Selecionar(_balancaConteudo, (Opcao<bool> o) => o.Valor == cfg.BalancaEtiquetaPreco);
        _balancaPorta.Items.Add("");
        _balancaPorta.Items.Add("SIMULADA");
        try
        {
            foreach (var nome in System.IO.Ports.SerialPort.GetPortNames()) _balancaPorta.Items.Add(nome);
        }
        catch (Exception)
        {
            // sem portas neste computador
        }
        if (!string.IsNullOrWhiteSpace(cfg.BalancaPorta) && !_balancaPorta.Items.Contains(cfg.BalancaPorta))
            _balancaPorta.Items.Add(cfg.BalancaPorta);
        _balancaPorta.Text = cfg.BalancaPorta;
        _balancaBaud.Text = (cfg.BalancaBaud > 0 ? cfg.BalancaBaud : 9600).ToString();
        _balancaSimulado.Text = cfg.BalancaPesoSimulado > 0 ? cfg.BalancaPesoSimulado.ToString("0.###") : "";

        foreach (var t in Enum.GetValues<Pix.TipoChave>()) _pixTipo.Items.Add(new Opcao<Pix.TipoChave>(Pix.Nome(t), t));
        Selecionar(_pixTipo, (Opcao<Pix.TipoChave> o) => o.Valor == cfg.PixTipo);
        _pixChave.Text = cfg.PixChave;
        foreach (var codigo in cfg.BotoesRapidos)
        {
            ProdutoLocal? p = null;
            try { p = Repositorio.BuscarProduto(codigo); } catch (Exception) { /* banco indisponível: mostra só o código */ }
            _rapidosLista.Items.Add(new ItemRapido(codigo, p is null ? $"{codigo} — (não encontrado neste caixa)" : $"{p.Codigo} — {p.Descricao}"));
        }
        _pixRecebedor.Text = cfg.PixRecebedor;
        _pixCidade.Text = cfg.PixCidade;
    }

    /// <summary>Seleciona o primeiro item que atende à condição; se nenhum, o item <paramref name="padrao"/>.</summary>
    private static void Selecionar<T>(ComboBox combo, Func<T, bool> condicao, int padrao = 0)
    {
        var item = combo.Items.Cast<object>().OfType<T>().FirstOrDefault(condicao);
        if (item is not null) combo.SelectedItem = item;
        else combo.SelectedIndex = padrao;
    }

    // ---------- Ações ----------

    private PdvConfig LerTela()
    {
        var acentos = ((Opcao<(int Pagina, byte Tabela, bool Sem)>)_acentos.SelectedItem!).Valor;
        var impressora = _impressora.SelectedItem as string;
        return new PdvConfig
        {
            Servidor = ServidorEscolhido,
            TerminalKey = _chave.Text.Trim(),
            NomeLoja = _loja.Text.Trim(),
            CabecalhoCupom = _cabecalho.Lines.Select(l => l.Trim()).Where(l => l.Length > 0).Take(4).ToList(),
            Impressora = impressora is null || impressora == SemImpressora ? "" : impressora,
            ImpressoraA4 = _impressoraA4.SelectedItem is string a4 && a4 != PadraoWindows ? a4 : null,
            ImpressaoOrcamento = _original.ImpressaoOrcamento,
            // Só as teclas diferentes do padrão vão para o pdv.json.
            Teclas = TeclasCaixa.Todas
                .Where(d => _teclas.GetValueOrDefault(d.Acao, d.Padrao) != d.Padrao)
                .ToDictionary(d => d.Acao.ToString(), d => ((int)_teclas[d.Acao]).ToString()),
            ColunasCupom = ((Opcao<int>)_largura.SelectedItem!).Valor,
            PaginaCodigo = acentos.Pagina,
            TabelaEscPos = acentos.Tabela,
            SemAcentos = acentos.Sem,
            CortarPapel = _cortar.Checked,
            AbrirGaveta = _gaveta.Checked,
            IntervaloSyncSegundos = ((Opcao<int>)_intervalo.SelectedItem!).Valor,
            PastaCopiaSeguranca = _pastaCopia.Text.Trim(),
            BalancaEtiqueta = _balancaEtiqueta.Checked,
            BalancaDigitosCodigo = ((Opcao<int>)_balancaDigitos.SelectedItem!).Valor,
            BalancaEtiquetaPreco = ((Opcao<bool>)_balancaConteudo.SelectedItem!).Valor,
            BalancaPorta = _balancaPorta.Text.Trim(),
            BalancaBaud = int.TryParse(_balancaBaud.Text.Trim(), out var baud) && baud > 0 ? baud : 9600,
            BalancaPesoSimulado = Ui.LerDecimal(_balancaSimulado.Text, out var pesoSim) ? pesoSim : 0,
            PixTipoChave = (((Opcao<Pix.TipoChave>?)_pixTipo.SelectedItem)?.Valor ?? Pix.TipoChave.CpfCnpj).ToString(),
            PixChave = _pixChave.Text.Trim(),
            BotoesRapidos = _rapidosLista.Items.Cast<ItemRapido>().Select(i => i.Codigo).ToList(),
            PixRecebedor = _pixRecebedor.Text.Trim(),
            PixCidade = _pixCidade.Text.Trim()
        };
    }

    /// <summary>Digitaram um site no lugar do nome: site só se cadastra na Configuração técnica.</summary>
    private bool NomeDigitadoInvalido()
    {
        if (!_digitandoUrl || !EnderecoServidor.PareceEndereco(_url.Text)) return false;
        MostrarPagina(PaginaServidor);
        Ui.Aviso(this, "Digite o NOME do servidor (ex.: " + EnderecoServidor.ParaTela(EnderecoServidor.NomePadrao) + "), não o site.\n\n" +
                       "Sites são cadastrados em \"Configuração técnica (suporte)\".");
        _url.SelectAll();
        _url.Focus();
        return true;
    }

    private void ComecarNovaUrl()
    {
        if (_digitandoUrl) return;
        _digitandoUrl = true;
        _url.ReadOnly = false;
        _url.Text = "";
        _url.ForeColor = Tema.Texto;
        _url.Focus();
    }

    private void MostrarCopia(bool? ok, string texto)
    {
        _resultadoCopia.Text = texto;
        _resultadoCopia.ForeColor = ok switch { true => Ui.Sucesso, false => Ui.Perigo, _ => Ui.Neutro };
    }

    private void Mostrar(bool? ok, string texto)
    {
        _resultado.Text = texto;
        _resultado.ForeColor = ok switch { true => Ui.Sucesso, false => Ui.Perigo, _ => Ui.Neutro };
    }

    /// <summary>Confere se o servidor responde e se a chave do terminal é aceita.</summary>
    private async Task TestarAsync()
    {
        var cfg = LerTela();
        if (NomeDigitadoInvalido()) return;
        if (!Uri.TryCreate(cfg.ApiUrl, UriKind.Absolute, out var uri))
        {
            Mostrar(false, $"Servidor \"{EnderecoServidor.ParaTela(ServidorEscolhido)}\" não encontrado. Confira o nome.");
            return;
        }

        _testar.Enabled = false;
        Mostrar(null, "Testando...");
        try
        {
            using var http = new HttpClient { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(15) };

            var saude = await http.GetAsync("health");
            if (!saude.IsSuccessStatusCode)
            {
                Mostrar(false, (int)saude.StatusCode switch
                {
                    404 => $"Servidor \"{EnderecoServidor.ParaTela(ServidorEscolhido)}\" não encontrado. Confira o nome.",
                    403 => "Acesso suspenso. Fale com o suporte.",
                    var c => $"O servidor respondeu com erro {c}."
                });
                return;
            }
            if (string.IsNullOrWhiteSpace(cfg.TerminalKey))
            {
                Mostrar(false, "Servidor respondendo. Falta a chave do terminal.");
                return;
            }

            // "desde = agora" devolve listas vazias: só confere a chave e traz o nome do terminal
            http.DefaultRequestHeaders.Add("X-Terminal-Key", cfg.TerminalKey);
            var agora = Uri.EscapeDataString(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            var resp = await http.GetAsync($"api/sync/receber?desde={agora}");
            if (resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                Mostrar(false, "O servidor respondeu, mas não reconheceu a chave do terminal. Confira:\n" +
                               $"• o Servidor do caixa (\"{EnderecoServidor.ParaTela(ServidorEscolhido)}\") é o mesmo usado na Gestão onde o terminal foi criado;\n" +
                               "• a chave foi colada inteira e o terminal está ativo em Gestão → Caixa → Terminais.");
                return;
            }
            resp.EnsureSuccessStatusCode();
            var dados = await resp.Content.ReadFromJsonAsync<SyncReceberResponse>();
            Mostrar(true, $"Conectado. Este caixa é o terminal \"{dados?.NomeTerminal}\".");
        }
        catch (TaskCanceledException)
        {
            Mostrar(false, "O servidor não respondeu a tempo. Confira a internet e o endereço.");
        }
        catch (HttpRequestException ex)
        {
            Mostrar(false, "Não foi possível conectar: " + EnderecoServidor.ErroConexao(ex) + ".");
        }
        finally
        {
            _testar.Enabled = true;
        }
    }

    /// <summary>
    /// Confere rapidamente o servidor e a chave escolhidos. Devolve o problema em palavras simples, ou null se está
    /// tudo certo ou se não deu para conferir (sem internet: não impede salvar).
    /// </summary>
    private async Task<string?> ConferirServidorAsync(PdvConfig cfg)
    {
        if (!Uri.TryCreate(cfg.ApiUrl.TrimEnd('/') + "/", UriKind.Absolute, out var uri)) return null;
        try
        {
            using var http = new HttpClient { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(8) };
            using var saude = await http.GetAsync("health");
            if (saude.StatusCode == HttpStatusCode.NotFound)
                return $"O servidor \"{EnderecoServidor.ParaTela(ServidorEscolhido)}\" não foi encontrado: esse nome não existe no servidor da empresa.\n" +
                       "Use o mesmo nome da Gestão (ex.: " + EnderecoServidor.ParaTela(EnderecoServidor.NomePadrao) + ", ou o código da empresa no Projeto JP Master). " +
                       "Nomes criados na Configuração técnica valem só no computador onde foram criados.";
            if (saude.StatusCode == HttpStatusCode.Forbidden) return "O servidor respondeu: acesso suspenso. Fale com o suporte.";
            if (!saude.IsSuccessStatusCode || string.IsNullOrWhiteSpace(cfg.TerminalKey)) return null;

            http.DefaultRequestHeaders.Add("X-Terminal-Key", cfg.TerminalKey);
            var agora = Uri.EscapeDataString(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            using var resp = await http.GetAsync($"api/sync/receber?desde={agora}");
            return resp.StatusCode == HttpStatusCode.Unauthorized
                ? $"O servidor \"{EnderecoServidor.ParaTela(ServidorEscolhido)}\" respondeu, mas não reconheceu a chave do terminal. Confira se a chave é " +
                  "de um terminal criado na Gestão deste mesmo servidor e se ele está ativo."
                : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    private void ImprimirTeste()
    {
        var cfg = LerTela();
        var erro = Cupom.ImprimirTeste(cfg);
        if (erro is not null)
            Ui.Erro(this, "Falha na impressão: " + erro);
        else if (string.IsNullOrWhiteSpace(cfg.Impressora))
            Ui.Aviso(this, $"Sem impressora escolhida: o teste foi salvo em\n{Path.Combine(Db.Pasta, "cupons")}");
        else
            MessageBox.Show(this, "Teste enviado. Confira o papel: a linha de números deve caber inteira e os acentos devem sair certos.",
                "Teste de impressão", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task SalvarAsync()
    {
        if (NomeDigitadoInvalido()) return;
        var cfg = LerTela();
        if (!cfg.Configurado)
        {
            MostrarPagina(PaginaServidor);
            Ui.Aviso(this, string.IsNullOrWhiteSpace(cfg.ApiUrl)
                ? $"Servidor \"{EnderecoServidor.ParaTela(ServidorEscolhido)}\" não encontrado. Confira o nome."
                : "Informe a chave do terminal.");
            (string.IsNullOrWhiteSpace(cfg.ApiUrl) ? _url : _chave).Focus();
            return;
        }
        if (cfg.NomeLoja.Length == 0)
        {
            MostrarPagina(PaginaLoja);
            Ui.Aviso(this, "Informe o nome da loja.");
            _loja.Focus();
            return;
        }
        if (ProblemaNoPix(cfg) is { } problemaPix)
        {
            MostrarPagina(PaginaPix);
            Ui.Aviso(this, problemaPix + "\n\nPara usar o Pix sem QR Code, deixe a chave, o nome e a cidade em branco.");
            _pixChave.Focus();
            return;
        }

        // Servidor ou chave trocados: confere antes de gravar, para o caixa não ficar Offline com um nome que não existe.
        // Sem internet não impede (o caixa trabalha offline e sincroniza depois).
        if (cfg.ApiUrl != _original.ApiUrl || cfg.TerminalKey != _original.TerminalKey)
        {
            Mostrar(null, "Conferindo o servidor...");
            var problema = await ConferirServidorAsync(cfg);
            if (IsDisposed) return;
            Mostrar(problema is null ? null : false, problema ?? "");
            if (problema is not null)
            {
                MostrarPagina(PaginaServidor);
                if (!Ui.Confirmar(this, problema + "\n\nSalvar mesmo assim? (o caixa vai ficar Offline)")) return;
            }
        }

        // Trocar o Servidor ou a chave com o caixa aberto ou com registros não enviados: o recomendado é fechar o
        // caixa e esperar tudo subir antes. Avisa e deixa decidir (servidor errado, que nunca recebeu nada, pode trocar).
        if (cfg.ApiUrl != _original.ApiUrl || cfg.TerminalKey != _original.TerminalKey)
        {
            int pendentes;
            CaixaLocal? aberto;
            try
            {
                pendentes = Repositorio.ContarPendencias();
                aberto = Repositorio.CaixaAberto();
            }
            catch (Exception)
            {
                pendentes = 0;
                aberto = null;
            }
            if (pendentes > 0 || aberto is not null)
            {
                var motivos = new List<string>();
                if (aberto is not null)
                {
                    var desde = DateTime.TryParse(aberto.Abertura, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d)
                        ? d.ToLocalTime().ToString("dd/MM 'às' HH:mm") : "antes";
                    motivos.Add($"• o caixa está aberto (desde {desde});");
                }
                if (pendentes > 0) motivos.Add($"• há {pendentes} venda(s)/registro(s) ainda não enviados ao servidor;");
                if (!Ui.Confirmar(this,
                        "Você está trocando o Servidor ou a chave do terminal, mas:\n" + string.Join("\n", motivos) + "\n\n" +
                        "O recomendado é fechar o caixa e esperar tudo ser enviado (etiqueta verde no rodapé) antes de trocar.\n" +
                        "Se o Servidor atual está errado e o caixa nunca conseguiu enviar, pode trocar: tudo vai para o novo.\n\n" +
                        "Trocar mesmo assim?"))
                {
                    MostrarPagina(PaginaServidor);
                    return;
                }
            }
        }

        bool aparenciaMudou;
        bool interfaceMudou;
        try
        {
            cfg.Salvar();
            interfaceMudou = _interface.Salvar();
            aparenciaMudou = _aparencia.Salvar();
        }
        catch (Exception ex)
        {
            Ui.Erro(this, "Não foi possível salvar: " + ex.Message);
            return;
        }

        // O Caixa so permite abrir estas opcoes quando nao ha itens na venda atual.
        // Tanto mudancas no tema quanto no modo/menu/tamanho exigem nova abertura.
        if (aparenciaMudou || interfaceMudou)
        {
            var reiniciar = MessageBox.Show(this,
                "Preferências de aparência salvas.\n\n" +
                "Deseja reiniciar o PDV agora para aplicar as alterações?\n" +
                "Se escolher Não, elas serão aplicadas na próxima abertura.\n\n" +
                "O operador precisará entrar novamente; o caixa aberto e as vendas já gravadas serão mantidos.",
                "Aplicar aparência", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (reiniciar == DialogResult.Yes)
            {
                try { Aparencia.Reiniciar(); }
                catch (Exception ex)
                {
                    Ui.Aviso(this, "Preferências salvas, mas não foi possível reiniciar automaticamente: " +
                                   ex.Message + "\nFeche e abra o PDV manualmente para aplicá-las.");
                }
            }
        }

        DialogResult = DialogResult.OK;
    }
}
