#!/usr/bin/env bash
set -euo pipefail

# SQL Server encrypted/compressed backup script (reads DB credentials from /etc/hamnava/hamnava.env)
ENV_FILE="/etc/hamnava/hamnava.env"
BACKUP_DIR="/var/backups/hamnava/sql"
DB_NAME="${HAMNAVA_DB_NAME:-HamnavaDb}"
RETENTION_DAYS="${HAMNAVA_BACKUP_RETENTION_DAYS:-14}"
TIMESTAMP="$(date +%Y%m%d_%H%M%S)"
BACKUP_FILE="${BACKUP_DIR}/${DB_NAME}_${TIMESTAMP}.bak"

if [ -f "${ENV_FILE}" ]; then
  set -a
  # shellcheck disable=SC1090
  source "${ENV_FILE}"
  set +a
fi

if [ -z "${SQL_BACKUP_USER:-}" ] || [ -z "${SQL_BACKUP_PASSWORD:-}" ]; then
  echo "[ERROR] SQL_BACKUP_USER and SQL_BACKUP_PASSWORD must be set in ${ENV_FILE}." >&2
  exit 1
fi

mkdir -p "${BACKUP_DIR}"
chmod 700 "${BACKUP_DIR}"

/opt/mssql-tools18/bin/sqlcmd -S localhost -U "${SQL_BACKUP_USER}" -P "${SQL_BACKUP_PASSWORD}" -C -Q \
  "BACKUP DATABASE [${DB_NAME}] TO DISK = N'${BACKUP_FILE}' WITH COMPRESSION, CHECKSUM, INIT, STATS = 10;"

echo "[Hamnava] Verifying backup integrity..."
/opt/mssql-tools18/bin/sqlcmd -S localhost -U "${SQL_BACKUP_USER}" -P "${SQL_BACKUP_PASSWORD}" -C -Q \
  "RESTORE VERIFYONLY FROM DISK = N'${BACKUP_FILE}' WITH CHECKSUM;"

echo "[Hamnava] Pruning backups older than ${RETENTION_DAYS} days..."
find "${BACKUP_DIR}" -type f -name "*.bak" -mtime "+${RETENTION_DAYS}" -delete

echo "[Hamnava] Database backup completed and verified: ${BACKUP_FILE}"
