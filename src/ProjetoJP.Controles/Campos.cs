using System.ComponentModel;
using ProjetoJP.Shared;

namespace ProjetoJP.Controles;

// Caixas de texto que só aceitam o formato certo enquanto a pessoa digita.
// Usadas pela Retaguarda e pelo caixa.

/// <summary>
/// Valor em R$ digitado como calculadora de banco: os números entram pela direita.
/// Digitar 1 2 3 4 mostra R$ 0,01 → R$ 0,12 → R$ 1,23 → R$ 12,34.
/// Backspace apaga o último dígito; Delete zera; com tudo selecionado, digitar recomeça do zero.
/// </summary>
public class CampoMoeda : TextBox
{
    private const int WmCut = 0x0300, WmPaste = 0x0302, WmClear = 0x0303;
    private static readonly long Limite = (long)(Formatos.MaxMoeda * 100m);
    private long _centavos;

    public CampoMoeda()
    {
        TextAlign = HorizontalAlignment.Right;
        Atualizar();
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public decimal Valor
    {
        get => _centavos / 100m;
        set
        {
            _centavos = (long)Math.Round(Math.Clamp(value, 0m, Formatos.MaxMoeda) * 100m);
            Atualizar();
        }
    }

    private bool TudoSelecionado => TextLength > 0 && SelectionLength == TextLength;

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (char.IsAsciiDigit(e.KeyChar))
        {
            if (TudoSelecionado) _centavos = 0;
            var novo = _centavos * 10 + (e.KeyChar - '0');
            if (novo <= Limite) _centavos = novo;
            Atualizar();
            e.Handled = true;
        }
        else if (e.KeyChar == '\b')
        {
            _centavos = TudoSelecionado ? 0 : _centavos / 10;
            Atualizar();
            e.Handled = true;
        }
        else if (!char.IsControl(e.KeyChar))
        {
            e.Handled = true; // letras, vírgula etc. não entram: a vírgula já é fixa
        }
        base.OnKeyPress(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Delete)
        {
            Valor = 0;
            e.Handled = e.SuppressKeyPress = true;
        }
        base.OnKeyDown(e);
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WmPaste:
                if (Clipboard.ContainsText() && Formatos.TryLerDecimal(Clipboard.GetText(), out var v) && v >= 0)
                    Valor = v;
                return;
            case WmCut:
                Copy();
                Valor = 0;
                return;
            case WmClear:
                Valor = 0;
                return;
        }
        base.WndProc(ref m);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        base.OnMouseUp(mevent);
        if (SelectionLength == 0) SelectionStart = TextLength; // cursor sempre no fim
    }

    private void Atualizar()
    {
        Text = Formatos.Moeda(_centavos / 100m);
        SelectionStart = TextLength;
    }
}

/// <summary>
/// Número com casas decimais fixas (quantidade, ordem...). Aceita só dígitos e uma vírgula;
/// o ponto vira vírgula. Ao sair do campo, completa as casas (3 → "3,000").
/// </summary>
public class CampoNumero : TextBox
{
    private const int WmPaste = 0x0302;
    private int _casas = 2;

    public CampoNumero()
    {
        TextAlign = HorizontalAlignment.Right;
        MaxLength = 16;
        Text = Formatar(0);
    }

    /// <summary>0 = só inteiros.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CasasDecimais
    {
        get => _casas;
        set
        {
            var atual = Valor;
            _casas = Math.Clamp(value, 0, 6);
            Text = Formatar(atual);
        }
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public decimal Maximo { get; set; } = Formatos.MaxQuantidade;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public decimal Valor
    {
        get => Formatos.TryLerDecimal(Text, out var v) ? Math.Round(Math.Clamp(v, 0m, Maximo), _casas) : 0m;
        set => Text = Formatar(value);
    }

    private string Formatar(decimal v) =>
        Math.Round(Math.Clamp(v, 0m, Maximo), _casas).ToString("F" + _casas, Formatos.Br);

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        var c = e.KeyChar;
        if (c == '.') c = ',';

        if (char.IsAsciiDigit(c) || c == ',')
        {
            var depois = Text.Remove(SelectionStart, SelectionLength).Insert(SelectionStart, c.ToString());
            var virgula = depois.IndexOf(',');
            var invalido =
                (c == ',' && (_casas == 0 || depois.Count(x => x == ',') > 1)) ||
                (virgula >= 0 && depois.Length - virgula - 1 > _casas);

            if (invalido)
            {
                e.Handled = true;
            }
            else if (c != e.KeyChar)
            {
                // ponto digitado: insere a vírgula no lugar
                SelectedText = ",";
                e.Handled = true;
            }
        }
        else if (!char.IsControl(c))
        {
            e.Handled = true;
        }
        base.OnKeyPress(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmPaste)
        {
            if (Clipboard.ContainsText() && Formatos.TryLerDecimal(Clipboard.GetText(), out var v) && v >= 0)
                Valor = v;
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        BeginInvoke(new Action(SelectAll)); // depois do clique do mouse, para a seleção valer
    }

    protected override void OnLeave(EventArgs e)
    {
        Text = Formatar(Valor);
        base.OnLeave(e);
    }
}

/// <summary>
/// Texto com máscara montada enquanto digita: CPF/CNPJ, telefone ou só dígitos (código de barras).
/// <see cref="Limpo"/> devolve sem pontuação, que é como vai para o servidor.
/// </summary>
public class CampoFormatado : TextBox
{
    private const int WmPaste = 0x0302;
    private readonly Func<string, string>? _formatar;
    private readonly int _maximo;
    private readonly bool _letras;
    private bool _formatando;

    /// <param name="formatar">Monta a máscara a partir do texto limpo (null = sem máscara).</param>
    /// <param name="maximo">Máximo de caracteres limpos.</param>
    /// <param name="aceitaLetras">CNPJ alfanumérico: aceita letras (sempre maiúsculas).</param>
    public CampoFormatado(Func<string, string>? formatar, int maximo, bool aceitaLetras = false)
    {
        _formatar = formatar;
        _maximo = maximo;
        _letras = aceitaLetras;
        if (aceitaLetras) CharacterCasing = CharacterCasing.Upper;
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Limpo => Limpar(Text);

    private string Limpar(string s)
    {
        var limpo = _letras ? Formatos.LimparDocumento(s) : Formatos.SoDigitos(s);
        if (_letras)
        {
            // A mesma regra da digitação vale para o que é colado: letra só nas 12 primeiras posições
            // (os 2 dígitos verificadores do CNPJ são sempre números).
            var sb = new System.Text.StringBuilder(limpo.Length);
            foreach (var ch in limpo)
                if (char.IsAsciiDigit(ch) || sb.Length < 12) sb.Append(ch);
            limpo = sb.ToString();
        }
        return limpo.Length > _maximo ? limpo[.._maximo] : limpo;
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        var c = e.KeyChar;
        // CNPJ alfanumérico (Receita Federal, desde jul/2026): letra só nas 12 primeiras posições;
        // os 2 dígitos verificadores são sempre números. CPF nunca tem letra (letra = é CNPJ).
        var posicao = Limpar(Text[..Math.Min(SelectionStart, TextLength)]).Length;
        var aceito = char.IsAsciiDigit(c) || (_letras && char.IsAsciiLetter(c) && posicao < 12);

        if (aceito)
        {
            // sem espaço para mais um caractere (e nada selecionado para ser substituído)
            if (SelectionLength == 0 && Limpo.Length >= _maximo) e.Handled = true;
        }
        else if (!char.IsControl(c))
        {
            e.Handled = true;
        }
        base.OnKeyPress(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmPaste)
        {
            if (Clipboard.ContainsText()) Text = Limpar(Clipboard.GetText());
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        if (!_formatando)
        {
            _formatando = true;
            var limpo = Limpar(Text);
            var formatado = _formatar?.Invoke(limpo) ?? limpo;
            if (formatado != Text)
            {
                Text = formatado;
                SelectionStart = TextLength;
            }
            _formatando = false;
        }
        base.OnTextChanged(e);
    }
}

/// <summary>Login: só minúsculas, números, ponto, hífen e sublinhado (também ao colar).</summary>
public class CampoLogin : TextBox
{
    public CampoLogin()
    {
        CharacterCasing = CharacterCasing.Lower;
        MaxLength = 60;
    }

    private static bool Permitido(char c) => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '-' or '_';

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        var c = char.ToLowerInvariant(e.KeyChar);
        if (!char.IsControl(c) && !Permitido(c)) e.Handled = true;
        base.OnKeyPress(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Colagem.WmPaste)
        {
            Colagem.Colar(this, Permitido);
            return;
        }
        base.WndProc(ref m);
    }
}

/// <summary>
/// Nome do servidor (o código da empresa, ex.: lojaabc): minúsculas, números e hífen, até 30 (também ao colar).
/// </summary>
/// <summary>
/// Nome do servidor (empresa): aparece e é digitado em MAIÚSCULAS COM ESPAÇO ("MERCADO BOM PRECO"). Por dentro
/// continua o código de sempre, em minúsculas com hífen ("mercado-bom-preco"): use <see cref="Codigo"/>.
/// Hífen ou sublinhado digitado vira espaço.
/// </summary>
public class CampoNomeServidor : TextBox
{
    public CampoNomeServidor()
    {
        CharacterCasing = CharacterCasing.Upper;
        MaxLength = 30;
    }

    /// <summary>O código (minúsculas com hífen) do que está no campo; ao definir, mostra em maiúsculas com espaço.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Codigo
    {
        get => ProjetoJP.Shared.Dtos.CodigoCliente.DaTela(Text);
        set => Text = ProjetoJP.Shared.Dtos.CodigoCliente.ParaTela(value);
    }

    private static bool Permitido(char c) => char.IsAsciiLetterOrDigit(c) || c == ' ';

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (e.KeyChar is '-' or '_') e.KeyChar = ' ';
        e.KeyChar = char.ToUpperInvariant(e.KeyChar);
        if (!char.IsControl(e.KeyChar) && !Permitido(e.KeyChar)) e.Handled = true;
        base.OnKeyPress(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Colagem.WmPaste)
        {
            if (Clipboard.ContainsText())
            {
                var limpo = new string(Formatos.RemoverAcentos(Clipboard.GetText().Trim()).ToUpperInvariant()
                    .Select(c => c is '-' or '_' ? ' ' : c).Where(Permitido).ToArray());
                var cabe = Math.Max(0, MaxLength - (TextLength - SelectionLength));
                SelectedText = limpo.Length <= cabe ? limpo : limpo[..cabe];
            }
            return;
        }
        base.WndProc(ref m);
    }
}

/// <summary>Colar (Ctrl+V / menu) deixando passar só os caracteres aceitos pelo campo.</summary>
internal static class Colagem
{
    public const int WmPaste = 0x0302;

    public static void Colar(TextBox caixa, Func<char, bool> permitido)
    {
        if (!Clipboard.ContainsText()) return;
        var limpo = new string(Clipboard.GetText().Trim().ToLowerInvariant().Where(permitido).ToArray());
        var cabe = Math.Max(0, caixa.MaxLength - (caixa.TextLength - caixa.SelectionLength));
        caixa.SelectedText = limpo.Length <= cabe ? limpo : limpo[..cabe];
    }
}

/// <summary>Texto livre: mantém acentos, mas rejeita colagem de controles invisíveis.</summary>
public class CampoTextoSeguro : TextBox
{
    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        // Só bloqueia caracteres invisíveis de formatação. Os de controle NÃO: Ctrl+C, Ctrl+V, Ctrl+X, Ctrl+Z e
        // Ctrl+A chegam aqui como caracteres de controle e, se bloqueados, copiar/colar pelo teclado para de funcionar.
        // O que é colado já passa pelo filtro do WM_PASTE abaixo.
        if (char.GetUnicodeCategory(e.KeyChar) == System.Globalization.UnicodeCategory.Format)
            e.Handled = true;
        base.OnKeyPress(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Colagem.WmPaste && Clipboard.ContainsText())
        {
            var texto = new string(Clipboard.GetText().Where(c =>
                (!char.IsControl(c) || (Multiline && (c is '\r' or '\n'))) &&
                char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format).ToArray());
            var cabe = Math.Max(0, MaxLength - (TextLength - SelectionLength));
            SelectedText = texto.Length <= cabe ? texto : texto[..cabe];
            return;
        }
        base.WndProc(ref m);
    }
}

/// <summary>Código de produto: permite colar e digitar apenas caracteres aceitos pela API.</summary>
public class CampoCodigoProduto : TextBox
{
    public CampoCodigoProduto() { CharacterCasing = CharacterCasing.Upper; MaxLength = 30; }
    private static bool Aceito(char c) => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or '/';
    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (!char.IsControl(e.KeyChar) && !Aceito(e.KeyChar)) e.Handled = true;
        base.OnKeyPress(e);
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Colagem.WmPaste && Clipboard.ContainsText())
        {
            var texto = new string(Clipboard.GetText().ToUpperInvariant().Where(Aceito).ToArray());
            var cabe = Math.Max(0, MaxLength - (TextLength - SelectionLength));
            SelectedText = texto.Length <= cabe ? texto : texto[..cabe];
            return;
        }
        base.WndProc(ref m);
    }
}

public static class Campos
{
    /// <summary>Cria o controle certo para o tipo do campo.</summary>
    public static Control Criar(TipoCampo tipo, int tamanho = 0) => tipo switch
    {
        TipoCampo.Moeda => new CampoMoeda(),
        TipoCampo.Quantidade => new CampoNumero { CasasDecimais = 3 },
        TipoCampo.Inteiro => new CampoNumero { CasasDecimais = 0, Maximo = 999_999 },
        TipoCampo.CpfCnpj => new CampoFormatado(Formatos.FormatarCpfCnpj, 14, aceitaLetras: true),
        TipoCampo.Telefone => new CampoFormatado(Formatos.FormatarTelefone, 11),
        TipoCampo.CodigoBarras => new CampoFormatado(null, 14),
        TipoCampo.Login => new CampoLogin(),
        TipoCampo.CodigoProduto => new CampoCodigoProduto(),
        TipoCampo.Unidade => CriarUnidade(),
        TipoCampo.Maiusculas => new CampoTextoSeguro { CharacterCasing = CharacterCasing.Upper, MaxLength = tamanho > 0 ? tamanho : 32767 },
        TipoCampo.Email => new CampoTextoSeguro { CharacterCasing = CharacterCasing.Lower, MaxLength = tamanho > 0 ? tamanho : 120 },
        _ => new CampoTextoSeguro { MaxLength = tamanho > 0 ? tamanho : 32767 }
    };

    private static ComboBox CriarUnidade()
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var u in Formatos.Unidades) combo.Items.Add(u);
        combo.SelectedIndex = 0;
        return combo;
    }
}
