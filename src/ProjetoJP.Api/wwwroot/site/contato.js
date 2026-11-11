'use strict';
// Páginas simples: só preenche o contato do suporte (configurado na API em Site:WhatsApp / Site:Email).
Erp.api('site/info').then((info) => {
  Erp.preencherContato(info);
  const sem = document.getElementById('sem-contato');
  if (sem && info && (info.whatsapp || info.email)) sem.hidden = true;
}).catch(() => { /* sem contato */ });
