---
tipo: referencia
servidor: ecorex-tableros
modulo: flujos
herramientas: 53
fuente: catalogo vivo tools/list (generado)
---

# MCP Tableros - Flujos de proceso (BPMN)

Servidor: `/api/mcp/tableros` - ver [[01 - Conectar los MCP]]. Volver a [[00 - LEEME PRIMERO]].

## Para que sirve

Diseno de procesos (WorkflowEngine BPMN 2.0): nodos tarea (paso humano), compuertas (decision), inicio y fin.

> Para disenar procesos reales usa el CEREBRO DE FLUJOS: `guia_ecorex` con `tema` = `flujos` (decision humana vs
> compuerta automatica, avisos con enlaces de decision, plazos, tablero y cierre, agente de IA del paso).

Reglas:
- `crear_flujo` nace con inicio->fin YA conectados. Inserta pasos con `agregar_nodo` dando `desde_nodo_id` y
  `hasta_nodo_id` (reemplaza esa conexion directa).
- Un nodo que NO es compuerta sigue TODAS sus salidas en paralelo. Para decidir usa una **compuerta**: automatica
  (cada salida con `nombre_salida` + `condicion_ruta` approval == '<nombre>'; una sin condicion = por defecto) o de
  decision humana (`decision_humana_nodo`).
- Encargado del paso: `asignar_encargado_nodo` (politica/hereda_inicio/hereda_anterior/campo_formulario); con
  `politica` agrega cargos con `asignar_cargo_nodo`.
- Revisa `ver_flujo`: si trae **avisos**, muestraselos al usuario antes de `publicar_flujo`.
- Un flujo publicado no se edita: usa `nueva_version_flujo`.
- Para que las tareas lo arranquen: `enganchar_flujo_a_concepto`.

Recetas: [[R03 - Disenar y publicar un flujo]]

## Herramientas (53)

- **Lectura:** `listar_flujos`, `ver_flujo`, `listar_reglas_disponibles`, `opciones_agente_nodo`, `ver_agente_nodo`
- **Escritura:** `crear_flujo`, `nueva_version_flujo`, `agregar_nodo`, `renombrar_nodo`, `conectar_nodos`, `eliminar_conexion`, `condicion_ruta`, `asignar_encargado_nodo`, `asignar_cargo_nodo`, `poner_formulario_a_nodo`, `importar_bpmn`, `publicar_flujo`, `enganchar_flujo_a_concepto`, `nombre_salida`, `decision_humana_nodo`, `reinicio_nodo`, `tablero_destino_nodo`, `concepto_cierre_nodo`, `color_nodo`, `nota_nodo`, `salto_a_flujo_nodo`, `plazo_nodo`, `tiempo_arranque_nodo`, `agregar_notificacion_nodo`, `formulario_obligatorio_por_salida`, `formulario_crear_al_llegar`, `agregar_regla_nodo`, `regla_autonoma_nodo`, `editar_flujo`, `pausar_flujo`, `reanudar_flujo`, `agente_nodo`, `instrucciones_agente_nodo`, `correo_agente_nodo`, `web_agente_nodo`, `whatsapp_agente_nodo`, `voz_agente_nodo`, `canales_agente_nodo`, `fallo_agente_nodo`, `seguimiento_agente_nodo`, `tope_preguntas_agente_nodo`, `traspaso_salida_agente_nodo`
- **Destructivas:** `eliminar_nodo`, `quitar_notificacion_nodo`, `quitar_formulario_nodo`, `quitar_regla_nodo`, `desenganchar_flujo_de_concepto`, `quitar_agente_nodo`

### `listar_flujos`

**Tipo:** Lectura

> Lista los flujos (procesos) del tenant con su estado, version, numero de nodos e instancias en marcha. Usala primero para conocer nombres, codigos e ids exactos.

_Sin parametros._

Llamada minima (solo obligatorios):

```json
{
  "name": "listar_flujos",
  "arguments": {}
}
```

### `ver_flujo`

**Tipo:** Lectura

> Muestra el grafo de un flujo: nodos (id, tipo, nombre, origen del encargado, cargos, formularios), aristas (id, desde, hasta, condicion) y AVISOS de revision (ramas paralelas involuntarias, nodos sin entrada o sin salida, pasos sin quien los atienda). 'flujo' = nombre, codigo o id (ver listar_flujos).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `flujo` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "ver_flujo",
  "arguments": {
    "flujo": "<flujo>"
  }
}
```

### `crear_flujo`

**Tipo:** Escritura

> Crea un flujo NUEVO en borrador. Nace con un nodo de inicio y uno de fin YA CONECTADOS entre si. Para meter pasos usa agregar_nodo con desde_nodo_id=inicio y hasta_nodo_id=fin (inserta y reemplaza esa conexion). Devuelve 'definicion_id', nodos y aristas.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nombre` | string | SI |  |
| `categoria` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "crear_flujo",
  "arguments": {
    "nombre": "<nombre>"
  }
}
```

### `nueva_version_flujo`

**Tipo:** Escritura

> Para MODIFICAR un flujo ya publicado: crea (o reutiliza) su version borrador siguiente y devuelve su 'definicion_id' con los ids NUEVOS de nodos y aristas (los vinculos se copian). Edita ese borrador y publicalo: los conceptos enganchados pasan solos a la version nueva.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `flujo` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "nueva_version_flujo",
  "arguments": {
    "flujo": "<flujo>"
  }
}
```

### `agregar_nodo`

**Tipo:** Escritura

> Agrega un nodo a un flujo en borrador. 'tipo': tarea (paso humano), compuerta (decision con rutas), fin o inicio (solo hay uno). Con 'desde_nodo_id' y 'hasta_nodo_id' lo INSERTA entre esos dos nodos: conecta desde->nuevo->hasta y quita la conexion directa desde->hasta (conserva su condicion). Sin ellos queda suelto y hay que conectarlo con conectar_nodos. Devuelve 'nodo_id'.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `flujo` | string | SI | definicion_id del borrador |
| `tipo` | string (tarea / compuerta / fin / inicio) | SI |  |
| `nombre` | string |  |  |
| `desde_nodo_id` | string |  | Insertar despues de este nodo |
| `hasta_nodo_id` | string |  | Insertar antes de este nodo |
| `x` | integer |  |  |
| `y` | integer |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "agregar_nodo",
  "arguments": {
    "flujo": "<flujo>",
    "tipo": "tarea"
  }
}
```

Ejemplo real (valores genericos):

```json
{
  "name": "agregar_nodo",
  "arguments": {
    "flujo": "<definicion_id>",
    "tipo": "tarea",
    "nombre": "Revisar solicitud",
    "desde_nodo_id": "<id_inicio>",
    "hasta_nodo_id": "<id_fin>"
  }
}
```

### `renombrar_nodo`

**Tipo:** Escritura

> Cambia el nombre de un nodo (ver 'nodo_id' en ver_flujo).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI |  |
| `nombre` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "renombrar_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>",
    "nombre": "<nombre>"
  }
}
```

### `conectar_nodos`

**Tipo:** Escritura

> Conecta dos nodos del mismo flujo en borrador (de 'desde_nodo_id' a 'hasta_nodo_id'). OJO: un nodo que no es compuerta sigue TODAS sus salidas en paralelo. En salidas de compuerta: 'nombre' = la opcion que se elige (ej. Aprobada) y 'condicion' = approval == 'Aprobada' (vacia = ruta por defecto). Devuelve 'arista_id'.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `desde_nodo_id` | string | SI |  |
| `hasta_nodo_id` | string | SI |  |
| `nombre` | string |  | Nombre de la salida (opcion de la decision) |
| `condicion` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "conectar_nodos",
  "arguments": {
    "desde_nodo_id": "<desde_nodo_id>",
    "hasta_nodo_id": "<hasta_nodo_id>"
  }
}
```

### `eliminar_conexion`

**Tipo:** Escritura

> Quita una conexion (arista) de un flujo en borrador. 'arista_id' de ver_flujo.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `arista_id` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "eliminar_conexion",
  "arguments": {
    "arista_id": "<arista_id>"
  }
}
```

### `condicion_ruta`

**Tipo:** Escritura

> Fija (o limpia) la condicion de una arista/salida de compuerta. 'arista_id' de ver_flujo; 'condicion' vacia = ruta por defecto.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `arista_id` | string | SI |  |
| `condicion` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "condicion_ruta",
  "arguments": {
    "arista_id": "<arista_id>"
  }
}
```

### `asignar_encargado_nodo`

**Tipo:** Escritura

> Define de donde sale el ENCARGADO de un nodo. 'origen': politica (por cargo/dependencia: asignalos con asignar_cargo_nodo), hereda_inicio (quien arranco el flujo), hereda_anterior (quien cerro el paso previo) o campo_formulario (toma el usuario de un campo; indica 'campo' con su codigo).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI |  |
| `origen` | string (politica / hereda_inicio / hereda_anterior / campo_formulario) | SI |  |
| `campo` | string |  | Codigo del campo (solo para campo_formulario) |

Llamada minima (solo obligatorios):

```json
{
  "name": "asignar_encargado_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>",
    "origen": "politica"
  }
}
```

### `asignar_cargo_nodo`

**Tipo:** Escritura

> Agrega (accion='agregar', por defecto) o quita (accion='quitar') un CARGO o DEPENDENCIA del organigrama que atiende el nodo (origen 'politica'). 'cargo' = id o nombre; si no existe responde las opciones validas.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI |  |
| `cargo` | string | SI |  |
| `accion` | string (agregar / quitar) |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "asignar_cargo_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>",
    "cargo": "<cargo>"
  }
}
```

### `poner_formulario_a_nodo`

**Tipo:** Escritura

> Vincula un formulario ACTIVO a un nodo (se diligencia al llegar el paso). 'formulario' = id, codigo o titulo (ver list_forms). 'obligatorio' true = el paso no se cierra hasta enviarlo (ADR-0077).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI |  |
| `formulario` | string | SI |  |
| `obligatorio` | boolean |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "poner_formulario_a_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>",
    "formulario": "<formulario>"
  }
}
```

### `eliminar_nodo`

**Tipo:** DESTRUCTIVA (confirma con el usuario)

> Borra un nodo del flujo en borrador con sus aristas y vinculos (NUNCA el nodo de inicio). Confirma con el usuario antes de usarla.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "eliminar_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>"
  }
}
```

### `importar_bpmn`

**Tipo:** Escritura

> Crea un flujo nuevo en borrador a partir de un XML BPMN 2.0 (el que exporta bpmn.io o un archivo .bpmn). Devuelve 'definicion_id'.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `bpmn_xml` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "importar_bpmn",
  "arguments": {
    "bpmn_xml": "<bpmn_xml>"
  }
}
```

### `publicar_flujo`

**Tipo:** Escritura

> Publica un flujo (deja activa esta version y despublica otras del mismo proceso). Antes revisa el grafo: si hay AVISOS no publica y los devuelve; muestraselos al usuario y solo con su visto bueno repite con forzar=true. Solo un flujo publicado arranca al crear tareas.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `flujo` | string | SI |  |
| `forzar` | boolean |  | Publicar aunque haya avisos (solo con visto bueno del usuario) |

Llamada minima (solo obligatorios):

```json
{
  "name": "publicar_flujo",
  "arguments": {
    "flujo": "<flujo>"
  }
}
```

### `enganchar_flujo_a_concepto`

**Tipo:** Escritura

> Asocia un flujo a un concepto (tipo de actividad): las tareas nuevas de ese concepto ARRANCAN el flujo. 'concepto' = codigo o nombre (ver listar_conceptos); 'flujo' = id, codigo o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `concepto` | string | SI |  |
| `flujo` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "enganchar_flujo_a_concepto",
  "arguments": {
    "concepto": "<concepto>",
    "flujo": "<flujo>"
  }
}
```

### `nombre_salida`

**Tipo:** Escritura

> Pone (o quita) el NOMBRE de una salida (arista). En una compuerta AUTOMATICA los nombres de sus salidas son las OPCIONES que elige quien cierra el paso anterior (ej. Aprobada / Rechazada) y cada salida lleva la condicion approval == '<nombre>' (condicion_ruta); en el diagrama es la etiqueta de la flecha. 'arista_id' de ver_flujo. Solo en borrador.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `arista_id` | string | SI |  |
| `nombre` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "nombre_salida",
  "arguments": {
    "arista_id": "<arista_id>"
  }
}
```

### `decision_humana_nodo`

**Tipo:** Escritura

> En una COMPUERTA o un FIN: activo=true hace que una PERSONA (el encargado del nodo) elija la ruta o confirme el cierre; activo=false (defecto del motor) = la compuerta se resuelve SOLA por las condiciones de sus salidas. En una TAREA equivale a 'permite asignacion manual del paso'. Solo en borrador (nueva_version_flujo). Despues define quien decide con asignar_encargado_nodo / asignar_cargo_nodo.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI | Id del nodo (ver ver_flujo) |
| `activo` | boolean | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "decision_humana_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>",
    "activo": true
  }
}
```

### `reinicio_nodo`

**Tipo:** Escritura

> Al pasar por este nodo el flujo ABRE UN CICLO NUEVO en otro nodo (p.ej. 'Corregir' vuelve a 'Revision'). 'reiniciar_en_nodo_id' = nodo destino del mismo flujo; omitelo para quitar el reinicio. Solo en borrador.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI | Id del nodo (ver ver_flujo) |
| `reiniciar_en_nodo_id` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "reinicio_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>"
  }
}
```

### `tablero_destino_nodo`

**Tipo:** Escritura

> Al LLEGAR a este paso la actividad salta a ese tablero y columna. 'tablero' = id, codigo o nombre; 'columna' = id o nombre (omitida = primera columna). Sin 'tablero' quita el destino. Si la columna es de cierre, fija despues el concepto con concepto_cierre_nodo. Editable aun publicado.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI | Id del nodo (ver ver_flujo) |
| `tablero` | string |  |  |
| `columna` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "tablero_destino_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>"
  }
}
```

### `concepto_cierre_nodo`

**Tipo:** Escritura

> Concepto (motivo) de cierre que el flujo estampa al llevar la actividad a la columna de CIERRE del nodo. Debe ser uno de los conceptos de cierre del tablero destino (si no, la respuesta lista los validos). Sin 'concepto' lo quita. Requiere tablero_destino_nodo con una columna de cierre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI | Id del nodo (ver ver_flujo) |
| `concepto` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "concepto_cierre_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>"
  }
}
```

### `color_nodo`

**Tipo:** Escritura

> Color del nodo en el diagrama (solo visual). Sin 'color' lo quita. Editable aun publicado.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI | Id del nodo (ver ver_flujo) |
| `color` | string (violet / blue / green / amber / rose / slate) |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "color_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>"
  }
}
```

### `nota_nodo`

**Tipo:** Escritura

> Nota adhesiva (post-it) junto al nodo en el diagrama, para explicar el paso (max 1000). Sin 'nota' la quita.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI | Id del nodo (ver ver_flujo) |
| `nota` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "nota_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>"
  }
}
```

### `salto_a_flujo_nodo`

**Tipo:** Escritura

> Al llegar a este nodo se arranca OTRO flujo (crea una tarea hija con ese proceso). 'flujo_destino' = id, codigo o nombre de otro flujo publicado; omitelo para quitar el salto.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI | Id del nodo (ver ver_flujo) |
| `flujo_destino` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "salto_a_flujo_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>"
  }
}
```

### `plazo_nodo`

**Tipo:** Escritura

> PLAZO del paso (tiempo para entregarlo): con el se calcula su vencimiento y los avisos de atraso. Todo en 0 o sin valores = sin plazo. Editable aun publicado.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI | Id del nodo (ver ver_flujo) |
| `dias` | integer >=0 |  |  |
| `horas` | integer >=0 |  |  |
| `minutos` | integer >=0 |  |  |
| `dias_habiles` | boolean |  | true = los dias saltan fines de semana y festivos del tenant; false (defecto) = dias calendario |

Llamada minima (solo obligatorios):

```json
{
  "name": "plazo_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>"
  }
}
```

### `tiempo_arranque_nodo`

**Tipo:** Escritura

> TIEMPO PARA ARRANCAR el paso: espera antes de que el paso quede activo (p.ej. 'llamar 2 dias despues'). Todo en 0 = inmediato. Mismo formato que plazo_nodo.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI | Id del nodo (ver ver_flujo) |
| `dias` | integer >=0 |  |  |
| `horas` | integer >=0 |  |  |
| `minutos` | integer >=0 |  |  |
| `dias_habiles` | boolean |  | true = los dias saltan fines de semana y festivos del tenant; false (defecto) = dias calendario |

Llamada minima (solo obligatorios):

```json
{
  "name": "tiempo_arranque_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>"
  }
}
```

### `agregar_notificacion_nodo`

**Tipo:** Escritura

> AGREGA una regla de aviso que se dispara al LLEGAR el paso. 'canal': correo | whatsapp (plantilla aprobada; linea_id + plantilla) | whatsapp_grupo (linea_grupo_id + grupo_jid) | telegram (chat_id). 'destino' (correo y whatsapp): encargado_paso (defecto), usuario (+ 'usuario' = correo o id) o contacto_tarea (el cliente). 'mensaje'/'asunto' admiten tokens {tarea.x}, {form.x}, {sistema.fecha}. 'enlaces_decision' (uno por salida de una compuerta) envia al cliente un enlace publico /d/... que, al abrirlo, ELIGE esa salida; cada uno llena la variable indicada del mensaje/plantilla. Devuelve el numero de la regla (para quitarla).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI |  |
| `canal` | string (correo / whatsapp / whatsapp_grupo / telegram) | SI |  |
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
| `enlaces_decision` | array de objetos |  |  |
| `enlaces_decision[].variable` | string | SI | Variable del mensaje/plantilla que recibe la URL |
| `enlaces_decision[].salida_nodo_id` | string | SI | Nodo destino de la salida que elige este enlace |
| `enlaces_decision[].captura` | string (ninguna / firma / observacion) |  |  |
| `enlaces_decision[].observacion_obligatoria` | boolean |  |  |
| `enlaces_decision[].boton` | string |  | Texto del boton en la pagina |
| `enlaces_decision[].vence_horas` | integer >=1 |  |  |
| `enlaces_decision[].etiqueta` | string |  | Etiqueta de tarea a aplicar si el cliente elige esta salida |

Llamada minima (solo obligatorios):

```json
{
  "name": "agregar_notificacion_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>",
    "canal": "correo"
  }
}
```

### `quitar_notificacion_nodo`

**Tipo:** DESTRUCTIVA (confirma con el usuario)

> Quita UNA regla de aviso del nodo por su numero (1, 2... como las lista ver_flujo). Confirma con el usuario.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI | Id del nodo (ver ver_flujo) |
| `numero` | integer >=1 | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "quitar_notificacion_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>",
    "numero": 1
  }
}
```

### `quitar_formulario_nodo`

**Tipo:** DESTRUCTIVA (confirma con el usuario)

> Desvincula un formulario del nodo (no borra el formulario ni sus respuestas). Confirma con el usuario.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI | Id del nodo (ver ver_flujo) |
| `formulario` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "quitar_formulario_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>",
    "formulario": "<formulario>"
  }
}
```

### `formulario_obligatorio_por_salida`

**Tipo:** Escritura

> En una COMPUERTA: el formulario del nodo es obligatorio SOLO si se elige alguna de estas salidas ('salidas' = ids de los nodos destino). Lista vacia = no obligatorio en ninguna. El formulario debe estar ya en el nodo (poner_formulario_a_nodo).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI | Id del nodo (ver ver_flujo) |
| `formulario` | string | SI |  |
| `salidas` | array de string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "formulario_obligatorio_por_salida",
  "arguments": {
    "nodo_id": "<nodo_id>",
    "formulario": "<formulario>",
    "salidas": [
      "<salidas>"
    ]
  }
}
```

### `formulario_crear_al_llegar`

**Tipo:** Escritura

> activo=true (defecto) = el formulario del nodo se crea solo al llegar el paso; false = queda para agregarlo a mano.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI | Id del nodo (ver ver_flujo) |
| `formulario` | string | SI |  |
| `activo` | boolean | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "formulario_crear_al_llegar",
  "arguments": {
    "nodo_id": "<nodo_id>",
    "formulario": "<formulario>",
    "activo": true
  }
}
```

### `listar_reglas_disponibles`

**Tipo:** Lectura

> Catalogo de reglas del tenant (nombre, verbo, estado, documento) que se pueden poner en un nodo con agregar_regla_nodo.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `buscar` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "listar_reglas_disponibles",
  "arguments": {}
}
```

### `agregar_regla_nodo`

**Tipo:** Escritura

> Vincula una regla existente al nodo (se ejecuta en ese paso). 'regla' = id o nombre (listar_reglas_disponibles). 'autonoma' true = actua sola sin que la persona la dispare.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI | Id del nodo (ver ver_flujo) |
| `regla` | string | SI |  |
| `autonoma` | boolean |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "agregar_regla_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>",
    "regla": "<regla>"
  }
}
```

### `quitar_regla_nodo`

**Tipo:** DESTRUCTIVA (confirma con el usuario)

> Desvincula una regla del nodo (la regla sigue existiendo). Confirma con el usuario.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI | Id del nodo (ver ver_flujo) |
| `regla` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "quitar_regla_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>",
    "regla": "<regla>"
  }
}
```

### `regla_autonoma_nodo`

**Tipo:** Escritura

> Marca si una regla del nodo actua sola (true) o la dispara la persona (false).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI | Id del nodo (ver ver_flujo) |
| `regla` | string | SI |  |
| `autonoma` | boolean | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "regla_autonoma_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>",
    "regla": "<regla>",
    "autonoma": true
  }
}
```

### `editar_flujo`

**Tipo:** Escritura

> Cambia nombre, categoria o descripcion del flujo (solo lo que envies).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `flujo` | string | SI |  |
| `nombre` | string |  |  |
| `categoria` | string |  |  |
| `descripcion` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "editar_flujo",
  "arguments": {
    "flujo": "<flujo>"
  }
}
```

### `pausar_flujo`

**Tipo:** Escritura

> Pausa un flujo publicado: no arranca en tareas NUEVAS (las que estan en marcha siguen). Se revierte con reanudar_flujo.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `flujo` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "pausar_flujo",
  "arguments": {
    "flujo": "<flujo>"
  }
}
```

### `reanudar_flujo`

**Tipo:** Escritura

> Reanuda un flujo pausado.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `flujo` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "reanudar_flujo",
  "arguments": {
    "flujo": "<flujo>"
  }
}
```

### `desenganchar_flujo_de_concepto`

**Tipo:** DESTRUCTIVA (confirma con el usuario)

> Quita el flujo de un concepto: las tareas NUEVAS de ese concepto ya no lo arrancan (las que estan en marcha siguen). 'concepto' = codigo o nombre. Confirma con el usuario.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `concepto` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "desenganchar_flujo_de_concepto",
  "arguments": {
    "concepto": "<concepto>"
  }
}
```

### `opciones_agente_nodo`

**Tipo:** Lectura

> Lo que se puede usar al configurar el agente de un paso: agentes de IA del tenant (activos primero), clientes Colmena (busqueda web), lineas de WhatsApp y lineas de voz. Usala antes de agente_nodo y de los recursos.

_Sin parametros._

Llamada minima (solo obligatorios):

```json
{
  "name": "opciones_agente_nodo",
  "arguments": {}
}
```

### `ver_agente_nodo`

**Tipo:** Lectura

> El agente de IA de un paso con toda su configuracion (autonomia, instrucciones, recursos, canales, fallo, seguimiento, tope de preguntas, traspasos). Sin agente = el paso lo atiende solo gente.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI | Id del nodo (ver ver_flujo) |

Llamada minima (solo obligatorios):

```json
{
  "name": "ver_agente_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>"
  }
}
```

### `agente_nodo`

**Tipo:** Escritura

> Pone (o reemplaza) el AGENTE DE IA que atiende un paso. 'agente' = id o nombre (opciones_agente_nodo). 'autonomia': autonomo = el agente cierra el paso solo; propone (defecto) = deja todo listo y una persona confirma. En una compuerta, primero decision_humana_nodo. El agente debe estar ENCENDIDO para actuar.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI | Id del nodo (ver ver_flujo) |
| `agente` | string | SI |  |
| `autonomia` | string (autonomo / propone) |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "agente_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>",
    "agente": "<agente>"
  }
}
```

### `quitar_agente_nodo`

**Tipo:** DESTRUCTIVA (confirma con el usuario)

> Quita el agente del paso: vuelve a ser 100% humano (se pierde su configuracion de recursos). Confirma con el usuario.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI | Id del nodo (ver ver_flujo) |

Llamada minima (solo obligatorios):

```json
{
  "name": "quitar_agente_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>"
  }
}
```

### `instrucciones_agente_nodo`

**Tipo:** Escritura

> Instrucciones del agente SOLO para este paso (se suman a su prompt general): que debe lograr, que datos buscar, como hablarle al cliente. Sin 'instrucciones' las quita.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI | Id del nodo (ver ver_flujo) |
| `instrucciones` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "instrucciones_agente_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>"
  }
}
```

### `correo_agente_nodo`

**Tipo:** Escritura

> activo=true permite al agente enviar CORREOS en este paso.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI | Id del nodo (ver ver_flujo) |
| `activo` | boolean | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "correo_agente_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>",
    "activo": true
  }
}
```

### `web_agente_nodo`

**Tipo:** Escritura

> Busqueda web con un navegador de la Colmena: 'cliente_colmena' (id o nombre, ver opciones_agente_nodo) y 'perfil' opcional (sesion guardada del navegador). Sin 'cliente_colmena' la quita.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI | Id del nodo (ver ver_flujo) |
| `cliente_colmena` | string |  |  |
| `perfil` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "web_agente_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>"
  }
}
```

### `whatsapp_agente_nodo`

**Tipo:** Escritura

> El agente pregunta por WhatsApp desde 'linea' (id o nombre). Para escribir en FRIO (sin conversacion abierta) usa 'plantilla' HSM aprobada + 'idioma', 'variables' (variable -> texto con tokens) y 'encabezado_url' si la plantilla lleva archivo. 'minutos_frio' = desde cuantos minutos sin respuesta usa la plantilla. Sin 'linea' quita WhatsApp.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI | Id del nodo (ver ver_flujo) |
| `linea` | string |  |  |
| `plantilla` | string |  |  |
| `idioma` | string |  |  |
| `variables` | object |  |  |
| `encabezado_url` | string |  |  |
| `minutos_frio` | integer >=1 |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "whatsapp_agente_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>"
  }
}
```

### `voz_agente_nodo`

**Tipo:** Escritura

> Llamadas de VOZ: 'agente_voz' (agente de voz, id o nombre) y 'linea_voz' (id o nombre; omitida = la por defecto). Reintento: 'max_llamadas' (1-10), 'reintento_minutos' (1-1440) y ventana 'hora_inicio'-'hora_fin' (0-24, hora de la empresa). Sin 'agente_voz' quita la voz.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI | Id del nodo (ver ver_flujo) |
| `agente_voz` | string |  |  |
| `linea_voz` | string |  |  |
| `max_llamadas` | integer >=1 <=10 |  |  |
| `reintento_minutos` | integer >=1 <=1440 |  |  |
| `hora_inicio` | integer >=0 <=23 |  |  |
| `hora_fin` | integer >=1 <=24 |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "voz_agente_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>"
  }
}
```

### `canales_agente_nodo`

**Tipo:** Escritura

> Canales que el agente puede usar en este paso: voz, whatsapp, web, correo. Lista vacia = todos los configurados.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI | Id del nodo (ver ver_flujo) |
| `canales` | array de string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "canales_agente_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>",
    "canales": [
      "<canales>"
    ]
  }
}
```

### `fallo_agente_nodo`

**Tipo:** Escritura

> Que hacer si el agente NO resuelve el paso: devolver_a_persona (defecto), reintentar ('reintentos' 0-10), tomar_ruta ('ruta' = NOMBRE de la opcion/salida a tomar) o notificar.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI | Id del nodo (ver ver_flujo) |
| `accion` | string (devolver_a_persona / reintentar / tomar_ruta / notificar) | SI |  |
| `reintentos` | integer >=0 <=10 |  |  |
| `ruta` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "fallo_agente_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>",
    "accion": "devolver_a_persona"
  }
}
```

### `seguimiento_agente_nodo`

**Tipo:** Escritura

> CADENCIA de seguimiento: lista de esperas entre recordatorios del agente (la 1a es el primer contacto; 0 = inmediato). Al agotarse deja de insistir. 'ventana' opcional (horario de envio: 'inicio'/'fin' HH:MM, 'dias' 1=lunes..7=domingo, 'festivos' true = no escribe en festivos). Lista vacia quita la cadencia.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI | Id del nodo (ver ver_flujo) |
| `esperas` | array de objetos | SI |  |
| `esperas[].dias` | integer >=0 |  |  |
| `esperas[].horas` | integer >=0 |  |  |
| `esperas[].minutos` | integer >=0 |  |  |
| `ventana` | object |  |  |
| `ventana.inicio` | string |  |  |
| `ventana.fin` | string |  |  |
| `ventana.dias` | array de integer |  |  |
| `ventana.festivos` | boolean |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "seguimiento_agente_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>",
    "esperas": [
      {
        "dias": 0,
        "horas": 0
      }
    ]
  }
}
```

### `tope_preguntas_agente_nodo`

**Tipo:** Escritura

> Maximo de preguntas/recordatorios por WhatsApp por actividad antes de pasarla a una persona. Sin 'tope' = el valor por defecto del sistema.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI | Id del nodo (ver ver_flujo) |
| `tope` | integer >=1 |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "tope_preguntas_agente_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>"
  }
}
```

### `traspaso_salida_agente_nodo`

**Tipo:** Escritura

> En una COMPUERTA con agente: nota que el flujo deja al agente conversacional cuando la tarea sale por esta salida ('salida_nodo_id' = nodo destino), para que sepa como seguir la conversacion. Sin 'nota' la quita.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nodo_id` | string | SI | Id del nodo (ver ver_flujo) |
| `salida_nodo_id` | string | SI |  |
| `nota` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "traspaso_salida_agente_nodo",
  "arguments": {
    "nodo_id": "<nodo_id>",
    "salida_nodo_id": "<salida_nodo_id>"
  }
}
```
