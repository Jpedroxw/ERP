using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using ProjetoJP.Controles;
using ProjetoJP.Gestao.Servicos;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;

namespace ProjetoJP.Gestao.Telas;

/// <summary>
/// Tela de cadastro genérica: lista + filtro + novo/editar/desativar.
/// Os campos e colunas saem dos atributos do DTO (DisplayName, ReadOnly, Lookup...).
/// </summary>
public class CadastroForm<T> : Form where T : class, new()
{
    private readonly ApiClient _api;
    private readonly string _endpoint;
    private readonly string _titulo;
    private readonly DataGridView _grade = Ui.Grade();
    private readonly TextBox _filtro = new() { Width = 260, Font = Ui.Fonte(10.5f) };
    private readonly CheckBox _inativos = new() { Text = "Mostrar inativos", AutoSize = true, Margin = new Padding(12, 8, 0, 0) };
    private readonly Label _contagem = Ui.Rotulo("", 9);
    private List<T> _todos = [];

    private static readonly PropertyInfo PropId = typeof(T).GetProperty("Id")!;
    private static readonly PropertyInfo? PropAtivo = typeof(T).GetProperty("Ativo");

    /// <summary>Chamado depois de gravar (item salvo, se era novo).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Action<T, bool>? AposSalvar { get; set; }

    private readonly bool _completo;
    private readonly bool _podeEditar;

    /// <param name="completo">Modo completo: todas as colunas, exportar e "mostrar inativos". Modo simples: só o essencial.</param>
    /// <param name="podeEditar">false = só consulta (o usuário não tem a permissão de cadastrar/alterar).</param>
    public CadastroForm(ApiClient api, string titulo, string endpoint, bool completo = true, bool podeEditar = true)
    {
        _api = api;
        _titulo = titulo;
        _completo = completo;
        _podeEditar = podeEditar;
        _endpoint = endpoint.TrimEnd('/');
        Text = titulo;
        Size = new Size(1000, 640);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Ui.Fundo;
        Font = Ui.Fonte(10);
        KeyPreview = true;
        Padding = new Padding(20, 12, 20, 20);

        // Cabeçalho: título e quantidade de registros
        var cabecalho = new Panel { Dock = DockStyle.Top, Height = 58 };
        var lblTitulo = new Label { Text = titulo, AutoSize = true, Font = Tema.Semibold(16), ForeColor = Tema.Texto, Location = new Point(0, 2) };
        _contagem.ForeColor = Tema.TextoSuave;
        _contagem.Location = new Point(2, 34);
        cabecalho.Controls.AddRange([lblTitulo, _contagem]);

        // Barra de ações
        var barra = new CartaoPainel { Dock = DockStyle.Top, Height = 60, Padding = new Padding(12) };
        var acoes = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false, BackColor = Tema.Superficie };
        var novo = Ui.Botao("Novo", largura: 100, altura: 36, icone: Icones.Novo);
        novo.Click += async (_, _) => await Novo();
        var editar = Ui.Botao("Editar", Ui.Neutro, 100, 36, Icones.Editar);
        editar.Click += async (_, _) => await Editar();
        var desativar = Ui.Botao("Desativar", Ui.Neutro, 116, 36, Icones.Excluir);
        desativar.Click += async (_, _) => await Desativar();
        var atualizar = Ui.Botao("Atualizar", Ui.Neutro, 112, 36, Icones.Atualizar);
        atualizar.Click += async (_, _) => await CarregarAsync();
        acoes.Controls.AddRange([novo, editar, desativar, atualizar]);
        if (!podeEditar)
        {
            novo.Visible = editar.Visible = desativar.Visible = false;
            _contagem.Text = "somente consulta";
        }
        if (typeof(T) == typeof(UsuarioDto))
        {
            // O que cada usuário pode ver e fazer (Retaguarda e caixa).
            var permissoes = Ui.Botao("Permissões", Ui.Neutro, 140, 36, Icones.Cadeado);
            permissoes.Click += (_, _) =>
            {
                if (Selecionado is not UsuarioDto u) { Ui.Aviso(this, "Selecione um usuário."); return; }
                using var f = new PermissoesForm(_api, u.Id, u.Nome);
                f.ShowDialog(this);
            };
            acoes.Controls.Add(permissoes);
        }
        if (typeof(T) == typeof(TerminalDto))
        {
            var copiarChave = Ui.Botao("Copiar chave", Ui.Neutro, 145, 36, Icones.Colar);
            copiarChave.Click += (_, _) =>
            {
                if (Selecionado is not TerminalDto t || string.IsNullOrWhiteSpace(t.Chave))
                {
                    Ui.Aviso(this, "Selecione um terminal para copiar a chave.");
                    return;
                }
                try { Clipboard.SetText(t.Chave); Ui.Info(this, "Chave do terminal copiada."); }
                catch (System.Runtime.InteropServices.ExternalException)
                { Ui.Aviso(this, "A área de transferência está ocupada. Tente novamente."); }
            };
            acoes.Controls.Add(copiarChave);
        }
        if (completo)
        {
            var exportar = Ui.Botao("Exportar", Ui.Neutro, 108, 36, Icones.Exportar);
            exportar.Click += (_, _) => Ui.ExportarCsv(this, _grade, _titulo);
            acoes.Controls.Add(exportar);
        }

        var busca = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, AutoSize = true, WrapContents = false,
            FlowDirection = FlowDirection.RightToLeft, BackColor = Tema.Superficie
        };
        var larguraBusca = completo ? 220 : 280;
        _filtro.Width = larguraBusca;
        _filtro.PlaceholderText = "Buscar...";
        _filtro.Margin = new Padding(0, 5, 8, 0);
        var lupa = new Label
        {
            Text = Icones.Buscar, Font = Tema.FonteIcone(11), ForeColor = Tema.TextoSuave,
            AutoSize = true, Margin = new Padding(0, 9, 2, 0)
        };
        if (completo)
        {
            _inativos.Margin = new Padding(4, 8, 0, 0);
            _inativos.ForeColor = Tema.TextoSuave;
            busca.Controls.Add(_inativos);
        }
        busca.Controls.AddRange([_filtro, lupa]);
        barra.Controls.Add(busca);
        barra.Controls.Add(acoes);
        // Tela estreita (1024): o campo de busca encolhe (até 140) para não ficar por cima dos botões.
        barra.Resize += (_, _) =>
        {
            var sobra = barra.ClientSize.Width - barra.Padding.Horizontal - acoes.Width - (busca.Width - _filtro.Width) - 12;
            _filtro.Width = Math.Clamp(sobra, 140, larguraBusca);
        };

        var espaco = new Panel { Dock = DockStyle.Top, Height = 12 };

        var cartaoGrade = new CartaoPainel { Dock = DockStyle.Fill, Padding = new Padding(1, 6, 1, 6) };
        _grade.Dock = DockStyle.Fill;
        cartaoGrade.Controls.Add(_grade);

        Controls.Add(cartaoGrade);
        Controls.Add(espaco);
        Controls.Add(barra);
        Controls.Add(cabecalho);

        new ToolTip().SetToolTip(novo, "Novo registro (Insert)");
        new ToolTip().SetToolTip(editar, "Editar o selecionado (Enter ou duplo clique)");
        new ToolTip().SetToolTip(desativar, "Desativar o selecionado (Delete)");
        new ToolTip().SetToolTip(atualizar, "Recarregar do servidor (F5)");

        _grade.DataBindingComplete += (_, _) => AjustarColunas();
        _grade.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0) await Editar(); };
        _filtro.TextChanged += (_, _) => Filtrar();
        _inativos.CheckedChanged += (_, _) => Filtrar();
        KeyDown += async (_, e) =>
        {
            switch (e.KeyCode)
            {
                case Keys.Insert: e.Handled = true; await Novo(); break;
                case Keys.Enter when _grade.Focused: e.Handled = true; e.SuppressKeyPress = true; await Editar(); break;
                case Keys.Delete when _grade.Focused: e.Handled = true; await Desativar(); break;
                case Keys.F5: e.Handled = true; await CarregarAsync(); break;
            }
        };

        Shown += async (_, _) => await CarregarAsync();
    }

    private async Task CarregarAsync()
    {
        try
        {
            UseWaitCursor = true;
            _todos = await _api.GetAsync<List<T>>(_endpoint);
            Filtrar();
        }
        catch (ApiException ex)
        {
            Ui.Erro(this, ex.Message);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void Filtrar()
    {
        var texto = _filtro.Text.Trim();
        var props = typeof(T).GetProperties().Where(p => p.PropertyType == typeof(string)).ToList();

        var lista = _todos.Where(item =>
            (_inativos.Checked || PropAtivo is null || (bool)PropAtivo.GetValue(item)!) &&
            (texto.Length == 0 || props.Any(p => (p.GetValue(item) as string)?.Contains(texto, StringComparison.CurrentCultureIgnoreCase) == true)))
            .ToList();

        _grade.DataSource = lista;
        _contagem.Text = $"{lista.Count} registro(s)" + (_podeEditar ? "" : "  ·  somente consulta");
    }

    private void AjustarColunas()
    {
        foreach (DataGridViewColumn c in _grade.Columns)
        {
            var p = typeof(T).GetProperty(c.DataPropertyName);
            if (p is null) continue;
            var tipo = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            var senha = p.GetCustomAttribute<PasswordPropertyTextAttribute>()?.Password == true;
            var soEdicao = p.GetCustomAttribute<LookupAttribute>() is not null;
            var detalhe = p.GetCustomAttribute<DetalheAttribute>() is not null;
            if (tipo == typeof(Guid) || senha || soEdicao || (detalhe && !_completo)) c.Visible = false;
        }
        Ui.AplicarFormatos(_grade, typeof(T));
    }

    private T? Selecionado => _grade.CurrentRow?.DataBoundItem as T;

    private async Task Novo()
    {
        if (!_podeEditar) return;
        var item = new T();
        using var f = EdicaoForm.Para("Novo — " + _titulo, item, _api);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var salvo = await _api.PostAsync<T>(_endpoint, item);
            await CarregarAsync();
            Selecionar((Guid)PropId.GetValue(salvo)!);
            AposSalvar?.Invoke(salvo, true);
            AvisarCarga();
        }
        catch (ApiException ex) { Ui.Erro(this, ex.Message); }
    }

    private async Task Editar()
    {
        if (!_podeEditar || Selecionado is not { } atual) return;
        // Edita uma cópia: se cancelar, a grade não muda.
        var copia = JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(atual))!;
        using var f = EdicaoForm.Para("Editar — " + _titulo, copia, _api);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var id = (Guid)PropId.GetValue(copia)!;
            var salvo = await _api.PutAsync<T>($"{_endpoint}/{id}", copia);
            await CarregarAsync();
            Selecionar(id);
            AposSalvar?.Invoke(salvo, false);
            AvisarCarga();
        }
        catch (ApiException ex) { Ui.Erro(this, ex.Message); }
    }

    private static bool _avisouCarga;

    /// <summary>
    /// Produtos: lembra (uma vez por sessão, não a cada salvamento) que a mudança só vai para os caixas
    /// depois de publicada em Cargas para caixas.
    /// </summary>
    private void AvisarCarga()
    {
        if (typeof(T) != typeof(ProdutoDto) || _avisouCarga) return;
        _avisouCarga = true;
        Ui.Info(this, "Salvo na Gestão.\n\nPara os caixas usarem esse produto (ou a alteração), publique em " +
                      "Caixas → Enviar produtos aos caixas. Até lá, os caixas continuam com os dados anteriores.\n\n" +
                      "(Este lembrete aparece só uma vez por sessão.)");
    }

    private async Task Desativar()
    {
        if (!_podeEditar || Selecionado is not { } atual) return;
        var mensagem = typeof(T) == typeof(ProdutoDto)
            ? "Desativar o produto?\n\nNos caixas ele só some depois de enviado em Caixas → Enviar produtos aos caixas."
            : "Desativar o registro selecionado?\n(ele deixa de aparecer no caixa, mas o histórico é mantido)";
        if (!Ui.Confirmar(this, mensagem)) return;
        try
        {
            await _api.DeleteAsync($"{_endpoint}/{PropId.GetValue(atual)}");
            await CarregarAsync();
        }
        catch (ApiException ex) { Ui.Erro(this, ex.Message); }
    }

    private void Selecionar(Guid id)
    {
        foreach (DataGridViewRow r in _grade.Rows)
        {
            if (r.DataBoundItem is T item && (Guid)PropId.GetValue(item)! == id)
            {
                _grade.CurrentCell = r.Cells.Cast<DataGridViewCell>().First(c => c.Visible);
                return;
            }
        }
    }
}

/// <summary>
/// Formulário de edição montado por reflexão a partir das propriedades do DTO.
/// O atributo [Campo] de cada propriedade decide o controle (R$, CPF/CNPJ, telefone...) e a validação.
/// </summary>
public class EdicaoForm : Form
{
    private sealed record Item(PropertyInfo Prop, Control Controle, CampoAttribute? Formato, string Rotulo);

    private static readonly TipoCampo[] Curtos =
        [TipoCampo.Moeda, TipoCampo.Quantidade, TipoCampo.Inteiro, TipoCampo.CpfCnpj, TipoCampo.Telefone, TipoCampo.CodigoBarras, TipoCampo.Unidade,
         TipoCampo.CodigoProduto];

    private readonly object _obj;
    private readonly ApiClient _api;
    private readonly List<Item> _campos = [];

    /// <summary>Janela de edição certa para o registro: produto tem a sua (blocos, margem, atacado); os outros, a automática.</summary>
    public static Form Para(string titulo, object obj, ApiClient api) =>
        obj is ProdutoDto produto ? new ProdutoEdicaoForm(titulo, produto, api) : new EdicaoForm(titulo, obj, api);

    public EdicaoForm(string titulo, object obj, ApiClient api)
    {
        _obj = obj;
        _api = api;
        Ui.Dialogo(this, titulo, 560, 200);

        // Campos curtos (R$, quantidade, unidade, categoria...) vão em duas colunas, lado a lado, para a janela
        // caber inteira em telas baixas (1366 x 768) sem rolar. Texto longo e caixas de marcar ocupam a linha toda.
        var campos = new List<(PropertyInfo Prop, string Rotulo, CampoAttribute? Formato, Control Controle, bool Curto)>();
        foreach (var p in obj.GetType().GetProperties())
        {
            if (!p.CanWrite) continue;
            if (p.GetCustomAttribute<BrowsableAttribute>()?.Browsable == false) continue;
            if (p.GetCustomAttribute<SomenteGradeAttribute>() is not null) continue;

            var rotulo = p.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? p.Name;
            var formato = p.GetCustomAttribute<CampoAttribute>();
            var somenteLeitura = p.GetCustomAttribute<ReadOnlyAttribute>()?.IsReadOnly == true;

            var controle = CriarControle(p, p.GetValue(obj), formato);
            controle.Font = Ui.Fonte(10.5f);
            // Texto somente leitura continua selecionável (dá para copiar a chave do terminal, por exemplo).
            if (somenteLeitura && controle is TextBox caixaTexto)
            {
                caixaTexto.ReadOnly = true;
                caixaTexto.TabStop = false;
            }
            else
            {
                controle.Enabled = !somenteLeitura;
            }
            var curto = controle is not CheckBox &&
                        (controle is ComboBox || (formato is not null && Curtos.Contains(formato.Tipo)) ||
                         (formato is null && p.PropertyType != typeof(string)));
            campos.Add((p, rotulo, formato, controle, curto));
        }

        const int margem = 24, larguraCheia = 510, larguraMeia = 245, segundaColuna = margem + larguraMeia + 20;
        var dicas = new ToolTip();
        FormClosed += (_, _) => dicas.Dispose();
        var y = 18;

        // Rótulo em cima, controle embaixo; a dica (cinza) vai ao lado do rótulo na linha cheia
        // e embaixo do controle na meia linha. Devolve a altura usada.
        int Colocar(int i, int x, int largura, bool meia)
        {
            var (p, rotulo, formato, controle, _) = campos[i];
            var l = Ui.Rotulo(rotulo + (formato?.Obrigatorio == true ? " *" : ""), 9.5f);
            l.ForeColor = Ui.Neutro;
            l.Location = new Point(x, y);
            Controls.Add(l);
            controle.Location = new Point(x, y + 20);
            controle.Width = largura;
            Controls.Add(controle);
            var altura = 28 + controle.Height + 8;

            if (formato?.Dica is { Length: > 0 } dica)
            {
                var d = new Label
                {
                    Text = meia ? dica : "— " + dica, Font = Ui.Fonte(8.5f), ForeColor = Tema.TextoSuave, AutoSize = false,
                    AutoEllipsis = true, BackColor = Color.Transparent
                };
                if (meia)
                {
                    d.SetBounds(x, y + 20 + controle.Height + 2, largura, 16);
                    altura += 12;
                }
                else
                {
                    d.SetBounds(x + l.PreferredWidth + 4, y + 1, Math.Max(40, largura - l.PreferredWidth - 4), 18);
                }
                dicas.SetToolTip(d, dica);
                dicas.SetToolTip(controle, dica);
                Controls.Add(d);
            }
            _campos.Add(new Item(p, controle, formato, rotulo));
            return altura;
        }

        for (var i = 0; i < campos.Count; i++)
        {
            var c = campos[i];
            if (c.Controle is CheckBox cb)
            {
                cb.Text = c.Rotulo;
                cb.Location = new Point(margem, y);
                Controls.Add(cb);
                _campos.Add(new Item(c.Prop, cb, c.Formato, c.Rotulo));
                y += 34;
            }
            else if (c.Curto && i + 1 < campos.Count && campos[i + 1].Curto)
            {
                var a1 = Colocar(i, margem, larguraMeia, meia: true);
                var a2 = Colocar(i + 1, segundaColuna, larguraMeia, meia: true);
                y += Math.Max(a1, a2);
                i++;
            }
            else
            {
                y += Colocar(i, margem, c.Curto ? larguraMeia : larguraCheia, meia: c.Curto);
            }
        }

        VincularUnidade();

        var ok = Ui.Botao("Salvar", largura: 140, altura: 38);
        ok.Location = new Point(24, y + 10);
        ok.Click += (_, _) => Salvar();
        var cancelar = Ui.Botao("Cancelar", Ui.Neutro, 140, 38);
        cancelar.Location = new Point(174, y + 10);
        cancelar.DialogResult = DialogResult.Cancel;
        var obrigatorios = Ui.Rotulo("* obrigatório", 8.5f);
        obrigatorios.ForeColor = Tema.TextoSuave;
        obrigatorios.Location = new Point(330, y + 22);
        Controls.AddRange([ok, cancelar, obrigatorios]);
        CancelButton = cancelar;
        // Com as duas colunas cabe em 1366 x 768. Só com o Windows em escala grande e muitos campos a janela
        // passaria da tela: aí ela para na área útil e rola, em vez de esconder o botão Salvar.
        var areaUtil = Screen.PrimaryScreen?.WorkingArea.Height ?? 768;
        ClientSize = new Size(560, Math.Min(y + 66, areaUtil - 70));
        AutoScroll = y + 66 > areaUtil - 70;

        Shown += async (_, _) => await CarregarListasAsync();
    }

    private static Control CriarControle(PropertyInfo p, object? valor, CampoAttribute? formato)
    {
        var tipo = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;

        if (tipo == typeof(bool))
            return new CheckBox { Checked = valor is true, AutoSize = true };

        if (tipo.IsEnum)
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var v in Enum.GetValues(tipo)) combo.Items.Add(v);
            combo.SelectedItem = valor;
            return combo;
        }

        if (p.GetCustomAttribute<LookupAttribute>() is not null)
            return new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Tag = valor };

        if (tipo == typeof(DateTime))
        {
            var texto = valor is DateTime dt ? (dt.Kind == DateTimeKind.Utc ? dt.ToLocalTime() : dt).ToString("dd/MM/yyyy HH:mm") : "";
            return new TextBox { Text = texto, ReadOnly = true };
        }

        Control c;
        if (formato is not null) c = Campos.Criar(formato.Tipo, formato.Tamanho);
        else if (tipo == typeof(decimal)) c = new CampoNumero { CasasDecimais = 2 };
        else if (tipo == typeof(int)) c = new CampoNumero { CasasDecimais = 0, Maximo = 999_999 };
        else c = new TextBox();

        switch (c)
        {
            case CampoMoeda m:
                m.Valor = Convert.ToDecimal(valor ?? 0m);
                break;
            case CampoNumero n:
                n.Valor = Convert.ToDecimal(valor ?? 0m);
                break;
            case ComboBox unidade:
                var u = (valor as string ?? "").Trim().ToUpperInvariant();
                if (unidade.Items.Contains(u)) unidade.SelectedItem = u;
                break;
            case TextBox t:
                t.Text = valor?.ToString() ?? "";
                if (p.GetCustomAttribute<MultilinhaAttribute>() is not null)
                {
                    t.Multiline = true;
                    t.Height = 70;
                    t.ScrollBars = ScrollBars.Vertical;
                }
                if (p.GetCustomAttribute<PasswordPropertyTextAttribute>()?.Password == true)
                    t.UseSystemPasswordChar = true;
                break;
        }
        return c;
    }

    /// <summary>Quantidades do produto seguem a unidade: KG/L/M... com 3 casas, UN/CX/PC... inteiras.</summary>
    private void VincularUnidade()
    {
        if (_campos.FirstOrDefault(c => c.Formato?.Tipo == TipoCampo.Unidade)?.Controle is not ComboBox unidade) return;

        var quantidades = _campos
            .Where(c => c.Formato?.Tipo == TipoCampo.Quantidade)
            .Select(c => c.Controle)
            .OfType<CampoNumero>()
            .ToList();

        void Aplicar()
        {
            var casas = Formatos.CasasQuantidade(unidade.SelectedItem as string);
            foreach (var q in quantidades) q.CasasDecimais = casas;
        }

        unidade.SelectedIndexChanged += (_, _) => Aplicar();
        Aplicar();
    }

    private async Task CarregarListasAsync()
    {
        foreach (var c in _campos)
        {
            var lookup = c.Prop.GetCustomAttribute<LookupAttribute>();
            if (lookup is null || c.Controle is not ComboBox combo) continue;
            try
            {
                var itens = await _api.GetAsync<List<LookupItem>>(lookup.Endpoint);
                combo.Items.Clear();
                combo.Items.Add(new LookupItem { Id = Guid.Empty, Nome = "(nenhum)" });
                foreach (var i in itens) combo.Items.Add(i);
                var atual = combo.Tag as Guid?;
                combo.SelectedItem = combo.Items.Cast<LookupItem>().FirstOrDefault(i => i.Id == (atual ?? Guid.Empty));
            }
            catch (ApiException ex)
            {
                Ui.Erro(this, ex.Message);
            }
        }
    }

    private void Salvar()
    {
        // Lê e valida tudo antes de mexer no objeto: se um campo estiver errado, nada é alterado.
        var valores = new List<(PropertyInfo prop, object? valor)>();

        foreach (var (p, controle, formato, rotulo) in _campos)
        {
            if (!controle.Enabled || controle is TextBox { ReadOnly: true }) continue;
            var tipo = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;

            object? valor = controle switch
            {
                CheckBox cb => cb.Checked,
                ComboBox { SelectedItem: LookupItem li } => li.Id == Guid.Empty ? (object?)null : li.Id,
                ComboBox cmb when tipo.IsEnum => cmb.SelectedItem,
                ComboBox { SelectedItem: string s } => s,
                ComboBox => p.GetValue(_obj), // lista ainda não carregou: mantém
                CampoMoeda m => m.Valor,
                CampoNumero n when tipo == typeof(int) => (int)n.Valor,
                CampoNumero n => n.Valor,
                CampoFormatado f => f.Limpo.Length == 0 ? null : f.Limpo,
                TextBox { UseSystemPasswordChar: true } t => t.Text.Length == 0 ? null : t.Text,
                TextBox t => string.IsNullOrWhiteSpace(t.Text) ? null : t.Text.Trim(),
                _ => p.GetValue(_obj)
            };

            // Código de produto antigo que não mudou: não aplica a regra nova (o servidor também aceita).
            var codigoAntigoIgual = formato?.Tipo == TipoCampo.CodigoProduto && valor is string &&
                                    Equals(valor, p.GetValue(_obj));
            var erro = codigoAntigoIgual ? null
                : Validar(formato, rotulo, valor, p.GetCustomAttribute<MultilinhaAttribute>() is not null);
            if (erro is not null)
            {
                Ui.Aviso(this, erro);
                controle.Focus();
                return;
            }

            // string não anulável recebe "" em vez de null
            if (valor is null && p.PropertyType == typeof(string) && new NullabilityInfoContext().Create(p).WriteState == NullabilityState.NotNull)
                valor = "";

            valores.Add((p, valor));
        }

        foreach (var (p, valor) in valores) p.SetValue(_obj, valor);
        DialogResult = DialogResult.OK;
    }

    private static string? Validar(CampoAttribute? f, string rotulo, object? valor, bool multilinha = false)
    {
        if (f is null) return null;

        var texto = valor as string ?? "";
        var vazio = valor is null || (valor is string s && s.Length == 0);

        if (f.Obrigatorio)
        {
            if (vazio) return $"Preencha o campo \"{rotulo}\".";
            if (f.Tipo == TipoCampo.Moeda && valor is decimal d && d <= 0) return $"Informe o valor de \"{rotulo}\".";
        }
        if (vazio) return null;
        if (valor is string textoEntrada &&
            !Formatos.TextoSeguro(textoEntrada, multilinha: multilinha))
            return $"O campo \"{rotulo}\" contém caracteres de controle ou invisíveis.";

        return f.Tipo switch
        {
            TipoCampo.CpfCnpj when texto.Length is not (11 or 14) =>
                "CPF deve ter 11 dígitos e CNPJ 14 caracteres.",
            TipoCampo.CpfCnpj when !Formatos.DocumentoDigitadoValido(texto) =>
                texto.Length == 11 ? "CPF inválido: confira os números." : "CNPJ inválido: confira os caracteres.",
            TipoCampo.Telefone when !Formatos.TelefoneDigitadoValido(texto) =>
                "Telefone incompleto: informe o DDD e o número.",
            TipoCampo.Email when !Formatos.EmailValido(texto) =>
                "E-mail inválido.",
            TipoCampo.CodigoBarras when !Formatos.GtinValido(texto) =>
                "Código de barras inválido: precisa ter 8, 12, 13 ou 14 dígitos e o dígito verificador correto.",
            TipoCampo.CodigoProduto when !Formatos.CodigoProdutoValido(texto) =>
                "Código do produto inválido: utilize letras, números, ponto, hífen, barra ou sublinhado.",
            TipoCampo.Login when !Formatos.LoginValido(texto) =>
                "O login precisa ter de 3 a 60 caracteres.",
            _ => null
        };
    }
}
