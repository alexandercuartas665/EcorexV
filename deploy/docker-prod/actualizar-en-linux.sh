#!/usr/bin/env bash
# Actualiza ECOREX prod por la VIA RAPIDA: descarga la imagen que publico el CI en GHCR y
# recrea el contenedor. NO compila en el host (eso es el from-git, lento y con OOM).
#   Uso:  cd /opt/ecorex && ./actualizar-en-linux.sh
#   Rollback a un tag viejo:  ECOREX_IMAGE_TAG=<sha> ./actualizar-en-linux.sh
set -euo pipefail

cd /opt/ecorex
COMPOSE=(docker compose -f docker-compose.image.yml)
TAG="${ECOREX_IMAGE_TAG:-latest}"

echo "== ECOREX deploy (pull GHCR, tag=$TAG) =="

# 1) Backup (BD + lo que el backup.sh contemple). No aborta el deploy si falla.
if [ -x ./backup.sh ]; then
  echo "-- Backup --"
  ./backup.sh || echo "WARN: backup.sh fallo; continuo con el deploy"
else
  echo "WARN: no hay backup.sh ejecutable; omito backup"
fi

# 2) Imagen actual (para rollback manual si hiciera falta).
OLD_IMG="$(docker inspect -f '{{.Image}}' ecorex-app 2>/dev/null || echo none)"
echo "Imagen anterior (rollback): $OLD_IMG"

# 3) Pull + up (solo la app; postgres no cambia).
echo "-- Pull --"
ECOREX_IMAGE_TAG="$TAG" "${COMPOSE[@]}" pull ecorex-app
echo "-- Up --"
ECOREX_IMAGE_TAG="$TAG" "${COMPOSE[@]}" up -d

# 4) Salud.
sleep 4
echo "-- Salud --"
docker inspect -f 'Restarts={{.RestartCount}} Running={{.State.Running}}' ecorex-app || true
PORT="$(grep -E '^ECOREX_PORT=' .env 2>/dev/null | cut -d= -f2- || true)"
PORT="${PORT:-5480}"
code="$(curl -s -o /dev/null -w '%{http_code}' -m 15 "http://127.0.0.1:${PORT}/login" || echo 000)"
ver="$(curl -s -m 15 "http://127.0.0.1:${PORT}/login" | grep -oiE 'v[0-9]+\.[0-9]+\.[0-9]+' | head -1 || true)"
echo "HTTP ${code}  version=${ver}"

echo "OK (si algo salio mal, rollback: ECOREX_IMAGE_TAG=<sha-anterior> ./actualizar-en-linux.sh ;"
echo "    imagen anterior era: $OLD_IMG)"
