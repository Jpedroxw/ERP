using ProjetoJP.Controles;
using ProjetoJP.Master.Servicos;
using ProjetoJP.Master.Telas;

namespace ProjetoJP.Master;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Qualquer erro ao abrir aparece na tela e fica em %LOCALAPPDATA%\ErpNaoFiscal\logs\master-inicializacao.log
        RegistroInicializacao.Executar("master", Executar);
    }

    private static void Executar()
    {
        ApplicationConfiguration.Initialize();
        // Antes de criar qualquer janela (o Windows não deixa mudar depois).
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            RegistroInicializacao.Registrar("master", "erro na tela", e.Exception);
            MessageBox.Show(e.Exception.Message, "Erro inesperado", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        Aparencia.Iniciar(master: true);  // tema e cor só do Master (master.json), separados dos da loja
        ConfigTecnica.Carregar();  // servidor padrão (sugestão na primeira conexão)

        var cfg = MasterConfig.Carregar();
        if (!cfg.Configurado)
        {
            RegistroInicializacao.Registrar("master", "sem conexão salva: abrindo a tela de conexão", null);
            if (!ConexaoForm.Abrir(null, cfg)) return;
            cfg = MasterConfig.Carregar();
        }
        RegistroInicializacao.Registrar("master", "abrindo a janela principal", null);
        Application.Run(new MasterForm(cfg));
    }
}
