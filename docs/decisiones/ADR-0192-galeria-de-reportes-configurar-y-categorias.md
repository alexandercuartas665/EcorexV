# ADR-0192: Galeria de reportes con boton Configurar, tuerca por reporte y categorias

- Estado: Aceptado
- Fecha: 2026-11-20
- Modulo: Reportes / Galeria (`/reportes/galeria`, ADR-0051)

## Contexto

La galeria mostraba arriba tres botones sueltos (Nuevo panel, Importar RDL externo, Generar reportes de
ejemplo) y cada tarjeta tenia su propio boton "Archivar" (o "Activar" en Archivados); el visor tambien
tenia "Archivar" en su menu. No habia forma de renombrar un reporte que no fuera panel por spec, ni de
agruparlos. Con mas de unos pocos reportes la galeria se volvia una lista plana dificil de recorrer.

El usuario pidio: un solo boton "Configurar" con las opciones actuales; tarjetas mas bonitas; poder
crear categorias y categorizar cada reporte; editar el nombre; una tuerca sobre la tarjeta con su propio
modal, y que archivar salga de la tarjeta y del visor y viva en esa configuracion.

## Decision

1. **Boton unico "Configurar"** (solo con permiso de crear reportes) que abre un modal con las tres
   opciones de siempre como tarjetas de opcion. Los flujos que abren (editor de PanelSpec, importador
   RDL, ejemplos) no cambian.
2. **Tuerca por tarjeta** (mismo permiso) con el modal "Configurar reporte": nombre, categoria,
   descripcion, tipo/fuente (solo lectura) y la zona de archivar / reactivar. Archivar conserva la doble
   confirmacion existente. Se quita "Archivar" de la tarjeta y del menu del visor; "Activar" de la
   tarjeta archivada pasa a "Reactivar" en la tuerca.
3. **Categoria = columna `category` (varchar/nvarchar 80, nullable) en `report_definitions`**, texto
   libre del tenant. Una categoria existe mientras algun reporte la use; se "crea" escribiendola en la
   tuerca (con autocompletado y chips de las existentes). Al guardar se normalizan espacios y, si ya
   existe la misma categoria con otras mayusculas, se reutiliza la escritura existente.
4. **Galeria agrupada por categoria** (alfabetica, "Sin categoria" al final), con chips que filtran y un
   buscador por nombre/descripcion/categoria. Tarjetas nuevas: portada con degradado e icono por tipo
   (Tablero / Panel / Imprimible), descripcion en dos lineas, tuerca al pasar el mouse (siempre visible
   en pantallas tactiles).
5. **Servicio**: `IReportDefinitionService.UpdateCardAsync(id, name, category, description)` y
   `ListCategoriesAsync()`. `UpdateCardAsync` no toca el spec ni el RDL, salvo en un panel por spec,
   donde sincroniza el `title` del JSON para que el visor muestre el nombre nuevo. Tenant-scoped por el
   filtro global; `ReportDefinitionSummary` gana `Category` y `Description` (parametros opcionales).

## Opciones consideradas

- **Entidad `ReportCategory` (catalogo con color/orden)**: mas control (colores, orden manual, categorias
  vacias), pero exige CRUD propio, FK y otra pantalla. Se descarta por ahora; la columna libre cubre lo
  pedido y se puede migrar a catalogo despues (los valores distintos de la columna serian la semilla).
- **Guardar la categoria en `TenantConfiguration` (sin migracion)**: evita migracion pero separa el dato
  del reporte y complica listar/filtrar. Se descarta.

## Consecuencias

- Migracion dual `AddReportCategory` (PostgreSQL y SQL Server): una columna nullable, sin backfill.
- Los reportes existentes quedan en "Sin categoria" hasta que alguien les asigne una.
- Prueba de integracion dual `ReportCardConfigTests` (renombrar, categorizar, sincronizar titulo del
  panel, reutilizar escritura, aislamiento cross-tenant, nombre vacio).
