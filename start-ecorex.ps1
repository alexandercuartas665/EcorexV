<#
  start-ecorex.ps1
  Carga ECOREX.tareas: levanta la infraestructura (Postgres via docker), compila la
  solucion, arranca la consola (Ecorex.SuperAdmin) y abre la pagina en el navegador.

  Uso:
    .\start-ecorex.ps1                 # puerto 8080 (necesario para el tunel/webhook de WhatsApp)
    .\start-ecorex.ps1 -Port 5232      # otro puerto
    .\start-ecorex.ps1 -SkipBuild      # no recompila (arranque mas rapido)
    .\start-ecorex.ps1 -SkipDocker     # no toca docker
    .\start-ecorex.ps1 -NoBrowser      # no abre el navegador
#>
param(
    [int]$Port = 8080,
    [switch]$SkipBuild,
    [switch]$SkipDocker,
    [switch]$NoBrowser
)

$ErrorActionPreference = 'Stop'

$Root      = $PSScriptRoot
$Backend   = Join-Path $Root 'apps\backend'
$Solution  = Join-Path $Backend 'Ecorex.sln'
$AppProj   = Join-Path $Backend 'src\Ecorex.SuperAdmin'
$DockerDir = Join-Path $Root 'deploy\docker'
$Url       = "http://localhost:$Port"
$PidFile   = Join-Path $Root '.ecorex-pid'

# Cadena de conexion y entorno. Si ya viene definida en la sesion, se respeta.
if (-not $env:ECOREX_DB_CONNECTION) {
    $env:ECOREX_DB_CONNECTION = 'Host=localhost;Port=5442;Database=ecorex_dev;Username=ecorex;Password=postgres'
}
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS        = $Url

Write-Host "==> ECOREX.tareas: cargando en $Url" -ForegroundColor Cyan

# 1) Infraestructura local (Postgres) via docker.
if (-not $SkipDocker -and (Test-Path (Join-Path $DockerDir 'docker-compose.yml'))) {
    Write-Host "==> Pre-flight de infraestructura..." -ForegroundColor DarkCyan
    & (Join-Path $DockerDir 'preflight.ps1')
    if ($LASTEXITCODE -ne 0) {
        Write-Error "El pre-flight fallo. Corrige lo reportado antes de levantar la pila."
        exit 1
    }
    Write-Host "==> Levantando infraestructura (docker compose up -d)..." -ForegroundColor DarkCyan
    Push-Location $DockerDir
    try { docker compose up -d | Out-Null }
    catch { Write-Warning "Docker no disponible o ya esta arriba: $($_.Exception.Message)" }
    finally { Pop-Location }
}

# 2) Detener cualquier instancia previa en el puerto (para compilar y arrancar limpio).
$existing = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
    Select-Object -First 1 -ExpandProperty OwningProcess
if ($existing) {
    Write-Host "==> Deteniendo instancia previa en el puerto $Port (PID $existing)..." -ForegroundColor DarkYellow
    Stop-Process -Id $existing -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}

# 3) Compilar.
if (-not $SkipBuild) {
    Write-Host "==> Compilando la solucion..." -ForegroundColor DarkCyan
    dotnet build $Solution -clp:ErrorsOnly
    if ($LASTEXITCODE -ne 0) {
        Write-Error "La compilacion fallo. No se levanta el servicio."
        exit 1
    }
}

# 4) Arrancar la consola en su propia ventana (muestra logs; se cierra con Ctrl+C o con stop-ecorex.ps1).
Write-Host "==> Iniciando la consola (Ecorex.SuperAdmin)..." -ForegroundColor DarkCyan
$runArgs = @('run', '--project', $AppProj, '--no-launch-profile', '--urls', $Url)
if (-not $SkipBuild) { $runArgs += '--no-build' }
$app = Start-Process -FilePath 'dotnet' -ArgumentList $runArgs -WorkingDirectory $Backend -PassThru

# Guardar el PID para que stop-ecorex.ps1 lo pueda detener aunque cambie el puerto.
$app.Id | Out-File -FilePath $PidFile -Encoding ascii

# 5) Esperar a que responda y abrir el navegador.
Write-Host "==> Esperando a que el servicio responda..." -ForegroundColor DarkCyan
$ready = $false
for ($i = 0; $i -lt 90; $i++) {
    Start-Sleep -Seconds 1
    try {
        $r = Invoke-WebRequest -Uri "$Url/login" -UseBasicParsing -TimeoutSec 3
        if ($r.StatusCode -ge 200) { $ready = $true; break }
    } catch { }
}

if ($ready) {
    Write-Host "==> ECOREX.tareas arriba en $Url  (PID $($app.Id))" -ForegroundColor Green
    Write-Host "    Para detenerlo:  .\stop-ecorex.ps1" -ForegroundColor DarkGray
    if (-not $NoBrowser) { Start-Process $Url }
} else {
    Write-Warning "El servicio no respondio a tiempo. Revisa la ventana de la app para ver el error."
}
