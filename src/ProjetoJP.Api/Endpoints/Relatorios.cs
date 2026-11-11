using ProjetoJP.Api.Dados;
using ProjetoJP.Api.Servicos;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ProjetoJP.Api.Endpoints;

public static class RelatorioEndpoints
{
    public static void MapRelatorios(this IEndpointRouteBuilder app)
    {
        // Datas "de" e "ate" no formato AAAA-MM-DD, inclusivas, no fuso da loja.
        var g = app.MapGroup("/api/relatorios").RequireAuthorization(PermissoesApi.Politica(Permissoes.VerVendas));

        g.MapGet("/vendas-por-dia", async (DateOnly de, DateOnly ate, ErpDbContext db, Fuso fuso) =>
        {
            var (ini, fim) = Periodo(de, ate, fuso);
            var vendas = await db.Vendas.AsNoTracking()
                .Where(v => v.Data >= ini && v.Data < fim && v.Status == StatusVenda.Concluida)
                // Devolução parcial sai do total do dia da venda.
                .Select(v => new { v.Data, Total = v.Total - v.ValorDevolvido })
                .ToListAsync();

            return vendas
                .GroupBy(v => DateOnly.FromDateTime(fuso.ParaLocal(v.Data)))
                .OrderBy(gr => gr.Key)
                .Select(gr => new VendasPorDiaDto
                {
                    Dia = gr.Key,
                    Quantidade = gr.Count(),
                    Total = gr.Sum(x => x.Total),
                    TicketMedio = Math.Round(gr.Sum(x => x.Total) / gr.Count(), 2)
                })
                .ToList();
        });

        g.MapGet("/mais-vendidos", async (DateOnly de, DateOnly ate, ErpDbContext db, Fuso fuso) =>
        {
            var (ini, fim) = Periodo(de, ate, fuso);
            var dados = await db.VendaItens.AsNoTracking()
                .Where(i => i.Venda!.Data >= ini && i.Venda.Data < fim && i.Venda.Status == StatusVenda.Concluida)
                .GroupBy(i => i.ProdutoId)
                // Itens devolvidos não contam como vendidos.
                .Select(gr => new
                {
                    ProdutoId = gr.Key,
                    Quantidade = gr.Sum(x => x.Quantidade - x.QuantidadeDevolvida),
                    Total = gr.Sum(x => x.Total - x.Total * x.QuantidadeDevolvida / x.Quantidade)
                })
                .Where(x => x.Quantidade > 0)
                .OrderByDescending(x => x.Quantidade)
                .Take(100)
                .ToListAsync();

            var ids = dados.Select(x => x.ProdutoId).ToList();
            var produtos = await db.Produtos.AsNoTracking().Where(p => ids.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => new { p.Codigo, p.Descricao, p.Unidade });

            return dados.Select(x =>
            {
                produtos.TryGetValue(x.ProdutoId, out var p);
                return new ProdutoMaisVendidoDto
                {
                    Codigo = p?.Codigo,
                    Descricao = p?.Descricao ?? "(produto removido)",
                    Quantidade = x.Quantidade,
                    Total = x.Total,
                    Unidade = p?.Unidade
                };
            }).ToList();
        });

        // Lucro por produto: o que entrou (já sem descontos e devoluções) menos o custo gravado em cada venda.
        // O desconto dado no total da venda é repartido entre os itens, na proporção do valor de cada um.
        // Venda antiga, sem custo gravado, usa o custo atual do cadastro (avisa na coluna Observação).
        // Mostra custo: além de "Ver vendas", pede "Ver preço de custo".
        g.MapGet("/lucro", async (DateOnly de, DateOnly ate, ErpDbContext db, Fuso fuso) =>
        {
            var (ini, fim) = Periodo(de, ate, fuso);
            var itens = await db.VendaItens.AsNoTracking()
                .Where(i => i.Venda!.Data >= ini && i.Venda.Data < fim && i.Venda.Status == StatusVenda.Concluida)
                .Select(i => new
                {
                    i.ProdutoId, i.Quantidade, i.QuantidadeDevolvida, i.Total, i.CustoUnitario,
                    SubtotalVenda = i.Venda!.Subtotal, DescontoVenda = i.Venda.Desconto
                })
                .ToListAsync();

            var ids = itens.Select(x => x.ProdutoId).Distinct().ToList();
            var produtos = await db.Produtos.AsNoTracking().Where(p => ids.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => new { p.Codigo, p.Descricao, p.Unidade, p.PrecoCusto });

            return itens
                .Where(x => x.Quantidade > 0)
                .GroupBy(x => x.ProdutoId)
                .Select(gr =>
                {
                    produtos.TryGetValue(gr.Key, out var p);
                    decimal quantidade = 0, vendido = 0, custo = 0;
                    bool semCusto = false, custoAtual = false;
                    foreach (var x in gr)
                    {
                        var ficou = x.Quantidade - x.QuantidadeDevolvida;
                        if (ficou <= 0) continue;
                        var desconto = x.SubtotalVenda > 0 ? x.DescontoVenda * x.Total / x.SubtotalVenda : 0;
                        vendido += (x.Total - desconto) * ficou / x.Quantidade;
                        var custoUnitario = x.CustoUnitario;
                        if (custoUnitario <= 0 && p is { PrecoCusto: > 0 }) { custoUnitario = p.PrecoCusto; custoAtual = true; }
                        if (custoUnitario <= 0) semCusto = true;
                        custo += custoUnitario * ficou;
                        quantidade += ficou;
                    }
                    vendido = Math.Round(vendido, 2);
                    custo = Math.Round(custo, 2);
                    return new LucroProdutoDto
                    {
                        Codigo = p?.Codigo,
                        Descricao = p?.Descricao ?? "(produto removido)",
                        Unidade = p?.Unidade,
                        Quantidade = quantidade,
                        Vendido = vendido,
                        Custo = custo,
                        Lucro = vendido - custo,
                        Margem = semCusto || vendido <= 0 ? null : Math.Round((vendido - custo) / vendido * 100, 1),
                        SemCusto = semCusto,
                        Observacao = semCusto ? "sem custo no cadastro" : custoAtual ? "custo atual do cadastro (venda antiga)" : null
                    };
                })
                .Where(x => x.Quantidade > 0)
                .OrderByDescending(x => x.Lucro)
                .ToList();
        }).RequireAuthorization(PermissoesApi.Politica(Permissoes.VerCusto));

        g.MapGet("/por-forma", async (DateOnly de, DateOnly ate, ErpDbContext db, Fuso fuso) =>
        {
            var (ini, fim) = Periodo(de, ate, fuso);
            return await PorFormaAsync(db, ini, fim);
        });

        g.MapGet("/estoque-baixo", async (ErpDbContext db) =>
            await db.Produtos.AsNoTracking()
                .Where(p => p.Ativo && p.EstoqueAtual <= p.EstoqueMinimo)
                .OrderBy(p => p.Descricao)
                .Select(p => new EstoqueBaixoDto
                {
                    Codigo = p.Codigo,
                    Descricao = p.Descricao,
                    Unidade = p.Unidade,
                    EstoqueAtual = p.EstoqueAtual,
                    EstoqueMinimo = p.EstoqueMinimo
                })
                .ToListAsync());

        // Totais do período todo, para o rodapé da "Lista de vendas" não ficar errado quando a lista é cortada.
        g.MapGet("/vendas-totais", async (DateOnly de, DateOnly ate, ErpDbContext db, Fuso fuso) =>
        {
            var (ini, fim) = Periodo(de, ate, fuso);
            var q = db.Vendas.AsNoTracking().Where(v => v.Data >= ini && v.Data < fim);
            return new VendasTotaisDto
            {
                Quantidade = await q.CountAsync(),
                Canceladas = await q.CountAsync(v => v.Status == StatusVenda.Cancelada),
                Total = await q.Where(v => v.Status == StatusVenda.Concluida).SumAsync(v => (decimal?)(v.Total - v.ValorDevolvido)) ?? 0,
                Devolvido = await q.Where(v => v.Status == StatusVenda.Concluida).SumAsync(v => (decimal?)v.ValorDevolvido) ?? 0,
                LimiteLista = LimiteListaVendas
            };
        });

        // Painel online (celular): o resumo da tela "Início" numa consulta só.
        // periodo = hoje | ontem | 7dias | mes — as datas saem do fuso da loja, não do celular.
        g.MapGet("/painel", async (string? periodo, ErpDbContext db, Fuso fuso) =>
        {
            var hoje = fuso.Hoje();
            var p = (periodo ?? "hoje").Trim().ToLowerInvariant();
            DateOnly de, ate, deAnt, ateAnt;
            string comparado;
            switch (p)
            {
                case "ontem":
                    de = ate = hoje.AddDays(-1);
                    deAnt = ateAnt = de.AddDays(-7);
                    comparado = $"{DiaDaSemana(de)} anterior";
                    break;
                case "7dias":
                    de = hoje.AddDays(-6);
                    ate = hoje;
                    deAnt = hoje.AddDays(-13);
                    ateAnt = hoje.AddDays(-7);
                    comparado = "os 7 dias anteriores";
                    break;
                case "mes":
                    de = new DateOnly(hoje.Year, hoje.Month, 1);
                    ate = hoje;
                    deAnt = de.AddMonths(-1);
                    ateAnt = deAnt.AddDays(Math.Min(hoje.Day, DateTime.DaysInMonth(deAnt.Year, deAnt.Month)) - 1);
                    comparado = $"o mesmo período de {Meses[deAnt.Month - 1]}";
                    break;
                default:
                    p = "hoje";
                    de = ate = hoje;
                    deAnt = ateAnt = hoje.AddDays(-7);
                    comparado = DiaDaSemana(hoje) + (hoje.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? " passado" : " passada");
                    break;
            }

            // Uma leitura só, do dia mais antigo usado (comparação ou gráfico) até o fim do período.
            var inicio = new[] { de, deAnt, ate.AddDays(-6) }.Min();
            var (iniUtc, fimUtc) = Periodo(inicio, ate, fuso);
            var vendas = (await db.Vendas.AsNoTracking()
                    .Where(v => v.Data >= iniUtc && v.Data < fimUtc)
                    .Select(v => new { v.Data, v.Total, v.ValorDevolvido, v.Status, v.TerminalId })
                    .ToListAsync())
                .Select(v => new
                {
                    Dia = DateOnly.FromDateTime(fuso.ParaLocal(v.Data)),
                    Liquido = v.Total - v.ValorDevolvido,
                    v.ValorDevolvido, v.Status, v.TerminalId
                })
                .ToList();

            var doPeriodo = vendas.Where(v => v.Dia >= de && v.Dia <= ate).ToList();
            var concluidas = doPeriodo.Where(v => v.Status == StatusVenda.Concluida).ToList();
            var total = concluidas.Sum(v => v.Liquido);
            var terminais = await db.Terminais.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Nome);
            var (iniPeriodo, fimPeriodo) = Periodo(de, ate, fuso);

            return new PainelResumoDto
            {
                Periodo = p,
                Hoje = hoje,
                De = de,
                Ate = ate,
                AtualizadoEm = fuso.ParaLocal(DateTime.UtcNow),
                Total = total,
                Vendas = concluidas.Count,
                Canceladas = doPeriodo.Count(v => v.Status == StatusVenda.Cancelada),
                Devolvido = concluidas.Sum(v => v.ValorDevolvido),
                TicketMedio = concluidas.Count == 0 ? 0 : Math.Round(total / concluidas.Count, 2),
                TotalAnterior = vendas.Where(v => v.Dia >= deAnt && v.Dia <= ateAnt && v.Status == StatusVenda.Concluida).Sum(v => v.Liquido),
                ComparadoCom = comparado,
                Dias = Enumerable.Range(0, 7).Select(i => ate.AddDays(i - 6)).Select(d =>
                {
                    var doDia = vendas.Where(v => v.Dia == d && v.Status == StatusVenda.Concluida).ToList();
                    return new PainelDiaDto { Dia = d, Total = doDia.Sum(v => v.Liquido), Vendas = doDia.Count };
                }).ToList(),
                Formas = await PorFormaAsync(db, iniPeriodo, fimPeriodo),
                Caixas = concluidas
                    .GroupBy(v => v.TerminalId)
                    .Select(gr => new PainelCaixaDto
                    {
                        Terminal = terminais.GetValueOrDefault(gr.Key, "Caixa"),
                        Total = gr.Sum(v => v.Liquido),
                        Vendas = gr.Count()
                    })
                    .OrderBy(c => c.Terminal, StringComparer.CurrentCultureIgnoreCase)
                    .ToList()
            };
        });

        g.MapGet("/vendas", async (DateOnly de, DateOnly ate, ErpDbContext db, Fuso fuso) =>
        {
            var (ini, fim) = Periodo(de, ate, fuso);
            var vendas = await db.Vendas.AsNoTracking()
                .Where(v => v.Data >= ini && v.Data < fim)
                .OrderByDescending(v => v.Data)
                .Take(LimiteListaVendas)
                .ToListAsync();

            var terminais = await db.Terminais.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Nome);
            var usuarios = await db.Usuarios.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Nome);
            var idsClientes = vendas.Where(v => v.ClienteId != null).Select(v => v.ClienteId!.Value).Distinct().ToList();
            var clientes = await db.Clientes.AsNoTracking().Where(c => idsClientes.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Nome);
            // Formas de pagamento de cada venda (ex.: "PIX + Dinheiro"), na ordem do maior valor.
            var idsVendas = vendas.Select(v => v.Id).ToList();
            var formas = await db.FormasPagamento.AsNoTracking().ToDictionaryAsync(f => f.Id, f => f.Descricao);
            var pagamentos = (await db.VendaPagamentos.AsNoTracking()
                    .Where(p => idsVendas.Contains(p.VendaId))
                    .Select(p => new { p.VendaId, p.FormaPagamentoId, p.Valor })
                    .ToListAsync())
                .GroupBy(p => p.VendaId)
                .ToDictionary(gr => gr.Key, gr => string.Join(" + ", gr
                    .GroupBy(p => p.FormaPagamentoId)
                    .OrderByDescending(f => f.Sum(x => x.Valor))
                    .Select(f => formas.GetValueOrDefault(f.Key, "(forma removida)"))));

            return vendas.Select(v => new VendaResumoDto
            {
                Id = v.Id,
                Numero = v.Numero,
                Terminal = terminais.GetValueOrDefault(v.TerminalId, ""),
                Data = fuso.ParaLocal(v.Data),
                Operador = usuarios.GetValueOrDefault(v.UsuarioId, ""),
                Cliente = v.ClienteId != null ? clientes.GetValueOrDefault(v.ClienteId.Value) : null,
                Total = v.Total,
                Devolvido = v.ValorDevolvido,
                Status = v.Status,
                Pagamento = pagamentos.GetValueOrDefault(v.Id)
            }).ToList();
        });
    }

    private const int LimiteListaVendas = 2000;

    private static readonly string[] Meses =
        ["janeiro", "fevereiro", "março", "abril", "maio", "junho", "julho", "agosto", "setembro", "outubro", "novembro", "dezembro"];

    private static string DiaDaSemana(DateOnly dia) => dia.DayOfWeek switch
    {
        DayOfWeek.Sunday => "domingo",
        DayOfWeek.Monday => "segunda",
        DayOfWeek.Tuesday => "terça",
        DayOfWeek.Wednesday => "quarta",
        DayOfWeek.Thursday => "quinta",
        DayOfWeek.Friday => "sexta",
        _ => "sábado"
    };

    /// <summary>Vendas por forma de pagamento no intervalo (UTC), com a linha "(devoluções)" quando houver.</summary>
    private static async Task<List<VendasPorFormaDto>> PorFormaAsync(ErpDbContext db, DateTime ini, DateTime fim)
    {
        var dados = await db.VendaPagamentos.AsNoTracking()
            .Where(p => p.Venda!.Data >= ini && p.Venda.Data < fim && p.Venda.Status == StatusVenda.Concluida)
            .GroupBy(p => p.FormaPagamentoId)
            .Select(gr => new { FormaId = gr.Key, Quantidade = gr.Count(), Total = gr.Sum(x => x.Valor) })
            .ToListAsync();

        var formas = await db.FormasPagamento.AsNoTracking().ToDictionaryAsync(f => f.Id, f => f.Descricao);

        var lista = dados.Select(x => new VendasPorFormaDto
        {
            Forma = formas.GetValueOrDefault(x.FormaId, "(forma removida)"),
            Quantidade = x.Quantidade,
            Total = x.Total
        }).OrderByDescending(x => x.Total).ToList();

        // O dinheiro devolvido ao cliente sai numa linha própria, para o total bater com as vendas.
        var comDevolucao = db.Vendas.AsNoTracking()
            .Where(v => v.Data >= ini && v.Data < fim && v.Status == StatusVenda.Concluida && v.ValorDevolvido > 0);
        var qtdDevolucoes = await comDevolucao.CountAsync();
        if (qtdDevolucoes > 0)
            lista.Add(new VendasPorFormaDto
            {
                Forma = "(devoluções)",
                Quantidade = qtdDevolucoes,
                Total = -(await comDevolucao.SumAsync(v => v.ValorDevolvido))
            });
        return lista;
    }

    private static (DateTime ini, DateTime fim) Periodo(DateOnly de, DateOnly ate, Fuso fuso) =>
        (fuso.InicioDoDiaUtc(de), fuso.InicioDoDiaUtc(ate.AddDays(1)));
}
