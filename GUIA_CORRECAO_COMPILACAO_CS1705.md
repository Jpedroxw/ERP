# Correção da compilação — CS1705 (03/11/2026)

## O que foi corrigido

- `Directory.Build.props`: fixa a versão de Entity Framework Core em `10.0.14` numa propriedade compartilhada.
- `Erp.Api.csproj`: define referências do EF Core, Relational e Design na mesma versão.
- `Erp.TestesBanco.csproj` e `Erp.TestesImportacao.csproj`: utilizam as mesmas versões.
- `MigracoesBanco.cs`: substitui `ExecuteSqlRawAsync` com string interpolada por `ExecuteSqlInterpolatedAsync`, parametrizando o número da migração e removendo o aviso EF1002 nesta linha.

**Sem mudanças em SQLite, PostgreSQL, ícones, regras de negócio, layout, scripts de deploy nem arquivos de dados.**

## Como validar no Windows (na raiz do ERP)

```powershell
dotnet restore .\ErpNaoFiscal.slnx --force-evaluate
dotnet build .\ErpNaoFiscal.slnx -c Release --no-restore
dotnet run -c Release --project .\testes\Erp.TestesValidacao\Erp.TestesValidacao.csproj
dotnet run -c Release --project .\testes\Erp.TestesPesquisaEstoque\Erp.TestesPesquisaEstoque.csproj
dotnet run -c Release --project .\testes\Erp.TestesImportacao\Erp.TestesImportacao.csproj
```

O teste de banco deve ser executado apenas contra banco descartável de testes, observando as instruções existentes do projeto.

### Avisos restantes

O log original mostrava dois avisos `AD0001` do analisador de rotas do ASP.NET Core. São exceções internas do analisador, não erros de compilação da aplicação. Se continuarem após atualizar/restaurar os pacotes, registre o novo log para investigação isolada.

## Limites da verificação

Este pacote passou por verificação de integridade de ZIP, XML de csproj/props, inspeção estática das dependências e comparação dos arquivos. **Não foi possível executar `dotnet build` neste ambiente sem o SDK .NET instalado.**
