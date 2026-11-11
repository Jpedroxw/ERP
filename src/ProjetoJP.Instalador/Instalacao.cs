using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace ProjetoJP.Instalador;

/// <summary>Um programa que o instalador sabe instalar.</summary>
internal sealed record ProgramaInstalavel(string Id, string Nome, string Descricao, string Pasta, string Exe, string Icone, string Atalho)
{
    // A Pasta não muda: quem atualiza por cima de uma versão antiga continua na mesma pasta, com as mesmas permissões.
    public static readonly ProgramaInstalavel Caixa = new("caixa", "PDV", "Vendas no balcão. Funciona sem internet.",
        "Caixa", "ProjetoJP-PDV.exe", "caixa.ico", "Projeto JP PDV");

    public static readonly ProgramaInstalavel Retaguarda = new("retaguarda", "Gestão", "Produtos, estoque, usuários e relatórios da loja.",
        "Retaguarda", "ProjetoJP-Gestao.exe", "retaguarda.ico", "Projeto JP Gestão");

    public static readonly ProgramaInstalavel[] Todos = [Caixa, Retaguarda];

    /// <summary>Nome do arquivo dentro do pacote, para o Windows deste computador (64 ou 32 bits).</summary>
    public string EntradaNoPacote => $"{Id}/{(Environment.Is64BitOperatingSystem ? "x64" : "x86")}/{Exe}";

    public string NomeDoProcesso => Path.GetFileNameWithoutExtension(Exe);

    /// <summary>Nome do programa antes do Projeto JP (ErpPdv.exe / ErpRetaguarda.exe): apagado ao instalar por cima.</summary>
    public string ExeAntigo => Id == "retaguarda" ? "ErpRetaguarda.exe" : "ErpPdv.exe";

    public string NomeDoProcessoAntigo => Path.GetFileNameWithoutExtension(ExeAntigo);

    /// <summary>"o PDV" / "a Gestão".</summary>
    public string ComArtigo => (Id == "retaguarda" ? "a " : "o ") + Nome;

    /// <summary>Atalhos com o nome antigo (antes do Projeto JP): apagados ao instalar e na desinstalação.</summary>
    public string AtalhoAntigo => Id == "retaguarda" ? "Retaguarda - ERP" : "Caixa - ERP";
}

internal sealed class OpcoesInstalacao
{
    public required List<ProgramaInstalavel> Programas { get; init; }
    public required string Pasta { get; init; }
    public bool AtalhoAreaDeTrabalho { get; init; }
    public bool AtalhoMenuIniciar { get; init; }
    public bool CaixaAoIniciarWindows { get; init; }
}

/// <summary>
/// Copia os programas do pacote embutido no instalador, cria os atalhos e registra em "Aplicativos instalados" do
/// Windows (com um desinstalador). Configurações e dados (C:\ProgramData\ErpNaoFiscal) não são tocados: atualizar
/// por cima mantém tudo.
/// </summary>
internal static class Instalacao
{
    private const string ChaveDesinstalar = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ErpNaoFiscal";
    private const string NomeNoWindows = "Projeto JP";
    private const string PastaMenuIniciar = "Projeto JP";
    /// <summary>Pasta do Menu Iniciar das versões antigas (antes do Projeto JP): apagada ao instalar.</summary>
    private const string PastaMenuIniciarAntiga = "ERP Não Fiscal";

    /// <summary>O pacote com os programas (embutido pelo publicar.ps1), ou null num instalador vazio.</summary>
    public static ZipArchive? AbrirPacote()
    {
        var fluxo = typeof(Instalacao).Assembly.GetManifestResourceStream("pacote.zip");
        return fluxo is null ? null : new ZipArchive(fluxo, ZipArchiveMode.Read);
    }

    /// <summary>Data dos programas do pacote (mostrada como versão).</summary>
    public static DateTime? DataDoPacote()
    {
        try
        {
            using var zip = AbrirPacote();
            var entrada = zip?.Entries.FirstOrDefault();
            return entrada?.LastWriteTime.LocalDateTime;
        }
        catch (Exception) { return null; }
    }

    public static long TamanhoNoPacote(ProgramaInstalavel p)
    {
        try
        {
            using var zip = AbrirPacote();
            return zip?.GetEntry(p.EntradaNoPacote)?.Length ?? 0;
        }
        catch (Exception) { return 0; }
    }

    private static RegistryKey Raiz() =>
        RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default);

    /// <summary>Pasta de uma instalação anterior (para atualizar no mesmo lugar), ou a pasta padrão.</summary>
    public static string PastaSugerida()
    {
        try
        {
            using var raiz = Raiz();
            using var chave = raiz.OpenSubKey(ChaveDesinstalar);
            if (chave?.GetValue("InstallLocation") is string anterior && anterior.Length > 0) return anterior;
        }
        catch (Exception) { /* sem acesso ao registro: usa a padrão */ }
        // Instalador é 32 bits: "ProgramFiles" daria "Program Files (x86)". Os programas de 64 bits vão em "Program Files".
        var arquivosDeProgramas = Environment.GetEnvironmentVariable("ProgramW6432");
        if (string.IsNullOrEmpty(arquivosDeProgramas)) arquivosDeProgramas = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        return Path.Combine(arquivosDeProgramas, PastaPadrao);
    }

    /// <summary>Pasta das instalações novas (dentro de Arquivos de Programas). Quem já tinha instalado continua na pasta antiga.</summary>
    public const string PastaPadrao = "Projeto JP";

    /// <summary>Nome da pasta das versões antigas (antes do Projeto JP).</summary>
    public const string PastaPadraoAntiga = "ErpNaoFiscal";

    public static bool JaInstalado(ProgramaInstalavel p, string pasta) =>
        File.Exists(Path.Combine(pasta, p.Pasta, p.Exe)) || File.Exists(Path.Combine(pasta, p.Pasta, p.ExeAntigo));

    /// <summary>Programas abertos agora (precisam ser fechados antes de trocar o .exe). Confere também o nome antigo.</summary>
    public static List<ProgramaInstalavel> Abertos(IEnumerable<ProgramaInstalavel> programas) =>
        programas.Where(p => System.Diagnostics.Process.GetProcessesByName(p.NomeDoProcesso).Length > 0
                          || System.Diagnostics.Process.GetProcessesByName(p.NomeDoProcessoAntigo).Length > 0).ToList();

    /// <summary>Instala. Avisa o andamento (0 a 100 e o que está fazendo). Erros sobem como exceção com texto simples.</summary>
    public static void Instalar(OpcoesInstalacao o, IProgress<(int percentual, string texto)> andamento)
    {
        using var zip = AbrirPacote() ?? throw new InvalidOperationException("Este instalador está vazio (sem os programas). Gere de novo pelo publicar.");
        var entradas = o.Programas.Select(p => (programa: p, entrada: zip.GetEntry(p.EntradaNoPacote)
            ?? throw new InvalidOperationException($"O {p.Nome} para Windows {(Environment.Is64BitOperatingSystem ? "64" : "32")} bits não está neste instalador."))).ToList();
        var total = Math.Max(1, entradas.Sum(x => x.entrada.Length));
        long copiado = 0;

        Directory.CreateDirectory(o.Pasta);
        foreach (var (programa, entrada) in entradas)
        {
            var bits = Environment.Is64BitOperatingSystem ? "64" : "32";
            andamento.Report(((int)(copiado * 90 / total), $"Copiando o {programa.Nome} ({bits} bits)..."));
            var pasta = Path.Combine(o.Pasta, programa.Pasta);
            Directory.CreateDirectory(pasta);
            var destino = Path.Combine(pasta, programa.Exe);
            var temporario = destino + ".novo";
            using (var origem = entrada.Open())
            using (var saida = new FileStream(temporario, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            {
                var buffer = new byte[1 << 20];
                int lidos;
                while ((lidos = origem.Read(buffer, 0, buffer.Length)) > 0)
                {
                    saida.Write(buffer, 0, lidos);
                    copiado += lidos;
                    andamento.Report(((int)(copiado * 90 / total), $"Copiando o {programa.Nome} ({bits} bits)..."));
                }
            }
            File.Move(temporario, destino, overwrite: true); // só troca o programa quando a cópia terminou inteira

            // Por cima de uma versão antiga: o programa tinha outro nome (ErpPdv.exe...). Apaga para não ficar duplicado.
            ApagarSemErro(Path.Combine(pasta, programa.ExeAntigo));
            foreach (var resto in Directory.EnumerateFiles(pasta, programa.ExeAntigo + ".antigo*")) ApagarSemErro(resto);
        }

        andamento.Report((91, "Liberando a atualização automática..."));
        LiberarAtualizacao(o.Pasta);

        andamento.Report((92, "Criando os atalhos..."));
        CriarAtalhos(o);

        andamento.Report((96, "Registrando no Windows..."));
        Registrar(o);

        andamento.Report((100, "Pronto."));
    }

    /// <summary>
    /// Deixa os usuários do Windows trocarem os arquivos desta pasta (só dela), para a atualização automática do Caixa e
    /// da Retaguarda não pedir senha de administrador. Se falhar, o programa só avisa que precisa do instalador.
    /// </summary>
    private static void LiberarAtualizacao(string pasta)
    {
        try
        {
            var icacls = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "icacls.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            icacls.ArgumentList.Add(pasta);
            icacls.ArgumentList.Add("/grant");
            icacls.ArgumentList.Add("*S-1-5-32-545:(OI)(CI)M"); // grupo Usuários (em qualquer idioma do Windows): modificar
            icacls.ArgumentList.Add("/T");
            icacls.ArgumentList.Add("/C");
            icacls.ArgumentList.Add("/Q");
            using var processo = System.Diagnostics.Process.Start(icacls);
            processo?.WaitForExit(60_000);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            // sem a liberação: a atualização automática avisa que precisa rodar o instalador
        }
    }

    // ---------- Atalhos ----------

    private static string AreaDeTrabalho => Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
    private static string MenuIniciar => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), PastaMenuIniciar);
    private static string MenuIniciarAntigo => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), PastaMenuIniciarAntiga);
    private static string Inicializar => Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);

    private static void CriarAtalhos(OpcoesInstalacao o)
    {
        ApagarAtalhosAntigos();
        foreach (var p in o.Programas)
        {
            var alvo = Path.Combine(o.Pasta, p.Pasta, p.Exe);
            var naAreaDeTrabalho = Path.Combine(AreaDeTrabalho, p.Atalho + ".lnk");
            var noMenu = Path.Combine(MenuIniciar, p.Atalho + ".lnk");
            if (o.AtalhoAreaDeTrabalho) CriarAtalho(naAreaDeTrabalho, alvo, p.Descricao);
            else ApagarSemErro(naAreaDeTrabalho);
            if (o.AtalhoMenuIniciar)
            {
                Directory.CreateDirectory(MenuIniciar);
                CriarAtalho(noMenu, alvo, p.Descricao);
            }
            else ApagarSemErro(noMenu);
        }
        if (o.Programas.Contains(ProgramaInstalavel.Caixa))
        {
            var naInicializacao = Path.Combine(Inicializar, ProgramaInstalavel.Caixa.Atalho + ".lnk");
            if (o.CaixaAoIniciarWindows)
                CriarAtalho(naInicializacao, Path.Combine(o.Pasta, ProgramaInstalavel.Caixa.Pasta, ProgramaInstalavel.Caixa.Exe), ProgramaInstalavel.Caixa.Descricao);
            else ApagarSemErro(naInicializacao);
        }
    }

    private static void CriarAtalho(string arquivo, string alvo, string descricao)
    {
        var tipo = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Não foi possível criar atalhos neste Windows.");
        object? shell = null, atalho = null;
        try
        {
            shell = Activator.CreateInstance(tipo);
            dynamic s = shell!;
            atalho = s.CreateShortcut(arquivo);
            dynamic a = atalho!;
            a.TargetPath = alvo;
            a.WorkingDirectory = Path.GetDirectoryName(alvo);
            a.Description = descricao;
            a.IconLocation = alvo + ",0";
            a.Save();
        }
        finally
        {
            if (atalho is not null && Marshal.IsComObject(atalho)) Marshal.FinalReleaseComObject(atalho);
            if (shell is not null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
    }

    /// <summary>Quem atualiza por cima de uma versão antiga não fica com os atalhos "Caixa - ERP" / "Retaguarda - ERP" repetidos.</summary>
    private static void ApagarAtalhosAntigos()
    {
        foreach (var p in ProgramaInstalavel.Todos)
        {
            ApagarSemErro(Path.Combine(AreaDeTrabalho, p.AtalhoAntigo + ".lnk"));
            ApagarSemErro(Path.Combine(Inicializar, p.AtalhoAntigo + ".lnk"));
        }
        try { if (Directory.Exists(MenuIniciarAntigo)) Directory.Delete(MenuIniciarAntigo, recursive: true); }
        catch (Exception) { /* fica */ }
    }

    private static void ApagarSemErro(string arquivo)
    {
        try { if (File.Exists(arquivo)) File.Delete(arquivo); }
        catch (Exception) { /* fica */ }
    }

    // ---------- Registro no Windows e desinstalador ----------

    private static void Registrar(OpcoesInstalacao o)
    {
        var instalados = ProgramaInstalavel.Todos.Where(p => JaInstalado(p, o.Pasta)).ToList();
        var script = Path.Combine(o.Pasta, "desinstalar.ps1");
        File.WriteAllText(script, ScriptDesinstalar(o.Pasta), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var icone = instalados.FirstOrDefault() is { } primeiro ? Path.Combine(o.Pasta, primeiro.Pasta, primeiro.Exe) : "";
        long tamanhoKb = 0;
        foreach (var p in instalados)
        {
            try { tamanhoKb += new FileInfo(Path.Combine(o.Pasta, p.Pasta, p.Exe)).Length / 1024; }
            catch (Exception) { /* sem tamanho */ }
        }

        using var raiz = Raiz();
        using var chave = raiz.CreateSubKey(ChaveDesinstalar);
        chave.SetValue("DisplayName", NomeNoWindows + " (" + string.Join(" e ", instalados.Select(p => p.Nome)) + ")");
        chave.SetValue("DisplayVersion", (DataDoPacote() ?? DateTime.Now).ToString("yyyy.MM.dd"));
        chave.SetValue("Publisher", NomeNoWindows);
        chave.SetValue("InstallLocation", o.Pasta);
        chave.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
        if (icone.Length > 0) chave.SetValue("DisplayIcon", icone);
        chave.SetValue("UninstallString", $"powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"");
        chave.SetValue("NoModify", 1, RegistryValueKind.DWord);
        chave.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        chave.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, tamanhoKb), RegistryValueKind.DWord);
    }

    private static string Aspas(string texto) => "'" + texto.Replace("'", "''") + "'";

    /// <summary>Desinstalador: tira programas, atalhos e o registro. Configurações e dados ficam.</summary>
    private static string ScriptDesinstalar(string pasta)
    {
        var atalhos = new List<string>();
        foreach (var p in ProgramaInstalavel.Todos)
        {
            atalhos.Add(Path.Combine(AreaDeTrabalho, p.Atalho + ".lnk"));
            atalhos.Add(Path.Combine(MenuIniciar, p.Atalho + ".lnk"));
        }
        atalhos.Add(Path.Combine(Inicializar, ProgramaInstalavel.Caixa.Atalho + ".lnk"));
        foreach (var p in ProgramaInstalavel.Todos)
        {
            atalhos.Add(Path.Combine(AreaDeTrabalho, p.AtalhoAntigo + ".lnk"));
            atalhos.Add(Path.Combine(Inicializar, p.AtalhoAntigo + ".lnk"));
        }
        var processos = string.Join(",", ProgramaInstalavel.Todos.SelectMany(p => new[] { Aspas(p.NomeDoProcesso), Aspas(p.NomeDoProcessoAntigo) }));
        var sb = new StringBuilder();
        sb.AppendLine("# Desinstala o Projeto JP: programas, atalhos e o registro no Windows.");
        sb.AppendLine("# As configurações e o banco do caixa (C:\\ProgramData\\ErpNaoFiscal) NÃO são apagados.");
        sb.AppendLine("$ErrorActionPreference = 'SilentlyContinue'");
        sb.AppendLine("$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)");
        sb.AppendLine("if (-not $admin) {");
        sb.AppendLine("  Start-Process powershell.exe -Verb RunAs -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-WindowStyle','Hidden','-File',('\"' + $MyInvocation.MyCommand.Path + '\"'))");
        sb.AppendLine("  exit");
        sb.AppendLine("}");
        sb.AppendLine("Add-Type -AssemblyName System.Windows.Forms");
        sb.AppendLine("$titulo = 'Desinstalar — Projeto JP'");
        sb.AppendLine("$r = [System.Windows.Forms.MessageBox]::Show(\"Desinstalar o Projeto JP deste computador?`n`nAs configurações e o banco do caixa (C:\\ProgramData\\ErpNaoFiscal) não são apagados.\", $titulo, 'YesNo', 'Question')");
        sb.AppendLine("if ($r -ne 'Yes') { exit }");
        sb.AppendLine($"foreach ($nome in @({processos})) {{");
        sb.AppendLine("  if (Get-Process -Name $nome -ErrorAction SilentlyContinue) {");
        sb.AppendLine("    [void][System.Windows.Forms.MessageBox]::Show('Feche o programa do Projeto JP que está aberto e rode a desinstalação de novo.', $titulo, 'OK', 'Warning')");
        sb.AppendLine("    exit");
        sb.AppendLine("  }");
        sb.AppendLine("}");
        foreach (var a in atalhos) sb.AppendLine($"Remove-Item -LiteralPath {Aspas(a)} -Force");
        sb.AppendLine($"Remove-Item -LiteralPath {Aspas(MenuIniciar)} -Recurse -Force");
        sb.AppendLine($"Remove-Item -LiteralPath {Aspas(MenuIniciarAntigo)} -Recurse -Force");
        sb.AppendLine($"Remove-Item -LiteralPath {Aspas("HKLM:\\" + ChaveDesinstalar)} -Recurse -Force");
        sb.AppendLine($"$pasta = {Aspas(pasta)}");
        sb.AppendLine("# A pasta é apagada logo depois (este script está dentro dela).");
        sb.AppendLine("Start-Process cmd.exe -ArgumentList ('/c timeout /t 2 /nobreak >nul & rmdir /s /q \"' + $pasta + '\"') -WindowStyle Hidden");
        sb.AppendLine("[void][System.Windows.Forms.MessageBox]::Show('Projeto JP desinstalado.', $titulo, 'OK', 'Information')");
        return sb.ToString();
    }
}
