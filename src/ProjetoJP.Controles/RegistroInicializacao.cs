using System.Text;

namespace ProjetoJP.Controles;

/// <summary>
/// Erros ao abrir o programa: em vez de fechar sem mostrar nada, grava o motivo em
/// %LOCALAPPDATA%\ErpNaoFiscal\logs\&lt;programa&gt;-inicializacao.log e mostra a mensagem na tela.
/// </summary>
public static class RegistroInicializacao
{
    public static string Pasta => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ErpNaoFiscal", "logs");

    /// <summary>Roda o programa protegido: qualquer erro vira log + mensagem.</summary>
    public static void Executar(string programa, Action executar)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Registrar(programa, "erro não tratado", ex);
        };
        try
        {
            Registrar(programa, "abrindo", null);
            executar();
        }
        catch (Exception ex)
        {
            var arquivo = Registrar(programa, "erro ao abrir", ex);
            try
            {
                MessageBox.Show(
                    "Não foi possível abrir o programa.\n\n" + ex.Message +
                    (arquivo is null ? "" : "\n\nDetalhes gravados em:\n" + arquivo),
                    Aparencia.NomePadrao + " — erro ao abrir", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception) { /* sem tela: fica só o log */ }
        }
    }

    /// <summary>Grava uma linha (e o erro completo, se houver). Devolve o caminho do arquivo, ou null.</summary>
    public static string? Registrar(string programa, string etapa, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Pasta);
            var arquivo = Path.Combine(Pasta, programa + "-inicializacao.log");
            // Arquivo pequeno: recomeça quando passa de 512 KB.
            if (File.Exists(arquivo) && new FileInfo(arquivo).Length > 512 * 1024) File.Delete(arquivo);
            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("  ").Append(etapa);
            sb.Append("  (versão ").Append(Application.ProductVersion).Append(", Windows ")
              .Append(Environment.OSVersion.Version).Append(Environment.Is64BitProcess ? ", 64 bits)" : ", 32 bits)");
            sb.AppendLine();
            if (ex is not null) sb.AppendLine(ex.ToString()).AppendLine();
            File.AppendAllText(arquivo, sb.ToString(), Encoding.UTF8);
            return arquivo;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
