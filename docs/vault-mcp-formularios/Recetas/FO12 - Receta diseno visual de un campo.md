---
tipo: receta
tema: formularios
---

# FO12 - Receta diseno visual de un campo

Volver a [[00 - LEEME PRIMERO]].

El color, la letra y las tarjetas del formulario COMPLETO son del tema (`set_theme`, guia general). Para cambiar el
aspecto de UN solo control usa `diseno_campo`.

## Claves del estilo

| Parte | Claves | Valores |
|---|---|---|
| Etiqueta | `labelSize`, `labelBold`, `labelItalic`, `labelColor`, `hideLabel` | tamano 8-48 px; color `#hex` |
| Caja | `boxBg`, `borderColor`, `borderWidth`, `radius`, `height`, `align` | grosor 0-8, redondeo 0-40, alto 20-400 px; `align` left, center o right |
| Valor | `valueSize`, `valueBold`, `valueColor` | tamano 8-64 px |
| Bloque | `marginTop`, `marginBottom`, `blockBg` | margenes 0-120 px |

- Solo cambia las claves que envias; lo demas del estilo se conserva.
- `quitar` devuelve claves al estilo del tema; `limpiar=true` lo devuelve todo.
- Lo invalido se descarta sin error (un color que no es `#hex`); las medidas fuera de rango se acotan. Revisa el
  `estilo` que devuelve la respuesta: es lo que quedo guardado.

```json
{"name":"diseno_campo","arguments":{"formulario":"COT-VENTA","campo":"total","estilo":{"labelBold":true,"valueSize":20,"valueBold":true,"boxBg":"#f1f8e9","borderColor":"#2e7d32","radius":8,"align":"right"}}}
{"name":"diseno_campo","arguments":{"formulario":"COT-VENTA","campo":"total","quitar":["boxBg"]}}
```

## Campo destacado y no editable

Un total calculado suele ir resaltado y protegido:

1. `diseno_campo` con letra grande y fondo suave (arriba).
2. `campo_bloqueado` con `activo=true`: nadie lo edita, tampoco en el enlace publico `/f/`. Su valor lo ponen el
   calculo, una regla, el prellenado o el valor por defecto.

Al crear campos nuevos tambien puedes mandar `is_read_only` y `style_json` (el mismo JSON de claves) en `add_question`
o `update_question`. Para retocar uno existente, `diseno_campo` es mas seguro: mezcla en lugar de reemplazar.
