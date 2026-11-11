namespace ProjetoJP.Shared.Dtos;

// Apenas a chave publica viaja para a API. A privada permanece no provedor CNG do Windows.
public sealed class RegistrarDispositivoRequest
{
    public string Nome { get; set; } = "";
    public string ChavePublica { get; set; } = "";
    public bool Tpm { get; set; }
}

public sealed class RegistrarDispositivoResposta
{
    public Guid Id { get; set; }
    public DateTime ExpiraEm { get; set; }
}

public sealed class LoginDispositivoRequest
{
    public Guid DispositivoId { get; set; }
    public string Login { get; set; } = "";
    public long Carimbo { get; set; }
    public string Assinatura { get; set; } = "";
}

public sealed class DispositivoResumoDto
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = "";
    public string Usuario { get; set; } = "";
    public bool Tpm { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime ExpiraEm { get; set; }
    public DateTime? UltimoUsoEm { get; set; }
    public bool Revogado { get; set; }
    public bool PrecisaRecadastro { get; set; }
}

public sealed class ConfirmarSenhaRequest
{
    public string Senha { get; set; } = "";
}

/// <summary>Mensagem assinada com ECDSA P-256. O carimbo e consumido atomicamente pela API.</summary>
public static class DispositivoAssinatura
{
    public static string Mensagem(Guid dispositivoId, string login, long carimbo) =>
        $"erp-nf/dispositivo/v1|{dispositivoId:N}|{login.Trim().ToLowerInvariant()}|{carimbo}";
}
