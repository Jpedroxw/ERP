#!/usr/bin/env bash
# Prepara a VPS Debian para a API do ERP não fiscal.
# Rode como root, uma única vez:   bash instalar-servidor.sh
# Pode rodar de novo sem estragar nada (não troca senhas já geradas).
set -euo pipefail

APP_USER=erpnf
APP_DIR=/opt/erp-nf/api
ENV_DIR=/etc/erp-nf
ENV_FILE=$ENV_DIR/api.env
DB_NAME=erp_nf
DB_USER=erp_nf

echo "==> Pacotes (PostgreSQL e Nginx; ignora se já estiverem instalados)"
apt-get update -qq
apt-get install -y -qq postgresql nginx openssl >/dev/null
systemctl enable --now postgresql >/dev/null

echo "==> Usuário de sistema e pastas"
id -u $APP_USER >/dev/null 2>&1 || useradd --system --home /opt/erp-nf --shell /usr/sbin/nologin $APP_USER
mkdir -p $APP_DIR $ENV_DIR
chown -R $APP_USER:$APP_USER /opt/erp-nf

if [[ ! -f $ENV_FILE ]]; then
  echo "==> Gerando senhas e chave JWT em $ENV_FILE"
  DB_PASS=$(openssl rand -hex 16)
  JWT_KEY=$(openssl rand -hex 48)
  ADMIN_PASS=$(openssl rand -base64 9 | tr -d '/+=' | cut -c1-10)
  cat > $ENV_FILE <<EOF
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__Erp=Host=localhost;Port=5432;Database=$DB_NAME;Username=$DB_USER;Password=$DB_PASS
Jwt__Chave=$JWT_KEY
Seed__AdminSenha=$ADMIN_PASS
Urls=http://127.0.0.1:5080
EOF
  chmod 640 $ENV_FILE
  chown root:$APP_USER $ENV_FILE
  NOVO=1
else
  DB_PASS=$(grep -oP 'Password=\K[^;]+' $ENV_FILE)
  ADMIN_PASS=$(grep -oP '^Seed__AdminSenha=\K.*' $ENV_FILE)
  NOVO=0
fi

echo "==> Banco PostgreSQL ($DB_NAME) — separado de qualquer outro banco que já exista"
sudo -u postgres psql -tc "SELECT 1 FROM pg_roles WHERE rolname='$DB_USER'" | grep -q 1 || \
  sudo -u postgres psql -c "CREATE USER $DB_USER WITH PASSWORD '$DB_PASS';"
sudo -u postgres psql -tc "SELECT 1 FROM pg_database WHERE datname='$DB_NAME'" | grep -q 1 || \
  sudo -u postgres psql -c "CREATE DATABASE $DB_NAME OWNER $DB_USER;"
# Vários clientes: a API cria um banco por cliente (erp_c_codigo) e o banco erp_master.
sudo -u postgres psql -qc "ALTER ROLE $DB_USER CREATEDB;"

echo "==> Chave do ERP Master"
MASTER_NOVA=0
if ! grep -q '^Master__Chave=' $ENV_FILE; then
  echo "Master__Chave=$(openssl rand -hex 32)" >> $ENV_FILE
  MASTER_NOVA=1
fi
MASTER_KEY=$(grep -oP '^Master__Chave=\K.*' $ENV_FILE)

echo "==> Backup diário de todos os bancos (2h, guarda 14 dias, em /opt/erp-nf/backups)"
mkdir -p /opt/erp-nf/backups
chown postgres:postgres /opt/erp-nf/backups
cat > /usr/local/bin/erp-nf-backup <<'BACKUP'
#!/usr/bin/env bash
# Cópia de cada banco do ERP (padrão, master e um por cliente): um arquivo .dump por banco por dia.
set -e
DIR=/opt/erp-nf/backups
DIA=$(date +%F)
for DB in $(psql -Atc "SELECT datname FROM pg_database WHERE datname IN ('erp_nf','erp_master') OR datname LIKE 'erp\_c\_%'"); do
  pg_dump -Fc "$DB" > "$DIR/$DB-$DIA.dump"
done
find "$DIR" -name '*.dump' -mtime +14 -delete
BACKUP
chmod 755 /usr/local/bin/erp-nf-backup
echo "0 2 * * * postgres /usr/local/bin/erp-nf-backup" > /etc/cron.d/erp-nf-backup

echo "==> Serviço systemd"
cat > /etc/systemd/system/erp-nf-api.service <<EOF
[Unit]
Description=Projeto JP - API
After=network.target postgresql.service

[Service]
WorkingDirectory=$APP_DIR
ExecStart=$APP_DIR/Erp.Api
EnvironmentFile=$ENV_FILE
User=$APP_USER
Restart=always
RestartSec=5
KillSignal=SIGINT
SyslogIdentifier=erp-nf-api
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

[Install]
WantedBy=multi-user.target
EOF
systemctl daemon-reload
systemctl enable erp-nf-api >/dev/null

echo
echo "Pronto. Próximos passos:"
echo "  1) Envie os arquivos publicados da API para $APP_DIR (veja o README)"
echo "  2) chmod +x $APP_DIR/Erp.Api && chown -R $APP_USER:$APP_USER $APP_DIR"
echo "  3) systemctl restart erp-nf-api && journalctl -u erp-nf-api -f"
echo "  4) Configure o Nginx e o HTTPS (deploy/nginx-erp-nf.conf, Passo 4 do guia)"
if [[ $NOVO == 1 ]]; then
  echo
  echo "  Usuário inicial da Retaguarda:  admin   senha: $ADMIN_PASS"
  echo "  (anote; também está em $ENV_FILE — troque depois do primeiro acesso)"
fi
echo
if [[ $MASTER_NOVA == 1 ]]; then
  echo "  Chave do ERP Master (gerada agora):"
else
  echo "  Chave do ERP Master:"
fi
echo "    $MASTER_KEY"
echo "  (cole no ERP Master, em Conexão; fica em $ENV_FILE, linha Master__Chave — não passe para ninguém)"
if [[ $MASTER_NOVA == 1 && $NOVO == 0 ]]; then
  echo "  A API já estava instalada: rode  systemctl restart erp-nf-api  para ela ler a chave nova."
fi
