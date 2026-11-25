#!/usr/bin/env bash
set -euo pipefail

echo "[Hamnava] Reloading systemd and restarting hamnava.service..."
sudo systemctl daemon-reload
sudo systemctl restart hamnava.service
sudo systemctl status hamnava.service --no-pager
"$(dirname "$0")/health-check.sh" "http://127.0.0.1:5000/health/ready"
