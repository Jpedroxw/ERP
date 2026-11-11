using ProjetoJP.Api.Dados;
using ProjetoJP.Api.Servicos;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ProjetoJP.Api.Endpoints;

/// <summary>
/// Endpoints usados pelo PDV. A autenticação é pela chave do terminal
/// (cabeçalho X-Terminal-Key), cadastrada na Retaguarda em Cadastros &gt; Terminais.
/// Assim o caixa sincroniza mesmo sem ninguém logado na API.
/// </summary>
public static class SyncEndpoints
{
    private const string ItemTerminal = "terminal";

    public static void MapSincronizacao(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/sync").AddEndpointFilter(async (ctx, next) =>
        {
            var chave = ctx.HttpContext.Request.Headers["X-Terminal-Key"].ToString();
            if (string.IsNullOrWhiteSpace(chave))
                return Results.Json(new { erro = "Chave do terminal não informada." }, statusCode: 401);

            var db = ctx.HttpContext.RequestServices.GetRequiredService<ErpDbContext>();
            var terminal = await db.Terminais.FirstOrDefaultAsync(t => t.Chave == chave && t.Ativo);
            if (terminal is null)
                return Results.Json(new { erro = "Terminal não cadastrado ou inativo." }, statusCode: 401);

            ctx.HttpContext.Items[ItemTerminal] = terminal;
            return await next(ctx);
        });

        // Cadastros alterados desde a última sincronização (inclui inativos, para o PDV desativar).
        g.MapGet("/receber", async (DateTime? desde, HttpContext http, ErpDbContext db) =>
        {
            var terminal = (Terminal)http.Items[ItemTerminal]!;
            var agora = DateTime.UtcNow;
            // Margem de 1 minuto para não perder nada gravado no mesmo instante; o PDV faz upsert.
            var corte = desde is null ? new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc) : desde.Value.Utc().AddMinutes(-1);

            var resp = new SyncReceberResponse
            {
                Servidor = agora,
                NomeTerminal = terminal.Nome,
                Usuarios = (await db.UsuariosPublicados.AsNoTracking().Where(x => x.PublicadoEm >= corte).ToListAsync())
                    .Select(x => new SyncUsuario
                    {
                        Id = x.Id, Nome = x.Nome, Login = x.Login, SenhaHash = x.SenhaHash, Perfil = x.Perfil, Ativo = x.Ativo,
                        Permissoes = [.. Permissoes.Efetivas(x.Perfil, x.Permissoes)]
                    }).ToList(),
                Produtos = (await db.ProdutosPublicados.AsNoTracking()
                    .Include(x => x.Produto).ThenInclude(p => p.Categoria)
                    .Where(x => x.PublicadoEm >= corte || x.Produto.AtualizadoEm >= corte)
                    .ToListAsync())
                    .Select(x =>
                    {
                        var dto = ProdutoMapper.Converter(x.Produto);
                        dto.PrecoCusto = 0; // o caixa não usa o custo: não sai do servidor
                        dto.Codigo = x.Codigo;
                        dto.CodigoBarras = x.CodigoBarras;
                        dto.Descricao = x.Descricao;
                        dto.Unidade = x.Unidade;
                        dto.PrecoVenda = x.PrecoVenda;
                        dto.PrecoAtacado = x.PrecoAtacado;
                        dto.QtdMinimaAtacado = x.QtdMinimaAtacado;
                        dto.Ativo = x.Ativo;
                        return dto;
                    }).ToList(),
                Promocoes = await db.Promocoes.AsNoTracking().Where(x => x.AtualizadoEm >= corte)
                    .Select(x => new SyncPromocao
                    {
                        Id = x.Id, ProdutoId = x.ProdutoId,
                        Inicio = x.Inicio, Fim = x.Fim,
                        PrecoPromocional = x.PrecoPromocional, QuantidadeMinima = x.QuantidadeMinima, Ativa = x.Ativa
                    }).ToListAsync(),
                Clientes = (await db.Clientes.AsNoTracking().Where(x => x.AtualizadoEm >= corte).ToListAsync())
                    .Select(ClienteMapper.Converter).ToList(),
                Formas = (await db.FormasPagamento.AsNoTracking().Where(x => x.AtualizadoEm >= corte).ToListAsync())
                    .Select(FormaPagamentoMapper.Converter).ToList()
            };

            await db.Terminais.Where(t => t.Id == terminal.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.UltimaSincronizacao, agora));

            return Results.Ok(resp);
        });

        // Caixas, sangrias/suprimentos e vendas feitas no PDV. Idempotente: reenviar não duplica.
        g.MapPost("/enviar", async Task<IResult> (SyncEnviarRequest req, HttpContext http, ErpDbContext db, ILoggerFactory lf) =>
        {
            var terminal = (Terminal)http.Items[ItemTerminal]!;
            var log = lf.CreateLogger("Sync");

            if (req.Caixas is null || req.MovimentosCaixa is null || req.Vendas is null || req.Orcamentos is null)
                return Results.BadRequest(new { erro = "Listas de sincronização não podem ser nulas." });
            if (req.Caixas.Count > 20 || req.MovimentosCaixa.Count > 300 ||
                req.Vendas.Count > 100 || req.Orcamentos.Count > 50)
                return Results.BadRequest(new { erro = "Lote de sincronização acima dos limites permitidos." });
            if (req.Caixas.Any(x => x.Id == Guid.Empty || x.UsuarioAberturaId == Guid.Empty ||
                    x.ValorAbertura < 0 || !Enum.IsDefined(x.Status)) ||
                req.MovimentosCaixa.Any(x => x.Id == Guid.Empty || x.CaixaSessaoId == Guid.Empty ||
                    x.UsuarioId == Guid.Empty || x.Valor <= 0 || x.Valor > 999_999_999m || !Enum.IsDefined(x.Tipo)))
                return Results.BadRequest(new { erro = "Caixa ou movimento com identificadores e valores inválidos." });
            if (req.Caixas.Select(x => x.Id).Distinct().Count() != req.Caixas.Count ||
                req.MovimentosCaixa.Select(x => x.Id).Distinct().Count() != req.MovimentosCaixa.Count ||
                req.Vendas.Select(x => x.Id).Distinct().Count() != req.Vendas.Count ||
                req.Orcamentos.Select(x => x.Id).Distinct().Count() != req.Orcamentos.Count)
                return Results.BadRequest(new { erro = "O mesmo identificador foi enviado duas vezes no lote." });

            // Dois caixas baixando o mesmo produto ao mesmo tempo: o segundo dá conflito de versão (xmin).
            // Em vez de devolver erro (o caixa mostraria OFFLINE), o servidor relê e tenta de novo.
            for (var tentativa = 1; ; tentativa++)
            {
                try
                {
                    return await ProcessarLoteAsync(req, terminal, db, log);
                }
                catch (DbUpdateConcurrencyException) when (tentativa < 4)
                {
                    db.ChangeTracker.Clear();
                    log.LogInformation("Sync do terminal {Terminal}: conflito de estoque, tentativa {N}.", terminal.Nome, tentativa + 1);
                    await Task.Delay(Random.Shared.Next(40, 200) * tentativa);
                }
            }
        });
    }

    private static async Task<IResult> ProcessarLoteAsync(SyncEnviarRequest req, Terminal terminal, ErpDbContext db, ILogger log)
    {
            var resp = new SyncEnviarResponse();
            await using var tx = await db.Database.BeginTransactionAsync();
            // Serializa reenvios simultâneos do mesmo terminal até commit ou rollback.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(74016, hashtext({terminal.Id.ToString()}))");

            foreach (var c in req.Caixas)
            {
                var e = await db.CaixaSessoes.FirstOrDefaultAsync(x => x.Id == c.Id);
                if (e is null)
                {
                    e = new CaixaSessao { Id = c.Id, TerminalId = terminal.Id };
                    db.CaixaSessoes.Add(e);
                }
                else if (e.TerminalId != terminal.Id)
                {
                    // Abertura registrada por outro terminal desta mesma empresa: o caixa trocou de chave (ou os dados
                    // foram levados para outro computador) com o caixa aberto. Aceita o fechamento e mantém o terminal
                    // original da abertura; sem isso a abertura ficaria "aguardando envio" para sempre no caixa.
                    log.LogWarning("Caixa {Id} registrado pelo terminal {Original}; atualização recebida do terminal {Terminal} aceita.",
                        c.Id, e.TerminalId, terminal.Nome);
                }

                if (e.Status == StatusCaixa.Fechado && c.Status == StatusCaixa.Aberto)
                {
                    // Envio antigo não reabre caixa fechado. Não confirma (o caixa avisa) e segue com o resto do lote.
                    log.LogWarning("Caixa {Id} já fechado no servidor; envio como aberto ignorado.", c.Id);
                    resp.Avisos.Add("Um caixa já fechado não pode ser reaberto por um envio antigo.");
                    continue;
                }
                e.UsuarioAberturaId = c.UsuarioAberturaId;
                e.Abertura = c.Abertura.Utc();
                e.ValorAbertura = c.ValorAbertura;
                e.Fechamento = c.Fechamento.Utc();
                e.UsuarioFechamentoId = c.UsuarioFechamentoId;
                e.ValorFechamentoInformado = c.ValorFechamentoInformado;
                e.Status = c.Status;
                resp.CaixasAceitos.Add(c.Id);
            }

            foreach (var m in req.MovimentosCaixa)
            {
                // A abertura precisa existir nesta empresa (pode ter sido registrada por outro terminal dela: chave trocada).
                if (!db.CaixaSessoes.Local.Any(x => x.Id == m.CaixaSessaoId) &&
                    !await db.CaixaSessoes.AsNoTracking().AnyAsync(x => x.Id == m.CaixaSessaoId))
                {
                    log.LogWarning("Movimento {Id}: abertura de caixa inexistente no servidor; ignorado.", m.Id);
                    resp.Avisos.Add("Sangria/suprimento de uma abertura de caixa que ainda não chegou ao servidor ficou para depois.");
                    continue;
                }
                if (!await db.CaixaMovimentos.AnyAsync(x => x.Id == m.Id))
                {
                    db.CaixaMovimentos.Add(new CaixaMovimento
                    {
                        Id = m.Id,
                        CaixaSessaoId = m.CaixaSessaoId,
                        Tipo = m.Tipo,
                        Valor = m.Valor,
                        Data = m.Data.Utc(),
                        Motivo = m.Motivo,
                        UsuarioId = m.UsuarioId
                    });
                }
                resp.MovimentosAceitos.Add(m.Id);
            }

            void Recusar(VendaDto v, string motivo)
            {
                log.LogWarning("Venda {Id} (nº {Numero}) do terminal {Terminal} recusada: {Motivo}", v.Id, v.Numero, terminal.Nome, motivo);
                resp.VendasRecusadas.Add(new VendaRecusadaDto { Id = v.Id, Numero = v.Numero, Motivo = motivo });
            }

            foreach (var v in req.Vendas)
            {
                var erroVenda = ValidacaoSincronizacao.ValidarVenda(v);
                if (erroVenda is not null) { Recusar(v, erroVenda); continue; }
                // A abertura do caixa precisa existir nesta empresa. Se foi registrada por outro terminal dela (chave trocada
                // ou dados levados para outro computador com o caixa aberto), a venda entra assim mesmo: ela foi feita
                // naquela abertura. A venda fica com o terminal que a enviou.
                var sessao = db.CaixaSessoes.Local.FirstOrDefault(x => x.Id == v.CaixaSessaoId) ??
                    await db.CaixaSessoes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == v.CaixaSessaoId);
                if (sessao is null)
                { Recusar(v, "a abertura do caixa desta venda ainda não chegou ao servidor (o caixa manda de novo sozinho)."); continue; }
                if (sessao.TerminalId != terminal.Id)
                    log.LogWarning("Venda {Id} (nº {Numero}) do terminal {Terminal} aceita na abertura {Caixa}, registrada por outro terminal.",
                        v.Id, v.Numero, terminal.Nome, v.CaixaSessaoId);
                var idsProdutos = v.Itens.Select(x => x.ProdutoId).Distinct().ToList();
                if (await db.Produtos.AsNoTracking().CountAsync(x => idsProdutos.Contains(x.Id)) != idsProdutos.Count)
                { Recusar(v, "produto não encontrado neste servidor (confira se o caixa aponta para o servidor certo)."); continue; }
                var idsFormas = v.Pagamentos.Select(x => x.FormaPagamentoId).Distinct().ToList();
                if (await db.FormasPagamento.AsNoTracking().CountAsync(x => idsFormas.Contains(x.Id)) != idsFormas.Count)
                { Recusar(v, "forma de pagamento desconhecida neste servidor."); continue; }
                var custos = await db.Produtos.AsNoTracking()
                    .Where(x => idsProdutos.Contains(x.Id))
                    .ToDictionaryAsync(x => x.Id, x => x.PrecoCusto);
                var formasFiado = await db.FormasPagamento.AsNoTracking()
                    .Where(x => idsFormas.Contains(x.Id) && x.EhFiado)
                    .Select(x => x.Id)
                    .ToListAsync();
                var valorFiado = v.Status == StatusVenda.Concluida
                    ? v.Pagamentos.Where(p => formasFiado.Contains(p.FormaPagamentoId)).Sum(p => p.Valor)
                    : 0;
                if (valorFiado > 0 && v.ClienteId is null)
                { Recusar(v, "fiado exige um cliente na venda."); continue; }
                // Sem o cliente no servidor a venda entraria e a dívida sumiria. Fica no caixa até o cliente existir.
                if (valorFiado > 0 && !await db.Clientes.AsNoTracking().AnyAsync(x => x.Id == v.ClienteId))
                { Recusar(v, "o cliente do fiado não existe neste servidor."); continue; }
                var existente = await db.Vendas.FirstOrDefaultAsync(x => x.Id == v.Id);
                if (existente is not null && existente.TerminalId != terminal.Id)
                { Recusar(v, "o identificador já pertence a outro terminal."); continue; }
                if (existente is not null &&
                    (existente.Total != v.Total || existente.Subtotal != v.Subtotal || existente.Desconto != v.Desconto ||
                     existente.CaixaSessaoId != v.CaixaSessaoId))
                { Recusar(v, "já existe no servidor com valores diferentes."); continue; }

                if (existente is null)
                {
                    var venda = new Venda
                    {
                        Id = v.Id,
                        Numero = v.Numero,
                        TerminalId = terminal.Id,
                        CaixaSessaoId = v.CaixaSessaoId,
                        UsuarioId = v.UsuarioId,
                        ClienteId = v.ClienteId,
                        Data = v.Data.Utc(),
                        Subtotal = v.Subtotal,
                        Desconto = v.Desconto,
                        Total = v.Total,
                        Troco = v.Troco,
                        Status = v.Status,
                        CanceladaEm = v.CanceladaEm.Utc(),
                        Itens = v.Itens.Select(i => new VendaItem
                        {
                            Id = i.Id, ProdutoId = i.ProdutoId, Descricao = i.Descricao, Quantidade = i.Quantidade,
                            PrecoUnitario = i.PrecoUnitario, Desconto = i.Desconto, Total = i.Total,
                            CustoUnitario = custos.GetValueOrDefault(i.ProdutoId)
                        }).ToList(),
                        Pagamentos = v.Pagamentos.Select(p => new VendaPagamento
                        {
                            Id = p.Id, FormaPagamentoId = p.FormaPagamentoId, Valor = p.Valor
                        }).ToList()
                    };
                    db.Vendas.Add(venda);

                    // Venda cancelada antes de sincronizar não mexe no estoque.
                    if (venda.Status == StatusVenda.Concluida)
                        await BaixarEstoqueAsync(db, venda, TipoMovimentoEstoque.Venda, -1);
                    if (valorFiado > 0)
                        await LancarFiadoAsync(db, v.ClienteId!.Value, venda.Id, valorFiado, $"Venda nº {venda.Numero}", venda.UsuarioId);
                }
                else if (existente.Status == StatusVenda.Concluida && v.Status == StatusVenda.Cancelada)
                {
                    // Trava e relê: se a Retaguarda estiver devolvendo item desta venda agora, espera e usa o que ela gravou.
                    await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Vendas\" WHERE \"Id\" = {existente.Id} FOR UPDATE");
                    await db.Entry(existente).ReloadAsync();
                    existente.Status = StatusVenda.Cancelada;
                    existente.CanceladaEm = (v.CanceladaEm ?? DateTime.UtcNow).Utc();
                    await db.Entry(existente).Collection(x => x.Itens).LoadAsync();
                    await BaixarEstoqueAsync(db, existente, TipoMovimentoEstoque.CancelamentoVenda, +1);
                    await EstornarFiadoDaVendaAsync(db, existente);
                }

                resp.VendasAceitas.Add(v.Id);
            }

            // Orçamentos: novos entram; os já existentes atualizam (ex.: viraram venda ou foram cancelados).
            foreach (var o in req.Orcamentos ?? [])
            {
                var erroOrcamento = ValidacaoSincronizacao.ValidarOrcamento(o);
                if (erroOrcamento is not null)
                {
                    resp.Avisos.Add($"Orçamento nº {o.Numero} recusado: {erroOrcamento}");
                    continue;
                }
                if (o.Status == StatusOrcamento.Convertido &&
                    !db.Vendas.Local.Any(v => v.Id == o.VendaId && v.TerminalId == terminal.Id) &&
                    !await db.Vendas.AsNoTracking().AnyAsync(v => v.Id == o.VendaId && v.TerminalId == terminal.Id))
                {
                    // Fica pendente no caixa e sobe depois da venda.
                    resp.Avisos.Add($"Orçamento nº {o.Numero}: aguardando a venda correspondente chegar ao servidor.");
                    continue;
                }
                var existente = await db.Orcamentos.Include(x => x.Itens).FirstOrDefaultAsync(x => x.Id == o.Id);
                if (existente is not null && existente.TerminalId != terminal.Id)
                {
                    log.LogWarning("Orçamento {Id} pertence a outro terminal; ignorado.", o.Id);
                    continue;
                }
                if (existente is not null)
                {
                    // Mesmo envio = aceito sem escrever novamente. Revisão antiga = não retrocede estado.
                    if (o.Revisao == existente.Revisao)
                    {
                        if (existente.Status == o.Status && existente.Total == o.Total &&
                            existente.VendaId == o.VendaId && existente.Itens.Count == o.Itens.Count &&
                            existente.Itens.All(i => o.Itens.Any(n => n.Id == i.Id &&
                                n.ProdutoId == i.ProdutoId && n.Total == i.Total && n.Quantidade == i.Quantidade)))
                            resp.OrcamentosAceitos.Add(o.Id);
                        else
                        {
                            log.LogWarning("Orçamento {Id}: mesma revisão, dados divergentes; requer revisão manual.", o.Id);
                            resp.Avisos.Add($"Orçamento nº {o.Numero}: dados divergentes na revisão {o.Revisao}; revisão manual necessária.");
                        }
                        continue;
                    }
                    if (!ValidacaoSincronizacao.PermiteAtualizarOrcamento(
                            existente.Revisao, existente.Status, o.Revisao, o.Status))
                    {
                        log.LogWarning("Orçamento {Id}: revisão antiga ou tentativa de reabrir/finalizar em outro estado.", o.Id);
                        resp.Avisos.Add($"Orçamento nº {o.Numero}: alteração antiga ou situação incompatível. Confira na Gestão.");
                        // Não confirma envio: registro local permanece pendente para revisão.
                        continue;
                    }
                }
                else
                {
                    existente = new Orcamento { Id = o.Id, TerminalId = terminal.Id };
                    db.Orcamentos.Add(existente);
                }
                existente.Revisao = o.Revisao;
                existente.Numero = o.Numero;
                existente.UsuarioId = o.UsuarioId;
                existente.ClienteId = o.ClienteId;
                existente.ClienteNome = Cortar(o.ClienteNome, 150);
                existente.Data = o.Data.Utc();
                existente.Validade = o.Validade;
                existente.Subtotal = o.Subtotal;
                existente.Desconto = o.Desconto;
                existente.Total = o.Total;
                existente.Observacao = Cortar(o.Observacao, 300);
                existente.Status = o.Status;
                existente.VendaId = o.VendaId;
                // Itens: o caixa gera Ids novos quando o orçamento é alterado. Mesmo conjunto de Ids = reenvio: mantém.
                var idsNovos = o.Itens.Select(i => i.Id).ToHashSet();
                var mesmosItens = existente.Itens.Count == idsNovos.Count && existente.Itens.All(i => idsNovos.Contains(i.Id));
                if (!mesmosItens)
                {
                    if (existente.Itens.Count > 0) db.OrcamentoItens.RemoveRange(existente.Itens);
                    foreach (var i in o.Itens)
                        db.OrcamentoItens.Add(new OrcamentoItem
                        {
                            Id = i.Id, OrcamentoId = o.Id, ProdutoId = i.ProdutoId, Codigo = Cortar(i.Codigo, 30) ?? "",
                            Descricao = Cortar(i.Descricao, 150) ?? "", Unidade = Cortar(i.Unidade, 6) ?? "UN",
                            Quantidade = i.Quantidade, PrecoUnitario = i.PrecoUnitario, Total = i.Total
                        });
                }
                resp.OrcamentosAceitos.Add(o.Id);
            }

            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.Ok(resp);
    }

    private static string? Cortar(string? texto, int maximo) =>
        texto is null ? null : texto.Length <= maximo ? texto : texto[..maximo];

    /// <summary>sinal -1 baixa (venda), +1 devolve (cancelamento).</summary>
    private static async Task BaixarEstoqueAsync(ErpDbContext db, Venda venda, TipoMovimentoEstoque tipo, int sinal)
    {
        foreach (var item in venda.Itens)
        {
            // No cancelamento, o que já voltou por devolução na Retaguarda não volta de novo.
            var quantidade = sinal > 0 ? item.Quantidade - item.QuantidadeDevolvida : item.Quantidade;
            if (quantidade <= 0) continue;
            // Find usa o que já está rastreado: dois itens do mesmo produto somam no mesmo objeto.
            var produto = await db.Produtos.FindAsync(item.ProdutoId);
            if (produto is null) continue;
            Estoque.Movimentar(db, produto, tipo, sinal * quantidade, $"Venda nº {venda.Numero}", venda.UsuarioId, venda.Id);
        }
    }

    // Soma atômica no banco (ver ContaFiado): roda dentro da transação do lote.
    private static Task LancarFiadoAsync(ErpDbContext db, Guid clienteId, Guid? vendaId, decimal valor, string observacao, Guid? usuarioId) =>
        ContaFiado.LancarAsync(db, clienteId, vendaId, valor, observacao, usuarioId);

    private static async Task EstornarFiadoDaVendaAsync(ErpDbContext db, Venda venda)
    {
        if (venda.ClienteId is null) return;
        var saldo = await db.LancamentosFiado.Where(l => l.VendaId == venda.Id).SumAsync(l => (decimal?)l.Valor) ?? 0;
        if (saldo == 0) return;
        await LancarFiadoAsync(db, venda.ClienteId.Value, venda.Id, -saldo, $"Cancelamento da venda nº {venda.Numero}", venda.UsuarioId);
    }
}
