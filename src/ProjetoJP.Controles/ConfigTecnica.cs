using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ProjetoJP.Shared;

namespace ProjetoJP.Controles;

/// <summary>
/// Configuração técnica (só suporte/admin, com senha própria): a lista de servidores — nome que se digita
/// no caixa e na Retaguarda → site para onde aponta. Fica em C:\ProgramData\ErpNaoFiscal\servidores.dat,
/// criptografada pelo Windows (DPAPI): abrindo o arquivo não dá para ler o site nem a senha.
/// Vale para o caixa e para a Retaguarda instalados no mesmo computador.
/// </summary>
public static class ConfigTecnica
{
    /// <summary>
    /// Senha de fábrica. No primeiro acesso o sistema obriga a criar outra (gravada só como hash).
    /// Troque aqui se quiser que os programas já saiam com outra senha inicial.
    /// </summary>
    public const string SenhaDeFabrica = "suporte@erp";

    private sealed class Dados
    {
        public Dictionary<string, string> Servidores { get; set; } = new();
        public string? SenhaHash { get; set; }
    }

    private static readonly byte[] Entropia = Encoding.UTF8.GetBytes("ErpNaoFiscal.ConfigTecnica.v1");

    private static string Pasta => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ErpNaoFiscal");

    public static string Caminho => Path.Combine(Pasta, "servidores.dat");

    private static Dados? _dados;

    /// <summary>Lê o arquivo e passa a lista de servidores para o sistema. Chame no início do programa.</summary>
    public static void Carregar()
    {
        _dados = Ler();
        EnderecoServidor.Definir(_dados.Servidores.Count > 0 ? _dados.Servidores : EnderecoServidor.NomesDeFabrica);
    }

    /// <summary>Lista atual (nome → site).</summary>
    public static Dictionary<string, string> Servidores() => new(EnderecoServidor.Nomes, StringComparer.OrdinalIgnoreCase);

    /// <summary>Ainda está com a senha de fábrica?</summary>
    public static bool SenhaEhDeFabrica => string.IsNullOrEmpty((_dados ??= Ler()).SenhaHash);

    public static bool ConferirSenha(string senha)
    {
        var d = _dados ??= Ler();
        return string.IsNullOrEmpty(d.SenhaHash)
            ? CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(senha), Encoding.UTF8.GetBytes(SenhaDeFabrica))
            : Senha.Verificar(senha, d.SenhaHash);
    }

    public static void TrocarSenha(string nova)
    {
        var d = _dados ??= Ler();
        d.SenhaHash = Senha.Gerar(nova);
        Gravar(d);
    }

    public static void SalvarServidores(IDictionary<string, string> servidores)
    {
        var d = _dados ??= Ler();
        d.Servidores = new Dictionary<string, string>(servidores, StringComparer.OrdinalIgnoreCase);
        Gravar(d);
        EnderecoServidor.Definir(d.Servidores);
    }

    private static Dados Ler()
    {
        try
        {
            if (!File.Exists(Caminho)) return new Dados();
            var aberto = ProtectedData.Unprotect(File.ReadAllBytes(Caminho), Entropia, DataProtectionScope.LocalMachine);
            return JsonSerializer.Deserialize<Dados>(aberto) ?? new Dados();
        }
        catch (Exception)
        {
            // arquivo de outro computador (copiado), corrompido ou sem permissão: volta para o de fábrica
            return new Dados();
        }
    }

    private static void Gravar(Dados d)
    {
        Directory.CreateDirectory(Pasta);
        var json = JsonSerializer.SerializeToUtf8Bytes(d);
        GravacaoAtomica.Bytes(Caminho, ProtectedData.Protect(json, Entropia, DataProtectionScope.LocalMachine));
    }

    /// <summary>
    /// Pede a senha técnica (e, se ainda for a de fábrica, obriga a criar uma nova). True = liberado.
    /// Usada também no caixa para entrar nas Opções quando não há gerente que consiga autorizar.
    /// </summary>
    public static bool PedirSenha(IWin32Window? dono)
    {
        using (var senha = new SenhaTecnicaForm())
            if (senha.ShowDialog(dono) != DialogResult.OK) return false;

        if (SenhaEhDeFabrica)
        {
            MessageBox.Show(dono, "Primeiro acesso: crie uma senha técnica nova (a de fábrica deixa de valer neste computador).",
                "Configuração técnica", MessageBoxButtons.OK, MessageBoxIcon.Information);
            using var nova = new NovaSenhaTecnicaForm();
            if (nova.ShowDialog(dono) != DialogResult.OK) return false;
        }
        return true;
    }

    /// <summary>
    /// Pede a senha técnica e abre a lista de servidores. No primeiro acesso obriga a trocar a senha de fábrica.
    /// Devolve true se a lista mudou.
    /// </summary>
    public static bool Abrir(IWin32Window? dono)
    {
        if (!PedirSenha(dono)) return false;
        using var f = new ServidoresForm();
        return f.ShowDialog(dono) == DialogResult.OK;
    }
}
