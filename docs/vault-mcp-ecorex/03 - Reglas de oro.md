---
tipo: guia
tema: reglas
---

# Reglas de oro

Volver a [[00 - LEEME PRIMERO]]. Estas reglas protegen los datos del cliente. No son opcionales.

## 1. Confirma con el usuario ANTES de...

Toda herramienta marcada **DESTRUCTIVA** en la Referencia, y ademas:

| Accion | Por que |
|---|---|
| Borrar cualquier cosa (`eliminar_*`, `delete_*`, `archive`) | Puede ser irreversible o romper reportes/flujos que dependen de eso |
| `inactivar_contacto` | Saca al contacto de busquedas y procesos |
| Cerrar pasos de flujo o mover tareas a columnas de cierre | Dispara notificaciones y el siguiente paso del proceso |
| `publicar_flujo` con `forzar=true` | Publica a pesar de avisos de errores en el grafo |
| `create_share_link` | Abre un enlace publico al formulario |
| `limpiar_marca` | Vacia una columna en todas las filas y fuerza reprocesar (tokens) |
| `rellenar_columna` con `solo_vacias=false` | Sobrescribe datos existentes |
| Clasificar / procesar miles de filas con IA (`clasificar_lote` en bucle, subir `max_lotes`, `ejecutar_clasificacion_ahora`) | Consume tokens del cupo de IA del cliente |
| Despublicar un contenedor, cambiar modo de importacion a `replace` | `replace` borra y recarga la tabla completa |
| WhatsApp: escribir a un numero que nunca escribio, borrar mensajes, bloquear, crear/salir de grupos, publicar estados | Riesgo de bloqueo del numero y efecto publico |
| Publicar una version de maqueta | Queda como actual para toda la empresa |

Pide la confirmacion con datos concretos: *que* se borra/cambia, *cuantos* registros, *si se puede deshacer*.

## 2. Secretos

- **Nunca** pidas, escribas, repitas ni guardes tokens, contrasenas, API keys o credenciales en el chat, en
  archivos o en parametros que no sean para eso.
- `guardar_conector` **no recibe** secretos: la clave del API / contrasena de la BD la pone el usuario en la
  pantalla del contenedor. Dile exactamente donde.
- Las variables de dron con clave van con `definir_variable` y `secreta=true` (quedan cifradas y no se pueden
  volver a leer en claro).
- Si ves un secreto expuesto, avisa al usuario para que lo rote.

## 3. Lee antes de escribir

- Antes de crear: busca si ya existe (`buscar_contacto`, `listar_tableros`, `list_forms`, `listar_contenedores_datos`,
  `listar_agentes`, `listar_drones`, `listar_reportes`...). Duplicar es el error mas comun.
- Antes de editar: mira el estado actual (`ver_*`, `get_form`). Las ediciones son "solo lo que envias", pero
  necesitas saber que hay.
- Antes de usar un campo en un reporte: `ver_fuente_reporte` te dice si existe y si es agrupable/sumable.

## 4. Respeta el proceso

- Una tarea **gobernada por flujo** no se mueve ni se termina a mano: `completar_paso` con la `opcion` exacta.
- Si un paso exige formulario sin diligenciar, el servidor lo rechaza: el usuario debe llenarlo en la app.
- No "arregles" datos saltandote reglas del sistema (por ejemplo, crear tareas duplicadas para forzar un flujo).

## 5. Volumen y costo

- Prueba siempre con **poco** primero: una ventana de 1 dia en una importacion, un lote de 5-50 filas en una
  clasificacion, un formulario en borrador. Valida y luego escala.
- Las importaciones tienen un tope de filas por corrida (50.000) y los lotes de IA de 50 filas. Planea en tandas.
- Informa al usuario el costo aproximado cuando uses IA en volumen (filas x lotes).

## 6. Honestidad en el reporte

- Reporta lo que paso, con numeros (ids, filas, conteos). Si algo fallo o quedo a medias, dilo.
- Si una herramienta no existe para lo que piden, dilo y sugiere como hacerlo en pantalla. No improvises con
  herramientas que no son para eso.
- Si una operacion es asincrona (clasificacion encolada, corrida programada), di "quedo encolado, se verifica
  con X" en vez de "listo".

Siguiente: [[04 - Trampas conocidas]].
