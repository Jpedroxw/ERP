namespace ProjetoJP.Shared.Dtos;

public sealed class AuditoriaResumoDto
{
    public Guid Id { get; set; }
    public DateTime Data { get; set; }
    public string Usuario { get; set; } = "";
    public string Acao { get; set; } = "";
    public string AlvoTipo { get; set; } = "";
    public Guid? AlvoId { get; set; }
    public string Resumo { get; set; } = "";
}

public sealed class SessaoResumoDto
{
    public Guid Id { get; set; }
    public string Usuario { get; set; } = "";
    public DateTime CriadaEm { get; set; }
    public DateTime ExpiraEm { get; set; }
    public bool Revogada { get; set; }
    public bool Atual { get; set; }
    public bool ComputadorLembrado { get; set; }
}

/// <summary>ERP Master: acessos de uma loja (sessões abertas, computadores lembrados e histórico de alterações).</summary>
public sealed class SegurancaLojaDto
{
    public List<SessaoResumoDto> Sessoes { get; set; } = [];
    public List<DispositivoResumoDto> Computadores { get; set; } = [];
    public List<AuditoriaResumoDto> Auditoria { get; set; } = [];
}
