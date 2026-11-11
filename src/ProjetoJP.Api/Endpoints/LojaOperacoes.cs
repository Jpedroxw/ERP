using ProjetoJP.Api.Dados;
using ProjetoJP.Api.Servicos;
using ProjetoJP.Shared;
using Microsoft.EntityFrameworkCore;

namespace ProjetoJP.Api.Endpoints;

public static class LojaOperacoes
{
    public record ItemDevolucao(Guid ItemId, decimal Quantidade);
    public record DevolucaoPedido(List<ItemDevolucao> Itens);
    public record DevolucaoResposta(decimal Valor, decimal ValorNoFiado, decimal ValorEmDinheiro);
    public record VendaParaDevolver(Guid Id, int Numero, string Terminal, DateTime Data, string? Cliente,
        decimal Total, decimal ValorDevolvido, bool TemFiado, List<ItemParaDevolver> Itens);
    public record ItemParaDevolver(Guid Id, string Descricao, string Unidade, decimal Quantidade, decimal JaDevolvida,
        decimal PrecoUnitario, decimal Total);
    public record FiadoPedido(decimal Valor, string? Observacao);
    public record FiadoResposta(decimal Saldo, List<FiadoLinha> Lancamentos);
    public record FiadoLinha(DateTime Data, decimal Valor, string? Observacao);
    public record EtiquetaProduto(Guid Id, string Codigo, string? CodigoBarras, string Descricao, string Unidade,
        decimal Preco, decimal? PrecoPromocional, DateOnly? PromocaoAte, bool PrecoNovoNaoEnviado,
        decimal? PrecoAtacado = null, decimal? AtacadoAPartirDe = null, DateOnly? AtacadoAte = null);
    public record ClienteFiado(Guid Id, string Nome, string? CpfCnpj, string? Telefone, decimal Saldo);

    public static void MapLojaOperacoes(this IEndpointRouteBuilder app)
    {
        var vendas = app.MapGroup("/api/vendas").RequireAuthorization("Gerente");

        // Busca a venda para devolver. O número se repete entre caixas (cada caixa conta do 1),
        // por isso a resposta traz o caixa, a data e o total para o usuário escolher a certa.
        vendas.MapGet("/", async (int? numero, DateOnly? data, Guid? terminal, ErpDbContext db, Fuso fuso) =>
        {
            var q = db.Vendas.AsNoTracking().Where(v => v.Status == StatusVenda.Concluida);
            if (numero is int n) q = q.Where(v => v.Numero == n);
            if (terminal is Guid t) q = q.Where(v => v.TerminalId == t);
            if (data is DateOnly d)
            {
                var ini = fuso.InicioDoDiaUtc(d);
                var fim = fuso.InicioDoDiaUtc(d.AddDays(1));
                q = q.Where(v => v.Data >= ini && v.Data < fim);
            }
            var linhas = await q.OrderByDescending(v => v.Data).Take(50).Include(v => v.Itens).ToListAsync();

            var idsClientes = linhas.Where(v => v.ClienteId != null).Select(v => v.ClienteId!.Value).Distinct().ToList();
            var nomes = await db.Clientes.AsNoTracking().Where(c => idsClientes.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Nome);
            var terminais = await db.Terminais.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Nome);
            var idsProdutos = linhas.SelectMany(v => v.Itens).Select(i => i.ProdutoId).Distinct().ToList();
            var unidades = await db.Produtos.AsNoTracking().Where(p => idsProdutos.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Unidade);
            var idsVendas = linhas.Select(v => v.Id).ToList();
            var comFiado = (await db.LancamentosFiado.AsNoTracking()
                .Where(l => l.VendaId != null && idsVendas.Contains(l.VendaId.Value))
                .Select(l => l.VendaId!.Value).Distinct().ToListAsync()).ToHashSet();

            var lista = linhas.Select(v => new VendaParaDevolver(
                v.Id, v.Numero, terminais.GetValueOrDefault(v.TerminalId, ""), fuso.ParaLocal(v.Data),
                v.ClienteId is { } cid && nomes.TryGetValue(cid, out var nome) ? nome : null,
                v.Total, v.ValorDevolvido, comFiado.Contains(v.Id),
                v.Itens.Select(i => new ItemParaDevolver(i.Id, i.Descricao, unidades.GetValueOrDefault(i.ProdutoId, "UN"),
                    i.Quantidade, i.QuantidadeDevolvida, i.PrecoUnitario, i.Total)).ToList())).ToList();
            return Results.Ok(lista);
        });

        vendas.MapPost("/{id:guid}/devolver", async (Guid id, DevolucaoPedido req, ErpDbContext db, HttpContext http) =>
        {
            if (req.Itens is null || req.Itens.Count == 0) return Results.BadRequest(new { erro = "Informe o item e a quantidade." });
            if (req.Itens.Count > 200 || req.Itens.Select(x => x.ItemId).Distinct().Count() != req.Itens.Count)
                return Results.BadRequest(new { erro = "Cada item só pode aparecer uma vez na devolução." });

            await using var tx = await db.Database.BeginTransactionAsync();
            // Trava a venda: duas devoluções da mesma venda ao mesmo tempo (ou devolução junto com o
            // cancelamento vindo do caixa) esperam uma pela outra em vez de devolver duas vezes.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Vendas\" WHERE \"Id\" = {id} FOR UPDATE");
            var venda = await db.Vendas.Include(v => v.Itens).FirstOrDefaultAsync(v => v.Id == id);
            if (venda is null || venda.Status != StatusVenda.Concluida)
                return Results.NotFound(new { erro = "Venda concluída não encontrada (pode ter sido cancelada no caixa)." });

            var usuario = http.User.UsuarioId();
            decimal valor = 0;
            foreach (var pedido in req.Itens)
            {
                var item = venda.Itens.FirstOrDefault(i => i.Id == pedido.ItemId);
                if (item is null) return Results.BadRequest(new { erro = "Item não pertence a esta venda." });
                if (pedido.Quantidade <= 0) return Results.BadRequest(new { erro = "A quantidade tem de ser maior que zero." });
                var produto = await db.Produtos.FindAsync(item.ProdutoId);
                if (produto is null) return Results.BadRequest(new { erro = $"Produto de \"{item.Descricao}\" não encontrado." });
                if (!Devolucao.QuantidadeValida(produto.Unidade, pedido.Quantidade))
                    return Results.BadRequest(new { erro = $"\"{item.Descricao}\" é vendido por {produto.Unidade}: devolva em quantidade inteira." });
                var restante = item.Quantidade - item.QuantidadeDevolvida;
                if (pedido.Quantidade > restante)
                    return Results.BadRequest(new { erro = $"De \"{item.Descricao}\" só restam {restante:0.###} para devolver." });

                item.QuantidadeDevolvida += pedido.Quantidade;
                valor += Devolucao.Valor(item.Total, item.Quantidade, pedido.Quantidade, venda.Subtotal, venda.Total);
                Estoque.Movimentar(db, produto, TipoMovimentoEstoque.DevolucaoCliente, pedido.Quantidade,
                    $"Devolução da venda nº {venda.Numero}", usuario, venda.Id);
            }

            valor = Math.Min(valor, venda.Total - venda.ValorDevolvido);
            venda.ValorDevolvido += valor;

            // Venda no fiado: a devolução abate a dívida dessa venda primeiro; o que passar disso volta em dinheiro.
            decimal noFiado = 0;
            if (venda.ClienteId is Guid clienteId && valor > 0)
            {
                var emAberto = await db.LancamentosFiado.Where(l => l.VendaId == venda.Id).SumAsync(l => (decimal?)l.Valor) ?? 0;
                noFiado = Math.Min(valor, Math.Max(0, emAberto));
                if (noFiado > 0)
                    await ContaFiado.LancarAsync(db, clienteId, venda.Id, -noFiado, $"Devolução da venda nº {venda.Numero}", usuario);
            }

            try
            {
                await db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new { erro = "O estoque desse produto mudou neste instante (venda chegando de um caixa). Tente de novo." });
            }
            return Results.Ok(new DevolucaoResposta(valor, noFiado, valor - noFiado));
        });

        // Etiqueta de gôndola: o preço que o CAIXA cobra hoje (catálogo publicado + promoção em vigor),
        // não o preço em edição na Retaguarda — senão a prateleira mostra um preço e o caixa cobra outro.
        app.MapGet("/api/etiquetas", async (ErpDbContext db, Fuso fuso) =>
        {
            var hoje = fuso.Hoje();
            var publicados = await db.ProdutosPublicados.AsNoTracking().Where(p => p.Ativo).OrderBy(p => p.Descricao).ToListAsync();
            var emEdicao = await db.Produtos.AsNoTracking()
                .Select(p => new { p.Id, p.PrecoVenda, p.PrecoAtacado, p.QtdMinimaAtacado })
                .ToDictionaryAsync(p => p.Id);
            var emVigor = await db.Promocoes.AsNoTracking()
                .Where(x => x.Ativa && x.Inicio <= hoje && x.Fim >= hoje)
                .ToListAsync();
            var promocoes = emVigor.Where(x => x.QuantidadeMinima == 0)
                .GroupBy(x => x.ProdutoId)
                .ToDictionary(g => g.Key, g => g.OrderBy(x => x.PrecoPromocional).First());
            var promocoesAtacado = emVigor.Where(x => x.QuantidadeMinima > 0)
                .GroupBy(x => x.ProdutoId)
                .ToDictionary(g => g.Key, g => g.OrderBy(x => x.PrecoPromocional).First());
            return Results.Ok(publicados.Select(p =>
            {
                promocoes.TryGetValue(p.Id, out var promo);
                // Mesma regra do caixa: a menor promoção em vigor substitui o preço.
                var promoValida = promo is not null && promo.PrecoPromocional != p.PrecoVenda;
                var varejo = promoValida ? promo!.PrecoPromocional : p.PrecoVenda;

                // Atacado: a promoção de atacado em vigor ou o preço fixo do produto, o que for menor.
                decimal? precoAtacado = null, aPartirDe = null;
                DateOnly? atacadoAte = null;
                if (p.PrecoAtacado > 0 && p.QtdMinimaAtacado > 0 && p.PrecoAtacado < varejo)
                    (precoAtacado, aPartirDe) = (p.PrecoAtacado, p.QtdMinimaAtacado);
                if (promocoesAtacado.TryGetValue(p.Id, out var pa) && pa.PrecoPromocional < varejo &&
                    (precoAtacado is null || pa.PrecoPromocional < precoAtacado))
                    (precoAtacado, aPartirDe, atacadoAte) = (pa.PrecoPromocional, pa.QuantidadeMinima, pa.Fim);

                var novo = emEdicao.GetValueOrDefault(p.Id);
                var naoEnviado = novo is not null &&
                                 (novo.PrecoVenda != p.PrecoVenda || novo.PrecoAtacado != p.PrecoAtacado || novo.QtdMinimaAtacado != p.QtdMinimaAtacado);
                return new EtiquetaProduto(p.Id, p.Codigo, p.CodigoBarras, p.Descricao, p.Unidade, p.PrecoVenda,
                    promoValida ? promo!.PrecoPromocional : null, promoValida ? promo!.Fim : null,
                    naoEnviado, precoAtacado, aPartirDe, atacadoAte);
            }).ToList());
        }).RequireAuthorization("Gerente");

        var fiado = app.MapGroup("/api/clientes").RequireAuthorization("Gerente");

        // Clientes com o saldo do fiado. "devendo=true" traz só quem deve.
        fiado.MapGet("/fiado", async (bool? devendo, ErpDbContext db) =>
        {
            var q = db.Clientes.AsNoTracking().Where(c => c.Ativo || c.SaldoFiado != 0);
            if (devendo == true) q = q.Where(c => c.SaldoFiado > 0);
            return Results.Ok(await q.OrderBy(c => c.Nome).Take(2000)
                .Select(c => new ClienteFiado(c.Id, c.Nome, c.CpfCnpj, c.Telefone, c.SaldoFiado))
                .ToListAsync());
        });
        fiado.MapGet("/{id:guid}/fiado", async (Guid id, ErpDbContext db, Fuso fuso) =>
        {
            var cliente = await db.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
            if (cliente is null) return Results.NotFound(new { erro = "Cliente não encontrado." });
            var linhas = await db.LancamentosFiado.AsNoTracking()
                .Where(l => l.ClienteId == id)
                .OrderByDescending(l => l.AtualizadoEm)
                .Take(50)
                .Select(l => new { l.AtualizadoEm, l.Valor, l.Observacao })
                .ToListAsync();
            return Results.Ok(new FiadoResposta(cliente.SaldoFiado,
                linhas.Select(l => new FiadoLinha(fuso.ParaLocal(l.AtualizadoEm), l.Valor, l.Observacao)).ToList()));
        });

        fiado.MapPost("/{id:guid}/fiado", async (Guid id, FiadoPedido req, ErpDbContext db, HttpContext http) =>
        {
            if (req.Valor == 0) return Results.BadRequest(new { erro = "Informe o valor. Negativo recebe, positivo lança dívida." });
            if (Math.Abs(req.Valor) > 999_999_999m) return Results.BadRequest(new { erro = "Valor fora do limite." });
            var valor = Math.Round(req.Valor, 2, MidpointRounding.AwayFromZero);
            if (!await db.Clientes.AnyAsync(c => c.Id == id)) return Results.NotFound(new { erro = "Cliente não encontrado." });

            await using var tx = await db.Database.BeginTransactionAsync();
            var observacao = string.IsNullOrWhiteSpace(req.Observacao) ? (valor < 0 ? "Recebimento" : "Lançamento") : req.Observacao.Trim();
            // Recebimento só entra se não deixar o saldo negativo — conferido no próprio UPDATE, sem janela entre ler e gravar.
            if (!await ContaFiado.LancarAsync(db, id, null, valor, observacao, http.User.UsuarioId(), naoNegativo: valor < 0))
                return Results.BadRequest(new { erro = "O recebimento passa do que o cliente deve." });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            var saldo = await db.Clientes.AsNoTracking().Where(c => c.Id == id).Select(c => c.SaldoFiado).FirstAsync();
            return Results.Ok(new { saldo });
        });
    }
}
