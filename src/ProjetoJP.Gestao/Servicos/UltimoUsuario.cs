namespace ProjetoJP.Gestao.Servicos;

/// <summary>
/// "Lembrar meu usuário": guarda só o NOME de login (nunca a senha) para vir preenchido na próxima vez.
/// Fica no perfil do usuário do Windows (%LOCALAPPDATA%\ErpNaoFiscal), separado por servidor.
/// </summary>
public static class UltimoUsuario
{
    private static string Arquivo => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ErpNaoFiscal", "retaguarda-usuario.txt");

    /// <summary>O login lembrado para este servidor, ou "" se não tiver.</summary>
    public static string Carregar(string servidor)
    {
        try
        {
            if (!File.Exists(Arquivo)) return "";
            var linhas = File.ReadAllLines(Arquivo);
            return linhas.Length >= 2 && string.Equals(linhas[0].Trim(), servidor.Trim(), StringComparison.OrdinalIgnoreCase)
                ? linhas[1].Trim()
                : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    public static void Salvar(string servidor, string login)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Arquivo)!);
            File.WriteAllLines(Arquivo, [servidor.Trim(), login.Trim()]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* só não lembra */ }
    }

    public static void Esquecer()
    {
        try { if (File.Exists(Arquivo)) File.Delete(Arquivo); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
