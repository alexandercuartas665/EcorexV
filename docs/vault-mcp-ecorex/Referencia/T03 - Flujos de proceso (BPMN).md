---
tipo: referencia
servidor: ecorex-tableros
modulo: flujos
herramientas: 16
fuente: catalogo vivo tools/list (generado)
---

# MCP Tableros - Flujos de proceso (BPMN)

Servidor: `/api/mcp/tableros` - ver [[01 - Conectar los MCP]]. Volver a [[00 - LEEME PRIMERO]].

## Para que sirve

Diseno de procesos (WorkflowEngine BPMN 2.0): nodos tarea (paso humano), compuertas (decision), inicio y fin.

Reglas:
- `crear_flujo` nace con inicio->fin YA conectados. Inserta pasos con `agregar_nodo` dando `desde_nodo_id` y
  `hasta_nodo_id` (reemplaza esa conexion directa).
- Un nodo que NO es compuerta sigue TODAS sus salidas en paralelo. Para decidir usa una **compuerta** y pon
  `condicion_ruta` en sus aristas (vacia = ruta por defecto).
- Encargado del paso: `asignar_encargado_nodo` (politica/hereda_inicio/hereda_anterior/campo_formulario); con
  `politica` agrega cargos con `asignar_cargo_nodo`.
- Revisa `ver_flujo`: si trae **avisos**, muestraselos al usuario antes de `publicar_flujo`.
- Un flujo publicado no se edita: usa `nueva_version_flujo`.
- Para que las tareas lo arranquen: `enganchar_flujo_a_concepto`.

Recetas: [[R03 - Disenar y publicar un flujo]]

## Herramientas (16)

- **Lectura:** `listar_flujos`, `ver_flujo`
- **Escritura:** `crear_flujo`, `nueva_version_flujo`, `agregar_nodo`, `renombrar_nodo`, `conectar_nodos`, `eliminar_conexion`, `condicion_ruta`, `asignar_encargado_nodo`, `asignar_cargo_nodo`, `poner_formulario_a_nodo`, `importar_bpmn`, `publicar_flujo`, `enganchar_flujo_a_concepto`
- **Destructivas:** `eliminar_nodo`

### `listar_flujos`

**Tipo:** Lectura

> Lista los flujos (procesos) del tenant con su estado, version, numero de nodos e instancias en marcha. Usala primero para conocer nombres, codigos e ids exactos.

_Sin parametros._

Llamada minima (solo obligatorios):

```json
{
  "name": "listar_flujos",
  "arguments": {}
}
```

### `ver_flujo`

**Tipo:** Lectura

> Muestra el grafo de un flujo: nodos (id, tipo, nombre, origen del encargado, cargos, formularios), aristas (id, desde, hasta, condicion) y AVISOS de revision (ramas paralelas involuntarias, nodos sin entrada o sin salida, pasos sin quien los atienda). 'flujo' = nombre, codigo o id (ver listar_flujos).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `flujo` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "ver_flujo",
  "arguments": {
    "flujo": "<flujo>"
  }
}
```

### `crear_flujo`

**Tipo:** Escritura

> Crea un flujo NUEVO en borrador. Nace con un nodo de inicio y uno de fin YA CONECTADOS entre si. Para meter pasos usa agregar_nodo con desde_nodo_id=inicio y hasta_nodo_id=fin (inserta y reemplaza esa conexion). Devuelve 'definicion_id', nodos y aristas.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nombre` | string | SI |  |
| `categoria` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "crear_flujo",
  "arguments": {
    "nombre": "<nombre>"
  }
}
```

### `nueva_version_flujo`

**Tipo:** Escritura

> Para MODIFICAR un flujo ya publicado: crea (o reutiliza) su version borrador siguiente y devuelve su 'definicion_id' con los ids NUEVOS de nodos y aristas (los vinculos se copian). Edita ese borrador y publicalo: los conceptos enganchados pasan solos a la version nueva.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `flujo` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "nueva_version_flujo",
  "arguments": {
    "flujo": "<flujo>"
  }
}
```

### `agregar_nodo`

**Tipo:** Escritura

> Agrega un nodo a un flujo en borrador. 'tipo': tarea (paso humano), compuerta (decision con rutas), fin o inicio (solo hay uno). Con 'desde_nodo_id' y 'hasta_nodo_id' lo INSERTA entre esos dos nodos: conecta desde->nuevo->hasta y quita la conexion directa desde->hasta (conserva su condicion). Sin ellos queda suelto y hay que conectarlo con conectar_nodos. Devuelve 'nodo_id'.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `flujo` | string | SI | definicion_id del borrador |
| `tipo` | string (tarea / compuerta / fin / inicio) | SI |  |
| `nombre` | string |  |  |
| `desde_nodo_id` | string |  | Insertar despues de este nodo |
| `hasta_nodo_id` | string |  | Insertar antes de este nodo |
| `x` | integer |  |  |
| `y` | integer |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "agregar_nodo",
  "arguments": {
    "flujo": "<flujo>",
    "tipo": "tarea"
  }
}
```

Ejemplo real (valores genericos):

```json
{
  "name": "agregar_nodo",
  "arguments": {
    "flujo": "<definicion_id>",
    "tipo": "tarea",
    "nombre": "Revisar solicitud",
    "desde_nodo_id": "<id_inicio>",
    "hasta_nodo_id": "<id_fin>"
  }
}
```

### `renombrar_nodo`

**Tipo:** Escritura

> Cambia el nombre de un nodo (ver 'nodo_id' en ver_flujo).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI |  |
| `nombre` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "renombrar_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>",
    "nombre": "<nombre>"
  }
}
```

### `conectar_nodos`

**Tipo:** Escritura

> Conecta dos nodos del mismo flujo en borrador (de 'desde_nodo_id' a 'hasta_nodo_id'). OJO: un nodo que no es compuerta sigue TODAS sus salidas en paralelo. 'condicion' opcional para una salida de compuerta (formato del motor, ej. approval == 'Aprobado'; vacio = ruta por defecto). Devuelve 'arista_id'.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `desde_nodo_id` | string | SI |  |
| `hasta_nodo_id` | string | SI |  |
| `condicion` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "conectar_nodos",
  "arguments": {
    "desde_nodo_id": "<desde_nodo_id>",
    "hasta_nodo_id": "<hasta_nodo_id>"
  }
}
```

### `eliminar_conexion`

**Tipo:** Escritura

> Quita una conexion (arista) de un flujo en borrador. 'arista_id' de ver_flujo.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `arista_id` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "eliminar_conexion",
  "arguments": {
    "arista_id": "<arista_id>"
  }
}
```

### `condicion_ruta`

**Tipo:** Escritura

> Fija (o limpia) la condicion de una arista/salida de compuerta. 'arista_id' de ver_flujo; 'condicion' vacia = ruta por defecto.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `arista_id` | string | SI |  |
| `condicion` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "condicion_ruta",
  "arguments": {
    "arista_id": "<arista_id>"
  }
}
```

### `asignar_encargado_nodo`

**Tipo:** Escritura

> Define de donde sale el ENCARGADO de un nodo. 'origen': politica (por cargo/dependencia: asignalos con asignar_cargo_nodo), hereda_inicio (quien arranco el flujo), hereda_anterior (quien cerro el paso previo) o campo_formulario (toma el usuario de un campo; indica 'campo' con su codigo).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI |  |
| `origen` | string (politica / hereda_inicio / hereda_anterior / campo_formulario) | SI |  |
| `campo` | string |  | Codigo del campo (solo para campo_formulario) |

Llamada minima (solo obligatorios):

```json
{
  "name": "asignar_encargado_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>",
    "origen": "politica"
  }
}
```

### `asignar_cargo_nodo`

**Tipo:** Escritura

> Agrega (accion='agregar', por defecto) o quita (accion='quitar') un CARGO o DEPENDENCIA del organigrama que atiende el nodo (origen 'politica'). 'cargo' = id o nombre; si no existe responde las opciones validas.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI |  |
| `cargo` | string | SI |  |
| `accion` | string (agregar / quitar) |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "asignar_cargo_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>",
    "cargo": "<cargo>"
  }
}
```

### `poner_formulario_a_nodo`

**Tipo:** Escritura

> Vincula un formulario ACTIVO a un nodo (se diligencia al llegar el paso). 'formulario' = id, codigo o titulo (ver list_forms). 'obligatorio' true = el paso no se cierra hasta enviarlo (ADR-0077).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI |  |
| `formulario` | string | SI |  |
| `obligatorio` | boolean |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "poner_formulario_a_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>",
    "formulario": "<formulario>"
  }
}
```

### `eliminar_nodo`

**Tipo:** DESTRUCTIVA (confirma con el usuario)

> Borra un nodo del flujo en borrador con sus aristas y vinculos (NUNCA el nodo de inicio). Confirma con el usuario antes de usarla.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "eliminar_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>"
  }
}
```

### `importar_bpmn`

**Tipo:** Escritura

> Crea un flujo nuevo en borrador a partir de un XML BPMN 2.0 (el que exporta bpmn.io o un archivo .bpmn). Devuelve 'definicion_id'.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `bpmn_xml` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "importar_bpmn",
  "arguments": {
    "bpmn_xml": "<bpmn_xml>"
  }
}
```

### `publicar_flujo`

**Tipo:** Escritura

> Publica un flujo (deja activa esta version y despublica otras del mismo proceso). Antes revisa el grafo: si hay AVISOS no publica y los devuelve; muestraselos al usuario y solo con su visto bueno repite con forzar=true. Solo un flujo publicado arranca al crear tareas.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `flujo` | string | SI |  |
| `forzar` | boolean |  | Publicar aunque haya avisos (solo con visto bueno del usuario) |

Llamada minima (solo obligatorios):

```json
{
  "name": "publicar_flujo",
  "arguments": {
    "flujo": "<flujo>"
  }
}
```

### `enganchar_flujo_a_concepto`

**Tipo:** Escritura

> Asocia un flujo a un concepto (tipo de actividad): las tareas nuevas de ese concepto ARRANCAN el flujo. 'concepto' = codigo o nombre (ver listar_conceptos); 'flujo' = id, codigo o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `concepto` | string | SI |  |
| `flujo` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "enganchar_flujo_a_concepto",
  "arguments": {
    "concepto": "<concepto>",
    "flujo": "<flujo>"
  }
}
```
