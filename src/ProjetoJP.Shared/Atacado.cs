namespace ProjetoJP.Shared;

/// <summary>
/// Preço de atacado por quantidade ("atacarejo"): a partir de X unidades do mesmo produto no cupom,
/// cada unidade sai pelo preço de atacado. Vale o preço fixo do produto e as promoções de atacado em vigor;
/// entre os que a quantidade alcança, fica o menor (e nunca acima do preço de varejo).
/// </summary>
public static class Atacado
{
    public readonly record struct Faixa(decimal Minimo, decimal Preco);

    /// <summary>Preço unitário para <paramref name="quantidade"/> e a faixa usada (null = ficou no varejo).</summary>
    public static (decimal Preco, Faixa? Faixa) Calcular(decimal precoVarejo, IEnumerable<Faixa> faixas, decimal quantidade)
    {
        var preco = precoVarejo;
        Faixa? usada = null;
        foreach (var f in faixas)
        {
            if (f.Minimo <= 0 || f.Preco <= 0 || quantidade < f.Minimo || f.Preco >= preco) continue;
            preco = f.Preco;
            usada = f;
        }
        return (preco, usada);
    }

    /// <summary>A próxima faixa que a quantidade ainda não alcançou (para avisar "a partir de 6 sai R$ 4,50").</summary>
    public static Faixa? Proxima(decimal precoVarejo, IEnumerable<Faixa> faixas, decimal quantidade) =>
        faixas.Where(f => f.Minimo > quantidade && f.Preco > 0 && f.Preco < precoVarejo)
            .OrderBy(f => f.Minimo).Cast<Faixa?>().FirstOrDefault();

    /// <summary>Confere o atacado do cadastro do produto. Os dois zerados = não vende no atacado.</summary>
    public static string? Validar(string? unidade, decimal precoVenda, decimal precoAtacado, decimal qtdMinima)
    {
        if (precoAtacado < 0 || qtdMinima < 0) return "O preço e a quantidade do atacado não podem ser negativos.";
        if (precoAtacado == 0 && qtdMinima == 0) return null;
        if (precoAtacado == 0 || qtdMinima == 0)
            return "Para vender no atacado, informe o preço de atacado e a partir de quantas unidades ele vale (ou deixe os dois zerados).";
        if (precoAtacado > Formatos.MaxMoeda || qtdMinima > Formatos.MaxQuantidade) return "Atacado acima do limite permitido.";
        if (precoVenda > 0 && precoAtacado >= precoVenda) return "O preço de atacado precisa ser menor que o preço de venda.";
        return ValidarQuantidade(unidade, qtdMinima);
    }

    /// <summary>UN, CX, PC...: a partir de 2 e inteiro. KG, L, M...: qualquer quantidade maior que zero.</summary>
    public static string? ValidarQuantidade(string? unidade, decimal quantidade)
    {
        if (quantidade <= 0) return "Informe a partir de quantas unidades vale o atacado.";
        if (Formatos.UnidadeFracionada(unidade)) return null;
        if (quantidade != decimal.Truncate(quantidade)) return $"Produto em {unidade}: a quantidade do atacado precisa ser inteira.";
        if (quantidade < 2) return "O atacado precisa valer a partir de 2 unidades ou mais.";
        return null;
    }
}
