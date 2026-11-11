using System.Collections.Concurrent;
using ProjetoJP.Api.Dados;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ProjetoJP.Api.Servicos;

/// <summary>Cliente (empresa) cadastrado no ERP Master. Fica no banco erp_master.</summary>
public class ClienteMaster
{
    public Guid Id { get; set; }
    public string Codigo { get; set; } = "";
    public string Nome { get; set; } = "";
    /// <summary>Nome do banco PostgreSQL do cliente (erp_c_codigo).</summary>
    public string Banco { get; set; } = "";
    public bool Ativo { get; set; } = true;
    public DateTime CriadoEm { get; set; }
    public string? Observacao { get; set; }

    // Dados da empresa (sem máscara: CPF/CNPJ e telefone só com os dígitos)
    public string? Documento { get; set; }
    public string? Responsavel { get; set; }
    public string? Telefone { get; set; }
    public string? Email { get; set; }
    public string? Cidade { get; set; }
    public string? Uf { get; set; }

    /// <summary>Quantos caixas ativos a empresa pode ter (definido no ERP Master). null = sem limite.</summary>
    public int? LimiteCaixas { get; set; }
}

/// <summary>Credencial de suporte individual de cada empresa, sem direitos comerciais ou de administrador.</summary>
public sealed class SuporteTecnicoCliente
{
    public Guid Id { get; set; }
    public Guid ClienteId { get; set; }
    public string Login { get; set; } = "suporte";
    public string SenhaHash { get; set; } = "";
    public bool Ativo { get; set; } = true;
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;
}

public enum SituacaoPedido { Pendente = 0, Aprovado = 1, Recusado = 2 }

/// <summary>Pedido de cadastro feito pelo próprio cliente (Retaguarda). Aparece no ERP Master para aprovar.</summary>
public class PedidoCadastro
{
    public Guid Id { get; set; }
    public DateTime CriadoEm { get; set; }
    public string Empresa { get; set; } = "";
    public string? Documento { get; set; }
    public string Responsavel { get; set; } = "";
    public string Telefone { get; set; } = "";
    public string? Email { get; set; }
    public string? Cidade { get; set; }
    public string? Uf { get; set; }
    public string CodigoDesejado { get; set; } = "";
    public string AdminLogin { get; set; } = "admin";
    public string AdminSenhaHash { get; set; } = "";
    /// <summary>Hash do código secreto que só a Retaguarda de quem pediu conhece (para consultar a situação).</summary>
    public string TokenHash { get; set; } = "";
    public SituacaoPedido Situacao { get; set; }
    public string? Motivo { get; set; }
    public Guid? ClienteId { get; set; }
    public string? CodigoAprovado { get; set; }
    public DateTime? ResolvidoEm { get; set; }
    public string? Ip { get; set; }
}

public class MasterDbContext(DbContextOptions<MasterDbContext> options) : DbContext(options)
{
    public DbSet<ClienteMaster> Clientes => Set<ClienteMaster>();
    public DbSet<PedidoCadastro> Pedidos => Set<PedidoCadastro>();
    public DbSet<SuporteTecnicoCliente> SuportesTecnicos => Set<SuporteTecnicoCliente>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<ClienteMaster>(e =>
        {
            e.HasIndex(x => x.Codigo).IsUnique();
            e.Property(x => x.Codigo).HasMaxLength(30);
            e.Property(x => x.Nome).HasMaxLength(120);
            e.Property(x => x.Banco).HasMaxLength(63);
            e.Property(x => x.Observacao).HasMaxLength(500);
            e.Property(x => x.Documento).HasMaxLength(14);
            e.Property(x => x.Responsavel).HasMaxLength(120);
            e.Property(x => x.Telefone).HasMaxLength(11);
            e.Property(x => x.Email).HasMaxLength(120);
            e.Property(x => x.Cidade).HasMaxLength(80);
            e.Property(x => x.Uf).HasMaxLength(2);
        });
        mb.Entity<SuporteTecnicoCliente>(e =>
        {
            e.HasIndex(x => x.ClienteId).IsUnique();
            e.Property(x => x.Login).HasMaxLength(60);
            e.Property(x => x.SenhaHash).IsRequired();
        });
        mb.Entity<PedidoCadastro>(e =>
        {
            e.HasIndex(x => x.Situacao);
            e.Property(x => x.Empresa).HasMaxLength(120);
            e.Property(x => x.Documento).HasMaxLength(14);
            e.Property(x => x.Responsavel).HasMaxLength(120);
            e.Property(x => x.Telefone).HasMaxLength(11);
            e.Property(x => x.Email).HasMaxLength(120);
            e.Property(x => x.Cidade).HasMaxLength(80);
            e.Property(x => x.Uf).HasMaxLength(2);
            e.Property(x => x.CodigoDesejado).HasMaxLength(30);
            e.Property(x => x.AdminLogin).HasMaxLength(60);
            e.Property(x => x.Motivo).HasMaxLength(300);
        });
    }
}

/// <summary>De qual cliente é o pedido atual: o código e o banco dele. Vazio = cliente padrão (banco erp_nf).</summary>
public sealed record Inquilino(string Codigo, string StringConexao)
{
    public const string ChaveItem = "erp.inquilino";
    public bool Padrao => Codigo.Length == 0;
}

/// <summary>
/// Vários clientes, um banco por cliente. O endereço diz o cliente: https://site/nf/lojaabc/api/...
/// usa o banco do cliente "lojaabc"; sem código (https://site/nf/api/...) é o cliente padrão — a
/// instalação que já existia continua funcionando sem mudar nada.
/// </summary>
public class Inquilinos(IConfiguration config, ILogger<Inquilinos> log)
{
    private readonly ConcurrentDictionary<string, (ClienteMaster? cliente, DateTime lido)> _cache = new();
    private static readonly TimeSpan Validade = TimeSpan.FromSeconds(30);

    /// <summary>String de conexão do banco padrão (erp_nf); serve de modelo para os outros.</summary>
    public string StringPadrao => config.GetConnectionString("Erp")!;

    public string StringDoBanco(string banco) =>
        new NpgsqlConnectionStringBuilder(StringPadrao) { Database = banco }.ConnectionString;

    private string StringMaster =>
        config.GetConnectionString("Master") is { Length: > 0 } m ? m : StringDoBanco("erp_master");

    public MasterDbContext AbrirMaster() =>
        new(new DbContextOptionsBuilder<MasterDbContext>().UseNpgsql(StringMaster).Options);

    public ErpDbContext AbrirCliente(string banco) =>
        new(new DbContextOptionsBuilder<ErpDbContext>().UseNpgsql(StringDoBanco(banco)).Options);

    public Inquilino Padrao => new("", StringPadrao);

    /// <summary>Cliente pelo código (com cache de 30 s para não consultar o master a cada pedido).</summary>
    public async Task<ClienteMaster?> BuscarAsync(string codigo)
    {
        if (_cache.TryGetValue(codigo, out var c) && DateTime.UtcNow - c.lido < Validade) return c.cliente;
        ClienteMaster? cliente;
        try
        {
            await using var m = AbrirMaster();
            cliente = await m.Clientes.AsNoTracking().FirstOrDefaultAsync(x => x.Codigo == codigo);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Não consegui ler o banco master");
            cliente = null;
        }
        _cache[codigo] = (cliente, DateTime.UtcNow);
        return cliente;
    }

    public void Esquecer(string codigo) => _cache.TryRemove(codigo, out _);

    // ---------- Criação dos bancos ----------

    /// <summary>Cria o banco erp_master (se faltar) e a tabela de clientes. Precisa do usuário com CREATEDB.</summary>
    public async Task GarantirMasterAsync()
    {
        await CriarBancoSeFaltarAsync(new NpgsqlConnectionStringBuilder(StringMaster).Database!);
        await using var m = AbrirMaster();
        await m.Database.EnsureCreatedAsync();

        // Atualizações do Master versionadas, idempotentes e transacionais.
        await MigracoesMaster.AplicarAsync(m, log);
    }

    public async Task CriarBancoSeFaltarAsync(string banco)
    {
        if (!BancoValido(banco)) throw new InvalidOperationException("Nome de banco inválido: " + banco);
        var cs = new NpgsqlConnectionStringBuilder(StringPadrao) { Database = "postgres", Pooling = false }.ConnectionString;
        await using var con = new NpgsqlConnection(cs);
        await con.OpenAsync();
        await using (var existe = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @n", con))
        {
            existe.Parameters.AddWithValue("n", banco);
            if (await existe.ExecuteScalarAsync() is not null) return;
        }
        // Nome já validado (só a-z, 0-9 e _): pode ir no comando.
        await using var criar = new NpgsqlCommand($"CREATE DATABASE \"{banco}\" ENCODING 'UTF8' TEMPLATE template0", con);
        await criar.ExecuteNonQueryAsync();
        log.LogInformation("Banco {Banco} criado", banco);
    }

    public static bool BancoValido(string banco) =>
        banco.Length is > 0 and <= 63 && banco.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_');

    public static string BancoDoCodigo(string codigo) => "erp_c_" + codigo.Replace('-', '_');

    /// <summary>
    /// Cópia de segurança do banco antes de excluir (pg_dump), em /opt/erp-nf/excluidos.
    /// Devolve o caminho do arquivo. Se a cópia falhar, lança erro — e nada é apagado.
    /// </summary>
    public async Task<string> CopiarAntesDeExcluirAsync(string banco)
    {
        if (!BancoValido(banco)) throw new InvalidOperationException("Nome de banco inválido.");
        var pasta = config["Master:PastaExcluidos"] is { Length: > 0 } p ? p : "/opt/erp-nf/excluidos";
        Directory.CreateDirectory(pasta);
        var arquivo = Path.Combine(pasta, $"{banco}-{DateTime.Now:yyyy-MM-dd-HHmmss}.dump");

        var cs = new NpgsqlConnectionStringBuilder(StringDoBanco(banco));
        var info = new System.Diagnostics.ProcessStartInfo("pg_dump")
        {
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var a in new[] { "-Fc", "-f", arquivo, banco }) info.ArgumentList.Add(a);
        info.Environment["PGHOST"] = cs.Host ?? "localhost";
        info.Environment["PGPORT"] = cs.Port.ToString();
        info.Environment["PGUSER"] = cs.Username ?? "";
        info.Environment["PGPASSWORD"] = cs.Password ?? "";

        using var proc = System.Diagnostics.Process.Start(info)
                         ?? throw new InvalidOperationException("Não foi possível rodar o pg_dump.");
        var erro = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        if (proc.ExitCode != 0 || !File.Exists(arquivo))
            throw new InvalidOperationException("A cópia de segurança falhou, então nada foi apagado. " + erro.Trim());
        return arquivo;
    }

    /// <summary>Apaga o banco do cliente (derruba as conexões abertas antes).</summary>
    public async Task ExcluirBancoAsync(string banco)
    {
        if (!BancoValido(banco) || !banco.StartsWith("erp_c_"))
            throw new InvalidOperationException("Só bancos de clientes (erp_c_...) podem ser excluídos por aqui.");
        NpgsqlConnection.ClearAllPools();
        var cs = new NpgsqlConnectionStringBuilder(StringPadrao) { Database = "postgres", Pooling = false }.ConnectionString;
        await using var con = new NpgsqlConnection(cs);
        await con.OpenAsync();
        // Nome já validado (só a-z, 0-9 e _). WITH (FORCE): PostgreSQL 13 ou mais novo.
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{banco}\" WITH (FORCE)", con);
        await cmd.ExecuteNonQueryAsync();
        log.LogWarning("Banco {Banco} excluído", banco);
    }

    /// <summary>Tamanho do banco em bytes (0 se não der para medir).</summary>
    public async Task<long> TamanhoAsync(string banco)
    {
        try
        {
            await using var con = new NpgsqlConnection(StringDoBanco(banco));
            await con.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT pg_database_size(current_database())", con);
            return Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>Aplica as atualizações de estrutura no banco de todos os clientes ativos (ao iniciar a API).</summary>
    public async Task AtualizarTodosAsync()
    {
        List<ClienteMaster> clientes;
        try
        {
            await using var m = AbrirMaster();
            clientes = await m.Clientes.AsNoTracking().Where(x => x.Ativo).ToListAsync();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Banco master indisponível: clientes extras não foram atualizados");
            return;
        }
        foreach (var c in clientes)
        {
            try
            {
                await using var db = AbrirCliente(c.Banco);
                await Inicializacao.PrepararAsync(db, null, log);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Falha ao atualizar o banco do cliente {Codigo}", c.Codigo);
            }
        }
    }
}
