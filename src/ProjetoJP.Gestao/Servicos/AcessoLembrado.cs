using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;

namespace ProjetoJP.Gestao.Servicos;

/// <summary>
/// "Lembrar este computador" — SEM guardar a senha.
///
/// Ao entrar com a senha e marcar a opção, a Retaguarda cria uma chave de assinatura própria deste computador:
///  - no chip de segurança (TPM 2.0), quando o computador tem: a chave não sai do chip — copiar os arquivos ou
///    o perfil do Windows para outro computador não leva a chave;
///  - sem TPM, numa chave do Windows marcada como não exportável (protege menos, mas a senha continua fora do disco).
/// Só a parte pública vai para o servidor. Nas próximas vezes, a Retaguarda assina o pedido de entrada com a chave.
///
/// Regras no servidor: vale 7 dias; trocar a senha do usuario ou desativa-lo invalida.
/// Sem TPM, a chave privada nao exportavel fica no CNG do usuario Windows. Metadata DPAPI CurrentUser.
/// Quem controlar a sessao Windows desbloqueada pode se autenticar sem senha. Nao usar em PCs compartilhados.
/// </summary>
public sealed class AcessoLembrado
{
    public string Servidor { get; set; } = "";
    public string Login { get; set; } = "";
    public Guid DispositivoId { get; set; }
    public string NomeChave { get; set; } = "";
    public bool Tpm { get; set; }
    // Compatibilidade somente para detectar credenciais V4 com PIN; nunca as rebaixar para acesso sem PIN.
    public bool TemPin { get; set; }
    public DateTime ExpiraEm { get; set; }

    private const string ProvedorTpm = "Microsoft Platform Crypto Provider";
    private static readonly byte[] Entropia = Encoding.UTF8.GetBytes("ErpNaoFiscal.Retaguarda.Lembrado.v2");
    private static string Pasta => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ErpNaoFiscal");
    private static string Arquivo => Path.Combine(Pasta, "retaguarda-lembrado.dat");
    // Versão anterior guardava a senha (protegida pelo Windows): é apagada na primeira abertura.
    private static string ArquivoAntigo => Path.Combine(Pasta, "retaguarda-acesso.dat");

    /// <summary>O computador lembrado para este servidor (null = não lembrado ou vencido).</summary>
    public static AcessoLembrado? Carregar(string servidor)
    {
        ApagarArquivoAntigo();
        var a = Ler();
        if (a is null || !string.Equals(a.Servidor, servidor, StringComparison.OrdinalIgnoreCase)) return null;
        if (a.TemPin || a.ExpiraEm < DateTime.UtcNow)
        {
            EsquecerLocal();
            return null;
        }
        return a;
    }

    /// <summary>
    /// Cria a chave do Windows e registra sua parte publica apos login com senha.
    /// O TPM e opcional; PCs sem TPM nao precisam de PIN do ERP.
    /// </summary>
    public static async Task<AcessoLembrado?> LembrarAsync(ApiClient api, string servidor, string login)
    {
        await EsquecerAsync(api); // um computador lembrado por vez
        var (nome, tpm, publica) = CriarChave();
        try
        {
            var r = await api.PostAsync<RegistrarDispositivoResposta>("api/auth/dispositivos", new RegistrarDispositivoRequest
            {
                Nome = Environment.MachineName, ChavePublica = publica, Tpm = tpm
            });
            var a = new AcessoLembrado
            {
                Servidor = servidor, Login = login.Trim().ToLowerInvariant(), DispositivoId = r.Id,
                NomeChave = nome, Tpm = tpm, TemPin = false, ExpiraEm = r.ExpiraEm
            };
            Gravar(a);
            return a;
        }
        catch
        {
            ApagarChave(nome, tpm);
            throw;
        }
    }

    /// <summary>Esquece este computador: apaga no servidor (se der), a chave e o arquivo.</summary>
    public static async Task EsquecerAsync(ApiClient? api)
    {
        var a = Ler();
        if (a is null) { EsquecerLocal(); return; }
        if (api?.Sessao is not null)
        {
            try { await api.DeleteAsync($"api/auth/dispositivos/{a.DispositivoId}"); }
            catch (ApiException) { /* offline: registro revogavel na API, expira em no maximo 7 dias */ }
        }
        EsquecerLocal();
    }

    /// <summary>Só no computador (quando o servidor já recusou).</summary>
    public static void EsquecerLocal()
    {
        if (Ler() is { } a) ApagarChave(a.NomeChave, a.Tpm);
        try { if (File.Exists(Arquivo)) File.Delete(Arquivo); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Assina o texto do pedido de entrada com a chave deste computador.</summary>
    public string Assinar(string mensagem)
    {
        using var chave = CngKey.Open(NomeChave, Provedor(Tpm));
        using var ec = new ECDsaCng(chave);
        return Convert.ToBase64String(ec.SignData(Encoding.UTF8.GetBytes(mensagem), HashAlgorithmName.SHA256));
    }

    // ---------- Chave ----------

    private static CngProvider Provedor(bool tpm) =>
        tpm ? new CngProvider(ProvedorTpm) : CngProvider.MicrosoftSoftwareKeyStorageProvider;

    /// <summary>ECDSA P-256 não exportável: primeiro tenta no TPM; sem TPM, no Windows.</summary>
    private static (string nome, bool tpm, string publica) CriarChave()
    {
        var nome = "ErpNaoFiscal-Retaguarda-" + Guid.NewGuid().ToString("N");
        foreach (var tpm in new[] { true, false })
        {
            try
            {
                using var chave = CngKey.Create(CngAlgorithm.ECDsaP256, nome, new CngKeyCreationParameters
                {
                    Provider = Provedor(tpm),
                    ExportPolicy = CngExportPolicies.None,
                    KeyUsage = CngKeyUsages.Signing
                });
                using var ec = new ECDsaCng(chave);
                return (nome, tpm, Convert.ToBase64String(ec.ExportSubjectPublicKeyInfo()));
            }
            catch (CryptographicException) when (tpm)
            {
                // Sem TPM (ou desligado na BIOS): segue para a chave do Windows.
            }
        }
        throw new CryptographicException("Não foi possível criar a chave deste computador.");
    }

    private static void ApagarChave(string nome, bool tpm)
    {
        if (string.IsNullOrEmpty(nome)) return;
        try
        {
            var chave = CngKey.Open(nome, Provedor(tpm));
            chave.Delete(); // também libera o objeto
        }
        catch (CryptographicException) { /* já não existe */ }
    }

    // ---------- Arquivo ----------

    private static AcessoLembrado? Ler()
    {
        try
        {
            if (!File.Exists(Arquivo)) return null;
            var json = ProtectedData.Unprotect(File.ReadAllBytes(Arquivo), Entropia, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<AcessoLembrado>(json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            return null;
        }
    }

    private static void Gravar(AcessoLembrado a)
    {
        Directory.CreateDirectory(Pasta);
        GravacaoAtomica.Bytes(Arquivo, ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(a), Entropia, DataProtectionScope.CurrentUser));
    }

    private static void ApagarArquivoAntigo()
    {
        try { if (File.Exists(ArquivoAntigo)) File.Delete(ArquivoAntigo); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
