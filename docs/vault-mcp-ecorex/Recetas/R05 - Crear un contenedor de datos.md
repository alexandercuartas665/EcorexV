---
tipo: receta
modulo: contenedores
referencia: "[[T05 - Contenedores de datos]]"
---

# R05 - Crear un contenedor de datos

Volver a [[00 - LEEME PRIMERO]]. Referencia: [[T05 - Contenedores de datos]]. Para llenarlo desde una API o BD
sigue despues con [[R06 - Conector API e importacion programada]].

## Paso 0 - Disenar ANTES de crear (no te lo saltes)

Una columna con datos **no se puede borrar**. Antes de crear nada, acuerda con el usuario:

| Decision | Recomendacion |
|---|---|
| Tablas | Una por entidad (Pedidos, Clientes, Productos). Las hijas (lineas de un pedido) como tabla aparte relacionada o `submodelo` |
| Clave natural | Una columna con el id de la fuente (`id_pedido`). Es la que permite **upsert** (no duplicar) |
| Tipos | Dinero -> `decimal`; cantidades enteras -> `numero`; fechas -> `fecha`; si/no -> `booleano`; lo demas `texto` |
| Columnas de IA (si se va a clasificar) | Una o mas de salida (`categoria`) + una **marca** (`clasificado_en`, texto) |
| Nombres | minusculas_con_guion_bajo, sin tildes: son los que usaras en mapeos y reportes |

## Paso 1 - Ver lo que existe

```json
{"name":"listar_contenedores_datos","arguments":{}}
```

## Paso 2 - Crear el contenedor y sus tablas

```json
{"name":"crear_contenedor_datos","arguments":{"nombre":"Ventas","descripcion":"Pedidos y clientes del ERP"}}
```
```json
{"name":"agregar_tabla","arguments":{"contenedor":"Ventas","nombre":"Clientes",
  "columnas":[{"nombre":"nit","tipo":"texto","requerido":true},{"nombre":"razon_social","tipo":"texto"},
              {"nombre":"ciudad","tipo":"texto"},{"nombre":"segmento","tipo":"texto"}]}}
```
```json
{"name":"agregar_tabla","arguments":{"contenedor":"Ventas","nombre":"Pedidos",
  "columnas":[{"nombre":"id_pedido","tipo":"texto","requerido":true},{"nombre":"nit_cliente","tipo":"texto"},
              {"nombre":"fecha","tipo":"fecha"},{"nombre":"estado","tipo":"texto"},
              {"nombre":"descripcion","tipo":"texto"},{"nombre":"valor","tipo":"decimal"},
              {"nombre":"categoria","tipo":"texto"},{"nombre":"clasificado_en","tipo":"texto"}]}}
```
Cada `agregar_tabla` devuelve `tabla_id`. Columnas despues:
```json
{"name":"agregar_columna","arguments":{"contenedor":"Ventas","tabla":"Pedidos","nombre":"vendedor","tipo":"texto"}}
```

## Paso 3 - Relaciones

```json
{"name":"agregar_relacion","arguments":{"contenedor":"Ventas","origen":"Pedidos","destino":"Clientes",
  "cardinalidad":"n1","nombre":"cliente del pedido"}}
```
`n1` = muchos pedidos -> un cliente (lookup). `nn` = muchos a muchos. Solo entre tablas del MISMO contenedor.
Sub-tabla anidada: columna tipo `submodelo` con `hijo` = id de la tabla hija.

## Paso 4 - Verificar

```json
{"name":"ver_contenedor_datos","arguments":{"contenedor":"Ventas"}}
```
Muestra tablas (con `tabla_id`), columnas (con tipo) y relaciones. Guarda el `tabla_id`: los reportes lo usan
(`container:{tabla_id}`).

## Paso 5 - Como se llena

Pocas filas (pruebas, catalogos chicos, datos que el usuario te dicta): **directo por MCP** con `agregar_filas`
(maximo 100 por llamada; usa los NOMBRES de columna de `ver_contenedor_datos`):

```json
{"name":"agregar_filas","arguments":{"contenedor":"Ventas","tabla":"Pedidos","filas":[{"id_pedido":"P-001","cliente":"Industrias Ejemplo","fecha":"2026-01-10","valor":1250.5},{"id_pedido":"P-002","cliente":"Comercial Demo","fecha":"2026-02-03","valor":980}]}}
{"name":"ver_filas","arguments":{"contenedor":"Ventas","tabla":"Pedidos","limite":10}}
```

- Formatos: decimal con punto y sin miles (`1250.5`), fecha `AAAA-MM-DD` (o ISO con hora), booleano
  `true/false/si/no`. Un solo valor invalido o una columna que no existe rechaza TODO el lote: corrige y reenvia.
- Con `columna_clave` (p.ej. `"id_pedido"`) actualiza las filas que ya existen en vez de duplicarlas (upsert).
- `ver_filas` verifica la carga (las mas recientes primero; `filtros` por texto que contiene).
- Si el usuario pide **datos de ejemplo**, marcalos como tales y avisale (son datos reales en su sistema).

Volumen o carga recurrente: NO uses `agregar_filas` en bucle; usa una de estas vias:

| Via | Cuando | Como |
|---|---|---|
| Conector API / BD + programacion | Fuente con API o base de datos | [[R06 - Conector API e importacion programada]] |
| Excel | Carga puntual o periodica de archivos | Conector `excel`; el archivo se sube en la pantalla del contenedor |
| Dron web | Solo hay pagina web | [[R08 - Crear un dron de extraccion]] |
| Agente de IA | La IA genera o extrae los datos (ej. lee PDFs) | Herramienta del agente `agregar_filas_a_contenedor` ([[R09 - Crear y configurar un agente de IA]]) |
| Pantalla | Captura manual | Grilla de la tabla en la app (o publicada como modulo) |

Para **ver** datos: `ver_filas` (hasta 100 filas), o un reporte en modo tabla (`crear_reporte` con
`grafico:"tabla"` y `consulta.campos`) si el usuario lo quiere guardado.

## Paso 6 - Publicar como modulo del menu (opcional)

```json
{"name":"publicar_contenedor","arguments":{"contenedor":"Ventas","tabla":"Pedidos","padre":"Comercial"}}
```
`padre` = seccion del menu donde cuelga (obligatoria si la vista no tiene donde ponerlo). Luego el administrador
define en **Roles y permisos** quien lo ve. `ver_publicacion_contenedor` confirma ruta y columnas;
`despublicar_contenedor` lo quita (confirma antes).

## Cambios posteriores

| Quiero | Herramienta | Nota |
|---|---|---|
| Renombrar o cambiar tipo de una columna | `editar_columna` (`columna` = nombre o posicion) | Funciona aunque tenga datos (ej. texto -> decimal) |
| Renombrar tabla / descripcion | `editar_tabla` | Conserva columnas |
| Borrar columna | `eliminar_columna` | Solo si esta VACIA |
| Vaciar o recalcular una columna | `limpiar_marca` / `rellenar_columna` | Ver [[R10 - Clasificar datos con IA]] |
| Borrar tabla / contenedor | `eliminar_tabla` / `eliminar_contenedor_datos` | Confirma: rompe reportes y destinos (el id cambia si recreas) |
