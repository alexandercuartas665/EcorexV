<#
.SYNOPSIS
  Deploy RAPIDO de ECOREX a produccion por imagen GHCR. VIA POR DEFECTO.

.DESCRIPTION
  El CI (.github/workflows/publish-image.yml) construye la imagen de la consola en GitHub
  (runners con RAM de sobra) al hacer push al tronco fase-0/clon-backbone, y la publica en
  ghcr.io/alexandercuartas665/ecorex/superadmin. Este script solo hace que PROD baje esa
  imagen y la levante (pull + up via /opt/ecorex/actualizar-en-linux.sh), en ~1-2 min, SIN
  compilar en el host (evita el OOM del build-from-git).

  Flujo tipico:
    1) bump AppVersion + commit + push al tronco y espejo main (dispara el CI).
    2) .\deploy\deploy-prod-fast.ps1 -WaitCI -Version 0.16.239
       (-WaitCI espera a que el CI termine de publicar la imagen del commit actual).

  FALLBACK: .\deploy\deploy-prod.ps1 (build-from-git, compila en 10.0.0.3). Usar SOLO si el
  CI o GHCR fallan. Requiere swap temporal en el host (ver docs / memoria deploy-host-oom-swap).

  ROLLBACK: .\deploy\deploy-prod-fast.ps1 -Tag <sha-anterior>
  (actualizar-en-linux.sh imprime la imagen anterior en cada corrida).
#>
param(
    [string]$VpsHost   = "root@10.0.0.3",
    [string]$KeyPath   = "$env:USERPROFILE\.ssh\id_ed25519_visal",
    [string]$RemoteDir = "/opt/ecorex",
    [string]$Tag       = "latest",   # o un <sha> de GHCR para fijar / hacer rollback
    [string]$Version   = "",         # version esperada a verificar (ej. 0.16.239); vacio = no compara
    [string]$Repo      = "alexandercuartas665/EcorexV",
    [switch]$WaitCI                   # esperar al run de publish-image.yml del commit actual
)
$ErrorActionPreference = "Stop"
function Section($m) { Write-Host "`n==== $m ====" }

# 0) (opcional) esperar a que el CI publique la imagen del HEAD actual
if ($WaitCI) {
    Section "Esperando al CI (publish-image.yml) del commit actual"
    $sha = (git rev-parse HEAD).Trim()
    Write-Host "HEAD = $sha"
    $rid = ""
    foreach ($try in 1..10) {
        $rid = (gh run list --repo $Repo --workflow publish-image.yml --commit $sha -L 1 --json databaseId --jq ".[0].databaseId" 2>$null)
        if ($rid) { break }
        Write-Host "  aun no aparece el run para este commit; reintento ($try/10)..."
        Start-Sleep -Seconds 6
    }
    if ($rid) {
        gh run watch $rid --repo $Repo --exit-status
    } else {
        Write-Warning "No encontre run de publish-image.yml para $sha. Verifica que el push al tronco disparo el CI, o corre sin -WaitCI si la imagen ya esta publicada."
    }
}

# 1) conexion
Section "Comprobando conexion con $VpsHost"
ssh -i $KeyPath -o BatchMode=yes -o StrictHostKeyChecking=accept-new $VpsHost "echo Conexion OK"
if ($LASTEXITCODE -ne 0) { throw "No hay conexion SSH con $VpsHost (VPN/tunel arriba?)." }

# 2) pull + up (el script del host hace backup + pull + up + salud)
Section "Desplegando por imagen GHCR (tag=$Tag)"
ssh -i $KeyPath -o BatchMode=yes $VpsHost "cd $RemoteDir && ECOREX_IMAGE_TAG=$Tag ./actualizar-en-linux.sh"
if ($LASTEXITCODE -ne 0) { throw "El deploy por imagen fallo. La version en curso sigue arriba (el script respalda y solo cambia al final)." }

# 3) verificacion de version (re-poll: el curl del script del host sale muy pronto y da HTTP 000)
Section "Verificando version"
$verify = @'
for i in 1 2 3 4 5 6; do
  code=$(curl -s -o /tmp/ecorex_login -w "%{http_code}" http://127.0.0.1:5480/login)
  ver=$(grep -oE "v0\.[0-9]+\.[0-9]+" /tmp/ecorex_login | head -1)
  if [ -n "$ver" ]; then echo "interno 5480: HTTP $code version=$ver"; break; fi
  sleep 5
done
docker ps --filter name=ecorex-app --format "contenedor: {{.Status}}"
'@
ssh -i $KeyPath -o BatchMode=yes $VpsHost $verify

if ($Version -ne "") {
    Write-Host "`nVersion esperada: v$Version"
    Write-Host "Confirma que la linea 'interno 5480' de arriba diga v$Version, y revisa https://app2.bitcode.com.co/login"
}
Section "LISTO (via rapida por imagen GHCR)"
