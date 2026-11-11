using ProjetoJP.Controles;
using ProjetoJP.Pdv.Dados;

namespace ProjetoJP.Pdv.Telas;

/// <summary>
/// Lista das vendas que o servidor recusou (aberta pela etiqueta vermelha do rodapé): número, data, total e motivo.
/// Elas continuam guardadas no caixa; "Tentar enviar agora" manda de novo na próxima sincronização.
/// </summary>
public sealed class VendasRecusadasForm : Form
{
    /// <summary>true = o operador pediu para tentar de novo (quem abriu sincroniza na hora).</summary>
    public bool TentarAgora { get; private set; }

    public VendasRecusadasForm()
    {
        Ui.Dialogo(this, "Vendas recusadas pelo servidor", 820, 470);

        var titulo = new Label
        {
            Text = "Vendas que o servidor recusou", Font = Tema.Semibold(15), ForeColor = Tema.Texto,
            AutoSize = true, Location = new Point(24, 16)
        };
        var explicacao = new Label
        {
            Text = "Elas continuam guardadas neste caixa e não se perdem. O caixa tenta enviar de novo a cada 30 minutos.\n" +
                   "Corrija o motivo (na Gestão ou em Opções do caixa) e clique em \"Tentar enviar agora\".",
            Font = Tema.Fonte(9.5f), ForeColor = Tema.TextoSuave, AutoSize = true, MaximumSize = new Size(770, 0),
            Location = new Point(24, 50)
        };

        var grade = Ui.Grade();
        grade.SetBounds(24, 100, 772, 296);
        grade.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        grade.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        grade.Columns.Add(Ui.Coluna("Venda", 10, true));
        grade.Columns.Add(Ui.Coluna("Data", 17));
        grade.Columns.Add(Ui.Coluna("Total", 14, true));
        grade.Columns.Add(Ui.Coluna("Motivo", 59));

        List<VendaRecusadaLocal> vendas;
        try { vendas = Repositorio.VendasRecusadasDetalhe(); }
        catch (Exception ex)
        {
            vendas = [];
            explicacao.Text = "Não foi possível ler as vendas recusadas: " + ex.Message;
        }
        foreach (var v in vendas)
            grade.Rows.Add("nº " + v.Numero, Data(v.Data), Ui.Dinheiro((decimal)v.Total), v.Motivo ?? "");

        var tentar = Ui.Botao("Tentar enviar agora", largura: 210, altura: 40);
        tentar.Location = new Point(24, 412);
        tentar.Enabled = vendas.Count > 0;
        tentar.Click += (_, _) =>
        {
            try { Repositorio.LiberarRecusadasParaReenvio(); }
            catch (Exception ex)
            {
                Ui.Aviso(this, "Não foi possível liberar as vendas: " + ex.Message);
                return;
            }
            TentarAgora = true;
            DialogResult = DialogResult.OK;
        };
        var fechar = Ui.Botao("Fechar", Ui.Neutro, 140, 40);
        fechar.Location = new Point(656, 412);
        fechar.DialogResult = DialogResult.Cancel;

        Controls.AddRange([titulo, explicacao, grade, tentar, fechar]);
        CancelButton = fechar;
    }

    private static string Data(string iso)
    {
        try { return Db.ParaData(iso).ToLocalTime().ToString("dd/MM/yyyy HH:mm"); }
        catch (FormatException) { return iso; }
    }
}
