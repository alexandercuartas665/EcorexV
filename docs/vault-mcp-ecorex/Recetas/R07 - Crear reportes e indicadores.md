---
tipo: receta
modulo: reportes
referencia: "[[T08 - Reportes e indicadores]]"
---

# R07 - Crear reportes e indicadores

Volver a [[00 - LEEME PRIMERO]]. Referencia: [[T08 - Reportes e indicadores]].

## Dos formas (puedes usar ambas sobre la misma fuente)

| | Galeria de reportes | Libro de indicadores |
|---|---|---|
| Herramientas | `crear_reporte` | `crear_libro_indicadores` + `agregar_indicador` |
| Que es | Un panel guardado (una consulta + un grafico) | Un dashboard con hojas y varios widgets |
| Graficos | tabla, barras, torta, linea | kpi, barras, barras_horizontales, dona, embudo, treemap, linea, area, tabla |
| Donde aparece | Galeria, pestana de su `categoria` | Libros de indicadores (privado del creador) |
| Ideal para | Listados y analisis puntuales para compartir | Tablero gerencial con KPIs |

## Paso 1 - Elegir la fuente y conocer sus campos

```json
{"name":"listar_fuentes_reporte","arguments":{}}
```
Fuentes: `native:taskitem` (actividades/tareas), `native:tercero` (directorio), `native:tercero-nota` (bitacora del
directorio), `native:worklog` (horas), `native:externalsource`..., **cada tabla de contenedor** como
`container:{tabla_id}` y fuentes externas `external:{id}`.

```json
{"name":"ver_fuente_reporte","arguments":{"contenedor":"Ventas","tabla":"Pedidos"}}
```
Devuelve cada campo con `tipo`, `filtrable`, `agrupable`, `sumable`. **Solo los campos `numero`/`decimal` son
sumables**; si un monto quedo como texto, cambialo con `editar_columna` (tipo `decimal`).

La fuente se indica con `fuente` (clave del catalogo) **o** con `contenedor` + `tabla` (id o nombre).

## Paso 2 - La `consulta` (igual para reportes e indicadores)

```json
{
  "campos": ["fecha", "nit_cliente", "valor"],
  "filtros": [
    {"campo": "estado", "operador": "en", "valores": ["Abierto", "Pendiente"]},
    {"campo": "valor", "operador": "mayor", "valor": "1000000"}
  ],
  "agrupar_por": "categoria",
  "agregaciones": [{"funcion": "contar"}, {"funcion": "sumar", "campo": "valor"}],
  "orden": [{"campo": "suma", "desc": true}],
  "limite": 20
}
```
- **Modo tabla (listado):** usa `campos` (vacio = todos) y opcional `orden` por un campo y `limite`.
- **Modo agregado:** `agrupar_por` (uno solo en contenedores) + `agregaciones` (`contar`, `sumar`, `promedio`,
  `minimo`, `maximo`).
- **Operadores de filtro:** `igual`, `distinto`, `contiene`, `mayor`, `mayor_igual`, `menor`, `menor_igual`,
  `entre`, `en` (lista en `valores`).
- **Ordenar por la agregacion:** `campo` = `conteo` / `suma` / `promedio` / `minimo` / `maximo`.
- Los filtros quedan **fijos** en el reporte. Para variantes ("solo activos", "este ano") crea reportes aparte.

## Paso 3a - Reporte de galeria

```json
{"name":"crear_reporte","arguments":{
  "nombre":"Ventas por categoria","descripcion":"Monto y cantidad de pedidos por categoria",
  "categoria":"Comercial","grafico":"barras","contenedor":"Ventas","tabla":"Pedidos",
  "consulta":{"agrupar_por":"categoria","agregaciones":[{"funcion":"sumar","campo":"valor"}],
              "orden":[{"campo":"suma","desc":true}]}}}
```
Antes de guardar EJECUTA la consulta: si algo no existe o no es sumable, no guarda y explica. Devuelve
`reporte_id`, total de filas y una muestra. `barras/torta/linea` necesitan `agrupar_por`.

Listado (sirve tambien para "ver los datos" de una tabla):
```json
{"name":"crear_reporte","arguments":{"nombre":"Pedidos recientes","categoria":"Comercial","grafico":"tabla",
  "contenedor":"Ventas","tabla":"Pedidos",
  "consulta":{"campos":["fecha","id_pedido","nit_cliente","estado","valor"],"orden":[{"campo":"fecha","desc":true}],"limite":100}}}
```

## Paso 3b - Libro de indicadores (dashboard)

```json
{"name":"crear_libro_indicadores","arguments":{"nombre":"Tablero comercial","descripcion":"KPIs de ventas"}}
```
-> `libro_id` y `hoja_id` (nace con "Hoja 1"). Agrega widgets (`ancho` 1..4 columnas de una rejilla de 4):

```json
{"name":"agregar_indicador","arguments":{"libro":"<libro_id>","titulo":"Pedidos","tipo":"kpi","ancho":1,
  "contenedor":"Ventas","tabla":"Pedidos","consulta":{"agregaciones":[{"funcion":"contar"}]}}}
```
```json
{"name":"agregar_indicador","arguments":{"libro":"<libro_id>","titulo":"Monto total","tipo":"kpi","ancho":1,
  "contenedor":"Ventas","tabla":"Pedidos","consulta":{"agregaciones":[{"funcion":"sumar","campo":"valor"}]}}}
```
```json
{"name":"agregar_indicador","arguments":{"libro":"<libro_id>","titulo":"Monto por categoria","tipo":"barras","ancho":2,
  "contenedor":"Ventas","tabla":"Pedidos",
  "consulta":{"agrupar_por":"categoria","agregaciones":[{"funcion":"sumar","campo":"valor"}],"orden":[{"campo":"suma","desc":true}]}}}
```
```json
{"name":"agregar_indicador","arguments":{"libro":"<libro_id>","titulo":"Resumen por categoria","tipo":"tabla","ancho":4,
  "contenedor":"Ventas","tabla":"Pedidos",
  "consulta":{"agrupar_por":"categoria","agregaciones":[{"funcion":"contar"},{"funcion":"sumar","campo":"valor"},{"funcion":"promedio","campo":"valor"}],
              "orden":[{"campo":"suma","desc":true}]}}}
```
- `hoja`: id o nombre; si el nombre no existe **se crea** (asi haces varias pestanas: "Resumen", "Detalle").
- Diseno sugerido: fila 1 = 4 KPIs de ancho 1; fila 2 = dos graficos de ancho 2; fila 3 = tabla de ancho 4.
- `kpi` = una agregacion sin `agrupar_por`. `barras/dona/embudo/treemap` agrupan por un campo.
- Revisa con `ver_libro_indicadores`.

## Recetas tipicas por fuente

| Pregunta del usuario | Fuente | Consulta |
|---|---|---|
| Tareas abiertas por encargado | `native:taskitem` | `ver_fuente_reporte` -> agrupar por el campo encargado, `contar`, filtro de estado |
| Horas por persona este mes | `native:worklog` | agrupar por usuario, `sumar` minutos/horas, filtro de fecha |
| Clientes por ciudad | `native:tercero` | agrupar por ciudad, `contar` |
| Monto por categoria (solo activos) | tabla de contenedor | filtro `estado en [...]`, agrupar, `sumar` |
| Top 20 clientes por monto | tabla de contenedor | agrupar por cliente, `sumar`, orden `suma` desc, `limite` 20 |
| Avance de una clasificacion IA | tabla de contenedor | agrupar por la columna de salida, `contar` (el grupo `null` = pendientes) |
| Ventas por mes | tabla de contenedor con columna `mes` (`AAAA-MM`) | agrupar por `mes`, `sumar` valor, orden `mes` ascendente, grafico `linea` o `barras` |

**Por mes / por anio:** `agrupar_por` usa el valor exacto del campo; no agrupa una fecha por periodo. La tabla debe
traer una columna de periodo (texto `AAAA-MM`) llenada al importar (ver trampa 35 en [[04 - Trampas conocidas]]).
Una tabla recien creada esta VACIA y el MCP no inserta filas ([[R05 - Crear un contenedor de datos]]): el reporte se
guarda igual (0 filas) y muestra datos cuando entren por conector, dron, agente o pantalla. Dilo al usuario.

Los nombres exactos de campos de las fuentes nativas salen de `ver_fuente_reporte`: no los adivines.

## Mantener y monitorear

- `listar_reportes` / `ver_reporte` (re-ejecuta y da datos frescos: sirve para seguir un proceso).
- Patron "version filtrada": conserva el reporte total y crea uno "ACTIVOS" con el filtro, en la misma categoria.
