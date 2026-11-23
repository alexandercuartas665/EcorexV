---
tipo: referencia
servidor: ecorex-whatsapp
herramientas: 36
fuente: specs en codigo (generado)
---

# MCP WhatsApp por linea

- **Ruta:** `/api/mcp/whatsapp/{lineId}`
- **Token:** alcance `mcp:whatsapp` (se configura desde la pantalla /lineas, una configuracion por linea). Un token de tableros NO abre este servidor.
- Volver a [[00 - LEEME PRIMERO]] - conexion en [[01 - Conectar los MCP]].

## Para que sirve

Cada **linea de WhatsApp (Evolution API)** del cliente es un servidor MCP propio (en el cliente se nombra p.ej.
`whatsapp-ventas`, `whatsapp-soporte`). Lo que envias queda en Conversaciones a nombre del usuario "(via IA)" y el
agente SARA deja de responder ese chat (usa `pausar_agente` / `reanudar_agente` para controlarlo).

Reglas de seguridad (obligatorias):
- Empieza con `estado_linea`. Antes de responder un chat, lee sus mensajes (`leer_mensajes`).
- **Un destinatario por llamada. Nada de envios masivos.**
- Escribirle a un numero que NUNCA escribio a la linea puede hacer que WhatsApp bloquee el numero: confirma con el
  usuario y usa `primer_contacto_confirmado=true`.
- Confirma antes de borrar mensajes, bloquear, salir de grupos, crear grupos o publicar estados.
- Archivos: por URL https publica (o base64 pequeno). Los grupos no quedan en Conversaciones: leelos con
  `fuente='whatsapp'`.

## Herramientas (36)

### `estado_linea`

**Tipo:** Lectura

> Estado de ESTA linea: nombre, numero, conexion real con WhatsApp, si el agente SARA la atiende y cuantos chats tiene en el sistema. Usala primero.

_Sin parametros._

Llamada minima (solo obligatorios):

```json
{
  "name": "estado_linea",
  "arguments": {}
}
```

### `listar_chats`

**Tipo:** Lectura

> Chats de la linea, el mas reciente primero. fuente='sistema' (por defecto: conversaciones guardadas en ECOREX, solo chats 1:1) o 'whatsapp' (lo que tiene WhatsApp, incluye grupos). Devuelve el id de conversacion o jid para usar en 'chat'.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `fuente` | string (sistema / whatsapp) |  |  |
| `texto` | string |  | Filtra por nombre o numero |
| `incluir_archivados` | boolean |  |  |
| `limite` | integer >=1 <=60 |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "listar_chats",
  "arguments": {}
}
```

### `leer_mensajes`

**Tipo:** Lectura

> Ultimos mensajes de un chat (mas reciente al final), con su mensaje_id (para reaccionar, citar, editar, borrar o descargar media). 'chat' = id de conversacion, numero o jid. fuente='sistema' (por defecto) o 'whatsapp' (historial que guarda WhatsApp/Evolution: grupos o mensajes que el sistema no tiene).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `chat` | string | SI |  |
| `fuente` | string (sistema / whatsapp) |  |  |
| `limite` | integer >=1 <=100 |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "leer_mensajes",
  "arguments": {
    "chat": "<chat>"
  }
}
```

### `buscar_contactos`

**Tipo:** Lectura

> Contactos que conoce la linea en WhatsApp (nombre visible y jid). 'texto' filtra por nombre o numero.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `texto` | string |  |  |
| `limite` | integer >=1 <=60 |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "buscar_contactos",
  "arguments": {}
}
```

### `verificar_numeros`

**Tipo:** Lectura

> Verifica que numeros tienen WhatsApp (hasta 20) y devuelve su jid. Util antes de escribir a un numero nuevo.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `numeros` | array de string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "verificar_numeros",
  "arguments": {
    "numeros": [
      "<numeros>"
    ]
  }
}
```

### `ver_perfil_contacto`

**Tipo:** Lectura

> Perfil publico de un contacto: nombre, estado/descripcion, foto y si es cuenta business.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `chat` | string | SI | Numero, jid o id de conversacion |

Llamada minima (solo obligatorios):

```json
{
  "name": "ver_perfil_contacto",
  "arguments": {
    "chat": "<chat>"
  }
}
```

### `descargar_media`

**Tipo:** Lectura

> Descarga la imagen, audio, video o documento de un mensaje (por mensaje_id, ver leer_mensajes) para que la IA lo vea/oiga. Tope 4 MB.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `mensaje_id` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "descargar_media",
  "arguments": {
    "mensaje_id": "<mensaje_id>"
  }
}
```

### `listar_grupos`

**Tipo:** Lectura

> Grupos de WhatsApp en los que esta la linea (jid, nombre, tamano).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `con_participantes` | boolean |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "listar_grupos",
  "arguments": {}
}
```

### `ver_grupo`

**Tipo:** Lectura

> Detalle de un grupo: nombre, descripcion, ajustes y participantes (con admins). 'grupo' = jid @g.us (ver listar_grupos).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `grupo` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "ver_grupo",
  "arguments": {
    "grupo": "<grupo>"
  }
}
```

### `listar_etiquetas`

**Tipo:** Lectura

> Etiquetas de WhatsApp Business de la linea (id, nombre, color) para etiquetar_chat.

_Sin parametros._

Llamada minima (solo obligatorios):

```json
{
  "name": "listar_etiquetas",
  "arguments": {}
}
```

### `catalogo_business`

**Tipo:** Lectura

> Catalogo de productos de WhatsApp Business (de la linea o de otro numero business).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `numero` | string |  | Vacio = catalogo de la propia linea |
| `limite` | integer >=1 <=50 |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "catalogo_business",
  "arguments": {}
}
```

### `enviar_texto`

**Tipo:** Escritura

> Envia un mensaje de texto (un destinatario por llamada). 'responder_a' = mensaje_id para citarlo. A un chat 1:1 queda en Conversaciones a tu nombre y SARA deja de responder ese chat. Lee el chat antes de responder.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `para` | string | SI | Destino: numero (con indicativo), jid (@s.whatsapp.net, @lid o grupo @g.us) o id de conversacion |
| `texto` | string | SI |  |
| `responder_a` | string |  | mensaje_id a citar (ver leer_mensajes) |
| `primer_contacto_confirmado` | boolean |  | true solo si el usuario confirmo escribirle a un numero que nunca ha escrito a esta linea |

Llamada minima (solo obligatorios):

```json
{
  "name": "enviar_texto",
  "arguments": {
    "para": "<para>",
    "texto": "<texto>"
  }
}
```

### `enviar_media`

**Tipo:** Escritura

> Envia un archivo: tipo imagen, video, documento, audio (llega como nota de voz), sticker o video_nota. El archivo va por 'url' (https publica, maximo 16 MB) o 'base64' (maximo 2 MB, con 'mime'). 'pie' = texto debajo (imagen/video/documento).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `para` | string | SI | Destino: numero (con indicativo), jid (@s.whatsapp.net, @lid o grupo @g.us) o id de conversacion |
| `tipo` | string (imagen / video / documento / audio / sticker / video_nota) | SI |  |
| `url` | string |  |  |
| `base64` | string |  |  |
| `mime` | string |  |  |
| `nombre_archivo` | string |  |  |
| `pie` | string |  |  |
| `primer_contacto_confirmado` | boolean |  | true solo si el usuario confirmo escribirle a un numero que nunca ha escrito a esta linea |

Llamada minima (solo obligatorios):

```json
{
  "name": "enviar_media",
  "arguments": {
    "para": "<para>",
    "tipo": "imagen"
  }
}
```

### `enviar_ubicacion`

**Tipo:** Escritura

> Envia una ubicacion (latitud/longitud) con nombre y direccion opcionales.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `para` | string | SI | Destino: numero (con indicativo), jid (@s.whatsapp.net, @lid o grupo @g.us) o id de conversacion |
| `latitud` | number | SI |  |
| `longitud` | number | SI |  |
| `nombre` | string |  |  |
| `direccion` | string |  |  |
| `primer_contacto_confirmado` | boolean |  | true solo si el usuario confirmo escribirle a un numero que nunca ha escrito a esta linea |

Llamada minima (solo obligatorios):

```json
{
  "name": "enviar_ubicacion",
  "arguments": {
    "para": "<para>",
    "latitud": 1,
    "longitud": 1
  }
}
```

### `enviar_contacto`

**Tipo:** Escritura

> Envia una o varias tarjetas de contacto (vCard).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `para` | string | SI | Destino: numero (con indicativo), jid (@s.whatsapp.net, @lid o grupo @g.us) o id de conversacion |
| `contactos` | array de objetos | SI |  |
| `contactos[].nombre` | string | SI |  |
| `contactos[].telefono` | string | SI |  |
| `contactos[].empresa` | string |  |  |
| `contactos[].email` | string |  |  |
| `primer_contacto_confirmado` | boolean |  | true solo si el usuario confirmo escribirle a un numero que nunca ha escrito a esta linea |

Llamada minima (solo obligatorios):

```json
{
  "name": "enviar_contacto",
  "arguments": {
    "para": "<para>",
    "contactos": [
      {
        "nombre": "<nombre>",
        "telefono": "<telefono>"
      }
    ]
  }
}
```

### `enviar_encuesta`

**Tipo:** Escritura

> Envia una encuesta con 2 a 12 opciones. 'multiple' = se pueden elegir varias.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `para` | string | SI | Destino: numero (con indicativo), jid (@s.whatsapp.net, @lid o grupo @g.us) o id de conversacion |
| `pregunta` | string | SI |  |
| `opciones` | array de string | SI |  |
| `multiple` | boolean |  |  |
| `primer_contacto_confirmado` | boolean |  | true solo si el usuario confirmo escribirle a un numero que nunca ha escrito a esta linea |

Llamada minima (solo obligatorios):

```json
{
  "name": "enviar_encuesta",
  "arguments": {
    "para": "<para>",
    "pregunta": "<pregunta>",
    "opciones": [
      "<opciones>"
    ]
  }
}
```

### `enviar_lista`

**Tipo:** Escritura

> Envia un mensaje con lista de opciones (menu). Ojo: WhatsApp restringe los mensajes interactivos en cuentas no oficiales y en algunos telefonos no se muestran; si importa que llegue, usa texto o encuesta.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `para` | string | SI | Destino: numero (con indicativo), jid (@s.whatsapp.net, @lid o grupo @g.us) o id de conversacion |
| `titulo` | string | SI |  |
| `descripcion` | string | SI |  |
| `boton` | string | SI | Texto del boton que abre la lista |
| `pie` | string |  |  |
| `secciones` | array de objetos | SI |  |
| `secciones[].titulo` | string | SI |  |
| `secciones[].filas` | array de objetos | SI |  |
| `secciones[].filas[].titulo` | string | SI |  |
| `secciones[].filas[].descripcion` | string |  |  |
| `secciones[].filas[].id` | string |  |  |
| `primer_contacto_confirmado` | boolean |  | true solo si el usuario confirmo escribirle a un numero que nunca ha escrito a esta linea |

Llamada minima (solo obligatorios):

```json
{
  "name": "enviar_lista",
  "arguments": {
    "para": "<para>",
    "titulo": "<titulo>",
    "descripcion": "<descripcion>",
    "boton": "<boton>",
    "secciones": [
      {
        "titulo": "<titulo>",
        "filas": [
          {
            "titulo": "<titulo>"
          }
        ]
      }
    ]
  }
}
```

### `publicar_estado`

**Tipo:** Escritura

> Publica un ESTADO de WhatsApp (historia) de la linea: texto, imagen, video o audio. Exige 'para_todos'=true (todos los contactos) o una lista de 'contactos'. Llega a mucha gente: confirma con el usuario antes.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `tipo` | string (texto / imagen / video / audio) | SI |  |
| `contenido` | string |  | Texto del estado (tipo texto) |
| `url` | string |  |  |
| `base64` | string |  |  |
| `pie` | string |  |  |
| `color_fondo` | string |  | Hex, ej. #25D366 (tipo texto) |
| `para_todos` | boolean |  |  |
| `contactos` | array de string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "publicar_estado",
  "arguments": {
    "tipo": "texto"
  }
}
```

### `reaccionar`

**Tipo:** Escritura

> Reacciona con un emoji a un mensaje (por mensaje_id). Emoji vacio = quitar la reaccion. Si el mensaje no esta en el sistema indica 'chat'.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `mensaje_id` | string | SI |  |
| `emoji` | string | SI |  |
| `chat` | string |  |  |
| `de_nosotros` | boolean |  | Si el mensaje lo enviamos nosotros (solo si no esta en el sistema) |

Llamada minima (solo obligatorios):

```json
{
  "name": "reaccionar",
  "arguments": {
    "mensaje_id": "<mensaje_id>",
    "emoji": "<emoji>"
  }
}
```

### `escribiendo`

**Tipo:** Escritura

> Muestra 'escribiendo...' o 'grabando audio...' en el chat durante unos segundos (o lo detiene con 'pausa').

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `chat` | string | SI |  |
| `estado` | string (escribiendo / grabando / pausa) |  |  |
| `segundos` | integer >=1 <=25 |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "escribiendo",
  "arguments": {
    "chat": "<chat>"
  }
}
```

### `editar_mensaje`

**Tipo:** Escritura

> Edita el texto de un mensaje ENVIADO por la linea (por mensaje_id). WhatsApp solo permite editar los recientes.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `mensaje_id` | string | SI |  |
| `texto` | string | SI |  |
| `chat` | string |  | Numero, jid o id de conversacion (ver listar_chats) |

Llamada minima (solo obligatorios):

```json
{
  "name": "editar_mensaje",
  "arguments": {
    "mensaje_id": "<mensaje_id>",
    "texto": "<texto>"
  }
}
```

### `borrar_mensaje`

**Tipo:** DESTRUCTIVA (confirma con el usuario)

> Borra PARA TODOS un mensaje enviado por la linea (por mensaje_id). No se puede deshacer: confirma con el usuario antes.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `mensaje_id` | string | SI |  |
| `chat` | string |  | Numero, jid o id de conversacion (ver listar_chats) |

Llamada minima (solo obligatorios):

```json
{
  "name": "borrar_mensaje",
  "arguments": {
    "mensaje_id": "<mensaje_id>"
  }
}
```

### `marcar_leido`

**Tipo:** Escritura

> Marca como leidos (doble check azul) los ultimos mensajes del cliente en un chat.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `chat` | string | SI | Numero, jid o id de conversacion (ver listar_chats) |
| `cantidad` | integer >=1 <=50 |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "marcar_leido",
  "arguments": {
    "chat": "<chat>"
  }
}
```

### `gestionar_chat`

**Tipo:** Escritura

> Acciones sobre un chat: archivar, desarchivar, marcar_no_leido, bloquear o desbloquear el contacto. Bloquear corta la comunicacion: confirma con el usuario.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `chat` | string | SI | Numero, jid o id de conversacion (ver listar_chats) |
| `accion` | string (archivar / desarchivar / marcar_no_leido / bloquear / desbloquear) | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "gestionar_chat",
  "arguments": {
    "chat": "<chat>",
    "accion": "archivar"
  }
}
```

### `etiquetar_chat`

**Tipo:** Escritura

> Agrega o quita una etiqueta de WhatsApp Business a un chat (etiqueta_id de listar_etiquetas).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `chat` | string | SI | Numero, jid o id de conversacion (ver listar_chats) |
| `etiqueta_id` | string | SI |  |
| `accion` | string (agregar / quitar) |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "etiquetar_chat",
  "arguments": {
    "chat": "<chat>",
    "etiqueta_id": "<etiqueta_id>"
  }
}
```

### `pausar_agente`

**Tipo:** Escritura

> Silencia al agente SARA para un contacto (pasa a atencion humana). Usa la lista negra de la empresa: aplica a ese numero en TODAS las lineas hasta reanudar_agente.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `chat` | string | SI | Numero, jid o id de conversacion (ver listar_chats) |
| `motivo` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "pausar_agente",
  "arguments": {
    "chat": "<chat>"
  }
}
```

### `reanudar_agente`

**Tipo:** Escritura

> Vuelve a dejar que el agente SARA atienda a un contacto (lo saca de la lista negra).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `chat` | string | SI | Numero, jid o id de conversacion (ver listar_chats) |

Llamada minima (solo obligatorios):

```json
{
  "name": "reanudar_agente",
  "arguments": {
    "chat": "<chat>"
  }
}
```

### `guardar_contacto`

**Tipo:** Escritura

> Guarda al contacto de un chat en el Directorio de ECOREX (persona, sin duplicar si ya existe). WhatsApp no permite crear contactos en la agenda del telefono; esto lo deja en el sistema.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `chat` | string | SI | Numero, jid o id de conversacion (ver listar_chats) |
| `nombre` | string |  | Por defecto el nombre del chat |
| `email` | string |  |  |
| `ciudad` | string |  |  |
| `cargo` | string |  |  |
| `identificacion` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "guardar_contacto",
  "arguments": {
    "chat": "<chat>"
  }
}
```

### `crear_grupo`

**Tipo:** Escritura

> Crea un grupo de WhatsApp con la linea como admin. Agregar personas a un grupo sin su permiso puede causar bloqueos: confirma con el usuario.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nombre` | string | SI |  |
| `descripcion` | string |  |  |
| `participantes` | array de string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "crear_grupo",
  "arguments": {
    "nombre": "<nombre>",
    "participantes": [
      "<participantes>"
    ]
  }
}
```

### `editar_grupo`

**Tipo:** Escritura

> Cambia un grupo: nombre, descripcion, foto (url/base64), solo_admins_escriben / todos_escriben, solo_admins_editan / todos_editan o mensajes_temporales (dias: 0, 1, 7 o 90). La linea debe ser admin.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `grupo` | string | SI | jid del grupo (termina en @g.us; ver listar_grupos) |
| `accion` | string (nombre / descripcion / foto / solo_admins_escriben / todos_escriben / solo_admins_editan / todos_editan / mensajes_temporales) | SI |  |
| `valor` | string |  | Nuevo nombre o descripcion |
| `url` | string |  |  |
| `base64` | string |  |  |
| `dias` | integer (0 / 1 / 7 / 90) |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "editar_grupo",
  "arguments": {
    "grupo": "<grupo>",
    "accion": "nombre"
  }
}
```

### `participantes_grupo`

**Tipo:** Escritura

> Agrega, quita, promueve a admin o degrada participantes de un grupo (numeros).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `grupo` | string | SI | jid del grupo (termina en @g.us; ver listar_grupos) |
| `accion` | string (agregar / quitar / promover / degradar) | SI |  |
| `participantes` | array de string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "participantes_grupo",
  "arguments": {
    "grupo": "<grupo>",
    "accion": "agregar",
    "participantes": [
      "<participantes>"
    ]
  }
}
```

### `invitacion_grupo`

**Tipo:** Escritura

> Enlaces de invitacion: ver_enlace, revocar (genera uno nuevo), enviar (a 'numeros'), info (de un 'codigo') o unirse (con un 'codigo').

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `accion` | string (ver_enlace / revocar / enviar / info / unirse) | SI |  |
| `grupo` | string |  | jid del grupo (termina en @g.us; ver listar_grupos) |
| `numeros` | array de string |  |  |
| `codigo` | string |  |  |
| `descripcion` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "invitacion_grupo",
  "arguments": {
    "accion": "ver_enlace"
  }
}
```

### `salir_grupo`

**Tipo:** DESTRUCTIVA (confirma con el usuario)

> La linea SALE del grupo. No se puede deshacer sin una nueva invitacion: confirma con el usuario.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `grupo` | string | SI | jid del grupo (termina en @g.us; ver listar_grupos) |

Llamada minima (solo obligatorios):

```json
{
  "name": "salir_grupo",
  "arguments": {
    "grupo": "<grupo>"
  }
}
```

### `perfil_linea`

**Tipo:** Escritura

> Perfil de la LINEA: ver, cambiar_nombre, cambiar_estado (texto 'info'), cambiar_foto (url/base64) o quitar_foto. Cambiar requiere permiso de editar Lineas.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `accion` | string (ver / cambiar_nombre / cambiar_estado / cambiar_foto / quitar_foto) | SI |  |
| `valor` | string |  |  |
| `url` | string |  |  |
| `base64` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "perfil_linea",
  "arguments": {
    "accion": "ver"
  }
}
```

### `privacidad_linea`

**Tipo:** Escritura

> Privacidad de la linea: ver o cambiar (solo los campos enviados). Valores: todos, contactos, contactos_excepto, nadie (en_linea: todos o igual_ultima_vez; leido: todos o nadie).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `accion` | string (ver / cambiar) | SI |  |
| `leido` | string (todos / nadie) |  |  |
| `foto_perfil` | string (todos / contactos / contactos_excepto / nadie) |  |  |
| `estado` | string (todos / contactos / contactos_excepto / nadie) |  |  |
| `en_linea` | string (todos / igual_ultima_vez) |  |  |
| `ultima_vez` | string (todos / contactos / contactos_excepto / nadie) |  |  |
| `agregar_a_grupos` | string (todos / contactos / contactos_excepto) |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "privacidad_linea",
  "arguments": {
    "accion": "ver"
  }
}
```

### `reiniciar_linea`

**Tipo:** Escritura

> Reinicia la conexion de la linea con WhatsApp (util si deja de recibir mensajes). No cierra la sesion.

_Sin parametros._

Llamada minima (solo obligatorios):

```json
{
  "name": "reiniciar_linea",
  "arguments": {}
}
```
