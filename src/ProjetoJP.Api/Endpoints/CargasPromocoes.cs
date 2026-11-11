using ProjetoJP.Api.Dados;
using ProjetoJP.Api.Servicos;
using ProjetoJP.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ProjetoJP.Api.Endpoints;

/// <summary>
/// Cargas (o que os caixas recebem: ProdutosPublicados) e promoções agendadas.
/// Salvar um produto na Retaguarda não muda o caixa; só a publicação muda.
/// </summary>
public static class CargasPromocoes
{
    public static void MapCargasPromocoes(this IEndpointRouteBuilder app)
    {
        var carga = app.MapGroup("/api/cargas").RequireAuthorization(PermissoesApi.Politica(ProjetoJP.Shared.Permissoes.PublicarCargas));

        carga.MapGet("/produtos", async (ErpDbContext db) =>
        {
            var produtos = await db.Produtos.AsNoTracking().OrderBy(x => x.Descricao).ToListAsync();
            var publicados = await db.ProdutosPublicados.AsNoTracking().ToDictionaryAsync(x => x.Id);
            return produtos.Select(p =>
            {
                publicados.TryGetValue(p.Id, out var pub);
                return new CargaProdutoDto
                {
                    Id = p.Id, Codigo = p.Codigo, Descricao = p.Descricao, Ativo = p.Ativo,
                    Publicado = pub is not null,
                    // Produto inativo que nunca foi para os caixas não tem nada a enviar (mesma regra do resumo de Produtos).
                    Pendente = pub is null ? p.Ativo : Mudou(p, pub),
                    Mudancas = Mudancas(p, pub),
                    DescricaoPublicada = pub?.Descricao ?? "",
                    AtivoPublicado = pub?.Ativo ?? false,
                    PrecoPublicado = pub?.PrecoVenda ?? 0,
                    UnidadePublicada = pub?.Unidade ?? p.Unidade,
                    PrecoAtacadoPublicado = pub?.PrecoAtacado ?? 0,
                    QtdAtacadoPublicado = pub?.QtdMinimaAtacado ?? 0,
                    PrecoEmEdicao = p.PrecoVenda,
                    PublicadoEm = pub?.PublicadoEm
                };
            }).ToList();
        });

        // Publica os válidos; os que têm problema (ex.: preço R$ 0,00) voltam em "Ignorados" sem travar o resto.
        carga.MapPost("/produtos/publicar", async (PublicarProdutosRequest req, ErpDbContext db, Fuso fuso) =>
        {
            var ids = (req.Ids ?? []).Where(x => x != Guid.Empty).Distinct().ToList();
            if (ids.Count == 0 || ids.Count > 1000)
                return Extensoes.Erro("Selecione de 1 a 1000 produtos por carga.");

            await using var tx = await db.Database.BeginTransactionAsync();
            var produtos = await db.Produtos.Where(p => ids.Contains(p.Id)).ToListAsync();
            if (produtos.Count == 0)
                return Extensoes.Erro("Nenhum dos produtos foi encontrado. Atualize a lista e tente de novo.");

            var (publicados, ignorados) = await Catalogo.PublicarAsync(db, produtos, fuso.Hoje());
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.Ok(new PublicarProdutosResposta
            {
                Publicados = publicados,
                Data = DateTime.UtcNow,
                Ignorados = ignorados.Select(i => new CargaIgnoradaDto { Codigo = i.Codigo, Descricao = i.Descricao, Motivo = i.Motivo }).ToList()
            });
        });

        carga.MapGet("/usuarios", async (ErpDbContext db) =>
        {
            var usuarios = await db.Usuarios.AsNoTracking().OrderBy(x => x.Nome).ToListAsync();
            var publicados = await db.UsuariosPublicados.AsNoTracking().ToDictionaryAsync(x => x.Id);
            return usuarios.Select(u =>
            {
                publicados.TryGetValue(u.Id, out var pub);
                return new CargaUsuarioDto
                {
                    Id = u.Id, Nome = u.Nome, Login = u.Login, Perfil = u.Perfil.ToString(), Ativo = u.Ativo,
                    Publicado = pub is not null,
                    Pendente = pub is null ? u.Ativo : UsuarioMudou(u, pub),
                    Mudancas = UsuarioMudancas(u, pub),
                    PublicadoEm = pub?.PublicadoEm
                };
            }).ToList();
        });

        carga.MapPost("/usuarios/publicar", async (PublicarProdutosRequest req, ErpDbContext db) =>
        {
            var ids = (req.Ids ?? []).Where(x => x != Guid.Empty).Distinct().ToList();
            if (ids.Count == 0 || ids.Count > 1000)
                return Extensoes.Erro("Selecione de 1 a 1000 usuários por carga.");

            var usuarios = await db.Usuarios.Where(u => ids.Contains(u.Id)).ToListAsync();
            if (usuarios.Count == 0)
                return Extensoes.Erro("Nenhum dos usuários foi encontrado. Atualize a lista e tente de novo.");

            var anteriores = await db.UsuariosPublicados.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(x => x.Id);
            var agora = DateTime.UtcNow;
            foreach (var u in usuarios)
            {
                if (!anteriores.TryGetValue(u.Id, out var pub))
                {
                    pub = new UsuarioPublicado { Id = u.Id };
                    db.UsuariosPublicados.Add(pub);
                }
                pub.Nome = u.Nome;
                pub.Login = u.Login;
                pub.SenhaHash = u.SenhaHash;
                pub.Perfil = u.Perfil;
                pub.Ativo = u.Ativo;
                pub.Permissoes = u.Permissoes;
                pub.PublicadoEm = agora;
            }
            await db.SaveChangesAsync();
            return Results.Ok(new PublicarProdutosResposta { Publicados = usuarios.Count, Data = agora });
        });

        // Cada caixa já baixou a última carga? (comparando a última sincronização com a última publicação)
        carga.MapGet("/terminais", async (ErpDbContext db) =>
        {
            var ultimaProduto = await db.ProdutosPublicados.MaxAsync(x => (DateTime?)x.PublicadoEm);
            var ultimaUsuario = await db.UsuariosPublicados.MaxAsync(x => (DateTime?)x.PublicadoEm);
            var ultimaPromocao = await db.Promocoes.MaxAsync(x => (DateTime?)x.AtualizadoEm);
            var ultima = new[] { ultimaProduto, ultimaUsuario, ultimaPromocao }.Max();
            var terminais = await db.Terminais.AsNoTracking().Where(t => t.Ativo).OrderBy(t => t.Nome).ToListAsync();
            return terminais.Select(t =>
            {
                var emDia = ultima is null || (t.UltimaSincronizacao is { } s && s >= ultima.Value);
                return new CargaTerminalDto
                {
                    Nome = t.Nome,
                    UltimaSincronizacao = t.UltimaSincronizacao,
                    EmDia = emDia,
                    Situacao = t.UltimaSincronizacao is null ? "Nunca sincronizou" : emDia ? "Sim" : "Ainda não"
                };
            }).ToList();
        });

        var promos = app.MapGroup("/api/promocoes").RequireAuthorization(PermissoesApi.Politica(ProjetoJP.Shared.Permissoes.Promocoes));

        promos.MapGet("/", async (ErpDbContext db) =>
        {
            var lista = await db.Promocoes.AsNoTracking().Include(x => x.Produto)
                .OrderByDescending(x => x.Inicio).ToListAsync();
            var ids = lista.Select(x => x.ProdutoId).Distinct().ToList();
            var normais = await db.ProdutosPublicados.AsNoTracking().Where(x => ids.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.PrecoVenda);
            return lista.Select(x => ParaDto(x, normais.GetValueOrDefault(x.ProdutoId))).ToList();
        });

        promos.MapPost("/", async (PromocaoCadastroRequest req, ErpDbContext db, Fuso fuso) =>
        {
            if (req.ProdutoId == Guid.Empty) return Extensoes.Erro("Escolha o produto.");
            if (req.Inicio == default || req.Fim == default || req.Fim < req.Inicio)
                return Extensoes.Erro("Confira as datas: o término não pode ser antes do início.");
            if (req.Fim < fuso.Hoje()) return Extensoes.Erro("O término já passou. Escolha uma data de hoje em diante.");
            if (req.Fim.DayNumber - req.Inicio.DayNumber > 365)
                return Extensoes.Erro("A promoção pode durar no máximo 366 dias.");
            if (req.PrecoPromocional <= 0) return Extensoes.Erro("Informe o preço promocional.");

            var pub = await db.ProdutosPublicados.Include(x => x.Produto)
                .FirstOrDefaultAsync(x => x.Id == req.ProdutoId && x.Ativo);
            if (pub is null) return Extensoes.Erro("Envie o produto aos caixas (Caixas → Enviar produtos aos caixas) antes de criar a promoção.");
            if (req.PrecoPromocional >= pub.PrecoVenda)
                return Extensoes.Erro($"O preço promocional precisa ser menor que o preço normal no caixa ({ProjetoJP.Shared.Formatos.Moeda(pub.PrecoVenda)}).");
            if (req.QuantidadeMinima < 0) return Extensoes.Erro("Quantidade mínima inválida.");
            var atacado = req.QuantidadeMinima > 0;
            if (atacado && ProjetoJP.Shared.Atacado.ValidarQuantidade(pub.Unidade, req.QuantidadeMinima) is { } erroQtd)
                return Extensoes.Erro(erroQtd);
            var minimo = atacado ? Math.Round(req.QuantidadeMinima, ProjetoJP.Shared.Formatos.CasasQuantidade(pub.Unidade)) : 0;

            // Uma de varejo e uma de atacado podem valer juntas; duas do mesmo tipo no mesmo período, não.
            var conflito = await db.Promocoes.AnyAsync(x => x.ProdutoId == req.ProdutoId && x.Ativa &&
                                                             x.Inicio <= req.Fim && x.Fim >= req.Inicio &&
                                                             (atacado ? x.QuantidadeMinima > 0 : x.QuantidadeMinima == 0));
            if (conflito)
                return Extensoes.Erro($"Já existe outra promoção de {(atacado ? "atacado" : "varejo")} ativa para esse produto nesse período.");

            var nome = (req.Nome ?? "").Trim();
            if (!ProjetoJP.Shared.Formatos.TextoSeguro(nome) || nome.Length > 100)
                return Extensoes.Erro("Nome da promoção inválido (máximo de 100 caracteres, sem caracteres invisíveis).");
            var nova = new Promocao
            {
                Id = Guid.NewGuid(), ProdutoId = req.ProdutoId,
                Nome = nome.Length > 0 ? nome[..Math.Min(nome.Length, 100)] : "Promoção",
                Inicio = req.Inicio, Fim = req.Fim,
                PrecoPromocional = Math.Round(req.PrecoPromocional, 2), QuantidadeMinima = minimo, Ativa = true
            };
            db.Promocoes.Add(nova);
            await db.SaveChangesAsync();
            nova.Produto = pub.Produto;
            return Results.Ok(ParaDto(nova, pub.PrecoVenda));
        });

        promos.MapDelete("/{id:guid}", async (Guid id, ErpDbContext db) =>
        {
            var promo = await db.Promocoes.FindAsync(id);
            if (promo is null) return Results.NotFound();
            if (!promo.Ativa) return Extensoes.Erro("Essa promoção já foi cancelada.");
            promo.Ativa = false;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    internal static bool Mudou(Produto p, ProdutoPublicado pub) =>
        p.Codigo != pub.Codigo || p.CodigoBarras != pub.CodigoBarras ||
        p.Descricao != pub.Descricao || p.Unidade != pub.Unidade ||
        p.PrecoVenda != pub.PrecoVenda || p.Ativo != pub.Ativo ||
        p.PrecoAtacado != pub.PrecoAtacado || p.QtdMinimaAtacado != pub.QtdMinimaAtacado;

    /// <summary>O que vai mudar no caixa ao publicar, em português (ex.: "preço R$ 5,00 → R$ 5,50 · descrição").</summary>
    internal static string Mudancas(Produto p, ProdutoPublicado? pub)
    {
        if (pub is null) return p.Ativo ? "produto novo" : "";
        var lista = new List<string>();
        if (p.PrecoVenda != pub.PrecoVenda)
            lista.Add($"preço {ProjetoJP.Shared.Formatos.Moeda(pub.PrecoVenda)} → {ProjetoJP.Shared.Formatos.Moeda(p.PrecoVenda)}");
        if (p.PrecoAtacado != pub.PrecoAtacado || p.QtdMinimaAtacado != pub.QtdMinimaAtacado)
            lista.Add(p.PrecoAtacado > 0
                ? $"atacado {ProjetoJP.Shared.Formatos.Moeda(p.PrecoAtacado)} a partir de {ProjetoJP.Shared.Formatos.Quantidade(p.QtdMinimaAtacado, p.Unidade)}"
                : "sem atacado");
        if (p.Descricao != pub.Descricao) lista.Add("descrição");
        if (p.Codigo != pub.Codigo) lista.Add($"código {pub.Codigo} → {p.Codigo}");
        if ((p.CodigoBarras ?? "") != (pub.CodigoBarras ?? "")) lista.Add("código de barras");
        if (p.Unidade != pub.Unidade) lista.Add($"unidade {pub.Unidade} → {p.Unidade}");
        if (p.Ativo != pub.Ativo) lista.Add(p.Ativo ? "reativado" : "desativado (sai dos caixas)");
        return string.Join(" · ", lista);
    }

    private static bool UsuarioMudou(Usuario u, UsuarioPublicado pub) =>
        u.Nome != pub.Nome || u.Login != pub.Login || u.SenhaHash != pub.SenhaHash ||
        u.Perfil != pub.Perfil || u.Ativo != pub.Ativo || u.Permissoes != pub.Permissoes;

    private static string UsuarioMudancas(Usuario u, UsuarioPublicado? pub)
    {
        if (pub is null) return u.Ativo ? "usuário novo" : "";
        var lista = new List<string>();
        if (u.Nome != pub.Nome) lista.Add("nome");
        if (u.Login != pub.Login) lista.Add($"login {pub.Login} → {u.Login}");
        if (u.SenhaHash != pub.SenhaHash) lista.Add("senha");
        if (u.Perfil != pub.Perfil) lista.Add($"perfil {pub.Perfil} → {u.Perfil}");
        if (u.Ativo != pub.Ativo) lista.Add(u.Ativo ? "reativado" : "desativado (sai do caixa)");
        if (u.Permissoes != pub.Permissoes) lista.Add("permissões");
        return string.Join(" · ", lista);
    }

    private static PromocaoDto ParaDto(Promocao p, decimal precoNormal) => new()
    {
        Id = p.Id, ProdutoId = p.ProdutoId, Produto = p.Produto?.Descricao ?? "",
        Nome = p.Nome, Inicio = p.Inicio, Fim = p.Fim, PrecoNormal = precoNormal,
        PrecoPromocional = p.PrecoPromocional, QuantidadeMinima = p.QuantidadeMinima, Unidade = p.Produto?.Unidade ?? "UN",
        Ativa = p.Ativa, AtualizadoEm = p.AtualizadoEm
    };
}
