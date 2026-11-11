using System.Security.Claims;
using ProjetoJP.Api.Dados;
using ProjetoJP.Api.Servicos;
using ProjetoJP.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ProjetoJP.Api.Endpoints;

/// <summary>Registra somente metadados operacionais; nunca inclui senhas, tokens ou chaves.</summary>
public static class RegistroAuditoria
{
    public static void Adicionar(ErpDbContext db, ClaimsPrincipal user, string acao,
        string alvoTipo, Guid? alvoId, string resumo)
    {
        db.Auditorias.Add(new AuditoriaAdministrativa
        {
            UsuarioId = user.UsuarioId(), Acao = acao,
            AlvoTipo = alvoTipo, AlvoId = alvoId,
            Resumo = resumo.Length <= 250 ? resumo : resumo[..250]
        });
    }
}

public static class SegurancaAdministrativaEndpoints
{
    public static void MapSegurancaAdministrativa(this IEndpointRouteBuilder app)
    {
        var auditoria = app.MapGroup("/api/administracao/auditoria").RequireAuthorization("Admin");
        auditoria.MapGet("/", async (int? limite, ErpDbContext db) =>
        {
            var quantidade = Math.Clamp(limite ?? 100, 1, 300);
            var lista = await (from a in db.Auditorias.AsNoTracking()
                               join u in db.Usuarios.AsNoTracking() on a.UsuarioId equals (Guid?)u.Id into usuarios
                               from u in usuarios.DefaultIfEmpty()
                               orderby a.Data descending
                               select new AuditoriaResumoDto
                               {
                                   Id = a.Id, Data = a.Data, Usuario = u == null ? "(sistema)" : u.Login,
                                   Acao = a.Acao, AlvoTipo = a.AlvoTipo, AlvoId = a.AlvoId, Resumo = a.Resumo
                               }).Take(quantidade).ToListAsync();
            return Results.Ok(lista);
        });

        var sessoes = app.MapGroup("/api/auth/sessoes").RequireAuthorization();
        sessoes.MapGet("/", async (bool? todos, ClaimsPrincipal user, ErpDbContext db) =>
        {
            if (user.UsuarioId() is not { } id) return Results.Unauthorized();
            var atual = Guid.TryParse(user.FindFirst("sessao_id")?.Value, out var sid) ? sid : Guid.Empty;
            var q = db.SessoesAcesso.AsNoTracking().Where(x => x.ExpiraEm > DateTime.UtcNow);
            if (!(todos == true && user.IsInRole("Admin"))) q = q.Where(x => x.UsuarioId == id);
            var itens = await (from x in q
                               join u in db.Usuarios.AsNoTracking() on x.UsuarioId equals u.Id
                               orderby x.CriadaEm descending
                               select new SessaoResumoDto
                               {
                                   Id = x.Id, Usuario = u.Login, CriadaEm = x.CriadaEm,
                                   ExpiraEm = x.ExpiraEm, Revogada = x.RevogadaEm != null,
                                   Atual = x.Id == atual, ComputadorLembrado = x.DispositivoId != null
                               }).Take(200).ToListAsync();
            return Results.Ok(itens);
        });

        sessoes.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, ErpDbContext db) =>
        {
            if (user.UsuarioId() is not { } autorId) return Results.Unauthorized();
            var alvo = await db.SessoesAcesso.AsNoTracking()
                .Where(x => x.Id == id).Select(x => new { x.UsuarioId, x.RevogadaEm }).FirstOrDefaultAsync();
            if (alvo is null) return Results.NoContent();
            if (alvo.UsuarioId != autorId)
            {
                if (!user.IsInRole("Admin")) return Results.Forbid();
                if (!AcessoSensivel.SenhaRecente(user)) return AcessoSensivel.PedirSenha();
            }
            if (alvo.RevogadaEm is null)
            {
                var alteradas = await db.SessoesAcesso.Where(x => x.Id == id && x.RevogadaEm == null)
                    .ExecuteUpdateAsync(x => x.SetProperty(s => s.RevogadaEm, (DateTime?)DateTime.UtcNow));
                if (alteradas != 0)
                {
                    var propria = Guid.TryParse(user.FindFirst("sessao_id")?.Value, out var sessaoAtual) && sessaoAtual == id;
                    RegistroAuditoria.Adicionar(db, user, "EncerrarSessao", "Sessao", id,
                        propria ? "Saiu da conta" : "Sessão encerrada remotamente");
                    await db.SaveChangesAsync();
                }
            }
            return Results.NoContent();
        });
    }
}
