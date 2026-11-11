using System.Security.Claims;
using ProjetoJP.Api.Dados;
using ProjetoJP.Api.Servicos;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ProjetoJP.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/login", async (LoginRequest req, HttpContext http, ErpDbContext db,
            TokenService tokens, ILoggerFactory logs) =>
        {
            var login = (req.Login ?? "").Trim().ToLowerInvariant();
            var u = await db.Usuarios.FirstOrDefaultAsync(x => x.Login == login && x.Ativo);
            if (u is null || !Senha.Verificar(req.Senha, u.SenhaHash))
            {
                logs.CreateLogger("Autenticacao").LogWarning("Falha de login para Usuario={Login}", login);
                return Results.Json(new { erro = "Login ou senha inválidos." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var cliente = (http.Items[Inquilino.ChaveItem] as Inquilino)?.Codigo ?? "";
            var sessao = new SessaoAcesso { UsuarioId = u.Id, ExpiraEm = tokens.Expiracao() };
            db.SessoesAcesso.Add(sessao);
            await db.SaveChangesAsync();
            var (token, expira) = tokens.Gerar(u, cliente, sessaoId: sessao.Id);
            return Results.Ok(new LoginResponse
            {
                Token = token,
                UsuarioId = u.Id,
                Nome = u.Nome,
                Perfil = u.Perfil,
                ExpiraEm = expira,
                Permissoes = [.. Permissoes.Efetivas(u.Perfil, u.Permissoes)]
            });
        }).RequireRateLimiting("login-dispositivo");

        // Confirma a senha para operacoes administrativas sem exigir sair e entrar novamente.
        app.MapPost("/api/auth/confirmar-senha", async (ConfirmarSenhaRequest req, ClaimsPrincipal user,
            HttpContext http, ErpDbContext db, TokenService tokens, ILoggerFactory logs) =>
        {
            if (user.UsuarioId() is not { } id) return Results.Unauthorized();
            var u = await db.Usuarios.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.Ativo);
            if (u is null || !Senha.Verificar(req.Senha ?? "", u.SenhaHash))
            {
                logs.CreateLogger("AcessoLembrado").LogWarning("Confirmacao de senha recusada para Usuario={UsuarioId}", id);
                return Results.Json(new { erro = "Senha incorreta." }, statusCode: 401);
            }
            var cliente = (http.Items[Inquilino.ChaveItem] as Inquilino)?.Codigo ?? "";
            // Confirmação renova o token mantendo a sessão, sem gerar sessões extras.
            if (!Guid.TryParse(user.FindFirst("sessao_id")?.Value, out var sessaoId))
                return Results.Unauthorized();
            var (token, expira) = tokens.Gerar(u, cliente, porSenha: true, sessaoId: sessaoId);
            return Results.Ok(new LoginResponse
            {
                Token = token, UsuarioId = u.Id, Nome = u.Nome, Perfil = u.Perfil, ExpiraEm = expira,
                Permissoes = [.. Permissoes.Efetivas(u.Perfil, u.Permissoes)]
            });
        }).RequireAuthorization().RequireRateLimiting("login-dispositivo");
    }
}

public static class EstoqueEndpoints
{
    public static void MapEstoque(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/estoque").RequireAuthorization();

        g.MapPost("/movimentos", async (MovimentoEstoqueRequest req, ClaimsPrincipal user, ErpDbContext db) =>
        {
            if (req.Tipo is TipoMovimentoEstoque.Venda or TipoMovimentoEstoque.CancelamentoVenda)
                return Extensoes.Erro("Movimentos de venda são gerados pelo PDV.");
            if (req.Tipo != TipoMovimentoEstoque.Ajuste && req.Quantidade <= 0)
                return Extensoes.Erro("Informe uma quantidade maior que zero.");
            if (req.Tipo == TipoMovimentoEstoque.Ajuste && req.Quantidade < 0)
                return Extensoes.Erro("O saldo do ajuste não pode ser negativo.");

            if (req.Quantidade > Formatos.MaxQuantidade)
                return Extensoes.Erro("Quantidade acima do limite permitido.");
            if (req.Observacao?.Length > 200)
                return Extensoes.Erro("A observação pode ter no máximo 200 caracteres.");

            var produto = await db.Produtos.FirstOrDefaultAsync(x => x.Id == req.ProdutoId);
            if (produto is null) return Results.NotFound();

            if (!Formatos.UnidadeFracionada(produto.Unidade) && req.Quantidade != decimal.Truncate(req.Quantidade))
                return Extensoes.Erro($"{produto.Descricao} é vendido em {produto.Unidade}: a quantidade precisa ser inteira.");

            var qtd = Math.Round(req.Quantidade, Formatos.CasasQuantidade(produto.Unidade));
            // Mesma regra da tela Saída de produtos: aqui não dá para deixar o estoque negativo
            // (lá exige a permissão "estoque negativo" e uma justificativa).
            if (req.Tipo == TipoMovimentoEstoque.Saida && qtd > produto.EstoqueAtual)
                return Extensoes.Erro($"Estoque insuficiente: {produto.Descricao} tem {produto.EstoqueAtual.ToString("N" + Formatos.CasasQuantidade(produto.Unidade), Formatos.Br)} {produto.Unidade}. " +
                                      "Para deixar o estoque negativo, use Saída de produtos com \"Permitir estoque negativo\".");
            var quantidade = req.Tipo == TipoMovimentoEstoque.Saida ? -qtd : qtd;
            var mov = Estoque.Movimentar(db, produto, req.Tipo, quantidade, req.Observacao, user.UsuarioId());
            await db.SaveChangesAsync();

            return Results.Ok(new { saldo = produto.EstoqueAtual, movimentoId = mov.Id });
        }).RequireAuthorization(PermissoesApi.Politica(Permissoes.MovimentarEstoque));

        // Sem produtoId: movimentações de TODOS os produtos (com período e tipo opcionais).
        g.MapGet("/movimentos", async (Guid? produtoId, int? limite, DateOnly? de, DateOnly? ate, TipoMovimentoEstoque? tipo,
            ErpDbContext db, Fuso fuso) =>
        {
            var q = db.MovimentosEstoque.AsNoTracking().Include(x => x.Produto).AsQueryable();
            if (produtoId is not null) q = q.Where(x => x.ProdutoId == produtoId);
            if (de is { } diaInicial) { var inicio = fuso.InicioDoDiaUtc(diaInicial); q = q.Where(x => x.Data >= inicio); }
            if (ate is { } diaFinal) { var limiteFim = fuso.InicioDoDiaUtc(diaFinal.AddDays(1)); q = q.Where(x => x.Data < limiteFim); }
            if (tipo is { } tipoFiltro) q = q.Where(x => x.Tipo == tipoFiltro);

            var lista = await q.OrderByDescending(x => x.Data).Take(Math.Clamp(limite ?? 200, 1, 5000)).ToListAsync();

            var idsUsuarios = lista.Where(x => x.UsuarioId != null).Select(x => x.UsuarioId!.Value).Distinct().ToList();
            var usuarios = await db.Usuarios.Where(x => idsUsuarios.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Nome);
            // Saídas por caixa: a venda diz de qual terminal (caixa) saiu o produto.
            var idsVendas = lista.Where(x => x.VendaId != null).Select(x => x.VendaId!.Value).Distinct().ToList();
            var terminalDaVenda = await (from v in db.Vendas.AsNoTracking()
                                         join t in db.Terminais.AsNoTracking() on v.TerminalId equals t.Id
                                         where idsVendas.Contains(v.Id)
                                         select new { v.Id, t.Nome }).ToDictionaryAsync(x => x.Id, x => x.Nome);

            return lista.Select(x => new MovimentoEstoqueDto
            {
                Id = x.Id,
                Data = x.Data,
                Codigo = x.Produto?.Codigo,
                Produto = x.Produto?.Descricao ?? "",
                Tipo = x.Tipo,
                Origem = x.VendaId is { } venda
                    ? terminalDaVenda.GetValueOrDefault(venda, "Caixa")
                    : x.Tipo is TipoMovimentoEstoque.Entrada or TipoMovimentoEstoque.Saida or TipoMovimentoEstoque.Ajuste ? "Gestão" : null,
                Quantidade = x.Quantidade,
                SaldoApos = x.SaldoApos,
                Observacao = x.Observacao,
                Usuario = x.UsuarioId != null && usuarios.TryGetValue(x.UsuarioId.Value, out var nome) ? nome : null
            }).ToList();
        });
    }
}
