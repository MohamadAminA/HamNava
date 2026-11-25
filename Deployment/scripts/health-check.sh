#!/usr/bin/env bash
set -euo pipefail

URL="${1:-http://127.0.0.1:5000/health/ready}"
MAX_RETRIES=12
SLEEP_SECONDS=2

for ((i=1; i<=MAX_RETRIES; i++)); do
  HTTP_CODE="$(curl -s -o /dev/null -w '%{http_code}' --max-time 5 "${URL}" || echo "000")"
  if [ "${HTTP_CODE}" = "200" ]; then
    echo "[Hamnava] Health check OK (${URL} -> HTTP 200)."
    exit 0
  fi
  echo "[Hamnava] Waiting for healthy response (${i}/${MAX_RETRIES}, got HTTP ${HTTP_CODE})..."
  sleep "${SLEEP_SECONDS}"
done

echo "[ERROR] Health check failed after ${MAX_RETRIES} attempts: ${URL}" >&2
exit 1
