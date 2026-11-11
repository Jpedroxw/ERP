using ProjetoJP.Api.Dados;
using ProjetoJP.Api.Servicos;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ProjetoJP.Api.Endpoints;

/// <summary>Acesso técnico por empresa, apenas para ajustes do PDV. Nunca autoriza operações comerciais.</summary>
public static class SuporteTecnicoEndpoints
{
    public static void MapSuporteTecnico(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/suporte/verificar", async (
            SuporteTecnicoValidarRequest req, HttpContext http, ErpDbContext db, Inquilinos inq) =>
        {
            var clienteCodigo = (http.Items[Inquilino.ChaveItem] as Inquilino)?.Codigo ?? "";
            if (clienteCodigo.Length == 0)
                return Results.Json(new
                {
                    erro = "Este caixa está no servidor padrão. O acesso técnico por empresa só existe para empresas " +
                           "cadastradas no Projeto JP Master. Use \"Senha técnica deste computador\"."
                }, statusCode: StatusCodes.Status403Forbidden);

            var chave = http.Request.Headers["X-Terminal-Key"].ToString();
            if (chave.Length == 0 || !await db.Terminais.AsNoTracking().AnyAsync(t => t.Chave == chave && t.Ativo))
                return Results.Unauthorized();

            if (req.Senha is null || req.Login is null || req.Senha.Length is 0 or > 128 || req.Login.Length is 0 or > 60)
                return Results.Unauthorized();
            await using var master = inq.AbrirMaster();
            var conta = await (from c in master.Clientes.AsNoTracking()
                               join s in master.SuportesTecnicos.AsNoTracking() on c.Id equals s.ClienteId
                               where c.Codigo == clienteCodigo && c.Ativo && s.Ativo
                               select s).FirstOrDefaultAsync();
            if (conta is null || !string.Equals(conta.Login, req.Login.Trim(), StringComparison.OrdinalIgnoreCase)
                || !Senha.Verificar(req.Senha, conta.SenhaHash))
                return Results.Unauthorized();

            return Results.NoContent();
        }).RequireRateLimiting("suporte-tecnico");
    }
}
