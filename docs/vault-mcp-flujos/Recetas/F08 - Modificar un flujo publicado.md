---
tipo: receta
tema: flujos
---

# F08 - Modificar un flujo publicado

Volver a [[00 - LEEME PRIMERO]]. Tabla completa en [[F01 - Modelo mental del motor de flujos]].

## Cambios EN VIVO (directo sobre el publicado)

Encargado y cargos, formularios del nodo (agregar, quitar, obligatorio, por salida, crear al llegar), avisos, plazo,
tiempo de arranque, tablero/columna, concepto de cierre, salto, reglas, color y nota, nombre/categoria/descripcion
del flujo, pausar/reanudar. Aplican a los pasos que se activen desde ese momento.

## Cambios que requieren VERSION NUEVA

Agregar/quitar nodos o salidas, nombres y condiciones de salidas, decision humana, reinicio.

```json
{"name":"nueva_version_flujo","arguments":{"flujo":"Aprobacion de compra"}}
```
- Devuelve un `definicion_id` NUEVO con **ids nuevos** de nodos y salidas: usa SOLO esos (los viejos son de la
  version publicada). Todo lo configurado se copia.
- Edita, revisa con `ver_flujo` y publica ESA definicion: los conceptos enganchados pasan solos a la version nueva.
- Las tareas que ya estaban en marcha terminan con la version con la que arrancaron.

Si intentas un cambio de borrador sobre el publicado, el error lo dice y sugiere `nueva_version_flujo`.

## Antes de tocar un flujo en uso

- `listar_flujos` muestra `instancias_en_marcha`. Si hay muchas, avisa al usuario que los cambios EN VIVO las
  afectan desde el siguiente paso.
- Pausar (`pausar_flujo`) evita que arranquen tareas nuevas mientras preparas la version.
