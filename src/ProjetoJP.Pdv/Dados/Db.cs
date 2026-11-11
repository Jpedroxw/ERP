using System.Globalization;
using System.Security.AccessControl;
using System.Security.Principal;
using Dapper;
using Microsoft.Data.Sqlite;

namespace ProjetoJP.Pdv.Dados;

/// <summary>
/// Banco local do caixa (SQLite). É o que permite vender sem internet:
/// cadastros chegam da VPS, vendas ficam aqui com sincronizado = 0 até subirem.
/// Ids e datas (UTC, ISO 8601) são gravados como texto.
/// </summary>
public static class Db
{
    public static string Pasta { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ErpNaoFiscal");

    public static string Caminho => Path.Combine(Pasta, "pdv.db");

    private static string StringConexao => new SqliteConnectionStringBuilder
    {
        DataSource = Caminho,
        DefaultTimeout = 15 // segundos esperando o outro acesso liberar o arquivo
    }.ToString();

    public static SqliteConnection Abrir()
    {
        var c = new SqliteConnection(StringConexao);
        c.Open();
        return c;
    }

    public static void Inicializar()
    {
        Directory.CreateDirectory(Pasta);
        LiberarPastaParaUsuarios();
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        try
        {
            using var c = Abrir();
            c.Execute("PRAGMA journal_mode=WAL;");
            c.Execute(Esquema);
        // Bancos de versões anteriores: colunas novas.
        var colunasUsuarios = c.Query<string>("SELECT name FROM pragma_table_info('usuarios')").ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!colunasUsuarios.Contains("permissoes")) c.Execute("ALTER TABLE usuarios ADD COLUMN permissoes TEXT");
        // Atualiza SQLite antigo sem perder orçamentos nem vendas pendentes.
        var colunasOrc = c.Query<string>("SELECT name FROM pragma_table_info('orcamentos')")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!colunasOrc.Contains("revisao"))
            c.Execute("ALTER TABLE orcamentos ADD COLUMN revisao INTEGER NOT NULL DEFAULT 1");
        // Venda recusada pelo servidor: guarda o motivo e não trava as outras (tenta de novo de tempos em tempos).
        var colunasVendas = c.Query<string>("SELECT name FROM pragma_table_info('vendas')")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!colunasVendas.Contains("erro_sync")) c.Execute("ALTER TABLE vendas ADD COLUMN erro_sync TEXT");
        if (!colunasVendas.Contains("erro_sync_em")) c.Execute("ALTER TABLE vendas ADD COLUMN erro_sync_em TEXT");
        // Forma "fiado": o caixa precisa saber para exigir o cliente. Caixa antigo: baixa os cadastros de novo
        // (as formas que já existiam não voltariam sozinhas, porque não mudaram no servidor).
        var colunasFormas = c.Query<string>("SELECT name FROM pragma_table_info('formas')")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!colunasFormas.Contains("eh_fiado"))
        {
            c.Execute("ALTER TABLE formas ADD COLUMN eh_fiado INTEGER NOT NULL DEFAULT 0");
            c.Execute("DELETE FROM config WHERE chave = 'ultima_sync'");
        }
        // Atacado: colunas novas em produtos e promoções. Baixa tudo de novo para trazer os preços de atacado
        // que já estavam no servidor (eles não mudaram, então não viriam sozinhos).
        var colunasProdutos = c.Query<string>("SELECT name FROM pragma_table_info('produtos')")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var colunasPromocoes = c.Query<string>("SELECT name FROM pragma_table_info('promocoes')")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!colunasProdutos.Contains("preco_atacado") || !colunasPromocoes.Contains("qtd_minima"))
        {
            if (!colunasProdutos.Contains("preco_atacado"))
                c.Execute("ALTER TABLE produtos ADD COLUMN preco_atacado REAL NOT NULL DEFAULT 0");
            if (!colunasProdutos.Contains("qtd_atacado"))
                c.Execute("ALTER TABLE produtos ADD COLUMN qtd_atacado REAL NOT NULL DEFAULT 0");
            if (!colunasPromocoes.Contains("qtd_minima"))
                c.Execute("ALTER TABLE promocoes ADD COLUMN qtd_minima REAL NOT NULL DEFAULT 0");
            c.Execute("DELETE FROM config WHERE chave = 'ultima_sync'");
        }
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 8)
        {
            throw new InvalidOperationException(
                "O banco local está somente leitura (SQLite erro 8). Feche o caixa e, no PowerShell aberto como administrador, execute:\n\n" +
                "icacls \"" + Pasta + "\" /grant *S-1-5-32-545:(OI)(CI)M /T", ex);
        }
    }

    /// <summary>
    /// O instalador abre o caixa como administrador. Sem esta permissão, o usuário normal
    /// não consegue gravar pdv.db e o SQLite devolve o erro 8.
    /// </summary>
    private static void LiberarPastaParaUsuarios()
    {
        try
        {
            var info = new DirectoryInfo(Pasta);
            var seguranca = info.GetAccessControl();
            seguranca.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                FileSystemRights.Modify,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            info.SetAccessControl(seguranca);
        }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Hora das vendas, caixas e movimentos: a do relógio confiável (servidor/internet), não a do Windows.</summary>
    public static string Agora() => ProjetoJP.Pdv.Servicos.Relogio.UtcAgora.ToString("o", CultureInfo.InvariantCulture);

    public static DateTime ParaData(string iso) =>
        DateTime.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    public static string? LerConfig(string chave)
    {
        using var c = Abrir();
        return c.ExecuteScalar<string?>("SELECT valor FROM config WHERE chave = @chave", new { chave });
    }

    public static void GravarConfig(string chave, string valor)
    {
        using var c = Abrir();
        c.Execute("INSERT INTO config(chave, valor) VALUES(@chave, @valor) ON CONFLICT(chave) DO UPDATE SET valor = excluded.valor",
            new { chave, valor });
    }

    private const string Esquema = """
        CREATE TABLE IF NOT EXISTS config (
            chave TEXT PRIMARY KEY,
            valor TEXT
        );

        CREATE TABLE IF NOT EXISTS usuarios (
            id TEXT PRIMARY KEY,
            nome TEXT NOT NULL,
            login TEXT NOT NULL,
            senha_hash TEXT NOT NULL,
            perfil INTEGER NOT NULL,
            ativo INTEGER NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_usuarios_login ON usuarios(login);

        CREATE TABLE IF NOT EXISTS produtos (
            id TEXT PRIMARY KEY,
            codigo TEXT NOT NULL,
            codigo_barras TEXT,
            descricao TEXT NOT NULL,
            unidade TEXT NOT NULL,
            preco_venda REAL NOT NULL,
            preco_atacado REAL NOT NULL DEFAULT 0,
            qtd_atacado REAL NOT NULL DEFAULT 0,
            estoque REAL NOT NULL,
            ativo INTEGER NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_produtos_codigo ON produtos(codigo);
        CREATE INDEX IF NOT EXISTS ix_produtos_barras ON produtos(codigo_barras);

        CREATE TABLE IF NOT EXISTS promocoes (
            id TEXT PRIMARY KEY,
            produto_id TEXT NOT NULL,
            inicio TEXT NOT NULL,
            fim TEXT NOT NULL,
            preco REAL NOT NULL,
            qtd_minima REAL NOT NULL DEFAULT 0,
            ativo INTEGER NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_promocoes_produto_periodo ON promocoes(produto_id, inicio, fim);

        CREATE TABLE IF NOT EXISTS clientes (
            id TEXT PRIMARY KEY,
            nome TEXT NOT NULL,
            cpf_cnpj TEXT,
            telefone TEXT,
            ativo INTEGER NOT NULL
        );

        CREATE TABLE IF NOT EXISTS formas (
            id TEXT PRIMARY KEY,
            descricao TEXT NOT NULL,
            permite_troco INTEGER NOT NULL,
            eh_fiado INTEGER NOT NULL DEFAULT 0,
            ordem INTEGER NOT NULL,
            ativo INTEGER NOT NULL
        );

        CREATE TABLE IF NOT EXISTS caixas (
            id TEXT PRIMARY KEY,
            usuario_abertura_id TEXT NOT NULL,
            abertura TEXT NOT NULL,
            valor_abertura REAL NOT NULL,
            fechamento TEXT,
            usuario_fechamento_id TEXT,
            valor_fechamento REAL,
            status INTEGER NOT NULL,
            sincronizado INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS caixa_movimentos (
            id TEXT PRIMARY KEY,
            caixa_id TEXT NOT NULL,
            tipo INTEGER NOT NULL,
            valor REAL NOT NULL,
            data TEXT NOT NULL,
            motivo TEXT,
            usuario_id TEXT NOT NULL,
            sincronizado INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS vendas (
            id TEXT PRIMARY KEY,
            numero INTEGER NOT NULL,
            caixa_id TEXT NOT NULL,
            usuario_id TEXT NOT NULL,
            cliente_id TEXT,
            data TEXT NOT NULL,
            subtotal REAL NOT NULL,
            desconto REAL NOT NULL,
            total REAL NOT NULL,
            troco REAL NOT NULL,
            status INTEGER NOT NULL,
            cancelada_em TEXT,
            sincronizado INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX IF NOT EXISTS ix_vendas_caixa ON vendas(caixa_id);
        CREATE INDEX IF NOT EXISTS ix_vendas_sync ON vendas(sincronizado);
        CREATE INDEX IF NOT EXISTS ix_vendas_numero ON vendas(numero);
        CREATE INDEX IF NOT EXISTS ix_vendas_data ON vendas(data);

        CREATE TABLE IF NOT EXISTS venda_itens (
            id TEXT PRIMARY KEY,
            venda_id TEXT NOT NULL,
            produto_id TEXT NOT NULL,
            descricao TEXT NOT NULL,
            quantidade REAL NOT NULL,
            preco_unitario REAL NOT NULL,
            desconto REAL NOT NULL,
            total REAL NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_itens_venda ON venda_itens(venda_id);

        CREATE TABLE IF NOT EXISTS venda_pagamentos (
            id TEXT PRIMARY KEY,
            venda_id TEXT NOT NULL,
            forma_id TEXT NOT NULL,
            valor REAL NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_pag_venda ON venda_pagamentos(venda_id);

        CREATE TABLE IF NOT EXISTS orcamentos (
            id TEXT PRIMARY KEY,
            revisao INTEGER NOT NULL DEFAULT 1,
            numero INTEGER NOT NULL,
            usuario_id TEXT NOT NULL,
            cliente_id TEXT,
            cliente_nome TEXT,
            data TEXT NOT NULL,
            validade TEXT,
            subtotal REAL NOT NULL,
            desconto REAL NOT NULL,
            total REAL NOT NULL,
            observacao TEXT,
            status INTEGER NOT NULL,
            venda_id TEXT,
            sincronizado INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX IF NOT EXISTS ix_orcamentos_data ON orcamentos(data);

        CREATE TABLE IF NOT EXISTS orcamento_itens (
            id TEXT PRIMARY KEY,
            orcamento_id TEXT NOT NULL,
            produto_id TEXT NOT NULL,
            codigo TEXT NOT NULL,
            descricao TEXT NOT NULL,
            unidade TEXT NOT NULL,
            quantidade REAL NOT NULL,
            preco_unitario REAL NOT NULL,
            total REAL NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_orc_itens ON orcamento_itens(orcamento_id);
        """;
}
