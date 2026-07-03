<#
  stop-ecorex.ps1
  Detiene ECOREX.tareas: para el servicio (consola SuperAdmin) y el tunel cloudflared.

  Uso:
    .\stop-ecorex.ps1                 # detiene el puerto 8080 (por defecto) + tunel
    .\stop-ecorex.ps1 -Port 5232      # si lo levantaste en otro puerto
    .\stop-ecorex.ps1 -KeepTunnel     # no detiene cloudflared
#>
param(
    [int]$Port = 8080,
    [switch]$KeepTunnel
)

$ErrorActionPreference = 'SilentlyContinue'
$Root    = $PSScriptRoot
$PidFile = Join-Path $Root '.ecorex-pid'
$stopped = $false

Write-Host "==> Deteniendo ECOREX.tareas..." -ForegroundColor Cyan

# 1) Por puerto: el proceso que escucha es la app.
$pids = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty OwningProcess -Unique
foreach ($processId in $pids) {
    Write-Host "   - servicio en puerto $Port (PID $processId)" -ForegroundColor DarkYellow
    Stop-Process -Id $processId -Force
    $stopped = $true
}

# 2) Por PID guardado (por si cambio el puerto o el listener ya cayo).
if (Test-Path $PidFile) {
    $savedPid = (Get-Content $PidFile | Select-Object -First 1)
    if ($savedPid -and (Get-Process -Id $savedPid -ErrorAction SilentlyContinue)) {
        Write-Host "   - proceso guardado (PID $savedPid)" -ForegroundColor DarkYellow
        Stop-Process -Id $savedPid -Force
        $stopped = $true
    }
    Remove-Item $PidFile -ErrorAction SilentlyContinue
}

# 3) Tunel cloudflared que pudo quedar huerfano.
if (-not $KeepTunnel) {
    Get-Process cloudflared -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "   - tunel cloudflared (PID $($_.Id))" -ForegroundColor DarkYellow
        Stop-Process -Id $_.Id -Force
        $stopped = $true
    }
}

if ($stopped) {
    Write-Host "==> ECOREX.tareas detenido." -ForegroundColor Green
} else {
    Write-Host "==> No habia nada corriendo en el puerto $Port." -ForegroundColor DarkGray
}
