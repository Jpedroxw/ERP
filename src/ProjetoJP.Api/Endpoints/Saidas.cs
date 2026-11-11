using System.Security.Claims;
using ProjetoJP.Api.Dados;
using ProjetoJP.Api.Servicos;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ProjetoJP.Api.Endpoints;

/// <summary>
/// Saída de produtos com vários itens (perda, vencido, uso interno, devolução ao fornecedor...).
/// Cada item vira uma movimentação de estoque do tipo Saída, com origem "Retaguarda". O valor fica pelo custo
/// do produto na hora (quanto se perdeu). Cancelar devolve as quantidades; nada é apagado.
/// </summary>
public static class SaidasEndpoints
{
    public static void MapSaidas(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/estoque/saidas").RequireAuthorization(PermissoesApi.Politica(Permissoes.MovimentarEstoque));

        g.MapGet("/", async (int? limite, ErpDbContext db) =>
        {
            var lista = await db.Saidas.AsNoTracking()
                .OrderByDescending(x => x.Numero)
                .Take(Math.Clamp(limite ?? 300, 1, 2000))
                .Select(x => new { Saida = x, Itens = x.Itens.Count })
                .ToListAsync();
            var nomes = await NomesAsync(db, lista.Select(x => x.Saida.UsuarioId));
            return lista.Select(x =>
            {
                var dto = ParaDto(x.Saida, nomes);
                dto.QuantidadeItens = x.Itens;
                return dto;
            }).ToList();
        });

        g.MapGet("/{id:guid}", async (Guid id, ErpDbContext db) =>
        {
            var dto = await CompletaAsync(db, id);
            return dto is null ? Results.NotFound() : Results.Ok(dto);
        });

        g.MapPost("/", async (SaidaRequest req, ClaimsPrincipal user, HttpContext http, ErpDbContext db) =>
        {
            var itens = req.Itens ?? [];
            var motivo = (req.Motivo ?? "").Trim();
            if (motivo.Length == 0) return Extensoes.Erro("Escolha o motivo da saída.");
            if (motivo.Length > 40 || !Formatos.TextoSeguro(motivo)) return Extensoes.Erro("Motivo inválido: máximo 40 caracteres.");
            if (itens.Count == 0) return Extensoes.Erro("Adicione pelo menos um produto.");
            if (itens.Count > 500) return Extensoes.Erro("No máximo 500 itens por saída.");
            if (!Formatos.TextoSeguro(req.Destino) || (req.Destino?.Trim().Length ?? 0) > 120)
                return Extensoes.Erro("Destino inválido: máximo 120 caracteres, sem caracteres de controle.");
            if (!Formatos.TextoSeguro(req.Documento) || (req.Documento?.Trim().Length ?? 0) > 60)
                return Extensoes.Erro("Documento inválido: máximo 60 caracteres, sem caracteres de controle.");
            if (!Formatos.TextoSeguro(req.Observacao, multilinha: true) || (req.Observacao?.Trim().Length ?? 0) > 200)
                return Extensoes.Erro("Observação inválida: máximo 200 caracteres, sem caracteres invisíveis.");

            if (req.PermitirNegativo)
            {
                if (!await PermissoesApi.TemAsync(http, Permissoes.PermitirEstoqueNegativo))
                    return PermissoesApi.Negado(Permissoes.PermitirEstoqueNegativo);
                if (!AcessoSensivel.SenhaRecente(user)) return AcessoSensivel.PedirSenha();
                if (string.IsNullOrWhiteSpace(req.JustificativaNegativo) ||
                    req.JustificativaNegativo.Trim().Length is < 10 or > 150 ||
                    !Formatos.TextoSeguro(req.JustificativaNegativo))
                    return Extensoes.Erro("Para permitir saldo negativo, informe uma justificativa de 10 a 150 caracteres.");
            }

            // O mesmo produto em duas linhas vira uma só.
            var porProduto = itens.GroupBy(i => i.ProdutoId).Select(gr => new { ProdutoId = gr.Key, Quantidade = gr.Sum(i => i.Quantidade) }).ToList();
            var ids = porProduto.Select(i => i.ProdutoId).ToList();
            var produtos = await db.Produtos.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
            var semSaldo = new List<string>();
            foreach (var i in porProduto)
            {
                if (!produtos.TryGetValue(i.ProdutoId, out var p))
                    return Extensoes.Erro("Um dos produtos não foi encontrado. Recarregue a tela e tente de novo.");
                if (i.Quantidade <= 0 || i.Quantidade > Formatos.MaxQuantidade)
                    return Extensoes.Erro($"{p.Descricao}: quantidade inválida.");
                if (!Formatos.UnidadeFracionada(p.Unidade) && i.Quantidade != decimal.Truncate(i.Quantidade))
                    return Extensoes.Erro($"{p.Descricao} é em {p.Unidade}: a quantidade precisa ser inteira.");
                if (!req.PermitirNegativo && i.Quantidade > p.EstoqueAtual)
                    semSaldo.Add($"{p.Descricao}: saldo {Formatos.Quantidade(p.EstoqueAtual, p.Unidade)} {p.Unidade}");
            }
            if (semSaldo.Count > 0)
                return Extensoes.Erro("Estoque insuficiente:\n• " + string.Join("\n• ", semSaldo.Take(10)) +
                                      "\n\nConfira as quantidades ou marque \"Permitir estoque negativo\".");

            await using var tx = await db.Database.BeginTransactionAsync();
            var numero = await db.Database.SqlQueryRaw<int>(
                "SELECT nextval('\"Saidas_Numero_seq\"')::integer AS \"Value\"").SingleAsync();
            var saida = new Saida
            {
                Id = Guid.NewGuid(),
                Numero = numero,
                Data = DateTime.UtcNow,
                Motivo = motivo,
                Destino = Limpo(req.Destino, 120),
                Documento = Limpo(req.Documento, 60),
                Observacao = req.PermitirNegativo
                    ? Limpo((req.Observacao?.Trim() is { Length: > 0 } obs ? obs + " | " : "") +
                        "Estoque negativo autorizado: " + req.JustificativaNegativo!.Trim(), 200)
                    : Limpo(req.Observacao, 200),
                UsuarioId = user.UsuarioId()
            };
            var texto = TextoMovimento(saida);
            foreach (var i in porProduto)
            {
                var p = produtos[i.ProdutoId];
                var qtd = Math.Round(i.Quantidade, Formatos.CasasQuantidade(p.Unidade));
                var custo = Math.Round(p.PrecoCusto, 2);
                var total = Math.Round(qtd * custo, 2);
                saida.Itens.Add(new SaidaItem { Id = Guid.NewGuid(), ProdutoId = p.Id, Quantidade = qtd, CustoUnitario = custo, Total = total });
                saida.Total += total;
                Estoque.Movimentar(db, p, TipoMovimentoEstoque.Saida, -qtd, texto, saida.UsuarioId);
            }
            db.Saidas.Add(saida);
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new { erro = "O estoque foi alterado por outro caixa ou usuário. Recarregue os dados e lance a saída de novo." });
            }
            await tx.CommitAsync();
            return Results.Ok(await CompletaAsync(db, saida.Id));
        });

        g.MapPost("/{id:guid}/cancelar", async (Guid id, ClaimsPrincipal user, ErpDbContext db) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var s = await db.Saidas.Include(x => x.Itens).FirstOrDefaultAsync(x => x.Id == id);
            if (s is null) return Results.NotFound();
            if (s.Cancelada) return Extensoes.Erro("Essa saída já foi cancelada.");

            var ids = s.Itens.Select(i => i.ProdutoId).Distinct().ToList();
            var produtos = await db.Produtos.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
            var texto = $"Cancelamento da saída nº {s.Numero}";
            foreach (var i in s.Itens)
                if (produtos.TryGetValue(i.ProdutoId, out var p))
                    Estoque.Movimentar(db, p, TipoMovimentoEstoque.Entrada, i.Quantidade, texto, user.UsuarioId());
            s.Cancelada = true;
            s.CanceladaEm = DateTime.UtcNow;
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new { erro = "A saída ou o estoque foi alterado por outro usuário. Atualize a tela e tente novamente." });
            }
            await tx.CommitAsync();
            return Results.Ok(await CompletaAsync(db, s.Id));
        });
    }

    private static string? Limpo(string? texto, int maximo)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var t = texto.Trim();
        return t.Length <= maximo ? t : t[..maximo];
    }

    /// <summary>O que aparece no histórico do produto, ex.: "Saída nº 7 · Produto vencido · Distribuidora X".</summary>
    private static string TextoMovimento(Saida s)
    {
        var t = $"Saída nº {s.Numero} · {s.Motivo}";
        if (s.Destino is not null) t += $" · {s.Destino}";
        if (s.Documento is not null) t += $" · doc. {s.Documento}";
        return t.Length <= 200 ? t : t[..200];
    }

    private static async Task<Dictionary<Guid, string>> NomesAsync(ErpDbContext db, IEnumerable<Guid?> ids)
    {
        var lista = ids.Where(x => x is not null).Select(x => x!.Value).Distinct().ToList();
        return lista.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Usuarios.AsNoTracking().Where(u => lista.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Nome);
    }

    private static SaidaDto ParaDto(Saida s, Dictionary<Guid, string> nomes) => new()
    {
        Id = s.Id,
        Numero = s.Numero,
        Data = s.Data,
        Motivo = s.Motivo,
        Destino = s.Destino,
        Documento = s.Documento,
        Observacao = s.Observacao,
        Total = s.Total,
        Cancelada = s.Cancelada,
        CanceladaEm = s.CanceladaEm,
        Usuario = s.UsuarioId is { } u && nomes.TryGetValue(u, out var nome) ? nome : null
    };

    private static async Task<SaidaDto?> CompletaAsync(ErpDbContext db, Guid id)
    {
        var s = await db.Saidas.AsNoTracking()
            .Include(x => x.Itens).ThenInclude(i => i.Produto)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (s is null) return null;
        var dto = ParaDto(s, await NomesAsync(db, [s.UsuarioId]));
        dto.QuantidadeItens = s.Itens.Count;
        dto.Itens = s.Itens
            .OrderBy(i => i.Produto?.Descricao)
            .Select(i => new SaidaItemDto
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
