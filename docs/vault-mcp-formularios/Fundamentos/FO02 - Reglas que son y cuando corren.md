---
tipo: fundamento
tema: formularios
---

# FO02 - Reglas que son y cuando corren

Volver a [[00 - LEEME PRIMERO]].

Una **regla** = un **verbo** (que hace) + sus **parametros** (con que datos). Vive en un **documento de reglas**. Por
si sola no hace nada: corre cuando esta **vinculada** a un momento.

## Los 4 momentos (disparadores)

| Momento | Como se vincula | Ejemplo |
|---|---|---|
| Al ENVIAR el formulario | `regla_al_enviar` (crea y vincula en un paso; una regla por verbo) | Crear una tarea con los datos del lead |
| Al CAMBIAR un campo | `crear_regla` + `regla_en_campo` (campo normal) | Copiar el NIT a otro campo |
| Al PULSAR un boton | `crear_regla` + `regla_en_campo` (campo de tipo `Button`) | Cerrar el paso del flujo |
| En un PASO de un flujo | `crear_regla` + `agregar_regla_nodo` (cerebro de flujos) | Notificar al llegar al paso |

Para mostrar/ocultar/exigir campos hay un atajo: `condicion_entre_campos` (crea la regla de UI y la vincula).

## Para que una regla CORRA

1. La regla esta **activa** (`estado_regla`). `desarrollo` = solo en prueba manual; `inactiva` = nunca.
2. Su **documento** esta activo. Las reglas creadas por este MCP van al documento del formulario
   (`FRMRULES-<codigo>`) o a "Reglas creadas por IA", ambos activos. Si eliges otro `documento` inactivo, la
   respuesta te avisa.
3. Esta **vinculada** al momento correcto.
4. El formulario esta **activo** (para envios).

## Parametros

- Sus NOMBRES y tipos salen de `listar_verbos_regla` con `verbo`: no los adivines. Un nombre desconocido se rechaza.
- Tipos: `texto`, `numero`, `si_no`, `codigo_de_campo` (se valida contra el formulario), `lista_de_codigos_de_campo`,
  `json`, `opcion`, `mapeo`, `nodo_de_flujo` (el id BPMN del nodo; lo ves en `ver_flujo` como `bpmn`).
- Referencias por id que el MCP traduce solo: `subcategoriaId` acepta el codigo o nombre del CONCEPTO,
  `activityTypeId` el nombre del tipo de actividad, `assigneeUserId` el correo del usuario.
- Parametros por vinculo: `parametros_regla_en_campo` sobrescribe parametros SOLO en ese campo/formulario (reusar
  una regla en varios formularios con distinto mensaje, plantilla, etc.).

## Mantener

`listar_reglas`, `ver_regla` (donde esta vinculada), `reglas_de_formulario` (todo lo de un formulario),
`historial_regla` (si corrio y con que error), `parametros_regla`, `renombrar_regla`, `estado_regla`.
`eliminar_regla` solo funciona si nunca corrio; si ya corrio, desactivala (`estado_regla` inactiva).
