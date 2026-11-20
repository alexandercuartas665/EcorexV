# ADR-0179: Maquetas en la nube (proyectos + versiones por ZIP, servidas en /maqueta/{slug}/)

Fecha: 2026-11-17
Estado: Aceptada
Version: v0.16.342
Relacionado: ADR-0176 (POST /api/maqueta/solicitudes), ADR-0178 (hub local de maquetas)

## Contexto

El hub de ADR-0178 sirve maquetas desde el disco del desarrollador (solo Development). El usuario necesita lo mismo
EN LA NUBE: subir varias maquetas, anotarlas en produccion y, despues (ola 2), que su PC local tome las solicitudes
por MCP, aplique los cambios en la maqueta y publique una version nueva.

## Decision

1. **Modelo**: `MockupProject` (Slug unico por tenant, Name, Description, CurrentVersionId, LastVersionNumber,
   soft-delete con DeletedAt) y `MockupVersion` (INMUTABLE: Number correlativo, ZipFileId en el gestor unificado de
   archivos modulo `maqueta-sitio`, SizeBytes, FileCount, RootPrefix, EntryPath, HasAnotador, Notes, UploadedByName).
   Migracion dual aditiva `AddMockupProjects` (solo crea las 2 tablas).
2. **Subida**: el ZIP de la carpeta de la maqueta. `MockupService.InspectZip` valida ANTES de guardar: tope 100 MB
   comprimido / 400 MB descomprimido / 10.000 entradas, sin rutas absolutas ni `..`, ignora ocultos (.git, .env,
   __MACOSX), exige una pagina .html; si todo cuelga de una carpeta raiz unica, se quita. Entrada = `anotador/anotador.html`
   si viene el kit, si no `index.html` (o la primera .html). Cada subida = version nueva y queda como ACTUAL. Canales:
   UI `/maqueta` (Blazor InputFile) y `POST /api/maqueta/proyectos/{slug}/versiones` (Bearer `integ:maqueta`; cuerpo ZIP
   o multipart; `?notas=`) para el script/Claude del PC local.
3. **Permisos**: crear proyecto, subir version, cambiar la version actual y archivar = Owner/Admin del tenant. Ver y
   anotar = cualquier usuario del tenant (policy `MaquetaHub.Usar`).
4. **Servir en el MISMO dominio (decision del usuario)**: `/maqueta/{slug}/...` (version actual) y
   `/maqueta/{slug}~{n}/...` (version n) detras del login de tenant. La version se descomprime UNA vez a una cache en
   disco desechable (`MaquetaNube:CacheDir`, por defecto temp; se rehace tras reiniciar) y se sirve con un
   StaticFileMiddleware por version (PhysicalFileProvider con ExclusionFilters.Sensitive, solo tipos conocidos, sin
   listado, nosniff, no-cache). La resolucion corre bajo el filtro global con el tenant de la cookie: otro tenant con el
   mismo slug no ve la maqueta. Las maquetas de disco local (ADR-0178) siguen solo en Development y tienen prioridad
   sobre un slug igual.
5. **Envio de tarjetas por sesion en produccion**: `POST /api/maqueta/solicitudes` acepta la cookie de sesion sin
   Bearer en cualquier entorno (ADR-0178 lo limitaba al hub de Development), con el mismo anti-CSRF (JSON + mismo
   origen). El anotador de una maqueta en la nube postea al mismo origen sin token.
6. **Auditoria** (`maqueta-nube`): `maqueta.proyecto.create`, `maqueta.version.upload`, `maqueta.version.set-current`,
   `maqueta.proyecto.archive`.

## Riesgo aceptado (decision del usuario)

Servir HTML/JS subido en el mismo origen de la app implica que ese JS corre con la sesion de quien abre la maqueta y
podria llamar APIs de ECOREX como ese usuario (XSS almacenado). Se recomendo un subdominio aparte
(maquetas.bitcode.com.co, con token efimero del launcher); el usuario eligio el mismo dominio por rapidez. Mitigacion:
solo Owner/Admin suben, toda subida queda auditada con autor, versiones inmutables, y se sirve solo dentro del tenant.
Si se abren maquetas a terceros o se suben maquetas no confiables, migrar a subdominio (cambio acotado al middleware y
al launcher).

## Consecuencias

- Ola 2 (pendiente): MCP de maquetas para el PC local (listar solicitudes con captura/selector/archivo/handoff,
  marcar en progreso/resuelta, publicar version via el endpoint de subida).
- La cache crece con cada version abierta (pocos MB por version); se limpia al reiniciar el contenedor.
- No hay item de menu: se entra por /maqueta (agregarlo en Configuracion de menu si se quiere).
- Pruebas: `MockupZipInspectionTests` (unitarias), `MockupProjectsTests` (dual PG/SQL Server: permisos, versiones,
  version actual, archivado, aislamiento por tenant), `MaquetaHubTests` (servir desde cache por version, sin ocultos,
  anonimo -> login, parseo de rutas).
