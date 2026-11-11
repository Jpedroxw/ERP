using ProjetoJP.Shared;

var erros = 0;
void Verificar(string nome, bool ok)
{
    Console.WriteLine($"{(ok ? "OK" : "ERRO")}: {nome}");
    if (!ok) erros++;
}

Verificar("Busca ignora acentos", PesquisaProdutos.Pontuar("cafe", "CAFÉ TORRADO 500G", "100", null) >= 900);
Verificar("Busca nome no meio", PesquisaProdutos.Pontuar("parboilizado", "ARROZ PARBOILIZADO 5KG", "200", null) >= 900);
Verificar("Busca palavras fora de ordem", PesquisaProdutos.Pontuar("5kg arroz", "ARROZ BRANCO 5KG", "300", null) >= 700);
Verificar("Busca nome semelhante (erro de digitação)", PesquisaProdutos.Pontuar("aroz", "ARROZ BRANCO", "400", null) > 0);
Verificar("Código exato tem prioridade", PesquisaProdutos.Pontuar("400", "ARROZ BRANCO", "400", null) == 1200);
Verificar("Código de barras exato tem prioridade", PesquisaProdutos.Pontuar("7890000000001", "ARROZ", "500", "7890000000001") == 1200);
Verificar("Produto não relacionado não aparece", PesquisaProdutos.Pontuar("cafe", "DETERGENTE LIMÃO", "999", null) < 0);
Verificar("Busca vazia permite lista inicial", PesquisaProdutos.Pontuar("", "ARROZ", "100", null) == 0);
Console.WriteLine(erros == 0 ? "Pesquisa aprovada" : $"{erros} teste(s) falharam");
return erros == 0 ? 0 : 1;
