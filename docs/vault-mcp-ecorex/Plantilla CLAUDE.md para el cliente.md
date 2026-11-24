---
tipo: plantilla
uso: OPCIONAL - bloque para el CLAUDE.md (o AGENTS.md / GEMINI.md) del proyecto donde trabaja la IA del cliente
---

# Plantilla CLAUDE.md para el cliente

Volver a [[00 - LEEME PRIMERO]].

## Ya no hace falta copiar archivos

Esta guia vive DENTRO de ECOREX: cualquier IA conectada al MCP de tableros la lee con la herramienta
`guia_ecorex` (y las instrucciones del servidor ya le piden llamarla antes de construir). Lo unico obligatorio es
conectar el MCP siguiendo [[01 - Conectar los MCP]] (token desde **/conectar-ia**).

Esta plantilla es OPCIONAL: sirve para reforzar el habito en la IA del cliente (algunas IA leen las instrucciones
del servidor con menos atencion que su propio CLAUDE.md).

## Como usarla

1. En la carpeta donde el cliente abre su IA, crea (o edita) `CLAUDE.md` y pega el bloque de abajo. Para Gemini CLI
   usa `GEMINI.md`; para Codex u otros, `AGENTS.md`.
2. Prueba pidiendole a la IA: "lista mis tableros" y "que contenedores de datos tengo".

## Bloque para pegar

```markdown
# Operacion de ECOREX por MCP

Tienes conectado el servidor MCP de ECOREX (`ecorex-tableros`) con el token de un usuario de esta empresa.
Actuas COMO ese usuario: mismos permisos, todo queda auditado "(via IA)".

ANTES de construir o cambiar algo en ECOREX:
1. Llama `guia_ecorex` sin argumentos (una vez por sesion) y sigue su protocolo:
   entender -> descubrir con herramientas de lectura -> planear -> confirmar lo destructivo -> ejecutar -> verificar -> reportar.
2. Abre la receta que corresponde con `guia_ecorex(nota="<titulo de la receta>")` y siguela paso a paso.
3. Antes de llamar una herramienta que no conoces, lee su ficha (`guia_ecorex(nota="Indice de herramientas (A-Z)")`
   te dice en que nota esta). No adivines nombres de parametros.
4. Ante cualquier error, `guia_ecorex(buscar="<texto del error>")` antes de reintentar.

Reglas que no se negocian:
- Confirma con el usuario antes de: borrar, inactivar, cerrar pasos de flujo, publicar con avisos, crear enlaces
  publicos, limpiar/sobrescribir columnas, o procesar con IA volumenes grandes (consume cupo).
- Nunca pidas, muestres ni guardes tokens, claves o contrasenas. Los secretos de conectores se ponen en la pantalla
  de ECOREX, no por MCP.
- Busca antes de crear (contactos, formularios, contenedores, agentes) para no duplicar.
- Prueba con poco (1 dia de datos, 5 filas, un borrador) antes de escalar.
- Si una operacion es asincrona (clasificacion encolada, importacion programada) dilo y explica como verificarla.
- Si ninguna herramienta permite lo que piden, dilo y explica como hacerlo en la pantalla de ECOREX.
- El servidor manda: si una herramienta no coincide con la guia, usa lo que devuelve `tools/list`.
```

## Si la IA no tiene el MCP de tableros

Una IA conectada solo al MCP de maquetas o de WhatsApp no tiene `guia_ecorex`. En ese caso pega al inicio de la
conversacion el contenido de [[00 - LEEME PRIMERO]], [[03 - Reglas de oro]] y la nota del servidor que use
([[M01 - MCP Maquetas]] o [[W01 - MCP WhatsApp por linea]]). Tambien puede leerse en el modulo **Cerebros** de
ECOREX ("Guia de uso de ECOREX por IA").
