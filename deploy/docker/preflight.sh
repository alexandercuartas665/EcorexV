#!/usr/bin/env bash
# preflight.sh - Validaciones previas al arranque de la pila Docker de ECOREX.tareas.
# Espejo POSIX de preflight.ps1. Corre ANTES de `docker compose up -d`.
set -u

FAIL=0
DIR="$(cd "$(dirname "$0")" && pwd)"

# Defaults del bloque dedicado ECOREX
POSTGRES_PORT=5442; SQLSERVER_PORT=1443; REDIS_PORT=6389
RABBITMQ_PORT=5682; RABBITMQ_MGMT_PORT=15682; ADMINER_PORT=8092
[ -f "$DIR/.env" ] && . "$DIR/.env" 2>/dev/null || true

echo -n "[1/4] Docker... "
if docker info >/dev/null 2>&1; then echo OK; else
  echo "FALLO: Docker no responde. Inicia Docker Desktop."; FAIL=1
fi

echo "[2/4] Puertos del bloque dedicado..."
OWN="$(docker ps --filter 'name=ecorex-tareas-' --format '{{.Ports}}' 2>/dev/null | tr '\n' ' ')"
for spec in "POSTGRES:$POSTGRES_PORT" "SQLSERVER:$SQLSERVER_PORT" "REDIS:$REDIS_PORT" \
            "RABBITMQ:$RABBITMQ_PORT" "RABBITMQ_MGMT:$RABBITMQ_MGMT_PORT" "ADMINER:$ADMINER_PORT"; do
  name="${spec%%:*}"; port="${spec##*:}"
  if command -v ss >/dev/null 2>&1; then busy="$(ss -ltn "sport = :$port" 2>/dev/null | tail -n +2)"
  else busy="$(netstat -an 2>/dev/null | grep -E "[:.]$port .*LISTEN" || true)"; fi
  if [ -n "$busy" ] && ! echo "$OWN" | grep -q ":$port->"; then
    echo "  $name $port OCUPADO por otro proceso"; FAIL=1
  else
    echo "  $name $port libre"
  fi
done

echo -n "[3/4] Contenedores previos... "
DEAD="$(docker ps -a --filter 'name=ecorex-tareas-' --filter 'status=exited' --format '{{.Names}}' 2>/dev/null | tr '\n' ' ')"
if [ -n "${DEAD// /}" ]; then echo "ATENCION: detenidos de corridas previas: $DEAD"; else echo OK; fi

echo -n "[4/4] Recursos... "
MEM="$(docker info --format '{{.MemTotal}}' 2>/dev/null || echo 0)"
if [ "$MEM" -ge 2147483648 ] 2>/dev/null; then echo OK; else
  echo "FALLO: Docker con menos de 2 GB de RAM (SQL Server no arrancara)."; FAIL=1
fi

if [ "$FAIL" -ne 0 ]; then echo; echo "PRE-FLIGHT FALLO"; exit 1; fi
echo; echo "PRE-FLIGHT OK: listo para docker compose up -d"
