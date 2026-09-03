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

- **Dos variantes = dos paginas/componentes FISICAMENTE independientes** (decision explicita del usuario:
  "copia completa independiente", para divergir sin acoplar):
  - `TercerosLigero` = la vista actual, en `DirectorioGeneral.razor` (ruta `/directorio-general`), intacta.
  - `TercerosEspecializado` = `DirectorioEspecializado.razor` (ruta `/directorio-especializado`), copia
    completa de la anterior (incluida su CSS scoped), hoy con un cambio visible (titulo/eyebrow) y su propio
    modal `TerceroModalEspecializado` (copia de `TerceroModal`, tambien con un marcador visible).
- **Eleccion por tenant, self-service, en el menu del tenant**: un selector en `/configuracion-entidad`
  (Ligero | Especializado) que guarda en `TenantConfiguration` (clave `directorio.variante`, tenant-scoped)
  via `IDirectoryVariantService`. Sin fila o valor desconocido => Ligero (por defecto, no rompe a nadie).
- **Un solo punto de entrada de menu** (`/directorio-general`): cada pagina lee la variante en
  `OnInitializedAsync` y, si no corresponde, redirige a la otra (`NavigateTo(..., replace: true)`). Asi el
  menu no cambia y el tenant siempre ve su variante.

## Alternativas consideradas

- **Un componente con un flag `variante`** (menos duplicacion): rechazada por el usuario; quiere copias
  independientes para divergir libremente.
- **Switcher que renderiza el componente hijo** (sin cambio de URL): exigia extraer las ~2000 lineas del
  actual a un componente; se prefirio el guard+redirect por ser mas quirurgico (toca minimamente la pagina
  existente) a cambio de un breve cambio de URL.

## Consecuencias

- **+**: cada tenant elige su vista; el equipo puede evolucionar la Especializada sin riesgo para la actual.
- **-**: duplicacion (~2000 lineas de pagina + ~2100 de modal + CSS). Doble mantenimiento hasta que las
  variantes justifiquen su divergencia; un fix comun hay que aplicarlo en ambas. Aceptado como costo del
  requisito de independencia.
- **Multi-tenant**: `TenantConfiguration` y las fuentes de datos del Directorio llevan el filtro global;
  nada cross-tenant. La variante de un tenant no afecta a otro.
- El modal compartido `TerceroModal` (usado tambien por GestorContactos y TaskWizard) NO cambia; solo la
  pagina Especializada usa su copia.

## Verificacion

Build verde. En dev (AGROMETALICAS): con la variante en Ligero, `/directorio-general` muestra la vista
actual; al cambiar a Especializado en `/configuracion-entidad`, `/directorio-general` redirige a
`/directorio-especializado` ("Directorio Especializado") y su modal muestra el marcador "ESPECIALIZADO";
al volver a Ligero, regresa a la vista actual. El valor persiste en `tenant_configurations` con el
TenantId correcto.
