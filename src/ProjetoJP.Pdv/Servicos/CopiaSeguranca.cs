using ProjetoJP.Pdv.Dados;
using Microsoft.Data.Sqlite;

namespace ProjetoJP.Pdv.Servicos;

/// <summary>
/// Cópia de segurança do banco do caixa numa pasta da rede da loja (ex.: \\RETAGUARDA\CopiasCaixa). Protege as vendas
/// que ainda não subiram para o servidor se o computador do caixa estragar. Usa só a rede da loja: funciona sem internet.
/// A cópia é feita pelo backup do próprio SQLite (arquivo sempre íntegro, mesmo com vendas acontecendo), primeiro num
/// arquivo local e depois copiada para a pasta como caixa-NOMEDOPC.db (a cópia anterior fica em caixa-NOMEDOPC.anterior.db).
/// </summary>
public static class CopiaSeguranca
{
    private static readonly object Trava = new();
    private static bool _rodando;
    private static bool _deNovo;

    public static DateTime? UltimaCopia { get; private set; }
    public static string? UltimoErro { get; private set; }

    /// <summary>Quando a cópia falha ou volta a funcionar (ok, mensagem). Pode vir de outra thread.</summary>
    public static event Action<bool, string>? SituacaoMudou;

    /// <summary>Nome do arquivo na pasta: um por computador, para vários caixas usarem a mesma pasta.</summary>
    public static string NomeArquivo => "caixa-" + NomeSeguro(Environment.MachineName) + ".db";

    /// <summary>Pede uma cópia em segundo plano (depois de cada venda, sangria, fechamento...). Pedidos seguidos viram um só.</summary>
    public static void Pedir(string? pasta)
    {
        if (string.IsNullOrWhiteSpace(pasta)) return;
        lock (Trava)
        {
            if (_rodando)
            {
                _deNovo = true;
                return;
            }
            _rodando = true;
        }
        var destino = pasta.Trim();
        _ = Task.Run(() =>
        {
            while (true)
            {
                Fazer(destino, out _);
                lock (Trava)
                {
                    if (!_deNovo)
                    {
                        _rodando = false;
                        return;
                    }
                    _deNovo = false;
                }
            }
        });
    }

    /// <summary>Faz a cópia agora (também usado pelo botão "Testar cópia"). Devolve true se deu certo.</summary>
    public static bool Fazer(string pasta, out string mensagem)
    {
        var temporario = Path.Combine(Path.GetTempPath(), "erp-copia-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            if (string.IsNullOrWhiteSpace(pasta)) throw new IOException("pasta não informada.");
            pasta = pasta.Trim();

            // 1) Backup do SQLite num arquivo local: rápido e sempre íntegro.
            using (var origem = Db.Abrir())
            using (var destino = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporario, Pooling = false }.ToString()))
            {
                destino.Open();
                origem.BackupDatabase(destino);
            }

            // 2) Copia para a pasta com outro nome e só no fim troca: na pasta nunca fica um arquivo pela metade.
            Directory.CreateDirectory(pasta);
            var final = Path.Combine(pasta, NomeArquivo);
            var anterior = Path.Combine(pasta, Path.GetFileNameWithoutExtension(NomeArquivo) + ".anterior.db");
            var parcial = final + ".copiando";
            File.Copy(temporario, parcial, overwrite: true);
            if (File.Exists(final)) File.Move(final, anterior, overwrite: true);
            File.Move(parcial, final, overwrite: true);

            UltimaCopia = DateTime.Now;
            mensagem = $"Cópia de segurança feita às {UltimaCopia:HH:mm:ss} ({final}).";
            if (UltimoErro is not null)
            {
                UltimoErro = null;
                SituacaoMudou?.Invoke(true, "Cópia de segurança na rede voltou a funcionar.");
            }
            return true;
        }
        catch (Exception ex)
        {
            mensagem = "Cópia de segurança na rede falhou: " + Motivo(ex);
            var mudou = UltimoErro != mensagem;
            UltimoErro = mensagem;
            if (mudou) SituacaoMudou?.Invoke(false, mensagem);
            return false;
        }
        finally
        {
            try { if (File.Exists(temporario)) File.Delete(temporario); }
            catch (Exception) { /* fica na pasta temporária do Windows */ }
        }
    }

    // Códigos do Windows (HRESULT) para erros de rede e de disco.
    private const int CaminhoDeRedeNaoEncontrado = unchecked((int)0x80070035);
    private const int NomeDeRedeNaoEncontrado = unchecked((int)0x80070043);
    private const int RedeIndisponivel = unchecked((int)0x80070033);
    private const int DiscoCheio = unchecked((int)0x80070070);

    private static string Motivo(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "sem permissão para gravar na pasta (libere a pasta compartilhada para este computador).",
        DirectoryNotFoundException => "pasta não encontrada (confira o caminho e se o outro computador está ligado).",
        IOException io when io.HResult is CaminhoDeRedeNaoEncontrado or NomeDeRedeNaoEncontrado or RedeIndisponivel =>
            "o computador da pasta não foi encontrado na rede (está ligado? a rede da loja está funcionando?).",
        IOException io when io.HResult is DiscoCheio => "a pasta está sem espaço.",
        _ => ex.Message
    };

    private static string NomeSeguro(string nome)
    {
        var invalidos = Path.GetInvalidFileNameChars();
        var limpo = new string(nome.Select(c => invalidos.Contains(c) ? '_' : c).ToArray()).Trim();
        return limpo.Length == 0 ? "caixa" : limpo;
    }
}
