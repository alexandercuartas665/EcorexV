# ADR-0143: titulos editables de campos duros + campo Lista dependiente de otro campo

**Status:** Accepted
**Date:** 2026-10-30
**Deciders:** Alexander Cuartas (producto), Claude Opus 4.8 (implementacion)

## Contexto

ADR-0141/0142 dejaron "Datos principales" (motor Clasico, `/directorio-general`) como una lista unica
ordenable de campos duros + dinamicos, con "Sector / Industria" configurable en linea. El usuario pidio
dos cosas mas:

1. **Renombrar el titulo de los campos DUROS** (Razon social, Identificacion, Ciudad, Sector, Correo,
   Telefono, Vendedor asignado, Estado). Sus etiquetas estaban quemadas en el modal del tercero.
2. **Crear un campo dinamico tipo Lista cuyas opciones DEPENDEN de otro campo** (p.ej. "Subsector" que
   cambia segun el "Sector / Industria" elegido). Un Select tenia una lista plana; solo `Lookup`
   (Contenedor) tenia cascada, pero Sector es una lista simple en `TenantConfiguration`, no un Contenedor.

Decisiones confirmadas con el usuario:

- Lista dependiente: **el usuario elige el campo "padre"** (desplegable "Depende de": Sector u otro
  campo tipo Lista de la ficha general), no fijo a Sector.
- Titulos de duros: **el titulo nuevo manda SIEMPRE**. Si renombra "Sector" a "Rubro", se ve "Rubro" en
  Empresa y en Persona (es el mismo slot dual). Vacio = titulo inteligente de hoy.

## Decision

Sin migracion (todo en `TenantConfiguration` o en el `Options` JSON del propio campo).

### Feature A - Titulos editables de los campos duros

- `DirectorioLayoutDto` y `Stored` ganan `Labels` (`Dictionary<string,string>`: claveDuro -> titulo).
  Nuevo `SetFixedLabelAsync(key, label)` (valida key en `ValidAnchors`; vacio/espacios limpia el
  override -> vuelve al default). `GetAsync` devuelve `Labels`.
- Configurador: el lapiz se muestra ahora en TODAS las filas duras. El editor en linea gana un input
  "Titulo" (bind `_fixLabel`, placeholder = titulo por defecto); para Sector conserva ademas la seccion
  de lista. `ToggleFixedEdit(key)` carga `_fixLabel` desde `_cfgLabels[key]`; `SaveFixedCfgAsync` guarda
  el titulo (+ opciones de Sector si aplica). `FixedRowLabel(key)` muestra el override en la lista.
- Modal del tercero: `FixedLabel(key, fallback)` = override si existe, si no el default. Las 8 etiquetas
  quemadas se reemplazan por `@FixedLabel(...)`. Regla dual: el override manda en ambos modos; sin
  override, Nombre = "Razon social"/"Nombre completo" y `#sector` = "Sector / Industria"/"Cargo" como hoy.
  `EnsureReadyAsync` carga `_generalLabels = layout.Labels`.

### Feature B - Campo dinamico tipo Lista con opciones dependientes de un campo padre

- Nuevo contrato `Ecorex.Application/Directorio/DependentSelectOptions.cs`:
  `record DependentSelectOptions(string DependsOn, Dictionary<string,List<string>> Groups)` +
  `TryParse(string? options)` (null si no es este JSON), `ToJson()`, `OptionsFor(parentValue)`. Se guarda
  en el MISMO `TerceroFieldDefinition.Options` donde un Select plano guarda su texto (mismo patron que
  Lookup/Table: `Options` sirve para texto o JSON, sin columna nueva). Se distingue de un Select plano
  porque `Options` parsea a este JSON con `DependsOn` no vacio. `DependsOn` = `"sector"` (duro) o la clave
  de otro campo Select de la ficha general.
- Configurador: con `_cfgType==Select` aparece "Depende de" (`_cfgDependsOn`): "(sin dependencia - lista
  simple)", "Sector / Industria" (solo si Sector esta en modo lista) y cada campo Select NO dependiente de
  la ficha general (distinto del que se edita). Con padre elegido, en vez del textarea plano se pinta un
  textarea de sub-opciones POR CADA valor del padre (`_cfgDepGroups`). Guardar arma el JSON via
  `BuildSelectOptions()`; editar lo parsea de vuelta. Los candidatos a padre excluyen campos ya
  dependientes (sin cadenas/recursion en v1).
- Modal (`GeneralInline`, caso Select): `DependentOptionsFor(f)` parsea `Options`; resuelve el valor del
  padre (`sector` -> `_mSector`; si no -> `GetFicha(GeneralFichaKey, DependsOn)`), devuelve
  `Groups[valorPadre]` (conserva el valor actual si no esta en el grupo). Al cambiar el padre, Blazor
  re-renderiza y el hijo recalcula. Edge: en Persona el slot `#sector` es Cargo (Sector no se captura);
  un hijo que dependa de Sector sale vacio para Persona (documentado, aceptable).

## Consecuencias

- Todo duro puede renombrarse; Sector sigue siendo el unico duro con lista de opciones editable.
- Una lista dependiente no requiere Contenedor: se define contra Sector o contra otro Select de la ficha.
- "Depende de Sector" requiere Sector en modo lista (si es texto libre no hay valores que agrupar; esa
  opcion no se ofrece). Cadenas de dependencia de mas de un nivel quedan fuera de v1.
- Sin migracion (Labels en `TenantConfiguration`; dependientes en `Options` JSON del campo). Alcance:
  motor Clasico; el modal es compartido, asi que aplica donde se use `TerceroModal`.

## Alternativa descartada

Columna nueva `DependsOnFieldId` + tabla de grupos para la lista dependiente: innecesario pudiendo
reusar `Options` (patron ya usado por Lookup/Table). Evita migracion y mantiene el campo autocontenido
(exporta/importa con su propio `Options`).

## Archivos

- `apps/backend/src/Ecorex.Application/Directorio/DirectorioLayoutConfigService.cs` (`Labels`,
  `SetFixedLabelAsync`).
- `apps/backend/src/Ecorex.Application/Directorio/DependentSelectOptions.cs` (NUEVO: parse/build).
- `apps/backend/src/Ecorex.SuperAdmin/Components/Pages/DirectorioSharedBase.cs` (editor de duro:
  titulo + sector; estado y helpers del Select dependiente; rutas de guardar/editar).
- `apps/backend/src/Ecorex.SuperAdmin/Components/Pages/DirectorioGeneral.razor` (+`.razor.css`) (lapiz en
  todas las filas duras, input Titulo, UI "Depende de" + sub-opciones por valor).
- `apps/backend/src/Ecorex.SuperAdmin/Components/Shared/TerceroModal.razor` (+`.razor.cs`) (`FixedLabel`,
  render del Select dependiente, cargar `Labels`).
- `apps/backend/src/Ecorex.SuperAdmin/AppVersion.cs` (0.16.251).

## Action Items

1. [ ] Deploy a prod (OK del usuario).
2. [ ] Reflejar en el vault (INVENTARIO GENERAL) al cerrar el modulo Directorio.
