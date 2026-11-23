---
tipo: receta
modulo: clasificacion
referencia: "[[T07 - Clasificacion y datos masivos con IA]]"
---

# R10 - Clasificar (enriquecer) datos con IA

Volver a [[00 - LEEME PRIMERO]]. Referencia: [[T07 - Clasificacion y datos masivos con IA]].

Caso tipico: miles de filas importadas (procesos, pedidos, tickets) a las que hay que poner una **categoria** o
**sector** leyendo su texto.

## El patron de la marca (entiendelo primero)

| Columna | Rol |
|---|---|
| `id_pedido` (clave) | Identifica la fila; el agente la devuelve para saber donde escribir |
| `descripcion`, `cliente`... (entrada) | Lo que lee el agente |
| `categoria` (salida) | Lo que escribe el agente |
| `clasificado_en` (marca) | **Vacia = pendiente.** Al clasificar se sella con la fecha UTC |

Todo es **incremental e idempotente**: solo se procesan filas con la marca vacia, asi que puedes cortar y seguir,
y las filas nuevas de cada importacion se clasifican solas.

## Paso 1 - Preparar columnas y agente

1. Columnas de salida y marca en la tabla (tipo `texto`): `agregar_columna` ([[R05 - Crear un contenedor de datos]]).
2. Agente con prompt de **salida solo JSON** con la clave y las salidas exactas
   ([[R09 - Crear y configurar un agente de IA]]):
   `[{"id_pedido":"...","categoria":"..."}]` y lista CERRADA de categorias.
3. `encender_agente` (si esta apagado, `clasificar_lote` falla).

## Paso 2 - Probar con un lote chico

```json
{"name":"clasificar_lote","arguments":{"contenedor":"Ventas","tabla":"Pedidos","agente":"Clasificador de pedidos",
  "columna_clave":"id_pedido","columna_marca":"clasificado_en","columnas_salida":["categoria"],
  "columnas_entrada":["cliente","descripcion"],"limite":5}}
```
Respuesta: `procesados`, `actualizados`, `sin_respuesta`, `restantes`, `tokens`. Revisa la calidad (reporte en modo
tabla con `descripcion` y `categoria`). Ajusta el prompt hasta que convenza. `columnas_entrada` es opcional (por
defecto envia todas menos marca y salidas): mandar solo lo relevante ahorra tokens.

## Paso 3a - Backlog (lo que ya existe): a mano, en lotes

Repetir `clasificar_lote` (max `limite` 50) hasta `restantes = 0`. **Confirma con el usuario el volumen** (costo
aproximado = filas x tokens por fila; como referencia, un clasificador DeepSeek gasto ~600 tokens por fila).
Para miles de filas usa un script con reintentos (ver [[01 - Conectar los MCP]]):

```powershell
$lote = @{ contenedor="Ventas"; tabla="Pedidos"; agente="Clasificador de pedidos"; columna_clave="id_pedido";
           columna_marca="clasificado_en"; columnas_salida=@("categoria"); columnas_entrada=@("cliente","descripcion"); limite=50 }
$fallos = 0; $sinAvance = 0
while ($true) {
  try { $r = Invoke-Mcp "clasificar_lote" $lote | ConvertFrom-Json } catch { $fallos++; if ($fallos -ge 8) { break }; Start-Sleep 15; continue }
  if ($null -eq $r.procesados) { $fallos++; if ($fallos -ge 8) { break }; Start-Sleep 15; continue }   # respuesta rara / cupo
  $fallos = 0
  Write-Output ("procesados={0} actualizados={1} sin_respuesta={2} restantes={3}" -f $r.procesados, $r.actualizados, $r.sin_respuesta, $r.restantes)
  if ([int]$r.restantes -le 0) { break }
  if ([int]$r.actualizados -eq 0) { $sinAvance++; if ($sinAvance -ge 2) { Write-Output "2 lotes sin avance: revisar prompt/filas"; break } } else { $sinAvance = 0 }
}
```
Si la respuesta menciona el **cupo** de IA, detente y avisa al usuario.
Un bucle local muere si el PC se reinicia: para volumenes grandes prefiere el Paso 3b (corre en el servidor).

## Paso 3b - Automatico: paso de clasificacion en la importacion

Lo mejor para lo nuevo (y para drenar backlog sin depender del PC):
```json
{"name":"configurar_paso_clasificacion","arguments":{"contenedor":"Ventas","programacion":"Pedidos diaria 5am",
  "agente":"Clasificador de pedidos","columna_clave":"id_pedido","columna_marca":"clasificado_en",
  "columnas_salida":["categoria"],"columnas_entrada":["cliente","descripcion"],"limite":50,"max_lotes":20}}
```
- Tras **cada corrida OK** de esa programacion, el servidor clasifica las filas con marca vacia lote a lote hasta no
  quedar pendientes, llegar a `max_lotes` (default 20 = 1.000 filas; tope 200) o al cupo de IA del plan. Reintenta
  errores del proveedor; para si 2 lotes seguidos no avanzan. Lo que falte queda para la siguiente corrida.
- Programacion de un **dron**: pasa el id de su programacion y `tabla` (obligatoria en ese caso).
- Correrlo YA sin esperar la importacion (asincrono, el motor lo toma en menos de 1 minuto):
```json
{"name":"ejecutar_clasificacion_ahora","arguments":{"contenedor":"Ventas","programacion":"Pedidos diaria 5am"}}
```
- El resumen (lotes, clasificadas, pendientes y por que paro) queda en el detalle de
  `listar_corridas_importacion`.
- Para drenar un backlog grande: sube `max_lotes` (ej. 200) con OK del usuario, dispara, y al terminar **bajalo** a
  20 de nuevo. `quitar_paso_clasificacion` lo desactiva.
- Si la importacion de esa programacion FALLA, el paso no corre: el pendiente se congela. Drena con
  `ejecutar_clasificacion_ahora` y arregla la importacion.

## Paso 4 - Monitorear

Un reporte agrupado por la salida; el grupo `null` son los pendientes:
```json
{"name":"crear_reporte","arguments":{"nombre":"Pedidos - avance de clasificacion","categoria":"Comercial","grafico":"barras",
  "contenedor":"Ventas","tabla":"Pedidos","consulta":{"agrupar_por":"categoria","agregaciones":[{"funcion":"contar"}],
  "orden":[{"campo":"conteo","desc":true}]}}}
```
`ver_reporte` lo re-ejecuta con datos frescos.

## Paso 5 - Reclasificar (cambio de taxonomia)

No borres la columna (no se puede si tiene datos). Vacia la marca (y si quieres las salidas) y vuelve a clasificar.
**DESTRUCTIVA y consume tokens: confirma.**
```json
{"name":"limpiar_marca","arguments":{"contenedor":"Ventas","tabla":"Pedidos","columna_marca":"clasificado_en",
  "columnas_salida":["categoria"]}}
```
Solo algunas filas: `filtro` = `{"categoria":"otros"}` (igualdad exacta).

## Operacion masiva sin IA: `rellenar_columna`

Calcula una columna a partir de otras con una plantilla (no consume IA). Ejemplos: armar una clave compuesta para
upsert, un texto combinado, copiar un valor.
```json
{"name":"rellenar_columna","arguments":{"contenedor":"Ventas","tabla":"Pedidos","columna":"clave_ingesta",
  "plantilla":"{id_pedido}|{nit_cliente}","solo_vacias":true}}
```
- Cada `{columna}` se reemplaza por el valor (recortado) de esa columna en la misma fila.
- `solo_vacias:true` (por defecto) solo toca filas con el destino vacio (idempotente); `false` sobrescribe
  (confirma antes).
- Hasta 20.000 filas por llamada; si `restantes` > 0, repite. Revisa la `muestra` que devuelve.
