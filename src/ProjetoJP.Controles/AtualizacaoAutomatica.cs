using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace ProjetoJP.Controles;

/// <summary>
/// Atualização automática do PDV e da Gestão pelo servidor da loja.
/// <para>
/// O publicar.ps1 grava a versão em cada programa (ano.mês.dia.horaminuto, ex.: 2026.11.8.1835) e gera os pacotes
/// em publicar\atualizacao, que vão para a VPS. Ao abrir (Caixa) ou ao entrar (Retaguarda), o programa pergunta ao
/// servidor se há versão mais nova; se houver, oferece atualizar. Sim = baixa, confere o tamanho e o SHA-256, troca o
/// .exe (o Windows deixa renomear o programa aberto: o atual vira .antigo) e abre a versão nova.
/// </para>
/// Nada de configuração ou banco é tocado: só o arquivo do programa.
/// </summary>
public static class AtualizacaoAutomatica
{
    public sealed class Info
    {
        public string Versao { get; set; } = "";
        public long Tamanho { get; set; }
        public string Sha256 { get; set; } = "";
    }

    private static bool _jaOferecido;

    /// <summary>x64 ou x86 (o pacote certo para este programa).</summary>
    public static string Arquitetura => RuntimeInformation.ProcessArchitecture == Architecture.X86 ? "x86" : "x64";

    /// <summary>Versão deste programa, gravada pelo publicar.ps1. null = compilado sem o publicar (não se atualiza).</summary>
    public static Version? VersaoInstalada { get; } = LerVersao();

    private static Version? LerVersao()
    {
        var texto = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var mais = texto.IndexOf('+');
        if (mais >= 0) texto = texto[..mais];
        return Version.TryParse(texto, out var v) && v.Major >= 2020 && v.Revision >= 0 ? v : null;
    }

    /// <summary>2026.11.8.1835 → "08/11/2026 18:35".</summary>
    public static string Texto(Version v) => v.Major >= 2020 && v.Build >= 1 && v.Revision >= 0
        ? $"{v.Build:00}/{v.Minor:00}/{v.Major} {v.Revision / 100:00}:{v.Revision % 100:00}"
        : v.ToString();

    private static string PastaDownload(string exe) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ErpNaoFiscal", "atualizacao",
        Path.GetFileNameWithoutExtension(exe));

    /// <summary>Apaga o que sobrou da última atualização (o programa antigo e o pacote baixado). Chame no começo.</summary>
    public static void LimparRestos()
    {
        var exe = Environment.ProcessPath;
        if (exe is null) return;
        try
        {
            var pasta = Path.GetDirectoryName(exe)!;
            foreach (var antigo in Directory.EnumerateFiles(pasta, Path.GetFileName(exe) + ".antigo*"))
                try { File.Delete(antigo); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* ainda em uso: fica para a próxima */ }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        try
        {
            var download = PastaDownload(exe);
            if (Directory.Exists(download)) Directory.Delete(download, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Confere se o servidor tem versão mais nova (no máximo uns 6 segundos; sem internet, segue sem perguntar) e, se
    /// tiver, pergunta se quer atualizar. Pergunta uma vez só por execução.
    /// Devolve true se atualizou e já abriu o programa novo: quem chamou deve encerrar o programa.
    /// </summary>
    /// <param name="nomePrograma">Como aparece para a pessoa ("Caixa", "Retaguarda").</param>
    /// <param name="programa">caixa ou retaguarda (nome do pacote no servidor).</param>
    /// <param name="urlServidor">Endereço da API da loja.</param>
    /// <param name="caminho">api/sync/atualizacao (caixa, chave do terminal) ou api/atualizacao (Retaguarda, login).</param>
    /// <param name="credenciais">Põe a chave do terminal ou o token nos cabeçalhos.</param>
    public static bool Oferecer(IWin32Window? dono, string nomePrograma, string programa, string urlServidor, string caminho,
        Action<HttpRequestHeaders> credenciais)
    {
        if (_jaOferecido || VersaoInstalada is null) return false;
        var exe = Environment.ProcessPath;
        if (exe is null || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        if (!Uri.TryCreate(urlServidor.TrimEnd('/') + "/", UriKind.Absolute, out var servidor)) return false;

        Info? info;
        try
        {
            info = Task.Run(() => ProcurarAsync(servidor, caminho, programa, credenciais)).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            return false; // sem internet, servidor fora do ar ou API antiga: abre normalmente
        }
        if (info is null) return false;
        _jaOferecido = true;

        var pasta = Path.GetDirectoryName(exe)!;
        if (!PodeEscrever(pasta))
        {
            MessageBox.Show(dono,
                $"Há uma versão nova do {nomePrograma}, mas este computador não permite trocar os arquivos da pasta:\n{pasta}\n\n" +
                "Rode uma vez o Instalar-ProjetoJP.exe mais novo (ele libera a atualização automática) ou peça ao responsável pelo computador.",
                $"{nomePrograma} — atualização", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        using var janela = new JanelaAtualizacao(nomePrograma, info, servidor, caminho, programa, credenciais, exe);
        if (janela.ShowDialog(dono) != DialogResult.OK || janela.ArquivoNovo is null) return false;
        return Trocar(exe, janela.ArquivoNovo, dono, nomePrograma);
    }

    private static HttpClient Cliente(Uri servidor, Action<HttpRequestHeaders> credenciais, TimeSpan limite)
    {
        var http = new HttpClient { BaseAddress = servidor, Timeout = limite };
        credenciais(http.DefaultRequestHeaders);
        return http;
    }

    private static string Consulta(string programa) => $"programa={Uri.EscapeDataString(programa)}&arquitetura={Arquitetura}";

    private static async Task<Info?> ProcurarAsync(Uri servidor, string caminho, string programa, Action<HttpRequestHeaders> credenciais)
    {
        using var http = Cliente(servidor, credenciais, TimeSpan.FromSeconds(6));
        using var resp = await http.GetAsync($"{caminho}?{Consulta(programa)}");
        if (!resp.IsSuccessStatusCode) return null; // 404 = nada publicado; 401 = sem acesso
        var info = await resp.Content.ReadFromJsonAsync<Info>();
        if (info is null || !Version.TryParse(info.Versao, out var nova) || info.Tamanho <= 0 || info.Sha256.Length != 64) return null;
        return nova > VersaoInstalada ? info : null;
    }

    /// <summary>Baixa o pacote, confere tamanho e SHA-256 e deixa o programa novo ao lado do atual (".novo").</summary>
    private static async Task<string> BaixarAsync(Uri servidor, string caminho, string programa, Action<HttpRequestHeaders> credenciais,
        Info info, string exe, IProgress<int> progresso, CancellationToken ct)
    {
        var pasta = PastaDownload(exe);
        Directory.CreateDirectory(pasta);
        var pacote = Path.Combine(pasta, "pacote.zip");

        using (var http = Cliente(servidor, credenciais, Timeout.InfiniteTimeSpan))
        using (var resp = await http.GetAsync($"{caminho}/arquivo?{Consulta(programa)}", HttpCompletionOption.ResponseHeadersRead, ct))
        {
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"O servidor respondeu com erro {(int)resp.StatusCode} ao baixar a atualização.");
            await using var origem = await resp.Content.ReadAsStreamAsync(ct);
            await using var destino = new FileStream(pacote, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
            var buffer = new byte[1 << 16];
            long total = 0;
            int lidos;
            while ((lidos = await origem.ReadAsync(buffer, ct)) > 0)
            {
                total += lidos;
                if (total > info.Tamanho) throw new InvalidDataException("O arquivo baixado é maior do que o esperado. A atualização foi cancelada.");
                await destino.WriteAsync(buffer.AsMemory(0, lidos), ct);
                progresso.Report((int)(total * 100 / info.Tamanho));
            }
        }

        if (new FileInfo(pacote).Length != info.Tamanho)
            throw new InvalidDataException("O arquivo baixado veio incompleto (a conexão caiu). Tente de novo.");
        string hash;
        await using (var arquivo = File.OpenRead(pacote))
            hash = Convert.ToHexString(await SHA256.HashDataAsync(arquivo, ct));
        if (!hash.Equals(info.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("O arquivo baixado não confere com o publicado no servidor. A atualização foi cancelada.");

        var novo = exe + ".novo";
        using (var zip = ZipFile.OpenRead(pacote))
        {
            // O pacote traz o programa com o nome antigo (ErpPdv.exe / ErpRetaguarda.exe), que as versões de antes do
            // Projeto JP procuram. Esta versão aceita o próprio nome ou o único .exe do pacote.
            var entrada = zip.Entries.FirstOrDefault(e => e.Name.Equals(Path.GetFileName(exe), StringComparison.OrdinalIgnoreCase))
                          ?? (zip.Entries.Count(e => e.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) == 1
                              ? zip.Entries.First(e => e.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) : null)
                          ?? throw new InvalidDataException($"O pacote de atualização não tem o {Path.GetFileName(exe)}.");
            entrada.ExtractToFile(novo, overwrite: true);
        }
        try { File.Delete(pacote); } catch (IOException) { }
        return novo;
    }

    /// <summary>Troca o .exe (o atual vira .antigo) e abre o novo. Se não der para trocar, volta como estava.</summary>
    private static bool Trocar(string exe, string novo, IWin32Window? dono, string nomePrograma)
    {
        var antigo = exe + ".antigo";
        try
        {
            if (File.Exists(antigo))
            {
                try { File.Delete(antigo); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { antigo = exe + ".antigo-" + DateTime.Now.Ticks; }
            }
            File.Move(exe, antigo); // o Windows deixa renomear o programa aberto
            try
            {
                File.Move(novo, exe);
            }
            catch
            {
                File.Move(antigo, exe);
                throw;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(novo); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            MessageBox.Show(dono, $"Não foi possível trocar o {nomePrograma}: {ex.Message}\n\nEle continua na versão atual.",
                $"{nomePrograma} — atualização", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! });
        }
        catch (Exception ex)
        {
            MessageBox.Show(dono, $"O {nomePrograma} foi atualizado, mas não abriu sozinho. Abra de novo pelo atalho.\n\n{ex.Message}",
                $"{nomePrograma} — atualização", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        return true; // já trocado: este (antigo) encerra
    }

    private static bool PodeEscrever(string pasta)
    {
        try
        {
            using var teste = new FileStream(Path.Combine(pasta, $".erp-teste-{Guid.NewGuid():N}.tmp"), FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>"Há uma versão nova. Atualizar agora?" e o andamento do download.</summary>
    private sealed class JanelaAtualizacao : Form
    {
        private readonly Label _texto = new() { AutoSize = true, MaximumSize = new Size(440, 0), Font = Tema.Fonte(10), ForeColor = Tema.Texto };
        private readonly Label _situacao = new() { AutoSize = true, MaximumSize = new Size(440, 0), Font = Tema.Fonte(9.5f), ForeColor = Tema.TextoSuave };
        private readonly ProgressBar _barra = new() { Size = new Size(440, 18), Minimum = 0, Maximum = 100, Visible = false };
        private readonly BotaoTema _atualizar = new() { Text = "Atualizar agora", Size = new Size(160, 38) };
        private readonly BotaoTema _depois = new() { Text = "Depois", Estilo = EstiloBotao.Secundario, Size = new Size(110, 38) };
        private readonly CancellationTokenSource _cancelar = new();
        private bool _baixando;
        private string? _arquivoNovo;

        /// <summary>O programa novo já baixado e conferido (".novo"), ou null.</summary>
        public string? ArquivoNovo => _arquivoNovo;

        public JanelaAtualizacao(string nome, Info info, Uri servidor, string caminho, string programa,
            Action<HttpRequestHeaders> credenciais, string exe)
        {
            Text = $"{nome} — atualização disponível";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            TopMost = true;
            BackColor = Tema.Superficie;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(488, 282);

            var titulo = new Label { Text = "Há uma versão nova", AutoSize = true, Font = Tema.Semibold(14), ForeColor = Tema.Texto, Location = new Point(24, 20) };
            var nova = Version.Parse(info.Versao);
            var mb = Math.Max(1, (int)Math.Round(info.Tamanho / 1048576.0));
            _texto.Text = $"Versão instalada: {Texto(VersaoInstalada!)}\nVersão nova: {Texto(nova)} (aprox. {mb} MB)\n\n" +
                          $"A atualização leva poucos instantes e o {nome} abre de novo sozinho. " +
                          "Vendas, configurações e dados deste computador não são alterados.";
            _texto.Location = new Point(24, 58);
            _barra.Location = new Point(24, 184);
            _situacao.Location = new Point(24, 208);
            _atualizar.Location = new Point(24, 222);
            _depois.Location = new Point(194, 222);

            _atualizar.Click += async (_, _) =>
            {
                _baixando = true;
                _atualizar.Visible = _depois.Visible = false;
                _barra.Visible = true;
                _barra.Value = 0;
                _situacao.ForeColor = Tema.TextoSuave;
                _situacao.Text = "Baixando a versão nova...";
                var progresso = new Progress<int>(p =>
                {
                    _barra.Value = Math.Clamp(p, 0, 100);
                    _situacao.Text = $"Baixando a versão nova... {Math.Clamp(p, 0, 100)}%";
                });
                try
                {
                    using var limite = CancellationTokenSource.CreateLinkedTokenSource(_cancelar.Token);
                    limite.CancelAfter(TimeSpan.FromMinutes(30));
                    _arquivoNovo = await BaixarAsync(servidor, caminho, programa, credenciais, info, exe, progresso, limite.Token);
                    _situacao.Text = $"Abrindo o {nome} atualizado...";
                    _baixando = false;
                    DialogResult = DialogResult.OK;
                }
                catch (Exception ex)
                {
                    _baixando = false;
                    if (IsDisposed) return;
                    _barra.Visible = false;
                    _situacao.ForeColor = Tema.Perigo;
                    _situacao.Text = ex switch
                    {
                        OperationCanceledException => "O download demorou demais e foi cancelado.",
                        HttpRequestException => "A conexão com o servidor caiu durante o download.",
                        _ => ex.Message
                    };
                    _situacao.Location = new Point(24, 176);
                    _atualizar.Text = "Tentar de novo";
                    _depois.Text = "Continuar sem atualizar";
                    _depois.Width = 190;
                    _atualizar.Visible = _depois.Visible = true;
                }
            };
            _depois.Click += (_, _) => DialogResult = DialogResult.Cancel;
            FormClosing += (_, e) =>
            {
                if (!_baixando) return;
                _cancelar.Cancel(); // fechou no meio do download: cancela e abre na versão atual
            };

            Controls.AddRange([titulo, _texto, _barra, _situacao, _atualizar, _depois]);
            AcceptButton = _atualizar;
            CancelButton = _depois;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _cancelar.Dispose();
            base.Dispose(disposing);
        }
    }
}
