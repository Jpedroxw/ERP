namespace ProjetoJP.Shared.Dtos;

// Importação de dados vindos de outro sistema (banco de dados ou planilha).
// A Retaguarda lê a origem, converte cada linha para estes objetos e manda em lotes.

public class ProdutoImportado
{
    /// <summary>Número da linha na origem (só para mostrar nos erros).</summary>
    public int Linha { get; set; }
    public string? Codigo { get; set; }
    public string? CodigoBarras { get; set; }
    public string? Descricao { get; set; }
    public string? Unidade { get; set; }
    /// <summary>Nome da categoria/grupo. Se não existir, é criada.</summary>
    public string? Categoria { get; set; }
    public decimal? PrecoCusto { get; set; }
    public decimal? PrecoVenda { get; set; }
    public decimal? Estoque { get; set; }
    public decimal? EstoqueMinimo { get; set; }
    public bool? Ativo { get; set; }
}

public class ClienteImportado
{
    public int Linha { get; set; }
    public string? Nome { get; set; }
    public string? CpfCnpj { get; set; }
    public string? Telefone { get; set; }
    public string? Email { get; set; }
    public string? Endereco { get; set; }
    public string? Observacao { get; set; }
    public bool? Ativo { get; set; }
}

public class ImportacaoRequest<T>
{
    public List<T> Itens { get; set; } = [];
    /// <summary>Atualiza os que já existem (mesmo código / código de barras / CPF-CNPJ). Senão, pula.</summary>
    public bool AtualizarExistentes { get; set; } = true;
    /// <summary>Produtos: grava o saldo de estoque da origem (ajuste de inventário).</summary>
    public bool ImportarEstoque { get; set; } = true;
    /// <summary>Só confere, sem gravar nada (para o botão "Testar").</summary>
    public bool Simular { get; set; }
    /// <summary>Produtos: já publica nos caixas o que foi importado (senão ficam pendentes em Cargas para caixas).</summary>
    public bool PublicarNoCaixa { get; set; } = true;
}

public class ImportacaoResultado
{
    public int Inseridos { get; set; }
    public int Atualizados { get; set; }
    public int Ignorados { get; set; }
    public int CategoriasCriadas { get; set; }
    /// <summary>Produtos já liberados para os caixas (PublicarNoCaixa).</summary>
    public int Publicados { get; set; }
    /// <summary>Linhas com problema (no máximo 200 por lote).</summary>
    public List<string> Avisos { get; set; } = [];
}
