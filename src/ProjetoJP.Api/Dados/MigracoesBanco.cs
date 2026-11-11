using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace ProjetoJP.Api.Dados;

/// <summary>
/// Atualizações versionadas para bancos PostgreSQL existentes (inclusive bancos criados com EnsureCreated).
/// As versões são independentes por banco/cliente. Uma falha reverte a transação e impede a inicialização
/// daquela base, em vez de registrar um aviso e deixar o esquema parcialmente atualizado.
/// Uma mudança futura no modelo deve ser adicionada aqui como NOVA versão; não editar versões aplicadas.
/// </summary>
public static class MigracoesBanco
{
    public const int VersaoSuportada = 12;

    public static async Task AplicarAsync(ErpDbContext db, ILogger log)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        // Serializa duas inicializações simultâneas do ERP no mesmo banco.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74015, 20261103)");
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "__ErpSchemaVersoes" (
                "Versao" integer NOT NULL PRIMARY KEY,
                "AplicadaEm" timestamp with time zone NOT NULL DEFAULT now()
            )
            """);

        var versao = await db.Database.SqlQueryRaw<int>(
            "SELECT COALESCE(MAX(\"Versao\"), 0)::integer AS \"Value\" FROM \"__ErpSchemaVersoes\"").SingleAsync();

        if (versao > VersaoSuportada)
            throw new InvalidOperationException($"Banco na versão {versao}, superior à suportada ({VersaoSuportada}). Atualize a API antes de iniciar.");

        // Bootstrap transacional de tabelas de instalações antigas. A partir da versão 7,
        // novas mudanças de esquema devem entrar em versões explícitas (sem criá-las silenciosamente).
        if (versao < 7) await CriarTabelasNovasAsync(db);

        if (versao < 1)
        {
            // Compatibilidade com instalações anteriores da tabela de computadores lembrados.
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Dispositivos\" ADD COLUMN IF NOT EXISTS \"PinHash\" text");
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Dispositivos\" ADD COLUMN IF NOT EXISTS \"ErrosPin\" integer NOT NULL DEFAULT 0");
            await RegistrarAsync(db, 1);
            log.LogInformation("Migração do banco aplicada: versão 1 (dispositivos)");
        }

        if (versao < 2)
        {
            // Detectar duplicações antigas ANTES de tentar instalar a restrição única.
            // Não corrigir automaticamente dados históricos: exigir análise manual.
            await db.Database.ExecuteSqlRawAsync("""
                DO $erro$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "Entradas" GROUP BY "Numero" HAVING COUNT(*) > 1) THEN
                        RAISE EXCEPTION 'Existem entradas com números repetidos. Corrija os registros antes de atualizar o ERP.';
                    END IF;
                END
                $erro$
                """);

            await db.Database.ExecuteSqlRawAsync("CREATE SEQUENCE IF NOT EXISTS \"Entradas_Numero_seq\" AS integer START WITH 1");
            // A sequência começa no maior número histórico do cliente. Se não existem entradas, o primeiro é 1.
            await db.Database.ExecuteSqlRawAsync("""
                SELECT setval('"Entradas_Numero_seq"',
                    GREATEST(1, (SELECT COALESCE(MAX("Numero"), 0) FROM "Entradas")),
                    (SELECT COALESCE(MAX("Numero"), 0) > 0 FROM "Entradas"))
                """);
            // A versão antiga tinha um índice não único com este mesmo nome.
            await db.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS \"IX_Entradas_Numero\"");
            await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX \"IX_Entradas_Numero\" ON \"Entradas\" (\"Numero\")");
            await RegistrarAsync(db, 2);
            log.LogInformation("Migração do banco aplicada: versão 2 (numeração exclusiva de entradas)");
        }

        if (versao < 3)
        {
            // Data de cadastro do produto (resumo "novos no mês"). Produtos antigos ficam sem data (null).
            // Inclui a data de cadastro dos produtos.
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"Produtos\" ADD COLUMN IF NOT EXISTS \"CriadoEm\" timestamp with time zone NULL");
            await RegistrarAsync(db, 3);
            log.LogInformation("Migração do banco aplicada: versão 3 (data de cadastro do produto)");
        }

        if (versao < 4)
        {
            // Permissões por usuário (null = padrão do perfil). Orçamentos: tabelas novas, criadas pela Inicializacao.
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"Usuarios\" ADD COLUMN IF NOT EXISTS \"Permissoes\" character varying(1000) NULL");
            await RegistrarAsync(db, 4);
            log.LogInformation("Migração do banco aplicada: versão 4 (permissões por usuário)");
        }

        if (versao < 5)
        {
            // Saída de produtos com vários itens: numeração própria (a tabela é criada pela Inicializacao).
            await db.Database.ExecuteSqlRawAsync("CREATE SEQUENCE IF NOT EXISTS \"Saidas_Numero_seq\" AS integer START WITH 1");
            await db.Database.ExecuteSqlRawAsync("""
                SELECT setval('"Saidas_Numero_seq"',
                    GREATEST(1, (SELECT COALESCE(MAX("Numero"), 0) FROM "Saidas")),
                    (SELECT COALESCE(MAX("Numero"), 0) > 0 FROM "Saidas"))
                """);
            await RegistrarAsync(db, 5);
            log.LogInformation("Migração do banco aplicada: versão 5 (saída de produtos)");
        }

        if (versao < 6)
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"Orcamentos\" ADD COLUMN IF NOT EXISTS \"Revisao\" bigint NOT NULL DEFAULT 1");
            await RegistrarAsync(db, 6);
            log.LogInformation("Migração aplicada: versão 6 (controle de revisão dos orçamentos)");
        }
        if (versao < 7)
        {
            // Registra o bootstrap que antigamente acontecia fora das migrações.
            await RegistrarAsync(db, 7);
            log.LogInformation("Migração aplicada: versão 7 (tabelas novas sob transação)");
        }

        if (versao < 8)
        {
            // Somente fotos de produtos. A remoção foi autorizada no ambiente de teste.
            // Não apaga Produtos, imagens de aparência, movimentações, vendas ou outras tabelas.
            await db.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS \"ProdutoFotos\"");
            await RegistrarAsync(db, 8);
            log.LogInformation("Migração aplicada: versão 8 (fotos dos produtos excluídas)");
        }

        if (versao < 9)
        {
            await db.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "VendaItens" ADD COLUMN IF NOT EXISTS "CustoUnitario" numeric NOT NULL DEFAULT 0;
                ALTER TABLE "VendaItens" ADD COLUMN IF NOT EXISTS "QuantidadeDevolvida" numeric NOT NULL DEFAULT 0;
                ALTER TABLE "Clientes" ADD COLUMN IF NOT EXISTS "SaldoFiado" numeric NOT NULL DEFAULT 0;
                ALTER TABLE "FormasPagamento" ADD COLUMN IF NOT EXISTS "EhFiado" boolean NOT NULL DEFAULT false;
                CREATE TABLE IF NOT EXISTS "LancamentosFiado" (
                    "Id" uuid NOT NULL PRIMARY KEY,
                    "AtualizadoEm" timestamp with time zone NOT NULL,
                    "ClienteId" uuid NOT NULL REFERENCES "Clientes"("Id"),
                    "VendaId" uuid NULL,
                    "Valor" numeric NOT NULL,
                    "Observacao" character varying(200) NULL,
                    "UsuarioId" uuid NULL
                );
                CREATE INDEX IF NOT EXISTS "IX_LancamentosFiado_ClienteId" ON "LancamentosFiado" ("ClienteId");
                CREATE INDEX IF NOT EXISTS "IX_LancamentosFiado_VendaId" ON "LancamentosFiado" ("VendaId");
                """);
            await RegistrarAsync(db, 9);
            log.LogInformation("Migração aplicada: versão 9 (custo da venda, devolução e fiado)");
        }

        if (versao < 10)
        {
            // Usuários só chegam aos caixas depois de um envio manual. Não copia os atuais: quem já está no caixa continua,
            // e a Retaguarda mostra todos como pendentes até alguém clicar em Enviar.
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "UsuariosPublicados" (
                    "Id" uuid NOT NULL PRIMARY KEY REFERENCES "Usuarios"("Id") ON DELETE CASCADE,
                    "Nome" character varying(120) NOT NULL,
                    "Login" character varying(60) NOT NULL,
                    "SenhaHash" text NOT NULL,
                    "Perfil" integer NOT NULL,
                    "Ativo" boolean NOT NULL,
                    "Permissoes" character varying(1000) NULL,
                    "PublicadoEm" timestamp with time zone NOT NULL
                );
                CREATE INDEX IF NOT EXISTS "IX_UsuariosPublicados_PublicadoEm" ON "UsuariosPublicados" ("PublicadoEm");
                """);
            await RegistrarAsync(db, 10);
            log.LogInformation("Migração aplicada: versão 10 (envio manual de usuários aos caixas)");
        }

        if (versao < 11)
        {
            // 1) Usuários que já existiam vão para os caixas uma vez (a versão 10 não copiava ninguém e
            //    um caixa novo ou reinstalado ficava sem nenhum login). Os já enviados não mudam.
            // 2) Valor devolvido de cada venda, para os relatórios mostrarem o valor líquido.
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "UsuariosPublicados" ("Id", "Nome", "Login", "SenhaHash", "Perfil", "Ativo", "Permissoes", "PublicadoEm")
                SELECT "Id", "Nome", "Login", "SenhaHash", "Perfil", "Ativo", "Permissoes", NOW()
                FROM "Usuarios" WHERE TRUE
                ON CONFLICT ("Id") DO NOTHING;
                ALTER TABLE "Vendas" ADD COLUMN IF NOT EXISTS "ValorDevolvido" numeric NOT NULL DEFAULT 0;
                """);
            await RegistrarAsync(db, 11);
            log.LogInformation("Migração aplicada: versão 11 (usuários atuais enviados aos caixas; valor devolvido nas vendas)");
        }

        if (versao < 12)
        {
            // Preço de atacado (por quantidade) no produto, no catálogo dos caixas e nas promoções.
            await db.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "Produtos" ADD COLUMN IF NOT EXISTS "PrecoAtacado" numeric NOT NULL DEFAULT 0;
                ALTER TABLE "Produtos" ADD COLUMN IF NOT EXISTS "QtdMinimaAtacado" numeric NOT NULL DEFAULT 0;
                ALTER TABLE "ProdutosPublicados" ADD COLUMN IF NOT EXISTS "PrecoAtacado" numeric NOT NULL DEFAULT 0;
                ALTER TABLE "ProdutosPublicados" ADD COLUMN IF NOT EXISTS "QtdMinimaAtacado" numeric NOT NULL DEFAULT 0;
                ALTER TABLE "Promocoes" ADD COLUMN IF NOT EXISTS "QuantidadeMinima" numeric NOT NULL DEFAULT 0;
                """);
            await RegistrarAsync(db, 12);
            log.LogInformation("Migração aplicada: versão 12 (preço de atacado e promoção de atacado)");
        }

        // Verifica estruturas essenciais: IF NOT EXISTS sozinho não confere colunas antigas divergentes.
        var faltando = await db.Database.SqlQueryRaw<string>("""
            SELECT (v.tabela || '.' || v.coluna)::text AS "Value"
            FROM (VALUES
                ('Entradas', 'Numero'),
                ('Entradas', 'Cancelada'),
                ('Dispositivos', 'PinHash'),
                ('Dispositivos', 'ErrosPin'),
                ('SessoesAcesso', 'RevogadaEm'),
                ('Auditorias', 'Acao'),
                ('ProdutosPublicados', 'PublicadoEm'),
                ('Promocoes', 'PrecoPromocional'),
                ('Produtos', 'CriadoEm'),
                ('Fornecedores', 'Nome'),
                ('Usuarios', 'Permissoes'),
                ('Orcamentos', 'Numero'),
                ('Orcamentos', 'Revisao'),
                ('OrcamentoItens', 'Quantidade'),
                ('Saidas', 'Numero'),
                ('SaidaItens', 'Quantidade'),
                ('VendaItens', 'CustoUnitario'),
                ('VendaItens', 'QuantidadeDevolvida'),
                ('Clientes', 'SaldoFiado'),
                ('FormasPagamento', 'EhFiado'),
                ('LancamentosFiado', 'Valor'),
                ('UsuariosPublicados', 'PublicadoEm'),
                ('Vendas', 'ValorDevolvido'),
                ('Produtos', 'PrecoAtacado'),
                ('ProdutosPublicados', 'QtdMinimaAtacado'),
                ('Promocoes', 'QuantidadeMinima')
            ) AS v(tabela, coluna)
            WHERE NOT EXISTS (
                SELECT 1 FROM information_schema.columns c
                WHERE c.table_schema = current_schema()
                  AND c.table_name = v.tabela AND c.column_name = v.coluna
            )
            """).ToListAsync();
        if (faltando.Count > 0)
            throw new InvalidOperationException("Esquema PostgreSQL incompleto: " + string.Join(", ", faltando) +
                ". Não continue a atualização até aplicar uma migração explícita.");

        await tx.CommitAsync();
    }

    /// <summary>
    /// Compatibilidade para tabelas ainda ausentes em bancos antigos criados por EnsureCreated.
    /// Colunas, índices que mudaram e restrições são atualizados pela MigracoesBanco, de forma versionada.
    /// </summary>
    private static async Task CriarTabelasNovasAsync(ErpDbContext db)
    {
        var script = db.Database.GenerateCreateScript();
        foreach (var parte in Regex.Split(script, @";\s*(?:\r?\n|$)"))
        {
            var sql = parte.Trim();
            string? comando = null;
            foreach (var inicio in new[] { "CREATE TABLE ", "CREATE UNIQUE INDEX ", "CREATE INDEX " })
            {
                if (sql.StartsWith(inicio, StringComparison.Ordinal) &&
                    !sql.StartsWith(inicio + "IF NOT EXISTS", StringComparison.Ordinal))
                {
                    comando = inicio + "IF NOT EXISTS " + sql[inicio.Length..];
                    break;
                }
            }
            if (comando is null) continue;
            // A atualização desse índice passa pela migração versionada (índice antigo não era único).
            if (comando.Contains("\"IX_Entradas_Numero\"", StringComparison.Ordinal)) continue;
            try
            {
                // Chaves { } seriam lidas como parâmetros pelo ExecuteSqlRaw.
                await db.Database.ExecuteSqlRawAsync(comando.Replace("{", "{{").Replace("}", "}}"));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Falha ao criar estrutura PostgreSQL necessária: " + comando[..Math.Min(100, comando.Length)] +
                    ". A inicialização foi interrompida para não usar banco incompleto.", ex);
            }
        }
    }


    private static async Task RegistrarAsync(ErpDbContext db, int versao)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"__ErpSchemaVersoes\" (\"Versao\") VALUES ({versao})");
    }
}
