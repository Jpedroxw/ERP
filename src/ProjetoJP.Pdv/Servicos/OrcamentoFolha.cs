using System.Globalization;
using ProjetoJP.Controles;
using ProjetoJP.Pdv.Dados;

namespace ProjetoJP.Pdv.Servicos;

/// <summary>Orçamento do caixa em folha A4, na impressora comum do Windows (a térmica continua para o cupom).</summary>
public static class OrcamentoFolha
{
    public static OrcamentoImpresso Montar(PdvConfig cfg, OrcamentoCompleto oc, string operador, string terminal)
    {
        var o = oc.Orcamento;
        return new OrcamentoImpresso
        {
            Loja = cfg.NomeLoja,
            Cabecalho = cfg.CabecalhoCupom,
            Numero = o.Numero,
            Data = Db.ParaData(o.Data).ToLocalTime(),
            Caixa = terminal,
            Atendente = operador,
            Cliente = o.ClienteNome,
            Validade = DateOnly.TryParseExact(o.Validade, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var v) ? v : null,
            Observacao = o.Observacao,
            Subtotal = o.Subtotal,
            Desconto = o.Desconto,
            Total = o.Total,
            Itens = oc.Itens.Select(i => new OrcamentoImpresso.Item(i.Codigo, i.Descricao, i.Unidade, i.Quantidade, i.PrecoUnitario, i.Total)).ToList()
        };
    }

    /// <summary>Imprime direto na impressora A4 das Opções (ou na padrão do Windows). Devolve o erro, ou null.</summary>
    public static string? Imprimir(PdvConfig cfg, OrcamentoCompleto oc, string operador, string terminal)
    {
        using var doc = ImpressaoA4.Orcamento(Montar(cfg, oc, operador, terminal), cfg.ImpressoraA4);
        return ImpressaoA4.ImprimirDireto(doc);
    }

    /// <summary>Mostra a folha antes; se imprimir numa impressora e as Opções não tiverem uma A4, lembra dela.</summary>
    public static void Visualizar(IWin32Window dono, PdvConfig cfg, OrcamentoCompleto oc, string operador, string terminal)
    {
        using var doc = ImpressaoA4.Orcamento(Montar(cfg, oc, operador, terminal), cfg.ImpressoraA4);
        var usada = VisualizarImpressaoForm.Abrir(dono, doc, $"Orçamento nº {oc.Orcamento.Numero} — folha A4");
        if (!string.IsNullOrWhiteSpace(usada) && string.IsNullOrWhiteSpace(cfg.ImpressoraA4))
        {
            cfg.ImpressoraA4 = usada;
            try { cfg.Salvar(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
