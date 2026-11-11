namespace ProjetoJP.Shared.Dtos;

/// <summary>Motivos sugeridos na tela de saída (o servidor aceita outro texto curto).</summary>
public static class MotivosSaida
{
    public static readonly string[] Todos =
    [
        "Perda / quebra", "Produto vencido", "Uso / consumo interno", "Devolução ao fornecedor",
        "Doação", "Transferência para outra loja", "Brinde / degustação", "Outro"
    ];
}

/// <summary>Um produto da saída: quanto saiu.</summary>
public class SaidaItemRequest
{
    public Guid ProdutoId { get; set; }
    public decimal Quantidade { get; set; }
}

/// <summary>Saída de produtos com vários itens (perda, vencido, uso interno, devolução...).</summary>
public class SaidaRequest
{
    public string Motivo { get; set; } = "";
    /// <summary>Para quem / para onde (fornecedor da devolução, loja da transferência...).</summary>
    public string? Destino { get; set; }
    public string? Documento { get; set; }
    public string? Observacao { get; set; }
    /// <summary>Deixa o estoque ficar negativo (quando o saldo do sistema está errado).</summary>
    public bool PermitirNegativo { get; set; }
    /// <summary>Obrigatória quando permite saldo negativo; registrada na saída.</summary>
    public string? JustificativaNegativo { get; set; }
    public List<SaidaItemRequest> Itens { get; set; } = [];
}

public class SaidaItemDto
{
    public Guid ProdutoId { get; set; }
    public string Codigo { get; set; } = "";
    public string Descricao { get; set; } = "";
    public string Unidade { get; set; } = "";
    public decimal Quantidade { get; set; }
    /// <summary>Preço de custo do produto na hora da saída (para saber quanto se perdeu).</summary>
    public decimal CustoUnitario { get; set; }
    public decimal Total { get; set; }
}

public class SaidaDto
{
    public Guid Id { get; set; }
    public int Numero { get; set; }
    public DateTime Data { get; set; }
    public string Motivo { get; set; } = "";
    public string? Destino { get; set; }
    public string? Documento { get; set; }
    public string? Observacao { get; set; }
    public int QuantidadeItens { get; set; }
    /// <summary>Valor de custo do que saiu.</summary>
    public decimal Total { get; set; }
    public string? Usuario { get; set; }
    public bool Cancelada { get; set; }
    public DateTime? CanceladaEm { get; set; }
    public List<SaidaItemDto> Itens { get; set; } = [];
}
