---
tipo: checklist
tema: flujos
---

# F10 - Checklist antes de publicar

Volver a [[00 - LEEME PRIMERO]].

Corre `ver_flujo` y revisa, nodo por nodo:

- [ ] `ver_flujo` NO trae `avisos`.
- [ ] Hay exactamente un `inicio` y al menos un `fin`; todo nodo tiene entrada y salida (salvo inicio/fin).
- [ ] Ninguna `tarea` tiene dos salidas (salvo que el paralelo sea intencional y el usuario lo pidio).
- [ ] Cada `compuerta` es AUTOMATICA (salidas con nombre + condicion `approval == '<nombre>'` + UNA por defecto) o
      de DECISION HUMANA (`decision_humana: true` + encargado). Ver [[F02 - Las dos formas de decidir]].
- [ ] Cada paso humano tiene encargado: `politica` con cargos, `hereda_inicio`, `hereda_anterior` o
      `campo_formulario`.
- [ ] Formularios: los que deben llenarse estan en su paso; `obligatorio` / `obligatorio_en_salidas` donde aplica.
- [ ] Avisos: destino y canal correctos; probados solo con contactos del usuario.
- [ ] Tablero/columna por etapa si el usuario sigue el proceso en un tablero; fines en columna de cierre con
      `concepto_cierre`.
- [ ] Plazos en los pasos que tienen tiempo de respuesta.
- [ ] El flujo destino de cada salto existe y esta publicado.

Luego:

```json
{"name":"publicar_flujo","arguments":{"flujo":"<definicion_id>"}}
{"name":"enganchar_flujo_a_concepto","arguments":{"concepto":"<concepto>","flujo":"<definicion_id>"}}
```

Y reporta al usuario: nombre del flujo, concepto enganchado, que pasa en cada paso y quien lo atiende, y como
probarlo (crear una tarea de ese concepto con `crear_tarea`).
