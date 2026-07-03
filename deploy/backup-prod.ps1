# Respaldo de la base de datos de PRODUCCION (Railway PostgreSQL 18).
# Usar ANTES de cada deploy que incluya migraciones (regla de oro del deploy).
#
# Requisitos: Docker Desktop corriendo.
# La cadena de conexion NO se versiona: se pasa por variable de entorno.
#
# Uso:
#   $env:ECOREX_PROD_DB_URL = "postgresql://USER:PASS@HOST:PORT/DB"   # Railway -> servicio Postgres -> Variables -> DATABASE_PUBLIC_URL
#   ./deploy/backup-prod.ps1
#
# Opcional: -OutDir "ruta" para cambiar la carpeta de salida (por defecto ..\ecorex-backups, fuera del repo).
#
# Nota: la BD de Railway es PostgreSQL 18; por eso el dump corre dentro de un contenedor
# postgres:18 y NO con el pg_dump local (que puede ser otra version y rechaza el dump).

param(
    [string]$OutDir = (Join-Path $PSScriptRoot "..\..\ecorex-backups")
)

$ErrorActionPreference = "Stop"

$url = $env:ECOREX_PROD_DB_URL
if ([string]::IsNullOrWhiteSpace($url)) {
    Write-Error "Falta ECOREX_PROD_DB_URL. Definela con la DATABASE_PUBLIC_URL de la Postgres de Railway (no se versiona)."
    exit 1
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$abs = (Resolve-Path $OutDir).Path
$ts = Get-Date -Format "yyyyMMdd_HHmm"
$fname = "prod_backup_$ts.dump"

Write-Host "Respaldando produccion (PostgreSQL 18) -> $abs\$fname ..."

# pg_dump dentro de un contenedor PG18; escribe en el volumen montado (no se pipea binario).
docker run --rm -e DBURL="$url" -e OUT="/backup/$fname" -v "${abs}:/backup" postgres:18-alpine `
    sh -c 'pg_dump "$DBURL" --no-owner --no-acl -Fc -f "$OUT"'

if ($LASTEXITCODE -ne 0) {
    Write-Error "pg_dump fallo (exit $LASTEXITCODE)."
    exit 1
}

$full = Join-Path $abs $fname
$kb = [math]::Round((Get-Item $full).Length / 1KB, 1)
Write-Host "OK. Backup creado: $full ($kb KB)"
Write-Host ""
Write-Host "Para restaurar (cuidado, sobreescribe la BD destino):"
Write-Host "  docker run --rm -e DBURL=`$env:ECOREX_PROD_DB_URL -v `"${abs}:/backup`" postgres:18-alpine \"
Write-Host "    sh -c 'pg_restore --no-owner --no-acl --clean --if-exists -d \`"`$DBURL\`" /backup/$fname'"
