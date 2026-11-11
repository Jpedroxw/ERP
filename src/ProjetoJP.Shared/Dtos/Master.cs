namespace ProjetoJP.Shared.Dtos;

// ERP Master: cadastro dos clientes (empresas). Cada cliente tem um banco próprio na VPS.

public class ClienteMasterDto
{
    public Guid Id { get; set; }
    /// <summary>Nome curto usado no endereço (https://site/nf/CODIGO/) e no "nome do servidor" das lojas.</summary>
    public string Codigo { get; set; } = "";
    public string Nome { get; set; } = "";
    public string? Documento { get; set; }
    public string? Responsavel { get; set; }
    public string? Telefone { get; set; }
    public string? Email { get; set; }
    public string? Cidade { get; set; }
    public string? Uf { get; set; }
    public bool Ativo { get; set; } = true;
    public DateTime CriadoEm { get; set; }
    public string? Observacao { get; set; }
    /// <summary>Tamanho do banco do cliente, em bytes (0 se não deu para medir).</summary>
    public long TamanhoBanco { get; set; }
    public int Usuarios { get; set; }
    public int Terminais { get; set; }
    /// <summary>Quantos caixas (terminais ativos) a empresa pode ter. Vazio = sem limite.</summary>
    public int? LimiteCaixas { get; set; }

    public DateTime? UltimaVenda { get; set; }
}

public class NovoClienteRequest
{
    public string Codigo { get; set; } = "";
    public string Nome { get; set; } = "";
    public string? Documento { get; set; }
    public string? Responsavel { get; set; }
    public string? Telefone { get; set; }
    public string? Email { get; set; }
    public string? Cidade { get; set; }
    public string? Uf { get; set; }
    public string? Observacao { get; set; }
    /// <summary>Quantos caixas (terminais ativos) a empresa pode ter. Vazio = sem limite.</summary>
    public int? LimiteCaixas { get; set; }
    public string AdminNome { get; set; } = "Administrador";
    public string AdminLogin { get; set; } = "admin";
    public string AdminSenha { get; set; } = "";
}

public class AtualizarClienteRequest
{
    public string Nome { get; set; } = "";
    public string? Documento { get; set; }
    public string? Responsavel { get; set; }
    public string? Telefone { get; set; }
    public string? Email { get; set; }
    public string? Cidade { get; set; }
    public string? Uf { get; set; }
    public bool Ativo { get; set; } = true;
    public string? Observacao { get; set; }
    /// <summary>Quantos caixas (terminais ativos) a empresa pode ter. Vazio = sem limite.</summary>
    public int? LimiteCaixas { get; set; }
}

/// <summary>Redefine (ou cria) o usuário administrador do cliente.</summary>
public class SenhaAdminRequest
{
    public string Login { get; set; } = "admin";
    public string Senha { get; set; } = "";
}

// ---------- Pedido de cadastro (feito pelo próprio cliente) ----------

public class PedidoCadastroRequest
{
    public string Empresa { get; set; } = "";
    public string? Documento { get; set; }
    public string Responsavel { get; set; } = "";
    public string Telefone { get; set; } = "";
    public string? Email { get; set; }
    public string? Cidade { get; set; }
    public string? Uf { get; set; }
    public string CodigoDesejado { get; set; } = "";
    public string AdminLogin { get; set; } = "admin";
    public string AdminSenha { get; set; } = "";
    /// <summary>
    /// Campo escondido do formulário do site: pessoas não veem e deixam vazio; robôs costumam preencher.
    /// Preenchido = pedido descartado em silêncio. A Retaguarda não envia.
    /// </summary>
    public string? Site { get; set; }
}

public class PedidoCadastroResposta
{
    public Guid Id { get; set; }
    /// <summary>Código secreto para a Retaguarda de quem pediu consultar a situação.</summary>
    public string Token { get; set; } = "";
}

public class SituacaoCadastroDto
{
    /// <summary>Pendente, Aprovado ou Recusado.</summary>
    public string Situacao { get; set; } = "";
    public string? Codigo { get; set; }
    public string? Motivo { get; set; }
    public string AdminLogin { get; set; } = "";
}

public class DisponibilidadeDto
{
    public bool Disponivel { get; set; }
    public string? Mensagem { get; set; }
}

public class PedidoCadastroDto
{
    public Guid Id { get; set; }
    public DateTime CriadoEm { get; set; }
    public string Empresa { get; set; } = "";
    public string? Documento { get; set; }
    public string Responsavel { get; set; } = "";
    public string Telefone { get; set; } = "";
    public string? Email { get; set; }
    public string? Cidade { get; set; }
    public string? Uf { get; set; }
    public string CodigoDesejado { get; set; } = "";
    public string AdminLogin { get; set; } = "";
    public string Situacao { get; set; } = "";
    public string? Motivo { get; set; }
    public string? CodigoAprovado { get; set; }
}

public class AprovarPedidoRequest
{
    /// <summary>Código final (pode ser diferente do pedido, se o desejado já existir).</summary>
    public string Codigo { get; set; } = "";
    public string Nome { get; set; } = "";
    /// <summary>Quantos caixas (terminais ativos) a empresa pode ter. Vazio = sem limite.</summary>
    public int? LimiteCaixas { get; set; }
}

public class RecusarPedidoRequest
{
    public string Motivo { get; set; } = "";
}

public static class CodigoCliente
{
    /// <summary>Palavras que já são caminhos da API e não podem ser código de cliente.</summary>
    // Os últimos são páginas do site público (cadastro, painel online, download): não podem virar nome de servidor.
    public static readonly string[] Reservados = ["api", "health", "master", "cadastro", "nf", "padrao", "admin", "www",
        "site", "painel", "acompanhar", "privacidade", "baixar"];

    /// <summary>De 2 a 30 letras minúsculas, números ou hífen, começando por letra.</summary>
    public static bool Valido(string? codigo) =>
        !string.IsNullOrEmpty(codigo) && codigo.Length is >= 2 and <= 30 &&
        char.IsAsciiLetterLower(codigo[0]) &&
        codigo.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-') &&
        !Reservados.Contains(codigo);

    /// <summary>
    /// Como o nome do servidor aparece e é digitado: maiúsculas com espaço ("mercado-bom-preco" → "MERCADO BOM PRECO").
    /// Por dentro (endereço da API, banco) ele continua em minúsculas com hífen.
    /// </summary>
    public static string ParaTela(string? codigo) => (codigo ?? "").Trim().Replace('-', ' ').ToUpperInvariant();

    /// <summary>O que foi digitado na tela → código ("Mercado Bom Preço" ou "MERCADO BOM PRECO" → "mercado-bom-preco").</summary>
    public static string DaTela(string? texto)
    {
        var s = Formatos.RemoverAcentos(texto ?? "").ToLowerInvariant();
        var sb = new System.Text.StringBuilder();
        foreach (var c in s)
        {
            if (char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c)) sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }

    /// <summary>Sugere um código a partir do nome da empresa ("Mercado Bom Preço" → "mercado-bom-preco").</summary>
    public static string Sugerir(string? nome)
    {
        var s = Formatos.RemoverAcentos(nome ?? "").ToLowerInvariant();
        var sb = new System.Text.StringBuilder();
        foreach (var c in s)
        {
            if (char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c)) sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var r = sb.ToString().Trim('-');
        if (r.Length > 30) r = r[..30].Trim('-');
        if (r.Length > 0 && !char.IsAsciiLetterLower(r[0])) r = "c-" + r;
        return r;
    }
}
