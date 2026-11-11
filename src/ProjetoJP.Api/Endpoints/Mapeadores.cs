using System.Security.Cryptography;
using ProjetoJP.Api.Dados;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ProjetoJP.Api.Endpoints;

// As mesmas regras de ProjetoJP.Shared.Formatos valem aqui: a Retaguarda já valida na tela,
// mas o servidor confere de novo para nada fora do formato entrar no banco.

public class CategoriaMapper : ICrudMapper<Categoria, CategoriaDto>
{
    public IQueryable<Categoria> Consulta(IQueryable<Categoria> q) => q.OrderBy(x => x.Nome);

    public CategoriaDto ParaDto(Categoria e) => new() { Id = e.Id, Nome = e.Nome, Ativo = e.Ativo };

    public async Task<string?> ValidarAsync(CategoriaDto dto, Guid? id, ErpDbContext db)
    {
        var nome = (dto.Nome ?? "").Trim();
        if (nome.Length == 0) return "Informe o nome da categoria.";
        if (nome.Length > 80) return "O nome da categoria pode ter no máximo 80 caracteres.";
        if (!Formatos.TextoSeguro(nome)) return "Nome de categoria contém caracteres inválidos.";
        var outro = id ?? Guid.Empty;
        if (await db.Categorias.AnyAsync(x => x.Nome == nome && x.Id != outro))
            return "Já existe uma categoria com esse nome.";
        return null;
    }

    public void Aplicar(CategoriaDto dto, Categoria e, bool novo)
    {
        e.Nome = dto.Nome.Trim();
        e.Ativo = dto.Ativo;
    }
}

public class ProdutoMapper : ICrudMapper<Produto, ProdutoDto>
{
    public IQueryable<Produto> Consulta(IQueryable<Produto> q) => q.Include(x => x.Categoria).OrderBy(x => x.Descricao);

    public ProdutoDto ParaDto(Produto e) => Converter(e);

    public static ProdutoDto Converter(Produto e) => new()
    {
        Id = e.Id,
        Codigo = e.Codigo,
        CodigoBarras = e.CodigoBarras,
        Descricao = e.Descricao,
        Unidade = e.Unidade,
        CategoriaId = e.CategoriaId,
        CategoriaNome = e.Categoria?.Nome,
        PrecoCusto = e.PrecoCusto,
        PrecoVenda = e.PrecoVenda,
        PrecoAtacado = e.PrecoAtacado,
        QtdMinimaAtacado = e.QtdMinimaAtacado,
        EstoqueAtual = e.EstoqueAtual,
        EstoqueMinimo = e.EstoqueMinimo,
        Ativo = e.Ativo
    };

    public async Task<string?> ValidarAsync(ProdutoDto dto, Guid? id, ErpDbContext db)
    {
        var descricao = (dto.Descricao ?? "").Trim();
        if (descricao.Length == 0) return "Informe a descrição do produto.";
        if (descricao.Length > 150) return "A descrição pode ter no máximo 150 caracteres.";
        if (!Formatos.TextoSeguro(descricao)) return "Descrição contém caracteres inválidos.";

        var unidade = (dto.Unidade ?? "").Trim().ToUpperInvariant();
        if (!Formatos.UnidadeValida(unidade))
            return $"Unidade inválida. Use uma destas: {string.Join(", ", Formatos.Unidades)}.";

        if (dto.PrecoVenda < 0) return "O preço de venda não pode ser negativo.";
        if (dto.PrecoVenda > Formatos.MaxMoeda || dto.PrecoCusto > Formatos.MaxMoeda) return "Preço acima do limite permitido.";
        if (dto.PrecoCusto < 0) return "O preço de custo não pode ser negativo.";
        if (Atacado.Validar(unidade, dto.PrecoVenda, dto.PrecoAtacado, dto.QtdMinimaAtacado) is { } erroAtacado) return erroAtacado;

        if (dto.EstoqueMinimo < 0 || dto.EstoqueMinimo > Formatos.MaxQuantidade) return "Estoque mínimo inválido.";
        if (!Formatos.UnidadeFracionada(unidade) && dto.EstoqueMinimo != decimal.Truncate(dto.EstoqueMinimo))
            return $"Produto em {unidade}: o estoque mínimo precisa ser um número inteiro.";

        // Código em branco: gera o próximo número livre.
        if (string.IsNullOrWhiteSpace(dto.Codigo))
            dto.Codigo = await ProximoCodigoAsync(db);

        var outro = id ?? Guid.Empty;
        var codigo = dto.Codigo.Trim().ToUpperInvariant();
        // Regra nova só para código novo ou alterado: produto antigo com código fora da regra (ex.: com espaço)
        // continua podendo ter preço e descrição alterados sem trocar o código (que pode estar em etiqueta/balança).
        var codigoAntigo = id is null ? null : await db.Produtos.Where(x => x.Id == id).Select(x => x.Codigo).FirstOrDefaultAsync();
        if (codigo != codigoAntigo && !Formatos.CodigoProdutoValido(codigo))
            return "Código inválido: utilize letras, números, ponto, hífen, barra ou sublinhado, até 30 caracteres.";
        if (await db.Produtos.AnyAsync(x => x.Codigo == codigo && x.Id != outro))
            return $"Já existe um produto com o código {codigo}.";

        if (!string.IsNullOrWhiteSpace(dto.CodigoBarras))
        {
            var barras = dto.CodigoBarras.Trim();
            if (!Formatos.GtinValido(barras))
                return "Código de barras inválido: precisa ter 8, 12, 13 ou 14 dígitos e o dígito verificador correto.";
            if (await db.Produtos.AnyAsync(x => x.CodigoBarras == barras && x.Id != outro))
                return $"Já existe um produto com o código de barras {barras}.";
        }
        return null;
    }

    private static async Task<string> ProximoCodigoAsync(ErpDbContext db)
    {
        var codigos = await db.Produtos.Where(x => x.Codigo.Length <= 9).Select(x => x.Codigo).ToListAsync();
        var maior = codigos.Select(c => int.TryParse(c, out var n) ? n : 0).DefaultIfEmpty(0).Max();
        return (maior + 1).ToString();
    }

    public void Aplicar(ProdutoDto dto, Produto e, bool novo)
    {
        var unidade = dto.Unidade.Trim().ToUpperInvariant();
        e.Codigo = dto.Codigo.Trim().ToUpperInvariant();
        e.CodigoBarras = string.IsNullOrWhiteSpace(dto.CodigoBarras) ? null : dto.CodigoBarras.Trim();
        e.Descricao = dto.Descricao.Trim().ToUpperInvariant();
        e.Unidade = unidade;
        e.CategoriaId = dto.CategoriaId == Guid.Empty ? null : dto.CategoriaId;
        e.PrecoCusto = Math.Round(dto.PrecoCusto, 2);
        e.PrecoVenda = Math.Round(dto.PrecoVenda, 2);
        var temAtacado = dto.PrecoAtacado > 0 && dto.QtdMinimaAtacado > 0;
        e.PrecoAtacado = temAtacado ? Math.Round(dto.PrecoAtacado, 2) : 0;
        e.QtdMinimaAtacado = temAtacado ? Math.Round(dto.QtdMinimaAtacado, Formatos.CasasQuantidade(unidade)) : 0;
        e.EstoqueMinimo = Math.Round(dto.EstoqueMinimo, Formatos.CasasQuantidade(unidade));
        e.Ativo = dto.Ativo;
        // EstoqueAtual só muda por movimentação de estoque ou venda.
    }
}

public class ClienteMapper : ICrudMapper<Cliente, ClienteDto>
{
    public IQueryable<Cliente> Consulta(IQueryable<Cliente> q) => q.OrderBy(x => x.Nome);

    public ClienteDto ParaDto(Cliente e) => Converter(e);

    public static ClienteDto Converter(Cliente e) => new()
    {
        Id = e.Id,
        Nome = e.Nome,
        CpfCnpj = e.CpfCnpj,
        Telefone = e.Telefone,
        Email = e.Email,
        Endereco = e.Endereco,
        Observacao = e.Observacao,
        Ativo = e.Ativo
    };

    public async Task<string?> ValidarAsync(ClienteDto dto, Guid? id, ErpDbContext db)
    {
        var nome = (dto.Nome ?? "").Trim();
        if (nome.Length == 0) return "Informe o nome do cliente.";
        if (nome.Length > 150) return "O nome pode ter no máximo 150 caracteres.";
        if (!Formatos.TextoSeguro(nome)) return "Nome contém caracteres inválidos.";

        if (!string.IsNullOrWhiteSpace(dto.CpfCnpj))
        {
            var doc = Formatos.LimparDocumento(dto.CpfCnpj);
            if (doc.Length is not (11 or 14)) return "CPF deve ter 11 dígitos e CNPJ 14 caracteres.";
            if (!Formatos.DocumentoDigitadoValido(dto.CpfCnpj)) return doc.Length == 11 ? "CPF inválido ou com caracteres não permitidos." : "CNPJ inválido ou com caracteres não permitidos.";
            var outro = id ?? Guid.Empty;
            if (await db.Clientes.AnyAsync(x => x.CpfCnpj == doc && x.Id != outro))
                return "Já existe um cliente com esse CPF/CNPJ.";
        }

        if (!string.IsNullOrWhiteSpace(dto.Telefone) && !Formatos.TelefoneDigitadoValido(dto.Telefone))
            return "Telefone inválido: informe o DDD e o número (10 ou 11 dígitos).";

        if (!string.IsNullOrWhiteSpace(dto.Email) &&
            (!Formatos.TextoSeguro(dto.Email) || !Formatos.EmailValido(dto.Email)))
            return "E-mail inválido.";

        if (dto.Endereco?.Length > 200) return "O endereço pode ter no máximo 200 caracteres.";
        if (!Formatos.TextoSeguro(dto.Endereco)) return "Endereço contém caracteres inválidos.";
        if (dto.Observacao?.Length > 500) return "A observação pode ter no máximo 500 caracteres.";
        if (!Formatos.TextoSeguro(dto.Observacao, multilinha: true)) return "Observação contém caracteres inválidos.";
        return null;
    }

    public void Aplicar(ClienteDto dto, Cliente e, bool novo)
    {
        e.Nome = dto.Nome.Trim();
        e.CpfCnpj = string.IsNullOrWhiteSpace(dto.CpfCnpj) ? null : Formatos.LimparDocumento(dto.CpfCnpj);
        e.Telefone = string.IsNullOrWhiteSpace(dto.Telefone) ? null : Formatos.SoDigitos(dto.Telefone);
        e.Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim().ToLowerInvariant();
        e.Endereco = string.IsNullOrWhiteSpace(dto.Endereco) ? null : dto.Endereco.Trim();
        e.Observacao = string.IsNullOrWhiteSpace(dto.Observacao) ? null : dto.Observacao.Trim();
        e.Ativo = dto.Ativo;
    }
}

public class FormaPagamentoMapper : ICrudMapper<FormaPagamento, FormaPagamentoDto>
{
    public IQueryable<FormaPagamento> Consulta(IQueryable<FormaPagamento> q) => q.OrderBy(x => x.Ordem).ThenBy(x => x.Descricao);

    public FormaPagamentoDto ParaDto(FormaPagamento e) => Converter(e);

    public static FormaPagamentoDto Converter(FormaPagamento e) => new()
    {
        Id = e.Id,
        Descricao = e.Descricao,
        PermiteTroco = e.PermiteTroco,
        EhFiado = e.EhFiado,
        Ordem = e.Ordem,
        Ativo = e.Ativo
    };

    public Task<string?> ValidarAsync(FormaPagamentoDto dto, Guid? id, ErpDbContext db)
    {
        var descricao = (dto.Descricao ?? "").Trim();
        string? erro =
            descricao.Length == 0 ? "Informe a descrição." :
            descricao.Length > 40 ? "A descrição pode ter no máximo 40 caracteres." :
            !Formatos.TextoSeguro(descricao) ? "Descrição contém caracteres inválidos." :
            dto.Ordem is < 0 or > 999 ? "A ordem deve ficar entre 0 e 999." :
            null;
        return Task.FromResult(erro);
    }

    public void Aplicar(FormaPagamentoDto dto, FormaPagamento e, bool novo)
    {
        e.Descricao = dto.Descricao.Trim();
        e.PermiteTroco = dto.PermiteTroco;
        e.EhFiado = dto.EhFiado;
        e.Ordem = dto.Ordem;
        e.Ativo = dto.Ativo;
    }
}

public class UsuarioMapper : ICrudMapper<Usuario, UsuarioDto>
{
    public IQueryable<Usuario> Consulta(IQueryable<Usuario> q) => q.OrderBy(x => x.Nome);

    // A senha nunca volta para a tela.
    public UsuarioDto ParaDto(Usuario e) => new()
    {
        Id = e.Id,
        Nome = e.Nome,
        Login = e.Login,
        Perfil = e.Perfil,
        Ativo = e.Ativo
    };

    public async Task<string?> ValidarAsync(UsuarioDto dto, Guid? id, ErpDbContext db)
    {
        var nome = (dto.Nome ?? "").Trim();
        if (nome.Length == 0) return "Informe o nome.";
        if (nome.Length > 120) return "O nome pode ter no máximo 120 caracteres.";
        if (!Formatos.TextoSeguro(nome)) return "Nome contém caracteres inválidos.";

        var login = (dto.Login ?? "").Trim().ToLowerInvariant();
        if (!Formatos.TextoSeguro(dto.Login) || !Formatos.LoginValido(login))
            return "Login inválido: use de 3 a 60 caracteres, só letras minúsculas, números, ponto, hífen ou sublinhado.";

        if (id is null && string.IsNullOrWhiteSpace(dto.Senha)) return "Informe a senha do novo usuário.";
        if (!string.IsNullOrWhiteSpace(dto.Senha) && dto.Senha.Length is < 4 or > 64)
            return "A senha precisa ter de 4 a 64 caracteres.";

        var outro = id ?? Guid.Empty;
        if (await db.Usuarios.AnyAsync(x => x.Login == login && x.Id != outro))
            return "Esse login já está em uso.";

        // Não deixa o sistema ficar sem nenhum administrador ativo.
        if (id is not null && (dto.Perfil != Perfil.Admin || !dto.Ativo))
        {
            if (!await db.Usuarios.AnyAsync(x => x.Perfil == Perfil.Admin && x.Ativo && x.Id != outro))
                return "É preciso manter pelo menos um administrador ativo.";
        }
        return null;
    }

    public async Task<string?> ValidarExclusaoAsync(Usuario e, ErpDbContext db)
    {
        if (e.Perfil == Perfil.Admin && !await db.Usuarios.AnyAsync(x => x.Perfil == Perfil.Admin && x.Ativo && x.Id != e.Id))
            return "É preciso manter pelo menos um administrador ativo.";
        return null;
    }

    public void Aplicar(UsuarioDto dto, Usuario e, bool novo)
    {
        e.Nome = dto.Nome.Trim();
        e.Login = dto.Login.Trim().ToLowerInvariant();
        e.Perfil = dto.Perfil;
        e.Ativo = dto.Ativo;
        if (!string.IsNullOrWhiteSpace(dto.Senha))
            e.SenhaHash = Senha.Gerar(dto.Senha);
    }
}

public class TerminalMapper : ICrudMapper<Terminal, TerminalDto>
{
    public IQueryable<Terminal> Consulta(IQueryable<Terminal> q) => q.OrderBy(x => x.Nome);

    public TerminalDto ParaDto(Terminal e) => new()
    {
        Id = e.Id,
        Nome = e.Nome,
        Chave = e.Chave,
        UltimaSincronizacao = e.UltimaSincronizacao,
        Ativo = e.Ativo
    };

    public async Task<string?> ValidarAsync(TerminalDto dto, Guid? id, ErpDbContext db)
    {
        var nome = (dto.Nome ?? "").Trim().ToUpperInvariant();
        if (nome.Length == 0) return "Informe o nome do terminal (ex.: CAIXA 01).";
        if (nome.Length > 40) return "O nome do terminal pode ter no máximo 40 caracteres.";
        if (!Formatos.TextoSeguro(nome)) return "Nome do terminal contém caracteres inválidos.";
        var outro = id ?? Guid.Empty;
        if (await db.Terminais.AnyAsync(x => x.Nome == nome && x.Id != outro))
            return "Já existe um terminal com esse nome.";
        return null;
    }

    public void Aplicar(TerminalDto dto, Terminal e, bool novo)
    {
        e.Nome = dto.Nome.Trim().ToUpperInvariant();
        e.Ativo = dto.Ativo;
        if (novo)
            e.Chave = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    }
}

public class FornecedorMapper : ICrudMapper<Fornecedor, FornecedorDto>
{
    public IQueryable<Fornecedor> Consulta(IQueryable<Fornecedor> q) => q.OrderBy(x => x.Nome);

    public FornecedorDto ParaDto(Fornecedor e) => new()
    {
        Id = e.Id,
        Nome = e.Nome,
        CpfCnpj = e.CpfCnpj,
        Contato = e.Contato,
        Telefone = e.Telefone,
        Email = e.Email,
        Cidade = e.Cidade,
        Uf = e.Uf,
        Observacao = e.Observacao,
        Ativo = e.Ativo
    };

    public async Task<string?> ValidarAsync(FornecedorDto dto, Guid? id, ErpDbContext db)
    {
        var nome = (dto.Nome ?? "").Trim();
        if (nome.Length == 0) return "Informe o nome do fornecedor.";
        if (nome.Length > 150) return "O nome pode ter no máximo 150 caracteres.";
        if (!Formatos.TextoSeguro(nome)) return "Nome contém caracteres inválidos.";

        if (!string.IsNullOrWhiteSpace(dto.CpfCnpj))
        {
            var doc = Formatos.LimparDocumento(dto.CpfCnpj);
            if (doc.Length is not (11 or 14)) return "CPF deve ter 11 dígitos e CNPJ 14 caracteres.";
            if (!Formatos.DocumentoDigitadoValido(dto.CpfCnpj))
                return doc.Length == 11 ? "CPF inválido ou com caracteres não permitidos." : "CNPJ inválido ou com caracteres não permitidos.";
            var outro = id ?? Guid.Empty;
            if (await db.Fornecedores.AnyAsync(x => x.CpfCnpj == doc && x.Id != outro))
                return "Já existe um fornecedor com esse CPF/CNPJ.";
        }

        if (dto.Contato?.Length > 100) return "O contato pode ter no máximo 100 caracteres.";
        if (!Formatos.TextoSeguro(dto.Contato)) return "Contato contém caracteres inválidos.";
        if (!string.IsNullOrWhiteSpace(dto.Telefone) && !Formatos.TelefoneDigitadoValido(dto.Telefone))
            return "Telefone inválido: informe o DDD e o número (10 ou 11 dígitos).";
        if (!string.IsNullOrWhiteSpace(dto.Email) &&
            (!Formatos.TextoSeguro(dto.Email) || !Formatos.EmailValido(dto.Email)))
            return "E-mail inválido.";
        if (dto.Cidade?.Length > 80) return "A cidade pode ter no máximo 80 caracteres.";
        if (!Formatos.TextoSeguro(dto.Cidade)) return "Cidade contém caracteres inválidos.";
        if (!string.IsNullOrWhiteSpace(dto.Uf) && !Formatos.UfValida(dto.Uf)) return "UF inválida (ex.: SP, RJ, MG).";
        if (dto.Observacao?.Length > 500) return "A observação pode ter no máximo 500 caracteres.";
        if (!Formatos.TextoSeguro(dto.Observacao, multilinha: true)) return "Observação contém caracteres inválidos.";
        return null;
    }

    public void Aplicar(FornecedorDto dto, Fornecedor e, bool novo)
    {
        e.Nome = dto.Nome.Trim();
        e.CpfCnpj = string.IsNullOrWhiteSpace(dto.CpfCnpj) ? null : Formatos.LimparDocumento(dto.CpfCnpj);
        e.Contato = string.IsNullOrWhiteSpace(dto.Contato) ? null : dto.Contato.Trim();
        e.Telefone = string.IsNullOrWhiteSpace(dto.Telefone) ? null : Formatos.SoDigitos(dto.Telefone);
        e.Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim().ToLowerInvariant();
        e.Cidade = string.IsNullOrWhiteSpace(dto.Cidade) ? null : dto.Cidade.Trim();
        e.Uf = string.IsNullOrWhiteSpace(dto.Uf) ? null : dto.Uf.Trim().ToUpperInvariant();
        e.Observacao = string.IsNullOrWhiteSpace(dto.Observacao) ? null : dto.Observacao.Trim();
        e.Ativo = dto.Ativo;
    }
}
