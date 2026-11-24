---
tipo: referencia
servidor: ecorex-tableros
modulo: contenedores
herramientas: 26
fuente: catalogo vivo tools/list (generado)
---

# MCP Tableros - Contenedores de datos

Servidor: `/api/mcp/tableros` - ver [[01 - Conectar los MCP]]. Volver a [[00 - LEEME PRIMERO]].

## Para que sirve

Un **contenedor de datos** es una mini base de datos del cliente (modelo ER): contenedor -> **tablas** ->
**columnas** (texto, numero, decimal, fecha, booleano, submodelo) + **relaciones** (n1 / nn).

Se llena por **conectores** (fuentes: excel, api REST, bd, tabla) + **programaciones de importacion** (manual,
intervalo, cron; modo append / replace / upsert por `columna_clave`), por drones, por agentes de IA, o en poca
cantidad directo por MCP con `agregar_filas` (max 100 por llamada). `ver_filas` lee filas para verificar.
Cada tabla raiz puede **publicarse** como modulo del menu y es **fuente de reportes** (`container:{tabla_id}`).

Reglas:
- **Los conectores NO reciben secretos por MCP**: claves/API keys se ponen en la pantalla del contenedor.
- **No se puede borrar una columna que ya tiene datos** (ni vaciarla con eliminar). Planea nombres y tipos antes
  de importar; para "resetear" una columna usa `limpiar_marca` o `rellenar_columna` (modulo Clasificacion).
- Para sumar/promediar montos la columna debe ser `numero` o `decimal` (no texto).
- Upsert: la `columna_clave` debe estar MAPEADA en el conector.
- `ver_conector` muestra el endpoint, el mapeo y la paginacion de un conector (sin secretos: los parametros
  sensibles salen como `***`). Leelo SIEMPRE antes de editar.
- `guardar_conector` en EDICION cambia solo lo que envies; lo omitido (mapeo, destino, auth...) se conserva.
  No reenvies un endpoint con `***` (lo rechaza).
- `agregar_filas` valida cada valor contra el tipo de su columna (decimal con punto y sin miles, fecha AAAA-MM-DD,
  booleano true/false/si/no) y las obligatorias: UN error rechaza TODO el lote (la respuesta lista los errores).
  Con `columna_clave` actualiza la fila existente (upsert). No sirve en tablas derivadas.

Recetas: [[R05 - Crear un contenedor de datos]] - [[R06 - Conector API e importacion programada]]

## Herramientas (26)

- **Lectura:** `listar_contenedores_datos`, `ver_contenedor_datos`, `listar_conectores`, `ver_conector`, `listar_programaciones_importacion`, `listar_corridas_importacion`, `ver_publicacion_contenedor`, `ver_filas`
- **Escritura:** `crear_contenedor_datos`, `editar_contenedor_datos`, `agregar_tabla`, `editar_tabla`, `agregar_columna`, `editar_columna`, `agregar_relacion`, `guardar_conector`, `guardar_programacion_importacion`, `publicar_contenedor`, `agregar_filas`
- **Destructivas:** `eliminar_contenedor_datos`, `eliminar_tabla`, `eliminar_columna`, `eliminar_relacion`, `eliminar_conector`, `eliminar_programacion_importacion`, `despublicar_contenedor`

### `listar_contenedores_datos`

**Tipo:** Lectura

> Lista los contenedores de datos del tenant (modelos que agrupan tablas): id, nombre, descripcion, numero de tablas y de relaciones. Usala primero para conocer ids y nombres exactos.

_Sin parametros._

Llamada minima (solo obligatorios):

```json
{
  "name": "listar_contenedores_datos",
  "arguments": {}
}
```

### `ver_contenedor_datos`

**Tipo:** Lectura

> Muestra un contenedor completo: sus TABLAS (id, nombre, columnas con tipo/requerido y si es derivada) y las RELACIONES entre tablas. 'contenedor' = id o nombre (ver listar_contenedores_datos).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `contenedor` | string | SI | id o nombre del contenedor |

Llamada minima (solo obligatorios):

```json
{
  "name": "ver_contenedor_datos",
  "arguments": {
    "contenedor": "<contenedor>"
  }
}
```

### `crear_contenedor_datos`

**Tipo:** Escritura

> Crea un contenedor de datos NUEVO (cabecera: nombre + descripcion). Despues agregale tablas con agregar_tabla. Devuelve 'contenedor_id'.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nombre` | string | SI |  |
| `descripcion` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "crear_contenedor_datos",
  "arguments": {
    "nombre": "<nombre>"
  }
}
```

Ejemplo real (valores genericos):

```json
{
  "name": "crear_contenedor_datos",
  "arguments": {
    "nombre": "Ventas",
    "descripcion": "Pedidos y clientes importados del ERP"
  }
}
```

### `editar_contenedor_datos`

**Tipo:** Escritura

> Edita la cabecera de un contenedor (nombre y/o descripcion). 'contenedor' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `contenedor` | string | SI |  |
| `nombre` | string |  |  |
| `descripcion` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "editar_contenedor_datos",
  "arguments": {
    "contenedor": "<contenedor>"
  }
}
```

### `eliminar_contenedor_datos`

**Tipo:** DESTRUCTIVA (confirma con el usuario)

> Borra un contenedor COMPLETO con sus tablas, columnas, filas, relaciones y configuracion de importacion (cascada). Confirma con el usuario antes de usarla. 'contenedor' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `contenedor` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "eliminar_contenedor_datos",
  "arguments": {
    "contenedor": "<contenedor>"
  }
}
```

### `agregar_tabla`

**Tipo:** Escritura

> Agrega una TABLA a un contenedor. 'columnas' (opcional) es una lista de objetos {nombre, tipo, requerido}. Tipos: texto, numero, decimal, fecha, booleano, submodelo (sub-tabla anidada; requiere 'hijo' = id de otra tabla). Devuelve 'tabla_id'.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `contenedor` | string | SI | id o nombre del contenedor |
| `nombre` | string | SI |  |
| `descripcion` | string |  |  |
| `columnas` | array de objetos |  |  |
| `columnas[].nombre` | string | SI |  |
| `columnas[].tipo` | string |  |  |
| `columnas[].requerido` | boolean |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "agregar_tabla",
  "arguments": {
    "contenedor": "<contenedor>",
    "nombre": "<nombre>"
  }
}
```

Ejemplo real (valores genericos):

```json
{
  "name": "agregar_tabla",
  "arguments": {
    "contenedor": "Ventas",
    "nombre": "Pedidos",
    "columnas": [
      {
        "nombre": "id_pedido",
        "tipo": "texto",
        "requerido": true
      },
      {
        "nombre": "cliente",
        "tipo": "texto"
      },
      {
        "nombre": "fecha",
        "tipo": "fecha"
      },
      {
        "nombre": "valor",
        "tipo": "decimal"
      }
    ]
  }
}
```

### `editar_tabla`

**Tipo:** Escritura

> Edita el nombre y/o descripcion de una tabla (conserva sus columnas). 'contenedor' y 'tabla' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `contenedor` | string | SI |  |
| `tabla` | string | SI |  |
| `nombre` | string |  |  |
| `descripcion` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "editar_tabla",
  "arguments": {
    "contenedor": "<contenedor>",
    "tabla": "<tabla>"
  }
}
```

### `eliminar_tabla`

**Tipo:** DESTRUCTIVA (confirma con el usuario)

> Borra una tabla de un contenedor (con sus columnas y filas). Confirma con el usuario. 'contenedor' y 'tabla' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `contenedor` | string | SI |  |
| `tabla` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "eliminar_tabla",
  "arguments": {
    "contenedor": "<contenedor>",
    "tabla": "<tabla>"
  }
}
```

### `agregar_columna`

**Tipo:** Escritura

> Agrega una columna a una tabla. 'tipo': texto, numero, decimal, fecha, booleano, submodelo (sub-tabla anidada; requiere 'hijo' = id de otra tabla). 'requerido' marca el campo obligatorio. 'contenedor' y 'tabla' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `contenedor` | string | SI |  |
| `tabla` | string | SI |  |
| `nombre` | string | SI |  |
| `tipo` | string | SI |  |
| `requerido` | boolean |  |  |
| `hijo` | string |  | solo tipo submodelo: id de la tabla hija |

Llamada minima (solo obligatorios):

```json
{
  "name": "agregar_columna",
  "arguments": {
    "contenedor": "<contenedor>",
    "tabla": "<tabla>",
    "nombre": "<nombre>",
    "tipo": "<tipo>"
  }
}
```

### `editar_columna`

**Tipo:** Escritura

> Edita una columna existente (por nombre o posicion 1..n). Cambia solo lo que envies (nombre nuevo, tipo, requerido). 'contenedor' y 'tabla' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `contenedor` | string | SI |  |
| `tabla` | string | SI |  |
| `columna` | string | SI | nombre actual o posicion 1..n |
| `nombre` | string |  | nombre nuevo (opcional) |
| `tipo` | string |  |  |
| `requerido` | boolean |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "editar_columna",
  "arguments": {
    "contenedor": "<contenedor>",
    "tabla": "<tabla>",
    "columna": "<columna>"
  }
}
```

### `eliminar_columna`

**Tipo:** DESTRUCTIVA (confirma con el usuario)

> Borra una columna de una tabla (por nombre o posicion). No se puede borrar una columna que ya tiene datos. 'contenedor' y 'tabla' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `contenedor` | string | SI |  |
| `tabla` | string | SI |  |
| `columna` | string | SI | nombre o posicion 1..n |

Llamada minima (solo obligatorios):

```json
{
  "name": "eliminar_columna",
  "arguments": {
    "contenedor": "<contenedor>",
    "tabla": "<tabla>",
    "columna": "<columna>"
  }
}
```

### `agregar_relacion`

**Tipo:** Escritura

> Crea una relacion entre dos tablas del MISMO contenedor. 'cardinalidad': n1 (muchos a uno / lookup) o nn (muchos a muchos). 'contenedor', 'origen' y 'destino' = id o nombre de tabla.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `contenedor` | string | SI |  |
| `origen` | string | SI | tabla origen (id o nombre) |
| `destino` | string | SI | tabla destino (id o nombre) |
| `cardinalidad` | string (n1 / nn) | SI |  |
| `nombre` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "agregar_relacion",
  "arguments": {
    "contenedor": "<contenedor>",
    "origen": "<origen>",
    "destino": "<destino>",
    "cardinalidad": "n1"
  }
}
```

Ejemplo real (valores genericos):

```json
{
  "name": "agregar_relacion",
  "arguments": {
    "contenedor": "Ventas",
    "origen": "Pedidos",
    "destino": "Clientes",
    "cardinalidad": "n1",
    "nombre": "cliente del pedido"
  }
}
```

### `eliminar_relacion`

**Tipo:** DESTRUCTIVA (confirma con el usuario)

> Borra una relacion por su id (ver ver_contenedor_datos).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `relacion_id` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "eliminar_relacion",
  "arguments": {
    "relacion_id": "<relacion_id>"
  }
}
```

### `listar_conectores`

**Tipo:** Lectura

> Lista los conectores (fuentes) de un contenedor: id, nombre, tipo (excel/api/bd/tabla), si ya tiene credenciales, tabla destino y si esta activo. 'contenedor' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `contenedor` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "listar_conectores",
  "arguments": {
    "contenedor": "<contenedor>"
  }
}
```

### `ver_conector`

**Tipo:** Lectura

> Muestra la configuracion COMPLETA de un conector (solo lectura, SIN secretos): tipo, tabla destino, activo y, segun el tipo, api -> endpoint (parametros sensibles enmascarados), metodo, auth, nombres de headers y mapeo_json (RestFetchSpec: arrayPath, campos y paginacion None/Offset/Page con limite y tope de paginas); bd -> motor, host, puerto, base, usuario y query; tabla -> receta de derivacion. Usala ANTES de editar con guardar_conector. 'contenedor' y 'conector' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `contenedor` | string | SI |  |
| `conector` | string | SI | id o nombre del conector |

Llamada minima (solo obligatorios):

```json
{
  "name": "ver_conector",
  "arguments": {
    "contenedor": "<contenedor>",
    "conector": "<conector>"
  }
}
```

Ejemplo real (valores genericos):

```json
{
  "name": "ver_conector",
  "arguments": {
    "contenedor": "Ventas",
    "conector": "API pedidos"
  }
}
```

### `guardar_conector`

**Tipo:** Escritura

> Crea o edita un conector (fuente de datos) de un contenedor. 'tipo': excel, api (REST), bd (base de datos) o tabla (desde otra tabla del mismo contenedor). IMPORTANTE: el MCP NO recibe credenciales; el secreto (clave de API / contrasena) se pone en la pantalla. 'destino' = tabla donde aterrizan los datos. Para editar, envia 'conector' (id o nombre): solo cambia lo que envies (el resto, incluido el mapeo, la tabla destino y los headers, se conserva; revisa antes con ver_conector). Para crear, 'nombre' y 'tipo' son obligatorios.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `contenedor` | string | SI |  |
| `conector` | string |  | id o nombre si editas; omitelo para crear |
| `nombre` | string |  |  |
| `tipo` | string (excel / api / bd / tabla) |  |  |
| `destino` | string |  | tabla destino (id o nombre) |
| `endpoint` | string |  | API: URL |
| `metodo` | string |  | API: GET/POST |
| `auth` | string (ninguna / apikey / bearer / basic / tokenexchange) |  |  |
| `motor` | string (postgresql / mysql / sqlserver / oracle / mariadb / sqlite) |  |  |
| `host` | string |  |  |
| `puerto` | integer |  |  |
| `base_datos` | string |  |  |
| `usuario` | string |  |  |
| `query` | string |  | BD: SELECT |
| `mapeo_json` | string |  |  |
| `activo` | boolean |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "guardar_conector",
  "arguments": {
    "contenedor": "<contenedor>"
  }
}
```

Ejemplo real (valores genericos):

```json
{
  "name": "guardar_conector",
  "arguments": {
    "contenedor": "Ventas",
    "nombre": "API pedidos",
    "tipo": "api",
    "destino": "Pedidos",
    "endpoint": "https://api.ejemplo.com/v1/pedidos",
    "metodo": "GET",
    "auth": "ninguna",
    "mapeo_json": "{\"arrayPath\":\"data\",\"paging\":{\"mode\":\"Offset\",\"offsetParam\":\"offset\",\"limitParam\":\"limit\",\"pageSize\":500,\"maxPages\":20},\"fields\":[{\"column\":\"id_pedido\",\"path\":\"id\"},{\"column\":\"cliente\",\"path\":\"customer.name\"},{\"column\":\"valor\",\"path\":\"total\"}]}"
  }
}
```

### `eliminar_conector`

**Tipo:** DESTRUCTIVA (confirma con el usuario)

> Borra un conector de un contenedor. 'contenedor' = id o nombre; 'conector' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `contenedor` | string | SI |  |
| `conector` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "eliminar_conector",
  "arguments": {
    "contenedor": "<contenedor>",
    "conector": "<conector>"
  }
}
```

### `listar_programaciones_importacion`

**Tipo:** Lectura

> Lista los procesos de importacion (horarios) de un contenedor: id, nombre, tipo (manual/intervalo/cron), cada cuanto, modo, activo y proxima/ultima corrida. 'contenedor' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `contenedor` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "listar_programaciones_importacion",
  "arguments": {
    "contenedor": "<contenedor>"
  }
}
```

### `guardar_programacion_importacion`

**Tipo:** Escritura

> Crea o edita un proceso de importacion (horario) de un contenedor. 'tipo': manual, intervalo (cada N minutos en 'cada_minutos') o cron ('cron' de 5 campos, hora del tenant). 'modo': append, replace o upsert (con 'columna_clave'). 'conector' (opcional) = fuente a disparar. Para editar envia 'proceso'.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `contenedor` | string | SI |  |
| `proceso` | string |  | id o nombre si editas; omitelo para crear |
| `nombre` | string | SI |  |
| `tipo` | string (manual / intervalo / cron) | SI |  |
| `cada_minutos` | integer |  |  |
| `cron` | string |  |  |
| `modo` | string (append / replace / upsert) |  |  |
| `columna_clave` | string |  |  |
| `conector` | string |  | id o nombre del conector |
| `activa` | boolean |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "guardar_programacion_importacion",
  "arguments": {
    "contenedor": "<contenedor>",
    "nombre": "<nombre>",
    "tipo": "manual"
  }
}
```

Ejemplo real (valores genericos):

```json
{
  "name": "guardar_programacion_importacion",
  "arguments": {
    "contenedor": "Ventas",
    "nombre": "Pedidos diaria 5am",
    "tipo": "cron",
    "cron": "0 5 * * *",
    "modo": "upsert",
    "columna_clave": "id_pedido",
    "conector": "API pedidos",
    "activa": true
  }
}
```

### `eliminar_programacion_importacion`

**Tipo:** DESTRUCTIVA (confirma con el usuario)

> Borra un proceso de importacion (horario). 'contenedor' = id o nombre; 'proceso' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `contenedor` | string | SI |  |
| `proceso` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "eliminar_programacion_importacion",
  "arguments": {
    "contenedor": "<contenedor>",
    "proceso": "<proceso>"
  }
}
```

### `listar_corridas_importacion`

**Tipo:** Lectura

> Bitacora de un proceso de importacion, mas recientes primero: cuando corrio, disparador (manual/programada), resultado (ok/error/omitida/corriendo/agente_desconectado), filas insertadas/actualizadas/borradas y detalle. 'contenedor' = id o nombre; 'proceso' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `contenedor` | string | SI |  |
| `proceso` | string | SI |  |
| `limite` | integer >=1 <=50 |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "listar_corridas_importacion",
  "arguments": {
    "contenedor": "<contenedor>",
    "proceso": "<proceso>"
  }
}
```

### `ver_publicacion_contenedor`

**Tipo:** Lectura

> Dice si una tabla esta publicada como modulo del menu: ruta, icono y columnas de grilla/filtro. 'contenedor' y 'tabla' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `contenedor` | string | SI |  |
| `tabla` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "ver_publicacion_contenedor",
  "arguments": {
    "contenedor": "<contenedor>",
    "tabla": "<tabla>"
  }
}
```

### `publicar_contenedor`

**Tipo:** Escritura

> Publica una tabla RAIZ como modulo del menu (aparece en el menu y en la matriz de Roles y permisos, donde defines quien la ve). 'vista' (opcional) = vista de menu destino por nombre; sin ella, la vista por defecto del tenant. 'padre' = nombre de la SECCION del menu donde colgar el modulo (un elemento no puede colgar de la raiz: si la omites y la vista no tiene donde, el menu lo rechaza). 'icono' opcional. 'contenedor' y 'tabla' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `contenedor` | string | SI |  |
| `tabla` | string | SI |  |
| `vista` | string |  |  |
| `padre` | string |  |  |
| `icono` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "publicar_contenedor",
  "arguments": {
    "contenedor": "<contenedor>",
    "tabla": "<tabla>"
  }
}
```

Ejemplo real (valores genericos):

```json
{
  "name": "publicar_contenedor",
  "arguments": {
    "contenedor": "Ventas",
    "tabla": "Pedidos",
    "padre": "Comercial"
  }
}
```

### `despublicar_contenedor`

**Tipo:** DESTRUCTIVA (confirma con el usuario)

> Retira una tabla del menu (deja de ser modulo). No borra la tabla ni sus datos y conserva la ruta, asi que al republicar los permisos siguen valiendo. 'contenedor' y 'tabla' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `contenedor` | string | SI |  |
| `tabla` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "despublicar_contenedor",
  "arguments": {
    "contenedor": "<contenedor>",
    "tabla": "<tabla>"
  }
}
```

### `agregar_filas`

**Tipo:** Escritura

> Inserta POCAS filas (maximo 100 por llamada) en una tabla de un contenedor: para pruebas y cargas chicas (para volumen usa un conector + programacion de importacion). 'filas' = lista de objetos columna->valor con los NOMBRES de columna (ver_contenedor_datos). Cada valor se valida contra el tipo de la columna (numero entero, decimal con punto, fecha AAAA-MM-DD, booleano true/false) y las columnas obligatorias; una columna desconocida o un valor invalido rechaza TODO el lote sin guardar nada. Con 'columna_clave' hace upsert: actualiza la fila que tenga esa clave (solo las columnas enviadas) o la crea si no existe.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `contenedor` | string | SI | id o nombre del contenedor |
| `tabla` | string |  | id o nombre de la tabla (opcional si el contenedor tiene una sola) |
| `filas` | array de objetos | SI |  |
| `columna_clave` | string |  | opcional: columna para upsert |

Llamada minima (solo obligatorios):

```json
{
  "name": "agregar_filas",
  "arguments": {
    "contenedor": "<contenedor>",
    "filas": [
      {}
    ]
  }
}
```

Ejemplo real (valores genericos):

```json
{
  "name": "agregar_filas",
  "arguments": {
    "contenedor": "Ventas",
    "tabla": "Pedidos",
    "columna_clave": "id_pedido",
    "filas": [
      {
        "id_pedido": "P-001",
        "cliente": "Industrias Ejemplo",
        "fecha": "2026-01-10",
        "valor": 1250.5
      },
      {
        "id_pedido": "P-002",
        "cliente": "Comercial Demo",
        "fecha": "2026-02-03",
        "valor": 980
      }
    ]
  }
}
```

### `ver_filas`

**Tipo:** Lectura

> Lee filas de una tabla de un contenedor para verificar una carga: las mas recientes primero, con 'filtros' opcionales (columna + texto que contiene, sin distinguir mayusculas) y 'limite' (20 por defecto, maximo 100). Devuelve el total que cumple los filtros y las filas como columna->valor.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `contenedor` | string | SI |  |
| `tabla` | string |  |  |
| `filtros` | array de objetos |  |  |
| `filtros[].columna` | string | SI |  |
| `filtros[].valor` | string | SI |  |
| `limite` | integer >=1 <=100 |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "ver_filas",
  "arguments": {
    "contenedor": "<contenedor>"
  }
}
```

Ejemplo real (valores genericos):

```json
{
  "name": "ver_filas",
  "arguments": {
    "contenedor": "Ventas",
    "tabla": "Pedidos",
    "filtros": [
      {
        "columna": "cliente",
        "valor": "ejemplo"
      }
    ],
    "limite": 10
  }
}
```
