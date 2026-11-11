using System.Text.RegularExpressions;
using ProjetoJP.Shared;
using Microsoft.EntityFrameworkCore;

namespace ProjetoJP.Api.Dados;

/// <summary>Usuário administrador a criar num banco novo.</summary>
public sealed record AdminInicial(string Nome, string Login, string SenhaHash);

public static class Inicializacao
{
    /// <summary>Prepara o banco padrão (erp_nf) ao iniciar a API.</summary>
    public static async Task PrepararBancoAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
        var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Inicializacao");
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var senha = config["Seed:AdminSenha"];
        // Nunca criar automaticamente uma conta administrativa com senha previsível.
        // Bancos já inicializados continuam funcionando sem a variável de seed.
        // Garante que as tabelas existam antes de consultar usuários em um banco novo.
        await PrepararAsync(db, null, log);
        var temUsuarios = await db.Usuarios.AnyAsync();
        if (!temUsuarios && (string.IsNullOrWhiteSpace(senha) || senha == "admin" || senha.StartsWith("TROQUE", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Banco sem usuários: defina Seed__AdminSenha com uma senha forte antes da primeira inicialização.");
        await PrepararAsync(db, temUsuarios ? null : new AdminInicial("Administrador", "admin", Senha.Gerar(senha!)), log);
    }

    /// <summary>
    /// Cria/atualiza as tabelas de um banco (padrão ou de um cliente) e grava os dados mínimos:
    /// o administrador (se o banco não tiver nenhum usuário) e as formas de pagamento.
    /// Se o projeto tiver migrations, aplica as migrations; senão cria as tabelas direto.
    /// </summary>
    public static async Task PrepararAsync(ErpDbContext db, AdminInicial? admin, ILogger log)
    {
        if (db.Database.GetMigrations().Any())
            await db.Database.MigrateAsync();
        else
            await db.Database.EnsureCreatedAsync();

        // Compatibilidade de tabelas antigas e atualizações transacionais/versionadas.
        await MigracoesBanco.AplicarAsync(db, log);
        // Falha explicitamente se as novas tabelas de segurança não estiverem disponíveis:
        // não deixar a API iniciar com revogação de sessões aparentemente ativa, mas quebrada.
        try
        {
            await db.SessoesAcesso.AsNoTracking().AnyAsync();
            await db.Auditorias.AsNoTracking().AnyAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "As tabelas de segurança (SessoesAcesso/Auditorias) não foram inicializadas. Verifique os logs e permissões do PostgreSQL.", ex);
        }
        // Cada login cria uma sessão: apaga as vencidas há mais de 30 dias para a tabela não crescer sem fim.
        try
        {
            var limite = DateTime.UtcNow.AddDays(-30);
            await db.SessoesAcesso.Where(s => s.ExpiraEm < limite).ExecuteDeleteAsync();
        }
        catch (Exception ex) { log.LogWarning(ex, "Não foi possível limpar sessões antigas"); }

        await InicializarCatalogoPublicadoAsync(db);

        if (admin is not null && !await db.Usuarios.AnyAsync())
        {
            var novo = new Usuario
            {
                Id = Guid.NewGuid(),
                Nome = admin.Nome,
                Login = admin.Login.Trim().ToLowerInvariant(),
                SenhaHash = admin.SenhaHash,
                Perfil = Perfil.Admin
            };
            db.Usuarios.Add(novo);
            // O administrador inicial já vai para os caixas: sem isso um caixa novo não teria nenhum login.
            db.UsuariosPublicados.Add(new UsuarioPublicado
            {
                Id = novo.Id, Nome = novo.Nome, Login = novo.Login, SenhaHash = novo.SenhaHash,
                Perfil = novo.Perfil, Ativo = true, PublicadoEm = DateTime.UtcNow
            });
            log.LogWarning("Usuário administrador '{Login}' criado.", admin.Login);
        }

        if (!await db.FormasPagamento.AnyAsync())
        {
            db.FormasPagamento.AddRange(
                new FormaPagamento { Id = Guid.NewGuid(), Descricao = "Dinheiro", PermiteTroco = true, Ordem = 1 },
                new FormaPagamento { Id = Guid.NewGuid(), Descricao = "PIX", Ordem = 2 },
                new FormaPagamento { Id = Guid.NewGuid(), Descricao = "Cartão de débito", Ordem = 3 },
                new FormaPagamento { Id = Guid.NewGuid(), Descricao = "Cartão de crédito", Ordem = 4 });
        }

        await db.SaveChangesAsync();
    }
    /// <summary>
    /// Existing products are published exactly once, on the first upgrade. A durable marker
    /// ensures new drafts will never be inadvertently published after a future reboot.
    /// </summary>
    private static async Task InicializarCatalogoPublicadoAsync(ErpDbContext db)
    {
        if (await db.ControlesPublicacao.AnyAsync()) return;
        await using var tx = await db.Database.BeginTransactionAsync();
        if (!await db.ControlesPublicacao.AnyAsync())
        {
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "ProdutosPublicados"
                    ("Id", "Codigo", "CodigoBarras", "Descricao", "Unidade", "PrecoVenda", "Ativo", "PublicadoEm")
                SELECT "Id", "Codigo", "CodigoBarras", "Descricao", "Unidade", "PrecoVenda", "Ativo", NOW()
                FROM "Produtos" WHERE TRUE
                ON CONFLICT ("Id") DO NOTHING;
                """);
            db.ControlesPublicacao.Add(new ControlePublicacao { Id = 1, InicializadoEm = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        await tx.CommitAsync();
    }

}
