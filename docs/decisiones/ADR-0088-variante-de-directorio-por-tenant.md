# ADR-0088: Variante del Directorio General elegible por el tenant (Ligero | Especializado)

**Status:** Accepted
**Date:** 2026-09-03
**Deciders:** Usuario (Alexander), sesion de codigo

## Contexto

A unos clientes no les gusta como se ve el Directorio General (modulo 000232) y a otros si. Se quiere que
cada tenant pueda elegir entre dos presentaciones del mismo modulo, y que la eleccion tambien afecte el
modal de crear/editar un Tercero. Hoy la segunda variante es una replica del actual con un cambio ligero,
pero debe poder DIVERGIR libremente en el futuro sin tocar la variante base.

## Decision

Arquitectura final: **una sola CAPA DE LOGICA compartida + dos FRONTS delgados** (cada front consume el
mismo backend; solo difiere el markup/CSS). Se descarta la duplicacion completa de la primera iteracion.

- **Logica compartida**: `DirectorioSharedBase` (`ComponentBase`, en `DirectorioSharedBase.cs`) concentra
  TODO el estado, los servicios inyectados (`[Inject]`) y los handlers del Directorio. Las dos vistas
  heredan de ella (`@inherits`), asi que consumen el MISMO backend/estado.
- **Dos fronts (vistas .razor delgadas)**: `DirectorioGeneral.razor` (Ligero, `/directorio-general`) y
  `DirectorioEspecializado.razor` (`/directorio-especializado`). Cada una es SOLO markup + su CSS scoped
  (el "front") y declara `protected override DirectoryVariant ViewVariant`. Hoy la Especializada cambia
  titulo/eyebrow y pasa `Especializado="true"` al modal; puede divergir mas ajustando solo su markup.
- **Modal COMPARTIDO**: el `TerceroModal` unico (usado tambien por GestorContactos/TaskWizard) gana un
  parametro `[Parameter] bool Especializado` que solo cambia detalles visuales (marcador en el encabezado).
  No hay copia del modal.
- **Eleccion por tenant, self-service**: selector en `/configuracion-entidad` (Ligero | Especializado) que
  guarda en `TenantConfiguration` (clave `directorio.variante`, tenant-scoped) via `IDirectoryVariantService`.
  Sin fila => Ligero (por defecto).
- **Un solo punto de menu** (`/directorio-general`): la base lee la variante en `OnInitializedAsync` y, si
  no coincide con el `ViewVariant` de la vista, redirige a la otra (`NavigateTo(..., replace: true)`).

## Alternativas consideradas

- **Copia completa independiente** (primera iteracion, entregada en v0.15.158): dos paginas + dos modales
  duplicados fisicamente. Rechazada por su costo de mantenimiento (un fix comun en dos lugares); se
  refactorizo a esta capa compartida a peticion del usuario ("que cada front consuma la misma capa de
  backend y luego ajustamos los cambios").
- **Un unico componente con un `@if(variante)`**: menos archivos, pero mezcla las dos presentaciones en un
  mismo markup; se prefirio vistas separadas (fronts) sobre una base comun para que cada una evolucione sola.

## Consecuencias

- **+**: cero duplicacion de LOGICA (un solo lugar para el backend/estado/handlers). Cada front evoluciona
  por su lado tocando solo su markup/CSS. El modal es unico.
- **-**: queda duplicacion de PRESENTACION (markup + CSS scoped por vista), que es justo lo que debe
  divergir por variante; un cambio de LAYOUT comun hay que reflejarlo en ambos markups.
- **Multi-tenant**: `TenantConfiguration` y las fuentes del Directorio llevan filtro global; nada
  cross-tenant.

## Verificacion

Build verde. En dev (AGROMETALICAS): con Ligero (por defecto) `/directorio-general` muestra la vista actual
(KPIs cargan via la base compartida) y el modal sin marcador; al configurar Especializado en
`/configuracion-entidad`, `/directorio-general` redirige a `/directorio-especializado` ("Directorio
Especializado") y el modal muestra "ESPECIALIZADO"; al volver a Ligero, regresa. El valor persiste en
`tenant_configurations` con el TenantId correcto.
