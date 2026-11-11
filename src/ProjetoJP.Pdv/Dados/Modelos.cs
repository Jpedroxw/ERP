using ProjetoJP.Shared;

namespace ProjetoJP.Pdv.Dados;

// Linhas do banco local. Ids e datas como texto (veja Db.cs).

public class UsuarioLocal
{
    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";
    public string Login { get; set; } = "";
    public string SenhaHash { get; set; } = "";
    public int Perfil { get; set; }
    public bool Ativo { get; set; }

    /// <summary>Permissões que vieram do servidor ("a,b,c"). null = servidor antigo: vale o padrão do perfil.</summary>
    public string? Permissoes { get; set; }

    public Shared.Perfil PerfilEnum => (Shared.Perfil)Perfil;
    public bool PodeAutorizar => PerfilEnum is Shared.Perfil.Gerente or Shared.Perfil.Admin;

    /// <summary>Pode fazer isso sem pedir senha de gerente? (veja ProjetoJP.Shared.Permissoes).</summary>
    public bool Pode(string permissao) =>
        PerfilEnum == Shared.Perfil.Admin ||
        (Permissoes is null
            ? Shared.Permissoes.Padrao(PerfilEnum).Contains(permissao)
            : Shared.Permissoes.Ler(Permissoes).Contains(permissao));
}

public class ProdutoLocal
{
    public string Id { get; set; } = "";
    public string Codigo { get; set; } = "";
    public string? CodigoBarras { get; set; }
    public string Descricao { get; set; } = "";
    public string Unidade { get; set; } = "UN";
    /// <summary>Preço que vale agora (com promoção, se tiver).</summary>
    public decimal PrecoVenda { get; set; }
    /// <summary>Preço normal publicado (só vem nas buscas da venda; 0 nas outras consultas).</summary>
    public decimal PrecoNormal { get; set; }
    public decimal Estoque { get; set; }
    public bool Ativo { get; set; }
    /// <summary>Atacado fixo do produto (0 = não tem).</summary>
    public decimal PrecoAtacado { get; set; }
    public decimal QtdAtacado { get; set; }
    /// <summary>Promoção de atacado em vigor hoje (a de menor preço), se houver.</summary>
    public decimal? PromoAtacadoPreco { get; set; }
    public decimal? PromoAtacadoMinimo { get; set; }

    public bool EmPromocao => PrecoNormal > PrecoVenda;

    /// <summary>Faixas de atacado que valem hoje: a fixa do produto e a da promoção de atacado.</summary>
    public List<Atacado.Faixa> Faixas
    {
        get
        {
            var lista = new List<Atacado.Faixa>();
            if (PrecoAtacado > 0 && QtdAtacado > 0) lista.Add(new Atacado.Faixa(QtdAtacado, PrecoAtacado));
            if (PromoAtacadoPreco is > 0 && PromoAtacadoMinimo is > 0) lista.Add(new Atacado.Faixa(PromoAtacadoMinimo.Value, PromoAtacadoPreco.Value));
            return lista;
        }
    }
}

public class ClienteLocal
{
    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";
    public string? CpfCnpj { get; set; }
    public string? Telefone { get; set; }
}

public class FormaLocal
{
    public string Id { get; set; } = "";
    public string Descricao { get; set; } = "";
    public bool PermiteTroco { get; set; }
    /// <summary>O valor fica na conta do cliente (fiado): exige cliente na venda.</summary>
    public bool EhFiado { get; set; }
    public int Ordem { get; set; }
    public override string ToString() => Descricao;
}

public class CaixaLocal
{
    public string Id { get; set; } = "";
    public string UsuarioAberturaId { get; set; } = "";
    public string Abertura { get; set; } = "";
    public decimal ValorAbertura { get; set; }
    public string? Fechamento { get; set; }
    public string? UsuarioFechamentoId { get; set; }
    public decimal? ValorFechamento { get; set; }
    public int Status { get; set; }
}

public class VendaLocal
{
    public string Id { get; set; } = "";
    public int Numero { get; set; }
    public string CaixaId { get; set; } = "";
    public string UsuarioId { get; set; } = "";
    public string? ClienteId { get; set; }
    public string Data { get; set; } = "";
    public decimal Subtotal { get; set; }
    public decimal Desconto { get; set; }
    public decimal Total { get; set; }
    public decimal Troco { get; set; }
    public int Status { get; set; }
    public string? CanceladaEm { get; set; }
}

public class VendaItemLocal
{
    public string Id { get; set; } = "";
    public string VendaId { get; set; } = "";
    public string ProdutoId { get; set; } = "";
    public string Descricao { get; set; } = "";
    public decimal Quantidade { get; set; }
    public decimal PrecoUnitario { get; set; }
    public decimal Desconto { get; set; }
    public decimal Total { get; set; }
    /// <summary>Preço normal quando o item saiu em promoção (só em memória, para o cupom da venda; não é gravado).</summary>
    public decimal PrecoNormal { get; set; }
    /// <summary>Saiu pelo preço de atacado (só em memória, para o cupom).</summary>
    public bool Atacado { get; set; }
}

public class VendaPagamentoLocal
{
    public string Id { get; set; } = "";
    public string VendaId { get; set; } = "";
    public string FormaId { get; set; } = "";
    public decimal Valor { get; set; }
}

public class CaixaMovimentoLocal
{
    public string Id { get; set; } = "";
    public string CaixaId { get; set; } = "";
    public int Tipo { get; set; }
    public decimal Valor { get; set; }
    public string Data { get; set; } = "";
    public string? Motivo { get; set; }
    public string UsuarioId { get; set; } = "";
}

/// <summary>Venda completa (cabeçalho + itens + pagamentos), usada para gravar, imprimir e sincronizar.</summary>
public class VendaCompleta
{
    public VendaLocal Venda { get; set; } = new();
    public List<VendaItemLocal> Itens { get; set; } = [];
    public List<VendaPagamentoLocal> Pagamentos { get; set; } = [];
}

public class LinhaResumo
{
    public string Descricao { get; set; } = "";
    public decimal Valor { get; set; }
}

public class ResumoCaixa
{
    public decimal Abertura { get; set; }
    public decimal Suprimentos { get; set; }
    public decimal Sangrias { get; set; }
    public int QuantidadeVendas { get; set; }
    public int QuantidadeCanceladas { get; set; }
    public decimal TotalVendas { get; set; }
    public List<LinhaResumo> PorForma { get; set; } = [];
    /// <summary>Soma das formas que aceitam troco (dinheiro).</summary>
    public decimal VendasDinheiro { get; set; }
    public decimal DinheiroEsperado => Abertura + Suprimentos - Sangrias + VendasDinheiro;
}

/// <summary>Orçamento feito no caixa (cabeçalho).</summary>
public class OrcamentoLocal
{
    public long Revisao { get; set; } = 1;
    public string Id { get; set; } = "";
    public int Numero { get; set; }
    public string UsuarioId { get; set; } = "";
    public string? ClienteId { get; set; }
    public string? ClienteNome { get; set; }
    public string Data { get; set; } = "";
    /// <summary>AAAA-MM-DD.</summary>
    public string? Validade { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Desconto { get; set; }
    public decimal Total { get; set; }
    public string? Observacao { get; set; }
    public int Status { get; set; }
    public string? VendaId { get; set; }
}

public class OrcamentoItemLocal
{
    public string Id { get; set; } = "";
    public string OrcamentoId { get; set; } = "";
    public string ProdutoId { get; set; } = "";
    public string Codigo { get; set; } = "";
    public string Descricao { get; set; } = "";
    public string Unidade { get; set; } = "UN";
    public decimal Quantidade { get; set; }
    public decimal PrecoUnitario { get; set; }
    public decimal Total { get; set; }
}

public class OrcamentoCompleto
{
    public OrcamentoLocal Orcamento { get; set; } = new();
    public List<OrcamentoItemLocal> Itens { get; set; } = [];
}
