---
tipo: guia
tema: conexion
---

# Conectar los MCP de ECOREX

Volver a [[00 - LEEME PRIMERO]].

## 1. Conceptos

- **Transporte:** "Streamable HTTP" (MCP 2025-06-18). Cada llamada es un `POST` con UN mensaje JSON-RPC 2.0;
  la respuesta es JSON directo (sin stream). Un `GET` responde 405 (es normal). El servidor no guarda sesion.
- **Autenticacion:** cabecera `Authorization: Bearer <TOKEN>`. El token es **personal**: resuelve la empresa
  (tenant) **y el usuario**. Toda herramienta corre COMO ese usuario: ve y cambia solo lo que el puede, y cada
  escritura queda en la bitacora de la tarea ("Nombre (via IA)") y en la auditoria.
- **Un token por servidor:** el de tableros NO abre WhatsApp ni maquetas (cada uno tiene su alcance).

## 2. Obtener el token

| Servidor | Donde se genera (en la aplicacion ECOREX) | Alcance |
|---|---|---|
| Tableros | Pantalla **/conectar-ia** (Conectar IA). Muestra la URL exacta y los snippets listos para copiar. | `mcp:tableros` |
| WhatsApp | Pantalla **/lineas**: configuracion MCP de cada linea (una URL por linea). | `mcp:whatsapp` |
| Maquetas | Seccion de maquetas (`/maqueta`), token de integracion de maquetas. El mismo token sirve para publicar versiones. | `integ:maqueta` |

El token se muestra **una sola vez**. Guardalo como secreto. Si se filtra, revocalo en la misma pantalla y genera
otro. Si el usuario del token se inactiva, el token deja de funcionar (401).

## 3. Configurar el cliente de IA

Reemplaza `<SERVIDOR>` por el dominio con el que entras a ECOREX (la pantalla /conectar-ia lo muestra) y
`<TOKEN>` por tu token. El nombre (`ecorex-tableros`) es libre.

**Claude Code (terminal):**
```bash
claude mcp add --transport http ecorex-tableros https://<SERVIDOR>/api/mcp/tableros --header "Authorization: Bearer <TOKEN>"
```
Verifica con `/mcp` dentro de Claude Code: debe aparecer `ecorex-tableros` conectado con sus herramientas.

**Claude Desktop** (`claude_desktop_config.json`, requiere Node.js):
```json
{
  "mcpServers": {
    "ecorex-tableros": {
      "command": "npx",
      "args": ["-y", "mcp-remote", "https://<SERVIDOR>/api/mcp/tableros", "--header", "Authorization:Bearer <TOKEN>"]
    }
  }
}
```

**Gemini CLI** (`~/.gemini/settings.json`):
```json
{
  "mcpServers": {
    "ecorex-tableros": {
      "httpUrl": "https://<SERVIDOR>/api/mcp/tableros",
      "headers": { "Authorization": "Bearer <TOKEN>" }
    }
  }
}
```

**Codex** (`~/.codex/config.toml`, requiere Node.js):
```toml
[mcp_servers.ecorex-tableros]
command = "npx"
args = ["-y", "mcp-remote", "https://<SERVIDOR>/api/mcp/tableros", "--header", "Authorization:Bearer <TOKEN>"]
```

**WhatsApp:** igual, con la URL de la linea (`https://<SERVIDOR>/api/mcp/whatsapp/<lineId>`) y su token; ponle un
nombre por linea (`whatsapp-ventas`, `whatsapp-soporte`). **Maquetas:** `https://<SERVIDOR>/api/mcp/maquetas`.

## 4. Probar la conexion "a mano" (diagnostico o scripts)

Pedir el catalogo de herramientas:
```bash
curl -s https://<SERVIDOR>/api/mcp/tableros \
  -H "Authorization: Bearer <TOKEN>" -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}'
```

Llamar una herramienta:
```bash
curl -s https://<SERVIDOR>/api/mcp/tableros \
  -H "Authorization: Bearer <TOKEN>" -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"listar_tableros","arguments":{}}}'
```
La respuesta util viene en `result.content[0].text` (un JSON en texto). Si la respuesta llega con lineas `data:`,
quita ese prefijo antes de parsear.

### Ayudante PowerShell para lotes largos (ej. clasificar miles de filas)

Cuando una tarea requiere repetir una llamada muchas veces (bucles de `clasificar_lote`, verificaciones), un
script es mas confiable que pedirle a la IA cientos de llamadas. Toma el token de una variable de entorno:
**nunca lo escribas en el archivo**.

```powershell
$url = "https://<SERVIDOR>/api/mcp/tableros"
$headers = @{ "Authorization" = "Bearer $env:ECOREX_TOKEN"; "Accept" = "application/json, text/event-stream"; "Content-Type" = "application/json" }
function Invoke-Mcp($tool, $arguments) {
  $body = @{ jsonrpc = "2.0"; id = 1; method = "tools/call"; params = @{ name = $tool; arguments = $arguments } } | ConvertTo-Json -Depth 30 -Compress
  $resp = Invoke-WebRequest -Uri $url -Method Post -Headers $headers -Body ([Text.Encoding]::UTF8.GetBytes($body)) -UseBasicParsing -TimeoutSec 180
  $c = $resp.Content
  if ($c -match "(?m)^data:") { $c = (($c -split "`n") | Where-Object { $_ -match "^data:" } | ForEach-Object { $_ -replace "^data:\s?", "" }) -join "" }
  return ($c | ConvertFrom-Json).result.content[0].text
}
# Ejemplo:
Invoke-Mcp "listar_tableros" @{}
```
Siempre envia el cuerpo en **UTF-8** (las tildes de los datos se corrompen si no). Para bucles, maneja errores y
reintentos (ver [[R10 - Clasificar datos con IA]]).

## 5. Errores de conexion

| Sintoma | Causa | Que hacer |
|---|---|---|
| HTTP 401 | Token invalido, revocado, de otro alcance, o usuario inactivo | Genera uno nuevo en la pantalla correspondiente |
| HTTP 405 | Hiciste GET | Usa POST |
| 404 en WhatsApp | La linea no existe en tu empresa (o es de otra) | Revisa el `lineId` en /lineas |
| 400 en WhatsApp | La linea no es de Evolution API | Este MCP solo opera lineas Evolution |
| `Herramienta desconocida: X` | Esa herramienta no existe en esta version | Mira `tools/list`; usa la que exista |
| La IA "no ve" herramientas | El cliente no recargo la config | Reinicia el cliente / revisa `/mcp` |

Siguiente: [[02 - Modelo mental del sistema]].
