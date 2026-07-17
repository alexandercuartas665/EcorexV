<#
.SYNOPSIS
    Instala el Agente Conector On-Prem: Servicio Windows (LocalSystem) + colmena WPF con
    auto-arranque de sesion. Ola 5d / ADR-0039.

.DESCRIPTION
    Reparto de responsabilidades (ADR-0039):
      - El SERVICIO es el dueno de la identidad, del canal y de la boveda. Corre 24/7 aunque nadie
        inicie sesion. Atiende Gateway y Archivos.
      - La COLMENA es su cara: arranca al iniciar sesion, muestra el estado real y le presta el
        escritorio al Navegador (WebView2 no vive en la sesion 0 de un servicio).

    Este script CREA LA BOVEDA, y no por comodidad: quien crea el directorio es su propietario, y un
    propietario siempre puede reescribir el ACL. Si se dejara que la creara el primer proceso que
    arranque, un usuario sin privilegios podria quedar de dueno y re-otorgarse acceso al secreto del
    tenant que el servicio guarde despues. Aqui se crea con owner = Administradores y ACL cerrada
    (SYSTEM + Administradores), que es la unica puerta real: con DPAPI de maquina, quien pueda LEER
    el archivo puede descifrarlo.

.PARAMETER ClientId
    Identidad del agente ante el servidor (DataClient del tenant). Opcional: se puede configurar
    despues desde la colmena (con permisos de administrador).

.PARAMETER HubUrl
    URL del servidor. Se acepta la URL BASE; el agente le agrega /hubs/agente si falta.

.PARAMETER Secret
    Secreto del DataClient (handshake HMAC). NUNCA queda en disco en claro: se cifra en la boveda.

.EXAMPLE
    .\install.ps1 -ClientId cli_acme -HubUrl https://ecorex.midominio.com -Secret "<secreto>"

.EXAMPLE
    .\install.ps1    # instala sin identidad; se configura luego desde la colmena
#>
[CmdletBinding()]
param(
    [string]$ClientId,
    [string]$HubUrl,
    [string]$Secret,
    [string]$SourceDir,
    [string]$InstallDir = "$env:ProgramFiles\ECOREX\Agente"
)

$ErrorActionPreference = "Stop"

# $PSScriptRoot NO es fiable como valor por defecto de un parametro cuando el script se invoca con
# -File (llega vacio, y la ruta queda en '\out'). Se resuelve aqui, en el cuerpo, donde si esta.
if (-not $SourceDir) { $SourceDir = Join-Path $PSScriptRoot "out" }

$ServiceName = "EcorexAgent"
$EventSource = "ECOREX Agente"   # debe coincidir con Program.cs del servicio
$VaultDir    = "$env:ProgramData\Ecorex\Agent"
$RunKey      = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
$RunValue    = "EcorexColmena"

# ---- 0. Requisitos ----

$id = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Este instalador exige una consola de ADMINISTRADOR (crea un servicio y una boveda cerrada)."
}
if (-not (Test-Path "$SourceDir\service\Ecorex.Agent.Service.exe")) {
    throw "No hay binarios publicados en '$SourceDir'. Ejecute primero .\publish.ps1"
}

Write-Host "Instalando el Agente ECOREX" -ForegroundColor Cyan

# ---- 1. Servicio previo: detener y quitar (reinstalacion limpia) ----

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "  [1/6] Deteniendo el servicio existente..."
    if ($existing.Status -ne "Stopped") { Stop-Service -Name $ServiceName -Force }
    & sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2   # el SCM tarda en soltar el nombre
} else {
    Write-Host "  [1/6] No habia servicio previo."
}

# ---- 2. Binarios ----

Write-Host "  [2/6] Copiando binarios a $InstallDir..."
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item "$SourceDir\service" -Destination $InstallDir -Recurse -Force
Copy-Item "$SourceDir\gui"     -Destination $InstallDir -Recurse -Force

# ---- 3. Boveda (ver nota de la cabecera: por esto la crea el instalador) ----

Write-Host "  [3/6] Creando la boveda con ACL cerrada ($VaultDir)..."
New-Item -ItemType Directory -Force -Path $VaultDir | Out-Null

$admins = New-Object Security.Principal.SecurityIdentifier([Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid, $null)
$system = New-Object Security.Principal.SecurityIdentifier([Security.Principal.WellKnownSidType]::LocalSystemSid, $null)

$acl = Get-Acl $VaultDir
$acl.SetAccessRuleProtection($true, $false)          # rompe la herencia de ProgramData
$acl.Access | ForEach-Object { $null = $acl.RemoveAccessRule($_) }
foreach ($sid in @($system, $admins)) {
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule(
        $sid, "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")))
}
$acl.SetOwner($admins)                                # que el dueno no sea quien la creo por accidente
Set-Acl -Path $VaultDir -AclObject $acl

# ---- 4. Identidad (opcional) ----

if ($ClientId -and $HubUrl) {
    Write-Host "  [4/6] Guardando la identidad en la boveda (cifrada)..."
    $args = @("--save-config", $ClientId, $HubUrl)
    if ($Secret) { $args += $Secret }
    & "$InstallDir\service\Ecorex.Agent.Service.exe" @args
    if ($LASTEXITCODE -ne 0) { throw "No se pudo guardar la identidad en la boveda." }
} else {
    Write-Host "  [4/6] Sin identidad: se configura despues desde la colmena (requiere administrador)."
}

# ---- 5. Servicio Windows ----

# El origen del Visor de eventos hay que CREARLO: si no existe, el proveedor de EventLog de .NET no
# escribe nada y tampoco avisa, y el servicio se queda mudo justo donde el README manda a mirar
# (verificado el 2026-07-16). Exige privilegio; por eso vive aqui y no en el arranque del servicio.
if (-not [System.Diagnostics.EventLog]::SourceExists($EventSource)) {
    Write-Host "  [5/6] Registrando el origen '$EventSource' en el Visor de eventos..."
    New-EventLog -LogName Application -Source $EventSource
}

Write-Host "  [5/6] Registrando el servicio '$ServiceName' (LocalSystem, arranque automatico)..."
$bin = "`"$InstallDir\service\Ecorex.Agent.Service.exe`""
& sc.exe create $ServiceName binPath= $bin obj= "LocalSystem" start= auto DisplayName= "ECOREX - Agente Conector" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "sc.exe create fallo (codigo $LASTEXITCODE)." }
& sc.exe description $ServiceName "Mantiene el canal seguro con ECOREX y atiende consultas a fuentes locales (Gateway y Archivos)." | Out-Null
# Que un fallo no deje al cliente sin agente hasta el proximo reinicio: reintentos escalonados.
& sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/30000/restart/60000 | Out-Null
Start-Service -Name $ServiceName

# ---- 6. Colmena al iniciar sesion ----

Write-Host "  [6/6] Auto-arranque de la colmena al iniciar sesion..."
Set-ItemProperty -Path $RunKey -Name $RunValue -Value "`"$InstallDir\gui\Ecorex.Agent.Gui.exe`""

Write-Host ""
Write-Host "Instalado." -ForegroundColor Green
Write-Host "  Servicio : $ServiceName ($((Get-Service $ServiceName).Status)) - LocalSystem, arranque automatico"
Write-Host "  Boveda   : $VaultDir (solo SYSTEM y Administradores)"
Write-Host "  Colmena  : $InstallDir\gui\Ecorex.Agent.Gui.exe (arranca al iniciar sesion)"
Write-Host "  Bitacora : Visor de eventos -> Registros de Windows -> Aplicacion -> origen 'ECOREX Agente'"
Write-Host ""
Write-Host "Para desinstalar: .\uninstall.ps1"
