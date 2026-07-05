# ADR-0027: Modulo de Inventarios con catalogos normalizados

Fecha: 2026-07-05
Estado: Aceptado

## Contexto

El grupo "Sistema - Inventarios" del menu (items legacy 000556 Bodegas, 000502 Marcas,
000506 Grupo inventarios, 000606 Subgrupos, 000498 Tipos de inventario, 000066 Items)
apuntaba al stub generico `/modulo/...`. El backbone CUBOT.nails tiene un modelo de
producto (Product/ProductImage/ProductStock por Sede) con marca/categoria como TEXTO LIBRE.

Se necesita portar ese modelo de items a ECOREX pero con los catalogos NORMALIZADOS (marca,
grupo, subgrupo, tipo y bodega como entidades del tenant, no cadenas), manteniendo las reglas
del proyecto: multi-tenant real (HasQueryFilter), DAL dual PostgreSQL/SQL Server con una sola
migracion por proveedor, servicios con resultados tipados y fidelidad visual del workspace.

No se porta nada del dominio belleza (agenda, citas, SalonFieldDefinition).

## Decision

### Modelo (Ecorex.Domain, TenantEntity)

- Catalogos con `Name` unico por tenant, `Description?`, `IsActive`, `SortOrder`:
  - `Warehouse` (Bodega, 000556): + `City` (obligatoria), `Address?`, `Phone?`.
  - `Brand` (Marca, 000502), `ItemGroup` (Grupo, 000506), `ItemType` (Tipo, 000498).
  - `ItemSubgroup` (Subgrupo, 000606): + `GroupId` (FK a `ItemGroup`, NO ACTION).
  - Los cuatro catalogos simples implementan `ICatalogEntity` para CRUD generico sin duplicar.
- `Item` (000066): `Sku` (80, unico por tenant si no vacio, indice filtrado), `Name` (200),
  `Description?`, `Specifications?` (text/nvarchar(max)), `Price` (decimal 14,2),
  `BrandId/GroupId/SubgroupId/ItemTypeId` (Guid?, FKs NO ACTION), `IsActive`,
  `FieldValuesJson?` (jsonb/nvarchar(max) dual, campos configurables portados del backbone).
- `ItemImage` (FK cascade al item, `Url` 500, `FileName?`, `SortOrder`).
- `ItemStock` (FK cascade al item, FK NO ACTION a bodega, `Stock` int; unico `(ItemId,
  WarehouseId)`; indice `(TenantId, WarehouseId)`).

Todas las FKs de catalogo son NO ACTION (`DeleteBehavior.Restrict`) para evitar rutas
multiples de cascada en SQL Server (error 1785): borrar/archivar un catalogo nunca arrastra
items ni existencias. Las imagenes y el stock viven y mueren con el item (cascade), y como la
FK de bodega en `ItemStock` es Restrict no hay doble ruta hacia esa tabla.

Una migracion dual `AddInventory` (Ecorex.Infrastructure + Ecorex.Infrastructure.SqlServer),
aplicada y verificada en los contenedores dev (PG 5442, SQL Server 1443).

### Servicios (Ecorex.Application/Inventory, resultados tipados)

`InventoryResult<T>` (Ok/NotFound/Invalid/Conflict), mismo patron que `OrgResult<T>`.

- `IInventoryCatalogService`: CRUD de bodegas y de los catalogos genericos (parametrizado por
  `CatalogKind`), listado con `includeInactive`, y archivar (IsActive=false) con guard: no se
  archiva una bodega con existencias, ni un catalogo con items activos, ni un grupo con
  subgrupos activos. El subgrupo valida su grupo.
- `IItemService`: CRUD del item (SKU unico con mensaje claro; consecutivo opcional con
  `ISequenceService`, prefijo "ITM"), imagenes por URL (add/remove reusando el patron de
  otros modulos que guardan URLs, con SortOrder = MAX+1), stock por bodega
  (`SaveItemRequest.StockByWarehouse`, recreado en transaccion; cantidad <= 0 no crea fila),
  `ListAsync` con filtros bodega/marca/grupo/tipo + texto + paginado (el filtro por bodega
  solo trae items con stock > 0), y `GetDetailAsync` con `TotalStock` y `AvailableAt`.
- Calculos puros en `InventoryCalculations` (total, disponibilidad, validacion de nombre),
  probados de forma unitaria.

### UI (Ecorex.SuperAdmin)

- `/inventario-items` reemplaza el stub Items 000066: grid (miniatura, nombre, sku,
  marca/grupo, precio, stock total y por bodega en chips), filtros bodega/marca/grupo/tipo +
  busqueda, modal crear/editar (datos + selects de catalogo + stock por bodega + imagenes por
  URL en modo edicion), activar/archivar.
- Catalogos: pagina generica `CatalogManager.razor` parametrizada por `CatalogKind` (marcas,
  grupos, subgrupos con select de grupo, tipos) + `/inventario-bodegas` aparte por sus campos
  propios (ciudad/direccion/telefono).
- NavMenu: los 6 items del grupo "Sistema - Inventarios" apuntan a las rutas reales
  (`/inventario-*`); el item 000066 se movio del grupo "Oferta - Catalogo" (que se retira) al
  grupo de inventarios; se retiraron sus 6 entradas del stub `Modulo.razor`.
- Policy `Inventario.Ver` en Program.cs (patron paso 1: mismo requisito que TenantMember).
- Tokens del workspace, claro/oscuro (verificado en navegador).

### Seeder (Development, idempotente)

`EnsureInventoryDemoAsync` en `DatabaseSeeder`, llamado desde SuperAdmin Program.cs: 2
bodegas, 3 marcas, 2 grupos con 2 subgrupos c/u, 3 tipos y 8 items con stock repartido
(algunos en 0 para probar el filtro de disponibles) e imagenes placeholder. Avanza el
consecutivo "ITM" a items+1 para que un SKU generado desde la UI no colisione con los SKUs
demo (ITM000001..ITM000008). Idempotente por tabla vacia (guard por tenant en cada bloque).

## Consecuencias

- El modulo de inventarios queda operativo con catalogos normalizados y stock por bodega,
  reusando los patrones existentes (resultados tipados, ISequenceService, filtro global,
  DAL dual). Ningun dato belleza portado.
- Cobertura: unit (calculos + validaciones), integracion dual (round-trip item con stock,
  SKU unico, aislamiento cross-tenant items+catalogos, subgrupo valida grupo, guards de
  archivado) y un E2E (crear bodega+marca+item con stock y verlo en el grid filtrando por
  bodega).
- Las policies por catalogo siguen el "paso 1": el paso 2 (derivar del Module Registry) queda
  pendiente, igual que el resto de modulos.
