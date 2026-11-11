using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ProjetoJP.Shared;
using Microsoft.Win32;

namespace ProjetoJP.Controles;

/// <summary>
/// As configurações do caixa e da Retaguarda valem só para o computador onde foram feitas (servidor, chave do
/// caixa, loja, impressora, aparência, banco local do caixa...). Elas ficam em C:\ProgramData\ErpNaoFiscal (e algumas
/// por usuário em %LOCALAPPDATA%\ErpNaoFiscal). Se essa pasta chegar a outro computador — copiada, ou com o
/// computador inteiro clonado (máquina virtual copiada, imagem de disco) —, o programa abre como na primeira vez:
/// o que veio do outro computador é guardado numa pasta à parte e nada se perde.
/// <para>
/// Como reconhece o computador: pela identificação do Windows (MachineGuid) e pela da placa-mãe (UUID do SMBIOS),
/// guardadas só como resumo (hash) em computador.id. Só considera "outro computador" quando uma delas foi lida agora,
/// estava guardada e é diferente — se alguma não puder ser lida, não mexe em nada.
/// </para>
/// </summary>
public static class ComputadorDestaInstalacao
{
    private sealed class Marca
    {
        public string? Windows { get; set; }
        public string? Placa { get; set; }
        public DateTime MarcadoEm { get; set; }
    }

    public static string PastaDados { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ErpNaoFiscal");

    private static string PastaDoUsuario => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ErpNaoFiscal");

    private static string Arquivo => Path.Combine(PastaDados, "computador.id");

    private const string PrefixoGuardado = "de-outro-computador-";

    /// <summary>
    /// Confere se os dados guardados são deste computador. Se vieram de outro, guarda tudo numa pasta à parte e deixa
    /// o programa começar do zero. Devolve essa pasta (para avisar quem está usando) ou null se estava tudo certo.
    /// Chame no começo do programa, antes de ler qualquer configuração. Nunca impede o programa de abrir.
    /// </summary>
    public static string? Conferir()
    {
        try
        {
            var atual = new Marca { Windows = Resumo(LerIdWindows()), Placa = Resumo(LerIdPlaca()), MarcadoEm = DateTime.Now };
            if (atual.Windows is null && atual.Placa is null) return null; // não deu para identificar: não mexe

            var salva = Ler();
            if (salva is null)
            {
                // Primeira vez neste computador, ou instalação de antes desta versão: só marca.
                Gravar(atual);
                return null;
            }

            var outro = Diferente(salva.Windows, atual.Windows) || Diferente(salva.Placa, atual.Placa);
            if (!outro)
            {
                // Completa o que não tinha sido lido antes (sem trocar o que já estava).
                if ((salva.Windows is null && atual.Windows is not null) || (salva.Placa is null && atual.Placa is not null))
                {
                    salva.Windows ??= atual.Windows;
                    salva.Placa ??= atual.Placa;
                    Gravar(salva);
                }
                return null;
            }

            var guardado = GuardarDadosDoOutroComputador();
            Gravar(atual);
            return guardado;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Aviso padrão para quem abriu o programa num computador com dados de outro.</summary>
    public static string Aviso(string programa, string pastaGuardada) =>
        $"Este {programa} está com as configurações de OUTRO computador (a pasta de dados foi copiada ou o computador foi clonado).\n\n" +
        "Por segurança, ele vai abrir como na primeira vez: configure o servidor e as opções deste computador. " +
        "Daqui em diante, o que for configurado aqui vale só para este computador.\n\n" +
        $"Nada foi apagado: o que veio do outro computador foi guardado em\n{pastaGuardada}";

    private static bool Diferente(string? guardado, string? agora) => guardado is not null && agora is not null && guardado != agora;

    private static Marca? Ler()
    {
        try
        {
            return File.Exists(Arquivo) ? JsonSerializer.Deserialize<Marca>(File.ReadAllText(Arquivo)) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void Gravar(Marca marca)
    {
        Directory.CreateDirectory(PastaDados);
        GravacaoAtomica.Texto(Arquivo, JsonSerializer.Serialize(marca));
    }

    /// <summary>
    /// Move o conteúdo de C:\ProgramData\ErpNaoFiscal (e de %LOCALAPPDATA%\ErpNaoFiscal, menos os logs) para
    /// C:\ProgramData\ErpNaoFiscal\de-outro-computador-AAAAMMDD-HHMMSS. Item que não puder ser movido fica onde está.
    /// </summary>
    private static string GuardarDadosDoOutroComputador()
    {
        var destino = Path.Combine(PastaDados, PrefixoGuardado + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(destino);
        Mover(PastaDados, destino, nome => nome.StartsWith(PrefixoGuardado, StringComparison.OrdinalIgnoreCase));
        if (Directory.Exists(PastaDoUsuario))
            Mover(PastaDoUsuario, Path.Combine(destino, "usuario"), nome => nome.Equals("logs", StringComparison.OrdinalIgnoreCase));
        return destino;
    }

    private static void Mover(string origem, string destino, Func<string, bool> pular)
    {
        Directory.CreateDirectory(destino);
        foreach (var caminho in Directory.EnumerateFileSystemEntries(origem).ToList())
        {
            var nome = Path.GetFileName(caminho);
            if (pular(nome) || Path.GetFullPath(caminho).Equals(Path.GetFullPath(destino), StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var alvo = Path.Combine(destino, nome);
                if (Directory.Exists(caminho)) Directory.Move(caminho, alvo);
                else File.Move(caminho, alvo);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // em uso ou sem permissão: fica onde está
            }
        }
    }

    private static string? Resumo(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("ErpNaoFiscal|" + valor.Trim().ToUpperInvariant()));
        return Convert.ToHexString(bytes, 0, 16);
    }

    /// <summary>Identificação da instalação do Windows (muda ao reinstalar o Windows).</summary>
    private static string? LerIdWindows()
    {
        try
        {
            using var raiz = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var chave = raiz.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return chave?.GetValue("MachineGuid") as string;
        }
        catch (Exception)
        {
            return null;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetSystemFirmwareTable(uint provedor, uint tabela, byte[]? dados, uint tamanho);

    /// <summary>
    /// UUID da placa-mãe (SMBIOS tipo 1). Num computador clonado (máquina virtual copiada), a placa "virtual" ganha
    /// um UUID novo, enquanto o Windows continua igual — por isso as duas identificações.
    /// </summary>
    private static string? LerIdPlaca()
    {
        try
        {
            const uint Rsmb = 0x52534D42; // 'RSMB'
            var tamanho = GetSystemFirmwareTable(Rsmb, 0, null, 0);
            if (tamanho is 0 or > 4_000_000) return null;
            var dados = new byte[tamanho];
            if (GetSystemFirmwareTable(Rsmb, 0, dados, tamanho) != tamanho || dados.Length < 8) return null;

            // Cabeçalho: 4 bytes de versão e 4 bytes com o tamanho da tabela; a tabela começa no byte 8.
            var fim = (int)Math.Min(dados.Length, 8L + BitConverter.ToUInt32(dados, 4));
            var i = 8;
            while (i + 4 <= fim)
            {
                var tipo = dados[i];
                var comprimento = dados[i + 1];
                if (comprimento < 4) break;
                if (tipo == 1 && comprimento >= 0x19 && i + 0x18 <= fim)
                {
                    var uuid = new byte[16];
                    Array.Copy(dados, i + 8, uuid, 0, 16);
                    if (uuid.All(b => b == 0) || uuid.All(b => b == 0xFF)) return null; // placa sem UUID
                    return Convert.ToHexString(uuid);
                }
                if (tipo == 127) break; // fim da tabela
                // Pula a parte fixa e os textos da estrutura (terminam com dois zeros seguidos).
                var j = i + comprimento;
                while (j + 1 < fim && !(dados[j] == 0 && dados[j + 1] == 0)) j++;
                i = j + 2;
            }
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
