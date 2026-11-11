using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;

static void Testar(bool condicao, string titulo)
{
    if (!condicao) throw new InvalidOperationException("Falhou: " + titulo);
    Console.WriteLine("OK: " + titulo);
}
Testar(!Permissoes.Padrao(Perfil.Operador).Contains(Permissoes.VerCusto), "Operador não vê custo por padrão");
Testar(!Permissoes.Padrao(Perfil.Operador).Contains(Permissoes.MovimentarEstoque), "Operador não lança estoque por padrão");
Testar(!Permissoes.Padrao(Perfil.Operador).Contains(Permissoes.EditarProdutos), "Operador não edita produtos por padrão");
Testar(Permissoes.Padrao(Perfil.Gerente).Contains(Permissoes.PermitirEstoqueNegativo), "Gerente pode solicitar negativo (mediante senha)");
Testar(!ValidacaoSincronizacao.PermiteAtualizarOrcamento(3, StatusOrcamento.Aberto, 2, StatusOrcamento.Cancelado), "Revisão antiga recusada");
Testar(!ValidacaoSincronizacao.PermiteAtualizarOrcamento(3, StatusOrcamento.Convertido, 4, StatusOrcamento.Aberto), "Orçamento vendido não reabre");
Testar(ValidacaoSincronizacao.PermiteAtualizarOrcamento(2, StatusOrcamento.Aberto, 3, StatusOrcamento.Convertido), "Orçamento aberto pode virar venda");
var venda = new VendaDto
{
    Id = Guid.NewGuid(), Numero = 1, CaixaSessaoId = Guid.NewGuid(), UsuarioId = Guid.NewGuid(),
    Status = StatusVenda.Concluida, Subtotal = 20, Desconto = 2, Total = 18, Troco = 0,
    Itens = [new VendaItemDto { Id = Guid.NewGuid(), ProdutoId = Guid.NewGuid(), Descricao = "Produto", Quantidade = 2, PrecoUnitario = 10, Total = 20 }],
    Pagamentos = [new VendaPagamentoDto { Id = Guid.NewGuid(), FormaPagamentoId = Guid.NewGuid(), Valor = 18 }]
};
Testar(ValidacaoSincronizacao.ValidarVenda(venda) is null, "Venda válida");
venda.Pagamentos[0].Valor = 17;
Testar(ValidacaoSincronizacao.ValidarVenda(venda) is not null, "Pagamento incorreto bloqueado");
venda.Pagamentos[0].Valor = 18;
venda.Itens[0].Quantidade = -1;
Testar(ValidacaoSincronizacao.ValidarVenda(venda) is not null, "Quantidade negativa bloqueada");
var orc = new OrcamentoDto
{
    Id = Guid.NewGuid(), Numero = 1, UsuarioId = Guid.NewGuid(), Revisao = 1,
    Status = StatusOrcamento.Aberto, Subtotal = 15, Total = 15,
    Itens = [new OrcamentoItemDto { Id = Guid.NewGuid(), ProdutoId = Guid.NewGuid(), Quantidade = 3, PrecoUnitario = 5, Total = 15 }]
};
Testar(ValidacaoSincronizacao.ValidarOrcamento(orc) is null, "Orçamento correto");
orc.Total = 10;
Testar(ValidacaoSincronizacao.ValidarOrcamento(orc) is not null, "Orçamento com total incorreto bloqueado");
Console.WriteLine("Regras V8: testes de unidade concluídos.");
