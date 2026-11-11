using System.Data;
using System.Globalization;
using System.Text;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;

namespace ProjetoJP.Gestao.Servicos;

/// <summary>Um campo daqui (Descrição, Preço de venda...) que recebe uma coluna da origem.</summary>
public sealed record CampoDestino(string Chave, string Rotulo, bool Obrigatorio, string[] Palpites, string? Dica = null);

/// <summary>Campos de cada importação, palpites de nome de coluna e conversão dos valores.</summary>
public static class Mapeamento
{
    public static readonly CampoDestino[] Produtos =
    [
        new("codigo", "Código", false,
            ["CODIGO", "COD", "CODPROD", "CODPRODUTO", "CODIGOPRODUTO", "CODINTERNO", "CODIGOINTERNO", "REFERENCIA", "REF", "IDPRODUTO", "CODPRO"],
            "em branco = número automático"),
        new("barras", "Código de barras", false,
            ["CODBARRA", "CODBARRAS", "CODIGOBARRA", "CODIGOBARRAS", "CODIGODEBARRAS", "EAN", "EAN13", "GTIN", "BARRAS", "CODEAN", "CODGTIN"]),
        new("descricao", "Descrição", true,
            ["DESCRICAO", "DESCRICAOPRODUTO", "DESCPRODUTO", "DESCPROD", "NOMEPRODUTO", "PRODUTO", "NOME", "DESCR", "DESCRI", "DESC"]),
        new("unidade", "Unidade", false,
            ["UNIDADE", "UN", "UND", "UNID", "UNIDADEMEDIDA", "UNMEDIDA", "MEDIDA", "UNIDADEVENDA"], "UND, LT, PÇ... viram UN, L, PC"),
        new("categoria", "Categoria / grupo", false,
            ["CATEGORIA", "GRUPO", "NOMEGRUPO", "DESCGRUPO", "GRUPODESCRICAO", "SECAO", "DEPARTAMENTO", "FAMILIA", "SUBGRUPO"],
            "use o NOME do grupo; os que não existem são criados"),
        new("custo", "Preço de custo", false,
            ["PRECOCUSTO", "CUSTO", "PRCUSTO", "VLCUSTO", "VALORCUSTO", "CUSTOUNITARIO", "PCUSTO", "CUSTOMEDIO"]),
        new("venda", "Preço de venda", true,
            ["PRECOVENDA", "PRVENDA", "PVENDA", "VLVENDA", "VALORVENDA", "PRECO", "VENDA", "VALOR", "PRECO1", "PRECOVAREJO", "VALORUNITARIO"]),
        new("estoque", "Estoque atual", false,
            ["ESTOQUE", "ESTOQUEATUAL", "SALDO", "SALDOESTOQUE", "QTDESTOQUE", "QTD", "QTDE", "QUANTIDADE", "ESTATUAL"]),
        new("minimo", "Estoque mínimo", false,
            ["ESTOQUEMINIMO", "ESTMINIMO", "ESTMIN", "MINIMO", "QTDMINIMA", "QTDEMINIMA"]),
        new("ativo", "Ativo (S/N)", false, ["ATIVO", "SITUACAO", "STATUS"], "em branco = ativo")
    ];

    public static readonly CampoDestino[] Clientes =
    [
        new("nome", "Nome / razão social", true,
            ["NOME", "NOMECLIENTE", "CLIENTE", "RAZAOSOCIAL", "RAZAO", "NOMERAZAO", "NOMECOMPLETO"]),
        new("documento", "CPF / CNPJ", false,
            ["CPFCNPJ", "CNPJCPF", "CPF", "CNPJ", "CGCCPF", "CGC", "DOCUMENTO", "DOC"]),
        new("telefone", "Telefone", false,
            ["TELEFONE", "FONE", "CELULAR", "WHATSAPP", "TELEFONE1", "FONE1", "TEL", "FONECELULAR"], "com DDD"),
        new("email", "E-mail", false, ["EMAIL", "EMAILCLIENTE", "MAIL"]),
        new("endereco", "Endereço (rua)", false, ["ENDERECO", "LOGRADOURO", "RUA", "END"]),
        new("numero", "Número", false, ["NUMERO", "NUM", "NRO", "NUMEROENDERECO"], "juntado ao endereço"),
        new("bairro", "Bairro", false, ["BAIRRO"], "juntado ao endereço"),
        new("cidade", "Cidade", false, ["CIDADE", "MUNICIPIO", "NOMECIDADE"], "juntado ao endereço"),
        new("uf", "UF", false, ["UF", "ESTADO"], "juntado ao endereço"),
        new("obs", "Observação", false, ["OBSERVACAO", "OBSERVACOES", "OBS"]),
        new("ativo", "Ativo (S/N)", false, ["ATIVO", "SITUACAO", "STATUS"], "em branco = ativo")
    ];

    /// <summary>Nome comparável: sem acento, maiúsculo, só letras e números (COD_BARRAS = CodBarras).</summary>
    public static string Normalizar(string nome)
    {
        var sb = new StringBuilder();
        foreach (var c in Formatos.RemoverAcentos(nome).ToUpperInvariant())
            if (char.IsAsciiLetterOrDigit(c)) sb.Append(c);
        return sb.ToString();
    }

    /// <summary>Sugere uma coluna da origem para cada campo, pelo nome. Cada coluna é usada uma vez só.</summary>
    public static Dictionary<string, string?> Sugerir(IEnumerable<CampoDestino> campos, IReadOnlyList<string> colunas)
    {
        var usadas = new HashSet<string>();
        var normal = colunas.ToDictionary(c => c, Normalizar);
        var resultado = new Dictionary<string, string?>();
        var lista = campos.ToList();

        // 1º: nome igual a algum palpite (na ordem dos palpites, que vão do mais certo ao mais genérico)
        foreach (var campo in lista)
        {
            resultado[campo.Chave] = null;
            foreach (var p in campo.Palpites)
            {
                var achou = colunas.FirstOrDefault(c => !usadas.Contains(c) && normal[c] == p);
                if (achou is null) continue;
                resultado[campo.Chave] = achou;
                usadas.Add(achou);
                break;
            }
        }
        // 2º: nome que contém um palpite comprido (ex.: PRODUTO_DESCRICAO contém DESCRICAO).
        // Colunas de código/id (COD_GRUPO, ID_CIDADE) só valem para os campos de código: o resto quer o nome.
        foreach (var campo in lista.Where(c => resultado[c.Chave] is null))
        {
            var aceitaCodigo = campo.Chave is "codigo" or "barras";
            foreach (var p in campo.Palpites.Where(p => p.Length >= 5))
            {
                var achou = colunas.FirstOrDefault(c => !usadas.Contains(c) && normal[c].Contains(p) &&
                    (aceitaCodigo || !(normal[c].StartsWith("COD") || normal[c].StartsWith("ID"))));
                if (achou is null) continue;
                resultado[campo.Chave] = achou;
                usadas.Add(achou);
                break;
            }
        }
        return resultado;
    }

    /// <summary>
    /// Quanto uma tabela parece ser a de produtos (ou clientes): campos que casam pelo nome da coluna,
    /// os obrigatórios valendo mais, e um bônus pelo nome da tabela. 0 = não serve (falta campo obrigatório).
    /// </summary>
    public static (int pontos, int campos) Pontuar(CampoDestino[] destinos, string tabela, IReadOnlyList<string> colunas)
    {
        var sugestao = Sugerir(destinos, colunas);
        if (destinos.Any(d => d.Obrigatorio && sugestao[d.Chave] is null)) return (0, 0);

        var campos = destinos.Count(d => sugestao[d.Chave] is not null);
        var pontos = destinos.Sum(d => sugestao[d.Chave] is null ? 0 : d.Obrigatorio ? 3 : 1);

        var nome = Normalizar(tabela.Split('.').Last());
        var deProdutos = ReferenceEquals(destinos, Produtos);
        string[] bons = deProdutos
            ? ["PRODUTOS", "PRODUTO", "PROD", "MERCADORIAS", "MERCADORIA", "ITENS", "ITEM", "ESTOQUE", "CADPRO", "CADPRODUTO"]
            : ["CLIENTES", "CLIENTE", "CLI", "CADCLI", "PESSOAS", "PESSOA", "CADASTRO", "PARCEIROS"];
        if (bons.Contains(nome)) pontos += 4;
        else if (bons.Any(b => b.Length >= 4 && nome.Contains(b))) pontos += 2;

        // Tabelas de movimento (vendas, itens de nota...) têm "descrição" e "valor" mas não são cadastro
        string[] movimento = ["VENDA", "PEDIDO", "NOTA", "NF", "MOVIMENTO", "MOV", "LOG", "HIST", "TEMP", "TMP", "BKP", "BACKUP", "ITENSVENDA"];
        if (movimento.Any(m => nome.StartsWith(m) || nome.EndsWith(m))) pontos -= 5;

        return (Math.Max(pontos, 1), campos);
    }

    // ---------- Conversão dos valores ----------

    private static readonly Encoding Win1252 = Encoding.GetEncoding(1252);

    public static string? Texto(object? v) => v switch
    {
        null or DBNull => null,
        string s => s.Trim(),
        byte[] b => Win1252.GetString(b).TrimEnd('\0').Trim(),   // texto gravado como binário (Firebird NONE, BLOB)
        DateTime d => d.ToString("dd/MM/yyyy", Formatos.Br),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString()?.Trim()
    };

    public static decimal? Numero(object? v)
    {
        switch (v)
        {
            case null or DBNull: return null;
            case decimal d: return d;
            case double db when !double.IsNaN(db) && !double.IsInfinity(db) && Math.Abs(db) < 1e15: return Math.Round((decimal)db, 6);
            case float f when !float.IsNaN(f) && !float.IsInfinity(f) && Math.Abs(f) < 1e15: return Math.Round((decimal)f, 6);
            case int or long or short or byte or uint or ulong or ushort or sbyte: return Convert.ToDecimal(v, CultureInfo.InvariantCulture);
        }
        var s = Texto(v);
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Replace("R$", "").Replace(" ", "").Replace(" ", "");
        // "1,234.56" (formato americano, com milhar): o último separador é o decimal.
        if (s.Contains(',') && s.Contains('.') && s.LastIndexOf('.') > s.LastIndexOf(','))
            s = s.Replace(",", "").Replace('.', ',');
        return Formatos.TryLerDecimal(s, out var n) ? n : null;
    }

    public static bool? Logico(object? v)
    {
        switch (v)
        {
            case null or DBNull: return null;
            case bool b: return b;
            case int or long or short or byte or decimal or double: return Convert.ToDecimal(v, CultureInfo.InvariantCulture) != 0;
        }
        var s = Normalizar(Texto(v) ?? "");
        return s switch
        {
            "" => null,
            "S" or "SIM" or "A" or "ATIVO" or "1" or "T" or "TRUE" or "Y" or "YES" or "V" => true,
            "N" or "NAO" or "I" or "INATIVO" or "0" or "F" or "FALSE" or "NO" or "D" or "DESATIVADO" or "EXCLUIDO" or "BLOQUEADO" => false,
            _ => null
        };
    }

    /// <summary>Monta os produtos a partir das linhas lidas e das colunas escolhidas.</summary>
    public static List<ProdutoImportado> ParaProdutos(DataTable dados, IReadOnlyDictionary<string, string?> mapa, int primeiraLinha)
    {
        object? Valor(DataRow r, string chave) =>
            mapa.TryGetValue(chave, out var col) && col is not null && dados.Columns.Contains(col) ? r[col] : null;

        var lista = new List<ProdutoImportado>(dados.Rows.Count);
        for (var i = 0; i < dados.Rows.Count; i++)
        {
            var r = dados.Rows[i];
            lista.Add(new ProdutoImportado
            {
                Linha = primeiraLinha + i,
                Codigo = Texto(Valor(r, "codigo")),
                CodigoBarras = Texto(Valor(r, "barras")),
                Descricao = Texto(Valor(r, "descricao")),
                Unidade = Texto(Valor(r, "unidade")),
                Categoria = Texto(Valor(r, "categoria")),
                PrecoCusto = Numero(Valor(r, "custo")),
                PrecoVenda = Numero(Valor(r, "venda")),
                Estoque = Numero(Valor(r, "estoque")),
                EstoqueMinimo = Numero(Valor(r, "minimo")),
                Ativo = Logico(Valor(r, "ativo"))
            });
        }
        return lista;
    }

    public static List<ClienteImportado> ParaClientes(DataTable dados, IReadOnlyDictionary<string, string?> mapa, int primeiraLinha)
    {
        object? Valor(DataRow r, string chave) =>
            mapa.TryGetValue(chave, out var col) && col is not null && dados.Columns.Contains(col) ? r[col] : null;

        var lista = new List<ClienteImportado>(dados.Rows.Count);
        for (var i = 0; i < dados.Rows.Count; i++)
        {
            var r = dados.Rows[i];

            // Endereço = rua, número - bairro - cidade/UF (o que tiver)
            var rua = Texto(Valor(r, "endereco"));
            var numero = Texto(Valor(r, "numero"));
            var bairro = Texto(Valor(r, "bairro"));
            var cidade = Texto(Valor(r, "cidade"));
            var uf = Texto(Valor(r, "uf"));
            var partes = new List<string>();
            var ruaNumero = string.Join(", ", new[] { rua, numero }.Where(x => !string.IsNullOrWhiteSpace(x)));
            if (ruaNumero.Length > 0) partes.Add(ruaNumero);
            if (!string.IsNullOrWhiteSpace(bairro)) partes.Add(bairro);
            var cidadeUf = string.Join("/", new[] { cidade, uf }.Where(x => !string.IsNullOrWhiteSpace(x)));
            if (cidadeUf.Length > 0) partes.Add(cidadeUf);

            lista.Add(new ClienteImportado
            {
                Linha = primeiraLinha + i,
                Nome = Texto(Valor(r, "nome")),
                CpfCnpj = Texto(Valor(r, "documento")),
                Telefone = Texto(Valor(r, "telefone")),
                Email = Texto(Valor(r, "email")),
                Endereco = partes.Count > 0 ? string.Join(" - ", partes) : null,
                Observacao = Texto(Valor(r, "obs")),
                Ativo = Logico(Valor(r, "ativo"))
            });
        }
        return lista;
    }
}
