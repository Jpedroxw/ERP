using ProjetoJP.Controles;
using ProjetoJP.Gestao.Servicos;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;

namespace ProjetoJP.Gestao.Telas;

/// <summary>
/// O que o usuário pode ver e fazer, na Retaguarda e no caixa. Vale na hora na Retaguarda (o servidor confere
/// a cada pedido) e no caixa depois da próxima sincronização (uns 30 segundos).
/// </summary>
public sealed class PermissoesForm : Form
{
    private readonly ApiClient _api;
    private readonly Guid _usuarioId;
    private readonly Dictionary<string, CheckBox> _marcas = new(StringComparer.Ordinal);
    private readonly Label _situacao = new() { AutoSize = false, Font = Tema.Fonte(9.5f), ForeColor = Tema.TextoSuave };
    private readonly BotaoTema _salvar = Ui.Botao("Salvar", largura: 140, altura: 38, icone: Icones.Salvar);
    private readonly BotaoTema _padrao = Ui.Botao("Voltar ao padrão do perfil", Ui.Neutro, 240, 38, Icones.Atualizar);
    private PermissoesUsuarioDto? _dados;

    public PermissoesForm(ApiClient api, Guid usuarioId, string nome)
    {
        _api = api;
        _usuarioId = usuarioId;
        Ui.Dialogo(this, "Permissões — " + nome, 860, 560);

        var titulo = new Label { Text = "O que " + nome + " pode ver e fazer", Font = Tema.Semibold(15), ForeColor = Tema.Texto, AutoSize = true, Location = new Point(24, 16) };
        _situacao.SetBounds(26, 50, 800, 40);

        GroupBox Grupo(string texto, string grupo, int x)
        {
            var quadro = new GroupBox { Text = texto, Font = Tema.Semibold(10.5f), ForeColor = Tema.Texto, Location = new Point(x, 96), Size = new Size(400, 370) };
            var lista = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(8, 6, 8, 6) };
            foreach (var p in Permissoes.Todas.Where(p => p.Grupo == grupo))
            {
                var marca = new CheckBox { Text = p.Descricao, AutoSize = true, Font = Tema.Fonte(10), ForeColor = Tema.Texto, Margin = new Padding(0, 4, 0, 4) };
                _marcas[p.Chave] = marca;
                lista.Controls.Add(marca);
            }
            quadro.Controls.Add(lista);
            return quadro;
        }
        var retaguarda = Grupo("Gestão", Permissoes.GrupoRetaguarda, 24);
        var doCaixa = Grupo("Caixa (sem marcar = pede a senha de quem pode)", Permissoes.GrupoCaixa, 436);

        _salvar.Location = new Point(24, 490);
        _salvar.Click += async (_, _) => await SalvarAsync(padrao: false);
        _padrao.Location = new Point(174, 490);
        _padrao.Click += async (_, _) => await SalvarAsync(padrao: true);
        var fechar = Ui.Botao("Fechar", Ui.Neutro, 120, 38);
        fechar.Location = new Point(716, 490);
        fechar.DialogResult = DialogResult.Cancel;

        Controls.AddRange([titulo, _situacao, retaguarda, doCaixa, _salvar, _padrao, fechar]);
        CancelButton = fechar;
        Habilitar(false);
        Shown += async (_, _) => await CarregarAsync();
    }

    private void Habilitar(bool sim)
    {
        foreach (var m in _marcas.Values) m.Enabled = sim;
        _salvar.Enabled = sim;
        _padrao.Enabled = sim;
    }

    private void Mostrar(PermissoesUsuarioDto d)
    {
        _dados = d;
        foreach (var (chave, marca) in _marcas) marca.Checked = d.Permissoes.Contains(chave);
        var admin = d.Perfil == Perfil.Admin;
        Habilitar(!admin);
        _situacao.Text = admin
            ? "Admin sempre pode tudo. Para limitar, mude o perfil do usuário para Gerente ou Operador (em Usuários → Editar)."
            : $"Perfil {d.Perfil}. " + (d.Personalizado
                ? "Permissões personalizadas para este usuário."
                : "Usando o padrão do perfil. Marque ou desmarque e salve para personalizar.") +
              " Na Gestão vale na hora; no caixa, depois da próxima sincronização.";
    }

    private async Task CarregarAsync()
    {
        try
        {
            Mostrar(await _api.GetAsync<PermissoesUsuarioDto>($"api/usuarios/{_usuarioId}/permissoes"));
        }
        catch (ApiException ex)
        {
            if (IsDisposed) return;
            _situacao.Text = ex.Message;
            _situacao.ForeColor = Tema.Perigo;
        }
    }

    private async Task SalvarAsync(bool padrao)
    {
        if (_dados is null) return;
        var marcadas = _marcas.Where(m => m.Value.Checked).Select(m => m.Key).ToList();
        if (padrao && !Ui.Confirmar(this, $"Voltar as permissões ao padrão do perfil {_dados.Perfil}?")) return;
        try
        {
            Habilitar(false);
            var r = await _api.PutAsync<PermissoesUsuarioDto>($"api/usuarios/{_usuarioId}/permissoes",
                new PermissoesUsuarioRequest { Permissoes = padrao ? null : marcadas });
            if (IsDisposed) return;
            Mostrar(r);
            Ui.Info(this, "Permissões salvas.");
        }
        catch (ApiException ex)
        {
            if (IsDisposed) return;
            Ui.Erro(this, ex.Message);
            Habilitar(_dados.Perfil != Perfil.Admin);
        }
    }
}
