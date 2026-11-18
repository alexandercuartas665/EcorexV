# ADR-0180: Editor de maquetas en el sistema, maqueta en HTML puro y galeria de proyectos

Fecha: 2026-11-18
Estado: Aceptada
Version: v0.16.343
Relacionado: ADR-0176 (solicitudes -> tarjetas), ADR-0178 (hub local), ADR-0179 (maquetas en la nube)

## Contexto

En v0.16.342 (ADR-0179) las maquetas se suben por ZIP y se sirven en /maqueta/{slug}/, pero la herramienta de
anotacion (el "anotador" tipo Lovable) viajaba DENTRO del ZIP (kit `anotador/` de la maqueta) y la pantalla /maqueta
era un selector con un iframe. El usuario pidio:

1. Que la herramienta tipo Lovable viva EN EL SISTEMA y que la maqueta sea solo el HTML puro, separado del editor.
2. Que cada proyecto se vea como una tarjeta (decision: galeria de tarjetas en /maqueta; NO una tarea por proyecto).
3. Que la carga se pueda hacer desde el tenant cliente (decision: entrada de menu para todos los tenants; siguen
   subiendo solo Owner/Admin y todos ven y anotan).

## Decision

1. **Editor del sistema** (`/maqueta/{slug}`, Blazor `MaquetaEditor`): barra con pantalla, recargar, escritorio/movil,
   tema y Seleccionar; panel izquierdo con las solicitudes del proyecto (abiertas / de esta pantalla / todas) y el
   compositor; la maqueta a la derecha en un iframe del MISMO origen. El editor inyecta su propio agente
   (`wwwroot/js/maqueta-agente.js`) en cada pagina que carga el iframe: selector de elementos, captura con html2canvas
   (vendorizado en `wwwroot/lib/html2canvas`, MIT) y resaltado de un selector. Nada de esto vive en el ZIP.
2. **Solicitudes**: el editor llama `MaquetaSolicitudService.RecibirAsync` en el servidor con el actor de la sesion
   (`ResolveSessionActorAsync`), origen `maqueta-{slug}`, tablero del proyecto. Mismo contrato, idempotencia y
   auditoria de ADR-0176/0178 (canal "hub"). `IMockupService.ListSolicitudesAsync` lista las del proyecto (join con
   tareas bajo el filtro global) con pantalla/archivo/selector del metadata; "Ver en la maqueta" abre la pantalla y
   resalta el elemento; "Abrir tarjeta" usa el `TaskDetailModal` del sistema.
3. **Tablero por proyecto**: nueva columna `MockupProject.BoardName` (migracion dual aditiva `AddMockupProjectBoard`).
   Por defecto "Maqueta {Nombre} - Ajustes"; Owner/Admin lo cambia (`UpdateProjectAsync`, auditado
   `maqueta.proyecto.update`). El tablero se crea con la primera solicitud si no existe (ADR-0176).
4. **Maqueta en HTML puro**: `InspectZip` toma `index.html` como entrada (o la primera pagina fuera de `anotador/`);
   el kit heredado, si viene, se conserva pero ya no es la entrada. Las pantallas del editor salen de los `.html` de
   la version (`MockupPages.Scan`: titulo de `maq-titulo`/`<title>`, modulo de `maq-modulo`, ignora ocultos,
   `anotador/`, `vendor/`, `node_modules/` y archivos `_*`), cacheadas por version (inmutable). Si la raiz trae
   `anotador.config.js`, el editor lo inyecta antes del agente para ofrecer los temas del proyecto.
5. **Galeria** (`/maqueta`): tarjetas con vista previa viva (iframe a escala 1/4, sin interaccion), version, archivos,
   ultima subida y solicitudes abiertas/total. Owner/Admin: + Nuevo proyecto y panel Versiones (configuracion,
   subir ZIP, abrir/usar una version, archivar). `?p=` (v0.16.341-342) redirige al editor; `?gestionar=` abre el panel.
6. **Menu**: item "Maquetas" (ruta `maqueta`) en Sistema - Actividades, debajo de "Administrar actividades", en el
   menu por defecto y por backfill idempotente (`EnsureMaquetasMenuItemAsync`, generalizado como
   `EnsureMenuItemAfterAsync`, que tambien usa el backfill de Plantillas de correo).

## Consecuencias

- El kit `anotador/` de las maquetas deja de ser necesario (sigue funcionando su URL directa por compatibilidad).
- El riesgo de ADR-0179 (JS subido corre en el mismo origen) no cambia: el editor solo agrega un agente propio.
- Las solicitudes enviadas antes con el kit (origen `maqueta-{id del manifiesto}`) no aparecen en el panel de un
  proyecto con otro slug; siguen en su tablero.
- Pendiente (ola 2): MCP de maquetas para el PC local (listar solicitudes con captura/selector/archivo, marcar
  estado, publicar version).
- Pruebas: `MockupZipInspectionTests` (entrada index.html, origen y tablero por defecto, metadata), `MockupPagesTests`
  (pantallas), `MaquetaSolicitudesTests.EditorDelProyecto_*` (tablero propio, lista y conteos, dual) y
  `PlantillasCorreoMenuBackfillTests.BackfillMaquetas_*` (menu, dual).
