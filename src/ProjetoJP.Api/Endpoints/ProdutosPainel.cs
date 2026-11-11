using System.Security.Claims;
using ProjetoJP.Api.Dados;
using ProjetoJP.Api.Servicos;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ProjetoJP.Api.Endpoints;

/// <summary>
/// Dados da tela Produtos e Estoque da Retaguarda: números do topo, consulta paginada
/// e o gráfico de vendas/estoque dos últimos dias. Nada aqui muda o que os caixas recebem.
/// </summary>
public static class ProdutosPainel
{
    public static void MapProdutosPainel(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/produtos").RequireAuthorization();

        // O catálogo grande não deve ser transferido inteiro a cada 30 segundos.
        g.MapGet("/pagina", async (string? busca, Guid? categoria, int? status, int? pagina,
            int? porPagina, int? ordenar, bool? desc, HttpContext http, ErpDbContext db) =>
        {
            var q = db.Produtos.AsNoTracking().AsQueryable();
            if (categoria is { } cat && cat != Guid.Empty) q = q.Where(x => x.CategoriaId == cat);
            q = status switch
            {
                1 => q.Where(x => x.Ativo),
                2 => q.Where(x => !x.Ativo),
                3 => q.Where(x => x.Ativo && x.EstoqueMinimo > 0 && x.EstoqueAtual <= x.EstoqueMinimo),
                4 => q.Where(x => x.Ativo && x.EstoqueAtual <= 0),
                _ => q
            };
            var filtrosSemBusca = q;
            if (!string.IsNullOrWhiteSpace(busca))
            {
                var termos = busca.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(5);
                foreach (var termo in termos)
                {
                    // Remove caracteres de curinga para manter a busca literal.
                    var literal = termo.Replace("%", "").Replace("_", "").Replace("\\", "");
                    if (literal.Length == 0) continue;
                    var padrao = "%" + literal + "%";
                    q = q.Where(x => EF.Functions.ILike(x.Descricao, padrao) ||
                                     EF.Functions.ILike(x.Codigo, padrao) ||
                                     (x.CodigoBarras != null && EF.Functions.ILike(x.CodigoBarras, padrao)));
                }
            }
            var total = await q.CountAsync();
            // Quando não há correspondência literal, tenta a busca aproximada em um subconjunto
            // limitado no servidor. Isso preserva a busca por pequenos erros ("aroz" -> "arroz")
            // sem transferir dezenas de milhares de produtos ao cliente.
            var buscaLimpa = busca?.Trim() ?? "";
            if (total == 0 && buscaLimpa.Length >= 3)
            {
                var prefixo = buscaLimpa[..2].Replace("%", "").Replace("_", "");
                if (prefixo.Length >= 1)
                {
                    var candidatos = await filtrosSemBusca.Include(x => x.Categoria)
                        .Where(x => EF.Functions.ILike(x.Descricao, "%" + prefixo + "%"))
                        .OrderBy(x => x.Descricao).Take(800).ToListAsync();
                    var proximos = candidatos.Select(x => (produto: x,
                            pontos: PesquisaProdutos.Pontuar(buscaLimpa, x.Descricao, x.Codigo, x.CodigoBarras)))
                        .Where(x => x.pontos >= 0)
                        .OrderByDescending(x => x.pontos).ThenBy(x => x.produto.Descricao)
                        .Select(x => ProdutoMapper.Converter(x.produto)).ToList();
                    var tamanhoAproximado = Math.Clamp(porPagina ?? 20, 10, 100);
                    var indiceAproximado = Math.Clamp(pagina ?? 0, 0, Math.Max(0, (proximos.Count - 1) / tamanhoAproximado));
                    var itensAproximados = proximos.Skip(indiceAproximado * tamanhoAproximado).Take(tamanhoAproximado).ToList();
                    if (!await PermissoesApi.TemAsync(http, Permissoes.VerCusto))
                        foreach (var produto in itensAproximados) produto.PrecoCusto = 0;
                    return new PaginaProdutosDto { Total = proximos.Count, Pagina = indiceAproximado,
                        PorPagina = tamanhoAproximado, Itens = itensAproximados };
                }
            }
            IOrderedQueryable<Produto> ordenados = ordenar switch
            {
                0 => desc == true ? q.OrderByDescending(x => x.Codigo) : q.OrderBy(x => x.Codigo),
                2 => desc == true ? q.OrderByDescending(x => x.Categoria!.Nome) : q.OrderBy(x => x.Categoria!.Nome),
                4 => desc == true ? q.OrderByDescending(x => x.EstoqueAtual) : q.OrderBy(x => x.EstoqueAtual),
                5 => desc == true ? q.OrderByDescending(x => x.PrecoCusto) : q.OrderBy(x => x.PrecoCusto),
                6 => desc == true ? q.OrderByDescending(x => x.PrecoVenda) : q.OrderBy(x => x.PrecoVenda),
                _ => desc == true ? q.OrderByDescending(x => x.Descricao) : q.OrderBy(x => x.Descricao)
            };
            var tamanho = Math.Clamp(porPagina ?? 20, 10, 100);
            var indice = Math.Clamp(pagina ?? 0, 0, Math.Max(0, (total - 1) / tamanho));
            var itens = await ordenados.ThenBy(x => x.Id).Include(x => x.Categoria)
                .Skip(indice * tamanho).Take(tamanho).ToListAsync();
            var dto = itens.Select(ProdutoMapper.Converter).ToList();
            if (!await PermissoesApi.TemAsync(http, Permissoes.VerCusto))
                foreach (var produto in dto) produto.PrecoCusto = 0;
            return new PaginaProdutosDto { Total = total, Pagina = indice, PorPagina = tamanho, Itens = dto };
        });

        g.MapGet("/resumo", async (HttpContext http, ErpDbContext db, Fuso fuso) =>
        {
            var hoje = fuso.Hoje();
            var inicioMes = fuso.InicioDoDiaUtc(new DateOnly(hoje.Year, hoje.Month, 1));
            var produtos = db.Produtos.AsNoTracking();

            var r = new ResumoProdutosDto
            {
                Cadastrados = await produtos.CountAsync(),
                Ativos = await produtos.CountAsync(p => p.Ativo),
                NovosNoMes = await produtos.CountAsync(p => p.CriadoEm != null && p.CriadoEm >= inicioMes),
                EstoqueBaixo = await produtos.CountAsync(p => p.Ativo && p.EstoqueMinimo > 0 && p.EstoqueAtual <= p.EstoqueMinimo),
                SemEstoque = await produtos.CountAsync(p => p.Ativo && p.EstoqueAtual <= 0),
                // Mesma regra da tela Cargas: nunca publicado (e ativo) ou diferente do que está nos caixas.
                PendentesCarga = await (
                    from p in db.Produtos.AsNoTracking()
                    join pub in db.ProdutosPublicados.AsNoTracking() on p.Id equals pub.Id into publicados
                    from pub in publicados.DefaultIfEmpty()
                    where (pub == null && p.Ativo) ||
                          (pub != null && (p.Codigo != pub.Codigo || p.CodigoBarras != pub.CodigoBarras ||
                                           p.Descricao != pub.Descricao || p.Unidade != pub.Unidade ||
                                           p.PrecoVenda != pub.PrecoVenda || p.Ativo != pub.Ativo ||
                                           p.PrecoAtacado != pub.PrecoAtacado || p.QtdMinimaAtacado != pub.QtdMinimaAtacado))
                    select p.Id).CountAsync()
            };

            // Valor vendido só para quem vê relatórios.
            if (await PermissoesApi.TemAsync(http, Permissoes.VerVendas))
            {
                var hojeIni = fuso.InicioDoDiaUtc(hoje);
                var amanhaIni = fuso.InicioDoDiaUtc(hoje.AddDays(1));
                var ontemIni = fuso.InicioDoDiaUtc(hoje.AddDays(-1));
                var concluidas = db.Vendas.AsNoTracking().Where(v => v.Status == StatusVenda.Concluida);
                r.VendasHoje = await concluidas.Where(v => v.Data >= hojeIni && v.Data < amanhaIni)
                    .SumAsync(v => (decimal?)v.Total) ?? 0m;
                r.VendasOntem = await concluidas.Where(v => v.Data >= ontemIni && v.Data < hojeIni)
                    .SumAsync(v => (decimal?)v.Total) ?? 0m;
            }
            return Results.Ok(r);
        });

        // ---------- Gráfico ----------

        // Por dia: quantidade vendida (vendas concluídas) e saldo no fim do dia.
        // O saldo é calculado de trás para frente a partir do saldo atual, usando as movimentações de estoque.
        g.MapGet("/{id:guid}/serie", async (Guid id, int? dias, ErpDbContext db, Fuso fuso) =>
        {
            var quantidadeDias = Math.Clamp(dias ?? 30, 7, 90);
            var produto = await db.Produtos.AsNoTracking().Where(p => p.Id == id)
                .Select(p => new { p.EstoqueAtual }).FirstOrDefaultAsync();
            if (produto is null) return Results.NotFound();

            var hoje = fuso.Hoje();
            var primeiro = hoje.AddDays(-(quantidadeDias - 1));
            var inicio = fuso.InicioDoDiaUtc(primeiro);

            var vendas = await db.VendaItens.AsNoTracking()
                .Where(i => i.ProdutoId == id && i.Venda!.Status == StatusVenda.Concluida && i.Venda.Data >= inicio)
                .Select(i => new { i.Venda!.Data, i.Quantidade })
                .ToListAsync();
            var movimentos = await db.MovimentosEstoque.AsNoTracking()
                .Where(m => m.ProdutoId == id && m.Data >= inicio)
                .Select(m => new { m.Data, m.Quantidade })
                .ToListAsync();

            var vendidoPorDia = vendas.GroupBy(v => DateOnly.FromDateTime(fuso.ParaLocal(v.Data)))
                .ToDictionary(x => x.Key, x => x.Sum(v => v.Quantidade));
            var variacaoPorDia = movimentos.GroupBy(m => DateOnly.FromDateTime(fuso.ParaLocal(m.Data)))
                .ToDictionary(x => x.Key, x => x.Sum(m => m.Quantidade));
            // Movimento com data depois de hoje (relógio adiantado): já está no saldo atual.
            var saldo = produto.EstoqueAtual - variacaoPorDia.Where(x => x.Key > hoje).Sum(x => x.Value);

            var lista = new List<SerieProdutoDto>(quantidadeDias);
            for (var dia = hoje; dia >= primeiro; dia = dia.AddDays(-1))
            {
                lista.Add(new SerieProdutoDto { Dia = dia, Vendido = vendidoPorDia.GetValueOrDefault(dia), Estoque = saldo });
                saldo -= variacaoPorDia.GetValueOrDefault(dia);
            }
            lista.Reverse();
            return Results.Ok(lista);
        });
    }


}
