---
tipo: receta
modulo: directorio
referencia: "[[T02 - Directorio de contactos]]"
---

# R02 - Directorio de contactos

Volver a [[00 - LEEME PRIMERO]]. Referencia: [[T02 - Directorio de contactos]].

## A. Buscar SIEMPRE antes de crear

```json
{"name":"buscar_contacto","arguments":{"texto":"Industrias Ejemplo"}}
{"name":"buscar_contacto","arguments":{"texto":"900123456"}}
```
`texto` busca en nombre, identificacion, email y telefono. Filtros: `tipo` (empresa/persona), `ciudad`, `sector`,
`estado` (activos por defecto; `inactivos`, `todos`). Busca por NIT y por nombre: los nombres varian ("SAS", tildes).

## B. Crear

```json
{"name":"crear_contacto","arguments":{
  "nombre":"Industrias Ejemplo SAS","tipo":"empresa","identificacion":"900123456",
  "tipo_identificacion":"nit","ciudad":"Bogota","email":"compras@ejemplo.com","telefono":"+573001234567",
  "sector":"Manufactura"}}
```
No duplica: si existe con la misma identificacion o telefono, devuelve el existente (te lo dice). Devuelve
`contacto_id`.

## C. Personas dentro de una empresa

- Persona NUEVA en la empresa:
```json
{"name":"contacto_interno","arguments":{"empresa":"Industrias Ejemplo SAS","nombre":"Laura Gomez",
  "cargo":"Jefe de compras","email":"laura@ejemplo.com","telefono":"+573009876543"}}
```
- Persona que YA existe en el directorio:
```json
{"name":"vincular_persona_empresa","arguments":{"persona":"Laura Gomez","empresa":"Industrias Ejemplo SAS","cargo":"Jefe de compras"}}
```
(`accion:"desvincular"` la desliga.)

## D. Editar y campos configurables

1. `listar_campos_contacto` -> claves validas (no inventes claves). Si una clave esta en varias fichas usa
   `ficha.clave` (ej. `base.direccion`).
2. Edita solo lo necesario:
```json
{"name":"editar_contacto","arguments":{"contacto":"900123456","telefono":"+573001112233",
  "campos":{"base.direccion":"Cra 7 # 10-20","tamano_empresa":"Mediana"}}}
```
- Valor vacio en `campos` limpia ese campo. Se niega si el NIT o telefono nuevo ya es de otro contacto.
- `estado`: `activo` o `prospecto`.

## E. Notas, tareas y relacion con el trabajo

```json
{"name":"nota_contacto","arguments":{"contacto":"900123456","texto":"Pidio visita en enero","tipo":"Llamada"}}
{"name":"nota_contacto","arguments":{"contacto":"900123456","accion":"listar"}}
{"name":"tareas_del_contacto","arguments":{"contacto":"900123456","estado":"abiertas"}}
{"name":"vincular_contacto","arguments":{"numero":"T00123","contacto":"900123456"}}
```

## F. Dar de baja

**Confirma antes.** No borra: queda Inactivo y se puede reactivar.
```json
{"name":"inactivar_contacto","arguments":{"contacto":"900123456","motivo":"Empresa liquidada"}}
{"name":"reactivar_contacto","arguments":{"contacto":"900123456","motivo":"Volvio a comprar"}}
```

## Reportes del directorio

El directorio es fuente de reportes: `native:tercero` (y `native:tercero-nota` para la bitacora). Ver
[[R07 - Crear reportes e indicadores]].
