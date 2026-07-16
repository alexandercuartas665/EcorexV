<#
.SYNOPSIS
    Levanta la web de ESTE worktree (agente colmena) aislada de las otras sesiones.

.DESCRIPTION
    Varias sesiones trabajan a la vez sobre worktrees distintos del mismo repo. Sin aislar, dos cosas
    se pisan: el PUERTO (si dos apps toman el de launchSettings) y sobre todo la BASE DE DATOS (los
    contenedores y clientes de prueba de uno le aparecen al otro).

    Este worktree usa lo suyo, y no lo comparte con nadie:
      - Puerto  : 5262            (dev de la otra sesion: 5253; la BD de prod NO se toca)
      - BD      : ecorex_agente   (en el Postgres de Docker del proyecto, 5442)

    La BD se crea sola la primera vez (la app migra y siembra los datos demo al arrancar en
    Development). Misma convencion que el worktree de formularios, que ya usa `ecorex_forms`.

.EXAMPLE
    .\start-agente-dev.ps1
    .\start-agente-dev.ps1 -Port 5262 -Database ecorex_agente
#>
[CmdletBinding()]
param(
    [int]$Port = 5262,
    [string]$Database = "ecorex_agente",
    [string]$PgContainer = "ecorex-tareas-postgres"
)

$ErrorActionPreference = "Stop"

# La clave del Postgres de dev sale del contenedor: nunca se versiona (regla 6 de CLAUDE.md).
$pw = (docker exec $PgContainer printenv POSTGRES_PASSWORD 2>&1 | Out-String).Trim()
if (-not $pw) { throw "No se pudo leer la clave de $PgContainer. Esta levantado el docker del proyecto?" }

# Idempotente: si la BD ya existe, psql se queja y seguimos.
$exists = (docker exec -e PGPASSWORD="$pw" $PgContainer psql -U ecorex -d postgres -tAc `
    "SELECT 1 FROM pg_database WHERE datname='$Database'" 2>&1 | Out-String).Trim()
if ($exists -ne "1") {
    Write-Host "Creando la BD '$Database' (la app la migra y siembra al arrancar)..." -ForegroundColor Cyan
    docker exec -e PGPASSWORD="$pw" $PgContainer psql -U ecorex -d postgres -c "CREATE DATABASE $Database OWNER ecorex;" | Out-Null
}

$busy = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
if ($busy) { throw "El puerto $Port ya esta ocupado (pid $($busy[0].OwningProcess)). Otra instancia de este worktree?" }

$env:ECOREX_DB_CONNECTION = "Host=localhost;Port=5442;Database=$Database;Username=ecorex;Password=$pw"
$env:ASPNETCORE_ENVIRONMENT = "Development"

Write-Host "Worktree del agente -> http://localhost:$Port  (BD: $Database)" -ForegroundColor Green
Write-Host "No toca a las otras sesiones ni a la BD de prod. Ctrl+C para parar."
Push-Location "$PSScriptRoot\apps\backend"
try {
    dotnet run --project src/Ecorex.SuperAdmin/Ecorex.SuperAdmin.csproj --no-launch-profile --urls "http://localhost:$Port"
}
finally { Pop-Location }
