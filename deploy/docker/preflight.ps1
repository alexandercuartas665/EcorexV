<#
  preflight.ps1 - Validaciones previas al arranque de la pila Docker de ECOREX.tareas.
  Corre ANTES de `docker compose up -d`. Aborta con mensaje claro si algo falla.

  Verifica:
    1. Docker esta corriendo (docker info responde).
    2. Cada puerto de host del bloque dedicado esta libre (o lo ocupa nuestro propio contenedor).
    3. No hay contenedores ecorex-tareas-* muertos de corridas previas.
    4. Recursos minimos (>= 2 GB RAM asignada a Docker, >= 5 GB de disco libre).

  Uso:
    .\preflight.ps1            # usa puertos del .env local o los defaults
#>
$ErrorActionPreference = 'Stop'
$failures = @()

# --- Cargar puertos del .env si existe (defaults del bloque dedicado ECOREX) ---
$ports = @{
    POSTGRES_PORT      = 5442
    SQLSERVER_PORT     = 1443
    REDIS_PORT         = 6389
    RABBITMQ_PORT      = 5682
    RABBITMQ_MGMT_PORT = 15682
    ADMINER_PORT       = 8092
}
$envFile = Join-Path $PSScriptRoot '.env'
if (Test-Path $envFile) {
    Get-Content $envFile | Where-Object { $_ -match '^\s*([A-Z_]+)\s*=\s*(\d+)\s*$' } | ForEach-Object {
        $k = $Matches[1]; $v = [int]$Matches[2]
        if ($ports.ContainsKey($k)) { $ports[$k] = $v }
    }
}

# --- 1. Docker corriendo ---
Write-Host '[1/4] Docker...' -NoNewline
try {
    docker info 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'docker info fallo' }
    Write-Host ' OK' -ForegroundColor Green
} catch {
    Write-Host ' FALLO' -ForegroundColor Red
    $failures += 'Docker no responde. Inicia Docker Desktop y reintenta.'
}

# --- 2. Puertos libres (se permite que los ocupe nuestro propio contenedor) ---
Write-Host '[2/4] Puertos del bloque dedicado...'
$own = @(docker ps --filter 'name=ecorex-tareas-' --format '{{.Ports}}' 2>$null) -join ' '
foreach ($entry in $ports.GetEnumerator() | Sort-Object Value) {
    $p = $entry.Value
    $busy = Get-NetTCPConnection -LocalPort $p -State Listen -ErrorAction SilentlyContinue
    if ($busy -and ($own -notmatch ":$p->")) {
        $ownerPid = ($busy | Select-Object -First 1).OwningProcess
        $proc = (Get-Process -Id $ownerPid -ErrorAction SilentlyContinue).ProcessName
        Write-Host ("  {0,-20} {1,6}  OCUPADO (PID {2} {3})" -f $entry.Key, $p, $ownerPid, $proc) -ForegroundColor Red
        $failures += "Puerto $p ($($entry.Key)) ocupado por otro proceso. Libera el puerto o cambia el valor en .env."
    } else {
        Write-Host ("  {0,-20} {1,6}  libre" -f $entry.Key, $p) -ForegroundColor Green
    }
}

# --- 3. Sin contenedores ecorex-tareas-* muertos ---
Write-Host '[3/4] Contenedores previos...' -NoNewline
$dead = @(docker ps -a --filter 'name=ecorex-tareas-' --filter 'status=exited' --format '{{.Names}}' 2>$null)
if ($dead.Count -gt 0) {
    Write-Host " ATENCION" -ForegroundColor Yellow
    Write-Host ("  Contenedores detenidos de corridas previas: {0}" -f ($dead -join ', '))
    Write-Host '  Sugerencia: docker compose up -d los reutiliza; si estan corruptos, docker compose down primero.'
} else {
    Write-Host ' OK' -ForegroundColor Green
}

# --- 4. Recursos minimos ---
Write-Host '[4/4] Recursos...' -NoNewline
try {
    $memBytes = [long](docker info --format '{{.MemTotal}}' 2>$null)
    $disk = (Get-PSDrive -Name C).Free
    $memOk = $memBytes -ge 2GB
    $diskOk = $disk -ge 5GB
    if ($memOk -and $diskOk) {
        Write-Host (' OK (RAM docker {0:N1} GB, disco libre {1:N0} GB)' -f ($memBytes/1GB), ($disk/1GB)) -ForegroundColor Green
    } else {
        Write-Host ' FALLO' -ForegroundColor Red
        if (-not $memOk)  { $failures += "Docker tiene menos de 2 GB de RAM asignada ($([math]::Round($memBytes/1GB,1)) GB). SQL Server no arrancara." }
        if (-not $diskOk) { $failures += "Menos de 5 GB libres en disco C:." }
    }
} catch {
    Write-Host ' (no se pudo medir, continuo)' -ForegroundColor Yellow
}

# --- Resultado ---
if ($failures.Count -gt 0) {
    Write-Host "`nPRE-FLIGHT FALLO:" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
Write-Host "`nPRE-FLIGHT OK: listo para docker compose up -d" -ForegroundColor Green
exit 0
