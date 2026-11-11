'use strict';
// Página inicial: formulário de cadastro (POST cadastro), conferência do nome do servidor e área de download.

(() => {
  const $ = (id) => document.getElementById(id);
  const UFS = ['AC', 'AL', 'AP', 'AM', 'BA', 'CE', 'DF', 'ES', 'GO', 'MA', 'MT', 'MS', 'MG', 'PA', 'PB', 'PR', 'PE', 'PI',
    'RJ', 'RN', 'RS', 'RO', 'RR', 'SC', 'SP', 'SE', 'TO'];
  const CHAVE_PEDIDO = 'erp.pedido';

  $('ano').textContent = String(new Date().getFullYear());
  for (const uf of UFS) $('uf').append(Erp.el('option', { value: uf }, uf));

  // ---------- Cadastro já enviado neste aparelho ----------
  const pedido = Erp.guardar.ler(CHAVE_PEDIDO);
  if (pedido && pedido.id && pedido.token) {
    $('meu-pedido').href = `acompanhar#${pedido.id}.${pedido.token}`;
    $('meu-pedido').hidden = false;
    $('link-pedido').href = `acompanhar#${pedido.id}.${pedido.token}`;
    $('link-pedido').hidden = false;
  }

  // ---------- Instalador e contato ----------
  Erp.api('site/info').then((info) => {
    Erp.preencherContato(info);
    if (info && info.instalador) {
      $('versao').textContent = `Versão de ${Erp.data(info.instalador.data)} · aprox. ${Erp.tamanho(info.instalador.tamanho)}`;
    } else {
      $('versao').textContent = 'O instalador estará disponível em breve.';
    }
  }).catch(() => { $('versao').textContent = 'Não foi possível verificar a versão no momento.'; });

  // ---------- Máscaras ----------
  function mascaraTelefone(v) {
    const d = v.replace(/\D/g, '').slice(0, 11);
    if (d.length <= 2) return d.length ? `(${d}` : '';
    if (d.length <= 6) return `(${d.slice(0, 2)}) ${d.slice(2)}`;
    if (d.length <= 10) return `(${d.slice(0, 2)}) ${d.slice(2, 6)}-${d.slice(6)}`;
    return `(${d.slice(0, 2)}) ${d.slice(2, 7)}-${d.slice(7)}`;
  }
  function mascaraDocumento(v) {
    if (/[a-z]/i.test(v)) return v.toUpperCase().slice(0, 18); // CNPJ com letras: deixa como digitado
    const d = v.replace(/\D/g, '').slice(0, 14);
    if (d.length <= 11) {
      return d.replace(/^(\d{3})(\d)/, '$1.$2').replace(/^(\d{3})\.(\d{3})(\d)/, '$1.$2.$3').replace(/\.(\d{3})(\d{1,2})$/, '.$1-$2');
    }
    return d.replace(/^(\d{2})(\d)/, '$1.$2').replace(/^(\d{2})\.(\d{3})(\d)/, '$1.$2.$3')
      .replace(/\.(\d{3})(\d)/, '.$1/$2').replace(/(\d{4})(\d{1,2})$/, '$1-$2');
  }
  $('telefone').addEventListener('input', (e) => { e.target.value = mascaraTelefone(e.target.value); });
  $('documento').addEventListener('input', (e) => { e.target.value = mascaraDocumento(e.target.value); });

  // ---------- Nome do servidor ----------
  const servidor = $('servidor');
  const disp = $('disp');
  let servidorMexido = false;
  let consulta = 0;
  let temporizador = 0;
  let disponivel = null; // true / false / null (ainda não sabe)

  function mostrarDisp(tipo, texto) {
    disp.className = 'disp' + (tipo ? ' ' + tipo : '');
    disp.textContent = texto;
    disp.hidden = !texto;
  }

  function codigoDoServidor() {
    let c = Erp.daTela(servidor.value);
    if (c.length > 30) c = c.slice(0, 30).replace(/-+$/, '');
    return c;
  }

  function conferirServidor() {
    clearTimeout(temporizador);
    disponivel = null;
    const codigo = codigoDoServidor();
    if (!servidor.value.trim()) { mostrarDisp('', ''); return; }
    if (!Erp.codigoValido(codigo)) {
      disponivel = false;
      mostrarDisp('erro', /^[a-z][a-z0-9-]{1,29}$/.test(codigo) ? 'Nome reservado' : 'Inicie com uma letra');
      return;
    }
    mostrarDisp('', 'Verificando…');
    const minha = ++consulta;
    temporizador = setTimeout(async () => {
      try {
        const r = await Erp.api(`cadastro/disponivel?codigo=${encodeURIComponent(codigo)}`);
        if (minha !== consulta) return;
        disponivel = !!(r && r.disponivel);
        mostrarDisp(disponivel ? 'ok' : 'erro', disponivel ? '✓ Disponível' : 'Indisponível');
      } catch (e) {
        if (minha !== consulta) return;
        mostrarDisp('erro', e.status === 429 ? 'Aguarde alguns instantes' : 'Não foi possível verificar');
      }
    }, 450);
  }

  servidor.addEventListener('input', () => { servidorMexido = servidor.value.trim().length > 0; conferirServidor(); });

  // Sugere o nome do servidor a partir do nome da empresa (até a pessoa mexer no campo).
  $('empresa').addEventListener('input', () => {
    if (servidorMexido) return;
    const sugestao = Erp.daTela($('empresa').value).replace(/-(ltda|me|epp|eireli|sa|s-a|mei)$/g, '').slice(0, 30).replace(/-+$/, '');
    servidor.value = Erp.paraTela(sugestao);
    conferirServidor();
  });

  // ---------- Envio ----------
  const form = $('form-cadastro');
  const msg = $('msg-form');

  function erro(campo, texto) {
    msg.textContent = texto;
    msg.hidden = false;
    if (campo) {
      campo.setAttribute('aria-invalid', 'true');
      campo.focus();
    } else {
      msg.scrollIntoView({ block: 'center', behavior: 'smooth' });
    }
    return false;
  }

  form.addEventListener('input', (e) => { if (e.target.getAttribute) e.target.removeAttribute('aria-invalid'); });

  function validar() {
    msg.hidden = true;
    const v = (id) => $(id).value.trim();
    if (!v('empresa')) return erro($('empresa'), 'Informe a razão social ou o nome fantasia.');
    if (!v('responsavel')) return erro($('responsavel'), 'Informe o nome do responsável.');
    const tel = v('telefone').replace(/\D/g, '');
    if (tel.length < 10 || tel.length > 11) return erro($('telefone'), 'Telefone inválido: informe o DDD e o número.');
    const doc = v('documento').replace(/[^0-9a-z]/gi, '');
    if (doc && doc.length !== 11 && doc.length !== 14) return erro($('documento'), 'CPF ou CNPJ incompleto.');
    if (v('email') && !/^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/.test(v('email'))) return erro($('email'), 'E-mail inválido.');
    const codigo = codigoDoServidor();
    if (!Erp.codigoValido(codigo)) return erro(servidor, 'Nome do servidor inválido. Utilize letras e números, iniciando por letra.');
    if (disponivel === false) return erro(servidor, 'O nome do servidor informado já está em uso. Escolha outro nome.');
    const login = v('admin').toLowerCase();
    if (!/^[a-z0-9._-]{3,60}$/.test(login)) return erro($('admin'), 'O usuário deve ter de 3 a 60 caracteres: letras minúsculas, números, ponto, hífen ou sublinhado.');
    const senha = $('senha').value;
    if (senha.length < 6 || senha.length > 64) return erro($('senha'), 'A senha deve ter de 6 a 64 caracteres.');
    if (senha !== $('senha2').value) return erro($('senha2'), 'As senhas informadas não conferem.');
    if (!$('aceite').checked) return erro($('aceite'), 'É necessário aceitar o aviso de privacidade para enviar a solicitação.');
    return true;
  }

  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    if (!validar()) return;
    const botao = $('enviar');
    botao.disabled = true;
    botao.textContent = 'Enviando…';
    const v = (id) => $(id).value.trim();
    const codigo = codigoDoServidor();
    try {
      const r = await Erp.api('cadastro', {
        corpo: {
          empresa: v('empresa'),
          documento: v('documento') || null,
          responsavel: v('responsavel'),
          telefone: v('telefone'),
          email: v('email') || null,
          cidade: v('cidade') || null,
          uf: v('uf') || null,
          codigoDesejado: codigo,
          adminLogin: v('admin').toLowerCase(),
          adminSenha: $('senha').value,
          site: $('site').value || null
        }
      });
      Erp.guardar.gravar(CHAVE_PEDIDO, {
        id: r.id, token: r.token, empresa: v('empresa'), codigo, admin: v('admin').toLowerCase(),
        enviadoEm: new Date().toISOString()
      });
      location.href = `acompanhar#${r.id}.${r.token}`;
    } catch (ex) {
      botao.disabled = false;
      botao.textContent = 'Enviar solicitação';
      erro(null, ex.message || 'Não foi possível enviar a solicitação. Tente novamente.');
    }
  });
})();
