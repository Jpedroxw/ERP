using System.Net;
using System.Net.Http;

namespace ProjetoJP.Shared;

/// <summary>
/// Endereço do servidor (API). Na configuração a pessoa digita só um NOME (ex.: nuvem) e o sistema sabe
/// para qual site ele aponta — o site fica guardado aqui dentro do programa e não aparece em tela,
/// mensagem nem arquivo de configuração. Para o usuário é só "Servidor".
/// </summary>
public static class EnderecoServidor
{
    /// <summary>
    /// Servidor que já vem no programa (instalação nova). Os demais — e trocas de site — são feitos na
    /// Configuração técnica (senha de suporte), que grava a lista criptografada no computador.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> NomesDeFabrica = new Dictionary<string, string>
    {
        ["nuvem"] = "https://erp.f84568e4.vps.hospedeaqui.cloud/nf/"
    };

    private static Dictionary<string, string> _nomes = Copiar(NomesDeFabrica);

    /// <summary>Lista em uso: nome → endereço da API.</summary>
    public static IReadOnlyDictionary<string, string> Nomes => _nomes;

    /// <summary>Nome sugerido numa instalação nova: o primeiro da lista.</summary>
    public static string NomePadrao => _nomes.Keys.FirstOrDefault() ?? "";

    /// <summary>Troca a lista em uso (chamado ao abrir o programa e ao salvar a Configuração técnica).</summary>
    public static void Definir(IEnumerable<KeyValuePair<string, string>> nomes)
    {
        var nova = Copiar(nomes);
        _nomes = nova.Count > 0 ? nova : Copiar(NomesDeFabrica);
    }

    /// <summary>Nome válido para servidor: letras, números, ponto não (para não parecer site), hífen e sublinhado.</summary>
    public static bool NomeValido(string? nome)
    {
        var c = Chave(nome);
        return c.Length is >= 2 and <= 30 && c.All(x => char.IsAsciiLetterOrDigit(x) || x is '-' or '_');
    }

    public static string NormalizarNome(string? nome) => Chave(nome);

    private static Dictionary<string, string> Copiar(IEnumerable<KeyValuePair<string, string>> origem)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (nome, url) in origem)
        {
            var chave = Chave(nome);
            var endereco = ParaUrlDeEndereco(url);
            if (chave.Length > 0 && endereco.Length > 0) d[chave] = endereco;
        }
        return d;
    }

    /// <summary>É um dos nomes conhecidos?</summary>
    public static bool EhNome(string? texto) => _nomes.ContainsKey(Chave(texto));

    /// <summary>Endereço → nome conhecido (para converter configurações antigas que guardavam o site).</summary>
    public static string? NomeDe(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var alvo = ParaUrl(url);
        return _nomes.FirstOrDefault(n => string.Equals(n.Value, alvo, StringComparison.OrdinalIgnoreCase)).Key;
    }

    /// <summary>Digitado parece um endereço (tem ponto, :// ou é localhost) e não um nome?</summary>
    public static bool PareceEndereco(string? texto)
    {
        var t = (texto ?? "").Trim();
        return t.Contains('.') || t.Contains("://") || t.StartsWith("localhost", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Configuração antiga (guardava o site inteiro em "ApiUrl"): troca pelo nome conhecido.
    /// Devolve true quando converteu — aí o arquivo deve ser regravado, já sem o site.
    /// </summary>
    public static bool MigrarAntigo(string json, string? servidorAtual, Action<string> definirServidor)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json, new System.Text.Json.JsonDocumentOptions
            {
                CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return false;
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (!p.Name.Equals("ApiUrl", StringComparison.OrdinalIgnoreCase)) continue;
                var antigo = p.Value.ValueKind == System.Text.Json.JsonValueKind.String ? p.Value.GetString() : null;
                if (string.IsNullOrWhiteSpace(servidorAtual) && !string.IsNullOrWhiteSpace(antigo) && !antigo.Contains("seu-dominio"))
                    definirServidor(NomeDe(antigo) ?? antigo);
                return true;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // arquivo estranho: fica como está
        }
        return false;
    }

    // Nome digitado em maiúsculas e com espaço ("MERCADO BOM PRECO") vale o mesmo que "mercado-bom-preco".
    private static string Chave(string? texto) =>
        System.Text.RegularExpressions.Regex.Replace(Formatos.RemoverAcentos((texto ?? "").Trim()).ToLowerInvariant(), @"\s+", "-");

    /// <summary>
    /// Nome do servidor como aparece na tela: maiúsculas com espaço ("mercado-bom-preco" → "MERCADO BOM PRECO",
    /// "nuvem" → "NUVEM"). Endereço de site (versões antigas) fica como está.
    /// </summary>
    public static string ParaTela(string? nome) =>
        string.IsNullOrWhiteSpace(nome) ? "" : PareceEndereco(nome) ? nome.Trim() : Chave(nome).Replace('-', ' ').ToUpperInvariant();

    /// <summary>Caminho da API atrás do Nginx (deploy/nginx-erp-nf.conf).</summary>
    public const string CaminhoPadrao = "/nf/";

    /// <summary>Texto do campo quando já existe um servidor salvo (o endereço fica escondido).</summary>
    public const string TextoConfigurado = "Servidor já configurado";

    /// <summary>
    /// Texto digitado → URL completa da API. Nome conhecido (nuvem) → o site dele.
    /// erp.loja.com.br → https://erp.loja.com.br/nf/ · 178.92.204.11 → http://178.92.204.11/nf/ ·
    /// localhost:5080 → http://localhost:5080/ (API direta, sem Nginx). URL completa fica como está.
    /// </summary>
    public static string ParaUrl(string? texto)
    {
        var chave = Chave(texto);
        if (_nomes.TryGetValue(chave, out var conhecido)) return conhecido;
        if (!PareceEndereco(texto) && Dtos.CodigoCliente.Valido(chave)) return UrlDoCliente(chave);
        return ParaUrlDeEndereco(texto);
    }

    /// <summary>
    /// Código de cliente (ex.: lojaabc) → o endereço desse cliente no servidor padrão
    /// (https://site/nf/lojaabc/). É assim que cada loja usa o seu banco sem cadastrar nada no computador.
    /// </summary>
    public static string UrlDoCliente(string codigo)
    {
        var baseUrl = UrlServidorPadrao;
        return baseUrl.Length == 0 ? "" : baseUrl + codigo + "/";
    }

    /// <summary>Servidor principal (o primeiro da lista): onde ficam os clientes e o pedido de cadastro.</summary>
    public static string UrlServidorPadrao => _nomes.Values.FirstOrDefault() ?? "";

    /// <summary>Completa um endereço digitado (site ou IP) até a URL da API.</summary>
    public static string ParaUrlDeEndereco(string? texto)
    {
        var t = (texto ?? "").Trim().Replace('\\', '/');
        if (t.Length == 0) return "";
        if (!PareceEndereco(t)) return ""; // nome que não existe

        if (!t.Contains("://"))
        {
            var host = t.Split('/', ':')[0];
            t = (EhLocalOuIp(host) ? "http://" : "https://") + t;
        }
        if (!Uri.TryCreate(t, UriKind.Absolute, out var uri)) return t;

        var caminho = uri.AbsolutePath;
        if (caminho is "" or "/")
            caminho = uri.IsDefaultPort ? CaminhoPadrao : "/"; // com porta própria = API direta
        if (!caminho.EndsWith('/')) caminho += "/";

        var porta = uri.IsDefaultPort ? "" : ":" + uri.Port;
        return $"{uri.Scheme}://{uri.Host}{porta}{caminho}";
    }

    /// <summary>Motivo da falha de conexão em palavras simples, sem mostrar o endereço.</summary>
    public static string ErroConexao(HttpRequestException ex) => ex.HttpRequestError switch
    {
        HttpRequestError.NameResolutionError => "endereço do servidor não encontrado (confira o que foi digitado ou a internet)",
        HttpRequestError.ConnectionError => "o servidor não aceitou a conexão (pode estar desligado ou sem internet)",
        HttpRequestError.SecureConnectionError => "problema no certificado HTTPS do servidor",
        _ when ex.StatusCode is { } s => $"o servidor respondeu com erro {(int)s}",
        _ => "sem conexão com o servidor"
    };

    private static bool EhLocalOuIp(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || IPAddress.TryParse(host, out _);
}
