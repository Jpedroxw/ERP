#!/usr/bin/env bash
# =====================================================================
#  limpar.sh — remove do servidor o que os sistemas instalaram,
#  SEM formatar: serviços, sites do Nginx, certificados HTTPS, bancos,
#  pastas, tarefas agendadas, usuários de sistema, repositórios e pacotes.
#
#  Uso (como root):
#     bash limpar.sh --listar   # só mostra o que encontrou, não apaga nada
#     bash limpar.sh            # pergunta o que remover e limpa
#
#  Antes de apagar, guarda backup de tudo o que vai sair em
#  /root/backup-limpeza-<data>.
#  Não toca em: sistema, IP, rede, SSH, swap, contas de login e no
#  agente de monitoramento da hospedagem.
# =====================================================================
set -uo pipefail

VERDE='\033[0;32m'; AMARELO='\033[1;33m'; VERMELHO='\033[0;31m'; AZUL='\033[0;36m'; NC='\033[0m'
ok()     { echo -e "${VERDE}[OK]${NC} $*"; }
info()   { echo -e "\n${AZUL}==> $*${NC}"; }
aviso()  { echo -e "${AMARELO}[AVISO]${NC} $*"; }
erro()   { echo -e "${VERMELHO}[ERRO]${NC} $*" >&2; exit 1; }
falhou() { echo -e "${VERMELHO}[FALHOU]${NC} $*"; }

# Nomes que NUNCA são tocados (sistema, acesso e agente da hospedagem)
PROTEGIDOS='(ssh|agent|agente|hospede|lumi|qemu|cloud|monitor|netdata|zabbix|exporter|wazuh|tailscale|getty|systemd|dbus|network|resolv|cron|rsyslog|chrony|ntp|unattended|apt-|fwupd|polkit|udisks|backup-limpeza|lost\+found|debian\.sources|ubuntu\.sources|debian-security)'
protegido() { [[ "${1,,}" =~ $PROTEGIDOS ]]; }

MODO_LISTAR=0
[[ "${1:-}" == "--listar" ]] && MODO_LISTAR=1

[[ $EUID -eq 0 ]] || erro "Rode como root:  bash limpar.sh"
[[ -f /etc/debian_version ]] || erro "Este script é para Debian."
if [[ $MODO_LISTAR -eq 0 && ! -t 0 ]]; then
  erro "Rode direto no terminal (o script faz perguntas)."
fi

dono_pacote() { dpkg -S "$1" >/dev/null 2>&1; }
pg_ativo()    { command -v psql >/dev/null 2>&1 && runuser -u postgres -- psql -Atc 'select 1' >/dev/null 2>&1; }
pgq()         { runuser -u postgres -- psql -Atc "$1" 2>/dev/null; }
unico()       { printf '%s\n' "$@" | awk 'NF && !v[$0]++'; }

PROT_ACHADOS=()

# ===================== DESCOBERTA =====================
info "Procurando o que foi instalado..."

# --- Serviços que não vieram de pacotes do Debian ---
tmp=()
for f in /etc/systemd/system/*.service /lib/systemd/system/*.service /usr/lib/systemd/system/*.service; do
  [[ -f "$f" && ! -L "$f" ]] || continue
  n="$(basename "$f")"
  [[ "$n" == *@.service ]] && continue
  dono_pacote "$f" && continue
  dono_pacote "/lib/systemd/system/$n" && continue
  dono_pacote "/usr/lib/systemd/system/$n" && continue
  if protegido "$n"; then PROT_ACHADOS+=("serviço $n"); continue; fi
  tmp+=("$n")
done
mapfile -t SERVICOS < <(unico "${tmp[@]+"${tmp[@]}"}")

# --- Sites do Nginx ---
tmp=()
for f in /etc/nginx/sites-available/* /etc/nginx/conf.d/*.conf; do
  [[ -f "$f" ]] || continue
  n="$(basename "$f")"
  [[ "$n" == "default" ]] && continue
  dono_pacote "$f" && continue
  if protegido "$n"; then PROT_ACHADOS+=("site $n"); continue; fi
  tmp+=("$f")
done
for f in /etc/nginx/sites-enabled/*; do
  [[ -f "$f" && ! -L "$f" ]] || continue
  [[ "$(basename "$f")" == "default" ]] && continue
  tmp+=("$f")
done
mapfile -t SITES < <(unico "${tmp[@]+"${tmp[@]}"}")

# --- Certificados HTTPS ---
tmp=()
for d in /etc/letsencrypt/live/*/; do
  [[ -d "$d" ]] || continue
  tmp+=("$(basename "$d")")
done
mapfile -t CERTS < <(unico "${tmp[@]+"${tmp[@]}"}")

# --- Bancos e usuários do PostgreSQL ---
BANCOS=(); PAPEIS=()
if pg_ativo; then
  mapfile -t BANCOS < <(pgq "SELECT datname FROM pg_database WHERE datname NOT IN ('postgres','template0','template1') ORDER BY 1")
  mapfile -t PAPEIS < <(pgq "SELECT rolname FROM pg_roles WHERE rolname <> 'postgres' AND rolname NOT LIKE 'pg\_%' ORDER BY 1")
fi

# --- Pastas de sistemas ---
tmp=()
for d in /opt/* /srv/* /var/www/* /usr/local/lib/node_modules /root/*; do
  [[ -e "$d" ]] || continue
  n="$(basename "$d")"
  if protegido "$n"; then PROT_ACHADOS+=("pasta $d"); continue; fi
  if [[ "$d" == "/var/www/html" ]]; then
    # só entra se tiver algo além da página padrão do Nginx
    sobra="$(find "$d" -mindepth 1 -maxdepth 1 ! -name 'index.nginx-debian.html' ! -name 'index.html' | head -n1)"
    [[ -n "$sobra" ]] || continue
  fi
  tmp+=("$d")
done
mapfile -t PASTAS < <(unico "${tmp[@]+"${tmp[@]}"}")

# --- Programas em /usr/local/bin ---
tmp=()
for f in /usr/local/bin/*; do
  [[ -e "$f" ]] || continue
  if protegido "$(basename "$f")"; then PROT_ACHADOS+=("programa $f"); continue; fi
  tmp+=("$f")
done
mapfile -t PROGRAMAS < <(unico "${tmp[@]+"${tmp[@]}"}")

# --- Tarefas agendadas ---
tmp=()
for f in /etc/cron.d/*; do
  [[ -f "$f" ]] || continue
  dono_pacote "$f" && continue
  if protegido "$(basename "$f")"; then PROT_ACHADOS+=("tarefa $f"); continue; fi
  tmp+=("$f")
done
while IFS=: read -r u _; do
  if crontab -l -u "$u" 2>/dev/null | grep -qvE '^\s*(#|$)'; then tmp+=("crontab:$u"); fi
done < /etc/passwd
mapfile -t TAREFAS < <(unico "${tmp[@]+"${tmp[@]}"}")

# --- Usuários de sistema criados para os sistemas ---
tmp=(); CONTAS_LOGIN=()
while IFS=: read -r u _ uid _ _ home shell; do
  if (( uid >= 1000 && uid < 60000 )); then CONTAS_LOGIN+=("$u"); continue; fi
  (( uid >= 100 && uid < 1000 )) || continue
  protegido "$u" && continue
  case "$home" in
    /opt/*|/srv/*|/var/www/*|/home/*) tmp+=("$u") ;;
  esac
done < /etc/passwd
mapfile -t USUARIOS < <(unico "${tmp[@]+"${tmp[@]}"}")

# --- Repositórios extras do apt ---
tmp=()
for f in /etc/apt/sources.list.d/*; do
  [[ -f "$f" ]] || continue
  dono_pacote "$f" && continue
  if protegido "$(basename "$f")"; then PROT_ACHADOS+=("repositório $f"); continue; fi
  tmp+=("$f")
done
mapfile -t REPOS < <(unico "${tmp[@]+"${tmp[@]}"}")

# --- Pacotes instalados pelos sistemas ---
mapfile -t PACOTES < <(dpkg-query -W -f='${binary:Package} ${db:Status-Status}\n' 2>/dev/null \
  | awk '$2=="installed"{print $1}' | sed 's/:amd64$//' \
  | grep -E '^(postgresql.*|nginx.*|certbot|python3-certbot.*|ufw|nodejs|npm|docker.*|containerd.*|redis-server|redis-tools|supervisor|mariadb-server.*|mysql-server.*|vsftpd|proftpd.*|pure-ftpd.*|firebird.*|python3-venv|python3-pip|gunicorn|sqlite3)$' \
  | sort -u)

# ===================== RELATÓRIO =====================
det_servico() { local p; p="$(systemctl show -p FragmentPath --value "$1" 2>/dev/null)"; [[ -f "$p" ]] && grep -m1 '^ExecStart=' "$p" | cut -c11-; }
det_site()    { grep -m1 -E '^\s*server_name' "$1" 2>/dev/null | sed 's/^\s*//'; }
det_banco()   { pgq "SELECT pg_size_pretty(pg_database_size('$1'))"; }
det_pasta()   { du -sh "$1" 2>/dev/null | cut -f1; }
det_tarefa()  { if [[ "$1" == crontab:* ]]; then crontab -l -u "${1#crontab:}" 2>/dev/null | grep -v '^\s*#' | head -n3 | tr '\n' ';'; else grep -v '^\s*#' "$1" | grep . | head -n2 | tr '\n' ';'; fi; }
det_nada()    { :; }

mostrar() { # titulo funcao itens...
  local titulo="$1" fn="$2"; shift 2
  echo -e "\n${AMARELO}${titulo}${NC} ($#)"
  if [[ $# -eq 0 ]]; then echo "   (nada encontrado)"; return; fi
  local i d
  for i in "$@"; do
    d="$($fn "$i" 2>/dev/null || true)"
    if [[ -n "$d" ]]; then echo "   - $i   [$d]"; else echo "   - $i"; fi
  done
}

echo -e "\n==================== O QUE FOI ENCONTRADO ===================="
mostrar "Serviços"                 det_servico "${SERVICOS[@]+"${SERVICOS[@]}"}"
mostrar "Sites do Nginx"           det_site    "${SITES[@]+"${SITES[@]}"}"
mostrar "Certificados HTTPS"       det_nada    "${CERTS[@]+"${CERTS[@]}"}"
mostrar "Bancos PostgreSQL"        det_banco   "${BANCOS[@]+"${BANCOS[@]}"}"
mostrar "Usuários do PostgreSQL"   det_nada    "${PAPEIS[@]+"${PAPEIS[@]}"}"
mostrar "Pastas"                   det_pasta   "${PASTAS[@]+"${PASTAS[@]}"}"
mostrar "Programas em /usr/local/bin" det_nada "${PROGRAMAS[@]+"${PROGRAMAS[@]}"}"
mostrar "Tarefas agendadas"        det_tarefa  "${TAREFAS[@]+"${TAREFAS[@]}"}"
mostrar "Usuários de sistema"      det_nada    "${USUARIOS[@]+"${USUARIOS[@]}"}"
mostrar "Repositórios extras"      det_nada    "${REPOS[@]+"${REPOS[@]}"}"
mostrar "Pacotes"                  det_nada    "${PACOTES[@]+"${PACOTES[@]}"}"

echo -e "\n${VERDE}Protegidos (não serão tocados):${NC}"
echo "   - sistema, rede, SSH, swap"
for p in "${PROT_ACHADOS[@]+"${PROT_ACHADOS[@]}"}"; do echo "   - $p"; done
[[ ${#CONTAS_LOGIN[@]} -gt 0 ]] && echo "   - contas de login: ${CONTAS_LOGIN[*]}"
if command -v ufw >/dev/null 2>&1 && ufw status 2>/dev/null | grep -q 'Status: active'; then
  echo -e "\n${AMARELO}Firewall (ufw) está ATIVO.${NC} Se o pacote ufw for removido, o firewall é desligado antes."
fi

if [[ $MODO_LISTAR -eq 1 ]]; then
  echo -e "\nModo --listar: nada foi alterado."
  exit 0
fi

# ===================== ESCOLHA =====================
escolher() { # nome_array_destino titulo itens...
  local -n _dest="$1"; local titulo="$2"; shift 2
  _dest=()
  [[ $# -eq 0 ]] && return
  local r i
  echo
  echo -e "${AMARELO}${titulo}:${NC} $*"
  read -rp "Remover? [t] todos / [u] um a um / [n] nenhum  (Enter = n): " r
  case "${r,,}" in
    t) _dest=("$@") ;;
    u) for i in "$@"; do
         read -rp "   Remover '$i'? [s/N]: " r
         [[ "${r,,}" == "s" ]] && _dest+=("$i")
       done ;;
    *) ;;
  esac
}

S_SERVICOS=(); S_SITES=(); S_CERTS=(); S_BANCOS=(); S_PAPEIS=(); S_PASTAS=(); S_PROGRAMAS=(); S_TAREFAS=(); S_USUARIOS=(); S_REPOS=(); S_PACOTES=()
echo -e "\n==================== ESCOLHA O QUE REMOVER ===================="
echo "Na dúvida sobre algum item, responda 'u' e escolha um a um."
escolher S_SERVICOS  "Serviços"                "${SERVICOS[@]+"${SERVICOS[@]}"}"
escolher S_SITES     "Sites do Nginx"          "${SITES[@]+"${SITES[@]}"}"
escolher S_CERTS     "Certificados HTTPS"      "${CERTS[@]+"${CERTS[@]}"}"
escolher S_BANCOS    "Bancos PostgreSQL"       "${BANCOS[@]+"${BANCOS[@]}"}"
escolher S_PAPEIS    "Usuários do PostgreSQL"  "${PAPEIS[@]+"${PAPEIS[@]}"}"
escolher S_PASTAS    "Pastas"                  "${PASTAS[@]+"${PASTAS[@]}"}"
escolher S_PROGRAMAS "Programas em /usr/local/bin" "${PROGRAMAS[@]+"${PROGRAMAS[@]}"}"
escolher S_TAREFAS   "Tarefas agendadas"       "${TAREFAS[@]+"${TAREFAS[@]}"}"
escolher S_USUARIOS  "Usuários de sistema"     "${USUARIOS[@]+"${USUARIOS[@]}"}"
escolher S_REPOS     "Repositórios extras"     "${REPOS[@]+"${REPOS[@]}"}"
escolher S_PACOTES   "Pacotes"                 "${PACOTES[@]+"${PACOTES[@]}"}"

TOTAL=$(( ${#S_SERVICOS[@]} + ${#S_SITES[@]} + ${#S_CERTS[@]} + ${#S_BANCOS[@]} + ${#S_PAPEIS[@]} + ${#S_PASTAS[@]} + ${#S_PROGRAMAS[@]} + ${#S_TAREFAS[@]} + ${#S_USUARIOS[@]} + ${#S_REPOS[@]} + ${#S_PACOTES[@]} ))
[[ $TOTAL -gt 0 ]] || { echo -e "\nNada selecionado. Nada foi alterado."; exit 0; }

tem_pacote() { local p; for p in "${S_PACOTES[@]+"${S_PACOTES[@]}"}"; do [[ "$p" == $1 ]] && return 0; done; return 1; }

# ===================== RESUMO + CONFIRMAÇÃO =====================
echo -e "\n==================== RESUMO ===================="
for par in "Serviços:S_SERVICOS" "Sites:S_SITES" "Certificados:S_CERTS" "Bancos:S_BANCOS" \
           "Usuários PostgreSQL:S_PAPEIS" "Pastas:S_PASTAS" "Programas:S_PROGRAMAS" "Tarefas:S_TAREFAS" \
           "Usuários de sistema:S_USUARIOS" "Repositórios:S_REPOS" "Pacotes:S_PACOTES"; do
  rot="${par%%:*}"; declare -n arr="${par#*:}"
  [[ ${#arr[@]} -gt 0 ]] && echo -e "${AMARELO}${rot}:${NC} ${arr[*]}"
  unset -n arr
done

BK="/root/backup-limpeza-$(date +%F_%H%M)"
echo -e "\nAntes de apagar, o backup do que vai sair fica em: ${VERDE}$BK${NC}"
read -rp "Para confirmar, digite LIMPAR: " conf
[[ "$conf" == "LIMPAR" ]] || { echo "Cancelado. Nada foi alterado."; exit 0; }

# ===================== BACKUP =====================
info "Fazendo backup em $BK"
mkdir -p "$BK"; chmod 700 "$BK"

if pg_ativo && { [[ ${#S_BANCOS[@]} -gt 0 || ${#S_PAPEIS[@]} -gt 0 ]] || tem_pacote 'postgresql*'; }; then
  if runuser -u postgres -- pg_dumpall 2>/dev/null | gzip > "$BK/postgresql-completo.sql.gz"; then
    ok "Bancos salvos em postgresql-completo.sql.gz"
  else
    falhou "pg_dumpall"
    read -rp "Continuar mesmo sem o backup dos bancos? [s/N]: " r
    [[ "${r,,}" == "s" ]] || erro "Cancelado. Nada foi apagado."
  fi
fi

for t in "${S_TAREFAS[@]+"${S_TAREFAS[@]}"}"; do
  [[ "$t" == crontab:* ]] && crontab -l -u "${t#crontab:}" > "$BK/crontab-${t#crontab:}.txt" 2>/dev/null
done

ARQS=()
for s in "${S_SERVICOS[@]+"${S_SERVICOS[@]}"}"; do
  p="$(systemctl show -p FragmentPath --value "$s" 2>/dev/null)"; [[ -f "$p" ]] && ARQS+=("$p")
  [[ -d "/etc/systemd/system/$s.d" ]] && ARQS+=("/etc/systemd/system/$s.d")
done
ARQS+=("${S_SITES[@]+"${S_SITES[@]}"}" "${S_PASTAS[@]+"${S_PASTAS[@]}"}" "${S_PROGRAMAS[@]+"${S_PROGRAMAS[@]}"}" "${S_REPOS[@]+"${S_REPOS[@]}"}")
for t in "${S_TAREFAS[@]+"${S_TAREFAS[@]}"}"; do [[ "$t" == crontab:* ]] || ARQS+=("$t"); done
if { [[ ${#S_CERTS[@]} -gt 0 ]] || tem_pacote 'certbot'; } && [[ -d /etc/letsencrypt ]]; then ARQS+=("/etc/letsencrypt"); fi
tem_pacote 'nginx*' && [[ -d /etc/nginx ]] && ARQS+=("/etc/nginx")
tem_pacote 'postgresql*' && [[ -d /etc/postgresql ]] && ARQS+=("/etc/postgresql")

if [[ ${#ARQS[@]} -gt 0 ]]; then
  REL=(); for a in "${ARQS[@]}"; do [[ -e "$a" ]] && REL+=("${a#/}"); done
  if [[ ${#REL[@]} -gt 0 ]]; then
    PRECISA=$(du -sck "${ARQS[@]}" 2>/dev/null | tail -n1 | cut -f1)
    LIVRE=$(df -Pk /root | awk 'NR==2{print $4}')
    if (( PRECISA > LIVRE )); then
      erro "Espaço insuficiente para o backup ($((PRECISA/1024)) MB necessários). Nada foi apagado."
    fi
    if tar -czf "$BK/arquivos.tar.gz" -C / "${REL[@]}" 2>/dev/null; then
      ok "Arquivos salvos em arquivos.tar.gz"
    else
      aviso "O tar terminou com avisos; confira $BK/arquivos.tar.gz"
    fi
  fi
fi

# ===================== LIMPEZA =====================
info "Parando e removendo serviços"
for s in "${S_SERVICOS[@]+"${S_SERVICOS[@]}"}"; do
  p="$(systemctl show -p FragmentPath --value "$s" 2>/dev/null)"
  systemctl disable --now "$s" >/dev/null 2>&1 || true
  [[ -f "$p" ]] && rm -f "$p"
  rm -rf "/etc/systemd/system/$s.d"
  ok "serviço $s"
done
systemctl daemon-reload; systemctl reset-failed >/dev/null 2>&1 || true

info "Removendo tarefas agendadas"
for t in "${S_TAREFAS[@]+"${S_TAREFAS[@]}"}"; do
  if [[ "$t" == crontab:* ]]; then crontab -r -u "${t#crontab:}" 2>/dev/null; else rm -f "$t"; fi
  ok "$t"
done

info "Removendo certificados HTTPS"
for c in "${S_CERTS[@]+"${S_CERTS[@]}"}"; do
  if command -v certbot >/dev/null 2>&1; then
    certbot delete --cert-name "$c" --non-interactive >/dev/null 2>&1 || falhou "certbot delete $c"
  else
    rm -rf "/etc/letsencrypt/live/$c" "/etc/letsencrypt/archive/$c" "/etc/letsencrypt/renewal/$c.conf"
  fi
  ok "certificado $c"
done

info "Removendo sites do Nginx"
for f in "${S_SITES[@]+"${S_SITES[@]}"}"; do
  n="$(basename "$f")"
  rm -f "$f" "/etc/nginx/sites-enabled/$n"
  ok "site $n"
done
# remove links quebrados que sobraram
find /etc/nginx/sites-enabled -xtype l -delete 2>/dev/null || true
if command -v nginx >/dev/null 2>&1 && ! tem_pacote 'nginx*'; then
  if nginx -t >/dev/null 2>&1; then systemctl reload nginx 2>/dev/null || true
  else aviso "A configuração do Nginx ficou com erro. Veja com: nginx -t"; fi
fi

info "Removendo bancos PostgreSQL"
if pg_ativo; then
  for b in "${S_BANCOS[@]+"${S_BANCOS[@]}"}"; do
    if pgq "DROP DATABASE \"$b\" WITH (FORCE);" >/dev/null || pgq "DROP DATABASE \"$b\";" >/dev/null; then
      ok "banco $b"
    else falhou "banco $b"; fi
  done
  for r in "${S_PAPEIS[@]+"${S_PAPEIS[@]}"}"; do
    if pgq "DROP ROLE \"$r\";" >/dev/null; then ok "usuário $r"
    else falhou "usuário $r (ainda é dono de algum banco)"; fi
  done
fi

info "Removendo pastas e programas"
for p in "${S_PASTAS[@]+"${S_PASTAS[@]}"}" "${S_PROGRAMAS[@]+"${S_PROGRAMAS[@]}"}"; do
  [[ "$p" == "$BK" ]] && continue
  rm -rf -- "$p" && ok "$p"
done
rm -f /etc/erpnaofiscal.conf

info "Removendo usuários de sistema"
for u in "${S_USUARIOS[@]+"${S_USUARIOS[@]}"}"; do
  pkill -u "$u" 2>/dev/null || true
  if userdel "$u" 2>/dev/null; then ok "usuário $u"; else falhou "usuário $u"; fi
done

info "Removendo repositórios extras"
for f in "${S_REPOS[@]+"${S_REPOS[@]}"}"; do rm -f "$f" && ok "$f"; done

if [[ ${#S_PACOTES[@]} -gt 0 ]]; then
  info "Removendo pacotes"
  if tem_pacote 'ufw' && command -v ufw >/dev/null 2>&1; then
    ufw --force disable >/dev/null 2>&1 || true
  fi
  export DEBIAN_FRONTEND=noninteractive
  if tem_pacote 'docker*' && command -v docker >/dev/null 2>&1; then
    docker ps -aq 2>/dev/null | xargs -r docker rm -f >/dev/null 2>&1 || true
  fi
  apt-get purge -y "${S_PACOTES[@]}" || falhou "apt-get purge"
  apt-get autoremove -y --purge || true

  # restos que o purge às vezes deixa
  esta_instalado() { dpkg-query -W -f='${db:Status-Status}' "$1" 2>/dev/null | grep -q installed; }
  if tem_pacote 'postgresql*' && ! esta_instalado postgresql-common; then
    rm -rf /etc/postgresql /etc/postgresql-common /var/lib/postgresql /var/log/postgresql
    userdel postgres 2>/dev/null || true
  fi
  if tem_pacote 'nginx*' && ! esta_instalado nginx-common && ! esta_instalado nginx; then
    rm -rf /etc/nginx /var/log/nginx /var/lib/nginx
  fi
  if tem_pacote 'certbot' && ! esta_instalado certbot; then
    rm -rf /etc/letsencrypt /var/lib/letsencrypt /var/log/letsencrypt
  fi
  if tem_pacote 'docker*' || tem_pacote 'containerd*'; then
    rm -rf /var/lib/docker /var/lib/containerd /etc/docker
  fi
  [[ ${#S_REPOS[@]} -gt 0 ]] && apt-get update -y >/dev/null 2>&1
  ok "Pacotes removidos"
fi

# ===================== FIM =====================
echo
echo "=============================================================="
echo " Limpeza concluída."
echo " Backup do que foi removido: $BK"
echo "   - baixe essa pasta (WinSCP/SFTP) se quiser guardar"
echo "   - depois, para apagar o backup:  rm -rf $BK"
echo
echo " Memória e disco agora:"
free -h | awk 'NR<=2'
df -h / | awk 'NR==2{print "   disco: "$3" usados de "$2}'
echo "=============================================================="
