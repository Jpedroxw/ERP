using System.Security.Cryptography;
using System.Text;
using ProjetoJP.Api.Dados;
using ProjetoJP.Api.Servicos;
using ProjetoJP.Shared;
using ProjetoJP.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ProjetoJP.Api.Endpoints;

/// <summary>
/// ERP Master (só o dono do sistema): clientes e pedidos de cadastro.
/// Protegido pela chave Master__Chave do /etc/erp-nf/api.env, enviada no cabeçalho X-Master-Key.
/// </summary>
public static class MasterEndpoints
{
    public static void MapMaster(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/master").AddEndpointFilter(ConferirChave).RequireRateLimiting("master");

        g.MapGet("/health", () => Results.Ok(new { ok = true }));

        // ---------- Clientes ----------

        g.MapGet("/clientes", async (Inquilinos inq) =>
        {
            await using var m = inq.AbrirMaster();
            var clientes = await m.Clientes.AsNoTracking().OrderBy(x => x.Nome).ToListAsync();
            var lista = new List<ClienteMasterDto>();
            foreach (var c in clientes) lista.Add(await ResumoAsync(inq, c));
            return lista;
        });

        g.MapPost("/clientes", async (NovoClienteRequest req, Inquilinos inq, ILogger<Inquilinos> log) =>
        {
            var codigo = (req.Codigo ?? "").Trim().ToLowerInvariant();
            if (!Formatos.TextoSeguro(req.AdminNome) || !Formatos.TextoSeguro(req.Observacao, multilinha: true) ||
                (req.Observacao?.Length ?? 0) > 500)
                return Extensoes.Erro("Nome do administrador ou observação contém caracteres inválidos.");
            var erro = ValidarAdmin(req.AdminLogin, req.AdminSenha);
            if (erro is not null) return Extensoes.Erro(erro);
            var (dados, erroDados) = Empresa(req.Documento, req.Responsavel, req.Telefone, req.Email, req.Cidade, req.Uf);
            if (dados is null) return Extensoes.Erro(erroDados!);
            if (!LimiteValido(req.LimiteCaixas)) return Extensoes.Erro("Limite de caixas inválido (0 = sem limite, máximo 999).");
            var (cliente, erroCriar) = await CriarClienteAsync(inq, log, codigo, req.Nome, req.Observacao, dados, Limite(req.LimiteCaixas),
                new AdminInicial(string.IsNullOrWhiteSpace(req.AdminNome) ? "Administrador" : req.AdminNome.Trim(),
                    req.AdminLogin, Senha.Gerar(req.AdminSenha)));
            return cliente is null ? Extensoes.Erro(erroCriar!) : Results.Ok(await ResumoAsync(inq, cliente));
        });

        g.MapPut("/clientes/{id:guid}", async (Guid id, AtualizarClienteRequest req, Inquilinos inq) =>
        {
            var nome = (req.Nome ?? "").Trim();
            if (nome.Length is 0 or > 120 || !Formatos.TextoSeguro(nome))
                return Extensoes.Erro("Informe um nome válido (até 120 caracteres).");
            if (!Formatos.TextoSeguro(req.Observacao, multilinha: true) || (req.Observacao?.Length ?? 0) > 500)
                return Extensoes.Erro("Observação inválida (máximo 500 caracteres).");
            var (dados, erroDados) = Empresa(req.Documento, req.Responsavel, req.Telefone, req.Email, req.Cidade, req.Uf);
            if (dados is null) return Extensoes.Erro(erroDados!);
            if (!LimiteValido(req.LimiteCaixas)) return Extensoes.Erro("Limite de caixas inválido (0 = sem limite, máximo 999).");
            await using var m = inq.AbrirMaster();
            var c = await m.Clientes.FirstOrDefaultAsync(x => x.Id == id);
            if (c is null) return Results.NotFound();
            c.Nome = nome;
            dados.Aplicar(c);
            c.LimiteCaixas = Limite(req.LimiteCaixas);
            c.Ativo = req.Ativo;
            c.Observacao = string.IsNullOrWhiteSpace(req.Observacao) ? null : req.Observacao.Trim();
            await m.SaveChangesAsync();
            inq.Esquecer(c.Codigo);
            return Results.Ok(await ResumoAsync(inq, c));
        });

        g.MapPost("/clientes/{id:guid}/senha-admin", async (Guid id, SenhaAdminRequest req, Inquilinos inq) =>
        {
            var erro = ValidarAdmin(req.Login, req.Senha);
            if (erro is not null) return Extensoes.Erro(erro);
            await using var m = inq.AbrirMaster();
            var c = await m.Clientes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (c is null) return Results.NotFound();

            await using var db = inq.AbrirCliente(c.Banco);
            var login = req.Login.Trim().ToLowerInvariant();
            var u = await db.Usuarios.FirstOrDefaultAsync(x => x.Login == login);
            if (u is null)
            {
                u = new Usuario { Id = Guid.NewGuid(), Nome = "Administrador", Login = login };
                db.Usuarios.Add(u);
            }
            await using var transacao = await db.Database.BeginTransactionAsync();
            u.SenhaHash = Senha.Gerar(req.Senha);
            u.Perfil = Perfil.Admin;
            u.Ativo = true;
            await PublicacaoUsuarios.PublicarAsync(db, u); // a senha nova também vale nos caixas
            RegistroAuditoria.Adicionar(db, new System.Security.Claims.ClaimsPrincipal(),
                "RedefinirSenhaMaster", "Usuario", u.Id, $"Senha do administrador {u.Login} redefinida pelo Projeto JP Master");
            await db.SaveChangesAsync();
            await db.SessoesAcesso.Where(x => x.UsuarioId == u.Id && x.RevogadaEm == null)
                .ExecuteUpdateAsync(x => x.SetProperty(s => s.RevogadaEm, (DateTime?)DateTime.UtcNow));
            await db.Dispositivos.Where(x => x.UsuarioId == u.Id && !x.Revogado)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Revogado, true));
            await transacao.CommitAsync();
            return Results.Ok(new { ok = true, login });
        });

        // ---------- Segurança de cada loja (antes ficava na Retaguarda) ----------
        // Sessões abertas, computadores lembrados (versões antigas) e histórico de alterações administrativas.
        g.MapGet("/clientes/{id:guid}/seguranca", async (Guid id, int? limite, Inquilinos inq) =>
        {
            await using var m = inq.AbrirMaster();
            var c = await m.Clientes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (c is null) return Results.NotFound();
            await using var db = inq.AbrirCliente(c.Banco);
            var agora = DateTime.UtcNow;
            var quantidade = Math.Clamp(limite ?? 300, 1, 1000);

            var sessoes = await (from s in db.SessoesAcesso.AsNoTracking()
                                 join u in db.Usuarios.AsNoTracking() on s.UsuarioId equals u.Id
                                 where s.ExpiraEm > agora && s.RevogadaEm == null
                                 orderby s.CriadaEm descending
                                 select new SessaoResumoDto
                                 {
                                     Id = s.Id, Usuario = u.Login, CriadaEm = s.CriadaEm, ExpiraEm = s.ExpiraEm,
                                     Revogada = false, ComputadorLembrado = s.DispositivoId != null
                                 }).Take(300).ToListAsync();
            var computadores = await (from d in db.Dispositivos.AsNoTracking()
                                      join u in db.Usuarios.AsNoTracking() on d.UsuarioId equals u.Id
                                      orderby d.CriadoEm descending
                                      select new DispositivoResumoDto
                                      {
                                          Id = d.Id, Nome = d.Nome, Usuario = u.Login, Tpm = d.Tpm,
                                          CriadoEm = d.CriadoEm, ExpiraEm = d.ExpiraEm, UltimoUsoEm = d.UltimoUsoEm,
                                          Revogado = d.Revogado, PrecisaRecadastro = d.PinHash != null
                                      }).Take(200).ToListAsync();
            var auditoria = await (from a in db.Auditorias.AsNoTracking()
                                   join u in db.Usuarios.AsNoTracking() on a.UsuarioId equals (Guid?)u.Id into usuarios
                                   from u in usuarios.DefaultIfEmpty()
                                   orderby a.Data descending
                                   select new AuditoriaResumoDto
                                   {
                                       Id = a.Id, Data = a.Data, Usuario = u == null ? "(sistema / Projeto JP Master)" : u.Login,
                                       Acao = a.Acao, AlvoTipo = a.AlvoTipo, AlvoId = a.AlvoId, Resumo = a.Resumo
                                   }).Take(quantidade).ToListAsync();
            return Results.Ok(new SegurancaLojaDto { Sessoes = sessoes, Computadores = computadores, Auditoria = auditoria });
        });

        // Encerra uma sessão da loja (a pessoa precisa entrar de novo na Retaguarda).
        g.MapDelete("/clientes/{id:guid}/sessoes/{sessaoId:guid}", async (Guid id, Guid sessaoId, Inquilinos inq) =>
        {
            await using var m = inq.AbrirMaster();
            var c = await m.Clientes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (c is null) return Results.NotFound();
            await using var db = inq.AbrirCliente(c.Banco);
            var alteradas = await db.SessoesAcesso.Where(x => x.Id == sessaoId && x.RevogadaEm == null)
                .ExecuteUpdateAsync(x => x.SetProperty(s => s.RevogadaEm, (DateTime?)DateTime.UtcNow));
            if (alteradas > 0)
            {
                RegistroAuditoria.Adicionar(db, new System.Security.Claims.ClaimsPrincipal(),
                    "EncerrarSessao", "Sessao", sessaoId, "Sessão encerrada pelo Projeto JP Master");
                await db.SaveChangesAsync();
            }
            return Results.NoContent();
        });

        // Encerra TODAS as sessões abertas da loja (ex.: senha vazou, funcionário saiu).
        g.MapPost("/clientes/{id:guid}/sessoes/encerrar-todas", async (Guid id, Inquilinos inq) =>
        {
            await using var m = inq.AbrirMaster();
            var c = await m.Clientes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (c is null) return Results.NotFound();
            await using var db = inq.AbrirCliente(c.Banco);
            var alteradas = await db.SessoesAcesso.Where(x => x.RevogadaEm == null && x.ExpiraEm > DateTime.UtcNow)
                .ExecuteUpdateAsync(x => x.SetProperty(s => s.RevogadaEm, (DateTime?)DateTime.UtcNow));
            RegistroAuditoria.Adicionar(db, new System.Security.Claims.ClaimsPrincipal(),
                "EncerrarSessoes", "Sessao", null, $"{alteradas} sessão(ões) encerrada(s) pelo Projeto JP Master");
            await db.SaveChangesAsync();
            return Results.Ok(new { encerradas = alteradas });
        });

        // Revoga um computador lembrado (de versões antigas) e as sessões abertas por ele.
        g.MapDelete("/clientes/{id:guid}/dispositivos/{dispositivoId:guid}", async (Guid id, Guid dispositivoId, Inquilinos inq) =>
        {
            await using var m = inq.AbrirMaster();
            var c = await m.Clientes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (c is null) return Results.NotFound();
            await using var db = inq.AbrirCliente(c.Banco);
            await using var transacao = await db.Database.BeginTransactionAsync();
            var quantidade = await db.Dispositivos.Where(x => x.Id == dispositivoId && !x.Revogado)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Revogado, true));
            if (quantidade > 0)
            {
                await db.SessoesAcesso.Where(x => x.DispositivoId == dispositivoId && x.RevogadaEm == null)
                    .ExecuteUpdateAsync(x => x.SetProperty(s => s.RevogadaEm, (DateTime?)DateTime.UtcNow));
                RegistroAuditoria.Adicionar(db, new System.Security.Claims.ClaimsPrincipal(),
                    "RevogarDispositivo", "Dispositivo", dispositivoId, "Computador lembrado revogado pelo Projeto JP Master");
                await db.SaveChangesAsync();
            }
            await transacao.CommitAsync();
            return Results.NoContent();
        });

        // ---------- Acesso técnico individual de cada cliente ----------
        // A credencial é exclusiva de configurações técnicas; não é usuário Gerente/Admin do ERP.
        g.MapGet("/clientes/{id:guid}/acesso-tecnico", async (Guid id, Inquilinos inq) =>
        {
            await using var m = inq.AbrirMaster();
            if (!await m.Clientes.AsNoTracking().AnyAsync(c => c.Id == id)) return Results.NotFound();
            var acesso = await m.SuportesTecnicos.AsNoTracking().FirstOrDefaultAsync(s => s.ClienteId == id);
            return Results.Ok(new SuporteTecnicoStatusDto
            {
                Configurado = acesso is not null,
                Login = acesso?.Login ?? "suporte",
                Ativo = acesso?.Ativo ?? false,
                AtualizadoEm = acesso?.AtualizadoEm
            });
        });

        g.MapPost("/clientes/{id:guid}/acesso-tecnico/senha", async (
            Guid id, SuporteTecnicoSenhaRequest req, Inquilinos inq, ILogger<Inquilinos> log) =>
        {
            var login = (req.Login ?? "").Trim().ToLowerInvariant();
            if (!Formatos.LoginValido(login) || login == "admin")
                return Extensoes.Erro("Informe um login técnico válido, diferente de admin.");
            if (req.NovaSenha is null || req.NovaSenha.Length is < 12 or > 128)
                return Extensoes.Erro("A senha técnica deve ter de 12 a 128 caracteres.");
            await using var m = inq.AbrirMaster();
            var cliente = await m.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
            if (cliente is null) return Results.NotFound();
            var acesso = await m.SuportesTecnicos.FirstOrDefaultAsync(s => s.ClienteId == id);
            if (acesso is null)
            {
                acesso = new SuporteTecnicoCliente { Id = Guid.NewGuid(), ClienteId = id };
                m.SuportesTecnicos.Add(acesso);
            }
            acesso.Login = login;
            acesso.SenhaHash = Senha.Gerar(req.NovaSenha);
            acesso.Ativo = true;
            acesso.AtualizadoEm = DateTime.UtcNow;
            await m.SaveChangesAsync();
            log.LogWarning("Senha técnica redefinida para cliente {Codigo} pelo Master em {Quando}", cliente.Codigo, acesso.AtualizadoEm);
            return Results.NoContent();
        });

        g.MapPut("/clientes/{id:guid}/acesso-tecnico/status", async (
            Guid id, SuporteTecnicoStatusRequest req, Inquilinos inq, ILogger<Inquilinos> log) =>
        {
            await using var m = inq.AbrirMaster();
            var cliente = await m.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
            if (cliente is null) return Results.NotFound();
            var acesso = await m.SuportesTecnicos.FirstOrDefaultAsync(s => s.ClienteId == id);
            if (acesso is null) return Extensoes.Erro("Primeiro defina uma senha para o suporte desta empresa.");
            acesso.Ativo = req.Ativo;
            acesso.AtualizadoEm = DateTime.UtcNow;
            await m.SaveChangesAsync();
            log.LogWarning("Acesso técnico do cliente {Codigo}: {Status}", cliente.Codigo, req.Ativo ? "liberado" : "bloqueado");
            return Results.NoContent();
        });

        // Exclui o cliente: cópia de segurança, apaga o banco dele e os registros no master (cliente e pedidos).
        // Pede o código digitado de novo (?confirmar=codigo) para ninguém apagar a empresa errada.
        g.MapDelete("/clientes/{id:guid}", async (Guid id, string? confirmar, Inquilinos inq) =>
        {
            await using var m = inq.AbrirMaster();
            var c = await m.Clientes.FirstOrDefaultAsync(x => x.Id == id);
            if (c is null) return Results.NotFound();
            if (!string.Equals((confirmar ?? "").Trim(), c.Codigo, StringComparison.OrdinalIgnoreCase))
                return Extensoes.Erro($"Para excluir, digite exatamente o nome do servidor: {c.Codigo}");

            string copia;
            try
            {
                copia = await inq.CopiarAntesDeExcluirAsync(c.Banco);
                await inq.ExcluirBancoAsync(c.Banco);
            }
            catch (Exception ex)
            {
                return Extensoes.Erro(ex.Message);
            }

            m.SuportesTecnicos.RemoveRange(m.SuportesTecnicos.Where(x => x.ClienteId == c.Id));
            m.Pedidos.RemoveRange(m.Pedidos.Where(x => x.ClienteId == c.Id || x.CodigoAprovado == c.Codigo));
            m.Clientes.Remove(c);
            await m.SaveChangesAsync();
            inq.Esquecer(c.Codigo);
            return Results.Ok(new { ok = true, copia });
        });

        // ---------- Pedidos de cadastro ----------

        g.MapDelete("/pedidos/{id:guid}", async (Guid id, Inquilinos inq) =>
        {
            await using var m = inq.AbrirMaster();
            var p = await m.Pedidos.FirstOrDefaultAsync(x => x.Id == id);
            if (p is null) return Results.NotFound();
            m.Pedidos.Remove(p); // apaga também os dados pessoais do pedido (nome, telefone, e-mail, documento)
            await m.SaveChangesAsync();
            return Results.NoContent();
        });

        g.MapGet("/pedidos", async (int? situacao, Inquilinos inq) =>
        {
            await using var m = inq.AbrirMaster();
            var q = m.Pedidos.AsNoTracking();
            if (situacao is { } s) q = q.Where(x => (int)x.Situacao == s);
            var lista = await q.OrderByDescending(x => x.CriadoEm).Take(500).ToListAsync();
            return lista.Select(p => new PedidoCadastroDto
            {
                Id = p.Id, CriadoEm = p.CriadoEm, Empresa = p.Empresa, Documento = p.Documento,
                Responsavel = p.Responsavel, Telefone = p.Telefone, Email = p.Email, Cidade = p.Cidade, Uf = p.Uf,
                CodigoDesejado = p.CodigoDesejado, AdminLogin = p.AdminLogin, Situacao = p.Situacao.ToString(),
                Motivo = p.Motivo, CodigoAprovado = p.CodigoAprovado
            }).ToList();
        });

        g.MapPost("/pedidos/{id:guid}/aprovar", async (Guid id, AprovarPedidoRequest req, Inquilinos inq, ILogger<Inquilinos> log) =>
        {
            await using var m = inq.AbrirMaster();
            var p = await m.Pedidos.FirstOrDefaultAsync(x => x.Id == id);
            if (p is null) return Results.NotFound();
            if (p.Situacao != SituacaoPedido.Pendente) return Extensoes.Erro("Esse pedido já foi resolvido.");

            var codigo = string.IsNullOrWhiteSpace(req.Codigo) ? p.CodigoDesejado : req.Codigo.Trim().ToLowerInvariant();
            var nome = string.IsNullOrWhiteSpace(req.Nome) ? p.Empresa : req.Nome.Trim();
            var obs = $"Cadastro pedido pela Gestão em {p.CriadoEm.ToLocalTime():dd/MM/yyyy HH:mm}.";
            var dados = new DadosEmpresa(p.Documento, p.Responsavel, p.Telefone, p.Email, p.Cidade, p.Uf);

            if (!LimiteValido(req.LimiteCaixas)) return Extensoes.Erro("Limite de caixas inválido (0 = sem limite, máximo 999).");
            var (cliente, erro) = await CriarClienteAsync(inq, log, codigo, nome, obs, dados, Limite(req.LimiteCaixas),
                new AdminInicial(p.Responsavel, p.AdminLogin, p.AdminSenhaHash), p.Id);
            if (cliente is null) return Extensoes.Erro(erro!);

            p.Situacao = SituacaoPedido.Aprovado;
            p.ClienteId = cliente.Id;
            p.CodigoAprovado = cliente.Codigo;
            p.ResolvidoEm = DateTime.UtcNow;
            p.AdminSenhaHash = ""; // já foi para o banco do cliente
            await m.SaveChangesAsync();
            return Results.Ok(await ResumoAsync(inq, cliente));
        });

        g.MapPost("/pedidos/{id:guid}/recusar", async (Guid id, RecusarPedidoRequest req, Inquilinos inq) =>
        {
            await using var m = inq.AbrirMaster();
            var p = await m.Pedidos.FirstOrDefaultAsync(x => x.Id == id);
            if (p is null) return Results.NotFound();
            if (p.Situacao != SituacaoPedido.Pendente) return Extensoes.Erro("Esse pedido já foi resolvido.");
            p.Situacao = SituacaoPedido.Recusado;
            p.Motivo = string.IsNullOrWhiteSpace(req.Motivo) ? null : req.Motivo.Trim()[..Math.Min(300, req.Motivo.Trim().Length)];
            p.ResolvidoEm = DateTime.UtcNow;
            p.AdminSenhaHash = "";
            await m.SaveChangesAsync();
            return Results.Ok(new { ok = true });
        });
    }

    // ---------- Pedido de cadastro (público, sem login) ----------

    public static void MapCadastro(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/cadastro");

        g.MapGet("/disponivel", async (string? codigo, Inquilinos inq) =>
        {
            var c = (codigo ?? "").Trim().ToLowerInvariant();
            if (!CodigoCliente.Valido(c))
                return new DisponibilidadeDto { Disponivel = false, Mensagem = "Use de 2 a 30 letras minúsculas, números ou hífen, começando por letra." };
            return await CodigoLivreAsync(inq, c)
                ? new DisponibilidadeDto { Disponivel = true, Mensagem = "Disponível." }
                : new DisponibilidadeDto { Disponivel = false, Mensagem = "Esse nome já está em uso. Escolha outro." };
        }).RequireRateLimiting("cadastro-consulta");

        g.MapPost("/", async (PedidoCadastroRequest req, HttpContext http, Inquilinos inq, ILoggerFactory logs) =>
        {
            // Campo escondido do site preenchido: é robô. Responde como se desse certo e não grava nada.
            if (!string.IsNullOrEmpty(req.Site))
            {
                logs.CreateLogger("Cadastro").LogWarning("Pedido de cadastro descartado (campo escondido preenchido) IP={Ip}",
                    http.Connection.RemoteIpAddress?.ToString());
                return Results.Ok(new PedidoCadastroResposta
                {
                    Id = Guid.NewGuid(), Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24))
                });
            }

            var empresa = (req.Empresa ?? "").Trim();
            var responsavel = (req.Responsavel ?? "").Trim();
            var telefone = Formatos.SoDigitos(req.Telefone);
            var email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim().ToLowerInvariant();
            var doc = string.IsNullOrWhiteSpace(req.Documento) ? null : Formatos.LimparDocumento(req.Documento);
            var codigo = (req.CodigoDesejado ?? "").Trim().ToLowerInvariant();

            if (empresa.Length is 0 or > 120 || !Formatos.TextoSeguro(empresa))
                return Extensoes.Erro("Informe um nome válido da empresa (até 120 caracteres).");
            if (responsavel.Length is 0 or > 120 || !Formatos.TextoSeguro(responsavel))
                return Extensoes.Erro("Informe o nome do responsável, sem caracteres de controle.");
            if (!Formatos.TelefoneDigitadoValido(req.Telefone)) return Extensoes.Erro("Telefone inválido: informe o DDD e o número.");
            if (email is not null && (email.Length > 120 || !Formatos.EmailValido(email))) return Extensoes.Erro("E-mail inválido.");
            if (doc is not null && !Formatos.DocumentoDigitadoValido(req.Documento)) return Extensoes.Erro("CPF/CNPJ inválido.");
            var uf = string.IsNullOrWhiteSpace(req.Uf) ? null : req.Uf.Trim().ToUpperInvariant();
            if (uf is not null && !Formatos.UfValida(uf)) return Extensoes.Erro("UF inválida.");
            if (!Formatos.TextoSeguro(req.Cidade) || (req.Cidade?.Length ?? 0) > 80)
                return Extensoes.Erro("Cidade inválida (até 80 caracteres, sem caracteres de controle).");
            if (!CodigoCliente.Valido(codigo)) return Extensoes.Erro("Nome do servidor inválido: de 2 a 30 letras minúsculas, números ou hífen.");
            var erroAdmin = ValidarAdmin(req.AdminLogin, req.AdminSenha);
            if (erroAdmin is not null) return Extensoes.Erro(erroAdmin);
            if (!await CodigoLivreAsync(inq, codigo)) return Extensoes.Erro("Esse nome do servidor já está em uso. Escolha outro.");

            await using var m = inq.AbrirMaster();
            if (await m.Pedidos.CountAsync(x => x.Situacao == SituacaoPedido.Pendente) >= 300)
                return Extensoes.Erro("Muitos pedidos aguardando. Tente mais tarde ou fale com o suporte.");

            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            var pedido = new PedidoCadastro
            {
                Id = Guid.NewGuid(),
                CriadoEm = DateTime.UtcNow,
                Empresa = empresa,
                Documento = doc,
                Responsavel = responsavel,
                Telefone = telefone,
                Email = email,
                Cidade = string.IsNullOrWhiteSpace(req.Cidade) ? null : req.Cidade.Trim()[..Math.Min(80, req.Cidade.Trim().Length)],
                Uf = uf,
                CodigoDesejado = codigo,
                AdminLogin = req.AdminLogin.Trim().ToLowerInvariant(),
                AdminSenhaHash = Senha.Gerar(req.AdminSenha),
                TokenHash = Hash(token),
                Situacao = SituacaoPedido.Pendente,
                Ip = http.Connection.RemoteIpAddress?.ToString()
            };
            m.Pedidos.Add(pedido);
            await m.SaveChangesAsync();
            return Results.Ok(new PedidoCadastroResposta { Id = pedido.Id, Token = token });
        }).RequireRateLimiting("cadastro-pedido");

        g.MapGet("/{id:guid}", async (Guid id, string? token, Inquilinos inq) =>
        {
            await using var m = inq.AbrirMaster();
            var p = await m.Pedidos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (p is null || string.IsNullOrEmpty(token) ||
                !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(token)), Encoding.ASCII.GetBytes(p.TokenHash)))
                return Results.NotFound();
            return Results.Ok(new SituacaoCadastroDto
            {
                Situacao = p.Situacao.ToString(), Codigo = p.CodigoAprovado, Motivo = p.Motivo, AdminLogin = p.AdminLogin
            });
        }).RequireRateLimiting("cadastro-consulta");

        // Link de download do instalador para quem tem o link do pedido (página "Cadastro aprovado" do site).
        // Só depois de aprovado e com a loja liberada.
        g.MapPost("/{id:guid}/instalador", async (Guid id, string? token, Inquilinos inq, IConfiguration config, IWebHostEnvironment env) =>
        {
            await using var m = inq.AbrirMaster();
            var p = await m.Pedidos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (p is null || string.IsNullOrEmpty(token) ||
                !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(token)), Encoding.ASCII.GetBytes(p.TokenHash)))
                return Results.NotFound();
            if (p.Situacao != SituacaoPedido.Aprovado || string.IsNullOrEmpty(p.CodigoAprovado))
                return Results.Json(new { erro = "O instalador é liberado após a aprovação do cadastro." }, statusCode: 403);
            if (!await SitePublico.LojaLiberadaAsync(inq, p.CodigoAprovado))
                return Results.Json(new { erro = "Acesso suspenso. Fale com o suporte." }, statusCode: 403);
            if (!SitePublico.InstaladorDisponivel(config, env))
                return Results.Json(new { erro = "O instalador ainda não está disponível. Fale com o suporte." }, statusCode: 404);
            return Results.Ok(new { url = "baixar?t=" + BilheteDownload.Gerar(p.CodigoAprovado, config) });
        }).RequireRateLimiting("cadastro-consulta");
    }

    // ---------- Ajudantes ----------

    /// <summary>Cria o banco do cliente, as tabelas, o administrador e o registro no master.</summary>
    private static async Task<(ClienteMaster? cliente, string? erro)> CriarClienteAsync(
        Inquilinos inq, ILogger log, string codigo, string? nome, string? observacao, DadosEmpresa dados, int? limiteCaixas,
        AdminInicial admin, Guid? pedidoEmAprovacao = null)
    {
        nome = (nome ?? "").Trim();
        if (!CodigoCliente.Valido(codigo)) return (null, "Código inválido: de 2 a 30 letras minúsculas, números ou hífen, começando por letra.");
        if (nome.Length is 0 or > 120 || !Formatos.TextoSeguro(nome))
            return (null, "Informe o nome da empresa (até 120 caracteres, sem caracteres inválidos).");
        if (!Formatos.TextoSeguro(observacao, multilinha: true) || (observacao?.Length ?? 0) > 500)
            return (null, "Observação inválida: máximo 500 caracteres.");
        if (!await CodigoLivreAsync(inq, codigo, pedidoEmAprovacao)) return (null, $"Já existe um cliente com o código \"{codigo}\".");

        var banco = Inquilinos.BancoDoCodigo(codigo);
        try
        {
            await inq.CriarBancoSeFaltarAsync(banco);
            await using var db = inq.AbrirCliente(banco);
            await Inicializacao.PrepararAsync(db, admin, log);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao criar o banco do cliente {Codigo}", codigo);
            return (null, "Não foi possível criar o banco do cliente. Confira se o usuário do banco tem permissão CREATEDB " +
                          "(veja o guia, seção Vários clientes). Detalhe: " + ex.Message);
        }

        var cliente = new ClienteMaster
        {
            Id = Guid.NewGuid(), Codigo = codigo, Nome = nome, Banco = banco, Ativo = true, CriadoEm = DateTime.UtcNow,
            Observacao = string.IsNullOrWhiteSpace(observacao) ? null : observacao.Trim()[..Math.Min(500, observacao.Trim().Length)]
        };
        dados.Aplicar(cliente);
        cliente.LimiteCaixas = limiteCaixas;
        await using var m = inq.AbrirMaster();
        m.Clientes.Add(cliente);
        await m.SaveChangesAsync();
        inq.Esquecer(codigo);
        return (cliente, null);
    }

    private static async Task<bool> CodigoLivreAsync(Inquilinos inq, string codigo, Guid? pedidoEmAprovacao = null)
    {
        await using var m = inq.AbrirMaster();
        if (await m.Clientes.AnyAsync(x => x.Codigo == codigo)) return false;

        // Um pedido pendente reserva o codigo, mas nao pode bloquear a sua propria aprovacao.
        var pedidosPendentes = m.Pedidos.Where(x => x.CodigoDesejado == codigo && x.Situacao == SituacaoPedido.Pendente);
        if (pedidoEmAprovacao is Guid id) pedidosPendentes = pedidosPendentes.Where(x => x.Id != id);
        return !await pedidosPendentes.AnyAsync();
    }

    private static string? ValidarAdmin(string? login, string? senha)
    {
        if (!Formatos.LoginValido((login ?? "").Trim().ToLowerInvariant()))
            return "Login do administrador inválido: de 3 a 60 caracteres, letras minúsculas, números, ponto, hífen ou sublinhado.";
        if ((senha ?? "").Length is < 6 or > 64) return "A senha do administrador precisa ter de 6 a 64 caracteres.";
        return null;
    }

    private static async Task<ClienteMasterDto> ResumoAsync(Inquilinos inq, ClienteMaster c)
    {
        var dto = new ClienteMasterDto
        {
            Id = c.Id, Codigo = c.Codigo, Nome = c.Nome, Ativo = c.Ativo, CriadoEm = c.CriadoEm, Observacao = c.Observacao,
            Documento = c.Documento, Responsavel = c.Responsavel, Telefone = c.Telefone, Email = c.Email, Cidade = c.Cidade, Uf = c.Uf,
            LimiteCaixas = c.LimiteCaixas,
            TamanhoBanco = await inq.TamanhoAsync(c.Banco)
        };
        try
        {
            await using var db = inq.AbrirCliente(c.Banco);
            dto.Usuarios = await db.Usuarios.CountAsync(x => x.Ativo);
            dto.Terminais = await db.Terminais.CountAsync(x => x.Ativo);
            dto.UltimaVenda = await db.Vendas.OrderByDescending(x => x.Data).Select(x => (DateTime?)x.Data).FirstOrDefaultAsync();
        }
        catch (Exception)
        {
            // banco fora do ar ou ainda sem tabelas: mostra só o que tem
        }
        return dto;
    }

    /// <summary>Dados da empresa já conferidos e sem máscara (CPF/CNPJ e telefone só dígitos).</summary>
    private sealed record DadosEmpresa(string? Documento, string? Responsavel, string? Telefone, string? Email, string? Cidade, string? Uf)
    {
        public void Aplicar(ClienteMaster c)
        {
            c.Documento = Documento;
            c.Responsavel = Responsavel;
            c.Telefone = Telefone;
            c.Email = Email;
            c.Cidade = Cidade;
            c.Uf = Uf;
        }
    }

    /// <summary>Confere e limpa os dados da empresa (todos opcionais, mas no formato certo quando preenchidos).</summary>
    private static (DadosEmpresa? dados, string? erro) Empresa(string? documento, string? responsavel, string? telefone,
        string? email, string? cidade, string? uf)
    {
        static string? Vazio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        var doc = Vazio(documento) is { } d ? Formatos.LimparDocumento(d) : null;
        if (doc is not null && (doc.Length is not (11 or 14) || !Formatos.DocumentoDigitadoValido(documento)))
            return (null, "CPF/CNPJ inválido ou com caracteres não permitidos.");

        var tel = Vazio(telefone) is { } t ? Formatos.SoDigitos(t) : null;
        if (tel is not null && !Formatos.TelefoneDigitadoValido(telefone))
            return (null, "Telefone inválido: informe o DDD e o número.");

        var mail = Vazio(email)?.ToLowerInvariant();
        if (mail is not null && (mail.Length > 120 || !Formatos.EmailValido(mail))) return (null, "E-mail inválido.");

        var resp = Vazio(responsavel);
        if (resp?.Length > 120 || !Formatos.TextoSeguro(resp))
            return (null, "O nome do responsável pode ter até 120 caracteres, sem controles invisíveis.");

        var cid = Vazio(cidade);
        if (cid?.Length > 80 || !Formatos.TextoSeguro(cid))
            return (null, "A cidade pode ter até 80 caracteres, sem controles invisíveis.");

        var u = Vazio(uf)?.ToUpperInvariant();
        if (u is not null && !Formatos.UfValida(u)) return (null, "UF inválida.");

        return (new DadosEmpresa(doc, resp, tel, mail, cid, u), null);
    }

    /// <summary>0 ou vazio = sem limite; senão de 1 a 999.</summary>
    private static bool LimiteValido(int? limite) => limite is null or (>= 0 and <= 999);
    private static int? Limite(int? limite) => limite is > 0 ? limite : null;

    private static string Hash(string texto) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texto)));

    /// <summary>Confere a chave master (comparação de tempo constante). Sem chave configurada, o Master fica desligado.</summary>
    private static async ValueTask<object?> ConferirChave(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var config = ctx.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var esperada = config["Master:Chave"];
        if (string.IsNullOrWhiteSpace(esperada) || esperada.Length < 32)
            return Results.Json(new { erro = "Projeto JP Master desligado: defina Master__Chave no /etc/erp-nf/api.env." }, statusCode: 503);

        var recebida = ctx.HttpContext.Request.Headers["X-Master-Key"].ToString();
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(recebida));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(esperada));
        if (!CryptographicOperations.FixedTimeEquals(a, b))
            return Results.Json(new { erro = "Chave master inválida." }, statusCode: 401);

        return await next(ctx);
    }
}
