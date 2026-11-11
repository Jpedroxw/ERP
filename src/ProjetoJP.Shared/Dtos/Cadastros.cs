using System.ComponentModel;

namespace ProjetoJP.Shared.Dtos;

public class LoginRequest
{
    public string Login { get; set; } = "";
    public string Senha { get; set; } = "";
}

public class LoginResponse
{
    public string Token { get; set; } = "";
    public Guid UsuarioId { get; set; }
    public string Nome { get; set; } = "";
    public Perfil Perfil { get; set; }
    public DateTime ExpiraEm { get; set; }
    /// <summary>O que este usuário pode ver e fazer (veja <see cref="ProjetoJP.Shared.Permissoes"/>).</summary>
    public List<string> Permissoes { get; set; } = [];
}

public class ErroResponse
{
    public string Erro { get; set; } = "";
}

/// <summary>Item genérico para listas de escolha (combos).</summary>
public class LookupItem
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = "";
    public override string ToString() => Nome;
}

public class CategoriaDto
{
    [Browsable(false)] public Guid Id { get; set; }

    [DisplayName("Nome"), Campo(TipoCampo.Texto, Tamanho = 80, Obrigatorio = true)]
    public string Nome { get; set; } = "";

    [DisplayName("Ativo"), Detalhe] public bool Ativo { get; set; } = true;
}

public class ProdutoDto
{
    [Browsable(false)] public Guid Id { get; set; }

    [DisplayName("Código"), Campo(TipoCampo.CodigoProduto, Tamanho = 30, Dica = "em branco = gera automático")]
    public string Codigo { get; set; } = "";

    [DisplayName("Código de barras"), Detalhe, Campo(TipoCampo.CodigoBarras, Dica = "EAN/GTIN de 8, 12, 13 ou 14 dígitos")]
    public string? CodigoBarras { get; set; }

    [DisplayName("Descrição"), Campo(TipoCampo.Maiusculas, Tamanho = 150, Obrigatorio = true)]
    public string Descricao { get; set; } = "";

    [DisplayName("Unidade"), Campo(TipoCampo.Unidade, Dica = "KG, G, L, ML, M, M2 aceitam fração")]
    public string Unidade { get; set; } = "UN";

    [DisplayName("Categoria"), Lookup("api/categorias")]
    public Guid? CategoriaId { get; set; }

    [DisplayName("Categoria"), SomenteGrade, Detalhe]
    public string? CategoriaNome { get; set; }

    [DisplayName("Preço de custo"), Detalhe, Campo(TipoCampo.Moeda)]
    public decimal PrecoCusto { get; set; }

    [DisplayName("Preço de venda"), Campo(TipoCampo.Moeda, Dica = "precisa de preço para ir ao caixa")]
    public decimal PrecoVenda { get; set; }

    [DisplayName("Preço de atacado"), Detalhe, Campo(TipoCampo.Moeda, Dica = "R$ 0,00 = não vende no atacado")]
    public decimal PrecoAtacado { get; set; }

    [DisplayName("Atacado a partir de"), Detalhe, Campo(TipoCampo.Quantidade, Dica = "quantidade no mesmo cupom")]
    public decimal QtdMinimaAtacado { get; set; }

    [DisplayName("Estoque atual"), ReadOnly(true), Campo(TipoCampo.Quantidade, Dica = "muda pelo menu Estoque")]
    public decimal EstoqueAtual { get; set; }

    [DisplayName("Estoque mínimo"), Detalhe, Campo(TipoCampo.Quantidade)]
    public decimal EstoqueMinimo { get; set; }

    [DisplayName("Ativo"), Detalhe] public bool Ativo { get; set; } = true;
}

public class ClienteDto
{
    [Browsable(false)] public Guid Id { get; set; }

    [DisplayName("Nome"), Campo(TipoCampo.Texto, Tamanho = 150, Obrigatorio = true)]
    public string Nome { get; set; } = "";

    [DisplayName("CPF/CNPJ"), Campo(TipoCampo.CpfCnpj)]
    public string? CpfCnpj { get; set; }

    [DisplayName("Telefone"), Campo(TipoCampo.Telefone)]
    public string? Telefone { get; set; }

    [DisplayName("E-mail"), Detalhe, Campo(TipoCampo.Email, Tamanho = 120)]
    public string? Email { get; set; }

    [DisplayName("Endereço"), Detalhe, Campo(TipoCampo.Texto, Tamanho = 200)]
    public string? Endereco { get; set; }

    [DisplayName("Observação"), Multilinha, Detalhe, Campo(TipoCampo.Texto, Tamanho = 500)]
    public string? Observacao { get; set; }

    [DisplayName("Ativo"), Detalhe] public bool Ativo { get; set; } = true;
}

public class FornecedorDto
{
    [Browsable(false)] public Guid Id { get; set; }

    [DisplayName("Nome / razão social"), Campo(TipoCampo.Texto, Tamanho = 150, Obrigatorio = true)]
    public string Nome { get; set; } = "";

    [DisplayName("CPF/CNPJ"), Campo(TipoCampo.CpfCnpj)]
    public string? CpfCnpj { get; set; }

    [DisplayName("Contato"), Campo(TipoCampo.Texto, Tamanho = 100, Dica = "nome do vendedor ou representante")]
    public string? Contato { get; set; }

    [DisplayName("Telefone"), Campo(TipoCampo.Telefone)]
    public string? Telefone { get; set; }

    [DisplayName("E-mail"), Detalhe, Campo(TipoCampo.Email, Tamanho = 120)]
    public string? Email { get; set; }

    [DisplayName("Cidade"), Campo(TipoCampo.Texto, Tamanho = 80)]
    public string? Cidade { get; set; }

    [DisplayName("UF"), Campo(TipoCampo.Maiusculas, Tamanho = 2, Dica = "SP, RJ, MG...")]
    public string? Uf { get; set; }

    [DisplayName("Observação"), Multilinha, Detalhe, Campo(TipoCampo.Texto, Tamanho = 500)]
    public string? Observacao { get; set; }

    [DisplayName("Ativo"), Detalhe] public bool Ativo { get; set; } = true;
}

public class FormaPagamentoDto
{
    [Browsable(false)] public Guid Id { get; set; }

    [DisplayName("Descrição"), Campo(TipoCampo.Texto, Tamanho = 40, Obrigatorio = true)]
    public string Descricao { get; set; } = "";

    [DisplayName("Permite troco")] public bool PermiteTroco { get; set; }

    [DisplayName("É fiado"), Detalhe] public bool EhFiado { get; set; }

    [DisplayName("Ordem no caixa"), Campo(TipoCampo.Inteiro, Dica = "1 = primeira da lista")]
    public int Ordem { get; set; }

    [DisplayName("Ativo"), Detalhe] public bool Ativo { get; set; } = true;
}

public class UsuarioDto
{
    [Browsable(false)] public Guid Id { get; set; }

    [DisplayName("Nome"), Campo(TipoCampo.Texto, Tamanho = 120, Obrigatorio = true)]
    public string Nome { get; set; } = "";

    [DisplayName("Login"), Campo(TipoCampo.Login, Tamanho = 60, Obrigatorio = true, Dica = "minúsculas, números, ponto ou hífen")]
    public string Login { get; set; } = "";

    [DisplayName("Senha"), PasswordPropertyText(true), Campo(TipoCampo.Texto, Tamanho = 64, Dica = "em branco = manter a atual")]
    public string? Senha { get; set; }

    [DisplayName("Perfil")] public Perfil Perfil { get; set; } = Perfil.Operador;

    [DisplayName("Ativo"), Detalhe] public bool Ativo { get; set; } = true;
}

public class TerminalDto
{
    [Browsable(false)] public Guid Id { get; set; }

    [DisplayName("Nome"), Campo(TipoCampo.Maiusculas, Tamanho = 40, Obrigatorio = true, Dica = "ex.: CAIXA 01")]
    public string Nome { get; set; } = "";

    [DisplayName("Chave do terminal"), ReadOnly(true), Campo(TipoCampo.Texto, Dica = "copie para a configuração do caixa")]
    public string? Chave { get; set; }

    [DisplayName("Última sincronização"), ReadOnly(true), Detalhe]
    public DateTime? UltimaSincronizacao { get; set; }

    [DisplayName("Ativo"), Detalhe] public bool Ativo { get; set; } = true;
}

public class MovimentoEstoqueRequest
{
    public Guid ProdutoId { get; set; }
    public TipoMovimentoEstoque Tipo { get; set; }
    public decimal Quantidade { get; set; }
    public string? Observacao { get; set; }
}

public class MovimentoEstoqueDto
{
    [Browsable(false)] public Guid Id { get; set; }
    [DisplayName("Data")] public DateTime Data { get; set; }
    [DisplayName("Código")] public string? Codigo { get; set; }
    [DisplayName("Produto")] public string Produto { get; set; } = "";
    [DisplayName("Tipo")] public TipoMovimentoEstoque Tipo { get; set; }
    /// <summary>De onde veio: o caixa (ex.: "CAIXA 2") nas vendas, "Retaguarda" nos lançamentos manuais.</summary>
    [DisplayName("Origem")] public string? Origem { get; set; }
    [DisplayName("Quantidade"), Campo(TipoCampo.Quantidade)] public decimal Quantidade { get; set; }
    [DisplayName("Saldo após"), Campo(TipoCampo.Quantidade)] public decimal SaldoApos { get; set; }
    [DisplayName("Observação")] public string? Observacao { get; set; }
    [DisplayName("Usuário")] public string? Usuario { get; set; }
}

/// <summary>Permissões de um usuário (tela "Permissões" em Usuários).</summary>
public class PermissoesUsuarioDto
{
    public Guid UsuarioId { get; set; }
    public string Nome { get; set; } = "";
    public string Login { get; set; } = "";
    public Perfil Perfil { get; set; }
    /// <summary>false = usa o padrão do perfil.</summary>
    public bool Personalizado { get; set; }
    public List<string> Permissoes { get; set; } = [];
}

public class PermissoesUsuarioRequest
{
    /// <summary>null = voltar ao padrão do perfil.</summary>
    public List<string>? Permissoes { get; set; }
}
