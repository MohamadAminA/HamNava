#!/usr/bin/env bash
set -euo pipefail

# Rollback-safe timestamped deployment script for Ubuntu Linux
ARTIFACT_DIR="${1:-./artifacts/publish}"
APP_ROOT="/opt/hamnava"
RELEASES_DIR="${APP_ROOT}/releases"
CURRENT_LINK="${APP_ROOT}/current"
SERVICE_NAME="hamnava.service"
TIMESTAMP="$(date +%Y%m%d_%H%M%S)"
NEW_RELEASE="${RELEASES_DIR}/${TIMESTAMP}"
PREVIOUS_RELEASE=""

if [ ! -d "${ARTIFACT_DIR}" ]; then
  echo "[ERROR] Publish artifact directory not found: ${ARTIFACT_DIR}" >&2
  exit 1
fi

if [ -L "${CURRENT_LINK}" ]; then
  PREVIOUS_RELEASE="$(readlink -f "${CURRENT_LINK}")"
fi

echo "[Hamnava] Creating new release directory: ${NEW_RELEASE}"
sudo mkdir -p "${NEW_RELEASE}" /var/lib/hamnava/media /var/lib/hamnava/temp /var/lib/hamnava/dataprotection-keys /var/log/hamnava
sudo rsync -a --delete "${ARTIFACT_DIR}/" "${NEW_RELEASE}/"
sudo chown -R hamnava:hamnava "${NEW_RELEASE}" /var/lib/hamnava /var/log/hamnava

echo "[Hamnava] Switching atomic symlink -> ${NEW_RELEASE}"
sudo ln -sfn "${NEW_RELEASE}" "${CURRENT_LINK}"

echo "[Hamnava] Restarting ${SERVICE_NAME}..."
sudo systemctl restart "${SERVICE_NAME}"

echo "[Hamnava] Running post-deploy health check..."
if "$(dirname "$0")/health-check.sh" "http://127.0.0.1:5000/health/ready"; then
  echo "[Hamnava] Deployment succeeded! Retaining latest 5 releases."
  ls -1dt "${RELEASES_DIR}"/* | tail -n +6 | xargs -r sudo rm -rf
else
  echo "[ERROR] Health check FAILED! Initiating automatic rollback..." >&2
  if [ -n "${PREVIOUS_RELEASE}" ] && [ -d "${PREVIOUS_RELEASE}" ]; then
    sudo ln -sfn "${PREVIOUS_RELEASE}" "${CURRENT_LINK}"
    sudo systemctl restart "${SERVICE_NAME}"
    echo "[Hamnava] Rolled back to ${PREVIOUS_RELEASE}."
  fi
  exit 1
fi
