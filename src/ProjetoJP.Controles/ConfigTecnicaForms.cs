using ProjetoJP.Shared;

namespace ProjetoJP.Controles;

/// <summary>Detalhes comuns das janelas da Configuração técnica (visual igual ao resto do sistema).</summary>
internal static class JanelaTecnica
{
    public static void Preparar(Form f, string titulo, int largura, int altura)
    {
        f.Text = titulo;
        f.FormBorderStyle = FormBorderStyle.FixedDialog;
        f.StartPosition = FormStartPosition.CenterParent;
        f.MaximizeBox = false;
        f.MinimizeBox = false;
        f.ShowInTaskbar = false;
        f.ClientSize = new Size(largura, altura);
        f.BackColor = Tema.Superficie;
        f.ForeColor = Tema.Texto;
        f.Font = Tema.Fonte(10);
        // Mesmo ícone do programa que abriu a janela
        f.Load += (_, _) =>
        {
            var icone = f.Owner?.Icon ?? (Application.OpenForms.Count > 0 ? Application.OpenForms[0]?.Icon : null);
            if (icone is not null) f.Icon = icone;
        };
    }

    /// <summary>Faixa azul-marinho do topo, como nas outras janelas.</summary>
    public static Panel Topo(string titulo, string subtitulo)
    {
        var topo = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = Tema.Barra };
        topo.Controls.Add(new Label { Text = Icones.Cadeado, Font = Tema.FonteIcone(18), ForeColor = Color.White, AutoSize = true, Location = new Point(22, 24) });
        topo.Controls.Add(new Label { Text = titulo, Font = Tema.Semibold(14), ForeColor = Color.White, AutoSize = true, Location = new Point(56, 12) });
        topo.Controls.Add(new Label { Text = subtitulo, Font = Tema.Fonte(9), ForeColor = Tema.BarraTextoSuave, AutoSize = true, Location = new Point(58, 40) });
        return topo;
    }

    public static Label Rotulo(string texto, int x, int y) =>
        new() { Text = texto, AutoSize = true, ForeColor = Tema.TextoSuave, Font = Tema.Fonte(9.5f), Location = new Point(x, y) };

    public static void Aviso(IWin32Window dono, string msg) =>
        MessageBox.Show(dono, msg, "Atenção", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}

/// <summary>Pede a senha técnica.</summary>
public class SenhaTecnicaForm : Form
{
    private readonly TextBox _senha = new() { UseSystemPasswordChar = true, Font = Tema.Fonte(12), MaxLength = 64 };
    private int _tentativas;

    public SenhaTecnicaForm()
    {
        JanelaTecnica.Preparar(this, "Configuração técnica", 420, 250);
        var topo = JanelaTecnica.Topo("Configuração técnica", "Acesso do suporte: servidores e endereços.");
        var l = JanelaTecnica.Rotulo("Senha técnica", 24, 92);
        _senha.SetBounds(24, 116, 372, 32);
        var ok = new BotaoTema { Text = "Entrar", Icone = Icones.Cadeado, Size = new Size(130, 40), Location = new Point(266, 192) };
        var cancelar = new BotaoTema { Text = "Cancelar", Estilo = EstiloBotao.Secundario, Size = new Size(110, 40), Location = new Point(148, 192), DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => Conferir();
        Controls.AddRange([l, _senha, cancelar, ok]);
        Controls.Add(topo);
        AcceptButton = ok;
        CancelButton = cancelar;
        Shown += (_, _) => _senha.Focus();
    }

    private void Conferir()
    {
        if (ConfigTecnica.ConferirSenha(_senha.Text))
        {
            DialogResult = DialogResult.OK;
            return;
        }
        _tentativas++;
        if (_tentativas >= 5)
        {
            JanelaTecnica.Aviso(this, "Senha técnica incorreta.");
            DialogResult = DialogResult.Cancel;
            return;
        }
        JanelaTecnica.Aviso(this, "Senha técnica incorreta.");
        _senha.SelectAll();
        _senha.Focus();
    }
}

/// <summary>Cria/troca a senha técnica (duas vezes, mínimo 6 caracteres).</summary>
public class NovaSenhaTecnicaForm : Form
{
    private readonly TextBox _nova = new() { UseSystemPasswordChar = true, Font = Tema.Fonte(12), MaxLength = 64 };
    private readonly TextBox _repetir = new() { UseSystemPasswordChar = true, Font = Tema.Fonte(12), MaxLength = 64 };

    public NovaSenhaTecnicaForm()
    {
        JanelaTecnica.Preparar(this, "Nova senha técnica", 420, 320);
        var topo = JanelaTecnica.Topo("Nova senha técnica", "Guarde bem: sem ela não dá para trocar os servidores.");
        var l1 = JanelaTecnica.Rotulo("Nova senha (mínimo 6 caracteres)", 24, 92);
        _nova.SetBounds(24, 116, 372, 32);
        var l2 = JanelaTecnica.Rotulo("Repita a senha", 24, 160);
        _repetir.SetBounds(24, 184, 372, 32);
        var ok = new BotaoTema { Text = "Salvar senha", Icone = Icones.Salvar, Size = new Size(150, 40), Location = new Point(246, 260) };
        var cancelar = new BotaoTema { Text = "Cancelar", Estilo = EstiloBotao.Secundario, Size = new Size(110, 40), Location = new Point(128, 260), DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => Salvar();
        Controls.AddRange([l1, _nova, l2, _repetir, cancelar, ok]);
        Controls.Add(topo);
        AcceptButton = ok;
        CancelButton = cancelar;
        Shown += (_, _) => _nova.Focus();
    }

    private void Salvar()
    {
        if (_nova.Text.Length < 6) { JanelaTecnica.Aviso(this, "A senha precisa ter pelo menos 6 caracteres."); _nova.Focus(); return; }
        if (_nova.Text != _repetir.Text) { JanelaTecnica.Aviso(this, "As duas senhas não são iguais."); _repetir.Focus(); return; }
        if (_nova.Text == ConfigTecnica.SenhaDeFabrica) { JanelaTecnica.Aviso(this, "Use uma senha diferente da de fábrica."); _nova.Focus(); return; }
        try
        {
            ConfigTecnica.TrocarSenha(_nova.Text);
        }
        catch (Exception ex)
        {
            JanelaTecnica.Aviso(this, "Não foi possível salvar: " + ex.Message);
            return;
        }
        DialogResult = DialogResult.OK;
    }
}

/// <summary>
/// Lista de servidores: o NOME que se digita no caixa/Retaguarda e o SITE para onde ele aponta.
/// Só aqui o site aparece.
/// </summary>
public class ServidoresForm : Form
{
    private readonly DataGridView _grade = new();
    private readonly Label _resultado = new() { AutoSize = false, Font = Tema.Fonte(9.5f), ForeColor = Tema.TextoSuave };

    public ServidoresForm()
    {
        JanelaTecnica.Preparar(this, "Servidores", 720, 500);
        var topo = JanelaTecnica.Topo("Servidores", "Nome que se digita no caixa e na Gestão → site para onde ele aponta.");

        var dica = new Label
        {
            Text = "Escreva na última linha para acrescentar. O site pode ser só o domínio (erp.loja.com.br): o https:// e o /nf/ são completados.",
            AutoSize = false, Size = new Size(672, 36), Location = new Point(24, 84), ForeColor = Tema.TextoSuave, Font = Tema.Fonte(9)
        };

        Tema.EstilizarGrade(_grade, 10);
        _grade.SetBounds(24, 124, 672, 220);
        _grade.BorderStyle = BorderStyle.FixedSingle;
        _grade.AllowUserToAddRows = true;
        _grade.AllowUserToDeleteRows = true;
        _grade.AllowUserToResizeRows = false;
        _grade.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _grade.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grade.EditMode = DataGridViewEditMode.EditOnEnter;
        _grade.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Nome (o que se digita)", FillWeight = 30, MaxInputLength = 30 });
        _grade.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Site do servidor", FillWeight = 70, MaxInputLength = 200 });
        foreach (var (nome, url) in ConfigTecnica.Servidores()) _grade.Rows.Add(nome, url);

        var testar = new BotaoTema { Text = "Testar linha", Estilo = EstiloBotao.Secundario, Icone = Icones.Servidor, Size = new Size(140, 34), Location = new Point(24, 354) };
        testar.Click += async (_, _) => await Testar();
        var remover = new BotaoTema { Text = "Remover linha", Estilo = EstiloBotao.Secundario, Icone = Icones.Excluir, Size = new Size(150, 34), Location = new Point(172, 354) };
        remover.Click += (_, _) =>
        {
            if (_grade.CurrentRow is { IsNewRow: false } r) _grade.Rows.Remove(r);
        };
        var senha = new BotaoTema { Text = "Trocar senha técnica", Estilo = EstiloBotao.Secundario, Icone = Icones.Cadeado, Size = new Size(200, 34), Location = new Point(496, 354) };
        senha.Click += (_, _) =>
        {
            using var f = new NovaSenhaTecnicaForm();
            if (f.ShowDialog(this) == DialogResult.OK) _resultado.Text = "Senha técnica trocada.";
        };
        _resultado.SetBounds(24, 396, 672, 22);

        var rodape = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Tema.Fundo };
        rodape.Paint += (_, e) =>
        {
            using var linha = new Pen(Tema.Borda);
            e.Graphics.DrawLine(linha, 0, 0, rodape.Width, 0);
        };
        var salvar = new BotaoTema { Text = "Salvar", Icone = Icones.Salvar, Size = new Size(130, 40), Location = new Point(566, 12) };
        var cancelar = new BotaoTema { Text = "Cancelar", Estilo = EstiloBotao.Secundario, Size = new Size(110, 40), Location = new Point(448, 12), DialogResult = DialogResult.Cancel };
        salvar.Click += (_, _) => Salvar();
        rodape.Controls.AddRange([cancelar, salvar]);

        Controls.AddRange([dica, _grade, testar, remover, senha, _resultado]);
        Controls.Add(rodape);
        Controls.Add(topo);
        CancelButton = cancelar;
    }

    private List<(int linha, string nome, string site)> Linhas() =>
        _grade.Rows.Cast<DataGridViewRow>()
            .Where(r => !r.IsNewRow)
            .Select(r => (r.Index, (r.Cells[0].Value?.ToString() ?? "").Trim(), (r.Cells[1].Value?.ToString() ?? "").Trim()))
            .Where(x => x.Item2.Length > 0 || x.Item3.Length > 0)
            .ToList();

    private async Task Testar()
    {
        _grade.EndEdit();
        var r = _grade.CurrentRow;
        var site = (r is null || r.IsNewRow ? "" : r.Cells[1].Value?.ToString()) ?? "";
        var url = EnderecoServidor.ParaUrlDeEndereco(site);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            Mostrar(false, "Escolha uma linha com o site preenchido.");
            return;
        }
        Mostrar(null, "Testando...");
        try
        {
            using var http = new HttpClient { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(15) };
            var resp = await http.GetAsync("health");
            Mostrar(resp.IsSuccessStatusCode,
                resp.IsSuccessStatusCode ? $"Servidor respondendo ({url})." : $"Respondeu com erro {(int)resp.StatusCode} ({url}).");
        }
        catch (TaskCanceledException) { Mostrar(false, "Não respondeu a tempo."); }
        catch (HttpRequestException ex) { Mostrar(false, "Não conectou: " + EnderecoServidor.ErroConexao(ex) + "."); }
    }

    private void Mostrar(bool? ok, string texto)
    {
        _resultado.Text = texto;
        _resultado.ForeColor = ok switch { true => Tema.Sucesso, false => Tema.Perigo, _ => Tema.TextoSuave };
    }

    private void Salvar()
    {
        _grade.EndEdit();
        var lista = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (linha, nome, site) in Linhas())
        {
            if (!EnderecoServidor.NomeValido(nome))
            {
                Erro(linha, 0, $"Linha {linha + 1}: nome inválido. Use de 2 a 30 letras ou números (hífen e _ valem; ponto não).");
                return;
            }
            var url = EnderecoServidor.ParaUrlDeEndereco(site);
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                Erro(linha, 1, $"Linha {linha + 1}: site inválido.");
                return;
            }
            var chave = EnderecoServidor.NormalizarNome(nome);
            if (!lista.TryAdd(chave, url))
            {
                Erro(linha, 0, $"Linha {linha + 1}: o nome \"{chave}\" está repetido.");
                return;
            }
        }
        if (lista.Count == 0)
        {
            JanelaTecnica.Aviso(this, "Cadastre pelo menos um servidor.");
            return;
        }

        try
        {
            ConfigTecnica.SalvarServidores(lista);
        }
        catch (Exception ex)
        {
            JanelaTecnica.Aviso(this, "Não foi possível salvar: " + ex.Message);
            return;
        }
        DialogResult = DialogResult.OK;
    }

    private void Erro(int linha, int coluna, string msg)
    {
        JanelaTecnica.Aviso(this, msg);
        _grade.CurrentCell = _grade.Rows[linha].Cells[coluna];
        _grade.BeginEdit(true);
    }
}

/// <summary>Janela "Aparência" (Retaguarda): o mesmo painel da página Aparência das Opções do caixa.</summary>
public class AparenciaForm : Form
{
    private readonly PainelAparencia _painel = new();
    private readonly ComboBox? _modo;
    private readonly bool? _modoOriginal;

    /// <summary>Com <paramref name="modoCompleto"/>, mostra também "Modo de exibição" (simples ou completo).</summary>
    public AparenciaForm(bool? modoCompleto = null)
    {
        var extra = modoCompleto is null ? 0 : 64;
        var alturaPainel = Aparencia.Master ? 210 : 432;   // no ERP Master: só tema e cor
        JanelaTecnica.Preparar(this, "Aparência", 600, alturaPainel + 168 + extra);
        var topo = JanelaTecnica.Topo("Aparência", Aparencia.Master
            ? "Tema e cor do Projeto JP Master neste computador."
            : modoCompleto is null
                ? "Tema, cor de destaque, nome da empresa, logo e plano de fundo."
                : "Modo de exibição, tema, cor de destaque, nome da empresa, logo e plano de fundo.");
        topo.Controls[0].Text = Icones.Lua;

        if (modoCompleto is { } completo)
        {
            _modoOriginal = completo;
            Controls.Add(new Label { Text = "Modo de exibição", AutoSize = true, Font = Tema.Semibold(10), ForeColor = Tema.Texto, Location = new Point(24, 88) });
            Controls.Add(new Label
            {
                Text = "(simples: botões grandes, só o essencial)", AutoSize = true, Font = Tema.Fonte(8.5f),
                ForeColor = Tema.TextoSuave, Location = new Point(160, 90)
            });
            _modo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = Tema.Fonte(10.5f) };
            _modo.Items.AddRange(new object[] { "Simples — apenas o essencial", "Completo — menu e todas as telas" });
            _modo.SelectedIndex = completo ? 1 : 0;
            _modo.SetBounds(24, 112, 300, 30);
            Controls.Add(_modo);
        }

        _painel.SetBounds(24, 88 + extra, 560, alturaPainel);

        var rodape = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Tema.Fundo };
        rodape.Paint += (_, e) =>
        {
            using var linha = new Pen(Tema.Borda);
            e.Graphics.DrawLine(linha, 0, 0, rodape.Width, 0);
        };
        var salvar = new BotaoTema { Text = "Salvar", Icone = Icones.Salvar, Size = new Size(130, 40), Location = new Point(446, 12) };
        var cancelar = new BotaoTema { Text = "Cancelar", Estilo = EstiloBotao.Secundario, Size = new Size(110, 40), Location = new Point(328, 12), DialogResult = DialogResult.Cancel };
        salvar.Click += (_, _) =>
        {
            try
            {
                AparenciaMudou = _painel.Salvar();
                DialogResult = AparenciaMudou || ModoMudou ? DialogResult.OK : DialogResult.Cancel;
            }
            catch (Exception ex)
            {
                JanelaTecnica.Aviso(this, "Não foi possível salvar: " + ex.Message);
            }
        };
        rodape.Controls.AddRange([cancelar, salvar]);

        Controls.Add(_painel);
        Controls.Add(rodape);
        Controls.Add(topo);
        CancelButton = cancelar;
    }

    /// <summary>Tema, cor, logo... mudaram (precisa reabrir o programa).</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool AparenciaMudou { get; private set; }

    /// <summary>O modo escolhido (true = completo); null quando a janela não mostra o modo.</summary>
    public bool? ModoCompleto => _modo is null ? null : _modo.SelectedIndex == 1;

    public bool ModoMudou => _modo is not null && ModoCompleto != _modoOriginal;

    /// <summary>
    /// Abre a janela; se a aparência mudou, oferece reabrir o programa para aplicar.
    /// Com <paramref name="trocarModo"/>, mostra o modo simples/completo e troca na hora (sem reabrir).
    /// </summary>
    public static void Abrir(IWin32Window? dono, string programa, bool? modoCompleto = null, Action<bool>? trocarModo = null)
    {
        using var f = new AparenciaForm(trocarModo is null ? null : modoCompleto);
        if (f.ShowDialog(dono) != DialogResult.OK) return;
        if (f.ModoMudou && f.ModoCompleto is { } completo) trocarModo?.Invoke(completo);
        if (!f.AparenciaMudou) return;
        if (MessageBox.Show(dono,
                $"A aparência foi salva.\n\nDeseja reiniciar {programa} agora para aplicar as alterações?\n" +
                "Se escolher Não, elas serão aplicadas na próxima abertura.",
                "Aplicar aparência", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
        {
            try { Aparencia.Reiniciar(); }
            catch (Exception ex)
            {
                MessageBox.Show(dono,
                    "A aparência foi salva, mas não foi possível reiniciar automaticamente: " +
                    ex.Message + "\nFeche e abra o programa manualmente para aplicar as alterações.",
                    "Reinício não realizado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
