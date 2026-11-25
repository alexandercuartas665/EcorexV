---
tipo: receta
tema: flujos
---

# F06 - Receta tiempos, tableros y cierre

Volver a [[00 - LEEME PRIMERO]].

Todo lo de esta receta se puede cambiar **aun con el flujo publicado** (no requiere version nueva).

## Plazo del paso (cuando vence)

```json
{"name":"plazo_nodo","arguments":{"nodo_id":"<id_paso>","dias":2,"dias_habiles":true}}
{"name":"plazo_nodo","arguments":{"nodo_id":"<id_paso>","horas":4}}
```
- `dias_habiles: true` salta fines de semana y festivos del tenant; sin el, son dias calendario.
- Todo en 0 (o sin valores) = quita el plazo.
- El plazo da el vencimiento del paso y alimenta los avisos de atraso.

## Arrancar despues (espera antes de activar el paso)

```json
{"name":"tiempo_arranque_nodo","arguments":{"nodo_id":"<id_llamada_seguimiento>","dias":3,"dias_habiles":true}}
```
Ejemplo: "Llamar al cliente 3 dias habiles despues de enviar la cotizacion". Todo en 0 = inmediato.

## Mover la actividad de tablero al llegar a un paso

Cada paso puede llevar la actividad a un tablero y columna (asi el tablero refleja en que va el proceso):

```json
{"name":"tablero_destino_nodo","arguments":{"nodo_id":"<id_revision>","tablero":"Compras","columna":"En revision"}}
{"name":"tablero_destino_nodo","arguments":{"nodo_id":"<id_compra>","tablero":"Compras","columna":"En compra"}}
```
- `tablero` = id, codigo o nombre; `columna` = id o nombre (omitida = primera columna).
- Si la columna no existe, la respuesta lista las del tablero. Sin `tablero` se quita el destino.
- Patron recomendado: una columna por etapa del flujo, y el **fin** en la columna de cierre.

## Cerrar con concepto (columna de cierre)

Si el destino es una columna de CIERRE, la respuesta trae `columna_de_cierre: true` y hay que decir con que concepto
(motivo) se cierra. Debe ser uno de los conceptos de cierre del tablero:

```json
{"name":"tablero_destino_nodo","arguments":{"nodo_id":"<id_fin_aprobado>","tablero":"Compras","columna":"Cerrado"}}
{"name":"concepto_cierre_nodo","arguments":{"nodo_id":"<id_fin_aprobado>","concepto":"Aprobada"}}
{"name":"tablero_destino_nodo","arguments":{"nodo_id":"<id_fin_rechazado>","tablero":"Compras","columna":"Cerrado"}}
{"name":"concepto_cierre_nodo","arguments":{"nodo_id":"<id_fin_rechazado>","concepto":"Rechazada"}}
```
Si el concepto no es valido, el error lista los del tablero. Si el tablero no tiene conceptos de cierre, se configuran
en el tablero (pantalla) antes; dilo al usuario.

## Apariencia (solo visual)

```json
{"name":"color_nodo","arguments":{"nodo_id":"<id_paso>","color":"amber"}}
{"name":"nota_nodo","arguments":{"nodo_id":"<id_paso>","nota":"Verificar que el proveedor este homologado"}}
```
Colores: violet, blue, green, amber, rose, slate. Cambiar el color no borra la nota (y viceversa).
