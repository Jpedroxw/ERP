using ProjetoJP.Shared.Dtos;

namespace ProjetoJP.Shared;

/// <summary>Regras puras e testáveis: a API nunca deve confiar somente nos totais enviados pelo PDV.</summary>
public static class ValidacaoSincronizacao
{
    /// <summary>Valida dados do caixa no servidor: nunca confiar apenas na interface/offline.</summary>
    public static string? ValidarVenda(VendaDto v)
    {
        if (v.Id == Guid.Empty || v.Numero <= 0 || v.CaixaSessaoId == Guid.Empty || v.UsuarioId == Guid.Empty)
            return "identificadores ou número inválidos.";
        if (!Enum.IsDefined(v.Status)) return "situação inválida.";
        if (v.Itens is null || v.Itens.Count is 0 or > 500 || v.Pagamentos is null || v.Pagamentos.Count is 0 or > 30)
            return "itens ou pagamentos ausentes/acima do limite.";
        if (v.Itens.Select(i => i.Id).Distinct().Count() != v.Itens.Count ||
            v.Pagamentos.Select(i => i.Id).Distinct().Count() != v.Pagamentos.Count)
            return "identificadores de itens ou pagamentos repetidos.";
        if (v.Itens.Any(i => i.Id == Guid.Empty || i.ProdutoId == Guid.Empty ||
                i.Quantidade <= 0 || i.Quantidade > Formatos.MaxQuantidade ||
                i.PrecoUnitario < 0 || i.PrecoUnitario > 99_999_999m ||
                i.Desconto < 0 || i.Total < 0 || i.Total > 999_999_999m ||
                i.Descricao is null || i.Descricao.Length > 150 ||
                Math.Abs(Math.Round(i.Quantidade * i.PrecoUnitario - i.Desconto, 2) - i.Total) > 0.01m))
            return "itens com quantidade, desconto, preço ou total inválidos.";
        if (v.Pagamentos.Any(p => p.Id == Guid.Empty || p.FormaPagamentoId == Guid.Empty || p.Valor < 0 || (v.Total > 0 && p.Valor == 0)))
            return "pagamentos inválidos.";
        if (v.Subtotal < 0 || v.Desconto < 0 || v.Total < 0 || v.Troco < 0 ||
            v.Subtotal > 999_999_999m || v.Desconto > v.Subtotal || v.Total > 999_999_999m ||
            v.Pagamentos.Any(x => x.Valor > 999_999_999m) ||
            Math.Abs(v.Itens.Sum(i => i.Total) - v.Subtotal) > 0.01m ||
            Math.Abs(v.Subtotal - v.Desconto - v.Total) > 0.01m ||
            Math.Abs(v.Pagamentos.Sum(p => p.Valor) - v.Total) > 0.01m)
            return "subtotal, desconto, total ou soma dos pagamentos não confere.";
        if (v.Status == StatusVenda.Cancelada && v.CanceladaEm is null)
            return "cancelamento sem data.";
        return null;
    }

    /// <summary>Não aceita totais inconsistentes nem itens inválidos em orçamento offline.</summary>
    public static string? ValidarOrcamento(OrcamentoDto o)
    {
        if (o.Id == Guid.Empty || o.Numero <= 0 || o.UsuarioId == Guid.Empty || o.Revisao < 1 ||
            !Enum.IsDefined(o.Status) ||
            (o.Status == StatusOrcamento.Convertido && o.VendaId is null) ||
            (o.Status != StatusOrcamento.Convertido && o.VendaId is not null))
            return "identificador, situação, revisão ou vínculo com venda inválidos.";
        if (o.Itens is null || o.Itens.Count is 0 or > 500 ||
            o.Itens.Select(x => x.Id).Distinct().Count() != o.Itens.Count)
            return "itens ausentes, duplicados ou acima do limite.";
        if (o.Itens.Any(i => i.Id == Guid.Empty || i.ProdutoId == Guid.Empty ||
            i.Quantidade <= 0 || i.Quantidade > Formatos.MaxQuantidade ||
            i.PrecoUnitario < 0 || i.PrecoUnitario > 99_999_999m ||
            i.Total < 0 || i.Total > 999_999_999m ||
            Math.Abs(Math.Round(i.Quantidade * i.PrecoUnitario, 2) - i.Total) > 0.01m))
            return "item com preço, quantidade ou total inválidos.";
        if (o.Subtotal < 0 || o.Subtotal > 999_999_999m || o.Desconto < 0 ||
            o.Desconto > o.Subtotal || o.Total < 0 || o.Total > 999_999_999m ||
            Math.Abs(o.Subtotal - o.Itens.Sum(i => i.Total)) > 0.01m ||
            Math.Abs(o.Total - o.Subtotal + o.Desconto) > 0.01m)
            return "subtotal, desconto ou total não confere.";
        return null;
    }

    /// <summary>Um orçamento finalizado é imutável. Uma revisão menor nunca sobrescreve uma maior.</summary>
    public static bool PermiteAtualizarOrcamento(long revisaoAtual, StatusOrcamento estadoAtual,
        long revisaoNova, StatusOrcamento estadoNovo) =>
        revisaoNova > revisaoAtual && estadoAtual == StatusOrcamento.Aberto &&
        Enum.IsDefined(estadoNovo);
}
