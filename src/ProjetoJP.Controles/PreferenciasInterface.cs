using System.Text.Json;
using System.Text.Json.Serialization;
using ProjetoJP.Shared;

namespace ProjetoJP.Controles;

public enum ModoInterface { Simples, Completo }
public enum NavegacaoInterface { Lateral, Superior, Compacta }
public enum TamanhoInterface { Compacto, Normal, Grande }

/// <summary>
/// Preferencias de apresentacao de cada modulo, por usuario do Windows.
/// Nao guarda dados de autenticacao; nao modifica o PostgreSQL ou a configuracao fiscal.
/// </summary>
public sealed class PreferenciasInterface
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ModoInterface Modo { get; set; } = ModoInterface.Completo;
    public NavegacaoInterface Menu { get; set; } = NavegacaoInterface.Lateral;
    public TamanhoInterface Tamanho { get; set; } = TamanhoInterface.Normal;
    public bool MostrarPainelInicial { get; set; } = true;

    private static string Caminho(string modulo)
    {
        if (modulo is not ("master" or "retaguarda" or "pdv"))
            throw new ArgumentException("Modulo de interface invalido.", nameof(modulo));
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "ErpNaoFiscal", "interface", modulo + ".json");
    }

    public static bool Existe(string modulo) => File.Exists(Caminho(modulo));

    public static PreferenciasInterface Carregar(string modulo, bool? modoCompletoPadrao = null)
    {
        var padrao = new PreferenciasInterface
        {
            Modo = modoCompletoPadrao == false ? ModoInterface.Simples : ModoInterface.Completo,
            Menu = NavegacaoInterface.Lateral,
            MostrarPainelInicial = modulo == "retaguarda"
        };
        var caminho = Caminho(modulo);
        if (!File.Exists(caminho)) return padrao;
        try
        {
            var dados = JsonSerializer.Deserialize<PreferenciasInterface>(File.ReadAllText(caminho), Json);
            if (dados is null || !Enum.IsDefined(dados.Modo) || !Enum.IsDefined(dados.Menu)
                || !Enum.IsDefined(dados.Tamanho)) return padrao;
            return dados;
        }
        catch (JsonException) { return padrao; }
        catch (IOException) { return padrao; }
        catch (UnauthorizedAccessException) { return padrao; }
    }

    public void Salvar(string modulo)
    {
        var caminho = Caminho(modulo);
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        GravacaoAtomica.Texto(caminho, JsonSerializer.Serialize(this, Json));
    }

    /// <summary>Ajuste cuidadoso de densidade: altera grades sem deslocar campos com posição fixa.</summary>
    /// <param name="fonteNormal">Letra da grade no tamanho Normal (o caixa usa 11, maior que a Retaguarda).</param>
    public static void AplicarDensidade(Control raiz, TamanhoInterface tamanho, float fonteNormal = 10f)
    {
        var fonte = tamanho switch
        {
            TamanhoInterface.Compacto => fonteNormal - 1f,
            TamanhoInterface.Grande => fonteNormal + 1.5f,
            _ => fonteNormal
        };
        // Altura da linha acompanha a letra (e a escala do Windows, pela altura real da fonte).
        var altura = Tema.Fonte(fonte).Height + tamanho switch
        {
            TamanhoInterface.Compacto => 8, TamanhoInterface.Grande => 18, _ => 13
        };
        void Percorrer(Control controle)
        {
            if (controle is DataGridView grade)
            {
                grade.RowTemplate.Height = altura;
                foreach (DataGridViewRow linha in grade.Rows) linha.Height = altura;
                grade.ColumnHeadersHeight = altura + 6;
                grade.DefaultCellStyle.Font = Tema.Fonte(fonte);
                grade.ColumnHeadersDefaultCellStyle.Font = Tema.Semibold(fonte);
            }
            foreach (Control filho in controle.Controls) Percorrer(filho);
        }
        Percorrer(raiz);
    }
}

/// <summary>Painel leve e reutilizavel, para incorporar nas configuracoes do PDV e nos dialogos dos outros modulos.</summary>
public sealed class PainelPreferenciasInterface : Panel
{
    private readonly ComboBox _modo = CriarLista();
    private readonly ComboBox _menu = CriarLista();
    private readonly ComboBox _tamanho = CriarLista();
    private readonly CheckBox _inicio = new() { Text = "Exibir painel inicial ao abrir", AutoSize = true };
    private readonly string _modulo;
    private readonly PreferenciasInterface _original;

    private static ComboBox CriarLista() => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList, Font = Tema.Fonte(10), Width = 320, Height = 30
    };

    public PainelPreferenciasInterface(string modulo, bool? modoCompletoPadrao = null)
    {
        _modulo = modulo;
        Size = new Size(530, 350);
        BackColor = Tema.Superficie;
        var atual = PreferenciasInterface.Carregar(modulo, modoCompletoPadrao);
        _original = atual;
        _modo.Items.AddRange(["Simples — apenas o essencial", "Completo — todas as informações"]);
        _menu.Items.AddRange(["Barra lateral", "Barra superior", "Lateral compacta (ícones)"]);
        _tamanho.Items.AddRange(["Compacto", "Normal", "Grande"]);
        _modo.SelectedIndex = (int)atual.Modo;
        _menu.SelectedIndex = (int)atual.Menu;
        _tamanho.SelectedIndex = (int)atual.Tamanho;
        _inicio.Checked = atual.MostrarPainelInicial;

        var y = 0;
        void Linha(string titulo, Control campo)
        {
            Controls.Add(new Label
            {
                Text = titulo, AutoSize = true, Font = Tema.Semibold(10), ForeColor = Tema.Texto,
                Location = new Point(0, y)
            });
            campo.Location = new Point(0, y + 24);
            Controls.Add(campo);
            y += 74;
        }
        Linha("Modo de exibição", _modo);
        Linha("Posição do menu", _menu);
        Linha("Tamanho das tabelas e informações", _tamanho);
        _inicio.Location = new Point(0, y + 2);
        Controls.Add(_inicio);
        Controls.Add(new Label
        {
            Text = "A preferência vale somente neste módulo e usuário do Windows.\n" +
                   "As alterações são aplicadas na próxima abertura do programa.",
            ForeColor = Tema.TextoSuave, Font = Tema.Fonte(9),
            Location = new Point(0, y + 42), Size = new Size(510, 54)
        });
    }

    /// <summary>Salva as preferencias e indica se o usuario alterou alguma opcao visual.</summary>
    public bool Salvar()
    {
        var novas = new PreferenciasInterface
        {
            Modo = (ModoInterface)_modo.SelectedIndex,
            Menu = (NavegacaoInterface)_menu.SelectedIndex,
            Tamanho = (TamanhoInterface)_tamanho.SelectedIndex,
            MostrarPainelInicial = _inicio.Checked
        };
        var mudou = novas.Modo != _original.Modo
                    || novas.Menu != _original.Menu
                    || novas.Tamanho != _original.Tamanho
                    || novas.MostrarPainelInicial != _original.MostrarPainelInicial;
        novas.Salvar(_modulo);
        return mudou;
    }
}

public sealed class PreferenciasInterfaceForm : Form
{
    private readonly PainelPreferenciasInterface _painel;
    public bool PreferenciasAlteradas { get; private set; }

    public PreferenciasInterfaceForm(string modulo, bool? completoPadrao = null)
    {
        Text = "Configurações — Aparência e interface";
        ClientSize = new Size(600, 490);
        MinimumSize = new Size(510, 410);
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Tema.Superficie;
        Icon = Form.ActiveForm?.Icon ?? Icon;

        // Cabecalho, conteudo rolavel e botoes ficam em areas separadas.
        // O rodape nunca e coberto pelo painel, mesmo com escala do Windows.
        var estrutura = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(20, 8, 20, 8),
            BackColor = Tema.Superficie
        };
        estrutura.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        estrutura.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        estrutura.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        estrutura.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        Controls.Add(estrutura);

        var titulo = new Label
        {
            Text = "Aparência e interface",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = Tema.Semibold(15),
            ForeColor = Tema.Texto
        };
        estrutura.Controls.Add(titulo, 0, 0);

        _painel = new PainelPreferenciasInterface(modulo, completoPadrao)
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Margin = new Padding(0, 0, 0, 8)
        };
        estrutura.Controls.Add(_painel, 0, 1);

        var rodape = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 10, 0, 0),
            BackColor = Tema.Superficie
        };
        var salvar = new BotaoTema
        {
            Text = "Salvar preferências",
            Size = new Size(170, 38),
            Margin = new Padding(6, 0, 0, 0)
        };
        var cancelar = new BotaoTema
        {
            Text = "Cancelar",
            Estilo = EstiloBotao.Secundario,
            Size = new Size(120, 38),
            Margin = new Padding(6, 0, 0, 0)
        };
        salvar.Click += (_, _) =>
        {
            try
            {
                PreferenciasAlteradas = _painel.Salvar();
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Erro ao salvar", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        cancelar.Click += (_, _) => DialogResult = DialogResult.Cancel;
        rodape.Controls.AddRange([salvar, cancelar]);
        estrutura.Controls.Add(rodape, 0, 2);
        AcceptButton = salvar;
        CancelButton = cancelar;
    }

    public static void Abrir(IWin32Window dono, string modulo, bool? completoPadrao = null)
    {
        using var form = new PreferenciasInterfaceForm(modulo, completoPadrao);
        if (form.ShowDialog(dono) != DialogResult.OK) return;
        if (!form.PreferenciasAlteradas) return;

        if (MessageBox.Show(dono,
                "Preferências salvas com sucesso.\n\n" +
                "Deseja reiniciar o programa agora para aplicar as alterações?\n" +
                "Se escolher Não, elas serão aplicadas na próxima abertura.\n\n" +
                "Salve qualquer trabalho em andamento antes de reiniciar.",
                "Aplicar aparência", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
        {
            try { Aparencia.Reiniciar(); }
            catch (Exception ex)
            {
                MessageBox.Show(dono,
                    "As preferências foram salvas, mas não foi possível reiniciar automaticamente: " +
                    ex.Message + "\nFeche e abra o programa manualmente para aplicá-las.",
                    "Reinício não realizado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
