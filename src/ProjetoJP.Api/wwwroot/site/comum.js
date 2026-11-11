'use strict';
// Ajudantes usados pelas páginas do site e pelo painel. Nada aqui usa innerHTML com dados de fora:
// todo texto entra com textContent (via el()), então nada do servidor vira código na página.

const Erp = (() => {
  const moedaFmt = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' });
  const numFmt = new Intl.NumberFormat('pt-BR');

  /** Cria um elemento: el('div', { class: 'x', onclick: fn }, 'texto', outroEl). */
  function el(tag, attrs, ...filhos) {
    const e = document.createElement(tag);
    for (const [k, v] of Object.entries(attrs || {})) {
      if (v === null || v === undefined || v === false) continue;
      if (k.startsWith('on') && typeof v === 'function') e.addEventListener(k.slice(2), v);
      else if (k === 'class') e.className = v;
      else e.setAttribute(k, v === true ? '' : String(v));
    }
    for (const f of filhos.flat()) {
      if (f === null || f === undefined || f === false) continue;
      e.append(f instanceof Node ? f : document.createTextNode(String(f)));
    }
    return e;
  }

  /** "mercado-bom-preco" → "MERCADO BOM PRECO" (como o nome do servidor aparece). */
  const paraTela = (codigo) => String(codigo || '').trim().replace(/-/g, ' ').toUpperCase();

  /** "Mercado Bom Preço" → "mercado-bom-preco" (mesma regra da Gestão: CodigoCliente.DaTela). */
  function daTela(texto) {
    const s = String(texto || '').normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
    let r = '';
    for (const c of s) {
      if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) r += c;
      else if (r.length > 0 && !r.endsWith('-')) r += '-';
    }
    return r.replace(/^-+|-+$/g, '');
  }

  const RESERVADOS = ['api', 'health', 'master', 'cadastro', 'nf', 'padrao', 'admin', 'www',
    'site', 'painel', 'acompanhar', 'privacidade', 'baixar'];

  /** Mesma regra do servidor: 2 a 30, começa por letra, letras/números/hífen, não reservado. */
  const codigoValido = (c) => /^[a-z][a-z0-9-]{1,29}$/.test(c) && !RESERVADOS.includes(c);

  /** Chama a API. Devolve o JSON (ou null). Em erro, lança Error com a mensagem do servidor. */
  async function api(url, opcoes = {}) {
    const cab = { Accept: 'application/json', ...(opcoes.headers || {}) };
    if (opcoes.corpo !== undefined) cab['Content-Type'] = 'application/json';
    let resp;
    try {
      resp = await fetch(url, {
        method: opcoes.metodo || (opcoes.corpo !== undefined ? 'POST' : 'GET'),
        headers: cab,
        body: opcoes.corpo !== undefined ? JSON.stringify(opcoes.corpo) : undefined,
        cache: 'no-store',
        credentials: 'omit'
      });
    } catch {
      throw Object.assign(new Error('Não foi possível conectar ao servidor. Verifique a conexão com a internet e tente novamente.'), { status: 0 });
    }
    let dados = null;
    const texto = await resp.text();
    if (texto) { try { dados = JSON.parse(texto); } catch { dados = null; } }
    if (!resp.ok) {
      let msg = (dados && (dados.erro || dados.title)) || '';
      if (!msg) {
        msg = resp.status === 429 ? 'Limite de tentativas atingido. Aguarde alguns minutos e tente novamente.'
          : resp.status === 401 ? 'Sessão encerrada. Acesse novamente.'
          : resp.status === 403 ? 'Este usuário não tem permissão para esta operação.'
          : resp.status === 404 ? 'Não encontrado.'
          : `O servidor respondeu com erro ${resp.status}.`;
      }
      throw Object.assign(new Error(msg), { status: resp.status });
    }
    return dados;
  }

  // localStorage pode não existir (aba anônima, bloqueado): tudo com try/catch.
  const guardar = {
    ler(chave, area = 'local') {
      try { const v = (area === 'sessao' ? sessionStorage : localStorage).getItem(chave); return v ? JSON.parse(v) : null; }
      catch { return null; }
    },
    gravar(chave, valor, area = 'local') {
      try { (area === 'sessao' ? sessionStorage : localStorage).setItem(chave, JSON.stringify(valor)); } catch { /* sem armazenamento */ }
    },
    apagar(chave, area = 'local') {
      try { (area === 'sessao' ? sessionStorage : localStorage).removeItem(chave); } catch { /* sem armazenamento */ }
    }
  };

  /** Data "2026-11-08" → "08/11/2026". */
  function data(iso) {
    const [a, m, d] = String(iso || '').slice(0, 10).split('-');
    return d && m && a ? `${d}/${m}/${a}` : '';
  }

  /** Data/hora local "2026-11-08T17:34:10" → "17:34". */
  const hora = (iso) => String(iso || '').slice(11, 16);

  function tamanho(bytes) {
    if (!bytes) return '';
    return bytes >= 1048576 ? `${Math.round(bytes / 1048576)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`;
  }

  /** Coloca o contato do suporte (WhatsApp/e-mail) nos elementos marcados com data-contato. */
  function preencherContato(info) {
    for (const a of document.querySelectorAll('[data-contato="whatsapp"]')) {
      if (info && info.whatsapp) { a.href = `https://wa.me/${info.whatsapp}`; a.hidden = false; }
    }
    for (const a of document.querySelectorAll('[data-contato="email"]')) {
      if (info && info.email) { a.href = `mailto:${info.email}`; a.textContent = info.email; a.hidden = false; }
    }
  }

  return {
    el, paraTela, daTela, codigoValido, api, guardar, data, hora, tamanho, preencherContato,
    moeda: (v) => moedaFmt.format(Number(v) || 0),
    numero: (v) => numFmt.format(Number(v) || 0)
  };
})();
