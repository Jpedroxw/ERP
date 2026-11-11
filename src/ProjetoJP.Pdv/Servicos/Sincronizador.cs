using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using ProjetoJP.Pdv.Dados;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;

namespace ProjetoJP.Pdv.Servicos;

/// <summary>
/// Sobe vendas/caixas pendentes e baixa cadastros alterados, em segundo plano.
/// Se a internet cair, o caixa continua vendendo; tudo sobe quando voltar.
/// </summary>
public sealed class Sincronizador : IDisposable
{
    private readonly PdvConfig _cfg;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _trava = new(1, 1);
    private System.Threading.Timer? _timer;
    private volatile bool _descartado;
    private string? _avisoEnvio;

    public bool Online { get; private set; }
    public string UltimaMensagem { get; private set; } = "Aguardando primeira sincronização";

    /// <summary>(online, mensagem) — disparado fora da thread da tela; use Invoke.</summary>
    public event Action<bool, string>? StatusMudou;

    public Sincronizador(PdvConfig cfg)
    {
        _cfg = cfg;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        if (Uri.TryCreate(cfg.ApiUrl.TrimEnd('/') + "/", UriKind.Absolute, out var uri))
            _http.BaseAddress = uri;
        _http.DefaultRequestHeaders.Add("X-Terminal-Key", cfg.TerminalKey);
    }

    public void Iniciar()
    {
        var intervalo = TimeSpan.FromSeconds(Math.Max(10, _cfg.IntervaloSyncSegundos));
        // O callback do timer trata falhas inesperadas e informa o status ao operador.
        _timer = new System.Threading.Timer(async _ =>
        {
            try { await SincronizarAsync(); }
            catch (Exception ex)
            {
                // Um erro aqui dentro (ex.: a tela fechando nesse instante) derrubaria o caixa: por isso o try.
                try
                {
                    if (!_descartado)
                        Avisar(false, $"Falha inesperada na sincronização: {Resumo(ex)}");
                }
                catch (Exception) { /* sem onde mostrar */ }
            }
        }, null, TimeSpan.FromSeconds(2), intervalo);
    }

    /// <summary>Retorna true se sincronizou. Se já houver uma sincronização rodando, não faz nada.</summary>
    public async Task<bool> SincronizarAsync()
    {
        if (_descartado) return false;
        if (_http.BaseAddress is null)
        {
            Avisar(false, "Endereço do servidor inválido: revise em Opções do caixa");
            return false;
        }
        if (!await _trava.WaitAsync(0)) return false;

        // Hora pela internet (no máximo 1x por hora, em segundo plano) — vale mesmo com o servidor fora do ar.
        Relogio.VerificarWebSePreciso();
        try
        {
            await EnviarAsync();
            await ReceberAsync();
            var pendentes = Repositorio.ContarPendencias();
            var recusadas = Repositorio.VendasRecusadas();
            if (recusadas.Count > 0)
            {
                var (numero, motivo) = recusadas[0];
                var texto = recusadas.Count == 1
                    ? $"Venda nº {numero} recusada pelo servidor: {motivo}"
                    : $"{recusadas.Count} vendas recusadas pelo servidor (nº {numero}: {motivo})";
                _avisoEnvio = _avisoEnvio is null ? texto : texto + "; " + _avisoEnvio;
            }
            Avisar(true, (pendentes == 0
                ? $"Sincronizado às {Relogio.Agora:HH:mm:ss}"
                : $"Sincronizado às {Relogio.Agora:HH:mm:ss} ({pendentes} pendente(s))") +
                (_avisoEnvio is null ? "" : " · " + _avisoEnvio) + AvisoRelogio());
            return true;
        }
        catch (Exception ex)
        {
            var pendentes = SafeContarPendencias();
            Avisar(false, $"OFFLINE — {Resumo(ex)}" + (pendentes > 0 ? $" — {pendentes} venda(s)/registro(s) aguardando envio" : "") + AvisoRelogio());
            return false;
        }
        finally
        {
            Relogio.Guardar();
            _trava.Release();
        }
    }

    private static string AvisoRelogio() => Relogio.Aviso is { } a ? $" · {a}" : "";

    private void Avisar(bool online, string msg)
    {
        Online = online;
        UltimaMensagem = msg;
        StatusMudou?.Invoke(online, msg);
    }

    private static int SafeContarPendencias()
    {
        try { return Repositorio.ContarPendencias(); } catch { return 0; }
    }

    private static string Resumo(Exception ex) => ex switch
    {
        TaskCanceledException => "servidor não respondeu",
        HttpRequestException h => ProjetoJP.Shared.EnderecoServidor.ErroConexao(h),
        _ => ex.Message
    };

    // ---------- Envio ----------

    private async Task EnviarAsync()
    {
        _avisoEnvio = null;
        // Envia em lotes até zerar (ou até 10 lotes por rodada).
        for (var lote = 0; lote < 10; lote++)
        {
            var caixas = Repositorio.CaixasPendentes();
            var movs = Repositorio.MovimentosPendentes();
            var vendas = Repositorio.VendasPendentes(100);
            var orcamentos = Repositorio.OrcamentosPendentes(50);
            if (caixas.Count == 0 && movs.Count == 0 && vendas.Count == 0 && orcamentos.Count == 0) return;

            var req = new SyncEnviarRequest
            {
                Caixas = caixas.Select(c => new CaixaSessaoDto
                {
                    Id = Guid.Parse(c.Id),
                    UsuarioAberturaId = Guid.Parse(c.UsuarioAberturaId),
                    Abertura = Db.ParaData(c.Abertura),
                    ValorAbertura = c.ValorAbertura,
                    Fechamento = c.Fechamento is null ? null : Db.ParaData(c.Fechamento),
                    UsuarioFechamentoId = c.UsuarioFechamentoId is null ? null : Guid.Parse(c.UsuarioFechamentoId),
                    ValorFechamentoInformado = c.ValorFechamento,
                    Status = (StatusCaixa)c.Status
                }).ToList(),
                MovimentosCaixa = movs.Select(m => new CaixaMovimentoDto
                {
                    Id = Guid.Parse(m.Id),
                    CaixaSessaoId = Guid.Parse(m.CaixaId),
                    Tipo = (TipoMovimentoCaixa)m.Tipo,
                    Valor = m.Valor,
                    Data = Db.ParaData(m.Data),
                    Motivo = m.Motivo,
                    UsuarioId = Guid.Parse(m.UsuarioId)
                }).ToList(),
                Vendas = vendas.Select(ParaDto).ToList(),
                Orcamentos = orcamentos.Select(ParaDto).ToList()
            };

            using var resp = await _http.PostAsJsonAsync("api/sync/enviar", req);
            await GarantirSucessoAsync(resp);
            var ok = await resp.Content.ReadFromJsonAsync<SyncEnviarResponse>() ?? new SyncEnviarResponse();
            if (ok.Avisos.Count > 0)
                _avisoEnvio = string.Join("; ", ok.Avisos.Take(2));

            var caixasOk = ok.CaixasAceitos.Select(g => g.ToString()).ToHashSet();
            var movsOk = ok.MovimentosAceitos.Select(g => g.ToString()).ToHashSet();
            var vendasOk = ok.VendasAceitas.Select(g => g.ToString()).ToHashSet();
            // Servidor antigo (sem orçamentos) não devolve a lista: ficam pendentes até a API ser atualizada.
            var orcamentosOk = (ok.OrcamentosAceitos ?? []).Select(g => g.ToString()).ToHashSet();
            Repositorio.MarcarOrcamentosSincronizados(orcamentos.Select(o => o.Orcamento).Where(o => orcamentosOk.Contains(o.Id)));

            Repositorio.MarcarSincronizado(
                caixas.Where(c => caixasOk.Contains(c.Id)),
                movs.Where(m => movsOk.Contains(m.Id)),
                vendas.Select(v => v.Venda).Where(v => vendasOk.Contains(v.Id)));
            // Recusadas: guardam o motivo e ficam de lado (tentam de novo depois); as outras seguem subindo.
            var recusadas = (ok.VendasRecusadas ?? []).Where(x => !vendasOk.Contains(x.Id.ToString())).ToList();
            if (recusadas.Count > 0)
                Repositorio.MarcarRecusadas(recusadas.Select(x => (x.Id.ToString(), x.Motivo)));

            if (recusadas.Count > 0 && vendasOk.Count == 0 && caixasOk.Count == 0 && movsOk.Count == 0) return; // nada andou
            if (vendas.Count < 100 && orcamentos.Count < 50) return;
            if (orcamentosOk.Count == 0 && vendas.Count < 100) return; // não fica repetindo orçamento recusado
        }
    }

    private static OrcamentoDto ParaDto(OrcamentoCompleto oc)
    {
        var o = oc.Orcamento;
        return new OrcamentoDto
        {
            Id = Guid.Parse(o.Id),
            Revisao = o.Revisao,
            Numero = o.Numero,
            UsuarioId = Guid.Parse(o.UsuarioId),
            ClienteId = string.IsNullOrEmpty(o.ClienteId) ? null : Guid.Parse(o.ClienteId),
            ClienteNome = o.ClienteNome,
            Data = Db.ParaData(o.Data),
            Validade = DateOnly.TryParseExact(o.Validade, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var validade)
                ? validade : null,
            Subtotal = o.Subtotal,
            Desconto = o.Desconto,
            Total = o.Total,
            Observacao = o.Observacao,
            Status = (StatusOrcamento)o.Status,
            VendaId = string.IsNullOrEmpty(o.VendaId) ? null : Guid.Parse(o.VendaId),
            Itens = oc.Itens.Select(i => new OrcamentoItemDto
            {
                Id = Guid.Parse(i.Id),
                ProdutoId = Guid.Parse(i.ProdutoId),
                Codigo = i.Codigo,
                Descricao = i.Descricao,
                Unidade = i.Unidade,
                Quantidade = i.Quantidade,
                PrecoUnitario = i.PrecoUnitario,
                Total = i.Total
            }).ToList()
        };
    }

    private static VendaDto ParaDto(VendaCompleta vc)
    {
        var v = vc.Venda;
        return new VendaDto
        {
            Id = Guid.Parse(v.Id),
            Numero = v.Numero,
            CaixaSessaoId = Guid.Parse(v.CaixaId),
            UsuarioId = Guid.Parse(v.UsuarioId),
            ClienteId = string.IsNullOrEmpty(v.ClienteId) ? null : Guid.Parse(v.ClienteId),
            Data = Db.ParaData(v.Data),
            Subtotal = v.Subtotal,
            Desconto = v.Desconto,
            Total = v.Total,
            Troco = v.Troco,
            Status = (StatusVenda)v.Status,
            CanceladaEm = v.CanceladaEm is null ? null : Db.ParaData(v.CanceladaEm),
            Itens = vc.Itens.Select(i => new VendaItemDto
            {
                Id = Guid.Parse(i.Id),
                ProdutoId = Guid.Parse(i.ProdutoId),
                Descricao = i.Descricao,
                Quantidade = i.Quantidade,
                PrecoUnitario = i.PrecoUnitario,
                Desconto = i.Desconto,
                Total = i.Total
            }).ToList(),
            Pagamentos = vc.Pagamentos.Select(p => new VendaPagamentoDto
            {
                Id = Guid.Parse(p.Id),
                FormaPagamentoId = Guid.Parse(p.FormaId),
                Valor = p.Valor
            }).ToList()
        };
    }

    // ---------- Recebimento ----------

    private async Task ReceberAsync()
    {
        // Cadastros baixados de outro servidor ou com outra chave não valem para este: baixa tudo de novo.
        // (Antes, ao trocar o Servidor, o caixa só pedia o que mudou depois da última sincronização
        // com o servidor ANTIGO — usuários e produtos mais velhos do servidor novo nunca chegavam.)
        var origem = OrigemAtual();
        var trocouOrigem = Db.LerConfig("origem_sync") != origem;
        var desde = trocouOrigem ? null : Db.LerConfig("ultima_sync");
        var url = "api/sync/receber" + (desde is null ? "" : "?desde=" + Uri.EscapeDataString(desde));

        var t0 = Environment.TickCount64;
        using var resp = await _http.GetAsync(url);
        var t1 = Environment.TickCount64;
        await GarantirSucessoAsync(resp);
        var dados = await resp.Content.ReadFromJsonAsync<SyncReceberResponse>()
                    ?? throw new InvalidOperationException("Resposta vazia do servidor");
        Relogio.RegistrarServidor(dados.Servidor, t0, t1); // hora do servidor (vale quando a internet não respondeu)

        using (var c = Db.Abrir())
        using (var tx = c.BeginTransaction())
        {
            if (trocouOrigem)
            {
                // Desativa o que veio do servidor anterior; o que existe neste volta ativo logo abaixo.
                // (Não apaga: vendas antigas deste caixa continuam apontando para esses registros.)
                c.Execute("UPDATE usuarios SET ativo = 0; UPDATE produtos SET ativo = 0; " +
                          "DELETE FROM promocoes; UPDATE clientes SET ativo = 0; UPDATE formas SET ativo = 0;", transaction: tx);
            }

            foreach (var u in dados.Usuarios)
                c.Execute(
                    """
                    INSERT INTO usuarios(id, nome, login, senha_hash, perfil, ativo, permissoes)
                    VALUES(@id, @nome, @login, @hash, @perfil, @ativo, @permissoes)
                    ON CONFLICT(id) DO UPDATE SET nome = excluded.nome, login = excluded.login,
                        senha_hash = excluded.senha_hash, perfil = excluded.perfil, ativo = excluded.ativo,
                        permissoes = excluded.permissoes
                    """,
                    new
                    {
                        id = u.Id.ToString(), nome = u.Nome, login = u.Login, hash = u.SenhaHash, perfil = (int)u.Perfil, ativo = u.Ativo,
                        permissoes = u.Permissoes is null ? null : Permissoes.Gravar(u.Permissoes)
                    }, tx);

            foreach (var p in dados.Produtos)
                c.Execute(
                    """
                    INSERT INTO produtos(id, codigo, codigo_barras, descricao, unidade, preco_venda, preco_atacado, qtd_atacado, estoque, ativo)
                    VALUES(@id, @codigo, @barras, @descricao, @unidade, @preco, @atacado, @qtdAtacado, @estoque, @ativo)
                    ON CONFLICT(id) DO UPDATE SET codigo = excluded.codigo, codigo_barras = excluded.codigo_barras,
                        descricao = excluded.descricao, unidade = excluded.unidade, preco_venda = excluded.preco_venda,
                        preco_atacado = excluded.preco_atacado, qtd_atacado = excluded.qtd_atacado,
                        estoque = excluded.estoque, ativo = excluded.ativo
                    """,
                    new
                    {
                        id = p.Id.ToString(), codigo = p.Codigo, barras = p.CodigoBarras, descricao = p.Descricao,
                        unidade = p.Unidade, preco = p.PrecoVenda, atacado = p.PrecoAtacado, qtdAtacado = p.QtdMinimaAtacado,
                        estoque = p.EstoqueAtual, ativo = p.Ativo
                    }, tx);

            // Venda feita neste caixa e ainda não enviada já baixou o estoque local. Só os produtos que VIERAM
            // nesta carga trocaram o estoque pelo saldo do servidor (que ainda não viu essa venda): só neles desconta
            // de novo. Os outros já estão com o desconto — descontar neles a cada sincronização baixava o estoque sem parar.
            foreach (var p in dados.Produtos)
                c.Execute(
                    """
                    UPDATE produtos SET estoque = estoque - IFNULL((
                        SELECT SUM(i.quantidade) FROM venda_itens i
                        JOIN vendas v ON v.id = i.venda_id
                        WHERE i.produto_id = produtos.id AND v.sincronizado = 0 AND v.status = 0
                    ), 0)
                    WHERE id = @id
                    """, new { id = p.Id.ToString() }, tx);

            foreach (var promo in dados.Promocoes)
                c.Execute(
                    """
                    INSERT INTO promocoes(id, produto_id, inicio, fim, preco, qtd_minima, ativo)
                    VALUES(@id, @produto, @inicio, @fim, @preco, @minimo, @ativo)
                    ON CONFLICT(id) DO UPDATE SET produto_id = excluded.produto_id,
                        inicio = excluded.inicio, fim = excluded.fim,
                        preco = excluded.preco, qtd_minima = excluded.qtd_minima, ativo = excluded.ativo
                    """, new {
                        id = promo.Id.ToString(), produto = promo.ProdutoId.ToString(),
                        inicio = promo.Inicio.ToString("yyyy-MM-dd"), fim = promo.Fim.ToString("yyyy-MM-dd"),
                        preco = promo.PrecoPromocional, minimo = promo.QuantidadeMinima, ativo = promo.Ativa
                    }, tx);

            foreach (var cl in dados.Clientes)
                c.Execute(
                    """
                    INSERT INTO clientes(id, nome, cpf_cnpj, telefone, ativo)
                    VALUES(@id, @nome, @doc, @tel, @ativo)
                    ON CONFLICT(id) DO UPDATE SET nome = excluded.nome, cpf_cnpj = excluded.cpf_cnpj,
                        telefone = excluded.telefone, ativo = excluded.ativo
                    """,
                    new { id = cl.Id.ToString(), nome = cl.Nome, doc = cl.CpfCnpj, tel = cl.Telefone, ativo = cl.Ativo }, tx);

            foreach (var f in dados.Formas)
                c.Execute(
                    """
                    INSERT INTO formas(id, descricao, permite_troco, eh_fiado, ordem, ativo)
                    VALUES(@id, @descricao, @troco, @fiado, @ordem, @ativo)
                    ON CONFLICT(id) DO UPDATE SET descricao = excluded.descricao, permite_troco = excluded.permite_troco,
                        eh_fiado = excluded.eh_fiado, ordem = excluded.ordem, ativo = excluded.ativo
                    """,
                    new { id = f.Id.ToString(), descricao = f.Descricao, troco = f.PermiteTroco, fiado = f.EhFiado, ordem = f.Ordem, ativo = f.Ativo }, tx);

            c.Execute("INSERT INTO config(chave, valor) VALUES('ultima_sync', @v) ON CONFLICT(chave) DO UPDATE SET valor = excluded.valor",
                new { v = dados.Servidor.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) }, tx);
            c.Execute("INSERT INTO config(chave, valor) VALUES('nome_terminal', @v) ON CONFLICT(chave) DO UPDATE SET valor = excluded.valor",
                new { v = dados.NomeTerminal }, tx);
            c.Execute("INSERT INTO config(chave, valor) VALUES('origem_sync', @v) ON CONFLICT(chave) DO UPDATE SET valor = excluded.valor",
                new { v = origem }, tx);

            tx.Commit();
        }
    }

    /// <summary>De onde vêm os cadastros: servidor + chave do terminal (só um resumo da chave, nunca a chave).</summary>
    private string OrigemAtual()
    {
        var chave = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_cfg.TerminalKey ?? "")))[..16];
        return (_cfg.Servidor ?? "").Trim().ToLowerInvariant() + "|" + chave;
    }

    private static async Task GarantirSucessoAsync(HttpResponseMessage resp)
    {
        if (resp.IsSuccessStatusCode) return;

        string msg;
        try
        {
            var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
            msg = json.TryGetProperty("erro", out var e) ? e.GetString() ?? "" : resp.ReasonPhrase ?? "";
        }
        catch
        {
            msg = resp.ReasonPhrase ?? "";
        }
        throw new InvalidOperationException((int)resp.StatusCode switch
        {
            401 => "chave do terminal não reconhecida (confira em Opções se o Servidor é o mesmo da Gestão)",
            403 => "acesso suspenso — fale com o suporte",
            404 => "servidor não encontrado — confira o nome em Opções → Servidor",
            var c => $"servidor respondeu {c}: {msg}"
        });
    }

    public void Dispose()
    {
        // Chamado ao trocar as configurações. Uma sincronização em andamento termina com erro e é ignorada.
        _descartado = true;
        _timer?.Dispose();
        _http.Dispose();
    }
}
