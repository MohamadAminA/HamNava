#!/usr/bin/env bash
set -euo pipefail

APP_ROOT="/opt/hamnava"
RELEASES_DIR="${APP_ROOT}/releases"
CURRENT_LINK="${APP_ROOT}/current"
SERVICE_NAME="hamnava.service"

CURRENT_TARGET="$(readlink -f "${CURRENT_LINK}")"
PREVIOUS_TARGET="$(ls -1dt "${RELEASES_DIR}"/* | grep -v "^${CURRENT_TARGET}$" | head -n 1 || true)"

if [ -z "${PREVIOUS_TARGET}" ]; then
  echo "[ERROR] No previous release found in ${RELEASES_DIR} to roll back to." >&2
  exit 1
fi

echo "[Hamnava] Rolling back from ${CURRENT_TARGET} to ${PREVIOUS_TARGET}..."
sudo ln -sfn "${PREVIOUS_TARGET}" "${CURRENT_LINK}"
sudo systemctl restart "${SERVICE_NAME}"
"$(dirname "$0")/health-check.sh" "http://127.0.0.1:5000/health/ready"
echo "[Hamnava] Rollback completed successfully."
