namespace ProjetoJP.Shared.Dtos;

/// <summary>Um produto da entrada: quanto chegou e quanto custou cada unidade.</summary>
public class EntradaItemRequest
{
    public Guid ProdutoId { get; set; }
    public decimal Quantidade { get; set; }
    public decimal CustoUnitario { get; set; }
}

/// <summary>Entrada de produtos (compra / nota do fornecedor) com vários itens de uma vez.</summary>
public class EntradaRequest
{
    public string? Fornecedor { get; set; }
    /// <summary>Número da nota ou do documento do fornecedor (só para consulta).</summary>
    public string? Documento { get; set; }
    public string? Observacao { get; set; }
    /// <summary>Grava o custo informado como novo preço de custo de cada produto.</summary>
    public bool AtualizarCusto { get; set; } = true;
    public List<EntradaItemRequest> Itens { get; set; } = [];
}

public class EntradaItemDto
{
    public Guid ProdutoId { get; set; }
    public string Codigo { get; set; } = "";
    public string Descricao { get; set; } = "";
    public string Unidade { get; set; } = "";
    public decimal Quantidade { get; set; }
    public decimal CustoUnitario { get; set; }
    public decimal Total { get; set; }
}

public class EntradaDto
{
    public Guid Id { get; set; }
    public int Numero { get; set; }
    public DateTime Data { get; set; }
    public string? Fornecedor { get; set; }
    public string? Documento { get; set; }
    public string? Observacao { get; set; }
    public int QuantidadeItens { get; set; }
    public decimal Total { get; set; }
    public string? Usuario { get; set; }
    public bool Cancelada { get; set; }
    public DateTime? CanceladaEm { get; set; }
    /// <summary>Preenchido só ao abrir uma entrada (GET por id) ou logo depois de lançar.</summary>
    public List<EntradaItemDto> Itens { get; set; } = [];
}
