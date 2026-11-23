---
tipo: referencia
servidor: ecorex-tableros
modulo: directorio
herramientas: 12
fuente: catalogo vivo tools/list (generado)
---

# MCP Tableros - Directorio de contactos

Servidor: `/api/mcp/tableros` - ver [[01 - Conectar los MCP]]. Volver a [[00 - LEEME PRIMERO]].

## Para que sirve

El Directorio General (terceros): empresas y personas (clientes, proveedores, prospectos).

Reglas:
- **Busca antes de crear** (`buscar_contacto`): `crear_contacto` no duplica por identificacion/telefono, pero
  buscar primero evita fichas casi-iguales.
- `editar_contacto` cambia SOLO lo que envies. Para campos personalizados consulta `listar_campos_contacto`
  antes de usar `campos`.
- Confirma con el usuario antes de `inactivar_contacto`.

Recetas: [[R02 - Directorio de contactos]]

## Herramientas (12)

- **Lectura:** `buscar_contacto`, `ver_contacto`, `tareas_del_contacto`, `listar_campos_contacto`
- **Escritura:** `crear_contacto`, `editar_contacto`, `nota_contacto`, `vincular_persona_empresa`, `contacto_interno`, `reactivar_contacto`, `vincular_contacto`
- **Destructivas:** `inactivar_contacto`

### `buscar_contacto`

**Tipo:** Lectura

> Busca contactos (empresas o personas) del Directorio General. 'texto' busca en nombre, identificacion, email y telefono. Filtros opcionales: tipo, ciudad, sector, estado (por defecto solo activos y prospectos).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `texto` | string |  |  |
| `tipo` | string (empresa / persona) |  |  |
| `ciudad` | string |  |  |
| `sector` | string |  |  |
| `estado` | string (activos / inactivos / todos) |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "buscar_contacto",
  "arguments": {}
}
```

### `ver_contacto`

**Tipo:** Lectura

> Ficha completa de un contacto: datos, campos configurables, personas/empresas vinculadas, ultimas notas y resumen de tareas. 'contacto' = id, identificacion o nombre exacto (ver buscar_contacto).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `contacto` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "ver_contacto",
  "arguments": {
    "contacto": "<contacto>"
  }
}
```

### `crear_contacto`

**Tipo:** Escritura

> Registra un contacto (empresa o persona) en el Directorio General. No duplica: si ya existe con la misma identificacion o telefono, devuelve el existente. Devuelve 'contacto_id'.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nombre` | string | SI | Persona o razon social |
| `tipo` | string (empresa / persona) |  |  |
| `identificacion` | string |  | NIT o cedula |
| `tipo_identificacion` | string (nit / cedula / correo / telefono) |  |  |
| `ciudad` | string |  |  |
| `email` | string |  |  |
| `telefono` | string |  |  |
| `sector` | string |  |  |
| `cargo` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "crear_contacto",
  "arguments": {
    "nombre": "<nombre>"
  }
}
```

Ejemplo real (valores genericos):

```json
{
  "name": "crear_contacto",
  "arguments": {
    "nombre": "Industrias Ejemplo SAS",
    "tipo": "empresa",
    "identificacion": "900123456",
    "tipo_identificacion": "nit",
    "ciudad": "Bogota",
    "email": "compras@ejemplo.com"
  }
}
```

### `editar_contacto`

**Tipo:** Escritura

> Cambia SOLO los datos indicados de un contacto (lo que no envies queda igual). 'campos' = campos configurables de la ficha como {clave_o_etiqueta: valor} (ver listar_campos_contacto; si la clave esta en varias fichas usa ficha.clave, ej. base.direccion); valor vacio lo limpia. Se niega si el NIT o el telefono nuevo ya pertenece a otro contacto. Queda nota y auditoria del cambio.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `contacto` | string | SI |  |
| `nombre` | string |  |  |
| `identificacion` | string |  |  |
| `tipo_identificacion` | string (nit / cedula / correo / telefono / ninguno) |  |  |
| `ciudad` | string |  |  |
| `email` | string |  |  |
| `telefono` | string |  |  |
| `sector` | string |  |  |
| `cargo` | string |  |  |
| `estado` | string (activo / prospecto) |  |  |
| `campos` | object |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "editar_contacto",
  "arguments": {
    "contacto": "<contacto>"
  }
}
```

### `nota_contacto`

**Tipo:** Escritura

> Agrega una nota a la ficha del contacto (accion='agregar', por defecto) o lista sus notas (accion='listar').

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `contacto` | string | SI |  |
| `accion` | string (agregar / listar) |  |  |
| `texto` | string |  |  |
| `tipo` | string |  | Nota, Llamada, Reunion, Correo... (por defecto Nota) |

Llamada minima (solo obligatorios):

```json
{
  "name": "nota_contacto",
  "arguments": {
    "contacto": "<contacto>"
  }
}
```

### `vincular_persona_empresa`

**Tipo:** Escritura

> Liga una persona EXISTENTE a una empresa con su cargo (accion='vincular', por defecto; si ya estaba ligada actualiza el cargo) o la desliga (accion='desvincular'). Para una persona nueva usa contacto_interno.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `persona` | string | SI |  |
| `empresa` | string | SI |  |
| `cargo` | string |  |  |
| `accion` | string (vincular / desvincular) |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "vincular_persona_empresa",
  "arguments": {
    "persona": "<persona>",
    "empresa": "<empresa>"
  }
}
```

### `contacto_interno`

**Tipo:** Escritura

> Agrega una persona dentro de la ficha de una empresa (ej. su contador o gerente de compras) con su cargo. Si la persona ya existe (misma identificacion, email o telefono) la liga en vez de duplicarla. Para editarla despues usa editar_contacto.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `empresa` | string | SI |  |
| `nombre` | string | SI |  |
| `cargo` | string |  |  |
| `email` | string |  |  |
| `telefono` | string |  |  |
| `identificacion` | string |  | Cedula (opcional) |

Llamada minima (solo obligatorios):

```json
{
  "name": "contacto_interno",
  "arguments": {
    "empresa": "<empresa>",
    "nombre": "<nombre>"
  }
}
```

### `tareas_del_contacto`

**Tipo:** Lectura

> Tareas ligadas a un contacto (como cliente/solicitante), abiertas y/o terminadas. Solo las que el usuario puede ver.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `contacto` | string | SI |  |
| `estado` | string (abiertas / terminadas / todas) |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "tareas_del_contacto",
  "arguments": {
    "contacto": "<contacto>"
  }
}
```

### `listar_campos_contacto`

**Tipo:** Lectura

> Campos configurables de la ficha del contacto (clave, etiqueta, tipo y opciones). Consultalo antes de llenar 'campos' en editar_contacto: no inventes claves.

_Sin parametros._

Llamada minima (solo obligatorios):

```json
{
  "name": "listar_campos_contacto",
  "arguments": {}
}
```

### `inactivar_contacto`

**Tipo:** DESTRUCTIVA (confirma con el usuario)

> Da de baja un contacto (estado Inactivo; NO se borra y se puede reactivar). Exige 'motivo'. Confirma con el usuario antes de usarla.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `contacto` | string | SI |  |
| `motivo` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "inactivar_contacto",
  "arguments": {
    "contacto": "<contacto>",
    "motivo": "<motivo>"
  }
}
```

### `reactivar_contacto`

**Tipo:** Escritura

> Reactiva un contacto inactivo (vuelve a estado Activo). Exige 'motivo'.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `contacto` | string | SI |  |
| `motivo` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "reactivar_contacto",
  "arguments": {
    "contacto": "<contacto>",
    "motivo": "<motivo>"
  }
}
```

### `vincular_contacto`

**Tipo:** Escritura

> Liga un contacto del Directorio a una tarea (queda como su cliente/solicitante). 'contacto' = id, identificacion o nombre exacto (ver buscar_contacto).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `numero` | string | SI |  |
| `contacto` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "vincular_contacto",
  "arguments": {
    "numero": "<numero>",
    "contacto": "<contacto>"
  }
}
```
