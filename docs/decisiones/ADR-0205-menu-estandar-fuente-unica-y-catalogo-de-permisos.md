# ADR-0205: Menu estandar como fuente unica y catalogo de permisos completo

- Estado: Aceptado
- Fecha: 2026-11-24
- Modulo: Administrador de Menu (000194, ADR-0030) + Roles y permisos (000198, ADR-0032)

## Contexto

Tres fallas reportadas por el usuario en el Administrador de Menu:

1. Al duplicar una vista no habia donde cambiarle el nombre: el modal "Editar vista" existia pero ningun boton lo
   abria (y duplicar dos veces la misma vista fallaba porque "X (copia)" ya existia).
2. Una vista NUEVA nacia vacia y no habia forma de "cargar el menu por defecto"; el boton "Restablecer" solo
   recargaba lo guardado. Ademas, si esa vista vacia se marcaba Predeterminada, los usuarios sin vista asignada
   se quedaban sin menu.
3. En algunos tenants cliente la matriz de Roles y permisos no listaba todos los modulos: el catalogo salia solo de
   los items de la vista PREDETERMINADA del tenant, asi que un menu recortado (o sembrado antes de que existiera
   un modulo nuevo, ej. Cerebros, Formacion, Calendario operativo) dejaba esos modulos sin forma de dar permisos.

La causa comun: el arbol canonico del menu vivia solo dentro de `DatabaseSeeder.EnsureDefaultMenuAsync`
(Infrastructure), inaccesible para el editor y para el catalogo de permisos.

## Decision

1. **`MenuCanonicalTree`** (Application/MenuConfig): FUENTE UNICA del menu estandar (enlaces rapidos, secciones
   con slug e icono, elementos con ruta y codigo legacy). `BuildNodes(tenantId, viewId)` produce exactamente lo que
   sembraba el alta; el seeder ahora lo usa (sin cambio de datos). Al agregar un modulo nuevo al producto se agrega
   aqui.
2. **`IMenuConfigService.LoadStandardMenuAsync(viewId)`**: en una vista vacia la deja igual al estandar; en una con
   contenido solo AGREGA lo que falta (elementos por ruta en cualquier parte de la vista; secciones por slug y luego
   por nombre; si no existe se crea al final). Nunca cambia ni borra lo existente (nombres, orden, ocultos).
   Idempotente y en transaccion.
3. **UI** (`/configuracion-menu`): boton "Renombrar" en cada tarjeta y en el encabezado del editor; "Nueva vista"
   pregunta el contenido inicial (menu estandar por defecto, vacia o copia de otra vista); "Cargar menu estandar" en
   el editor; "Restablecer" pasa a llamarse "Descartar cambios"; duplicar usa un nombre libre ("(copia 2)"...) y abre
   de una el modal para renombrar.
4. **Catalogo de permisos** (`RolService.GetModuleCatalogAsync`): items Ready de TODAS las vistas del tenant (la
   predeterminada primero: da nombre y grupo) + los items Ready del menu estandar que falten. Los stubs `modulo/...`
   siguen fuera. El selector "Agregar elemento" del editor tambien ofrece los modulos estandar que el tenant no tiene.
5. **Menu resuelto**: si la vista predeterminada esta vacia, se cae a la vista mas rica (antes solo si ninguna era
   predeterminada).

## Consecuencias

- Sin migracion. Un tenant existente no cambia hasta que use "Cargar menu estandar" o cree una vista nueva.
- La matriz de permisos puede mostrar modulos que el tenant aun no tiene en su menu; dar permiso no los agrega al
  menu (eso se hace en el editor). El enforcement no cambia.
- Pruebas dual: `MenuConfigEditorTests` (cargar estandar en vista vacia e idempotente, completar una parcial sin
  tocar lo renombrado/oculto, predeterminada vacia cae a la mas rica, selector con modulos estandar) y
  `RolesTests.ModuleCatalog_IncludesOtherViewsAndStandardModules`; unitarias `MenuCanonicalTreeTests`.
