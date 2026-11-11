namespace ProjetoJP.Controles;

/// <summary>
/// Janela que não passa da tela. Em monitores pequenos (1366×768, comum nos caixas) uma janela mais alta que a área
/// livre ficava com os botões de baixo escondidos atrás da barra de tarefas. Ao abrir, ela é reduzida à área livre
/// (com uma folga) e recentralizada; se o conteúdo é de posição fixa, ganha barra de rolagem.
/// </summary>
public static class JanelaNaTela
{
    /// <summary>Liga a regra: vale quando a janela abrir (Load), já com o tamanho final.</summary>
    public static void Limitar(Form f) => f.Load += (_, _) => Ajustar(f);

    private static void Ajustar(Form f)
    {
        if (f.WindowState != FormWindowState.Normal || !f.TopLevel) return;
        var area = Screen.FromControl(f.Owner ?? f).WorkingArea;
        var largura = Math.Min(f.Width, area.Width - 16);
        var altura = Math.Min(f.Height, area.Height - 16);
        if (largura == f.Width && altura == f.Height) return;

        // O mínimo da janela não pode ser maior que a tela.
        f.MinimumSize = new Size(Math.Min(f.MinimumSize.Width, largura), Math.Min(f.MinimumSize.Height, altura));
        // Conteúdo em posição fixa: rola em vez de ficar cortado. (Painéis encaixados só encolhem: a rolagem não pesa.)
        f.AutoScroll = true;
        f.Size = new Size(largura, altura);
        f.StartPosition = FormStartPosition.Manual;
        var centro = f.Owner is { } dono
            ? new Point(dono.Left + (dono.Width - largura) / 2, dono.Top + (dono.Height - altura) / 2)
            : new Point(area.Left + (area.Width - largura) / 2, area.Top + (area.Height - altura) / 2);
        f.Location = new Point(
            Math.Clamp(centro.X, area.Left, Math.Max(area.Left, area.Right - largura)),
            Math.Clamp(centro.Y, area.Top, Math.Max(area.Top, area.Bottom - altura)));
    }
}
