# ADR-0110: Formulario REPORTABLE sin ser modulo (flag IsReportable) - extiende ADR-0068

**Estado:** Aceptado
**Fecha:** 2026-09-25
**Deciders:** equipo ECOREX.tareas (peticion de la sesion de Reportes)

## Contexto

ADR-0068 hizo que las RESPUESTAS de un formulario sean fuente de reportes con clave estable `form:{code}`,
pero SOLO si el formulario es MODULO (`form_definitions.is_module = true`): `FormResponseReportReader` filtra
por `IsModule` en sus 3 metodos (ListModules / Describe / Query-resolve-id). Un formulario de CAPTURA que NO es
modulo (p.ej. la COT de AGROMETALICAS) no aparece en el catalogo de reportes. Forzar `is_module = true` es
indeseable: reactiva pagina de modulo (`/m/{code}`), nodo de menu y export, cambiando la naturaleza del form.

## Decision

Nuevo flag independiente **`FormDefinition.IsReportable`** (columna `is_reportable`, bool NOT NULL default
false, migracion DUAL PG + SQL Server). Un formulario es fuente de reportes si **`IsModule` O `IsReportable`**.
`FormResponseReportReader` cambia sus 3 condiciones a `(d.IsModule || d.IsReportable) && !d.IsArchived` (y en la
resolucion de id: `(d.IsModule || d.IsReportable) && d.Code == code`). Sin otros cambios de contrato: la clave
sigue `form:{code}`; los campos salen de las `form_questions` escalares + sinteticos.

Editable self-serve en el DISENADOR (regla del proyecto): checkbox "Disponible para reportes" junto al toggle
"Es un modulo", en la pestana Modulo de Propiedades. Se persiste por `SetTransactionalAsync` (panel Propiedades),
que NO toca `IsModule` ni la logica de menu/modulo/export.

## Consecuencias

- Un formulario reportable-no-modulo aparece en el catalogo de reportes (form:{code}) pero NO en el menu, NO
  expone `/m/{code}` y NO cambia como se guardan sus respuestas.
- Los modulos existentes siguen reportables (un modulo ya es reportable por si mismo). Un form normal (ambos
  flags false) no aparece.
- Aditivo y multi-tenant (se hereda el filtro global). Sin impacto en `IsModule`.

## Alternativas descartadas

- Forzar `is_module = true`: cambia la naturaleza del formulario (menu, /m/{code}, export). Rechazado.
