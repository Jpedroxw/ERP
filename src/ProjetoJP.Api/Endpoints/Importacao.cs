using System.Security.Claims;
using ProjetoJP.Api.Dados;
using ProjetoJP.Api.Servicos;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ProjetoJP.Api.Endpoints;

/// <summary>
/// Importação em lote de produtos e clientes vindos de outro sistema.
/// A Retaguarda lê o banco/planilha de origem e manda até 1000 linhas por chamada.
/// Linhas com problema não travam o lote: são puladas (ou corrigidas) e voltam como aviso.
/// </summary>
public static class ImportacaoEndpoints
{
    private const int MaxItensPorLote = 1000;
    private const int MaxAvisos = 200;

    public static void MapImportacao(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/importacao").RequireAuthorization(PermissoesApi.Politica(Permissoes.ImportarDados));
        g.MapPost("/produtos", ImportarProdutos);
        g.MapPost("/clientes", ImportarClientes);
    }

    // ---------- Produtos ----------

    private static async Task<IResult> ImportarProdutos(ImportacaoRequest<ProdutoImportado> req, ClaimsPrincipal user, ErpDbContext db, Fuso fuso)
    {
        if (req.Itens.Count > MaxItensPorLote) return Extensoes.Erro($"Mande no máximo {MaxItensPorLote} linhas por vez.");
        var r = new ImportacaoResultado();
        if (req.Itens.Count == 0) return Results.Ok(r);

        var produtos = await db.Produtos.ToListAsync();
        var porCodigo = produtos.ToDictionary(p => p.Codigo, StringComparer.OrdinalIgnoreCase);
        var porBarras = produtos.Where(p => p.CodigoBarras != null)
            .GroupBy(p => p.CodigoBarras!).ToDictionary(x => x.Key, x => x.First());
        var categorias = (await db.Categorias.ToListAsync())
            .GroupBy(c => Chave(c.Nome)).ToDictionary(x => x.Key, x => x.First());
        var proximoCodigo = porCodigo.Keys.Where(c => c.Length <= 9)
            .Select(c => int.TryParse(c, out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1;
        var usuario = user.UsuarioId();
        var mexidos = new List<Produto>(); // inseridos e atualizados: publicados no fim, se pedido
        // Evita duas linhas distintas apontando para o mesmo código após higienização.
        // Uma importação nunca deve atualizar um produto por simples colisão de normalização.
        var originaisPorCodigoNormalizado = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var codigosProcessados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in req.Itens)
        {
            var linha = $"Linha {item.Linha}";

            var descricao = Limpo(item.Descricao).ToUpperInvariant();
            if (descricao.Length == 0) { Pular(r, $"{linha}: sem descrição."); continue; }
            if (descricao.Length > 150) descricao = descricao[..150].TrimEnd();
            linha += $" ({Resumo(descricao)})";

            // Código de outro sistema: espaço vira hífen e símbolos fora da regra saem (avisando). O original também
            // é procurado, para atualizar um produto que já foi importado com o código antigo.
            var codigoOriginal = Limpo(item.Codigo).ToUpperInvariant();
            var codigo = CodigosImportacao.Normalizar(codigoOriginal);
            if (codigoOriginal.Length > 0 && codigo != codigoOriginal && !porCodigo.ContainsKey(codigoOriginal))
                if (codigo.Length > 0) Avisar(r, $"{linha}: código \"{codigoOriginal}\" ajustado para \"{codigo}\".");
            if (codigo.Length > 30) { Pular(r, $"{linha}: código com mais de 30 caracteres."); continue; }
            if (codigoOriginal.Length > 0 && codigo.Length == 0)
            {
                Pular(r, $"{linha}: o código original não contém caracteres válidos. Informe um código diferente na origem.");
                continue;
            }

            // Rejeita colisões entre códigos originais diferentes dentro do MESMO lote.
            // Ex.: "REF@1" e "REF#1" seriam ambos "REF1".
            if (codigo.Length > 0 && codigoOriginal.Length > 0)
            {
                if (originaisPorCodigoNormalizado.TryGetValue(codigo, out var origemAnterior) &&
                    CodigosImportacao.ColisaoDeOrigem(origemAnterior, codigoOriginal))
                {
                    Pular(r, $"{linha}: código original \"{codigoOriginal}\" conflita após normalização com \"{origemAnterior}\" (ambos viram \"{codigo}\"). Corrija o arquivo de origem.");
                    continue;
                }
                originaisPorCodigoNormalizado[codigo] = codigoOriginal;
                if (!codigosProcessados.Add(codigo))
                {
                    Pular(r, $"{linha}: código \"{codigo}\" repetido no mesmo lote. Nenhum produto foi alterado nesta linha.");
                    continue;
                }
            }

            var barras = Formatos.SoDigitos(item.CodigoBarras);
            if (barras.Length > 0 && !Formatos.GtinValido(barras))
            {
                Avisar(r, $"{linha}: código de barras {barras} inválido — importado sem ele.");
                barras = "";
            }

            var unidade = NormalizarUnidade(item.Unidade, out var unidadeReconhecida);
            if (!unidadeReconhecida)
                Avisar(r, $"{linha}: unidade \"{item.Unidade}\" desconhecida — ficou UN.");

            var precoVenda = Math.Round(item.PrecoVenda ?? 0, 2);
            var precoCusto = Math.Round(Math.Max(0, item.PrecoCusto ?? 0), 2);
            if (precoVenda > Formatos.MaxMoeda || precoCusto > Formatos.MaxMoeda) { Pular(r, $"{linha}: preço acima do limite."); continue; }

            var casas = Formatos.CasasQuantidade(unidade);
            decimal? estoque = item.Estoque is { } e ? Math.Round(e, casas) : null;
            if (estoque < 0)
            {
                Avisar(r, $"{linha}: estoque negativo ({item.Estoque}) — ficou 0.");
                estoque = 0;
            }
            if (estoque > Formatos.MaxQuantidade) { Pular(r, $"{linha}: estoque acima do limite."); continue; }
            decimal? minimo = item.EstoqueMinimo is { } m ? Math.Clamp(Math.Round(m, casas), 0, Formatos.MaxQuantidade) : null;

            Guid? categoriaId = null;
            var nomeCategoria = Limpo(item.Categoria);
            if (nomeCategoria.Length > 0)
            {
                if (nomeCategoria.Length > 80) nomeCategoria = nomeCategoria[..80].TrimEnd();
                if (!categorias.TryGetValue(Chave(nomeCategoria), out var cat))
                {
                    cat = new Categoria { Id = Guid.NewGuid(), Nome = nomeCategoria, Ativo = true };
                    db.Categorias.Add(cat);
                    categorias[Chave(nomeCategoria)] = cat;
                    r.CategoriasCriadas++;
                }
                categoriaId = cat.Id;
            }

            // Já existe? Procura pelo código e, se não achar, pelo código de barras.
            Produto? p = null;
            if (codigoOriginal.Length > 0) porCodigo.TryGetValue(codigoOriginal, out p);
            if (p is null && codigo.Length > 0) porCodigo.TryGetValue(codigo, out p);
            if (p is null && barras.Length > 0 && porBarras.TryGetValue(barras, out var peloGtin))
            {
                // Mesmo GTIN não autoriza substituir cadastro cujo código interno seja diferente.
                // Descrições coincidentes também podem representar produtos distintos.
                if (codigoOriginal.Length > 0 && !CodigosImportacao.MesmoCodigo(peloGtin.Codigo, codigoOriginal))
                {
                    Pular(r, $"{linha}: código de barras {barras} já pertence a \"{Resumo(peloGtin.Descricao)}\" " +
                        $"(código \"{peloGtin.Codigo}\"). Revise o vínculo antes de importar.");
                    continue;
                }
                if (codigoOriginal.Length == 0 && Chave(peloGtin.Descricao) != Chave(descricao))
                {
                    Pular(r, $"{linha}: código de barras {barras} encontrado em outro produto com descrição diferente. " +
                        "Revise manualmente para não substituir um cadastro.");
                    continue;
                }
                p = peloGtin;
            }

            // Se o código precisou de normalização, só é seguro atualizar um produto
            // cujo código ORIGINAL já exista no banco. Não usar correspondência pelo
            // código resultante nem pelo GTIN para sobrescrever outro cadastro.
            if (p is not null && CodigosImportacao.AtualizacaoInsegura(codigoOriginal, codigo, p.Codigo))
            {
                Pular(r, $"{linha}: código \"{codigoOriginal}\" vira \"{codigo}\", que já corresponde ao produto \"{Resumo(p.Descricao)}\". Atualização bloqueada para evitar troca indevida de cadastro.");
                continue;
            }

            if (p is not null)
            {
                if (!req.AtualizarExistentes) { r.Ignorados++; continue; }

                if (barras.Length > 0 && porBarras.TryGetValue(barras, out var dono) && dono != p)
                {
                    Avisar(r, $"{linha}: código de barras {barras} já é de \"{Resumo(dono.Descricao)}\" — mantido no outro produto.");
                    barras = "";
                }

                p.Descricao = descricao;
                p.Unidade = unidade;
                if (barras.Length > 0 && p.CodigoBarras != barras)
                {
                    if (p.CodigoBarras != null) porBarras.Remove(p.CodigoBarras);
                    p.CodigoBarras = barras;
                    porBarras[barras] = p;
                }
                if (item.PrecoVenda is not null && precoVenda > 0) p.PrecoVenda = precoVenda;
                if (item.PrecoCusto is not null) p.PrecoCusto = precoCusto;
                if (minimo is not null) p.EstoqueMinimo = minimo.Value;
                if (categoriaId is not null) p.CategoriaId = categoriaId;
                if (item.Ativo is not null) p.Ativo = item.Ativo.Value;

                if (req.ImportarEstoque && estoque is not null && estoque.Value != p.EstoqueAtual)
                    Estoque.Movimentar(db, p, TipoMovimentoEstoque.Ajuste, estoque.Value, "Importação de dados", usuario);

                r.Atualizados++;
                mexidos.Add(p);
                continue;
            }

            if (precoVenda <= 0) { Pular(r, $"{linha}: sem preço de venda."); continue; }

            if (codigo.Length == 0)
            {
                while (porCodigo.ContainsKey(proximoCodigo.ToString())) proximoCodigo++;
                codigo = proximoCodigo.ToString();
                proximoCodigo++;
            }
            if (barras.Length > 0 && porBarras.TryGetValue(barras, out var outro))
            {
                Avisar(r, $"{linha}: código de barras {barras} já é de \"{Resumo(outro.Descricao)}\" — importado sem ele.");
                barras = "";
            }

            var novo = new Produto
            {
                Id = Guid.NewGuid(),
                Codigo = codigo,
                CodigoBarras = barras.Length > 0 ? barras : null,
                Descricao = descricao,
                Unidade = unidade,
                CategoriaId = categoriaId,
                PrecoCusto = precoCusto,
                PrecoVenda = precoVenda,
                EstoqueMinimo = minimo ?? 0,
                Ativo = item.Ativo ?? true
            };
            db.Produtos.Add(novo);
            porCodigo[codigo] = novo;
            if (novo.CodigoBarras != null) porBarras[novo.CodigoBarras] = novo;

            if (req.ImportarEstoque && estoque is > 0m)
                Estoque.Movimentar(db, novo, TipoMovimentoEstoque.Ajuste, estoque.Value, "Importação de dados", usuario);

            r.Inseridos++;
            mexidos.Add(novo);
        }

        if (req.Simular) return Results.Ok(r);

        if (req.PublicarNoCaixa && mexidos.Count > 0)
        {
            var (publicados, ignorados) = await Catalogo.PublicarAsync(db, mexidos.Distinct().ToList(), fuso.Hoje());
            r.Publicados = publicados;
            foreach (var i in ignorados) Avisar(r, $"{i.Codigo} ({Resumo(i.Descricao)}): não foi para os caixas — {i.Motivo}.");
        }
        await db.SaveChangesAsync();
        return Results.Ok(r);
    }

    // ---------- Clientes ----------

    private static async Task<IResult> ImportarClientes(ImportacaoRequest<ClienteImportado> req, ErpDbContext db)
    {
        if (req.Itens.Count > MaxItensPorLote) return Extensoes.Erro($"Mande no máximo {MaxItensPorLote} linhas por vez.");
        var r = new ImportacaoResultado();
        if (req.Itens.Count == 0) return Results.Ok(r);

        var clientes = await db.Clientes.ToListAsync();
        var porDocumento = clientes.Where(c => c.CpfCnpj != null)
            .GroupBy(c => c.CpfCnpj!).ToDictionary(x => x.Key, x => x.First());
        // Sem CPF/CNPJ, reconhece pelo nome (só entre os que também não têm documento).
        var porNome = clientes.Where(c => c.CpfCnpj == null)
            .GroupBy(c => Chave(c.Nome)).ToDictionary(x => x.Key, x => x.First());

        foreach (var item in req.Itens)
        {
            var nome = Limpo(item.Nome);
            if (nome.Length == 0) { Pular(r, $"Linha {item.Linha}: sem nome."); continue; }
            if (nome.Length > 150) nome = nome[..150].TrimEnd();
            var linha = $"Linha {item.Linha} ({Resumo(nome)})";

            var doc = Formatos.LimparDocumento(item.CpfCnpj);
            if (doc.Length > 0 && (doc.Length is not (11 or 14) || !Formatos.DocumentoDigitadoValido(item.CpfCnpj)))
            {
                Avisar(r, $"{linha}: CPF/CNPJ {item.CpfCnpj} inválido — importado sem ele.");
                doc = "";
            }

            var telefone = item.Telefone is null || item.Telefone.All(c => char.IsAsciiDigit(c) ||
                c is '+' or '(' or ')' or '-' or ' ') ? NormalizarTelefone(item.Telefone) : null;
            if (telefone is null && !string.IsNullOrWhiteSpace(item.Telefone) && Formatos.SoDigitos(item.Telefone).Length > 0)
                Avisar(r, $"{linha}: telefone {item.Telefone} sem DDD ou inválido — importado sem ele.");

            var email = Texto(item.Email).ToLowerInvariant();
            if (email.Length > 0 && (email.Length > 120 || !Formatos.EmailValido(email)))
            {
                Avisar(r, $"{linha}: e-mail {item.Email} inválido — importado sem ele.");
                email = "";
            }

            var endereco = Cortar(Limpo(item.Endereco), 200);
            var observacao = Cortar(Limpo(item.Observacao), 500);

            Cliente? c = null;
            if (doc.Length > 0) porDocumento.TryGetValue(doc, out c);
            else porNome.TryGetValue(Chave(nome), out c);

            if (c is not null)
            {
                if (!req.AtualizarExistentes) { r.Ignorados++; continue; }
                c.Nome = nome;
                if (telefone is not null) c.Telefone = telefone;
                if (email.Length > 0) c.Email = email;
                if (endereco.Length > 0) c.Endereco = endereco;
                if (observacao.Length > 0) c.Observacao = observacao;
                if (item.Ativo is not null) c.Ativo = item.Ativo.Value;
                r.Atualizados++;
                continue;
            }

            var novo = new Cliente
            {
                Id = Guid.NewGuid(),
                Nome = nome,
                CpfCnpj = doc.Length > 0 ? doc : null,
                Telefone = telefone,
                Email = email.Length > 0 ? email : null,
                Endereco = endereco.Length > 0 ? endereco : null,
                Observacao = observacao.Length > 0 ? observacao : null,
                Ativo = item.Ativo ?? true
            };
            db.Clientes.Add(novo);
            if (novo.CpfCnpj != null) porDocumento[novo.CpfCnpj] = novo;
            else porNome[Chave(nome)] = novo;
            r.Inseridos++;
        }

        if (!req.Simular) await db.SaveChangesAsync();
        return Results.Ok(r);
    }

    // ---------- Ajudantes ----------

    private static string Texto(string? s) =>
        string.Join(' ', (s ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Cortar(string s, int max) => s.Length > max ? s[..max].TrimEnd() : s;

    /// <summary>
    /// Texto vindo de outro sistema: tira caracteres invisíveis (formatação) e troca tabulação/quebra de linha por
    /// espaço, em vez de recusar a linha inteira. Espaços repetidos viram um só.
    /// </summary>
    private static string Limpo(string? s) =>
        Texto(new string((s ?? "")
            .Where(c => char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format)
            .Select(c => char.IsControl(c) ? ' ' : c).ToArray()));

    /// <summary>Chave para comparar nomes: sem acento, maiúsculas, espaços simples.</summary>
    private static string Chave(string s) => Formatos.RemoverAcentos(Texto(s)).ToUpperInvariant();

    private static string Resumo(string s) => s.Length > 40 ? s[..40] + "…" : s;

    private static void Avisar(ImportacaoResultado r, string msg)
    {
        if (r.Avisos.Count < MaxAvisos) r.Avisos.Add(msg);
    }

    private static void Pular(ImportacaoResultado r, string msg)
    {
        r.Ignorados++;
        Avisar(r, msg + " Linha não importada.");
    }

    /// <summary>Converte os nomes de unidade usados por outros sistemas (UND, LT, PÇ, KILO...) para os daqui.</summary>
    private static string NormalizarUnidade(string? valor, out bool reconhecida)
    {
        reconhecida = true;
        var u = Formatos.RemoverAcentos(Texto(valor)).ToUpperInvariant().Replace(".", "").Replace("²", "2");
        if (u.Length == 0) return "UN";
        if (Formatos.UnidadeValida(u)) return u;

        var r = u switch
        {
            "U" or "UND" or "UNID" or "UNI" or "UNIDADE" or "UNIT" or "UNT" or "UNS" => "UN",
            "K" or "KGS" or "KILO" or "QUILO" or "QUILOS" or "KILOS" => "KG",
            "GR" or "GRS" or "GRAMA" or "GRAMAS" => "G",
            "LT" or "LTS" or "LITRO" or "LITROS" => "L",
            "MLS" or "MILILITRO" => "ML",
            "MT" or "MTS" or "METRO" or "METROS" => "M",
            "MQ" or "M²" or "METRO2" => "M2",
            "CXS" or "CAIXA" or "CAIXAS" => "CX",
            "PCS" or "PECA" or "PECAS" => "PC",
            "PT" or "PCTE" or "PACOTE" or "PACOTES" => "PCT",
            "DUZIA" or "DZS" => "DZ",
            "FDO" or "FARDO" or "FARDOS" => "FD",
            "KT" or "CONJUNTO" or "JG" or "JOGO" => "KIT",
            "PR" or "PARES" => "PAR",
            _ => null
        };
        if (r is not null) return r;
        reconhecida = false;
        return "UN";
    }

    /// <summary>Telefone com DDD (10 ou 11 dígitos). Tira 0 de operadora e +55 da frente. Sem DDD = null.</summary>
    private static string? NormalizarTelefone(string? valor)
    {
        var d = Formatos.SoDigitos(valor);
        if (d.Length is 12 or 13 && d.StartsWith("55")) d = d[2..];
        if (d.Length is 11 or 12 && d.StartsWith('0')) d = d[1..];
        return d.Length is 10 or 11 ? d : null;
    }
}
