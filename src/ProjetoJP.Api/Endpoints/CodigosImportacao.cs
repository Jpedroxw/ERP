namespace ProjetoJP.Api.Endpoints;

/// <summary>Regras puras para importar códigos de produtos sem sobrescrever cadastros por colisão.</summary>
public static class CodigosImportacao
{
    public static string Normalizar(string codigo)
    {
        var sb = new System.Text.StringBuilder(codigo.Length);
        foreach (var c in codigo.Replace(' ', '-'))
            if (char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or '/') sb.Append(c);
        var limpo = sb.ToString().TrimStart('.', '_', '-', '/');
        while (limpo.Contains("--", StringComparison.Ordinal)) limpo = limpo.Replace("--", "-", StringComparison.Ordinal);
        return limpo;
    }

    public static bool MesmoCodigo(string primeiro, string segundo) =>
        string.Equals(primeiro, segundo, StringComparison.OrdinalIgnoreCase);

    public static bool ColisaoDeOrigem(string anterior, string original) =>
        !MesmoCodigo(anterior, original);

    // Descrições iguais não provam que a origem é o mesmo cadastro; colisões continuam bloqueadas.
    public static bool AtualizacaoInsegura(string original, string normalizado, string codigoEncontrado) =>
        original.Length > 0 && !MesmoCodigo(normalizado, original) && !MesmoCodigo(codigoEncontrado, original);

    /// <summary>
    /// Código de barras de circulação restrita (uso interno da loja, balança, peso variável): EAN-13 começando por 2,
    /// UPC-A começando por 2 ou 4, EAN-8 começando por 0 ou 2. Esses podem se repetir entre lojas/sistemas, então
    /// não servem para dizer que é o mesmo produto. Os demais GTINs são únicos no mundo (GS1).
    /// </summary>
    public static bool GtinRestrito(string gtin) => gtin.Length switch
    {
        13 => gtin[0] == '2',
        12 => gtin[0] is '2' or '4',
        8 => gtin[0] is '0' or '2',
        14 => gtin[1] == '2',
        _ => true
    };
}
