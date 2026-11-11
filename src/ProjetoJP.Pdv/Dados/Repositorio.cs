using System.Globalization;
using Dapper;
using ProjetoJP.Shared;

namespace ProjetoJP.Pdv.Dados;

public static class Repositorio
{
    // ---------- Cadastros ----------

    public static int ContarUsuarios()
    {
        using var c = Db.Abrir();
        return c.ExecuteScalar<int>("SELECT COUNT(*) FROM usuarios WHERE ativo = 1");
    }

    /// <summary>Quantos usuários ativos deste caixa podem autorizar (Gerente ou Admin).</summary>
    public static int ContarAutorizadores()
    {
        using var c = Db.Abrir();
        return c.ExecuteScalar<int>("SELECT COUNT(*) FROM usuarios WHERE ativo = 1 AND perfil IN (1, 2)");
    }

    public static UsuarioLocal? BuscarUsuario(string login)
    {
        using var c = Db.Abrir();
        return c.QueryFirstOrDefault<UsuarioLocal>(
            "SELECT * FROM usuarios WHERE login = @login AND ativo = 1", new { login = login.Trim().ToLowerInvariant() });
    }

    public static UsuarioLocal? UsuarioPorId(string id)
    {
        using var c = Db.Abrir();
        return c.QueryFirstOrDefault<UsuarioLocal>("SELECT * FROM usuarios WHERE id = @id", new { id });
    }

    // Preço com promoção, calculado aqui no caixa (vende sem internet). A data vem do relógio confiável
    // (servidor/internet), então mexer no relógio do Windows não liga nem desliga promoção.
    // preco_normal = preço publicado, para mostrar "de ... por ..." na venda e no cupom.
    private const string SqlProduto = """
        SELECT p.id, p.codigo, p.codigo_barras, p.descricao, p.unidade, p.estoque, p.ativo, p.preco_venda AS preco_normal,
            COALESCE((SELECT MIN(pr.preco) FROM promocoes pr
                WHERE pr.produto_id = p.id AND pr.ativo = 1 AND pr.qtd_minima = 0
                AND pr.inicio <= @hoje AND pr.fim >= @hoje), p.preco_venda) AS preco_venda,
            p.preco_atacado, p.qtd_atacado AS qtd_atacado,
            (SELECT pr.preco FROM promocoes pr
                WHERE pr.produto_id = p.id AND pr.ativo = 1 AND pr.qtd_minima > 0
                AND pr.inicio <= @hoje AND pr.fim >= @hoje ORDER BY pr.preco LIMIT 1) AS promo_atacado_preco,
            (SELECT pr.qtd_minima FROM promocoes pr
                WHERE pr.produto_id = p.id AND pr.ativo = 1 AND pr.qtd_minima > 0
                AND pr.inicio <= @hoje AND pr.fim >= @hoje ORDER BY pr.preco LIMIT 1) AS promo_atacado_minimo
        FROM produtos p
        """;

    /// <summary>Código da etiqueta de balança, com ou sem os zeros à esquerda.</summary>
    public static ProdutoLocal? BuscarProdutoBalanca(string codigo)
    {
        var bruto = codigo.Trim();
        var semZero = bruto.TrimStart('0');
        if (semZero.Length == 0) semZero = "0";
        return BuscarProduto(bruto) ?? (semZero == bruto ? null : BuscarProduto(semZero));
    }

    /// <summary>Procura por código interno ou código de barras.</summary>
    public static ProdutoLocal? BuscarProduto(string codigo)
    {
        using var c = Db.Abrir();
        return c.QueryFirstOrDefault<ProdutoLocal>(
            SqlProduto + " WHERE p.ativo = 1 AND (p.codigo = @codigo OR p.codigo_barras = @codigo) LIMIT 1",
            new { codigo = codigo.Trim(), hoje = ProjetoJP.Pdv.Servicos.Relogio.Hoje.ToString("yyyy-MM-dd") });
    }

    /// <summary>Todos os produtos ativos com o preço de hoje (sugestões da busca do caixa, com tolerância a erro de digitação).</summary>
    public static List<ProdutoLocal> ProdutosAtivos()
    {
        using var c = Db.Abrir();
        return c.Query<ProdutoLocal>(SqlProduto + " WHERE p.ativo = 1",
            new { hoje = ProjetoJP.Pdv.Servicos.Relogio.Hoje.ToString("yyyy-MM-dd") }).ToList();
    }

    /// <summary>
    /// Botões rápidos: os produtos escolhidos nas Opções (na ordem) ou, sem escolha, os produtos sem código de barras
    /// mais vendidos neste caixa nos últimos 60 dias (pão, salgados, hortifrúti). Produto inativo não aparece.
    /// </summary>
    public static List<ProdutoLocal> ProdutosRapidos(IReadOnlyList<string> codigos, int maximo)
    {
        if (codigos.Count > 0)
            return codigos.Select(BuscarProduto).OfType<ProdutoLocal>().DistinctBy(p => p.Id).Take(maximo).ToList();
        using var c = Db.Abrir();
        var hoje = ProjetoJP.Pdv.Servicos.Relogio.Hoje;
        return c.Query<ProdutoLocal>(SqlProduto + "\n" + """
             WHERE p.ativo = 1 AND (p.codigo_barras IS NULL OR TRIM(p.codigo_barras) = '')
             ORDER BY (SELECT COUNT(*) FROM venda_itens i JOIN vendas v ON v.id = i.venda_id
                       WHERE i.produto_id = p.id AND v.status = 0 AND v.data >= @desde) DESC, p.descricao
             LIMIT @maximo
             """,
            new { hoje = hoje.ToString("yyyy-MM-dd"), desde = hoje.AddDays(-60).ToString("yyyy-MM-dd"), maximo }).ToList();
    }

    public static List<ProdutoLocal> PesquisarProdutos(string texto)
    {
        using var c = Db.Abrir();
        return c.Query<ProdutoLocal>(
            SqlProduto + " WHERE p.ativo = 1 AND (p.descricao LIKE @t OR p.codigo LIKE @t OR p.codigo_barras LIKE @t) ORDER BY p.descricao LIMIT 200",
            new { t = $"%{texto.Trim()}%", hoje = ProjetoJP.Pdv.Servicos.Relogio.Hoje.ToString("yyyy-MM-dd") }).ToList();
    }

    /// <summary>Por nome, ou por CPF/CNPJ/telefone digitados com ou sem pontuação.</summary>
    public static List<ClienteLocal> PesquisarClientes(string texto)
    {
        var limpo = Formatos.LimparDocumento(texto);
        using var c = Db.Abrir();
        return c.Query<ClienteLocal>(
            """
            SELECT * FROM clientes
            WHERE ativo = 1
              AND (nome LIKE @t OR (@limpo <> '' AND (cpf_cnpj LIKE @l OR telefone LIKE @l)))
            ORDER BY nome LIMIT 200
            """,
            new { t = $"%{texto.Trim()}%", limpo, l = $"%{limpo}%" }).ToList();
    }

    public static ClienteLocal? ClientePorId(string id)
    {
        using var c = Db.Abrir();
        return c.QueryFirstOrDefault<ClienteLocal>("SELECT * FROM clientes WHERE id = @id", new { id });
    }

    public static List<FormaLocal> Formas()
    {
        using var c = Db.Abrir();
        return c.Query<FormaLocal>("SELECT * FROM formas WHERE ativo = 1 ORDER BY ordem, descricao").ToList();
    }

    public static Dictionary<string, string> NomesDasFormas()
    {
        using var c = Db.Abrir();
        return c.Query<FormaLocal>("SELECT * FROM formas").ToDictionary(f => f.Id, f => f.Descricao);
    }

    // ---------- Caixa ----------

    public static CaixaLocal? CaixaAberto()
    {
        using var c = Db.Abrir();
        return c.QueryFirstOrDefault<CaixaLocal>(
            "SELECT * FROM caixas WHERE status = @s ORDER BY abertura DESC LIMIT 1", new { s = (int)StatusCaixa.Aberto });
    }

    public static CaixaLocal AbrirCaixa(string usuarioId, decimal valorAbertura)
    {
        var caixa = new CaixaLocal
        {
            Id = Guid.NewGuid().ToString(),
            UsuarioAberturaId = usuarioId,
            Abertura = Db.Agora(),
            ValorAbertura = valorAbertura,
            Status = (int)StatusCaixa.Aberto
        };
        using var c = Db.Abrir();
        c.Execute(
            """
            INSERT INTO caixas(id, usuario_abertura_id, abertura, valor_abertura, status, sincronizado)
            VALUES(@Id, @UsuarioAberturaId, @Abertura, @ValorAbertura, @Status, 0)
            """, caixa);
        return caixa;
    }

    public static void FecharCaixa(string caixaId, string usuarioId, decimal valorContado)
    {
        using var c = Db.Abrir();
        c.Execute(
            """
            UPDATE caixas SET fechamento = @agora, usuario_fechamento_id = @usuarioId,
                   valor_fechamento = @valorContado, status = @status, sincronizado = 0
            WHERE id = @caixaId
            """,
            new { agora = Db.Agora(), usuarioId, valorContado, status = (int)StatusCaixa.Fechado, caixaId });
    }

    public static CaixaMovimentoLocal RegistrarMovimentoCaixa(string caixaId, TipoMovimentoCaixa tipo, decimal valor, string? motivo, string usuarioId)
    {
        var m = new CaixaMovimentoLocal
        {
            Id = Guid.NewGuid().ToString(),
            CaixaId = caixaId,
            Tipo = (int)tipo,
            Valor = valor,
            Data = Db.Agora(),
            Motivo = motivo,
            UsuarioId = usuarioId
        };
        using var c = Db.Abrir();
        c.Execute(
            """
            INSERT INTO caixa_movimentos(id, caixa_id, tipo, valor, data, motivo, usuario_id, sincronizado)
            VALUES(@Id, @CaixaId, @Tipo, @Valor, @Data, @Motivo, @UsuarioId, 0)
            """, m);
        return m;
    }

    public static ResumoCaixa Resumo(CaixaLocal caixa)
    {
        using var c = Db.Abrir();
        var r = new ResumoCaixa { Abertura = caixa.ValorAbertura };

        r.Suprimentos = c.ExecuteScalar<decimal?>(
            "SELECT SUM(valor) FROM caixa_movimentos WHERE caixa_id = @id AND tipo = @t",
            new { id = caixa.Id, t = (int)TipoMovimentoCaixa.Suprimento }) ?? 0;
        r.Sangrias = c.ExecuteScalar<decimal?>(
            "SELECT SUM(valor) FROM caixa_movimentos WHERE caixa_id = @id AND tipo = @t",
            new { id = caixa.Id, t = (int)TipoMovimentoCaixa.Sangria }) ?? 0;

        r.QuantidadeVendas = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM vendas WHERE caixa_id = @id AND status = @s", new { id = caixa.Id, s = (int)StatusVenda.Concluida });
        r.QuantidadeCanceladas = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM vendas WHERE caixa_id = @id AND status = @s", new { id = caixa.Id, s = (int)StatusVenda.Cancelada });
        r.TotalVendas = c.ExecuteScalar<decimal?>(
            "SELECT SUM(total) FROM vendas WHERE caixa_id = @id AND status = @s", new { id = caixa.Id, s = (int)StatusVenda.Concluida }) ?? 0;

        var linhas = c.Query<LinhaForma>(
            """
            SELECT f.descricao AS descricao, SUM(p.valor) AS valor, f.permite_troco AS permite_troco
            FROM venda_pagamentos p
            JOIN vendas v ON v.id = p.venda_id
            JOIN formas f ON f.id = p.forma_id
            WHERE v.caixa_id = @id AND v.status = @s
            GROUP BY f.id, f.descricao, f.permite_troco
            ORDER BY f.ordem
            """, new { id = caixa.Id, s = (int)StatusVenda.Concluida });

        foreach (var l in linhas)
        {
            var valor = Math.Round(l.Valor, 2);
            r.PorForma.Add(new LinhaResumo { Descricao = l.Descricao, Valor = valor });
            if (l.PermiteTroco) r.VendasDinheiro += valor;
        }
        return r;
    }

    public class LinhaForma
    {
        public string Descricao { get; set; } = "";
        public decimal Valor { get; set; }
        public bool PermiteTroco { get; set; }
    }

    // ---------- Vendas ----------

    public static int ProximoNumeroVenda()
    {
        using var c = Db.Abrir();
        return c.ExecuteScalar<int>("SELECT COALESCE(MAX(numero), 0) + 1 FROM vendas");
    }

    /// <summary>Grava a venda e baixa o estoque local numa única transação.</summary>
    public static void SalvarVenda(VendaCompleta v)
    {
        using var c = Db.Abrir();
        using var tx = c.BeginTransaction();

        // A numeração comercial é reservada dentro da mesma transação que grava a venda.
        // Evita a janela de concorrência entre consultar MAX(numero) e inserir a venda.
        v.Venda.Numero = c.ExecuteScalar<int>(
            "SELECT COALESCE(MAX(numero), 0) + 1 FROM vendas", transaction: tx);

        c.Execute(
            """
            INSERT INTO vendas(id, numero, caixa_id, usuario_id, cliente_id, data, subtotal, desconto, total, troco, status, cancelada_em, sincronizado)
            VALUES(@Id, @Numero, @CaixaId, @UsuarioId, @ClienteId, @Data, @Subtotal, @Desconto, @Total, @Troco, @Status, @CanceladaEm, 0)
            """, v.Venda, tx);

        c.Execute(
            """
            INSERT INTO venda_itens(id, venda_id, produto_id, descricao, quantidade, preco_unitario, desconto, total)
            VALUES(@Id, @VendaId, @ProdutoId, @Descricao, @Quantidade, @PrecoUnitario, @Desconto, @Total)
            """, v.Itens, tx);

        c.Execute(
            """
            INSERT INTO venda_pagamentos(id, venda_id, forma_id, valor)
            VALUES(@Id, @VendaId, @FormaId, @Valor)
            """, v.Pagamentos, tx);

        foreach (var i in v.Itens)
            c.Execute("UPDATE produtos SET estoque = estoque - @q WHERE id = @id", new { q = i.Quantidade, id = i.ProdutoId }, tx);

        tx.Commit();
    }

    public static VendaCompleta? CarregarVenda(string id)
    {
        using var c = Db.Abrir();
        var venda = c.QueryFirstOrDefault<VendaLocal>("SELECT * FROM vendas WHERE id = @id", new { id });
        if (venda is null) return null;
        return new VendaCompleta
        {
            Venda = venda,
            Itens = c.Query<VendaItemLocal>("SELECT * FROM venda_itens WHERE venda_id = @id", new { id }).ToList(),
            Pagamentos = c.Query<VendaPagamentoLocal>("SELECT * FROM venda_pagamentos WHERE venda_id = @id", new { id }).ToList()
        };
    }

    public static List<VendaLocal> VendasDoCaixa(string caixaId)
    {
        using var c = Db.Abrir();
        return c.Query<VendaLocal>("SELECT * FROM vendas WHERE caixa_id = @caixaId ORDER BY numero DESC", new { caixaId }).ToList();
    }

    /// <summary>Cancela e devolve o estoque local. Marca para sincronizar de novo.</summary>
    public static void CancelarVenda(string vendaId)
    {
        using var c = Db.Abrir();
        using var tx = c.BeginTransaction();

        var linhas = c.Execute(
            "UPDATE vendas SET status = @cancelada, cancelada_em = @agora, sincronizado = 0, erro_sync = NULL, erro_sync_em = NULL WHERE id = @vendaId AND status = @concluida",
            new { cancelada = (int)StatusVenda.Cancelada, concluida = (int)StatusVenda.Concluida, agora = Db.Agora(), vendaId }, tx);

        if (linhas > 0)
        {
            var itens = c.Query<VendaItemLocal>("SELECT * FROM venda_itens WHERE venda_id = @vendaId", new { vendaId }, tx);
            foreach (var i in itens)
                c.Execute("UPDATE produtos SET estoque = estoque + @q WHERE id = @id", new { q = i.Quantidade, id = i.ProdutoId }, tx);
        }
        tx.Commit();
    }

    // ---------- Sincronização ----------

    public static List<CaixaLocal> CaixasPendentes()
    {
        using var c = Db.Abrir();
        return c.Query<CaixaLocal>("SELECT * FROM caixas WHERE sincronizado = 0").ToList();
    }

    public static List<CaixaMovimentoLocal> MovimentosPendentes()
    {
        using var c = Db.Abrir();
        return c.Query<CaixaMovimentoLocal>("SELECT * FROM caixa_movimentos WHERE sincronizado = 0").ToList();
    }

    public static List<VendaCompleta> VendasPendentes(int limite)
    {
        using var c = Db.Abrir();
        // Recusada pelo servidor: só tenta de novo depois de 30 min (o problema pode ter sido corrigido lá),
        // sem travar as vendas novas.
        var retentar = DateTime.UtcNow.AddMinutes(-30).ToString("o", CultureInfo.InvariantCulture);
        var ids = c.Query<string>(
            "SELECT id FROM vendas WHERE sincronizado = 0 AND (erro_sync IS NULL OR erro_sync_em < @retentar) " +
            "ORDER BY (erro_sync IS NOT NULL), data LIMIT @limite", new { limite, retentar }).ToList();
        return ids.Select(CarregarVenda).Where(v => v is not null).Select(v => v!).ToList();
    }

    public static int ContarPendencias()
    {
        using var c = Db.Abrir();
        return c.ExecuteScalar<int>(
            """
            SELECT (SELECT COUNT(*) FROM vendas WHERE sincronizado = 0)
                 + (SELECT COUNT(*) FROM caixas WHERE sincronizado = 0)
                 + (SELECT COUNT(*) FROM caixa_movimentos WHERE sincronizado = 0)
                 + (SELECT COUNT(*) FROM orcamentos WHERE sincronizado = 0)
            """);
    }

    /// <summary>
    /// Marca como enviado. Usa o mesmo "estado" que foi lido: se a venda foi cancelada
    /// enquanto subia, ela continua pendente e o cancelamento sobe na próxima rodada.
    /// </summary>
    public static void MarcarSincronizado(IEnumerable<CaixaLocal> caixas, IEnumerable<CaixaMovimentoLocal> movs, IEnumerable<VendaLocal> vendas)
    {
        using var c = Db.Abrir();
        using var tx = c.BeginTransaction();
        foreach (var x in caixas)
            c.Execute("UPDATE caixas SET sincronizado = 1 WHERE id = @Id AND status = @Status", x, tx);
        foreach (var x in movs)
            c.Execute("UPDATE caixa_movimentos SET sincronizado = 1 WHERE id = @Id", x, tx);
        foreach (var x in vendas)
            c.Execute("UPDATE vendas SET sincronizado = 1, erro_sync = NULL, erro_sync_em = NULL WHERE id = @Id AND status = @Status", x, tx);
        tx.Commit();
    }

    /// <summary>Guarda o motivo das vendas que o servidor recusou (continuam pendentes, sem travar as outras).</summary>
    public static void MarcarRecusadas(IEnumerable<(string id, string motivo)> recusadas)
    {
        using var c = Db.Abrir();
        using var tx = c.BeginTransaction();
        var agora = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        foreach (var (id, motivo) in recusadas)
            c.Execute("UPDATE vendas SET erro_sync = @motivo, erro_sync_em = @agora WHERE id = @id AND sincronizado = 0",
                new { id, motivo = motivo.Length > 300 ? motivo[..300] : motivo, agora }, tx);
        tx.Commit();
    }

    /// <summary>Vendas recusadas pelo servidor que ainda não subiram: número e motivo (para avisar o operador).</summary>
    public static List<(int numero, string motivo)> VendasRecusadas()
    {
        using var c = Db.Abrir();
        return c.Query<LinhaRecusa>("SELECT numero, erro_sync FROM vendas WHERE sincronizado = 0 AND erro_sync IS NOT NULL ORDER BY data")
            .Select(x => ((int)x.Numero, x.ErroSync ?? "")).ToList();
    }

    /// <summary>Vendas recusadas com data, total e motivo (lista "ver motivo" do rodapé do caixa).</summary>
    public static List<VendaRecusadaLocal> VendasRecusadasDetalhe()
    {
        using var c = Db.Abrir();
        return c.Query<VendaRecusadaLocal>(
            "SELECT numero AS Numero, data AS Data, total AS Total, erro_sync AS Motivo FROM vendas " +
            "WHERE sincronizado = 0 AND erro_sync IS NOT NULL ORDER BY data").ToList();
    }

    /// <summary>"Tentar enviar agora": as recusadas entram na próxima sincronização, sem esperar os 30 minutos.</summary>
    public static void LiberarRecusadasParaReenvio()
    {
        using var c = Db.Abrir();
        c.Execute("UPDATE vendas SET erro_sync_em = '2000-01-01T00:00:00.0000000Z' WHERE sincronizado = 0 AND erro_sync IS NOT NULL");
    }

    private sealed class LinhaRecusa
    {
        public long Numero { get; set; }
        public string? ErroSync { get; set; }
    }

    // ---------- Orçamentos ----------

    /// <summary>Grava (novo ou alterado). Número novo é reservado na mesma transação. Marca para enviar ao servidor.</summary>
    public static void SalvarOrcamento(OrcamentoCompleto o)
    {
        using var c = Db.Abrir();
        using var tx = c.BeginTransaction();
        var existe = c.ExecuteScalar<int>("SELECT COUNT(*) FROM orcamentos WHERE id = @Id", o.Orcamento, tx) > 0;
        if (!existe)
        {
            o.Orcamento.Numero = c.ExecuteScalar<int>("SELECT COALESCE(MAX(numero), 0) + 1 FROM orcamentos", transaction: tx);
            c.Execute(
                """
                INSERT INTO orcamentos(id, numero, usuario_id, cliente_id, cliente_nome, data, validade, subtotal, desconto, total,
                    observacao, status, venda_id, sincronizado)
                VALUES(@Id, @Numero, @UsuarioId, @ClienteId, @ClienteNome, @Data, @Validade, @Subtotal, @Desconto, @Total,
                    @Observacao, @Status, @VendaId, 0)
                """, o.Orcamento, tx);
        }
        else
        {
            var alterados = c.Execute(
                """
                UPDATE orcamentos SET usuario_id = @UsuarioId, cliente_id = @ClienteId, cliente_nome = @ClienteNome, data = @Data,
                    validade = @Validade, subtotal = @Subtotal, desconto = @Desconto, total = @Total, observacao = @Observacao,
                    status = @Status, venda_id = @VendaId, sincronizado = 0, revisao = revisao + 1
                WHERE id = @Id AND status = 0
                """, o.Orcamento, tx);
            if (alterados == 0) throw new InvalidOperationException("Este orçamento já foi vendido ou cancelado e não pode ser reaberto.");
            o.Orcamento.Revisao = c.ExecuteScalar<long>(
                "SELECT revisao FROM orcamentos WHERE id = @Id", o.Orcamento, tx);
            c.Execute("DELETE FROM orcamento_itens WHERE orcamento_id = @Id", o.Orcamento, tx);
            o.Orcamento.Numero = c.ExecuteScalar<int>("SELECT numero FROM orcamentos WHERE id = @Id", o.Orcamento, tx);
        }
        // Itens com Ids novos a cada gravação: o servidor troca a lista inteira.
        foreach (var i in o.Itens)
        {
            i.Id = Guid.NewGuid().ToString();
            i.OrcamentoId = o.Orcamento.Id;
        }
        c.Execute(
            """
            INSERT INTO orcamento_itens(id, orcamento_id, produto_id, codigo, descricao, unidade, quantidade, preco_unitario, total)
            VALUES(@Id, @OrcamentoId, @ProdutoId, @Codigo, @Descricao, @Unidade, @Quantidade, @PrecoUnitario, @Total)
            """, o.Itens, tx);
        tx.Commit();
    }

    public static OrcamentoCompleto? CarregarOrcamento(string id)
    {
        using var c = Db.Abrir();
        var o = c.QueryFirstOrDefault<OrcamentoLocal>("SELECT * FROM orcamentos WHERE id = @id", new { id });
        if (o is null) return null;
        return new OrcamentoCompleto
        {
            Orcamento = o,
            Itens = c.Query<OrcamentoItemLocal>("SELECT * FROM orcamento_itens WHERE orcamento_id = @id", new { id }).ToList()
        };
    }

    /// <summary>Orçamentos deste caixa, mais recentes primeiro (abertos de qualquer data; os outros dos últimos 90 dias).</summary>
    public static List<OrcamentoLocal> ListarOrcamentos(string texto)
    {
        using var c = Db.Abrir();
        var corte = DateTime.UtcNow.AddDays(-90).ToString("o", System.Globalization.CultureInfo.InvariantCulture);
        var t = "%" + texto.Trim() + "%";
        return c.Query<OrcamentoLocal>(
            """
            SELECT * FROM orcamentos
            WHERE (status = 0 OR data >= @corte)
              AND (@vazio = 1 OR cliente_nome LIKE @t OR CAST(numero AS TEXT) LIKE @t OR observacao LIKE @t)
            ORDER BY numero DESC LIMIT 300
            """, new { corte, t, vazio = texto.Trim().Length == 0 ? 1 : 0 }).ToList();
    }

    public static void AlterarStatusOrcamento(string id, StatusOrcamento status, string? vendaId)
    {
        using var c = Db.Abrir();
        c.Execute("UPDATE orcamentos SET status = @status, venda_id = @vendaId, sincronizado = 0, revisao = revisao + 1 WHERE id = @id AND status = 0",
            new { id, status = (int)status, vendaId });
    }

    public static List<OrcamentoCompleto> OrcamentosPendentes(int limite)
    {
        using var c = Db.Abrir();
        var ids = c.Query<string>("SELECT id FROM orcamentos WHERE sincronizado = 0 ORDER BY data LIMIT @limite", new { limite }).ToList();
        return ids.Select(CarregarOrcamento).Where(o => o is not null).Select(o => o!).ToList();
    }

    /// <summary>Marca como enviado apenas a revisão exata, mesmo que duas alterações mantenham o mesmo valor/status.</summary>
    public static void MarcarOrcamentosSincronizados(IEnumerable<OrcamentoLocal> orcamentos)
    {
        using var c = Db.Abrir();
        using var tx = c.BeginTransaction();
        foreach (var o in orcamentos)
            c.Execute("UPDATE orcamentos SET sincronizado = 1 WHERE id = @Id AND revisao = @Revisao", o, tx);
        tx.Commit();
    }
}

/// <summary>Venda recusada pelo servidor, para mostrar ao operador.</summary>
public sealed class VendaRecusadaLocal
{
    public long Numero { get; set; }
    public string Data { get; set; } = "";
    public double Total { get; set; }
    public string? Motivo { get; set; }
}
