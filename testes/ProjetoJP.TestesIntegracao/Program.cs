using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ProjetoJP.Shared.Dtos;

// NUNCA usar em empresa real. A conta de teste deve estar no banco /bot.../.
var url = Environment.GetEnvironmentVariable("ERP_TEST_URL")?.Trim();
var login = Environment.GetEnvironmentVariable("ERP_TEST_LOGIN")?.Trim();
if (!Uri.TryCreate(url, UriKind.Absolute, out var endereco) ||
    endereco.Scheme != Uri.UriSchemeHttps ||
    !(endereco.AbsolutePath.TrimEnd('/').Split('/').Last().StartsWith("bot", StringComparison.OrdinalIgnoreCase)))
{
    Console.WriteLine("Configure ERP_TEST_URL com a URL HTTPS de uma empresa fictícia cujo código começa por bot.");
    return 2;
}
if (string.IsNullOrWhiteSpace(login))
{
    Console.WriteLine("Configure ERP_TEST_LOGIN com o administrador da empresa fictícia.");
    return 2;
}
Console.Write("Senha da empresa de testes (não é salva): ");
var senha = new System.Text.StringBuilder();
while (true)
{
    var k = Console.ReadKey(true);
    if (k.Key == ConsoleKey.Enter) break;
    if (k.Key == ConsoleKey.Backspace) { if (senha.Length > 0) senha.Length--; continue; }
    if (!char.IsControl(k.KeyChar)) senha.Append(k.KeyChar);
}
Console.WriteLine();
using var http = new HttpClient { BaseAddress = new Uri(endereco.ToString().TrimEnd('/') + "/"),
    Timeout = TimeSpan.FromSeconds(20) };
int erros = 0;
void Exigir(string rotulo, bool ok)
{
    Console.WriteLine($"[{(ok ? "OK" : "FALHOU")}] {rotulo}");
    if (!ok) erros++;
}
async Task<HttpResponseMessage> Requisitar(string metodo, string endpoint, string? token = null,
    object? corpo = null)
{
    using var req = new HttpRequestMessage(new HttpMethod(metodo), endpoint);
    if (!string.IsNullOrEmpty(token)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    if (corpo != null) req.Content = JsonContent.Create(corpo);
    return await http.SendAsync(req);
}
try
{
    using (var invalido = await Requisitar("POST", "api/auth/login", corpo: new LoginRequest
           { Login = login, Senha = senha + "-senha-incorreta" }))
        Exigir("Senha inválida é recusada", invalido.StatusCode == HttpStatusCode.Unauthorized);

    using var autenticado = await Requisitar("POST", "api/auth/login", corpo: new LoginRequest { Login = login, Senha = senha.ToString() });
    Exigir("Login válido", autenticado.IsSuccessStatusCode);
    if (!autenticado.IsSuccessStatusCode) return 1;
    var principal = await autenticado.Content.ReadFromJsonAsync<LoginResponse>();
    if (principal is null) return 1;

    using (var confirmacaoErrada = await Requisitar("POST", "api/auth/confirmar-senha", principal.Token,
           new ConfirmarSenhaRequest { Senha = "senha-incorreta-para-teste" }))
        Exigir("Confirmação de senha errada é recusada", confirmacaoErrada.StatusCode == HttpStatusCode.Unauthorized);

    using (var confirmacao = await Requisitar("POST", "api/auth/confirmar-senha", principal.Token,
           new ConfirmarSenhaRequest { Senha = senha.ToString() }))
        Exigir("Confirmação de senha correta", confirmacao.IsSuccessStatusCode);

    // Guarde o identificador exato da sessão que este teste acabou de criar,
    // para não encerrar outra sessão existente do administrador de testes.
    using var primeiraLista = await Requisitar("GET", "api/auth/sessoes", principal.Token);
    Exigir("Identificar primeira sessão", primeiraLista.IsSuccessStatusCode);
    if (!primeiraLista.IsSuccessStatusCode) return 1;
    var primeiraSessao = (await primeiraLista.Content.ReadFromJsonAsync<List<SessaoResumoDto>>() ?? [])
        .FirstOrDefault(x => x.Atual);
    if (primeiraSessao is null) return 1;

    using var segundo = await Requisitar("POST", "api/auth/login", corpo: new LoginRequest { Login = login, Senha = senha.ToString() });
    Exigir("Segunda sessão criada", segundo.IsSuccessStatusCode);
    if (!segundo.IsSuccessStatusCode) return 1;
    var outra = await segundo.Content.ReadFromJsonAsync<LoginResponse>();
    if (outra is null) return 1;
    using var lista = await Requisitar("GET", "api/auth/sessoes", outra.Token);
    Exigir("Listar sessões", lista.IsSuccessStatusCode);
    if (!lista.IsSuccessStatusCode) return 1;
    var sessoes = await lista.Content.ReadFromJsonAsync<List<SessaoResumoDto>>() ?? [];
    // A sessão atual é a do segundo login; a primeira é revogada para testar o bloqueio.
    var alvo = sessoes.FirstOrDefault(x => x.Id == primeiraSessao.Id && !x.Revogada);
    Exigir("Encontrar sessão de teste anterior", alvo is not null);
    if (alvo is null) return 1;
    using (var exclusao = await Requisitar("DELETE", $"api/auth/sessoes/{alvo.Id}", outra.Token))
        Exigir("Encerrar sessão", exclusao.IsSuccessStatusCode);
    using (var bloqueada = await Requisitar("GET", "api/auth/sessoes", principal.Token))
        Exigir("Sessão revogada é bloqueada", bloqueada.StatusCode == HttpStatusCode.Unauthorized);
    using (var ativa = await Requisitar("GET", "api/auth/sessoes", outra.Token))
        Exigir("Outra sessão continua ativa", ativa.IsSuccessStatusCode);
}
catch (Exception ex)
{
    Console.WriteLine("Falha de comunicação ou execução: " + ex.GetType().Name + " - " + ex.Message);
    erros++;
}
Console.WriteLine($"Resultado: {erros} falhas.");
return erros == 0 ? 0 : 1;
