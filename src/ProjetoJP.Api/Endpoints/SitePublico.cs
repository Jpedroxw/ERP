using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ProjetoJP.Api.Servicos;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.WebUtilities;

namespace ProjetoJP.Api.Endpoints;

/// <summary>
/// Site público, servido pela própria API (sem programa novo na VPS):
/// <list type="bullet">
/// <item><c>/</c> — apresentação, cadastro da empresa e download do instalador;</item>
/// <item><c>/acompanhar</c> — situação do cadastro (link recebido ao enviar);</item>
/// <item><c>/painel</c> — painel da loja pelo celular (só consulta; usa a API da loja com o login da Retaguarda);</item>
/// <item><c>/privacidade</c> — aviso de privacidade;</item>
/// <item><c>/baixar?t=...</c> — o Instalar-ProjetoJP.exe da pasta de downloads (Site:PastaDownloads), só com um link
/// gerado para loja já liberada (página de cadastro aprovado ou Painel online). Veja <see cref="BilheteDownload"/>.</item>
/// </list>
/// Os arquivos ficam em wwwroot/site. Os links das páginas são relativos, então funcionam atrás do /nf do Nginx.
/// </summary>
public static class SitePublico
{
    /// <summary>Primeira parte do caminho que é do site (não é nome de servidor). Também estão em CodigoCliente.Reservados.</summary>
    public static readonly HashSet<string> Caminhos = new(StringComparer.OrdinalIgnoreCase)
        { "site", "painel", "acompanhar", "privacidade", "baixar" };

    private const string NomeInstalador = "Instalar-ProjetoJP.exe";
    /// <summary>Nome de antes do Projeto JP: ainda serve enquanto o arquivo novo não foi enviado para a VPS.</summary>
    private const string NomeInstaladorAntigo = "Instalar-ERP.exe";

    private static readonly Dictionary<string, string> Paginas = new(StringComparer.OrdinalIgnoreCase)
    {
        ["painel"] = "painel.html",
        ["acompanhar"] = "acompanhar.html",
        ["privacidade"] = "privacidade.html"
    };

    /// <summary>O pedido é de uma página ou arquivo do site (recebe os cabeçalhos de segurança do site).</summary>
    public static bool EhDoSite(PathString caminho)
    {
        var valor = caminho.Value ?? "/";
        if (valor == "/") return true;
        var primeiro = valor.TrimStart('/').Split('/', 2)[0];
        return Caminhos.Contains(primeiro);
    }

    /// <summary>Cabeçalhos de segurança das páginas do site (só scripts e estilos do próprio site).</summary>
    public static IApplicationBuilder UseCabecalhosDoSite(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        if (EhDoSite(ctx.Request.Path))
        {
            ctx.Response.OnStarting(() =>
            {
                var h = ctx.Response.Headers;
                h["Content-Security-Policy"] =
                    "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; " +
                    "object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'";
                h["X-Content-Type-Options"] = "nosniff";
                h["X-Frame-Options"] = "DENY";
                h["Referrer-Policy"] = "same-origin";
                h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
                // Sempre confere se mudou (o site é atualizado junto com a API).
                if (!h.ContainsKey("Cache-Control")) h["Cache-Control"] = "no-cache";
                return Task.CompletedTask;
            });
        }
        await next();
    });

    /// <summary>Arquivos de wwwroot (estilos, scripts, imagens do site).</summary>
    public static IApplicationBuilder UseArquivosDoSite(this IApplicationBuilder app)
    {
        var tipos = new FileExtensionContentTypeProvider();
        tipos.Mappings[".webmanifest"] = "application/manifest+json";
        return app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = tipos });
    }

    public static void MapSitePublico(this IEndpointRouteBuilder app)
    {
        app.MapGet("/", (IWebHostEnvironment env) => Pagina(env, "index.html"));

        foreach (var (rota, arquivo) in Paginas)
        {
            app.MapGet("/" + rota, (HttpContext ctx, IWebHostEnvironment env) =>
                // "/painel/" quebraria os links relativos (site/...): manda para "/painel".
                (ctx.Request.Path.Value ?? "").EndsWith('/') ? Results.Redirect("../" + rota) : Pagina(env, arquivo));
        }

        // Download do instalador: só com um link (bilhete) gerado para uma loja já liberada, que vale 30 minutos.
        // O link sai da página de cadastro aprovado (POST cadastro/{id}/instalador) ou do Painel online (abaixo).
        // O arquivo é colocado na VPS à mão (veja o guia): Site:PastaDownloads/Instalar-ProjetoJP.exe (o nome antigo, Instalar-ERP.exe, também serve).
        app.MapGet("/baixar", async (string? t, IConfiguration config, IWebHostEnvironment env, Inquilinos inq) =>
        {
            var cliente = BilheteDownload.Validar(t, config);
            if (cliente is null)
                return Results.Text("Link de download inválido ou vencido. Gere um novo pela página de acompanhamento do cadastro " +
                                    "ou pelo Painel online (opção Instalador).", "text/plain; charset=utf-8", statusCode: 403);
            if (!await LojaLiberadaAsync(inq, cliente))
                return Results.Text("Acesso suspenso. Fale com o suporte.", "text/plain; charset=utf-8", statusCode: 403);
            var arquivo = Instalador(config, env);
            return arquivo is null
                ? Results.Text("O instalador ainda não está disponível. Fale com o suporte.", "text/plain; charset=utf-8", statusCode: 404)
                : Results.File(arquivo.FullName, "application/octet-stream", NomeInstalador,
                    lastModified: arquivo.LastWriteTimeUtc, enableRangeProcessing: true);
        });

        // Painel online: quem entrou com o login da loja pede o link de download (a loja já está liberada,
        // senão a API nem aceitaria o login).
        app.MapPost("/api/instalador/link", (ClaimsPrincipal user, IConfiguration config, IWebHostEnvironment env) =>
        {
            if (Instalador(config, env) is null)
                return Results.Json(new { erro = "O instalador ainda não está disponível. Fale com o suporte." }, statusCode: 404);
            var cliente = user.FindFirst("cliente")?.Value ?? "";
            return Results.Ok(new { url = "baixar?t=" + BilheteDownload.Gerar(cliente, config) });
        }).RequireAuthorization().RequireRateLimiting("cadastro-consulta");

        // Dados que as páginas mostram: instalador (data e tamanho) e contato do suporte.
        app.MapGet("/site/info", (IConfiguration config, IWebHostEnvironment env, Fuso fuso) =>
        {
            var arquivo = Instalador(config, env);
            var whatsapp = new string((config["Site:WhatsApp"] ?? "").Where(char.IsAsciiDigit).ToArray());
            var email = (config["Site:Email"] ?? "").Trim();
            return Results.Ok(new
            {
                instalador = arquivo is null ? null : new
                {
                    tamanho = arquivo.Length,
                    data = DateOnly.FromDateTime(fuso.ParaLocal(arquivo.LastWriteTimeUtc))
                },
                whatsapp = whatsapp.Length is >= 10 and <= 15 ? whatsapp : null,
                email = email.Length is > 3 and <= 120 && email.Contains('@') ? email : null
            });
        });
    }

    /// <summary>Loja existe e não está suspensa ("" = empresa padrão).</summary>
    public static async Task<bool> LojaLiberadaAsync(Inquilinos inq, string cliente)
    {
        if (cliente.Length == 0) return true;
        var c = await inq.BuscarAsync(cliente);
        return c is not null && c.Ativo;
    }

    /// <summary>O instalador está na pasta de downloads?</summary>
    public static bool InstaladorDisponivel(IConfiguration config, IWebHostEnvironment env) => Instalador(config, env) is not null;

    private static IResult Pagina(IWebHostEnvironment env, string arquivo)
    {
        var caminho = string.IsNullOrEmpty(env.WebRootPath) ? null : Path.Combine(env.WebRootPath, "site", arquivo);
        return caminho is not null && File.Exists(caminho)
            ? Results.File(caminho, "text/html; charset=utf-8")
            : Results.NotFound();
    }

    /// <summary>Pasta de downloads na VPS (Site:PastaDownloads; padrão /opt/erp-nf/downloads no appsettings).</summary>
    public static string PastaDownloads(IConfiguration config, IWebHostEnvironment env)
    {
        var pasta = config["Site:PastaDownloads"];
        if (string.IsNullOrWhiteSpace(pasta)) pasta = "downloads";
        return Path.IsPathRooted(pasta) ? pasta : Path.Combine(env.ContentRootPath, pasta);
    }

    private static FileInfo? Instalador(IConfiguration config, IWebHostEnvironment env)
    {
        var pasta = PastaDownloads(config, env);
        var arquivo = new FileInfo(Path.Combine(pasta, NomeInstalador));
        if (arquivo.Exists) return arquivo;
        var antigo = new FileInfo(Path.Combine(pasta, NomeInstaladorAntigo));
        return antigo.Exists ? antigo : null;
    }
}

/// <summary>
/// Link de download do instalador: "validade|loja" assinado com uma chave tirada da Jwt:Chave (HMAC-SHA256).
/// Ninguém consegue montar um link válido sem a chave, e ele vence em 30 minutos.
/// </summary>
public static class BilheteDownload
{
    private static readonly TimeSpan Validade = TimeSpan.FromMinutes(30);

    public static string Gerar(string cliente, IConfiguration config)
    {
        var dados = Encoding.UTF8.GetBytes($"{DateTimeOffset.UtcNow.Add(Validade).ToUnixTimeSeconds()}|{cliente}");
        return WebEncoders.Base64UrlEncode(dados) + "." + WebEncoders.Base64UrlEncode(HMACSHA256.HashData(Chave(config), dados));
    }

    /// <summary>Devolve a loja do link ("" = empresa padrão) ou null se o link for inválido ou estiver vencido.</summary>
    public static string? Validar(string? bilhete, IConfiguration config)
    {
        if (string.IsNullOrEmpty(bilhete) || bilhete.Length > 300) return null;
        var partes = bilhete.Split('.');
        if (partes.Length != 2) return null;
        byte[] dados, assinatura;
        try
        {
            dados = WebEncoders.Base64UrlDecode(partes[0]);
            assinatura = WebEncoders.Base64UrlDecode(partes[1]);
        }
        catch (FormatException)
        {
            return null;
        }
        if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(Chave(config), dados), assinatura)) return null;

        var texto = Encoding.UTF8.GetString(dados);
        var barra = texto.IndexOf('|');
        if (barra <= 0 || !long.TryParse(texto[..barra], out var vence)) return null;
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds() > vence ? null : texto[(barra + 1)..];
    }

    // Chave própria para o download (não é a mesma usada nos logins).
    private static byte[] Chave(IConfiguration config) =>
        SHA256.HashData(Encoding.UTF8.GetBytes("erp-nf-instalador|" + config["Jwt:Chave"]));
}
