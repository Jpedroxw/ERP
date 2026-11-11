using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProjetoJP.Pdv.Dados;
using ProjetoJP.Shared;

namespace ProjetoJP.Pdv;

/// <summary>
/// Configuração do caixa, feita pela tela de configurações (não precisa editar arquivo).
/// Fica em C:\ProgramData\ErpNaoFiscal\pdv.json, junto do banco local,
/// para não se perder ao atualizar o .exe.
/// </summary>
public class PdvConfig
{
    /// <summary>Nome do servidor digitado na configuração (ex.: nuvem). O site fica dentro do programa.</summary>
    public string Servidor { get; set; } = "";

    /// <summary>Endereço da API, calculado pelo nome. Não é gravado no arquivo.</summary>
    [JsonIgnore]
    public string ApiUrl => EnderecoServidor.ParaUrl(Servidor);

    /// <summary>
    /// Chave do terminal (a que libera a sincronização). Em memória fica aberta; no arquivo vai
    /// criptografada pelo Windows (DPAPI) em "TerminalKeyProtegida", que só abre neste computador.
    /// </summary>
    [JsonIgnore]
    public string TerminalKey { get; set; } = "";

    /// <summary>A chave criptografada, como fica gravada no pdv.json.</summary>
    [JsonPropertyName("TerminalKeyProtegida"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TerminalKeyProtegida { get; set; }

    /// <summary>Versões antigas gravavam a chave aberta ("TerminalKey"). Só é lida, para migrar; nunca mais gravada.</summary>
    [JsonPropertyName("TerminalKey"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TerminalKeyAntiga { get; set; }

    private static readonly byte[] Entropia = Encoding.UTF8.GetBytes("ErpNaoFiscal.Pdv.TerminalKey.v1");
    public string NomeLoja { get; set; } = "MINHA LOJA";
    public List<string> CabecalhoCupom { get; set; } = [];

    /// <summary>Nome da impressora no Windows. Vazio = salva o cupom em .txt.</summary>
    public string? Impressora { get; set; }
    /// <summary>48 ou 42 para bobina 80 mm, 32 para 58 mm.</summary>
    public int ColunasCupom { get; set; } = 48;
    /// <summary>Página de código do Windows usada para converter acentos (850 ou 860).</summary>
    public int PaginaCodigo { get; set; } = 850;
    /// <summary>Tabela ESC/POS correspondente (ESC t n): 2 = PC850, 3 = PC860.</summary>
    public byte TabelaEscPos { get; set; } = 2;
    /// <summary>Impressoras que não imprimem acentos: tira os acentos do texto.</summary>
    public bool SemAcentos { get; set; }
    public bool CortarPapel { get; set; } = true;
    /// <summary>Impressora comum (A4) para o orçamento em folha. Vazio = a padrão do Windows.</summary>
    public string? ImpressoraA4 { get; set; }
    /// <summary>O que imprimir ao salvar um orçamento: 0 = nada, 1 = cupom (térmica), 2 = folha A4.</summary>
    public int ImpressaoOrcamento { get; set; } = 1;
    public bool AbrirGaveta { get; set; }
    public int IntervaloSyncSegundos { get; set; } = 30;
    /// <summary>
    /// Pasta da rede da loja para a cópia de segurança do banco do caixa (ex.: \\RETAGUARDA\CopiasCaixa).
    /// Vazio = sem cópia. Protege as vendas que ainda não subiram se este computador estragar.
    /// </summary>
    public string PastaCopiaSeguranca { get; set; } = "";
    /// <summary>Teclas trocadas em Opções → Teclado: nome da ação → código da tecla (0 = sem tecla). Vazio = padrão.</summary>
    public Dictionary<string, string> Teclas { get; set; } = new();

    /// <summary>
    /// Códigos dos produtos dos botões rápidos, na ordem dos botões (até 16). Vazio = automático: os produtos sem
    /// código de barras mais vendidos neste caixa.
    /// </summary>
    public List<string> BotoesRapidos { get; set; } = [];

    /// <summary>Pix com QR Code no pagamento: tipo da chave (CpfCnpj, Celular, Email ou Aleatoria).</summary>
    public string PixTipoChave { get; set; } = nameof(Pix.TipoChave.CpfCnpj);
    /// <summary>Chave Pix da loja, como foi digitada. Vazio = sem QR Code (o Pix continua como forma comum).</summary>
    public string PixChave { get; set; } = "";
    /// <summary>Nome de quem recebe, como está no banco (vai no QR Code).</summary>
    public string PixRecebedor { get; set; } = "";
    public string PixCidade { get; set; } = "";

    [JsonIgnore]
    public Pix.TipoChave PixTipo => Enum.TryParse<Pix.TipoChave>(PixTipoChave, out var t) ? t : Pix.TipoChave.CpfCnpj;

    /// <summary>A chave pronta para o código do Pix; null = não configurada ou inválida.</summary>
    [JsonIgnore]
    public string? PixChaveNormalizada => Pix.NormalizarChave(PixTipo, PixChave, out _);

    /// <summary>Tem chave, nome e cidade: o pagamento em Pix mostra o QR Code com o valor.</summary>
    [JsonIgnore]
    public bool PixConfigurado => PixChaveNormalizada is not null &&
                                  Pix.Limpar(PixRecebedor, 25).Length > 0 && Pix.Limpar(PixCidade, 15).Length > 0;

    /// <summary>Lê etiqueta EAN-13 da balança (código começa com 2).</summary>
    public bool BalancaEtiqueta { get; set; }
    /// <summary>Quantos dígitos do código do produto vêm depois do 2. 4, 5 ou 6.</summary>
    public int BalancaDigitosCodigo { get; set; } = 6;
    /// <summary>true = o miolo da etiqueta é o preço total; false = é o peso em kg.</summary>
    public bool BalancaEtiquetaPreco { get; set; }
    /// <summary>Porta USB/serial (COM3) ou SIMULADA. Vazio = não lê peso ao vivo.</summary>
    public string BalancaPorta { get; set; } = "";
    public int BalancaBaud { get; set; } = 9600;
    /// <summary>Peso usado quando a porta é SIMULADA.</summary>
    public decimal BalancaPesoSimulado { get; set; }

    public static string Caminho => Path.Combine(Db.Pasta, "pdv.json");


    private static readonly JsonSerializerOptions Leitura = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions Gravacao = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // acentos legíveis no arquivo
    };

    public static PdvConfig Carregar()
    {
        // Só a configuração deste computador (C:\ProgramData\ErpNaoFiscal). Um pdv.json antigo ao lado do .exe não é
        // mais lido: ele ia junto ao copiar a pasta do programa e o caixa abria com a configuração de outro computador.
        var origem = File.Exists(Caminho) ? Caminho : null;
        if (origem is null) return new PdvConfig();

        PdvConfig cfg;
        bool migrado;
        try
        {
            var texto = File.ReadAllText(origem);
            cfg = JsonSerializer.Deserialize<PdvConfig>(texto, Leitura) ?? new PdvConfig();
            migrado = EnderecoServidor.MigrarAntigo(texto, cfg.Servidor, s => cfg.Servidor = s);
            migrado |= cfg.AbrirChave();
        }
        catch (JsonException)
        {
            return new PdvConfig(); // arquivo corrompido: a tela de configurações abre de novo
        }

        if (migrado)
        {
            try { cfg.Salvar(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return cfg;
    }

    public void Salvar()
    {
        TerminalKeyProtegida = Proteger(TerminalKey);
        TerminalKeyAntiga = null; // a chave aberta sai do arquivo
        Directory.CreateDirectory(Db.Pasta);
        GravacaoAtomica.Texto(Caminho, JsonSerializer.Serialize(this, Gravacao));
    }

    /// <summary>
    /// Lê a chave do arquivo. Devolve true se o arquivo precisa ser regravado (chave antiga, aberta,
    /// que agora passa a ser criptografada). Se a chave criptografada não abrir — pdv.json copiado de
    /// outro computador ou Windows reinstalado — fica vazia e a tela de configurações pede de novo.
    /// </summary>
    private bool AbrirChave()
    {
        if (!string.IsNullOrEmpty(TerminalKeyProtegida))
        {
            try
            {
                var aberto = ProtectedData.Unprotect(Convert.FromBase64String(TerminalKeyProtegida), Entropia, DataProtectionScope.LocalMachine);
                TerminalKey = Encoding.UTF8.GetString(aberto);
                return !string.IsNullOrEmpty(TerminalKeyAntiga); // sobrou a aberta no arquivo: limpa
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                TerminalKey = "";
                return false; // não regrava: se for engano, o arquivo original continua lá
            }
        }

        TerminalKey = TerminalKeyAntiga ?? "";
        return !string.IsNullOrEmpty(TerminalKeyAntiga);
    }

    private static string? Proteger(string chave) =>
        string.IsNullOrEmpty(chave)
            ? null
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(chave), Entropia, DataProtectionScope.LocalMachine));

    public bool Configurado =>
        Uri.TryCreate(ApiUrl, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" &&
        !string.IsNullOrWhiteSpace(TerminalKey) &&
        !TerminalKey.StartsWith("COLE_AQUI", StringComparison.OrdinalIgnoreCase);

    /// <summary>Nome do servidor (nuvem) ou endereço digitado → endereço da API (veja EnderecoServidor).</summary>
    public static string NormalizarUrl(string? url) => EnderecoServidor.ParaUrl(url);
}
