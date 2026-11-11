'use strict';
// Painel da loja (celular, tablet e computador) — só consulta. Usa a API da própria loja (<servidor>/api/...) com o login da Gestão.
// O acesso (token) fica só nesta aba (sessionStorage) e vale até sair ou fechar; servidor e usuário podem ficar lembrados.

(() => {
  const { el } = Erp;
  const $ = (id) => document.getElementById(id);
  const CHAVE_SESSAO = 'erp.painel.sessao';
  const CHAVE_LEMBRAR = 'erp.painel.lembrar';
  const CHAVE_PERIODO = 'erp.painel.periodo';
  const PERIODOS = [['hoje', 'Hoje'], ['ontem', 'Ontem'], ['7dias', '7 dias'], ['mes', 'Mês']];
  const ABAS = ['inicio', 'vendas', 'produtos', 'estoque', 'instalador'];
  const DIAS = ['dom', 'seg', 'ter', 'qua', 'qui', 'sex', 'sáb'];
  const DIAS_LONGOS = ['Domingo', 'Segunda-feira', 'Terça-feira', 'Quarta-feira', 'Quinta-feira', 'Sexta-feira', 'Sábado'];
  const MESES = ['janeiro', 'fevereiro', 'março', 'abril', 'maio', 'junho', 'julho', 'agosto', 'setembro', 'outubro', 'novembro', 'dezembro'];
  const FRACIONADAS = ['KG', 'G', 'L', 'ML', 'M', 'M2'];

  let sessao = Erp.guardar.ler(CHAVE_SESSAO, 'sessao');
  let periodo = Erp.guardar.ler(CHAVE_PERIODO, 'sessao') || 'hoje';
  let resumo = null;          // último /painel carregado { periodo, dados, quando }
  let carregando = 0;         // número da carga atual (ignora respostas antigas)
  let autoAtualizar = 0;

  // ---------- utilidades ----------
  function svg(caminhos, extra) {
    const ns = 'http://www.w3.org/2000/svg';
    const s = document.createElementNS(ns, 'svg');
    s.setAttribute('viewBox', '0 0 24 24'); s.setAttribute('fill', 'none'); s.setAttribute('stroke', 'currentColor');
    s.setAttribute('stroke-width', '2'); s.setAttribute('aria-hidden', 'true');
    for (const d of caminhos) { const p = document.createElementNS(ns, 'path'); p.setAttribute('d', d); s.append(p); }
    if (extra) extra(s, ns);
    return s;
  }
  const iconeAtualizar = () => svg(['M21 12a9 9 0 1 1-3-6.7', 'M21 4v5h-5']);
  const iconeSair = () => svg(['M15 4h4a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1h-4', 'M10 17l5-5-5-5', 'M15 12H3']);
  const iconeExportar = () => svg(['M12 3v12m0 0l-4-4m4 4l4-4', 'M5 21h14']);
  const iconeBusca = () => svg(['M20 20l-4-4'], (s, ns) => { const c = document.createElementNS(ns, 'circle'); c.setAttribute('cx', 11); c.setAttribute('cy', 11); c.setAttribute('r', 7); s.prepend(c); });

  const dataDe = (iso) => { const [a, m, d] = iso.split('-').map(Number); return new Date(a, m - 1, d); };
  const diaMes = (iso) => Erp.data(iso).slice(0, 5);

  function quantidade(qtd, unidade) {
    const u = String(unidade || 'UN').toUpperCase();
    const casas = FRACIONADAS.includes(u) ? 3 : 0;
    return `${Number(qtd || 0).toLocaleString('pt-BR', { minimumFractionDigits: casas, maximumFractionDigits: casas })} ${u.toLowerCase()}`;
  }

  function iniciais(nome) {
    const p = nome.split(/\s+/).filter(Boolean);
    return ((p[0] || '?')[0] + (p[1] ? p[1][0] : (p[0] || '')[1] || '')).toUpperCase();
  }

  /** Sessão salva → id da sessão dentro do token (para "Sair" encerrar no servidor). */
  function sessaoDoToken(token) {
    try {
      const b64 = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
      return JSON.parse(atob(b64.padEnd(b64.length + (4 - b64.length % 4) % 4, '='))).sessao_id || null;
    } catch { return null; }
  }

  // ---------- API da loja ----------
  async function api(caminho, post = false) {
    if (!sessao) throw Object.assign(new Error('Acesse novamente.'), { status: 401 });
    try {
      return await Erp.api(`${sessao.codigo}/api/${caminho}`, { metodo: post ? 'POST' : 'GET', headers: { Authorization: `Bearer ${sessao.token}` } });
    } catch (e) {
      if (e.status === 401) { encerrarLocal(); mostrarLogin(e.message || 'Sessão encerrada. Entre novamente.'); }
      throw e;
    }
  }

  async function carregarResumo(forcar) {
    if (!forcar && resumo && resumo.periodo === periodo && Date.now() - resumo.quando < 60000) return resumo.dados;
    const dados = await api(`relatorios/painel?periodo=${periodo}`);
    resumo = { periodo, dados, quando: Date.now() };
    return dados;
  }

  // ---------- entrar / sair ----------
  function mostrarLogin(erro) {
    clearInterval(autoAtualizar);
    $('tela-painel').hidden = true;
    $('tela-login').hidden = false;
    const lembrar = Erp.guardar.ler(CHAVE_LEMBRAR);
    if (lembrar) {
      if (!$('l-servidor').value) $('l-servidor').value = lembrar.servidor || '';
      if (!$('l-usuario').value) $('l-usuario').value = lembrar.usuario || '';
    }
    $('l-senha').value = '';
    const caixa = $('erro-login');
    caixa.textContent = erro || '';
    caixa.hidden = !erro;
    ($('l-servidor').value ? ($('l-usuario').value ? $('l-senha') : $('l-usuario')) : $('l-servidor')).focus();
  }

  function encerrarLocal() {
    sessao = null;
    resumo = null;
    Erp.guardar.apagar(CHAVE_SESSAO, 'sessao');
  }

  async function sair() {
    const s = sessao;
    const id = s && sessaoDoToken(s.token);
    encerrarLocal();
    mostrarLogin('');
    if (s && id) {
      // Encerra a sessão no servidor também (se falhar, ela vence sozinha).
      Erp.api(`${s.codigo}/api/auth/sessoes/${encodeURIComponent(id)}`, { metodo: 'DELETE', headers: { Authorization: `Bearer ${s.token}` } })
        .catch(() => { /* sem conexão: a sessão expira sozinha */ });
    }
  }

  $('form-login').addEventListener('submit', async (e) => {
    e.preventDefault();
    const erroCaixa = $('erro-login');
    const falha = (campo, msg) => { erroCaixa.textContent = msg; erroCaixa.hidden = false; if (campo) { campo.setAttribute('aria-invalid', 'true'); campo.focus(); } };
    for (const c of ['l-servidor', 'l-usuario', 'l-senha']) $(c).removeAttribute('aria-invalid');
    erroCaixa.hidden = true;

    const codigo = Erp.daTela($('l-servidor').value);
    const usuario = $('l-usuario').value.trim().toLowerCase();
    const senha = $('l-senha').value;
    if (!Erp.codigoValido(codigo)) return falha($('l-servidor'), 'Informe o nome do servidor (o mesmo utilizado na Gestão).');
    if (!usuario) return falha($('l-usuario'), 'Informe o usuário.');
    if (!senha) return falha($('l-senha'), 'Informe a senha.');

    const botao = $('l-entrar');
    botao.disabled = true;
    botao.textContent = 'Entrando…';
    try {
      const r = await Erp.api(`${codigo}/api/auth/login`, { corpo: { login: usuario, senha } });
      const nova = { codigo, token: r.token, nome: r.nome || usuario, expira: r.expiraEm };
      if (!(r.permissoes || []).includes('ver_vendas')) {
        // Sem permissão: encerra a sessão que acabou de abrir.
        const id = sessaoDoToken(r.token);
        if (id) Erp.api(`${codigo}/api/auth/sessoes/${encodeURIComponent(id)}`, { metodo: 'DELETE', headers: { Authorization: `Bearer ${r.token}` } }).catch(() => {});
        falha(null, 'Este usuário não possui a permissão “Ver vendas”. Solicite a liberação ao administrador, na tela Permissões da Gestão.');
        return;
      }
      if ($('l-lembrar').checked) Erp.guardar.gravar(CHAVE_LEMBRAR, { servidor: Erp.paraTela(codigo), usuario });
      else Erp.guardar.apagar(CHAVE_LEMBRAR);
      sessao = nova;
      Erp.guardar.gravar(CHAVE_SESSAO, nova, 'sessao');
      $('l-senha').value = '';
      mostrarPainel();
    } catch (ex) {
      if (ex.status === 401) falha($('l-senha'), 'Usuário ou senha incorretos.');
      else if (ex.status === 404) falha($('l-servidor'), `Servidor "${Erp.paraTela(codigo)}" não encontrado. Verifique o nome informado.`);
      else falha(null, ex.message);
    } finally {
      botao.disabled = false;
      botao.textContent = 'Entrar';
    }
  });
  $('l-servidor').addEventListener('blur', () => {
    const c = Erp.daTela($('l-servidor').value);
    if (c) $('l-servidor').value = Erp.paraTela(c);
  });

  // ---------- montagem das telas ----------
  const telaLarga = window.matchMedia('(min-width: 1024px)');
  const largo = () => telaLarga.matches;

  function abaAtual() {
    const h = location.hash.replace('#', '');
    return ABAS.includes(h) ? h : 'inicio';
  }

  function mostrarPainel() {
    $('tela-login').hidden = true;
    $('tela-painel').hidden = false;
    const nomeLoja = Erp.paraTela(sessao.codigo);
    $('nome-loja').textContent = nomeLoja;
    $('logo-loja').textContent = iniciais(nomeLoja);
    $('nome-usuario').textContent = sessao.nome;
    clearInterval(autoAtualizar);
    // Atualiza sozinho a cada 2 minutos enquanto a tela estiver visível.
    autoAtualizar = setInterval(() => {
      if (document.visibilityState === 'visible' && sessao && abaAtual() !== 'instalador') desenhar(true);
    }, 120000);
    desenhar(false);
  }

  function topo(titulo, subtitulo, { comPeriodo = true, comLogo = false } = {}) {
    const nomeLoja = Erp.paraTela(sessao.codigo);
    const botaoAtualizar = el('button', { class: 'acao', type: 'button', 'aria-label': 'Atualizar', title: 'Atualizar', onclick: () => desenhar(true) },
      iconeAtualizar(), el('span', null, 'Atualizar'));
    const botaoSair = el('button', { class: 'acao sair', type: 'button', 'aria-label': 'Sair', title: 'Sair', onclick: sair }, iconeSair());
    const linha = el('div', { class: 'loja' },
      comLogo ? el('div', { class: 'logo', 'aria-hidden': 'true' }, iniciais(nomeLoja)) : null,
      el('div', { class: 'titulos' }, el('h1', null, titulo || nomeLoja), el('small', { id: 'subtitulo' }, subtitulo || '')),
      botaoAtualizar, botaoSair);
    const filhos = [linha];
    if (comPeriodo) {
      filhos.push(el('div', { class: 'periodo', role: 'group', 'aria-label': 'Período' },
        PERIODOS.map(([valor, rotulo]) => el('button', {
          type: 'button', 'aria-pressed': String(valor === periodo),
          onclick: () => { if (periodo !== valor) { periodo = valor; Erp.guardar.gravar(CHAVE_PERIODO, valor, 'sessao'); desenhar(false); } }
        }, rotulo))));
    }
    $('topo').replaceChildren(...filhos);
    return botaoAtualizar;
  }

  function erroNaTela(msg) {
    $('corpo').replaceChildren(el('div', { class: 'aviso-erro', role: 'alert' }, el('span', null, msg),
      el('button', { type: 'button', onclick: () => desenhar(true) }, 'Tentar novamente')));
  }

  async function desenhar(forcar) {
    if (!sessao) { mostrarLogin(''); return; }
    const aba = abaAtual();
    for (const a of document.querySelectorAll('.nav a')) {
      if (a.dataset.aba === aba) a.setAttribute('aria-current', 'page'); else a.removeAttribute('aria-current');
    }
    const minha = ++carregando;
    const ainda = () => minha === carregando && sessao;
    const nomes = { inicio: largo() ? 'Início' : null, vendas: 'Vendas', produtos: 'Mais vendidos', estoque: 'Estoque baixo', instalador: 'Instalador' };
    const subtitulos = { estoque: 'Produtos no mínimo ou abaixo', instalador: 'PDV e Gestão para Windows' };
    const botao = topo(nomes[aba], subtitulos[aba] || '', {
      comPeriodo: aba !== 'estoque' && aba !== 'instalador',
      comLogo: aba === 'inicio'
    });
    if (!forcar || !$('corpo').firstChild) {
      $('corpo').replaceChildren(el('div', { class: 'carregando' }, 'Carregando…'));
      window.scrollTo(0, 0); // outra aba ou outro período: começa do topo
    }
    botao.classList.add('girando');
    try {
      if (aba === 'inicio') await telaInicio(forcar, ainda);
      else if (aba === 'vendas') await telaVendas(forcar, ainda);
      else if (aba === 'produtos') await telaProdutos(forcar, ainda);
      else if (aba === 'estoque') await telaEstoque(ainda);
      else await telaInstalador(ainda);
    } catch (e) {
      if (ainda()) erroNaTela(e.status === 403 ? 'Este usuário não tem permissão para consultar as vendas.' : e.message);
    } finally {
      botao.classList.remove('girando');
    }
  }

  function descricaoPeriodo(r) {
    if (r.periodo === 'hoje' || r.periodo === 'ontem') {
      const d = dataDe(r.de);
      return `${DIAS_LONGOS[d.getDay()]}, ${d.getDate()} de ${MESES[d.getMonth()]}`;
    }
    if (r.periodo === 'mes') { const m = MESES[dataDe(r.de).getMonth()]; return `${m[0].toUpperCase()}${m.slice(1)} · até ${diaMes(r.ate)}`; }
    return `${diaMes(r.de)} a ${diaMes(r.ate)}`;
  }

  /** Tabela simples: colunas [{ t: 'Total', n: true }], linhas = arrays de células (texto, Node ou { v, classe }). */
  function tabela(colunas, linhas) {
    return el('table', { class: 'tabela' },
      el('thead', null, el('tr', null, colunas.map((c) => el('th', { class: c.n ? 'n' : null }, c.t)))),
      el('tbody', null, linhas.map((cels) => el('tr', null, cels.map((c, i) => {
        const obj = c !== null && typeof c === 'object' && !(c instanceof Node) ? c : { v: c };
        return el('td', { class: [colunas[i].n ? 'n' : '', obj.classe || ''].join(' ').trim() || null }, obj.v);
      })))));
  }

  /** Lista que mostra aos poucos (botão "Mostrar mais"). */
  function aosPoucos(itens, passo, desenharItem, recipiente) {
    const mais = el('button', { class: 'mais', type: 'button' });
    let mostrados = 0;
    function mostrar() {
      const ate = Math.min(itens.length, mostrados + passo);
      for (; mostrados < ate; mostrados++) recipiente.append(desenharItem(itens[mostrados], mostrados));
      mais.hidden = mostrados >= itens.length;
      mais.textContent = `Mostrar mais (${itens.length - mostrados} restantes)`;
    }
    mais.addEventListener('click', mostrar);
    mostrar();
    return mais;
  }

  const vazio = (texto) => el('div', { class: 'lista' }, el('div', { class: 'vazio' }, texto));

  // ---------- planilha (CSV para o Excel) ----------
  /** Número com vírgula, sem separador de milhar (o Excel em português lê como número). */
  const N = (valor, casas = 2) => ({ n: Number(valor) || 0, casas });
  /** Código só com números (ex.: código de barras): o Excel mantém como texto (sem perder zeros nem virar 7,89E+12). */
  const COD = (codigo) => ({ cod: String(codigo || '') });

  function baixarPlanilha(nome, cabecalho, linhas) {
    const celula = (v) => {
      if (v === null || v === undefined) return '';
      if (typeof v === 'object' && 'n' in v) {
        return v.n.toLocaleString('pt-BR', { minimumFractionDigits: v.casas, maximumFractionDigits: v.casas, useGrouping: false });
      }
      if (typeof v === 'object' && 'cod' in v) return /^\d+$/.test(v.cod) ? `="${v.cod}"` : celula(v.cod);
      let t = String(v);
      if (/^[=+\-@\t\r]/.test(t)) t = `'${t}`; // texto que o Excel trataria como fórmula
      return /[";\r\n]/.test(t) ? `"${t.replace(/"/g, '""')}"` : t;
    };
    const texto = '\uFEFF' + [cabecalho, ...linhas].map((l) => l.map(celula).join(';')).join('\r\n');
    const url = URL.createObjectURL(new Blob([texto], { type: 'text/csv;charset=utf-8' }));
    const a = el('a', { href: url, download: nome });
    document.body.append(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 15000);
  }

  const nomeArquivo = (base, r) => `${base}_${r.de === r.ate ? r.de : `${r.de}_a_${r.ate}`}_${sessao.codigo}.csv`;

  function barraAcoes(texto, aoClicar) {
    return el('div', { class: 'barra-acoes' },
      el('button', { class: 'exportar', type: 'button', onclick: aoClicar }, iconeExportar(), el('span', null, texto)));
  }
  const quandoVenda = (v, variosDias) => (variosDias ? `${diaMes(v.data)} ${Erp.hora(v.data)}` : Erp.hora(v.data));

  // ---------- Início ----------
  async function telaInicio(forcar, ainda) {
    const r = await carregarResumo(forcar);
    if (!ainda()) return;
    const pc = largo();
    $('subtitulo').textContent = descricaoPeriodo(r) + (pc ? ` · atualizado às ${Erp.hora(r.atualizadoEm)}` : '');
    const rotulos = { hoje: 'Vendido hoje', ontem: 'Vendido ontem', '7dias': 'Vendido em 7 dias', mes: 'Vendido no mês' };

    let comparacao = null;
    if (r.totalAnterior > 0) {
      const pct = Math.round((r.total / r.totalAnterior - 1) * 100);
      comparacao = el('span', { class: 'c' }, pct >= 0 ? `▲ ${pct}%` : `▼ ${Math.abs(pct)}%`, ` em relação a ${r.comparadoCom}`);
    } else if (r.total > 0) {
      comparacao = el('span', { class: 'c' }, `Sem vendas em ${r.comparadoCom} para comparação`);
    }

    const kpis = el('div', { class: 'kpis' },
      el('div', { class: 'kpi total' }, el('span', null, rotulos[r.periodo] || 'Vendido'), el('b', null, Erp.moeda(r.total)), comparacao),
      el('div', { class: 'kpi' }, el('span', null, 'Vendas'), el('b', null, Erp.numero(r.vendas)),
        el('small', null, r.canceladas === 1 ? '1 cancelada' : `${Erp.numero(r.canceladas)} canceladas`)),
      el('div', { class: 'kpi' }, el('span', null, 'Ticket médio'), el('b', null, Erp.moeda(r.ticketMedio)), el('small', null, 'por venda')),
      el('div', { class: 'kpi' }, el('span', null, 'Devoluções'), el('b', null, Erp.moeda(r.devolvido)), el('small', null, 'já deduzidas')));

    // Gráfico dos 7 dias que terminam no fim do período.
    const maior = Math.max(...r.dias.map((d) => d.total), 0);
    const barras = el('div', { class: 'barras', role: 'img', 'aria-label': 'Vendas dos últimos 7 dias' },
      r.dias.map((d, i) => {
        const barra = el('i', { title: `${Erp.data(d.dia)}: ${Erp.moeda(d.total)} (${d.vendas} vendas)` });
        barra.style.height = `${maior > 0 ? Math.max(3, Math.round(d.total / maior * 100)) : 3}%`;
        const ultimo = i === r.dias.length - 1;
        const nome = ultimo && d.dia === r.hoje ? 'hoje' : DIAS[dataDe(d.dia).getDay()];
        const valor = Erp.moeda(d.total).replace(/,\d\d$/, '');
        return el('div', { class: ultimo ? 'fim' : '' },
          el('em', { class: 'todos' }, valor),
          ultimo ? el('em', { class: 'so-fim' }, valor) : null,
          barra, el('small', null, nome));
      }));
    const grafico = el('div', { class: 'card' },
      el('h2', null, 'Últimos 7 dias', el('button', { type: 'button', onclick: () => { location.hash = 'vendas'; } }, 'Ver vendas')),
      barras);

    const formas = (r.formas || []).filter((f) => f.total !== 0);
    const maiorForma = Math.max(...formas.map((f) => f.total), 0);
    const cartaoFormas = el('div', { class: 'card' }, el('h2', null, 'Formas de pagamento'),
      formas.length === 0 ? el('div', { class: 'vazio' }, 'Nenhuma venda no período.') :
        el('div', { class: 'forma' }, formas.map((f) => {
          if (f.total < 0) return el('div', { class: 'l neg' }, el('span', null, 'Devoluções'), el('div'), el('b', null, Erp.moeda(f.total)));
          const trilho = el('i');
          trilho.style.width = `${maiorForma > 0 ? Math.max(2, Math.round(f.total / maiorForma * 100)) : 0}%`;
          return el('div', { class: 'l' }, el('span', { title: f.forma }, f.forma), el('div', { class: 'trilho' }, trilho), el('b', null, Erp.moeda(f.total)));
        })));

    const blocos = [kpis, el('div', { class: 'linha2' }, grafico, cartaoFormas)];

    if (pc) {
      // No computador: por caixa, últimas vendas e estoque baixo na mesma tela.
      const [vendas, estoque] = await Promise.all([
        api(`relatorios/vendas?de=${r.de}&ate=${r.ate}`).catch(() => null),
        api('relatorios/estoque-baixo').catch(() => null)
      ]);
      if (!ainda()) return;
      const variosDias = r.de !== r.ate;
      const caixas = el('div', { class: 'card' }, el('h2', null, 'Por caixa'),
        (r.caixas || []).length ? el('div', { class: 'resumo' }, r.caixas.map((c) =>
          el('div', null, `${c.terminal} · ${c.vendas} ${c.vendas === 1 ? 'venda' : 'vendas'}`, el('b', null, Erp.moeda(c.total)))))
          : el('div', { class: 'vazio' }, 'Nenhuma venda no período.'));
      const ultimas = el('div', { class: 'card sem-borda-int' },
        el('h2', null, 'Últimas vendas', el('button', { type: 'button', onclick: () => { location.hash = 'vendas'; } }, 'Ver todas')),
        !vendas ? el('div', { class: 'vazio' }, 'Não foi possível carregar.') : vendas.length === 0 ? el('div', { class: 'vazio' }, 'Nenhuma venda no período.') :
          tabela([{ t: 'Nº' }, { t: variosDias ? 'Data' : 'Hora' }, { t: 'Caixa' }, { t: 'Total', n: true }],
            vendas.slice(0, 6).map((v) => [
              v.status === 1 ? el('span', null, String(v.numero), el('span', { class: 'tag canc' }, 'CANCELADA')) : String(v.numero),
              quandoVenda(v, variosDias), v.terminal,
              { v: Erp.moeda(v.total), classe: v.status === 1 ? 'riscado' : 'forte' }])));
      const baixos = (estoque || []).slice().sort((a, b) => (a.estoqueAtual / (a.estoqueMinimo || 1)) - (b.estoqueAtual / (b.estoqueMinimo || 1)));
      const cartaoEstoque = el('div', { class: 'card sem-borda-int' },
        el('h2', null, 'Estoque baixo', el('button', { type: 'button', onclick: () => { location.hash = 'estoque'; } }, `Ver todos (${baixos.length})`)),
        !estoque ? el('div', { class: 'vazio' }, 'Não foi possível carregar.') : baixos.length === 0 ? el('div', { class: 'vazio' }, 'Nenhum produto com estoque baixo.') :
          tabela([{ t: 'Produto' }, { t: 'Tem', n: true }, { t: 'Mínimo', n: true }],
            baixos.slice(0, 6).map((p) => [p.descricao,
              { v: quantidade(p.estoqueAtual, p.unidade), classe: p.estoqueAtual <= 0 ? 'zero' : 'baixo' },
              quantidade(p.estoqueMinimo, p.unidade)])));
      blocos.push(el('div', { class: 'linha3' }, caixas, ultimas, cartaoEstoque));
    }

    blocos.push(el('div', { class: 'nota' }, pc ? 'As vendas de caixas sem conexão são exibidas após o envio.'
      : `Atualizado às ${Erp.hora(r.atualizadoEm)}. As vendas de caixas sem conexão são exibidas após o envio.`));
    if (!pc) blocos.push(el('button', { class: 'sair-link', type: 'button', onclick: sair }, `Sair (${sessao.nome})`));
    $('corpo').replaceChildren(...blocos);
  }

  // ---------- Vendas ----------
  async function telaVendas(forcar, ainda) {
    const r = await carregarResumo(forcar);
    const vendas = await api(`relatorios/vendas?de=${r.de}&ate=${r.ate}`);
    if (!ainda()) return;
    $('subtitulo').textContent = `${descricaoPeriodo(r)} · ${Erp.numero(r.vendas)} vendas · ${Erp.moeda(r.total)}`;
    if (vendas.length === 0) { $('corpo').replaceChildren(vazio('Nenhuma venda no período.')); return; }
    const variosDias = r.de !== r.ate;

    const caixas = (r.caixas || []).length ? el('div', { class: 'resumo' },
      r.caixas.map((c) => el('div', null, `${c.terminal} · ${c.vendas}`, el('b', null, Erp.moeda(c.total))))) : '';
    const limite = vendas.length >= 2000 ? el('div', { class: 'nota' }, 'A lista mostra as 2.000 vendas mais recentes do período.') : '';

    if (largo()) {
      const corpoTabela = el('tbody');
      const t = el('table', { class: 'tabela' },
        el('thead', null, el('tr', null, ['Nº', variosDias ? 'Data' : 'Hora', 'Caixa', 'Operador', 'Cliente', 'Pagamento', 'Situação'].map((x) => el('th', null, x)),
          el('th', { class: 'n' }, 'Devolvido'), el('th', { class: 'n' }, 'Total'))),
        corpoTabela);
      const mais = aosPoucos(vendas, 100, (v) => {
        const cancelada = v.status === 1;
        return el('tr', null,
          el('td', { class: 'forte' }, String(v.numero)), el('td', null, quandoVenda(v, variosDias)), el('td', null, v.terminal),
          el('td', null, v.operador), el('td', null, v.cliente || '—'), el('td', null, v.pagamento || '—'),
          el('td', null, cancelada ? el('span', { class: 'tag canc' }, 'CANCELADA') : v.devolvido > 0 ? el('span', { class: 'tag dev' }, 'DEVOLUÇÃO') : 'Concluída'),
          el('td', { class: 'n' }, v.devolvido > 0 ? Erp.moeda(v.devolvido) : '—'),
          el('td', { class: cancelada ? 'n riscado' : 'n forte' }, Erp.moeda(v.total)));
      }, corpoTabela);
      const exportar = barraAcoes('Exportar planilha', () => baixarPlanilha(nomeArquivo('vendas', r),
        ['Nº', 'Data', 'Hora', 'Caixa', 'Operador', 'Cliente', 'Pagamento', 'Situação', 'Devolvido', 'Total'],
        vendas.map((v) => [String(v.numero), Erp.data(v.data), Erp.hora(v.data), v.terminal, v.operador, v.cliente || '', v.pagamento || '',
          v.status === 1 ? 'Cancelada' : v.devolvido > 0 ? 'Com devolução' : 'Concluída', N(v.devolvido), N(v.total)])));
      $('corpo').replaceChildren(caixas, exportar, el('div', { class: 'card sem-borda-int' }, t), mais, limite);
      return;
    }

    const lista = el('div', { class: 'lista' });
    const mais = aosPoucos(vendas, 60, (v) => {
      const cancelada = v.status === 1;
      const detalhe = [quandoVenda(v, variosDias), v.terminal, v.operador, v.cliente].filter(Boolean).join(' · ');
      return el('div', { class: 'item' },
        el('div', { class: 't' },
          el('b', null, `Nº ${v.numero}`,
            cancelada ? el('span', { class: 'tag canc' }, 'CANCELADA') : null,
            !cancelada && v.devolvido > 0 ? el('span', { class: 'tag dev' }, 'DEVOLUÇÃO') : null),
          el('small', null, detalhe)),
        el('div', { class: cancelada ? 'v riscado' : 'v' }, Erp.moeda(v.total),
          !cancelada && v.devolvido > 0 ? el('small', null, `devolvido ${Erp.moeda(v.devolvido)}`)
            : v.pagamento ? el('small', null, v.pagamento) : null));
    }, lista);
    $('corpo').replaceChildren(caixas, lista, mais, limite);
  }

  // ---------- Mais vendidos ----------
  async function telaProdutos(forcar, ainda) {
    const r = await carregarResumo(forcar);
    const itens = await api(`relatorios/mais-vendidos?de=${r.de}&ate=${r.ate}`);
    if (!ainda()) return;
    $('subtitulo').textContent = descricaoPeriodo(r);
    if (itens.length === 0) { $('corpo').replaceChildren(vazio('Nenhum produto vendido no período.')); return; }

    if (largo()) {
      const corpoTabela = el('tbody');
      const t = el('table', { class: 'tabela' },
        el('thead', null, el('tr', null, el('th', null, '#'), el('th', null, 'Produto'), el('th', null, 'Código'),
          el('th', { class: 'n' }, 'Quantidade'), el('th', { class: 'n' }, 'Total'))),
        corpoTabela);
      const mais = aosPoucos(itens, 50, (p, i) => el('tr', null,
        el('td', null, el('span', { class: i < 3 ? 'pos top' : 'pos' }, String(i + 1))),
        el('td', { class: 'forte' }, p.descricao), el('td', null, p.codigo || '—'),
        el('td', { class: 'n' }, quantidade(p.quantidade, p.unidade)), el('td', { class: 'n forte' }, Erp.moeda(p.total))), corpoTabela);
      const exportar = barraAcoes('Exportar planilha', () => baixarPlanilha(nomeArquivo('mais-vendidos', r),
        ['Posição', 'Código', 'Produto', 'Quantidade', 'Unidade', 'Total'],
        itens.map((p, i) => [String(i + 1), COD(p.codigo), p.descricao,
          N(p.quantidade, FRACIONADAS.includes(String(p.unidade || '').toUpperCase()) ? 3 : 0), String(p.unidade || 'UN').toUpperCase(), N(p.total)])));
      $('corpo').replaceChildren(exportar, el('div', { class: 'card sem-borda-int' }, t), mais);
      return;
    }

    const lista = el('div', { class: 'lista' });
    const mais = aosPoucos(itens, 30, (p, i) => el('div', { class: 'item' },
      el('span', { class: i < 3 ? 'pos top' : 'pos' }, String(i + 1)),
      el('div', { class: 't' }, el('b', { title: p.descricao }, p.descricao), el('small', null, quantidade(p.quantidade, p.unidade))),
      el('div', { class: 'v' }, Erp.moeda(p.total))), lista);
    $('corpo').replaceChildren(lista, mais);
  }

  // ---------- Estoque baixo ----------
  async function telaEstoque(ainda) {
    const itens = await api('relatorios/estoque-baixo');
    if (!ainda()) return;
    const zerados = itens.filter((p) => p.estoqueAtual <= 0);
    itens.sort((a, b) => {
      const ra = a.estoqueMinimo > 0 ? a.estoqueAtual / a.estoqueMinimo : 0;
      const rb = b.estoqueMinimo > 0 ? b.estoqueAtual / b.estoqueMinimo : 0;
      return ra - rb || a.descricao.localeCompare(b.descricao, 'pt-BR');
    });
    $('subtitulo').textContent = itens.length ? `${itens.length} produtos no mínimo ou abaixo` : 'Produtos no mínimo ou abaixo';
    if (itens.length === 0) { $('corpo').replaceChildren(vazio('Nenhum produto com estoque baixo.')); return; }

    const resumoEl = el('div', { class: 'resumo' },
      el('div', { class: 'perigo' }, 'Zerados ou negativos', el('b', null, Erp.numero(zerados.length))),
      el('div', { class: 'alerta' }, 'No mínimo ou abaixo', el('b', null, Erp.numero(itens.length - zerados.length))));
    const campo = el('input', { type: 'search', placeholder: 'Procurar produto', 'aria-label': 'Procurar produto', autocomplete: 'off' });
    const pc = largo();
    const lista = pc ? el('div', { class: 'card sem-borda-int' }) : el('div', { class: 'lista' });

    const barraDe = (p) => {
      const barra = el('i', { class: p.estoqueAtual <= 0 ? 'zero' : '' });
      barra.style.width = `${p.estoqueMinimo > 0 ? Math.min(100, Math.max(2, Math.round(p.estoqueAtual / p.estoqueMinimo * 100))) : 2}%`;
      return el('div', { class: 'est' }, barra);
    };
    function filtrar() {
      const termo = Erp.daTela(campo.value).replace(/-/g, ' ');
      const achados = itens.filter((p) => !termo || Erp.daTela(`${p.descricao} ${p.codigo}`).replace(/-/g, ' ').includes(termo)).slice(0, 300);
      if (achados.length === 0) { lista.replaceChildren(el('div', { class: 'vazio' }, 'Nenhum produto encontrado.')); return; }
      if (pc) {
        lista.replaceChildren(tabela([{ t: 'Produto' }, { t: 'Código' }, { t: 'Tem', n: true }, { t: 'Mínimo', n: true }, { t: 'Situação' }],
          achados.map((p) => [{ v: p.descricao, classe: 'forte' }, p.codigo,
            { v: quantidade(p.estoqueAtual, p.unidade), classe: p.estoqueAtual <= 0 ? 'zero' : 'baixo' },
            quantidade(p.estoqueMinimo, p.unidade), { v: barraDe(p), classe: 'barra' }])));
        return;
      }
      lista.replaceChildren(...achados.map((p) => el('div', { class: 'item' }, el('div', { class: 't' },
        el('b', { title: p.descricao }, p.descricao),
        el('small', null, `Tem ${quantidade(p.estoqueAtual, p.unidade)} · mínimo ${quantidade(p.estoqueMinimo, p.unidade)}`),
        barraDe(p)))));
    }
    campo.addEventListener('input', filtrar);
    filtrar();
    $('corpo').replaceChildren(resumoEl, el('label', { class: 'busca' }, iconeBusca(), campo), lista);
  }

  // ---------- Instalador ----------
  // O download só sai com um link gerado aqui (vale 30 minutos): a loja já está liberada, senão nem entraria.
  async function telaInstalador(ainda) {
    let info = null;
    try { info = await Erp.api('site/info'); } catch { info = null; }
    if (!ainda()) return;
    const versao = info && info.instalador
      ? `Versão de ${Erp.data(info.instalador.data)} · aprox. ${Erp.tamanho(info.instalador.tamanho)}`
      : 'O instalador ainda não está disponível. Entre em contato com o suporte.';
    const erro = el('p', { class: 'erro', role: 'alert', hidden: true });
    const botao = el('button', { class: 'baixar', type: 'button', disabled: !(info && info.instalador) }, 'Baixar o instalador');
    botao.addEventListener('click', async () => {
      botao.disabled = true;
      erro.hidden = true;
      try {
        const r = await api('instalador/link', true);
        location.href = r.url;
      } catch (e) {
        if (sessao) { erro.textContent = e.message; erro.hidden = false; }
      } finally {
        setTimeout(() => { botao.disabled = false; }, 1500);
      }
    });
    $('corpo').replaceChildren(el('div', { class: 'card instalador' },
      el('h2', null, 'Instalar-ProjetoJP.exe'),
      el('p', null, versao),
      el('p', null, 'Durante a instalação, é possível escolher os programas (PDV, Gestão ou ambos) e a pasta de destino. ' +
        'Faça o download no computador onde o sistema será instalado.'),
      el('ul', null,
        el('li', null, el('span', null, 'Sistema operacional'), el('b', null, 'Windows 10 ou 11')),
        el('li', null, el('span', null, 'Requisitos mínimos'), el('b', null, '32 ou 64 bits, 4 GB de memória')),
        el('li', null, el('span', null, 'Internet'), el('b', null, 'Necessária apenas para o envio das vendas'))),
      botao, erro,
      el('p', { class: 'nota' }, 'Caso o Windows exiba a mensagem “O Windows protegeu o computador”, clique em Mais informações e, em seguida, em Executar assim mesmo.')));
  }

  // ---------- início ----------
  $('bt-sair').addEventListener('click', sair);
  window.addEventListener('hashchange', () => { if (sessao) desenhar(false); });
  // Mudou de tela pequena para grande (ou girou o tablet): monta de novo no formato certo.
  telaLarga.addEventListener('change', () => { if (sessao) desenhar(false); });
  document.addEventListener('visibilitychange', () => {
    // Voltou para o painel depois de um tempo: atualiza.
    if (document.visibilityState === 'visible' && sessao && resumo && Date.now() - resumo.quando > 120000 && abaAtual() !== 'instalador') desenhar(true);
  });
  if (sessao && sessao.token && sessao.codigo && (!sessao.expira || new Date(sessao.expira) > new Date())) mostrarPainel();
  else { encerrarLocal(); mostrarLogin(''); }
})();
