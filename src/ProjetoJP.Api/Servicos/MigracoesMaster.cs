using ProjetoJP.Api.Dados;
using Microsoft.EntityFrameworkCore;

namespace ProjetoJP.Api.Servicos;

/// <summary>
/// Migrações incrementais para erp_master. Não recria o banco e não apaga clientes/pedidos.
/// Novas mudanças devem gerar uma nova versão, sem modificar as versões já implantadas.
/// </summary>
public static class MigracoesMaster
{
    public static async Task AplicarAsync(MasterDbContext db, ILogger log)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74016, 20261103)");
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "__ErpMasterSchemaVersoes" (
                "Versao" integer NOT NULL PRIMARY KEY,
                "AplicadaEm" timestamp with time zone NOT NULL DEFAULT now()
            )
            """);
        var versao = await db.Database.SqlQueryRaw<int>(
            "SELECT COALESCE(MAX(\"Versao\"), 0)::integer AS \"Value\" FROM \"__ErpMasterSchemaVersoes\"").SingleAsync();

        if (versao < 1)
        {
            foreach (var sql in new[]
            {
                "ALTER TABLE \"Clientes\" ADD COLUMN IF NOT EXISTS \"Documento\" varchar(14)",
                "ALTER TABLE \"Clientes\" ADD COLUMN IF NOT EXISTS \"Responsavel\" varchar(120)",
                "ALTER TABLE \"Clientes\" ADD COLUMN IF NOT EXISTS \"Telefone\" varchar(11)",
                "ALTER TABLE \"Clientes\" ADD COLUMN IF NOT EXISTS \"Email\" varchar(120)",
                "ALTER TABLE \"Clientes\" ADD COLUMN IF NOT EXISTS \"Cidade\" varchar(80)",
                "ALTER TABLE \"Clientes\" ADD COLUMN IF NOT EXISTS \"Uf\" varchar(2)",
                "ALTER TABLE \"Pedidos\" ADD COLUMN IF NOT EXISTS \"Uf\" varchar(2)",
                "ALTER TABLE \"Clientes\" ADD COLUMN IF NOT EXISTS \"LimiteCaixas\" integer"
            }) await db.Database.ExecuteSqlRawAsync(sql);
            await db.Database.ExecuteSqlRawAsync("INSERT INTO \"__ErpMasterSchemaVersoes\" (\"Versao\") VALUES (1)");
            log.LogInformation("Migração master versão 1 aplicada");
        }

        if (versao < 2)
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "SuportesTecnicos" (
                    "Id" uuid NOT NULL PRIMARY KEY,
                    "ClienteId" uuid NOT NULL,
                    "Login" varchar(60) NOT NULL,
                    "SenhaHash" text NOT NULL,
                    "Ativo" boolean NOT NULL,
                    "AtualizadoEm" timestamp with time zone NOT NULL
                )
                """);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_SuportesTecnicos_ClienteId"
                    ON "SuportesTecnicos" ("ClienteId")
                """);
            await db.Database.ExecuteSqlRawAsync("INSERT INTO \"__ErpMasterSchemaVersoes\" (\"Versao\") VALUES (2)");
            log.LogInformation("Migração master versão 2 aplicada");
        }

        var faltando = await db.Database.SqlQueryRaw<string>("""
            SELECT (v.tabela || '.' || v.coluna)::text AS "Value"
            FROM (VALUES
                ('Clientes', 'Documento'),
                ('Clientes', 'LimiteCaixas'),
                ('Pedidos', 'Uf'),
                ('SuportesTecnicos', 'SenhaHash')
            ) AS v(tabela, coluna)
            WHERE NOT EXISTS (
                SELECT 1 FROM information_schema.columns c
                WHERE c.table_schema = current_schema()
                  AND c.table_name = v.tabela AND c.column_name = v.coluna
            )
            """).ToListAsync();
        if (faltando.Count > 0)
            throw new InvalidOperationException("Banco Master incompleto: " + string.Join(", ", faltando));

        await tx.CommitAsync();
    }
}
