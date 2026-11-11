using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;

namespace ProjetoJP.Gestao.Servicos;

public class ApiException(string mensagem) : Exception(mensagem);

/// <summary>O servidor respondeu e recusou o login (diferente de estar sem conexão).</summary>
public class LoginRecusadoException(string mensagem) : ApiException(mensagem);

/// <summary>O servidor pediu a senha de novo (alteração de usuários/terminais com sessão antiga ou de computador lembrado).</summary>
public class SenhaNecessariaException(string mensagem) : ApiException(mensagem);

/// <summary>Cliente HTTP da API. Guarda o token depois do login.</summary>
public class ApiClient
{
    private readonly HttpClient _http;

    public LoginResponse? Sessao { get; private set; }

    /// <summary>
    /// Chamado quando o servidor pede a senha para uma alteração sensível (usuários, terminais). A tela principal
    /// abre a janela "Confirmar senha"; se confirmar, o pedido é repetido sozinho. Sem isso, a pessoa teria que
    /// achar Configurações → Confirmar senha e tentar de novo.
    /// </summary>
    public Func<Task<bool>>? ConfirmarSenha { get; set; }
    public Perfil Perfil => Sessao?.Perfil ?? Perfil.Operador;
    public bool EhAdmin => Perfil == Perfil.Admin;
    public bool EhGerente => Perfil is Perfil.Admin or Perfil.Gerente;

    /// <summary>
    /// O usuário logado tem essa permissão? (veja <see cref="ProjetoJP.Shared.Permissoes"/>). Servidor antigo, que não manda
    /// a lista, vale o padrão do perfil. O servidor confere de novo em cada pedido.
    /// </summary>
    public bool Pode(string permissao)
    {
        if (Sessao is null) return false;
        if (EhAdmin) return true;
        // Lista vazia enviada pela API significa nenhuma permissão (não restaurar padrão).
        // O servidor continua sendo a autoridade final para cada requisição.
        return Sessao.Permissoes.Contains(permissao);
    }

    public ApiClient(string apiUrl)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(apiUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public async Task LoginAsync(string login, string senha)
    {
        using var resp = await Enviar(() => _http.PostAsJsonAsync("api/auth/login", new LoginRequest { Login = login, Senha = senha }), login: true);
        Sessao = await resp.Content.ReadFromJsonAsync<LoginResponse>() ?? throw new ApiException("Resposta vazia do servidor.");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Sessao.Token);
    }

    /// <summary>Entra com o computador lembrado: assina o pedido com a chave deste computador (sem senha).</summary>
    public async Task LoginDispositivoAsync(AcessoLembrado acesso)
    {
        var carimbo = await CarimboAsync();
        var req = new LoginDispositivoRequest
        {
            DispositivoId = acesso.DispositivoId,
            Login = acesso.Login,
            Carimbo = carimbo,
            Assinatura = acesso.Assinar(DispositivoAssinatura.Mensagem(acesso.DispositivoId, acesso.Login, carimbo))
        };
        using var resp = await Enviar(() => _http.PostAsJsonAsync("api/auth/dispositivo/login", req), login: true);
        Sessao = await resp.Content.ReadFromJsonAsync<LoginResponse>() ?? throw new ApiException("Resposta vazia do servidor.");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Sessao.Token);
    }

    /// <summary>Reconfirma a senha para criar/editar usuarios ou terminais durante 10 minutos.</summary>
    public async Task ConfirmarSenhaAsync(string senha)
    {
        using var resp = await Enviar(() => _http.PostAsJsonAsync("api/auth/confirmar-senha", new ConfirmarSenhaRequest { Senha = senha }), login: true);
        Sessao = await resp.Content.ReadFromJsonAsync<LoginResponse>() ?? throw new ApiException("Resposta vazia do servidor.");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Sessao.Token);
    }

    /// <summary>
    /// Encerra a sessão atual na API quando disponível e sempre descarta o token local.
    /// Não remove o computador lembrado: essa é uma opção separada em Configurações.
    /// </summary>
    public async Task EncerrarSessaoAsync()
    {
        try
        {
            using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            using var resposta = await _http.GetAsync("api/auth/sessoes", limite.Token);
            if (resposta.IsSuccessStatusCode)
            {
                var sessoes = await resposta.Content.ReadFromJsonAsync<List<SessaoResumoDto>>(cancellationToken: limite.Token);
                var atual = sessoes?.FirstOrDefault(s => s.Atual);
                if (atual is not null)
                {
                    using var revogacao = await _http.DeleteAsync($"api/auth/sessoes/{atual.Id}", limite.Token);
                }
            }
        }
        catch (Exception) { /* offline: o token nunca é salvo; limpa a sessão local mesmo assim */ }
        finally
        {
            Sessao = null;
            ConfirmarSenha = null;
            _http.DefaultRequestHeaders.Authorization = null;
        }
    }

    /// <summary>Hora do pedido pela hora do SERVIDOR (cabeçalho Date do /health): relógio do Windows errado não atrapalha.</summary>
    private async Task<long> CarimboAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            using var resp = await _http.GetAsync("health", cts.Token);
            if (resp.Headers.Date is { } data)
            {
                // O Date vem em segundos: guarda a diferença e usa o relógio local (em ms) corrigido.
                var diferenca = data.ToUnixTimeMilliseconds() + 500 - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + diferenca;
            }
        }
        catch (Exception) { /* usa o relógio local */ }
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <summary>Chama /health do servidor. true = respondeu (usado no indicador Online/Offline).</summary>
    public async Task<bool> ServidorOnlineAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            using var resp = await _http.GetAsync("health", cts.Token);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<T> GetAsync<T>(string url)
    {
        using var resp = await EnviarComSenha(() => _http.GetAsync(url));
        return await LerAsync<T>(resp);
    }

    public async Task<T> PostAsync<T>(string url, object corpo)
    {
        using var resp = await EnviarComSenha(() => _http.PostAsJsonAsync(url, corpo));
        return await LerAsync<T>(resp);
    }

    public async Task<T> PutAsync<T>(string url, object corpo)
    {
        using var resp = await EnviarComSenha(() => _http.PutAsJsonAsync(url, corpo));
        return await LerAsync<T>(resp);
    }

    /// <summary>
    /// Lê a resposta. Conexão que cai no meio, demora ou resposta que não é do sistema (ex.: página de erro do
    /// provedor) viram ApiException, que as telas já mostram como aviso, em vez de erro inesperado.
    /// </summary>
    private static async Task<T> LerAsync<T>(HttpResponseMessage resp)
    {
        try
        {
            return (await resp.Content.ReadFromJsonAsync<T>())!;
        }
        catch (TaskCanceledException)
        {
            throw new ApiException("O servidor demorou demais para responder. Tente de novo.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            throw new ApiException("A conexão caiu enquanto recebia os dados. Tente de novo.");
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
            throw new ApiException("O servidor respondeu algo inesperado. Confira se a API foi atualizada junto com a Gestão.");
        }
    }

    public async Task DeleteAsync(string url)
    {
        using var resp = await EnviarComSenha(() => _http.DeleteAsync(url));
    }

    /// <summary>Se o servidor pedir a senha, abre a confirmação e repete o mesmo pedido uma vez.</summary>
    private async Task<HttpResponseMessage> EnviarComSenha(Func<Task<HttpResponseMessage>> chamada)
    {
        try
        {
            return await Enviar(chamada);
        }
        catch (SenhaNecessariaException) when (ConfirmarSenha is not null)
        {
            if (!await ConfirmarSenha()) throw new ApiException("Alteração cancelada: é preciso confirmar a senha.");
            return await Enviar(chamada);
        }
    }

    private static async Task<HttpResponseMessage> Enviar(Func<Task<HttpResponseMessage>> chamada, bool login = false)
    {
        HttpResponseMessage resp;
        try
        {
            resp = await chamada();
        }
        catch (TaskCanceledException)
        {
            throw new ApiException("O servidor não respondeu a tempo. Verifique a internet.");
        }
        catch (HttpRequestException ex)
        {
            throw new ApiException("Não foi possível conectar ao servidor: " + EnderecoServidor.ErroConexao(ex) + ".");
        }

        if (resp.IsSuccessStatusCode) return resp;

        var (msg, codigoErro) = await LerErro(resp);
        var codigo = resp.StatusCode;
        resp.Dispose();
        throw codigo switch
        {
            HttpStatusCode.Forbidden when codigoErro == "confirmar_senha" => new SenhaNecessariaException(msg),
            HttpStatusCode.Unauthorized when !login => new ApiException("Sessão expirada. Feche e entre novamente."),
            HttpStatusCode.Unauthorized => new LoginRecusadoException(string.IsNullOrEmpty(msg) ? "Login recusado." : msg),
            HttpStatusCode.Forbidden => new ApiException(string.IsNullOrWhiteSpace(msg)
                ? "Seu perfil não tem permissão para esta função." : msg),
            HttpStatusCode.NotFound when string.IsNullOrEmpty(msg) => new ApiException("Registro não encontrado."),
            _ => new ApiException(string.IsNullOrEmpty(msg) ? $"Erro {(int)codigo} no servidor." : msg)
        };
    }

    /// <summary>Mensagem ("erro") e, quando houver, o código ("codigo") que a API manda junto com a recusa.</summary>
    private static async Task<(string msg, string? codigo)> LerErro(HttpResponseMessage resp)
    {
        try
        {
            var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
            if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("erro", out var e))
                return (e.GetString() ?? "", json.TryGetProperty("codigo", out var c) ? c.GetString() : null);
        }
        catch
        {
            // corpo vazio ou não-JSON
        }
        return ("", null);
    }
}
