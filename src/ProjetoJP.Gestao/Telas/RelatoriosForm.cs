using ProjetoJP.Controles;
using System.Collections;
using ProjetoJP.Gestao.Servicos;
using ProjetoJP.Shared.Dtos;

namespace ProjetoJP.Gestao.Telas;

public class RelatoriosForm : Form
{
    private record Relatorio(string Nome, bool UsaPeriodo, Func<ApiClient, string, Task<IList>> Buscar, bool PedeCusto = false);

    private static readonly Relatorio[] Relatorios =
    [
        new("Vendas por dia", true, async (api, q) => await api.GetAsync<List<VendasPorDiaDto>>("api/relatorios/vendas-por-dia" + q)),
        new("Produtos mais vendidos", true, async (api, q) => await api.GetAsync<List<ProdutoMaisVendidoDto>>("api/relatorios/mais-vendidos" + q)),
        new("Lucro por produto", true, async (api, q) => await api.GetAsync<List<LucroProdutoDto>>("api/relatorios/lucro" + q), PedeCusto: true),
        new("Vendas por forma de pagamento", true, async (api, q) => await api.GetAsync<List<VendasPorFormaDto>>("api/relatorios/por-forma" + q)),
        new("Lista de vendas", true, async (api, q) => await api.GetAsync<List<VendaResumoDto>>("api/relatorios/vendas" + q)),
        new("Estoque baixo (abaixo do mínimo)", false, async (api, _) => await api.GetAsync<List<EstoqueBaixoDto>>("api/relatorios/estoque-baixo"))
    ];

    private readonly ApiClient _api;
    /// <summary>Os relatórios que este usuário pode ver (o de lucro só com "Ver preço de custo").</summary>
    private readonly Relatorio[] _relatorios;
    private readonly ComboBox _tipo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, Font = Ui.Fonte(10) };
    private readonly DateTimePicker _de = new() { Format = DateTimePickerFormat.Short, Width = 130 };
    private readonly DateTimePicker _ate = new() { Format = DateTimePickerFormat.Short, Width = 130 };
    private readonly DataGridView _grade = Ui.Grade();
    private readonly Label _resumo = Ui.Rotulo("", 11, FontStyle.Bold);

    public RelatoriosForm(ApiClient api)
    {
        _api = api;
        Text = "Relatórios";
        Size = new Size(1080, 680);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Ui.Fundo;
        Font = Ui.Fonte(10);
        Padding = new Padding(20, 16, 20, 20);

        _relatorios = Relatorios.Where(r => !r.PedeCusto || api.Pode(Shared.Permissoes.VerCusto)).ToArray();
        foreach (var r in _relatorios) _tipo.Items.Add(r.Nome);
        _tipo.SelectedIndex = 0;
        _de.Value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        _ate.Value = DateTime.Today;

        var barra = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, 56), Padding = new Padding(12, 12, 12, 0), BackColor = Tema.Superficie, WrapContents = true };
        var lde = Ui.Rotulo("de");
        lde.Margin = new Padding(12, 6, 4, 0);
        var late = Ui.Rotulo("até");
        late.Margin = new Padding(8, 6, 4, 0);
        var gerar = Ui.Botao("Gerar", icone: Icones.Relatorios);
        gerar.Margin = new Padding(16, 0, 8, 0);
        gerar.Click += async (_, _) => await Gerar();
        var exportar = Ui.Botao("Exportar CSV", Ui.Neutro, 140, icone: Icones.Exportar);
        exportar.Click += (_, _) => Ui.ExportarCsv(this, _grade, _tipo.Text);
        barra.Controls.AddRange([_tipo, lde, _de, late, _ate, gerar, exportar]);

        var rodape = new Panel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(14, 10, 0, 0) };
        rodape.Controls.Add(_resumo);

        var meio = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        _grade.Dock = DockStyle.Fill;
        // R$ nos totais, quantidades conforme o campo
        _grade.DataBindingComplete += (_, _) =>
        {
            var tipoLinha = _grade.DataSource?.GetType().GetGenericArguments().FirstOrDefault();
            if (tipoLinha is not null) Ui.AplicarFormatos(_grade, tipoLinha);
        };
        meio.Controls.Add(_grade);

        Controls.Add(meio);
        Controls.Add(rodape);
        Controls.Add(barra);

        _tipo.SelectedIndexChanged += (_, _) =>
        {
            var usa = _relatorios[_tipo.SelectedIndex].UsaPeriodo;
            _de.Enabled = _ate.Enabled = usa;
        };
    }

    private async Task Gerar()
    {
        if (_de.Value.Date > _ate.Value.Date) { Ui.Aviso(this, "A data inicial é maior que a final."); return; }
        var rel = _relatorios[_tipo.SelectedIndex];
        var q = $"?de={_de.Value:yyyy-MM-dd}&ate={_ate.Value:yyyy-MM-dd}";
        try
        {
            UseWaitCursor = true;
            var dados = await rel.Buscar(_api, q);
            _grade.DataSource = dados;
            _resumo.Text = Resumir(dados);
            if (dados.Count > 0 && dados[0] is LucroProdutoDto) _resumo.Text = ResumirLucro(dados.Cast<LucroProdutoDto>().ToList());
            if (dados.Count > 0 && dados[0] is VendaResumoDto)
            {
                // A lista mostra no máximo as mais recentes: total e quantidade vêm do período inteiro.
                var t = await _api.GetAsync<VendasTotaisDto>("api/relatorios/vendas-totais" + q);
                var cortada = t.Quantidade > dados.Count;
                _resumo.Text = (cortada
                                   ? $"{t.Quantidade:N0} venda(s) no período — mostrando as {dados.Count:N0} mais recentes"
                                   : $"{dados.Count} linha(s)") +
                               $"   •   Total: {t.Total.ToString("C2", Ui.Br)}" +
                               (t.Devolvido > 0 ? $" (já sem {t.Devolvido.ToString("C2", Ui.Br)} de devoluções)" : "") +
                               (t.Canceladas > 0 ? $"   •   {t.Canceladas} cancelada(s) fora do total" : "");
            }
        }
        catch (ApiException ex) { Ui.Erro(this, ex.Message); }
        finally { UseWaitCursor = false; }
    }

    /// <summary>Vendido, custo, lucro e margem do período, e quantos produtos estão sem custo cadastrado.</summary>
    private static string ResumirLucro(List<LucroProdutoDto> linhas)
    {
        var vendido = linhas.Sum(x => x.Vendido);
        var custo = linhas.Sum(x => x.Custo);
        var lucro = vendido - custo;
        var texto = $"Vendido: {vendido.ToString("C2", Ui.Br)}   •   Custo: {custo.ToString("C2", Ui.Br)}   •   " +
                    $"Lucro: {lucro.ToString("C2", Ui.Br)}" +
                    (vendido > 0 ? $" ({(lucro / vendido * 100).ToString("N1", Ui.Br)}%)" : "");
        var semCusto = linhas.Count(x => x.SemCusto);
        if (semCusto > 0) texto += $"   •   {semCusto} produto(s) sem custo: o lucro deles aparece maior que o real";
        return texto;
    }

    /// <summary>Soma a coluna "Total" (quando existe) e conta as linhas.</summary>
    private static string Resumir(IList dados)
    {
        if (dados.Count == 0) return "Nenhum registro no período.";
        var prop = dados[0]!.GetType().GetProperty("Total");
        var texto = $"{dados.Count} linha(s)";
        if (prop?.PropertyType == typeof(decimal))
        {
            var itens = dados.Cast<object>();
            // Na lista de vendas, canceladas não entram no total.
            if (dados[0] is VendaResumoDto)
                itens = itens.Where(x => ((VendaResumoDto)x).Status == Shared.StatusVenda.Concluida);
            var total = dados[0] is VendaResumoDto
                ? itens.Cast<VendaResumoDto>().Sum(x => x.Total - x.Devolvido)
                : itens.Sum(x => (decimal)prop.GetValue(x)!);
            texto += $"   •   Total: {total.ToString("C2", Ui.Br)}";
        }
        return texto;
    }
}
