namespace ProjetoJP.Shared;

// Atributos lidos pela Retaguarda para montar as telas de cadastro automaticamente.
// Também valem os atributos padrão de System.ComponentModel:
//   [DisplayName]          -> rótulo do campo / título da coluna
//   [Browsable(false)]     -> não aparece em lugar nenhum
//   [ReadOnly(true)]       -> aparece desabilitado na edição
//   [PasswordPropertyText] -> campo de senha (oculto na grade)

/// <summary>Formato do campo na digitação, na grade e na validação.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class CampoAttribute(TipoCampo tipo) : Attribute
{
    public TipoCampo Tipo { get; } = tipo;
    /// <summary>Máximo de caracteres (0 = sem limite).</summary>
    public int Tamanho { get; set; }
    public bool Obrigatorio { get; set; }
    /// <summary>Texto de ajuda mostrado ao lado do rótulo na tela de edição.</summary>
    public string? Dica { get; set; }
}

/// <summary>O campo é escolhido numa lista carregada do endpoint (itens com Id e Nome).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class LookupAttribute(string endpoint) : Attribute
{
    public string Endpoint { get; } = endpoint;
}

/// <summary>Aparece só na grade, nunca na tela de edição.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SomenteGradeAttribute : Attribute;

/// <summary>Coluna que aparece só no modo completo da Retaguarda (no simples, a lista mostra só o essencial).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class DetalheAttribute : Attribute;

/// <summary>Campo de texto com várias linhas.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class MultilinhaAttribute : Attribute;
