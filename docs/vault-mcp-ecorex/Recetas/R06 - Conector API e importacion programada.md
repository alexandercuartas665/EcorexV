---
tipo: receta
modulo: contenedores
referencia: "[[T05 - Contenedores de datos]]"
---

# R06 - Conector API (o BD) e importacion programada

Volver a [[00 - LEEME PRIMERO]]. Requisito: la tabla destino ya existe ([[R05 - Crear un contenedor de datos]]).
Referencia: [[T05 - Contenedores de datos]].

Un **conector** dice DE DONDE vienen los datos y como se mapean a columnas. Una **programacion de importacion**
dice CUANDO corre y en que MODO (append / replace / upsert).

## Paso 1 - Entender la fuente (antes de configurar nada)

- Prueba la URL a mano (navegador o `curl`) y mira la forma del JSON: donde esta el **arreglo** de filas, como se
  llaman los campos, si hay **paginacion** (offset/limit, page) y cuanto tarda.
- Cuenta cuantas filas hay en la ventana que vas a traer (muchas APIs tienen un "count").
- Identifica el **id unico** de cada fila (para upsert).

## Paso 2 - El `mapeo_json` (formato exacto)

```json
{
  "arrayPath": "data.items",
  "paging": {
    "mode": "Offset",
    "offsetParam": "offset",
    "limitParam": "limit",
    "pageSize": 500,
    "maxPages": 20
  },
  "fields": [
    {"column": "id_pedido",   "path": "id"},
    {"column": "nit_cliente", "path": "customer.taxId"},
    {"column": "fecha",       "path": "createdAt"},
    {"column": "estado",      "path": "status"},
    {"column": "descripcion", "path": "notes"},
    {"column": "valor",       "path": "total"}
  ]
}
```

| Clave | Significado |
|---|---|
| `arrayPath` | Ruta con puntos al arreglo de filas en la respuesta. Vacio/omitido = la respuesta ES el arreglo |
| `paging.mode` | `None` (una sola llamada), `Offset` (desplazamiento), `Page` (numero de pagina) |
| `offsetParam` / `pageParam` / `limitParam` | Nombres de los parametros de la API (defaults: `start`, `page`, `limit`) |
| `startValue` | Valor inicial del offset/pagina (default 0) |
| `pageSize` | Filas por pagina (default 100). Para cuando una pagina trae menos de esto |
| `maxPages` | Tope de paginas (default **1000**: fijalo SIEMPRE) |
| `fields[].column` | Nombre EXACTO de la columna destino |
| `fields[].path` | Ruta al valor dentro de cada fila; anidados con puntos (`customer.name`, `urlproceso.url`) |

El sistema agrega los parametros de paginacion a la URL por ti: el `endpoint` va limpio, solo con sus filtros fijos.

## Paso 3 - Crear el conector

```json
{"name":"guardar_conector","arguments":{
  "contenedor":"Ventas","nombre":"API pedidos","tipo":"api","destino":"Pedidos",
  "endpoint":"https://api.ejemplo.com/v1/pedidos?status=all","metodo":"GET","auth":"bearer",
  "mapeo_json":"{\"arrayPath\":\"data.items\",\"paging\":{\"mode\":\"Offset\",\"offsetParam\":\"offset\",\"limitParam\":\"limit\",\"pageSize\":500,\"maxPages\":20},\"fields\":[{\"column\":\"id_pedido\",\"path\":\"id\"},{\"column\":\"valor\",\"path\":\"total\"}]}",
  "activo":true}}
```

**Autenticacion (`auth`):** `ninguna`, `apikey`, `bearer`, `basic`, `tokenexchange` (login en 2 pasos). **El secreto
NO va por MCP**: dile al usuario que entre a la pantalla del contenedor -> conector -> y pegue ahi la clave/token.
Tambien en pantalla: cabeceras fijas (ej. `Partner-Id`) y la config del token exchange.

**Otros tipos:**
- `bd`: `motor` (postgresql, mysql, sqlserver, oracle, mariadb, sqlite), `host`, `puerto`, `base_datos`, `usuario`,
  `query` (un SELECT). La contrasena en pantalla.
- `excel`: el archivo se sube en pantalla.
- `tabla`: desde otra tabla del mismo contenedor.

Para **editar** un conector:
1. Leelo primero: `ver_conector` devuelve endpoint, metodo, auth, nombres de cabeceras, destino, `mapeo` y
   `mapeo_json` (texto listo para reenviar). Nunca trae secretos (los parametros sensibles salen como `***`).
2. `guardar_conector` con `conector` (id o nombre) y SOLO lo que cambia; lo omitido se conserva. Si cambias el
   mapeo, envia el `mapeo_json` completo (parte del que devolvio `ver_conector`).
3. Vuelve a llamar `ver_conector` para confirmar.

```json
{"name":"ver_conector","arguments":{"contenedor":"Ventas","conector":"API pedidos"}}
{"name":"guardar_conector","arguments":{"contenedor":"Ventas","conector":"API pedidos",
  "endpoint":"https://api.ejemplo.com/v1/pedidos?status=open"}}
```
Ver [[04 - Trampas conocidas]] #14.

## Paso 4 - La programacion (cuando y como)

```json
{"name":"guardar_programacion_importacion","arguments":{
  "contenedor":"Ventas","nombre":"Pedidos diaria 5am","tipo":"cron","cron":"0 5 * * *",
  "modo":"upsert","columna_clave":"id_pedido","conector":"API pedidos","activa":true}}
```

| Modo | Que hace | Cuando |
|---|---|---|
| `append` | Agrega todas las filas | Bitacoras donde cada corrida trae solo lo nuevo |
| `replace` | Borra y recarga la tabla | Catalogos pequenos que vienen completos (confirma: borra) |
| `upsert` | Inserta lo nuevo y actualiza lo existente por `columna_clave` | **Casi siempre.** La clave debe estar en `fields` |

`tipo`: `manual` (solo a mano), `intervalo` (`cada_minutos`), `cron` (5 campos, **hora de la empresa**).
Patron recomendado: una programacion **"Historico manual"** (ventana grande, se corre a mano una vez) y una
**"Recientes diaria"** (cron, ventana corta) sobre la misma tabla, ambas en `upsert`.

## Paso 5 - Correr y validar

- **No hay herramienta MCP para "importar ahora"**: el usuario pulsa **Importar** en la pantalla del contenedor, o
  se espera al cron.
- Revisa el resultado:
```json
{"name":"listar_corridas_importacion","arguments":{"contenedor":"Ventas","proceso":"Pedidos diaria 5am","limite":5}}
```
  `resultado` (ok / error / omitida / corriendo / agente_desconectado), `insertadas`, `actualizadas`, `detalle`.
- **Valida con poco primero:** ventana de 1 dia -> compara `insertadas` con el conteo de la fuente. Si cuadra,
  mapeo + paginacion + ventana estan bien; entonces ensancha.
- Mira los datos con un reporte en modo tabla ([[R07 - Crear reportes e indicadores]]).

## Problemas tipicos

| Sintoma | Arreglo |
|---|---|
| "Tiempo de espera agotado al llamar al API" | Baja `pageSize`/`maxPages`; consulta mas angosta (trae lo reciente ordenado por fecha + limite) |
| "Se alcanzo el limite de 50000 filas por corrida" | Parte por ventanas de fecha |
| 0 filas con resultado ok | `arrayPath` mal, ventana vacia o filtro que no casa: prueba la URL a mano |
| Duplicados | Usa `upsert` con `columna_clave` mapeada |
| URL muy larga | Maximo 1.000 caracteres: simplifica el filtro |

Siguiente paso natural: clasificar lo importado ([[R10 - Clasificar datos con IA]]) y reportarlo
([[R07 - Crear reportes e indicadores]]). Caso real completo: [[R11 - Caso completo de punta a punta]].
