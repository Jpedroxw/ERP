using ProjetoJP.Api.Endpoints;

var erros = 0;
var total = 0;
void Teste(string nome, bool condicao)
{
    total++;
    Console.WriteLine($"[{(condicao ? "OK" : "FALHOU")}] {nome}");
    if (!condicao) erros++;
}

Teste("Código normal sem alteração", CodigosImportacao.Normalizar("REF-01") == "REF-01");
Teste("Símbolos inválidos removidos", CodigosImportacao.Normalizar("REF@1") == "REF1");
Teste("Duas origens colidem", CodigosImportacao.Normalizar("REF@1") == CodigosImportacao.Normalizar("REF#1") &&
    CodigosImportacao.ColisaoDeOrigem("REF@1", "REF#1"));
Teste("Não bloqueia mesma origem", !CodigosImportacao.ColisaoDeOrigem("REF@1", "REF@1"));
Teste("Bloqueia sobrescrever por código convertido", CodigosImportacao.AtualizacaoInsegura("REF@1", "REF1", "REF1"));
Teste("Permite encontrar código legado original", !CodigosImportacao.AtualizacaoInsegura("REF@1", "REF1", "REF@1"));
Teste("Permite atualização de código inalterado", !CodigosImportacao.AtualizacaoInsegura("REF1", "REF1", "REF1"));
Teste("Código inválido vira vazio", CodigosImportacao.Normalizar("@@@") == "");
Teste("Reimportação com descrição igual não ignora colisão", CodigosImportacao.AtualizacaoInsegura("REF@1", "REF1", "REF1"));
Teste("Código convertido com descrição diferente continua bloqueado", CodigosImportacao.AtualizacaoInsegura("REF@1", "REF1", "REF1"));
Teste("GTIN comum (789...) é único", !CodigosImportacao.GtinRestrito("7891000315507"));
Teste("EAN-13 de balança (2...) é interno", CodigosImportacao.GtinRestrito("2000001000014"));
Teste("UPC-A interno (4...) é restrito", CodigosImportacao.GtinRestrito("400000000017"));

Console.WriteLine($"{total - erros} testes passaram; {erros} falharam.");
return erros > 0 ? 1 : 0;
