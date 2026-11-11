namespace ProjetoJP.Shared.Dtos;

// Contratos entre o PDV e a API.
// Cadastros descem do servidor (receber); vendas e caixas sobem do PDV (enviar).
// Todas as datas trafegam em UTC.

public class SyncReceberResponse
{
    /// <summary>Hora do servidor no momento da consulta. O PDV devolve esse valor no próximo "desde".</summary>
    public DateTime Servidor { get; set; }
    public string NomeTerminal { get; set; } = "";
    public List<SyncUsuario> Usuarios { get; set; } = [];
    public List<ProdutoDto> Produtos { get; set; } = [];
    public List<SyncPromocao> Promocoes { get; set; } = [];
    public List<ClienteDto> Clientes { get; set; } = [];
    public List<FormaPagamentoDto> Formas { get; set; } = [];
}

public class SyncUsuario
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = "";
    public string Login { get; set; } = "";
    public string SenhaHash { get; set; } = "";
    public Perfil Perfil { get; set; }
    public bool Ativo { get; set; }
    /// <summary>Permissões valendo para este usuário (o caixa confere sem internet). null = servidor antigo (vale o padrão do perfil).</summary>
    public List<string>? Permissoes { get; set; }
}

public class SyncEnviarRequest
{
    public List<CaixaSessaoDto> Caixas { get; set; } = [];
    public List<CaixaMovimentoDto> MovimentosCaixa { get; set; } = [];
    public List<VendaDto> Vendas { get; set; } = [];
    public List<OrcamentoDto> Orcamentos { get; set; } = [];
}

public class SyncEnviarResponse
{
    public List<Guid> CaixasAceitos { get; set; } = [];
    public List<Guid> MovimentosAceitos { get; set; } = [];
    public List<Guid> VendasAceitas { get; set; } = [];
    public List<Guid> OrcamentosAceitos { get; set; } = [];
    public List<string> Avisos { get; set; } = [];
    /// <summary>
    /// Vendas que o servidor recusou (dado inválido, produto ou forma desconhecida...). As outras do lote
    /// são gravadas normalmente: uma venda com problema não trava as demais. Servidor antigo: vem vazio.
    /// </summary>
    public List<VendaRecusadaDto> VendasRecusadas { get; set; } = [];
}

public class VendaRecusadaDto
{
    public Guid Id { get; set; }
    public int Numero { get; set; }
    public string Motivo { get; set; } = "";
}

public class CaixaSessaoDto
{
    public Guid Id { get; set; }
    public Guid UsuarioAberturaId { get; set; }
    public DateTime Abertura { get; set; }
    public decimal ValorAbertura { get; set; }
    public DateTime? Fechamento { get; set; }
    public Guid? UsuarioFechamentoId { get; set; }
    public decimal? ValorFechamentoInformado { get; set; }
    public StatusCaixa Status { get; set; }
}

public class CaixaMovimentoDto
{
    public Guid Id { get; set; }
    public Guid CaixaSessaoId { get; set; }
    public TipoMovimentoCaixa Tipo { get; set; }
    public decimal Valor { get; set; }
    public DateTime Data { get; set; }
    public string? Motivo { get; set; }
    public Guid UsuarioId { get; set; }
}

public class VendaDto
{
    public Guid Id { get; set; }
    public int Numero { get; set; }
    public Guid CaixaSessaoId { get; set; }
    public Guid UsuarioId { get; set; }
    public Guid? ClienteId { get; set; }
    public DateTime Data { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Desconto { get; set; }
    public decimal Total { get; set; }
    public decimal Troco { get; set; }
    public StatusVenda Status { get; set; }
    public DateTime? CanceladaEm { get; set; }
    public List<VendaItemDto> Itens { get; set; } = [];
    public List<VendaPagamentoDto> Pagamentos { get; set; } = [];
}

public class VendaItemDto
{
    public Guid Id { get; set; }
    public Guid ProdutoId { get; set; }
    public string Descricao { get; set; } = "";
    public decimal Quantidade { get; set; }
    public decimal PrecoUnitario { get; set; }
    public decimal Desconto { get; set; }
    public decimal Total { get; set; }
}

/// <summary>Valor líquido recebido na forma (já descontado o troco).</summary>
public class VendaPagamentoDto
{
    public Guid Id { get; set; }
    public Guid FormaPagamentoId { get; set; }
    public decimal Valor { get; set; }
}

/// <summary>Orçamento feito no caixa (sobe na sincronização; a Retaguarda consulta).</summary>
public class OrcamentoDto
{
    public Guid Id { get; set; }
    /// <summary>Revisão monotônica do orçamento neste caixa. Impede reenvio antigo de sobrescrever dados novos.</summary>
    public long Revisao { get; set; } = 1;
    public int Numero { get; set; }
    public string Terminal { get; set; } = "";
    public Guid UsuarioId { get; set; }
    public string Usuario { get; set; } = "";
    public Guid? ClienteId { get; set; }
    public string? ClienteNome { get; set; }
    public DateTime Data { get; set; }
    public DateOnly? Validade { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Desconto { get; set; }
    public decimal Total { get; set; }
    public string? Observacao { get; set; }
    public StatusOrcamento Status { get; set; }
    public Guid? VendaId { get; set; }
    public List<OrcamentoItemDto> Itens { get; set; } = [];
}

public class OrcamentoItemDto
{
    public Guid Id { get; set; }
    public Guid ProdutoId { get; set; }
    public string Codigo { get; set; } = "";
    public string Descricao { get; set; } = "";
    public string Unidade { get; set; } = "UN";
    public decimal Quantidade { get; set; }
    public decimal PrecoUnitario { get; set; }
    public decimal Total { get; set; }
}
