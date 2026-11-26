---
tipo: referencia
servidor: ecorex-tableros
modulo: tableros-tareas
herramientas: 17
fuente: catalogo vivo tools/list (generado)
---

# MCP Tableros - Tableros y tareas

Servidor: `/api/mcp/tableros` - ver [[01 - Conectar los MCP]]. Volver a [[00 - LEEME PRIMERO]].

## Para que sirve

Opera el trabajo diario: tableros Kanban, tareas (= actividades, se identifican por numero tipo `T00123`),
comentarios, checklist, horas y los PASOS de flujo que tiene el usuario.

Conceptos clave:
- Una tarea puede estar **gobernada por un flujo** (BPMN). Si lo esta, NO se mueve de columna ni se termina a mano:
  se avanza con `completar_paso` (eligiendo la `opcion` exacta si el paso tiene rutas).
- Un **concepto** (tipo de actividad, ver `listar_conceptos`) clasifica la tarea; si el concepto tiene flujo
  enganchado, `crear_tarea` con ese concepto ARRANCA el flujo.
- Empieza siempre por `listar_tableros` o `mis_pasos_pendientes`.
- `guia_ecorex` es ESTA guia servida por el MCP: sin argumentos da la nota de entrada; `nota` abre una nota por
  titulo; `buscar` encuentra notas por texto (por ejemplo, el texto de un error).

Recetas: [[R01 - Operar tareas y tableros]]

## Herramientas (17)

- **Lectura:** `listar_tableros`, `ver_tablero`, `buscar_tareas`, `ver_tarea`, `mis_pasos_pendientes`, `listar_conceptos`, `listar_usuarios`, `guia_ecorex`
- **Escritura:** `crear_tarea`, `mover_tarea`, `terminar_tarea`, `comentar`, `asignar_tarea`, `checklist`, `registrar_tiempo`, `tomar_paso`, `completar_paso`
- **Destructivas:** _ninguna_

### `listar_tableros`

**Tipo:** Lectura

> Lista los tableros de tareas que el usuario puede ver: nombre, codigo, columnas y numero de tareas. Usala primero para conocer los nombres exactos de tableros y columnas.

_Sin parametros._

Llamada minima (solo obligatorios):

```json
{
  "name": "listar_tableros",
  "arguments": {}
}
```

### `ver_tablero`

**Tipo:** Lectura

> Muestra un tablero por columnas con sus tareas (numero, titulo, prioridad, encargado, vencimiento y si la gobierna un flujo). 'alcance': equipo (todas), mias, sin_asignar o terminadas. Si el tablero limita la vista del usuario, el servidor fuerza 'mias'.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `tablero` | string | SI | Nombre, codigo o id del tablero |
| `alcance` | string (equipo / mias / sin_asignar / terminadas) |  |  |
| `columna` | string |  | Solo esta columna (nombre) |
| `texto` | string |  | Filtra por numero, titulo, descripcion o solicitante |

Llamada minima (solo obligatorios):

```json
{
  "name": "ver_tablero",
  "arguments": {
    "tablero": "<tablero>"
  }
}
```

### `buscar_tareas`

**Tipo:** Lectura

> Busca tareas por texto (numero, titulo, descripcion) en todo el tenant, limitado a lo que el usuario puede ver. 'solo_mias' = solo donde es encargado/asignado o tiene un paso de flujo.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `texto` | string |  |  |
| `estado` | string (abiertas / terminadas / todas) |  |  |
| `solo_mias` | boolean |  |  |
| `limite` | integer >=1 <=50 |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "buscar_tareas",
  "arguments": {}
}
```

### `ver_tarea`

**Tipo:** Lectura

> Detalle de una tarea por numero (ej. T00123): descripcion, solicitante, checklist, asignados, ultimas anotaciones, adjuntos y, si tiene flujo, el PASO ACTUAL con sus opciones/rutas y si el usuario puede atenderlo.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `numero` | string | SI | Numero de la tarea, ej. T00123 |

Llamada minima (solo obligatorios):

```json
{
  "name": "ver_tarea",
  "arguments": {
    "numero": "<numero>"
  }
}
```

### `mis_pasos_pendientes`

**Tipo:** Lectura

> Lista los pasos de flujo que esperan al usuario (asignados a el o que puede tomar), con la tarea, el proceso, el paso y sus opciones de aprobacion.

_Sin parametros._

Llamada minima (solo obligatorios):

```json
{
  "name": "mis_pasos_pendientes",
  "arguments": {}
}
```

### `listar_conceptos`

**Tipo:** Lectura

> Lista los conceptos (tipos de actividad) con los que se puede crear una tarea. Un concepto puede traer su propio tablero y ARRANCAR un flujo al crear la tarea.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `texto` | string |  | Filtro por nombre o codigo |

Llamada minima (solo obligatorios):

```json
{
  "name": "listar_conceptos",
  "arguments": {}
}
```

### `listar_usuarios`

**Tipo:** Lectura

> Lista los usuarios activos del tenant (nombre y email) para asignar tareas.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `texto` | string |  | Filtro por nombre o email |

Llamada minima (solo obligatorios):

```json
{
  "name": "listar_usuarios",
  "arguments": {}
}
```

### `crear_tarea`

**Tipo:** Escritura

> Crea una tarea en un tablero. Si indicas 'concepto' la tarea se clasifica por el y, si el concepto tiene proceso, ARRANCA su flujo (y puede usar el tablero del concepto si no das 'tablero'). Sin 'columna' cae en la primera. Devuelve el numero de la tarea.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `tablero` | string |  | Nombre, codigo o id del tablero |
| `columna` | string |  |  |
| `concepto` | string |  | Nombre o codigo del concepto (ver listar_conceptos) |
| `titulo` | string | SI |  |
| `descripcion` | string |  |  |
| `prioridad` | string (baja / media / alta) |  |  |
| `vence` | string |  | Fecha limite ISO 8601 (ej. 2026-11-20) |
| `asignado` | string |  | Email o nombre del encargado (ver listar_usuarios) |

Llamada minima (solo obligatorios):

```json
{
  "name": "crear_tarea",
  "arguments": {
    "titulo": "<titulo>"
  }
}
```

Ejemplo real (valores genericos):

```json
{
  "name": "crear_tarea",
  "arguments": {
    "tablero": "Comercial",
    "concepto": "Cotizacion",
    "titulo": "Cotizar 20 sillas ergonomicas",
    "descripcion": "Cliente pide entrega en 15 dias",
    "prioridad": "alta",
    "vence": "2026-12-15",
    "asignado": "ana@miempresa.com"
  }
}
```

### `mover_tarea`

**Tipo:** Escritura

> Mueve una tarea SIN flujo a otra columna de su tablero. Si la tarea esta gobernada por un flujo en curso NO se mueve a mano: usa completar_paso. Si la columna destino es de cierre y el tablero exige motivo, envia 'motivo_cierre'.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `numero` | string | SI |  |
| `columna` | string | SI | Nombre de la columna destino |
| `motivo_cierre` | string |  |  |
| `nota` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "mover_tarea",
  "arguments": {
    "numero": "<numero>",
    "columna": "<columna>"
  }
}
```

### `terminar_tarea`

**Tipo:** Escritura

> Marca una tarea como TERMINADA (estado Hecho). La lleva por los estados validos de la maquina (pendiente -> en progreso -> hecho) y, si el tablero tiene columna de cierre, deja la tarjeta alli. Es idempotente: si ya estaba terminada no hace nada. Si la gobierna un flujo EN CURSO no se cierra a mano: avanzalo con completar_paso. Si el tablero exige concepto de cierre, pasalo en 'motivo_cierre'.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `numero` | string | SI | Numero de la tarea, ej. T00123 |
| `motivo_cierre` | string |  | Concepto de cierre si el tablero lo exige |
| `nota` | string |  | Nota opcional para la bitacora |

Llamada minima (solo obligatorios):

```json
{
  "name": "terminar_tarea",
  "arguments": {
    "numero": "<numero>"
  }
}
```

### `comentar`

**Tipo:** Escritura

> Agrega un comentario a la bitacora de la tarea (queda a nombre del usuario, marcado 'via IA').

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `numero` | string | SI |  |
| `texto` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "comentar",
  "arguments": {
    "numero": "<numero>",
    "texto": "<texto>"
  }
}
```

### `asignar_tarea`

**Tipo:** Escritura

> Cambia el ENCARGADO de la tarea. 'usuario' = email o nombre (ver listar_usuarios); vacio = quitar encargado.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `numero` | string | SI |  |
| `usuario` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "asignar_tarea",
  "arguments": {
    "numero": "<numero>"
  }
}
```

### `checklist`

**Tipo:** Escritura

> Gestiona el checklist de la tarea. accion=agregar (con 'texto'), marcar o desmarcar (con 'item' = texto o posicion 1..n).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `numero` | string | SI |  |
| `accion` | string (agregar / marcar / desmarcar) | SI |  |
| `texto` | string |  |  |
| `item` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "checklist",
  "arguments": {
    "numero": "<numero>",
    "accion": "agregar"
  }
}
```

### `registrar_tiempo`

**Tipo:** Escritura

> Registra tiempo trabajado por el usuario en la tarea.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `numero` | string | SI |  |
| `minutos` | integer >=1 <=1440 | SI |  |
| `nota` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "registrar_tiempo",
  "arguments": {
    "numero": "<numero>",
    "minutos": 1
  }
}
```

### `tomar_paso`

**Tipo:** Escritura

> Toma (reclama) el paso actual del flujo de una tarea que esta sin asignar y que el usuario puede atender.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `numero` | string | SI |  |
| `paso` | string |  | Nombre del paso si hay varios en curso |

Llamada minima (solo obligatorios):

```json
{
  "name": "tomar_paso",
  "arguments": {
    "numero": "<numero>"
  }
}
```

### `completar_paso`

**Tipo:** Escritura

> Cierra el PASO ACTUAL del flujo de la tarea y el flujo avanza. Si el paso ofrece opciones/rutas (ver ver_tarea) indica 'opcion' con una de ellas EXACTA. Si el paso exige un formulario sin diligenciar, el servidor lo rechaza: el usuario debe llenarlo en la aplicacion.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `numero` | string | SI |  |
| `opcion` | string |  | Opcion/ruta exacta del paso (ver ver_tarea) |
| `comentario` | string |  |  |
| `paso` | string |  | Nombre del paso si hay varios en curso |

Llamada minima (solo obligatorios):

```json
{
  "name": "completar_paso",
  "arguments": {
    "numero": "<numero>"
  }
}
```

Ejemplo real (valores genericos):

```json
{
  "name": "completar_paso",
  "arguments": {
    "numero": "T00123",
    "opcion": "Aprobado",
    "comentario": "Revisado con el cliente"
  }
}
```

### `guia_ecorex`

**Tipo:** Lectura

> Manual de uso de este MCP: protocolo, reglas, trampas conocidas y recetas paso a paso (contenedores, conectores, reportes, flujos, formularios, drones, agentes, clasificacion IA). Llamala antes de construir algo nuevo. Sin argumentos devuelve la nota de entrada, los titulos de todas las notas y los 'temas' (cerebros especializados, p.ej. flujos y formularios); 'tema' = abre ese cerebro (combinable con nota/buscar); 'nota' = una nota completa por titulo; 'buscar' = notas que contienen el texto (por ejemplo, el texto de un error), maximo 20.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `tema` | string |  | Cerebro especializado (ver 'temas' sin argumentos), ej. flujos, formularios. Omitido = guia general |
| `nota` | string |  | Titulo de la nota (ver la lista sin argumentos) |
| `buscar` | string |  | Texto a buscar en titulos y contenido |

Llamada minima (solo obligatorios):

```json
{
  "name": "guia_ecorex",
  "arguments": {}
}
```

Ejemplo real (valores genericos):

```json
{
  "name": "guia_ecorex",
  "arguments": {
    "buscar": "Tiempo de espera agotado"
  }
}
```
