using ProjetoJP.Pdv.Dados;
using ProjetoJP.Pdv.Servicos;
using ProjetoJP.Pdv.Telas;

namespace ProjetoJP.Pdv;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        try
        {
            Registrar("Início do processo do Caixa");
            Executar();
            Registrar("Caixa encerrado normalmente");
        }
        catch (Exception ex)
        {
            Registrar("Falha fatal na inicialização ou execução", ex);
            MessageBox.Show("O PDV encontrou um erro e não conseguiu continuar.\n\n" +
                            ex.Message + "\n\nDetalhes gravados em:\n" + CaminhoLog,
                            ProjetoJP.Controles.Aparencia.NomePadrao + " PDV", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string CaminhoLog => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ErpNaoFiscal", "logs", "caixa-inicializacao.log");

    private static void Registrar(string etapa, Exception? ex = null)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CaminhoLog)!);
            File.AppendAllText(CaminhoLog,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {etapa}\n" +
                (ex is null ? "" : ex + "\n"));
        }
        catch (Exception) { /* log não pode impedir a abertura do Caixa */ }
    }

    private static void Executar()
    {
        ApplicationConfiguration.Initialize();
        // Tem de ser antes de criar qualquer janela (o Windows não deixa mudar depois). O tema escuro
        // (Aparencia.Iniciar) e as mensagens abaixo já criam janelas: por isso fica logo aqui no começo.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            Registrar("Erro inesperado na interface", e.Exception);
            MessageBox.Show(e.Exception.Message + "\n\nRegistro: " + CaminhoLog,
                ProjetoJP.Controles.Aparencia.NomePadrao + " PDV — erro inesperado", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Registrar("Exceção não tratada", ex);
        };
        ProjetoJP.Controles.AtualizacaoAutomatica.LimparRestos(); // apaga o programa antigo da última atualização, se sobrou
        // Configurações valem só para este computador: dados copiados de outro (ou computador clonado) são guardados
        // à parte e o caixa abre como na primeira vez.
        var guardado = ProjetoJP.Controles.ComputadorDestaInstalacao.Conferir();
        if (guardado is not null)
        {
            Registrar("Dados de outro computador guardados em " + guardado);
            MessageBox.Show(ProjetoJP.Controles.ComputadorDestaInstalacao.Aviso("caixa", guardado) +
                            "\n\nVendas que ainda não tinham subido continuam no computador de origem.",
                "Caixa — outro computador", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        Registrar("Carregando tema e preferências");
        ProjetoJP.Controles.Aparencia.Iniciar(); // tema, cor, logo e fundo — antes de abrir qualquer janela

        PdvConfig cfg;
        try
        {
            Registrar("Abrindo o banco SQLite do Caixa");
            Db.Inicializar();
            Registrar("Banco SQLite aberto");
            Relogio.Carregar();                     // hora confiável (última diferença conhecida do relógio do Windows)
            ProjetoJP.Controles.ConfigTecnica.Carregar(); // lista de servidores (nome → site), antes da configuração
            cfg = PdvConfig.Carregar();
            Registrar("Configuração do terminal carregada");
        }
        catch (Exception ex)
        {
            Registrar("Falha ao carregar o banco ou configuração local", ex);
            MessageBox.Show("Falha ao iniciar o PDV: " + ex.Message +
                            "\n\nRegistro: " + CaminhoLog,
                            "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // Primeira vez (ou configuração apagada): abre a tela de configurações.
        if (!cfg.Configurado)
        {
            if (!Configurar(ref cfg, primeiraVez: true)) return;
        }

        var sync = new Sincronizador(cfg);
        try
        {
            // Primeira execução: precisa baixar usuários, produtos e formas de pagamento.
            while (Repositorio.ContarUsuarios() == 0)
            {
                Registrar("Primeira sincronização: banco ainda sem usuários");
                var ok = SincronizarPrimeiraVez(sync);
                if (ok && Repositorio.ContarUsuarios() > 0)
                {
                    Registrar("Primeira sincronização concluída");
                    break;
                }
                Registrar("Primeira sincronização sem sucesso: " + sync.UltimaMensagem);

                var resposta = MessageBox.Show(
                    "A primeira sincronização com o servidor não funcionou, e sem ela o caixa não tem usuários nem produtos.\n\n" +
                    sync.UltimaMensagem + "\n\nAbrir as configurações para revisar?",
                    "Primeira sincronização", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (resposta != DialogResult.Yes || !Configurar(ref cfg, primeiraVez: true)) return;

                sync.Dispose();
                sync = new Sincronizador(cfg);
            }

            // Versão nova publicada no servidor? Pergunta aqui, ao abrir, antes de alguém entrar (nunca no meio de uma venda).
            // Sim = baixa, troca o programa e abre a versão nova; as vendas pendentes continuam no banco local.
            var chaveTerminal = cfg.TerminalKey;
            if (ProjetoJP.Controles.AtualizacaoAutomatica.Oferecer(null, ProjetoJP.Controles.Aparencia.NomePadrao + " PDV", "caixa", cfg.ApiUrl, "api/sync/atualizacao",
                    h => h.Add("X-Terminal-Key", chaveTerminal)))
            {
                Registrar("Caixa atualizado: abrindo a versão nova");
                return;
            }

            sync.Iniciar();
            CopiaSeguranca.Pedir(cfg.PastaCopiaSeguranca); // cópia na rede da loja (se configurada)
            Registrar("Abrindo janela de login do Caixa");

            // Ao abrir o programa: direto no login. Depois que o operador sai da conta ou fecha o caixa: tela
            // "Caixa fechado" (com as promoções da loja para o cliente), e Enter abre o login.
            var abrirComLogin = true;
            while (true)
            {
                DialogResult resultado;
                UsuarioLocal? operador;
                if (abrirComLogin)
                {
                    using var login = new LoginForm(cfg, sync);
                    resultado = login.ShowDialog();
                    operador = login.Usuario;
                }
                else
                {
                    using var fechado = new CaixaFechadoForm(cfg, sync);
                    resultado = fechado.ShowDialog();
                    operador = fechado.Usuario;
                }

                if (resultado == DialogResult.Retry)
                {
                    // Configurações alteradas na tela de login: recarrega, recomeça a sincronização e volta ao login.
                    sync.Dispose();
                    cfg = PdvConfig.Carregar();
                    sync = new Sincronizador(cfg);
                    sync.Iniciar();
                    abrirComLogin = true;
                    continue;
                }
                if (resultado != DialogResult.OK || operador is null) break;

                var caixa = Repositorio.CaixaAberto();
                if (caixa is null)
                {
                    using var abertura = new ValorForm("Abertura de caixa", "Fundo de troco (dinheiro na gaveta)");
                    abertura.StartPosition = FormStartPosition.CenterScreen;
                    if (abertura.ShowDialog() != DialogResult.OK) continue;
                    caixa = Repositorio.AbrirCaixa(operador.Id, abertura.Valor);
                    _ = sync.SincronizarAsync();
                }

                // Tela de venda. "Opções" salvas lá dentro: recarrega a configuração e volta à venda
                // com o mesmo operador, sem passar pelo login.
                SaidaVenda saida;
                while (true)
                {
                    using (var venda = new VendaForm(cfg, sync, operador, caixa))
                    {
                        venda.ShowDialog();
                        saida = venda.Saida;
                    }
                    if (saida != SaidaVenda.Reconfigurado) break;

                    sync.Dispose();
                    cfg = PdvConfig.Carregar();
                    sync = new Sincronizador(cfg);
                    sync.Iniciar();
                }
                if (saida == SaidaVenda.Sair) break;
                abrirComLogin = false; // saiu da conta ou fechou o caixa: mostra "Caixa fechado"
            }

            // Última tentativa de subir o que ficou pendente antes de fechar.
            try { Task.Run(() => sync.SincronizarAsync()).Wait(TimeSpan.FromSeconds(10)); } catch { /* sai mesmo assim */ }
            if (!string.IsNullOrWhiteSpace(cfg.PastaCopiaSeguranca))
            {
                var pastaCopia = cfg.PastaCopiaSeguranca;
                try { Task.Run(() => CopiaSeguranca.Fazer(pastaCopia, out _)).Wait(TimeSpan.FromSeconds(15)); } catch { /* sai mesmo assim */ }
            }
        }
        finally
        {
            sync.Dispose();
        }
    }

    /// <summary>Mostra o andamento da primeira sincronização sem congelar a interface do Windows.</summary>
    private static bool SincronizarPrimeiraVez(Sincronizador sync)
    {
        var ok = false;
        using var janela = new Form
        {
            Text = ProjetoJP.Controles.Aparencia.NomePadrao + " PDV — Preparando o caixa", ClientSize = new Size(450, 140),
            StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, ControlBox = false
        };
        janela.Controls.Add(new Label
        {
            Text = "Baixando os usuários e produtos do servidor.\nIsso só é necessário na primeira configuração.",
            AutoSize = false, Location = new Point(24, 20), Size = new Size(402, 52),
            Font = new Font("Segoe UI", 10)
        });
        janela.Controls.Add(new ProgressBar
        {
            Location = new Point(24, 92), Size = new Size(402, 24),
            Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 25
        });
        janela.Shown += async (_, _) =>
        {
            try { ok = await Task.Run(() => sync.SincronizarAsync()); }
            catch (Exception ex) { Registrar("Erro ao sincronizar a primeira vez", ex); }
            if (!janela.IsDisposed) janela.Close();
        };
        janela.ShowDialog();
        return ok;
    }

    private static bool Configurar(ref PdvConfig cfg, bool primeiraVez)
    {
        using var f = new ConfiguracaoForm(cfg, primeiraVez);
        if (f.ShowDialog() != DialogResult.OK) return false;
        cfg = PdvConfig.Carregar();
        return true;
    }
}
