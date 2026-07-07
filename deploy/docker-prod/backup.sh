#!/usr/bin/env bash
# =========================================================================
#  backup.sh - dump de la BD de produccion de ECOREX.
#  Uso (en el server, desde /opt/ecorex):
#     ./backup.sh
#  Deja el .sql en ./backups/ (ignorado por git). Programa con cron:
#     0 3 * * *  cd /opt/ecorex && ./backup.sh >> backups/cron.log 2>&1
# =========================================================================
set -euo pipefail

DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$DIR"

# Lee POSTGRES_USER / POSTGRES_DB del .env (sin exportar el resto).
USER_DB="$(grep -E '^POSTGRES_USER=' .env | cut -d= -f2-)"
NAME_DB="$(grep -E '^POSTGRES_DB=' .env | cut -d= -f2-)"
: "${USER_DB:?falta POSTGRES_USER en .env}"
: "${NAME_DB:?falta POSTGRES_DB en .env}"

mkdir -p backups
STAMP="$(date +%Y-%m-%d-%H%M)"
OUT="backups/ecorex-${STAMP}.sql"

echo "Dump de ${NAME_DB} -> ${OUT}"
docker exec ecorex-postgres-prod pg_dump -U "${USER_DB}" -d "${NAME_DB}" > "${OUT}"
gzip -f "${OUT}"
echo "OK: ${OUT}.gz"
