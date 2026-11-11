using Microsoft.EntityFrameworkCore;

namespace ProjetoJP.Api.Dados;

public class ErpDbContext(DbContextOptions<ErpDbContext> options) : DbContext(options)
{
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<UsuarioPublicado> UsuariosPublicados => Set<UsuarioPublicado>();
    public DbSet<Categoria> Categorias => Set<Categoria>();
    public DbSet<Produto> Produtos => Set<Produto>();
    public DbSet<ProdutoPublicado> ProdutosPublicados => Set<ProdutoPublicado>();
    public DbSet<ControlePublicacao> ControlesPublicacao => Set<ControlePublicacao>();
    public DbSet<Promocao> Promocoes => Set<Promocao>();
    public DbSet<MovimentoEstoque> MovimentosEstoque => Set<MovimentoEstoque>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<FormaPagamento> FormasPagamento => Set<FormaPagamento>();
    public DbSet<Terminal> Terminais => Set<Terminal>();
    public DbSet<CaixaSessao> CaixaSessoes => Set<CaixaSessao>();
    public DbSet<CaixaMovimento> CaixaMovimentos => Set<CaixaMovimento>();
    public DbSet<Venda> Vendas => Set<Venda>();
    public DbSet<VendaItem> VendaItens => Set<VendaItem>();
    public DbSet<VendaPagamento> VendaPagamentos => Set<VendaPagamento>();
    public DbSet<Entrada> Entradas => Set<Entrada>();
    public DbSet<EntradaItem> EntradaItens => Set<EntradaItem>();
    public DbSet<DispositivoConfiavel> Dispositivos => Set<DispositivoConfiavel>();
    public DbSet<AuditoriaAdministrativa> Auditorias => Set<AuditoriaAdministrativa>();
    public DbSet<SessaoAcesso> SessoesAcesso => Set<SessaoAcesso>();
    public DbSet<Fornecedor> Fornecedores => Set<Fornecedor>();
    public DbSet<Orcamento> Orcamentos => Set<Orcamento>();
    public DbSet<OrcamentoItem> OrcamentoItens => Set<OrcamentoItem>();
    public DbSet<Saida> Saidas => Set<Saida>();
    public DbSet<SaidaItem> SaidaItens => Set<SaidaItem>();
    public DbSet<LancamentoFiado> LancamentosFiado => Set<LancamentoFiado>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Dinheiro e quantidades: 3 casas cobrem produtos vendidos por peso (kg).
        builder.Properties<decimal>().HavePrecision(18, 3);
        builder.Properties<decimal?>().HavePrecision(18, 3);
    }

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Usuario>(e =>
        {
            e.HasIndex(x => x.Login).IsUnique();
            e.Property(x => x.Login).HasMaxLength(60);
            e.Property(x => x.Nome).HasMaxLength(120);
            e.Property(x => x.Permissoes).HasMaxLength(1000);
        });
        mb.Entity<UsuarioPublicado>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Usuario).WithOne().HasForeignKey<UsuarioPublicado>(x => x.Id).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Nome).HasMaxLength(120);
            e.Property(x => x.Login).HasMaxLength(60);
            e.Property(x => x.Permissoes).HasMaxLength(1000);
            e.HasIndex(x => x.PublicadoEm);
        });
        mb.Entity<Orcamento>(e =>
        {
            e.Property(x => x.Revisao).IsConcurrencyToken();
            e.HasIndex(x => x.Data);
            e.HasIndex(x => new { x.TerminalId, x.Numero });
            e.Property(x => x.ClienteNome).HasMaxLength(150);
            e.Property(x => x.Observacao).HasMaxLength(300);
            e.HasMany(x => x.Itens).WithOne(x => x.Orcamento).HasForeignKey(x => x.OrcamentoId).OnDelete(DeleteBehavior.Cascade);
        });
        mb.Entity<Saida>(e =>
        {
            e.HasIndex(x => x.Numero).IsUnique();
            e.HasIndex(x => x.Data);
            e.Property(x => x.Motivo).HasMaxLength(40);
            e.Property(x => x.Destino).HasMaxLength(120);
            e.Property(x => x.Documento).HasMaxLength(60);
            e.Property(x => x.Observacao).HasMaxLength(200);
            e.HasMany(x => x.Itens).WithOne(x => x.Saida).HasForeignKey(x => x.SaidaId).OnDelete(DeleteBehavior.Cascade);
        });
        mb.Entity<SaidaItem>(e =>
            e.HasOne(x => x.Produto).WithMany().HasForeignKey(x => x.ProdutoId).OnDelete(DeleteBehavior.Restrict));
        mb.Entity<OrcamentoItem>(e =>
        {
            e.Property(x => x.Codigo).HasMaxLength(30);
            e.Property(x => x.Descricao).HasMaxLength(150);
            e.Property(x => x.Unidade).HasMaxLength(6);
        });

        mb.Entity<Categoria>(e => e.Property(x => x.Nome).HasMaxLength(80));

        mb.Entity<Produto>(e =>
        {
            e.HasIndex(x => x.Codigo).IsUnique();
            e.HasIndex(x => x.CodigoBarras);
            e.HasIndex(x => x.AtualizadoEm);
            e.Property(x => x.Codigo).HasMaxLength(30);
            e.Property(x => x.CodigoBarras).HasMaxLength(30);
            e.Property(x => x.Descricao).HasMaxLength(150);
            e.Property(x => x.Unidade).HasMaxLength(6);
            e.Property(x => x.Versao).IsRowVersion();
            e.HasOne(x => x.Categoria).WithMany().HasForeignKey(x => x.CategoriaId).OnDelete(DeleteBehavior.SetNull);
        });

        mb.Entity<ProdutoPublicado>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Produto).WithOne().HasForeignKey<ProdutoPublicado>(x => x.Id).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Codigo).HasMaxLength(30);
            e.Property(x => x.CodigoBarras).HasMaxLength(30);
            e.Property(x => x.Descricao).HasMaxLength(150);
            e.Property(x => x.Unidade).HasMaxLength(6);
            e.HasIndex(x => x.PublicadoEm);
        });
        mb.Entity<ControlePublicacao>(e => e.HasKey(x => x.Id));
        mb.Entity<Fornecedor>(e =>
        {
            e.Property(x => x.Nome).HasMaxLength(150);
            e.Property(x => x.CpfCnpj).HasMaxLength(14);
            e.Property(x => x.Contato).HasMaxLength(100);
            e.Property(x => x.Telefone).HasMaxLength(11);
            e.Property(x => x.Email).HasMaxLength(120);
            e.Property(x => x.Cidade).HasMaxLength(80);
            e.Property(x => x.Uf).HasMaxLength(2);
            e.Property(x => x.Observacao).HasMaxLength(500);
            e.HasIndex(x => x.CpfCnpj);
        });
        mb.Entity<Promocao>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Produto).WithMany().HasForeignKey(x => x.ProdutoId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Nome).HasMaxLength(100);
            e.HasIndex(x => new { x.ProdutoId, x.Inicio, x.Fim });
            e.HasIndex(x => x.AtualizadoEm);
        });

        mb.Entity<MovimentoEstoque>(e =>
        {
            e.HasIndex(x => new { x.ProdutoId, x.Data });
            e.HasOne(x => x.Produto).WithMany().HasForeignKey(x => x.ProdutoId);
        });

        mb.Entity<Cliente>(e =>
        {
            e.Property(x => x.Nome).HasMaxLength(150);
            e.HasIndex(x => x.CpfCnpj);
            e.HasIndex(x => x.AtualizadoEm);
        });

        mb.Entity<Terminal>(e => e.HasIndex(x => x.Chave).IsUnique());

        mb.Entity<CaixaSessao>(e => e.HasIndex(x => new { x.TerminalId, x.Abertura }));

        mb.Entity<Venda>(e =>
        {
            e.HasIndex(x => x.Data);
            e.HasIndex(x => new { x.TerminalId, x.Numero });
            e.HasMany(x => x.Itens).WithOne(x => x.Venda).HasForeignKey(x => x.VendaId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Pagamentos).WithOne(x => x.Venda).HasForeignKey(x => x.VendaId).OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<VendaItem>(e => e.Property(x => x.Descricao).HasMaxLength(150));

        mb.Entity<LancamentoFiado>(e =>
        {
            e.HasIndex(x => x.ClienteId);
            e.HasIndex(x => x.VendaId);
            e.Property(x => x.Observacao).HasMaxLength(200);
            e.HasOne(x => x.Cliente).WithMany().HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<Entrada>(e =>
        {
            e.HasIndex(x => x.Numero).IsUnique();
            e.HasIndex(x => x.Data);
            e.Property(x => x.Fornecedor).HasMaxLength(120);
            e.Property(x => x.Documento).HasMaxLength(60);
            e.Property(x => x.Observacao).HasMaxLength(200);
            e.HasMany(x => x.Itens).WithOne(x => x.Entrada).HasForeignKey(x => x.EntradaId).OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<DispositivoConfiavel>(e =>
        {
            e.HasIndex(x => x.UsuarioId);
            e.Property(x => x.Nome).HasMaxLength(120);
            e.Property(x => x.SenhaCarimbo).HasMaxLength(64);
        });

        mb.Entity<AuditoriaAdministrativa>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Acao).HasMaxLength(50);
            e.Property(x => x.AlvoTipo).HasMaxLength(50);
            e.Property(x => x.Resumo).HasMaxLength(250);
            e.HasIndex(x => x.Data);
            e.HasIndex(x => x.UsuarioId);
        });
        mb.Entity<SessaoAcesso>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UsuarioId, x.ExpiraEm });
            e.HasIndex(x => x.DispositivoId);
        });

        mb.Entity<EntradaItem>(e =>
            e.HasOne(x => x.Produto).WithMany().HasForeignKey(x => x.ProdutoId).OnDelete(DeleteBehavior.Restrict));
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var agora = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<Entidade>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
                entry.Entity.AtualizadoEm = agora;
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
