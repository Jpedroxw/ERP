using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ProjetoJP.Api.Dados;
using ProjetoJP.Api.Servicos;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ProjetoJP.Api.Endpoints;

/// <summary>Login por chave do computador. Nunca guarda senhas nem chaves privadas na API.</summary>
public static class DispositivosEndpoints
{
    private static readonly TimeSpan Validade = TimeSpan.FromDays(7);
    private const int MaximoPorUsuario = 10;

    public static void MapDispositivos(this IEndpointRouteBuilder app)
    {
        // Registro: apenas apos autenticar com SENHA recentemente, nao via dispositivo lembrado.
        app.MapPost("/api/auth/dispositivos", async (RegistrarDispositivoRequest req, ClaimsPrincipal user,
            ErpDbContext db, ILoggerFactory logs) =>
        {
            if (!AcessoSensivel.SenhaRecente(user)) return AcessoSensivel.PedirSenha();
            if (user.UsuarioId() is not { } usuarioId) return Results.Unauthorized();
            var u = await db.Usuarios.FirstOrDefaultAsync(x => x.Id == usuarioId && x.Ativo);
            if (u is null) return Results.Unauthorized();

            string chave;
            try
            {
                var bytes = Convert.FromBase64String(req.ChavePublica ?? "");
                using var ec = ECDsa.Create();
                ec.ImportSubjectPublicKeyInfo(bytes, out _);
                if (ec.KeySize != 256) return Extensoes.Erro("Chave pública do computador inválida.");
                chave = Convert.ToBase64String(bytes);
            }
            catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
            {
                return Extensoes.Erro("Chave pública do computador inválida.");
            }

            var ativos = await db.Dispositivos.Where(d => d.UsuarioId == u.Id && !d.Revogado)
                .OrderByDescending(d => d.CriadoEm).ToListAsync();
            foreach (var velho in ativos.Skip(MaximoPorUsuario - 1)) velho.Revogado = true;

            var nome = (req.Nome ?? "").Trim();
            if (!Formatos.TextoSeguro(nome) || nome.Length > 120)
                return Extensoes.Erro("Nome do computador inválido (máximo 120 caracteres).");
            var agora = DateTime.UtcNow;
            var d = new DispositivoConfiavel
            {
                Id = Guid.NewGuid(), UsuarioId = u.Id,
                Nome = nome.Length == 0 ? "Computador" : nome[..Math.Min(nome.Length, 120)],
                ChavePublica = chave, Tpm = req.Tpm,
                CriadoEm = agora, ExpiraEm = agora + Validade,
                SenhaCarimbo = CarimboDaSenha(u.SenhaHash), PinHash = null, ErrosPin = 0
            };
            db.Dispositivos.Add(d);
            await db.SaveChangesAsync();
            logs.CreateLogger("AcessoLembrado").LogInformation(
                "Computador registrado: Usuario={UsuarioId}, Dispositivo={DispositivoId}, TPM={Tpm}", u.Id, d.Id, d.Tpm);
            return Results.Ok(new RegistrarDispositivoResposta { Id = d.Id, ExpiraEm = d.ExpiraEm });
        }).RequireAuthorization().RequireRateLimiting("login-dispositivo");

        app.MapGet("/api/auth/dispositivos", async (bool? todos, ClaimsPrincipal user, ErpDbContext db) =>
        {
            if (user.UsuarioId() is not { } usuarioId) return Results.Unauthorized();
            // Admin pode ver os computadores da mesma empresa. Nao ha consulta entre tenants.
            var verTodos = todos == true && user.IsInRole("Admin");
            var consulta = db.Dispositivos.AsNoTracking();
            if (!verTodos) consulta = consulta.Where(x => x.UsuarioId == usuarioId);
            var lista = await (from d in consulta
                               join u in db.Usuarios.AsNoTracking() on d.UsuarioId equals u.Id
                               orderby d.CriadoEm descending
                               select new DispositivoResumoDto
                               {
                                   Id = d.Id, Nome = d.Nome, Usuario = u.Login, Tpm = d.Tpm,
                                   CriadoEm = d.CriadoEm, ExpiraEm = d.ExpiraEm,
                                   UltimoUsoEm = d.UltimoUsoEm, Revogado = d.Revogado,
                                   PrecisaRecadastro = d.PinHash != null
                               }).Take(100).ToListAsync();
            return Results.Ok(lista);
        }).RequireAuthorization();

        app.MapPost("/api/auth/dispositivo/login", async (LoginDispositivoRequest req, HttpContext http,
            ErpDbContext db, TokenService tokens, ILoggerFactory logs) =>
        {
            var log = logs.CreateLogger("AcessoLembrado");
            static IResult Recusa(string msg) => Results.Json(new { erro = msg }, statusCode: 401);
            var d = await db.Dispositivos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == req.DispositivoId);
            if (d is null || d.Revogado) return Recusa("Este computador não está mais lembrado. Entre com a senha.");
            var agora = DateTime.UtcNow;
            if (d.ExpiraEm <= agora || d.CriadoEm.Add(Validade) <= agora)
                return Recusa("O acesso lembrado venceu (7 dias). Entre com a senha.");
            if (d.PinHash is not null)
                return Recusa("Acesso antigo (com PIN). Entre com a senha para lembrar o computador de novo.");

            var login = (req.Login ?? "").Trim().ToLowerInvariant();
            var u = await db.Usuarios.AsNoTracking().FirstOrDefaultAsync(x => x.Id == d.UsuarioId && x.Ativo);
            if (u is null || u.Login != login) return Recusa("Usuário desativado ou diferente. Entre com a senha.");
            if (d.SenhaCarimbo != CarimboDaSenha(u.SenhaHash))
            {
                await db.Dispositivos.Where(x => x.Id == d.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Revogado, true));
                return Recusa("A senha desse usuário foi trocada. Entre com a senha atual.");
            }

            var agoraMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (Math.Abs(agoraMs - req.Carimbo) > 5 * 60_000) return Recusa("Pedido fora do horário. Entre com a senha.");
            if (req.Carimbo <= d.UltimoCarimbo) return Recusa("Pedido repetido. Tente entrar de novo.");

            bool valida;
            try
            {
                using var ec = ECDsa.Create();
                ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(d.ChavePublica), out _);
                valida = ec.VerifyData(Encoding.UTF8.GetBytes(DispositivoAssinatura.Mensagem(d.Id, login, req.Carimbo)),
                    Convert.FromBase64String(req.Assinatura ?? ""), HashAlgorithmName.SHA256);
            }
            catch (Exception ex) when (ex is FormatException or CryptographicException) { valida = false; }
            if (!valida)
            {
                log.LogWarning("Assinatura invalida para Dispositivo={DispositivoId}", d.Id);
                return Recusa("Não foi possível confirmar este computador. Entre com a senha.");
            }

            // Comparar + atualizar no proprio banco, numa unica instrucao: duas requisicoes
            // simultaneas com o mesmo carimbo nao podem gerar dois tokens validos.
            var atualizados = await db.Dispositivos.Where(x => x.Id == d.Id && !x.Revogado &&
                    x.UltimoCarimbo < req.Carimbo && x.ExpiraEm > agora && x.PinHash == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.UltimoCarimbo, req.Carimbo)
                    .SetProperty(x => x.UltimoUsoEm, (DateTime?)agora));
            if (atualizados != 1)
                return Recusa("Pedido repetido, computador esquecido ou acesso vencido. Entre com a senha.");

            var cliente = (http.Items[Inquilino.ChaveItem] as Inquilino)?.Codigo ?? "";
            var sessao = new SessaoAcesso
            {
                UsuarioId = u.Id, DispositivoId = d.Id, ExpiraEm = tokens.Expiracao()
            };
            db.SessoesAcesso.Add(sessao);
            await db.SaveChangesAsync();
            var (token, expira) = tokens.Gerar(u, cliente, porSenha: false,
                dispositivoId: d.Id, sessaoId: sessao.Id);
            log.LogInformation("Login por dispositivo: Usuario={UsuarioId}, Dispositivo={DispositivoId}", u.Id, d.Id);
            return Results.Ok(new LoginResponse
            {
                Token = token, UsuarioId = u.Id, Nome = u.Nome, Perfil = u.Perfil, ExpiraEm = expira,
                Permissoes = [.. Permissoes.Efetivas(u.Perfil, u.Permissoes)]
            });
        }).RequireRateLimiting("login-dispositivo");

        app.MapDelete("/api/auth/dispositivos/{id:guid}", async (Guid id, ClaimsPrincipal user,
            ErpDbContext db, ILoggerFactory logs) =>
        {
            if (user.UsuarioId() is not { } usuarioId) return Results.Unauthorized();
            // O proprio usuario revoga os seus. Um Admin com senha recente tambem pode revogar
            // dispositivos de outros usuarios, mas somente no banco do tenant atual.
            var alvo = await db.Dispositivos.AsNoTracking().Where(x => x.Id == id)
                .Select(x => new { x.UsuarioId }).FirstOrDefaultAsync();
            if (alvo is null) return Results.NoContent();
            if (alvo.UsuarioId != usuarioId)
            {
                if (!user.IsInRole("Admin")) return Results.Forbid();
                if (!AcessoSensivel.SenhaRecente(user)) return AcessoSensivel.PedirSenha();
            }
            await using var transacao = await db.Database.BeginTransactionAsync();
            var quantidade = await db.Dispositivos.Where(x => x.Id == id && !x.Revogado)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Revogado, true));
            if (quantidade > 0)
            {
                // Revogar o dispositivo também encerra os tokens já emitidos por ele.
                await db.SessoesAcesso.Where(x => x.DispositivoId == id && x.RevogadaEm == null)
                    .ExecuteUpdateAsync(x => x.SetProperty(s => s.RevogadaEm, (DateTime?)DateTime.UtcNow));
                RegistroAuditoria.Adicionar(db, user, "RevogarDispositivo", "Dispositivo", id,
                    "Computador lembrado revogado");
                await db.SaveChangesAsync();
                logs.CreateLogger("AcessoLembrado").LogInformation("Dispositivo revogado: Usuario={UsuarioId}, Dispositivo={DispositivoId}", usuarioId, id);
            }
            await transacao.CommitAsync();
            return Results.NoContent();
        }).RequireAuthorization();
    }

    private static string CarimboDaSenha(string senhaHash) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(senhaHash)))[..32];
}

/// <summary>Protege mutacoes administrativas em sessoes iniciadas por computador lembrado.</summary>
public static class AcessoSensivel
{
    /// <summary>
    /// Sessão aberta com senha (login normal) já vale para as alterações administrativas: a confirmação extra de
    /// senha a cada 10 minutos foi retirada a pedido (a Retaguarda não lembra mais o computador, todo login é com
    /// senha). Sessão de "computador lembrado" de versões antigas continua precisando da senha.
    /// </summary>
    public static bool SenhaRecente(ClaimsPrincipal user) =>
        user.FindFirst("metodo_login")?.Value == "senha";

    /// <summary>403 com codigo "confirmar_senha": a Retaguarda abre a janela de senha sozinha e repete o pedido.</summary>
    public static IResult PedirSenha() => Results.Json(
        new { erro = "Para esta alteração, confirme a sua senha.", codigo = "confirmar_senha" },
        statusCode: StatusCodes.Status403Forbidden);

    public static async ValueTask<object?> FiltrarAlteracao(EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        if (HttpMethods.IsGet(context.HttpContext.Request.Method) || SenhaRecente(context.HttpContext.User))
            return await next(context);
        return PedirSenha();
    }
}
