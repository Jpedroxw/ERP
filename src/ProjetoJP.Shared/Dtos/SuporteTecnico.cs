namespace ProjetoJP.Shared.Dtos;

/// <summary>O Master mostra somente o status; nunca recupera ou exibe a senha atual.</summary>
public sealed class SuporteTecnicoStatusDto
{
    public bool Configurado { get; set; }
    public bool Ativo { get; set; }
    public string Login { get; set; } = "suporte";
    public DateTime? AtualizadoEm { get; set; }
}

/// <summary>Redefine a senha técnica de uma empresa; aceita somente a nova senha.</summary>
public sealed class SuporteTecnicoSenhaRequest
{
    public string Login { get; set; } = "suporte";
    public string NovaSenha { get; set; } = "";
}

public sealed class SuporteTecnicoStatusRequest
{
    public bool Ativo { get; set; }
}

/// <summary>Usado exclusivamente para autorizar acesso às configurações técnicas do PDV.</summary>
public sealed class SuporteTecnicoValidarRequest
{
    public string Login { get; set; } = "";
    public string Senha { get; set; } = "";
}
