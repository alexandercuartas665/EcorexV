---
tipo: receta
modulo: tableros-tareas
referencia: "[[T01 - Tableros y tareas]]"
---

# R01 - Operar tareas y tableros

Volver a [[00 - LEEME PRIMERO]]. Referencia: [[T01 - Tableros y tareas]].

## A. "Que tengo pendiente?"

1. `mis_pasos_pendientes` (sin parametros): pasos de flujo que esperan al usuario, con la tarea, el proceso y las
   **opciones** de cada paso.
2. `buscar_tareas` con `solo_mias=true`, `estado="abiertas"`: sus tareas sin importar el flujo.
3. Resume al usuario: numero, titulo, vencimiento, que decision espera cada paso.

## B. Ver un tablero

```json
{"name":"listar_tableros","arguments":{}}
{"name":"ver_tablero","arguments":{"tablero":"Comercial","alcance":"equipo"}}
```
`alcance`: `equipo` (todas), `mias`, `sin_asignar`, `terminadas`. Filtra con `columna` o `texto`. Si el tablero
restringe la vista del usuario, el servidor fuerza `mias`.

## C. Crear una tarea

1. Identifica tablero y concepto: `listar_tableros`, `listar_conceptos`. Si va asignada: `listar_usuarios`.
2. Crea:
```json
{"name":"crear_tarea","arguments":{
  "tablero":"Comercial","concepto":"Cotizacion",
  "titulo":"Cotizar 20 sillas ergonomicas","descripcion":"Entrega en 15 dias",
  "prioridad":"alta","vence":"2026-12-15","asignado":"ana@miempresa.com"}}
```
- Con `concepto` que tiene flujo, la tarea **arranca el flujo** (y puede caer en el tablero del concepto aunque no
  des `tablero`).
- Sin `columna` cae en la primera columna.
3. Devuelve el **numero** (`T00xxx`): reportalo al usuario.

## D. Trabajar una tarea

```json
{"name":"ver_tarea","arguments":{"numero":"T00123"}}
{"name":"comentar","arguments":{"numero":"T00123","texto":"Cliente confirma cantidades"}}
{"name":"checklist","arguments":{"numero":"T00123","accion":"agregar","texto":"Enviar ficha tecnica"}}
{"name":"checklist","arguments":{"numero":"T00123","accion":"marcar","item":"1"}}
{"name":"registrar_tiempo","arguments":{"numero":"T00123","minutos":45,"nota":"Llamada con el cliente"}}
{"name":"asignar_tarea","arguments":{"numero":"T00123","usuario":"pedro@miempresa.com"}}
```

## E. Mover o cerrar

**Primero mira `ver_tarea`:** si tiene **paso de flujo en curso**, ve a F.

Sin flujo:
```json
{"name":"mover_tarea","arguments":{"numero":"T00123","columna":"En progreso"}}
{"name":"terminar_tarea","arguments":{"numero":"T00123","motivo_cierre":"Ganada","nota":"OC recibida"}}
```
`terminar_tarea` es idempotente y lleva la tarea por los estados validos hasta "Hecho". Si el tablero exige
concepto de cierre, envia `motivo_cierre`. **Confirma con el usuario antes de cerrar.**

## F. Avanzar un paso de flujo

1. `ver_tarea` -> lee el **paso actual**, sus **opciones** (rutas) y si el usuario puede atenderlo.
2. Si el paso esta sin asignar y el usuario puede: `tomar_paso`.
3. Con confirmacion del usuario:
```json
{"name":"completar_paso","arguments":{"numero":"T00123","opcion":"Aprobado","comentario":"Precio validado"}}
```
- `opcion` debe ser **exactamente** una de las que lista `ver_tarea`.
- Si hay varios pasos en paralelo, indica `paso` (nombre).
- Si el paso exige formulario no diligenciado, el servidor lo rechaza: el usuario lo llena en la aplicacion.

## Errores tipicos

- Intentar `mover_tarea` en una tarea con flujo -> usa F.
- Inventar el nombre del encargado -> usa `listar_usuarios` (email es lo mas seguro).
- Ver [[04 - Trampas conocidas]] (seccion Tareas) y [[05 - Solucion de problemas]].
