using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;

namespace ProjetoJP.Gestao.Servicos;

/// <summary>
/// Pedido de cadastro de uma empresa nova, feito pela própria Retaguarda (sem login).
/// Vai para o servidor principal e aparece no ERP Master para aprovar.
/// </summary>
public static class CadastroEmpresa
{
    private static HttpClient Novo() => new()
    {
        BaseAddress = new Uri(EnderecoServidor.UrlServidorPadrao),
        Timeout = TimeSpan.FromSeconds(30)
    };

    public static bool ServidorConfigurado => Uri.TryCreate(EnderecoServidor.UrlServidorPadrao, UriKind.Absolute, out _);

    public static async Task<DisponibilidadeDto> DisponivelAsync(string codigo)
    {
        using var http = Novo();
        var resp = await Enviar(() => http.GetAsync("cadastro/disponivel?codigo=" + Uri.EscapeDataString(codigo)));
        return await resp.Content.ReadFromJsonAsync<DisponibilidadeDto>() ?? new DisponibilidadeDto();
    }

    public static async Task<PedidoCadastroResposta> EnviarAsync(PedidoCadastroRequest pedido)
    {
        using var http = Novo();
        var resp = await Enviar(() => http.PostAsJsonAsync("cadastro", pedido));
        return await resp.Content.ReadFromJsonAsync<PedidoCadastroResposta>()
               ?? throw new ApiException("Resposta vazia do servidor.");
    }

    /// <summary>Situação do pedido. null = pedido não existe mais (ou código secreto errado).</summary>
    public static async Task<SituacaoCadastroDto?> SituacaoAsync(Guid id, string token)
    {
        using var http = Novo();
        HttpResponseMessage resp;
        try
        {
            resp = await http.GetAsync($"cadastro/{id}?token={Uri.EscapeDataString(token)}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ApiException("Sem conexão com o servidor.");
        }
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        if (!resp.IsSuccessStatusCode) throw new ApiException($"O servidor respondeu com erro {(int)resp.StatusCode}.");
        return await resp.Content.ReadFromJsonAsync<SituacaoCadastroDto>();
    }

    private static async Task<HttpResponseMessage> Enviar(Func<Task<HttpResponseMessage>> chamada)
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

        if (resp.StatusCode == HttpStatusCode.TooManyRequests)
            throw new ApiException("Muitas tentativas a partir desta internet. Tente de novo mais tarde.");
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
        throw new ApiException(msg.Length > 0 ? msg : $"O servidor respondeu com erro {(int)resp.StatusCode}.");
    }
}
