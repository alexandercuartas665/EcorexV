---
tipo: referencia
tema: formularios
generado: gen_referencia_codigo.py (no editar a mano)
---

# FO11 - Referencia de herramientas de reglas y opciones

Volver a [[00 - LEEME PRIMERO]]. Generada del codigo del servidor. 31 herramientas del MCP de tableros.

Marcas: (lectura) no cambia nada; (confirmar) quita, borra o reemplaza algo: confirma con el usuario.

## Descubrir reglas

### `listar_verbos_regla` (lectura)

Catalogo de VERBOS del motor de reglas (lo que una regla puede hacer: crear tarea, pasar campos, notificar, completar paso del flujo, imprimir, convertir a otro formulario...) con sus PARAMETROS (nombre, tipo, obligatorio, opciones). Leelo antes de crear_regla o regla_al_enviar.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `buscar` | string |  |  |
| `verbo` | string |  | Un verbo concreto (detalle completo) |

### `listar_reglas` (lectura)

Reglas del tenant (nombre, verbo, estado, documento). 'buscar' filtra por nombre, verbo o documento.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `buscar` | string |  |  |

### `ver_regla` (lectura)

Una regla completa: verbo, parametros, estado, donde esta vinculada (campos de formularios y nodos de flujo).

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `regla` | string | si | Id o nombre (ver listar_reglas) |

### `reglas_de_formulario` (lectura)

Todas las reglas de un formulario: las que corren AL ENVIAR y las de cada campo/boton (con sus parametros propios del vinculo, si los tiene).

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `formulario` | string | si | Id, codigo o titulo (ver list_forms) |

### `historial_regla` (lectura)

Ultimas ejecuciones de una regla (estado, registros afectados, error). Sirve para verificar que corrio.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `regla` | string | si | Id o nombre (ver listar_reglas) |
| `limite` | integer |  |  |

## Crear y mantener reglas (modulo Reglas)

### `crear_regla`

Crea una regla con un VERBO y sus parametros (valida que el verbo exista y los obligatorios). Con 'formulario' queda en el documento de reglas de ese formulario; si no, en 'documento' o en 'Reglas creadas por IA'. Nace ACTIVA salvo estado=desarrollo. Para que actue, vinculala: regla_en_campo (campo o boton) o agregar_regla_nodo. Para el ENVIO del formulario usa mejor regla_al_enviar.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `nombre` | string | si |  |
| `verbo` | string | si |  |
| `parametros` | object |  | Parametros del verbo (nombres de listar_verbos_regla) |
| `formulario` | string |  | Id, codigo o titulo (ver list_forms) |
| `documento` | string |  | Codigo o nombre de un documento de reglas existente |
| `descripcion` | string |  |  |
| `estado` | string (activa / desarrollo) |  |  |

### `parametros_regla`

REEMPLAZA los parametros de una regla (envia el objeto completo; valida contra el verbo).

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `regla` | string | si | Id o nombre (ver listar_reglas) |
| `parametros` | object | si | Parametros del verbo (nombres de listar_verbos_regla) |

### `estado_regla`

activa = corre; desarrollo = solo en prueba manual; inactiva = nunca corre.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `regla` | string | si | Id o nombre (ver listar_reglas) |
| `estado` | string (activa / desarrollo / inactiva) | si |  |

### `renombrar_regla`

Cambia el nombre de una regla (max 100).

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `regla` | string | si | Id o nombre (ver listar_reglas) |
| `nombre` | string | si |  |

### `eliminar_regla` (confirmar)

Borra una regla y sus vinculos. Solo si nunca se ha ejecutado (si tiene historial, usa estado_regla inactiva). Confirma con el usuario.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `regla` | string | si | Id o nombre (ver listar_reglas) |

## Reglas en un formulario

### `regla_al_enviar`

Al ENVIAR el formulario se ejecuta este verbo (ej. CREAR_TAREA, NOTIFICAR, GENERAR_TAREAS_DESDE_TABLA, COMPLETAR_PASO_FLUJO). Una regla por verbo y formulario: si ya existe, actualiza nombre y parametros. Corre tambien en los enlaces publicos /f/.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `formulario` | string | si | Id, codigo o titulo (ver list_forms) |
| `verbo` | string | si |  |
| `nombre` | string | si |  |
| `parametros` | object |  | Parametros del verbo (nombres de listar_verbos_regla) |

### `quitar_regla_al_enviar` (confirmar)

Quita una regla del envio (y la borra si nunca corrio). 'regla' = nombre, id o verbo. Confirma con el usuario.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `formulario` | string | si | Id, codigo o titulo (ver list_forms) |
| `regla` | string | si | Id o nombre (ver listar_reglas) |

### `regla_en_campo`

Vincula una regla existente a un CAMPO (corre al cambiar su valor) o a un BOTON (corre al pulsarlo; un boton es un campo de tipo Button; agregalo con apply_form_spec). Crea la regla antes con crear_regla.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `formulario` | string | si | Id, codigo o titulo (ver list_forms) |
| `campo` | string | si | Codigo del campo (field_code), id o etiqueta (ver get_form) |
| `regla` | string | si | Id o nombre (ver listar_reglas) |

### `quitar_regla_de_campo` (confirmar)

Desvincula una regla de un campo o boton (la regla sigue existiendo). Confirma con el usuario.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `formulario` | string | si | Id, codigo o titulo (ver list_forms) |
| `campo` | string | si | Codigo del campo (field_code), id o etiqueta (ver get_form) |
| `regla` | string | si | Id o nombre (ver listar_reglas) |

### `parametros_regla_en_campo`

Parametros PROPIOS de este vinculo (sobrescriben los de la regla solo en este campo/formulario; util para reusar una regla en varios formularios). Sin 'parametros' los quita.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `formulario` | string | si | Id, codigo o titulo (ver list_forms) |
| `campo` | string | si | Codigo del campo (field_code), id o etiqueta (ver get_form) |
| `regla` | string | si | Id o nombre (ver listar_reglas) |
| `parametros` | object |  | Parametros del verbo (nombres de listar_verbos_regla) |

### `condicion_entre_campos`

Regla de UI: si 'si_campo' cumple 'operador' ('valor'), entonces 'efecto' sobre 'campo' (ocultar, mostrar, requerir u opcional). Se evalua mientras se llena. Para solo ocultar/mostrar tambien sirve visible_when_json.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `formulario` | string | si | Id, codigo o titulo (ver list_forms) |
| `si_campo` | string | si |  |
| `operador` | string (igual / distinto / vacio / no_vacio) | si |  |
| `valor` | string |  |  |
| `efecto` | string (ocultar / mostrar / requerir / opcional) | si |  |
| `campo` | string | si | Codigo del campo (field_code), id o etiqueta (ver get_form) |

## Opciones del formulario

### `ocultar_barra_envio`

activo=true oculta Enviar/Imprimir/autoguardado cuando el formulario se llena dentro de la creacion de una tarea.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `formulario` | string | si | Id, codigo o titulo (ver list_forms) |
| `activo` | boolean | si |  |

### `formulario_reportable`

activo=true lo vuelve fuente de reportes (form:{codigo}) sin hacerlo modulo del menu.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `formulario` | string | si | Id, codigo o titulo (ver list_forms) |
| `activo` | boolean | si |  |

### `codigo_automatico_formulario`

Al crear un registro llena 'campo' con iniciales del asignado + consecutivo (ancho de 1 a 12). activo=false lo quita.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `formulario` | string | si | Id, codigo o titulo (ver list_forms) |
| `activo` | boolean | si |  |
| `campo` | string |  | Codigo del campo (field_code), id o etiqueta (ver get_form) |
| `ancho` | integer |  |  |

### `registro_editable`

activo=true: el registro queda siempre editable (autoguardado, nunca se cierra por envio).

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `formulario` | string | si | Id, codigo o titulo (ver list_forms) |
| `activo` | boolean | si |  |

### `cierre_automatico_formulario`

Al guardar, si 'campo' cumple 'operador' ('valor'), el registro se ENVIA y cierra solo (ej. al firmar). Sin 'campo' quita el cierre automatico.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `formulario` | string | si | Id, codigo o titulo (ver list_forms) |
| `campo` | string |  | Codigo del campo (field_code), id o etiqueta (ver get_form) |
| `operador` | string (no_vacio / vacio / igual / distinto / contiene) |  |  |
| `valor` | string |  |  |

## Versiones y enlaces

### `guardar_version_formulario`

Guarda una VERSION del formulario (estructura completa) para poder volver a ella. Hazlo antes de cambios grandes.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `formulario` | string | si | Id, codigo o titulo (ver list_forms) |
| `nombre` | string |  |  |

### `listar_versiones_formulario` (lectura)

Versiones guardadas del formulario (id, nombre, fecha).

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `formulario` | string | si | Id, codigo o titulo (ver list_forms) |

### `restaurar_version_formulario` (confirmar)

Vuelve el formulario a una version guardada ('version' = id de listar_versiones_formulario). Reemplaza la estructura actual: guarda una version antes y confirma con el usuario.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `formulario` | string | si | Id, codigo o titulo (ver list_forms) |
| `version` | string | si |  |

### `listar_enlaces_formulario` (lectura)

Enlaces publicos emitidos del formulario (create_share_link): vigencia, uso unico, usado, revocado.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `formulario` | string | si | Id, codigo o titulo (ver list_forms) |

### `revocar_enlace_formulario` (confirmar)

Revoca un enlace publico ('enlace' = id de listar_enlaces_formulario): deja de abrir. Confirma con el usuario.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `formulario` | string | si | Id, codigo o titulo (ver list_forms) |
| `enlace` | string | si |  |

## Propiedades de campo

### `campo_bloqueado`

activo=true: el campo se ve pero NO se puede editar (solo lectura para todos).

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `formulario` | string | si | Id, codigo o titulo (ver list_forms) |
| `campo` | string | si | Codigo del campo (field_code), id o etiqueta (ver get_form) |
| `activo` | boolean | si |  |

### `campo_oculto`

activo=true: el campo existe y guarda valor pero no se muestra (util para calculos o datos que pone una regla).

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `formulario` | string | si | Id, codigo o titulo (ver list_forms) |
| `campo` | string | si | Codigo del campo (field_code), id o etiqueta (ver get_form) |
| `activo` | boolean | si |  |

### `valor_por_defecto_dinamico`

Valor inicial que se calcula al abrir: hoy, usuario_actual, entidad_actual, usuario_asignado; ninguno lo quita.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `formulario` | string | si | Id, codigo o titulo (ver list_forms) |
| `campo` | string | si | Codigo del campo (field_code), id o etiqueta (ver get_form) |
| `valor` | string (ninguno / hoy / usuario_actual / entidad_actual / usuario_asignado) | si |  |

### `permisos_campo_por_rol`

Por ROL del usuario: 'ocultar_a' = roles que no lo ven; 'solo_lectura_a' = roles que lo ven sin editar. Roles: Owner, Admin, Supervisor, Advisor. Listas vacias = sin restriccion. Reemplaza lo anterior.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `formulario` | string | si | Id, codigo o titulo (ver list_forms) |
| `campo` | string | si | Codigo del campo (field_code), id o etiqueta (ver get_form) |
| `ocultar_a` | lista de string (Owner / Admin / Supervisor / Advisor) |  |  |
| `solo_lectura_a` | lista de string (Owner / Admin / Supervisor / Advisor) |  |  |

### `subformulario_de_campo`

En un campo de tipo Subform: el formulario HIJO (maestro-detalle) que se llena dentro. Sin 'subformulario' lo quita.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `formulario` | string | si | Id, codigo o titulo (ver list_forms) |
| `campo` | string | si | Codigo del campo (field_code), id o etiqueta (ver get_form) |
| `subformulario` | string |  | Formulario hijo (id, codigo o titulo) |

