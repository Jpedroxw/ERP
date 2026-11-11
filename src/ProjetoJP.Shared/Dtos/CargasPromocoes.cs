using System.ComponentModel;

namespace ProjetoJP.Shared.Dtos;

public sealed class CargaProdutoDto
{
    public Guid Id { get; set; }
    public string Codigo { get; set; } = "";
    public string Descricao { get; set; } = "";
    public bool Ativo { get; set; }
    public bool Publicado { get; set; }
    public bool Pendente { get; set; }
    public string DescricaoPublicada { get; set; } = "";
    public bool AtivoPublicado { get; set; }
    public decimal PrecoPublicado { get; set; }
    public string UnidadePublicada { get; set; } = "UN";
    /// <summary>Atacado fixo que está nos caixas (0 = não tem).</summary>
    public decimal PrecoAtacadoPublicado { get; set; }
    public decimal QtdAtacadoPublicado { get; set; }
    public decimal PrecoEmEdicao { get; set; }
    public DateTime? PublicadoEm { get; set; }
    /// <summary>O que muda no caixa ao publicar (ex.: "preço R$ 5,00 → R$ 5,50 · descrição").</summary>
    public string Mudancas { get; set; } = "";
    public string Situacao => !Publicado ? (Ativo ? "Novo, não publicado" : "Inativo, nunca publicado") : Pendente ? "Alterado, pendente" : "Publicado";
}

public sealed class PublicarProdutosRequest
{
    public List<Guid> Ids { get; set; } = [];
}

/// <summary>Produto que ficou fora da carga e o motivo (ex.: sem preço).</summary>
public sealed class CargaIgnoradaDto
{
    public string Codigo { get; set; } = "";
    public string Descricao { get; set; } = "";
    public string Motivo { get; set; } = "";
}

public sealed class PublicarProdutosResposta
{
    public int Publicados { get; set; }
    public DateTime Data { get; set; }
    /// <summary>Os que não foram publicados: os outros saem normalmente.</summary>
    public List<CargaIgnoradaDto> Ignorados { get; set; } = [];
}

public sealed class CargaUsuarioDto
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = "";
    public string Login { get; set; } = "";
    public string Perfil { get; set; } = "";
    public bool Ativo { get; set; }
    public bool Publicado { get; set; }
    public bool Pendente { get; set; }
    public string Mudancas { get; set; } = "";
    public DateTime? PublicadoEm { get; set; }
    public string Situacao => !Publicado ? (Ativo ? "Novo, não enviado" : "Inativo, nunca enviado") : Pendente ? "Alterado, pendente" : "Enviado";
}

/// <summary>Se cada caixa já baixou a última carga (produtos e promoções).</summary>
public sealed class CargaTerminalDto
{
    [DisplayName("Caixa")] public string Nome { get; set; } = "";
    [DisplayName("Última sincronização")] public DateTime? UltimaSincronizacao { get; set; }
    [DisplayName("Recebeu a última carga")] public string Situacao { get; set; } = "";
    [Browsable(false)] public bool EmDia { get; set; }
}

public sealed class PromocaoCadastroRequest
{
    public Guid ProdutoId { get; set; }
    public string Nome { get; set; } = "";
    public DateOnly Inicio { get; set; }
    public DateOnly Fim { get; set; }
    public decimal PrecoPromocional { get; set; }
    /// <summary>0 = varejo (qualquer quantidade). Maior que 0 = atacado, a partir dessa quantidade.</summary>
    public decimal QuantidadeMinima { get; set; }
}

public sealed class PromocaoDto
{
    public Guid Id { get; set; }
    public Guid ProdutoId { get; set; }
    public string Produto { get; set; } = "";
    public string Nome { get; set; } = "";
    public DateOnly Inicio { get; set; }
    public DateOnly Fim { get; set; }
    /// <summary>Preço normal publicado (para comparar com o promocional).</summary>
    public decimal PrecoNormal { get; set; }
    public decimal PrecoPromocional { get; set; }
    public decimal QuantidadeMinima { get; set; }
    public string Unidade { get; set; } = "UN";
    public bool Ativa { get; set; }
    public DateTime AtualizadoEm { get; set; }
    public string Tipo => QuantidadeMinima > 0 ? $"Atacado (a partir de {ProjetoJP.Shared.Formatos.Quantidade(QuantidadeMinima, Unidade)})" : "Varejo";
    public string Situacao => !Ativa ? "Cancelada" : DateOnly.FromDateTime(DateTime.Today) < Inicio
        ? "Agendada" : DateOnly.FromDateTime(DateTime.Today) > Fim ? "Encerrada" : "Em vigor";
}

public sealed class SyncPromocao
{
    public Guid Id { get; set; }
    public Guid ProdutoId { get; set; }
    public DateOnly Inicio { get; set; }
    public DateOnly Fim { get; set; }
    public decimal PrecoPromocional { get; set; }
    /// <summary>0 = varejo; maior que 0 = atacado a partir dessa quantidade.</summary>
    public decimal QuantidadeMinima { get; set; }
    public bool Ativa { get; set; }
}
