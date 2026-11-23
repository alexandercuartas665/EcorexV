---
tipo: referencia
servidor: ecorex-tableros
modulo: agentes
herramientas: 17
fuente: catalogo vivo tools/list (generado)
---

# MCP Tableros - Agentes de IA

Servidor: `/api/mcp/tableros` - ver [[01 - Conectar los MCP]]. Volver a [[00 - LEEME PRIMERO]].

## Para que sirve

Agentes de IA del modulo de agentes (los mismos que atienden WhatsApp, clasifican datos o leen documentos).
Proveedores: claude, gemini, chatgpt, deepseek (el gateway del plan del cliente debe tener ese proveedor activo).

Reglas:
- `crear_agente` nace **APAGADO**: valida y luego `encender_agente`.
- `herramientas_deshabilitadas` es una lista de exclusion: si la omites, el agente tiene TODAS sus herramientas
  (incluida `agregar_filas_a_contenedor`, con la que escribe en un contenedor).
- `editar_agente` cambia solo lo que envies. El lector de medios (como lee PDF/imagenes) va en `agente_lector_medios`.
- Cada cambio de prompt queda versionado (`historial_prompts_agente`, `restaurar_version_prompt`).

Recetas: [[R09 - Crear y configurar un agente de IA]]

## Herramientas (17)

- **Lectura:** `listar_agentes`, `ver_agente`, `historial_prompts_agente`
- **Escritura:** `crear_agente`, `editar_agente`, `agente_lector_medios`, `encender_agente`, `apagar_agente`, `duplicar_agente`, `restaurar_version_prompt`, `agregar_recurso_agente`, `editar_recurso_agente`, `agregar_prompt_agente`, `editar_prompt_agente`
- **Destructivas:** `eliminar_agente`, `eliminar_recurso_agente`, `eliminar_prompt_agente`

### `listar_agentes`

**Tipo:** Lectura

> Lista los agentes de IA del tenant: id, nombre, rol, proveedor, modelo, si esta encendido y numero de recursos. Usala primero para conocer ids y nombres exactos.

_Sin parametros._

Llamada minima (solo obligatorios):

```json
{
  "name": "listar_agentes",
  "arguments": {}
}
```

### `ver_agente`

**Tipo:** Lectura

> Muestra un agente completo: proveedor/modelo, prompt de sistema, prompt del lector de medios, herramientas DESHABILITADAS (vacio = todas habilitadas), tableros permitidos, asignados round-robin, concepto por defecto, recursos y prompts enrutados. 'agente' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `agente` | string | SI | id o nombre del agente |

Llamada minima (solo obligatorios):

```json
{
  "name": "ver_agente",
  "arguments": {
    "agente": "<agente>"
  }
}
```

### `historial_prompts_agente`

**Tipo:** Lectura

> Historial de versiones de los prompts del agente (red de seguridad, ultimas 5), mas reciente primero. Si envias 'prompt' muestra solo las versiones de ese prompt enrutado por nombre. 'agente' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `agente` | string | SI |  |
| `prompt` | string |  | nombre de un prompt enrutado (opcional) |

Llamada minima (solo obligatorios):

```json
{
  "name": "historial_prompts_agente",
  "arguments": {
    "agente": "<agente>"
  }
}
```

### `crear_agente`

**Tipo:** Escritura

> Crea un agente de IA NUEVO. 'proveedor': claude, gemini, chatgpt o deepseek. 'modelo' opcional (vacio = el por defecto del proveedor). 'prompt' = prompt de sistema (rol). 'herramientas_deshabilitadas' (opcional) = lista de nombres de herramientas a APAGAR; si la omites quedan TODAS habilitadas (incluida 'agregar_filas_a_contenedor', con la que el agente escribe a un contenedor). Nace APAGADO (enciendelo con encender_agente). Devuelve 'agente_id'.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nombre` | string | SI |  |
| `rol` | string |  | rol/tipo descriptivo |
| `proveedor` | string (claude / gemini / chatgpt / deepseek) |  |  |
| `modelo` | string |  |  |
| `prompt` | string |  | prompt de sistema |
| `herramientas_deshabilitadas` | array de string |  |  |
| `reacciones_activas` | boolean |  |  |
| `reaccion_n` | integer |  |  |
| `reaccion_m` | integer |  |  |
| `reaccion_emojis` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "crear_agente",
  "arguments": {
    "nombre": "<nombre>"
  }
}
```

Ejemplo real (valores genericos):

```json
{
  "name": "crear_agente",
  "arguments": {
    "nombre": "Clasificador de pedidos",
    "rol": "Clasificador",
    "proveedor": "deepseek",
    "prompt": "Recibes filas JSON de pedidos. Para cada una devuelve SOLO un arreglo JSON [{\"id_pedido\":\"...\",\"categoria\":\"...\"}] con categoria en: hardware, software, servicios, otros."
  }
}
```

### `editar_agente`

**Tipo:** Escritura

> Edita un agente (reemplazo por campo: lo que NO envies conserva su valor actual). Cambia nombre, rol, proveedor (claude, gemini, chatgpt o deepseek.), modelo, prompt de sistema, herramientas deshabilitadas, tableros permitidos (nombre o id; lista vacia = sin restriccion), asignados round-robin (email o nombre) y concepto por defecto (codigo o nombre; cadena vacia = ninguno). El lector de medios se edita con agente_lector_medios. 'agente' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `agente` | string | SI |  |
| `nombre` | string |  |  |
| `rol` | string |  |  |
| `proveedor` | string (claude / gemini / chatgpt / deepseek) |  |  |
| `modelo` | string |  |  |
| `prompt` | string |  |  |
| `herramientas_deshabilitadas` | array de string |  |  |
| `tableros_permitidos` | array de string |  |  |
| `asignados_round_robin` | array de string |  |  |
| `concepto_default` | string |  |  |
| `reacciones_activas` | boolean |  |  |
| `reaccion_n` | integer |  |  |
| `reaccion_m` | integer |  |  |
| `reaccion_emojis` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "editar_agente",
  "arguments": {
    "agente": "<agente>"
  }
}
```

### `agente_lector_medios`

**Tipo:** Escritura

> Fija (o limpia) el prompt del LECTOR DE MEDIOS del agente: el prompt con que lee cada imagen o PDF que recibe, antes de responder. 'prompt' vacio o ausente = lector generico. 'agente' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `agente` | string | SI |  |
| `prompt` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "agente_lector_medios",
  "arguments": {
    "agente": "<agente>"
  }
}
```

### `encender_agente`

**Tipo:** Escritura

> Enciende el agente (lo pone en produccion). 'agente' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `agente` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "encender_agente",
  "arguments": {
    "agente": "<agente>"
  }
}
```

### `apagar_agente`

**Tipo:** Escritura

> Apaga el agente (lo saca de produccion). 'agente' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `agente` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "apagar_agente",
  "arguments": {
    "agente": "<agente>"
  }
}
```

### `duplicar_agente`

**Tipo:** Escritura

> Duplica un agente con su config (prompts, recursos, herramientas, historial), SIN la linea de atencion ni los datos de conversacion. La copia queda APAGADA. 'agente' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `agente` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "duplicar_agente",
  "arguments": {
    "agente": "<agente>"
  }
}
```

### `restaurar_version_prompt`

**Tipo:** Escritura

> Restaura una version del historial de prompts del agente (ver historial_prompts_agente). 'version' = indice (0 = mas reciente). 'agente' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `agente` | string | SI |  |
| `version` | integer >=0 | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "restaurar_version_prompt",
  "arguments": {
    "agente": "<agente>",
    "version": 0
  }
}
```

### `agregar_recurso_agente`

**Tipo:** Escritura

> Agrega un recurso al agente (archivo/dato que puede usar para responder). 'tipo': imagen, video, audio, pdf, ubicacion, texto. 'detalle' (texto/descripcion), 'archivo_url' y 'archivo_nombre' opcionales. 'agente' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `agente` | string | SI |  |
| `nombre` | string | SI |  |
| `tipo` | string (imagen / video / audio / pdf / ubicacion / texto) | SI |  |
| `detalle` | string |  |  |
| `archivo_url` | string |  |  |
| `archivo_nombre` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "agregar_recurso_agente",
  "arguments": {
    "agente": "<agente>",
    "nombre": "<nombre>",
    "tipo": "imagen"
  }
}
```

### `editar_recurso_agente`

**Tipo:** Escritura

> Edita un recurso del agente por su id (ver ver_agente). Cambia nombre, tipo (imagen, video, audio, pdf, ubicacion, texto.), detalle y archivo.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `recurso_id` | string | SI |  |
| `nombre` | string |  |  |
| `tipo` | string (imagen / video / audio / pdf / ubicacion / texto) |  |  |
| `detalle` | string |  |  |
| `archivo_url` | string |  |  |
| `archivo_nombre` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "editar_recurso_agente",
  "arguments": {
    "recurso_id": "<recurso_id>"
  }
}
```

### `agregar_prompt_agente`

**Tipo:** Escritura

> Agrega un prompt ENRUTADO al agente (ademas del prompt base): se activa cuando su 'regla' aplica. 'cuerpo' = texto del prompt. 'agente' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `agente` | string | SI |  |
| `nombre` | string | SI |  |
| `regla` | string |  | condicion de enrutamiento (opcional) |
| `cuerpo` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "agregar_prompt_agente",
  "arguments": {
    "agente": "<agente>",
    "nombre": "<nombre>",
    "cuerpo": "<cuerpo>"
  }
}
```

### `editar_prompt_agente`

**Tipo:** Escritura

> Edita un prompt enrutado por su id (ver ver_agente). Cambia nombre, regla y cuerpo.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `prompt_id` | string | SI |  |
| `nombre` | string |  |  |
| `regla` | string |  |  |
| `cuerpo` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "editar_prompt_agente",
  "arguments": {
    "prompt_id": "<prompt_id>"
  }
}
```

### `eliminar_agente`

**Tipo:** DESTRUCTIVA (confirma con el usuario)

> Borra un agente COMPLETO con sus recursos, prompts y config. Confirma con el usuario antes de usarla. 'agente' = id o nombre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `agente` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "eliminar_agente",
  "arguments": {
    "agente": "<agente>"
  }
}
```

### `eliminar_recurso_agente`

**Tipo:** DESTRUCTIVA (confirma con el usuario)

> Borra un recurso del agente por su id (ver ver_agente).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `recurso_id` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "eliminar_recurso_agente",
  "arguments": {
    "recurso_id": "<recurso_id>"
  }
}
```

### `eliminar_prompt_agente`

**Tipo:** DESTRUCTIVA (confirma con el usuario)

> Borra un prompt enrutado del agente por su id (ver ver_agente).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `prompt_id` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "eliminar_prompt_agente",
  "arguments": {
    "prompt_id": "<prompt_id>"
  }
}
```
