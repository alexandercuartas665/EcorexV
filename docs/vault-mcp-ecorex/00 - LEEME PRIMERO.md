---
tipo: indice
audiencia: sesion de IA (Claude, Gemini, ChatGPT...) conectada al MCP de ECOREX
---

# ECOREX por MCP - LEEME PRIMERO

Esta guia ensena a una sesion de IA **que nunca ha visto ECOREX** a operar y construir el sistema del cliente
a traves de sus servidores MCP: tareas, tableros, flujos, formularios, contactos, contenedores de datos,
conectores e importaciones, drones de extraccion web, agentes de IA, clasificacion con IA, reportes e
indicadores, maquetas y WhatsApp.

> Si eres una IA: lee esta nota completa, luego [[02 - Modelo mental del sistema]] y [[03 - Reglas de oro]].
> Antes de construir algo, abre la **receta** correspondiente (tabla de abajo). Antes de llamar una herramienta
> que no conoces, revisa su ficha en la **Referencia**. No adivines nombres de parametros.

## Como leer esta guia por MCP

Si la recibiste con la herramienta `guia_ecorex` (MCP de tableros), cada `[[Titulo]]` de estas notas es el titulo
de otra nota: abrela pasando ese titulo en `nota`. Ante un error, busca su texto con `buscar` ANTES de reintentar
(casi todos los errores ya vividos estan en las trampas y en las recetas).

```json
{"name": "guia_ecorex", "arguments": {"nota": "R05 - Crear un contenedor de datos"}}
{"name": "guia_ecorex", "arguments": {"buscar": "Tiempo de espera agotado"}}
```

**Cerebros por tema.** Para FLUJOS (procesos con aprobaciones, compuertas, avisos, enlaces de decision del cliente,
plazos) hay un cerebro especializado y mas profundo: abrelo con `tema`. Sin argumentos, `guia_ecorex` lista en
`temas` los que esten publicados.

```json
{"name": "guia_ecorex", "arguments": {"tema": "flujos"}}
```

Lee solo lo que necesites para la tarea: las notas de **Referencia** son largas (una por modulo); para una sola
herramienta, `buscar` con su nombre suele bastar. La misma guia esta en el modulo **Cerebros** de ECOREX
("Guia de uso de ECOREX por IA", solo lectura) y puede abrirse como vault de Obsidian.

## Que es ECOREX en una linea

Un SaaS multi-empresa (multi-tenant) donde **el proceso, el formulario, los datos y la regla se configuran sin
codigo**. El MCP te deja hacer por conversacion lo mismo que el usuario hace en pantalla, **con sus mismos
permisos**.

## Los 3 servidores MCP

| Servidor | Ruta | Para que | Token |
|---|---|---|---|
| **Tableros** (el principal, 164 herramientas) | `/api/mcp/tableros` | Todo el sistema: tareas, flujos, formularios, contactos, contenedores, drones, agentes, IA, reportes | Token personal de `/conectar-ia` |
| **Maquetas** (17) | `/api/mcp/maquetas` | Ajustar maquetas HTML desde solicitudes anotadas y leer/escribir cerebros | Token de maquetas |
| **WhatsApp por linea** (36) | `/api/mcp/whatsapp/{lineId}` | Leer y escribir por una linea de WhatsApp | Token de `/lineas` |

Como conectarlos: [[01 - Conectar los MCP]].

## Mapa rapido: "quiero hacer X" -> ve a Y

| Quiero... | Receta | Herramientas clave |
|---|---|---|
| Ver mis pendientes, crear/mover/cerrar tareas, avanzar pasos de flujo | [[R01 - Operar tareas y tableros]] | `mis_pasos_pendientes`, `crear_tarea`, `completar_paso` |
| Buscar, crear o editar clientes/proveedores | [[R02 - Directorio de contactos]] | `buscar_contacto`, `crear_contacto` |
| Disenar un proceso (aprobaciones, pasos, decisiones) | [[R03 - Disenar y publicar un flujo]] y el cerebro `tema: flujos` | `crear_flujo`, `agregar_nodo`, `publicar_flujo` |
| Crear un formulario (encuesta, solicitud, cotizador...) | [[R04 - Crear un formulario completo]] | `describe_components`, `apply_form_spec`, `activate` |
| Crear una base de datos propia (tablas, columnas) | [[R05 - Crear un contenedor de datos]] | `crear_contenedor_datos`, `agregar_tabla` |
| Traer datos de una API o ERP automaticamente | [[R06 - Conector API e importacion programada]] | `guardar_conector`, `guardar_programacion_importacion` |
| Hacer reportes, graficas, KPIs, tableros de indicadores | [[R07 - Crear reportes e indicadores]] | `crear_reporte`, `agregar_indicador` |
| Extraer datos de una pagina web (robot) | [[R08 - Crear un dron de extraccion]] | `crear_dron`, `agregar_paso` |
| Crear un asistente/agente de IA | [[R09 - Crear y configurar un agente de IA]] | `crear_agente`, `encender_agente` |
| Clasificar/enriquecer miles de filas con IA | [[R10 - Clasificar datos con IA]] | `clasificar_lote`, `configurar_paso_clasificacion` |
| Ver un caso real completo de punta a punta | [[R11 - Caso completo de punta a punta]] | todas |
| Trabajar maquetas HTML | [[M01 - MCP Maquetas]] | `listar_solicitudes`, `ver_solicitud` |
| Escribir/leer por WhatsApp | [[W01 - MCP WhatsApp por linea]] | `estado_linea`, `leer_mensajes` |

## Protocolo de trabajo (sigue SIEMPRE este orden)

1. **Entiende** el pedido. Si es ambiguo en algo que cambia el resultado (que tabla, que tablero, que columnas),
   pregunta una vez y con opciones concretas.
2. **Descubre antes de escribir**: usa las herramientas de lectura (`listar_*`, `ver_*`, `buscar_*`,
   `describe_components`, `listar_fuentes_reporte`) para conocer lo que YA existe. Casi todas las herramientas
   aceptan **nombre o id**; los ids salen de estas lecturas. Nunca inventes un id.
3. **Planea** la secuencia de llamadas (las recetas te dan el orden correcto). Si algo es destructivo, masivo o
   consume tokens de IA en volumen, **confirma con el usuario** antes.
4. **Ejecuta** paso a paso, leyendo cada respuesta: devuelven ids que necesitas para el siguiente paso, y si algo
   falla explican por que (muchas herramientas validan ANTES de guardar).
5. **Verifica** con una lectura (`ver_*`, `ver_reporte`, `listar_corridas*`, `verify_form`).
6. **Reporta** al usuario que quedo hecho, con nombres y donde verlo en pantalla, y que quedo pendiente.

## Contenido de la guia

**Fundamentos**
- [[01 - Conectar los MCP]] - tokens, configuracion de Claude Code/Desktop, Gemini, Codex, prueba de conexion.
- [[02 - Modelo mental del sistema]] - conceptos y glosario (tenant, tablero, concepto, flujo, contenedor...).
- [[03 - Reglas de oro]] - seguridad, confirmaciones, secretos, permisos.
- [[04 - Trampas conocidas]] - errores reales ya vividos y como evitarlos. **Leelo: ahorra horas.**
- [[05 - Solucion de problemas]] - mensajes de error y que hacer.

**Recetas paso a paso**: R01 a R11 (tabla de arriba).

**Referencia completa** (generada del catalogo real del servidor):
- [[T01 - Tableros y tareas]] - [[T02 - Directorio de contactos]] - [[T03 - Flujos de proceso (BPMN)]]
- [[T04 - Drones de extraccion web]] - [[T05 - Contenedores de datos]] - [[T06 - Agentes de IA]]
- [[T07 - Clasificacion y datos masivos con IA]] - [[T08 - Reportes e indicadores]] - [[T09 - Formularios dinamicos]]
- [[M01 - MCP Maquetas]] - [[W01 - MCP WhatsApp por linea]]
- [[Indice de herramientas (A-Z)]] - las 217 herramientas con su modulo.

**Para el cliente (opcional)**: [[Plantilla CLAUDE.md para el cliente]] - bloque para el CLAUDE.md de la IA del
cliente que refuerza el habito de consultar esta guia.

## Si la herramienta que buscas no esta

El catalogo crece con cada version. La fuente de verdad SIEMPRE es lo que devuelve el servidor (`tools/list`):
si ves una herramienta que esta guia no documenta, lee su descripcion y esquema; si esta guia documenta una
que el servidor ya no expone, el servidor manda. Si el usuario necesita algo que ninguna herramienta permite,
dilo claramente (y que puede hacerse en pantalla) en vez de improvisar.
