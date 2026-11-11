using ProjetoJP.Shared;

namespace ProjetoJP.Api.Dados;

// Todas as chaves são Guid: o PDV cria vendas e caixas offline com o próprio Id,
// e o servidor só aceita (sem conflito de numeração).
// AtualizadoEm é preenchido automaticamente no SaveChanges e é o que o PDV usa
// para baixar só o que mudou.

public abstract class Entidade
{
    public Guid Id { get; set; }
    public DateTime AtualizadoEm { get; set; }
}

public interface IAtivavel
{
    bool Ativo { get; set; }
}

public class Usuario : Entidade, IAtivavel
{
    public string Nome { get; set; } = "";
    public string Login { get; set; } = "";
    public string SenhaHash { get; set; } = "";
    public Perfil Perfil { get; set; }
    public bool Ativo { get; set; } = true;
    /// <summary>Permissões marcadas pelo Admin ("a,b,c"). null = padrão do perfil (veja ProjetoJP.Shared.Permissoes).</summary>
    public string? Permissoes { get; set; }
}

/// <summary>Cópia enviada aos caixas. Alterar o usuário na Retaguarda não muda o caixa até publicar de novo.</summary>
public class UsuarioPublicado
{
    public Guid Id { get; set; }
    public Usuario Usuario { get; set; } = null!;
    public string Nome { get; set; } = "";
    public string Login { get; set; } = "";
    public string SenhaHash { get; set; } = "";
    public Perfil Perfil { get; set; }
    public bool Ativo { get; set; }
    public string? Permissoes { get; set; }
    public DateTime PublicadoEm { get; set; }
}

public class Categoria : Entidade, IAtivavel
{
    public string Nome { get; set; } = "";
    public bool Ativo { get; set; } = true;
}

public class Produto : Entidade, IAtivavel
{
    public string Codigo { get; set; } = "";
    public string? CodigoBarras { get; set; }
    public string Descricao { get; set; } = "";
    public string Unidade { get; set; } = "UN";
    public Guid? CategoriaId { get; set; }
    public Categoria? Categoria { get; set; }
    public decimal PrecoCusto { get; set; }
    public decimal PrecoVenda { get; set; }
    /// <summary>Preço de atacado (0 = não vende no atacado). Vale a partir de QtdMinimaAtacado no mesmo cupom.</summary>
    public decimal PrecoAtacado { get; set; }
    public decimal QtdMinimaAtacado { get; set; }
    public decimal EstoqueAtual { get; set; }
    public decimal EstoqueMinimo { get; set; }
    public bool Ativo { get; set; } = true;
    /// <summary>Quando foi cadastrado (null = produto anterior a este campo). Todo produto novo nasce com a hora atual.</summary>
    public DateTime? CriadoEm { get; set; } = DateTime.UtcNow;
    /// <summary>Controle de concorrência (coluna xmin do PostgreSQL): dois caixas baixando o mesmo produto ao mesmo tempo não perdem saldo.</summary>
    public uint Versao { get; set; }
}

/// <summary>Fornecedor (de quem a loja compra).</summary>
public class Fornecedor : Entidade, IAtivavel
{
    public string Nome { get; set; } = "";
    public string? CpfCnpj { get; set; }
    public string? Contato { get; set; }
    public string? Telefone { get; set; }
    public string? Email { get; set; }
    public string? Cidade { get; set; }
    public string? Uf { get; set; }
    public string? Observacao { get; set; }
    public bool Ativo { get; set; } = true;
}

/// <summary>Snapshot published to PDVs; draft edits never change what a PDV sees.</summary>
public class ProdutoPublicado
{
    public Guid Id { get; set; } // same Id as Produto
    public Produto Produto { get; set; } = null!;
    public string Codigo { get; set; } = "";
    public string? CodigoBarras { get; set; }
    public string Descricao { get; set; } = "";
    public string Unidade { get; set; } = "UN";
    public decimal PrecoVenda { get; set; }
    public decimal PrecoAtacado { get; set; }
    public decimal QtdMinimaAtacado { get; set; }
    public bool Ativo { get; set; }
    public DateTime PublicadoEm { get; set; }
}

/// <summary>One-time bootstrap marker so drafts do NOT become published on restart.</summary>
public class ControlePublicacao
{
    public int Id { get; set; }
    public DateTime InicializadoEm { get; set; }
}

/// <summary>Promotion is sent in advance to PDVs, activated by local business date.</summary>
public class Promocao : Entidade
{
    public Guid ProdutoId { get; set; }
    public Produto Produto { get; set; } = null!;
    public string Nome { get; set; } = "";
    public DateOnly Inicio { get; set; }
    public DateOnly Fim { get; set; }
    public decimal PrecoPromocional { get; set; }
    /// <summary>0 = promoção de varejo (qualquer quantidade). Maior que 0 = atacado: vale a partir dessa quantidade.</summary>
    public decimal QuantidadeMinima { get; set; }
    public bool Ativa { get; set; } = true;
}

public class MovimentoEstoque : Entidade
{
    public Guid ProdutoId { get; set; }
    public Produto? Produto { get; set; }
    public DateTime Data { get; set; }
    public TipoMovimentoEstoque Tipo { get; set; }
    /// <summary>Variação no saldo (positiva ou negativa).</summary>
    public decimal Quantidade { get; set; }
    public decimal SaldoApos { get; set; }
    public string? Observacao { get; set; }
    public Guid? UsuarioId { get; set; }
    public Guid? VendaId { get; set; }
}

public class Cliente : Entidade, IAtivavel
{
    public string Nome { get; set; } = "";
    public string? CpfCnpj { get; set; }
    public string? Telefone { get; set; }
    public string? Email { get; set; }
    public string? Endereco { get; set; }
    public string? Observacao { get; set; }
    public bool Ativo { get; set; } = true;
    /// <summary>Quanto o cliente ainda deve (fiado). Sobe na venda, desce no recebimento.</summary>
    public decimal SaldoFiado { get; set; }
}

public class FormaPagamento : Entidade, IAtivavel
{
    public string Descricao { get; set; } = "";
    public bool PermiteTroco { get; set; }
    /// <summary>O valor desta forma entra no saldo do cliente, em vez de no caixa.</summary>
    public bool EhFiado { get; set; }
    public int Ordem { get; set; }
    public bool Ativo { get; set; } = true;
}

public class Terminal : Entidade, IAtivavel
{
    public string Nome { get; set; } = "";
    public string Chave { get; set; } = "";
    public DateTime? UltimaSincronizacao { get; set; }
    public bool Ativo { get; set; } = true;
}

public class CaixaSessao : Entidade
{
    public Guid TerminalId { get; set; }
    public Guid UsuarioAberturaId { get; set; }
    public DateTime Abertura { get; set; }
    public decimal ValorAbertura { get; set; }
    public DateTime? Fechamento { get; set; }
    public Guid? UsuarioFechamentoId { get; set; }
    public decimal? ValorFechamentoInformado { get; set; }
    public StatusCaixa Status { get; set; }
}

public class CaixaMovimento : Entidade
{
    public Guid CaixaSessaoId { get; set; }
    public TipoMovimentoCaixa Tipo { get; set; }
    public decimal Valor { get; set; }
    public DateTime Data { get; set; }
    public string? Motivo { get; set; }
    public Guid UsuarioId { get; set; }
}

public class Venda : Entidade
{
    public int Numero { get; set; }
    public Guid TerminalId { get; set; }
    public Guid CaixaSessaoId { get; set; }
    public Guid UsuarioId { get; set; }
    public Guid? ClienteId { get; set; }
    public DateTime Data { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Desconto { get; set; }
    public decimal Total { get; set; }
    public decimal Troco { get; set; }
    /// <summary>Quanto já foi devolvido ao cliente (devolução parcial). Relatórios mostram Total − ValorDevolvido.</summary>
    public decimal ValorDevolvido { get; set; }
    public StatusVenda Status { get; set; }
    public DateTime? CanceladaEm { get; set; }
    public List<VendaItem> Itens { get; set; } = [];
    public List<VendaPagamento> Pagamentos { get; set; } = [];
}

public class VendaItem
{
    public Guid Id { get; set; }
    public Guid VendaId { get; set; }
    public Venda? Venda { get; set; }
    public Guid ProdutoId { get; set; }
    public string Descricao { get; set; } = "";
    public decimal Quantidade { get; set; }
    public decimal PrecoUnitario { get; set; }
    public decimal Desconto { get; set; }
    public decimal Total { get; set; }
    /// <summary>Custo do produto no momento em que a venda chegou ao servidor.</summary>
    public decimal CustoUnitario { get; set; }
    /// <summary>Quantidade já devolvida deste item. Não passa da quantidade vendida.</summary>
    public decimal QuantidadeDevolvida { get; set; }
}

public class VendaPagamento
{
    public Guid Id { get; set; }
    public Guid VendaId { get; set; }
    public Venda? Venda { get; set; }
    public Guid FormaPagamentoId { get; set; }
    public decimal Valor { get; set; }
}

public class LancamentoFiado : Entidade
{
    public Guid ClienteId { get; set; }
    public Cliente? Cliente { get; set; }
    public Guid? VendaId { get; set; }
    /// <summary>Positivo aumenta a dívida. Negativo é recebimento ou cancelamento.</summary>
    public decimal Valor { get; set; }
    public string? Observacao { get; set; }
    public Guid? UsuarioId { get; set; }
}

/// <summary>Entrada de produtos (compra, nota do fornecedor): vários itens lançados de uma vez.</summary>
public class Entrada : Entidade
{
    public int Numero { get; set; }
    public DateTime Data { get; set; }
    public string? Fornecedor { get; set; }
    public string? Documento { get; set; }
    public string? Observacao { get; set; }
    public decimal Total { get; set; }
    public Guid? UsuarioId { get; set; }
    public bool Cancelada { get; set; }
    public DateTime? CanceladaEm { get; set; }
    public List<EntradaItem> Itens { get; set; } = [];
}

public class EntradaItem : Entidade
{
    public Guid EntradaId { get; set; }
    public Entrada? Entrada { get; set; }
    public Guid ProdutoId { get; set; }
    public Produto? Produto { get; set; }
    public decimal Quantidade { get; set; }
    public decimal CustoUnitario { get; set; }
    public decimal Total { get; set; }
}

/// <summary>
/// Computador lembrado ("Lembrar este computador"): guarda só a chave PÚBLICA. A privada fica no computador
/// (no TPM, quando tem) e nunca vem para ca. Vence em ate 7 dias; trocar a senha do usuario invalida.
/// </summary>
public class DispositivoConfiavel : Entidade
{
    public Guid UsuarioId { get; set; }
    public string Nome { get; set; } = "";
    public string ChavePublica { get; set; } = "";
    public bool Tpm { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime ExpiraEm { get; set; }
    public DateTime? UltimoUsoEm { get; set; }
    /// <summary>Maior carimbo já aceito: um pedido igual ou mais antigo é recusado (não dá para repetir).</summary>
    public long UltimoCarimbo { get; set; }
    /// <summary>Resumo da senha do usuário na hora de lembrar: se a senha mudar, este computador deixa de valer.</summary>
    public string SenhaCarimbo { get; set; } = "";
    public bool Revogado { get; set; }
    /// <summary>Hash do PIN (computador sem TPM). Null = sem PIN.</summary>
    public string? PinHash { get; set; }
    /// <summary>PINs errados seguidos; no 5º o computador é esquecido.</summary>
    public int ErrosPin { get; set; }
}

/// <summary>Registro de operação administrativa (sem senhas, hashes ou chaves).</summary>
public class AuditoriaAdministrativa
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Data { get; set; } = DateTime.UtcNow;
    public Guid? UsuarioId { get; set; }
    public string Acao { get; set; } = "";
    public string AlvoTipo { get; set; } = "";
    public Guid? AlvoId { get; set; }
    public string Resumo { get; set; } = "";
}

/// <summary>Identifica um JWT para permitir revogação de sessão individual.</summary>
public class SessaoAcesso
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UsuarioId { get; set; }
    public Guid? DispositivoId { get; set; }
    public DateTime CriadaEm { get; set; } = DateTime.UtcNow;
    public DateTime ExpiraEm { get; set; }
    public DateTime? RevogadaEm { get; set; }
}

/// <summary>Orçamento feito num caixa (Id criado no caixa, como as vendas: reenviar não duplica).</summary>
public class Orcamento : Entidade
{
    public long Revisao { get; set; } = 1;
    public int Numero { get; set; }
    public Guid TerminalId { get; set; }
    public Guid UsuarioId { get; set; }
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
    public List<OrcamentoItem> Itens { get; set; } = [];
}

public class OrcamentoItem
{
    public Guid Id { get; set; }
    public Guid OrcamentoId { get; set; }
    public Orcamento? Orcamento { get; set; }
    public Guid ProdutoId { get; set; }
    public string Codigo { get; set; } = "";
    public string Descricao { get; set; } = "";
    public string Unidade { get; set; } = "UN";
    public decimal Quantidade { get; set; }
    public decimal PrecoUnitario { get; set; }
    public decimal Total { get; set; }
}

/// <summary>Saída de produtos (perda, vencido, uso interno, devolução...): vários itens lançados de uma vez.</summary>
public class Saida : Entidade
{
    public int Numero { get; set; }
    public DateTime Data { get; set; }
    public string Motivo { get; set; } = "";
    public string? Destino { get; set; }
    public string? Documento { get; set; }
    public string? Observacao { get; set; }
    /// <summary>Valor de custo do que saiu.</summary>
    public decimal Total { get; set; }
    public Guid? UsuarioId { get; set; }
    public bool Cancelada { get; set; }
    public DateTime? CanceladaEm { get; set; }
    public List<SaidaItem> Itens { get; set; } = [];
}

public class SaidaItem : Entidade
{
    public Guid SaidaId { get; set; }
    public Saida? Saida { get; set; }
    public Guid ProdutoId { get; set; }
    public Produto? Produto { get; set; }
    public decimal Quantidade { get; set; }
    public decimal CustoUnitario { get; set; }
    public decimal Total { get; set; }
}
