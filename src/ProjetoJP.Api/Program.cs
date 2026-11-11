using System.Text;
using System.Threading.RateLimiting;
using ProjetoJP.Api.Dados;
using ProjetoJP.Api.Endpoints;
using ProjetoJP.Api.Servicos;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

var chaveJwt = config["Jwt:Chave"];
if (string.IsNullOrWhiteSpace(chaveJwt) || chaveJwt.Length < 32)
    throw new InvalidOperationException("Configure Jwt:Chave com pelo menos 32 caracteres (appsettings ou variável de ambiente Jwt__Chave).");
if (builder.Environment.IsProduction() && chaveJwt.StartsWith("TROQUE", StringComparison.Ordinal))
    throw new InvalidOperationException("Em produção a Jwt:Chave de exemplo não pode ser usada. Defina Jwt__Chave no /etc/erp-nf/api.env.");

// Vários clientes, um banco por cliente: o banco de cada pedido vem do endereço (veja Inquilinos).
builder.Services.AddSingleton<Inquilinos>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddDbContext<ErpDbContext>((sp, o) =>
{
    var http = sp.GetRequiredService<IHttpContextAccessor>().HttpContext;
    var inquilino = http?.Items[Inquilino.ChaveItem] as Inquilino;
    o.UseNpgsql(inquilino?.StringConexao ?? config.GetConnectionString("Erp"));
});

// Pedido de cadastro é público: limita por IP para ninguém encher o master de pedidos.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("cadastro-pedido", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "?",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromHours(1) }));
    o.AddPolicy("suporte-tecnico", ctx => RateLimitPartition.GetFixedWindowLimiter(
        (ctx.Connection.RemoteIpAddress?.ToString() ?? "?") + ":" +
        (ctx.Request.PathBase.Value ?? ""),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 8, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
    o.AddPolicy("login-dispositivo", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "?",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
    // ERP Master: limite por IP (dificulta tentar adivinhar a chave master).
    o.AddPolicy("master", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "?",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 600, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
    o.AddPolicy("cadastro-consulta", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "?",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(10) }));
});

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidIssuer = config["Jwt:Emissor"],
            ValidAudience = config["Jwt:Emissor"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(chaveJwt)),
            ClockSkew = TimeSpan.FromMinutes(2)
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Admin", p => p.RequireRole("Admin"))
    .AddPolicy("Gerente", p => p.RequireRole("Admin", "Gerente"))
    .AdicionarPoliticas(); // uma política por permissão (perm:ver_custo, perm:ver_vendas...)
builder.Services.AddSingleton<IAuthorizationHandler, PermissaoHandler>();

builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<Fuso>();

var app = builder.Build();

// Atrás do Nginx: confia no IP/protocolo repassados pelo proxy local.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

// Erros não tratados viram JSON { erro } — a Retaguarda e o PDV mostram essa mensagem.
app.UseExceptionHandler(e => e.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, msg) = ex switch
    {
        DbUpdateConcurrencyException => (409, "O registro foi alterado por outro usuário/caixa ao mesmo tempo. Tente novamente."),
        DbUpdateException => (400, "Não foi possível gravar: verifique se não há dados duplicados ou inválidos."),
        _ => (500, "Erro interno no servidor.")
    };
    ctx.Response.StatusCode = status;
    await ctx.Response.WriteAsJsonAsync(new { erro = msg });
}));

// Qual cliente: /lojaabc/api/... → banco do cliente "lojaabc" (o caminho segue como /api/...).
// Sem código (/api/..., /health, /master, /cadastro) e páginas do site (/, /painel, /site/...) → cliente padrão (banco erp_nf).
var inquilinos = app.Services.GetRequiredService<Inquilinos>();
app.Use(async (ctx, next) =>
{
    var caminho = ctx.Request.Path.Value ?? "/";
    var partes = caminho.TrimStart('/').Split('/', 2);
    var primeiro = partes[0].ToLowerInvariant();

    if (primeiro is "" or "api" or "health" or "master" or "cadastro" || SitePublico.Caminhos.Contains(primeiro))
    {
        ctx.Items[Inquilino.ChaveItem] = inquilinos.Padrao;
        await next();
        return;
    }

    var cliente = CodigoCliente.Valido(primeiro) ? await inquilinos.BuscarAsync(primeiro) : null;
    if (cliente is null)
    {
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        await ctx.Response.WriteAsJsonAsync(new { erro = "Servidor não encontrado. Confira o nome digitado." });
        return;
    }
    if (!cliente.Ativo)
    {
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        await ctx.Response.WriteAsJsonAsync(new { erro = "Acesso suspenso. Fale com o suporte." });
        return;
    }

    ctx.Items[Inquilino.ChaveItem] = new Inquilino(cliente.Codigo, inquilinos.StringDoBanco(cliente.Banco));
    ctx.Request.PathBase = ctx.Request.PathBase.Add("/" + partes[0]);
    ctx.Request.Path = "/" + (partes.Length > 1 ? partes[1] : "");
    await next();
});

// Site público (cadastro, painel online, download): cabeçalhos de segurança e arquivos de wwwroot.
app.UseCabecalhosDoSite();
app.UseArquivosDoSite();

// Rotas depois de escolher o cliente (senão o caminho ainda teria o código na frente).
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();

// Um token vale só no cliente em que foi gerado (o login de uma loja não abre outra).
app.Use(async (ctx, next) =>
{
    if (ctx.User.Identity?.IsAuthenticated == true)
    {
        var doToken = ctx.User.FindFirst("cliente")?.Value ?? "";
        var atual = (ctx.Items[Inquilino.ChaveItem] as Inquilino)?.Codigo ?? "";
        if (doToken != atual)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsJsonAsync(new { erro = "Sessão de outro servidor. Entre novamente." });
            return;
        }
    }
    // Verificação por requisição: a revogação de uma sessão é efetiva imediatamente.
    // Tokens emitidos antes da atualização (sem sessao_id) exigem novo login.
    if (ctx.User.Identity?.IsAuthenticated == true)
    {
        if (!Guid.TryParse(ctx.User.FindFirst("sessao_id")?.Value, out var sessaoId) ||
            !Guid.TryParse(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var usuarioId))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsJsonAsync(new { erro = "Sessão antiga. Entre novamente." });
            return;
        }
        var banco = ctx.RequestServices.GetRequiredService<ErpDbContext>();
        var ativa = await banco.SessoesAcesso.AsNoTracking().AnyAsync(s =>
            s.Id == sessaoId && s.UsuarioId == usuarioId && s.RevogadaEm == null &&
            s.ExpiraEm > DateTime.UtcNow);
        if (!ativa)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsJsonAsync(new { erro = "Sessão encerrada ou expirada. Entre novamente." });
            return;
        }
    }
    // Sessoes iniciadas por computador lembrado precisam continuar validas
    // apenas enquanto o dispositivo e seu usuario permanecem autorizados.
    if (ctx.User.Identity?.IsAuthenticated == true &&
        ctx.User.FindFirst("metodo_login")?.Value == "dispositivo")
    {
        var dispositivoStr = ctx.User.FindFirst("dispositivo_id")?.Value;
        var usuarioStr = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(dispositivoStr, out var dispositivoId) ||
            !Guid.TryParse(usuarioStr, out var usuarioId))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsJsonAsync(new { erro = "Sessão de computador inválida. Entre com a senha." });
            return;
        }
        var db = ctx.RequestServices.GetRequiredService<ErpDbContext>();
        var registro = await db.Dispositivos.AsNoTracking()
            .Where(d => d.Id == dispositivoId && d.UsuarioId == usuarioId && !d.Revogado)
            .Select(d => new { d.CriadoEm, d.ExpiraEm, d.SenhaCarimbo, d.PinHash })
            .FirstOrDefaultAsync();
        var usuario = await db.Usuarios.AsNoTracking()
            .Where(u => u.Id == usuarioId && u.Ativo)
            .Select(u => new { u.SenhaHash })
            .FirstOrDefaultAsync();
        var carimboAtual = usuario is null ? "" : Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(usuario.SenhaHash)))[..32];
        if (registro is null || registro.ExpiraEm <= DateTime.UtcNow ||
            registro.CriadoEm.AddDays(7) <= DateTime.UtcNow || registro.PinHash is not null ||
            usuario is null || registro.SenhaCarimbo != carimboAtual)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsJsonAsync(new { erro = "Este computador não está mais autorizado (esquecido, vencido ou senha trocada). Entre com a senha." });
            return;
        }
    }
    await next();
});
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { ok = true, hora = DateTime.UtcNow }));

app.MapAuth();
app.MapDispositivos(); // "Lembrar este computador" (sem guardar senha)
app.MapSegurancaAdministrativa(); // auditoria e encerramento de sessoes
app.MapSincronizacao();
app.MapSuporteTecnico();
app.MapEstoque();
app.MapEntradas(); // entrada de produtos com vários itens (compra / nota do fornecedor)
app.MapSaidas();   // saída de produtos com vários itens (perda, vencido, uso interno, devolução...)
app.MapLojaOperacoes(); // devolução parcial de venda e fiado
app.MapCargasPromocoes(); // publicacao controlada e promocoes agendadas
app.MapRelatorios();
app.MapImportacao();
app.MapMaster();
app.MapCadastro();
app.MapSitePublico(); // site de cadastro, painel online pelo celular e download do instalador
app.MapAtualizacaoProgramas(); // atualização automática do Caixa e da Retaguarda

// Consultar é livre para quem está logado; criar/alterar/desativar depende da permissão do usuário.
app.MapCrud("/api/categorias", new CategoriaMapper()).RequireAuthorization()
    .AddEndpointFilter(PermissoesApi.SoEscrita(Permissoes.EditarProdutos));
app.MapCrud("/api/produtos", new ProdutoMapper()).RequireAuthorization()
    .AddEndpointFilter(PermissoesApi.SoEscrita(Permissoes.EditarProdutos))
    .AddEndpointFilter(PermissoesApi.ProtegerCusto); // custo zerado para quem não pode ver
app.MapProdutosPainel(); // resumo, paginação e gráfico da tela Produtos e Estoque
app.MapCrud("/api/fornecedores", new FornecedorMapper()).RequireAuthorization(PermissoesApi.Politica(Permissoes.Fornecedores));
app.MapCrud("/api/clientes", new ClienteMapper()).RequireAuthorization()
    .AddEndpointFilter(PermissoesApi.SoEscrita(Permissoes.EditarClientes));
app.MapPermissoesUsuarios(); // tela "Permissões" (Admin)
app.MapOrcamentos();        // orçamentos que os caixas enviaram
app.MapCrud("/api/formas-pagamento", new FormaPagamentoMapper()).RequireAuthorization("Gerente");
app.MapCrud("/api/usuarios", new UsuarioMapper()).RequireAuthorization("Admin")
    .AddEndpointFilter(AcessoSensivel.FiltrarAlteracao);
app.MapCrud("/api/terminais", new TerminalMapper()).RequireAuthorization("Admin")
    .AddEndpointFilter(AcessoSensivel.FiltrarAlteracao)
    .AddEndpointFilter(LimiteCaixas.Filtro); // limite de caixas do plano (ERP Master)

await app.PrepararBancoAsync();

// Banco master (lista de clientes) e atualização da estrutura dos bancos dos clientes.
try
{
    await inquilinos.GarantirMasterAsync();
    await inquilinos.AtualizarTodosAsync();
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Vários clientes indisponível (o usuário do banco precisa de CREATEDB). O cliente padrão funciona normalmente.");
}

app.Run();
