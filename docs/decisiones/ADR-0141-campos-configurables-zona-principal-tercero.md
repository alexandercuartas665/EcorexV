# ADR-0141: Campos configurables intercalados en la zona principal del tercero + Sector como lista

**Status:** Accepted
**Date:** 2026-10-30
**Deciders:** Alexander Cuartas (producto), Claude Opus 4.8 (implementacion)

## Contexto

La ficha del tercero (modal EDITAR/NUEVO TERCERO, motor Clasico en `/directorio-general`) tiene
8 campos PRINCIPALES fijos con controles nativos (Razon social, Identificacion, Ciudad,
Sector/Industria, Correo, Telefono, Vendedor asignado, Estado) y, debajo, las fichas colapsables
configurables (Base, Datos fiscales, etc.) servidas por el motor de campos EAV
(`TerceroFieldDefinition` -> valores en `Tercero.FichasJson`).

El usuario pidio dos cosas:

1. Poder insertar campos NUEVOS configurados ENTRE los campos principales (no solo en las fichas
   colapsables de abajo), eligiendo junto a cual principal aparecen.
2. Que el campo principal "Sector / Industria", hoy texto libre, se pueda llenar desde una LISTA
   de opciones configurable en el editor de campos.

Decisiones de alcance confirmadas con el usuario:

- Solo el motor **Clasico** (el modal de `/directorio-general`); el motor Modular/Capa 8 queda fuera.
- **Enfoque A**: los 8 principales siguen fijos con sus controles nativos (NO se vuelven
  reordenables); los campos nuevos se INTERCALAN entre ellos mediante un "ancla" (despues de cual
  principal aparecen).
- Un valor de Sector preexistente que no este en la nueva lista se **conserva** como opcion extra
  al tope del desplegable (no se pierde dato de texto libre previo).

## Decision

Reutilizar el motor de campos configurables existente y evitar migraciones:

- **Ficha reservada `general`** ("Datos principales"): nueva ficha de sistema, sembrada por
  `TerceroFichaService` (tambien backfill para tenants existentes). Sus campos se renderizan INLINE
  en la zona principal (no como seccion colapsable); se excluye de la lista de fichas colapsables.
  Sus valores se guardan igual que cualquier ficha, en `Tercero.FichasJson["general"]`.
- **Ancla por campo + modo de Sector** viven en UNA fila de `TenantConfiguration`
  (`ConfigKey = "directorio.general.layout"`, valor JSON), NO en columnas nuevas -> **sin migracion**.
  Servicio nuevo `IDirectorioLayoutConfigService` (en `Ecorex.Application/Directorio/`):
  `GetAsync`, `SetSectorAsync(isList, options)`, `SetAnchorAsync(fieldKey, anchor)`,
  `RemoveAnchorAsync(fieldKey)`. Anclas validas = los 8 principales; un campo `general` se pinta
  DESPUES de su ancla (default `sector` si no se configuro).
- **Por que el ancla NO va en `SortOrder`**: el SortOrder del campo se renumera al mover arriba/abajo
  (`ReorderFieldsAsync`), asi que codificar la posicion ahi seria fragil. El ancla es ortogonal al
  orden y pertenece al layout del tenant, no al campo -> vive en `TenantConfiguration`. Esto ademas
  deja intacto `TerceroFieldService` (menor riesgo).
- **UI de configuracion** (modal "Configurar campos", ficha "Datos principales" seleccionada):
  bloque "CAMPO SECTOR / INDUSTRIA" (checkbox "Llenar desde una lista" + textarea de opciones, una
  por linea, + "Guardar Sector") y, en el form de campo nuevo/editar, un selector "Posicion"
  (= ancla) visible solo para la ficha `general`.
- **UI del modal del tercero**: cada campo `general` se intercala con
  `GeneralFieldsAfter(anchor)` tras su principal; Sector se pinta como `<select>` (opciones de
  `SectorOptionList()`, que ANTEPONE el valor actual si no esta en la lista) cuando el layout lo
  marca como lista, si no, como el `<input>` de texto de siempre.

## Opciones consideradas

### Opcion A (elegida): ficha reservada `general` + layout en TenantConfiguration
| Dimension | Evaluacion |
|-----------|------------|
| Complejidad | Baja: reutiliza motor EAV + patron TenantConfiguration ya usado (AutorizacionComprasConfig) |
| Migracion BD | Ninguna |
| Riesgo | Bajo: no toca TerceroFieldService ni el esquema |
| Fidelidad al prototipo | Alta: los 8 principales quedan intactos |

**Pros:** cero migraciones; self-serve con el editor de campos existente; aislado por tenant.
**Cons:** el ancla vive aparte del campo (dos lecturas), no en la misma entidad.

### Opcion B (descartada): nueva columna `anchor` en `TerceroFieldDefinition`
**Pros:** el ancla viaja con el campo.
**Cons:** requiere migracion DUAL (PG + SQL Server) + snapshots; toca una entidad central del motor
de campos por una necesidad especifica de un solo motor de UI. Mas riesgo por poco beneficio.

### Opcion C (descartada): principales reordenables / totalmente dinamicos
Romperia la fidelidad milimetrica al prototipo y los controles nativos (ciudad con buscador,
telefono con prefijo, estado). El usuario lo descarto explicitamente (Enfoque A).

## Consecuencias

- Mas facil: agregar/mover campos en la zona principal sin tocar codigo ni esquema; el Sector pasa a
  lista sin perder valores historicos.
- Mas dificil / a vigilar: hay DOS fuentes para la zona principal (campos nativos + ficha `general`);
  quien lea el layout debe recordar el default de ancla `sector`. El valor de un campo `general`
  borrado queda huerfano en `FichasJson` (inocuo, se ignora).
- Pendiente opcional: si el motor Modular/Capa 8 necesitara lo mismo, habria que portar el render
  (no incluido aqui por decision de alcance).

## Validacion (E2E)

Dev preview 5234 (AGRO, BD local `ecorex` en 5442, login calidad@agrometalicas.com):
agregado campo "Pais" (Texto) anclado "Despues de Ciudad" y Sector configurado como lista
(Agropecuario/Industrial/Servicios/Comercio). En el modal del tercero "360 Investments S.A.S":
"Pais" aparecio intercalado tras Ciudad y "Sector / Industria" como desplegable; se guardo
Pais=Colombia y Sector=Industrial, persistieron al reabrir (lectura fresca de BD) y el registro se
revirtio limpio. Build de SuperAdmin verde (0 errores).

## Action Items

1. [x] `IDirectorioLayoutConfigService` + impl (TenantConfiguration, sin migracion).
2. [x] Ficha reservada `general` sembrada + backfill en `TerceroFichaService`.
3. [x] UI de configuracion (Sector lista + ancla por campo) en `DirectorioGeneral` / `DirectorioSharedBase`.
4. [x] Render inline intercalado + Sector desplegable en `TerceroModal`.
5. [x] Validacion E2E en dev.
6. [ ] Reflejar en el vault (INVENTARIO GENERAL) al cerrar el modulo de Directorio.
