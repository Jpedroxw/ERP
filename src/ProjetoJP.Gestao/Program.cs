using System.Net.Http.Headers;
using ProjetoJP.Controles;
using ProjetoJP.Gestao.Servicos;
using ProjetoJP.Gestao.Telas;

namespace ProjetoJP.Gestao;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Qualquer erro ao abrir aparece na tela e fica em %LOCALAPPDATA%\ErpNaoFiscal\logs\retaguarda-inicializacao.log
        RegistroInicializacao.Executar("retaguarda", Executar);
    }

    private static void Executar()
    {
        ApplicationConfiguration.Initialize();
        AtualizacaoAutomatica.LimparRestos(); // apaga o programa antigo da última atualização, se sobrou
        // Antes de criar qualquer janela (o Windows não deixa mudar depois).
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            RegistroInicializacao.Registrar("retaguarda", "erro na tela", e.Exception);
            MessageBox.Show(e.Exception.Message, "Erro inesperado", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        // Configurações valem só para este computador: dados copiados de outro (ou computador clonado) são guardados
        // à parte e a Retaguarda abre como na primeira vez.
        var guardado = ComputadorDestaInstalacao.Conferir();
        if (guardado is not null)
        {
            RegistroInicializacao.Registrar("retaguarda", "dados de outro computador guardados em " + guardado, null);
            MessageBox.Show(ComputadorDestaInstalacao.Aviso("programa", guardado), Aparencia.NomePadrao + " Gestão — outro computador",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        ProjetoJP.Controles.Aparencia.Iniciar(); // tema, cor, logo e fundo — antes de abrir qualquer janela
        // Codificações do Windows (1252...) para ler planilhas e bancos antigos na importação.
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        ProjetoJP.Controles.ConfigTecnica.Carregar(); // lista de servidores (nome → site), antes da configuração
        var cfg = RetaguardaConfig.Carregar();
        var apiUrl = cfg.ApiUrl;

        // Primeira vez: pede o endereço do servidor pela tela.
        if (!cfg.Configurado && !PedirServidor(ref apiUrl)) return;

        while (true)
        {
            var api = new ApiClient(apiUrl);
            DialogResult resultado;
            using (var login = new LoginForm(api, apiUrl))
                resultado = login.ShowDialog();

            if (resultado == DialogResult.Yes)
            {
                // Cadastro aprovado: o servidor da empresa já foi gravado — reabre o login nele.
                apiUrl = RetaguardaConfig.Carregar().ApiUrl;
                continue;
            }
            if (resultado == DialogResult.Retry)
            {
                // "Alterar servidor" na tela de login
                PedirServidor(ref apiUrl);
                continue;
            }
            if (resultado != DialogResult.OK) return;

            // Versão nova publicada no servidor? Pergunta logo depois de entrar (uma vez por execução).
            var token = api.Sessao?.Token ?? "";
            if (AtualizacaoAutomatica.Oferecer(null, Aparencia.NomePadrao + " Gestão", "retaguarda", apiUrl, "api/atualizacao",
                    h => h.Authorization = new AuthenticationHeaderValue("Bearer", token)))
            {
                // A versão nova já abriu: encerra esta sessão no servidor e sai.
                try { Task.Run(api.EncerrarSessaoAsync).Wait(TimeSpan.FromSeconds(3)); } catch (Exception) { /* sai mesmo assim */ }
                return;
            }

            // Modo da tela principal: o escolhido por último neste computador, ou automático pelo perfil.
            var completo = PreferenciasInterface.Carregar("retaguarda", RetaguardaConfig.Carregar().ModoCompleto ?? api.EhGerente).Modo == ModoInterface.Completo;
            using var principal = new PrincipalForm(api, apiUrl, completo);
            Application.Run(principal);
            if (principal.LogoutSolicitado)
            {
                // Reabre o login da mesma empresa sem encerrar o programa.
                // Não reaproveita a sessão anterior em memória.
                continue;
            }
            return;
        }
    }

    private static bool PedirServidor(ref string apiUrl)
    {
        using var f = new ServidorForm();
        if (f.ShowDialog() != DialogResult.OK) return false;
        apiUrl = f.ApiUrl;
        return true;
    }
}
