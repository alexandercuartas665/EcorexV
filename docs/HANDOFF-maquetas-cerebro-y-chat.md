# HANDOFF - Maquetas: "Cerebro digital" del proyecto (ola 3) y chat con la maqueta (ola 4)

Fecha: 2026-11-18. Autor: sesion dev principal (ECOREX.desarrollo). Destino: una sesion NUEVA de Claude Code.
Tronco al entregar: `fase-0/clon-backbone` = `main` = el commit que agrega este archivo (padre `69db21ce`, v0.16.345).

---

## 0. Arranque (leer primero)

| Que | Valor |
|---|---|
| Worktree | `C:\DesarrolloIA\ECOREX.tareas\.claude\worktrees\maquetas-cerebro` (ya creado) |
| Rama | `feat/maquetas-cerebro` (ya creada y pusheada a origin, nace del tronco) |
| Puerto de la app | **5285** (cookie suffix `5285`). NO usar 5234, 5263, 5265, 5272, 5275 ni otros de `.claude/launch.json` |
| Preview | `.claude/launch.json` DEL WORKTREE ya trae `maquetas-cerebro-5285` (gitignored, no se commitea) |
| Login local | abrir `http://localhost:5285/dev/login` (entra como acuartas@bitcode.com.co, tenant BITCODE, Owner) |
| BD local | PostgreSQL `localhost:5442`, BD `ecorex` (COMPARTIDA con otras sesiones; es copia de prod) |

Reglas de convivencia (otras sesiones trabajan en paralelo en la misma maquina):

1. Trabaja SOLO en tu worktree y tu rama. NO cambies de rama el checkout principal (`C:\DesarrolloIA\ECOREX.tareas`,
   esta en otra rama de otra sesion) ni toques otros worktrees.
2. NO hagas push a `fase-0/clon-backbone` ni a `main`, y NO despliegues. Al terminar: push de TU rama y deja un
   prompt para el dev principal (el integra, renumera version/ADR y despliega con OK del usuario).
3. Nunca `git add -A`: agrega rutas explicitas. Nunca force-push. No uses `git stash` sin etiqueta.
4. Migraciones en la BD compartida: SOLO aditivas (crear tablas/columnas nullable). Nada de borrar/renombrar.
5. No pares apps de otras sesiones. Si un puerto esta ocupado, usa 5286.
6. Respuestas al usuario SIEMPRE en espanol. Archivos nuevos SOLO ASCII (sin tildes/enie; escapes `\u00f1` si hace
   falta). Commits terminan con `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
7. Lee `CLAUDE.md` (raiz) antes de codificar: multi-tenant real (HasQueryFilter + ITenantScoped), DAL dual (PG + SQL
   Server, migracion en ambos), transacciones multi-tabla, soft-delete, concurrencia optimista, fechas UTC.
8. NO modifiques la carpeta de la maqueta del usuario `D:\DesarrolloIA\tronox.maqueta` (solo lectura).

---

## 1. Lo que YA existe (no rehacer)

ADRs: `docs/decisiones/ADR-0176` (solicitudes -> tarjetas), `ADR-0178` (hub local), `ADR-0179` (maquetas en la nube),
`ADR-0180` (editor del sistema + galeria + HTML puro), `ADR-0182` (MCP de maquetas). Guia de uso:
`docs/integraciones/maqueta-solicitudes.md` (secciones 4-7). En prod esta hasta v0.16.344; v0.16.345 (MCP) aun sin deploy.

### Modelo
- `MockupProject` (`Ecorex.Domain/Entities/MockupProject.cs`): Slug unico por tenant, Name, Description, BoardName,
  CurrentVersionId, LastVersionNumber, DeletedAt (soft-delete), Versions.
- `MockupVersion`: inmutable; ZipFileId en el gestor de archivos (modulo `maqueta-sitio`), FileCount, RootPrefix,
  EntryPath, HasAnotador, Notes, UploadedByName.
- Solicitudes = `TaskItem` + `ExternalTaskLink` con `Source = "maqueta-{slug}"` y `MetadataJson` (pantalla, ruta,
  archivoMaqueta, origenApp, handoff, elemento{tag,texto,clases,selector,rect}, tema, viewport, autor...).

### Servicios (Ecorex.Application/Integraciones/Maqueta)
- `MockupService` / `IMockupService`: ListProjects (con conteos de solicitudes), GetProject, Create/Update (Owner/
  Admin), UploadVersion, SetCurrentVersion, Archive, ListVersions, ListSolicitudes(slug), ResolveForServing,
  `InspectZip`, `SourceFor(slug)`, `DefaultBoardFor(name)`, `ReadMetadata(json)`.
- `MaquetaSolicitudService.RecibirAsync(request, actor)`: crea/actualiza la tarjeta (idempotente por origen+idLocal),
  adjunta la captura. `ResolveSessionActorAsync(platformUserId)` = actor desde la sesion.

### SuperAdmin (UI y HTTP)
- `/maqueta` -> `Components/Pages/MaquetaHubPage.razor`: galeria de tarjetas + panel Versiones (Owner/Admin).
- `/maqueta/{slug}` -> `Components/Pages/MaquetaEditor.razor` (+ `.razor.css`): EDITOR. Barra (pantalla, recargar,
  escritorio/movil, tema, Seleccionar), panel izquierdo (solicitudes + compositor), iframe a la derecha.
  `?v=n` = version especifica. AQUI va la pestana nueva "Cerebro".
- `/maqueta/{slug}/archivo.html` -> HTML puro servido por `Maqueta/MaquetaHub.cs` + `Maqueta/MockupSiteCache.cs`
  (descomprime cada version a cache; `GetSiteInfo` -> pantallas via `Maqueta/MockupPages.cs`).
- JS: `wwwroot/js/maqueta-editor.js` (puente Blazor<->iframe, `window.ecxMaquetaEditor`) y `wwwroot/js/maqueta-agente.js`
  (se inyecta en el iframe: seleccionar, capturar con `wwwroot/lib/html2canvas`, resaltar). Registrado en `App.razor`.
- Graficas: `wwwroot/lib/echarts/echarts.min.js` + `js/echart-interop.js` ya cargados en `App.razor` (ECharts trae
  serie `graph` con `layout: 'force'` -> sirve para el grafo del cerebro SIN npm).
- MCP: `Endpoints/McpMaquetasEndpoints.cs` (POST `/api/mcp/maquetas`) + `Ecorex.Application/Mcp/MaquetaMcpToolset.cs`
  sobre el host comun `Endpoints/McpJsonRpcHost.cs`. Token personal alcance `integ:maqueta` (Conectar IA).
- Patron Blazor del modulo: las operaciones corren en `RunAsync` = `AmbientTenantContext.Begin(tenant,user)` + scope
  nuevo (el circuito no tiene HttpContext). Copiar ese patron.

### Pruebas existentes
- `tests/Ecorex.Application.Tests/MockupZipInspectionTests.cs`, `tests/Ecorex.SuperAdmin.Tests/MockupPagesTests.cs`
- `tests/Ecorex.Integration.Tests/MockupProjectsTests.cs`, `MaquetaSolicitudesTests.cs` (dual; incluye MCP),
  `MaquetaHubTests.cs` (TestServer; tiene un `FakeMockups : IMockupService` -> si agregas metodos a la interfaz,
  implementalos ahi con `throw new NotSupportedException()`).

### Datos locales para probar (BD 5442, tenant BITCODE)
- Proyecto nube `tronox-sgdea` (v1 actual, v2), proyecto local `tronox` (carpeta D:, solo Development).
- Tarjeta de prueba T00046 (resuelta) en el tablero "Maqueta TRONOX SGDEA - Ajustes".

---

## 2. El pedido del usuario (textual, resumido)

> "Darle poder a esa caja de herramientas para que se pueda chatear un poco con la maqueta ... usar ese toque extra
> que nos da Claude ($100) que vengo perdiendo ... que este chat nos ayude a pensar que estamos haciendo dentro de la
> maqueta. ... En este panel donde abro la maqueta, un SEGUNDO TAB: el CEREBRO DIGITAL del proyecto, algo muy similar
> a Obsidian: escribir texto y enlazar un tramo de texto con otro, mostrando la imagen grafica del cerebro digital,
> la conexion de los nodos donde se conecta un texto con otro."

El usuario pidio PENSARLO antes de codificar. Propuesta ya presentada (aceptada en principio): ola 3 = Cerebro
(sin costo de IA), ola 4 = Chat (depende de como se pague la IA).

## 3. PREGUNTAS ABIERTAS (hacerlas al usuario ANTES de codificar, con AskUserQuestion)

1. **Los "$100"**: plan **Claude Max** (suscripcion personal de claude.ai / Claude Code) o **creditos de la API de
   Anthropic** (console.anthropic.com)? Importa porque:
   - Plan Max: NO da llave de API para el backend; la forma de aprovecharlo es el MCP (Claude Desktop/Code del usuario
     conectado a ECOREX conversa con la maqueta y el cerebro, cargado a su plan). Ola 4 dentro del sistema no aplica
     (o usaria otro proveedor del gateway).
   - Creditos API: se configura como proveedor en el AI Provider Gateway del tenant y el chat vive en el panel.
2. **Alcance del cerebro**: uno por proyecto de maqueta (recomendado; luego enlaces entre proyectos) o uno por empresa.
3. **Importar el vault de Obsidian** existente (ZIP de .md) desde el inicio, o arrancar vacio (recomendado: ola 3b).
4. (Menor) Quien escribe notas: cualquier usuario del tenant (recomendado, como anotar) y archivar solo autor/Admin.

---

## 4. Diseno propuesto - OLA 3: Cerebro digital

### 4.1 Modelo (migracion dual aditiva, ej. `AddMockupBrain`)
- `MockupNote : TenantEntity, IVersioned` (concurrencia optimista, regla 8):
  `MockupProjectId`, `Title` (max 200; unico por proyecto entre no archivadas, comparar sin mayusculas),
  `Body` (texto largo markdown), `UpdatedByName`, `DeletedAt` (soft-delete). Indice (TenantId, MockupProjectId, Title).
- `MockupNoteLink : TenantEntity` (tabla DERIVADA, se reescribe en la MISMA transaccion al guardar la nota):
  `FromNoteId`, `TargetKind` (`note` | `page` | `task` | `missing`), `TargetKey` (titulo normalizado / archivo .html /
  numero T00046), `TargetNoteId?`. Permite backlinks y el grafo con una consulta.
- Registrar DbSets en `IApplicationDbContext` con implementacion por defecto (`=> throw new NotSupportedException()`)
  para no romper fakes de pruebas (patron del repo). HasQueryFilter global llega por ITenantScoped.

### 4.2 Sintaxis (parser puro + pruebas unitarias)
- `[[Titulo de nota]]`, `[[Titulo|texto visible]]` -> nota (si no existe = nodo "fantasma"; clic la crea).
- `[[rq01-dependencias.html]]` (termina en .html y existe en la version actual) -> pantalla de la maqueta.
- `[[T00046]]` (patron `^T\d{5}$`) -> tarjeta/solicitud.
- Opcional: `#etiqueta`. Bloques de codigo no generan enlaces.

### 4.3 Servicio `IMockupBrainService` (Application/Integraciones/Maqueta)
`ListNotesAsync(slug, texto?)`, `GetNoteAsync(slug, title|id)` (con backlinks), `SaveNoteAsync(slug, id?, title, body,
version)` (crea/actualiza + reescribe links en transaccion; conflicto de version -> 409), `RenameNoteAsync` (opcional:
reescribe los `[[...]]` que apuntan a ella), `ArchiveNoteAsync`, `GetGraphAsync(slug)` -> nodos (nota/pantalla/
tarjeta/fantasma, con grado) + aristas. Todo bajo el filtro de tenant.

### 4.4 UI en `MaquetaEditor.razor`
- Pestanas en la barra superior: **Maqueta** (lo actual) | **Cerebro**. No recargar el iframe al cambiar de pestana
  (ocultar con CSS, no destruir).
- Cerebro, 3 zonas: lista/busqueda de notas + "+ Nota" | editor markdown (textarea con autocompletado al escribir
  `[[`; vista previa) | panel derecho con "Enlazan aqui" (backlinks) y grafo local.
- Boton "Grafo completo": ECharts `series.type='graph'`, `layout:'force'`, `roam:true`, `draggable:true`; colores por
  tipo (nota, pantalla, tarjeta, fantasma punteado); tamano por grado; clic: nota -> abrirla; pantalla -> volver a la
  pestana Maqueta en esa pantalla (`ecxMaquetaEditor.load(file)`); tarjeta -> `TaskDetailModal.OpenAsync`.
  Crear un JS propio `wwwroot/js/maqueta-cerebro.js` (registrar en App.razor) en vez de tocar `echart-interop.js`.
- Desde la pestana Maqueta: accion "Nota de esta pantalla" que crea/abre una nota con `[[pantalla.html]]`.
- Markdown -> HTML: revisar si el repo ya usa Markdig (buscar en los .csproj); si no, render minimo propio y SANITIZADO
  (nada de HTML crudo del usuario: riesgo XSS, y el sitio es el mismo origen de la app).
- Tokens visuales: los del workspace (`--surface`, `--ink`, `--line`, `--t-violet`, `--sh-*` de `wwwroot/app.css`),
  igual que `MaquetaEditor.razor.css`.

### 4.5 MCP (para que Claude del PC/Desktop piense con el cerebro)
Agregar a `MaquetaMcpToolset`: `listar_notas`, `ver_nota` (con backlinks), `buscar_notas`, `crear_nota`,
`editar_nota` (reemplazar o agregar al final, con version), `grafo_cerebro` (resumen de nodos/aristas). Marcar las de
lectura en `ReadOnly`. Escrituras quedan auditadas por el host (`mcp.maquetas.*`).

### 4.6 Pruebas
Unitarias del parser de enlaces; dual (PG + SQL Server) del servicio: crear/editar/backlinks/fantasmas/renombrar/
archivar/aislamiento por tenant/conflicto de version; MCP de notas en `MaquetaSolicitudesTests` o archivo nuevo dual.
E2E en 5285 con el preview y screenshots.

---

## 5. Diseno propuesto - OLA 4: Chat con la maqueta (segun respuesta a la pregunta 1)

- **Si hay creditos API**: chat en el panel (tercera pestana o drawer en la pestana Maqueta) usando el AI Provider
  Gateway existente (cupos por plan, `AiUsageLog` OBLIGATORIO por CLAUDE.md). Antes, estudiar como lo hacen los chats
  ya construidos (ej. constructor de formularios por chat, agente editor de plantillas de correo) y reutilizar.
  Contexto: pantalla actual (texto de la pagina desde la cache de la version, recortado), elemento seleccionado,
  notas relevantes del cerebro, solicitudes abiertas. Herramientas del chat: buscar_notas, crear_nota (guardar
  conclusion), crear_solicitud (via `MaquetaSolicitudService`), listar_solicitudes. NO edita la maqueta (versiones
  inmutables; los cambios se aplican en el PC via MCP).
- **Si es plan Max**: no hay chat de IA dentro del sistema con ese plan. Documentar en Conectar IA como conectar
  Claude Desktop / Claude Code al MCP de maquetas (ya existe) para "chatear con la maqueta y el cerebro".

---

## 6. Comandos utiles

```powershell
# Build (desde apps/backend del worktree)
dotnet build Ecorex.sln
# Migracion dual
dotnet ef migrations add AddMockupBrain --project src/Ecorex.Infrastructure --startup-project src/Ecorex.Infrastructure --context EcorexDbContext
dotnet ef migrations add AddMockupBrain --project src/Ecorex.Infrastructure.SqlServer --startup-project src/Ecorex.Infrastructure.SqlServer --context SqlServerEcorexDbContext
dotnet ef migrations has-pending-model-changes --project src/Ecorex.Infrastructure --startup-project src/Ecorex.Infrastructure --context EcorexDbContext
# Pruebas
dotnet test tests/Ecorex.Application.Tests
dotnet test tests/Ecorex.Integration.Tests --filter "FullyQualifiedName~Mockup|FullyQualifiedName~Maqueta"   # dual, ~10 min (Docker)
# Gate de formato del CI
dotnet format whitespace Ecorex.sln --verify-no-changes --no-restore
dotnet format style Ecorex.sln --verify-no-changes --no-restore
```

Notas practicas:
- El preview usa `--no-build`: compila antes de `preview_start`; deten el preview antes de compilar (bloquea el bin).
- Los archivos del repo estan en CRLF; ediciones con sed/Python pueden dejar fines de linea mezclados y el gate de
  whitespace falla: normaliza a CRLF los archivos tocados.
- La app aplica las migraciones al arrancar.
- La suite completa de integracion tarda ~40 min; fallos conocidos ajenos: timeouts de arranque del contenedor SQL
  Server (pasan aislados) y `ChatIngestTests.Webhook_IngestsMessageIdempotently_AndAdvisorCanRead` (ya fallaba).

## 7. Entrega esperada

1. Respuestas a las preguntas de la seccion 3.
2. Ola 3 completa en `feat/maquetas-cerebro`: ADR nuevo (usar **ADR-0183**; 0181 lo tiene feat/agentes-sistema y 0182
   el MCP), entrada en `PROGRESO.md`, `docs/integraciones/maqueta-solicitudes.md` (seccion 8), pruebas en verde.
   AppVersion: subir a **0.16.346** en la rama (el dev principal renumera si choca).
3. Push de la rama y un mensaje final con: commits, migraciones, pruebas corridas, como probar, y el prompt para el
   dev principal ("Une feat/maquetas-cerebro hasta <sha>, vX, ADR-0183; trae migracion dual aditiva ...").
4. Ola 4 solo despues de la respuesta 1 y con OK del usuario.
