using System.Security.Claims;
using ProjetoJP.Api.Dados;
using ProjetoJP.Api.Servicos;
using Microsoft.EntityFrameworkCore;

namespace ProjetoJP.Api.Endpoints;

/// <summary>Como cada cadastro converte entidade &lt;-&gt; DTO e valida.</summary>
public interface ICrudMapper<TEnt, TDto> where TEnt : Entidade, IAtivavel, new()
{
    IQueryable<TEnt> Consulta(IQueryable<TEnt> q);
    TDto ParaDto(TEnt e);
    Task<string?> ValidarAsync(TDto dto, Guid? id, ErpDbContext db);
    void Aplicar(TDto dto, TEnt e, bool novo);
    Task<string?> ValidarExclusaoAsync(TEnt e, ErpDbContext db) => Task.FromResult<string?>(null);
}

public static class Crud
{
    /// <summary>
    /// GET lista, GET por id, POST, PUT e DELETE (DELETE apenas inativa —
    /// nada é apagado, porque vendas antigas continuam apontando para o registro).
    /// </summary>
    public static RouteGroupBuilder MapCrud<TEnt, TDto>(this IEndpointRouteBuilder app, string rota, ICrudMapper<TEnt, TDto> m)
        where TEnt : Entidade, IAtivavel, new()
        where TDto : class
    {
        var g = app.MapGroup(rota);

        g.MapGet("/", async (ErpDbContext db) =>
        {
            var lista = await m.Consulta(db.Set<TEnt>().AsNoTracking()).ToListAsync();
            return lista.Select(m.ParaDto).ToList();
        });

        g.MapGet("/{id:guid}", async (Guid id, ErpDbContext db) =>
        {
            var e = await m.Consulta(db.Set<TEnt>().AsNoTracking()).FirstOrDefaultAsync(x => x.Id == id);
            return e is null ? Results.NotFound() : Results.Ok(m.ParaDto(e));
        });

        g.MapPost("/", async (TDto dto, ErpDbContext db, ClaimsPrincipal user) =>
        {
            var erro = await m.ValidarAsync(dto, null, db);
            if (erro is not null) return Extensoes.Erro(erro);

            var e = new TEnt { Id = Guid.NewGuid() };
            m.Aplicar(dto, e, true);
            db.Set<TEnt>().Add(e);
            if (e is Usuario || e is Terminal)
                RegistroAuditoria.Adicionar(db, user, "Criar", typeof(TEnt).Name, e.Id,
                    "Criado: " + Descrever(e));
            await db.SaveChangesAsync();
            return Results.Ok(await RecarregarAsync(db, m, e.Id));
        });

        g.MapPut("/{id:guid}", async (Guid id, TDto dto, ErpDbContext db, ClaimsPrincipal user) =>
        {
            var e = await db.Set<TEnt>().FirstOrDefaultAsync(x => x.Id == id);
            if (e is null) return Results.NotFound();

            var erro = await m.ValidarAsync(dto, id, db);
            if (erro is not null) return Extensoes.Erro(erro);

            // A mudança de usuário e a revogação dos tokens devem ser atômicas.
            await using var transacao = await db.Database.BeginTransactionAsync();
            var senhaAnterior = (e as Usuario)?.SenhaHash;
            m.Aplicar(dto, e, false);
            // Senha nova ou usuário desativado: vale no caixa na hora, sem esperar o envio manual.
            if (e is Usuario usuarioSeguranca && (senhaAnterior != usuarioSeguranca.SenhaHash || !usuarioSeguranca.Ativo))
                await PublicacaoUsuarios.AplicarSegurancaAsync(db, usuarioSeguranca);
            if (e is Usuario || e is Terminal)
                RegistroAuditoria.Adicionar(db, user, "Alterar", typeof(TEnt).Name, e.Id,
                    "Alterado: " + Descrever(e) +
                    (e is Usuario usuarioAlterado && senhaAnterior != usuarioAlterado.SenhaHash ? " — senha redefinida" : "") +
                    (!e.Ativo ? " — desativado" : ""));
            await db.SaveChangesAsync();
            if (e is Usuario usuario && (senhaAnterior != usuario.SenhaHash || !usuario.Ativo))
            {
                await db.Dispositivos.Where(x => x.UsuarioId == usuario.Id && !x.Revogado)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Revogado, true));
                await db.SessoesAcesso.Where(x => x.UsuarioId == usuario.Id && x.RevogadaEm == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevogadaEm, (DateTime?)DateTime.UtcNow));
            }
            await transacao.CommitAsync();
            return Results.Ok(await RecarregarAsync(db, m, id));
        });

        g.MapDelete("/{id:guid}", async (Guid id, ErpDbContext db, ClaimsPrincipal user) =>
        {
            var e = await db.Set<TEnt>().FirstOrDefaultAsync(x => x.Id == id);
            if (e is null) return Results.NotFound();

            var erro = await m.ValidarExclusaoAsync(e, db);
            if (erro is not null) return Extensoes.Erro(erro);

            await using var transacao = await db.Database.BeginTransactionAsync();
            e.Ativo = false;
            if (e is Usuario usuarioDesativado)
                await PublicacaoUsuarios.AplicarSegurancaAsync(db, usuarioDesativado); // sai do caixa na próxima sincronização
            if (e is Usuario || e is Terminal)
                RegistroAuditoria.Adicionar(db, user, "Desativar", typeof(TEnt).Name, e.Id,
                    "Desativado: " + Descrever(e));
            await db.SaveChangesAsync();
            if (e is Usuario usuario)
            {
                await db.Dispositivos.Where(x => x.UsuarioId == usuario.Id && !x.Revogado)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Revogado, true));
                await db.SessoesAcesso.Where(x => x.UsuarioId == usuario.Id && x.RevogadaEm == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevogadaEm, (DateTime?)DateTime.UtcNow));
            }
            await transacao.CommitAsync();
            return Results.NoContent();
        });

        return g;
    }

    private static async Task<TDto> RecarregarAsync<TEnt, TDto>(ErpDbContext db, ICrudMapper<TEnt, TDto> m, Guid id)
        where TEnt : Entidade, IAtivavel, new()
    {
        var e = await m.Consulta(db.Set<TEnt>().AsNoTracking()).FirstAsync(x => x.Id == id);
        return m.ParaDto(e);
    }

    /// <summary>Quem foi alterado, para a auditoria ser legível (login/nome; nunca senha, hash ou chave).</summary>
    private static string Descrever(object e) => e switch
    {
        Usuario u => $"usuário {u.Login} ({u.Perfil})",
        Terminal t => $"terminal {t.Nome}",
        _ => e.GetType().Name
    };
}
