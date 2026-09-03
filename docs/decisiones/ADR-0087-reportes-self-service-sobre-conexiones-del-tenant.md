# ADR-0087: Reportes self-service sobre conexiones de datos del tenant

**Status:** Proposed
**Date:** 2026-09-03
**Deciders:** Sesion de reportes (solicita), sesion de codigo (implementa), owner del producto
**Relacionado:** ADR-0064 (conector externo gobernado), ADR-0084 (conexiones de datos por tenant),
ADR-0066 (renderizador de panel generico por spec). Origen: prueba en SOLDARCO
(SOLDARCO_MULTISYS -> dataset `items_demo` -> panel "Inventario Multisys (SQL)", v0.15.155 / f732d9f).

## Context

Ya es posible que un PanelSpec use un dataset EXTERNO (ADR-0064) como fuente Main y renderice datos EN
VIVO del SQL ajeno, tenant-safe y sin exponer la cadena de conexion. Pero al llevar una conexion CREADA
POR EL TENANT (ADR-0084, `/conexiones-datos`, owner_tenant_id = ese tenant) hasta un panel, aparecieron
tres fricciones que obligaron a escribir la BD a mano (UPDATE de `fields_json`, INSERT de un grant) —
inaceptable como flujo de usuario final:

1. **El limite de autoria capa el reporte.** El dataset `items_demo` es `SELECT TOP(@limite) * FROM ...`
   con `@limite` DefaultValue = 5. Al ejecutar el panel, `ExternalParameterBinder` cae al DefaultValue y
   el reporte se trunca a 5 filas. El default de AUTORIA/preview contamina la EJECUCION de reportes.
2. **El tenant no puede definir los campos de salida.** El editor de "Campos de salida (JSON)" solo vive
   en `/fuentes-externas` (policy `PlatformOperator`). `/conexiones-datos` (tenant, policy
   `Conexiones.Editar`) no lo tiene, asi que `fields_json` queda NULL -> 0 campos en el catalogo -> el
   panel no valida.
3. **La fuente propia no es reportable sin grant manual.** `ExternalReportReader.ListGrantedAsync` /
   `IsGrantedAsync` solo miran `external_data_source_grants`; una fuente con `owner_tenant_id == tenantId`
   NO aparece en el catalogo de reportes de su propio dueno hasta insertar un grant (que ademas el
   clasificador de escritura bloquea por ser tabla de permisos).

Fuerzas: multi-tenant real (nada cross-tenant, cadena jamas expuesta), tope de seguridad de filas ya
existente (`ExternalQuery.MaxRows`), y la meta de autonomia de la sesion de reportes (autoria como DATO,
sin BD ni deploy por reporte).

## Decision

Cerrar las tres fricciones para que el TENANT deje una conexion externa reportable de punta a punta por
UI, sin tocar la BD:

1. **El default de autoria de un parametro NO aplica a reportes.** En contexto de reporte, el unico tope
   efectivo de filas es `ExternalQuery.MaxRows` (tope duro del sistema), no el DefaultValue de autoria.
2. **Editor de campos de salida en `/conexiones-datos`** (tenant), validado y persistido por el servicio
   del tenant.
3. **El catalogo de reportes expone al tenant sus fuentes PROPIAS** (owner) ademas de las concedidas.

## Options Considered

### Cambio 1 -- que el limite no aplique a reportes

#### Opcion A: parametro marcado como "limite de filas" -> en reporte se enlaza a MaxRows (Recomendada)
Un flag/kind en el parametro (p.ej. `ExternalDataParameterBinding.RowLimit` o `bool EsLimiteDeFilas`). En
ejecucion de reporte ese param se enlaza a `ExternalQuery.MaxRows`; la consola "Ejecutar" del editor sigue
usando el valor tecleado.

| Dimension | Assessment |
|-----------|------------|
| Complejidad | Baja-Media (un flag + rama en el binder) |
| Riesgo | Bajo: explicito, sin adivinar |
| Generalidad | Alta: cualquier dataset con TOP(@param) |

**Pros:** explicito; no rompe params de negocio (fechas, filtros) que si deben respetar su valor.
**Cons:** requiere marcar el parametro (una casilla en el editor).

#### Opcion B: en reporte, TODO param Input sin valor -> MaxRows en vez de DefaultValue
**Pros:** cero configuracion.
**Cons:** peligroso: un param que NO es limite (una fecha, un id) recibiria MaxRows y romperia el SQL o
devolveria datos incorrectos. Rechazada.

### Cambio 3 -- fuente propia reportable sin grant manual

#### Opcion A: incluir al owner en la consulta del catalogo (Recomendada)
`ListGrantedAsync`/`IsGrantedAsync` tratan como accesible toda fuente con `owner_tenant_id == tenantId`
ademas de las de `external_data_source_grants`.

| Dimension | Assessment |
|-----------|------------|
| Complejidad | Baja (un OR en el predicado) |
| Datos | No genera filas; sin backfill |
| Semantica | El dueno siempre ve lo suyo (coherente con ADR-0084) |

**Pros:** simple; sin filas huerfanas; nada que migrar.
**Cons:** el concepto "grant" deja de ser la unica puerta (hay que documentarlo).

#### Opcion B: auto-crear un grant al crear/habilitar la conexion
**Pros:** mantiene el grant como unica puerta.
**Cons:** filas redundantes, backfill para las existentes, y hay que mantener el grant sincronizado con el
estado de la fuente. Rechazada como principal; queda como equivalente si se prefiere.

### Cambio 2 -- editor de campos en /conexiones-datos

#### Opcion A: grid de campos + boton "Detectar campos" (Recomendada)
Reusa el path de "Ejecutar" (que ya corre la consulta y devuelve columnas) para prellenar Name + Type
inferido; el usuario marca cuales exponer. Validado con `ExternalDataJson.ValidateFieldsJson`.

**Pros:** self-service real; no se teclea JSON; menos errores de tipo/token.
**Cons:** algo mas de UI.

#### Opcion B: solo textarea JSON (como /fuentes-externas)
**Pros:** minimo esfuerzo, reusa validacion existente.
**Cons:** el usuario teclea JSON a mano (tokens de tipo exactos). Aceptable como piso; A es la meta.

## Trade-off Analysis

El eje comun es "explicito y seguro" sobre "magico y comodo": en el cambio 1 preferimos marcar el
parametro (A) antes que reinterpretar todos los Input (B), porque un reporte que devuelve datos
silenciosamente incorrectos es peor que pedir una casilla. En el cambio 3, incluir al owner en la
consulta (A) evita estado duplicado frente al auto-grant (B). En el cambio 2, "detectar campos" (A)
convierte un flujo que hoy exige SQL/JSON manual en algo de usuario final; el textarea (B) es el piso
aceptable. Todo dentro del limite duro: el render corre via `IReportDataSource` (filtro global), la
cadena nunca se expone, y `MaxRows` sigue siendo el techo de filas.

## Consequences

- **Mas facil:** el tenant deja una conexion reportable sin BD; los paneles no se truncan al default de
  preview; la fuente propia aparece sola en "Nuevo panel".
- **Mas dificil / a vigilar:** el binder gana una rama por contexto (reporte vs consola); "acceso a
  reportes" ahora = owner OR grant (documentar en ADR-0064/0084); el editor de campos suma superficie de
  UI/tests en el lado tenant.
- **A revisar:** si mas adelante se quiere limitar por ROL dentro del tenant (rol_id del grant), el
  camino owner-siempre-ve del cambio 3 debera convivir con ese scoping.

## Action Items

1. [ ] Cambio 1: flag de parametro "limite de filas"; en ejecucion de reporte enlazarlo a
   `ExternalQuery.MaxRows`; la consola "Ejecutar" respeta el valor tecleado. Test: RowLimit -> MaxRows en
   reporte pero DefaultValue en consola.
2. [ ] Cambio 3: `ListGrantedAsync`/`IsGrantedAsync` incluyen `owner_tenant_id == tenantId`. Test:
   catalogo expone la fuente propia sin grant; sigue sin fuga cross-tenant.
3. [ ] Cambio 2: editor de campos de salida en `/conexiones-datos` (grid + "Detectar campos" reusando el
   path de Ejecutar), validado con `ExternalDataJson.ValidateFieldsJson`, persistido por
   `ITenantDataConnectionService` (agregar Fields a su Save de dataset si falta). Test: guardar dataset con
   campos desde /conexiones-datos.
4. [ ] Aceptacion global: en SOLDARCO, el panel "Inventario Multisys (SQL)" muestra TODOS los items (hasta
   MaxRows) y la fuente items_demo aparece en "Nuevo panel" sin ningun UPDATE/INSERT manual.
5. [ ] Gates: multi-tenant real; solo ASCII; CI (gitleaks, build Release, dotnet format, tests, matriz
   dual); commit/PR a `fase-0/clon-backbone`; nota en ADR-0064 y ADR-0084; PROGRESO.md.
