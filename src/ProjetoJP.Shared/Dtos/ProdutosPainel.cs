namespace ProjetoJP.Shared.Dtos;

/// <summary>Números do topo da tela Produtos e Estoque.</summary>
public sealed class ResumoProdutosDto
{
    public int Cadastrados { get; set; }
    public int Ativos { get; set; }
    /// <summary>Cadastrados neste mês (produtos antigos, sem data de cadastro, não entram).</summary>
    public int NovosNoMes { get; set; }
    /// <summary>Ativos com estoque mínimo definido e saldo igual ou abaixo dele.</summary>
    public int EstoqueBaixo { get; set; }
    /// <summary>Ativos com saldo zero ou negativo.</summary>
    public int SemEstoque { get; set; }
    /// <summary>Ativos novos ou alterados que ainda não foram publicados para os caixas.</summary>
    public int PendentesCarga { get; set; }
    /// <summary>Vendas concluídas hoje e ontem (só para gerente/admin; null para operador).</summary>
    public decimal? VendasHoje { get; set; }
    public decimal? VendasOntem { get; set; }
}

/// <summary>Um dia do gráfico do produto: quanto vendeu e com quanto de estoque terminou.</summary>
public sealed class SerieProdutoDto
{
    public DateOnly Dia { get; set; }
    public decimal Vendido { get; set; }
    public decimal Estoque { get; set; }
}

/// <summary>Consulta de produtos paginada no PostgreSQL, sem carregar o catálogo completo na Retaguarda.</summary>
public sealed class PaginaProdutosDto
{
    public int Total { get; set; }
    public int Pagina { get; set; }
    public int PorPagina { get; set; }
    public List<ProdutoDto> Itens { get; set; } = [];
}
