#!/usr/bin/env bash
set -Eeuo pipefail

# Compatibility wrapper for the pre-1.0.3 installer name.
# All installation work is delegated to the maintained installer so that
# runtime checks, App_Data preservation, health checks, and rollback remain
# identical for clean installs and upgrades.
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
INSTALLER="$SCRIPT_DIR/Install-MemoDashboard.sh"

[[ -f "$INSTALLER" ]] || {
  printf '[memo-dashboard] ERROR: %s is a compatibility wrapper. Place Install-MemoDashboard.sh beside it, or use install.sh from the Dashboard ZIP.\n' "${BASH_SOURCE[0]}" >&2
  exit 1
}

translated=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --root)
      [[ $# -ge 2 ]] || { printf '[memo-dashboard] ERROR: --root needs a value.\n' >&2; exit 2; }
      translated+=(--install-root "$2")
      shift 2
      ;;
    --service)
      [[ $# -ge 2 ]] || { printf '[memo-dashboard] ERROR: --service needs a value.\n' >&2; exit 2; }
      translated+=(--service-name "$2")
      shift 2
      ;;
    --user)
      [[ $# -ge 2 ]] || { printf '[memo-dashboard] ERROR: --user needs a value.\n' >&2; exit 2; }
      translated+=(--service-user "$2")
      shift 2
      ;;
    *)
      translated+=("$1")
      shift
      ;;
  esac
done

exec bash "$INSTALLER" "${translated[@]}"