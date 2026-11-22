# ADR-0200: Cerebros como modulo propio (categorias, vinculo N:M con maquetas, siembra del Super Admin)

Fecha: 2026-11-22
Estado: Aceptada (ola 1 construida; olas 2-4 pendientes)
Version: la asigna el dev principal al integrar (rama feat/maquetas-cerebro)
Relacionado: ADR-0183 (cerebro digital de la maqueta), ADR-0182 (MCP de maquetas), ADR-0062 (plantillas globales
+ activacion por tenant), ADR-0013 (concurrencia optimista portable)

## Contexto

El cerebro digital (ADR-0183) nacio "uno por proyecto de maqueta" y funciono bien (VISAL: 308 notas importadas de
Obsidian). El usuario quiere usar los cerebros para MAS cosas: bases de conocimiento, procesos, clientes, sin que
existan maquetas. Pidio (2026-11-22):

- Separar los cerebros del modulo de maquetas: un modulo "Cerebros" con las tarjetas de TODOS los cerebros.
- Seguir viendolos en las maquetas: desde una maqueta se vinculan uno o varios cerebros, como tarjetas que se abren
  en un modal grande.
- Categorias para los cerebros.
- El Super Admin siembra cerebros a los tenants; los sembrados pueden quedar bloqueados (solo el Super Admin los
  administra).

Decisiones del usuario sobre la propuesta (todas las recomendadas):

1. Todos en el tenant ven todos los cerebros; solo Owner/Admin crea, edita, archiva y vincula.
2. Un cerebro sembrado bloqueado es SOLO LECTURA total, con "Duplicar como propio".
3. Los bloqueados se sincronizan cuando el Super Admin publica una version nueva; los libres nunca.
4. Categorias: lista administrable por el tenant + categorias sembradas por el Super Admin.

## Decision

Se construye en 4 olas, cada una con su version:

| Ola | Alcance |
|---|---|
| 1 | Cerebro como agregado propio, categorias, migracion de datos y modulo Cerebros (galeria + pagina) |
| 2 | Vinculo desde la maqueta: tarjetas de cerebros vinculados, modal grande y "Vincular cerebro" |
| 3 | Plantillas del Super Admin: siembra a todos / por plan / tenants elegidos, bloqueo y sincronizacion |
| 4 | MCP por cerebro (`listar_cerebros`, parametro `cerebro`), sin romper `proyecto` |

### Ola 1 (esta entrega)

1. **Modelo** (migracion dual `AddBrainsModule`, PostgreSQL + SQL Server):
   - `Brain : TenantEntity, ISoftDeletable, IVersioned`: `Slug` (40, unico por tenant entre los activos, indice
     filtrado), `Name` (150), `Description` (600), `CategoryId?`, `CreatedByName`.
   - `BrainCategory : TenantEntity, ISoftDeletable`: `Name` (80, unico por tenant entre las activas), `Color`
     (#RRGGBB), `SortOrder`.
   - `MockupProjectBrain : TenantEntity` (fila de enlace N:M): `MockupProjectId`, `BrainId`, `SortOrder`. El de menor
     orden (entre los cerebros activos) es el PRINCIPAL de la maqueta. Desvincular borra la fila (no es un agregado).
   - `MockupNote` y `MockupNoteLink` cambian `MockupProjectId` por `BrainId` (se renombra la columna; la FK pasa de
     `mockup_projects` a `brains`). Los nombres de clase/tabla de las notas se conservan para no tocar mas codigo.
2. **Migracion de datos** (en la misma migracion, SQL por proveedor): cada maqueta que ya tenia notas recibe
   "Cerebro de {nombre}" con el **MISMO Id que el proyecto**, en la categoria "Maquetas" (una por tenant) y vinculado
   como principal. Las notas ya apuntan a ese Id al renombrar la columna, y los adjuntos del gestor de archivos usan
   el Id como llave del duenio (`Ref1`), asi que **no se mueve ningun binario**. Maqueta archivada -> cerebro
   archivado. Maqueta sin notas -> sin cerebro (se crea desde su pestana).
3. **Servicios**: `IMockupBrainService` (notas, grafo, adjuntos, vault, papelera) resuelve por **slug del cerebro**.
   `IBrainService` nuevo: galeria (con notas y maquetas vinculadas), crear/editar (concurrencia optimista)/archivar,
   categorias (archivar una deja sus cerebros sin categoria, en la misma transaccion), principal de una maqueta,
   crear el cerebro de una maqueta, vincular/desvincular. Todo auditado (`cerebro.*`).
4. **UI**: menu "Cerebros" debajo de "Maquetas" (backfill idempotente `EnsureCerebrosMenuItemAsync`).
   `/cerebros` = galeria de tarjetas con chips por categoria, busqueda, alta/edicion y administracion de categorias.
   `/cerebros/{slug}` = el mismo editor del cerebro (componente `MaquetaCerebro`) a pantalla completa; un
   `[[pantalla.html]]` abre esa pantalla en la maqueta principal vinculada. La pestana Cerebro de la maqueta abre su
   cerebro PRINCIPAL (con enlace "abrir en Cerebros"); si no tiene, Owner/Admin lo crea con un clic.
5. **MCP (compatibilidad)**: las herramientas de notas siguen recibiendo `proyecto`; se resuelve al cerebro principal
   de esa maqueta (o, si no hay maqueta con ese slug, a un cerebro con ese slug). Sin cerebro vinculado, el error lo
   explica.

## Consecuencias

- Un cerebro vive solo o vinculado a varias maquetas; las notas ya no dependen del proyecto.
- Las maquetas nuevas no traen cerebro automatico: se crea o (ola 2) se vincula uno existente.
- El `Down` de la migracion solo es fiel mientras los cerebros sean los migrados (Id = proyecto); con cerebros
  nuevos con notas, volver atras falla en la FK (aceptado: no se planea revertir en prod).
- RLS: igual que las tablas del cerebro de ADR-0183, la defensa es el filtro global por tenant + soft-delete.
- Prueba: `BrainsModuleTests` (dual) baja la migracion, siembra una maqueta con notas y enlaces en el esquema viejo y
  la vuelve a subir; ademas catalogo, vinculos y aislamiento entre tenants.
