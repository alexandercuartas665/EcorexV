# ADR-0030: Menu configurable por vista (perfil) y su editor

Fecha: 2026-07-07
Estado: Aceptado

## Contexto

El sidebar del workspace del tenant (secciones acordeon, quick-links, items con codigo
legacy y subgrupos anidados) estaba HARD-CODEADO en `NavMenu.razor`. Cada tenant y cada
perfil de usuario necesita un menu distinto: un operador ve pocas pantallas, un
administrador ve todo. Ademas el prototipo maestro (Claude Design "Administrador de Menu",
concepto TRONOX SGDEA) define una pantalla donde el tenant crea y administra "vistas del
sistema" y asigna cada usuario a la suya, sin tocar codigo.

El trabajo se hizo en dos olas:

- **Ola 1** (commit `bdda279`): modelo de datos + resolucion del arbol + sidebar data-driven.
- **Ola 2** (este ADR): la pagina editora, la ampliacion del servicio y export/import.

## Decision

### Modelo (Ecorex.Domain, TenantEntity) - Ola 1

- `MenuView`: perfil/"vista del sistema" del tenant. `Name` (unico por tenant),
  `Description?`, `IsDefault` (la usan los usuarios sin asignacion), `SortOrder`.
- `MenuNode`: nodo del arbol (adjacency-list, `ParentId` self-ref NO ACTION para evitar
  multiples rutas de cascada en SQL Server). `Kind` (enum `QuickLink`/`Section`/`Subgroup`/
  `Item`), `Name`, `IconKey` (CLAVE de icono, no el SVG), `LegacyCode`, `Route`,
  `Description?`, `HelpText?`, `State` (enum `Ready`/`InDevelopment`/`Disabled`), `IsVisible`,
  `SortOrder`. FK a `MenuView` en cascada (borrar la vista borra sus nodos).
- `TenantUser.MenuViewId` (Guid? FK NO ACTION): vista asignada al usuario; null = usa la
  `IsDefault` del tenant.

El modelo de Ola 1 cubrio toda la Ola 2: NO hizo falta migracion nueva.

### Resolucion y render (Ola 1)

- `MenuTreeBuilder` (logica pura, testeable sin BD): toma la lista plana de nodos, filtra
  invisibles (podando su descendencia), ordena por `SortOrder` y anida por `ParentId`.
- `IMenuConfigService.GetMenuForTenantUserAsync` resuelve el arbol de la vista asignada al
  usuario (o la `IsDefault` del tenant como fallback). `NavMenu.razor` es data-driven: pinta
  quick-links, label "Modulos", secciones acordeon con icono/contador/chevron, subgrupos e
  items con codigo legacy, IDENTICO al prototipo.

### Editor "Administrador de Menu" (Ola 2)

- **Servicio ampliado** (`IMenuConfigService`/`MenuConfigService`, resultados tipados
  `MenuConfigResult<T>` = Ok/NotFound/Invalid/Conflict, transaccional, tenant-scoped):
  - Vistas: `UpdateViewAsync`, `DeleteViewAsync` (cascade a nodos + desasigna usuarios;
    PROHIBIDO borrar la `IsDefault` -> `Invalid`), `SetDefaultViewAsync` (marca una y desmarca
    las demas en transaccion), `GetViewTreeAsync` (arbol COMPLETO incl. invisibles).
  - Nodos: `CreateNodeAsync`, `UpdateNodeAsync`, `ToggleNodeVisibilityAsync`,
    `SetNodeStateAsync`, `MoveNodeAsync` (reordena/reparenta; valida ciclos y coherencia de
    Kind), `DeleteNodeAsync` (cascade a descendientes en transaccion; el self-ref es NO ACTION
    asi que la descendencia se recolecta y borra a mano).
  - Asignacion: `AssignUserToViewAsync`, `ListTenantUsersWithViewAsync`.
  - Export/Import: `ExportViewAsync` -> `MenuExportDocument` portable (System.Text.Json, sin
    ids de BD); `ImportViewAsync(json, newName)` -> vista nueva (nunca IsDefault).
  - Las reglas de anidamiento (`MenuNodeKindRules.Validate`, pura) se extrajeron para poder
    testearlas sin BD: QuickLink/Section son de primer nivel; Subgroup cuelga de Section;
    Item cuelga de Section o Subgroup; un Item nunca tiene hijos.
- **Pagina** `Ecorex.SuperAdmin/Components/Pages/ConfiguracionMenu.razor`,
  `@page "/configuracion-menu"`, `@attribute [Authorize(Policy = "ConfiguracionMenu.Administrar")]`.
  Index de vistas (tarjetas con badges/contadores y acciones Editar/Duplicar/Predeterminada/
  Eliminar), editor (KPIs Secciones/Modulos/Pantallas/Ocultos, tabs Estructura/Vista previa,
  arbol con acciones por fila -ojo, subir/bajar, +, papelera-, toolbar con buscar/expandir/
  contraer/+Seccion, panel de propiedades con selector de iconos grid, Exportar/Restablecer/
  Guardar) y modal de asignacion de usuarios. El drag-and-drop del prototipo se implementa como
  botones **subir/bajar** (`MoveNode` reorder) en esta version 1; el reparenting por arrastre
  queda como deuda.
- **Iconos compartidos**: el diccionario `IconKey -> SVG` se extrajo de `NavMenu.razor` a un
  componente compartido `Components/Shared/MenuIcons.razor` (fuente unica) que consumen el
  sidebar, el arbol/selector del editor y la vista previa, para que un icono elegido en el
  editor renderice identico en el sidebar.
- **Policy** `ConfiguracionMenu.Administrar` (Program.cs). Paso 1: `RequireClaim("tenant_id")`
  (igual que TenantMember, no cambia el acceso). Paso 2 (pendiente): restringir a Owner/Admin
  del tenant derivandolo del `tenant_role`, sin tocar la pagina.

### Seed (idempotente)

`EnsureMenuConfigDemoAsync` siembra para el tenant demo SKY SYSTEM: vista **Completo** (por
defecto, ~67 nodos = transcripcion 1:1 del menu heredado) y vista **Simple** (~10 nodos), mas
dos usuarios demo `completo@sky-system.local` / `simple@sky-system.local`. El item
"Administrador de Menu" se agrego a la seccion "Sistema . General" reutilizando el code
**000194** (antes "Roles y permisos", que era un stub `modulo/...`) apuntandolo a la pagina
real `/configuracion-menu`; es un rename, no un alta, asi que los conteos del seed no cambian.

### Relacion con el Module Registry (000109)

El menu configurable NO reemplaza al Module Registry: el Registry decide QUE modulos estan
habilitados por plan/tenant (gobierno del catalogo); el menu configurable decide COMO se
presentan y agrupan en el sidebar por perfil de usuario. El paso 2 de las policies conectara
ambos (derivar el requisito real de cada pantalla desde el Registry + rol del TenantUser).

## Consecuencias

- El menu deja de estar hard-codeado: cada tenant modela su propio arbol por vista y asigna
  usuarios a vistas sin desplegar codigo.
- Aislamiento multi-tenant real: todas las operaciones del editor pasan por el filtro global;
  un tenant no ve ni toca las vistas de otro (verificado en Integration.Tests dual).
- Deuda: (1) drag-and-drop real (hoy botones subir/bajar); (2) paso 2 de la policy
  (Owner/Admin); (3) el editor persiste cada accion al vuelo, por lo que "Guardar" es una
  confirmacion/recarga (no un batch diferido).
