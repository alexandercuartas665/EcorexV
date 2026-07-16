<#
.SYNOPSIS
    Publica el Agente Conector On-Prem (servicio + colmena) listo para instalar.

.DESCRIPTION
    Ola 5d. Publica AUTOCONTENIDO (--self-contained) a proposito: el criterio de aceptacion es
    "instalable en una maquina Windows LIMPIA", y exigirle al cliente que instale antes el runtime
    de .NET 10 no es eso. A cambio pesa mas; es el precio correcto.

    Requisito que NO se puede autocontener: el Runtime de WebView2 (Edge), que necesita el
    sub-agente Navegador. Viene de serie en Windows 11 y en Windows 10 actualizado; si falta, el
    Navegador falla con motivo y el resto del agente (Gateway, Archivos) sigue trabajando.

.EXAMPLE
    .\publish.ps1
    .\publish.ps1 -OutDir D:\salida
#>
[CmdletBinding()]
param(
    [string]$OutDir
)

$ErrorActionPreference = "Stop"

# Ver nota en install.ps1: $PSScriptRoot no es fiable en el valor por defecto de un parametro
# cuando el script se invoca con -File.
if (-not $OutDir) { $OutDir = Join-Path $PSScriptRoot "out" }
$agentRoot = Resolve-Path "$PSScriptRoot\..\..\apps\agent"

Write-Host "Publicando el Agente ECOREX en: $OutDir" -ForegroundColor Cyan
if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }

$common = @("-c", "Release", "-r", "win-x64", "--self-contained", "true", "-p:PublishSingleFile=false")

Write-Host "  [1/2] Servicio (Ecorex.Agent.Service)..."
& dotnet publish "$agentRoot\Ecorex.Agent.Service\Ecorex.Agent.Service.csproj" @common -o "$OutDir\service" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Fallo la publicacion del servicio." }

Write-Host "  [2/2] Colmena (Ecorex.Agent.Gui)..."
& dotnet publish "$agentRoot\Ecorex.Agent.Gui\Ecorex.Agent.Gui.csproj" @common -o "$OutDir\gui" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Fallo la publicacion de la colmena." }

$size = [Math]::Round(((Get-ChildItem $OutDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Host "Listo. $size MB en $OutDir" -ForegroundColor Green
Write-Host "Siguiente: .\install.ps1 -ClientId <id> -HubUrl <url> -Secret <secreto>  (consola de ADMINISTRADOR)"
