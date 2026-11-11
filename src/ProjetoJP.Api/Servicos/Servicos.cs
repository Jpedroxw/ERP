using System.Security.Claims;
using System.Text;
using ProjetoJP.Api.Dados;
using ProjetoJP.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ProjetoJP.Api.Servicos;

public class TokenService(IConfiguration config)
{
    public DateTime Expiracao() => DateTime.UtcNow.AddHours(config.GetValue("Jwt:HorasValidade", 12));

    /// <param name="cliente">Código do cliente (vazio = padrão): o token só vale nesse cliente.</param>
    public (string token, DateTime expira) Gerar(Usuario u, string cliente = "", bool porSenha = true, Guid? dispositivoId = null, Guid? sessaoId = null)
    {
        var chave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Chave"]!));
        var horas = config.GetValue("Jwt:HorasValidade", 12);
        var expira = DateTime.UtcNow.AddHours(horas);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, u.Id.ToString()),
            new Claim(ClaimTypes.Name, u.Nome),
            new Claim(ClaimTypes.Role, u.Perfil.ToString()),
            new Claim("cliente", cliente),
            new Claim("metodo_login", porSenha ? "senha" : "dispositivo"),
            new Claim("senha_ate", porSenha ? DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds().ToString() : "0"),
            new Claim("dispositivo_id", dispositivoId?.ToString() ?? ""),
            new Claim("sessao_id", sessaoId?.ToString() ?? "")
        };

        var descritor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = config["Jwt:Emissor"],
            Audience = config["Jwt:Emissor"],
            Expires = expira,
            SigningCredentials = new SigningCredentials(chave, SecurityAlgorithms.HmacSha256)
        };

        return (new JsonWebTokenHandler().CreateToken(descritor), expira);
    }
}

/// <summary>Converte datas do fuso da loja para UTC e vice-versa (relatórios por dia).</summary>
public class Fuso(IConfiguration config)
{
    private readonly TimeZoneInfo _tz = TimeZoneInfo.FindSystemTimeZoneById(config["Fuso"] ?? "America/Sao_Paulo");

    public DateTime InicioDoDiaUtc(DateOnly dia) =>
        TimeZoneInfo.ConvertTimeToUtc(dia.ToDateTime(TimeOnly.MinValue), _tz);

    public DateTime ParaLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _tz);

    /// <summary>Data de hoje no fuso da loja (promoções valem por dia).</summary>
    public DateOnly Hoje() => DateOnly.FromDateTime(ParaLocal(DateTime.UtcNow));
}

/// <summary>
/// Catálogo publicado: o que os caixas recebem. Copia os dados atuais dos produtos para ProdutosPublicados.
/// Produto com problema fica de fora e volta na lista de ignorados — os outros são publicados mesmo assim.
/// Quem chama grava (SaveChanges).
/// </summary>
public static class Catalogo
{
    public sealed record Ignorado(string Codigo, string Descricao, string Motivo);

    public static async Task<(int publicados, List<Ignorado> ignorados)> PublicarAsync(
        ErpDbContext db, IReadOnlyCollection<Produto> produtos, DateOnly hoje)
    {
        var ids = produtos.Select(p => p.Id).ToList();
        var anteriores = await db.ProdutosPublicados.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(x => x.Id);
        // Só promoções que ainda valem (em vigor ou agendadas). Promoção encerrada não impede baixar o preço.
        var promocoes = await db.Promocoes.AsNoTracking()
            .Where(x => ids.Contains(x.ProdutoId) && x.Ativa && x.Fim >= hoje)
            .GroupBy(x => x.ProdutoId)
            .Select(g => new { ProdutoId = g.Key, Maior = g.Max(x => x.PrecoPromocional) })
            .ToDictionaryAsync(x => x.ProdutoId, x => x.Maior);

        var ignorados = new List<Ignorado>();
        var agora = DateTime.UtcNow;
        var publicados = 0;
        foreach (var p in produtos)
        {
            var motivo = Motivo(p, promocoes);
            if (motivo is not null)
            {
                ignorados.Add(new Ignorado(p.Codigo, p.Descricao, motivo));
                continue;
            }
            if (!anteriores.TryGetValue(p.Id, out var pub))
            {
                pub = new ProdutoPublicado { Id = p.Id };
                db.ProdutosPublicados.Add(pub);
                anteriores[p.Id] = pub;
            }
            pub.Codigo = p.Codigo;
            pub.CodigoBarras = p.CodigoBarras;
            pub.Descricao = p.Descricao;
            pub.Unidade = p.Unidade;
            pub.PrecoVenda = p.PrecoVenda;
            pub.PrecoAtacado = p.PrecoAtacado;
            pub.QtdMinimaAtacado = p.QtdMinimaAtacado;
            pub.Ativo = p.Ativo;
            pub.PublicadoEm = agora;
            publicados++;
        }
        return (publicados, ignorados);
    }

    private static string? Motivo(Produto p, Dictionary<Guid, decimal> promocoes)
    {
        if (string.IsNullOrWhiteSpace(p.Codigo)) return "sem código";
        if (string.IsNullOrWhiteSpace(p.Descricao)) return "sem descrição";
        if (!p.Ativo) return null; // desativar sempre pode ser publicado
        if (p.PrecoVenda <= 0) return "sem preço de venda (está R$ 0,00)";
        if (p.PrecoAtacado > 0 && p.PrecoAtacado >= p.PrecoVenda)
            return $"o preço de atacado ({Formatos.Moeda(p.PrecoAtacado)}) precisa ser menor que o preço de venda ({Formatos.Moeda(p.PrecoVenda)})";
        if (promocoes.TryGetValue(p.Id, out var maior) && maior >= p.PrecoVenda)
            return $"o preço normal ({Formatos.Moeda(p.PrecoVenda)}) precisa ser maior que o da promoção em vigor ou agendada ({Formatos.Moeda(maior)})";
        return null;
    }
}

public static class Extensoes
{
    /// <summary>Npgsql exige DateTime em UTC para colunas timestamptz.</summary>
    public static DateTime Utc(this DateTime d) => d.Kind switch
    {
        DateTimeKind.Utc => d,
        DateTimeKind.Local => d.ToUniversalTime(),
        _ => DateTime.SpecifyKind(d, DateTimeKind.Utc)
    };

    public static DateTime? Utc(this DateTime? d) => d?.Utc();

    public static Guid? UsuarioId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static IResult Erro(string mensagem) => Results.BadRequest(new { erro = mensagem });
}

public static class Estoque
{
    /// <summary>
    /// Aplica uma movimentação no produto (já rastreado pelo contexto) e registra o histórico.
    /// Para Ajuste, "quantidade" é o novo saldo; para os demais, é a variação com sinal.
    /// </summary>
    public static MovimentoEstoque Movimentar(ErpDbContext db, Produto produto, TipoMovimentoEstoque tipo,
        decimal quantidade, string? observacao, Guid? usuarioId, Guid? vendaId = null)
    {
        var variacao = tipo == TipoMovimentoEstoque.Ajuste ? quantidade - produto.EstoqueAtual : quantidade;
        produto.EstoqueAtual += variacao;

        var mov = new MovimentoEstoque
        {
            Id = Guid.NewGuid(),
            ProdutoId = produto.Id,
            Data = DateTime.UtcNow,
            Tipo = tipo,
            Quantidade = variacao,
            SaldoApos = produto.EstoqueAtual,
            Observacao = observacao,
            UsuarioId = usuarioId,
            VendaId = vendaId
        };
        db.MovimentosEstoque.Add(mov);
        return mov;
    }
}

/// <summary>
/// Cópia dos usuários que os caixas recebem (tabela UsuariosPublicados). Mudança comum só vale no caixa depois do
/// envio manual ("Enviar usuários aos caixas"), mas senha nova e desativação valem na hora: quem foi desligado
/// ou teve a senha trocada não pode continuar entrando no caixa com a senha antiga.
/// </summary>
public static class PublicacaoUsuarios
{
    /// <summary>Copia o usuário inteiro para os caixas (admin inicial, senha redefinida pelo Master, novo usuário do caixa).</summary>
    public static async Task PublicarAsync(ErpDbContext db, Usuario u)
    {
        var pub = await db.UsuariosPublicados.FindAsync(u.Id);
        if (pub is null)
        {
            pub = new UsuarioPublicado { Id = u.Id };
            db.UsuariosPublicados.Add(pub);
        }
        pub.Nome = u.Nome;
        pub.Login = u.Login;
        pub.SenhaHash = u.SenhaHash;
        pub.Perfil = u.Perfil;
        pub.Ativo = u.Ativo;
        pub.Permissoes = u.Permissoes;
        pub.PublicadoEm = DateTime.UtcNow;
    }

    /// <summary>Leva só a senha e a situação (ativo/inativo) para os caixas, sem publicar as outras mudanças pendentes.</summary>
    public static async Task AplicarSegurancaAsync(ErpDbContext db, Usuario u)
    {
        var pub = await db.UsuariosPublicados.FindAsync(u.Id);
        if (pub is null) return; // nunca foi aos caixas: nada a tirar de lá
        if (pub.SenhaHash == u.SenhaHash && pub.Ativo == u.Ativo) return;
        pub.SenhaHash = u.SenhaHash;
        pub.Ativo = u.Ativo;
        pub.PublicadoEm = DateTime.UtcNow;
    }
}

/// <summary>
/// Saldo do fiado. A soma é feita direto no banco (SaldoFiado = SaldoFiado + valor), não "lê, soma e grava":
/// dois caixas lançando fiado do mesmo cliente ao mesmo tempo não apagam o lançamento um do outro.
/// Precisa rodar dentro de uma transação junto com o SaveChanges que grava o lançamento.
/// </summary>
public static class ContaFiado
{
    /// <returns>false se o cliente não existe ou, com <paramref name="naoNegativo"/>, se o saldo ficaria negativo.</returns>
    public static async Task<bool> LancarAsync(ErpDbContext db, Guid clienteId, Guid? vendaId, decimal valor,
        string observacao, Guid? usuarioId, bool naoNegativo = false)
    {
        if (valor == 0) return true;
        var q = db.Clientes.Where(c => c.Id == clienteId);
        if (naoNegativo) q = q.Where(c => c.SaldoFiado + valor >= 0);
        var linhas = await q.ExecuteUpdateAsync(s => s.SetProperty(c => c.SaldoFiado, c => c.SaldoFiado + valor));
        if (linhas == 0) return false;
        db.LancamentosFiado.Add(new LancamentoFiado
        {
            Id = Guid.NewGuid(),
            ClienteId = clienteId,
            VendaId = vendaId,
            Valor = valor,
            Observacao = observacao.Length <= 200 ? observacao : observacao[..200],
            UsuarioId = usuarioId
        });
        return true;
    }
}
