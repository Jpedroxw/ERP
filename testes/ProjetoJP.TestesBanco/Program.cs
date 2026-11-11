using ProjetoJP.Api.Dados;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

// Este teste altera SOMENTE o contador de entradas (consome números da sequência).
// Exigir banco efêmero de testes, nunca um banco de cliente real.
var conexao = Environment.GetEnvironmentVariable("ERP_TEST_PG_CONN") ?? "";
if (Environment.GetEnvironmentVariable("ERP_TESTE_BANCO_CONFIRMAR") != "SIM_TESTE" ||
    string.IsNullOrWhiteSpace(conexao))
{
    Console.Error.WriteLine("Configure ERP_TEST_PG_CONN e ERP_TESTE_BANCO_CONFIRMAR=SIM_TESTE.");
    return 2;
}

var banco = new NpgsqlConnectionStringBuilder(conexao).Database ?? "";
if (!banco.StartsWith("erp_c_bot", StringComparison.OrdinalIgnoreCase) &&
    !banco.StartsWith("erp_nf_teste", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Recusado: use somente um banco com prefixo erp_c_bot ou erp_nf_teste.");
    return 2;
}

var opcoes = new DbContextOptionsBuilder<ErpDbContext>().UseNpgsql(conexao).Options;
await using (var contexto = new ErpDbContext(opcoes))
{
    await Inicializacao.PrepararAsync(contexto, null, NullLogger.Instance);
    var versao = await contexto.Database.SqlQueryRaw<int>(
        "SELECT COALESCE(MAX(\"Versao\"),0)::integer AS \"Value\" FROM \"__ErpSchemaVersoes\"").SingleAsync();
    if (versao < 2) throw new Exception("A migração de entradas não foi instalada.");
    if (versao < 3) throw new Exception("A migração da data de cadastro do produto (versão 3) não foi instalada.");
    // A funcionalidade de fotos foi removida. A migração 8 também elimina a tabela antiga.
    if (versao < 8) throw new Exception("A migração de exclusão de fotos (versão 8) não foi aplicada.");
    var tabelaFotosAindaExiste = await contexto.Database.SqlQueryRaw<bool>("""
        SELECT EXISTS (
            SELECT 1 FROM information_schema.tables
            WHERE table_schema = current_schema() AND table_name = 'ProdutoFotos'
        ) AS "Value"
        """).SingleAsync();
    if (tabelaFotosAindaExiste) throw new Exception("A tabela ProdutoFotos deveria ter sido excluída.");
    await contexto.Fornecedores.AsNoTracking().AnyAsync();
}

// Simula 20 reservas simultâneas no mesmo banco, como se várias Retaguardas cadastrassem entradas.
var numeros = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
{
    await using var db = new ErpDbContext(opcoes);
    return await db.Database.SqlQueryRaw<int>(
        "SELECT nextval('\"Entradas_Numero_seq\"')::integer AS \"Value\"").SingleAsync();
}));
if (numeros.Distinct().Count() != numeros.Length)
    throw new Exception("ERRO: a sequência atribuiu números duplicados.");

await using (var db = new ErpDbContext(opcoes))
{
    var unico = await db.Database.SqlQueryRaw<bool>("""
        SELECT EXISTS (
            SELECT 1 FROM pg_index i
            JOIN pg_class c ON c.oid = i.indexrelid
            WHERE c.relname = 'IX_Entradas_Numero' AND i.indisunique
        ) AS "Value"
        """).SingleAsync();
    if (!unico) throw new Exception("Falta índice único da numeração das entradas.");

    // Reexecutar as migrações deve ser idempotente e não reiniciar a sequência.
    await Inicializacao.PrepararAsync(db, null, NullLogger.Instance);
    var depois = await db.Database.SqlQueryRaw<int>(
        "SELECT nextval('\"Entradas_Numero_seq\"')::integer AS \"Value\"").SingleAsync();
    if (depois <= numeros.Max()) throw new Exception("A migração reiniciou a sequência indevidamente.");
}
Console.WriteLine("[OK] Migrações idempotentes, números distintos, índice único e sequência persistente.");
return 0;
