<#
.SYNOPSIS
  Construye el instalador MSI SELF-CONTAINED del Agente Conector On-Prem "Colmena".

.DESCRIPTION
  Idempotente. En una sola pasada:
    1. Publica el Servicio (Ecorex.Agent.Service) self-contained win-x64 a una carpeta staging.
    2. Publica la GUI (Ecorex.Agent.Gui) self-contained win-x64 a la MISMA carpeta staging
       (comparten runtime net10.0-windows; los .dll identicos se sobreescriben sin duplicar).
    3. Limpia simbolos y docs (.pdb / .xml) del staging.
    4. Separa los dos exe a dist\exe (para que el instalador los declare como componentes propios
       y la cosecha <Files> del resto no los duplique).
    5. Compila el proyecto WiX y produce el .msi.
    6. Copia el .msi final a installer\dist.

  La maquina cliente NO necesita ningun runtime .NET: viaja dentro del paquete.
  El MSI NO se firma (no hay certificado); la firma de codigo es un paso posterior (ver README).

.PARAMETER Version
  Version del producto (por defecto 1.0.0). Se usa en el MSI y en el nombre del archivo.

.PARAMETER Configuration
  Configuracion de compilacion (por defecto Release).

.EXAMPLE
  .\build-installer.ps1
  .\build-installer.ps1 -Version 1.1.0
#>
[CmdletBinding()]
param(
    [string]$Version = "1.0.0",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$InstallerDir = $PSScriptRoot
$AgentDir     = Split-Path -Parent $InstallerDir
$DistDir      = Join-Path $InstallerDir "dist"
$StageDir     = Join-Path $DistDir "publish"
$ExeDir       = Join-Path $DistDir "exe"

$ServiceProj  = Join-Path $AgentDir "Ecorex.Agent.Service\Ecorex.Agent.Service.csproj"
$GuiProj      = Join-Path $AgentDir "Ecorex.Agent.Gui\Ecorex.Agent.Gui.csproj"
$WixProj      = Join-Path $InstallerDir "Ecorex.Agent.Installer.wixproj"

$Profile      = "win-x64-selfcontained"

function Write-Step($msg) { Write-Host "`n==== $msg ====" -ForegroundColor Cyan }

Write-Step "Limpiando staging"
foreach ($d in @($StageDir, $ExeDir)) {
    if (Test-Path $d) { Remove-Item -Recurse -Force $d }
    New-Item -ItemType Directory -Force -Path $d | Out-Null
}

Write-Step "Publicando Servicio (self-contained win-x64)"
dotnet publish $ServiceProj -p:PublishProfile=$Profile -p:Version=$Version -o $StageDir
if ($LASTEXITCODE -ne 0) { throw "Fallo la publicacion del Servicio." }

Write-Step "Publicando GUI (self-contained win-x64) en el mismo staging"
dotnet publish $GuiProj -p:PublishProfile=$Profile -p:Version=$Version -o $StageDir
if ($LASTEXITCODE -ne 0) { throw "Fallo la publicacion de la GUI." }

Write-Step "Limpiando simbolos y docs (.pdb / .xml)"
Get-ChildItem -Path $StageDir -Recurse -Include *.pdb, *.xml | Remove-Item -Force

Write-Step "Separando los dos exe a dist\exe"
foreach ($exe in @("Ecorex.Agent.Service.exe", "Ecorex.Agent.Gui.exe")) {
    $src = Join-Path $StageDir $exe
    if (-not (Test-Path $src)) { throw "No se encontro $exe en el staging (publicacion incompleta)." }
    Move-Item -Force $src (Join-Path $ExeDir $exe)
}

Write-Step "Compilando el instalador WiX (MSI)"
dotnet build $WixProj -c $Configuration `
    -p:ProductVersion=$Version `
    -p:AgentPublishDir=$StageDir `
    -p:AgentExeDir=$ExeDir
if ($LASTEXITCODE -ne 0) { throw "Fallo la compilacion del MSI." }

$MsiName  = "Ecorex-AgenteColmena-$Version.msi"
$MsiBuilt = Join-Path $InstallerDir "bin\$Configuration\$MsiName"
if (-not (Test-Path $MsiBuilt)) { throw "No se genero el MSI esperado: $MsiBuilt" }

$MsiFinal = Join-Path $DistDir $MsiName
Copy-Item -Force $MsiBuilt $MsiFinal

$sizeMb = [math]::Round((Get-Item $MsiFinal).Length / 1MB, 1)
Write-Step "LISTO"
Write-Host "MSI: $MsiFinal" -ForegroundColor Green
Write-Host "Tamano: $sizeMb MB" -ForegroundColor Green
Write-Host "Sin firmar (la firma de codigo es un paso posterior)." -ForegroundColor Yellow
