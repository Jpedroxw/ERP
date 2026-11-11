using System.Data;
using System.Data.Common;
using System.Data.Odbc;
using System.Text;
using ExcelDataReader;
using FirebirdSql.Data.FirebirdClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;

namespace ProjetoJP.Gestao.Servicos;

/// <summary>Tipos de origem aceitos na importação.</summary>
public enum TipoOrigem
{
    Planilha,     // .csv, .txt, .xlsx, .xls
    Firebird,     // .FDB / .GDB (precisa do Firebird rodando no computador ou servidor)
    Sqlite,       // .db, .sqlite
    PostgreSql,
    MySql,        // MySQL e MariaDB
    SqlServer,    // pelo driver ODBC "SQL Server", que já vem no Windows
    Odbc          // qualquer outro banco com driver ODBC instalado (Access, Oracle...)
}

/// <summary>
/// Onde estão os dados a importar e como ler. Só LÊ: nunca grava nada na origem.
/// </summary>
public sealed class OrigemDados
{
    public TipoOrigem Tipo { get; set; }
    public string Arquivo { get; set; } = "";
    public string Servidor { get; set; } = "localhost";
    public int Porta { get; set; }
    public string Banco { get; set; } = "";
    public string Usuario { get; set; } = "";
    public string Senha { get; set; } = "";
    /// <summary>Firebird: conjunto de caracteres do banco (WIN1252 na maioria dos sistemas antigos).</summary>
    public string Charset { get; set; } = "WIN1252";
    /// <summary>ODBC: string de conexão completa.</summary>
    public string StringConexao { get; set; } = "";

    public const int LimitePrevia = 100;

    static OrigemDados()
    {
        // Codificações do Windows (1252 etc.) para CSV antigo, Excel .xls e Firebird.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static int PortaPadrao(TipoOrigem t) => t switch
    {
        TipoOrigem.Firebird => 3050,
        TipoOrigem.PostgreSql => 5432,
        TipoOrigem.MySql => 3306,
        TipoOrigem.SqlServer => 1433,
        _ => 0
    };

    public bool EhArquivo => Tipo is TipoOrigem.Planilha or TipoOrigem.Sqlite;

    // ---------- Conexão ----------

    private DbConnection CriarConexao() => Tipo switch
    {
        TipoOrigem.Firebird => new FbConnection(new FbConnectionStringBuilder
        {
            DataSource = string.IsNullOrWhiteSpace(Servidor) ? "localhost" : Servidor.Trim(),
            Port = Porta > 0 ? Porta : 3050,
            Database = Banco.Trim(),
            UserID = string.IsNullOrWhiteSpace(Usuario) ? "SYSDBA" : Usuario.Trim(),
            Password = Senha,
            Charset = string.IsNullOrWhiteSpace(Charset) ? "NONE" : Charset,
            Dialect = 3,
            Pooling = false,
            ConnectionTimeout = 15
        }.ConnectionString),

        TipoOrigem.Sqlite => new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Arquivo,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ConnectionString),

        TipoOrigem.PostgreSql => new NpgsqlConnection(new NpgsqlConnectionStringBuilder
        {
            Host = Servidor.Trim(),
            Port = Porta > 0 ? Porta : 5432,
            Database = Banco.Trim(),
            Username = Usuario.Trim(),
            Password = Senha,
            Timeout = 15,
            Pooling = false
        }.ConnectionString),

        TipoOrigem.MySql => new MySqlConnection(new MySqlConnectionStringBuilder
        {
            Server = Servidor.Trim(),
            Port = (uint)(Porta > 0 ? Porta : 3306),
            Database = Banco.Trim(),
            UserID = Usuario.Trim(),
            Password = Senha,
            ConnectionTimeout = 15,
            Pooling = false,
            AllowZeroDateTime = true,
            ConvertZeroDateTime = true
        }.ConnectionString),

        TipoOrigem.SqlServer => new OdbcConnection(StringSqlServer()),

        TipoOrigem.Odbc => new OdbcConnection(StringConexao),

        _ => throw new InvalidOperationException("Planilha não usa conexão.")
    };

    private string StringSqlServer()
    {
        var servidor = string.IsNullOrWhiteSpace(Servidor) ? "localhost" : Servidor.Trim();
        if (Porta > 0 && Porta != 1433 && !servidor.Contains('\\')) servidor += "," + Porta;
        var sb = new StringBuilder($"Driver={{SQL Server}};Server={servidor};Database={Banco.Trim()};");
        // Sem usuário = login do Windows.
        sb.Append(string.IsNullOrWhiteSpace(Usuario) ? "Trusted_Connection=Yes;" : $"Uid={Usuario.Trim()};Pwd={Senha};");
        return sb.ToString();
    }

    /// <summary>Abre e fecha a conexão (ou o arquivo) para ver se está tudo certo.</summary>
    public async Task TestarAsync()
    {
        if (Tipo == TipoOrigem.Planilha)
        {
            if (!File.Exists(Arquivo)) throw new FileNotFoundException("Arquivo não encontrado: " + Arquivo);
            return;
        }
        if (Tipo == TipoOrigem.Sqlite && !File.Exists(Arquivo))
            throw new FileNotFoundException("Arquivo não encontrado: " + Arquivo);

        await using var c = CriarConexao();
        await c.OpenAsync();
    }

    // ---------- Tabelas ----------

    /// <summary>Tabelas (e visões) da origem. Na planilha: as abas do Excel, ou o próprio arquivo CSV.</summary>
    public async Task<List<string>> ListarTabelasAsync()
    {
        switch (Tipo)
        {
            case TipoOrigem.Planilha:
                return EhExcel(Arquivo) ? AbasExcel() : [Path.GetFileName(Arquivo)];

            case TipoOrigem.Odbc:
            case TipoOrigem.SqlServer:
            {
                await using var c = CriarConexao();
                await c.OpenAsync();
                var lista = new List<string>();
                foreach (DataRow r in c.GetSchema("Tables").Rows)
                {
                    var tipo = r["TABLE_TYPE"]?.ToString() ?? "";
                    if (tipo is not ("TABLE" or "VIEW")) continue;
                    var esquema = r.Table.Columns.Contains("TABLE_SCHEM") ? r["TABLE_SCHEM"]?.ToString() : null;
                    var nome = r["TABLE_NAME"]?.ToString() ?? "";
                    if (Tipo == TipoOrigem.SqlServer && esquema is "sys" or "INFORMATION_SCHEMA") continue;
                    lista.Add(string.IsNullOrEmpty(esquema) || esquema == "dbo" ? nome : $"{esquema}.{nome}");
                }
                return lista.OrderBy(x => x).ToList();
            }

            default:
            {
                var sql = Tipo switch
                {
                    TipoOrigem.Firebird =>
                        "SELECT TRIM(RDB$RELATION_NAME) FROM RDB$RELATIONS WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 ORDER BY 1",
                    TipoOrigem.Sqlite =>
                        "SELECT name FROM sqlite_master WHERE type IN ('table','view') AND name NOT LIKE 'sqlite_%' ORDER BY name",
                    TipoOrigem.PostgreSql =>
                        "SELECT CASE WHEN table_schema = 'public' THEN table_name ELSE table_schema || '.' || table_name END " +
                        "FROM information_schema.tables WHERE table_schema NOT IN ('pg_catalog','information_schema') ORDER BY 1",
                    TipoOrigem.MySql =>
                        "SELECT table_name FROM information_schema.tables WHERE table_schema = DATABASE() ORDER BY 1",
                    _ => throw new InvalidOperationException()
                };
                await using var c = CriarConexao();
                await c.OpenAsync();
                await using var cmd = c.CreateCommand();
                cmd.CommandText = sql;
                var lista = new List<string>();
                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync()) lista.Add(Convert.ToString(rd.GetValue(0))?.Trim() ?? "");
                return lista;
            }
        }
    }

    /// <summary>
    /// Colunas de todas as tabelas, numa consulta só quando o banco permite.
    /// Serve para achar sozinho a tabela de produtos/clientes pelas colunas que ela tem.
    /// </summary>
    public async Task<Dictionary<string, List<string>>> ListarColunasAsync(IReadOnlyList<string> tabelas)
    {
        var resultado = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        void Somar(string tabela, string coluna)
        {
            tabela = tabela.Trim();
            if (!resultado.TryGetValue(tabela, out var l)) resultado[tabela] = l = new List<string>();
            l.Add(coluna.Trim());
        }

        switch (Tipo)
        {
            case TipoOrigem.Planilha:
                if (EhExcel(Arquivo))
                {
                    var ds = await Task.Run(() =>
                    {
                        using var fs = AbrirCompartilhado(Arquivo);
                        using var leitor = ExcelReaderFactory.CreateReader(fs);
                        return leitor.AsDataSet(new ExcelDataSetConfiguration
                        {
                            ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = true }
                        });
                    });
                    foreach (DataTable t in ds.Tables)
                        foreach (DataColumn col in t.Columns) Somar(t.TableName, col.ColumnName);
                }
                else
                {
                    var t = await Task.Run(() => LerCsv(Arquivo, 1));
                    foreach (DataColumn col in t.Columns) Somar(Path.GetFileName(Arquivo), col.ColumnName);
                }
                return resultado;

            case TipoOrigem.SqlServer:
            case TipoOrigem.Odbc:
            {
                await using var c = CriarConexao();
                await c.OpenAsync();
                foreach (DataRow r in c.GetSchema("Columns").Rows)
                {
                    var esquema = r.Table.Columns.Contains("TABLE_SCHEM") ? r["TABLE_SCHEM"]?.ToString() : null;
                    var nome = r["TABLE_NAME"]?.ToString() ?? "";
                    var tabela = string.IsNullOrEmpty(esquema) || esquema == "dbo" ? nome : $"{esquema}.{nome}";
                    Somar(tabela, r["COLUMN_NAME"]?.ToString() ?? "");
                }
                return resultado;
            }

            case TipoOrigem.Sqlite:
            {
                await using var c = CriarConexao();
                await c.OpenAsync();
                foreach (var tabela in tabelas)
                {
                    await using var cmd = c.CreateCommand();
                    cmd.CommandText = $"PRAGMA table_info({Citar(tabela)})";
                    await using var rd = await cmd.ExecuteReaderAsync();
                    while (await rd.ReadAsync()) Somar(tabela, Convert.ToString(rd.GetValue(1)) ?? "");
                }
                return resultado;
            }

            default:
            {
                var sql = Tipo switch
                {
                    TipoOrigem.Firebird =>
                        "SELECT RF.RDB$RELATION_NAME, RF.RDB$FIELD_NAME FROM RDB$RELATION_FIELDS RF " +
                        "JOIN RDB$RELATIONS R ON R.RDB$RELATION_NAME = RF.RDB$RELATION_NAME " +
                        "WHERE COALESCE(R.RDB$SYSTEM_FLAG, 0) = 0 ORDER BY RF.RDB$RELATION_NAME, RF.RDB$FIELD_POSITION",
                    TipoOrigem.PostgreSql =>
                        "SELECT CASE WHEN table_schema = 'public' THEN table_name ELSE table_schema || '.' || table_name END, column_name " +
                        "FROM information_schema.columns WHERE table_schema NOT IN ('pg_catalog','information_schema') " +
                        "ORDER BY 1, ordinal_position",
                    TipoOrigem.MySql =>
                        "SELECT table_name, column_name FROM information_schema.columns WHERE table_schema = DATABASE() " +
                        "ORDER BY table_name, ordinal_position",
                    _ => throw new InvalidOperationException()
                };
                await using var c = CriarConexao();
                await c.OpenAsync();
                await using var cmd = c.CreateCommand();
                cmd.CommandText = sql;
                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                    Somar(Convert.ToString(rd.GetValue(0)) ?? "", Convert.ToString(rd.GetValue(1)) ?? "");
                return resultado;
            }
        }
    }

    /// <summary>Nome da tabela entre aspas do jeito de cada banco (aceita nomes com espaço ou minúsculas).</summary>
    private string Citar(string tabela)
    {
        string Partes(char abre, char fecha) =>
            string.Join(".", tabela.Split('.').Select(p => $"{abre}{p.Replace(fecha.ToString(), fecha.ToString() + fecha)}{fecha}"));
        return Tipo switch
        {
            TipoOrigem.Firebird or TipoOrigem.Sqlite or TipoOrigem.PostgreSql => Partes('"', '"'),
            TipoOrigem.MySql => Partes('`', '`'),
            TipoOrigem.SqlServer => Partes('[', ']'),
            _ => tabela.Contains(' ') ? $"[{tabela}]" : tabela
        };
    }

    // ---------- Leitura ----------

    /// <summary>
    /// Lê os dados de uma tabela (ou de uma consulta SELECT). Com limite, lê só as primeiras linhas (prévia).
    /// Todas as colunas voltam como object; a conversão é feita depois, campo a campo.
    /// </summary>
    public async Task<DataTable> LerAsync(string? tabela, string? consultaSql, int? limite, CancellationToken ct = default)
    {
        if (Tipo == TipoOrigem.Planilha)
            return await Task.Run(() => LerPlanilha(tabela, limite), ct);

        string sql;
        if (!string.IsNullOrWhiteSpace(consultaSql))
        {
            sql = consultaSql.Trim().TrimEnd(';');
            var inicio = sql.TrimStart('(').Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToUpperInvariant();
            if (inicio is not ("SELECT" or "WITH"))
                throw new InvalidOperationException("A consulta precisa começar com SELECT (aqui só se lê a origem, nada é alterado).");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(tabela)) throw new InvalidOperationException("Escolha uma tabela.");
            var t = Citar(tabela);
            sql = (Tipo, limite) switch
            {
                (TipoOrigem.Firebird, not null) => $"SELECT FIRST {limite} * FROM {t}",
                (TipoOrigem.SqlServer, not null) => $"SELECT TOP {limite} * FROM {t}",
                (TipoOrigem.Sqlite or TipoOrigem.PostgreSql or TipoOrigem.MySql, not null) => $"SELECT * FROM {t} LIMIT {limite}",
                _ => $"SELECT * FROM {t}"
            };
        }

        await using var c = CriarConexao();
        await c.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 300;
        await using var rd = await cmd.ExecuteReaderAsync(ct);

        var dt = new DataTable();
        var nomes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < rd.FieldCount; i++)
        {
            var nome = rd.GetName(i)?.Trim();
            if (string.IsNullOrEmpty(nome)) nome = $"COLUNA{i + 1}";
            var unico = nome;
            for (var n = 2; !nomes.Add(unico); n++) unico = $"{nome}_{n}";
            dt.Columns.Add(unico, typeof(object));
        }

        var valores = new object[rd.FieldCount];
        while (await rd.ReadAsync(ct))
        {
            for (var i = 0; i < rd.FieldCount; i++)
            {
                try { valores[i] = rd.GetValue(i); }
                catch (Exception) { valores[i] = DBNull.Value; } // data inválida, tipo estranho: fica vazio
            }
            dt.Rows.Add(valores);
            if (limite is not null && dt.Rows.Count >= limite)
            {
                try { cmd.Cancel(); } catch { /* alguns drivers não cancelam: tudo bem */ }
                break;
            }
        }
        return dt;
    }

    // ---------- Planilhas ----------

    private static bool EhExcel(string arquivo) =>
        Path.GetExtension(arquivo).ToLowerInvariant() is ".xlsx" or ".xls" or ".xlsm";

    private List<string> AbasExcel()
    {
        using var fs = AbrirCompartilhado(Arquivo);
        using var leitor = ExcelReaderFactory.CreateReader(fs);
        var abas = new List<string>();
        do abas.Add(leitor.Name); while (leitor.NextResult());
        return abas;
    }

    private DataTable LerPlanilha(string? aba, int? limite)
    {
        if (EhExcel(Arquivo))
        {
            using var fs = AbrirCompartilhado(Arquivo);
            using var leitor = ExcelReaderFactory.CreateReader(fs);
            var ds = leitor.AsDataSet(new ExcelDataSetConfiguration
            {
                ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = true }
            });
            var tabela = (aba is not null && ds.Tables.Contains(aba) ? ds.Tables[aba] : ds.Tables.Count > 0 ? ds.Tables[0] : null)
                         ?? throw new InvalidOperationException("A planilha está vazia.");
            return Copiar(tabela, limite);
        }
        return LerCsv(Arquivo, limite);
    }

    /// <summary>Copia para colunas object e corta no limite (a prévia mostra só as primeiras linhas).</summary>
    private static DataTable Copiar(DataTable origem, int? limite)
    {
        var dt = new DataTable();
        var nomes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < origem.Columns.Count; i++)
        {
            var nome = origem.Columns[i].ColumnName.Trim();
            if (nome.Length == 0 || nome.StartsWith("Column", StringComparison.Ordinal) && int.TryParse(nome[6..], out _))
                nome = $"COLUNA{i + 1}";
            var unico = nome;
            for (var n = 2; !nomes.Add(unico); n++) unico = $"{nome}_{n}";
            dt.Columns.Add(unico, typeof(object));
        }
        foreach (DataRow r in origem.Rows)
        {
            if (r.ItemArray.All(v => v is DBNull || v is string s && s.Trim().Length == 0)) continue; // linha em branco
            dt.Rows.Add(r.ItemArray);
            if (limite is not null && dt.Rows.Count >= limite) break;
        }
        return dt;
    }

    /// <summary>
    /// CSV/TXT exportado de outro sistema ou do Excel. Descobre sozinho o separador (; , tab ou |)
    /// e a codificação (UTF-8 ou a do Windows). A primeira linha precisa ter os nomes das colunas.
    /// </summary>
    public static DataTable LerCsv(string arquivo, int? limite)
    {
        var bytes = File.ReadAllBytes(arquivo);
        var texto = Decodificar(bytes);
        var linhas = texto.Replace("\r\n", "\n").Replace('\r', '\n');

        var primeira = linhas.Split('\n', 2)[0];
        var separador = new[] { ';', '\t', ',', '|' }
            .OrderByDescending(c => primeira.Count(x => x == c)).First();

        var registros = LerRegistrosCsv(linhas, separador);
        var dt = new DataTable();
        if (registros.Count == 0) return dt;

        var nomes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < registros[0].Count; i++)
        {
            var nome = registros[0][i].Trim();
            if (nome.Length == 0) nome = $"COLUNA{i + 1}";
            var unico = nome;
            for (var n = 2; !nomes.Add(unico); n++) unico = $"{nome}_{n}";
            dt.Columns.Add(unico, typeof(object));
        }

        foreach (var reg in registros.Skip(1))
        {
            if (reg.All(c => c.Trim().Length == 0)) continue;
            var valores = new object[dt.Columns.Count];
            for (var i = 0; i < valores.Length; i++)
                valores[i] = i < reg.Count ? reg[i] : DBNull.Value;
            dt.Rows.Add(valores);
            if (limite is not null && dt.Rows.Count >= limite) break;
        }
        return dt;
    }

    /// <summary>UTF-8 quando o arquivo é UTF-8 válido; senão Windows-1252 (padrão do Excel em português).</summary>
    private static string Decodificar(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
    }

    /// <summary>Separa os campos respeitando aspas ("texto; com separador" e "aspas ""duplas""").</summary>
    private static List<List<string>> LerRegistrosCsv(string texto, char sep)
    {
        var registros = new List<List<string>>();
        var atual = new List<string>();
        var campo = new StringBuilder();
        var entreAspas = false;

        for (var i = 0; i < texto.Length; i++)
        {
            var c = texto[i];
            if (entreAspas)
            {
                if (c == '"')
                {
                    if (i + 1 < texto.Length && texto[i + 1] == '"') { campo.Append('"'); i++; }
                    else entreAspas = false;
                }
                else campo.Append(c);
            }
            else if (c == '"' && campo.Length == 0) entreAspas = true;
            else if (c == sep) { atual.Add(campo.ToString()); campo.Clear(); }
            else if (c == '\n')
            {
                atual.Add(campo.ToString());
                campo.Clear();
                registros.Add(atual);
                atual = [];
            }
            else campo.Append(c);
        }
        if (campo.Length > 0 || atual.Count > 0)
        {
            atual.Add(campo.ToString());
            registros.Add(atual);
        }
        return registros;
    }

    /// <summary>Abre mesmo com o arquivo aberto no Excel.</summary>
    private static FileStream AbrirCompartilhado(string arquivo) =>
        new(arquivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
}
