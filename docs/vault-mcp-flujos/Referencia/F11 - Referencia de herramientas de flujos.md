---
tipo: referencia
tema: flujos
generado: gen_referencia_flujos.py (no editar a mano)
---

# F11 - Referencia de herramientas de flujos

Volver a [[00 - LEEME PRIMERO]]. Generada del codigo del servidor. 39 herramientas del MCP de tableros.

Marcas: (lectura) no cambia nada; (confirmar) quita o desvincula algo: confirma con el usuario.

## Descubrir

### `listar_flujos` (lectura)

Lista los flujos (procesos) del tenant con su estado, version, numero de nodos e instancias en marcha. Usala primero para conocer nombres, codigos e ids exactos.

Sin parametros.

### `ver_flujo` (lectura)

Muestra el grafo de un flujo: nodos (id, tipo, nombre, origen del encargado, cargos, formularios), aristas (id, desde, hasta, condicion) y AVISOS de revision (ramas paralelas involuntarias, nodos sin entrada o sin salida, pasos sin quien los atienda). 'flujo' = nombre, codigo o id (ver listar_flujos).

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `flujo` | string | si |  |

### `listar_reglas_disponibles` (lectura)

Catalogo de reglas del tenant (nombre, verbo, estado, documento) que se pueden poner en un nodo con agregar_regla_nodo.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `buscar` | string |  |  |

## Armar el grafo (solo borrador)

### `crear_flujo`

Crea un flujo NUEVO en borrador. Nace con un nodo de inicio y uno de fin YA CONECTADOS entre si. Para meter pasos usa agregar_nodo con desde_nodo_id=inicio y hasta_nodo_id=fin (inserta y reemplaza esa conexion). Devuelve 'definicion_id', nodos y aristas.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `nombre` | string | si |  |
| `categoria` | string |  |  |

### `importar_bpmn`

Crea un flujo nuevo en borrador a partir de un XML BPMN 2.0 (el que exporta bpmn.io o un archivo .bpmn). Devuelve 'definicion_id'.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `bpmn_xml` | string | si |  |

### `nueva_version_flujo`

Para MODIFICAR un flujo ya publicado: crea (o reutiliza) su version borrador siguiente y devuelve su 'definicion_id' con los ids NUEVOS de nodos y aristas (los vinculos se copian). Edita ese borrador y publicalo: los conceptos enganchados pasan solos a la version nueva.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `flujo` | string | si |  |

### `agregar_nodo`

Agrega un nodo a un flujo en borrador. 'tipo': tarea (paso humano), compuerta (decision con rutas), fin o inicio (solo hay uno). Con 'desde_nodo_id' y 'hasta_nodo_id' lo INSERTA entre esos dos nodos: conecta desde->nuevo->hasta y quita la conexion directa desde->hasta (conserva su condicion). Sin ellos queda suelto y hay que conectarlo con conectar_nodos. Devuelve 'nodo_id'.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `flujo` | string | si | definicion_id del borrador |
| `tipo` | string (tarea / compuerta / fin / inicio) | si |  |
| `nombre` | string |  |  |
| `desde_nodo_id` | string |  | Insertar despues de este nodo |
| `hasta_nodo_id` | string |  | Insertar antes de este nodo |
| `x` | integer |  |  |
| `y` | integer |  |  |

### `renombrar_nodo`

Cambia el nombre de un nodo (ver 'nodo_id' en ver_flujo).

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `nodo_id` | string | si |  |
| `nombre` | string | si |  |

### `conectar_nodos`

Conecta dos nodos del mismo flujo en borrador (de 'desde_nodo_id' a 'hasta_nodo_id'). OJO: un nodo que no es compuerta sigue TODAS sus salidas en paralelo. En salidas de compuerta: 'nombre' = la opcion que se elige (ej. Aprobada) y 'condicion' = approval == 'Aprobada' (vacia = ruta por defecto). Devuelve 'arista_id'.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `desde_nodo_id` | string | si |  |
| `hasta_nodo_id` | string | si |  |
| `nombre` | string |  | Nombre de la salida (opcion de la decision) |
| `condicion` | string |  |  |

### `nombre_salida`

Pone (o quita) el NOMBRE de una salida (arista). En una compuerta AUTOMATICA los nombres de sus salidas son las OPCIONES que elige quien cierra el paso anterior (ej. Aprobada / Rechazada) y cada salida lleva la condicion approval == '<nombre>' (condicion_ruta); en el diagrama es la etiqueta de la flecha. 'arista_id' de ver_flujo. Solo en borrador.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `arista_id` | string | si |  |
| `nombre` | string |  |  |

### `condicion_ruta`

Fija (o limpia) la condicion de una arista/salida de compuerta. 'arista_id' de ver_flujo; 'condicion' vacia = ruta por defecto.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `arista_id` | string | si |  |
| `condicion` | string |  |  |

### `eliminar_conexion`

Quita una conexion (arista) de un flujo en borrador. 'arista_id' de ver_flujo.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `arista_id` | string | si |  |

### `eliminar_nodo` (confirmar)

Borra un nodo del flujo en borrador con sus aristas y vinculos (NUNCA el nodo de inicio). Confirma con el usuario antes de usarla.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `nodo_id` | string | si |  |

## Decision, encargado y ciclo

### `decision_humana_nodo`

En una COMPUERTA o un FIN: activo=true hace que una PERSONA (el encargado del nodo) elija la ruta o confirme el cierre; activo=false (defecto del motor) = la compuerta se resuelve SOLA por las condiciones de sus salidas. En una TAREA equivale a 'permite asignacion manual del paso'. Solo en borrador (nueva_version_flujo). Despues define quien decide con asignar_encargado_nodo / asignar_cargo_nodo.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `nodo_id` | string | si | Id del nodo (ver ver_flujo) |
| `activo` | boolean | si |  |

### `asignar_encargado_nodo`

Define de donde sale el ENCARGADO de un nodo. 'origen': politica (por cargo/dependencia: asignalos con asignar_cargo_nodo), hereda_inicio (quien arranco el flujo), hereda_anterior (quien cerro el paso previo) o campo_formulario (toma el usuario de un campo; indica 'campo' con su codigo).

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `nodo_id` | string | si |  |
| `origen` | string (politica / hereda_inicio / hereda_anterior / campo_formulario) | si |  |
| `campo` | string |  | Codigo del campo (solo para campo_formulario) |

### `asignar_cargo_nodo`

Agrega (accion='agregar', por defecto) o quita (accion='quitar') un CARGO o DEPENDENCIA del organigrama que atiende el nodo (origen 'politica'). 'cargo' = id o nombre; si no existe responde las opciones validas.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `nodo_id` | string | si |  |
| `cargo` | string | si |  |
| `accion` | string (agregar / quitar) |  |  |

### `reinicio_nodo`

Al pasar por este nodo el flujo ABRE UN CICLO NUEVO en otro nodo (p.ej. 'Corregir' vuelve a 'Revision'). 'reiniciar_en_nodo_id' = nodo destino del mismo flujo; omitelo para quitar el reinicio. Solo en borrador.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `nodo_id` | string | si | Id del nodo (ver ver_flujo) |
| `reiniciar_en_nodo_id` | string |  |  |

## Formularios del nodo

### `poner_formulario_a_nodo`

Vincula un formulario ACTIVO a un nodo (se diligencia al llegar el paso). 'formulario' = id, codigo o titulo (ver list_forms). 'obligatorio' true = el paso no se cierra hasta enviarlo (ADR-0077).

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `nodo_id` | string | si |  |
| `formulario` | string | si |  |
| `obligatorio` | boolean |  |  |

### `formulario_obligatorio_por_salida`

En una COMPUERTA: el formulario del nodo es obligatorio SOLO si se elige alguna de estas salidas ('salidas' = ids de los nodos destino). Lista vacia = no obligatorio en ninguna. El formulario debe estar ya en el nodo (poner_formulario_a_nodo).

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `nodo_id` | string | si | Id del nodo (ver ver_flujo) |
| `formulario` | string | si |  |
| `salidas` | lista de string | si |  |

### `formulario_crear_al_llegar`

activo=true (defecto) = el formulario del nodo se crea solo al llegar el paso; false = queda para agregarlo a mano.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `nodo_id` | string | si | Id del nodo (ver ver_flujo) |
| `formulario` | string | si |  |
| `activo` | boolean | si |  |

### `quitar_formulario_nodo` (confirmar)

Desvincula un formulario del nodo (no borra el formulario ni sus respuestas). Confirma con el usuario.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `nodo_id` | string | si | Id del nodo (ver ver_flujo) |
| `formulario` | string | si |  |

## Avisos

### `agregar_notificacion_nodo`

AGREGA una regla de aviso que se dispara al LLEGAR el paso. 'canal': correo | whatsapp (plantilla aprobada; linea_id + plantilla) | whatsapp_grupo (linea_grupo_id + grupo_jid) | telegram (chat_id). 'destino' (correo y whatsapp): encargado_paso (defecto), usuario (+ 'usuario' = correo o id) o contacto_tarea (el cliente). 'mensaje'/'asunto' admiten tokens {tarea.x}, {form.x}, {sistema.fecha}. 'enlaces_decision' (uno por salida de una compuerta) envia al cliente un enlace publico /d/... que, al abrirlo, ELIGE esa salida; cada uno llena la variable indicada del mensaje/plantilla. Devuelve el numero de la regla (para quitarla).

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `nodo_id` | string | si |  |
| `canal` | string (correo / whatsapp / whatsapp_grupo / telegram) | si |  |
| `destino` | string (encargado_paso / usuario / contacto_tarea) |  |  |
| `usuario` | string |  | Correo o id del usuario (destino=usuario) |
| `asunto` | string |  |  |
| `mensaje` | string |  |  |
| `incluir_enlace` | boolean |  | Agrega el enlace a la tarea al final (no en whatsapp plantilla) |
| `linea_id` | string |  | Linea WhatsApp (plantillas) |
| `plantilla` | string |  | Nombre de la plantilla HSM aprobada |
| `idioma` | string |  | Idioma de la plantilla, ej. es |
| `variables` | object |  | Variable de la plantilla -> texto con tokens |
| `linea_grupo_id` | string |  |  |
| `grupo_jid` | string |  | ...@g.us |
| `chat_id` | string |  |  |
| `adjuntar_pdf_formulario` | string |  | Formulario (id, codigo o titulo) cuyo PDF se adjunta |
| `nota_agente` | string |  | Nota interna para el agente conversacional (no se envia al cliente) |
| `enlaces_decision` | lista de objetos |  |  |
| `enlaces_decision[].variable` | string | si | Variable del mensaje/plantilla que recibe la URL |
| `enlaces_decision[].salida_nodo_id` | string | si | Nodo destino de la salida que elige este enlace |
| `enlaces_decision[].captura` | string (ninguna / firma / observacion) |  |  |
| `enlaces_decision[].observacion_obligatoria` | boolean |  |  |
| `enlaces_decision[].boton` | string |  | Texto del boton en la pagina |
| `enlaces_decision[].vence_horas` | integer |  |  |
| `enlaces_decision[].etiqueta` | string |  | Etiqueta de tarea a aplicar si el cliente elige esta salida |

### `quitar_notificacion_nodo` (confirmar)

Quita UNA regla de aviso del nodo por su numero (1, 2... como las lista ver_flujo). Confirma con el usuario.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `nodo_id` | string | si | Id del nodo (ver ver_flujo) |
| `numero` | integer | si |  |

## Tiempos, tablero y cierre

### `plazo_nodo`

PLAZO del paso (tiempo para entregarlo): con el se calcula su vencimiento y los avisos de atraso. Todo en 0 o sin valores = sin plazo. Editable aun publicado.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `nodo_id` | string | si | Id del nodo (ver ver_flujo) |
| `dias` | integer |  |  |
| `horas` | integer |  |  |
| `minutos` | integer |  |  |
| `dias_habiles` | boolean |  | true = los dias saltan fines de semana y festivos del tenant; false (defecto) = dias calendario |

### `tiempo_arranque_nodo`

TIEMPO PARA ARRANCAR el paso: espera antes de que el paso quede activo (p.ej. 'llamar 2 dias despues'). Todo en 0 = inmediato. Mismo formato que plazo_nodo.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `nodo_id` | string | si | Id del nodo (ver ver_flujo) |
| `dias` | integer |  |  |
| `horas` | integer |  |  |
| `minutos` | integer |  |  |
| `dias_habiles` | boolean |  | true = los dias saltan fines de semana y festivos del tenant; false (defecto) = dias calendario |

### `tablero_destino_nodo`

Al LLEGAR a este paso la actividad salta a ese tablero y columna. 'tablero' = id, codigo o nombre; 'columna' = id o nombre (omitida = primera columna). Sin 'tablero' quita el destino. Si la columna es de cierre, fija despues el concepto con concepto_cierre_nodo. Editable aun publicado.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `nodo_id` | string | si | Id del nodo (ver ver_flujo) |
| `tablero` | string |  |  |
| `columna` | string |  |  |

### `concepto_cierre_nodo`

Concepto (motivo) de cierre que el flujo estampa al llevar la actividad a la columna de CIERRE del nodo. Debe ser uno de los conceptos de cierre del tablero destino (si no, la respuesta lista los validos). Sin 'concepto' lo quita. Requiere tablero_destino_nodo con una columna de cierre.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `nodo_id` | string | si | Id del nodo (ver ver_flujo) |
| `concepto` | string |  |  |

### `color_nodo`

Color del nodo en el diagrama (solo visual). Sin 'color' lo quita. Editable aun publicado.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `nodo_id` | string | si | Id del nodo (ver ver_flujo) |
| `color` | string (violet / blue / green / amber / rose / slate) |  |  |

### `nota_nodo`

Nota adhesiva (post-it) junto al nodo en el diagrama, para explicar el paso (max 1000). Sin 'nota' la quita.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `nodo_id` | string | si | Id del nodo (ver ver_flujo) |
| `nota` | string |  |  |

## Reglas y encadenamiento

### `agregar_regla_nodo`

Vincula una regla existente al nodo (se ejecuta en ese paso). 'regla' = id o nombre (listar_reglas_disponibles). 'autonoma' true = actua sola sin que la persona la dispare.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `nodo_id` | string | si | Id del nodo (ver ver_flujo) |
| `regla` | string | si |  |
| `autonoma` | boolean |  |  |

### `regla_autonoma_nodo`

Marca si una regla del nodo actua sola (true) o la dispara la persona (false).

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `nodo_id` | string | si | Id del nodo (ver ver_flujo) |
| `regla` | string | si |  |
| `autonoma` | boolean | si |  |

### `quitar_regla_nodo` (confirmar)

Desvincula una regla del nodo (la regla sigue existiendo). Confirma con el usuario.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `nodo_id` | string | si | Id del nodo (ver ver_flujo) |
| `regla` | string | si |  |

### `salto_a_flujo_nodo`

Al llegar a este nodo se arranca OTRO flujo (crea una tarea hija con ese proceso). 'flujo_destino' = id, codigo o nombre de otro flujo publicado; omitelo para quitar el salto.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `nodo_id` | string | si | Id del nodo (ver ver_flujo) |
| `flujo_destino` | string |  |  |

## Publicar y ciclo de vida

### `publicar_flujo`

Publica un flujo (deja activa esta version y despublica otras del mismo proceso). Antes revisa el grafo: si hay AVISOS no publica y los devuelve; muestraselos al usuario y solo con su visto bueno repite con forzar=true. Solo un flujo publicado arranca al crear tareas.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `flujo` | string | si |  |
| `forzar` | boolean |  | Publicar aunque haya avisos (solo con visto bueno del usuario) |

### `enganchar_flujo_a_concepto`

Asocia un flujo a un concepto (tipo de actividad): las tareas nuevas de ese concepto ARRANCAN el flujo. 'concepto' = codigo o nombre (ver listar_conceptos); 'flujo' = id, codigo o nombre.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `concepto` | string | si |  |
| `flujo` | string | si |  |

### `desenganchar_flujo_de_concepto` (confirmar)

Quita el flujo de un concepto: las tareas NUEVAS de ese concepto ya no lo arrancan (las que estan en marcha siguen). 'concepto' = codigo o nombre. Confirma con el usuario.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `concepto` | string | si |  |

### `editar_flujo`

Cambia nombre, categoria o descripcion del flujo (solo lo que envies).

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `flujo` | string | si |  |
| `nombre` | string |  |  |
| `categoria` | string |  |  |
| `descripcion` | string |  |  |

### `pausar_flujo`

Pausa un flujo publicado: no arranca en tareas NUEVAS (las que estan en marcha siguen). Se revierte con reanudar_flujo.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `flujo` | string | si |  |

### `reanudar_flujo`

Reanuda un flujo pausado.

| Parametro | Tipo | Obligatorio | Nota |
|---|---|---|---|
| `flujo` | string | si |  |

