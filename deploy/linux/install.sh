#!/usr/bin/env bash
# C4 GuestPass – instalacja / aktualizacja na serwerze Linux (Ubuntu 22.04+/Debian 12+, systemd, x64).
# Uruchamiaj z rozpakowanej paczki c4guestpass-linux-x64 (obok tego pliku jest katalog app/):
#
#   sudo bash install.sh                                     # aplikacja na http://SERWER:8080 (test, zaufana sieć)
#   sudo bash install.sh --domain guestpass.firma.pl         # + Caddy z HTTPS (certyfikat Let's Encrypt)
#   sudo bash install.sh --domain guestpass.firma.local --internal-tls
#                                                            # + Caddy z HTTPS z wewnętrznego CA (sieć bez internetu)
#   sudo bash install.sh --uninstall [--purge]               # usunięcie (--purge: także dane i konfiguracja)
#
# Ponowne uruchomienie = aktualizacja programu. Dane (/var/lib/c4guestpass) i konfiguracja
# (/etc/c4guestpass/c4guestpass.env) zostają bez zmian.
set -euo pipefail

APP_USER=c4guestpass
APP_DIR=/opt/c4guestpass
DATA_DIR=/var/lib/c4guestpass
CONF_DIR=/etc/c4guestpass
ENV_FILE=$CONF_DIR/c4guestpass.env
UNIT=/etc/systemd/system/c4guestpass.service
PORT=8080
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

DOMAIN="" INTERNAL_TLS=0 UNINSTALL=0 PURGE=0
while [ $# -gt 0 ]; do
  case "$1" in
    --domain) DOMAIN="${2:-}"; shift 2 ;;
    --internal-tls) INTERNAL_TLS=1; shift ;;
    --uninstall) UNINSTALL=1; shift ;;
    --purge) PURGE=1; shift ;;
    -h|--help) sed -n '2,13p' "$0"; exit 0 ;;
    *) echo "Nieznana opcja: $1 (pomoc: bash install.sh --help)" >&2; exit 2 ;;
  esac
done

say()  { printf '\033[1;32m==>\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m!!\033[0m  %s\n' "$*"; }
die()  { printf '\033[1;31mBŁĄD:\033[0m %s\n' "$*" >&2; exit 1; }

[ "$(id -u)" -eq 0 ] || die "uruchom jako root: sudo bash install.sh"
command -v systemctl >/dev/null || die "ten instalator wymaga systemd"

if [ "$UNINSTALL" -eq 1 ]; then
  say "Zatrzymuję i usuwam usługę"
  systemctl disable --now c4guestpass 2>/dev/null || true
  rm -f "$UNIT"; systemctl daemon-reload
  rm -rf "$APP_DIR"
  if [ -f /etc/caddy/c4guestpass.caddy ]; then
    rm -f /etc/caddy/c4guestpass.caddy
    sed -i '\#^import /etc/caddy/c4guestpass.caddy$#d' /etc/caddy/Caddyfile 2>/dev/null || true
    systemctl reload caddy 2>/dev/null || true
  fi
  if [ "$PURGE" -eq 1 ]; then
    rm -rf "$DATA_DIR" "$CONF_DIR"; userdel "$APP_USER" 2>/dev/null || true
    say "Usunięto także dane i konfigurację"
  else
    say "Dane zostały w $DATA_DIR, konfiguracja w $CONF_DIR (usuń je opcją --purge)"
  fi
  exit 0
fi

[ -x "$HERE/app/C4GuestPass.Web" ] || [ -f "$HERE/app/C4GuestPass.Web" ] || die "brak $HERE/app/C4GuestPass.Web – uruchom install.sh z rozpakowanej paczki"
[ "$(uname -m)" = "x86_64" ] || [ -f "$HERE/app/.arm64" ] || die "paczka jest dla x64, a serwer to $(uname -m)"

# --- zależności systemowe (.NET potrzebuje ICU do polskich formatów dat i sortowania) ---------------
if ! ldconfig -p 2>/dev/null | grep -q 'libicuuc\.so'; then
  ICU=$(apt-cache search --names-only '^libicu[0-9]+$' 2>/dev/null | awk '{print $1}' | sort -V | tail -1)
  [ -n "$ICU" ] || die "brak biblioteki ICU (libicu) – zainstaluj ją menedżerem pakietów"
  say "Instaluję $ICU"
  apt-get install -y -qq "$ICU" >/dev/null
fi

# --- użytkownik i katalogi ------------------------------------------------------------------------
if ! id "$APP_USER" >/dev/null 2>&1; then
  say "Tworzę użytkownika systemowego $APP_USER"
  useradd --system --home-dir "$DATA_DIR" --no-create-home --shell /usr/sbin/nologin "$APP_USER"
fi
install -d -m 700 -o "$APP_USER" -g "$APP_USER" "$DATA_DIR"
install -d -m 755 -o root -g root "$CONF_DIR"

# --- program --------------------------------------------------------------------------------------
FRESH=0; [ -d "$APP_DIR" ] || FRESH=1
if systemctl is-active --quiet c4guestpass; then say "Zatrzymuję działającą usługę"; systemctl stop c4guestpass; fi
say "Kopiuję program do $APP_DIR"
rm -rf "$APP_DIR.new"; mkdir -p "$APP_DIR.new"
cp -a "$HERE/app/." "$APP_DIR.new/"
rm -f "$APP_DIR.new/appsettings.Development.json"
chown -R root:root "$APP_DIR.new"; chmod -R u=rwX,go=rX "$APP_DIR.new"; chmod 755 "$APP_DIR.new/C4GuestPass.Web"
rm -rf "$APP_DIR"; mv "$APP_DIR.new" "$APP_DIR"

# --- konfiguracja (tylko przy pierwszej instalacji) -----------------------------------------------
if [ ! -f "$ENV_FILE" ]; then
  say "Tworzę konfigurację $ENV_FILE"
  install -m 600 -o root -g root "$HERE/c4guestpass.env.example" "$ENV_FILE"
  if [ -z "$DOMAIN" ]; then
    # Bez reverse proxy: aplikacja dostępna wprost po HTTP – ciasteczko nie może wymagać HTTPS.
    sed -i "s#^ASPNETCORE_URLS=.*#ASPNETCORE_URLS=http://0.0.0.0:$PORT#; s#^GuestPass__SecureCookies=.*#GuestPass__SecureCookies=false#" "$ENV_FILE"
  fi
else
  say "Konfiguracja $ENV_FILE już istnieje – zostawiam bez zmian"
fi

# --- usługa ---------------------------------------------------------------------------------------
install -m 644 "$HERE/c4guestpass.service" "$UNIT"
systemctl daemon-reload
systemctl enable --quiet c4guestpass
say "Uruchamiam usługę c4guestpass"
systemctl start c4guestpass || { journalctl -u c4guestpass -n 40 --no-pager; die "usługa nie wystartowała (log powyżej)"; }

LISTEN=$(grep -E '^ASPNETCORE_URLS=' "$ENV_FILE" | cut -d= -f2- | tr -d "'\"" | sed 's#0\.0\.0\.0#127.0.0.1#; s#+#127.0.0.1#; s#\*#127.0.0.1#')
for _ in $(seq 1 30); do
  if (exec 3<>"/dev/tcp/127.0.0.1/${LISTEN##*:}") 2>/dev/null; then OK=1; break; fi
  sleep 1
done
[ "${OK:-0}" -eq 1 ] || { journalctl -u c4guestpass -n 40 --no-pager; die "aplikacja nie odpowiada na $LISTEN (log powyżej)"; }

# --- HTTPS przez Caddy ----------------------------------------------------------------------------
if [ -n "$DOMAIN" ]; then
  # Porty 80/443 zajęte przez inny serwer WWW (np. nginx) -> Caddy nie wystartuje; wtedy użyj nginx.conf z paczki.
  BUSY=$(ss -ltnpH '( sport = :80 or sport = :443 )' 2>/dev/null | grep -v '"caddy"' | grep -o 'users:(("[^"]*' | cut -d'"' -f2 | sort -u | tr '\n' ' ')
  if [ -n "$BUSY" ]; then
    die "porty 80/443 zajmuje już: $BUSY– aplikacja działa na http://127.0.0.1:$PORT; podłącz ją do istniejącego serwera WWW (przykład: nginx.conf w paczce) albo zwolnij porty i uruchom install.sh ponownie"
  fi
  if ! command -v caddy >/dev/null; then
    say "Instaluję Caddy (reverse proxy z automatycznym HTTPS)"
    apt-get update -qq && DEBIAN_FRONTEND=noninteractive apt-get install -y -qq caddy >/dev/null
  fi
  say "Konfiguruję Caddy dla https://$DOMAIN"
  {
    echo "# C4 GuestPass – wygenerowane przez install.sh"
    echo "$DOMAIN {"
    [ "$INTERNAL_TLS" -eq 1 ] && echo "	tls internal"
    cat <<EOF
	encode zstd gzip
	header {
		Strict-Transport-Security "max-age=31536000"
		X-Content-Type-Options "nosniff"
		X-Frame-Options "DENY"
		Referrer-Policy "same-origin"
		-Server
	}
	reverse_proxy 127.0.0.1:$PORT
}
EOF
  } > /etc/caddy/c4guestpass.caddy
  if grep -q '/usr/share/caddy' /etc/caddy/Caddyfile 2>/dev/null; then
    # Domyślna strona powitalna pakietu zajmuje port 80 – zastępujemy ją (kopia zostaje).
    cp /etc/caddy/Caddyfile "/etc/caddy/Caddyfile.bak-$(date +%Y%m%d%H%M%S)"
    echo "import /etc/caddy/c4guestpass.caddy" > /etc/caddy/Caddyfile
  elif ! grep -qx 'import /etc/caddy/c4guestpass.caddy' /etc/caddy/Caddyfile 2>/dev/null; then
    echo "import /etc/caddy/c4guestpass.caddy" >> /etc/caddy/Caddyfile
  fi
  caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile >/dev/null 2>&1 || die "błędna konfiguracja Caddy: caddy validate --config /etc/caddy/Caddyfile"
  systemctl enable --quiet caddy
  systemctl reload caddy 2>/dev/null || systemctl restart caddy
  if grep -q '^ASPNETCORE_URLS=http://0\.0\.0\.0' "$ENV_FILE"; then
    warn "W $ENV_FILE aplikacja słucha na wszystkich adresach – za Caddy wystarczy ASPNETCORE_URLS=http://127.0.0.1:$PORT i GuestPass__SecureCookies=true"
  fi
  URL="https://$DOMAIN"
else
  URL="http://$(hostname -I 2>/dev/null | awk '{print $1}'):$PORT"
  warn "Brak HTTPS – hasła idą przez sieć jawnym tekstem. Produkcyjnie: sudo bash install.sh --domain NAZWA"
fi

# --- podsumowanie ---------------------------------------------------------------------------------
echo
say "Gotowe: $URL"
if [ "$FRESH" -eq 1 ]; then
  PW=$(journalctl -u c4guestpass --no-pager -o cat 2>/dev/null | grep -o "hasło tymczasowe: .*" | tail -1 | cut -d' ' -f3- || true)
  if [ -n "$PW" ]; then echo "    Pierwsze logowanie: login admin, hasło tymczasowe: $PW"
  else echo "    Pierwsze logowanie: login admin i hasło z Bootstrap__AdminPassword w $ENV_FILE"; fi
fi
cat <<EOF
    Dalej w przeglądarce: Konfiguracja C4 → Połączenie z C4 (adres serwera C4, login, hasło).
    Poczta (SMTP) i inne ustawienia: $ENV_FILE, potem: sudo systemctl restart c4guestpass
    Logi: journalctl -u c4guestpass -f
EOF
