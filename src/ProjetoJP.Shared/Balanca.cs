namespace ProjetoJP.Shared;

/// <summary>Etiqueta de balança: EAN-13 que começa com 2, com o código do produto e o peso ou o preço.</summary>
public static class EtiquetaBalanca
{
    public readonly record struct Leitura(string CodigoProduto, decimal? PesoKg, decimal? PrecoTotal);

    /// <summary>
    /// Lê um EAN-13 de balança. <paramref name="digitosCodigo"/> é 4, 5 ou 6.
    /// O miolo, antes do dígito verificador, é peso em milésimos de kg (01250 = 1,250 kg)
    /// ou preço em centavos, quando <paramref name="preco"/> é true.
    /// </summary>
    public static bool TentarLer(string? codigo, int digitosCodigo, bool preco, out Leitura leitura)
    {
        leitura = default;
        if (digitosCodigo is < 4 or > 6) return false;
        var d = new string((codigo ?? "").Where(char.IsAsciiDigit).ToArray());
        if (d.Length != 13 || d[0] != '2' || !Formatos.GtinValido(d)) return false;

        var codigoProduto = d.Substring(1, digitosCodigo);
        var miolo = d.Substring(1 + digitosCodigo, 11 - digitosCodigo);
        if (miolo.Length < 4 || !decimal.TryParse(miolo, out var bruto) || bruto <= 0) return false;

        if (preco)
            leitura = new Leitura(codigoProduto, null, bruto / 100m);
        else
            leitura = new Leitura(codigoProduto, bruto / 1000m, null);
        return true;
    }
}

public enum SituacaoPeso { SemLeitura, SemPeso, Instavel, Estavel }

/// <summary>Texto que as balanças de USB/serial mandam, sem abrir a porta.</summary>
public static class PesoBalanca
{
    public readonly record struct Leitura(SituacaoPeso Situacao, decimal Kg);

    /// <summary>
    /// ST = estável, US = instável. Sem essa marca, um número maior que zero vale como estável
    /// (balança simples só envia o peso depois que firma). Zero é "sem peso".
    /// </summary>
    public static bool TentarLer(string? linha, out Leitura leitura)
    {
        leitura = default;
        if (string.IsNullOrWhiteSpace(linha)) return false;
        var compacto = linha.Trim().ToLowerInvariant().Replace(" ", "");
        var instavel = Marca(compacto, "us") || compacto.Contains("instav", StringComparison.Ordinal) || compacto.Contains("unstable", StringComparison.Ordinal);
        var numero = new System.Text.StringBuilder();
        var viuDigito = false;
        var fim = compacto.Length;
        for (var i = 0; i < compacto.Length; i++)
        {
            var c = compacto[i];
            if (char.IsAsciiDigit(c))
            {
                numero.Append(c);
                viuDigito = true;
            }
            else if (c is ',' or '.' && viuDigito)
                numero.Append(c);
            else if (viuDigito)
            {
                fim = i;
                break;
            }
        }
        var temNumero = LerNumero(numero.ToString(), out var valor);
        // A unidade é a que vem logo DEPOIS do número ("1250g", "1.250kg"). Letras antes dele são marcas
        // da balança (ST, US, GS = peso bruto, NT...) — o "g" de "GS" não pode virar grama.
        var unidade = new string(compacto.Skip(fim).TakeWhile(char.IsAsciiLetter).ToArray());
        var gramas = unidade == "g";
        var kg = temNumero ? (gramas ? valor / 1000m : valor) : 0;

        if (instavel)
        {
            leitura = new Leitura(SituacaoPeso.Instavel, 0);
            return true;
        }
        if (!temNumero) return false;
        if (kg <= 0)
        {
            leitura = new Leitura(SituacaoPeso.SemPeso, 0);
            return true;
        }
        if (kg > 9999) return false;
        leitura = new Leitura(SituacaoPeso.Estavel, kg);
        return true;
    }

    public static bool TentarExtrair(string? linha, out decimal kg)
    {
        kg = 0;
        if (!TentarLer(linha, out var leitura) || leitura.Situacao != SituacaoPeso.Estavel) return false;
        kg = leitura.Kg;
        return true;
    }

    /// <summary>ST ou US no início da linha ou depois de vírgula, como na balança Toledo.</summary>
    private static bool Marca(string compacto, string token)
    {
        var i = 0;
        while (i < compacto.Length)
        {
            if (compacto.AsSpan(i).StartsWith(token) &&
                (i + token.Length == compacto.Length || !char.IsAsciiLetter(compacto[i + token.Length])))
                return true;
            var proxima = compacto.IndexOf(',', i);
            if (proxima < 0) return false;
            i = proxima + 1;
        }
        return false;
    }

    /// <summary>Na balança o ponto é decimal, mesmo com 3 casas (1.250 kg). Não é separador de milhar.</summary>
    private static bool LerNumero(string numero, out decimal valor)
    {
        valor = 0;
        if (numero.Length == 0) return false;
        var virgula = numero.LastIndexOf(',');
        var ponto = numero.LastIndexOf('.');
        var normal = virgula >= 0 && virgula > ponto
            ? numero.Replace(".", "").Replace(',', '.')
            : numero.Replace(",", "");
        return decimal.TryParse(normal, System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out valor) && valor >= 0;
    }
}
