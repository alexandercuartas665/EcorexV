---
tipo: referencia
servidor: ecorex-tableros
modulo: drones
herramientas: 17
fuente: catalogo vivo tools/list (generado)
---

# MCP Tableros - Drones de extraccion web

Servidor: `/api/mcp/tableros` - ver [[01 - Conectar los MCP]]. Volver a [[00 - LEEME PRIMERO]].

## Para que sirve

Un **dron** es un robot de navegador que recorre un sitio web y extrae datos a una tabla de contenedor.
Corre en un **agente colmena** (PC/servidor con navegador real del cliente), no en la nube.

Estructura: cabecera (`crear_dron`: nombre + `url_inicio` + tabla destino) -> **pasos** en orden
(`agregar_paso`: navegar, esperar, clic, inyectar_script, extraer, ia, procesar_archivos, ...) -> **variables**
(`definir_variable`, marca `secreta` para claves) -> **programaciones** (`guardar_programacion`: manual, intervalo,
cron o barrido por tramos) -> bitacora (`listar_corridas`).

Reglas:
- El paso `extraer` lleva un `script` JS que DEVUELVE un arreglo de filas + `mapeo_json` (campo->columna) +
  `contenedor_destino`. `ingesta_llave` = columna clave para UPSERT (sin ella, cada corrida AGREGA filas).
- El paso `esperar` usa `espera_ms` (no `mapeo_json`).
- Si el sitio tiene reCAPTCHA o hay API publica, NO uses dron: usa un conector API (ver [[R06 - Conector API e importacion programada]]).

Recetas: [[R08 - Crear un dron de extraccion]]

## Herramientas (17)

- **Lectura:** `listar_drones`, `ver_dron`, `listar_destinos_extraccion`, `listar_reglas_extraccion`, `listar_corridas`, `listar_programaciones`
- **Escritura:** `crear_dron`, `editar_dron`, `agregar_paso`, `editar_paso`, `reordenar_pasos`, `definir_variable`, `guardar_programacion`
- **Destructivas:** `eliminar_paso`, `eliminar_variable`, `eliminar_programacion`, `eliminar_dron`

### `listar_drones`

**Tipo:** Lectura

> Lista los drones (flujos) de extraccion de datos del tenant: id, nombre, estado (activo/inactivo/error), numero de pasos, contenedor destino y ultima corrida. Usala primero para conocer ids y nombres exactos.

_Sin parametros._

Llamada minima (solo obligatorios):

```json
{
  "name": "listar_drones",
  "arguments": {}
}
```

### `ver_dron`

**Tipo:** Lectura

> Muestra un dron completo: cabecera (nombre, URL de arranque, estado, contenedor), sus PASOS en orden (id, tipo, nombre y campos), sus VARIABLES (sin revelar secretos) y sus PROGRAMACIONES. 'dron' = id o nombre (ver listar_drones).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `dron` | string | SI | id o nombre del dron |

Llamada minima (solo obligatorios):

```json
{
  "name": "ver_dron",
  "arguments": {
    "dron": "<dron>"
  }
}
```

### `listar_destinos_extraccion`

**Tipo:** Lectura

> Lista las tablas que un dron puede usar como destino de los datos extraidos (etiquetadas 'Modelo / Tabla'). Usa el id devuelto en 'contenedor' de crear_dron/editar_dron o 'contenedor_destino' de un paso.

_Sin parametros._

Llamada minima (solo obligatorios):

```json
{
  "name": "listar_destinos_extraccion",
  "arguments": {}
}
```

### `listar_reglas_extraccion`

**Tipo:** Lectura

> Lista las reglas de negocio del tenant disponibles para un paso tipo 'ensamblado' (etiquetadas 'Documento / Regla (verbo)'). Usa el id devuelto en 'regla' de agregar_paso/editar_paso.

_Sin parametros._

Llamada minima (solo obligatorios):

```json
{
  "name": "listar_reglas_extraccion",
  "arguments": {}
}
```

### `listar_corridas`

**Tipo:** Lectura

> Bitacora de ejecuciones de un dron, mas recientes primero: cuando corrio, resultado (ok/error/omitida/agente_desconectado), disparador (manual/programada), filas insertadas/actualizadas/borradas y detalle. 'dron' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `dron` | string | SI |  |
| `limite` | integer >=1 <=50 |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "listar_corridas",
  "arguments": {
    "dron": "<dron>"
  }
}
```

### `listar_programaciones`

**Tipo:** Lectura

> Lista las programaciones de un dron (un dron puede tener varias, ADR-0171): id, nombre, tipo (manual/intervalo/cron), cada cuanto, activa y proxima corrida. 'dron' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `dron` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "listar_programaciones",
  "arguments": {
    "dron": "<dron>"
  }
}
```

### `crear_dron`

**Tipo:** Escritura

> Crea un dron de extraccion NUEVO (cabecera). 'url_inicio' es la pagina de arranque (obligatoria). 'estado': activo (por defecto) o inactivo. 'contenedor' = id o nombre de la tabla destino (ver listar_destinos_extraccion). Despues agrega pasos con agregar_paso y variables con definir_variable. Devuelve 'dron_id'.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nombre` | string | SI |  |
| `descripcion` | string |  |  |
| `url_inicio` | string | SI |  |
| `estado` | string (activo / inactivo) |  |  |
| `contenedor` | string |  | id o nombre de la tabla destino |
| `pagina_var` | string |  | variable de paginacion (opcional) |
| `pagina_desde` | integer |  |  |
| `pagina_hasta` | integer |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "crear_dron",
  "arguments": {
    "nombre": "<nombre>",
    "url_inicio": "<url_inicio>"
  }
}
```

Ejemplo real (valores genericos):

```json
{
  "name": "crear_dron",
  "arguments": {
    "nombre": "Convocatorias portal X",
    "url_inicio": "https://www.ejemplo.gov.co/contratacion/",
    "contenedor": "Convocatorias",
    "descripcion": "Baja convocatorias publicadas"
  }
}
```

### `editar_dron`

**Tipo:** Escritura

> Edita la CABECERA de un dron: cambia solo los campos que envies (nombre, descripcion, url_inicio, estado, contenedor, paginacion). 'dron' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `dron` | string | SI |  |
| `nombre` | string |  |  |
| `descripcion` | string |  |  |
| `url_inicio` | string |  |  |
| `estado` | string (activo / inactivo / error) |  |  |
| `contenedor` | string |  |  |
| `pagina_var` | string |  |  |
| `pagina_desde` | integer |  |  |
| `pagina_hasta` | integer |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "editar_dron",
  "arguments": {
    "dron": "<dron>"
  }
}
```

### `agregar_paso`

**Tipo:** Escritura

> Agrega un paso al final (o en 'orden') de un dron. 'tipo' define que hace el paso: navegar (ir a una URL, admite {{VAR}}), esperar, clic, inyectar_script, extraer (JS que devuelve filas + mapeo), captura, ia (un agente maneja el navegador segun 'instruccion', acotado por topes), leer_correo_otp, procesar_archivos, ingesta_newton, cerrar_navegador, crear_actividad, notificar, ensamblado (invoca una Regla), ejecutar_sql, api, tabla, variable. La config fina de cada tipo va en 'mapeo_json' (ver la pantalla /extraccion-datos). Pasa solo los campos que apliquen al tipo. Devuelve 'paso_id'. 'dron' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `dron` | string | SI |  |
| `tipo` | string (navegar / esperar / clic / inyectar_script / extraer / captura / ia / leer_correo_otp / procesar_archivos / ingesta_newton / cerrar_navegador / crear_actividad / notificar / ensamblado / ejecutar_sql / api / tabla / variable) | SI |  |
| `nombre` | string | SI |  |
| `orden` | integer |  | posicion (si falta, al final) |
| `espera_ms` | integer |  |  |
| `url` | string |  |  |
| `script` | string |  |  |
| `selector` | string |  |  |
| `instruccion` | string |  | para el paso tipo ia |
| `contenedor_destino` | string |  | id o nombre de la tabla destino de ESTE paso |
| `mapeo_json` | string |  | config fina del paso en JSON (segun el tipo) |
| `out_var` | string |  |  |
| `recorre_var` | string |  | variable de lista que recorre el bucle |
| `fin_bucle` | boolean |  |  |
| `pagina_var` | string |  |  |
| `pagina_siguiente` | boolean |  |  |
| `ingesta_ruta` | string |  |  |
| `ingesta_llave` | string |  |  |
| `regla` | string |  | id o etiqueta de la regla (paso ensamblado) |
| `regla_input_var` | string |  |  |
| `ia_proveedor_id` | string |  |  |
| `ia_modelo` | string |  |  |
| `tope_pasos` | integer |  |  |
| `tope_segundos` | integer |  |  |
| `aviso_etiqueta` | string |  |  |
| `aviso_accion` | string (ninguna / notificar / detener) |  |  |
| `tool_allowlist_json` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "agregar_paso",
  "arguments": {
    "dron": "<dron>",
    "tipo": "navegar",
    "nombre": "<nombre>"
  }
}
```

Ejemplo real (valores genericos):

```json
{
  "name": "agregar_paso",
  "arguments": {
    "dron": "Convocatorias portal X",
    "tipo": "extraer",
    "nombre": "Extraer filas",
    "script": "var rows=[]; document.querySelectorAll('table tr').forEach(function(tr){var td=tr.querySelectorAll('td'); if(td.length<2)return; rows.push({titulo:td[0].innerText.trim(), url:(tr.querySelector('a')||{}).href||''});}); return rows;",
    "contenedor_destino": "Convocatorias",
    "mapeo_json": "{\"titulo\":\"titulo\",\"url\":\"url\"}",
    "ingesta_llave": "url"
  }
}
```

### `editar_paso`

**Tipo:** Escritura

> Edita un paso existente: cambia solo los campos que envies (los demas se conservan). 'paso_id' de ver_dron. Puedes cambiar el 'tipo'. Para los campos que admite ver agregar_paso.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `paso_id` | string | SI |  |
| `tipo` | string |  |  |
| `nombre` | string |  |  |
| `orden` | integer |  |  |
| `espera_ms` | integer |  |  |
| `url` | string |  |  |
| `script` | string |  |  |
| `selector` | string |  |  |
| `instruccion` | string |  |  |
| `contenedor_destino` | string |  |  |
| `mapeo_json` | string |  |  |
| `out_var` | string |  |  |
| `recorre_var` | string |  |  |
| `fin_bucle` | boolean |  |  |
| `pagina_var` | string |  |  |
| `pagina_siguiente` | boolean |  |  |
| `ingesta_ruta` | string |  |  |
| `ingesta_llave` | string |  |  |
| `regla` | string |  |  |
| `regla_input_var` | string |  |  |
| `ia_proveedor_id` | string |  |  |
| `ia_modelo` | string |  |  |
| `tope_pasos` | integer |  |  |
| `tope_segundos` | integer |  |  |
| `aviso_etiqueta` | string |  |  |
| `aviso_accion` | string (ninguna / notificar / detener) |  |  |
| `tool_allowlist_json` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "editar_paso",
  "arguments": {
    "paso_id": "<paso_id>"
  }
}
```

### `reordenar_pasos`

**Tipo:** Escritura

> Reordena los pasos de un dron segun la lista de ids que envies (de primero a ultimo). Deben ser TODOS los pasos del dron. 'dron' = id o nombre; 'pasos' = lista de paso_id (ver ver_dron).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `dron` | string | SI |  |
| `pasos` | array de string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "reordenar_pasos",
  "arguments": {
    "dron": "<dron>",
    "pasos": [
      "<pasos>"
    ]
  }
}
```

### `definir_variable`

**Tipo:** Escritura

> Crea o actualiza (por nombre) una variable del dron. 'secreta'=true la cifra y no se podra volver a leer en claro. 'pedir_al_correr'=true hace que se pregunte su valor en cada corrida (con 'prompt'). Si editas una secreta sin 'valor', conserva el valor actual. 'dron' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `dron` | string | SI |  |
| `nombre` | string | SI |  |
| `valor` | string |  |  |
| `secreta` | boolean |  |  |
| `pedir_al_correr` | boolean |  |  |
| `prompt` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "definir_variable",
  "arguments": {
    "dron": "<dron>",
    "nombre": "<nombre>"
  }
}
```

### `guardar_programacion`

**Tipo:** Escritura

> Crea (sin 'id') o actualiza una programacion del dron. 'tipo': manual (solo bajo demanda), intervalo (cada 'cada_minutos') o cron ('cron' de 5 campos en hora del tenant). 'activa' la enciende. Opcional 'tramos' = barrido por tramos (ADR-0171): corre el dron varias veces seguidas moviendo dos variables (p.ej. DIAS_DESDE/DIAS_ATRAS). Devuelve id y proxima corrida. 'dron' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `dron` | string | SI |  |
| `id` | string |  | para editar una existente |
| `nombre` | string |  |  |
| `tipo` | string (manual / intervalo / cron) | SI |  |
| `cada_minutos` | integer >=1 |  |  |
| `cron` | string |  |  |
| `activa` | boolean | SI |  |
| `tramos` | object |  |  |
| `tramos.var_desde` | string |  |  |
| `tramos.var_largo` | string |  |  |
| `tramos.desde` | integer |  |  |
| `tramos.hasta` | integer |  |  |
| `tramos.paso` | integer |  |  |
| `tramos.pausa_min` | integer |  |  |
| `tramos.reintento_min` | integer |  |  |
| `tramos.reintentos` | integer |  |  |
| `tramos.max_horas` | integer |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "guardar_programacion",
  "arguments": {
    "dron": "<dron>",
    "tipo": "manual",
    "activa": true
  }
}
```

Ejemplo real (valores genericos):

```json
{
  "name": "guardar_programacion",
  "arguments": {
    "dron": "Convocatorias portal X",
    "nombre": "Diaria 7am",
    "tipo": "cron",
    "cron": "0 7 * * *",
    "activa": true
  }
}
```

### `eliminar_paso`

**Tipo:** DESTRUCTIVA (confirma con el usuario)

> Borra un paso de un dron. 'paso_id' de ver_dron. Confirma con el usuario antes de usarla.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `paso_id` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "eliminar_paso",
  "arguments": {
    "paso_id": "<paso_id>"
  }
}
```

### `eliminar_variable`

**Tipo:** DESTRUCTIVA (confirma con el usuario)

> Borra una variable de un dron. 'variable_id' de ver_dron. Confirma con el usuario antes de usarla.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `variable_id` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "eliminar_variable",
  "arguments": {
    "variable_id": "<variable_id>"
  }
}
```

### `eliminar_programacion`

**Tipo:** DESTRUCTIVA (confirma con el usuario)

> Borra una programacion de un dron. 'programacion_id' de ver_dron o listar_programaciones. Confirma con el usuario antes de usarla.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `programacion_id` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "eliminar_programacion",
  "arguments": {
    "programacion_id": "<programacion_id>"
  }
}
```

### `eliminar_dron`

**Tipo:** DESTRUCTIVA (confirma con el usuario)

> Borra un dron completo (cabecera, pasos, variables y programaciones). 'dron' = id o nombre. Es destructiva: confirma con el usuario antes de usarla.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `dron` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "eliminar_dron",
  "arguments": {
    "dron": "<dron>"
  }
}
```
