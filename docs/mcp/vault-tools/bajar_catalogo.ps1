# Baja el catalogo VIVO del MCP de tableros (initialize + tools/list) a catalogo.local\ (ignorado por git).
# Requiere variables de entorno (NUNCA escribir el token en archivos del repo):
#   $env:ECOREX_MCP_URL   = "https://<tu-servidor>/api/mcp/tableros"
#   $env:ECOREX_MCP_TOKEN = "<token personal de /conectar-ia>"
$ErrorActionPreference = "Stop"
if (-not $env:ECOREX_MCP_URL -or -not $env:ECOREX_MCP_TOKEN) {
    throw "Define ECOREX_MCP_URL y ECOREX_MCP_TOKEN (ver README.md)."
}
$headers = @{ "Authorization" = "Bearer $($env:ECOREX_MCP_TOKEN)"; "Accept" = "application/json, text/event-stream"; "Content-Type" = "application/json" }
$out = Join-Path $PSScriptRoot "catalogo.local"
New-Item -ItemType Directory -Force $out | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Invoke-Rpc($method, $params) {
    $body = @{ jsonrpc = "2.0"; id = 1; method = $method; params = $params } | ConvertTo-Json -Depth 10 -Compress
    $resp = Invoke-WebRequest -Uri $env:ECOREX_MCP_URL -Method Post -Headers $headers -Body ([System.Text.Encoding]::UTF8.GetBytes($body)) -UseBasicParsing -TimeoutSec 120
    $c = [System.Text.Encoding]::UTF8.GetString($resp.RawContentStream.ToArray())
    if ($c -match "(?m)^data:") { $c = (($c -split "`n") | Where-Object { $_ -match "^data:" } | ForEach-Object { $_ -replace "^data:\s?", "" }) -join "" }
    return $c
}

$init = Invoke-Rpc "initialize" @{ protocolVersion = "2025-06-18"; capabilities = @{}; clientInfo = @{ name = "vault-tools"; version = "1" } }
[System.IO.File]::WriteAllText((Join-Path $out "initialize.json"), $init, $utf8)
$tl = Invoke-Rpc "tools/list" @{}
[System.IO.File]::WriteAllText((Join-Path $out "tools_list.json"), $tl, $utf8)
$tools = ($tl | ConvertFrom-Json).result.tools
Write-Output ("Herramientas en el catalogo: " + $tools.Count)
