---
tipo: receta
tema: flujos
---

# F07 - Receta encadenar procesos, reglas y avisos

Volver a [[00 - LEEME PRIMERO]].

## Encadenar con otro proceso (salto)

Al llegar a un nodo se arranca OTRO flujo en una tarea hija (ej. al aprobar una venta arranca "Despacho"):

```json
{"name":"salto_a_flujo_nodo","arguments":{"nodo_id":"<id_aprobado>","flujo_destino":"Despacho"}}
```
- El flujo destino debe existir (id, codigo o nombre). Un nodo no puede saltar a su propio flujo.
- Sin `flujo_destino` se quita el salto.
- El flujo destino debe estar PUBLICADO para que arranque.

## Reglas en un paso

Las reglas (verbos del motor de reglas: crear tarea, pasar campos, completar paso...) se crean con `crear_regla`
(cerebro `formularios`: `listar_verbos_regla` da los verbos y sus parametros). Aqui se VINCULAN a un nodo:

```json
{"name":"listar_reglas_disponibles","arguments":{"buscar":"tarea"}}
{"name":"agregar_regla_nodo","arguments":{"nodo_id":"<id_paso>","regla":"Crear tarea de despacho","autonoma":true}}
```
- `autonoma: true` = la regla actua sola en ese paso; `false` = la dispara la persona.
- `regla_autonoma_nodo` cambia eso despues; `quitar_regla_nodo` la desvincula (la regla sigue existiendo).

## Avisos internos (equipo)

Avisar al encargado del paso cuando le llega trabajo:

```json
{"name":"agregar_notificacion_nodo","arguments":{"nodo_id":"<id_paso>","canal":"correo","destino":"encargado_paso","asunto":"Tienes un paso pendiente: {tarea.numero}","mensaje":"{usuario.nombre}, te llego {tarea.titulo} del cliente {tarea.cliente}.","incluir_enlace":true}}
```

Avisar a un grupo de WhatsApp o a Telegram (texto libre; el destino va en la regla):

```json
{"name":"agregar_notificacion_nodo","arguments":{"nodo_id":"<id_paso>","canal":"whatsapp_grupo","linea_grupo_id":"<id_linea_evolution>","grupo_jid":"<jid_del_grupo>","mensaje":"Nueva solicitud {tarea.numero}: {tarea.titulo}"}}
{"name":"agregar_notificacion_nodo","arguments":{"nodo_id":"<id_paso>","canal":"telegram","chat_id":"<chat_id>","mensaje":"Nueva solicitud {tarea.numero}"}}
```

Avisar a un usuario concreto: `destino: usuario` + `usuario` (su correo).

- Adjuntar el PDF de un formulario de la tarea: `adjuntar_pdf_formulario` (WhatsApp).
- `nota_agente`: nota interna para el agente conversacional cuando el aviso va al cliente (no se le envia).
- Los ids de lineas de WhatsApp los conoce el usuario o el MCP de WhatsApp; no los inventes.

## Ciclo de vida del flujo

```json
{"name":"editar_flujo","arguments":{"flujo":"<flujo>","descripcion":"Aprobacion de compras mayores a 5 millones"}}
{"name":"pausar_flujo","arguments":{"flujo":"<flujo>"}}
{"name":"reanudar_flujo","arguments":{"flujo":"<flujo>"}}
{"name":"desenganchar_flujo_de_concepto","arguments":{"concepto":"Solicitud de compra"}}
```
- Pausar: las tareas NUEVAS no arrancan el flujo; las que estan en marcha siguen.
- Desenganchar: el concepto deja de arrancar el flujo (confirma con el usuario).
