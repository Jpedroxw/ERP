using System.Security.Claims;
using ProjetoJP.Api.Dados;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ProjetoJP.Api.Servicos;

/// <summary>
/// Permissões por usuário no servidor. A conferência é feita no banco a cada pedido (com cache por pedido):
/// mudar a permissão vale na hora, sem esperar o usuário sair e entrar.
/// </summary>
public static class PermissoesApi
{
    private const string ItemCache = "erp_permissoes";

    /// <summary>Nome da política de autorização de uma permissão (ex.: RequireAuthorization(Politica(...))).</summary>
    public static string Politica(string chave) => "perm:" + chave;

    /// <summary>Registra uma política para cada permissão conhecida.</summary>
    public static AuthorizationBuilder AdicionarPoliticas(this AuthorizationBuilder builder)
    {
        foreach (var p in Permissoes.Todas)
            builder.AddPolicy(Politica(p.Chave), x => x.RequireAuthenticatedUser().AddRequirements(new PermissaoRequisito(p.Chave)));
        return builder;
    }

    /// <summary>Permissões valendo para o usuário do pedido (vazio se não estiver logado ou estiver desativado).</summary>
    public static async Task<HashSet<string>> DoUsuarioAsync(HttpContext http)
    {
        if (http.Items[ItemCache] is HashSet<string> cache) return cache;
        var resultado = new HashSet<string>(StringComparer.Ordinal);
        if (http.User.UsuarioId() is { } id)
        {
            var db = http.RequestServices.GetRequiredService<ErpDbContext>();
            var u = await db.Usuarios.AsNoTracking().Where(x => x.Id == id && x.Ativo)
                .Select(x => new { x.Perfil, x.Permissoes }).FirstOrDefaultAsync();
            if (u is not null) resultado = Permissoes.Efetivas(u.Perfil, u.Permissoes);
        }
        http.Items[ItemCache] = resultado;
        return resultado;
    }

    public static async Task<bool> TemAsync(HttpContext http, string chave) =>
        http.User.IsInRole("Admin") || (await DoUsuarioAsync(http)).Contains(chave);

    public static IResult Negado(string chave) => Results.Json(
        new { erro = "Seu usuário não tem permissão para: " + Permissoes.Descricao(chave).ToLowerInvariant() + ". Peça ao administrador." },
        statusCode: StatusCodes.Status403Forbidden);

    /// <summary>Filtro para cadastros: consultar (GET) é livre; criar, alterar e desativar exigem a permissão.</summary>
    public static Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> SoEscrita(string chave) =>
        async (ctx, next) =>
        {
            if (HttpMethods.IsGet(ctx.HttpContext.Request.Method) || await TemAsync(ctx.HttpContext, chave))
                return await next(ctx);
            return Negado(chave);
        };

    /// <summary>
    /// Preço de custo: quem não pode ver recebe R$ 0,00 nas consultas, e ao salvar o custo que já estava gravado é mantido
    /// (o 0,00 que a tela mandou não apaga o custo real).
    /// </summary>
    public static async ValueTask<object?> ProtegerCusto(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http = ctx.HttpContext;
        if (await TemAsync(http, Permissoes.VerCusto)) return await next(ctx);

        var dto = ctx.Arguments.OfType<ProdutoDto>().FirstOrDefault();
        if (dto is not null)
        {
            var id = ctx.Arguments.OfType<Guid>().Cast<Guid?>().FirstOrDefault();
            var db = http.RequestServices.GetRequiredService<ErpDbContext>();
            dto.PrecoCusto = id is { } existente
                ? await db.Produtos.AsNoTracking().Where(x => x.Id == existente).Select(x => x.PrecoCusto).FirstOrDefaultAsync()
                : 0m;
        }

        var resultado = await next(ctx);
        switch (resultado)
        {
            case IEnumerable<ProdutoDto> lista:
                foreach (var p in lista) p.PrecoCusto = 0;
                break;
            case IValueHttpResult { Value: ProdutoDto p }:
                p.PrecoCusto = 0;
                break;
        }
        return resultado;
    }
}

public sealed class PermissaoRequisito(string chave) : IAuthorizationRequirement
{
    public string Chave { get; } = chave;
}

/// <summary>Confere a permissão no banco do cliente do pedido.</summary>
public sealed class PermissaoHandler(IHttpContextAccessor acesso) : AuthorizationHandler<PermissaoRequisito>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissaoRequisito requisito)
    {
        if (context.User.IsInRole("Admin"))
        {
            context.Succeed(requisito);
            return;
        }
        var http = acesso.HttpContext;
        if (http is null) return;
        if ((await PermissoesApi.DoUsuarioAsync(http)).Contains(requisito.Chave)) context.Succeed(requisito);
    }
}
