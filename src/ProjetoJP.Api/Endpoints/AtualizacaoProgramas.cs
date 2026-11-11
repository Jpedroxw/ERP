using System.Text.Json;
using ProjetoJP.Api.Dados;
using Microsoft.EntityFrameworkCore;

namespace ProjetoJP.Api.Endpoints;

/// <summary>
/// Atualização automática do Caixa e da Retaguarda. O publicar.ps1 gera a pasta publicar\atualizacao
/// (versao.json + caixa-x64.zip, caixa-x86.zip, retaguarda-x64.zip, retaguarda-x86.zip), que vai para
/// Site:PastaDownloads/atualizacao na VPS. Os programas perguntam aqui se há versão nova e baixam o arquivo.
/// <list type="bullet">
/// <item>Retaguarda: <c>/api/atualizacao</c>, com o login (token);</item>
/// <item>Caixa: <c>/api/sync/atualizacao</c>, com a chave do terminal (X-Terminal-Key), igual à sincronização.</item>
/// </list>
/// Só quem já tem acesso à loja consegue baixar. O programa confere o tamanho e o SHA-256 antes de trocar.
/// </summary>
public static class AtualizacaoProgramas
{
    private static readonly HashSet<string> Programas = ["caixa", "retaguarda"];
    private static readonly HashSet<string> Arquiteturas = ["x64", "x86"];

    private sealed class Manifesto
    {
        public string Versao { get; set; } = "";
        public Dictionary<string, ArquivoManifesto> Arquivos { get; set; } = [];
    }

    private sealed class ArquivoManifesto
    {
        public string Arquivo { get; set; } = "";
        public long Tamanho { get; set; }
        public string Sha256 { get; set; } = "";
    }

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static void MapAtualizacaoProgramas(this IEndpointRouteBuilder app)
    {
        // Retaguarda: depois do login.
        var retaguarda = app.MapGroup("/api/atualizacao").RequireAuthorization();
        retaguarda.MapGet("/", (string? programa, string? arquitetura, IConfiguration config, IWebHostEnvironment env) =>
            Info(programa, arquitetura, config, env));
        retaguarda.MapGet("/arquivo", (string? programa, string? arquitetura, IConfiguration config, IWebHostEnvironment env) =>
            Arquivo(programa, arquitetura, config, env));

        // Caixa: chave do terminal (o caixa confere ao abrir, antes de alguém entrar).
        var caixa = app.MapGroup("/api/sync/atualizacao").AddEndpointFilter(async (ctx, next) =>
        {
            var chave = ctx.HttpContext.Request.Headers["X-Terminal-Key"].ToString();
            if (string.IsNullOrWhiteSpace(chave))
                return Results.Json(new { erro = "Chave do terminal não informada." }, statusCode: 401);
            var db = ctx.HttpContext.RequestServices.GetRequiredService<ErpDbContext>();
            if (!await db.Terminais.AsNoTracking().AnyAsync(t => t.Chave == chave && t.Ativo))
                return Results.Json(new { erro = "Terminal não cadastrado ou inativo." }, statusCode: 401);
            return await next(ctx);
        });
        caixa.MapGet("/", (string? programa, string? arquitetura, IConfiguration config, IWebHostEnvironment env) =>
            Info(programa, arquitetura, config, env));
        caixa.MapGet("/arquivo", (string? programa, string? arquitetura, IConfiguration config, IWebHostEnvironment env) =>
            Arquivo(programa, arquitetura, config, env));
    }

    private static IResult Info(string? programa, string? arquitetura, IConfiguration config, IWebHostEnvironment env)
    {
        var (versao, item, caminho) = Localizar(programa, arquitetura, config, env);
        if (item is null || caminho is null)
            return Results.Json(new { erro = "Nenhuma atualização publicada." }, statusCode: 404);
        return Results.Ok(new { versao, tamanho = item.Tamanho, sha256 = item.Sha256 });
    }

    private static IResult Arquivo(string? programa, string? arquitetura, IConfiguration config, IWebHostEnvironment env)
    {
        var (_, item, caminho) = Localizar(programa, arquitetura, config, env);
        if (item is null || caminho is null)
            return Results.Json(new { erro = "Nenhuma atualização publicada." }, statusCode: 404);
        return Results.File(caminho, "application/zip", Path.GetFileName(caminho), enableRangeProcessing: true);
    }

    /// <summary>Lê o versao.json e acha o arquivo do programa (caixa/retaguarda) para a arquitetura (x64/x86).</summary>
    private static (string versao, ArquivoManifesto? item, string? caminho) Localizar(
        string? programa, string? arquitetura, IConfiguration config, IWebHostEnvironment env)
    {
        programa = (programa ?? "").Trim().ToLowerInvariant();
        arquitetura = (arquitetura ?? "").Trim().ToLowerInvariant();
        if (!Programas.Contains(programa) || !Arquiteturas.Contains(arquitetura)) return ("", null, null);

        var pasta = Path.Combine(SitePublico.PastaDownloads(config, env), "atualizacao");
        var arquivoManifesto = Path.Combine(pasta, "versao.json");
        Manifesto? manifesto;
        try
        {
            manifesto = File.Exists(arquivoManifesto)
                ? JsonSerializer.Deserialize<Manifesto>(File.ReadAllText(arquivoManifesto), Json)
                : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return ("", null, null);
        }
        if (manifesto is null || !Version.TryParse(manifesto.Versao, out _)) return ("", null, null);
        if (!manifesto.Arquivos.TryGetValue($"{programa}-{arquitetura}", out var item)) return ("", null, null);

        // Só um nome de arquivo dentro da pasta (nada de "../").
        if (string.IsNullOrWhiteSpace(item.Arquivo) || Path.GetFileName(item.Arquivo) != item.Arquivo) return ("", null, null);
        var caminho = Path.Combine(pasta, item.Arquivo);
        return File.Exists(caminho) ? (manifesto.Versao, item, caminho) : ("", null, null);
    }
}
