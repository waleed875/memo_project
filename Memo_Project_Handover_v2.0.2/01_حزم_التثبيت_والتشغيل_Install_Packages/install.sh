#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'

# MemoDashboard 2.0.2 Ubuntu installer.
# Run after extracting this package, from the directory containing MemoDashboard.Backend.
# Example: sudo ./install.sh --install-root /opt/memo-dashboard

SERVICE_NAME="${SERVICE_NAME:-memo-dashboard}"
SERVICE_USER="${SERVICE_USER:-memo-dashboard}"
SERVICE_GROUP="${SERVICE_GROUP:-$SERVICE_USER}"
INSTALL_ROOT="${INSTALL_ROOT:-/opt/memo-dashboard}"
# Record whether a network setting was explicitly supplied before applying
# defaults. On upgrade, an existing service URL is retained unless overridden.
PORT_EXPLICIT=0
BIND_ADDRESS_EXPLICIT=0
DEPLOYMENT_MODE_EXPLICIT=0
ALLOWED_HOSTS_EXPLICIT=0
[[ -v PORT ]] && PORT_EXPLICIT=1
[[ -v BIND_ADDRESS ]] && BIND_ADDRESS_EXPLICIT=1
[[ -v DEPLOYMENT_MODE ]] && DEPLOYMENT_MODE_EXPLICIT=1
[[ -v ALLOWED_HOSTS ]] && ALLOWED_HOSTS_EXPLICIT=1
PORT="${PORT:-8080}"
DEPLOYMENT_MODE="${DEPLOYMENT_MODE:-InternalLanHttp}"
ALLOWED_HOSTS="${ALLOWED_HOSTS:-localhost;127.0.0.1}"
BIND_ADDRESS="${BIND_ADDRESS:-127.0.0.1}"
AUTO_INSTALL_RUNTIME="${AUTO_INSTALL_RUNTIME:-1}"
NO_START=0

log() { printf '[memo-dashboard] %s\n' "$*"; }
die() { printf '[memo-dashboard] ERROR: %s\n' "$*" >&2; exit 1; }
random_secret() {
  if command -v openssl >/dev/null 2>&1; then
    openssl rand -hex 48
  else
    head -c 48 /dev/urandom | od -An -tx1 | tr -d ' \n'
    printf '\n'
  fi
}
usage() {
  cat <<'EOF'
Usage: sudo ./install.sh [options]

Options:
  --install-root PATH   Installation root (default: /opt/memo-dashboard)
  --service-name NAME   systemd unit name (default: memo-dashboard)
  --service-user NAME   Linux service user (default: memo-dashboard)
  --port PORT           Local HTTP port (default: 8080)
  --bind-address IP     Bind address (use the server internal IP for LAN; default: 127.0.0.1)
  --allowed-hosts LIST  Allowed host/IP list separated by semicolons
  --deployment-mode MODE InternalLanHttp or InternalLanHttps (default: InternalLanHttp)
  --no-runtime-install  Do not install ASP.NET Core Runtime 8 automatically
  --no-start            Install files and unit but do not start the service
  --help                Show this help

Server PDF export (company report) needs headless Chromium; the installer
tries "apt-get install -y chromium-browser" automatically and records the
binary in MEMO_CHROMIUM_PATH. Without a browser the dashboard still runs and
the PDF endpoint answers 503 with guidance.

Environment variables can provide the same defaults. Existing service env files and the external runtime data directory are preserved during upgrades.
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --install-root) [[ $# -ge 2 ]] || die "--install-root needs a value"; INSTALL_ROOT="$2"; shift 2 ;;
    --service-name) [[ $# -ge 2 ]] || die "--service-name needs a value"; SERVICE_NAME="$2"; shift 2 ;;
    --service-user) [[ $# -ge 2 ]] || die "--service-user needs a value"; SERVICE_USER="$2"; SERVICE_GROUP="$2"; shift 2 ;;
    --port) [[ $# -ge 2 ]] || die "--port needs a value"; PORT="$2"; PORT_EXPLICIT=1; shift 2 ;;
    --bind-address) [[ $# -ge 2 ]] || die "--bind-address needs a value"; BIND_ADDRESS="$2"; BIND_ADDRESS_EXPLICIT=1; shift 2 ;;
    --allowed-hosts) [[ $# -ge 2 ]] || die "--allowed-hosts needs a value"; ALLOWED_HOSTS="$2"; ALLOWED_HOSTS_EXPLICIT=1; shift 2 ;;
    --deployment-mode) [[ $# -ge 2 ]] || die "--deployment-mode needs a value"; DEPLOYMENT_MODE="$2"; DEPLOYMENT_MODE_EXPLICIT=1; shift 2 ;;
    --no-runtime-install) AUTO_INSTALL_RUNTIME=0; shift ;;
    --no-start) NO_START=1; shift ;;
    --help|-h) usage; exit 0 ;;
    *) die "Unknown option: $1 (use --help)" ;;
  esac
done

# A specific LAN bind is safe to expose only when the bound host is allowed.
# Infer it automatically so --bind-address alone cannot leave the service
# restricted to localhost. Wildcard binds require an explicit allow-list.
if (( BIND_ADDRESS_EXPLICIT == 1 && ALLOWED_HOSTS_EXPLICIT == 0 )); then
  case "$BIND_ADDRESS" in
    0.0.0.0|::) die "Wildcard bind requires --allowed-hosts with the approved LAN host list." ;;
    *) ALLOWED_HOSTS="$BIND_ADDRESS;localhost;127.0.0.1" ;;
  esac
fi

[[ "$(id -u)" -eq 0 ]] || die "Run as root (for example: sudo ./install.sh)."
[[ "$INSTALL_ROOT" == /* ]] || die "--install-root must be an absolute path."
[[ "$INSTALL_ROOT" != *[[:space:]]* ]] || die "Installation paths must not contain whitespace."
case "$INSTALL_ROOT" in
  /|/opt|/var|/usr|/etc|/home) die "Refusing an overly broad installation root: $INSTALL_ROOT" ;;
esac
[[ "$PORT" =~ ^[0-9]+$ ]] && (( PORT >= 1 && PORT <= 65535 )) || die "Invalid port: $PORT"
[[ "$SERVICE_NAME" =~ ^[a-zA-Z0-9_.@-]+$ ]] || die "Invalid service name."
[[ "$SERVICE_USER" =~ ^[a-zA-Z0-9_.@-]+$ ]] || die "Invalid service user."
[[ "$SERVICE_GROUP" =~ ^[a-zA-Z0-9_.@-]+$ ]] || die "Invalid service group."
[[ "$BIND_ADDRESS" != *[[:space:]]* ]] || die "Invalid bind address."
[[ "$DEPLOYMENT_MODE" == "InternalLanHttp" || "$DEPLOYMENT_MODE" == "InternalLanHttps" ]] || die "DEPLOYMENT_MODE must be InternalLanHttp or InternalLanHttps."
[[ "$ALLOWED_HOSTS" != *[[:space:]]* ]] || die "ALLOWED_HOSTS must not contain whitespace."

# Bare-metal bootstrap: assume an empty Ubuntu server. Install every system
# package the installer and the self-contained backend need (curl, TLS roots,
# unzip, and ICU for .NET globalization). All installs are idempotent.
bootstrap_system_packages() {
  local missing=()
  for candidate in curl unzip openssl; do
    command -v "$candidate" >/dev/null 2>&1 || missing+=("$candidate")
  done
  [[ -f /etc/ssl/certs/ca-certificates.crt ]] || missing+=("ca-certificates")

  local icu_missing=0
  if ! command -v ldconfig >/dev/null 2>&1 || ! ldconfig -p 2>/dev/null | grep -q 'libicu'; then
    icu_missing=1
  fi

  if (( ${#missing[@]} == 0 && icu_missing == 0 )); then
    log "All base system tools (curl, unzip, openssl, ca-certificates, libicu) are already available."
    return 0
  fi

  export DEBIAN_FRONTEND=noninteractive
  log "Refreshing the APT package index for missing dependencies: ${missing[*]}..."
  if apt-get update >/dev/null 2>&1; then
    if (( ${#missing[@]} > 0 )); then
      log "Installing missing base tools: ${missing[*]}."
      apt-get install -y "${missing[@]}" || log "Warning: Could not install some base tools."
    fi
    if (( icu_missing == 1 )); then
      log "Installing ICU libraries required for .NET globalization."
      for candidate in libicu74 libicu72 libicu70 libicu66 libicu-dev; do
        if apt-get install -y "$candidate" >/dev/null 2>&1; then break; fi
      done
    fi
  else
    log "Notice: Offline mode or APT update unreachable. Continuing with existing system libraries."
  fi
}

bootstrap_system_packages

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
PACKAGE_DIR="$SCRIPT_DIR"
PACKAGE_TEMP_DIR=""

# DevOps sometimes keeps install.sh beside the ZIP instead of running the copy
# inside the extracted package. Support that safe, unambiguous layout as well.
if [[ ! -f "$PACKAGE_DIR/MemoDashboard.Backend" || ! -f "$PACKAGE_DIR/wwwroot/index.html" ]]; then
  shopt -s nullglob
  target_zip=""
  if [[ -n "${PACKAGE_ZIP:-}" && -f "$PACKAGE_ZIP" ]]; then
    target_zip="$PACKAGE_ZIP"
  elif [[ -f "$SCRIPT_DIR/MemoDashboard-2.0.2-linux-x64.zip" ]]; then
    target_zip="$SCRIPT_DIR/MemoDashboard-2.0.2-linux-x64.zip"
  elif [[ -f "$SCRIPT_DIR/MemoDashboard-2.0.1-linux-x64.zip" ]]; then
    target_zip="$SCRIPT_DIR/MemoDashboard-2.0.1-linux-x64.zip"
  elif [[ -f "$SCRIPT_DIR/MemoDashboard-2.0.0-linux-x64.zip" ]]; then
    target_zip="$SCRIPT_DIR/MemoDashboard-2.0.0-linux-x64.zip"
  else
    dashboard_zips=("$SCRIPT_DIR"/MemoDashboard-*.zip)
    if (( ${#dashboard_zips[@]} == 1 )); then
      target_zip="${dashboard_zips[0]}"
    fi
  fi
  shopt -u nullglob
  if [[ -n "$target_zip" && -f "$target_zip" ]]; then
    if ! command -v unzip >/dev/null 2>&1; then
      log "Installing unzip to extract the dashboard package."
      apt-get update
      DEBIAN_FRONTEND=noninteractive apt-get install -y unzip
    fi
    PACKAGE_TEMP_DIR="$(mktemp -d)"
    unzip -q "$target_zip" -d "$PACKAGE_TEMP_DIR" \
      || die "Could not extract $target_zip."
    PACKAGE_DIR="$PACKAGE_TEMP_DIR"
  else
    die "MemoDashboard.Backend was not found beside install.sh. Extract MemoDashboard-2.0.2-linux-x64.zip in this folder, or run install.sh from inside the extracted package."
  fi
fi
[[ -f "$PACKAGE_DIR/MemoDashboard.Backend" ]] || die "MemoDashboard.Backend is missing from the package."
[[ -f "$PACKAGE_DIR/MemoDashboard.Backend.runtimeconfig.json" ]] || die "MemoDashboard.Backend.runtimeconfig.json is missing from the package."
[[ -f "$PACKAGE_DIR/wwwroot/index.html" ]] || die "wwwroot/index.html was not found; package is incomplete."

require_cmd() { command -v "$1" >/dev/null 2>&1 || die "Required command not found: $1"; }
require_cmd apt-get
require_cmd systemctl
require_cmd sha256sum
require_cmd install
require_cmd getent
require_cmd useradd
require_cmd groupadd
require_cmd chown
require_cmd readlink

ensure_runtime() {
  if [[ -f "$PACKAGE_DIR/libcoreclr.so" ]]; then
    log "The package is self-contained (.NET 8 Runtime is embedded). No external runtime installation needed."
    return 0
  fi

  if ! command -v curl >/dev/null 2>&1; then
    log "Installing curl for the post-install health check."
    apt-get update
    DEBIAN_FRONTEND=noninteractive apt-get install -y ca-certificates curl
  fi
  if command -v dotnet >/dev/null 2>&1 && dotnet --list-runtimes 2>/dev/null | grep -Eq '^Microsoft\.AspNetCore\.App 8\.'; then
    log "ASP.NET Core Runtime 8 is available."
    return
  fi

  [[ "$AUTO_INSTALL_RUNTIME" == 1 ]] || die "Microsoft.AspNetCore.App 8.x is missing. Install aspnetcore-runtime-8.0 or omit --no-runtime-install."
  [[ -r /etc/os-release ]] || die "Cannot identify Ubuntu version."
  # shellcheck disable=SC1091
  . /etc/os-release
  [[ "${ID:-}" == "ubuntu" ]] || die "Automatic runtime installation supports Ubuntu only (detected: ${ID:-unknown})."
  local version="${VERSION_ID:-}"
  [[ "$version" =~ ^[0-9]+\.[0-9]+$ ]] || die "Unsupported Ubuntu VERSION_ID: $version"

  local tmp
  tmp="$(mktemp -d)"
  trap 'rm -rf -- "${tmp:-}"' RETURN
  log "Installing Microsoft ASP.NET Core Runtime 8 for Ubuntu $version."
  apt-get update
  DEBIAN_FRONTEND=noninteractive apt-get install -y ca-certificates curl
  curl -fsSL "https://packages.microsoft.com/config/ubuntu/${version}/packages-microsoft-prod.deb" -o "$tmp/packages-microsoft-prod.deb" \
    || die "Microsoft package feed is unavailable for Ubuntu $version."
  dpkg -i "$tmp/packages-microsoft-prod.deb"
  apt-get update
  DEBIAN_FRONTEND=noninteractive apt-get install -y aspnetcore-runtime-8.0
  rm -rf -- "$tmp"
  trap - RETURN
  command -v dotnet >/dev/null 2>&1 && dotnet --list-runtimes | grep -Eq '^Microsoft\.AspNetCore\.App 8\.' \
    || die "ASP.NET Core Runtime 8 installation could not be verified."
}

ensure_runtime
if ! command -v curl >/dev/null 2>&1 && ! command -v wget >/dev/null 2>&1; then
  log "Notice: Neither curl nor wget is found. Process status via systemctl will be used for health check."
fi

# 1.2.0: server-side company-report PDF renders through headless Chromium
# (no license cost, correct Arabic shaping). Auto-discovered at runtime from
# MEMO_CHROMIUM_PATH or the well-known binary locations; install it here so a
# fresh server can export PDF immediately. A missing browser only degrades PDF
# export (HTTP 503 with guidance) and never blocks the dashboard itself.
# 1.2.0: unified font is Simplified Arabic (Windows system font, cannot be
# apt-installed for licensing reasons). If the admin copies simpo.ttf +
# simpbdo.ttf next to the installer or into /usr/share/fonts, they are
# registered here so server PDF matches Windows exactly; otherwise Chromium
# falls back gracefully and the dashboard itself is unaffected.
CHROMIUM_BIN=""
detect_chromium() {
  if [[ -n "${MEMO_CHROMIUM_PATH:-}" && -x "${MEMO_CHROMIUM_PATH:-}" ]]; then
    CHROMIUM_BIN="${MEMO_CHROMIUM_PATH:-}"
    return 0
  fi
  local candidate
  for candidate in /usr/bin/chromium /usr/bin/chromium-browser /usr/bin/google-chrome /usr/bin/google-chrome-stable /snap/bin/chromium; do
    if [[ -x "$candidate" ]]; then
      CHROMIUM_BIN="$candidate"
      return 0
    fi
  done
  if command -v chromium >/dev/null 2>&1; then CHROMIUM_BIN="$(command -v chromium)"; return 0; fi
  if command -v chromium-browser >/dev/null 2>&1; then CHROMIUM_BIN="$(command -v chromium-browser)"; return 0; fi
  if command -v google-chrome >/dev/null 2>&1; then CHROMIUM_BIN="$(command -v google-chrome)"; return 0; fi
  if command -v google-chrome-stable >/dev/null 2>&1; then CHROMIUM_BIN="$(command -v google-chrome-stable)"; return 0; fi
  return 1
}
ensure_chromium() {
  if detect_chromium; then
    log "Chromium for server PDF export is available at: $CHROMIUM_BIN"
    return 0
  fi
  log "Installing Chromium (server PDF export for the company report)."
  local installed=0 candidate
  for candidate in chromium-browser chromium; do
    if DEBIAN_FRONTEND=noninteractive apt-get install -y "$candidate" >/dev/null 2>&1 && detect_chromium; then
      installed=1
      break
    fi
  done
  if (( installed == 1 )); then
    log "Chromium installed at: $CHROMIUM_BIN"
    return 0
  fi
  log "WARNING: Chromium could not be installed; company-report PDF export will answer 503 until a browser is installed (apt-get install -y chromium-browser) or MEMO_CHROMIUM_PATH is set. The dashboard itself is unaffected."
  return 0
}

ensure_chromium

ensure_arabic_fonts() {
  local qomra_dir="/usr/share/fonts/truetype/qomra"
  local simpo_dir="/usr/share/fonts/truetype/simplified-arabic"
  local script_dir
  script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
  local updated_fonts=0

  # 1. ITF Qomra Arabic fonts
  mkdir -p "$qomra_dir"
  for font in "$script_dir"/Fonts/itfQomraArabic-* "$script_dir"/itfQomraArabic-*; do
    if [[ -f "$font" ]]; then
      cp -f "$font" "$qomra_dir/" 2>/dev/null || true
      updated_fonts=1
    fi
  done

  # 2. Simplified Arabic fonts
  mkdir -p "$simpo_dir"
  for font in "$script_dir/simpo.ttf" "$script_dir/simpbdo.ttf" "$script_dir/Fonts/simpo.ttf" "$script_dir/Fonts/simpbdo.ttf"; do
    if [[ -f "$font" ]]; then
      cp -f "$font" "$simpo_dir/" 2>/dev/null || true
      updated_fonts=1
    fi
  done

  if (( updated_fonts == 1 )); then
    if command -v fc-cache >/dev/null 2>&1; then
      fc-cache -f "$qomra_dir" "$simpo_dir" >/dev/null 2>&1 || true
    fi
    log "Arabic system fonts (ITF Qomra Arabic & Simplified Arabic) registered successfully."
  else
    log "Arabic fonts not bundled beside installer; standard system font stack will be used."
  fi
}

ensure_arabic_fonts

if ! getent group "$SERVICE_GROUP" >/dev/null 2>&1; then
  groupadd --system "$SERVICE_GROUP"
fi
if ! id -u "$SERVICE_USER" >/dev/null 2>&1; then
  useradd --system --gid "$SERVICE_GROUP" --home-dir /nonexistent --shell /usr/sbin/nologin "$SERVICE_USER"
fi

INSTALL_ROOT="${INSTALL_ROOT%/}"
RELEASES_DIR="$INSTALL_ROOT/releases"
BACKUPS_DIR="$INSTALL_ROOT/backups"
CURRENT_LINK="$INSTALL_ROOT/current"
CONFIG_DIR="/etc/$SERVICE_NAME"
ENV_FILE="$CONFIG_DIR/$SERVICE_NAME.env"
SECRET_DIR="$CONFIG_DIR/secrets"
SIGNING_PRIVATE_KEY_FILE="$SECRET_DIR/signing-private-key.pem"
SIGNING_PUBLIC_KEY_FILE="$SECRET_DIR/signing-public-key.pem"
LEGACY_ADMIN_KEY_FILE="$CONFIG_DIR/update-admin-key"
ADMIN_KEY_FILE="$SECRET_DIR/legacy-admin-key"
if [[ -s "$LEGACY_ADMIN_KEY_FILE" && ! -e "$ADMIN_KEY_FILE" ]]; then ADMIN_KEY_FILE="$LEGACY_ADMIN_KEY_FILE"; fi
CHECKIN_KEY_FILE="$SECRET_DIR/checkin-device-key"
UNIT_FILE="/etc/systemd/system/$SERVICE_NAME.service"
DATA_DIR="${MEMO_DASHBOARD_DATA_DIR:-/var/lib/$SERVICE_NAME}"

install -d -m 0755 "$INSTALL_ROOT" "$RELEASES_DIR" "$BACKUPS_DIR"
install -d -o root -g "$SERVICE_GROUP" -m 0750 "$CONFIG_DIR" "$SECRET_DIR"
install -d -o "$SERVICE_USER" -g "$SERVICE_GROUP" -m 0750 "$DATA_DIR" "$DATA_DIR/keys"

if [[ ! -f "$ENV_FILE" ]]; then
  cat > "$ENV_FILE" <<EOF
# MemoDashboard service environment. Keep secrets outside the release ZIP.
ASPNETCORE_URLS=http://${BIND_ADDRESS}:${PORT}
ASPNETCORE_ENVIRONMENT=Production
# Set this to the trusted MemoUpdateService URL when the web update center is enabled.
UPDATE_SERVICE_URL=
MEMO_DASHBOARD_INTERNAL_SETUP=1
DeploymentMode=$DEPLOYMENT_MODE
AllowedHosts=$ALLOWED_HOSTS
MEMO_UPDATE_SIGNING_PRIVATE_KEY_FILE=$SIGNING_PRIVATE_KEY_FILE
MEMO_UPDATE_SIGNING_PUBLIC_KEY_FILE=$SIGNING_PUBLIC_KEY_FILE
MEMO_UPDATE_ADMIN_KEY_FILE=$ADMIN_KEY_FILE
MEMO_DASHBOARD_DATA_DIR=${DATA_DIR}
EOF
fi

# Existing deployments may listen on a non-default local address/port. Preserve
# that URL unless the caller explicitly supplied a new value via env or CLI.
EFFECTIVE_BIND_ADDRESS="$BIND_ADDRESS"
EFFECTIVE_PORT="$PORT"
existing_urls="$(sed -n 's/^ASPNETCORE_URLS=//p' "$ENV_FILE" | tail -n 1)"
if [[ "$existing_urls" =~ ^http://([^:]+):([0-9]+)$ ]]; then
  existing_bind_address="${BASH_REMATCH[1]}"
  existing_port="${BASH_REMATCH[2]}"
  if (( BIND_ADDRESS_EXPLICIT == 0 )); then EFFECTIVE_BIND_ADDRESS="$existing_bind_address"; fi
  if (( PORT_EXPLICIT == 0 )); then EFFECTIVE_PORT="$existing_port"; fi
fi
[[ "$EFFECTIVE_PORT" =~ ^[0-9]+$ ]] && (( EFFECTIVE_PORT >= 1 && EFFECTIVE_PORT <= 65535 )) \
  || die "Invalid effective service port: $EFFECTIVE_PORT"

if (( BIND_ADDRESS_EXPLICIT == 1 || PORT_EXPLICIT == 1 )); then
  desired_urls="http://${EFFECTIVE_BIND_ADDRESS}:${EFFECTIVE_PORT}"
  env_tmp="$(mktemp "$CONFIG_DIR/.env.XXXXXX")"
  awk -v value="$desired_urls" '
    BEGIN { updated = 0 }
    /^ASPNETCORE_URLS=/ { print "ASPNETCORE_URLS=" value; updated = 1; next }
    { print }
    END { if (!updated) print "ASPNETCORE_URLS=" value }
  ' "$ENV_FILE" > "$env_tmp"
  mv -f "$env_tmp" "$ENV_FILE"
fi
ensure_env_file_setting() {
  local key="$1" value="$2" force="${3:-0}" env_tmp
  if grep -q "^$key=" "$ENV_FILE" && [[ "$force" != 1 ]]; then return; fi
  env_tmp="$(mktemp "$CONFIG_DIR/.env.XXXXXX")"
  awk -v key="$key" -v value="$value" '
    BEGIN { updated = 0 }
    $0 ~ "^" key "=" { print key "=" value; updated = 1; next }
    { print }
    END { if (!updated) print key "=" value }
  ' "$ENV_FILE" > "$env_tmp"
  chown root:"$SERVICE_GROUP" "$env_tmp"
  chmod 0640 "$env_tmp"
  mv -f "$env_tmp" "$ENV_FILE"
}
if grep -q '^MEMO_DASHBOARD_SETUP_TOKEN=' "$ENV_FILE"; then
  env_tmp="$(mktemp "$CONFIG_DIR/.env.XXXXXX")"
  awk '!/^MEMO_DASHBOARD_SETUP_TOKEN=/' "$ENV_FILE" > "$env_tmp"
  chown root:"$SERVICE_GROUP" "$env_tmp"
  chmod 0640 "$env_tmp"
  mv -f "$env_tmp" "$ENV_FILE"
fi
ensure_env_file_setting MEMO_DASHBOARD_INTERNAL_SETUP 1
ensure_env_file_setting DeploymentMode "$DEPLOYMENT_MODE" "$DEPLOYMENT_MODE_EXPLICIT"
ensure_env_file_setting AllowedHosts "$ALLOWED_HOSTS" "$([ "$ALLOWED_HOSTS_EXPLICIT" -eq 1 ] || [ "$BIND_ADDRESS_EXPLICIT" -eq 1 ] && echo 1 || echo 0)"
ensure_env_file_setting MEMO_UPDATE_SIGNING_PRIVATE_KEY_FILE "$SIGNING_PRIVATE_KEY_FILE"
ensure_env_file_setting MEMO_UPDATE_SIGNING_PUBLIC_KEY_FILE "$SIGNING_PUBLIC_KEY_FILE"
ensure_env_file_setting MEMO_UPDATE_ADMIN_KEY_FILE "$ADMIN_KEY_FILE"
if [[ -n "$CHROMIUM_BIN" ]]; then
  ensure_env_file_setting MEMO_CHROMIUM_PATH "$CHROMIUM_BIN"
fi

if [[ -e "$SIGNING_PRIVATE_KEY_FILE" && ! -e "$SIGNING_PUBLIC_KEY_FILE" ]] || [[ -e "$SIGNING_PUBLIC_KEY_FILE" && ! -e "$SIGNING_PRIVATE_KEY_FILE" ]]; then
  die "RSA signing pair is incomplete; no replacement key was generated."
fi
if [[ ! -e "$SIGNING_PRIVATE_KEY_FILE" ]]; then
  command -v openssl >/dev/null 2>&1 || die "openssl is required to generate the RSA signing pair."
  private_tmp="$(mktemp "$SECRET_DIR/.private.XXXXXX")"
  public_tmp="$(mktemp "$SECRET_DIR/.public.XXXXXX")"
  chmod 0640 "$private_tmp" "$public_tmp"
  chown root:"$SERVICE_GROUP" "$private_tmp" "$public_tmp"
  openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out "$private_tmp" >/dev/null 2>&1
  openssl pkey -in "$private_tmp" -pubout -out "$public_tmp" >/dev/null 2>&1
  mv -f "$private_tmp" "$SIGNING_PRIVATE_KEY_FILE"
  mv -f "$public_tmp" "$SIGNING_PUBLIC_KEY_FILE"
else
  chmod 0640 "$SIGNING_PRIVATE_KEY_FILE" "$SIGNING_PUBLIC_KEY_FILE"
  chown root:"$SERVICE_GROUP" "$SIGNING_PRIVATE_KEY_FILE" "$SIGNING_PUBLIC_KEY_FILE"
fi

if [[ ! -s "$ADMIN_KEY_FILE" ]]; then
  umask 077
  admin_tmp="$(mktemp "$SECRET_DIR/.admin-key.XXXXXX")"
  printf '%s\n' "$(random_secret)" > "$admin_tmp"
  chmod 0640 "$admin_tmp"
  chown root:"$SERVICE_GROUP" "$admin_tmp"
  mv -f "$admin_tmp" "$ADMIN_KEY_FILE"
fi
chmod 0640 "$ADMIN_KEY_FILE"
chown root:"$SERVICE_GROUP" "$ADMIN_KEY_FILE"
chmod 0640 "$ENV_FILE"
chown root:"$SERVICE_GROUP" "$ENV_FILE"

RELEASE_ID="${PACKAGE_VERSION:-2.0.2}-$(date -u +%Y%m%d%H%M%S)-$$"
migrate_legacy_data() {
  local legacy_root="$CURRENT_LINK/App_Data"
  [[ -d "$legacy_root" ]] || return 0
  if [[ -f "$legacy_root/app_settings.json" ]]; then
    if [[ -f "$DATA_DIR/app_settings.json" ]]; then
      cmp -s "$legacy_root/app_settings.json" "$DATA_DIR/app_settings.json" || die "Legacy dashboard settings conflict with $DATA_DIR/app_settings.json."
    else
      install -o "$SERVICE_USER" -g "$SERVICE_GROUP" -m 0640 "$legacy_root/app_settings.json" "$DATA_DIR/app_settings.json"
    fi
  fi
  if [[ -d "$legacy_root/keys" ]]; then
    while IFS= read -r -d '' key_file; do
      key_name="$(basename "$key_file")"
      if [[ -f "$DATA_DIR/keys/$key_name" ]]; then
        cmp -s "$key_file" "$DATA_DIR/keys/$key_name" || die "Legacy DataProtection key conflicts: $key_name"
      else
        install -o "$SERVICE_USER" -g "$SERVICE_GROUP" -m 0640 "$key_file" "$DATA_DIR/keys/$key_name"
      fi
    done < <(find "$legacy_root/keys" -maxdepth 1 -type f -name 'key-*.xml' -print0)
  fi
}
migrate_legacy_data

RELEASE_DIR="$RELEASES_DIR/$RELEASE_ID"
[[ ! -e "$RELEASE_DIR" ]] || die "Release directory already exists: $RELEASE_DIR"
STAGING="$(mktemp -d "$INSTALL_ROOT/.staging.XXXXXX")"
cleanup() {
  if [[ -n "${STAGING:-}" && -d "$STAGING" ]]; then
    rm -rf -- "$STAGING"
  fi
  if [[ -n "${PACKAGE_TEMP_DIR:-}" && -d "$PACKAGE_TEMP_DIR" ]]; then
    rm -rf -- "$PACKAGE_TEMP_DIR"
  fi
}
trap cleanup EXIT

log "Preparing release $RELEASE_ID."
cp -a "$PACKAGE_DIR"/. "$STAGING"/
# Runtime settings and DataProtection keys live outside the release directory.
rm -rf -- "$STAGING/App_Data"
rm -f "$STAGING/install.sh"
chmod 0755 "$STAGING/MemoDashboard.Backend"
chmod -R a+rX "$STAGING"
chown -R root:root "$STAGING"
chown -R "$SERVICE_USER":"$SERVICE_GROUP" "$DATA_DIR"

had_active_service=0
if systemctl is-active --quiet "$SERVICE_NAME"; then
  had_active_service=1
  systemctl stop "$SERVICE_NAME"
fi

mv "$STAGING" "$RELEASE_DIR"
STAGING=""
OLD_BACKUP=""
ROLLBACK_ARMED=0
rollback_on_error() {
  local status=$?
  trap - ERR
  if (( ROLLBACK_ARMED == 1 )); then
    log "Deployment failed; restoring the previous release."
    restore_previous || true
  fi
  exit "$status"
}
trap rollback_on_error ERR
if [[ -L "$CURRENT_LINK" || -d "$CURRENT_LINK" ]]; then
  ROLLBACK_ARMED=1
  OLD_BACKUP="$BACKUPS_DIR/$RELEASE_ID"
  mv "$CURRENT_LINK" "$OLD_BACKUP"
fi
ln -s "$RELEASE_DIR" "$CURRENT_LINK.new"
mv -Tf "$CURRENT_LINK.new" "$CURRENT_LINK"

cat > "$UNIT_FILE" <<EOF
[Unit]
Description=MemoDashboard web dashboard
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=${SERVICE_USER}
Group=${SERVICE_GROUP}
WorkingDirectory=${CURRENT_LINK}
EnvironmentFile=-${ENV_FILE}
ExecStart=${CURRENT_LINK}/MemoDashboard.Backend
Restart=on-failure
RestartSec=5
TimeoutStopSec=30
NoNewPrivileges=true
PrivateTmp=true
ProtectHome=true
ProtectSystem=strict
ProtectKernelTunables=true
ProtectKernelModules=true
ProtectControlGroups=true
RestrictSUIDSGID=true
LockPersonality=true
ReadWritePaths=${INSTALL_ROOT} ${DATA_DIR}
UMask=0027
LimitNOFILE=65535

[Install]
WantedBy=multi-user.target
EOF
chmod 0644 "$UNIT_FILE"
systemctl daemon-reload
systemctl enable "$SERVICE_NAME" >/dev/null

restore_previous() {
  systemctl stop "$SERVICE_NAME" >/dev/null 2>&1 || true
  if [[ -L "$CURRENT_LINK" || -d "$CURRENT_LINK" ]]; then
    rm -rf -- "$CURRENT_LINK"
  fi
  if [[ -n "$OLD_BACKUP" && ( -L "$OLD_BACKUP" || -d "$OLD_BACKUP" ) ]]; then
    mv "$OLD_BACKUP" "$CURRENT_LINK"
  fi
  systemctl daemon-reload
  if [[ -n "$OLD_BACKUP" && ( -L "$CURRENT_LINK" || -d "$CURRENT_LINK" ) ]]; then
    systemctl start "$SERVICE_NAME" || true
  fi
}

if (( NO_START == 1 )); then
  log "Installed without starting the service (--no-start)."
  log "Unit: $UNIT_FILE"
  log "Start with: systemctl start $SERVICE_NAME"
  exit 0
fi

systemctl reset-failed "$SERVICE_NAME" >/dev/null 2>&1 || true
systemctl restart "$SERVICE_NAME"
# Probe the address the service actually binds to. A specific bind address
# (for example the server LAN IP) does not necessarily listen on loopback;
# wildcard binds are probed through loopback for a deterministic local check.
health_host="$EFFECTIVE_BIND_ADDRESS"
case "$health_host" in
  ""|0.0.0.0|::|"*") health_host="127.0.0.1" ;;
esac
if [[ "$health_host" == *:* && "$health_host" != \[*\] ]]; then
  health_url_host="[$health_host]"
else
  health_url_host="$health_host"
fi
health_url="http://${health_url_host}:${EFFECTIVE_PORT}/api/setup/status"
healthy=0
for _ in $(seq 1 30); do
  if command -v curl >/dev/null 2>&1; then
    if curl -fsS --max-time 2 "$health_url" >/dev/null 2>&1; then
      healthy=1
      break
    fi
  elif command -v wget >/dev/null 2>&1; then
    if wget -q --spider --timeout=2 "$health_url" >/dev/null 2>&1; then
      healthy=1
      break
    fi
  else
    if systemctl is-active --quiet "$SERVICE_NAME"; then
      healthy=1
      break
    fi
  fi
  sleep 1
done

if (( healthy != 1 )); then
  log "The new service did not answer /api/setup/status; restoring the previous release."
  restore_previous
  ROLLBACK_ARMED=0
  die "Installation rolled back. Inspect: journalctl -u $SERVICE_NAME -n 100 --no-pager"
fi
ROLLBACK_ARMED=0

log "Installation completed successfully."
log "Dashboard: http://${health_url_host}:${EFFECTIVE_PORT}/"
log "Setup status: $health_url"
log "Service: systemctl status $SERVICE_NAME"
log "External signing/admin secret files are configured outside the release."
if [[ -n "$OLD_BACKUP" ]]; then
  log "Previous release preserved at: $OLD_BACKUP"
fi