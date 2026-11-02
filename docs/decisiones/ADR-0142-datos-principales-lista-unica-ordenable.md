# ADR-0142: "Datos principales" como lista unica ordenable (campos duros + dinamicos)

**Status:** Accepted
**Date:** 2026-10-30
**Deciders:** Alexander Cuartas (producto), Claude Opus 4.8 (implementacion)

## Contexto

ADR-0141 permitio insertar campos dinamicos ENTRE los 8 campos duros de la ficha del tercero (motor
Clasico, `/directorio-general`) y llenar "Sector / Industria" desde una lista. La posicion de cada
campo dinamico se fijaba con un desplegable "Posicion / ancla" ("Despues de Sector...") y el mapa de
anclas vivia en `TenantConfiguration` (clave `directorio.general.layout`).

Limitaciones que motivan este ADR:

- En el configurador, los 8 campos DUROS **no se veian**: la lista solo mostraba los dinamicos. No se
  podian reordenar los duros entre si.
- Un dinamico solo podia caer "despues de" un duro (ancla), no en cualquier punto de la secuencia.

El usuario pidio que "Datos principales" se vea y opere **igual que una ficha dinamica**: una sola
lista con TODOS los campos (duros + dinamicos), reordenable con flechas, donde los duros solo se
reposicionan (y, si son lista como Sector, editan sus opciones) y los dinamicos se crean/editan/borran
y se mueven libremente entre los duros.

## Decision

Introducir un **ORDEN unico** de la zona principal como una lista de *tokens* en el mismo
`TenantConfiguration` (`directorio.general.layout`), sin migracion:

- token de campo DURO: la clave del principal con prefijo reservado `#` (`#nombre`, `#sector`, ...).
- token de campo dinamico ("general"): la clave del campo (`pais`, ...).

Esa lista `Order` es la **unica fuente de verdad** de la posicion y reemplaza al mapa de anclas (que se
conserva solo para sembrar `Order` la primera vez). `SectorIsList`/`SectorOptions` quedan igual.

Funcion pura compartida `DirectorioLayoutConfigService.BuildEffectiveOrder(storedOrder, anchors,
customKeys)`:

- si `storedOrder` trae datos: respeta el orden guardado (quitando tokens inexistentes), inserta los
  duros faltantes en su posicion canonica y los dinamicos faltantes tras su ancla (o al final);
- si viene vacio (tenants ADR-0141 que nunca reordenaron): reconstruye el layout ACTUAL desde las
  anclas -> el resultado es identico al que ya veian (compatibilidad hacia atras).

La misma funcion la consumen el modal del tercero (para pintar) y el configurador (para listar/ordenar).

### Alternativa descartada

Convertir los 8 duros en filas reales `TerceroFieldDefinition` para ordenarlos todos por SortOrder: su
binding es especial (Vendedor = asesor, Estado = enum, Ciudad = autocomplete, Identificacion = mini-menu;
se guardan en columnas de `Tercero`, no en `FichasJson`). Es invasivo y arriesga la fidelidad del
prototipo. Se mantiene el Enfoque A de ADR-0141: los duros siguen siendo controles nativos.

## Consecuencias

- El configurador de "Datos principales" muestra una sola lista: filas duras (tag "fijo", solo flechas)
  y filas dinamicas (ancho/editar/borrar/mover-ficha + flechas). Las flechas reordenan en `Order`
  (`SetOrderAsync`). Se elimino el selector "Posicion"; un dinamico nuevo nace al final y se reordena.
- La config de la lista de un campo DURO (hoy solo Sector) se edita EN LINEA bajo su propia fila (lapiz
  que despliega checkbox "Llenar desde lista" + opciones + Guardar), no en un bloque suelto fuera de la
  lista. Estado `_cfgFixedEditing`; `FixedIsConfigurable(key)` decide que duro ofrece el lapiz.
- El modal del tercero pinta la zona principal recorriendo `EffectiveOrder()`: por token, el bloque
  nativo del duro (Nombre/Identificacion solo en modo editar; `#sector` = Sector o Cargo) o
  `GeneralInline` del dinamico. Cada control conserva markup/clases/bindings -> fidelidad milimetrica;
  con `Order` vacio el render es identico al clasico.
- `SortOrder` de los campos dinamicos "general" deja de gobernar su posicion en la zona principal
  (sigue existiendo, inofensivo). La posicion la gobierna `Order`.
- Prefijo `#` reservado para tokens duros (las claves de campo son slugs: letras/digitos/`_`, nunca `#`).
- Sin migracion (todo en `TenantConfiguration`). Alcance: motor Clasico (como ADR-0141). El modal es
  compartido, asi que el render por `Order` aplica donde se use `TerceroModal`.

## Archivos

- `apps/backend/src/Ecorex.Application/Directorio/DirectorioLayoutConfigService.cs` (DTO+`Order`,
  `Stored`+`Order`, `SetOrderAsync`, `BuildEffectiveOrder`, `FixedPrefix`).
- `apps/backend/src/Ecorex.SuperAdmin/Components/Pages/DirectorioSharedBase.cs` (`_genOrder`,
  `RebuildGenOrderAsync`, `MoveGeneralOrderAsync`; crear/borrar persisten `Order`).
- `apps/backend/src/Ecorex.SuperAdmin/Components/Pages/DirectorioGeneral.razor` (lista combinada;
  quitar "Posicion").
- `apps/backend/src/Ecorex.SuperAdmin/Components/Shared/TerceroModal.razor` (+`.razor.cs`) (render por
  `EffectiveOrder`).
- `apps/backend/src/Ecorex.SuperAdmin/AppVersion.cs` (0.16.250).

## Action Items

1. [ ] Deploy a prod (OK del usuario).
2. [ ] Reflejar en el vault (INVENTARIO GENERAL) al cerrar el modulo Directorio.
