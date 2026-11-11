using ProjetoJP.Controles;
using ProjetoJP.Pdv.Dados;
using ProjetoJP.Pdv.Servicos;
using ProjetoJP.Shared;

namespace ProjetoJP.Pdv.Telas;

/// <summary>Vendas do caixa atual: reimprimir ou cancelar.</summary>
public class VendasCaixaForm : Form
{
    private readonly PdvConfig _cfg;
    private readonly CaixaLocal _caixa;
    private readonly UsuarioLocal _operador;
    private readonly string _terminal;
    private readonly DataGridView _grade = Ui.Grade();

    public bool HouveCancelamento { get; private set; }

    public VendasCaixaForm(PdvConfig cfg, CaixaLocal caixa, UsuarioLocal operador, string terminal)
    {
        _cfg = cfg;
        _caixa = caixa;
        _operador = operador;
        _terminal = terminal;
        Ui.Dialogo(this, "Vendas deste caixa", 760, 520);

        _grade.SetBounds(16, 16, 728, 420);
        _grade.Columns.Add(Ui.Coluna("Nº", 15));
        _grade.Columns.Add(Ui.Coluna("Hora", 25));
        _grade.Columns.Add(Ui.Coluna("Total", 30, true));
        _grade.Columns.Add(Ui.Coluna("Situação", 30));

        var reimprimir = Ui.Botao("Reimprimir", Ui.Escuro);
        reimprimir.Location = new Point(16, 456);
        reimprimir.Click += (_, _) => Reimprimir();
        var cancelar = Ui.Botao("Cancelar venda", Ui.Perigo, 170);
        cancelar.Location = new Point(180, 456);
        cancelar.Click += (_, _) => Cancelar();
        var fechar = Ui.Botao("Fechar", Ui.Neutro);
        fechar.Location = new Point(594, 456);
        fechar.DialogResult = DialogResult.Cancel;

        Controls.AddRange([_grade, reimprimir, cancelar, fechar]);
        CancelButton = fechar;
        Shown += (_, _) => Carregar();
    }

    private void Carregar()
    {
        _grade.Rows.Clear();
        foreach (var v in Repositorio.VendasDoCaixa(_caixa.Id))
        {
            var i = _grade.Rows.Add(
                v.Numero,
                Db.ParaData(v.Data).ToLocalTime().ToString("HH:mm:ss"),
                Ui.Dinheiro(v.Total),
                v.Status == (int)StatusVenda.Cancelada ? "CANCELADA" : "Concluída");
            _grade.Rows[i].Tag = v.Id;
            if (v.Status == (int)StatusVenda.Cancelada)
                _grade.Rows[i].DefaultCellStyle.ForeColor = Ui.Perigo;
        }
    }

    private string? IdSelecionado => _grade.CurrentRow?.Tag as string;

    private void Reimprimir()
    {
        if (IdSelecionado is not { } id) return;
        var vc = Repositorio.CarregarVenda(id);
        if (vc is null) return;
        var op = Repositorio.UsuarioPorId(vc.Venda.UsuarioId)?.Nome ?? "";
        var cli = vc.Venda.ClienteId is null ? null : Repositorio.ClientePorId(vc.Venda.ClienteId)?.Nome;
        var erro = Cupom.ImprimirVenda(_cfg, vc, op, cli, _terminal, segundaVia: true);
        if (erro is not null) Ui.Erro(this, "Falha na impressão: " + erro);
    }

    private void Cancelar()
    {
        if (IdSelecionado is not { } id) return;
        var vc = Repositorio.CarregarVenda(id);
        if (vc is null) return;
        if (vc.Venda.Status == (int)StatusVenda.Cancelada) { Ui.Aviso(this, "Essa venda já está cancelada."); return; }
        if (!Ui.Confirmar(this, $"Cancelar a venda nº {vc.Venda.Numero} de {Ui.Dinheiro(vc.Venda.Total)}?\nO estoque será devolvido.")) return;
        if (!AutorizacaoForm.Pedir(this, _operador, "Cancelamento de venda", Permissoes.CaixaCancelarVenda)) return;

        Repositorio.CancelarVenda(id);
        HouveCancelamento = true;
        Carregar();
    }
}

/// <summary>Fechamento: mostra o esperado por forma e compara com o dinheiro contado.</summary>
public class FechamentoForm : Form
{
    private readonly CampoMoeda _contado = new() { Font = Ui.Fonte(18) };
    private readonly Label _diferenca = Ui.Rotulo("", 13, FontStyle.Bold);
    private readonly ResumoCaixa _resumo;

    public decimal ValorContado { get; private set; }

    public FechamentoForm(ResumoCaixa resumo)
    {
        _resumo = resumo;
        Ui.Dialogo(this, "Fechamento de caixa", 460, 400);

        var y = 16;
        void Linha(string texto, decimal valor, bool negrito = false)
        {
            var estilo = negrito ? FontStyle.Bold : FontStyle.Regular;
            var l = Ui.Rotulo(texto, 11, estilo);
            l.Location = new Point(24, y);
            var v = Ui.Rotulo(Ui.Dinheiro(valor), 11, estilo);
            v.AutoSize = false;
            v.TextAlign = ContentAlignment.MiddleRight;
            v.SetBounds(240, y, 190, 24);
            Controls.AddRange([l, v]);
            y += 28;
        }

        Linha($"Vendas ({resumo.QuantidadeVendas})", resumo.TotalVendas, true);
        foreach (var f in resumo.PorForma) Linha("   " + f.Descricao, f.Valor);
        y += 10;
        Linha("Fundo de troco", resumo.Abertura);
        Linha("Suprimentos", resumo.Suprimentos);
        Linha("Sangrias", -resumo.Sangrias);
        Linha("Vendas em dinheiro", resumo.VendasDinheiro);
        Linha("Dinheiro esperado na gaveta", resumo.DinheiroEsperado, true);

        y += 10;
        var lc = Ui.Rotulo("Dinheiro contado na gaveta", 11);
        lc.Location = new Point(24, y);
        _contado.SetBounds(24, y + 26, 406, 40);
        _contado.Valor = resumo.DinheiroEsperado;
        _contado.TextChanged += (_, _) => AtualizarDiferenca();
        _diferenca.Location = new Point(24, y + 76);

        var ok = Ui.Botao("Fechar caixa", Ui.Perigo, 200, 48);
        ok.Location = new Point(24, y + 116);
        ok.Click += (_, _) =>
        {
            if (!Ui.Confirmar(this, $"Confirma o fechamento do caixa com {Ui.Dinheiro(_contado.Valor)} contados na gaveta?")) return;
            ValorContado = _contado.Valor;
            DialogResult = DialogResult.OK;
        };
        var voltar = Ui.Botao("Voltar", Ui.Neutro, 180, 48);
        voltar.Location = new Point(250, y + 116);
        ClientSize = new Size(460, y + 184);
        voltar.DialogResult = DialogResult.Cancel;

        Controls.AddRange([lc, _contado, _diferenca, ok, voltar]);
        CancelButton = voltar;
        AtualizarDiferenca();
        Shown += (_, _) => { _contado.Focus(); _contado.SelectAll(); };
    }

    private void AtualizarDiferenca()
    {
        var dif = _contado.Valor - _resumo.DinheiroEsperado;
        _diferenca.Text = dif == 0 ? "Caixa batendo" : (dif > 0 ? "Sobra: " : "Falta: ") + Ui.Dinheiro(Math.Abs(dif));
        _diferenca.ForeColor = dif == 0 ? Ui.Sucesso : Ui.Perigo;
    }
}
