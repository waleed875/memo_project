#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

SECRET_DIR="${MEMO_DASHBOARD_SECRET_DIR:-/etc/memo-dashboard/secrets}"
SERVICE_USER="${SERVICE_USER:-memo-dashboard}"
SERVICE_GROUP="${SERVICE_GROUP:-$SERVICE_USER}"
ENABLE_CHECKIN=0
REGENERATE_SETUP_TOKEN=0

usage() {
  cat <<'EOF'
Usage: sudo ./Provision-MemoDashboardSecurity.sh [options]

  --secret-dir PATH       External secret directory
  --service-user NAME     Service account (default: memo-dashboard)
  --enable-checkin        Create the optional device check-in key
  --regenerate-setup-token Explicitly replace the first-run token
EOF
}

die() { printf '[memo-dashboard-security] ERROR: %s\n' "$*" >&2; exit 1; }
log() { printf '[memo-dashboard-security] %s\n' "$*"; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    --secret-dir) [[ $# -ge 2 ]] || die "--secret-dir needs a value"; SECRET_DIR="$2"; shift 2 ;;
    --service-user) [[ $# -ge 2 ]] || die "--service-user needs a value"; SERVICE_USER="$2"; SERVICE_GROUP="$2"; shift 2 ;;
    --enable-checkin) ENABLE_CHECKIN=1; shift ;;
    --regenerate-setup-token) REGENERATE_SETUP_TOKEN=1; shift ;;
    --help|-h) usage; exit 0 ;;
    *) die "Unknown option: $1" ;;
  esac
done

[[ "$(id -u)" -eq 0 ]] || die "Run as root."
[[ "$SECRET_DIR" == /* ]] || die "--secret-dir must be absolute."
[[ "$SECRET_DIR" != "/" && "$SECRET_DIR" != "/etc" && "$SECRET_DIR" != "/var" && "$SECRET_DIR" != "/opt" ]] || die "Refusing a broad secret directory."
[[ "$SERVICE_USER" =~ ^[a-zA-Z0-9_.@-]+$ ]] || die "Invalid service user."
getent passwd "$SERVICE_USER" >/dev/null 2>&1 || die "Service account does not exist: $SERVICE_USER"

install -d -o root -g "$SERVICE_GROUP" -m 0750 "$SECRET_DIR"

random_secret() {
  if command -v openssl >/dev/null 2>&1; then
    openssl rand -hex 48
  else
    head -c 48 /dev/urandom | od -An -tx1 | tr -d ' \n'
    printf '\n'
  fi
}

write_new_secret() {
  local path="$1" value="$2" temp
  [[ ! -e "$path" ]] || die "Refusing to overwrite existing secret: $path"
  temp="$(mktemp "$SECRET_DIR/.secret.XXXXXX")"
  chmod 0640 "$temp"
  chown root:"$SERVICE_GROUP" "$temp"
  printf '%s\n' "$value" > "$temp"
  mv -n -- "$temp" "$path" || {
    rm -f -- "$temp"
    die "Secret creation raced or failed: $path"
  }
}

ensure_secret() {
  local path="$1"
  if [[ -s "$path" ]]; then
    chmod 0640 "$path"
    chown root:"$SERVICE_GROUP" "$path"
    return 1
  fi
  write_new_secret "$path" "$(random_secret)"
  return 0
}

if [[ "$REGENERATE_SETUP_TOKEN" == 1 ]]; then
  rm -f -- "$SECRET_DIR/setup-token"
fi
setup_token_created=0
if ensure_secret "$SECRET_DIR/setup-token"; then setup_token_created=1; fi

private_key="$SECRET_DIR/signing-private-key.pem"
public_key="$SECRET_DIR/signing-public-key.pem"
if [[ -e "$private_key" && ! -e "$public_key" ]] || [[ -e "$public_key" && ! -e "$private_key" ]]; then
  die "RSA pair is incomplete; no replacement key was generated."
fi
if [[ ! -e "$private_key" ]]; then
  command -v openssl >/dev/null 2>&1 || die "openssl is required to generate the RSA signing pair."
  temp_private="$(mktemp "$SECRET_DIR/.private.XXXXXX")"
  temp_public="$(mktemp "$SECRET_DIR/.public.XXXXXX")"
  chmod 0640 "$temp_private" "$temp_public"
  chown root:"$SERVICE_GROUP" "$temp_private" "$temp_public"
  openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out "$temp_private" >/dev/null 2>&1
  openssl pkey -in "$temp_private" -pubout -out "$temp_public" >/dev/null 2>&1
  mv -n -- "$temp_private" "$private_key" || die "Could not install private signing key."
  mv -n -- "$temp_public" "$public_key" || die "Could not install public signing key."
else
  chmod 0640 "$private_key" "$public_key"
  chown root:"$SERVICE_GROUP" "$private_key" "$public_key"
fi

ensure_secret "$SECRET_DIR/legacy-admin-key" || true
if [[ "$ENABLE_CHECKIN" == 1 ]]; then
  ensure_secret "$SECRET_DIR/checkin-device-key" || true
fi

log "External security files are ready: $SECRET_DIR"
log "Configure these non-secret file references in the service environment:"
log "MEMO_DASHBOARD_SETUP_TOKEN_FILE=$SECRET_DIR/setup-token"
log "MEMO_UPDATE_SIGNING_PRIVATE_KEY_FILE=$private_key"
log "MEMO_UPDATE_SIGNING_PUBLIC_KEY_FILE=$public_key"
log "MEMO_UPDATE_ADMIN_KEY_FILE=$SECRET_DIR/legacy-admin-key"
if [[ "$ENABLE_CHECKIN" == 1 ]]; then log "MEMO_UPDATE_CHECKIN_KEY_FILE=$SECRET_DIR/checkin-device-key"; fi
if [[ "$setup_token_created" == 1 ]]; then
  printf '\nFirst-run setup token — copy it now; this command will not print it again:\n'
  tr -d '\r\n' < "$SECRET_DIR/setup-token"
  printf '\n'
else
  log "The first-run token already existed and was not displayed."
fi
log "Private, admin, and check-in secret values were not printed."