---
tipo: referencia
servidor: ecorex-tableros
modulo: reportes
herramientas: 13
fuente: catalogo vivo tools/list (generado)
---

# MCP Tableros - Reportes e indicadores

Servidor: `/api/mcp/tableros` - ver [[01 - Conectar los MCP]]. Volver a [[00 - LEEME PRIMERO]].

## Para que sirve

Dos formas de visualizar datos:
1. **Galeria de reportes** (`crear_reporte`): un PANEL guardado (tabla, barras, torta, linea) que aparece en la
   pestana de su `categoria`.
2. **Libros de indicadores** (`crear_libro_indicadores` + `agregar_indicador`): un tablero tipo dashboard con hojas y
   widgets (kpi, barras, barras_horizontales, dona, embudo, treemap, linea, area, tabla) en una rejilla de 4 columnas.

Fuentes (`listar_fuentes_reporte`): nativas (`native:taskitem` actividades, `native:tercero` directorio,
`native:worklog` horas...), **cada tabla de contenedor** (`container:{tabla_id}`) y externas. Usa `ver_fuente_reporte`
para ver campos y que se puede agrupar/sumar.

Reglas:
- Ambos EJECUTAN la consulta antes de guardar: si un campo no existe o no es sumable, no guardan y explican.
- `sumar/promedio/minimo/maximo` exigen campo `numero`/`decimal`.
- Ordenar por una agregacion: `orden=[{campo:"conteo"|"suma"|"promedio"..., desc:true}]`.
- Los `filtros` quedan FIJOS en el reporte (no son interactivos). Para "solo activos" crea una version filtrada.
- `ver_reporte` re-ejecuta y da datos frescos (sirve para monitorear).
- **Por periodo**: `consulta.periodo` = dia | semana | mes | trimestre | anio agrupa un campo FECHA (claves
  2026-01-15 / 2026-W03 / 2026-01 / 2026-T1 / 2026, orden cronologico). Solo con `agrupar_por` de tipo fecha.
- `editar_reporte` cambia solo lo que envies (si cambia grafico o consulta, la ejecuta antes de guardar);
  `archivar_reporte` lo saca de la galeria (recuperable desde 'Archivados').

Recetas: [[R07 - Crear reportes e indicadores]]

## Herramientas (13)

- **Lectura:** `listar_fuentes_reporte`, `ver_fuente_reporte`, `listar_reportes`, `ver_reporte`, `listar_libros_indicadores`, `ver_libro_indicadores`
- **Escritura:** `crear_reporte`, `crear_libro_indicadores`, `agregar_indicador`, `editar_reporte`
- **Destructivas:** `archivar_reporte`, `eliminar_indicador`, `eliminar_libro_indicadores`

### `listar_fuentes_reporte`

**Tipo:** Lectura

> Lista las FUENTES de datos sobre las que se pueden crear reportes e indicadores: tablas de contenedores de datos (una fuente por tabla, con su contenedor), entidades del sistema (tareas, terceros...) y modulos de formulario. Devuelve la clave 'fuente' y el numero de campos. 'tipo' opcional filtra: contenedor, nativa, formulario, externa.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `tipo` | string (contenedor / nativa / formulario / externa) |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "listar_fuentes_reporte",
  "arguments": {}
}
```

### `ver_fuente_reporte`

**Tipo:** Lectura

> Muestra los CAMPOS de una fuente: nombre, tipo (texto/numero/decimal/fecha/booleano) y si admite filtro, agrupacion o suma/promedio (solo numericos). Usala antes de armar una consulta. La fuente se indica con 'fuente' (clave del catalogo, p.ej. container:{id}, o su nombre) o con 'contenedor' + 'tabla' (id o nombre): cada tabla de un contenedor es una fuente.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `fuente` | string |  |  |
| `contenedor` | string |  |  |
| `tabla` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "ver_fuente_reporte",
  "arguments": {}
}
```

### `listar_reportes`

**Tipo:** Lectura

> Lista los reportes (paneles e imprimibles) que el usuario puede ver: id, nombre, tipo, fuente y categoria.

_Sin parametros._

Llamada minima (solo obligatorios):

```json
{
  "name": "listar_reportes",
  "arguments": {}
}
```

### `ver_reporte`

**Tipo:** Lectura

> Ejecuta un reporte guardado y muestra sus columnas, el total de filas y una muestra. 'reporte' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `reporte` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "ver_reporte",
  "arguments": {
    "reporte": "<reporte>"
  }
}
```

### `crear_reporte`

**Tipo:** Escritura

> Crea un reporte tipo PANEL (galeria de reportes) sobre una fuente. 'grafico': tabla (por defecto), barras, torta o linea; barras/torta/linea necesitan 'agrupar_por' en la consulta (si no indicas agregaciones se cuenta). Antes de guardar EJECUTA la consulta: si un campo no existe o no admite la operacion, no guarda nada y explica por que. Devuelve 'reporte_id', el total de filas y una muestra. La fuente se indica con 'fuente' (clave del catalogo, p.ej. container:{id}, o su nombre) o con 'contenedor' + 'tabla' (id o nombre): cada tabla de un contenedor es una fuente. Los imprimibles se disenian en pantalla.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nombre` | string | SI |  |
| `descripcion` | string |  |  |
| `categoria` | string |  |  |
| `grafico` | string (tabla / barras / torta / linea) |  |  |
| `fuente` | string |  |  |
| `contenedor` | string |  |  |
| `tabla` | string |  |  |
| `consulta` | object | SI | consulta sobre la fuente, con NOMBRES de campo (ver_fuente_reporte) |
| `consulta.campos` | array de string |  | modo tabla: campos a mostrar (vacio = todos) |
| `consulta.filtros` | array de objetos |  |  |
| `consulta.filtros[].campo` | string | SI |  |
| `consulta.filtros[].operador` | string (igual / distinto / contiene / mayor / mayor_igual / menor / menor_igual / entre / en) |  |  |
| `consulta.filtros[].valor` | string |  |  |
| `consulta.filtros[].valores` | array de string |  |  |
| `consulta.agrupar_por` | string |  | campo por el que se agrupa (un contenedor admite uno) |
| `consulta.periodo` | string (dia / semana / mes / trimestre / anio) |  | si agrupar_por es un campo FECHA, agrupa por ese periodo (ventas por mes) en orden cronologico |
| `consulta.agregaciones` | array de objetos |  |  |
| `consulta.agregaciones[].funcion` | string (contar / sumar / promedio / minimo / maximo) | SI |  |
| `consulta.agregaciones[].campo` | string |  | campo numerico (no hace falta para contar) |
| `consulta.orden` | array de objetos |  |  |
| `consulta.orden[].campo` | string | SI | un campo, o conteo/suma/promedio/minimo/maximo para ordenar por la agregacion |
| `consulta.orden[].desc` | boolean |  |  |
| `consulta.limite` | integer >=1 |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "crear_reporte",
  "arguments": {
    "nombre": "<nombre>",
    "consulta": {}
  }
}
```

Ejemplo real (valores genericos):

```json
{
  "name": "crear_reporte",
  "arguments": {
    "nombre": "Ventas por categoria",
    "categoria": "Comercial",
    "grafico": "barras",
    "contenedor": "Ventas",
    "tabla": "Pedidos",
    "consulta": {
      "agrupar_por": "categoria",
      "agregaciones": [
        {
          "funcion": "sumar",
          "campo": "valor"
        }
      ],
      "orden": [
        {
          "campo": "suma",
          "desc": true
        }
      ]
    }
  }
}
```

### `listar_libros_indicadores`

**Tipo:** Lectura

> Lista los libros de indicadores que el usuario puede ver: id, nombre, visibilidad, si es dueno, hojas y widgets.

_Sin parametros._

Llamada minima (solo obligatorios):

```json
{
  "name": "listar_libros_indicadores",
  "arguments": {}
}
```

### `ver_libro_indicadores`

**Tipo:** Lectura

> Muestra un libro de indicadores: sus hojas y los widgets de cada una (id, tipo, titulo, fuente, ancho). 'libro' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `libro` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "ver_libro_indicadores",
  "arguments": {
    "libro": "<libro>"
  }
}
```

### `crear_libro_indicadores`

**Tipo:** Escritura

> Crea un libro de indicadores NUEVO (privado del usuario, nace con la 'Hoja 1'). Devuelve 'libro_id' y 'hoja_id'.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nombre` | string | SI |  |
| `descripcion` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "crear_libro_indicadores",
  "arguments": {
    "nombre": "<nombre>"
  }
}
```

### `agregar_indicador`

**Tipo:** Escritura

> Agrega un indicador (widget) a una hoja de un libro. 'tipo': kpi (un numero: una agregacion sin agrupar), barras, barras_horizontales, dona, embudo, treemap (agrupan por un campo), linea, area o tabla. 'hoja' = id o nombre (por defecto la primera; si el nombre no existe se crea). 'ancho' = 1..4 columnas. Antes de guardar EJECUTA el widget: si no corre, no guarda nada y explica por que. Devuelve 'widget_id' y una muestra del resultado. La fuente se indica con 'fuente' (clave del catalogo, p.ej. container:{id}, o su nombre) o con 'contenedor' + 'tabla' (id o nombre): cada tabla de un contenedor es una fuente.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `libro` | string | SI |  |
| `hoja` | string |  |  |
| `titulo` | string | SI |  |
| `tipo` | string (kpi / barras / barras_horizontales / dona / embudo / treemap / linea / area / tabla) | SI |  |
| `ancho` | integer >=1 <=4 |  |  |
| `fuente` | string |  |  |
| `contenedor` | string |  |  |
| `tabla` | string |  |  |
| `consulta` | object | SI | consulta sobre la fuente, con NOMBRES de campo (ver_fuente_reporte) |
| `consulta.campos` | array de string |  | modo tabla: campos a mostrar (vacio = todos) |
| `consulta.filtros` | array de objetos |  |  |
| `consulta.filtros[].campo` | string | SI |  |
| `consulta.filtros[].operador` | string (igual / distinto / contiene / mayor / mayor_igual / menor / menor_igual / entre / en) |  |  |
| `consulta.filtros[].valor` | string |  |  |
| `consulta.filtros[].valores` | array de string |  |  |
| `consulta.agrupar_por` | string |  | campo por el que se agrupa (un contenedor admite uno) |
| `consulta.periodo` | string (dia / semana / mes / trimestre / anio) |  | si agrupar_por es un campo FECHA, agrupa por ese periodo (ventas por mes) en orden cronologico |
| `consulta.agregaciones` | array de objetos |  |  |
| `consulta.agregaciones[].funcion` | string (contar / sumar / promedio / minimo / maximo) | SI |  |
| `consulta.agregaciones[].campo` | string |  | campo numerico (no hace falta para contar) |
| `consulta.orden` | array de objetos |  |  |
| `consulta.orden[].campo` | string | SI | un campo, o conteo/suma/promedio/minimo/maximo para ordenar por la agregacion |
| `consulta.orden[].desc` | boolean |  |  |
| `consulta.limite` | integer >=1 |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "agregar_indicador",
  "arguments": {
    "libro": "<libro>",
    "titulo": "<titulo>",
    "tipo": "kpi",
    "consulta": {}
  }
}
```

Ejemplo real (valores genericos):

```json
{
  "name": "agregar_indicador",
  "arguments": {
    "libro": "Tablero comercial",
    "titulo": "Pedidos abiertos",
    "tipo": "kpi",
    "ancho": 1,
    "contenedor": "Ventas",
    "tabla": "Pedidos",
    "consulta": {
      "filtros": [
        {
          "campo": "estado",
          "operador": "en",
          "valores": [
            "Abierto",
            "Pendiente"
          ]
        }
      ],
      "agregaciones": [
        {
          "funcion": "contar"
        }
      ]
    }
  }
}
```

### `editar_reporte`

**Tipo:** Escritura

> Edita un reporte tipo PANEL creado con crear_reporte. Cambio PARCIAL: solo lo que mandes (nombre, categoria, descripcion, grafico y/o consulta; la fuente solo si mandas consulta). Si cambias grafico o consulta se EJECUTA antes de guardar, igual que al crear. 'reporte' = id o nombre. Los imprimibles y los paneles disenados en pantalla se editan alli.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `reporte` | string | SI |  |
| `nombre` | string |  |  |
| `descripcion` | string |  |  |
| `categoria` | string |  |  |
| `grafico` | string (tabla / barras / torta / linea) |  |  |
| `fuente` | string |  |  |
| `contenedor` | string |  |  |
| `tabla` | string |  |  |
| `consulta` | object |  | consulta sobre la fuente, con NOMBRES de campo (ver_fuente_reporte) |
| `consulta.campos` | array de string |  | modo tabla: campos a mostrar (vacio = todos) |
| `consulta.filtros` | array de objetos |  |  |
| `consulta.filtros[].campo` | string | SI |  |
| `consulta.filtros[].operador` | string (igual / distinto / contiene / mayor / mayor_igual / menor / menor_igual / entre / en) |  |  |
| `consulta.filtros[].valor` | string |  |  |
| `consulta.filtros[].valores` | array de string |  |  |
| `consulta.agrupar_por` | string |  | campo por el que se agrupa (un contenedor admite uno) |
| `consulta.periodo` | string (dia / semana / mes / trimestre / anio) |  | si agrupar_por es un campo FECHA, agrupa por ese periodo (ventas por mes) en orden cronologico |
| `consulta.agregaciones` | array de objetos |  |  |
| `consulta.agregaciones[].funcion` | string (contar / sumar / promedio / minimo / maximo) | SI |  |
| `consulta.agregaciones[].campo` | string |  | campo numerico (no hace falta para contar) |
| `consulta.orden` | array de objetos |  |  |
| `consulta.orden[].campo` | string | SI | un campo, o conteo/suma/promedio/minimo/maximo para ordenar por la agregacion |
| `consulta.orden[].desc` | boolean |  |  |
| `consulta.limite` | integer >=1 |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "editar_reporte",
  "arguments": {
    "reporte": "<reporte>"
  }
}
```

Ejemplo real (valores genericos):

```json
{
  "name": "editar_reporte",
  "arguments": {
    "reporte": "Ventas por mes",
    "consulta": {
      "agrupar_por": "fecha",
      "periodo": "trimestre",
      "agregaciones": [
        {
          "funcion": "sumar",
          "campo": "valor"
        }
      ]
    }
  }
}
```

### `archivar_reporte`

**Tipo:** DESTRUCTIVA (confirma con el usuario)

> ARCHIVA un reporte (sale de la galeria; un administrador lo recupera desde 'Archivados'). Usala para quitar reportes de prueba o que quedaron sin fuente. 'reporte' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `reporte` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "archivar_reporte",
  "arguments": {
    "reporte": "<reporte>"
  }
}
```

### `eliminar_indicador`

**Tipo:** DESTRUCTIVA (confirma con el usuario)

> Quita un indicador (widget) de su libro. 'indicador' = widget_id (ver_libro_indicadores). Solo el dueno del libro o un administrador.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `indicador` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "eliminar_indicador",
  "arguments": {
    "indicador": "<indicador>"
  }
}
```

### `eliminar_libro_indicadores`

**Tipo:** DESTRUCTIVA (confirma con el usuario)

> Elimina un libro de indicadores completo (con sus hojas). 'libro' = id o nombre. Solo el dueno o un administrador.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `libro` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "eliminar_libro_indicadores",
  "arguments": {
    "libro": "<libro>"
  }
}
```
