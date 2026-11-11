namespace ProjetoJP.Pdv.Servicos;

/// <summary>O que uma tecla faz na tela de venda.</summary>
public enum AcaoCaixa
{
    Ajuda, Quantidade, Buscar, Cliente, AlterarItem, Desconto,
    Forma1, Forma2, Forma3, Forma4, Forma5, Forma6, ProximaForma,
    Finalizar, RemoverItem,
    Vendas, Orcamentos, Sincronizar, Opcoes, PesoBalanca, Reimprimir, Rapidos
}

/// <summary>
/// Teclas do caixa. Enter (com a busca vazia) abre o pagamento e, no pagamento, conclui a venda: o jeito mais fácil
/// para quem não está acostumado. O resto da venda fica em teclas simples (F1 a F12 e Del), sem Ctrl — o que um teclado de
/// automação manda com uma tecla só. Cada caixa pode trocar qualquer tecla em Opções → Teclado (pdv.json).
/// </summary>
public static class TeclasCaixa
{
    public sealed record Definicao(AcaoCaixa Acao, string Nome, Keys Padrao, bool Frequente);

    public static readonly IReadOnlyList<Definicao> Todas =
    [
        new(AcaoCaixa.Ajuda, "Mais opções (todas as ações e teclas)", Keys.F1, true),
        new(AcaoCaixa.Quantidade, "Quantidade", Keys.F2, true),
        new(AcaoCaixa.Buscar, "Buscar produto", Keys.F3, true),
        new(AcaoCaixa.Cliente, "Cliente da venda", Keys.F4, true),
        new(AcaoCaixa.AlterarItem, "Alterar quantidade/preço do item", Keys.F5, true),
        new(AcaoCaixa.Desconto, "Desconto", Keys.F6, true),
        new(AcaoCaixa.Forma1, "1ª forma de pagamento", Keys.F7, true),
        new(AcaoCaixa.Forma2, "2ª forma de pagamento", Keys.F8, true),
        new(AcaoCaixa.Forma3, "3ª forma de pagamento", Keys.F9, true),
        new(AcaoCaixa.Forma4, "4ª forma de pagamento", Keys.F10, true),
        new(AcaoCaixa.Forma5, "5ª forma de pagamento", Keys.F11, true),
        new(AcaoCaixa.Forma6, "6ª forma de pagamento (ou \"Outros\")", Keys.F12, true),
        new(AcaoCaixa.RemoverItem, "Remover item", Keys.Delete, true),
        new(AcaoCaixa.Rapidos, "Botões rápidos (pão, salgados, itens sem código)", Keys.Insert, true),
        new(AcaoCaixa.Finalizar, "Outra tecla para pagar/concluir (o Enter já faz)", Keys.None, false),
        new(AcaoCaixa.ProximaForma, "Próxima forma de pagamento", Keys.None, false),
        new(AcaoCaixa.Vendas, "Vendas do caixa (reimprimir/cancelar)", Keys.Control | Keys.L, false),
        new(AcaoCaixa.Orcamentos, "Orçamentos", Keys.Control | Keys.R, false),
        new(AcaoCaixa.Sincronizar, "Sincronizar agora", Keys.Control | Keys.S, false),
        new(AcaoCaixa.Opcoes, "Opções do caixa", Keys.Control | Keys.O, false),
        new(AcaoCaixa.PesoBalanca, "Ler o peso da balança de novo", Keys.Control | Keys.B, false),
        new(AcaoCaixa.Reimprimir, "Reimprimir o cupom da última venda", Keys.Control | Keys.P, false)
    ];

    /// <summary>Tecla da ação neste caixa (a escolhida nas Opções, ou a padrão). Keys.None = sem tecla.</summary>
    public static Keys Tecla(PdvConfig cfg, AcaoCaixa acao)
    {
        if (cfg.Teclas.TryGetValue(acao.ToString(), out var texto) && int.TryParse(texto, out var n))
        {
            var k = (Keys)n;
            return k == Keys.None || Permitida(k) ? k : Padrao(acao);
        }
        return Padrao(acao);
    }

    public static Keys Padrao(AcaoCaixa acao) => Todas.First(d => d.Acao == acao).Padrao;

    /// <summary>Tecla → ação. Se duas ações tiverem a mesma tecla, vale a primeira da lista.</summary>
    public static Dictionary<Keys, AcaoCaixa> Mapa(PdvConfig cfg)
    {
        var mapa = new Dictionary<Keys, AcaoCaixa>();
        foreach (var d in Todas)
        {
            var k = Tecla(cfg, d.Acao);
            if (k != Keys.None) mapa.TryAdd(k, d.Acao);
        }
        return mapa;
    }

    /// <summary>
    /// Teclas que não podem virar atalho: as que se digitam na busca (letras, números, espaço, * , . -),
    /// Enter, Esc, setas, Tab e Backspace. Com Ctrl ou Alt, letras e números podem.
    /// </summary>
    public static bool Permitida(Keys tecla)
    {
        var codigo = tecla & Keys.KeyCode;
        var ctrlOuAlt = (tecla & (Keys.Control | Keys.Alt)) != 0;
        if (codigo is Keys.None or Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return false;
        if (codigo is Keys.Enter or Keys.Escape or Keys.Tab or Keys.Back or Keys.Up or Keys.Down or Keys.Left or Keys.Right) return false;
        if (ctrlOuAlt) return true;
        if (codigo is >= Keys.F1 and <= Keys.F24) return true;
        return codigo is Keys.Delete or Keys.Insert or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown or Keys.Pause
            or Keys.Add or Keys.Subtract or Keys.Divide;
    }

    /// <summary>Como a tecla aparece na tela: "F7", "Ctrl+L", "Del", "Alt+1"...</summary>
    public static string Texto(Keys tecla)
    {
        if (tecla == Keys.None) return "";
        var partes = new List<string>();
        if ((tecla & Keys.Control) != 0) partes.Add("Ctrl");
        if ((tecla & Keys.Alt) != 0) partes.Add("Alt");
        if ((tecla & Keys.Shift) != 0) partes.Add("Shift");
        var codigo = tecla & Keys.KeyCode;
        partes.Add(codigo switch
        {
            Keys.Delete => "Del",
            Keys.Insert => "Ins",
            Keys.PageUp => "PgUp",
            Keys.PageDown => "PgDn",
            Keys.Add => "+ (teclado numérico)",
            Keys.Subtract => "− (teclado numérico)",
            Keys.Divide => "/ (teclado numérico)",
            >= Keys.D0 and <= Keys.D9 => ((int)(codigo - Keys.D0)).ToString(),
            >= Keys.NumPad0 and <= Keys.NumPad9 => "Num " + (int)(codigo - Keys.NumPad0),
            _ => codigo.ToString()
        });
        return string.Join("+", partes);
    }

    /// <summary>"Cliente (F4)" — ou só "Cliente" se a ação estiver sem tecla.</summary>
    public static string ComTecla(PdvConfig cfg, string texto, AcaoCaixa acao)
    {
        var t = Texto(Tecla(cfg, acao));
        return t.Length == 0 ? texto : $"{texto} ({t})";
    }
}
