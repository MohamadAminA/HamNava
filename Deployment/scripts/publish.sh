#!/usr/bin/env bash
set -euo pipefail

# Build and publish Hamnava.Web into a clean release artifact directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
OUTPUT_DIR="${1:-${REPO_ROOT}/artifacts/publish}"

echo "[Hamnava] Restoring and publishing Release build (.NET 10)..."
rm -rf "${OUTPUT_DIR}"
mkdir -p "${OUTPUT_DIR}"

dotnet restore "${REPO_ROOT}/Hamnava.sln"
dotnet publish "${REPO_ROOT}/Hamnava.Web/Hamnava.Web.csproj" \
  -c Release \
  -o "${OUTPUT_DIR}" \
  --no-restore

echo "[Hamnava] Generating idempotent EF Core SQL migration bundle..."
dotnet ef migrations script \
  --idempotent \
  --project "${REPO_ROOT}/Hamnava.DataLayer/Hamnava.DataLayer.csproj" \
  --startup-project "${REPO_ROOT}/Hamnava.Web/Hamnava.Web.csproj" \
  -o "${OUTPUT_DIR}/migrations.idempotent.sql" || echo "[WARN] Ensure dotnet-ef tool is installed to emit SQL script."

echo "[Hamnava] Publish completed: ${OUTPUT_DIR}"
