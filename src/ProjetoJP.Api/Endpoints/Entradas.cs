using System.Security.Claims;
using ProjetoJP.Api.Dados;
using ProjetoJP.Api.Servicos;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ProjetoJP.Api.Endpoints;

/// <summary>
/// Entrada de produtos com vários itens (compra / nota do fornecedor). Cada item vira uma movimentação de
/// estoque do tipo Entrada (aparece no histórico do produto) e, se pedido, atualiza o preço de custo.
/// Cancelar lança as saídas de volta; nada é apagado.
/// </summary>
public static class EntradasEndpoints
{
    public static void MapEntradas(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/estoque/entradas").RequireAuthorization(PermissoesApi.Politica(Permissoes.EntradaProdutos));

        g.MapGet("/", async (int? limite, ErpDbContext db) =>
        {
            var lista = await db.Entradas.AsNoTracking()
                .OrderByDescending(x => x.Numero)
                .Take(Math.Clamp(limite ?? 300, 1, 2000))
                .Select(x => new { Entrada = x, Itens = x.Itens.Count })
                .ToListAsync();

            var nomes = await NomesAsync(db, lista.Select(x => x.Entrada.UsuarioId));
            return lista.Select(x =>
            {
                var dto = ParaDto(x.Entrada, nomes);
                dto.QuantidadeItens = x.Itens;
                return dto;
            }).ToList();
        });

        g.MapGet("/{id:guid}", async (Guid id, ErpDbContext db) =>
        {
            var dto = await CompletaAsync(db, id);
            return dto is null ? Results.NotFound() : Results.Ok(dto);
        });

        g.MapPost("/", async (EntradaRequest req, ClaimsPrincipal user, ErpDbContext db) =>
        {
            var itens = req.Itens ?? [];
            if (itens.Count == 0) return Extensoes.Erro("Adicione pelo menos um produto.");
            if (itens.Count > 500) return Extensoes.Erro("No máximo 500 itens por entrada.");
            if (!Formatos.TextoSeguro(req.Fornecedor) || (req.Fornecedor?.Trim().Length ?? 0) > 120)
                return Extensoes.Erro("Fornecedor inválido: máximo 120 caracteres, sem caracteres de controle.");
            if (!Formatos.TextoSeguro(req.Documento) || (req.Documento?.Trim().Length ?? 0) > 60)
                return Extensoes.Erro("Documento inválido: máximo 60 caracteres, sem caracteres de controle.");
            if (!Formatos.TextoSeguro(req.Observacao, multilinha: true) || (req.Observacao?.Trim().Length ?? 0) > 200)
                return Extensoes.Erro("Observação inválida: máximo 200 caracteres, sem caracteres invisíveis.");

            var ids = itens.Select(i => i.ProdutoId).Distinct().ToList();
            var produtos = await db.Produtos.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
            foreach (var i in itens)
            {
                if (!produtos.TryGetValue(i.ProdutoId, out var p))
                    return Extensoes.Erro("Um dos produtos não foi encontrado. Recarregue a tela e tente de novo.");
                if (i.Quantidade <= 0 || i.Quantidade > Formatos.MaxQuantidade)
                    return Extensoes.Erro($"{p.Descricao}: quantidade inválida.");
                if (!Formatos.UnidadeFracionada(p.Unidade) && i.Quantidade != decimal.Truncate(i.Quantidade))
                    return Extensoes.Erro($"{p.Descricao} é em {p.Unidade}: a quantidade precisa ser inteira.");
                if (i.CustoUnitario < 0 || i.CustoUnitario > Formatos.MaxMoeda)
                    return Extensoes.Erro($"{p.Descricao}: custo inválido.");
            }

            await using var tx = await db.Database.BeginTransactionAsync();
            // Número reservado por sequência PostgreSQL própria deste banco/cliente.
            // nextval é atômico entre várias Retaguardas e não reutiliza números cancelados.
            // A sequência e o índice único são instalados na inicialização versionada do banco.
            var numero = await db.Database.SqlQueryRaw<int>(
                "SELECT nextval('\"Entradas_Numero_seq\"')::integer AS \"Value\"").SingleAsync();
            var entrada = new Entrada
            {
                Id = Guid.NewGuid(),
                Numero = numero,
                Data = DateTime.UtcNow,
                Fornecedor = Limpo(req.Fornecedor, 120),
                Documento = Limpo(req.Documento, 60),
                Observacao = Limpo(req.Observacao, 200),
                UsuarioId = user.UsuarioId()
            };
            var texto = TextoMovimento(entrada);

            foreach (var i in itens)
            {
                var p = produtos[i.ProdutoId];
                var qtd = Math.Round(i.Quantidade, Formatos.CasasQuantidade(p.Unidade));
                var custo = Math.Round(i.CustoUnitario, 2);
                var total = Math.Round(qtd * custo, 2);
                entrada.Itens.Add(new EntradaItem
                {
                    Id = Guid.NewGuid(), ProdutoId = p.Id, Quantidade = qtd, CustoUnitario = custo, Total = total
                });
                entrada.Total += total;
                Estoque.Movimentar(db, p, TipoMovimentoEstoque.Entrada, qtd, texto, entrada.UsuarioId);
                if (req.AtualizarCusto && custo > 0) p.PrecoCusto = custo;
            }

            db.Entradas.Add(entrada);
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new { erro = "O estoque foi alterado por outro caixa ou usuário. Recarregue os dados antes de registrar a entrada." });
            }
            await tx.CommitAsync();
            return Results.Ok(await CompletaAsync(db, entrada.Id));
        });

        g.MapPost("/{id:guid}/cancelar", async (Guid id, ClaimsPrincipal user, ErpDbContext db) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var e = await db.Entradas.Include(x => x.Itens).FirstOrDefaultAsync(x => x.Id == id);
            if (e is null) return Results.NotFound();
            if (e.Cancelada) return Extensoes.Erro("Essa entrada já foi cancelada.");

            var ids = e.Itens.Select(i => i.ProdutoId).Distinct().ToList();
            var produtos = await db.Produtos.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
            var texto = $"Cancelamento da entrada nº {e.Numero}";
            foreach (var i in e.Itens)
            {
                if (produtos.TryGetValue(i.ProdutoId, out var p))
                    Estoque.Movimentar(db, p, TipoMovimentoEstoque.Saida, -i.Quantidade, texto, user.UsuarioId());
            }
            e.Cancelada = true;
            e.CanceladaEm = DateTime.UtcNow;
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new { erro = "A entrada ou o estoque foi alterado por outro usuário. Atualize a tela e tente novamente." });
            }
            await tx.CommitAsync();
            return Results.Ok(await CompletaAsync(db, e.Id));
        });
    }

    private static string? Limpo(string? texto, int maximo)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var t = texto.Trim();
        return t.Length <= maximo ? t : t[..maximo];
    }

    /// <summary>O que aparece no histórico de estoque do produto, ex.: "Entrada nº 12 · NF 4567 · Distribuidora X".</summary>
    private static string TextoMovimento(Entrada e)
    {
        var t = $"Entrada nº {e.Numero}";
        if (e.Documento is not null) t += $" · NF {e.Documento}";
        if (e.Fornecedor is not null) t += $" · {e.Fornecedor}";
        return t.Length <= 200 ? t : t[..200];
    }

    private static async Task<Dictionary<Guid, string>> NomesAsync(ErpDbContext db, IEnumerable<Guid?> ids)
    {
        var lista = ids.Where(x => x is not null).Select(x => x!.Value).Distinct().ToList();
        return lista.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Usuarios.AsNoTracking().Where(u => lista.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Nome);
    }

    private static EntradaDto ParaDto(Entrada e, Dictionary<Guid, string> nomes) => new()
    {
        Id = e.Id,
        Numero = e.Numero,
        Data = e.Data,
        Fornecedor = e.Fornecedor,
        Documento = e.Documento,
        Observacao = e.Observacao,
        Total = e.Total,
        Cancelada = e.Cancelada,
        CanceladaEm = e.CanceladaEm,
        Usuario = e.UsuarioId is { } u && nomes.TryGetValue(u, out var nome) ? nome : null
    };

    private static async Task<EntradaDto?> CompletaAsync(ErpDbContext db, Guid id)
    {
        var e = await db.Entradas.AsNoTracking()
            .Include(x => x.Itens).ThenInclude(i => i.Produto)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return null;

        var dto = ParaDto(e, await NomesAsync(db, [e.UsuarioId]));
        dto.QuantidadeItens = e.Itens.Count;
        dto.Itens = e.Itens
            .OrderBy(i => i.Produto?.Descricao)
            .Select(i => new EntradaItemDto
            {
                ProdutoId = i.ProdutoId,
                Codigo = i.Produto?.Codigo ?? "",
                Descricao = i.Produto?.Descricao ?? "(produto excluído)",
                Unidade = i.Produto?.Unidade ?? "",
                Quantidade = i.Quantidade,
                CustoUnitario = i.CustoUnitario,
                Total = i.Total
            }).ToList();
        return dto;
    }
}
