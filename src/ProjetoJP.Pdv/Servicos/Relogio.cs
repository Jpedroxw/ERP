using System.Buffers.Binary;
using System.Globalization;
using System.Net.Sockets;
using ProjetoJP.Pdv.Dados;

namespace ProjetoJP.Pdv.Servicos;

/// <summary>
/// Hora confiável do caixa, sem depender do relógio do Windows (que pode estar errado ou ser mudado).
///
/// De onde vem a hora certa:
///  1. internet — NTP.br (pool.ntp.br) e, se o NTP estiver bloqueado na rede, o cabeçalho Date de sites grandes;
///  2. servidor do ERP — a hora que a API manda em cada sincronização.
/// A internet tem preferência; o servidor vale quando a internet não respondeu nas últimas horas.
///
/// Como fica certa mesmo offline:
///  - depois de uma medida, a hora anda pelo contador interno do Windows (TickCount), que não muda se alguém
///    mexer no relógio — então, durante a sessão, mudar a data do Windows não afeta promoções nem vendas;
///  - a diferença medida fica gravada; ao abrir o caixa sem internet, ela é aplicada ao relógio do Windows;
///  - a maior hora certa já vista também fica gravada: a hora do caixa nunca volta para antes dela
///    (atrasar o relógio do Windows não reativa promoção vencida).
/// </summary>
public static class Relogio
{
    private static readonly object Trava = new();
    private static DateTime _baseUtc = DateTime.UtcNow;
    private static long _baseTick = Environment.TickCount64;
    private static TimeSpan _diferencaSalva;
    private static DateTime _maiorUtc = DateTime.MinValue;
    private static long _ultimaWebTick = long.MinValue;
    private static long _ultimaGravacaoTick = long.MinValue;
    private static int _verificandoWeb;

    private static readonly TimeSpan IntervaloWeb = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan ValidadeWeb = TimeSpan.FromHours(6);

    /// <summary>De onde veio a hora em uso: "internet", "servidor" ou "estimada" (sem medida nesta sessão).</summary>
    public static string Fonte { get; private set; } = "estimada";

    /// <summary>Quanto o relógio do Windows está errado (positivo = Windows atrasado).</summary>
    public static TimeSpan Diferenca { get; private set; }

    /// <summary>Hora certa em UTC.</summary>
    public static DateTime UtcAgora
    {
        get
        {
            lock (Trava)
                return DateTime.SpecifyKind(_baseUtc.AddMilliseconds(Environment.TickCount64 - _baseTick), DateTimeKind.Utc);
        }
    }

    /// <summary>Hora certa no fuso do Windows.</summary>
    public static DateTime Agora => UtcAgora.ToLocalTime();

    /// <summary>Data de hoje (é o que decide se uma promoção está valendo).</summary>
    public static DateOnly Hoje => DateOnly.FromDateTime(Agora);

    /// <summary>Aviso para a barra de status quando o relógio do Windows está bem errado (null = está certo).</summary>
    public static string? Aviso
    {
        get
        {
            var minutos = Math.Abs(Diferenca.TotalMinutes);
            if (Fonte == "estimada" || minutos < 2) return null;
            var quanto = minutos >= 120 ? $"{minutos / 60:0} h" : $"{minutos:0} min";
            return $"relógio do Windows {quanto} {(Diferenca > TimeSpan.Zero ? "atrasado" : "adiantado")} — usando a hora da {Fonte}";
        }
    }

    /// <summary>Ao abrir o caixa: aplica a última diferença conhecida e não deixa a hora voltar para trás.</summary>
    public static void Carregar()
    {
        try
        {
            if (double.TryParse(Db.LerConfig("relogio_diferenca_ms"), NumberStyles.Float, CultureInfo.InvariantCulture, out var ms))
                _diferencaSalva = TimeSpan.FromMilliseconds(ms);
            if (Db.LerConfig("relogio_maior_utc") is { } maior)
                _maiorUtc = Db.ParaData(maior);
        }
        catch (Exception) { /* sem histórico: usa o relógio do Windows */ }

        lock (Trava)
        {
            var estimada = DateTime.UtcNow + _diferencaSalva;
            _baseUtc = estimada < _maiorUtc ? _maiorUtc : estimada;
            _baseTick = Environment.TickCount64;
            Diferenca = _baseUtc - DateTime.UtcNow;
        }
    }

    /// <summary>Hora que veio do servidor numa sincronização (t0/t1 = TickCount antes e depois da chamada).</summary>
    public static void RegistrarServidor(DateTime servidor, long t0, long t1)
    {
        if (t1 - t0 > 10_000) return; // resposta lenta demais para medir hora
        var webRecente = _ultimaWebTick != long.MinValue &&
                         Environment.TickCount64 - _ultimaWebTick < ValidadeWeb.TotalMilliseconds;
        if (webRecente) return; // a internet é mais confiável e foi medida há pouco
        var utc = servidor.Kind switch
        {
            DateTimeKind.Utc => servidor,
            DateTimeKind.Local => servidor.ToUniversalTime(),
            _ => DateTime.SpecifyKind(servidor, DateTimeKind.Utc)
        };
        Registrar(utc, t0 + (t1 - t0) / 2, "servidor");
    }

    /// <summary>Confere a hora pela internet (no máximo uma vez por hora; não trava quem chama).</summary>
    public static void VerificarWebSePreciso()
    {
        if (_ultimaWebTick != long.MinValue && Environment.TickCount64 - _ultimaWebTick < IntervaloWeb.TotalMilliseconds) return;
        if (Interlocked.Exchange(ref _verificandoWeb, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var medida = await MedirWebAsync();
                if (medida is { } m)
                {
                    _ultimaWebTick = Environment.TickCount64;
                    Registrar(m.utc, m.tick, "internet");
                }
            }
            catch (Exception) { /* sem internet: tenta de novo no próximo ciclo */ }
            finally { Interlocked.Exchange(ref _verificandoWeb, 0); }
        });
    }

    /// <summary>Grava de tempos em tempos a maior hora vista (proteção contra atrasar o relógio com o caixa fechado).</summary>
    public static void Guardar()
    {
        if (_ultimaGravacaoTick != long.MinValue && Environment.TickCount64 - _ultimaGravacaoTick < 5 * 60_000) return;
        _ultimaGravacaoTick = Environment.TickCount64;
        try
        {
            var agora = UtcAgora;
            if (agora > _maiorUtc)
            {
                _maiorUtc = agora;
                Db.GravarConfig("relogio_maior_utc", agora.ToString("o", CultureInfo.InvariantCulture));
            }
        }
        catch (Exception) { /* tenta na próxima */ }
    }

    private static void Registrar(DateTime utc, long tick, string fonte)
    {
        // Ignora respostas absurdas (servidor ou site com data quebrada).
        if (utc.Year is < 2024 or > 2100) return;
        lock (Trava)
        {
            _baseUtc = utc;
            _baseTick = tick;
            Fonte = fonte;
            // Diferença em relação ao relógio do Windows, no mesmo instante.
            var windows = DateTime.UtcNow.AddMilliseconds(-(Environment.TickCount64 - tick));
            Diferenca = utc - windows;
            _diferencaSalva = Diferenca;
        }
        try
        {
            Db.GravarConfig("relogio_diferenca_ms", Diferenca.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture));
            if (utc > _maiorUtc)
            {
                _maiorUtc = utc;
                Db.GravarConfig("relogio_maior_utc", utc.ToString("o", CultureInfo.InvariantCulture));
            }
        }
        catch (Exception) { /* fica só em memória */ }
    }

    // ---------- Internet ----------

    private static readonly string[] ServidoresNtp = ["pool.ntp.br", "a.st1.ntp.br", "time.windows.com"];
    private static readonly string[] SitesHttp = ["https://www.google.com", "https://www.cloudflare.com", "https://www.microsoft.com"];

    private static async Task<(DateTime utc, long tick)?> MedirWebAsync()
    {
        foreach (var host in ServidoresNtp)
            if (await NtpAsync(host) is { } ntp) return ntp;
        foreach (var site in SitesHttp)
            if (await HttpDateAsync(site) is { } http) return http;
        return null;
    }

    /// <summary>Pergunta a hora a um servidor NTP (UDP 123). Precisão de milissegundos.</summary>
    private static async Task<(DateTime utc, long tick)?> NtpAsync(string host)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var udp = new UdpClient();
            var pedido = new byte[48];
            pedido[0] = 0x1B; // versão 3, modo cliente
            var t0 = Environment.TickCount64;
            await udp.SendAsync(pedido, host, 123, cts.Token);
            var resposta = await udp.ReceiveAsync(cts.Token);
            var t1 = Environment.TickCount64;
            var b = resposta.Buffer;
            if (b.Length < 48) return null;
            var segundos = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(40, 4));
            var fracao = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(44, 4));
            if (segundos == 0) return null;
            var ms = segundos * 1000.0 + fracao * 1000.0 / 4294967296.0;
            var utc = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms);
            return (utc, t0 + (t1 - t0) / 2);
        }
        catch (Exception) { return null; }
    }

    /// <summary>Plano B quando a rede bloqueia NTP: o cabeçalho Date de um site (precisão de 1 s).</summary>
    private static async Task<(DateTime utc, long tick)?> HttpDateAsync(string url)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var pedido = new HttpRequestMessage(HttpMethod.Head, url);
            var t0 = Environment.TickCount64;
            using var resposta = await http.SendAsync(pedido);
            var t1 = Environment.TickCount64;
            if (resposta.Headers.Date is not { } data) return null;
            // O Date vem cortado no segundo: meio segundo a mais fica, em média, mais perto da hora real.
            return (data.UtcDateTime.AddMilliseconds(500), t0 + (t1 - t0) / 2);
        }
        catch (Exception) { return null; }
    }
}
