---
tipo: fundamento
tema: flujos
---

# F02 - Las dos formas de decidir

Volver a [[00 - LEEME PRIMERO]]. Base: [[F01 - Modelo mental del motor de flujos]].

Una compuerta elige UNA salida. Hay dos maneras y **se configuran distinto**. Elegir mal deja el flujo trabado.

## A) Compuerta AUTOMATICA (la decision la toma quien cierra el paso ANTERIOR)

Es el comportamiento por defecto. El paso que va antes de la compuerta (ej. "Revision del jefe") muestra como
**opciones** los **NOMBRES de las salidas** de la compuerta. Quien cierra ese paso elige una opcion; el motor compara
esa opcion con la **condicion** de cada salida y toma la que coincide.

Requisitos (todos):

1. Cada salida de la compuerta tiene **nombre** (`nombre_salida` o `conectar_nodos` con `nombre`).
2. Cada salida, salvo UNA, tiene la condicion `approval == '<su nombre>'` (`condicion_ruta`).
3. Exactamente UNA salida sin condicion = **ruta por defecto** (se toma si ninguna condicion aplica).

```json
{"name":"conectar_nodos","arguments":{"desde_nodo_id":"<id_compuerta>","hasta_nodo_id":"<id_compra>","nombre":"Aprobada","condicion":"approval == 'Aprobada'"}}
{"name":"conectar_nodos","arguments":{"desde_nodo_id":"<id_compuerta>","hasta_nodo_id":"<id_fin>","nombre":"Rechazada"}}
```

En este ejemplo "Rechazada" no tiene condicion: es la ruta por defecto. El paso anterior mostrara las opciones
**Aprobada** y **Rechazada**.

Sintaxis de condicion: SOLO `approval == 'Valor'` o `approval != 'Valor'` (mayusculas/minusculas da igual). Cualquier
otra expresion NUNCA se cumple. No hay condiciones por campos de formulario en la compuerta (para eso, reglas).

## B) Compuerta con DECISION HUMANA (la decide su propio encargado)

La compuerta se vuelve un paso: aparece en la bandeja de su encargado, que **elige la salida por su destino**.

```json
{"name":"decision_humana_nodo","arguments":{"nodo_id":"<id_compuerta>","activo":true}}
{"name":"asignar_encargado_nodo","arguments":{"nodo_id":"<id_compuerta>","origen":"politica"}}
{"name":"asignar_cargo_nodo","arguments":{"nodo_id":"<id_compuerta>","cargo":"Gerente"}}
```

- No necesita condiciones (si las tiene, se ignoran al elegir por destino). Igual ponle **nombre** a cada salida:
  es lo que se lee en el diagrama y en la bitacora.
- Es la forma de usar **enlaces de decision del cliente** ([[F05 - Receta decision del cliente por enlace]]) y
  **formulario obligatorio por salida** (`formulario_obligatorio_por_salida`).
- Solo en borrador (`nueva_version_flujo` si ya esta publicado).

## Cual usar

| Situacion | Forma |
|---|---|
| Quien revisa el paso anterior es quien decide (jefe aprueba lo que reviso) | A) automatica |
| La decision es de OTRA persona, o merece su propio paso en la bandeja | B) decision humana |
| Decide el cliente desde un enlace | B) + aviso con `enlaces_decision` |
| Hay que exigir un formulario solo en una de las salidas (ej. motivo de rechazo) | B) + `formulario_obligatorio_por_salida` |

## Como se ve si esta mal

`ver_flujo` devuelve avisos como:
- "La compuerta automatica 'X' tiene salidas SIN NOMBRE" -> faltan nombres (no habra opciones que elegir).
- "La compuerta 'X' se resuelve SOLA pero varias salidas no tienen condicion" -> faltan condiciones o falta
  `decision_humana_nodo`.
- "'X' toma el encargado por politica pero no tiene cargo/dependencia" -> nadie atenderia la decision.
