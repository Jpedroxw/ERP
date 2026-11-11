using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ProjetoJP.Shared;

/// <summary>Como cada campo é digitado, mostrado e validado (Retaguarda, caixa e API usam as mesmas regras).</summary>
public enum TipoCampo
{
    Texto,
    /// <summary>Texto sempre em maiúsculas (descrição de produto, nome do terminal).</summary>
    Maiusculas,
    /// <summary>Código interno do produto: caracteres ASCII seguros, sem espaços.</summary>
    CodigoProduto,
    /// <summary>Login: minúsculas, números, ponto, hífen e sublinhado.</summary>
    Login,
    /// <summary>Valor em R$ com 2 casas, digitado como calculadora (1, 12, 1,23...).</summary>
    Moeda,
    /// <summary>Quantidade: 3 casas para KG/L/M..., inteira para UN/CX/PC...</summary>
    Quantidade,
    Inteiro,
    /// <summary>CPF (000.000.000-00) ou CNPJ (00.000.000/0000-00), inclusive o CNPJ alfanumérico.</summary>
    CpfCnpj,
    /// <summary>(00) 0000-0000 ou (00) 00000-0000.</summary>
    Telefone,
    Email,
    /// <summary>EAN/GTIN com 8, 12, 13 ou 14 dígitos e dígito verificador correto.</summary>
    CodigoBarras,
    /// <summary>Escolhida numa lista (UN, KG, L...).</summary>
    Unidade
}

public static partial class Formatos
{
    public static readonly CultureInfo Br = new("pt-BR");

    public const decimal MaxMoeda = 9_999_999.99m;
    public const decimal MaxQuantidade = 999_999.999m;

    /// <summary>Siglas dos estados (lista de escolha de UF).</summary>
    public static readonly string[] Ufs =
        ["AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG", "PA", "PB", "PR", "PE", "PI",
         "RJ", "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO"];

    public static bool UfValida(string? uf) => uf is not null && Ufs.Contains(uf.Trim().ToUpperInvariant());

    /// <summary>Unidades aceitas no cadastro de produto.</summary>
    public static readonly string[] Unidades = ["UN", "KG", "G", "L", "ML", "M", "M2", "CX", "PC", "PCT", "DZ", "FD", "KIT", "PAR"];

    private static readonly HashSet<string> Fracionadas = new(StringComparer.OrdinalIgnoreCase) { "KG", "G", "L", "ML", "M", "M2" };
    private static readonly HashSet<string> Pesaveis = new(StringComparer.OrdinalIgnoreCase) { "KG", "G" };

    /// <summary>Produtos vendidos por peso/volume/medida aceitam quantidade com casas decimais.</summary>
    public static bool UnidadeFracionada(string? unidade) => unidade is not null && Fracionadas.Contains(unidade.Trim());

    /// <summary>Só KG e G usam a balança. Unidade, litro e metro não.</summary>
    public static bool UnidadePesavel(string? unidade) => unidade is not null && Pesaveis.Contains(unidade.Trim());

    public static int CasasQuantidade(string? unidade) => UnidadeFracionada(unidade) ? 3 : 0;

    public static bool UnidadeValida(string? unidade) =>
        unidade is not null && Unidades.Contains(unidade.Trim().ToUpperInvariant());

    // ---------- Exibição ----------

    public static string Moeda(decimal v) => v.ToString("C2", Br);

    /// <summary>"2" para inteiros, "1,250" para fracionados. Com a unidade, usa as casas dela.</summary>
    public static string Quantidade(decimal v, string? unidade = null)
    {
        if (unidade is not null) return v.ToString("N" + CasasQuantidade(unidade), Br);
        return v == decimal.Truncate(v) ? v.ToString("N0", Br) : v.ToString("N3", Br);
    }

    public static string FormatarCpfCnpj(string? valor)
    {
        var d = LimparDocumento(valor);
        if (d.Length == 0) return "";
        var cnpj = d.Length > 11 || d.Any(char.IsAsciiLetter);
        return Aplicar(d, cnpj ? "##.###.###/####-##" : "###.###.###-##");
    }

    public static string FormatarTelefone(string? valor)
    {
        var d = SoDigitos(valor);
        if (d.Length == 0) return "";
        return Aplicar(d, d.Length <= 10 ? "(##) ####-####" : "(##) #####-####");
    }

    /// <summary>Encaixa os caracteres na máscara; para no último caractere digitado (sem separador sobrando).</summary>
    private static string Aplicar(string caracteres, string mascara)
    {
        var sb = new StringBuilder();
        var i = 0;
        foreach (var m in mascara)
        {
            if (i >= caracteres.Length) break;
            sb.Append(m == '#' ? caracteres[i++] : m);
        }
        return sb.ToString();
    }

    // ---------- Limpeza ----------

    public static string SoDigitos(string? s) => s is null ? "" : new string(s.Where(char.IsAsciiDigit).ToArray());

    /// <summary>CPF/CNPJ sem pontuação, em maiúsculas (o CNPJ alfanumérico tem letras).</summary>
    public static string LimparDocumento(string? s) =>
        s is null ? "" : new string(s.ToUpperInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());

    public static string RemoverAcentos(string texto)
    {
        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Aceita "1,50", "1.50", "1.234,50" e "R$ 1.234,50".</summary>
    public static bool TryLerDecimal(string? texto, out decimal valor)
    {
        texto = (texto ?? "").Replace("R$", "").Trim();
        if (texto.Contains('.') && !texto.Contains(','))
        {
            // "1.50" = um e meio; "1.234.567" (só milhares) continua sendo milhar
            var partes = texto.Split('.');
            if (partes.Length == 2 && partes[1].Length != 3)
                texto = texto.Replace('.', ',');
        }
        return decimal.TryParse(texto, NumberStyles.Number, Br, out valor);
    }

    // ---------- Validação ----------

    public static bool CpfValido(string? valor)
    {
        var d = SoDigitos(valor);
        if (d.Length != 11 || d.Distinct().Count() == 1) return false;

        int Digito(int tamanho)
        {
            var soma = 0;
            for (var i = 0; i < tamanho; i++)
                soma += (d[i] - '0') * (tamanho + 1 - i);
            var resto = soma % 11;
            return resto < 2 ? 0 : 11 - resto;
        }

        return Digito(9) == d[9] - '0' && Digito(10) == d[10] - '0';
    }

    /// <summary>
    /// CNPJ numérico e alfanumérico (emitido desde jul/2026): as 12 primeiras posições podem ter letras,
    /// cada caractere vale (código ASCII − 48) e os dois dígitos verificadores continuam numéricos.
    /// </summary>
    public static bool CnpjValido(string? valor)
    {
        var c = LimparDocumento(valor);
        if (c.Length != 14) return false;
        if (!char.IsAsciiDigit(c[12]) || !char.IsAsciiDigit(c[13])) return false;
        if (c.Distinct().Count() == 1) return false;

        int[] pesos1 = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        int[] pesos2 = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

        int Digito(int[] pesos)
        {
            var soma = 0;
            for (var i = 0; i < pesos.Length; i++)
                soma += (c[i] - '0') * pesos[i];
            var resto = soma % 11;
            return resto < 2 ? 0 : 11 - resto;
        }

        return Digito(pesos1) == c[12] - '0' && Digito(pesos2) == c[13] - '0';
    }

    /// <summary>CPF (11 dígitos) ou CNPJ (14 caracteres). Vazio não é válido: confira antes se o campo é opcional.</summary>
    public static bool DocumentoValido(string? valor)
    {
        var c = LimparDocumento(valor);
        return c.Length == 11 ? CpfValido(c) : CnpjValido(c);
    }

    public static bool TelefoneValido(string? valor) => SoDigitos(valor).Length is 10 or 11;

    public static bool EmailValido(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor) || valor.Length > 120 || !TextoSeguro(valor)) return false;
        var mail = valor.Trim();
        if (!RegexEmail().IsMatch(mail)) return false;
        var partes = mail.Split('@');
        if (partes.Length != 2 || partes[0].Length == 0 || partes[0].StartsWith('.') ||
            partes[0].EndsWith('.') || partes[0].Contains("..")) return false;
        var dominio = partes[1].Split('.');
        if (dominio.Length < 2 || dominio[^1].Length < 2) return false;
        return dominio.All(d => d.Length is >= 1 and <= 63 && d[0] != '-' && d[^1] != '-' &&
               d.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'));
    }

    public static bool LoginValido(string? valor) => !string.IsNullOrEmpty(valor) && RegexLogin().IsMatch(valor);

    /// <summary>
    /// Impede caracteres invisíveis/de controle nos textos comerciais sem bloquear
    /// letras acentuadas, nomes reais, pontuação normal ou emojis válidos.
    /// </summary>
    public static bool TextoSeguro(string? valor, bool multilinha = false)
    {
        if (valor is null) return true;
        foreach (var ch in valor)
        {
            if (char.IsControl(ch) && !(multilinha && (ch is '\r' or '\n'))) return false;
            if (char.GetUnicodeCategory(ch) == UnicodeCategory.Format) return false;
        }
        return true;
    }

    /// <summary>Código interno do produto: letras, dígitos, ponto, traço, barra e sublinhado.</summary>
    public static bool CodigoProdutoValido(string? valor)
    {
        var codigo = valor?.Trim().ToUpperInvariant() ?? "";
        return codigo.Length is >= 1 and <= 30 && char.IsAsciiLetterOrDigit(codigo[0]) &&
               codigo.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or '/');
    }

    /// <summary>Não aceita letras ou símbolos escondidos que a limpeza de CPF/CNPJ descartaria.</summary>
    public static bool DocumentoDigitadoValido(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return true;
        return valor.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '/' or '-' or ' ') &&
               DocumentoValido(valor);
    }

    /// <summary>Valida o conteúdo bruto, antes de remover a máscara do telefone.</summary>
    public static bool TelefoneDigitadoValido(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return true;
        return valor.All(c => char.IsAsciiDigit(c) || c is '(' or ')' or '-' or ' ') &&
               TelefoneValido(valor);
    }

    /// <summary>GTIN-8, 12 (UPC), 13 (EAN) ou 14, com dígito verificador.</summary>
    public static bool GtinValido(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return false;
        var d = valor.Trim();
        if (d.Length is not (8 or 12 or 13 or 14) || !d.All(char.IsAsciiDigit)) return false;

        var soma = 0;
        var peso = 3;
        for (var i = d.Length - 2; i >= 0; i--)
        {
            soma += (d[i] - '0') * peso;
            peso = peso == 3 ? 1 : 3;
        }
        return (10 - soma % 10) % 10 == d[^1] - '0';
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex RegexEmail();

    [GeneratedRegex(@"^[a-z0-9._-]{3,60}$")]
    private static partial Regex RegexLogin();
}
