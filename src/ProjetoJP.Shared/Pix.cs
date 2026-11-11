using System.Globalization;
using System.Text;

namespace ProjetoJP.Shared;

/// <summary>
/// Pix "copia e cola" (BR Code) estático, com o valor da venda, no padrão do Banco Central
/// (Manual de Padrões para Iniciação do Pix, formato EMV QRCPS-MPM). O mesmo texto vira o QR Code.
/// Estático = não passa por banco nem por internet: o cliente paga direto para a chave da loja e o operador
/// confere no app do banco antes de concluir a venda.
/// </summary>
public static class Pix
{
    public enum TipoChave { CpfCnpj, Celular, Email, Aleatoria }

    public static string Nome(TipoChave tipo) => tipo switch
    {
        TipoChave.CpfCnpj => "CPF ou CNPJ",
        TipoChave.Celular => "Celular",
        TipoChave.Email => "E-mail",
        _ => "Chave aleatória"
    };

    /// <summary>
    /// A chave no formato que vai dentro do código: CPF/CNPJ só com números (CNPJ pode ter letras), celular como
    /// +55DDNNNNNNNNN, e-mail em minúsculas, chave aleatória como está. null = chave inválida (motivo em <paramref name="erro"/>).
    /// </summary>
    public static string? NormalizarChave(TipoChave tipo, string? chave, out string erro)
    {
        erro = "";
        var c = (chave ?? "").Trim();
        if (c.Length == 0) { erro = "Informe a chave Pix."; return null; }
        switch (tipo)
        {
            case TipoChave.CpfCnpj:
            {
                var doc = Formatos.LimparDocumento(c);
                if (Formatos.DocumentoValido(doc)) return doc;
                erro = "CPF ou CNPJ inválido. Confira os números.";
                return null;
            }
            case TipoChave.Celular:
            {
                var d = Formatos.SoDigitos(c);
                if (d.Length is 12 or 13 && d.StartsWith("55", StringComparison.Ordinal)) d = d[2..];
                if (d.Length == 11 && d[2] == '9') return "+55" + d;
                erro = "Celular inválido. Use DDD + número com 9 dígitos (ex.: 11 91234-5678).";
                return null;
            }
            case TipoChave.Email:
            {
                var mail = c.ToLowerInvariant();
                if (mail.Length <= 77 && Formatos.EmailValido(mail)) return mail;
                erro = "E-mail inválido.";
                return null;
            }
            default:
            {
                if (Guid.TryParseExact(c, "D", out var g)) return g.ToString("D");
                erro = "Chave aleatória inválida. Ela tem 36 caracteres, como 123e4567-e89b-12d3-a456-426614174000.";
                return null;
            }
        }
    }

    /// <summary>
    /// Monta o código. <paramref name="chave"/> já normalizada; recebedor e cidade como no cadastro do banco
    /// (vão sem acentos e em maiúsculas, cortados em 25 e 15 letras, como o padrão pede).
    /// </summary>
    public static string Codigo(string chave, string recebedor, string cidade, decimal valor)
    {
        var conta = Campo("00", "br.gov.bcb.pix") + Campo("01", chave);
        var sb = new StringBuilder();
        sb.Append(Campo("00", "01"));                 // versão do payload
        sb.Append(Campo("26", conta));                // conta Pix (chave)
        sb.Append(Campo("52", "0000"));               // categoria do comerciante (não informada)
        sb.Append(Campo("53", "986"));                // moeda: real
        if (valor > 0) sb.Append(Campo("54", Math.Round(valor, 2).ToString("0.00", CultureInfo.InvariantCulture)));
        sb.Append(Campo("58", "BR"));
        sb.Append(Campo("59", Limpar(recebedor, 25)));
        sb.Append(Campo("60", Limpar(cidade, 15)));
        sb.Append(Campo("62", Campo("05", "***")));   // identificador: "***" = Pix estático sem identificador
        sb.Append("6304");                            // CRC16 a seguir
        sb.Append(Crc16(sb.ToString()).ToString("X4", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    private static string Campo(string id, string valor) =>
        id + valor.Length.ToString("00", CultureInfo.InvariantCulture) + valor;

    /// <summary>Sem acentos, só letras, números e espaços, em maiúsculas, no tamanho máximo do campo.</summary>
    public static string Limpar(string? texto, int maximo)
    {
        var semAcento = new StringBuilder();
        foreach (var ch in (texto ?? "").Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            semAcento.Append(char.IsAsciiLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : ' ');
        }
        var limpo = string.Join(' ', semAcento.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return limpo.Length <= maximo ? limpo : limpo[..maximo].TrimEnd();
    }

    /// <summary>CRC16-CCITT (polinômio 0x1021, início 0xFFFF), o que o padrão do Pix usa no campo 63.</summary>
    public static ushort Crc16(string texto)
    {
        ushort crc = 0xFFFF;
        foreach (var b in Encoding.UTF8.GetBytes(texto))
        {
            crc ^= (ushort)(b << 8);
            for (var i = 0; i < 8; i++)
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
        }
        return crc;
    }
}
