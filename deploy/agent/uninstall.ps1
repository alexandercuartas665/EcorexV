<#
.SYNOPSIS
    Desinstala el Agente Conector On-Prem. Ola 5d.

.DESCRIPTION
    Por defecto CONSERVA la boveda (identidad, allow-lists, consentimiento y la credencial de la
    fuente local): reinstalar no deberia costarle al cliente volver a configurar todo, y borrar
    secretos es una decision que se pide explicitamente, no un efecto colateral de desinstalar.
    Use -RemoveVault para borrarla.

.EXAMPLE
    .\uninstall.ps1
    .\uninstall.ps1 -RemoveVault
#>
[CmdletBinding()]
param(
    [switch]$RemoveVault,
    [string]$InstallDir = "$env:ProgramFiles\ECOREX\Agente"
)

$ErrorActionPreference = "Stop"

$ServiceName = "EcorexAgent"
$EventSource = "ECOREX Agente"
$VaultDir    = "$env:ProgramData\Ecorex\Agent"
$RunKey      = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
$RunValue    = "EcorexColmena"

$id = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Exige una consola de ADMINISTRADOR."
}

Write-Host "Desinstalando el Agente ECOREX" -ForegroundColor Cyan

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc) {
    Write-Host "  Deteniendo y quitando el servicio..."
    if ($svc.Status -ne "Stopped") { Stop-Service -Name $ServiceName -Force }
    & sc.exe delete $ServiceName | Out-Null
} else {
    Write-Host "  El servicio no estaba registrado."
}

Write-Host "  Cerrando la colmena si esta abierta..."
Get-Process -Name "Ecorex.Agent.Gui" -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Host "  Quitando el auto-arranque..."
Remove-ItemProperty -Path $RunKey -Name $RunValue -ErrorAction SilentlyContinue

# El origen se quita, pero los eventos YA escritos se conservan: son el historial de lo que hizo el
# agente y pueden hacer falta justo despues de desinstalar, que es cuando se investiga un problema.
try {
    if ([System.Diagnostics.EventLog]::SourceExists($EventSource)) {
        Write-Host "  Quitando el origen del Visor de eventos (los eventos ya escritos se conservan)..."
        Remove-EventLog -Source $EventSource
    }
} catch {
    Write-Host "  Aviso: no se pudo quitar el origen del Visor de eventos ($($_.Exception.Message))."
}

if (Test-Path $InstallDir) {
    Write-Host "  Borrando binarios ($InstallDir)..."
    Start-Sleep -Seconds 2   # dar tiempo a que suelten los archivos
    Remove-Item $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
}

# No dejar la carpeta padre huerfana: desinstalar debe no dejar rastro. Solo si quedo VACIA, por si
# el sitio lo comparte con otro producto ECOREX.
$parent = Split-Path $InstallDir -Parent
if ((Test-Path $parent) -and -not (Get-ChildItem $parent -Force -ErrorAction SilentlyContinue)) {
    Remove-Item $parent -Force -ErrorAction SilentlyContinue
}

if ($RemoveVault) {
    Write-Host "  Borrando la boveda ($VaultDir): identidad, secretos y allow-lists." -ForegroundColor Yellow
    Remove-Item $VaultDir -Recurse -Force -ErrorAction SilentlyContinue
} else {
    Write-Host "  Boveda CONSERVADA en $VaultDir (use -RemoveVault para borrarla)."
}

Write-Host "Desinstalado." -ForegroundColor Green
