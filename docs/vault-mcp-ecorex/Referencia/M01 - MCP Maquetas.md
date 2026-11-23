---
tipo: referencia
servidor: ecorex-maquetas
herramientas: 16
fuente: specs en codigo (generado)
---

# MCP Maquetas

- **Ruta:** `/api/mcp/maquetas`
- **Token:** alcance `integ:maqueta` (token personal de maquetas, se emite desde el editor de maquetas del sistema). Un token de tableros NO abre este servidor.
- Volver a [[00 - LEEME PRIMERO]] - conexion en [[01 - Conectar los MCP]].

## Para que sirve

Para la IA del PC local (Claude Code abierto en la carpeta de una maqueta HTML): lee las **solicitudes de
ajuste** anotadas en el editor de maquetas del sistema (con captura y selector CSS del elemento), las comenta,
mueve y resuelve, y obtiene el comando para **publicar una version** nueva. Incluye el **cerebro digital** del
proyecto (notas markdown enlazadas tipo Obsidian).

Flujo: `listar_proyectos` -> `listar_solicitudes` -> `ver_solicitud` (trae la captura) -> editar archivos locales ->
`comentar_solicitud` -> (con OK del usuario) publicar con el comando de `como_publicar_version` ->
`resolver_solicitud`. La maqueta es HTML puro: no agregues scripts de ECOREX. Nunca escribas el token en archivos.

## Herramientas (16)

### `listar_proyectos`

**Tipo:** Lectura

> Lista los proyectos de maqueta de la empresa: identificador (slug), nombre, version actual, archivos, tablero de las solicitudes y cuantas solicitudes hay abiertas. Usala primero.

_Sin parametros._

Llamada minima (solo obligatorios):

```json
{
  "name": "listar_proyectos",
  "arguments": {}
}
```

### `listar_solicitudes`

**Tipo:** Lectura

> Lista las solicitudes de ajuste de un proyecto (tarjetas creadas desde el editor de maquetas): numero, titulo, prioridad, estado/columna, pantalla, ARCHIVO de la maqueta y selector CSS del elemento. Por defecto solo las abiertas, de la mas reciente a la mas vieja.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `proyecto` | string |  | Slug del proyecto (ver listar_proyectos). Si la empresa tiene uno solo, se puede omitir. |
| `estado` | string (abiertas / terminadas / todas) |  |  |
| `archivo` | string |  | Solo las de esta pantalla (ruta relativa, ej. rq01-dependencias.html) |

Llamada minima (solo obligatorios):

```json
{
  "name": "listar_solicitudes",
  "arguments": {}
}
```

### `ver_solicitud`

**Tipo:** Lectura

> Detalle de una solicitud por numero (ej. T00046): descripcion completa, contexto tecnico (archivo, selector, texto y clases del elemento, ruta del menu, origen en el app real, handoff, tema, viewport), comentarios y adjuntos de la tarjeta. Devuelve ademas la CAPTURA de pantalla como imagen (el elemento va resaltado en rojo).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `numero` | string | SI | Numero de la tarjeta, ej. T00046 |
| `sin_captura` | boolean |  | true = no devolver la imagen |

Llamada minima (solo obligatorios):

```json
{
  "name": "ver_solicitud",
  "arguments": {
    "numero": "<numero>"
  }
}
```

### `listar_versiones`

**Tipo:** Lectura

> Versiones publicadas de un proyecto (numero, fecha, autor, archivos, notas) y cual es la actual.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `proyecto` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "listar_versiones",
  "arguments": {}
}
```

### `como_publicar_version`

**Tipo:** Lectura

> Devuelve el comando exacto (PowerShell y curl) para publicar la carpeta local de la maqueta como version NUEVA del proyecto con el MISMO token de este servidor: comprimir en ZIP (HTML puro, con index.html) y subirlo. Publicar exige ser Owner/Admin de la empresa. Pide confirmacion al usuario antes de publicar.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `proyecto` | string |  |  |
| `notas` | string |  | Notas de la version (ej. 'Ajustes T00046, T00047') |

Llamada minima (solo obligatorios):

```json
{
  "name": "como_publicar_version",
  "arguments": {}
}
```

### `comentar_solicitud`

**Tipo:** Escritura

> Agrega un comentario a la bitacora de la solicitud (queda a nombre del usuario 'via IA'). Usala para contar que se cambio y en que archivo.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `numero` | string | SI |  |
| `texto` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "comentar_solicitud",
  "arguments": {
    "numero": "<numero>",
    "texto": "<texto>"
  }
}
```

### `mover_solicitud`

**Tipo:** Escritura

> Mueve la solicitud a otra columna de su tablero (ej. 'En progreso' al empezar). Si la tarjeta la gobierna un flujo en curso, no se mueve a mano.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `numero` | string | SI |  |
| `columna` | string | SI | Nombre de la columna destino |
| `nota` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "mover_solicitud",
  "arguments": {
    "numero": "<numero>",
    "columna": "<columna>"
  }
}
```

### `resolver_solicitud`

**Tipo:** Escritura

> Marca la solicitud como RESUELTA (tarjeta terminada) y deja una nota con lo que se hizo. Hazlo despues de aplicar el cambio en la maqueta (idealmente despues de publicar la version, indicandola en la nota).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `numero` | string | SI |  |
| `nota` | string | SI | Que se cambio (archivo, version publicada) |
| `motivo_cierre` | string |  | Concepto de cierre si el tablero lo exige |

Llamada minima (solo obligatorios):

```json
{
  "name": "resolver_solicitud",
  "arguments": {
    "numero": "<numero>",
    "nota": "<nota>"
  }
}
```

### `listar_notas`

**Tipo:** Lectura

> Lista las notas del CEREBRO DIGITAL del proyecto (notas tipo Obsidian sobre lo que se esta construyendo en la maqueta): titulo, resumen, enlaces salientes y cuantas notas la enlazan. Mas recientes primero.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `proyecto` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "listar_notas",
  "arguments": {}
}
```

### `buscar_notas`

**Tipo:** Lectura

> Busca notas del cerebro digital por texto en el titulo o en el contenido.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `proyecto` | string |  |  |
| `texto` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "buscar_notas",
  "arguments": {
    "texto": "<texto>"
  }
}
```

### `ver_nota`

**Tipo:** Lectura

> Contenido completo de una nota del cerebro (markdown), su version, sus enlaces salientes ya resueltos (notas, pantallas .html de la maqueta, tarjetas T00000, etiquetas) y las notas que la enlazan (backlinks).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `proyecto` | string |  |  |
| `titulo` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "ver_nota",
  "arguments": {
    "titulo": "<titulo>"
  }
}
```

### `crear_nota`

**Tipo:** Escritura

> Crea una nota en el cerebro digital del proyecto. Sintaxis de enlaces: `[[Titulo de nota]]`, `[[Titulo|texto]]`, `[[pantalla.html]]` (pantalla de la maqueta), `[[T00046]]` (solicitud) y #etiqueta. Un enlace a una nota que no existe queda como nodo pendiente. Usala para guardar conclusiones, decisiones y preguntas abiertas.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `proyecto` | string |  |  |
| `titulo` | string | SI | Max 200, sin [ ] / # ^ |
| `contenido` | string | SI | Markdown |

Llamada minima (solo obligatorios):

```json
{
  "name": "crear_nota",
  "arguments": {
    "titulo": "<titulo>",
    "contenido": "<contenido>"
  }
}
```

### `editar_nota`

**Tipo:** Escritura

> Edita una nota del cerebro: modo 'agregar' (por defecto) anade el contenido al final; 'reemplazar' sustituye todo el cuerpo (pasa la 'version' que te dio ver_nota para no pisar cambios ajenos). 'nuevo_titulo' la renombra y actualiza los `[[enlaces]]` de las demas notas.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `proyecto` | string |  |  |
| `titulo` | string | SI |  |
| `contenido` | string |  |  |
| `modo` | string (agregar / reemplazar) |  |  |
| `nuevo_titulo` | string |  |  |
| `version` | integer |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "editar_nota",
  "arguments": {
    "titulo": "<titulo>"
  }
}
```

### `grafo_cerebro`

**Tipo:** Lectura

> Resumen del grafo del cerebro digital: nodos (notas, pantallas, tarjetas, etiquetas y notas pendientes por crear) con su grado de conexion, y las aristas. Sirve para ver de que trata el proyecto y que esta suelto.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `proyecto` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "grafo_cerebro",
  "arguments": {}
}
```

### `listar_adjuntos`

**Tipo:** Lectura

> Lista los adjuntos del cerebro digital (imagenes y PDF: diagramas, bocetos, capturas, documentos): nombre, tipo, tamano y en que notas se embeben (`![[nombre]]`). Usa ver_adjunto para mirar una imagen.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `proyecto` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "listar_adjuntos",
  "arguments": {}
}
```

### `ver_adjunto`

**Tipo:** Lectura

> Devuelve un adjunto del cerebro como IMAGEN para que la mires (diagramas, bocetos, capturas). Los PDF no se devuelven: solo su informacion. Pasa el nombre exacto de listar_adjuntos o el que aparece en `![[...]]`.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `proyecto` | string |  |  |
| `nombre` | string | SI | Nombre del adjunto, ej. boceto.png |

Llamada minima (solo obligatorios):

```json
{
  "name": "ver_adjunto",
  "arguments": {
    "nombre": "<nombre>"
  }
}
```
