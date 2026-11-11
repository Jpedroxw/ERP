using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using ProjetoJP.Pdv.Dados;
using ProjetoJP.Shared;

namespace ProjetoJP.Pdv.Servicos;

/// <summary>
/// Monta comandos ESC/POS (Epson, Elgin, Bematech, Daruma, Tanca e compatíveis)
/// e envia em modo RAW para a impressora do Windows.
/// Sem impressora configurada, grava o texto em ProgramData\ErpNaoFiscal\cupons.
/// </summary>
public class Cupom
{
    private static readonly CultureInfo Br = new("pt-BR");
    private readonly PdvConfig _cfg;
    private readonly List<byte> _bytes = [];
    private readonly StringBuilder _texto = new();
    private readonly Encoding _enc;

    static Cupom() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public Cupom(PdvConfig cfg)
    {
        _cfg = cfg;
        _enc = Encoding.GetEncoding(cfg.PaginaCodigo);
        Comando(0x1B, 0x40);                    // ESC @  inicializa
        Comando(0x1B, 0x74, cfg.TabelaEscPos);  // ESC t  tabela de caracteres
    }

    private int Colunas => _cfg.ColunasCupom;

    private void Comando(params byte[] b) => _bytes.AddRange(b);

    public Cupom Linha(string texto = "")
    {
        if (_cfg.SemAcentos) texto = Formatos.RemoverAcentos(texto);
        if (texto.Length > Colunas) texto = texto[..Colunas];
        _bytes.AddRange(_enc.GetBytes(texto + "\n"));
        _texto.AppendLine(texto);
        return this;
    }

    public Cupom Centro(string texto) =>
        Linha(texto.Length >= Colunas ? texto : new string(' ', (Colunas - texto.Length) / 2) + texto);

    public Cupom Separador(char c = '-') => Linha(new string(c, Colunas));

    public Cupom DuasColunas(string esquerda, string direita)
    {
        var espaco = Colunas - direita.Length;
        if (esquerda.Length > espaco - 1) esquerda = esquerda[..Math.Max(0, espaco - 1)];
        return Linha(esquerda.PadRight(espaco) + direita);
    }

    public Cupom Negrito(bool ligado) { Comando(0x1B, 0x45, (byte)(ligado ? 1 : 0)); return this; }

    /// <summary>Altura dupla (largura normal, então o alinhamento em colunas continua valendo).</summary>
    public Cupom Grande(bool ligado) { Comando(0x1D, 0x21, (byte)(ligado ? 0x01 : 0x00)); return this; }

    public void Finalizar(bool abrirGaveta = false)
    {
        Comando(0x1B, 0x64, 4); // avança 4 linhas
        if (_cfg.CortarPapel) Comando(0x1D, 0x56, 0x42, 0x00); // corte parcial
        if (abrirGaveta && _cfg.AbrirGaveta) Comando(0x1B, 0x70, 0x00, 0x19, 0xFA);
    }

    /// <summary>Imprime. Retorna mensagem de erro ou null se deu certo.</summary>
    public string? Enviar(string nomeDocumento)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_cfg.Impressora))
            {
                var pasta = Path.Combine(Db.Pasta, "cupons");
                Directory.CreateDirectory(pasta);
                var nome = string.Concat(nomeDocumento.Split(Path.GetInvalidFileNameChars()));
                File.WriteAllText(Path.Combine(pasta, $"{DateTime.Now:yyyyMMdd-HHmmss}-{nome}.txt"), _texto.ToString());
                return null;
            }
            ImpressoraRaw.Enviar(_cfg.Impressora, nomeDocumento, _bytes.ToArray());
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Confere a impressora configurada sem imprimir (para avisar antes de finalizar a venda).</summary>
    public static SituacaoImpressora Situacao(PdvConfig cfg) => ImpressoraRaw.Verificar(cfg.Impressora);

    public static string Dinheiro(decimal v) => v.ToString("N2", Br);
    public static string Qtd(decimal v) => v == Math.Truncate(v) ? v.ToString("N0", Br) : v.ToString("N3", Br);

    // ---------- Modelos de impressão ----------

    private void Cabecalho()
    {
        Negrito(true).Centro(_cfg.NomeLoja).Negrito(false);
        foreach (var l in _cfg.CabecalhoCupom) Centro(l);
        Separador();
    }

    /// <summary>Página de teste da tela de configurações: confere largura, acentos, corte e gaveta.</summary>
    public static string? ImprimirTeste(PdvConfig cfg)
    {
        var c = new Cupom(cfg);
        c.Cabecalho();
        c.Negrito(true).Centro("TESTE DE IMPRESSÃO").Negrito(false);
        c.Linha($"Data: {Relogio.Agora:dd/MM/yyyy HH:mm}");
        c.Linha($"Largura: {cfg.ColunasCupom} colunas");
        c.Separador();
        c.Linha("Acentos: áéíóú âêô ãõ ç ÁÉÍÓÚ Ç");
        c.Linha(string.Concat(Enumerable.Range(1, cfg.ColunasCupom).Select(i => (char)('0' + i % 10))));
        c.DuasColunas("Item de exemplo", Dinheiro(12.5m));
        c.Negrito(true).Grande(true).DuasColunas("TOTAL", Dinheiro(12.5m)).Grande(false).Negrito(false);
        c.Separador();
        c.Centro("A linha de números acima deve");
        c.Centro("caber inteira numa linha só.");
        c.Finalizar(abrirGaveta: true);
        return c.Enviar("teste");
    }

    public static string? ImprimirVenda(PdvConfig cfg, VendaCompleta vc, string operador, string? cliente, string terminal, bool segundaVia = false)
    {
        var v = vc.Venda;
        var formas = Repositorio.NomesDasFormas();
        var c = new Cupom(cfg);
        c.Cabecalho();
        c.Negrito(true).Centro("DOCUMENTO SEM VALOR FISCAL").Negrito(false);
        if (segundaVia) c.Centro("*** SEGUNDA VIA ***");
        if (v.Status == (int)StatusVenda.Cancelada) c.Negrito(true).Centro("*** VENDA CANCELADA ***").Negrito(false);
        c.DuasColunas($"Venda nº {v.Numero}", Db.ParaData(v.Data).ToLocalTime().ToString("dd/MM/yyyy HH:mm", Br));
        c.Linha($"Terminal: {terminal}  Operador: {operador}");
        if (!string.IsNullOrEmpty(cliente)) c.Linha($"Cliente: {cliente}");
        c.Separador();
        c.Linha("ITEM DESCRIÇÃO");
        c.DuasColunas("     QTD x UNIT", "TOTAL");
        c.Separador();

        var n = 1;
        foreach (var i in vc.Itens)
        {
            c.Linha($"{n++:000} {i.Descricao}");
            c.DuasColunas($"     {Qtd(i.Quantidade)} x {Dinheiro(i.PrecoUnitario)}", Dinheiro(i.Total));
            if (i.PrecoNormal > i.PrecoUnitario)
            {
                var economia = Math.Round((i.PrecoNormal - i.PrecoUnitario) * i.Quantidade, 2);
                c.DuasColunas($"     {(i.Atacado ? "ATACADO" : "PROMOÇÃO")} (de {Dinheiro(i.PrecoNormal)})", "-" + Dinheiro(economia));
            }
        }
        var economiaTotal = vc.Itens.Where(i => i.PrecoNormal > i.PrecoUnitario)
            .Sum(i => Math.Round((i.PrecoNormal - i.PrecoUnitario) * i.Quantidade, 2));

        c.Separador();
        c.DuasColunas("Subtotal", Dinheiro(v.Subtotal));
        if (v.Desconto > 0) c.DuasColunas("Desconto", "-" + Dinheiro(v.Desconto));
        c.Negrito(true).Grande(true).DuasColunas("TOTAL", Dinheiro(v.Total)).Grande(false).Negrito(false);
        c.Separador();
        foreach (var p in vc.Pagamentos)
        {
            var valor = p.Valor;
            c.DuasColunas(formas.GetValueOrDefault(p.FormaId, "Pagamento"), Dinheiro(valor));
        }
        if (v.Troco > 0) c.DuasColunas("Troco", Dinheiro(v.Troco));
        c.Separador();
        if (economiaTotal > 0) c.Negrito(true).Centro($"Você economizou {Dinheiro(economiaTotal)}").Negrito(false);
        c.Centro("Obrigado pela preferência!");
        c.Finalizar(abrirGaveta: !segundaVia && v.Status == (int)StatusVenda.Concluida);
        return c.Enviar($"venda-{v.Numero}");
    }

    public static string? ImprimirOrcamento(PdvConfig cfg, OrcamentoCompleto oc, string operador, string terminal)
    {
        var o = oc.Orcamento;
        var c = new Cupom(cfg);
        c.Cabecalho();
        c.Negrito(true).Grande(true).Centro("ORÇAMENTO").Grande(false).Negrito(false);
        c.Centro("NÃO É DOCUMENTO DE VENDA");
        c.DuasColunas($"Orçamento nº {o.Numero}", Db.ParaData(o.Data).ToLocalTime().ToString("dd/MM/yyyy HH:mm", Br));
        c.Linha($"Terminal: {terminal}  Atendente: {operador}");
        if (!string.IsNullOrWhiteSpace(o.ClienteNome)) c.Linha($"Cliente: {o.ClienteNome}");
        c.Separador();
        c.Linha("ITEM DESCRIÇÃO");
        c.DuasColunas("     QTD x UNIT", "TOTAL");
        c.Separador();
        var n = 1;
        foreach (var i in oc.Itens)
        {
            c.Linha($"{n++:000} {i.Descricao}");
            c.DuasColunas($"     {Qtd(i.Quantidade)} {i.Unidade} x {Dinheiro(i.PrecoUnitario)}", Dinheiro(i.Total));
        }
        c.Separador();
        c.DuasColunas("Subtotal", Dinheiro(o.Subtotal));
        if (o.Desconto > 0) c.DuasColunas("Desconto", "-" + Dinheiro(o.Desconto));
        c.Negrito(true).Grande(true).DuasColunas("TOTAL", Dinheiro(o.Total)).Grande(false).Negrito(false);
        c.Separador();
        if (DateOnly.TryParseExact(o.Validade, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var validade))
            c.Centro($"Válido até {validade:dd/MM/yyyy}");
        if (!string.IsNullOrWhiteSpace(o.Observacao)) c.Linha(o.Observacao);
        c.Centro("Preços sujeitos a alteração e à disponibilidade.");
        c.Finalizar();
        return c.Enviar($"orcamento-{o.Numero}");
    }

    public static string? ImprimirMovimento(PdvConfig cfg, CaixaMovimentoLocal m, string operador)
    {
        var c = new Cupom(cfg);
        c.Cabecalho();
        c.Negrito(true).Centro(((TipoMovimentoCaixa)m.Tipo).ToString().ToUpperInvariant()).Negrito(false);
        c.Linha($"Data: {Db.ParaData(m.Data).ToLocalTime():dd/MM/yyyy HH:mm}");
        c.Linha($"Operador: {operador}");
        c.Negrito(true).DuasColunas("Valor", Dinheiro(m.Valor)).Negrito(false);
        if (!string.IsNullOrWhiteSpace(m.Motivo)) c.Linha($"Motivo: {m.Motivo}");
        c.Linha();
        c.Linha();
        c.Centro("______________________________");
        c.Centro("Assinatura");
        c.Finalizar(abrirGaveta: true);
        return c.Enviar("movimento-caixa");
    }

    public static string? ImprimirFechamento(PdvConfig cfg, CaixaLocal caixa, ResumoCaixa r, decimal contado, string operador, string terminal)
    {
        var c = new Cupom(cfg);
        c.Cabecalho();
        c.Negrito(true).Centro("FECHAMENTO DE CAIXA").Negrito(false);
        c.Linha($"Terminal: {terminal}");
        c.Linha($"Abertura: {Db.ParaData(caixa.Abertura).ToLocalTime():dd/MM/yyyy HH:mm}");
        c.Linha($"Fechamento: {Relogio.Agora:dd/MM/yyyy HH:mm}");
        c.Linha($"Operador: {operador}");
        c.Separador();
        c.DuasColunas($"Vendas ({r.QuantidadeVendas})", Dinheiro(r.TotalVendas));
        c.DuasColunas($"Canceladas", r.QuantidadeCanceladas.ToString());
        c.Separador();
        foreach (var f in r.PorForma) c.DuasColunas(f.Descricao, Dinheiro(f.Valor));
        c.Separador();
        c.DuasColunas("Fundo de troco", Dinheiro(r.Abertura));
        c.DuasColunas("Suprimentos", Dinheiro(r.Suprimentos));
        c.DuasColunas("Sangrias", "-" + Dinheiro(r.Sangrias));
        c.DuasColunas("Vendas em dinheiro", Dinheiro(r.VendasDinheiro));
        c.Negrito(true).DuasColunas("Dinheiro esperado", Dinheiro(r.DinheiroEsperado)).Negrito(false);
        c.DuasColunas("Dinheiro contado", Dinheiro(contado));
        var dif = contado - r.DinheiroEsperado;
        c.Negrito(true).DuasColunas(dif >= 0 ? "Sobra" : "Falta", Dinheiro(Math.Abs(dif))).Negrito(false);
        c.Linha();
        c.Centro("______________________________");
        c.Centro("Assinatura");
        c.Finalizar();
        return c.Enviar("fechamento");
    }
}

/// <summary>Situação da impressora do cupom (para avisar ANTES de finalizar a venda).</summary>
public sealed record SituacaoImpressora(bool Ok, bool Configurada, string Mensagem, int CuponsNaFila = 0);

/// <summary>Envio RAW pelo spooler do Windows (winspool.drv).</summary>
internal static class ImpressoraRaw
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PrinterInfo2
    {
        public IntPtr pServerName, pPrinterName, pShareName, pPortName, pDriverName, pComment, pLocation, pDevMode,
            pSepFile, pPrintProcessor, pDatatype, pParameters, pSecurityDescriptor;
        public uint Attributes, Priority, DefaultPriority, StartTime, UntilTime, Status, cJobs, AveragePPM;
    }

    [DllImport("winspool.drv", EntryPoint = "GetPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetPrinter(IntPtr hPrinter, int level, IntPtr pPrinter, int cbBuf, out int pcbNeeded);

    private const uint AtributoTrabalharOffline = 0x400;
    private const uint StatusPausada = 0x1, StatusErro = 0x2, StatusPapelPreso = 0x8, StatusSemPapel = 0x10,
        StatusOffline = 0x80, StatusNaoDisponivel = 0x1000, StatusTampaAberta = 0x400000, StatusPrecisaAtencao = 0x100000;

    /// <summary>
    /// A impressora existe no Windows e não está offline/sem papel/com erro? Rápido (não imprime nada).
    /// Obs.: algumas impressoras térmicas USB não informam "sem papel"; desconectada, o Windows costuma marcar "offline".
    /// </summary>
    public static SituacaoImpressora Verificar(string? impressora)
    {
        if (string.IsNullOrWhiteSpace(impressora))
            return new SituacaoImpressora(true, false, "Sem impressora configurada: o cupom é salvo em arquivo (Opções → Impressora).");
        if (!OpenPrinter(impressora, out var h, IntPtr.Zero))
            return new SituacaoImpressora(false, true, $"A impressora \"{impressora}\" não foi encontrada neste computador.\n" +
                                                       "Confira se ela está ligada e conectada, ou escolha outra em Opções → Impressora.");
        try
        {
            GetPrinter(h, 2, IntPtr.Zero, 0, out var tamanho);
            if (tamanho <= 0) return new SituacaoImpressora(true, true, "Impressora pronta");
            var buffer = Marshal.AllocHGlobal(tamanho);
            try
            {
                if (!GetPrinter(h, 2, buffer, tamanho, out _)) return new SituacaoImpressora(true, true, "Impressora pronta");
                var info = Marshal.PtrToStructure<PrinterInfo2>(buffer);
                var problema =
                    (info.Attributes & AtributoTrabalharOffline) != 0 || (info.Status & StatusOffline) != 0 ? "está desligada ou desconectada (offline)" :
                    (info.Status & StatusNaoDisponivel) != 0 ? "não está disponível" :
                    (info.Status & StatusSemPapel) != 0 ? "está sem papel" :
                    (info.Status & StatusPapelPreso) != 0 ? "está com papel preso" :
                    (info.Status & StatusTampaAberta) != 0 ? "está com a tampa aberta" :
                    (info.Status & StatusPausada) != 0 ? "está pausada no Windows" :
                    (info.Status & (StatusErro | StatusPrecisaAtencao)) != 0 ? "está com erro" : null;
                var fila = (int)info.cJobs;
                if (problema is not null)
                    return new SituacaoImpressora(false, true, $"A impressora \"{impressora}\" {problema}." +
                        (fila > 0 ? $"\nHá {fila} impressão(ões) parada(s) na fila do Windows." : ""), fila);
                return new SituacaoImpressora(true, true, fila > 3 ? $"Impressora com {fila} impressões na fila" : "Impressora pronta", fila);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        catch (Exception ex) when (ex is ArgumentException or MarshalDirectiveException or OutOfMemoryException)
        {
            return new SituacaoImpressora(true, true, "Impressora pronta"); // não conseguiu ler a situação: tenta imprimir mesmo
        }
        finally { ClosePrinter(h); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private class DocInfo1
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string pDocName = "";
        [MarshalAs(UnmanagedType.LPWStr)] public string? pOutputFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string pDataType = "RAW";
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int StartDocPrinter(IntPtr hPrinter, int level, [In] DocInfo1 docInfo);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr hPrinter, byte[] pBytes, int dwCount, out int dwWritten);

    public static void Enviar(string impressora, string documento, byte[] dados)
    {
        if (!OpenPrinter(impressora, out var h, IntPtr.Zero))
            throw new InvalidOperationException($"Impressora \"{impressora}\" não encontrada. Confira se está ligada e conectada " +
                                                $"(Opções → Impressora). Código {Marshal.GetLastWin32Error()}.");
        try
        {
            if (StartDocPrinter(h, 1, new DocInfo1 { pDocName = documento }) == 0)
                throw new InvalidOperationException($"Falha ao iniciar impressão (erro {Marshal.GetLastWin32Error()}).");
            try
            {
                StartPagePrinter(h);
                if (!WritePrinter(h, dados, dados.Length, out _))
                    throw new InvalidOperationException($"Falha ao enviar dados (erro {Marshal.GetLastWin32Error()}).");
                EndPagePrinter(h);
            }
            finally
            {
                EndDocPrinter(h);
            }
        }
        finally
        {
            ClosePrinter(h);
        }
    }
}
