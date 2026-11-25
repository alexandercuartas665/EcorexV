---
tipo: receta
tema: flujos
---

# F12 - Receta agente de IA en un paso

Volver a [[00 - LEEME PRIMERO]].

Un paso (tarea o compuerta) puede atenderlo un **agente de IA**: llena el formulario del paso buscando datos,
pregunta al cliente por WhatsApp, llama por voz, escribe correos y, segun su autonomia, cierra el paso o lo deja
listo para que una persona confirme.

Caso: paso **"Confirmar fecha de pago"**. El agente le escribe al cliente por WhatsApp (plantilla en frio), insiste
en dias habiles de 8 a 18 h, y si no obtiene respuesta toma la salida "Sin respuesta".

## 1. Que hay disponible

```json
{"name":"opciones_agente_nodo","arguments":{}}
```
Agentes del tenant (y si estan encendidos), clientes Colmena, lineas de WhatsApp y de voz. Si no hay agente, se crea
con `crear_agente` (guia general, receta R09) y se enciende con `encender_agente`.

## 2. Asignar el agente

```json
{"name":"agente_nodo","arguments":{"nodo_id":"<id_paso>","agente":"Asistente de Cobranza","autonomia":"propone"}}
```
- `propone` (defecto, recomendado al empezar): deja todo listo y una persona confirma.
- `autonomo`: cierra el paso solo. Usalo cuando el usuario ya confia en el agente.
- Si el agente esta APAGADO la respuesta lo avisa: no atendera hasta encenderlo.

## 3. Que debe hacer en ESTE paso

```json
{"name":"instrucciones_agente_nodo","arguments":{"nodo_id":"<id_paso>","instrucciones":"Confirma con el cliente la fecha en que pagara la factura {tarea.numero}. Se cordial y breve. Registra la fecha en el campo fecha_pago."}}
```

## 4. Recursos (cada uno es una herramienta; cambiar uno no borra los demas)

| Recurso | Herramienta |
|---|---|
| WhatsApp (y plantilla para escribir en frio) | `whatsapp_agente_nodo` (`linea`, `plantilla`, `idioma`, `variables`, `encabezado_url`, `minutos_frio`) |
| Llamadas de voz con reintentos y horario | `voz_agente_nodo` (`agente_voz`, `linea_voz`, `max_llamadas`, `reintento_minutos`, `hora_inicio`, `hora_fin`) |
| Buscar en la web con un navegador de la Colmena | `web_agente_nodo` (`cliente_colmena`, `perfil`) |
| Enviar correos | `correo_agente_nodo` |
| Limitar canales | `canales_agente_nodo` (`voz`, `whatsapp`, `web`, `correo`; vacio = todos) |

```json
{"name":"whatsapp_agente_nodo","arguments":{"nodo_id":"<id_paso>","linea":"Linea comercial","plantilla":"recordatorio_pago","idioma":"es","variables":{"nombre":"{tarea.cliente}","factura":"{tarea.numero}"},"minutos_frio":1440}}
```

## 5. Insistencia (cadencia) y tope

```json
{"name":"seguimiento_agente_nodo","arguments":{"nodo_id":"<id_paso>","esperas":[{"minutos":0},{"dias":1},{"dias":2}],"ventana":{"inicio":"08:00","fin":"18:00","dias":[1,2,3,4,5],"festivos":true}}}
{"name":"tope_preguntas_agente_nodo","arguments":{"nodo_id":"<id_paso>","tope":4}}
```
- Primer contacto inmediato, recordatorio al dia siguiente y otro dos dias despues; al agotarse deja de insistir.
- La ventana corre los envios que caigan de noche o fin de semana al siguiente horario permitido.

## 6. Si no lo logra

```json
{"name":"fallo_agente_nodo","arguments":{"nodo_id":"<id_paso>","accion":"tomar_ruta","ruta":"Sin respuesta"}}
```
`ruta` = NOMBRE de la opcion/salida que se toma (debe existir en la compuerta siguiente, ver [[F02 - Las dos formas de decidir]]).
Otras: `devolver_a_persona` (defecto), `reintentar` (+ `reintentos`), `notificar`.

## 7. Compuertas con agente: nota por salida

Una compuerta solo admite agente si es de DECISION HUMANA (`decision_humana_nodo` activo=true, en borrador): el agente
decide en lugar de la persona. Luego puedes dejar una nota al agente conversacional segun la salida tomada:

```json
{"name":"traspaso_salida_agente_nodo","arguments":{"nodo_id":"<id_compuerta>","salida_nodo_id":"<id_pago_confirmado>","nota":"El cliente confirmo la fecha de pago: agradece y no vuelvas a cobrar."}}
```

## 8. Verificar

`ver_agente_nodo` (todo junto) y `ver_flujo` (columna `agente_ia` en cada paso; marca APAGADO). Para quitar el agente:
`quitar_agente_nodo` (confirma: se pierde su configuracion).

Cuidados: cada prueba con WhatsApp o voz contacta personas reales; usa una tarea de prueba con el telefono del usuario
y avisale antes. El consumo de IA cuenta contra el cupo del plan.
