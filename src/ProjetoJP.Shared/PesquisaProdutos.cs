using System.Globalization;
using System.Text;

namespace ProjetoJP.Shared;

/// <summary>
/// Pesquisa de produtos por nome e códigos. Não altera cadastros nem faz correspondência automática
/// para movimentar estoque: o operador sempre precisa selecionar o produto encontrado.
/// </summary>
public static class PesquisaProdutos
{
    public static string Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "";
        var decomposicao = texto.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposicao.Length);
        var anteriorEspaco = false;
        foreach (var c in decomposicao)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToUpperInvariant(c));
                anteriorEspaco = false;
            }
            else if (!anteriorEspaco && sb.Length > 0)
            {
                sb.Append(' ');
                anteriorEspaco = true;
            }
        }
        return sb.ToString().Trim();
    }

    /// <summary>Quanto maior, mais relevante; -1 significa que não corresponde à busca.</summary>
    public static int Pontuar(string pesquisa, string descricao, string? codigo, string? barras)
    {
        var termo = Normalizar(pesquisa);
        if (termo.Length == 0) return 0;
        var nome = Normalizar(descricao);
        var cod = Normalizar(codigo);
        var gtin = Normalizar(barras);
        if ((cod.Length > 0 && termo == cod) || (gtin.Length > 0 && termo == gtin)) return 1200;
        if (nome == termo) return 1100;
        if (nome.StartsWith(termo, StringComparison.Ordinal)) return 1000;
        if (cod.StartsWith(termo, StringComparison.Ordinal) || gtin.StartsWith(termo, StringComparison.Ordinal)) return 950;
        if (nome.Contains(termo, StringComparison.Ordinal)) return 900;
        if (cod.Contains(termo, StringComparison.Ordinal) || gtin.Contains(termo, StringComparison.Ordinal)) return 850;

        var palavras = nome.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var buscas = termo.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var proximos = 0;
        foreach (var busca in buscas)
        {
            if (palavras.Any(p => p.Contains(busca, StringComparison.Ordinal))) continue;
            // Só sugere erros de digitação quando a palavra é grande o bastante para evitar falsos positivos.
            var tolerancia = busca.Length >= 7 ? 2 : busca.Length >= 4 ? 1 : 0;
            if (tolerancia == 0 || !palavras.Any(p => DistanciaLimitada(busca, p, tolerancia))) return -1;
            proximos++;
        }
        return 700 - proximos * 80;
    }

    private static bool DistanciaLimitada(string a, string b, int limite)
    {
        if (Math.Abs(a.Length - b.Length) > limite) return false;
        var anterior = new int[b.Length + 1];
        var atual = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) anterior[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            atual[0] = i;
            var menor = atual[0];
            for (var j = 1; j <= b.Length; j++)
            {
                var custo = a[i - 1] == b[j - 1] ? 0 : 1;
                atual[j] = Math.Min(Math.Min(anterior[j] + 1, atual[j - 1] + 1), anterior[j - 1] + custo);
                menor = Math.Min(menor, atual[j]);
            }
            if (menor > limite) return false;
            (anterior, atual) = (atual, anterior);
        }
        return anterior[b.Length] <= limite;
    }
}
