# crear-llave-tunel.ps1 (PC2) - crea la llave SSH que usa el tunel a la BD de PROD de ECOREX.tareas.
# Para un PC NUEVO (PC2) que ya tiene la VPN y alcanza 10.0.0.3. Genera el par ed25519 en la MISMA
# ruta que espera abrir-tunel-prod.ps1 y deja la llave publica lista para instalarla en el servidor.
# Compatible con Windows PowerShell 5.1 y pwsh 7.
#
# IMPORTANTE
#  - La llave se crea SIN passphrase a proposito: el tunel corre con BatchMode=yes
#    (sin prompts). Una llave con passphrase haria fallar el tunel automatico.
#  - La llave privada NUNCA va al repo (este repo es PUBLICO). Se guarda en ~/.ssh.
#  - Cada PC tiene su PROPIO par de llaves; el Comment incluye usuario@equipo para
#    reconocerlo en authorized_keys del servidor. Instalar la publica de PC2 NO
#    invalida la de otros PCs (se agregan ambas).
#  - Si la llave YA existe, el script NO la pisa salvo que pases -Force: regenerarla
#    invalida el acceso actual de ESTE PC hasta reinstalar la publica en el servidor.
#
# USO (en PC2):
#   1) .\crear-llave-tunel.ps1                       # crea la llave y muestra la publica
#   2) Instala la publica en el servidor (ver paso 6 del script):
#        - Opcion A:  .\crear-llave-tunel.ps1 -InstalarEnServidor   (si PC2 ya tiene otro acceso SSH)
#        - Opcion B:  pega la publica en ~/.ssh/authorized_keys del server (consola del server u otro PC)
#   3) .\abrir-tunel-prod.ps1                         # abre el tunel

[CmdletBinding()]
param(
    # Ruta de la llave privada. Debe coincidir con la de abrir-tunel-prod.ps1.
    [string]$KeyPath = (Join-Path $HOME '.ssh\id_ed25519_visal'),

    # Servidor destino, solo para las instrucciones y la verificacion.
    [string]$Server = 'root@10.0.0.3',

    # Comentario que queda dentro de la llave publica (para identificarla en el server).
    [string]$Comment = "ecorex-tunel-$env:USERNAME@$env:COMPUTERNAME",

    # Regenerar aunque ya exista. OJO: invalida el acceso actual de este PC.
    [switch]$Force,

    # Intenta instalar la llave publica en el servidor reusando un acceso SSH que YA funcione.
    [switch]$InstalarEnServidor
)

function Pausa { Write-Host ""; Read-Host "Presiona ENTER para cerrar" | Out-Null }

$pubPath = "$KeyPath.pub"

Write-Host "1) Verificando ssh-keygen..." -ForegroundColor Cyan
$keygen = Get-Command ssh-keygen -ErrorAction SilentlyContinue
if (-not $keygen) {
    Write-Host "  ERROR: no se encontro ssh-keygen." -ForegroundColor Red
    Write-Host "  Instala el cliente OpenSSH: Configuracion > Aplicaciones > Caracteristicas opcionales" -ForegroundColor Yellow
    Pausa; exit 1
}
Write-Host "   OK: $($keygen.Source)" -ForegroundColor Green

Write-Host "2) Revisando si la llave ya existe: $KeyPath" -ForegroundColor Cyan
if (Test-Path $KeyPath) {
    Write-Host "   La llave YA EXISTE." -ForegroundColor Yellow
    & ssh-keygen -l -f $KeyPath 2>&1 | ForEach-Object { Write-Host "   $_" -ForegroundColor DarkGray }
    if (-not $Force) {
        Write-Host ""
        Write-Host "   No se toca nada. Si la regeneras PIERDES el acceso actual de este PC hasta" -ForegroundColor Yellow
        Write-Host "   instalar la nueva llave publica en $Server." -ForegroundColor Yellow
        Write-Host "   Para regenerarla de todas formas:  .\crear-llave-tunel.ps1 -Force" -ForegroundColor Yellow
        Pausa; exit 0
    }
    Write-Host "   -Force indicado: se respalda la actual y se regenera." -ForegroundColor Yellow
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    Move-Item $KeyPath "$KeyPath.bak-$stamp" -Force
    if (Test-Path $pubPath) { Move-Item $pubPath "$pubPath.bak-$stamp" -Force }
    Write-Host "   Respaldo: $KeyPath.bak-$stamp" -ForegroundColor DarkGray
}

Write-Host "3) Creando el par de llaves (ed25519, sin passphrase)..." -ForegroundColor Cyan
$sshDir = Split-Path -Parent $KeyPath
if (-not (Test-Path $sshDir)) { New-Item -ItemType Directory -Force -Path $sshDir | Out-Null }

# -N "" = sin passphrase (obligatorio para BatchMode); -q = silencioso
& ssh-keygen -t ed25519 -f $KeyPath -N '""' -C $Comment -q
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $KeyPath)) {
    Write-Host "  ERROR: ssh-keygen fallo (exit=$LASTEXITCODE)." -ForegroundColor Red
    Pausa; exit 1
}
Write-Host "   Creada: $KeyPath" -ForegroundColor Green

Write-Host "4) Ajustando permisos (OpenSSH rechaza llaves demasiado abiertas)..." -ForegroundColor Cyan
# Quita herencia y deja SOLO al usuario actual con acceso.
& icacls $KeyPath /inheritance:r | Out-Null
& icacls $KeyPath /grant:r "$($env:USERNAME):(R)" | Out-Null
Write-Host "   OK" -ForegroundColor Green

Write-Host "5) Llave PUBLICA (esta es la que va al servidor):" -ForegroundColor Cyan
$pub = (Get-Content $pubPath -Raw).Trim()
Write-Host ""
Write-Host $pub -ForegroundColor White
Write-Host ""

if ($InstalarEnServidor) {
    Write-Host "6) Instalando la publica en $Server (requiere un acceso SSH que YA funcione desde este PC)..." -ForegroundColor Cyan
    $cmd = "mkdir -p ~/.ssh && chmod 700 ~/.ssh && grep -qxF '$pub' ~/.ssh/authorized_keys 2>/dev/null || echo '$pub' >> ~/.ssh/authorized_keys; chmod 600 ~/.ssh/authorized_keys; echo INSTALADA"
    $out = & ssh -o ConnectTimeout=15 -o StrictHostKeyChecking=accept-new $Server $cmd 2>&1
    if ($LASTEXITCODE -eq 0 -and "$out" -match 'INSTALADA') {
        Write-Host "   OK: la llave quedo en authorized_keys." -ForegroundColor Green
    } else {
        Write-Host "   No se pudo instalar automaticamente (exit=$LASTEXITCODE)." -ForegroundColor Yellow
        Write-Host "   Salida: $out" -ForegroundColor DarkGray
        Write-Host "   Instalala a mano (ver paso 6 de abajo)." -ForegroundColor Yellow
    }
} else {
    Write-Host "6) Instalar la llave publica en el servidor (hazlo TU, no requiere pegar nada aqui):" -ForegroundColor Cyan
    Write-Host "   Opcion A - si este PC ya tiene otro acceso SSH al server:" -ForegroundColor Gray
    Write-Host "     .\crear-llave-tunel.ps1 -InstalarEnServidor" -ForegroundColor White
    Write-Host "   Opcion B - a mano, desde una consola del servidor (o desde otro PC con acceso):" -ForegroundColor Gray
    Write-Host "     mkdir -p ~/.ssh && chmod 700 ~/.ssh" -ForegroundColor White
    Write-Host "     echo '<pega aqui la llave publica de arriba>' >> ~/.ssh/authorized_keys" -ForegroundColor White
    Write-Host "     chmod 600 ~/.ssh/authorized_keys" -ForegroundColor White
}

Write-Host ""
Write-Host "7) Verificando el acceso con la llave nueva (requiere la VPN activa)..." -ForegroundColor Cyan
$test = & ssh -n -o BatchMode=yes -o ConnectTimeout=15 -o StrictHostKeyChecking=accept-new -i $KeyPath $Server "echo ACCESO_OK; hostname" 2>&1
if ($LASTEXITCODE -eq 0 -and "$test" -match 'ACCESO_OK') {
    Write-Host "   OK - la llave autentica sin passphrase." -ForegroundColor Green
    Write-Host "   Ya puedes abrir el tunel:  .\abrir-tunel-prod.ps1" -ForegroundColor Green
} else {
    Write-Host "   Todavia NO autentica (exit=$LASTEXITCODE)." -ForegroundColor Yellow
    Write-Host "   Salida: $test" -ForegroundColor DarkGray
    Write-Host "   Revisa: (a) la VPN esta conectada, (b) ya instalaste la llave publica en el servidor (paso 6)." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "RECORDATORIO: la llave privada ($KeyPath) NUNCA se commitea. Este repo es PUBLICO." -ForegroundColor Yellow
Pausa
