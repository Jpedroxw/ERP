using ProjetoJP.Controles;
using ProjetoJP.Pdv.Dados;

namespace ProjetoJP.Pdv.Telas;

/// <summary>
/// Recebimento da venda: aceita várias formas (ex.: parte no PIX, parte em dinheiro).
/// Troco só sai de forma que permite troco (dinheiro).
/// Teclas: 1..9 escolhem a forma, Enter lança, Delete remove, F2 finaliza, Esc volta.
/// </summary>
public class PagamentoForm : Form
{
    private readonly decimal _total;
    private readonly List<FormaLocal> _formas;
    private readonly List<(FormaLocal forma, decimal valor)> _lancados = [];

    private readonly ListBox _lista = new() { Font = Ui.Fonte(13), BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false };
    private readonly CampoMoeda _valor = new() { Font = Ui.Fonte(20) };
    private readonly DataGridView _grade = Ui.Grade();
    private readonly Label _lblTotal = Ui.Rotulo("", 22, FontStyle.Bold);
    private readonly Label _lblPago = Ui.Rotulo("", 14);
    private readonly Label _lblFalta = Ui.Rotulo("", 18, FontStyle.Bold);
    private readonly Button _finalizar = Ui.Botao("F2 — Finalizar", largura: 220, altura: 52);
    private readonly Keys _teclaFinalizar;

    /// <summary>Pagamentos com o troco já descontado (soma = total da venda).</summary>
    public List<(FormaLocal forma, decimal valor)> Pagamentos { get; } = [];
    public decimal Troco { get; private set; }

    public PagamentoForm(decimal total, List<FormaLocal> formas, Keys teclaFinalizar = Keys.None)
    {
        _teclaFinalizar = teclaFinalizar;
        _total = total;
        _formas = formas;
        Ui.Dialogo(this, "Pagamento", 760, 470);

        var lt = Ui.Rotulo("Total da venda", 11);
        lt.Location = new Point(20, 16);
        _lblTotal.Location = new Point(18, 38);
        _lblTotal.Text = Ui.Dinheiro(total);

        var lf = Ui.Rotulo("Forma de pagamento", 10);
        lf.Location = new Point(20, 100);
        _lista.SetBounds(20, 124, 300, 220);
        for (var i = 0; i < formas.Count; i++)
            _lista.Items.Add($"{(i < 9 ? (i + 1).ToString() : " ")} - {formas[i].Descricao}");
        if (formas.Count > 0) _lista.SelectedIndex = 0;

        var lv = Ui.Rotulo("Valor recebido", 10);
        lv.Location = new Point(20, 354);
        _valor.SetBounds(20, 378, 300, 44);

        _grade.SetBounds(340, 124, 400, 180);
        _grade.Columns.Add(Ui.Coluna("Forma", 60));
        _grade.Columns.Add(Ui.Coluna("Valor", 40, true));

        _lblPago.Location = new Point(340, 314);
        _lblFalta.Location = new Point(340, 344);

        _finalizar.Location = new Point(520, 400);
        _finalizar.Click += (_, _) => Finalizar();
        var voltar = Ui.Botao("Esc — Voltar", Ui.Neutro, 160, 52);
        voltar.Location = new Point(350, 400);
        voltar.DialogResult = DialogResult.Cancel;

        Controls.AddRange([lt, _lblTotal, lf, _lista, lv, _valor, _grade, _lblPago, _lblFalta, _finalizar, voltar]);
        CancelButton = voltar;

        _valor.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { Lancar(); e.SuppressKeyPress = true; }
        };
        _lista.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { _valor.Focus(); _valor.SelectAll(); e.SuppressKeyPress = true; }
        };
        _grade.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete && _grade.CurrentRow is { } r)
            {
                _lancados.RemoveAt(r.Index);
                Atualizar();
            }
        };

        Shown += (_, _) => { Atualizar(); _valor.Focus(); _valor.SelectAll(); };
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // F2, ou a "outra tecla para finalizar" das Opções do caixa (se tiver; ex.: a tecla TOTAL do teclado de automação).
        if (keyData == Keys.F2 || (_teclaFinalizar != Keys.None && keyData == _teclaFinalizar)) { Finalizar(); return true; }

        // Atalho numérico só quando o foco não está no campo de valor.
        if (!_valor.Focused && keyData is >= Keys.D1 and <= Keys.D9 or >= Keys.NumPad1 and <= Keys.NumPad9)
        {
            var n = keyData >= Keys.NumPad1 ? keyData - Keys.NumPad1 : keyData - Keys.D1;
            if (n < _formas.Count)
            {
                _lista.SelectedIndex = n;
                _valor.Focus();
                _valor.SelectAll();
                return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private decimal Pago => _lancados.Sum(x => x.valor);
    private decimal Falta => Math.Max(0, _total - Pago);

    private void Lancar()
    {
        if (_lista.SelectedIndex < 0) { Ui.Aviso(this, "Escolha a forma de pagamento."); return; }
        var v = _valor.Valor;
        if (v <= 0) { Ui.Aviso(this, "Informe o valor recebido."); _valor.Focus(); return; }

        var forma = _formas[_lista.SelectedIndex];
        if (!forma.PermiteTroco && v > Falta)
        {
            Ui.Aviso(this, $"{forma.Descricao} não permite troco. O valor máximo é {Ui.Dinheiro(Falta)}.");
            return;
        }

        _lancados.Add((forma, v));
        Atualizar();

        if (Falta == 0) Finalizar();
        else { _lista.Focus(); }
    }

    private void Atualizar()
    {
        _grade.Rows.Clear();
        foreach (var (forma, valor) in _lancados)
            _grade.Rows.Add(forma.Descricao, Ui.Dinheiro(valor));

        _lblPago.Text = "Recebido: " + Ui.Dinheiro(Pago);
        if (Pago >= _total)
        {
            _lblFalta.Text = "Troco: " + Ui.Dinheiro(Pago - _total);
            _lblFalta.ForeColor = Ui.Sucesso;
        }
        else
        {
            _lblFalta.Text = "Falta: " + Ui.Dinheiro(Falta);
            _lblFalta.ForeColor = Ui.Perigo;
        }
        _finalizar.Enabled = Pago >= _total && _lancados.Count > 0;
        _valor.Valor = Falta;
    }

    private void Finalizar()
    {
        if (Pago < _total || _lancados.Count == 0) return;

        var troco = Pago - _total;
        var aceitaTroco = _lancados.Where(x => x.forma.PermiteTroco).Sum(x => x.valor);
        if (troco > aceitaTroco)
        {
            Ui.Aviso(this, "O troco é maior que o valor recebido em dinheiro. Revise os pagamentos.");
            return;
        }

        // Desconta o troco das formas que aceitam troco, da última para a primeira.
        var restante = troco;
        var liquidos = _lancados.Select(x => (x.forma, x.valor)).ToList();
        for (var i = liquidos.Count - 1; i >= 0 && restante > 0; i--)
        {
            if (!liquidos[i].forma.PermiteTroco) continue;
            var abate = Math.Min(restante, liquidos[i].valor);
            liquidos[i] = (liquidos[i].forma, liquidos[i].valor - abate);
            restante -= abate;
        }

        Pagamentos.Clear();
        Pagamentos.AddRange(liquidos.Where(x => x.valor > 0));
        Troco = troco;
        DialogResult = DialogResult.OK;
    }
}
