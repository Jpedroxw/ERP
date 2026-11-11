using System.Globalization;
using System.Reflection;
using System.Text;
using ProjetoJP.Controles;
using ProjetoJP.Shared;

namespace ProjetoJP.Gestao.Telas;

internal static class Ui
{
    public static readonly CultureInfo Br = new("pt-BR");

    // Cores do tema (ProjetoJP.Controles.Tema). Os nomes antigos continuam valendo nas telas.
    public static Color Fundo => Tema.Fundo;
    public static Color Escuro => Tema.Barra;
    public static Color Destaque => Tema.Primaria;
    public static Color Sucesso => Tema.Sucesso;
    public static Color Perigo => Tema.Perigo;
    public static Color Neutro => Tema.TextoSuave;

    public static Font Fonte(float tamanho, FontStyle estilo = FontStyle.Regular) => Tema.Fonte(tamanho, estilo);

    public static Label Rotulo(string texto, float tamanho = 10, FontStyle estilo = FontStyle.Regular) =>
        new() { Text = texto, AutoSize = true, Font = Fonte(tamanho, estilo), ForeColor = Tema.Texto, BackColor = Color.Transparent };

    /// <summary>
    /// Botão do tema. A cor escolhe o estilo: nenhuma = azul (principal), Neutro = branco com borda,
    /// Escuro = azul-marinho, Perigo = vermelho.
    /// </summary>
    public static BotaoTema Botao(string texto, Color? cor = null, int largura = 120, int altura = 34, string? icone = null)
    {
        var estilo =
            cor == Perigo ? EstiloBotao.Perigo :
            cor == Neutro ? EstiloBotao.Secundario :
            cor == Escuro ? EstiloBotao.Escuro :
            cor == Sucesso ? EstiloBotao.Sucesso :
            EstiloBotao.Primario;
        return new BotaoTema
        {
            Text = texto,
            Estilo = estilo,
            Icone = icone,
            Size = new Size(largura, altura),
            Margin = new Padding(0, 0, 8, 0)
        };
    }


    private static Icon? _iconeApp;

    /// <summary>Ícone do programa (o mesmo do .exe), usado na barra de título e na barra de tarefas.</summary>
    public static Icon? IconeApp
    {
        get
        {
            if (_iconeApp is null)
            {
                try
                {
                    // retaguarda.ico embutido no programa (todos os tamanhos: o Windows escolhe o certo).
                    using var s = typeof(Ui).Assembly.GetManifestResourceStream("ProjetoJP.Gestao.retaguarda.ico");
                    _iconeApp = s is not null ? new Icon(s) : Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                }
                catch (Exception) { /* sem ícone: o Windows usa o padrão */ }
            }
            return _iconeApp;
        }
    }

    public static void Dialogo(Form f, string titulo, int largura, int altura)
    {
        f.Text = titulo;
        f.Icon = IconeApp ?? f.Icon;
        f.FormBorderStyle = FormBorderStyle.FixedDialog;
        f.StartPosition = FormStartPosition.CenterParent;
        f.MaximizeBox = false;
        f.MinimizeBox = false;
        f.ShowInTaskbar = false;
        f.ClientSize = new Size(largura, altura);
        f.BackColor = Tema.Superficie;
        f.ForeColor = Tema.Texto;
        f.Font = Fonte(10);
        JanelaNaTela.Limitar(f); // monitor pequeno: a janela não passa da tela
    }

    public static DataGridView Grade()
    {
        var g = new DataGridView
        {
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        Tema.EstilizarGrade(g, 10);

        // Cada coluna mostra o valor no formato do campo (R$, CPF, telefone...).
        // Datas em UTC vindas da API aparecem no horário local; números alinhados à direita.
        g.CellFormatting += (_, e) =>
        {
            if (e.Value is null || e.ColumnIndex < 0) return;

            if (g.Columns[e.ColumnIndex].Tag is TipoCampo tipo)
            {
                switch (tipo)
                {
                    case TipoCampo.Moeda when e.Value is decimal m:
                        e.Value = Formatos.Moeda(m);
                        e.CellStyle!.Alignment = DataGridViewContentAlignment.MiddleRight;
                        e.FormattingApplied = true;
                        return;
                    case TipoCampo.Quantidade when e.Value is decimal q:
                        e.Value = Formatos.Quantidade(q);
                        e.CellStyle!.Alignment = DataGridViewContentAlignment.MiddleRight;
                        e.FormattingApplied = true;
                        return;
                    case TipoCampo.CpfCnpj when e.Value is string doc:
                        e.Value = Formatos.FormatarCpfCnpj(doc);
                        e.FormattingApplied = true;
                        return;
                    case TipoCampo.Telefone when e.Value is string tel:
                        e.Value = Formatos.FormatarTelefone(tel);
                        e.FormattingApplied = true;
                        return;
                }
            }

            switch (e.Value)
            {
                case DateTime d:
                    e.Value = (d.Kind == DateTimeKind.Utc ? d.ToLocalTime() : d).ToString("dd/MM/yyyy HH:mm", Br);
                    e.FormattingApplied = true;
                    break;
                case DateOnly dia:
                    e.Value = dia.ToString("dd/MM/yyyy", Br);
                    e.FormattingApplied = true;
                    break;
                case decimal n:
                    e.Value = n.ToString("N2", Br);
                    e.CellStyle!.Alignment = DataGridViewContentAlignment.MiddleRight;
                    e.FormattingApplied = true;
                    break;
                case int:
                    e.CellStyle!.Alignment = DataGridViewContentAlignment.MiddleRight;
                    break;
            }
        };
        return g;
    }

    /// <summary>Coluna de texto para grades montadas à mão (sem DataSource). A largura vira peso no modo Fill.</summary>
    public static DataGridViewTextBoxColumn ColunaTexto(string titulo, int largura, bool direita = false)
    {
        var c = new DataGridViewTextBoxColumn { HeaderText = titulo, FillWeight = largura, MinimumWidth = 60, SortMode = DataGridViewColumnSortMode.NotSortable };
        if (direita) c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        return c;
    }

    /// <summary>Marca cada coluna da grade com o formato do campo (atributo [Campo] do DTO).</summary>
    public static void AplicarFormatos(DataGridView grade, Type tipo)
    {
        foreach (DataGridViewColumn c in grade.Columns)
        {
            var campo = tipo.GetProperty(c.DataPropertyName)?.GetCustomAttribute<CampoAttribute>();
            c.Tag = campo?.Tipo;
            if (campo?.Tipo is TipoCampo.Moeda or TipoCampo.Quantidade or TipoCampo.Inteiro)
                c.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
        }
    }

    public static void Aviso(IWin32Window? dono, string msg) =>
        MessageBox.Show(dono, msg, "Atenção", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    public static void Erro(IWin32Window? dono, string msg) =>
        MessageBox.Show(dono, msg, "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);

    public static void Info(IWin32Window? dono, string msg) =>
        MessageBox.Show(dono, msg, "Informação", MessageBoxButtons.OK, MessageBoxIcon.Information);

    public static bool Confirmar(IWin32Window? dono, string msg) =>
        MessageBox.Show(dono, msg, "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    /// <summary>Exporta as colunas visíveis da grade para CSV (abre no Excel em português).</summary>
    public static void ExportarCsv(IWin32Window dono, DataGridView grade, string nomeSugerido)
    {
        using var dlg = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = nomeSugerido + ".csv" };
        if (dlg.ShowDialog(dono) != DialogResult.OK) return;

        var colunas = grade.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).OrderBy(c => c.DisplayIndex).ToList();
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(";", colunas.Select(c => Csv(c.HeaderText))));
        foreach (DataGridViewRow linha in grade.Rows)
            sb.AppendLine(string.Join(";", colunas.Select(c => Csv(linha.Cells[c.Index].FormattedValue?.ToString() ?? ""))));

        File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
        Info(dono, "Arquivo exportado.");
    }

    private static string Csv(string s) =>
        s.Contains(';') || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}
