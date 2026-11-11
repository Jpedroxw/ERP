using System.Text;

namespace ProjetoJP.Shared;

/// <summary>
/// Salva arquivos de configuracao em duas etapas para evitar um JSON truncado
/// caso o processo seja interrompido durante a gravacao.
/// A substituicao ocorre na mesma pasta/volume do arquivo original.
/// </summary>
public static class GravacaoAtomica
{
    public static void Texto(string caminho, string conteudo) =>
        Bytes(caminho, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(conteudo));

    public static void Bytes(string caminho, byte[] conteudo)
    {
        var pasta = Path.GetDirectoryName(Path.GetFullPath(caminho))
                    ?? throw new ArgumentException("Caminho de arquivo invalido.", nameof(caminho));
        Directory.CreateDirectory(pasta);

        var temporario = Path.Combine(pasta, "." + Path.GetFileName(caminho) +
                                               "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllBytes(temporario, conteudo);
            File.Move(temporario, caminho, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporario)) File.Delete(temporario);
        }
    }
}
