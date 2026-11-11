using System.Text.Json;
using System.Text.Json.Serialization;
using ProjetoJP.Shared;

namespace ProjetoJP.Gestao.Servicos;

/// <summary>
/// Endereço do servidor, gravado em C:\ProgramData\ErpNaoFiscal\retaguarda.json.
/// Fica fora da pasta do programa para não se perder ao atualizar o .exe.
/// </summary>
public class RetaguardaConfig
{
    /// <summary>Nome do servidor digitado na configuração (ex.: nuvem). O site fica dentro do programa.</summary>
    public string Servidor { get; set; } = "";

    /// <summary>Endereço da API, calculado pelo nome. Não é gravado no arquivo.</summary>
    [JsonIgnore]
    public string ApiUrl => EnderecoServidor.ParaUrl(Servidor);


    /// <summary>Pedido de cadastro aguardando aprovação (feito por "Criar cadastro da empresa").</summary>
    public Guid? PedidoId { get; set; }
    public string? PedidoToken { get; set; }

    /// <summary>Modo da tela principal. Vazio = automático (completo para gerente/admin, simples para operador).</summary>
    public bool? ModoCompleto { get; set; }

    private static string Pasta => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ErpNaoFiscal");

    public static string Caminho => Path.Combine(Pasta, "retaguarda.json");


    public bool Configurado =>
        Uri.TryCreate(ApiUrl, UriKind.Absolute, out var u) && u.Scheme is "http" or "https";

    public static RetaguardaConfig Carregar()
    {
        // Só a configuração deste computador (C:\ProgramData\ErpNaoFiscal). Um retaguarda.json antigo ao lado do .exe
        // não é mais lido: ele ia junto ao copiar a pasta do programa.
        foreach (var caminho in new[] { Caminho })
        {
            try
            {
                if (File.Exists(caminho))
                {
                    var texto = File.ReadAllText(caminho);
                    var cfg = JsonSerializer.Deserialize<RetaguardaConfig>(texto,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                    var migrado = EnderecoServidor.MigrarAntigo(texto, cfg.Servidor, s => cfg.Servidor = s);
                    if (migrado)
                    {
                        try { cfg.Salvar(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    }
                    return cfg;
                }
            }
            catch (JsonException)
            {
                // arquivo corrompido: pede o endereço de novo
            }
        }
        return new RetaguardaConfig();
    }

    public void Salvar()
    {
        Directory.CreateDirectory(Pasta);
        GravacaoAtomica.Texto(Caminho, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Nome do servidor (nuvem) ou endereço digitado → endereço da API (veja EnderecoServidor).</summary>
    public static string Normalizar(string? url) => EnderecoServidor.ParaUrl(url);
}
