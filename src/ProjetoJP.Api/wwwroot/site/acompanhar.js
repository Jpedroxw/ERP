'use strict';
// Situação do cadastro: o link tem "#<id>.<código secreto>" (depois do #, então não vai para o servidor nos logs).
// Consulta GET cadastro/<id>?token=<código> — devolve Pendente, Aprovado ou Recusado.

(() => {
  const CHAVE_PEDIDO = 'erp.pedido';
  const conteudo = document.getElementById('conteudo');
  const { el } = Erp;
  let atualizar = 0;
  let mostrada = '';        // situação que está na tela (para não redesenhar à toa)
  const INTERVALO = 10000;  // enquanto estiver em análise, confere a cada 10 segundos

  // Em análise: confere de novo sozinho. Assim que o Master aprovar (ou recusar), a página muda sem recarregar.
  function agendar(id, token, local) {
    clearTimeout(atualizar);
    atualizar = setTimeout(() => {
      // Aba escondida (celular bloqueado, outra aba): espera voltar; o "visibilitychange" confere na hora.
      if (document.visibilityState === 'visible') carregar(id, token, local, true);
      else agendar(id, token, local);
    }, INTERVALO);
  }

  function icone(tipo) {
    const ns = 'http://www.w3.org/2000/svg';
    const svg = document.createElementNS(ns, 'svg');
    for (const [k, v] of Object.entries({ width: 28, height: 28, fill: 'none', stroke: 'currentColor', 'stroke-width': 2.4, viewBox: '0 0 24 24', 'aria-hidden': 'true' })) svg.setAttribute(k, v);
    const caminhos = { espera: ['M12 7v5l3 2'], ok: ['M5 12l5 5 9-10'], erro: ['M8 8l8 8M16 8l-8 8'] }[tipo];
    if (tipo === 'espera') { const c = document.createElementNS(ns, 'circle'); c.setAttribute('cx', 12); c.setAttribute('cy', 12); c.setAttribute('r', 9); svg.append(c); }
    for (const d of caminhos) { const p = document.createElementNS(ns, 'path'); p.setAttribute('d', d); svg.append(p); }
    return el('div', { class: `icone-grande ${tipo}` }, svg);
  }

  function trilha(situacao) {
    const etapa = (classe, marca, texto) => el('div', { class: `etapa ${classe}` }, el('b', null, marca), texto);
    if (situacao === 'Aprovado') return el('div', { class: 'trilha' }, etapa('feita', '✓', 'Enviado'), etapa('feita', '✓', 'Em análise'), etapa('feita', '✓', 'Aprovado'));
    if (situacao === 'Recusado') return el('div', { class: 'trilha' }, etapa('feita', '✓', 'Enviado'), etapa('feita', '✓', 'Em análise'), etapa('recusado', '✕', 'Não aprovado'));
    return el('div', { class: 'trilha' }, etapa('feita', '✓', 'Enviado'), etapa('agora', '2', 'Em análise'), etapa('', '3', 'Aprovado'));
  }

  const linha = (rotulo, valor, classe) => el('div', null, el('span', null, rotulo), el('b', { class: classe }, valor));

  function botaoCopiar() {
    const b = el('button', { type: 'button' }, 'Copiar link');
    b.addEventListener('click', async () => {
      try { await navigator.clipboard.writeText(location.href); b.textContent = 'Link copiado'; }
      catch { b.textContent = 'Copie o endereço exibido na barra do navegador'; }
    });
    return b;
  }

  function mostrar(...filhos) { conteudo.replaceChildren(...filhos); }

  // Download só para cadastro aprovado: pede ao servidor um link que vale 30 minutos.
  function botaoBaixar(id, token) {
    const aviso = el('p', { class: 'erro-baixar', role: 'alert', hidden: true });
    const b = el('button', { class: 'bt bt-prim', type: 'button' }, 'Baixar o instalador');
    b.addEventListener('click', async () => {
      b.disabled = true;
      aviso.hidden = true;
      try {
        const r = await Erp.api(`cadastro/${encodeURIComponent(id)}/instalador?token=${encodeURIComponent(token)}`, { metodo: 'POST' });
        location.href = r.url;
      } catch (e) {
        aviso.textContent = e.message;
        aviso.hidden = false;
      } finally {
        setTimeout(() => { b.disabled = false; }, 1500);
      }
    });
    return el('div', null, b, aviso);
  }

  function semCadastro(texto) {
    mostrar(
      icone('erro'),
      el('h1', null, 'Solicitação não localizada'),
      el('p', null, texto),
      el('a', { class: 'bt bt-prim', href: './#cadastro' }, 'Solicitar cadastro')
    );
  }

  async function carregar(id, token, local, emSegundoPlano = false) {
    let r;
    try {
      r = await Erp.api(`cadastro/${encodeURIComponent(id)}?token=${encodeURIComponent(token)}`);
    } catch (e) {
      // Falha numa conferência automática (internet oscilou): mantém a tela e tenta de novo depois.
      if (emSegundoPlano && e.status !== 404) { agendar(id, token, local); return; }
      if (e.status === 404) {
        mostrada = '';
        clearTimeout(atualizar);
        semCadastro('O link está incompleto ou a solicitação não existe mais. Verifique o link ou faça uma nova solicitação.');
      } else {
        mostrar(icone('erro'), el('h1', null, 'Não foi possível consultar a situação'), el('p', null, e.message),
          el('button', { class: 'bt bt-prim', type: 'button', onclick: () => carregar(id, token, local) }, 'Tentar novamente'));
      }
      return;
    }

    const situacao = r.situacao;
    const admin = r.adminLogin || (local && local.admin) || '';
    if (situacao === 'Pendente' && mostrada === 'Pendente') { agendar(id, token, local); return; } // nada mudou
    const mudou = mostrada === 'Pendente' && situacao !== 'Pendente';
    mostrada = situacao;
    if (mudou) document.title = situacao === 'Aprovado' ? 'Cadastro aprovado | Projeto JP' : 'Situação do cadastro | Projeto JP';

    if (situacao === 'Aprovado') {
      const servidor = Erp.paraTela(r.codigo);
      mostrar(
        icone('ok'),
        el('h1', null, 'Cadastro aprovado'),
        el('p', null, 'O servidor da empresa já está disponível. Anote os dados de acesso abaixo.'),
        trilha(situacao),
        el('div', { class: 'dados' },
          linha('Nome do servidor', servidor, 'grande'),
          linha('Usuário administrador', admin),
          linha('Senha', 'a informada na solicitação')),
        el('ol', { class: 'passos' },
          el('li', null, el('span', null, el('b', null, 'Baixe e instale'), ' o PDV e a Gestão.')),
          el('li', null, el('span', null, 'Na Gestão, informe o servidor ', el('b', null, servidor), ' e acesse com o usuário ', el('b', null, admin), '.')),
          el('li', null, el('span', null, 'Cadastre os produtos e os caixas para iniciar as vendas.')),
          el('li', null, el('span', null, 'As vendas podem ser acompanhadas pelo celular no ', el('b', null, 'Painel online'), ', com o mesmo usuário.'))),
        botaoBaixar(id, token),
        el('a', { class: 'bt bt-ouro', href: 'painel' }, 'Acessar o painel online')
      );
      if (mudou) conteudo.classList.add('acabou-de-mudar');
      return;
    }

    if (situacao === 'Recusado') {
      mostrar(
        icone('erro'),
        el('h1', null, 'Cadastro não aprovado'),
        el('p', null, r.motivo ? 'Motivo: ' : 'Para mais informações, entre em contato com o suporte.', r.motivo ? el('b', null, r.motivo) : null),
        trilha(situacao),
        el('a', {
          class: 'bt bt-prim', href: './#cadastro',
          onclick: () => Erp.guardar.apagar(CHAVE_PEDIDO)
        }, 'Nova solicitação')
      );
      return;
    }

    // Pendente
    const dados = [];
    if (local && local.codigo) dados.push(linha('Nome do servidor solicitado', Erp.paraTela(local.codigo)));
    if (admin) dados.push(linha('Usuário administrador', admin));
    if (local && local.enviadoEm) {
      const d = new Date(local.enviadoEm);
      if (!isNaN(d)) dados.push(linha('Data da solicitação', d.toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' }).replace(',', ' às')));
    }
    mostrar(
      icone('espera'),
      el('h1', null, 'Solicitação recebida'),
      el('p', null, 'A solicitação de cadastro', local && local.empresa ? [' da empresa ', el('b', null, local.empresa)] : '',
        ' foi recebida e está em análise. Após a aprovação, esta página exibirá o nome do servidor e as instruções de instalação.'),
      trilha(situacao),
      dados.length ? el('div', { class: 'dados' }, dados) : null,
      el('div', { class: 'link-guardar' }, 'Guarde este link para consultar a situação do cadastro. A consulta também pode ser feita neste mesmo dispositivo. ' +
        'O instalador será disponibilizado nesta página após a aprovação.', el('br'), botaoCopiar()),
      el('p', { class: 'ao-vivo' }, el('span', { class: 'ponto', 'aria-hidden': 'true' }),
        'Esta página se atualiza sozinha: assim que o cadastro for aprovado, os dados de acesso aparecem aqui.')
    );
    agendar(id, token, local);
  }

  Erp.api('site/info').then(Erp.preencherContato).catch(() => { /* sem contato */ });

  const local = Erp.guardar.ler(CHAVE_PEDIDO);
  let [id, token] = location.hash.replace(/^#/, '').split('.');
  if (!id || !token) {
    if (local && local.id && local.token) {
      id = local.id;
      token = local.token;
      history.replaceState(null, '', `#${id}.${token}`);
    } else {
      semCadastro('Nenhuma solicitação de cadastro foi feita neste dispositivo. Utilize o link exibido ao final da solicitação.');
      return;
    }
  }
  if (!/^[0-9a-f-]{36}$/i.test(id) || !/^[0-9a-f]{20,64}$/i.test(token)) {
    semCadastro('O link está incompleto. Verifique se ele foi copiado por inteiro.');
    return;
  }
  const doAparelho = local && local.id === id ? local : null;
  // Voltou para a aba (ou desbloqueou o celular): confere na hora.
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'visible' && mostrada === 'Pendente') carregar(id, token, doAparelho, true);
  });
  carregar(id, token, doAparelho);
})();
