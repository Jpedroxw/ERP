namespace ProjetoJP.Shared;

public enum Perfil
{
    Operador = 0,
    Gerente = 1,
    Admin = 2
}

public enum TipoMovimentoEstoque
{
    /// <summary>Soma a quantidade ao saldo.</summary>
    Entrada = 0,
    /// <summary>Subtrai a quantidade do saldo (perda, consumo, devolução a fornecedor).</summary>
    Saida = 1,
    /// <summary>Define o saldo exatamente igual à quantidade informada (inventário).</summary>
    Ajuste = 2,
    /// <summary>Gerado automaticamente pelas vendas do PDV.</summary>
    Venda = 3,
    /// <summary>Gerado automaticamente quando uma venda é cancelada.</summary>
    CancelamentoVenda = 4,
    /// <summary>Devolução de parte dos itens de uma venda já concluída.</summary>
    DevolucaoCliente = 5
}

public enum StatusVenda
{
    Concluida = 0,
    Cancelada = 1
}

public enum StatusCaixa
{
    Aberto = 0,
    Fechado = 1
}

public enum TipoMovimentoCaixa
{
    Sangria = 0,
    Suprimento = 1
}

/// <summary>Situação do orçamento feito no caixa.</summary>
public enum StatusOrcamento
{
    Aberto = 0,
    /// <summary>Virou venda (VendaId preenchido).</summary>
    Convertido = 1,
    Cancelado = 2
}
