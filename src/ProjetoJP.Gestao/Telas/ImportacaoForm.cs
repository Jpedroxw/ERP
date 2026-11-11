using System.Data;
using System.Text;
using ProjetoJP.Controles;
using ProjetoJP.Gestao.Servicos;
using ProjetoJP.Shared.Dtos;

namespace ProjetoJP.Gestao.Telas;

/// <summary>
/// Importa produtos ou clientes de outro sistema: banco de dados (Firebird, SQLite, PostgreSQL, MySQL,
/// SQL Server, ODBC) ou planilha (CSV, Excel). Passo a passo: o que importar, de onde, qual tabela,
/// qual coluna vai para cada campo. A origem só é lida; os dados vão para o servidor em lotes.
/// </summary>
public class ImportacaoForm : Form
{
    private const int Lote = 500;
    private const int LarguraCartao = 452;

    private sealed record ItemOrigem(TipoOrigem Tipo, string Nome)
    {
        public override string ToString() => Nome;
    }

    /// <summary>Tabela da origem. As que parecem ser a de produtos/clientes (pelas colunas) vêm primeiro, com estrela.</summary>
    private sealed record ItemTabela(string Nome, int Pontos, int Campos, string? Alvo)
    {
        public override string ToString() => Alvo is null ? Nome : $"{Nome}    ★ parece ser a de {Alvo} ({Campos} campos)";
    }

    private readonly ApiClient _api;

    // 1. O que importar
    private readonly RadioButton _rbProdutos = new() { Text = "Produtos", Checked = true, AutoSize = true, Font = Tema.Semibold(10) };
    private readonly RadioButton _rbClientes = new() { Text = "Clientes", AutoSize = true, Font = Tema.Semibold(10) };

    // 2. De onde
    private readonly ComboBox _tipo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Font = Tema.Fonte(10.5f) };
    private readonly Panel _camposOrigem = new() { BackColor = Tema.Superficie };
    private readonly Dictionary<string, Control> _campos = new();
    private readonly CartaoPainel _cartaoOrigem = new() { Titulo = "2. De onde vêm os dados" };

    // 3. Tabela
    private readonly ComboBox _tabela = new() { DropDownStyle = ComboBoxStyle.DropDownList, Font = Tema.Fonte(10.5f) };
    private readonly CheckBox _usarSql = new() { Text = "Escrever uma consulta SQL (avançado)", AutoSize = true, ForeColor = Tema.TextoSuave };
    private readonly TextBox _sql = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 10f), Visible = false, AcceptsReturn = true };
    private readonly CartaoPainel _cartaoTabela = new() { Titulo = "3. Tabela" };

    // 4. Colunas
    private readonly CartaoPainel _cartaoColunas = new() { Titulo = "4. Qual coluna vai para cada campo" };
    private readonly Dictionary<string, ComboBox> _mapa = new();
    private readonly CheckBox _atualizar = new() { Text = "Atualizar os que já existem (mesmo código, código de barras ou CPF/CNPJ)", Checked = true, AutoSize = true };
    private readonly CheckBox _estoque = new() { Text = "Gravar o estoque da origem (vira ajuste de inventário)", Checked = true, AutoSize = true };
    private readonly CheckBox _publicar = new() { Text = "Já publicar nos caixas (senão ficam pendentes em Caixas → Enviar produtos aos caixas)", Checked = true, AutoSize = true };
    private readonly BotaoTema _testar = new() { Text = "Testar sem gravar", Estilo = EstiloBotao.Secundario, Icone = Icones.Ok, Size = new Size(170, 40) };
    private readonly BotaoTema _importar = new() { Text = "Importar", Icone = Icones.Importar, Size = new Size(150, 40) };
    private readonly ProgressBar _progresso = new() { Visible = false, Height = 8 };

    // Lado direito
    private readonly DataGridView _previa = Ui.Grade();
    private readonly Label _tituloPrevia = new() { Text = "Prévia", AutoSize = true, Font = Tema.Semibold(11), ForeColor = Tema.Texto };
    private readonly TextBox _log = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None,
        BackColor = Tema.Superficie, Font = Tema.Fonte(9.5f), ForeColor = Tema.Texto
    };

    private readonly FlowLayoutPanel _passos = new()
    {
        Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
        BackColor = Tema.Fundo, Padding = new Padding(0, 0, 8, 0)
    };

    private OrigemDados? _origem;
    private Dictionary<string, List<string>>? _colunasPorTabela;
    private List<string> _tabelasOrigem = [];
    private DataTable? _dadosPrevia;
    private bool _ocupado;

    private bool Produtos => _rbProdutos.Checked;
    private CampoDestino[] Destinos => Produtos ? Mapeamento.Produtos : Mapeamento.Clientes;

    public ImportacaoForm(ApiClient api)
    {
        _api = api;
        Text = "Importar dados";
        Size = new Size(1180, 720);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Ui.Fundo;
        Font = Ui.Fonte(10);
        Padding = new Padding(20, 12, 20, 20);

        // Cabeçalho
        var cabecalho = new Panel { Dock = DockStyle.Top, Height = 58 };
        cabecalho.Controls.Add(new Label { Text = "Importar dados", AutoSize = true, Font = Tema.Semibold(16), ForeColor = Tema.Texto, Location = new Point(0, 2) });
        cabecalho.Controls.Add(new Label
        {
            Text = "Traga produtos e clientes de outro sistema (banco de dados ou planilha). A origem só é lida: nada é alterado nela.",
            AutoSize = true, ForeColor = Tema.TextoSuave, Location = new Point(2, 34)
        });

        MontarPassos();

        // Lado direito: prévia em cima, resultado embaixo
        var direita = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Tema.Fundo, Padding = new Padding(12, 0, 0, 0) };
        direita.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
        direita.RowStyles.Add(new RowStyle(SizeType.Percent, 38));

        var cartaoPrevia = new CartaoPainel { Dock = DockStyle.Fill, Padding = new Padding(1, 44, 1, 6), Margin = new Padding(0, 0, 0, 12) };
        _tituloPrevia.Location = new Point(16, 14);
        _previa.Dock = DockStyle.Fill;
        _previa.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _previa.ColumnHeadersHeight = 40;
        cartaoPrevia.Controls.Add(_previa);
        cartaoPrevia.Controls.Add(_tituloPrevia);

        var cartaoLog = new CartaoPainel { Titulo = "Resultado", Dock = DockStyle.Fill, Margin = Padding.Empty };
        _log.Dock = DockStyle.Fill;
        _log.Text = "1) Escolha o que importar e de onde.\r\n2) Clique em Conectar e escolha a tabela.\r\n" +
                    "3) Confira as colunas (o sistema sugere pelo nome).\r\n4) Clique em \"Testar sem gravar\" e depois em Importar.";
        cartaoLog.Controls.Add(_log);

        direita.Controls.Add(cartaoPrevia, 0, 0);
        direita.Controls.Add(cartaoLog, 0, 1);

        var corpo = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Tema.Fundo };
        corpo.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LarguraCartao + 26));
        corpo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        corpo.Controls.Add(_passos, 0, 0);
        corpo.Controls.Add(direita, 1, 0);

        Controls.Add(corpo);
        Controls.Add(cabecalho);
    }

    // ---------- Montagem dos passos ----------

    private void MontarPassos()
    {
        // 1. O que importar
        var c1 = new CartaoPainel { Titulo = "1. O que importar", Size = new Size(LarguraCartao, 92), Margin = new Padding(0, 0, 0, 12) };
        _rbProdutos.Location = new Point(18, 52);
        _rbClientes.Location = new Point(140, 52);
        _rbProdutos.CheckedChanged += (_, _) => { if (_rbProdutos.Checked) TrocarAlvo(); };
        _rbClientes.CheckedChanged += (_, _) => { if (_rbClientes.Checked) TrocarAlvo(); };
        c1.Controls.AddRange([_rbProdutos, _rbClientes]);

        // 2. De onde
        _cartaoOrigem.Width = LarguraCartao;
        _cartaoOrigem.Margin = new Padding(0, 0, 0, 12);
        _tipo.Items.AddRange(new object[]
        {
            new ItemOrigem(TipoOrigem.Planilha, "Planilha (Excel .xlsx/.xls ou CSV/TXT)"),
            new ItemOrigem(TipoOrigem.Firebird, "Firebird (.FDB / .GDB)"),
            new ItemOrigem(TipoOrigem.Sqlite, "SQLite (.db)"),
            new ItemOrigem(TipoOrigem.PostgreSql, "PostgreSQL"),
            new ItemOrigem(TipoOrigem.MySql, "MySQL / MariaDB"),
            new ItemOrigem(TipoOrigem.SqlServer, "SQL Server"),
            new ItemOrigem(TipoOrigem.Odbc, "Outro banco (ODBC: Access, Oracle...)")
        });
        _tipo.SetBounds(18, 50, LarguraCartao - 36, 30);
        _tipo.SelectedIndexChanged += (_, _) => MontarCamposOrigem();
        _camposOrigem.Location = new Point(18, 90);
        _camposOrigem.Width = LarguraCartao - 36;
        _cartaoOrigem.Controls.AddRange([_tipo, _camposOrigem]);

        // 3. Tabela
        _cartaoTabela.Width = LarguraCartao;
        _cartaoTabela.Margin = new Padding(0, 0, 0, 12);
        _tabela.SetBounds(18, 50, LarguraCartao - 36 - 130, 30);
        var ver = new BotaoTema { Text = "Ver dados", Estilo = EstiloBotao.Secundario, Icone = Icones.Grade, Size = new Size(120, 32) };
        ver.Location = new Point(LarguraCartao - 18 - ver.Width, 49);
        ver.Click += async (_, _) => await VerDados();
        _tabela.SelectedIndexChanged += async (_, _) => { if (!_usarSql.Checked && _tabela.SelectedItem is not null) await VerDados(); };
        _usarSql.Location = new Point(18, 92);
        _sql.SetBounds(18, 118, LarguraCartao - 36, 110);
        _sql.PlaceholderText = "SELECT p.codigo, p.descricao, e.saldo FROM produtos p JOIN estoque e ON e.produto = p.codigo";
        _usarSql.CheckedChanged += (_, _) => AjustarCartaoTabela();
        _cartaoTabela.Controls.AddRange([_tabela, ver, _usarSql, _sql]);
        AjustarCartaoTabela();

        // 4. Colunas (montado quando houver dados)
        _cartaoColunas.Width = LarguraCartao;
        _cartaoColunas.Margin = new Padding(0, 0, 0, 12);
        _testar.Click += async (_, _) => await Importar(simular: true);
        _importar.Click += async (_, _) => await Importar(simular: false);

        _passos.Controls.AddRange([c1, _cartaoOrigem, _cartaoTabela, _cartaoColunas]);

        _tipo.SelectedIndex = 0;
        MontarColunas();
    }

    private void AjustarCartaoTabela()
    {
        _sql.Visible = _usarSql.Checked;
        _tabela.Enabled = !_usarSql.Checked;
        _cartaoTabela.Height = _usarSql.Checked ? 246 : 128;
    }

    /// <summary>Campos de conexão de acordo com o tipo de origem.</summary>
    private void MontarCamposOrigem()
    {
        _camposOrigem.Controls.Clear();
        _campos.Clear();
        _origem = null;
        _colunasPorTabela = null;
        _tabelasOrigem = [];
        _tabela.Items.Clear();
        var tipo = ((ItemOrigem)_tipo.SelectedItem!).Tipo;
        var largura = _camposOrigem.Width;
        var y = 0;

        void Rotulo(string texto, int x, int top, string? dica = null)
        {
            var l = new Label { Text = texto, AutoSize = true, ForeColor = Tema.TextoSuave, Font = Tema.Fonte(9), Location = new Point(x, top) };
            _camposOrigem.Controls.Add(l);
            if (dica is not null)
                _camposOrigem.Controls.Add(new Label { Text = dica, AutoSize = true, ForeColor = Tema.TextoSuave, Font = Tema.Fonte(8), Location = new Point(x + l.PreferredWidth + 4, top + 1) });
        }

        TextBox Texto(string chave, string rotulo, int x, int w, string valor = "", bool senha = false, string? dica = null)
        {
            Rotulo(rotulo, x, y, dica);
            var t = new TextBox { Text = valor, UseSystemPasswordChar = senha, Font = Tema.Fonte(10) };
            t.SetBounds(x, y + 20, w, 28);
            _camposOrigem.Controls.Add(t);
            _campos[chave] = t;
            return t;
        }

        void Arquivo(string rotulo, string filtro, string chave = "arquivo", string? dica = null)
        {
            var t = Texto(chave, rotulo, 0, largura - 100, dica: dica);
            var procurar = new BotaoTema { Text = "Procurar", Estilo = EstiloBotao.Secundario, Size = new Size(92, 30) };
            procurar.Location = new Point(largura - 92, y + 19);
            procurar.Click += (_, _) =>
            {
                using var dlg = new OpenFileDialog { Filter = filtro, Title = rotulo };
                if (dlg.ShowDialog(this) == DialogResult.OK) t.Text = dlg.FileName;
            };
            _camposOrigem.Controls.Add(procurar);
            y += 56;
        }

        void ServidorPorta(string servidor = "localhost")
        {
            Texto("servidor", "Servidor (nome ou IP)", 0, largura - 110, servidor);
            var porta = Texto("porta", "Porta", largura - 98, 98, OrigemDados.PortaPadrao(tipo).ToString());
            porta.MaxLength = 5;
            porta.KeyPress += (_, e) => { if (!char.IsControl(e.KeyChar) && !char.IsAsciiDigit(e.KeyChar)) e.Handled = true; };
            y += 56;
        }

        void UsuarioSenha(string usuario = "", string senha = "", string? dica = null)
        {
            var meio = (largura - 12) / 2;
            Texto("usuario", "Usuário", 0, meio, usuario, dica: dica);
            Texto("senha", "Senha", meio + 12, meio, senha, senha: true);
            y += 56;
        }

        switch (tipo)
        {
            case TipoOrigem.Planilha:
                Arquivo("Arquivo", "Planilhas (*.xlsx;*.xls;*.csv;*.txt)|*.xlsx;*.xls;*.xlsm;*.csv;*.txt|Todos os arquivos|*.*");
                Dica(ref y, largura, "A primeira linha precisa ter os nomes das colunas. No CSV o separador (; , tab) é descoberto sozinho.");
                break;

            case TipoOrigem.Sqlite:
                Arquivo("Arquivo do banco", "SQLite (*.db;*.sqlite;*.sqlite3;*.db3)|*.db;*.sqlite;*.sqlite3;*.db3|Todos os arquivos|*.*");
                break;

            case TipoOrigem.Firebird:
                ServidorPorta();
                Arquivo("Caminho do banco no servidor", "Firebird (*.fdb;*.gdb)|*.fdb;*.gdb|Todos os arquivos|*.*", "banco",
                        "(ex.: C:\\Sistema\\DADOS.FDB)");
                UsuarioSenha("SYSDBA", "masterkey");
                Rotulo("Conjunto de caracteres", 0, y, "(acentos errados? troque aqui)");
                var cs = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = Tema.Fonte(10) };
                cs.Items.AddRange(new object[] { "WIN1252", "ISO8859_1", "UTF8", "NONE" });
                cs.SelectedIndex = 0;
                cs.SetBounds(0, y + 20, 160, 28);
                _camposOrigem.Controls.Add(cs);
                _campos["charset"] = cs;
                y += 56;
                Dica(ref y, largura, "O Firebird precisa estar rodando no computador ou servidor do banco. Feche o outro sistema antes, se ele travar o arquivo.");
                break;

            case TipoOrigem.PostgreSql:
            case TipoOrigem.MySql:
                ServidorPorta();
                Texto("banco", "Nome do banco", 0, largura);
                y += 56;
                UsuarioSenha(tipo == TipoOrigem.PostgreSql ? "postgres" : "root");
                break;

            case TipoOrigem.SqlServer:
                ServidorPorta("localhost\\SQLEXPRESS");
                Texto("banco", "Nome do banco", 0, largura);
                y += 56;
                UsuarioSenha(dica: "(vazio = login do Windows)");
                break;

            case TipoOrigem.Odbc:
                Rotulo("String de conexão ODBC", 0, y);
                var sc = new TextBox { Multiline = true, Font = new Font("Consolas", 9.5f), ScrollBars = ScrollBars.Vertical };
                sc.SetBounds(0, y + 20, largura, 70);
                sc.PlaceholderText = @"Driver={Microsoft Access Driver (*.mdb, *.accdb)};Dbq=C:\dados\loja.mdb;";
                _camposOrigem.Controls.Add(sc);
                _campos["string"] = sc;
                y += 98;
                Dica(ref y, largura, "Precisa do driver ODBC 64 bits do banco instalado neste computador.");
                break;
        }

        var conectar = new BotaoTema { Text = "Conectar", Icone = Icones.Banco, Size = new Size(140, 36) };
        conectar.Location = new Point(0, y + 4);
        conectar.Click += async (_, _) => await Conectar();
        _camposOrigem.Controls.Add(conectar);
        y += 48;

        _camposOrigem.Height = y;
        _cartaoOrigem.Height = _camposOrigem.Bottom + 16;
    }

    private void Dica(ref int y, int largura, string texto)
    {
        var l = new Label { Text = texto, ForeColor = Tema.TextoSuave, Font = Tema.Fonte(8.5f), AutoSize = false, Location = new Point(0, y) };
        l.Size = new Size(largura, TextRenderer.MeasureText(texto, l.Font, new Size(largura, 0), TextFormatFlags.WordBreak).Height + 4);
        _camposOrigem.Controls.Add(l);
        y += l.Height + 6;
    }

    /// <summary>Linhas "campo daqui → coluna da origem", com as sugestões pelo nome.</summary>
    private void MontarColunas()
    {
        _cartaoColunas.SuspendLayout();
        _cartaoColunas.Controls.Clear();
        _mapa.Clear();

        var colunas = _dadosPrevia?.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList() ?? [];
        var sugestao = Mapeamento.Sugerir(Destinos, colunas);
        var y = 50;

        if (colunas.Count == 0)
        {
            _cartaoColunas.Controls.Add(new Label
            {
                Text = "Conecte e escolha uma tabela para ligar as colunas.", AutoSize = true,
                ForeColor = Tema.TextoSuave, Location = new Point(18, y)
            });
            y += 30;
        }
        else
        {
            foreach (var campo in Destinos)
            {
                var l = new Label
                {
                    Text = campo.Rotulo + (campo.Obrigatorio ? " *" : ""), AutoSize = false, Size = new Size(160, 28),
                    TextAlign = ContentAlignment.MiddleLeft, Location = new Point(18, y),
                    Font = campo.Obrigatorio ? Tema.Semibold(9.5f) : Tema.Fonte(9.5f), ForeColor = Tema.Texto
                };
                var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = Tema.Fonte(10) };
                cb.Items.Add("(não importar)");
                foreach (var c in colunas) cb.Items.Add(c);
                cb.SelectedItem = sugestao[campo.Chave] is { } s ? s : "(não importar)";
                cb.SetBounds(180, y, LarguraCartao - 198, 28);
                cb.SelectedIndexChanged += (_, _) => MarcarColunasNaPrevia();
                if (campo.Dica is not null) new ToolTip().SetToolTip(cb, campo.Dica);
                _cartaoColunas.Controls.AddRange([l, cb]);
                _mapa[campo.Chave] = cb;
                y += 34;
            }
        }

        y += 6;
        _atualizar.Location = new Point(18, y);
        _atualizar.MaximumSize = new Size(LarguraCartao - 36, 0);
        _cartaoColunas.Controls.Add(_atualizar);
        y += _atualizar.PreferredSize.Height + 6;
        if (Produtos)
        {
            _estoque.Location = new Point(18, y);
            _cartaoColunas.Controls.Add(_estoque);
            y += 28;
            _publicar.Location = new Point(18, y);
            _publicar.MaximumSize = new Size(LarguraCartao - 36, 0);
            _cartaoColunas.Controls.Add(_publicar);
            y += _publicar.PreferredSize.Height + 6;
        }

        y += 8;
        _testar.Location = new Point(18, y);
        _importar.Location = new Point(_testar.Right + 10, y);
        _testar.Enabled = _importar.Enabled = colunas.Count > 0;
        _cartaoColunas.Controls.AddRange([_testar, _importar]);
        y += 48;
        _progresso.SetBounds(18, y, LarguraCartao - 36, 8);
        _cartaoColunas.Controls.Add(_progresso);
        y += 20;

        _cartaoColunas.Height = y;
        _cartaoColunas.ResumeLayout(true);
        MarcarColunasNaPrevia();
    }

    /// <summary>Na prévia, a coluna escolhida ganha cabeçalho azul com o nome do campo daqui.</summary>
    private void MarcarColunasNaPrevia()
    {
        if (_previa.Columns.Count == 0) return;
        var usados = _mapa.Where(m => m.Value.SelectedIndex > 0)
            .GroupBy(m => (string)m.Value.SelectedItem!)
            .ToDictionary(g => g.Key, g => string.Join(" + ", g.Select(x => Destinos.First(d => d.Chave == x.Key).Rotulo)));

        foreach (DataGridViewColumn col in _previa.Columns)
        {
            var nome = col.DataPropertyName;
            if (usados.TryGetValue(nome, out var destino))
            {
                col.HeaderText = $"{nome}\n→ {destino}";
                col.HeaderCell.Style.BackColor = Tema.PrimariaClara;
                col.HeaderCell.Style.ForeColor = Tema.PrimariaEscura;
            }
            else
            {
                col.HeaderText = nome;
                col.HeaderCell.Style.BackColor = Color.Empty;
                col.HeaderCell.Style.ForeColor = Color.Empty;
            }
        }
    }

    // ---------- Ações ----------

    private OrigemDados LerOrigem()
    {
        var tipo = ((ItemOrigem)_tipo.SelectedItem!).Tipo;
        string Valor(string chave) => _campos.TryGetValue(chave, out var c) ? c.Text.Trim() : "";
        var o = new OrigemDados
        {
            Tipo = tipo,
            Arquivo = Valor("arquivo"),
            Servidor = Valor("servidor"),
            Porta = int.TryParse(Valor("porta"), out var p) ? p : OrigemDados.PortaPadrao(tipo),
            Banco = Valor("banco"),
            Usuario = Valor("usuario"),
            Senha = _campos.TryGetValue("senha", out var s) ? s.Text : "",
            Charset = Valor("charset"),
            StringConexao = Valor("string")
        };
        if (o.EhArquivo && o.Arquivo.Length == 0) throw new InvalidOperationException("Escolha o arquivo.");
        if (tipo == TipoOrigem.Firebird && o.Banco.Length == 0)
            throw new InvalidOperationException("Informe o caminho do banco.");
        if (tipo is TipoOrigem.PostgreSql or TipoOrigem.MySql or TipoOrigem.SqlServer && o.Banco.Length == 0)
            throw new InvalidOperationException("Informe o nome do banco.");
        if (tipo == TipoOrigem.Odbc && o.StringConexao.Length == 0) throw new InvalidOperationException("Informe a string de conexão.");
        return o;
    }

    private async Task Conectar()
    {
        if (_ocupado) return;
        try
        {
            Ocupar(true, "Conectando...");
            var origem = LerOrigem();

            await origem.TestarAsync();
            var tabelas = await origem.ListarTabelasAsync();
            _origem = origem;
            _tabelasOrigem = tabelas;

            // Colunas de cada tabela: com elas o sistema acha sozinho a tabela de produtos/clientes.
            try
            {
                if (tabelas.Count > 1) Log("Olhando as colunas das tabelas para achar a certa...");
                _colunasPorTabela = await origem.ListarColunasAsync(tabelas);
            }
            catch (Exception)
            {
                _colunasPorTabela = null; // sem isso, sugere só pelo nome da tabela
            }

            Log($"Conectado. {tabelas.Count} tabela(s) encontrada(s).");
            Ocupar(false);
            PreencherTabelas();
        }
        catch (Exception ex)
        {
            Ocupar(false);
            _origem = null;
            Log("Não foi possível conectar: " + ex.Message);
            Ui.Aviso(this, "Não foi possível conectar:\n\n" + ex.Message +
                (_origem is null && ((ItemOrigem)_tipo.SelectedItem!).Tipo == TipoOrigem.Firebird
                    ? "\n\nConfira se o Firebird está ligado (serviço Firebird Server) e a senha do SYSDBA."
                    : ""));
        }
    }

    /// <summary>
    /// Monta a lista do passo 3: as tabelas que parecem ser de produtos (ou clientes) primeiro, com estrela,
    /// e seleciona a melhor — o que já carrega a prévia e liga as colunas.
    /// </summary>
    private void PreencherTabelas()
    {
        var alvo = Produtos ? "produtos" : "clientes";
        var itens = _tabelasOrigem.Select(t =>
        {
            if (_colunasPorTabela is not null && _colunasPorTabela.TryGetValue(t, out var cols))
            {
                var (pontos, campos) = Mapeamento.Pontuar(Destinos, t, cols);
                return new ItemTabela(t, pontos, campos, pontos >= 6 ? alvo : null);
            }
            // Sem as colunas: só pelo nome da tabela
            var pelo = Mapeamento.Pontuar(Destinos, t, Destinos.Select(d => d.Palpites[0]).ToList()).pontos;
            var nomeBom = pelo > Destinos.Sum(d => d.Obrigatorio ? 3 : 1);
            return new ItemTabela(t, nomeBom ? 1 : 0, 0, null);
        })
        .OrderByDescending(i => i.Pontos >= 6 ? i.Pontos : 0)
        .ThenBy(i => i.Nome, StringComparer.OrdinalIgnoreCase)
        .ToList();

        _tabela.BeginUpdate();
        _tabela.Items.Clear();
        foreach (var i in itens) _tabela.Items.Add(i);
        _tabela.EndUpdate();

        var candidatas = itens.Count(i => i.Alvo is not null);
        var melhor = itens.FirstOrDefault(i => i.Alvo is not null)
                     ?? itens.FirstOrDefault(i => i.Pontos > 0)
                     ?? (itens.Count == 1 ? itens[0] : null);

        if (melhor is not null)
        {
            Log(candidatas > 1
                ? $"Achei {candidatas} tabelas que parecem ser de {alvo}; escolhi {melhor.Nome}. Se não for ela, troque no passo 3 (as outras têm ★)."
                : $"Tabela de {alvo}: {melhor.Nome}. Se não for ela, troque no passo 3.");
            _tabela.SelectedItem = melhor; // carrega a prévia
        }
        else if (itens.Count > 0)
        {
            Log($"Não achei sozinho a tabela de {alvo}. Escolha no passo 3.");
            _tabela.DroppedDown = true;
        }
    }

    /// <summary>Trocou entre Produtos e Clientes: reordena as tabelas para o novo alvo (se já conectou).</summary>
    private void TrocarAlvo()
    {
        if (_origem is not null && _tabelasOrigem.Count > 0 && !_usarSql.Checked) PreencherTabelas();
        else MontarColunas();
    }

    private async Task VerDados()
    {
        if (_ocupado) return;
        if (_origem is null) { Ui.Aviso(this, "Clique em Conectar primeiro."); return; }
        try
        {
            Ocupar(true, "Lendo a prévia...");
            var sql = _usarSql.Checked ? _sql.Text : null;
            var tabela = (_tabela.SelectedItem as ItemTabela)?.Nome;
            _dadosPrevia = await _origem.LerAsync(tabela, sql, OrigemDados.LimitePrevia);

            _previa.DataSource = null;
            _previa.Columns.Clear();
            _previa.DataSource = _dadosPrevia;
            foreach (DataGridViewColumn c in _previa.Columns)
            {
                c.SortMode = DataGridViewColumnSortMode.NotSortable;
                c.Width = Math.Clamp(TextRenderer.MeasureText(c.HeaderText, Tema.Semibold(9.5f)).Width + 40, 90, 260);
            }
            _tituloPrevia.Text = $"Prévia: {(sql is null ? tabela : "consulta SQL")}  —  primeiras {_dadosPrevia.Rows.Count} linha(s)";
            MontarColunas();
            Log($"Prévia carregada: {_dadosPrevia.Columns.Count} colunas. Confira no passo 4 qual coluna vai para cada campo " +
                "(as escolhidas ficam azuis na prévia).");
        }
        catch (Exception ex)
        {
            Log("Erro ao ler: " + ex.Message);
            Ui.Aviso(this, "Não foi possível ler os dados:\n\n" + ex.Message);
        }
        finally
        {
            Ocupar(false);
        }
    }

    private Dictionary<string, string?> MapaEscolhido() =>
        _mapa.ToDictionary(m => m.Key, m => m.Value.SelectedIndex > 0 ? (string?)m.Value.SelectedItem : null);

    private async Task Importar(bool simular)
    {
        if (_ocupado || _origem is null || _dadosPrevia is null) return;

        var mapa = MapaEscolhido();
        var faltando = Destinos.Where(d => d.Obrigatorio && mapa[d.Chave] is null).Select(d => d.Rotulo).ToList();
        if (faltando.Count > 0)
        {
            Ui.Aviso(this, "Escolha a coluna de: " + string.Join(", ", faltando) + ".");
            return;
        }
        if (!simular && !Ui.Confirmar(this,
                $"Importar {(Produtos ? "os produtos" : "os clientes")} agora?\n\n" +
                (_atualizar.Checked ? "Os que já existem serão ATUALIZADOS com os dados da origem.\n" : "Os que já existem serão pulados.\n") +
                (Produtos && _estoque.Checked ? "O estoque da origem vira ajuste de inventário.\n" : "") +
                "\nDica: rode antes \"Testar sem gravar\"."))
            return;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            Ocupar(true, "Lendo todos os dados da origem...");
            _log.Clear();
            Log(simular ? "TESTE (nada será gravado)" : "IMPORTAÇÃO");
            Log("Lendo a origem...");

            var dados = await _origem.LerAsync((_tabela.SelectedItem as ItemTabela)?.Nome, _usarSql.Checked ? _sql.Text : null, null);
            // Planilha: a linha 1 é o cabeçalho, então os dados começam na 2 (igual ao número que aparece no Excel).
            var primeiraLinha = _origem.Tipo == TipoOrigem.Planilha ? 2 : 1;
            Log($"{dados.Rows.Count} linha(s) lida(s). Enviando para o servidor em lotes de {Lote}...");

            var total = new ImportacaoResultado();
            _progresso.Value = 0;
            _progresso.Maximum = Math.Max(1, dados.Rows.Count);
            _progresso.Visible = true;

            if (Produtos)
            {
                var itens = Mapeamento.ParaProdutos(dados, mapa, primeiraLinha);
                foreach (var lote in itens.Chunk(Lote))
                {
                    var r = await _api.PostAsync<ImportacaoResultado>("api/importacao/produtos", new ImportacaoRequest<ProdutoImportado>
                    {
                        Itens = lote.ToList(), AtualizarExistentes = _atualizar.Checked, ImportarEstoque = _estoque.Checked, Simular = simular,
                        PublicarNoCaixa = _publicar.Checked
                    });
                    Somar(total, r, lote.Length);
                }
            }
            else
            {
                var itens = Mapeamento.ParaClientes(dados, mapa, primeiraLinha);
                foreach (var lote in itens.Chunk(Lote))
                {
                    var r = await _api.PostAsync<ImportacaoResultado>("api/importacao/clientes", new ImportacaoRequest<ClienteImportado>
                    {
                        Itens = lote.ToList(), AtualizarExistentes = _atualizar.Checked, Simular = simular
                    });
                    Somar(total, r, lote.Length);
                }
            }

            var resumo = new StringBuilder();
            resumo.AppendLine();
            resumo.AppendLine(simular ? "Resultado do teste (nada foi gravado):" : $"Pronto em {sw.Elapsed.TotalSeconds:N0} s:");
            resumo.AppendLine($"  Novos:        {total.Inseridos}");
            resumo.AppendLine($"  Atualizados:  {total.Atualizados}");
            resumo.AppendLine($"  Pulados:      {total.Ignorados}");
            if (total.CategoriasCriadas > 0) resumo.AppendLine($"  Categorias novas: {total.CategoriasCriadas}");
            if (Produtos && !simular && _publicar.Checked) resumo.AppendLine($"  Publicados nos caixas: {total.Publicados}");
            if (total.Avisos.Count > 0)
            {
                resumo.AppendLine();
                resumo.AppendLine($"Avisos ({total.Avisos.Count}{(total.Avisos.Count >= 1000 ? "+, mostrando os primeiros" : "")}):");
                foreach (var a in total.Avisos.Take(1000)) resumo.AppendLine("  • " + a);
            }
            if (simular) resumo.AppendLine().AppendLine("Se estiver tudo certo, clique em Importar.");
            else if (Produtos)
                resumo.AppendLine().AppendLine(_publicar.Checked
                    ? "Os caixas recebem os produtos publicados na próxima sincronização."
                    : "Os produtos ficaram pendentes: envie em Caixas → Enviar produtos aos caixas.");
            Log(resumo.ToString());

            if (!simular)
                Ui.Info(this, $"Importação concluída.\n\nNovos: {total.Inseridos}\nAtualizados: {total.Atualizados}\nPulados: {total.Ignorados}" +
                              (total.Avisos.Count > 0 ? "\n\nVeja os avisos no quadro Resultado." : ""));
        }
        catch (Exception ex)
        {
            Log("\r\nParou com erro: " + ex.Message +
                (simular ? "" : "\r\nOs lotes enviados antes do erro já foram gravados. Pode rodar de novo: os repetidos são atualizados, não duplicados."));
            Ui.Aviso(this, "A importação parou:\n\n" + ex.Message);
        }
        finally
        {
            _progresso.Visible = false;
            Ocupar(false);
        }

        void Somar(ImportacaoResultado t, ImportacaoResultado r, int quantidade)
        {
            t.Inseridos += r.Inseridos;
            t.Atualizados += r.Atualizados;
            t.Ignorados += r.Ignorados;
            t.CategoriasCriadas += r.CategoriasCriadas;
            t.Publicados += r.Publicados;
            t.Avisos.AddRange(r.Avisos);
            _progresso.Value = Math.Min(_progresso.Maximum, _progresso.Value + quantidade);
            Log($"  ... {_progresso.Value} de {_progresso.Maximum}");
        }
    }

    // ---------- Ajudantes ----------

    private void Ocupar(bool ocupado, string? mensagem = null)
    {
        _ocupado = ocupado;
        UseWaitCursor = ocupado;
        _passos.Enabled = !ocupado;
        if (mensagem is not null) Log(mensagem);
        Application.DoEvents();
    }

    private void Log(string texto)
    {
        if (_log.TextLength > 0) _log.AppendText("\r\n");
        _log.AppendText(texto.Replace("\r\n", "\n").Replace("\n", "\r\n"));
    }
}
