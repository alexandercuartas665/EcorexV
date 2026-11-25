---
tipo: receta
tema: formularios
---

# FO08 - Receta proteger campos por rol

Volver a [[00 - LEEME PRIMERO]].

| Necesidad | Herramienta | Efecto |
|---|---|---|
| Nadie edita el campo (lo llena una regla o un calculo) | `campo_bloqueado` | Se ve, no se edita |
| Guardar un dato sin mostrarlo | `campo_oculto` | No se ve, guarda valor |
| Un rol no lo ve / no lo edita | `permisos_campo_por_rol` | Por rol: Owner, Admin, Supervisor, Advisor |
| Una seccion solo para ciertos cargos del organigrama | `allowed_cargos_json` del contenedor (guia general) | Por cargo |

```json
{"name":"permisos_campo_por_rol","arguments":{"formulario":"COT-VENTA","campo":"margen","ocultar_a":["Advisor"],"solo_lectura_a":["Supervisor"]}}
```

- Reemplaza la configuracion anterior del campo; listas vacias quitan la restriccion.
- Rol = rol del usuario en la empresa (no el cargo del organigrama).
- Para datos realmente sensibles prefiere una SECCION por cargo: oculta el bloque entero.

Combinaciones utiles: `total` calculado -> `campo_bloqueado`; `costo` interno -> `permisos_campo_por_rol` ocultar a
Advisor; `tarea_creada` (anti-duplicado) -> `campo_oculto`.
