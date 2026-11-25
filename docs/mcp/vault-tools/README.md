# vault-tools: mantener la guia de los MCP (guia_ecorex)

Scripts para mantener al dia `docs/vault-mcp-ecorex/`, la guia que las IA conectadas al MCP de tableros leen con
la herramienta `guia_ecorex` (ADR-0204, spec `docs/mcp/SPEC-guia-mcp-como-cerebro.md`).

Viven en `docs/mcp/vault-tools/` (la spec decia `tools/vault-mcp/`, pero `tools/` esta ignorado por git en este repo).

## Archivos

| Archivo | Que hace |
|---|---|
| `bajar_catalogo.ps1` | Baja `initialize` + `tools/list` del MCP de tableros a `catalogo.local/` |
| `gen_referencia.py` | Regenera las notas `Referencia/` (T01..T09, M01, W01, Indice A-Z) desde el catalogo vivo y las specs en codigo de Maquetas/WhatsApp |
| `validar_vault.py` | ASCII, enlaces `[[...]]`, datos privados y TODAS las llamadas de ejemplo contra los esquemas reales |
| `empaquetar_vault.py` | Verifica titulos unicos y la nota de entrada, y arma el ZIP para el Super Admin |

Archivos locales (terminan en `.local`, ignorados por git; NUNCA commitearlos):

- `catalogo.local/` - catalogo bajado.
- `patrones-privados.local` - una regex por linea con lo que NO puede aparecer en la guia (ids de empresa, nombres de
  clientes, dominios, correos). El repo es publico: estos patrones no se versionan.
- `salida.local/guia-ecorex-vault.zip` - el ZIP generado.

## Cuando se agregan o cambian herramientas de un MCP

```powershell
cd docs\mcp\vault-tools
$env:ECOREX_MCP_URL = "https://<servidor>/api/mcp/tableros"
$env:ECOREX_MCP_TOKEN = "<token personal de /conectar-ia>"   # solo en la sesion, nunca en archivos
.\bajar_catalogo.ps1
python gen_referencia.py      # si avisa "sin modulo: [...]", agrega la herramienta a MODULES
python validar_vault.py       # debe terminar con PROBLEMAS: 0 (las "MENCION DUDOSA" se revisan a mano)
python empaquetar_vault.py
```

1. Actualiza a mano las notas que cambian de fondo (recetas, trampas, conteos de la nota `00 - LEEME PRIMERO`).
2. Commit del vault.
3. Super Admin: `/cerebros-plantilla` -> "Guia de uso de ECOREX por IA" -> importar el ZIP -> **Publicar**. Los
   cerebros sembrados (bloqueados) se sincronizan y `guia_ecorex` sirve la version nueva.

## Cerebros por tema (ADR-0211)

Ademas de la guia general hay cerebros especializados que `guia_ecorex` sirve con `tema`:

| Tema | Vault | Plantilla en el Super Admin | Referencia |
|---|---|---|---|
| general | `docs/vault-mcp-ecorex/` | "Guia de uso de ECOREX por IA" (tema general) | `gen_referencia.py` (catalogo vivo) |
| flujos | `docs/vault-mcp-flujos/` | "Cerebro de flujos de ECOREX por IA" (tema flujos) | `gen_referencia_codigo.py --tema flujos` (lee el CODIGO) |
| formularios | `docs/vault-mcp-formularios/` | "Cerebro de formularios de ECOREX por IA" (tema formularios) | `gen_referencia_codigo.py --tema formularios` (lee el CODIGO) |

```powershell
python gen_referencia_codigo.py                              # regenera las referencias de todos los temas
python validar_vault.py --vault vault-mcp-flujos
python validar_vault.py --vault vault-mcp-formularios
python empaquetar_vault.py --vault vault-mcp-flujos          # -> salida.local/vault-mcp-flujos.zip
python empaquetar_vault.py --vault vault-mcp-formularios     # -> salida.local/vault-mcp-formularios.zip
```

En `/cerebros-plantilla`: plantilla nueva (categoria Plataforma, bloqueada, todas las empresas), marcar "Es una guia de
los MCP" con el **tema** correspondiente, importar su ZIP y Publicar. Una guia por tema: marcar otra del mismo tema
desmarca la anterior. Cada cerebro de tema tiene su propia nota `00 - LEEME PRIMERO`.

Reglas: la nota `00 - LEEME PRIMERO` no se renombra (`guia_ecorex` la usa como entrada); titulos unicos en todo el
vault (el importador ignora carpetas); solo ASCII; nada de tokens, ids de empresa ni datos de clientes.
