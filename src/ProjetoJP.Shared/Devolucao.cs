namespace ProjetoJP.Shared;

/// <summary>Conta da devolução parcial, a mesma no servidor e na Retaguarda.</summary>
public static class Devolucao
{
    /// <summary>
    /// Quanto vale devolver <paramref name="quantidade"/> do item: o valor do item (já com o desconto dele)
    /// e o rateio do desconto da venda (Total ÷ Subtotal).
    /// </summary>
    public static decimal Valor(decimal totalItem, decimal quantidadeItem, decimal quantidade, decimal subtotalVenda, decimal totalVenda)
    {
        if (quantidadeItem <= 0 || quantidade <= 0) return 0;
        var fator = subtotalVenda > 0 ? totalVenda / subtotalVenda : 1m;
        return Math.Round(totalItem / quantidadeItem * quantidade * fator, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>UN, CX, PC... só devolvem quantidade inteira; KG, L, M... aceitam fração.</summary>
    public static bool QuantidadeValida(string? unidade, decimal quantidade) =>
        quantidade > 0 && (Formatos.UnidadeFracionada(unidade) || quantidade == decimal.Truncate(quantidade));
}
