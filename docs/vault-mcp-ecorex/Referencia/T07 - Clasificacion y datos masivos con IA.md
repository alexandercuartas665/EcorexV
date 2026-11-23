---
tipo: referencia
servidor: ecorex-tableros
modulo: clasificacion
herramientas: 6
fuente: catalogo vivo tools/list (generado)
---

# MCP Tableros - Clasificacion y datos masivos con IA

Servidor: `/api/mcp/tableros` - ver [[01 - Conectar los MCP]]. Volver a [[00 - LEEME PRIMERO]].

## Para que sirve

Enriquecer filas de un contenedor con IA (ej. poner `sector` a miles de procesos) y operaciones masivas sobre
columnas.

Patron de la **marca**: una columna (ej. `clasificado_en`) vacia = fila pendiente. La IA solo toca filas con la
marca vacia y, al escribir el resultado, pone la marca con fecha UTC. Asi todo es incremental e idempotente.

- `clasificar_lote`: un lote manual (max 50 filas). Repetir hasta `restantes` = 0.
- `configurar_paso_clasificacion`: lo vuelve AUTOMATICO como paso de una programacion de importacion (tras cada
  corrida OK clasifica lo nuevo, hasta `max_lotes`).
- `ejecutar_clasificacion_ahora`: encola el paso ya (asincrono; el resultado sale en `listar_corridas_importacion`).
- `limpiar_marca`: pone la marca (y opcionalmente salidas) en NULL para reclasificar todo. **DESTRUCTIVA**.
- `rellenar_columna`: backfill por plantilla `{col}` (ej. `{numero_proceso}|{url}`) sobre filas existentes.

Todas consumen tokens de IA (salvo rellenar/limpiar): confirma con el usuario volumenes grandes.

Recetas: [[R10 - Clasificar datos con IA]]

## Herramientas (6)

- **Lectura:** 
- **Escritura:** `clasificar_lote`, `rellenar_columna`, `configurar_paso_clasificacion`, `quitar_paso_clasificacion`, `ejecutar_clasificacion_ahora`
- **Destructivas:** `limpiar_marca`

### `clasificar_lote`

**Tipo:** Escritura

> Clasifica un LOTE de filas de una tabla con un agente de IA y escribe el resultado en la misma fila. Toma hasta 'limite' filas (max 50) cuya 'columna_marca' este vacia, le envia al agente las 'columnas_entrada' (por defecto todas menos la marca y las de salida), y por cada fila que el agente devuelva (identificada por 'columna_clave') escribe SOLO las 'columnas_salida' y pone la marca con la fecha UTC. Devuelve procesados, actualizados, sin_respuesta y 'restantes': repitela hasta que restantes sea 0. El agente debe estar ENCENDIDO. Consume tokens de IA. 'contenedor' y 'tabla' = id o nombre; 'agente' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `contenedor` | string | SI | id o nombre del contenedor |
| `tabla` | string | SI | id o nombre de la tabla |
| `agente` | string | SI | id o nombre del agente de IA que clasifica |
| `columna_clave` | string | SI | columna que identifica la fila (ej. id_proceso) |
| `columna_marca` | string | SI | columna que marca la fila como clasificada (ej. clasificado_en); vacia = pendiente |
| `columnas_salida` | array de string | SI | columnas que el agente puede escribir (ej. ["sector"]) |
| `columnas_entrada` | array de string |  | opcional: columnas que se le envian al agente |
| `limite` | integer >=1 <=50 |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "clasificar_lote",
  "arguments": {
    "contenedor": "<contenedor>",
    "tabla": "<tabla>",
    "agente": "<agente>",
    "columna_clave": "<columna_clave>",
    "columna_marca": "<columna_marca>",
    "columnas_salida": [
      "<columnas_salida>"
    ]
  }
}
```

Ejemplo real (valores genericos):

```json
{
  "name": "clasificar_lote",
  "arguments": {
    "contenedor": "Ventas",
    "tabla": "Pedidos",
    "agente": "Clasificador de pedidos",
    "columna_clave": "id_pedido",
    "columna_marca": "clasificado_en",
    "columnas_salida": [
      "categoria"
    ],
    "columnas_entrada": [
      "cliente",
      "descripcion"
    ],
    "limite": 50
  }
}
```

### `limpiar_marca`

**Tipo:** DESTRUCTIVA (confirma con el usuario)

> VACIA la columna marca (y opcionalmente las columnas de salida) de las filas de una tabla para que se vuelvan a clasificar (ej. al cambiar la taxonomia). Sin 'filtro' afecta TODAS las filas; con 'filtro' ({columna: valor}, igualdad exacta, todas las condiciones) solo las que coinciden. No borra filas ni columnas. Confirma con el usuario antes de usarla: reclasificar consume tokens de IA.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `contenedor` | string | SI | id o nombre del contenedor |
| `tabla` | string | SI | id o nombre de la tabla |
| `columna_marca` | string | SI | columna marca a vaciar (ej. clasificado_en) |
| `columnas_salida` | array de string |  | opcional: columnas de salida a vaciar tambien (ej. ["sector"]) |
| `filtro` | object |  | opcional: {columna: valor} para limpiar solo esas filas |

Llamada minima (solo obligatorios):

```json
{
  "name": "limpiar_marca",
  "arguments": {
    "contenedor": "<contenedor>",
    "tabla": "<tabla>",
    "columna_marca": "<columna_marca>"
  }
}
```

### `rellenar_columna`

**Tipo:** Escritura

> RELLENA una columna de las filas existentes de una tabla calculando su valor desde otras columnas de la MISMA fila con una 'plantilla' (cada {columna} se reemplaza por su valor; ej. "{numero_proceso}|{url}" para una clave de ingesta compuesta). Por defecto solo toca filas con la columna destino VACIA (repetible sin efectos); con solo_vacias=false sobrescribe. Ignora filas sin ningun valor de origen. No borra filas ni toca otras columnas. Hasta 20000 filas por llamada en una transaccion; si 'restantes' > 0, repitela.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `contenedor` | string | SI | id o nombre del contenedor |
| `tabla` | string | SI | id o nombre de la tabla |
| `columna` | string | SI | columna destino a rellenar (ej. clave_ingesta) |
| `plantilla` | string | SI | texto con {columna} por cada valor, ej. "{numero_proceso}/{url}" |
| `solo_vacias` | boolean |  | true (defecto) = solo filas con la columna vacia; false = sobrescribe |

Llamada minima (solo obligatorios):

```json
{
  "name": "rellenar_columna",
  "arguments": {
    "contenedor": "<contenedor>",
    "tabla": "<tabla>",
    "columna": "<columna>",
    "plantilla": "<plantilla>"
  }
}
```

Ejemplo real (valores genericos):

```json
{
  "name": "rellenar_columna",
  "arguments": {
    "contenedor": "Ventas",
    "tabla": "Pedidos",
    "columna": "clave_ingesta",
    "plantilla": "{id_pedido}|{cliente}",
    "solo_vacias": true
  }
}
```

### `configurar_paso_clasificacion`

**Tipo:** Escritura

> Agrega (o reemplaza) un PASO DE CLASIFICACION con IA a una programacion de importacion: despues de cada corrida exitosa, el sistema clasifica automaticamente las filas NUEVAS (las que tienen la 'columna_marca' vacia) lote a lote con el agente, hasta no quedar pendientes o llegar a 'max_lotes' (default 20) o al cupo de IA del plan; lo que falte queda para la siguiente corrida. 'tabla' por defecto = tabla destino del conector (obligatoria si la programacion es de un flujo de extraccion). El agente debe estar ENCENDIDO para que clasifique. 'contenedor' = id o nombre; 'programacion' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `contenedor` | string | SI | id o nombre del contenedor |
| `programacion` | string | SI | id o nombre de la programacion de importacion (o id de la programacion de un flujo) |
| `tabla` | string |  | opcional: id o nombre de la tabla a clasificar |
| `agente` | string | SI | id o nombre del agente de IA |
| `columna_clave` | string | SI |  |
| `columna_marca` | string | SI |  |
| `columnas_salida` | array de string | SI |  |
| `columnas_entrada` | array de string |  |  |
| `limite` | integer >=1 <=50 |  | filas por lote (default 50) |
| `max_lotes` | integer >=1 <=200 |  | tope de lotes por corrida (default 20) |

Llamada minima (solo obligatorios):

```json
{
  "name": "configurar_paso_clasificacion",
  "arguments": {
    "contenedor": "<contenedor>",
    "programacion": "<programacion>",
    "agente": "<agente>",
    "columna_clave": "<columna_clave>",
    "columna_marca": "<columna_marca>",
    "columnas_salida": [
      "<columnas_salida>"
    ]
  }
}
```

Ejemplo real (valores genericos):

```json
{
  "name": "configurar_paso_clasificacion",
  "arguments": {
    "contenedor": "Ventas",
    "programacion": "Pedidos diaria 5am",
    "agente": "Clasificador de pedidos",
    "columna_clave": "id_pedido",
    "columna_marca": "clasificado_en",
    "columnas_salida": [
      "categoria"
    ],
    "columnas_entrada": [
      "cliente",
      "descripcion"
    ],
    "limite": 50,
    "max_lotes": 20
  }
}
```

### `quitar_paso_clasificacion`

**Tipo:** Escritura

> Quita el paso de clasificacion con IA de una programacion de importacion (la importacion sigue igual). 'contenedor' = id o nombre; 'programacion' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `contenedor` | string | SI |  |
| `programacion` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "quitar_paso_clasificacion",
  "arguments": {
    "contenedor": "<contenedor>",
    "programacion": "<programacion>"
  }
}
```

### `ejecutar_clasificacion_ahora`

**Tipo:** Escritura

> Pide correr YA el paso de clasificacion de una programacion (sin esperar a la siguiente importacion): el motor lo toma en menos de un minuto y deja el resultado (lotes, clasificadas, pendientes y por que paro) en el detalle de la ultima corrida (listar_corridas_importacion). Consume tokens de IA. 'contenedor' = id o nombre; 'programacion' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `contenedor` | string | SI |  |
| `programacion` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "ejecutar_clasificacion_ahora",
  "arguments": {
    "contenedor": "<contenedor>",
    "programacion": "<programacion>"
  }
}
```
