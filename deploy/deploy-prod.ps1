# =============================================================================
# deploy-prod.ps1  -  Despliegue de ECOREX.tareas a PRODUCCION (app2 / 10.0.0.3)
# =============================================================================
# Ejecuta el runbook estandar sobre el VPS de produccion, en este orden:
#   1) ./backup.sh                                   (respaldo de la BD ANTES de nada)
#   2) docker compose ... build --no-cache           (build-from-git de la rama)
#   3) docker compose ... up -d                       (levanta la nueva version)
#   4) verificacion: version que sirve /login (interno 5480 + publico app2)
#
# El build clona el codigo desde GitHub segun ECOREX_BRANCH del .env del servidor
# (NO desde tu disco): asegurate de haber hecho push de la rama ANTES de desplegar.
#
# REQUISITOS
#   - El tunel/VPN al 10.0.0.3 ARRIBA (este script hace SSH directo a esa IP privada).
#   - Tu llave SSH en %USERPROFILE%\.ssh\id_ed25519_visal (o pasa -KeyPath).
#
# USO (PowerShell)
#   ./deploy/deploy-prod.ps1                          # despliega la rama del .env tal cual
#   ./deploy/deploy-prod.ps1 -Version 0.15.20         # ademas verifica que aterrice esa version
#   ./deploy/deploy-prod.ps1 -Branch feat/mi-rama     # fija ECOREX_BRANCH en el .env y despliega
#   ./deploy/deploy-prod.ps1 -SkipBackup              # (raro) omite el backup previo
#
# SEGURIDAD: solo toca la linea ECOREX_BRANCH del .env cuando pasas -Branch; NUNCA
# toca ECOREX_SEED_ADMIN_PASSWORD ni ningun secreto. El resto del .env queda INTACTO.
# =============================================================================

param(
    [string]$Version = "",                                   # opcional: version a verificar (sin la 'v')
    [string]$Branch  = "",                                   # opcional: fija ECOREX_BRANCH antes del build
    [string]$VpsHost = "root@10.0.0.3",                      # destino SSH (IP privada por VPN)
    [string]$KeyPath = (Join-Path $env:USERPROFILE ".ssh\id_ed25519_visal"),
    [string]$RemoteDir = "/opt/ecorex",
    [string]$ComposeFile = "docker-compose.from-git.yml",
    [switch]$SkipBackup                                      # omite ./backup.sh (no recomendado)
)

$ErrorActionPreference = "Stop"

function Say($msg, $color = "Cyan") { Write-Host "`n==== $msg ====" -ForegroundColor $color }

if (-not (Test-Path $KeyPath)) {
    Write-Error "No se encontro la llave SSH: $KeyPath  (pasa -KeyPath con la ruta correcta)."
    exit 1
}

$sshBase = @("-o", "BatchMode=yes", "-o", "ConnectTimeout=20", "-i", $KeyPath, $VpsHost)

# --- 0) Comprobar que el tunel/VPN esta arriba (SSH responde) ---
Say "Comprobando conexion con $VpsHost"
$ping = & ssh @sshBase "echo ok" 2>&1
if ($LASTEXITCODE -ne 0 -or "$ping".Trim() -ne "ok") {
    Write-Error "No hay conexion SSH con $VpsHost. Verifica que el tunel/VPN al 10.0.0.3 este ARRIBA. Detalle: $ping"
    exit 1
}
Write-Host "Conexion OK."

# --- 1) (opcional) Fijar ECOREX_BRANCH en el .env del servidor ---
if (-not [string]::IsNullOrWhiteSpace($Branch)) {
    Say "Fijando ECOREX_BRANCH=$Branch en $RemoteDir/.env (sin tocar el resto)"
    # Reemplaza SOLO la linea ECOREX_BRANCH=...; si no existe, la agrega. No toca ninguna otra clave.
    $setBranch = "cd $RemoteDir && (grep -q '^ECOREX_BRANCH=' .env && sed -i 's|^ECOREX_BRANCH=.*|ECOREX_BRANCH=$Branch|' .env || echo 'ECOREX_BRANCH=$Branch' >> .env) && grep '^ECOREX_BRANCH=' .env"
    $res = & ssh @sshBase $setBranch 2>&1
    if ($LASTEXITCODE -ne 0) { Write-Error "No se pudo fijar ECOREX_BRANCH. Detalle: $res"; exit 1 }
    Write-Host "Ahora el .env dice: $res"
}

# Rama efectiva (para el aviso de push)
$effBranch = (& ssh @sshBase "grep '^ECOREX_BRANCH=' $RemoteDir/.env | cut -d= -f2-" 2>&1).Trim()
Say "Rama a desplegar (build-from-git): $effBranch" "Yellow"
Write-Host "Recuerda haber hecho 'git push' de esa rama ANTES de continuar."

# --- 2) Backup de la BD (regla de oro) ---
if (-not $SkipBackup) {
    Say "Backup de la BD (./backup.sh)"
    & ssh @sshBase "cd $RemoteDir && ./backup.sh"
    if ($LASTEXITCODE -ne 0) { Write-Error "El backup fallo. Se ABORTA el deploy (no se toca la version en curso)."; exit 1 }
} else {
    Say "SALTANDO backup (-SkipBackup)" "Red"
}

# --- 3) Build (from git, sin cache) ---
Say "Build --no-cache (clona la rama y compila; tarda varios minutos)"
& ssh @sshBase "cd $RemoteDir && docker compose -f $ComposeFile build --no-cache"
if ($LASTEXITCODE -ne 0) { Write-Error "El build fallo. La version en curso sigue arriba (no se hizo 'up')."; exit 1 }

# --- 4) Up (levanta la nueva version) ---
Say "Levantando la nueva version (up -d)"
& ssh @sshBase "cd $RemoteDir && docker compose -f $ComposeFile up -d"
if ($LASTEXITCODE -ne 0) { Write-Error "El 'up -d' fallo. Revisa 'docker compose ps' y los logs en el servidor."; exit 1 }

# --- 5) Verificacion: version + salud ---
Say "Verificando (espera a que arranque)"
Start-Sleep -Seconds 25
$verInt = (& ssh @sshBase "curl -s -m8 http://127.0.0.1:5480/login | grep -oiE 'v[0-9]+\.[0-9]+\.[0-9]+' | head -1" 2>&1).Trim()
$httpInt = (& ssh @sshBase "curl -s -m8 -o /dev/null -w '%{http_code}' http://127.0.0.1:5480/login" 2>&1).Trim()
Write-Host "Interno (5480):  version=$verInt  login=$httpInt"

# Publico (Caddy). Puede tardar unos segundos mas en refrescar.
try {
    $pub = Invoke-WebRequest -UseBasicParsing -TimeoutSec 20 "https://app2.bitcode.com.co/login"
    $verPub = ([regex]::Match($pub.Content, 'v[0-9]+\.[0-9]+\.[0-9]+')).Value
    Write-Host "Publico (app2):  version=$verPub  login=$($pub.StatusCode)"
} catch {
    Write-Host "Publico (app2):  no se pudo consultar ahora ($($_.Exception.Message)). Reintenta en unos segundos."
}

Say "LISTO" "Green"
if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $want = "v" + $Version.TrimStart('v')
    if ($verInt -eq $want) {
        Write-Host "Aterrizo la version esperada: $want" -ForegroundColor Green
    } else {
        Write-Host "OJO: esperabas $want pero el servidor sirve '$verInt'. Revisa que la rama tenga el commit y que el build no haya cacheado." -ForegroundColor Red
    }
} else {
    Write-Host "Deploy terminado. Version servida: $verInt"
}
