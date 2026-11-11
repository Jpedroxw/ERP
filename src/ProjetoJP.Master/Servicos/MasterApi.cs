using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;

namespace ProjetoJP.Master.Servicos;

public class MasterException(string mensagem) : Exception(mensagem);

/// <summary>Endereço do servidor e chave master, guardados criptografados para o usuário do Windows.</summary>
public class MasterConfig
{
    public string Servidor { get; set; } = "";
    public string Chave { get; set; } = "";

    private static string Caminho => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ErpMaster", "master.dat");

    private static readonly byte[] Entropia = Encoding.UTF8.GetBytes("ErpMaster.v1");

    public bool Configurado => Uri.TryCreate(Servidor, UriKind.Absolute, out _) && Chave.Length >= 32;

    public static MasterConfig Carregar()
    {
        try
        {
            if (!File.Exists(Caminho)) return new MasterConfig { Servidor = EnderecoServidor.UrlServidorPadrao };
            var aberto = ProtectedData.Unprotect(File.ReadAllBytes(Caminho), Entropia, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<MasterConfig>(aberto) ?? new MasterConfig();
        }
        catch (Exception)
        {
            return new MasterConfig { Servidor = EnderecoServidor.UrlServidorPadrao };
        }
    }

    public void Salvar()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Caminho)!);
        GravacaoAtomica.Bytes(Caminho, ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(this), Entropia, DataProtectionScope.CurrentUser));
    }
}

/// <summary>Chamadas aos endpoints /master da API (cabeçalho X-Master-Key).</summary>
public class MasterApi
{
    private readonly HttpClient _http;

    public MasterApi(MasterConfig cfg)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(EnderecoServidor.ParaUrlDeEndereco(cfg.Servidor)),
            Timeout = TimeSpan.FromSeconds(60) // criar o banco de um cliente leva alguns segundos
        };
        _http.DefaultRequestHeaders.Add("X-Master-Key", cfg.Chave);
    }

    public Uri Endereco => _http.BaseAddress!;

    /// <summary>Endereço que as lojas desse cliente usam (para conferir/copiar).</summary>
    public string EnderecoDoCliente(string codigo) => Endereco.ToString().TrimEnd('/') + "/" + codigo + "/";

    /// <summary>
    /// Testa a conexão e, se der 404, descobre o motivo: API antiga (sem o Master) ou endereço errado.
    /// </summary>
    public async Task TestarAsync()
    {
        MasterException erro;
        try
        {
            await Enviar(() => _http.GetAsync("master/health"));
            return;
        }
        catch (MasterException ex)
        {
            erro = ex;
        }

        // Só o 404 precisa de investigação; chave errada (401) ou sem chave (503) já vêm com a mensagem certa.
        if (!erro.Message.StartsWith("Não encontrado")) throw erro;

        if (await RespondeHealthAsync() == false)
            throw new MasterException(
                "Nesse endereço não tem a API do Projeto JP.\n\n" +
                "Confira o endereço: precisa terminar em /nf/ (ex.: https://erp.minhaempresa.com.br/nf/) " +
                "e o Nginx/HTTPS da VPS precisam estar configurados.");

        throw new MasterException(
            "A API da VPS está no ar, mas é a versão antiga (ainda sem o Projeto JP Master).\n\n" +
            "Mande a API nova (pasta publicar\\api) para /opt/erp-nf/api, rode o instalar-servidor.sh " +
            "para gerar a chave master e reinicie: systemctl restart erp-nf-api");
    }

    /// <summary>true = /health responde (API no ar); false = não responde; null = não deu para saber.</summary>
    private async Task<bool?> RespondeHealthAsync()
    {
        try
        {
            using var resp = await _http.GetAsync("health");
            return resp.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<List<ClienteMasterDto>> ClientesAsync() =>
        await Ler<List<ClienteMasterDto>>(await Enviar(() => _http.GetAsync("master/clientes")));

    public async Task<ClienteMasterDto> CriarClienteAsync(NovoClienteRequest req) =>
        await Ler<ClienteMasterDto>(await Enviar(() => _http.PostAsJsonAsync("master/clientes", req)));

    public async Task<ClienteMasterDto> AtualizarClienteAsync(Guid id, AtualizarClienteRequest req) =>
        await Ler<ClienteMasterDto>(await Enviar(() => _http.PutAsJsonAsync($"master/clientes/{id}", req)));

    /// <summary>Exclui o cliente e o banco dele. Devolve onde ficou a cópia de segurança, na VPS.</summary>
    public async Task<string> ExcluirClienteAsync(Guid id, string confirmar)
    {
        var resp = await Enviar(() => _http.DeleteAsync($"master/clientes/{id}?confirmar={Uri.EscapeDataString(confirmar)}"));
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return json.TryGetProperty("copia", out var c) ? c.GetString() ?? "" : "";
    }

    public Task ExcluirPedidoAsync(Guid id) => Enviar(() => _http.DeleteAsync($"master/pedidos/{id}"));

    public Task SenhaAdminAsync(Guid id, SenhaAdminRequest req) =>
        Enviar(() => _http.PostAsJsonAsync($"master/clientes/{id}/senha-admin", req));

    // ---------- Segurança de cada loja ----------

    public async Task<SegurancaLojaDto> SegurancaAsync(Guid id) =>
        await Ler<SegurancaLojaDto>(await Enviar(() => _http.GetAsync($"master/clientes/{id}/seguranca")));

    public Task EncerrarSessaoAsync(Guid id, Guid sessaoId) =>
        Enviar(() => _http.DeleteAsync($"master/clientes/{id}/sessoes/{sessaoId}"));

    public async Task<int> EncerrarTodasSessoesAsync(Guid id)
    {
        var resp = await Enviar(() => _http.PostAsync($"master/clientes/{id}/sessoes/encerrar-todas", null));
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return json.TryGetProperty("encerradas", out var n) && n.TryGetInt32(out var q) ? q : 0;
    }

    public Task RevogarComputadorAsync(Guid id, Guid dispositivoId) =>
        Enviar(() => _http.DeleteAsync($"master/clientes/{id}/dispositivos/{dispositivoId}"));

    public async Task<SuporteTecnicoStatusDto> SuporteTecnicoAsync(Guid id) =>
        await Ler<SuporteTecnicoStatusDto>(await Enviar(() => _http.GetAsync($"master/clientes/{id}/acesso-tecnico")));

    public Task DefinirSenhaSuporteAsync(Guid id, SuporteTecnicoSenhaRequest req) =>
        Enviar(() => _http.PostAsJsonAsync($"master/clientes/{id}/acesso-tecnico/senha", req));

    public Task HabilitarSuporteAsync(Guid id, bool ativo) =>
        Enviar(() => _http.PutAsJsonAsync($"master/clientes/{id}/acesso-tecnico/status",
            new SuporteTecnicoStatusRequest { Ativo = ativo }));

    /// <param name="situacao">0 = pendentes, 1 = aprovados, 2 = recusados, null = todos.</param>
    public async Task<List<PedidoCadastroDto>> PedidosAsync(int? situacao) =>
        await Ler<List<PedidoCadastroDto>>(await Enviar(() =>
            _http.GetAsync("master/pedidos" + (situacao is null ? "" : "?situacao=" + situacao))));

    public async Task<ClienteMasterDto> AprovarAsync(Guid id, AprovarPedidoRequest req) =>
        await Ler<ClienteMasterDto>(await Enviar(() => _http.PostAsJsonAsync($"master/pedidos/{id}/aprovar", req)));

    public Task RecusarAsync(Guid id, string motivo) =>
        Enviar(() => _http.PostAsJsonAsync($"master/pedidos/{id}/recusar", new RecusarPedidoRequest { Motivo = motivo }));

    private static async Task<T> Ler<T>(HttpResponseMessage resp) =>
        await resp.Content.ReadFromJsonAsync<T>() ?? throw new MasterException("Resposta vazia do servidor.");

    private static async Task<HttpResponseMessage> Enviar(Func<Task<HttpResponseMessage>> chamada)
    {
        HttpResponseMessage resp;
        try
        {
            resp = await chamada();
        }
        catch (TaskCanceledException)
        {
            throw new MasterException("O servidor não respondeu a tempo.");
        }
        catch (HttpRequestException ex)
        {
            throw new MasterException("Não conectou ao servidor: " + EnderecoServidor.ErroConexao(ex) + ".");
        }
        if (resp.IsSuccessStatusCode) return resp;

        var msg = "";
        try
        {
            var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
            if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("erro", out var e)) msg = e.GetString() ?? "";
        }
        catch
        {
            // corpo sem JSON
        }
        if (msg.Length == 0)
            msg = (int)resp.StatusCode switch
            {
                404 => "Não encontrado (a API do servidor está atualizada?).",
                _ => $"O servidor respondeu com erro {(int)resp.StatusCode}."
            };
        throw new MasterException(msg);
    }
}
