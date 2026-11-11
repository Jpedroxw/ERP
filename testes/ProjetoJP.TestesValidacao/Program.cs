using ProjetoJP.Shared;

int passou = 0;
int falhou = 0;
void Verificar(string nome, bool condicao)
{
    if (condicao) passou++; else falhou++;
    Console.WriteLine($"[{(condicao ? "OK" : "FALHOU")}] {nome}");
}

Verificar("Código de produto alfanumérico", Formatos.CodigoProdutoValido("ARROZ-01"));
Verificar("Código permite barra e ponto", Formatos.CodigoProdutoValido("REF/01.2"));
Verificar("Código vazio deve gerar automático", !Formatos.CodigoProdutoValido(""));
Verificar("Código rejeita ponto inicial", !Formatos.CodigoProdutoValido(".ABC"));
Verificar("Código rejeita símbolos arbitrários", !Formatos.CodigoProdutoValido("AB@#"));
Verificar("Código rejeita espaço", !Formatos.CodigoProdutoValido("ARROZ 5KG"));
Verificar("Código rejeita mais de 30 caracteres", !Formatos.CodigoProdutoValido(new string('A', 31)));
Verificar("Nome com acento permitido", Formatos.TextoSeguro("João D'Ávila & Filhos"));
Verificar("Nome com caractere invisível rejeitado", !Formatos.TextoSeguro("João\u200BFilhos"));
Verificar("Nome com quebra de linha rejeitado", !Formatos.TextoSeguro("Cliente\nIncorreto"));
Verificar("Observação com quebra de linha permitida", Formatos.TextoSeguro("Linha 1\nLinha 2", multilinha: true));
Verificar("Observação com caractere de controle rejeitado", !Formatos.TextoSeguro("Texto\u0001", multilinha: true));
Verificar("CPF válido formatado", Formatos.DocumentoDigitadoValido("529.982.247-25"));
Verificar("CPF com símbolo estranho rejeitado", !Formatos.DocumentoDigitadoValido("529.982.247-25!"));
Verificar("CPF inválido rejeitado", !Formatos.DocumentoDigitadoValido("111.111.111-11"));
Verificar("Telefone com máscara aceito", Formatos.TelefoneDigitadoValido("(11) 98765-4321"));
Verificar("Telefone com letras rejeitado", !Formatos.TelefoneDigitadoValido("(11) 98765-4321abc"));
Verificar("Telefone sem DDD rejeitado", !Formatos.TelefoneDigitadoValido("98765-4321"));
Verificar("Login válido", Formatos.LoginValido("gerente.loja_01"));
Verificar("Login com espaço rejeitado", !Formatos.LoginValido("gerente loja"));
Verificar("Unidade UN válida", Formatos.UnidadeValida("UN"));
Verificar("Unidade desconhecida rejeitada", !Formatos.UnidadeValida("PACOTE_GRANDE"));
Verificar("Preço máximo positivo", Formatos.MaxMoeda > 0);
Verificar("Etiqueta 123 com 1,250 kg",
    EtiquetaBalanca.TentarLer("2000123012506", 6, false, out var et) && et.CodigoProduto == "000123" && et.PesoKg == 1.250m);
Verificar("Etiqueta com dígito errado é recusada", !EtiquetaBalanca.TentarLer("2000123012500", 6, false, out _));
Verificar("Código normal não é etiqueta", !EtiquetaBalanca.TentarLer("7891000315507", 6, false, out _));
Verificar("Peso em kg no texto da USB", PesoBalanca.TentarExtrair("ST,GS,  1.250kg", out var kg) && kg == 1.250m);
Verificar("Peso em gramas no texto da USB", PesoBalanca.TentarExtrair("350 g", out var g) && g == 0.350m);
Verificar("Peso instável não entra na venda",
    PesoBalanca.TentarLer("US,GS,  1.250kg", out var instavel) && instavel.Situacao == SituacaoPeso.Instavel);
Verificar("Balança zerada é sem peso",
    PesoBalanca.TentarLer("ST,GS,  0.000kg", out var zero) && zero.Situacao == SituacaoPeso.SemPeso);
Verificar("\"GS\" (peso bruto) não vira grama quando a balança não manda unidade",
    PesoBalanca.TentarExtrair("ST,GS,  1.250", out var semUnidade) && semUnidade == 1.250m);
Verificar("Gramas grudadas no número", PesoBalanca.TentarExtrair("ST,NT,1250g", out var grudado) && grudado == 1.250m);
Verificar("Etiqueta por preço: R$ 12,34", EtiquetaBalanca.TentarLer("2000123012346", 6, true, out var etp) && etp.PrecoTotal == 12.34m);
Verificar("Devolução de 1 de 3 itens a R$ 10 = R$ 10", Devolucao.Valor(30m, 3m, 1m, 30m, 30m) == 10m);
Verificar("Devolução rateia o desconto da venda (10% off)", Devolucao.Valor(30m, 3m, 1m, 30m, 27m) == 9m);
Verificar("Devolução de 0,500 kg de 1,000 kg a R$ 20", Devolucao.Valor(20m, 1m, 0.5m, 20m, 20m) == 10m);
Verificar("UN não devolve fração", !Devolucao.QuantidadeValida("UN", 0.5m) && Devolucao.QuantidadeValida("UN", 2m));
Verificar("KG devolve fração", Devolucao.QuantidadeValida("KG", 0.35m));
var faixas = new[] { new Atacado.Faixa(6, 4.50m), new Atacado.Faixa(12, 4.00m) };
Verificar("Atacado: 5 unidades ficam no varejo", Atacado.Calcular(5m, faixas, 5).Preco == 5m && Atacado.Calcular(5m, faixas, 5).Faixa is null);
Verificar("Atacado: 6 unidades saem a R$ 4,50", Atacado.Calcular(5m, faixas, 6).Preco == 4.50m);
Verificar("Atacado: 12 unidades pegam a faixa melhor", Atacado.Calcular(5m, faixas, 12).Preco == 4.00m);
Verificar("Atacado nunca passa do varejo (promoção de varejo menor)", Atacado.Calcular(3.99m, faixas, 20).Preco == 3.99m);
Verificar("Próxima faixa de atacado", Atacado.Proxima(5m, faixas, 3) is { Minimo: 6 });
Verificar("Atacado em UN precisa de 2 ou mais", Atacado.ValidarQuantidade("UN", 1) is not null && Atacado.ValidarQuantidade("UN", 2) is null);
Verificar("Atacado em KG aceita fração", Atacado.ValidarQuantidade("KG", 2.5m) is null);
Verificar("Atacado precisa ser menor que o varejo", Atacado.Validar("UN", 5m, 5m, 6) is not null && Atacado.Validar("UN", 5m, 4.5m, 6) is null);
Verificar("Atacado sem quantidade é recusado", Atacado.Validar("UN", 5m, 4.5m, 0) is not null);
Verificar("Sem atacado (os dois zerados) é aceito", Atacado.Validar("UN", 5m, 0, 0) is null);
Verificar("Só KG e G são pesáveis", Formatos.UnidadePesavel("KG") && Formatos.UnidadePesavel("g") && !Formatos.UnidadePesavel("UN") && !Formatos.UnidadePesavel("L"));

Console.WriteLine($"\n{passou} verificações passaram; {falhou} falharam.");
return falhou == 0 ? 0 : 1;
