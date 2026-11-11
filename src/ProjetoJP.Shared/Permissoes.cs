namespace ProjetoJP.Shared;

/// <summary>
/// O que cada usuário pode ver e fazer, na Retaguarda e no caixa.
/// Admin pode tudo (não dá para tirar). Gerente e Operador começam com o padrão do perfil e o Admin
/// marca ou desmarca cada item por usuário. No caixa, "pode" = faz sem pedir senha de gerente;
/// quem não pode precisa da autorização de um usuário que tenha a mesma permissão.
/// </summary>
public static class Permissoes
{
    public sealed record Item(string Chave, string Grupo, string Descricao);

    // ----- Retaguarda -----
    public const string VerCusto = "ver_custo";
    public const string VerVendas = "ver_vendas";
    public const string EditarProdutos = "editar_produtos";
    public const string MovimentarEstoque = "movimentar_estoque";
    public const string PermitirEstoqueNegativo = "permitir_estoque_negativo";
    public const string EntradaProdutos = "entrada_produtos";
    public const string PublicarCargas = "publicar_cargas";
    public const string Promocoes = "promocoes";
    public const string EditarClientes = "editar_clientes";
    public const string Fornecedores = "fornecedores";
    public const string VerOrcamentos = "ver_orcamentos";
    public const string ImportarDados = "importar";

    // ----- Caixa -----
    public const string CaixaAlterarPreco = "caixa_alterar_preco";
    public const string CaixaDesconto = "caixa_desconto";
    public const string CaixaRemoverItem = "caixa_remover_item";
    public const string CaixaCancelarVenda = "caixa_cancelar_venda";
    public const string CaixaSangria = "caixa_sangria";
    public const string CaixaOrcamento = "caixa_orcamento";
    public const string CaixaOpcoes = "caixa_opcoes";

    public const string GrupoRetaguarda = "Retaguarda";
    public const string GrupoCaixa = "Caixa";

    public static readonly IReadOnlyList<Item> Todas =
    [
        new(VerCusto, GrupoRetaguarda, "Ver preço de custo dos produtos"),
        new(VerVendas, GrupoRetaguarda, "Ver valores de vendas (painel, resumo e relatórios)"),
        new(EditarProdutos, GrupoRetaguarda, "Cadastrar e alterar produtos, preços e categorias"),
        new(MovimentarEstoque, GrupoRetaguarda, "Lançar saídas e ajustes de estoque"),
        new(PermitirEstoqueNegativo, GrupoRetaguarda, "Autorizar saída que deixa estoque negativo"),
        new(EntradaProdutos, GrupoRetaguarda, "Lançar entradas de produtos (compras)"),
        new(PublicarCargas, GrupoRetaguarda, "Publicar cargas para os caixas"),
        new(Promocoes, GrupoRetaguarda, "Criar e cancelar promoções"),
        new(EditarClientes, GrupoRetaguarda, "Cadastrar e alterar clientes"),
        new(Fornecedores, GrupoRetaguarda, "Ver e cadastrar fornecedores"),
        new(VerOrcamentos, GrupoRetaguarda, "Ver orçamentos feitos nos caixas"),
        new(ImportarDados, GrupoRetaguarda, "Importar dados de outro sistema"),
        new(CaixaAlterarPreco, GrupoCaixa, "Alterar o preço de um item na venda"),
        new(CaixaDesconto, GrupoCaixa, "Dar desconto na venda"),
        new(CaixaRemoverItem, GrupoCaixa, "Remover item da venda"),
        new(CaixaCancelarVenda, GrupoCaixa, "Cancelar venda já finalizada"),
        new(CaixaSangria, GrupoCaixa, "Fazer sangria"),
        new(CaixaOrcamento, GrupoCaixa, "Fazer orçamentos"),
        new(CaixaOpcoes, GrupoCaixa, "Abrir as opções do caixa")
    ];

    private static readonly HashSet<string> Conhecidas = Todas.Select(i => i.Chave).ToHashSet(StringComparer.Ordinal);

    /// <summary>Padrão restrito para operadores novos e para contas sem personalização. Permissões salvas não mudam.</summary>
    public static IReadOnlyCollection<string> Padrao(Perfil perfil) => perfil switch
    {
        Perfil.Admin or Perfil.Gerente => Conhecidas,
        _ => new[] { EditarClientes, CaixaRemoverItem, CaixaOrcamento }
    };

    /// <summary>Permissões valendo: Admin = todas; sem personalização (null) = padrão do perfil.</summary>
    public static HashSet<string> Efetivas(Perfil perfil, string? salvas)
    {
        if (perfil == Perfil.Admin) return new HashSet<string>(Conhecidas, StringComparer.Ordinal);
        if (salvas is null) return new HashSet<string>(Padrao(perfil), StringComparer.Ordinal);
        return Ler(salvas);
    }

    /// <summary>Texto gravado ("a,b,c") → conjunto, ignorando chaves desconhecidas.</summary>
    public static HashSet<string> Ler(string? texto) =>
        (texto ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(Conhecidas.Contains).ToHashSet(StringComparer.Ordinal);

    /// <summary>Conjunto → texto gravado, só com chaves conhecidas, em ordem fixa.</summary>
    public static string Gravar(IEnumerable<string> chaves)
    {
        var marcadas = chaves.ToHashSet(StringComparer.Ordinal);
        return string.Join(",", Todas.Select(i => i.Chave).Where(marcadas.Contains));
    }

    public static string Descricao(string chave) => Todas.FirstOrDefault(i => i.Chave == chave)?.Descricao ?? chave;
}
