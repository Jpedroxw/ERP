namespace ProjetoJP.Instalador;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
            MessageBox.Show("Erro inesperado na instalação: " + e.Exception.Message, "Projeto JP Instalador",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        Application.Run(new InstaladorForm());
    }
}
