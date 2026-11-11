using System.Security.Claims;
using ProjetoJP.Api.Dados;
using ProjetoJP.Api.Servicos;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ProjetoJP.Api.Endpoints;

/// <summary>Tela "Permissões" de cada usuário (só Admin, com senha confirmada para alterar).</summary>
public static class PermissoesUsuariosEndpoints
{
    public static void MapPermissoesUsuarios(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/usuarios").RequireAuthorization("Admin");

        g.MapGet("/{id:guid}/permissoes", async (Guid id, ErpDbContext db) =>
        {
            var u = await db.Usuarios.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (u is null) return Results.NotFound();
            return Results.Ok(new PermissoesUsuarioDto
            {
                UsuarioId = u.Id, Nome = u.Nome, Login = u.Login, Perfil = u.Perfil,
                Personalizado = u.Perfil != Perfil.Admin && u.Permissoes is not null,
                Permissoes = [.. Permissoes.Efetivas(u.Perfil, u.Permissoes)]
            });
        });

        g.MapPut("/{id:guid}/permissoes", async (Guid id, PermissoesUsuarioRequest req, ClaimsPrincipal user, ErpDbContext db) =>
        {
            if (!AcessoSensivel.SenhaRecente(user)) return AcessoSensivel.PedirSenha();
            var u = await db.Usuarios.FirstOrDefaultAsync(x => x.Id == id);
            if (u is null) return Results.NotFound();
            if (u.Perfil == Perfil.Admin)
                return Extensoes.Erro("O Admin sempre tem todas as permissões. Para limitar, mude o perfil do usuário para Gerente ou Operador.");

            u.Permissoes = req.Permissoes is null ? null : Permissoes.Gravar(req.Permissoes);
            var efetivas = Permissoes.Efetivas(u.Perfil, u.Permissoes);
            var resumo = req.Permissoes is null
                ? $"Permissões de {u.Login}: voltou ao padrão do perfil {u.Perfil}"
                : $"Permissões de {u.Login}: {efetivas.Count} de {Permissoes.Todas.Count} marcadas";
            RegistroAuditoria.Adicionar(db, user, "Permissoes", "Usuario", u.Id, resumo);
            await db.SaveChangesAsync(); // AtualizadoEm muda: os caixas recebem na próxima sincronização
            return Results.Ok(new PermissoesUsuarioDto
            {
                UsuarioId = u.Id, Nome = u.Nome, Login = u.Login, Perfil = u.Perfil,
                Personalizado = u.Permissoes is not null, Permissoes = [.. efetivas]
            });
        });
    }
}

/// <summary>Orçamentos enviados pelos caixas (consulta na Retaguarda).</summary>
public static class OrcamentosEndpoints
{
    public static void MapOrcamentos(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/orcamentos").RequireAuthorization(PermissoesApi.Politica(Permissoes.VerOrcamentos));

        // Datas "de" e "ate" no formato AAAA-MM-DD, inclusivas, no fuso da loja.
        g.MapGet("/", async (DateOnly de, DateOnly ate, StatusOrcamento? status, ErpDbContext db, Fuso fuso) =>
        {
            var ini = fuso.InicioDoDiaUtc(de);
            var fim = fuso.InicioDoDiaUtc(ate.AddDays(1));
            var q = db.Orcamentos.AsNoTracking().Include(x => x.Itens).Where(x => x.Data >= ini && x.Data < fim);
            if (status is not null) q = q.Where(x => x.Status == status);
            var lista = await q.OrderByDescending(x => x.Data).Take(1000).ToListAsync();

            var terminais = await db.Terminais.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Nome);
            var idsUsuarios = lista.Select(x => x.UsuarioId).Distinct().ToList();
            var usuarios = await db.Usuarios.AsNoTracking().Where(u => idsUsuarios.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Nome);

            return lista.Select(o => new OrcamentoDto
            {
                Id = o.Id,
                Numero = o.Numero,
                Terminal = terminais.GetValueOrDefault(o.TerminalId, ""),
                UsuarioId = o.UsuarioId,
                Usuario = usuarios.GetValueOrDefault(o.UsuarioId, ""),
                ClienteId = o.ClienteId,
                ClienteNome = o.ClienteNome,
                Data = fuso.ParaLocal(o.Data),
                Validade = o.Validade,
                Subtotal = o.Subtotal,
                Desconto = o.Desconto,
                Total = o.Total,
                Observacao = o.Observacao,
                Status = o.Status,
                VendaId = o.VendaId,
                Itens = o.Itens.Select(i => new OrcamentoItemDto
                {
                    Id = i.Id, ProdutoId = i.ProdutoId, Codigo = i.Codigo, Descricao = i.Descricao, Unidade = i.Unidade,
                    Quantidade = i.Quantidade, PrecoUnitario = i.PrecoUnitario, Total = i.Total
                }).ToList()
            }).ToList();
        });
    }
}
