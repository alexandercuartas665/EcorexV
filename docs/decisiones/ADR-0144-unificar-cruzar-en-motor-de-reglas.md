# ADR-0144: unificar "Cruzar" dentro del motor de reglas (fuentes integradas ERP/Newton)

**Status:** Accepted
**Date:** 2026-11-02
**Deciders:** Alexander Cuartas (producto), Claude Opus 4.8 (implementacion)

## Contexto

El conciliador DIAN (`/conciliacion-dian`) tenia dos acciones parecidas por caminos distintos:

- **Cruzar** (`ConciliacionDianService.CruzarAsync`): cruce FIJO, hardcoded, sin configuracion ni
  simulacion. VLOOKUP del No. de factura del proveedor contra la tabla ERP, escribe `NumFacturaSoldarco`,
  auto-aprueba si hay 1 coincidencia (solo si el usuario no toco el toggle manual), marca `Inconsistencia!`
  si hay >1, y prende `PlataformaProveedor` si el CUFE esta en NEWTON.
- **Aplicar reglas** (`ConciliacionRuleService.AplicarReglasAsync`): motor CONFIGURABLE por tenant
  (VLOOKUP contra Contenedor + condiciones Y/O), con dry-run.

El usuario pidio eliminar esa dualidad: que el cruce sea reglas del motor configurable, editable sin codigo
y con simulacion (un solo boton "Aplicar reglas").

Decisiones confirmadas con el usuario:

- **Fuente ERP/NEWTON = fuentes INTEGRADAS en el motor** (no volcar a Contenedores). Como esas tablas son
  DUMMY transitorias (ver `ConciliacionDianDummySources.cs`: Fase 2 las reemplaza por la conexion externa,
  ADR-0084), el VLOOKUP gana un "origen" interno; hoy lee las dummy y al migrar solo cambia el lector del
  motor, la regla y la UI quedan iguales.
- **Transicion = boton "Agregar cruce estandar"** que inserta las 3 reglas (idempotente) + sembrar esas
  reglas en el tenant demo. Cada tenant adopta cuando quiere; se quita el boton "Cruzar".

## Decision

Sin migracion EF: el origen usa Guids centinela, la bandera de normalizacion y las 3 reglas viven en datos
(`ConciliacionRule`) y en `DefinitionJson`. `AprobadaManual`/`PlataformaProveedor` ya eran columnas.

### Fuentes integradas en el VLOOKUP (reusa el selector "Contenedor")

- `ConciliacionRuleSources` (nuevo): centinelas `ErpSourceId` / `NewtonSourceId` + columnas virtuales
  (ERP: `Referencia`, `DocumentoInterno`; Newton: `Cufe`, `TienePdf`).
- `ListContenedoresAsync` antepone esas 2 fuentes como `RuleContenedorDto` ("ERP DIAN (cruce)" /
  "Newton (plataforma)"): aparecen en el MISMO dropdown "Contenedor", sin UI nueva.
- `BuildIndexAsync` deriva a `BuildBuiltInIndexAsync` cuando el `ContainerId` es centinela: indexa desde
  `ConciliacionDianErpRefDummies` / `ConciliacionDianNewtonDummies` (solo `GuidPdf != ""`, `TienePdf`="1").

### Tres capacidades nuevas del motor (para expresar Cruzar al 100%)

- **`plataforma` escribible**: `plataforma` pasa a `Writable=true` y se vuelca a `r.PlataformaProveedor`.
- **`aprobada_manual` legible**: nuevo campo bool NO writable en el catalogo + en `BuildContext`, para que
  la condicion de auto-aprobar respete el candado manual.
- **`OnMultiple = "inconsistencia"`**: en el dedup del indice, una clave con >1 doc escribe el literal
  `Inconsistencia!`. Nueva opcion en el editor (junto a first/blank) y control "Si no encuentra".
- **`RuleNorm.Alnum`** ("solo letras y numeros", `IsLetterOrDigit`): replica exacta del `Norm()` del cruce
  legacy (quita guiones/espacios, conserva letras). Checkbox en el editor de normalizacion.

### Las 3 reglas del "cruce estandar" (`ConciliacionRuleSeed.CruceEstandar`)

1. **VLOOKUP "Cruce ERP"**: origen ERP; `Referencia = numprov`; Norm {Trim, Alnum, Upper}; devolver
   `DocumentoInterno` -> `numsol`; OnNotFound=keep; OnMultiple=inconsistencia.
2. **VLOOKUP "Newton (plataforma)"**: origen Newton; `Cufe = cufe`; Norm {Trim, Upper}; devolver `TienePdf`
   -> `plataforma`; OnNotFound=blank (no-hit -> false); OnMultiple=first.
3. **Condicion "Auto-aprobar cruzadas"**: Cuando (Y) `numsol` con contenido - `numsol` != "Inconsistencia!"
   - `aprobada`=no - `aprobada_manual`=no -> Entonces `aprobada`=true.

### UI / seed

- Se quita el boton "Cruzar" y su wrapper `CruzarAsync()` del razor. El metodo de servicio
  `ConciliacionDianService.CruzarAsync` PERMANECE (lo usa el seed demo internamente).
- Boton "+ Cruce estandar (ERP + Newton)" en la toolbar de la pestana Reglas -> `AgregarCruceEstandarAsync`
  (idempotente por nombre). `SeedDummyAsync` siembra tambien las 3 reglas en el tenant demo; el arranque
  lo invoca para el demo (ver Seguimiento 2026-11-05).

## Consecuencias

- Un solo boton ("Aplicar reglas") hace el cruce + las reglas del tenant, en orden, con dry-run.
- El cruce es editable sin codigo: se puede cambiar normalizacion, columnas, destino, que hacer ante
  multiples/no-encontrado, y el orden respecto a otras reglas.
- Al llegar Fase 2 (ADR-0084) solo cambia `BuildBuiltInIndexAsync` (el lector de la fuente); la regla
  sembrada y la UI no cambian.
- El cruce estandar NO se auto-siembra en tenants existentes: cada uno pulsa "Agregar cruce estandar".

## Alternativa descartada

Volcar ERP/NEWTON a Contenedores reales por tenant (ETL): 100% generico pero agrega piezas (seed/sync del
ETL) sobre tablas que de todas formas desaparecen en Fase 2. Las fuentes integradas abstraen el origen con
menos codigo y sin ETL.

## Validacion (E2E, dev 5263, BD local `ecorex`@5442, tenant SKY SYSTEM)

- Build `Ecorex.sln` verde. UI: boton "Cruzar" ya no existe; "Aplicar reglas" permanece.
- "+ Cruce estandar" inserta las 3 reglas (idempotente: segundo click -> "ya estaba configurado"); los dos
  VLOOKUP muestran origen "ERP DIAN (cruce)" / "Newton (plataforma)".
- Reset de las 6 filas del bot -> "Aplicar reglas" (simulacion y de verdad) reprodujo EXACTO el cruce de
  `CruzarAsync`: FE-1001..SETP-4110 -> COMP-000x aprobadas con plataforma=t, estado Conciliado; FE-5500 ->
  `Inconsistencia!` (no aprobada); FE-6600 sin cruce. Candado: con `aprobada_manual=t` la regla asigna el
  `numsol` pero NO auto-aprueba (3 aprobados en vez de 4). Datos demo restaurados tras la prueba.

## Archivos

- `apps/backend/src/Ecorex.Application/Automatizaciones/ConciliacionDian/ConciliacionRuleModels.cs`
  (`ConciliacionRuleSources`, `RuleNorm.Alnum` + `Normalize`, `plataforma` writable, `aprobada_manual`).
- `.../ConciliacionDian/ConciliacionRuleService.cs` (`ListContenedoresAsync` fuentes virtuales;
  `BuildBuiltInIndexAsync` + `DedupInto` con `inconsistencia`; `BuildContext.aprobada_manual`; volcado de
  `plataforma`; `AgregarCruceEstandarAsync` + `ConciliacionRuleSeed`).
- `.../ConciliacionDian/ConciliacionDianService.cs` (`SeedDummyAsync` siembra las 3 reglas demo).
- `apps/backend/src/Ecorex.SuperAdmin/Components/Pages/ConciliacionDian.razor` (quita "Cruzar"; boton
  "+ Cruce estandar"; controles OnNotFound/OnMultiple + checkbox Norm alnum; handlers).
- `apps/backend/src/Ecorex.SuperAdmin/AppVersion.cs` (0.16.256).

## Action Items

1. [ ] Deploy a prod (OK del usuario por version).
2. [ ] En cada tenant que use el conciliador, pulsar "Agregar cruce estandar" una vez para adoptar el cruce.
3. [ ] Reflejar en el vault (INVENTARIO GENERAL / ADRs) al cerrar el modulo.

## Seguimiento 2026-11-05 (v0.16.275): auto-sembrar el demo en el arranque

El seed del demo (seccion "UI / seed") solo ocurria al pulsar el boton "Sembrar datos dummy": el arranque
no lo disparaba, asi que un demo reconstruido desde cero NO nacia con el cruce de ejemplo. Se cablea en el
bloque de seed demo de `Ecorex.SuperAdmin/Program.cs` (rama NO-SkipDemoSeed): bajo `AmbientTenantContext.Begin`
del tenant demo se resuelve `IConciliacionDianService` y se llama `SeedDummyAsync()` (mismo patron que los
demas seeds por-tenant del bloque; idempotente, bail si ya hay un CCD). Reusa el constructor de demo probado
(facturas bot, refs ERP, Newton, 3 reglas, CCD importado y cruzado). `Ecorex.Api/Program.cs` hace seed minimo
y no toca el demo. Sin migracion. Build `Ecorex.SuperAdmin` verde. SIN deploy (a la espera del OK por version).
