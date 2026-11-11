using ProjetoJP.Api.Dados;
using ProjetoJP.Api.Servicos;
using ProjetoJP.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ProjetoJP.Api.Endpoints;

/// <summary>
/// Limite de caixas definido no ERP Master: a empresa não consegue criar (ou reativar) um terminal
/// além do que o plano dela permite. O cliente padrão (banco erp_nf) não tem limite.
/// </summary>
public static class LimiteCaixas
{
    public static async ValueTask<object?> Filtro(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http = ctx.HttpContext;
        if (http.Request.Method is not ("POST" or "PUT")) return await next(ctx);

        var dto = ctx.Arguments.OfType<TerminalDto>().FirstOrDefault();
        if (dto is null || !dto.Ativo) return await next(ctx); // desativar ou deixar inativo é sempre permitido

        if (http.Items[Inquilino.ChaveItem] is not Inquilino { Padrao: false } inquilino) return await next(ctx);
        var cliente = await http.RequestServices.GetRequiredService<Inquilinos>().BuscarAsync(inquilino.Codigo);
        if (cliente?.LimiteCaixas is not { } limite) return await next(ctx);

        var db = http.RequestServices.GetRequiredService<ErpDbContext>();
        Guid? id = ctx.Arguments.OfType<Guid>().Select(g => (Guid?)g).FirstOrDefault();

        // Editar um caixa que já está ativo (trocar o nome) não conta como caixa novo.
        if (id is not null && await db.Terminais.AnyAsync(x => x.Id == id && x.Ativo)) return await next(ctx);

        var ativos = await db.Terminais.CountAsync(x => x.Ativo);
        if (ativos >= limite)
            return Results.BadRequest(new
            {
                erro = $"O plano desta empresa permite {limite} caixa(s) ativo(s), e esse limite já foi atingido. " +
                       "Desative um caixa que não usa mais (Caixa → Terminais) ou fale com o suporte para aumentar."
            });

        return await next(ctx);
    }
}
