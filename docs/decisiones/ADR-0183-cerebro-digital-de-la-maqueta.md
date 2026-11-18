# ADR-0183: Cerebro digital del proyecto de maqueta (notas enlazadas tipo Obsidian)

Fecha: 2026-11-18
Estado: Aceptada
Version: v0.16.346 (rama feat/maquetas-cerebro; el dev principal renumera si choca)
Relacionado: ADR-0179 (maquetas en la nube), ADR-0180 (editor de maquetas), ADR-0182 (MCP de maquetas),
ADR-0013 (concurrencia optimista portable)

## Contexto

El usuario pidio, dentro del editor de maquetas, una SEGUNDA pestana: el "cerebro digital" del proyecto, "algo muy
similar a Obsidian": escribir texto, enlazar un tramo con otro y ver el grafo de nodos conectados. Tambien queria
aprovechar su plan de Claude para "pensar que estamos haciendo dentro de la maqueta".

Respuestas del usuario (2026-11-18) a las preguntas del hand-off:

1. Los "$100" son el **plan Claude Max** (suscripcion personal), no creditos de API: no hay llave para el backend.
   La IA conversa con el cerebro desde Claude Desktop/Code via el MCP de maquetas (ADR-0182), cargado a su plan.
   No se construye chat de IA dentro del sistema (la ola 4 queda como documentacion de conexion, con OK aparte).
2. **Un cerebro por proyecto de maqueta** (enlaces entre proyectos, despues).
3. **Importar el vault de Obsidian desde el inicio** (ZIP de .md).
4. **Cualquier usuario del tenant escribe**; archiva el autor o un Owner/Admin.

## Decision

1. **Modelo** (migracion dual aditiva `AddMockupBrain`, PostgreSQL + SQL Server):
   - `MockupNote : TenantEntity, ISoftDeletable, IVersioned`: proyecto, `Title` (200), `TitleKey` (titulo
     normalizado: minusculas, espacios colapsados, sin .md), `Body` (markdown, texto largo), autor
     (`AuthorTenantUserId`, `AuthorName`), `UpdatedByName`, `ImportedFrom` (ruta del .md). Indice UNICO FILTRADO
     `(tenant, proyecto, title_key)` entre las notas no archivadas: el titulo de una archivada vuelve a quedar libre.
   - `MockupNoteLink : TenantEntity` (tabla DERIVADA): `FromNoteId`, `TargetKind` (note | page | task | tag),
     `TargetKey`, `Label`. Se reescribe en el MISMO SaveChanges que guarda la nota. Los enlaces a notas se
     resuelven por `TargetKey == TitleKey` en la consulta, asi un enlace a una nota inexistente ("fantasma") queda
     resuelto en cuanto alguien la crea, sin recalcular nada.
   - Filtro global por tenant + soft-delete via `ITenantScoped`/`ISoftDeletable`; las consultas de enlaces hacen
     join con la nota origen para excluir archivadas.
2. **Sintaxis** (`MockupBrainLinks`, parser puro): `[[Titulo]]`, `[[Titulo|alias]]`, `[[Titulo#Seccion]]`,
   `[[carpeta/Titulo]]` -> nota; `[[pantalla.html]]` -> pantalla de la maqueta; `[[T00046]]` -> solicitud
   (tarjeta); `#etiqueta` (no numerica) -> etiqueta. Se ignoran los bloques/tramos de codigo y los adjuntos de
   Obsidian (`![[img.png]]`, `[[doc.pdf]]`). Titulos invalidos: con `[ ] | # ^`, con forma `T00000` o terminados en
   `.html` (se confundirian con otros tipos).
3. **Servicio** `IMockupBrainService`: listar/buscar (titulo y contenido), ver (enlaces resueltos + backlinks),
   guardar (crear/actualizar con version esperada -> 409 si otro cambio; renombrar reescribe los `[[...]]` de las
   demas notas en la misma transaccion), archivar (soft-delete; solo autor u Owner/Admin; borra sus enlaces
   salientes derivados y los entrantes pasan a fantasma), grafo (nodos nota/pantalla/tarjeta/etiqueta/pendiente
   con grado + aristas) e importar vault. Auditoria `maqueta.nota.*` con reason `maqueta-cerebro`.
4. **Importar vault de Obsidian**: ZIP (max 30 MB, 3.000 notas). Cada `.md` es una nota cuyo titulo es el nombre
   del archivo (asi enlaza Obsidian). Se ignoran `.obsidian/`, ocultos, `__MACOSX` y adjuntos (se informan). Nombres
   repetidos en carpetas distintas -> "Nombre (carpeta)"; nombres con caracteres reservados se limpian. Las notas
   con titulo existente se omiten salvo "sobrescribir". Todo el vault entra en UN SaveChanges (todo o nada).
   Los adjuntos (imagenes) quedan para una ola 3b.
5. **UI**: pestanas **Maqueta | Cerebro** en la barra del editor (`/maqueta/{slug}`, `?tab=cerebro&nota=...`). El
   cerebro se monta una vez y se oculta con CSS (no recarga el iframe ni pierde el borrador). Tres zonas: lista con
   busqueda / editor markdown con autocompletado al escribir `[[` (notas, pantallas, solicitudes) y vista previa /
   panel con "Enlazan aqui", enlaces salientes, etiquetas y grafo local. "Grafo completo" en modal con ECharts
   (`series.type='graph'`, `layout:'force'`, roam, arrastrar, filtros por tipo, tamano por grado). Clic: nota ->
   abrirla; pantalla -> pestana Maqueta en esa pantalla; tarjeta -> `TaskDetailModal`; fantasma -> crearla. Desde la
   pestana Maqueta, boton "Nota de esta pantalla". JS propio `wwwroot/js/maqueta-cerebro.js` (sin npm).
6. **Markdown -> HTML propio y SANITIZADO** (`MockupMarkdown`): el repo no usa Markdig y el editor vive en el mismo
   origen de la app, asi que TODO texto se codifica y solo se emiten etiquetas fijas (encabezados, listas, tareas,
   citas/callouts, codigo, enfasis, enlaces http/https con `rel=noopener noreferrer nofollow`). Sin HTML crudo.
7. **MCP** (ADR-0182, mismo token `integ:maqueta`): `listar_notas`, `buscar_notas`, `ver_nota`, `grafo_cerebro`
   (lectura) y `crear_nota`, `editar_nota` (agregar al final o reemplazar con `version`; `nuevo_titulo` renombra).
   Las escrituras quedan a nombre del usuario "(via IA)" y auditadas por el host (`mcp.maquetas.*`). Esta es la via
   para usar el plan Claude Max: Claude Desktop/Code conectado al MCP piensa con el cerebro y guarda conclusiones.

## Consecuencias

- El cerebro solo existe para maquetas de la nube (las locales de disco, solo Development, no tienen proyecto en BD).
- Las pantallas enlazadas se validan contra la version abierta en la UI (el servicio no conoce la cache del sitio).
- Topes: 5.000 notas por proyecto, 200.000 caracteres por nota, 500 enlaces por nota.
- Ola 4 (chat dentro del sistema) no aplica con plan Max; queda documentar en Conectar IA como chatear con la
  maqueta y el cerebro desde Claude Desktop/Code (con OK del usuario).
