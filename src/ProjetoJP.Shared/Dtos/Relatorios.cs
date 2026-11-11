using System.ComponentModel;

namespace ProjetoJP.Shared.Dtos;

public class VendasPorDiaDto
{
    [DisplayName("Dia")] public DateOnly Dia { get; set; }
    [DisplayName("Vendas"), Campo(TipoCampo.Inteiro)] public int Quantidade { get; set; }
    [DisplayName("Total"), Campo(TipoCampo.Moeda)] public decimal Total { get; set; }
    [DisplayName("Ticket médio"), Campo(TipoCampo.Moeda)] public decimal TicketMedio { get; set; }
}

public class ProdutoMaisVendidoDto
{
    [DisplayName("Código")] public string? Codigo { get; set; }
    [DisplayName("Descrição")] public string Descricao { get; set; } = "";
    [DisplayName("Quantidade"), Campo(TipoCampo.Quantidade)] public decimal Quantidade { get; set; }
    [DisplayName("Total"), Campo(TipoCampo.Moeda)] public decimal Total { get; set; }
    /// <summary>Unidade do produto (UN, KG...) — usada pelo painel online; a Retaguarda não mostra.</summary>
    [Browsable(false)] public string? Unidade { get; set; }
}

/// <summary>Lucro por produto no período: o que entrou (sem descontos e devoluções) menos o custo.</summary>
public class LucroProdutoDto
{
    [DisplayName("Código")] public string? Codigo { get; set; }
    [DisplayName("Descrição")] public string Descricao { get; set; } = "";
    [DisplayName("Quantidade"), Campo(TipoCampo.Quantidade)] public decimal Quantidade { get; set; }
    [DisplayName("Vendido"), Campo(TipoCampo.Moeda)] public decimal Vendido { get; set; }
    [DisplayName("Custo"), Campo(TipoCampo.Moeda)] public decimal Custo { get; set; }
    [DisplayName("Lucro"), Campo(TipoCampo.Moeda)] public decimal Lucro { get; set; }
    /// <summary>Lucro sobre o vendido, em %. Vazio quando o produto não tem custo cadastrado.</summary>
    [DisplayName("Margem %")] public decimal? Margem { get; set; }
    [DisplayName("Observação")] public string? Observacao { get; set; }
    /// <summary>Algum item ficou sem custo (nem na venda, nem no cadastro): o lucro dele aparece maior que o real.</summary>
    [Browsable(false)] public bool SemCusto { get; set; }
    [Browsable(false)] public string? Unidade { get; set; }
}

public class VendasPorFormaDto
{
    [DisplayName("Forma de pagamento")] public string Forma { get; set; } = "";
    [DisplayName("Lançamentos"), Campo(TipoCampo.Inteiro)] public int Quantidade { get; set; }
    [DisplayName("Total"), Campo(TipoCampo.Moeda)] public decimal Total { get; set; }
}

public class EstoqueBaixoDto
{
    [DisplayName("Código")] public string Codigo { get; set; } = "";
    [DisplayName("Descrição")] public string Descricao { get; set; } = "";
    [DisplayName("Unidade")] public string Unidade { get; set; } = "";
    [DisplayName("Estoque atual"), Campo(TipoCampo.Quantidade)] public decimal EstoqueAtual { get; set; }
    [DisplayName("Estoque mínimo"), Campo(TipoCampo.Quantidade)] public decimal EstoqueMinimo { get; set; }
}

/// <summary>Totais do período inteiro (a lista de vendas mostra no máximo as 2.000 mais recentes).</summary>
public class VendasTotaisDto
{
    public int Quantidade { get; set; }
    public int Canceladas { get; set; }
    /// <summary>Soma das vendas concluídas, já sem o que foi devolvido (canceladas não entram).</summary>
    public decimal Total { get; set; }
    /// <summary>Quanto foi devolvido aos clientes nas vendas do período.</summary>
    public decimal Devolvido { get; set; }
    /// <summary>Quantas a lista de vendas mostra no máximo.</summary>
    public int LimiteLista { get; set; }
}

public class VendaResumoDto
{
    [Browsable(false)] public Guid Id { get; set; }
    [DisplayName("Nº"), Campo(TipoCampo.Inteiro)] public int Numero { get; set; }
    [DisplayName("Terminal")] public string Terminal { get; set; } = "";
    [DisplayName("Data")] public DateTime Data { get; set; }
    [DisplayName("Operador")] public string Operador { get; set; } = "";
    [DisplayName("Cliente")] public string? Cliente { get; set; }
    [DisplayName("Total"), Campo(TipoCampo.Moeda)] public decimal Total { get; set; }
    [DisplayName("Devolvido"), Campo(TipoCampo.Moeda)] public decimal Devolvido { get; set; }
    [DisplayName("Situação")] public StatusVenda Status { get; set; }
    /// <summary>Formas de pagamento da venda (ex.: "PIX + Dinheiro").</summary>
    [DisplayName("Pagamento")] public string? Pagamento { get; set; }
}

// ---------- Painel online (consulta pelo celular) ----------

/// <summary>Tudo o que a tela "Início" do painel online mostra, numa consulta só (datas no fuso da loja).</summary>
public class PainelResumoDto
{
    /// <summary>hoje, ontem, 7dias ou mes.</summary>
    public string Periodo { get; set; } = "";
    public DateOnly Hoje { get; set; }
    public DateOnly De { get; set; }
    public DateOnly Ate { get; set; }
    /// <summary>Hora da loja em que o resumo foi montado.</summary>
    public DateTime AtualizadoEm { get; set; }

    /// <summary>Soma das vendas concluídas, já sem o que foi devolvido.</summary>
    public decimal Total { get; set; }
    /// <summary>Vendas concluídas (canceladas não entram).</summary>
    public int Vendas { get; set; }
    public int Canceladas { get; set; }
    public decimal Devolvido { get; set; }
    public decimal TicketMedio { get; set; }

    /// <summary>Total do período de comparação (mesmo dia da semana passada, 7 dias anteriores ou mês passado).</summary>
    public decimal TotalAnterior { get; set; }
    public string ComparadoCom { get; set; } = "";

    /// <summary>Os 7 dias que terminam no fim do período (para o gráfico).</summary>
    public List<PainelDiaDto> Dias { get; set; } = [];
    public List<VendasPorFormaDto> Formas { get; set; } = [];
    public List<PainelCaixaDto> Caixas { get; set; } = [];
}

public class PainelDiaDto
{
    public DateOnly Dia { get; set; }
    public decimal Total { get; set; }
    public int Vendas { get; set; }
}

public class PainelCaixaDto
{
    public string Terminal { get; set; } = "";
    public decimal Total { get; set; }
    public int Vendas { get; set; }
}
