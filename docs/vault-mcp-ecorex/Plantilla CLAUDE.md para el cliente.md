---
tipo: plantilla
uso: copiar el bloque en el CLAUDE.md (o AGENTS.md / GEMINI.md) del proyecto donde trabaja la IA del cliente
---

# Plantilla CLAUDE.md para el cliente

Volver a [[00 - LEEME PRIMERO]].

## Como instalar este vault en el equipo del cliente

1. Copia la carpeta completa del vault (`vault-mcp-ecorex/`) dentro de la carpeta de trabajo donde el cliente abre
   su IA (por ejemplo `C:\IA\ecorex\vault-mcp-ecorex\`). Puede abrirse tambien como vault en Obsidian.
2. Conecta el MCP siguiendo [[01 - Conectar los MCP]] (token desde **/conectar-ia**).
3. En esa misma carpeta crea (o edita) el archivo `CLAUDE.md` y pega el bloque de abajo. Para Gemini CLI usa
   `GEMINI.md`; para Codex u otros, `AGENTS.md`.
4. Prueba pidiendole a la IA: "lista mis tableros" y "que contenedores de datos tengo".

## Bloque para pegar

```markdown
# Operacion de ECOREX por MCP

Tienes conectado el servidor MCP de ECOREX (`ecorex-tableros`) con el token de un usuario de esta empresa.
Actuas COMO ese usuario: mismos permisos, todo queda auditado "(via IA)".

ANTES de construir o cambiar algo en ECOREX:
1. Lee `vault-mcp-ecorex/00 - LEEME PRIMERO.md` (una vez por sesion) y sigue su protocolo:
   entender -> descubrir con herramientas de lectura -> planear -> confirmar lo destructivo -> ejecutar -> verificar -> reportar.
2. Busca la receta que corresponde en `vault-mcp-ecorex/Recetas/` y siguela paso a paso.
3. Antes de llamar una herramienta que no conoces, lee su ficha en `vault-mcp-ecorex/Referencia/`
   (o el Indice A-Z). No adivines nombres de parametros.
4. Revisa `vault-mcp-ecorex/04 - Trampas conocidas.md` para el modulo que vas a tocar.

Reglas que no se negocian:
- Confirma con el usuario antes de: borrar, inactivar, cerrar pasos de flujo, publicar con avisos, crear enlaces
  publicos, limpiar/sobrescribir columnas, o procesar con IA volumenes grandes (consume cupo).
- Nunca pidas, muestres ni guardes tokens, claves o contrasenas. Los secretos de conectores se ponen en la pantalla
  de ECOREX, no por MCP.
- Busca antes de crear (contactos, formularios, contenedores, agentes) para no duplicar.
- Prueba con poco (1 dia de datos, 5 filas, un borrador) antes de escalar.
- Si una operacion es asincrona (clasificacion encolada, importacion programada) dilo y explica como verificarla.
- Si ninguna herramienta permite lo que piden, dilo y explica como hacerlo en la pantalla de ECOREX.
- El servidor manda: si una herramienta no coincide con el vault, usa lo que devuelve `tools/list`.
```

## Variante corta (si el cliente no puede copiar el vault)

Si la IA del cliente no tiene acceso a archivos, pega al inicio de la conversacion el contenido de
[[00 - LEEME PRIMERO]], [[03 - Reglas de oro]] y la receta que necesite. Para formularios pidele que llame primero
`describe_components`; para reportes, `listar_fuentes_reporte` y `ver_fuente_reporte`.
