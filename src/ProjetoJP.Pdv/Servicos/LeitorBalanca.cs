using System.IO.Ports;

namespace ProjetoJP.Pdv.Servicos;

/// <summary>
/// Lê o peso de uma balança USB/serial. A porta "SIMULADA" devolve o peso gravado
/// nas opções, para testar sem o aparelho. A etiqueta de código de barras não passa por aqui.
/// </summary>
public static class LeitorBalanca
{
    public static bool Configurada(PdvConfig cfg) => !string.IsNullOrWhiteSpace(cfg.BalancaPorta);

    public static bool TentarLerPeso(PdvConfig cfg, out decimal kg, out string? erro)
    {
        kg = 0;
        erro = null;
        var porta = (cfg.BalancaPorta ?? "").Trim();
        if (porta.Length == 0)
        {
            erro = "Balança USB não configurada.";
            return false;
        }
        if (porta.Equals("SIMULADA", StringComparison.OrdinalIgnoreCase))
        {
            kg = cfg.BalancaPesoSimulado;
            if (kg <= 0) erro = "Balança sem peso.";
            return kg > 0;
        }

        SerialPort? serial = null;
        try
        {
            serial = new SerialPort(porta, cfg.BalancaBaud > 0 ? cfg.BalancaBaud : 9600, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 800,
                NewLine = "\n",
                DtrEnable = true,
                RtsEnable = true
            };
            serial.Open();
            var limite = Environment.TickCount64 + 1200;
            var ultima = ProjetoJP.Shared.SituacaoPeso.SemLeitura;
            while (Environment.TickCount64 < limite)
            {
                var linha = serial.ReadLine();
                if (!ProjetoJP.Shared.PesoBalanca.TentarLer(linha, out var leitura)) continue;
                if (leitura.Situacao == ProjetoJP.Shared.SituacaoPeso.Estavel)
                {
                    kg = leitura.Kg;
                    return true;
                }
                ultima = leitura.Situacao;
            }
            erro = ultima switch
            {
                ProjetoJP.Shared.SituacaoPeso.Instavel => "Peso instável. Espere a balança firmar.",
                ProjetoJP.Shared.SituacaoPeso.SemPeso => "Balança sem peso.",
                _ => "A balança não enviou o peso."
            };
            return false;
        }
        catch (TimeoutException)
        {
            erro = "A balança não respondeu.";
            return false;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException or InvalidOperationException)
        {
            erro = $"Não foi possível abrir {porta}. {ex.Message}";
            return false;
        }
        finally
        {
            if (serial is not null)
            {
                try { if (serial.IsOpen) serial.Close(); } catch (IOException) { }
                serial.Dispose();
            }
        }
    }
}
