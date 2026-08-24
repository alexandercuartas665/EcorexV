# PROGRESO - ECOREX Sistema de Tareas

> Bitacora de avance por sesion. Formato: fecha, agentes, hecho, siguiente, bloqueos, decisiones.
> Complementa (no reemplaza) los ADRs de `docs/decisiones/` y el vault Obsidian.

---

## 2026-08-24 - v0.15.73: cerrar/decidir/reabrir SOLO el asignado o su cargo (ADR-0073, revierte ADR-0064)

- Decision del usuario: el flujo respeta la asignacion; un usuario distinto al asignado no cierra pasos
  ajenos. Se retira el override de Owner/Admin en `CompletePendingStepAsync`, `CompleteGatewayChoiceAsync`
  y `ReopenStepAsync` (reabre solo quien cerro). En el diagrama, `mcanAct`/`canAct`/`CanReopen` ya no usan
  `viewerIsManager`; se elimino el campo y el metodo `IsOwnerOrAdminAsync` quedo sin uso (removido).
- Las NOTAS del equipo NO cambian (siguen colaborativas para cualquiera).
- **Auditado (preview)**: como Diego (Owner/Admin, no asignado) el paso actual ahora es "PASO ACTUAL" de
  solo lectura, sin cerrar ni rutas (solo las notas). Antes lo podia cerrar.
- **Caveat registrado en ADR-0073**: todo nodo atendido debe tener encargado resoluble (asignado o cargo
  con candidatos); si no, ya nadie lo cierra. Se resuelve configurando "por cargo" por nodo en el editor.
- Config #1 (por cargo por nodo) es del editor de flujos (dropdown "Cargo / dependencia" + cargo por nodo);
  el motor ya lo soporta, no requiere codigo. Sin desplegar.

---

## 2026-08-24 - v0.15.73 (visuales): rama descartada en gris + eventos mas grandes (ADR-0074)

- **Aclaracion asignacion (no bug)**: el primer paso SI se asigna por cargo (Cotizacion sin cargo -> usa de
  respaldo el cargo del inicio; el asignado real, direccionventas, es del cargo Coordinador Comercial). Los
  pasos siguientes usan InheritStart (heredan a ese usuario), por decision del usuario se deja asi.
- **Rama descartada en gris**: al decidir una compuerta, el destino NO tomado se atenua (opacity/grayscale)
  con su arista punteada (`TaskFlowNodeDto.IsAbandoned`: nodo sin historial ya no alcanzable desde ningun
  paso vigente).
- **Eventos mas grandes**: inicio/fin de 38px -> 72px (re-centrados para no descolocar aristas).
- **Auditado (preview)**: eventos 72x72; "Cliente No compra" gris (opacity 0.5) + arista punteada tras
  elegir "Cliente Decide Comprar". Sin desplegar.

---

## 2026-08-24 - v0.15.72: la compuerta atendida deja ELEGIR la rama por destino (ADR-0072)

- **Bug**: en una compuerta de decision ATENDIDA (paso actual) no se podia escoger la ruta. Causa: las
  rutas se derivaban del NOMBRE del edge y las ramas del flujo de prueba no tenian nombre ni condicion,
  asi que el menu no ofrecia opciones (caia en "Cerrar actividad").
- **Fix**: una compuerta atendida deja elegir la rama por su NODO DESTINO. El diagrama lista TODAS las
  salidas (etiqueta = nombre del edge o del paso destino) con `TaskFlowRouteDto.TargetNodeId`. Nuevo
  `IWorkflowEngine.ChooseGatewayRouteAsync` completa la compuerta y sigue SOLO esa rama (sin depender de
  ConditionExpression). `IWorkflowInboxService.CompleteGatewayChoiceAsync` autoriza (asignado/candidato u
  Owner/Admin). La UI llama a la eleccion por destino cuando el nodo es compuerta.
- **Auditado (preview)**: la compuerta "Cliente Decide si compra" mostro las 2 rutas ("Cliente No compra"
  / "Cliente Decide Comprar"); al elegir "Cliente Decide Comprar" el motor siguio ESA rama (fin atendido
  quedo vigente esperando confirmacion). Sin desplegar.
- **Pendiente de decision del usuario (config, no bug)**: los nodos Cotizacion/compuerta usan
  AssigneeSource=InheritStart (heredan al iniciador), no Policy(cargo). Resolver "por cargo" es config por
  nodo en el editor. Y "otro usuario cierra" es el override de Owner/Admin (ADR-0064). A definir con el
  usuario si se cambia.

---

## 2026-08-24 - v0.15.71: contador de notas del equipo por nodo en el diagrama

- Cada nodo muestra CUANTAS notas del equipo tiene (ADR-0071): badge "(globo) N". En tarjetas Task va
  inline en la fila de badges (`.tk-flow-badge.notes`, color de marca); en compuertas/eventos es una
  pildora flotante en la esquina (`.tk-flow-notecount`). Solo si hay >0 notas. Reusa
  `TaskFlowNodeDto.TeamNotes.Count`; sin backend ni migracion.
- Auditado (navegador interno del preview): Cotización muestra "N 1" y la compuerta "N 1". Sin desplegar.

---

## 2026-08-23 - v0.15.70: notas colaborativas del equipo por nodo + color del nodo VISIBLE (ADR-0071)

- **Notas del equipo (nuevo)**: cualquier miembro con acceso a la tarea deja notas en CUALQUIER nodo
  del diagrama -- incluidos pasos FUTUROS de los que no es encargado -- para avisar algo a quien lo
  atienda. Entidad nueva `WorkflowNodeNote` (tenant-scoped, append-only) por (InstanceId, NodeId) con
  autor+fecha+texto; migracion DUAL (PG tabla workflow_node_notes + SQL Server). Servicio
  `IWorkflowInboxService.AddNodeNoteAsync`; el diagrama expone `TaskFlowNodeDto.TeamNotes`. El menu del
  nodo SIEMPRE muestra "NOTAS DEL EQUIPO" (hilo + caja para agregar); tras agregar, el menu queda
  abierto para ver la nota. Es distinta de la nota de CONFIG del editor y del comentario de CIERRE.
- **Color del nodo visible**: el color configurado en el editor ahora se ve claro en el diagrama de la
  tarea: tarjeta Task con tinte suave (color-mix 12%) + barra de acento 4px; compuerta/evento con tinte
  16-18%. Antes solo se pintaba una linea fina y "no se veian" los colores. `EnsureDraftAsync` ya copiaba
  Color/Note al derivar (no habia perdida de datos; era solo render).
- **Auditado con Chrome MCP** (tarea "test"): Cotización se ve amber, fin verde, fin rosa (compuerta sin
  color queda blanca, correcto). Nota agregada en el paso actual y tambien en un nodo FUTURO (compuerta);
  ambas persisten y se muestran con autor+fecha. Sin desplegar (deploy lo indica el usuario).

---

## 2026-08-23 - v0.15.69: reabrir paso cerrado + cerrar directo el paso con formulario (ADR-0070)

- **Menu del nodo mas visible**: el boton `...` pasa a pildora con borde/fondo/sombra, resaltada en
  hover y en color de marca en el paso vigente. El usuario no lo ubicaba.
- **Cerrar actividad directo**: el menu de un paso vigente atendible SIEMPRE ofrece "Cerrar actividad"
  con nota OPCIONAL, tenga o no formulario (antes un paso con formulario obligaba a "ir a diligenciar
  y cerrar"). Si hay formulario, se ofrece ademas "Diligenciar formulario" (opcional). Solo UI: el
  motor nunca exigio formulario para cerrar.
- **Reabrir actividad** (nuevo `IWorkflowEngine.ReopenStepAsync` + `IWorkflowInboxService.ReopenStepAsync`):
  reactiva EN SITIO un paso Task cerrado (Completed -> Pending+IsCurrent), deshace lo que activo aguas
  abajo (Pending -> Skipped) y conserva el asignado. Guarda dura: solo si la instancia sigue Running y
  NINGUN nodo posterior tiene cierre humano (Task/EndEvent Completed) ni rechazo; las compuertas
  automaticas Completed NO cuentan. Autoriza al encargado que cerro o a Owner/Admin. El diagrama expone
  `CanReopen`/`ReopenStepId` por nodo. Tablero: la tarjeta regresa al tablero/columna del nodo reabierto.
- **Estado vigente robusto**: `GetTaskFlowDiagramAsync` desempata el paso del nodo por
  CycleIndex desc, IsCurrent desc, CreatedAt desc (evita mostrar una fila vieja tras rechazo/reapertura).
- **Auditado con Chrome MCP** (tarea "test", flujo PROCESO COMERCIAL): pildora visible; menu de paso con
  formulario muestra "Cerrar actividad" + "Diligenciar formulario"; ciclo cerrar -> el paso queda CERRADO
  y avanza la compuerta -> "Reabrir actividad" vuelve el paso a actual y deshace la compuerta; el menu se
  cierra bien con clic real. Sin desplegar (deploy lo indica el usuario).

---

## 2026-08-23 - v0.15.68: menu de nodo del diagrama = popover FIJO (no lo recorta el scroller/zoom)

- **Bug:** el menu del nodo (anotar/cerrar/rutas) se abria dentro del canvas y quedaba RECORTADO por el
  scroller del diagrama (overflow:auto, max-height) -> no se veian los botones -> inusable. El
  padding-bottom (v0.15.67) no bastaba y el `zoom` del canvas descoloca `position:fixed` interno.
- **Fix:** el menu se renderiza a NIVEL RAIZ como popover `position:fixed`, anclado a la posicion en
  PANTALLA del boton del nodo (helper JS `ecorexFlow.anchorRect` en task-timer.js, con clamp al
  viewport). z-index 3000 + backdrop para cerrar al clic afuera. Los botones ... llevan
  `data-flow-anchor`. Se quito el padding-bottom del canvas (v0.15.67).
- **Auditado con Chrome MCP:** menu `position:fixed`, z-index 3000, `fullyVisible:true` (sin recorte);
  el backdrop lo cierra.

---

## 2026-08-23 - v0.15.66: al derivar el borrador del flujo se copian los CARGOS y agentes por nodo

- **Bug:** "los usuarios asignados se borran solos". Al editar un flujo publicado, `EnsureDraftAsync`
  deriva una version NUEVA (nodos con IDs nuevos) y copiaba color/nota/tablero/reinicio/formularios/
  reglas, PERO NO las `WorkflowNodePolicies` (cargos) ni los `WorkflowNodeAgents`. Asi, cada edicion/
  publicacion dejaba los cargos varados en la version vieja -> la nueva quedaba sin usuarios.
- **Fix:** `EnsureDraftAsync` ahora tambien copia las policies (cargo/dependencia por nodo) y los agentes,
  mapeando por BpmnElementId (igual que formularios/reglas). Junto con v0.15.65 (publicar re-apunta el
  concepto), editar+publicar ya conserva TODO (cargo, agentes, forms, color, nota, tablero, etc.).
- Correccion puntual en la copia local: se copiaron los 3 cargos del StartEvent a la version publicada
  actual para poder seguir probando de una.

---

## 2026-08-23 - v0.15.65: publicar re-apunta el concepto a la version publicada + menu de nodo se cierra

- **Bug (recurrente):** al editar+publicar un flujo se crea una version NUEVA, pero el concepto
  (ActividadSubcategoria) seguia apuntando a la version vieja (ya despublicada) -> el wizard mostraba
  "el flujo aun no esta publicado / se creara sin proceso" aunque el flujo SI estuviera publicado.
  Fix: `WorkflowEngine.PublishAsync` ahora re-apunta TODAS las subcategorias enlazadas a cualquier
  version de ese flujo (mismo ProcessCode) a la version recien publicada, en la misma transaccion.
- **UI menu de nodo:** el menu (anotar/cerrar/rutas) quedaba "abierto todo el tiempo" porque no cerraba
  al hacer clic afuera. Fix: clic en el fondo del diagrama (`.tk-flow-canvas`) cierra el menu abierto
  (los nodos hacen stopPropagation). Sigue abriendose/cerrandose con el boton ... del nodo.
- Sin migracion.

---

## 2026-08-23 - v0.15.64: Owner/Admin cierra cualquier paso desde el diagrama + nombre del responsable

- **Pedido:** no se podia "dar terminado" a un paso desde el menu del nodo, ni se veia el responsable por
  nombre. Causa: el menu abre pero queda en solo-lectura porque el VIEWER (Admin) no es el asignado
  (por diseno solo el asignado cierra); y el responsable salia como correo/cedula.
- **Cambios (WorkflowInboxService.GetTaskFlowDiagramAsync + CompletePendingStepAsync):**
  - Etiqueta de usuario = NOMBRE (display_name del platform user; fallback correo) -> el nodo muestra
    "Lilian Loaiza".
  - StepId del paso VIGENTE se puebla aunque el viewer no sea el asignado (para que el Owner/Admin pueda
    accionar). HasForm ahora sale de los WorkflowNodeForms del nodo (no del viewer) -> correcto para todos.
  - CompletePendingStepAsync autoriza tambien a OWNER/ADMIN del tenant (IsOwnerOrAdminAsync), no solo al
    asignado/candidato: gobierno del proceso.
- **UI (TaskDetailModal):** `_viewerIsManager` (tenant_role Owner/Admin) -> `canAct` incluye al manager,
  asi el menu del paso vigente ofrece "Cerrar paso"/rutas al Owner/Admin. Los pasos con formulario se
  cierran por el formulario ("Ir a diligenciar y cerrar").
- **Notas/color:** ya se guardan (version editada del flujo); se ven en actividades NUEVAS (las viejas
  usan la version anterior del flujo). Recordatorio: tras republicar, re-enlazar el concepto a la version
  publicada (hueco pendiente: publicar no re-apunta el concepto).

---

## 2026-08-23 - v0.15.63: diagrama de la tarjeta - nombres en TODOS los nodos + cerrar paso con form

- **Pedido:** el diagrama del proceso no mostraba los NOMBRES de los pasos siguientes (eventos/
  compuerta salian como circulos/diamante vacios) ni una forma de CERRAR el paso desde el grafico.
- **Agregado:**
  - Etiqueta de NOMBRE bajo el shape de eventos (inicio/fin) y compuerta (antes solo el nodo Task
    mostraba nombre). Ahora se leen "Requisicion de informacion tecnica", "Cliente Decide si compra",
    "Cliente Decide Comprar", "Cliente No compra".
  - Boton de menu (...) tambien en los eventos (antes solo Task y compuerta), para ver su info/nota.
  - Menu de un paso CON FORMULARIO: boton "Ir a diligenciar y cerrar" que abre la pestana Formularios
    (ese paso se cierra al enviar su formulario, y avanza al siguiente encargado).
- **Encargado por paso:** el nodo Task muestra su encargado; eventos/compuerta lo mostraran cuando
  tengan un cargo asignado (hoy solo el inicio tenia cargo). El menu por nodo ya permite anotar/cerrar
  (pasos sin formulario) o elegir ruta (compuerta atendida).
- Validado en local (T00026): los 5 nodos muestran nombre; colores verde/rosa correctos.

---

## 2026-08-23 - v0.15.62: diagrama de la tarjeta muestra color + nota configurada del nodo

- **Pedido:** en el diagrama del proceso (detalle de la tarea) cada nodo debe mostrar el COLOR y las
  NOTAS dejadas en la configuracion del flujo; el menu por nodo debe permitir anotar y cerrar el paso;
  y en Formularios ver los del paso del flujo sin perder los del concepto.
- **Ya existia:** el menu por nodo (textarea + "Cerrar paso" / rutas / reclamar) y la seccion de
  Formularios muestra concepto (tarjetas) + "Formularios del proceso" (del paso). Los del paso solo
  aparecen con un paso vigente (GetTaskStepFormsAsync).
- **Agregado (v0.15.62):** `TaskFlowNodeDto.Color` y `ConfigNote` (de WorkflowNode.Color/Note via
  GetCanvasAsync). El diagrama pinta el borde del nodo con el color configurado (o azul auto/violeta
  manual por defecto) y muestra la nota de configuracion en el cuerpo del nodo y en su menu. Helper
  `FlowNodeColor` (paleta violet/blue/green/amber/rose/slate -> var --t-*). Sin migracion.
- Nota: para VER el menu "Cerrar paso" y los formularios del paso hace falta que el flujo tenga un paso
  vigente (no Stuck). El flujo demo "PROCESO COMERCIAL" queda Stuck por un reinicio mal configurado en
  "Cotizacion Renombrado" (Reinicio -> inicio): quitarlo en el editor para que corra.

---

## 2026-08-23 - v0.15.61: el formulario del INICIO del flujo se ofrece en el wizard al crear (ADR-0069)

- **Pedido:** que el formulario asignado a un nodo del flujo "salga" en el paso 3 del wizard igual que
  los del concepto, sin perder la funcionalidad del concepto.
- **Causa:** el paso 3 solo mira `ActividadSubcategoria.FormDefinitionId` (form del CONCEPTO). Los del
  flujo son de NODO y se atienden en el detalle de la tarea; el del inicio ademas queda huerfano (el
  inicio auto-completa). Con el concepto sin form, el paso 3 mostraba "no tiene formulario asociado".
- **Fix (ADR-0069):** cuando el concepto no tiene form, el wizard usa el formulario del EVENTO DE INICIO
  del flujo. `EffectiveFormDefId = concepto ?? primer form del inicio`; se reemplazo el uso directo del
  form del concepto por esa propiedad en el paso 3, el modal y los helpers. Nuevo
  `IFormResponseService.GetSubcategoriaCreationFlowFormsAsync`. Anclaje: el form del flujo se ancla al
  numero EXACTO de la tarea (no "{numero}-{n}") para que el paso del flujo con ese mismo formulario
  reuse el borrador (continuidad v0.15.60). Si el concepto SI tiene form, todo queda IGUAL (cero riesgo).
- Sin migracion. Alcance: solo el/los form(s) del inicio; el primero garantiza continuidad.

---

## 2026-08-22 - v0.15.60: mismo formulario en varios nodos carga los MISMOS datos (continuidad)

- **Pedido:** si el mismo formulario esta asignado en varios nodos del flujo, cargar el MISMO
  formulario Y DATOS en los pasos siguientes (no en blanco).
- **Causa:** `FormResponseService.GetTaskStepFormsAsync` usaba `GetOrCreateDraftAsync(def, task.Number)`
  que SOLO reutiliza BORRADORES. Mientras el form estaba en borrador, los nodos con el mismo formulario
  compartian la respuesta; pero al ENVIARLO en un nodo pasaba a Submitted y el siguiente nodo ya no
  hallaba borrador -> creaba uno EN BLANCO (se perdia la continuidad).
- **Fix:** al resolver el formulario de un paso, PREFERIR una respuesta ya ENVIADA de ese formulario
  para la misma tarea (misma ancla definicion+numero). Si existe, se reusa (mismos datos) y el
  FormFlowLink del nodo nace Completed -> los datos se cargan en solo-lectura y el paso NO queda
  bloqueado esperando el formulario. Si no hay enviada, se usa/crea el borrador (Pending) como antes.
  UI: node posterior muestra el form como "Enviado" + "Ver" con los mismos datos.
- Sin migracion. Sin cambio de esquema.

---

## 2026-08-22 - v0.15.59: encargado del flujo usa el cargo del INICIO como respaldo + copia local de BD

- **Encargado (fallback al inicio):** `WorkflowStartService.ResolveFirstStepAsync` ahora, si el PRIMER
  TASK no tiene cargo, usa como respaldo el cargo del EVENTO DE INICIO. Antes solo miraba el primer Task,
  y un cargo puesto en el inicio (lo que el usuario hacia) daba "primer paso sin cargo" y la actividad
  nacia sin encargado. El runtime ya ruteaba el encargado elegido al primer paso (TaskItemService fija
  currentStep.AssignedToTenantUserId y salta la validacion cuando el Task no tiene cargo), asi que el
  fallback SOLO faltaba en la resolucion del wizard. Se ajusto la nota del panel del inicio.
- **Copia local de BD (dev):** el tunel SSH a prod (15433) hacia el dev local inusable (thread pool
  starvation, paginas congeladas). Se descargo un pg_dump de prod (db ecorex, ~22 MB) y se restauro en
  el Postgres local (ecorex-tareas-postgres:5442, db `ecorex`, 9 tenants). `appsettings.Development.local
  .json` -> Default apunta a 5442. App UP en 3s (antes minutos). Tunel cerrado. Ver memoria
  db-conexion-siempre-prod para refrescar/revertir. La copia NO se sincroniza con prod.

---

## 2026-08-22 - v0.15.58: fix concurrencia DbContext en editor de flujos + nota de cargo en inicio

**Agentes:** Claude Opus 4.8 (sesion de codigo). Sin migracion. Diagnostico con datos reales de prod
(via tunel, solo lectura). NO desplegado (el usuario avisa cuando).

- **Bug 1 (crash al guardar en el editor):** `System.InvalidOperationException: A second operation was
  started on this context instance...` al seleccionar un nodo (p.ej. el evento de fin "Cliente No
  compra") y Guardar. Causa: `OnAllowsAssignmentChangedAsync` y `SetAppearanceAsync` (FlowEditor)
  tocaban el DbContext del circuito SIN pasar por el `_dbGate` (semaforo que serializa el resto). Al
  togglear + guardar colisionaban. Fix: ambos handlers ahora corren dentro de `GuardAsync`
  (OnAllowsAssignmentChangedAsync ademas recarga policies). Bug preexistente que el interruptor nuevo
  de "punto de decision/cierre" (v0.15.56) hizo mas visible.
- **Bug 2 (crear tarea: "primer paso sin cargo" / sin encargado):** NO es bug de codigo. En el flujo
  publicado "PROCESO COMERCIAL" el cargo (3 policies) estaba en el nodo StartEvent "Requisicion de
  informacion tecnica", pero el encargado lo dicta el PRIMER TASK "Cotizacion Renombrado" (0 cargos).
  El cargo estaba en el nodo equivocado. Mitigacion UI: al seleccionar un StartEvent, el panel avisa
  que su cargo NO define el encargado (lo dicta el primer paso Tarea). El usuario debe asignar el
  cargo en "Cotizacion Renombrado" y publicar.

---

## 2026-08-21 - v0.15.57: adjuntos de tareas admiten CUALQUIER tipo de archivo (servido seguro)

**Agentes:** Claude Opus 4.8 (sesion de codigo). Sin migracion. Build verde. NO desplegado (el usuario
avisa cuando).

- **Pedido:** subir documentacion (.html) y prototipados (.vsdx) y, en general, CUALQUIER tipo de
  archivo en el sistema de tareas; para los no previsualizables, el visor solo DESCARGA (no renderiza).
- **Antes:** `StoreAttachmentAsync` (TaskDetailModal) tenia lista blanca de extensiones (pdf/office/
  imagenes/zip/video) y bloqueaba el resto; ademas tipos sin content-type (.vsdx) no se servian (404).
- **Cambio (seguro):**
  - Se ELIMINA la lista blanca en la subida (adjuntos Y comentarios usan el mismo store): se admite
    cualquier tipo; se conserva el limite de tamano (25 MB; 200 MB video).
  - `/uploads` se sirve endurecido (Program.cs): `ServeUnknownFileTypes` (entrega .vsdx como binario),
    `X-Content-Type-Options: nosniff` en todo, y `Content-Disposition: attachment` para todo lo que NO
    sea inline-seguro (imagen raster, pdf, video, audio, texto plano; el SVG se excluye porque puede
    ejecutar script). Asi un .html/.svg subido NUNCA se ejecuta inline en el origen de la app (XSS
    almacenado): el navegador SOLO lo descarga. Imagenes y pdf siguen viendose en el visor.
  - Los enlaces de descarga llevan `download="@FileName"` para conservar el nombre original.
- **Seguridad:** el motivo real de la lista blanca era evitar XSS por .html inline; se reemplaza por la
  defensa correcta (attachment + nosniff) que ademas cumple el requisito "solo descarga".

---

## 2026-08-21 - v0.15.56: compuerta exclusiva y evento como PUNTO DE DECISION HUMANO (ADR-0068)

**Agentes:** Claude Opus 4.8 (sesion de codigo). Sin migracion (propiedad calculada). Build verde.
Enfoque confirmado con el usuario: "punto de decision humano" + "todos los nodos".

- **Problema:** solo el inicio y las Tareas admitian asignacion de cargo/dependencia. La compuerta
  exclusiva se auto-resolvia (ADR-0037) y el evento de fin cerraba la rama al instante, asi que un
  cargo asignado ahi no se usaba. El usuario modela decisiones como compuertas ("Cliente Decide si
  compra") y necesita que las atienda un usuario/cargo que ELIJA la ruta.
- **Solucion (ADR-0068):** propiedad calculada `WorkflowNode.WaitsForHuman` (sin columna/migracion):
  Task siempre; compuerta/fin SOLO si el disenador activa la asignacion (`AllowsAssignment`); inicio
  nunca. Una compuerta atendida queda `Pending` al activarse y el asignado elige la ruta desde la
  bandeja/tarjeta; un fin atendido espera que un responsable confirme el cierre. Sin asignacion,
  compuertas/fines siguen automaticos (compatibilidad total).
- **Motor** (`WorkflowEngine`): `ActivateNodeAsync` no auto-completa compuerta/fin atendidos;
  `AdvanceAsync` los enruta al cerrarse por `ResolveOutgoing` (mismo `ApprovalResult`, ahora puesto
  por el humano). La resolucion del asignado dinamico (origen del asignado) se extiende a esos nodos.
- **Bandeja** (`WorkflowInboxService`/`WorkflowInboxProjection`): resuelve candidatos por policy para
  nodos atendidos; una compuerta atendida ofrece SUS PROPIAS rutas (`OwnRoutes`); el paso anterior a
  ella ya no las ofrece (la decision vive en la compuerta). La tarjeta/diagrama lo refleja igual.
- **UI** (`FlowEditor`): el panel "Asignar usuarios" habilita compuerta y fin con un interruptor
  claro ("Punto de decision" / "Punto de cierre") + selector de origen del asignado; el resto del
  panel (policies, origen) se reusa tal cual.
- **Sin tocar:** `WorkflowNodePolicyService`/`NodeAssigneeResolver`/`SetNodeConfigAsync`/
  `SetNodeAssigneeAsync` ya eran agnosticos al tipo de nodo.

---

## 2026-08-21 - v0.15.55: scheduler de busquedas interpreta runTime en la TZ del tenant (no UTC)

**Agentes:** Claude Opus 4.8 (sesion de codigo). Sin migracion (solo logica + label). Build verde.

- **Problema:** `ContactSearchScheduleWorker` interpretaba el `runTime` "HH:mm" en UTC, obligando al
  usuario a convertir a mano (09:00 Colombia = 14:00 UTC) y a equivocarse.
- **Fix:** el worker ahora trae la TZ del tenant (`Tenant.TimeZoneId`, IANA; default America/Bogota via
  `ScheduledJobRecurrence.ResolveTimeZone`) con un join a Tenants en el barrido cross-tenant.
  `LastScheduledOccurrence`/`IsDue` calculan la ocurrencia en HORA LOCAL del tenant y la convierten a UTC
  (`TimeZoneInfo.ConvertTimeToUtc`, respeta DST) antes de comparar con LastRunAt/nowUtc. Aplica a Diaria/
  Semanal/Mensual (dia y DOW en hora local).
- **UI** (`ContactSearchConfig.razor`): nota "Las horas se interpretan en la zona horaria del tenant
  (America/Bogota), NO en UTC" + title del input "Hora local del tenant".
- Verificacion: runTime=09:00 corre a las 09:00 hora del tenant (14:00 UTC en Bogota).

---

## 2026-08-20 - v0.15.54: origen del asignado por nodo (4 modos) en flujos (ADR-0056)

**Agentes:** Claude Opus 4.8 (sesion de codigo). Migracion DUAL (3 columnas). Config verificada en
navegador contra AGROMETALICAS; resolucion en runtime implementada + build verde (no ejercitada de
punta a punta con una instancia real).

- **Que:** cada Tarea define COMO se resuelve su encargado al activarse el paso, con 4 modos
  (`WorkflowAssigneeSource`): **Policy** (cargo/dependencia, historico), **InheritStart** (iniciador del
  flujo), **InheritPrevious** (quien atendio el paso anterior por el camino real), **FormField** (valor de
  un campo de un formulario diligenciado en un nodo anterior; id o correo de usuario).
- **Dominio:** `WorkflowNode.AssigneeSource` + `AssigneeFormFieldCode`; `WorkflowInstance.StartedByTenantUserId`
  (iniciador, seteado en StartInstanceAsync desde actorUserId). Enum registrado en ConfigureConventions
  (string). Migracion dual AddWorkflowAssigneeSource (default corregido a "Policy" y filas existentes de
  prod actualizadas de '' -> 'Policy').
- **Diseno:** `SetNodeAssigneeAsync` (guarda modo + campo); FlowCanvasNodeDto + proyeccion + EnsureDraftAsync
  (sobrevive publicar->editar). UI en "Asignar usuarios": selector "Origen del asignado"; en FormField
  aparece el input del codigo del campo; en Policy, el UI de cargo/dependencia existente.
- **Runtime:** `WorkflowEngine.ActivateNodeAsync` resuelve `AssignedToTenantUserId` al activar un paso Task
  pendiente segun el modo (Policy = null, la bandeja expande candidatos). InheritPrevious recibe el usuario
  del paso de origen via nuevo parametro; FormField lee FormFlowLink+FormResponse (ParseDocument) y mapea
  el valor a un TenantUser por id o correo.
- Verificado: el selector y el codigo de campo persisten (nodo Cotizacion Renombrado -> FormField /
  responsable_asignado). Gotcha resuelto: elegir FormField ya no exige el campo de una vez (se guarda el
  modo y aparece el input).

---

## 2026-08-20 - v0.15.53: "saltar a otro flujo" se muestra en el panel del nodo (no en el lienzo)

**Agentes:** Claude Opus 4.8 (sesion de codigo). Migracion DUAL (columna nueva). Verificado end-to-end
en AGROMETALICAS (PROCESO COMERCIAL -> ORDENES DE TRABAJO): el salto aparece en el panel, el lienzo
queda limpio, y el boton "x" lo quita.

- **Problema:** el salto a otro flujo se dibujaba como nodo CallActivity en el lienzo ("ORDENES DE
  TRABAJO"), y se veia feo/confuso (no se sabia que era ni a que estaba atado). La idea era mostrar,
  DEBAJO del boton "Saltar a otro flujo", a que flujo conecta el nodo SELECCIONADO.
- **Ahora:** el salto es una PROPIEDAD del nodo (`WorkflowNode.JumpToDefinitionId`, referencia suelta
  como el destino de tablero; migracion dual AddWorkflowNodeJumpTo). `IWorkflowDesignService.SetNodeJumpAsync`
  (valida que el flujo destino exista y no sea el propio). `FlowCanvasNodeDto` suma JumpToDefinitionId +
  JumpToName (proyeccion en BuildCanvasAsync con lookup de nombres). EnsureDraftAsync lo copia
  (sobrevive publicar->editar).
- **UI (FlowEditor):** ConfirmJump ya NO llama addCallActivity; guarda el salto en el nodo y recarga el
  canvas. Debajo del boton se muestra "Conecta a: <flujo>" con boton "x" para quitarlo; si no hay nodo
  seleccionado, un hint. OpenJumpAsync exige seleccionar un nodo primero.
- Verificado: 0 CallActivities en el lienzo; el destino se ve en el panel; el "x" lo limpia.

---

## 2026-08-20 - v0.15.52: correo filtrado con plantillas propias (cierra ADR-0056 paso E-mail)

**Agentes:** Claude Opus 4.8 (sesion de codigo). Migracion DUAL (EmailTemplate). Verificado end-to-end
contra AGROMETALICAS (BD prod por tunel): admin de plantillas, selector+preview en el disenador y
persistencia del template_id en la ventana.

- **Entidad + migracion:** `EmailTemplate : TenantEntity` (Nombre, Asunto, CuerpoHtml, Activa) con DbSet
  en IApplicationDbContext + EcorexDbContext + config inline; migraciones PG y SQL Server (AddEmailTemplate).
  Tabla `email_templates` con indice (tenant_id, activa). Query filter tenant automatico (ITenantScoped).
- **Servicio + merge:** `IEmailTemplateService` (CRUD tenant-scoped) + helper estatico
  `RenderTemplate(texto, EmailMergeFields, htmlEscapeValues)` que reemplaza {nombre},{empresa},{cargo},
  {ciudad},{correo}; valor faltante -> ""; ESCAPA las variables en el cuerpo (HTML) para evitar inyeccion,
  no en el asunto (texto plano). Registrado en DI.
- **UI:** pagina `/plantillas-correo` (lista + modal crear/editar con vista previa en vivo con datos de
  ejemplo). Disenador (`ContactWorkflowDesigner`): en el paso E-mail, selector de plantilla activa
  (bindea a la ventana via ScheduleVM.TemplateId, misma plantilla para todas las ventanas del paso) +
  vista previa del asunto/cuerpo renderizado. Guarda como ya guardaba.
- **Motor** (`ContactWorkflowDispatcher.ExecuteEmailAsync`): carga la `EmailTemplate` por
  schedule.TemplateId (Guid); si no existe o esta inactiva -> Skipped con motivo. Merge por contacto ->
  subject + htmlBody -> IEmailSender.SendAsync. `ResolveSegmentAsync` ahora trae Empresa (nombre de la
  empresa padre o Sector), Cargo y Ciudad del Tercero para el merge. Cache de plantilla por corrida.
  Conserva dedupe (ContactWorkflowRun), PackageSize (50/500) y RepeatEvery.
- **PRE-REQUISITO (lo hace el usuario, config, NO codigo):** el SMTP global de SendGrid tiene el usuario
  mal (`1144198690`); debe ser `apikey` (user=apikey, password=API key). Sin esto todo envio da 535.
  Cambiar en Configuracion -> Servidor de correo. Probar primero con un filtro chico y un correo propio.
- **Deuda previa:** los mismos test-doubles se ampliaron con el DbSet EmailTemplates (sln verde).

---

## 2026-08-20 - v0.15.51: FASE 2 - adaptador MCP JSON-RPC nativo (ADR-0067)

**Agentes:** Claude Opus 4.8 (sesion de codigo). Sin migracion (solo protocolo -> toolset). Verificado
contra AGROMETALICAS via instancia local -> BD prod.

- **Puente MCP nativo:** `POST /api/mgmt/agent/mcp?tenant={guid}` habla JSON-RPC 2.0 (initialize,
  notifications/*, tools/list, tools/call, ping) para que un cliente MCP nativo (Claude Desktop) se
  conecte directo. SOLO traduce protocolo -> las MISMAS llamadas del toolset; no duplica logica. Mismo
  gate (X-Ecorex-Mgmt-Key + ?tenant + AmbientTenantContext.Begin). tools/call audita cada mutacion
  (mgmt-api.mcp.{tool}); lecturas no.
- **Verificado:** initialize (serverInfo v0.15.x), notifications/initialized -> 202, tools/list (40
  tools con inputSchema), tools/call describe_components (lectura) + create_form (mutacion, auditada),
  metodo desconocido -> -32601, sin clave -> 401. Form de prueba archivado.

---

## 2026-08-20 - v0.15.50: API/MCP de autoria de formularios gobernada (ADR-0067)

**Agentes:** Claude Opus 4.8 (sesion de codigo). Sin migracion (lectura + delegacion). Verificado de
punta a punta contra el tenant AGROMETALICAS via instancia local -> BD de prod por tunel.

- **Problema:** crear formularios/plantillas/enlaces/modulos para un tenant se hacia por SQL directo
  (no gobernado). Ahora un agente de IA o cliente MCP lo hace "conectandose al sistema" con auth,
  seleccion de tenant, validacion, transaccion y auditoria, SIN SQL. Se EXPONE la logica existente.
- **Toolset nuevo** `FormAuthoringToolset : IAgentToolset` (Ecorex.Application/Tenancy), registrado en
  DependencyInjection.cs (3 lineas). ~34 tools (JSON-Schema inline) que delegan en IFormDefinitionService,
  IFormTokenService, IQuoteTemplateService, IRuleDocumentService, IFormResponseService, IMenuConfigService,
  IDataContainerService, ITerceroFieldService. Incluye `describe_components` auto-descriptivo (enums por
  reflexion + esquema de grilla lookup/resolve + marcadores de plantilla + verbos de impresion) y
  `wire_print_button` (documento+regla IMPRIMIR_PLANTILLA+boton+enlace en una operacion).
- **Superficie REST** reusando /api/mgmt (ADR-0057): `GET /api/mgmt/agent/tools` (catalogo con schema,
  solo auth) y `POST /api/mgmt/agent/tools/{tool}?tenant={guid}` (ejecuta con AmbientTenantContext.Begin +
  scope). Auth X-Ecorex-Mgmt-Key; cada MUTACION audita en super_admin_audit_logs (las lecturas no).
  Errores de validacion -> resultado estructurado {ok:false,status,error,field_errors}, no 500.
- **Verificado (aceptacion):** GET tools lista el toolset+schema; describe_components; y con solo tools
  (sin SQL) cree en AGROMETALICAS un formulario Text+Select+GridDetail(columna resolve), transaccional
  Sequence prefijo ADEMO (record ADEMO000001), plantilla, boton imprimir cableado, enlace /f/{token}
  (HTTP 200) y modulo /m/{code}, con auditoria por mutacion. Artefactos demo archivados tras la prueba.
- **Deuda previa resuelta de paso:** test-doubles desactualizados (ContactSearchRuns, ISequenceService,
  ctor de FormDefinitionService con `sequences`) impedian compilar los proyectos de test desde v0.15.39;
  se completaron para dejar `Ecorex.sln` verde.
- **Siguiente (FASE 2 opcional):** adaptador MCP JSON-RPC nativo (initialize/tools/list/tools/call) sobre
  AgentMcpServer que haga de puente hacia estos endpoints (sin duplicar logica).

---

## 2026-08-19 - v0.15.36: tokens {tareas.campo} en el valor por defecto de un campo de formulario

**Agentes:** Claude Opus 4.8 (sesion de codigo). Sin migracion (reusa DefaultValue). Validado req #2 en
preview (Vista Previa del COT sin tarea -> token vacio, sin texto crudo).

- **Problema:** un formulario ligado a un concepto (p.ej. cotizacion) volvia a pedir el cliente que ya
  tenia la tarea. Ahora el "Valor por defecto" de un campo admite TOKENS `{tareas.campo}` que se
  resuelven del contexto de la tarea anfitriona.
- **Renderer** (`DynamicFormRenderer`): nuevo parametro `TaskTokens` (dict) + `ResolveTokens(raw)` que
  reemplaza `{tareas.cliente|contacto|solicitante|email|correo|telefono|titulo|numero}` (regex,
  case-insensitive). Token sin contexto o desconocido -> VACIO (req #2: no genera conflicto ni deja el
  texto crudo cuando el form no viene de una tarea). Se aplica en la resolucion del default (gana el
  DefaultDynamic si existe).
- **TaskDetailModal**: `BuildTaskTokens()` arma el dict (cliente=RequesterName, email, telefono, titulo,
  numero) y lo pasa al `DynamicFormRenderer` de los formularios del concepto/paso.
- **FormDesigner**: hint bajo "Valor por defecto" con los tokens disponibles.
- **Extensible:** hoy solo el origen `tareas.*`; otros origenes se agregan en el mismo resolvedor.

---

## 2026-08-19 - v0.15.35: enlace flujo <-> tableros (la actividad salta de tablero/estado por paso)

**Agentes:** Claude Opus 4.8 (sesion de codigo). Feature nueva; migracion dual.

- **Idea:** cada nodo Tarea de un flujo puede apuntar a un TABLERO + COLUMNA destino; al activarse ese
  paso, la actividad ligada al flujo SALTA a ese tablero/columna. Al cerrar un paso y activarse el
  siguiente, la tarjeta se mueve sola.
- **Esquema:** `WorkflowNode.TargetBoardId` + `TargetColumnId` (migracion `AddWorkflowNodeBoardTarget`,
  nullable; metadatos por nodo, editables incluso sobre flujo publicado).
- **Motor** (`WorkflowEngine.ActivateNodeAsync`): cuando un paso queda Pending (espera a un humano) y su
  nodo tiene tablero destino, mueve la TaskItem ligada (`BoardId`/`ColumnId`/`ColumnEnteredAt`) en el
  mismo SaveChanges del avance. Columna null -> primera columna del tablero.
- **Servicio:** `IWorkflowDesignService.SetNodeBoardTargetAsync(nodeId, boardId?, columnId?)` (valida que
  la columna pertenezca al tablero). `FlowCanvasNodeDto` + `TargetBoardId`/`TargetColumnId`.
- **Diseñador** (`FlowEditor.razor`): en un nodo Tarea, selectores "Tablero destino" + "Columna/estado";
  carga tableros + columnas del tenant (IActivityBoardService + ITaskBoardService).

**Siguiente:** crear un flujo de 3 pasos en AGROMETALICAS y validar el salto al cerrar cada actividad.

---

## 2026-08-19 - v0.15.34: herramienta MCP de inventario (agente consulta items, solo lectura)

**Agentes:** Claude Opus 4.8 (sesion de codigo). Nuevo toolset del agente de IA; sin migracion.

- **`InventarioToolset`** (`IAgentToolset`, grupo "Inventario"): dos herramientas de SOLO LECTURA que el
  agente puede invocar al conversar:
  - `consultar_inventario(busqueda, incluir_inactivos, pagina)` -> lista de items (nombre, SKU, precio,
    marca, grupo, subgrupo, tipo, activo, stock total y por bodega). Reusa `IItemService.ListAsync`.
  - `ver_item(item_id)` -> detalle (descripcion, especificaciones, precio, stock por bodega,
    disponibilidad). Reusa `IItemService.GetDetailAsync`.
  - Registrado en DI como los demas toolsets -> aparece automatico en el catalogo de HERRAMIENTAS (MCP)
    del agente (toggles por agente). Aislado por tenant (query filter de Item). No crea ni modifica nada.

---

## 2026-08-19 - v0.15.33: hand-off Colmena (contactos scrapeados: avatar, promocion limpia, direccion/web, ficha Base)

**Agentes:** Claude Opus 4.8 (sesion de codigo). Implementa el hand-off de la sesion de arquitectura
Colmena (4 puntos). Migracion dual + seed de ficha Base al arrancar.

- **#1 Avatar -> iniciales (no icono roto):** en `GestorContactos.razor` y `DirectorioGeneral.razor` el
  avatar rinde iniciales de BASE + `<img>` superpuesta con `onerror="this.remove()"`; si la imagen
  falla o falta, quedan las iniciales. CSS `.gc-av-wrap/.gc-av-img` y `.dg-av-wrap/.dg-av-img`.
- **#2 Promocion limpia:** `PromoverProspectoAsync` -> `Perfiles=Ninguno` (antes Sospechoso),
  `IdTipo=Ninguno` (antes caia a Nit), copia `ImagenUrl`; `Estado=Prospecto`.
- **#3 Direccion + SitioWeb en el scraping:** `ProspectoScrapeado.Direccion` + `SitioWeb` (migracion
  dual `AddProspectoDireccionSitioWeb`, nullable). Sink `ContactSearchRunner` mapea direccion (SafeText)
  y sitio_web (SafeHttpUrl, distinto de OrigenUrl=ficha Maps); instruccion pide `direccion` y `sitio_web`.
- **#4 Ficha "Base" (000232):** `TerceroFichaService`/`TerceroFieldService` siembran ficha `base`
  (Perfil=null, siempre visible, IsSystem) con campos direccion/sitio_web/correo; seed IDEMPOTENTE para
  tenants existentes (se agrega si falta). La promocion escribe `Tercero.FichasJson` = { base:
  { direccion, sitio_web } } (correo/telefono siguen en columnas base del Tercero).

**Gotchas:** FichasJson = ficha->campo->valor; el seed de fichas/campos ahora agrega solo lo que falta
(no re-siembra todo). La ficha Base aparece siempre en el modal del tercero.

---

## 2026-08-18 - v0.15.32: tiempo en estado por tarjeta + motivo de cierre por tablero + auto-archivar >30d

**Agentes:** Claude Opus 4.8 (sesion principal). Validado en preview (dev local + /dev/login) via MCP
Chrome, punta a punta. Migracion dual (PG + SqlServer) aplicada y probada en dev.

- **Tiempo en estado por tarjeta (kanban):** el relojito discreto pasa a etiqueta clara
  "En este estado: X" con color (gris/ambar/rojo) segun antiguedad en la columna. Dato: `ColumnEnteredAt`.
- **Motivo de cierre por tablero (#B):** nuevo `TaskBoard.CloseReasonsJson` (lista configurable en
  Editar tablero) + `TaskItem.CloseReason`. Al mover una tarea a una columna FINAL (IsDone), si el
  tablero tiene motivos, se pregunta (OPCIONAL: elegir uno o "Cerrar sin motivo"); se guarda y se
  muestra en Resumen. `MoveTaskAsync` acepta `closeReason`. Aplica desde la modal (drag-drop directo
  queda como fast-follow). VALIDADO: prompt con 4 motivos -> "Resuelto" -> Resumen muestra el motivo.
- **Auto-archivar terminadas > 30 dias (#C):** las vistas normales del tablero (Todas/Mios/No
  asignados) ocultan las cerradas hace mas de 30 dias; siguen en el alcance "Terminados". Filtro en
  `ApplyScope` (`status NOT IN (Done,Closed) OR closed_at >= now-30d`), con `teamCount` consistente.

**Esquema:** migracion `AddCloseReasons` (task_boards.close_reasons_json + task_items.close_reason),
ambos nullable/aditivos. Auto-aplica en el arranque (backup previo del deploy).

---

## 2026-08-18 - v0.15.31: fecha inicio (Gantt) + video en adjuntos + contactos empresa consistentes

**Agentes:** Claude Opus 4.8 (sesion principal). Cambios de UI/servicio; SIN migracion (StartDate ya
existia). Investigacion de datos contra prod (via tunel 15433) + local.

- **Doble fecha en tareas para el Gantt (#1):** `TaskItem` YA tenia `StartDate` + `DueDate`, pero la
  UI solo editaba la de entrega y, peor, `BuildUpdate` no reenviaba `StartDate` -> CADA update la ponia
  en null (bug latente). Ahora el modal trae una pildora "Inicio" (fecha) junto a "Entrega"; el update
  conserva `StartDate`; el Gantt ya pinta la barra `StartDate -> DueDate` (antes caia a CreatedAt).
  Granularidad de dia (el Gantt es por dias); la hora no se usa.
- **Video en adjuntos (#2):** la bitacora (worklog) y el wizard rechazaban mp4 ("tipo no permitido").
  Se agregaron `.mp4/.webm/.mov/.m4v/.mkv/.avi/.ogg` a ambas listas blancas, con tope propio de
  **200 MB** para video (25 MB el resto). La subida por InputFile es en chunks -> el tamano no choca
  con SignalR.
- **Contactos ligados a empresa (#3):** el contador de la empresa sumaba TODOS los vinculados, pero la
  lista de relaciones filtraba `Tipo=Persona && Estado<>Inactivo` -> "dice 10, muestra 0". Se alinearon:
  la lista muestra todos los vinculados ACTIVOS (sin exigir Tipo=Persona) y el contador tambien excluye
  inactivos. NOTA: no reproducible con la data actual (ni prod ni local tienen una empresa con 10
  contactos); pendiente el caso puntual del usuario (tenant/empresa) para confirmar al 100%.

---

## 2026-08-18 - v0.15.30 + MSI 1.7.7: copiar descripcion, roles en tableta, agente (panal arriba, sin demo)

**Agentes:** Claude Opus 4.8 (sesion principal). Web validado en preview (5234 -> ecorex_dev 5442) con
`/dev/login`. Agente WPF (MSI 1.7.7) compilado y copiado a Downloads.

**Web (v0.15.30):**
- **Copiar descripcion de la tarea**: boton "Copiar" junto a "Editar" en la seccion Descripcion del
  `TaskDetailModal`; copia TODO el texto al portapapeles (`navigator.clipboard.writeText`) con flash
  "Copiado". VALIDADO: el boton aparece cuando hay descripcion.
- **Roles y permisos en tableta**: la matriz se salia por la derecha. Causa: `.rp-layout` usaba
  `300px 1fr` y el `1fr` (=minmax(auto,1fr)) no encogia bajo el min-content de la tabla. Fix:
  `300px minmax(0,1fr)` (+ `minmax(0,1fr)` en <=900px) para que la matriz ENCOJA y su
  `.rp-matrix-scroll` (overflow-x:auto) haga el scroll interno. VALIDADO en 1024x768: la pagina ya no
  scrollea en horizontal; la matriz scrollea dentro de su tarjeta.

**Agente Colmena (MSI 1.7.7):**
- **Panal anclado ARRIBA**: `ItemsControl` del panal pasa de `VerticalAlignment=Center` a `Top`
  (`MainWindow.xaml`). Antes el centrado dejaba un hueco transparente arriba (se veia oscuro) e impedia
  subir el panal al tope de la pantalla.
- **Sin "Demo"**: se quita el item "Demo (Ctrl+D)" del menu de bandeja y el atajo Ctrl+D; el menu de
  bandeja queda "Mostrar / Ocultar / Salir" (se agrego "Ocultar" -> HideToTray, lo que el usuario pedia
  como "ocultar el aplicativo"). Cerrar ya iba a la bandeja (OnClosing cancela salvo "Salir").

**Entrega:** MSI en `apps/agent/installer/dist/Ecorex-AgenteColmena-1.7.7.msi` + copia en Downloads.

---

## 2026-08-18 - v0.15.29: editor de CSS del formulario (#8) + kanban celular + fecha del tablero

**Agentes:** Claude Opus 4.8 (sesion principal). Validado en preview local (5234 -> `ecorex_dev` 5442)
con `/dev/login` (owner@sky-system.local) via MCP Chrome. Deploy a prod al final.

**Que:**
- **#8 CSS/estilos del formulario** (cierra el lote de 9): columna nueva `custom_css` en
  `FormDefinition` (migracion dual PG + SqlServer, auto-aplica al arrancar -> `ALTER TABLE
  form_definitions ADD custom_css text`). El disenador (pestana Propiedades) trae un textarea "CSS
  personalizado"; el servicio `SetCustomCssAsync` lo guarda (solo presentacion, no toca revision). El
  `DynamicFormRenderer` inyecta un `<style>` acotado con **@scope (#ecx-form-{id})** al contenedor del
  formulario (no se filtra a la pagina; si el navegador no soporta @scope, no se aplica -> nunca
  contamina) y emite una clase estable **field-{FieldCode}** por campo para estilar un objeto puntual.
  Se neutraliza cualquier `</style>` de ruptura. VALIDADO: el `font-style:italic` del CSS de prueba se
  aplico al titulo y el `<style>` sale con @scope; el textarea carga el valor guardado.
- **Kanban en celular** (seguia apinado): la rejilla inline `repeat(N, minmax(0,1fr))` metia N columnas
  en 375px. En `<=640px` se sustituye por `grid-auto-flow:column` + `grid-auto-columns:82vw` +
  `overflow-x:auto` + scroll-snap -> columnas de ~308px legibles, se desliza una a la vez y el body ya
  NO scrollea en horizontal. VALIDADO.
- **Fecha de entrega del tablero**: la cabecera pintaba "Sin fecha limite" aunque el tablero no tuviera
  fecha (inutil). Ahora el chip `.ab-ddue` solo se muestra si `_detail.DueDate` no es null.

**Archivos:** `FormDefinition.cs` (+CustomCss), migraciones `AddFormCustomCss` (PG + SqlServer),
`FormDtos.cs`/`IFormDefinitionService.cs`/`FormDefinitionService.cs` (DTO + SetCustomCssAsync),
`DynamicFormRenderer.razor` (inyeccion @scope + field-{code}), `FormDesigner.razor` (textarea en
Propiedades + guardado), `ActivityBoardDetail.razor` (chip fecha condicional), `app.css` (kanban movil),
`AppVersion.cs`.

**Decisiones:** una sola columna (`custom_css` a nivel formulario) + clases `field-{code}` en el render
en vez de una columna de estilo por campo (menos esquema, misma potencia). Scoping con @scope (falla
seguro). Kanban movil = patron "una columna a la vez" (estilo Trello), no apilado.

---

## 2026-08-17 - Lote UX/movil v0.15.28 (8 de 9 puntos; persistencias, formularios, galeria, sede, auditoria movil)

**Agentes:** Claude Opus 4.8 (sesion principal). Validado en preview local (5234 -> BD `ecorex_dev`
docker 5442) con `/dev/login` (`ECOREX_DEV_LOGIN=owner@sky-system.local`, atajo Development-only de
`Program.cs`) via MCP Chrome. Sin migracion (todo codigo/CSS/JS).

**Que (lote de 9 solicitudes del usuario):**
1. **Tema oscuro no persistia** al saltar de modulo -> `App.razor`: la navegacion mejorada "morphea"
   el `<html>` y borraba las clases de estado. Se re-aplican `dark` + `sidebar-collapsed` en cada
   `enhancedload` Y con un `MutationObserver` sobre la clase de `<html>` (red de seguridad); los
   toggles ahora escriben localStorage ANTES de la clase para no pelear con el observer. VALIDADO:
   ambas clases sobreviven al navegar a /tableros.
2. **Filtro por sede en conceptos** (`TaskWizard.razor`): SIN Empresa/Area elegida el picker mostraba
   TODOS los conceptos, incluidos los que tienen sede configurada. Fix en `SubcategoriasVisibles`: sin
   sede elegida solo se ven los conceptos que aplican a TODAS; un concepto con sede solo aparece al
   elegir su sede. (Data de repro creada en SKY: "Cotizacion de equipos" -> sede "Agencia Norte".)
4. **Menu ocultar persistente**: mismo fix de #1 (bulletproof). VALIDADO en vivo.
3. **Filtro por tablero persistente** (`ActivityBoardDetail.razor`): antes solo persistian vista +
   panel; ahora un snapshot JSON en localStorage (`ecorex.board.{id}.prefs`) guarda tambien ALCANCE
   (equipo/mios/no asignados/terminados) y los CHIPS (columnas/usuarios/etiquetas/vencimiento); se
   restaura por tablero al entrar. `_prefsLoaded` se resetea al cambiar de tablero.
5. **Galeria de adjuntos al crear tarea** (`TaskWizard.razor`): el paso "Archivos" pasa de una lista de
   nombres a una GALERIA con miniatura (imagenes via `RequestImageFileAsync` -> data URL) e insignia de
   tipo (PDF/DOC/XLS/ZIP...) + tamano legible + quitar. CSS `.tk-gallery` en `app.css`.
6. **Modal en tableta**: footer pegajoso (`.modal-foot { position: sticky; bottom: 0 }`) para que
   Guardar/Cerrar/Enviar queden SIEMPRE visibles en pantallas cortas. (Verificado que el modal de vista
   previa del designer ya era alcanzable por scroll en 768x1024 y 1024x768.)
7. **Controles listcheckbox/roundbox** (`FormDesigner.razor`): `Radio` y `MultiCheck` estaban
   registrados y renderizados pero FALTABAN en el desplegable "Tipo de elemento" -> agregados junto a
   Select.
9. **Auditoria movil**: overflow horizontal sistemico. (a) ~15 rejillas de KPIs con columnas fijas
   `repeat(4|5|6,1fr)` sin breakpoints -> bloque global en `app.css` (2 col <=900px, 1 col <=560px,
   `!important` para ganar al CSS scoped). (b) Barra del tablero (`.ab-scopes`, `.ab-tabs`,
   `.ab-tabs-right`) se ajusta por lineas <=640px. (c) Kanban: relleno lateral reducido + `overflow-x`
   contenido en celular. VALIDADO: /actividades y /formularios sin scroll horizontal tras el fix.

**Siguiente:** #8 (editar CSS/estilos del formulario) es lo unico SIN empezar — feature nueva:
requiere columna `CustomCss` en `FormDefinition` (+ `CssClass` por campo), UI en el designer (textarea
"CSS personalizado" + clase por campo) e inyeccion `<style>` scoped en `DynamicFormRenderer`. Pendiente
de una siguiente ola.

**Decisiones:** persistencia de UI con `MutationObserver` como red de seguridad (no depender solo de
`enhancedload`); fix de sede en el nivel del wizard (unico picker de conceptos); auditoria movil con
override global + `!important` para no editar 15 CSS scoped uno por uno.

---

## 2026-08-13 - Deploy prod v0.15.6 -> v0.15.7 (nombre del tenant en el token del agente)

**Agentes:** Claude Opus 4.8 (sesion principal). Deploy a prod via build-from-git.

**Que:** el agente Colmena no mostraba el nombre del tenant. Causa: `app2.bitcode.com.co` (a donde se
conecta el agente) corria backend **v0.15.6**, sin el cambio v0.15.7 que devuelve `TenantName` en
`/api/agente/token` (`AgentChannel.cs`). Aclaracion importante: **app2 NO es un server aparte** — es el
Caddy externo (66.94.104.237) reverse-proxy a `10.0.0.3:5480` (contenedor `ecorex-app`); se despliega
por 10.0.0.3 (mi runbook de siempre).

**Deploy:** commit `a704581` (`fase-0/clon-backbone`). Backup `ecorex-2026-08-13-0544.sql.gz` (regla de
oro) -> `docker compose -f docker-compose.from-git.yml build --no-cache` (~16 min) -> `up -d`. Imagen
`ecorex-superadmin:local` nueva; `/login` 200; footer local (`:5480`) y publico (`app2`) = **v0.15.7**.
`.env` INTACTO (a pedido del usuario: no se toco ninguna clave). Warning de fondo no fatal:
`duplicate key ix_import_runs_tenant_id_process_id_fired_at` (scheduler de importaciones deduplica;
preexistente). El agente reconecto solo al subir el contenedor (06:07) y ya tiene el TenantName en
memoria; para PINTARLO en la Colmena falta un refresco de estado (reiniciar Colmena/servicio o instalar
MSI 1.5.1, que ademas trae el fix de la allow-list).

---

## 2026-08-12 - Agente Colmena: fix persistencia allow-list del Navegador (MSI 1.5.0)

**Agentes:** Claude Opus 4.8 (sesion principal). Solo `apps/agent` (WPF). Sin cambio de backend
(AppVersion sigue 0.15.7). Commit `e3e5a90` a `fase-0/clon-backbone` + `main`.

**Bug reportado:** en el flyout de Navegador/Archivos de la colmena, agregar dominios y "Guardar",
cerrar y reabrir -> la lista salia VACIA (no persistia). Ejemplos que se perdian: example.com,
quotes.toscrape.com, google.com, www.google.com, maps.google.com, google.com.co.

**Causa raiz:** `HiveViewModel.SaveCapability()` escribia por el pipe con la GUI NO elevada. La boveda
(allow-list + consentimiento) la posee el Servicio LocalSystem (ADR-0039), que rechaza SILENCIOSAMENTE
escrituras de una conexion no-admin. La colmena se auto-lanza sin elevar tras el MSI, asi que ese era
el caso tipico -> nunca se guardaba.

**Fix (mismo patron que SaveConfig / ADR-0050):**
- `App.xaml.cs`: comando headless combinado `--save-caps <browser|files> <0|1> [entries-coma]` que
  escribe allow-list + consentimiento en UNA invocacion (un solo prompt de UAC).
- `ElevationHelper.cs`: `SaveCapabilityElevated(kind, enabled, entries)` que relanza el exe elevado
  (Verb=runas) con `--save-caps`; refactor de `RunElevated(exe, args)` compartido con SaveConfig.
- `HiveViewModel.SaveCapability()`: si no esta elevado y hay servicio, eleva por UAC (una confirmacion);
  si ya esta elevado o es demo, conserva la ruta directa/pipe. El Servicio recoge el cambio por su
  FileSystemWatcher.
- MSI reconstruido como **1.5.0** (build-installer.ps1 -Version 1.5.0), dejado en Descargas para que el
  usuario lo instale (upgrade sobre 1.0.0 instalado).

**Siguiente:** usuario instala MSI 1.5.0, habilita Navegador y agrega dominios (ya persisten); rebuild
de app2 con la rama para que el token devuelva el nombre del tenant (feature v0.15.7).

**ACTUALIZACION (mismo dia) - segundo eslabon, MSI 1.5.1 (commit `71fc935`):** con 1.5.0 el usuario
reporto que SEGUIA sin guardar. Diagnostico completo con evidencia (servicio Running, producto 1.5.0,
GUI 1.5.0 corriendo, boveda ACL'd ilegible sin elevar): la escritura elevada SI persistia el archivo,
pero el Servicio -unico que puede LEER la boveda- solo re-difundia estado al conectar o en los Set* del
pipe; su unico FileSystemWatcher vigilaba SOLO `config.dat`. Como la colmena vive en la BANDEJA, cerrar
y reabrir la VENTANA no reconecta el pipe (proceso vivo) -> `_serviceState` viejo -> flyout vacio aunque
el disco tuviera los dominios. Fix: segundo watcher en `AgentWorker` sobre `browser-allow.dat` /
`file-allow.dat` / `consent.dat` que REEMITE estado (`BroadcastStateAsync`, sin rearmar el hub) al
detectar la escritura; la colmena refleja la lista en vivo y el sub-agente Navegador recibe la politica
nueva. MSI reconstruido como **1.5.1** a Descargas. Recordar: SALIR de la colmena desde la bandeja antes
de instalar para que el binario se reemplace.

---

## 2026-08-12 - Renderizador de panel GENERICO por spec (ADR-0066)

**Agentes:** Claude Opus 4.8 (worktree `feat/renderizador-panel-spec-adr0066` desde
`origin/fase-0/clon-backbone`). Build verde (Application + SuperAdmin). Application.Tests 663/663.
SIN commit-a-tronco ni deploy (PR abierto a `fase-0/clon-backbone`; deploy lo maneja la sesion principal).

**Contexto:** peticion de la sesion de reportes para dejar de necesitar codigo + deploy por cada panel.
Un panel debe ser DATO (un PanelSpec JSON) que UN solo componente renderiza, con la MISMA calidad que
los paneles a medida (OCS/Tareas/SIIGO).

**Que (ADR-0066, implementado):**
- **PanelSpec + validador** (`Ecorex.Application/Reporting/Panels/`): DTO del spec (sources main+lookups,
  join, derived year/yyyymm/month/date, filters dropdown|daterange|text, kpis sum|count|countDistinct|avg
  con format money|moneyM|percent|int, widgets line|bar|donut|pareto|matrix|table). `PanelSpecValidator`
  PURO valida contra el catalogo tenant-safe (fuentes por nombre de negocio, alias de lookup, derivados).
- **PanelDataEngine** (puro, testeable sin Docker/Blazor): join + derivados + filtros + agregaciones +
  group-aggregate (escala/topN/orden) + pareto con acumulado + matriz cruzada + tabla + formato. Es el
  nucleo NUMERICO que reproduce 1:1 a los paneles a medida.
- **EChartBuilders** (Shared/Reporting): builders de option de ECharts extraidos (Line/VerticalBar/
  HorizontalBar/Donut/Pareto) para que a-medida y generico usen el mismo option.
- **SpecPanelRenderer.razor(.css)**: UN componente que resuelve fuentes por nombre, carga la principal
  UNA vez, join/derivados/filtros en memoria, y pinta KPIs+widgets reusando los estilos rpt-*.
- **Autoria como DATO** en la Galeria: boton "Nuevo panel" (Nombre + editor JSON + Validar contra el
  catalogo + Guardar como ReportDefinition Kind=Dashboard, SourceKey `panel:spec`) + Editar/Duplicar/
  Archivar desde el visor. Sin recompilar, sin desplegar. Convive con `panel:ocs`/`panel:system-activities`
  a medida (fallback vivo). Reusa plantillas (ADR-0062) para ofrecerlo entre tenants (ReportTemplate
  Kind=Panel + SourceKey `panel:spec`).
- **Migraciones:** NINGUNA (se reusa `ReportDefinition.SpecJson`/`SourceKey`).
- **Pruebas:** `PanelDataEngineTests` + `PanelSpecValidatorTests` (20 nuevas, verdes).
- **Ejemplos:** `docs/decisiones/ADR-0066-ejemplos/{ocs,tareas,siigo}.json`.

**1:1 vs fallback:** SIIGO se reproduce 1:1 (KPIs, serie mensual, pareto con acumulado, join NIT->Nombre,
millones, por vendedor/estado, tabla). OCS reproduce KPIs/graficas/matriz+heatmap (orden de filas de la
matriz por total de celda, no por equipos distintos: valores y heatmap iguales, orden minimamente distinto).
Tareas reproduce graficas + KPI Total; los KPIs CONDICIONALES (Abiertas/Cerradas/Suspendidas) NO son una
agregacion simple -> `ActivitiesDashboardPanel` sigue como componente a medida (escape previsto por la ADR).

**Siguiente:** que la sesion de reportes cargue los 3 specs de ejemplo (o los publique como plantillas) y
valide en PROD con los contenedores reales (facturas/clientes, Software OCS). Posible: op derivada extra
y KPIs condicionales por spec si se quiere jubilar el panel de tareas a medida.

**Decisiones:** ADR-0066 ACEPTADA (portada al tronco desde `feat/reporte-siigo-agro` con addendum de
implementacion). El panel es dato; el componente a medida queda como escape.

---

## 2026-08-11 - Lista: colores Excel + bordes + ancho por columna + import/export de formularios

**Agentes:** Claude Opus 4.8. Trabajo contra la BD de PROD (tunel 15433 -> db `ecorex`) y el docker local
`ecorex_dev` segun el caso. Build verde (SuperAdmin + Application). SIN commit ni deploy aun.

**Contexto:** afinando el tablero "Comercial - Requerimiento Infraestructura" (SKY SYSTEM) para que se vea
como la hoja `SEGUIMIENTO_COTIZACIONES` del Cotizador.xlsx.

**Que:**
- **Colores de header = Excel**: la banda de GRUPO (supra-titulo) ahora se pinta SOLIDA con texto blanco
  (`GroupHeadStyle`); antes no tenia color. Config del tablero (local) con los hex del Excel por grupo
  (Datos cliente `#1D7A4A`, Cotizacion `#8B6914`, Asesor `#374151`, Seguimiento tarea `#7A1D1D`).
- **Bordes de columna** visibles (rejilla) en la Lista.
- **Ancho por columna configurable y persistente por tablero**: nuevo `TaskListColumnConfig.Width` (aditivo).
  Se ajusta ARRASTRANDO el borde derecho del encabezado (`.tkl-resizer` + JS `initResizers` -> `[JSInvokable]
  SaveColumnWidthAsync` que guarda en `list_view_config_json`) o por el campo "Ancho" del configurador.
  Cuando hay anchos, la tabla pasa a `table-layout:fixed` con elipsis (permite angostar). `<colgroup>` render.
- **Import/Export de formularios por JSON** (000131): `IFormDefinitionService.ExportAsync/ImportAsync`
  (partial `FormDefinitionService.ImportExport.cs`). Export = sobre `{formatVersion, definition}` con el
  DetailDto (contenedores+preguntas, enums por nombre). Import = crea SIEMPRE uno NUEVO con codigo unico
  (nunca pisa), remapea contenedores padre-primero y ContainerId de preguntas, anula SubformDefinitionId
  colgado. UI en `Formularios.razor`: boton "Importar JSON" (modal textarea) + "Exportar" por tarjeta
  (descarga via `ecorexDownloadText` en board-list.js?v=3).
- **Bug formularios en la tarjeta**: el modal exige `tarea -> subcategoria -> FormDefinitionId`; el tablero
  no. En local las tareas de ejemplo no tenian subcategoria y el concepto no tenia form. Corregido (dato):
  concepto "Cotizacion de equipos" (local) enlazado + T00010/T00011 con subcategoria.

**Identidades (a pedido del usuario):**
- PROD: reset clave de `mercadeo@skysystem.com.co` (Owner SKY) a la que pidio. `calidad@agrometalicas.com`
  ya era solo de AGROMETALICAS (nada que quitar).
- LOCAL (`ecorex_dev`): creado `mercadeo@skysystem.com.co` (Owner SKY) + creado tenant AGROMETALICAS
  (clonando tenant+suscripcion+vista de menu "Completo" de SKY, 69 nodos) y movido `calidad` a AGROMETALICAS.

**Siguiente:**
- Traer SIMULADOR DE COTIZACIONES (prod `59a91ffe...`) a LOCAL via el Import nuevo, reenlazar el concepto y
  reconfigurar las columnas del tablero contra los field codes de SIMULADOR (en PROD el concepto ya apunta a
  SIMULADOR: correcto). Reiniciar la app local para cargar el codigo nuevo. Commit/deploy pendientes.

**Decisiones:** dev conectado a PROD por diseno (appsettings `Default` gana sobre `ECOREX_DB_CONNECTION`).
Import de formulario = SIEMPRE crea nuevo (no sobrescribe).

**Adenda (misma sesion):**
- **SIMULADOR a LOCAL**: exportado de prod (JSON envelope) e insertado en `ecorex_dev` (mismo id), unico
  formulario activo (los otros 21 archivados). Concepto "Cotizacion de equipos" -> SIMULADOR. Tablero
  reconfigurado contra SIMULADOR (cliente/fecha/total + detalle grilla `items`). Solo 3 tableros activos
  (Comercial/Marketing/Soporte); T00010 = unica cotizacion de ejemplo con respuesta SIMULADOR.
- **BUG Directorio General (campos que desaparecen)**: causa = `<DataLookupField>` sin `@using` de su
  namespace (`Components.Shared.Lookups`) -> Blazor lo pintaba como elemento vacio (warning RZ10012). Fix:
  `@using ...Components.Shared.Lookups` en `Components/_Imports.razor` (global; cubre Terceros/Inventario/Tareas).
- **Campo geografico Colombia (nuevo)**: `FormControlType.Geografia` = Pais(fijo Colombia)/Departamento/Ciudad
  encadenado desde el catalogo DANE (`Ciudad`). Nuevo componente `GeografiaField.razor` + caso en
  `DynamicFormRenderer` (`RenderGeografia`); metodos `ICiudadCatalogService.ListDepartamentosAsync/
  ListMunicipiosAsync`; expuesto en el disenador (paleta `ControlReg` + selector de tipo). Valor = JSON
  {Pais,Departamento,Ciudad}. PENDIENTE menor: formatear ese JSON al mostrarlo en columnas/reportes.
- **Reportes**: hand-off del BUG/GAP del conector RDL entregado a la sesion ECOREX.reportes.
- Todo compila (0 errores). Falta reiniciar la app local para ver los cambios de UI.

---

## 2026-08-10 - v0.14.0: detalle configurable en la Lista (expandir filas de un GridDetail)

**Agentes:** Claude Opus 4.8, worktree `feat/campos-personalizados-tarea`. Validado en sandbox (sembrando
una grilla + una respuesta). UI (razor + css) + backend (2 metodos de lectura), sin migracion. COMMITEADO,
NO desplegado (prod en v0.13.1).

**Contexto:** el tablero "Comercial - Requerimiento Infraestructura" (SKY SYSTEM) debe verse como el Excel
`SEGUIMIENTO_COTIZACIONES` (Cotizador.xlsx). Una cotizacion tiene VARIOS items -> se resuelve con el
GridDetail nativo del formulario (form SIMULADOR, grilla "Items", que NO se toca). El usuario pidio que las
columnas del detalle sean CONFIGURABLES (form dinamico) y un solo GridDetail por tablero.

**Que (capacidad generica):**
- `TaskListViewConfig.Detail` (aditivo): `{FormDefId, GridFieldCode, Columns[]}`. Configs viejas parsean igual.
- Backend `IFormResponseService`: `GetBoardGridSourcesAsync(boardId)` (campos GridDetail de los forms del
  tablero + sus columnas via `FormGridCalculator.ParseColumns`) y `GetBoardTaskGridRowsAsync(boardId,
  formDefId, gridFieldCode)` (filas del grid del form EFECTIVO por tarea, leidas del jsonb de la respuesta).
- Configurador (modal Columnas): seccion "Detalle (items)" -> elegir UN formulario+GridDetail y cuales
  columnas ver/ordenar (nada fijo).
- Render (Lista): si hay detalle configurado, la fila padre muestra "+" y expande a las filas del grid
  (items) con las columnas elegidas (cabecera + valores, numeros formateados). Reusa el arbol; cuando hay
  detalle, la expansion usa el grid en vez de subtareas.

**Verificacion:** sembrado en sandbox una grilla `items` (Detalle/Cantidad/Valor unitario) en el form del
tablero + una cotizacion (T00010) con 3 items -> el configurador lista la fuente, se eligen columnas, y al
expandir aparecen los 3 items formateados. `dotnet build` verde.

**Pendiente (config del tablero real, no ingenieria):** crear los 9 campos de "Seguimiento tarea" (Estado/
Resultado Lista, fechas, prob.cierre, notas...) + mapear las 26 columnas del Excel + apuntar el detalle al
SIMULADOR/Items (esto ultimo requiere desplegar el feature).

## 2026-08-10 - v0.13.1: /api/mgmt GET /tenants (descubrimiento de tenants) + API habilitada en prod

**Agentes:** Claude Opus 4.8, worktree `feat/campos-personalizados-tarea`. Validado en sandbox. UN archivo
(`AgentMgmtEndpoints.cs`), sin migracion. COMMITEADO, NO desplegado (prod en v0.13.0).

**Que:** `GET /api/mgmt/tenants` (no requiere `?tenant=`): lista TODOS los tenants con `status`, `kind` y
conteo de agentes/lineas, para que el operador (Claude) descubra sobre que tenant operar. Se extrajo el
gate de AUTH (`CheckAuthGate`, sin tenant) del `CheckGate` completo, y un helper `RunNoTenant` que abre
scope + DbContext y consulta con `IgnoreQueryFilters` (Tenants es global). Verificado: sin header->401,
con key->lista.

**Ademas (operativo, este dia):** la API `/api/mgmt` se HABILITO en prod (v0.13.0): se genero
`ECOREX_MGMT_API_KEY`, se agrego al `.env` + al bloque `environment:` del compose (server y repo, commit
`0a61298`), se recreo el contenedor y se probo E2E contra prod (lectura cross-tenant de AGROMETALICAS +
crear/configurar/borrar un agente de prueba). La clave la tiene el usuario; vive solo en `/opt/ecorex/.env`.

## 2026-08-10 - v0.13.0: API de gobierno /api/mgmt extendida (tools MCP + lineas + bindings + logs por conversacion)

**Agentes:** Claude Opus 4.8 (2 sub-agentes Explore para mapear infra + dominio), worktree
`feat/campos-personalizados-tarea`. Validado en sandbox `ecorex_dev`. UN archivo (`AgentMgmtEndpoints.cs`),
sin migracion ni config nueva. Desplegado v0.13.0.

**Contexto:** peticion de construir una API REST cross-tenant para que un operador (Claude) administre
los agentes de IA de cualquier tenant sin UI. ECOREX YA tiene esa API (`/api/mgmt`, ADR-0057: gate por
env `ECOREX_MGMT_API_KEY`->404, allowlist IP, key en tiempo constante, auditada) pero le faltaban huecos.
Decision del usuario: EXTENDER la existente (API key + tenant por ?tenant=), no crear una fachada JWT.

**Que se agrego (huecos del spec):**
- `GET /mcp-tools` catalogo de tools MCP (via `IAgentToolset`).
- `PUT /agents/{id}/tools {toolKeys:[...]}` opt-IN: valida contra el catalogo (400 si invalida) y persiste
  el COMPLEMENTO como `DisabledTools` (el modelo interno es opt-out). El detalle `GET /agents/{id}` ahora
  expone `mcpTools` con `enabled` por-agente.
- `GET /lines` (id/label/provider/phone/estado/boundAgentId).
- `POST /agents/{id}/line-binding {whatsAppLineId,reassign?}` (200 / 409 si otra agente la atiende) y
  `DELETE /agents/{id}/line-binding/{lineId}` (204/404). Reusa `AiAgentLineService.SetConnectedAsync`.
- `GET /agent-logs` (conversaciones) + `GET /agent-logs/{conversationId}` (entradas).
- `POST/PUT/DELETE .../resources` (recursos del agente, bonus).

**Adaptacion a ECOREX (vs PROPIA):** aislamiento Modelo B (solo EF query filters, sin RLS) via
`AmbientTenantContext.Begin`; claim super admin real = `platform_role=SuperAdmin` (no aplica aqui porque
se eligio API key); host desplegado = `Ecorex.SuperAdmin` (ya sirve el grupo `/api/mgmt`). TODA mutacion
audita en `super_admin_audit_logs` (IAuditWriter, actorType=System, reason "mgmt-api"). Verificacion
fail-closed OK (sin key->404, sin header->401, ruta inexistente->404, sin tenant->400) y flujo E2E completo
en el sandbox (crear agente, tools opt-in/out, bind/409/reassign/unbind, logs) con 7 filas de auditoria.

## 2026-08-10 - v0.12.0: descripcion sobre tabs + subtareas anidadas en Lista + agrupador con totales

**Agentes:** Claude Opus 4.8, worktree `feat/campos-personalizados-tarea`. Validado en sandbox `ecorex_dev`.
UI (razor + css) + un campo aditivo `RequesterName` en `ActivityCardDto` (sin migracion). COMMITEADO, NO
desplegado (prod sigue en v0.11.1).

**Que (3 mejoras del modulo de actividades):**
1. **Descripcion SIEMPRE visible:** la tarjeta de Descripcion salio del tab "Detalle" y quedo ENCIMA de
   los tabs (Detalle/Formularios/Documentos), asi se ve en cualquier tab.
2. **Subtareas anidadas en la vista Lista/Tabla:** cuando NO se agrupa, las tareas padre muestran un
   boton `+`/`-` que despliega sus subtareas como filas indentadas debajo (con `↳`), en vez de dispersas.
   Estado por tarjeta en `_expandedParents`.
3. **Agrupador de la Lista + totales numericos:** selector "Agrupar por" (Etapa/Estado/Encargado/Prioridad/
   Solicitante + cualquier campo de formulario/custom visible). Cada grupo muestra un encabezado con el
   valor + conteo + la SUMA de las columnas numericas, y un `<tfoot>` con el TOTAL GENERAL. Numerico =
   campo Number/Currency/Calculated o campo de formulario con valores parseables. El agrupador es en vivo
   (no se persiste); el arbol de subtareas aplica solo cuando no se agrupa.

Todo el render de la Lista se reescribio sobre `BuildDisplayRows` (record `LcDisplayRow`) que arma las
filas (encabezados de grupo + datos con profundidad) y pagina por filas de nivel superior. `dotnet build`
verde; los errores de consola vistos eran SignalR reconectando por los reinicios del dev.

## 2026-08-10 - v0.11.2: reacomodo del modal de detalle (Resumen al rail + Subtareas en la izquierda)

**Agentes:** Claude Opus 4.8, worktree `feat/campos-personalizados-tarea`. Validado en sandbox `ecorex_dev`.
Solo UI (TaskDetailModal.razor + app.css), sin migraciones ni cambios de backend. COMMITEADO, NO desplegado
(a peticion del usuario; prod sigue en v0.11.1 hasta confirmar deploy).

**Que:** reorganizacion de las columnas del detalle de la tarea a peticion del usuario:
- **Resumen** sale de la columna central y sube al **rail derecho** (arriba de la Bitacora), como tarjeta
  propia. La `aside` de bitacora pasa a ser un rail (`.tk-detail-rail`, flex column sticky con scroll) que
  contiene dos tarjetas: Resumen + Bitacora.
- **Subtareas** pasa a la **columna izquierda**, debajo de Registro de tiempo.
- Columna central queda: Lista de chequeo, Asignados, Flujo.

El movimiento de bloques se hizo por marcadores (scripts idempotentes) para no romper el markup; el Resumen
conserva toda su funcion (etiquetas, avance) por moverse verbatim. `dotnet build` verde, sin errores de
consola, sin secretos.

## 2026-08-09 - v0.11.1: reload del modal en scope EF propio + color de columna claro

**Agentes:** Claude Opus 4.8, worktree `feat/campos-personalizados-tarea`. Validado en sandbox `ecorex_dev`.
Sin migraciones (solo codigo/UI). Desplegado v0.11.1.

**Que:**
1. **Endurecer el race del DbContext:** `TaskDetailModal.ReloadAsync` corre ahora en un scope EF PROPIO
   (`IServiceScopeFactory.CreateAsyncScope` + `AmbientTenantContext.Begin` para el filtro por tenant) y
   serializado con un `_reloadLock`; el cuerpo se extrajo a `ReloadCoreAsync(...)` que recibe los servicios
   del scope fresco. Elimina el "second operation on this context instance" que aparecia cuando la recarga
   del modal solapaba con la del tablero (por broadcast) sobre el DbContext compartido del circuito.
   Verificado: 1a mutacion tras reinicio refresca en sitio, 0 errores en log.
2. **Color de la COLUMNA (cuerpo), no solo el titulo:** ya existia (`CellColor`/`ColCellStyle`) pero era
   dificil de ver. Rotulos del configurador claros ("Encab." vs "Columna", 1a col. renombrada a "Campo")
   + tip; el cuerpo se tine visiblemente (20% + franja lateral inset del color en cada celda).

**Verificacion:** MCP Chrome en dev: color rojo aplicado a la columna "Etapa" -> celdas tenidas + franja;
subtarea agregada refresca en sitio sin error. `dotnet build` verde. Sin secretos; clave super admin intacta.

## 2026-08-09 - v0.11.0: subtareas + encabezado multinivel con color + UX de tablero/lista

**Agentes:** Claude Opus 4.8, worktree `feat/campos-personalizados-tarea` sobre `fase-0/clon-backbone`.
Construido y validado contra el sandbox local `ecorex_dev` (localhost:5234, dev-login). Desplegado v0.11.0.

**Que:**
1. **Tareas hijas (subtareas):** `TaskItem.ParentId` (self-FK Restrict, un solo nivel; migracion dual
   `AddTaskSubtasks`). Alta rapida por titulo desde la seccion "Subtareas" del detalle
   (`ITaskItemService.CreateSubtaskAsync`: tarea completa en el MISMO tablero/columna del padre, sin
   concepto para no arrastrar el flujo). La subtarea se pinta como TARJETA propia con indicador
   `↳ Txxxxx` (numero del padre). El **progreso del padre agrega las subtareas terminadas** al checklist:
   la barra y el texto de la tarjeta usan `(checklist + subtareas)` Done/Total (campos `SubtaskDone`/
   `SubtaskTotal` en `ActivityCardDto`); el "Avance" del modal tambien.
2. **Encabezado multinivel con color en la vista Lista:** super-titulos/titulos/subtitulos que se
   repiten agrupan por colspan (`MergeRuns`); color de encabezado (fondo) + 2o color para el cuerpo de
   la columna (`ColHeadStyle`/`ColCellStyle`).
3. **UX de tablero/lista:** scroll acotado por columna en Tablero, paginacion en Lista (50/pagina) y
   una barra de scroll horizontal SUPERIOR sincronizada con la inferior (`board-list.js`).
4. **Dev-login de depuracion:** `GET /dev/login` solo en Development + env `ECOREX_DEV_LOGIN` (inicia
   sesion sin clave; nunca se mapea en prod que corre en Production).

**Migracion (dual PG+SQL):** `AddTaskSubtasks` (parent_id + indices + FK self Restrict). Aditiva,
idempotente; se aplica sola en el arranque (MigrateAsync).

**Verificacion:** auditoria completa por MCP Chrome en `ecorex_dev`: alta de 2 subtareas (T00220/T00221),
seccion Subtareas 1/2, Avance combinado 1/5, tarjeta de subtarea con `↳ T00010`, tarjeta del padre 1/5
(texto+barra coinciden). `dotnet build` verde. Diff sin secretos; clave del super admin intacta.

**Bloqueos:** un race puntual del DbContext del circuito (modal+tablero recargan a la vez tras una
mutacion) que aparecio en el 1er alta; latente/pre-existente al patron, no corrompe (la 2a entro limpia).

## 2026-08-08 - v0.10.0: 4 olas sobre el detalle de tarea + vista Lista (ADR-0065)

**Agentes:** Claude Opus 4.8, worktree `feat/campos-personalizados-tarea` sobre `fase-0/clon-backbone`.
Construido contra la BD de PROD por el tunel (SkipDemoSeed); las 3 migraciones aditivas ya se
aplicaron a prod durante el desarrollo. Desplegado en v0.10.0.

**Que (4 olas, todas sobre el modulo Tareas):**
1. **Campo "Lista del Directorio"** (nuevo `TerceroFieldType.DirectoryLookup`): un campo de tablero que
   lista terceros del Directorio General filtrados por perfil (Cliente/Proveedor/Empleado/Todos),
   reusando `TerceroLookupSource`/`IFormLookupService`. Config en `Options` (JSON `DirectoryLookupConfig`),
   sin migracion. Captura con `DirectoryLookupField.razor` (guarda el Id del tercero, referencia viva).
2. **Vista Lista configurable POR TABLERO:** `TaskBoard.ListViewConfigJson` (jsonb/nvarchar(max),
   migracion `AddTaskBoardListViewConfig`). Configurador "Columnas": elegir/reordenar columnas
   (incorporadas + campos del tablero + campos de formulario), color, titulo/subtitulo; encabezado y
   1a columna fijos por CSS. Render de tabla dinamica (`TaskListViewConfig`/`TaskListColumnConfig`).
3. **Formulario activo por defecto de la tarea:** `FormResponse.IsActive` (migracion
   `AddFormResponseIsActive`), exclusivo por tarea; efectivo = marcado / original / mas antiguo. UI:
   badge "Activo" + boton "Marcar activo" en las tarjetas de la pestana Formularios.
4. **Columnas de campo de FORMULARIO en la Lista:** en el configurador se elige un formulario (concepto
   o paso, relevantes al tablero) y sus campos; se pintan como columnas informativas tomando el valor
   del formulario ACTIVO (concepto) o de la respuesta del paso. Sin migracion (lee de `FormResponse.Data`).
   Servicios `GetBoardFormsAsync`/`GetBoardTaskFormValuesAsync`.

**Migraciones (duales PG+SQL):** `AddTaskBoardListViewConfig`, `AddFormResponseIsActive`
(`AddTaskCustomFields` de PR #5 tambien va en este deploy). Todas aditivas; ya aplicadas en prod.

**Bloqueos:** validacion visual pendiente (el login lo hace el usuario; se desplego a peticion
explicita sin recorrido UI). `dotnet build` verde en cada ola.

## 2026-08-06 - Campos personalizados de la tarea POR TABLERO (ADR-0065)

**Agentes:** Claude Opus 4.8 (sub-agente de ingenieria), worktree `feat/campos-personalizados-tarea`
sobre `origin/fase-0/clon-backbone`. NO desplegado (va en el proximo deploy unificado).

**Que:** al trabajar una tarea se pueden AGREGAR campos que se presentan y capturan en el modal de
detalle (`TaskDetailModal`), y que **solo existen en el TABLERO donde se crearon** (alcance por board).
La configuracion usa la misma logica de "Directorio General" (modal "Configurar campos") y soporta
TODO el set de tipos de DG.

**Hecho:**
- **Dominio:** `TaskFieldDefinition` (TenantEntity, filtro global) agrupada por `BoardId` (FK NO ACTION),
  reusa el enum `TerceroFieldType` (Text/Number/Currency/TextArea/Select/Date/Phone/Separator/
  Calculated/Lookup). FieldKey unico por (tenant, board). `TaskItem.CustomFieldsJson` (jsonb PG /
  nvarchar(max) SQL Server), dict FieldKey->valor de un solo nivel.
- **Application:** `ITaskFieldService`/`TaskFieldService` calcado de `ItemFieldService` (CRUD por board,
  reordenar, mover-a-otro-board, validar/calcular Calculated reusando `FormulaEngine`/`FormulaCalculator`,
  Lookup via `IDataLookupService`). `ITaskItemService.UpdateCustomFieldsAsync` persiste el JSON (editable
  si la tarea no esta Cerrada; actividad solo si cambia). `TaskItemDetailDto.CustomFieldsJson`.
- **UI:** boton "Campos" en `ActivityBoardDetail` -> modal de configuracion del board (replica la UI de
  `InventarioItems`, sin selector de tipo porque el board es fijo). Tarjeta "Campos del tablero" en
  `TaskDetailModal` que renderiza cada campo editable por tipo (reusa el componente compartido
  `DataLookupField` para Lookup), recalcula Calculated (solo lectura) y guarda en `CustomFieldsJson`.
- **Decision reuse-vs-replicar (ADR-0065):** se REUSA la logica (tipos, formulas, DataLookups +
  `DataLookupField`); se REPLICA la UI de config + editor de valor dentro de los componentes de Tareas,
  siguiendo la convencion del repo (Item ya replico de Tercero). Documentado en el ADR.
- **Migraciones DUALES aditivas** `AddTaskCustomFields` (PG `EcorexDbContext` + SQL Server
  `SqlServerEcorexDbContext`, `--context` explicito): 1 tabla + 1 columna. `has-pending-model-changes` =
  "No changes" en ambos. La app las auto-aplica en el arranque.
- **Tests:** `TaskFieldTests` en matriz dual (PG + SQL Server, Testcontainers): (a) definiciones scoped
  por board, (b) round-trip de valores en `CustomFieldsJson`, (c) Calculated se recalcula, (d) aislamiento
  cross-tenant. 8/8 verdes localmente (Docker). Application.Tests 643/643 verdes.

**Pendiente:** captura multi-valor/repetible (AllowMultiple/RepeatWithFieldKey) queda en el esquema pero
sin cablear en la UI (single-value + Calculated + Lookup + Select + Separator completos end-to-end).

**Siguiente:** merge del PR a `fase-0/clon-backbone`; el deploy lo hace la sesion principal (unificado).

---

## 2026-08-06 - v0.9.3: DEPLOY UNIFICADO (conector de datos externos + consolidacion)

**Que:** deploy unificado a prod (v0.9.2 -> v0.9.3). Lleva el conector de origen de datos externo
gobernado (ADR-0064, PR #4) con su **migracion DUAL** `AddExternalDataConnector` (PG + SQL Server),
que el app auto-aplica en el arranque (`db.Database.MigrateAsync`). Incluye la limpieza del `panel:tareas`
(camino A obsoleto). La identidad del agente reconfigurable por MSI (ADR-0063) es solo del binario del
agente, no del servidor. Gates locales verdes; backup previo con ./backup.sh. El piloto en vivo de db3dev
(registrar la fuente externa + mapear el RDL) queda para validacion del usuario.

---

## 2026-08-06 - Conector de datos externos GOBERNADO (ADR-0064)

**Que:** Nueva via CORRECTA para que un reporte lea datos EXTERNOS en vivo (p.ej. la legacy db3dev)
SIN que el reporte lleve su propia cadena de conexion. Antes todo entraba por `IReportDataSource`
tenant-safe; un RDL con su ConnectionString violaba 3 de los 9 errores heredados. Rama
`feat/conector-datos-externos` rebaseada sobre `origin/fase-0/clon-backbone`.

- **Entidades de PLATAFORMA (no tenant-scoped):** `ExternalDataSource` (motor SqlServer/Postgres,
  cadena CIFRADA con ISecretProtector, IsReadOnly, IsEnabled, LastValidatedAt), `ExternalDataSet`
  (consulta CURADA = allowlist: un unico SELECT parametrizado + parametros tipados + campos),
  `ExternalDataSourceGrant` (concesion explicita por tenant: la unica via de acceso, ya que el dato
  externo no se puede filtrar por TenantId). Enum `ExternalDataProvider/ParameterType/ParameterBinding`.
- **Conector `ExternalReportReader`** (analogo a ContainerReportReader): `ReportSourceKind.External`,
  clave `external:{datasetId}`. Verifica concesion del tenant (fail-closed), descifra en memoria,
  enlaza parametros y ejecuta. El catalogo (`IReportCatalog`) expone solo lo concedido al tenant activo.
- **Alcance por contexto de confianza** (`ExternalParameterBinder`): userid/sucursal/tenant salen del
  contexto, NUNCA de entrada libre; los inputs (fechas) se convierten al tipo y viajan como parametro
  TIPADO (cero concatenacion).
- **Executor `AdoExternalQueryExecutor`** (en Infrastructure, drivers Microsoft.Data.SqlClient/Npgsql):
  SOLO LECTURA forzada por `ExternalReadOnlyGuard` (rechaza escrituras/DDL/multi-statement/SELECT INTO)
  + `SET TRANSACTION READ ONLY` real en Postgres + `DbParameter` tipados.
- **Render imprimible:** `ReportDefinition.ExternalBindingJson` (mapeo dataset RDL -> ExternalDataSet +
  inputs). `ExternalReportBindingService` importa el RDL y, al renderizar, ejecuta cada dataset por el
  conector e inyecta UNA DataTable por dataset. `BoldReportsApiController` extendido para multi-dataset
  (sigue en ProcessingMode.Local; el RDL nunca usa su conexion).
- **PlatformAdmin:** pagina `/fuentes-externas` (policy PlatformOperator) CRUD de fuentes/datasets/
  concesiones + prueba de conexion de solo lectura. Toda mutacion -> SuperAdminAuditLog; el secreto no
  se re-muestra. Enlace agregado al NavMenu.
- **Migraciones DUALES `AddExternalDataConnector`** (PG + SQL Server, `--context` explicito):
  3 tablas + columna `external_binding_json`. Tras el rebase, el ModelSnapshot de ambos proveedores
  se consolido con el tronco y se verifico con `dotnet ef migrations has-pending-model-changes` =
  "No changes" en PG y SQL Server.
- **Limpieza (camino A obsoleto):** se retira la plantilla dormida `panel:tareas` (Reporte de Sistema
  de Tareas) del seeder + `TareasDashboardPanel` + su entrada en `ReportGallery`, porque ese reporte
  ahora va por el conector EN VIVO, no por contenedor.
- **Tests:** unit `ExternalReadOnlyGuardTests` + `ExternalParameterBinderTests`; integracion dual
  `ExternalConnectorGovernanceTests` (tenant concedido ve/ejecuta, sin concesion no ve/lanza, revocar
  quita acceso, cadena cifrada). ADR-0064 ACEPTADA.
- **Pendiente (validacion humana):** el piloto en vivo con db3dev (data source "Maravilla", .rdl y
  credencial read-only los tiene el usuario). NO desplegado (lo hace la sesion principal, deploy unificado).

---

## 2026-08-06 - Agente Colmena: identidad reconfigurable por MSI + blindaje del secreto vacio (MSI 1.3.0)

**Agentes:** Claude Opus 4.8 (sub-agente de ingenieria), worktree `fix/agente-identidad-msi`.

**Problema (visto en SOLDARCO):** el vault de identidad del agente
(`%ProgramData%\Ecorex\Agent\config.dat`, machine-scope) no se podia re-fijar. (1) La custom action
del MSI solo corria en install NUEVO (`NOT Installed`), asi que reinstalar con propiedades sobre un
agente ya instalado no re-aplicaba la identidad. (2) Un `SECRET` vacio corrompia el vault: MSI
reemplazaba el argumento final vacio `""` por basura (`CURRENTDIRECTORY=...`) que se escribia como
secreto -> `401 Firma invalida` permanente.

**Hecho:**
- **MSI reconfigurable:** condicion de `EcorexConfigureIdentity` cambiada de
  `CLIENTID AND HUBURL AND NOT Installed` a solo `CLIENTID AND HUBURL` (cubre install/reinstall/modify).
  Guard intacto: sin props no toca el vault. CA sigue deferida + SYSTEM. (Product.wxs)
- **Blindaje `__KEEP__`:** `SetProperty` fuerza `SECRET=__KEEP__` cuando no vino SECRET, antes de
  armar el comando, para que el 4o argumento nunca sea vacio (evita la corrupcion CURRENTDIRECTORY).
  `AgentIdentity.Merge` (nuevo, en Core, con tests) interpreta `__KEEP__`/vacio como "conserva el
  secreto actual". Para re-fijar ClientId/hub sin exponer el secreto: OMITIR SECRET.
- **Diagnostico `--show-identity` / `--whoami`:** imprime la identidad activa del vault (ClientId +
  hub + si hay secreto, sin el valor); se engancha a la consola del proceso padre. (App.xaml.cs)
- ADR-0063. Build verde, 38 tests verdes.

**Validado en esta maquina (MSI 1.3.0, elevado):** config.dat se reescribe en CADA reinstall
(incluida misma version) -> reconfigurable OK; CustomActionData ya pasa `"__KEEP__"` limpio (sin
basura); `--show-identity` reporta la identidad; el secreto se conserva (size estable) al omitir SECRET.

**Bloqueo/consecuencia:** el PRIMER test (con `SECRET=` explicito, como pedia el guion) DISPARO la
corrupcion antes del fix y SOBREESCRIBIO el secreto real del vault de esta maquina; quedo un secreto
DUMMY -> el servicio esta Offline (`Firma invalida`). **Accion humana pendiente:** re-ingresar el
secreto REAL de `cli_a942beecf941` por la GUI (o `--save-config` elevado) para volver Online.

**Siguiente:** merge del agente (sin redeploy del servidor); test interactivo de la GUI (Guardar -> UAC)
lo valida un humano.

---

## 2026-08-06 - v0.9.2: formularios de la actividad como tarjetas + tablero Terminados + impresion

**Formularios del concepto como TARJETAS (multiples por tarea):** la pestana Formularios del detalle
lista todas las respuestas del formulario del concepto como tarjetas, con "+ Agregar formulario" y
"Copiar" por tarjeta. Numeracion heredada de la tarea: Reference = "{numero tarea}-{n}" (T00001-1,
-2, ...), estable (max sufijo + 1), pre-llenada en el campo "numero" del formulario. El wizard ancla
la primera como "-1". Editable segun estado (KeepEditable), Finalizar/Reabrir por tarjeta. Backend:
GetTaskConceptFormsAsync (linkage por prefijo), CreateTaskConceptFormAsync, DuplicateResponseAsync,
helpers de numeracion. Renombrado generico "cotizacion"->"formulario".

**Tablero:** nuevo scope **"Terminados"** (4o chip junto a Todas del equipo / Pendientes mios / No
asignados) que filtra a tareas con estado Done, con su contador (ActivityBoardScope.Done +
ScopeCounters.Done + ApplyScope). Y **badge de estado** en la tarjeta del kanban (Terminada /
Suspendida / Cerrada) para que al marcar el estado se vea. Resuelve el "no la marca como terminada".

**Recarga resiliente:** ReloadAsync del detalle envuelto en try/catch: un error transitorio de BD
(el tunel de dev cayendose) ya NO tumba el circuito Blazor (se avisa para reintentar). El "explota"
al marcar Terminada era el tunel dev, no un bug; la transicion en si funciona (validado en navegador).

**Impresion:** el endpoint HTML de plantilla acepta print como string ("1"/"true"), el boton
"Imprimir cotizacion" navega con print=1 que no bindeaba a bool -> 500 (commit 1541355, ya en tronco).

Sin migracion. Validado en navegador (Chrome MCP): chip Terminados + badge Terminada + impresion.
Gates locales verdes. Deploy con ./backup.sh.

---

## 2026-08-06 - v0.9.1: libs de Chromium en el runtime del Dockerfile

**Que:** se agregan las librerias del sistema que el Chromium headless (PuppeteerSharp) necesita al stage
de RUNTIME del `apps/backend/Dockerfile.superadmin` (libglib2.0-0 -de donde viene libgobject-2.0.so.0-,
libnss3, libgbm1, libpango, libcairo2, libasound2, etc.). Sin ellas, el navegador descargado no arrancaba
en prod y los endpoints /formularios/plantilla/{id}/pdf e /img y /cotizacion/.../pdf daban 500
("Failed to launch browser! ... error while loading shared libraries: libgobject-2.0.so.0"). Entra tambien
el fix del `print` ya pusheado por otra sesion al tronco. Sin migracion.

---

## 2026-08-05 - v0.9.0: DEPLOY UNIFICADO (formulario del concepto + panel:tareas)

**Que:** deploy unificado a prod (v0.8.3 -> v0.9.0) que junta lo acumulado en el tronco desde el ultimo
deploy: (1) formulario del concepto en el detalle de la tarea -selector concepto+flujo, editable
mientras la tarea este abierta, idempotente- (eff372d, e0f23c2); (2) panel "Reporte de Sistema de
Tareas" (panel:tareas) enganchado a la Galeria via PR #3 (merge 2b5d00c), gated por el contenedor
"Reporte Tareas Personal". El fix del agente Siigo (5eab532) es del binario del agente, NO del servidor
(ya vivo por reinstalacion local 1.2.3). Sin migracion nueva. Gates locales pre-deploy: build Release +
unitarios verdes. Backup previo con ./backup.sh.

---

## 2026-08-05 - Galeria: engancha "Reporte de Sistema de Tareas" (panel:tareas), patron Panel OCS

**Que:** integracion SELECTIVA (sin mergear feat/motor-reportes, que va ~atras) del panel de tareas a
la Galeria de reportes, replicando 1:1 el patron de Panel OCS. Tres piezas:
1. Componente portado desde feat/motor-reportes (cb1fe04): `Components/Shared/Reporting/
   TareasDashboardPanel.razor` (+ `.css`). Es autonomo: mismas deps que OcsDashboardPanel
   (`IReportCatalog`, `IReportDataSource`), resuelve su contenedor por nombre ("Tareas Personal",
   tenant-safe via el catalogo). NO se porto `Pages/Reporting/PanelTareas.razor` (banco de pruebas).
2. Despacho en `ReportGallery.razor`: `PanelComponents["panel:tareas"] = typeof(TareasDashboardPanel)`
   (junto a panel:ocs y panel:system-activities).
3. Plantilla (dato, ADR-0062) en `DatabaseSeeder.EnsureReportTemplatesAsync`: ReportTemplate
   Name="Reporte de Sistema de Tareas", Kind=Panel, SourceKey=panel:tareas, RequiredSourceKind=Container,
   RequiredContainerName="Reporte Tareas Personal", IsPublished=true. Idempotente por SourceKey; se
   siembra siempre (metadato de plataforma). El gating por contenedor es generico
   (IReportActivationService/ReportTemplateCompatibility): la tarjeta se ve solo donde exista el
   contenedor; un tenant sin el no la ve. OCS y Actividades sin cambios.

**Sin migracion.** Build Release verde. El contenedor "Reporte Tareas Personal" (21.671 tareas de
db3dev sucursal 01, foto) lo ingesta la sesion de reportes en BITCODE prod (con backup), NO desde
codigo; refresco = sync programado / conector en vivo (ADR-0063). **Deploy: en el PROXIMO UNIFICADO**
(backup + OK del usuario), NO por separado. PR a fase-0/clon-backbone.

---

## 2026-08-05 - Agente Colmena: fix del fetch REST via agente (Siigo fallaba con "Host desconocido api")

**Sintoma:** el `/run` via agente del conector Siigo (AGROMETALICAS) fallaba SIEMPRE con
`REST_LIST_NET: ... Host desconocido. (api:443)`: la URL logueada era correcta (`api.siigo.com`) pero
el socket intentaba resolver el host truncado `api`. El camino server-directo (ApiImportService) con
el mismo conector funcionaba.

**Diagnostico (instrumentacion temporal en RestExecutor, ya retirada):** descarto la hipotesis del
proxy -> `DefaultProxy.GetProxy = (direct)`, no habia proxy. El token POST a `api.siigo.com/auth` SI
conectaba; solo fallaba el GET de la lista. Diferencia real con el server-directo: el AGENTE pegaba a
`https://api.siigo.com/v1/customers/` (con SLASH final) mientras el server usa `.../v1/customers` (sin
slash). El slash disparaba, ya autenticado, un redirect de Siigo a un host mal formado (`api`) que no
resuelve.

**Causa raiz + fix:** `RestExecutor.Combine`, con `ListPath` vacio, agregaba un slash final al BaseUrl
(`baseUrl.TrimEnd('/') + "/"`). Ahora, sin path relativo, devuelve el BaseUrl TAL CUAL (sin slash),
igualando al server-directo. Ademas `SharedHandler.UseProxy=false` como endurecimiento (REST directo).
Tests nuevos: `Combine_EmptyPath_KeepsBaseUrlWithoutTrailingSlash` y `SharedHandler_DoesNotUseAmbientProxy`
(31/31 verde).

**Validacion end-to-end (agente reinstalado 1.2.3, identidad DPAPI en %ProgramData% preservada, sin
tocar el secreto):** Upsert via agente -> `agent_activity_logs Fetch=Ok (ins 0, upd 1797)`; Replace
(empty->fill) -> `Fetch=Ok (del 1797, ins 1797)`, contenedor `siigo/clientes` en 1797 con anidados
poblados (siigo_id 5f4bd052... -> Ciudad=Popayan). El SERVIDOR no requiere redeploy: el fix es del
binario del agente.

---

## 2026-08-05 - Formularios de la actividad: idempotencia + editable-mientras-abierta (KeepEditable)

**Que:** ajuste al formulario del concepto en el detalle de la tarea, tras probarlo con MCP (cree T00010
de subcategoria "Cotizacion de Servicios", edite el formulario sin cerrar la tarea):
1. **Idempotencia**: `GetTaskConceptFormAsync` ya no usa `GetOrCreateDraftAsync` (que solo mira Draft y
   creaba un borrador nuevo tras cada envio). Ahora reutiliza la respuesta EXISTENTE (borrador o enviada)
   por (definicion, numero de tarea) -> UNA sola respuesta del concepto por tarea, sin duplicar ni perder
   lo guardado al reabrir.
2. **Editable mientras la tarea este abierta** (decision del usuario): nuevo `[Parameter] KeepEditable`
   en `DynamicFormRenderer` (opt-in, default false -> comportamiento identico para el resto). Con el
   activo el formulario NO se bloquea al enviar: `IsLocked = IsSubmitted && !KeepEditable` reemplaza los
   usos de bloqueo (IsDisabled, boton, autosave, reglas), el boton dice "Guardar" y hace `SaveAsync(false)`
   (guarda BORRADOR, no pasa a Submitted) porque el servidor `SaveAsync` rechaza modificar una respuesta
   ya enviada. El detalle pasa `KeepEditable="true"` al formulario del concepto; al cerrar la tarea el host
   lo pone en `ReadOnly`. Probado en prod (via tunel dev): editar telefono + Guardar -> "Borrador guardado",
   sin error, 1 sola respuesta Draft con el valor nuevo. Sin migracion. Build verde.

---

## 2026-08-05 - Formularios de la actividad en el detalle: concepto + flujo con selector

**Que:** la pestana "Formularios" del detalle de la tarea (TaskDetailModal) ahora muestra AMBAS
fuentes de formulario, elegibles con un selector: (A) el formulario por defecto que el concepto
definio para la actividad (`ActividadSubcategoria.FormDefinitionId`, 000131) y (B) los formularios que
exige el paso actual del flujo (ya existian). Al elegir uno se renderiza inline (antes el del paso
abria un modal aparte). El del concepto es editable (Fill) mientras la tarea este abierta (no
Done/Closed) y solo lectura al cerrarla; los del flujo conservan su envio (completa el paso).

**Como (sin migracion, deriva de lo existente):** nuevo `IFormResponseService.GetTaskConceptFormAsync`
(impl en FormResponseService) que, desde la subcategoria de la tarea, resuelve su `FormDefinitionId`,
exige `FormStatus.Active`, y asegura el borrador de respuesta anclado a `TaskItem.Number` (idempotente,
mismo `GetOrCreateDraftAsync` que los formularios del paso). NO crea FormFlowLink (no es paso de flujo).
DTO nuevo `TaskConceptFormDto`. En la UI: `_conceptForm` + `_selectedFormKey`, selector unificado,
render inline via DynamicFormRenderer, contador de la pestana y texto vacio actualizados; se elimino el
modal "Diligenciar" y el metodo OpenStepForm. Build SuperAdmin verde. Pendiente: validacion visual en
dev (T00001 de AGROMETALICAS debe mostrar el formulario "SIMULADOR COTIZACIONES") y decidir deploy.

---

## 2026-08-05 - v0.8.3: numero de ticket al cliente + confirmacion limpia del agente

**Que:** `crear_tarea` ahora devuelve el `ticket` (el numero legible de la tarea, `TaskItem.Number`) y
lo incluye en el mensaje. El prompt de SARA v1 (AGROMETALICAS) se ajusto para: (1) confirmar con
seguridad cuando la herramienta responde ok (NUNCA decir "inconveniente" si fue exito), (2) si
crear_tarea devuelve la lista de tableros, reintentar en silencio con el nombre exacto, y (3)
ENTREGAR el numero de ticket al cliente como comprobante (usando el valor exacto que devolvio la
herramienta, sin inventarlo). Probado en prod: SARA registro contacto + creo tarea T00009 + la asigno
(round-robin) + le dio el ticket T00009 al cliente confirmando limpio. Sin migracion. Build verde.

**Deploy:** desplegado a prod (v0.8.2 -> v0.8.3) junto con el fix de observabilidad `2870232`
(Punto 2: `agent_activity_logs` en el camino de fetch/import via agente) y `685a3b2` (fakes de
`IApplicationDbContext` que dejaban el proyecto de tests roto en Release tras Bloque A/B). Backup
previo `ecorex-2026-08-05-1155.sql.gz`. Validado post-deploy: el `/run` del conector Siigo via
agente (correlationId 73b9cd80) ya deja fila `kind=Fetch` en `agent_activity_logs` (result=Error de
red hacia Siigo, pendiente conocido de esa integracion, no del fix). Rama redundante
`fix/agente-dispatch-presencia-activitylog` (ya cherry-pickeada) eliminada del remoto.

---

## 2026-08-05 - v0.8.2: fix "el sistema se traga el texto" en cajas de texto (Blazor Server)

**Que:** Auditoria + fix del bug donde algunos inputs perdian caracteres al escribir. Causa: inputs con
round-trip POR TECLA (`@bind:event="oninput"` o el patron controlado `value=@x @oninput=`) que, bajo
latencia SignalR (amplificado por el tunel local->prod), se re-renderizan y el server reescribe el
`value=`, descartando lo tecleado. Variante extra en el chat (Patron C): un mensaje entrante del hub
re-renderiza y pisa el borrador.

- **~59 inputs de CAMPOS DE FORMULARIO** convertidos a `@bind` (commit onchange, sin round-trip por
  tecla) en 10 archivos: TerceroModal (14), TaskDetailModal (4), TaskWizard (2), GestorContactos (9),
  ContactWorkflowDesigner (7), DirectorioGeneral (4), ActivityBoardDetail (4), Agentes (4),
  TableroDetalle (10). Handlers que hacian mas que asignar -> `@bind:after="Metodo"` para conservar el
  efecto sin round-trip.
- **Buscadores/filtros: intactos** (ahi `oninput`/debounce es correcto; convertirlos romperia el
  filtrado en vivo). **Inputs con Enter-para-enviar/agregar** (tags, cc, chat): se dejan en `oninput`
  porque con onchange el valor no se confirma al presionar Enter (se enviaria vacio).
- **Chat** (`Conversaciones.razor`): se mantiene `oninput` (Enter-para-enviar funciona); el "wipe" por
  mensaje entrante queda como mejora pendiente (aislar el compositor en un subcomponente).
- **DynamicFormRenderer (cotizador) y ContenedorDatos ya estaban bien** (usan onchange); no se tocaron.
- Sin migracion (solo UI). Build verde.

---

## 2026-08-05 - v0.8.1: herramienta del agente para registrar contactos en el Directorio

**Que:** Nuevo `DirectorioToolset` con `crear_contacto`: el agente de IA registra un contacto (tercero,
perfil Cliente) en el Directorio General cuando conoce a un cliente nuevo. Idempotente por
identificacion (si ya existe, no duplica). Reusa `ITerceroService.CreateAsync`. Registrado como
IAgentToolset. Prompt de SARA v1 (AGROMETALICAS) actualizado en prod con el bloque de contacto.
Probado en prod: SARA registro "Construcciones Ospina SAS" (NIT) y creo la tarea (round-robin).

---

## 2026-08-05 - v0.8.0: el agente crea tareas en tableros (MCP) + archivos + reparto a comerciales

**Que:** El agente de IA (SARA) puede CERRAR una conversacion creando una tarea en un tablero, con los
archivos que el cliente envio adjuntos, y la tarea se reparte round-robin entre los comerciales marcados.

- **`TasksToolset`** (function calling / "MCP"): `crear_tarea` (tablero por nombre + titulo + descripcion
  + prioridad/vence) y `listar_tableros`. Reusa `ITaskItemService.CreateAsync`. Auto-provisiona un tipo
  de actividad "General" si el tenant no tiene ninguno. Registrado como IAgentToolset (lo agrega
  AiInferenceService y lo filtra por agente).
- **Archivos**: `crear_tarea` auto-adjunta a la tarea la media entrante de la conversacion + los adjuntos
  pendientes del contexto (`AiToolRunContext.PendingAttachments`). La herramienta de pruebas "Probar
  agente" gana subida de archivo (se almacena via IDocumentoFileStore -> URL -> se anexa).
- **Reparto round-robin**: nueva MARCA `Asesor.AssignableByAgent` (checkbox en /asesores) +
  `LastAgentAssignmentAt`. crear_tarea asigna la tarea al SIGUIENTE asesor elegible (activo + marcado +
  con usuario vinculado); recibe primero el que hace mas tiempo no recibe. Migracion DUAL
  `AddAsesorAgentAssignment`. Probado en prod: 3 tareas -> Julian, Lilian, Julian (cicla).
- **Fix keyring local->prod**: el dev en modo prod-tunnel comparte el keyring de DataProtection de prod
  (BD) -> descifra secretos de prod (API de IA, SMTP, credenciales). Sin esto no se podia ni probar el
  agente en local. Solo afecta al branch de dev; prod igual.
- **FormModule (`/m/{Code}`)**: boton "Ver" por fila -> abre un registro guardado en el formulario en
  solo lectura (DynamicFormRenderer Mode=ReadOnly por ResponseId).
- Prompt de SARA v1 (AGROMETALICAS) actualizado en prod con el bloque de cierre por tarea.


---

## 2026-08-04 - Deploy unificado v0.7.0

**Que:** Un solo deploy a prod con todo lo acumulado en el tronco: Asesores/Vendedor por FK (Bloque A,
ya estaba en prod v0.6.0), correo SMTP por tenant (Bloque B.1), Azure Blob global (Bloque B.2) y el
filtro por Dominio del Panel OCS (sesion ECOREX.reportes, PR #1). Migraciones aplicadas en el arranque:
AddTenantEmailConfig + AddStorageConfig (AddAsesor ya venia de v0.6.0). Build-from-git con
`network: host` fijo (evita el cuelgue de apt de chromium). Config de Azure y SMTP la pone el usuario
en la UI (cifradas), no van al repo.

---

## 2026-08-04 - Bloque B (parte 2): almacenamiento Azure Blob (global, Super Admin)

**Que:** Los archivos (documentos/adjuntos) pueden guardarse en **Azure Blob Storage** en vez del disco
local, configurable y probable desde el Super Admin. Contenedor PRIVADO; las descargas siguen pasando
por el servidor (proxy via `IDocumentoFileStore.ReadAsync`), asi que no hace falta exponer el blob.

- **Entidad global `StorageConfig`** (singleton plataforma): Provider (AzureBlob), cadena de conexion
  CIFRADA (ISecretProtector), ContainerName, IsEnabled, LastValidatedAt. **Migracion DUAL
  `AddStorageConfig`** (PG + SQL Server), probada en local.
- **`IStorageConfigService`** (interfaz en Application/Admin; impl en SuperAdmin porque usa el SDK):
  Get / Save (re-cifra la cadena solo si llega una nueva) / **TestConnection** (crea/verifica el
  contenedor con el SDK -> valida credenciales+permisos; marca LastValidatedAt).
- **`DocumentoFileStore` ahora es blob-aware**: si `StorageConfig` esta habilitado, sube al blob
  (`documentos/{tenant}/{archivo}`) y devuelve el MISMO esquema de URL `/uploads/documentos/...`; al
  leer intenta blob primero y cae a disco (los archivos viejos en disco siguen funcionando). Sin
  config, disco como siempre.
- Paquete `Azure.Storage.Blobs` 12.29.1 en Ecorex.SuperAdmin. UI: pagina `/servidor-almacenamiento`
  (PlatformOperator) con formulario + "probar conexion".
- La cadena de conexion NO va al repo: se pega en la UI del Super Admin y se guarda cifrada.
- **NO desplegado aun:** va en el deploy unificado (reportes + Asesores + SMTP por tenant + storage).

---

## 2026-08-04 - Bloque B (parte 1): correo SMTP propio por tenant (Mi cuenta)

**Que:** Cada tenant puede configurar su PROPIO servidor SMTP para enviar los correos de atencion a
clientes desde su cuenta/dominio, en vez de depender solo del correo global de plataforma.

- **Entidad `TenantEmailConfig`** (tenant-scoped, uno por tenant via indice unico): host/puerto/usuario/
  clave CIFRADA (ISecretProtector)/SSL/from/nombre/habilitado. Espeja el `EmailConfig` global pero por
  tenant. **Migracion DUAL `AddTenantEmailConfig`** (PG + SQL Server), probada en local.
- **`SmtpEmailSender` ahora es tenant-aware**: si el tenant activo tiene su config habilitada, envia con
  ella; si no (o no hay tenant en contexto, p.ej. reseteo de clave), cae al SMTP GLOBAL. El filtro
  global acota la config del tenant; ningun tenant ve/usa la de otro.
- **`ITenantEmailConfigService`** (Application/Tenancy): Get / Save (re-cifra la clave solo si llega
  una nueva) / SendTest (usa IEmailSender, marca LastValidatedAt). Registrado en DI.
- **Mi cuenta** (`Cuenta.razor`): tarjeta "Servidor de correo saliente (SMTP)" con formulario + "enviar
  correo de prueba".
- **Pendiente Bloque B parte 2:** Azure Blob global (falta cadena de conexion del usuario).
- **NO desplegado aun:** se despliega TODO JUNTO con el cierre del Motor de Reportes (sesion
  ECOREX.reportes) en un deploy unificado con backup + OK del usuario.

---

## 2026-08-04 - Bloque A: catalogo de Asesores (000074) + Vendedor asignado por FK (v0.6.0)

**Que:** Los asesores/vendedores pasan a ser una TABLA propia del tenant (antes "asesor" era un
TenantUser). El campo "Vendedor asignado" del Tercero deja de ser texto libre y se vincula al
catalogo por FK. Ademas, fix del guardado de formulario en Conceptos de actividades y botones de
accion en la parrilla del Gestor de contactos (tareas previas de la misma sesion).

- **Entidad `Asesor`** (Ecorex.Domain, TenantEntity): Nombre, Documento, Email, Telefono,
  `TenantUserId?` (link OPCIONAL a un usuario/login: "un asesor puede ser usuario o no"), IsActive.
  La gestion de logins sigue en Administracion de usuarios (000073, `/admin-usuarios`).
- **`Tercero.VendedorAsesorId`** (Guid?, FK -> Asesor, Restrict). Se conserva el texto legado
  `Vendedor` como respaldo de display cuando no hay asesor.
- **Migraciones DUALES `AddAsesor`** (Ecorex.Infrastructure / PG y Ecorex.Infrastructure.SqlServer),
  encadenadas tras AddReportTemplates. Solo agregan la tabla `asesores` + `vendedor_asesor_id` +
  FKs (Restrict) + indices. Probada en local (`ecorex_dev`) antes del deploy.
- **`IAsesorService`** (Ecorex.Application/Asesores): ListAsync (con conteo de terceros y nombre del
  usuario vinculado), ListOptionsAsync (selector), ListLinkableUsersAsync, Create/Update, y
  **DeleteAsync con GUARDA**: bloquea (Conflict) si el asesor tiene terceros vinculados, con el
  conteo en el mensaje. Registrado en DI.
- **`/asesores`** reconvertida de gestion de logins a **catalogo de asesores** (CRUD + eliminar
  guardado + vinculo opcional a usuario). **`TerceroModal`**: "Vendedor asignado" ahora es un
  selector de asesores (con hint del texto anterior). **Directorio General**: la grilla muestra el
  nombre del asesor (fallback al texto legado).
- Selects nuevos con el patron robusto `@bind` sobre propiedad string (no value+onchange+selected).
- **Siguiente (Bloque B):** correo SMTP propio POR TENANT + Azure Blob global, en Mi cuenta.

---

## 2026-08-03 - Fix guardado de formulario en Conceptos de actividades + acciones en Gestor

**Que:** (1) En Conceptos de actividades el cambio de "Formulario asociado"/"Tarea de proceso" a
veces no se guardaba: los selects usaban el anti-patron value=+@onchange+selected=; convertidos a
`@bind` sobre propiedad string (robusto, como ya hacia "Modo"). (2) La parrilla del Gestor de
contactos (`/cargador-contactos`) ahora muestra en las filas del Directorio los mismos 3 iconos que
el Directorio General: Editar, Asignar a empresa (solo personas) y Eliminar (soft-delete), via
TerceroService; las filas scrapeadas conservan su boton Promover.

---

## 2026-08-03 - "Sincronizar ahora" en la tarjeta del conector (Contenedor de Datos)

**Que:** Boton "Sincronizar ahora" en la tarjeta de cada conector REST del Contenedor de Datos
(`ContenedorDatos.razor`), junto a "Importar"/"Editar". Un clic dispara la sincronizacion sin abrir el
asistente (elegir tabla/mapeo/modo cada vez).

- **Reusa el camino existente**, no inventa uno: llama `IProcessRunner.RunNowAsync(processId)` de la
  programacion (`ImportProcess`) ligada al conector. Ahi vive la POLITICA de reconciliacion (Mode +
  KeyColumn) que el conector por si solo NO guarda; por eso el boton solo aparece cuando el conector
  tiene una programacion (`ProcessForConnector`). Aplica Mode/KeyColumn PERSISTIDOS (Upsert reconcilia,
  no duplica) y elige agente o server-direct segun la programacion (igual que "Actualizar datos").
- **Acuse honesto:** server-direct devuelve el desenlace en el acto; via agente "enviado" no es
  "cargado", asi que espera el acuse real (`Imports.TryGetOutcome`, tope de 60s solo de pantalla).
  Al terminar refresca el detalle y la bitacora de corridas. Estado por conector en `_connSync`
  (clase `dc-probe on/off`), tooltip que explica como reconcilia y quien ejecuta (`SyncTooltip`).
- **Sin migracion** (solo UI + reuso de servicios). Build SuperAdmin verde. Version v0.5.1.

**Siguiente:** validar en prod con el conector "Siigo clientes" de AGROMETALICAS (tiene programacion
via agente, Upsert). Deploy pendiente de OK del usuario + `./backup.sh`.

---

## 2026-08-03 - Plantillas de reportes reutilizables entre tenants (ADR-0062, modelo hibrido)

**Que:** Implementado el doc 06 del Motor de Reportes (ADR-0062) sobre el tronco de prod. Modelo
HIBRIDO en 3 capas: plantilla GLOBAL de plataforma + instancia tenant-scoped al activar (snapshot +
vinculo `TemplateId`, con re-sincronizacion) + reporte propio del tenant (`TemplateId=null`, sin
cambios). Compartir la plantilla NUNCA filtra datos: la instancia corre via `IReportDataSource` con el
filtro global (aislamiento fail-closed).

- **Entidad `ReportTemplate`** (Ecorex.Domain): GLOBAL, hereda de `BaseEntity` (NO `ITenantScoped`),
  mismo patron que `PlatformUser`/`SaasPlan` dentro de `EcorexDbContext` (DbSet global, sin query
  filter). Campos: Name, Description, Kind (Dashboard|Printable|Panel), SourceKey, SpecJson?, Rdl?,
  RequiredSourceKind (Native|Container), RequiredContainerName?, Category, Icon, IsPublished + auditoria.
- **Columna `TemplateId`** (uuid?, null = reporte propio) en `report_definitions`, indice
  `(TenantId, TemplateId)`, sin FK dura (la plantilla es global; el reporte es tenant-scoped).
- **Migraciones DUALES** `AddReportTemplates`: `Ecorex.Infrastructure` (EcorexDbContext / PG) y
  `Ecorex.Infrastructure.SqlServer` (SqlServerEcorexDbContext), encadenadas tras AddContactWorkflowRuns.
  Cada una crea `report_templates` + agrega `template_id`; nada mas.
- **`IReportActivationService`** (nuevo, Application): `ActivateTemplateAsync` (valida compatibilidad de
  fuente, snapshot + vinculo, idempotente/reactiva), `DeactivateTemplateAsync` (archiva la instancia sin
  tocar la plantilla), `ResyncFromTemplateAsync` (re-copia el molde SIN perder `report_definition_roles`),
  `ActivateCompatibleAsync(includeNative)` (barrido) y `ListActivatableAsync`. Compatibilidad: Native ->
  siempre OK; Container -> OK solo si el tenant tiene un contenedor raiz cuyo nombre coincide con
  RequiredContainerName (via el DbContext tenant-scoped), si no rechaza con mensaje claro.
- **`IReportTemplateService`** (nuevo): listar publicadas, CRUD (solo PlatformAdmin, AUDITADO con
  `IAuditWriter`/AdminAuditLog en la transaccion) y `GetActivatableForTenantAsync`.
- **`CreateExampleReportsAsync` refactorizado** a template-based: ya no hardcodea `panel:ocs` ni
  `panel:system-activities`; activa todas las plantillas publicadas compatibles (Panel OCS aparece solo
  donde hay contenedor OCS) y solo mantiene 3 dashboards nativos de ejemplo del tenant.
- **Seed de las 2 plantillas base** (`DatabaseSeeder.EnsureReportTemplatesAsync`, idempotente por
  SourceKey, corre SIEMPRE como metadato de plataforma igual que el catalogo de ciudades): Panel de
  Actividades (Native) y Panel OCS (Container, "Software OCS"). Cableado en `Program.cs` (rama prod y dev).
- **PlatformAdmin**: pagina `/plantillas-reportes` (policy `PlatformOperator`, auditada) para
  crear/editar/publicar/despublicar + NavLink en "Super Admin SaaS". Auto-activacion en `ReportGallery`
  (al entrar activa las de contenedor compatibles).
- **Pruebas**: `ReportActivationTests` dual (PG + SQL Server), 8 casos en verde (aislamiento
  cross-tenant, idempotencia, compatibilidad de contenedor, barrido, resync conservando roles).
  `dotnet build` verde; 618 unit + 34 Report* integration en verde. ADR-0062 -> ACEPTADA.

**Siguiente:** (opcional) pagina de activacion en `/reportes/admin` para el tenant (ver activables y
activar/desactivar). NO desplegado, NO commiteado.

## 2026-08-03 - Panel OCS integrado al Motor de Reportes (galeria)

**Que:** Se integro el "Panel OCS - Inventario de software" a la galeria de reportes del tronco SIN
mergear la rama `feat/motor-reportes` (esta 3 adelante / 52 ATRAS: un merge REGRESARIA el motor). Se
trajeron por checkout SELECTIVO (ADD limpios, el tronco no los tenia) solo 3 archivos:
`Components/Shared/Reporting/OcsDashboardPanel.razor` (+`.css`) y
`docs/decisiones/ADR-0062-catalogo-de-reportes-reutilizables-entre-tenants.md`. NO se trajo el banco de
pruebas `Pages/Reporting/PanelOcs.razor`. ADR-0062 queda presente pero NO implementado (es otra tarea).

**Compatibilidad:** `OcsDashboardPanel` compilo contra el motor del tronco SIN ajustes: usa las mismas
firmas que `ActivitiesDashboardPanel` (`IReportCatalog.GetSourcesAsync`, `ReportSourceDescriptor`
`.Kind`/`.DisplayName`/`.Key`/`.Fields`, `ReportSourceKind.Container`, `IReportDataSource.QueryAsync`,
`ReportFilter.Eq` + `ReportFilterOperator.Contains`, `ReportContext`, componente `<EChart>`). Resuelve
el contenedor por nombre ("OCS"), asi es portable a cualquier tenant que tenga cargado el inventario.

**Despacho por SourceKey:** en `Components/Pages/Reporting/ReportGallery.razor`, el bloque
`@if (_isPanel)` ya no fija `<ActivitiesDashboardPanel />`; ahora usa un resolver
`SourceKey -> Type` (diccionario `PanelComponents` + `ResolvePanelType`) renderizado con
`<DynamicComponent>`: `panel:ocs` -> `OcsDashboardPanel`, `panel:system-activities` -> el de
actividades, y cualquier panel desconocido cae al de actividades por defecto. Agregar un panel nuevo es
una entrada en el diccionario, no tocar el markup. El `@using ...Shared.Reporting` ya estaba.

**Siembra de la tarjeta:** se EXTENDIO `ReportDefinitionService.CreateExampleReportsAsync()` (no se toco
el resto del motor ni la gobernanza). Tras sembrar los ejemplos base, si el tenant tiene un contenedor
RAIZ cuyo nombre contiene "OCS" (deteccion via `_db.DataContainers`, tenant-safe por el filtro global,
`ToUpper().Contains` para ser provider-agnostico), crea idempotentemente la tarjeta
`Name="Panel OCS - Inventario de software"`, `SourceKey="panel:ocs"`, `Kind=Dashboard`, `Status=Active`
(via `SaveAsync`, igual que el panel de actividades). Un tenant sin contenedor OCS no obtiene la tarjeta.
Una tarjeta sin roles asignados es visible para todos (gobernanza intacta).

**Resultado:** `dotnet build Ecorex.sln` VERDE (0 errores, 27 warnings preexistentes). SIN migracion.
NO se toco el motor de consulta/despacho ni la gobernanza por roles. NO se desplego ni commiteo.

---

## 2026-08-03 - Despacho RestApi via agente Colmena en la Config API (ADR-0061)

**Que:** Follow-up B de ADR-0059/0060. Se RE-HABILITO como OPCION el camino RestApi-via-agente que
ADR-0060 habia eliminado (dejandolo solo server-direct), pero ahora armando el `RestFetchSpec`
COMPLETO (version post-TokenExchange): baseUrl + arrayPath + paging + fields ANIDADOS + Headers
estaticos (Partner-Id) + TokenExchange (login de 2 pasos). Motiva: un RestApi solo alcanzable desde la
LAN del cliente (caso Siigo AGROMETALICAS via agente `cli_...`). NO hubo migracion (`ImportProcess.ClientId`
ya existia).

**Runner:** `ProcessRunner.RunNowAsync` ramifica el RestApi por presencia de cliente: con
`process.ClientId` -> nuevo `RunRestViaAgentAsync` (arma spec completo, descifra el secreto del login
que viaja en `ConnectorSpec.Secret`, despacha por el hub; simetrico al camino Database: abre corrida
antes de despachar, PendingOffline + parquea si el agente esta caido); sin `ClientId` -> server-direct
(ADR-0060). El via-agente honra el Mode/KeyColumn PERSISTIDOS (no vuelve al Replace fijo de la vieja
rama).

**RestSpecBuilder:** el viejo `BuildRestSpec` (privado, borrado en ADR-0060) se restaura como clase
publica `Ecorex.SuperAdmin.Agents.RestSpecBuilder`, unit-testeable. Reusa `connector.MappingJson`
(mismo `RestFetchSpec` que lee `ConnectorRunPlanner`) + `HeadersJson`/`TokenExchangeJson` (via
`ConnectorRestConfig`). NO duplica el modelo de mapeo.

**Ingesta:** identica por agente. Los chunks (`FetchResult`) pasan por el MISMO `RowIngestService`;
`DispatchFetchAsync` recibe `mode`+`keyColumnId` del proceso. Convencion del agente: `mapping`
columnaId -> NOMBRE de columna (el agente ya aplico el mapeo campo->columna, filas indexadas por
nombre). Upsert por "Siigo Id" reconcilia sin duplicar.

**Endpoints** (`ConfigApiEndpoints`, tenant-scoped, auditados): `PUT .../schedule` y `POST .../run`
aceptan `clientId`/`agent` OPCIONAL. `ResolveClientAsync` lo resuelve por Guid de fila, ClientId
publico (`cli_...`) o nombre (404 si no existe). En `/schedule` se guarda como `ImportProcess.ClientId`;
en `/run` (RestApi) despacha por el hub y devuelve 202 con `correlationId` (`status="dispatched"`), 409
si el agente esta offline. `ScheduleView` y auditoria reflejan el `clientId`/`clientName`.

**Resultado:** `dotnet build Ecorex.sln` VERDE (0 errores). Tests: SuperAdmin.Tests 63/63 (nuevo
`AgentRestSpecBuilderTests`: spec completo + casos borde), Application.Tests 615/615 (nuevo caso de
ingest via agente en `ScheduledUpsertRunTests`). Solo ASCII. NO se desplego ni commiteo. NO se toco
ContactWorkflow* (feature en curso en otro worktree).

**Siguiente:** cablear la UI de Contenedores para elegir "server-direct vs agente" en la programacion;
regenerar/redistribuir el MSI del agente si cambia el contrato del `RestExecutor`.

---

## 2026-08-03 - Motor de ejecucion del disenador de acciones por filtro (ADR-0056 Fase 2)

**Que:** Fase 2 del "disenador de acciones por filtro de contactos" (000740): el MOTOR que ejecuta la
secuencia de pasos de cada `ContactWorkflow` activo sobre el segmento de contactos que define su
`TerceroFiltro`. Fase 1 (entidades + UI + persistencia) ya existia; esta sesion agrega ejecucion.

**Entidad + migracion:** nueva `ContactWorkflowRun` (Domain, tenant-scoped): bitacora de un disparo por
(paso, ventana, contacto) con `Status` (`ContactWorkflowRunStatus` Pending/Sent/Failed/Skipped),
`WindowDate`, `Channel`, `ExternalRef`, `Error`. La FK dura es SOLO al paso (cascada); ventana y contacto
son Guid planos (para no bloquear el reemplazo fisico de pasos/ventanas al re-guardar el disenador ni
arrastrar cascadas multiples en SQL Server). Indice UNICO de dedupe
`(TenantId, StepId, ScheduleId, TerceroId, WindowDate)`. Migracion DUAL `AddContactWorkflowRuns`:
PG `20260803202937` + SQL Server `20260803203028`, encadenada tras `AddImportProcessRunMode`. DbSet en
`IApplicationDbContext`/`EcorexDbContext`.

**Dispatcher + worker:** `IContactWorkflowDispatcher`/`ContactWorkflowDispatcher`
(`Ecorex.Application/Gestor`) + `ContactWorkflowWorker` (`Ecorex.SuperAdmin/RealTime`, hosted service
registrado en Program.cs, barrido 1 min). Mismo patron que `ScheduledJobWorker`/`ImportSchedulerWorker`:
barrido cross-tenant SOLO ids (IgnoreQueryFilters) -> `AmbientTenantContext.Begin` -> ejecucion acotada.

**Dedupe/ventana/rate:** la "ventana" del dedupe es `(ScheduleId + WindowDate)` = un contacto recibe un
paso a lo sumo UNA vez por dia por ventana; re-correr el mismo dia NO reenvia. Ventana de horario evaluada
en la zona del tenant (rango de fechas + ActiveDays + franja StartTime/EndTime, con soporte de franja
nocturna). `PackageSize` = tope por corrida (default 50, techo duro 500); `RepeatEvery` = minutos minimos
entre corridas de la misma ventana. Segmento evaluado EN VIVO con `ContactFilterEvaluator` (extraido de
`GestorContactosService` para una sola logica de filtrado).

**Mapeo real de las 5 acciones:** WhatsApp -> `IWhatsAppConnectorService.SendTestAsync` (linea de AccountId
o primera conectada; remoteJid de una Conversation previa, soporta LID); Email -> `IEmailSender.SendAsync`;
Llamada -> `ITaskItemService.CreateAsync` (ParamsJson: Subcategoria->SubcategoriaId puente Concepto->Tarea,
Comercial->assignee, Prioridad->TaskPriority); Conectar -> paso no-envio (Sent). **MensajeRed -> Skipped
documentado** (no hay canal para INICIAR salida de redes; el dispatcher del agente solo RESPONDE entrantes;
se resuelve en Fase 3). Contactos sin el dato requerido (telefono/correo/subcategoria) -> Skipped con motivo,
sin frenar la corrida. Banner del disenador cambiado a "Motor programado".

**Resultado:** `dotnet build Ecorex.sln` VERDE (0 errores). Tests: Application.Tests 617/617 (nuevos:
`ContactWorkflowDispatcherTests` -> corre una vez sobre 2 contactos con dedupe al re-correr, y respeta
ventana/dia inactivo). Solo ASCII. Sin desplegar ni commitear (lo hace el orquestador).

**Siguiente:** Fase 3 (plantillas y cuentas reales de mensajeria; canal de MensajeRed); flag de opt-out por
Tercero antes de uso masivo en prod.

---

## 2026-08-03 - Scheduling en la Config API + corrida programada server-direct (Upsert) (ADR-0060)

**Que:** Fase 2 del scheduling de conectores. Se expuso el scheduling por HTTP en la Config API y se
hizo que la corrida PROGRAMADA de un conector RestApi use el MISMO camino que el `/run` manual
(`ConnectorRunPlanner` -> `ApiImportService`), reconciliando por Upsert en vez de duplicar.

**Persistencia del modo:** `ImportProcess` (Domain) gana `Mode` (enum `ImportRunMode`
Append/Replace/Upsert, espejo exacto de `ApiImportMode` de Application; se castea en el borde porque
Domain no referencia Application) y `KeyColumn` (string?). Antes el disparo iba fijo en `Replace`;
ahora persiste la politica. Default = Replace (comportamiento historico). Se guarda como string
(`nvarchar/varchar(16)`, `HasConversion<string>()` + `ValueGeneratedNever()` para no caer en la
sustitucion del default cuando el valor es Append=0). Propagado por `SaveImportProcessRequest` e
`ImportProcessDto`; `SaveProcessAsync`/`MapProcess` mapean con el cast.

**Migracion DUAL** `AddImportProcessRunMode`, encadenada tras `AddContactWorkflows`:
PG `20260803173153` (Ecorex.Infrastructure) y SQL Server `20260803173210`
(Ecorex.Infrastructure.SqlServer). Agrega `mode` (default "Replace", rellena filas existentes) y
`key_column` (nullable). Sin otros cambios de esquema.

**Disparo del scheduler:** `ProcessRunner.RunNowAsync` (compartido por el boton "Actualizar datos" y
el scheduler) ahora ramifica por tipo de conector. **RestApi -> server-direct**: nuevo
`RunRestServerDirectAsync` arma el plan con `ConnectorRunPlanner.Build(..., (ApiImportMode)process.Mode,
process.KeyColumn)` y ejecuta `IApiImportService.ImportAsync` (sin agente; token-exchange y headers ya
viven en ApiImportService). Deja/cierra corrida en la bitacora sincronicamente. **Database -> via
agente** (sin cambio de mecanismo) pero honrando el Mode/KeyColumn persistidos. Se elimino la rama
RestApi-via-agente y el helper `BuildRestSpec`. `ProcessRunner` ahora inyecta `IApiImportService`.

**Endpoints** (`ConfigApiEndpoints`, tenant-scoped por Bearer, auditados, OpenAPI): `PUT/GET/DELETE
/api/config/connectors/{id}/schedule` (upsert por conector, estado, borrado) y `GET
/api/config/connectors/{id}/runs?take=N` (bitacora). El PUT valida el cron con el MISMO parser Cronos
(via `SaveProcessAsync`) y devuelve 400 si es invalido, sin activar la programacion.

**Resultado:** `dotnet build Ecorex.sln` VERDE (0 errores). Tests: Application.Tests 614/614 (incluye
`ScheduledUpsertRunTests` nuevo: alineacion del cast, el planner usa Mode/KeyColumn persistidos, y la
corrida Upsert reconcilia sin duplicar), SuperAdmin.Tests 60/60. Solo ASCII. No se desplego ni
commiteo. NO se toco la feature en curso de ContactWorkflow (disenador de Acciones).

**Siguiente:** cablear la UI de Contenedores para editar Mode/KeyColumn de la programacion; opcional
follow-up B de ADR-0059 (despacho al agente cuando el RestApi solo sea alcanzable desde la LAN).

---

## 2026-08-03 - Disenador de acciones por filtro de contactos - FASE 1 (ADR-0056)

**Que:** Se implemento la Fase 1 del "disenador de acciones por filtro de contactos" (port del
`ucWorkflowDesigner` legacy) segun ADR-0056: modelo + UI + persistencia atada al filtro. NO se
implemento el motor de ejecucion (es Fase 2). Entidades tenant-scoped nuevas: `ContactWorkflow`
(FK 1:1 con `TerceroFiltro`, indice unico `(TenantId, TerceroFiltroId)`, `Version` de concurrencia
optimista), `ContactWorkflowStep` (StepType enum Conectar/MensajeRed/WhatsApp/Email/Llamada, Label,
Orden, ParamsJson jsonb/nvarchar(max) solo para el tipo Llamada) y `ContactWorkflowSchedule`
(ventanas: StartDate/EndDate DateOnly?, StartTime/EndTime TimeOnly, ActiveDays "1,2,3,4,5",
TemplateId?, AccountId?, RepeatEvery?, PackageSize?). Cascada Workflow->Steps->Schedules; el vinculo
al filtro es Restrict. Query filter global por reflexion (ITenantScoped) aplica el aislamiento.

**Migracion DUAL** encadenada tras la ultima (`AddTenantApiTokens`): PG
`20260803164008_AddContactWorkflows` (Ecorex.Infrastructure) y SQL Server
`20260803164054_AddContactWorkflows` (Ecorex.Infrastructure.SqlServer).

**Servicio:** `IContactWorkflowService` + `ContactWorkflowService` (Tenancy) con `GetByFiltroAsync`
y `SaveAsync` (upsert que REEMPLAZA pasos+ventanas en un solo SaveChanges; ParamsJson serializa los
campos CRM del paso Llamada). Guardado auditado con `IAuditWriter` (actor = ITenantContext.UserId).
DTOs en `ContactWorkflowDtos.cs`. Registrado en DI (`DependencyInjection.cs`).

**UI:** nueva opcion **"Acciones"** en el menu "..." de cada filtro guardado
(`GestorContactos.razor`, junto a Filtrar ahora/Eliminar) que abre el componente nuevo
`Components/Shared/ContactWorkflowDesigner.razor` (+ `.razor.css`): modal con paleta de las 5
acciones (colores/iconos del legacy), lista secuencial de pasos con reordenar/editar/quitar, campos
CRM visibles solo para Llamada, y N ventanas de horario por paso (chips de dias, horas, vigencia,
repetir cada, tam. paquete). El drag-and-drop quedo resuelto por **botones** ("+" en la paleta y
flechas subir/bajar), que el ADR admite para Fase 1. Un banner deja claro que la EJECUCION es Fase 2.

**Resultado:** `dotnet build Ecorex.sln` VERDE (0 errores). Se agregaron los 3 DbSets a los fakes
`FakeAppDb` de las pruebas (RowIngest/TenantUser). Solo ASCII. No se desplego ni se commiteo.

**Siguiente (Fase 2):** motor de ejecucion + scheduler (`ContactWorkflowRun` con indice unico de
idempotencia, `IContactWorkflowDispatcher` enganchado al patron `ScheduledJobWorker`, resolucion del
segmento del filtro, ventanas/dedupe/rate limiting y cableado a los 5 servicios de ejecucion).

---

## 2026-08-03 - Fix /run: resolver JSON anidado en el importador in-process + preview con valores (ADR-0059)

**Que:** El `/run` de la Config API (`ConnectorRunPlanner` -> `ApiImportService.ImportAsync`) resolvia
el mapeo columna->campo con `TryGetProperty` (solo primer nivel), asi que las rutas ANIDADAS/INDEXADAS
del conector (`id_type.name`, `name[0]`, `address.city.city_name`, `phones[0].number`,
`contacts[0].email`, `metadata.created`) quedaban VACIAS y, en Upsert, sobrescribian la data existente.
Repro: conector Siigo `019fc83c-5876-744c-a717-9a448da0b281` (AGROMETALICAS).

**Fix A:** nuevo helper `NestedJsonResolver` en `Ecorex.Application` (TryResolve + Scalar + ParseSegments
+ ProjectRow), REPLICA byte-a-byte de la logica del agente (`Ecorex.Agent.Core.Services.RestJson`, que
usa `RestExecutor`). Se replica -no se comparte por referencia- porque el agente esta en otra solucion
(`apps/agent`), apunta a net10.0-windows (DPAPI) y no esta en `apps/backend/Ecorex.sln`; y Application es
net10.0 multiplataforma (matriz dual de CI). `ImportAsync` ahora proyecta con `ProjectRow`.
**No-sobrescribir-con-vacio:** `ProjectRow` OMITE las rutas que no resuelven, y `RowIngestService`
(sesion Upsert, fila existente) SALTA los campos ausentes (`if (!src.ContainsKey(field)) continue;`),
conservando el valor. Distincion: ruta que resuelve a JSON null SI limpia la celda (viaja con valor null).
El runner via agente no cambia (su MergeRow siempre incluye todas las columnas).

**Preview:** nuevo `POST /api/config/connectors/{id}/preview` (mismo Bearer + tenant-scoping): para la
primera fila de muestra devuelve el mapeo YA aplicado columna->valor con el resolver anidado e indica por
columna si `resolved`. Respaldo: `IApiImportService.PreviewAsync` + `ApiPreviewResult`/`ApiPreviewField`.
El endpoint reusa `ConnectorRunPlanner.Build` para el mapeo persistido.

**Archivos:** `NestedJsonResolver.cs` (nuevo), `ApiImportService.cs` (ProjectRow en ImportAsync +
PreviewAsync; se quito `ScalarString` muerto), `ApiImportContracts.cs` (preview contracts + PreviewAsync),
`RowIngestService.cs` (guard Upsert), `ConnectorRunPlanner.cs` (nota), `ConfigApiEndpoints.cs` (endpoint
preview). Tests: `NestedJsonResolverTests.cs` (nuevo, fixture Siigo: a.b, arr[0], arr[0].x, ausente,
json-null) y 2 casos nuevos en `RowIngestServiceTests` (Upsert no borra con ruta ausente; presente-null
si limpia).

**(B) Despachar /run al agente Colmena conectado:** DISENADO en ADR-0059 (no implementado por
alcance/riesgo): si el tenant tiene agente activo, construir `RestFetchSpec` y
`IAgentImportService.DispatchFetchAsync`; fallback server-direct; y unificar el resolver en un leaf
`Ecorex.Shared` referenciado por ambas soluciones.

**Build/tests:** `dotnet build apps/backend/Ecorex.sln` VERDE (0 errores, 27 warnings preexistentes).
`Ecorex.Application.Tests` VERDE (resolver + RowIngest 24/24; ConfigApiTests 10/10). NO desplegado, NO
commiteado.

---

## 2026-08-03 - API REST de configuracion tenant-scoped (Contenedores / Conectores / Agentes) - FASE 1

**Que:** API DELGADA bajo `/api/config` para configurar por HTTP, sin la UI Blazor, toda la maquinaria
del Contenedor de datos: Contenedores (lectura), Conectores REST (CRUD completo: TokenExchange 2 pasos,
headers arbitrarios tipo `Partner-Id`, paginacion, mapeo campo->columna, Append/Replace/Upsert, secreto
cifrado, probe, run + estado) y Agentes Colmena (list + register). Caso guia: dejar operable el conector
Siigo de `siigo/clientes` (AGROMETALICAS) de punta a punta. Va en `Ecorex.SuperAdmin` (app de prod;
`Ecorex.Api` no se despliega), patron `AgentMgmtEndpoints` (metodo de extension + `Program.cs`).

**Auth (per-tenant, NUNCA cross-tenant):** entidad nueva `TenantApiToken` (tenant-scoped: `TokenHash`
SHA-256, `RevokedAt?`, `LastUsedAt?`). El `Authorization: Bearer <token>` se hashea, se busca el token
activo (con `IgnoreQueryFilters`, unica lectura sin tenant, por hash opaco), su `TenantId` se fija con
`AmbientTenantContext.Begin` en scope de DI aislado. Emision gateada por COOKIE de admin del tenant
(claim `tenant_id` + `tenant_role` Owner/Admin): `POST /tokens` devuelve el valor en claro UNA vez.
Auditoria de toda mutacion en `super_admin_audit_logs` (`reason=config-api`, actor System).

**Endpoints:** tokens (`POST/GET /tokens`, `POST /tokens/{id}/revoke`); contenedores (`GET /containers`,
`GET /containers/{id}`); conectores (`GET /connectors[?model=]`, `GET/PUT/DELETE /connectors/{id}`,
`POST /connectors` upsert por nombre, `PUT /connectors/{id}/secret`, `POST /connectors/{id}/probe`,
`POST /connectors/{id}/run`, `GET /runs/{id}`); agentes (`GET /agents`, `POST /agents`).

**Reuso:** `IDataModelService` (contenedores), `IDataImportConfigService` (conectores),
`IApiImportService` (probe + run server-direct), `IAgentClientService` (agentes),
`ConnectorRestConfig`/`TokenExchangeConfig`/`ConnectorHeader` (modelo REST). Helpers nuevos y puros:
`ApiTokenHasher` y `ConnectorRunPlanner` (traduce el `MappingJson` persistido -> `ApiImportRequest`);
`ConfigRunStore` (estado de corridas en memoria). OpenAPI en `/openapi/v1.json` (built-in).

**Migracion:** dual `AddTenantApiTokens` (PG + SQL Server), encadenada tras `AddCiudadCatalog`.

**Tests:** `ConfigApiTests` (10, VERDE): determinismo/entropia del hasher y el planeador de corrida
(upsert por `siigo_id`, paginacion page/page_size, mapeo, errores). Integracion dual: TODO FASE 2.

**Build:** `dotnet build apps/backend/Ecorex.sln` VERDE (0 errores). NO desplegado, NO commiteado.

**Archivos:** nuevos `Ecorex.Domain/Entities/TenantApiToken.cs`, `Ecorex.Application/Common/ApiTokenHasher.cs`,
`Ecorex.Application/DataContainers/ConnectorRunPlanner.cs`, `Ecorex.SuperAdmin/Endpoints/ConfigApiEndpoints.cs`
+ `ConfigRunStore.cs`, migraciones dual `AddTenantApiTokens`, `tests/.../ConfigApiTests.cs`,
`docs/decisiones/ADR-0058-api-configuracion-tenant-scoped.md`. Modificados: `IApplicationDbContext`,
`EcorexDbContext` (DbSet + config), `Program.cs` (registro + mapeo + OpenAPI), csproj de SuperAdmin
(`Microsoft.AspNetCore.OpenApi`), 2 fakes de test.

**Siguiente (FASE 2):** escritura de contenedores por HTTP; rotate/revoke de agentes; ejecucion via
AGENTE + rutas anidadas/fan-out (el importador server-direct mapea por propiedad plana); corridas
persistentes/asincronas; gate de habilitacion + allowlist de IP + antiforgery/CORS endurecidos.

---

## 2026-08-03 - API REST de gestion de agentes de IA (gobierno cross-tenant)

**Que:** API de gobierno bajo `/api/mgmt` para que un operador externo (Claude via WebFetch)
LEA y EDITE la estructura de los agentes de IA de cualquier tenant y LEA sus bitacoras, sin la
cookie del panel. Minimal API en un archivo nuevo con metodo de extension, montado en `Program.cs`.

**Endpoints:** GET `/api/mgmt/agents`, GET `/api/mgmt/agents/{id}`, POST `/api/mgmt/agents`,
PUT `/api/mgmt/agents/{id}`, PUT `/api/mgmt/agents/{id}/prompt` (solo system prompt),
POST `/api/mgmt/agents/{id}/prompts`, PUT `/api/mgmt/prompts/{promptId}`,
DELETE `/api/mgmt/prompts/{promptId}`, GET `/api/mgmt/agents/{id}/bitacora?kind=&limit=`.
Todos con `?tenant={guid}` obligatorio. Mapean a `IAiAgentService`/`IAiAgentCacheService` y a
lectura directa de `ai_agent_run_logs`.

**Auth:** header `X-Ecorex-Mgmt-Key` validado en tiempo constante contra la env var
`ECOREX_MGMT_API_KEY`. Sin la env var -> 404 (API deshabilitada, no se revela). Header ausente o
malo -> 401. Tenant ausente/invalido -> 400. Actor de auditoria = `Guid.Empty`.

**Tenant scoping:** se reusa `AmbientTenantContext.Begin(tenant)` + scope de DI aislado, igual que
el webhook entrante (`AgentReplyDispatcher`). SIN migracion (entidades existentes).

**Archivos:** nuevo `apps/backend/src/Ecorex.SuperAdmin/Endpoints/AgentMgmtEndpoints.cs`; modificado
`Program.cs` (llamada a `MapAgentMgmtEndpoints`); nuevo `docs/decisiones/ADR-0057-api-gestion-agentes.md`.

**Build:** `dotnet build apps/backend/Ecorex.sln` VERDE (0 errores).

**Activacion:** setear `ECOREX_MGMT_API_KEY` (secreto) en el `.env` de prod, NO versionada.

**Siguiente:** exponer opcionalmente datos cache por sesion e historial de versiones de prompts
si el operador externo lo requiere.

---

## 2026-08-01 - Conector RestApi: headers arbitrarios + intercambio de token (auth 2 pasos) + UI estructurada

**Que:** se completo el conector RestApi del Contenedor de datos (TENANT-scoped, policy
`Perm:contenedor-datos:View`) con lo que faltaba: (1) headers HTTP estaticos arbitrarios (ej.
`Partner-Id`), (2) auth de 2 pasos `TokenExchange` (login -> `access_token` -> `Authorization: Bearer`
+ headers), (3) UI estructurada para ArrayPath + paginacion + mapeo columna<-campo que reemplaza a la
textarea de JSON crudo (con modo "JSON avanzado" colapsable como respaldo), y (4) boton "Probar"
autonomo del conector. Caso guia Siigo, pero TODO configurable por el usuario del cliente; nada
hardcodeado. Sigue siendo funcionalidad del tenant, NO del PlatformAdmin.

**Cambios (5 capas):**
- **Dominio + migracion dual:** `ConnectorAuthKind.TokenExchange`; `DataConnector.HeadersJson` y
  `DataConnector.TokenExchangeJson` (JSON no secretos); el SECRETO del login reutiliza
  `CredentialsEncrypted` (cifrado). Config EF en `EcorexDbContext` (jsonColumnType, compartida con SQL
  Server). Migracion **dual** `AddConnectorHeadersAndTokenExchange` (PG `20260801020015`, `jsonb`; SQL
  Server `20260801...`, `nvarchar(max)`), 2 AddColumn snake_case sin indices, **encadenada tras
  `AddFormContainerInlineLabels`** (feature de formularios, NO tocada). Snapshot verificado con AMBAS
  columnas + `inline_labels`.
- **Motor in-process (`ApiImportService`):** aplica headers en toda request; implementa el intercambio
  de token (login una vez, extrae token por ruta JSON, lo cachea por corrida, lo aplica como header);
  `ApplyAuth` intacto para None/ApiKey/Bearer/Basic; `tokenUrl` con el mismo anti-SSRF que el endpoint.
  Helper compartido `ConnectorRestConfig` (parse/serialize de headers + token exchange).
- **Contrato del agente (`RestFetchSpec`) + ejecutor:** `RestHeader` y `RestTokenExchangeSpec` nuevos;
  `RestFetchSpec.Headers`/`TokenExchange` opcionales al final (compat hacia atras). `RestExecutor`
  (agente) aplica headers + resuelve el token una vez antes del fetch (lista y detalle). El secreto
  viaja en `ConnectorSpec.Secret` (ADR-0040), nunca en el spec. `ProcessRunner.BuildRestSpec` puebla
  Headers/TokenExchange desde las columnas del conector.
- **CRUD (`DataImportConfigService` + contratos):** `HeadersJson`/`TokenExchangeJson` en DTO/Request;
  Save los persiste (y limpia para Excel/Database); tenant-scoping intacto.
- **UI (`ContenedorDatos.razor`):** opcion de auth "Intercambio de token" con su formulario; seccion
  de Headers con filas repetibles; mapeo estructurado (ArrayPath + paginacion + columna<-campo con
  probe real y datalist de campos) que lee/escribe el mismo `MappingJson` (RestFetchSpec), preservando
  `Fanout`; modo "JSON avanzado" colapsable; boton "Probar" autonomo (fetch real via `ApiImportService`
  que muestra ok/error, nro de registros y muestra). Todo bajo la policy tenant actual.

**Estado:** `dotnet build apps/backend/Ecorex.sln` **verde** (0 errores, 22 warnings preexistentes).
Agente: `Ecorex.Contracts.Agent` + `Ecorex.Agent.Core` compilan; **29/29** tests del ejecutor REST
verdes. Sin deploy, sin commit (por indicacion). Migraciones generadas, NO aplicadas.

**MSI del agente:** el instalador MSI del agente Colmena (ADR-0049) **debe regenerarse y
redistribuirse manualmente** para que los agentes ya instalados entiendan los campos
`Headers`/`TokenExchange` del `RestFetchSpec`. NO se ejecuto `build-installer.ps1` (paso manual del
operador). Los agentes viejos siguen atendiendo specs sin esos campos igual que antes.

**Siguiente:** aplicar migracion en el entorno; validar en la consola del tenant con un conector real
(Siigo); regenerar/redistribuir el MSI cuando se indique.

**Decisiones:** ADR-0054.

---

## 2026-08-01 - Etiquetas en linea por contenedor de formulario (label al frente del valor)

**Que:** nueva opcion config-driven **"Etiquetas en linea"** por contenedor de formulario. Hoy cada
campo pinta su label ARRIBA del control; con esta opcion el label va al frente del valor (misma
linea: label fijo ~150px a la derecha + control llenando el resto). Sirve para compactar bloques
tipo "Totales" de cotizacion. NO hardcodeado: es una **propiedad de contenedor** (toggle en el
disenador), activable en cualquier contenedor Row/Col de cualquier formulario. Default = actual
(label arriba).

**Cambios (7 partes):**
- `FormContainer.InlineLabels` (bool) en el dominio; mapea por convencion (como `IsLocked`/`IsHidden`),
  sin config EF explicita.
- `FormContainerDto` + `SaveFormContainerRequest`: nuevo `InlineLabels = false`; propagado en
  `FormDefinitionService` (Create/Update/`ToDto`).
- `FormDesigner.razor`: toggle "Etiquetas en linea" en el panel de propiedades (solo grupos Row/Col),
  junto a "Fijo"/"Oculto"; mapeo en `ToRequest`.
- `DynamicFormRenderer.razor`: emite la clase `dfr-inline` en el `div.dfr-group` del contenedor cuando
  `InlineLabels == true` (helper `GroupCssClass`).
- `DynamicFormRenderer.razor.css`: reglas `.dfr-inline ::deep ...` (flex, label 150px a la derecha,
  control llena el resto, caption/ayuda/error caen abajo con flex-wrap; en <=640px vuelve a
  label-arriba).
- Migracion **dual** `AddFormContainerInlineLabels` (PG `20260801013455`, columna `inline_labels`
  `boolean`; SQL Server `20260801013623`, `bit`), default false, sin indices, patron de
  `AddFormCardLayout`.
- ADR-0053.

**Estado:** `dotnet build Ecorex.sln` **verde** (0 errores). Sin deploy (por indicacion). Las columnas
nuevas se aplican cuando corra la migracion en el entorno.

**Siguiente:** validar visualmente en la vista previa del disenador; commit/push al tronco cuando se
indique.

---

## 2026-07-31 - Port de REACCIONES del agente (desde CUBOT.redmanager)

**Que:** portada la funcion de **reacciones automaticas (emoji)** del agente de IA desde el proyecto
hermano `CUBOT.redmanager`. El agente pone un emoji (pulgar, corazon, etc.) a ~N de cada M mensajes
del cliente **sin pasar por el LLM (cero tokens)**; un mensaje que ya tiene reaccion no recibe otra.

**Cambios (7 partes):**
- `AiAgent`: 4 campos nuevos (`ReactionsEnabled`, `ReactionRatioN`=3, `ReactionRatioM`=4, `ReactionEmojis`).
- Migracion **dual** `AddAgentReactions` (PG `20260731200654`, SQL Server `20260731200740`): 4 columnas en
  `ai_agents` + backfill `UPDATE ... SET n=3,m=4 WHERE m=0`. (`Message.Reaction` ya existia.)
- `IEvolutionApiClient.SendReactionAsync` + impl (`POST /message/sendReaction/{instance}`, reaccion por
  clave del mensaje original, `fromMe=false`).
- `IWhatsAppConnectorService.SendReactionAsync` + impl (resuelve linea Evolution conectada, `remoteJid`,
  `IgnoreQueryFilters` para el contexto del webhook).
- `AgentConversationService` (dispatcher real, NO "AgentDispatcher"): inyecta `IWhatsAppConnectorService` +
  metodo `MaybeReactAsync` llamado **antes** de la inferencia (dado N/M, emoji al azar, marca
  `Message.Reaction`, bitacora Tool/Error).
- `EvolutionWebhookParser.Parse`: **descarta** los `messages.upsert` con `reactionMessage` (reacciones
  ENTRANTES no se persisten como mensaje ni disparan al agente).
- UI `Agentes.razor`: acordeon "Reacciones" (switch, frecuencia N/M, stack de emojis) + DTOs
  (`AiAgentDto`/Create/Update) + `AiAgentService` (Create/Update/Map/Duplicate).

**Estado:** `dotnet build` verde (0 errores). Sin smoke-test local porque el dev apunta a la BD de prod
(las columnas nuevas no existen hasta que corra la migracion en el deploy). Verificacion en vivo = tras
el deploy (ECOREX_RUN_MIGRATIONS aplica la migracion), configurar emojis en SARA y probar por WhatsApp.

**Siguiente:** commit/push al tronco; deploy a prod con confirmacion + backup. Recordatorio pendiente
del hilo anterior: reconectar la linea AGROMETALICAS en `/lineas` para activar el webhook ya configurado.

---

## 2026-07-31 - Datos (contenedor de tarifas del cotizador en AGROMETALICAS)

**Contenedor "Datos de cotizacion" en AGROMETALICAS (por SQL directo):** cargadas las **5 tablas de
tarifas** de la hoja **"32"** de `MODELO COTIZACION.xlsx` como un DataModel manual (mismo mecanismo que
GESTION COMERCIAL de SOLDARCO). Backup `ecorex-2026-07-31-0854.sql.gz`. **56 filas** en 5 tablas:
Espesores (26: CAL 10-30 + numericos), Laminas (5: HR/INOX/ALFAJOR/GALVANIZADA/CR con Venta y Costeo),
Servicio corte (10: lamina/espesor/precio), Servicio doblez (10) y Servicio rolado (5: lamina/precio).
Columnas Text. Idempotente (guarda a nivel de contenedor: solo carga filas si la tabla esta vacia, para
no chocar con la 1a columna repetida de corte/doblez -lamina se repite-). Estas tarifas son la fuente
que el cotizador (form COT de AGROMETALICAS) puede consultar por LOOKUP para autollenar precio de lamina
y servicios por tipo/espesor cuando se enganche esa columna.
Nota: bajo "Servicio rolado" el Excel tiene ademas un mini-listado SI/NO (opcion de rolado) que NO se
cargo como tabla; son las 5 tablas de tarifas que pidio el usuario.

**Lookup del cotizador enganchado al contenedor + VALIDADO (2026-07-31):** se engancho la columna
`tipo_lamina` del cotizador de AGROMETALICAS (form COT) a la tabla **Laminas** del contenedor. Es CONFIG
(dato en `options_json`), no codigo: el motor de lookup de columna ya existe (`FormGridColumnLookup` +
`DataContainerLookupSource`). Config anadida a la columna:
`"lookup": {source:"DataContainer", sourceRef:"<id Laminas>", displayField/valueField:"Lamina",
presentation:"dropdown", autofill:{"Venta":"precio_venta_lamina","Costeo":"costo_lamina_kg"}}`.
Backup `ecorex-2026-07-31-0901.sql.gz`.
- **Validado en vivo (Chrome, URL publica):** `tipo_lamina` paso a ser un desplegable con las 5 laminas
  del contenedor (CR/GALVANIZADA/ALFAJOR/INOX/HR); al elegir **HR** autollenó **Precio venta lamina =
  4800** y **Costo lamina = 5000** (= Venta/Costeo de HR en el contenedor). Funciona.
- **LIMITE del motor (para la sesion de diseño/codigo, NO es dato):** el lookup de columna es de UNA
  sola clave. Los precios de **corte/doblez** dependen de (lamina + espesor) = clave COMPUESTA, y el de
  **rolado** de la lamina pero en una tabla aparte -> el motor actual no los puede autollenar con este
  mecanismo. Requiere lookup multi-clave o formula-con-lookup (mejora de codigo). Por ahora esos 3
  precios siguen siendo entrada manual.

## 2026-07-30 - Motor de Reportes: Ola 2 (embed VISOR Bold) + convertidor RDL

**Agentes:** Claude (worktree `informes`) + sub-agente de investigacion de la integracion Bold Blazor.

**Hecho:** VISOR Bold Reports EMBEBIDO y verificado en vivo (modo evaluacion, sin clave) +
convertidor `ReportSpecToRdl` y persistencia del imprimible.
- Gate #2 (net10) CONFIRMADO: `BoldReports.Net.Core` 14.1.14 publica net10.0 y restaura en la solucion
  (+ `Microsoft.AspNetCore.Mvc.NewtonsoftJson`).
- `BoldReportsApiController` (IReportController, policy TenantMember): carga el RDL de la
  ReportDefinition e inyecta las filas TENANT-SAFE (`IReportDefinitionService.GetPrintableAsync`), en
  ProcessingMode.Local. Paginas `/reportes/imprimibles` (indice) + `/reportes/imprimibles/{id}` (visor)
  + boton "Guardar como imprimible" en `/reportes/ia`. Registro de licencia en Program.cs (lee
  `Bold:LicenseKey`, sin clave = evaluacion). Assets Bold + jQuery desde la CDN oficial en runtime
  (interop `boldreports-interop.js`, on-demand) -> JS propietario NO versionado (gitignore).
- Convertidor `ReportSpecToRdl` (RDL 2016, nombres data source/dataset/inyeccion alineados) +
  `SavePrintableAsync` (Kind=Printable + Rdl). Test `ReportRdlTests` 2/2 (puro).
- APRENDIZAJE (resuelto en vivo): los datos inyectados solo se usan en **ProcessingMode.Local**; el
  `ReportDataSource.Name` == `<DataSource Name>` == `<DataSet Name>` == `<Query><DataSourceName>`; data
  source con ConnectionProperties embebido (connectstring ignorado en Local), NO DataSourceReference.
  Documentado en `docs/motor-reportes-ola2-embed-bold.md`.

**Verificacion en vivo** (owner@sky-system, server propio 5260 vs BD local): imprimible RECIEN creado
(convertidor -> RDL, sin parches) renderiza en el visor Bold con datos reales del tenant (Done 4/
Suspended 1/Active 86/Pending 123/InProgress 5 = 219 = SKY SYSTEM) + barra export. Assets via CDN:
servidor sirve el loader CDN y las 6 URLs responden 200. Migracion `AddReportDefinition` aplicada al PG
local. `POST /api/BoldReportsApi/PostReportAction -> 200`. Server propio detenido por PID/ruta; dev
principal (5234) intacto.

**Ampliacion (misma fecha): DISENIADOR drag-drop Bold + RDL afinado.**
- `BoldReportsDesignerController` (IReportDesignerController, que hereda IReportController): abre el RDL
  de la ReportDefinition (GetData por itemId=Id) y lo guarda (SetData -> `UpdateRdlAsync`); la vista
  previa del diseniador reusa la inyeccion tenant-safe en Local. Servicio: `GetRdlAsync`/`UpdateRdlAsync`.
- Pagina `/reportes/imprimibles/editor/{id}` (mount del diseniador via interop `renderDesigner` que hace
  `openReport(id)`) + boton "Editar" en el indice.
- RDL afinado (`ReportSpecToRdl`): encabezado con fondo indigo + texto blanco/negrita centrado, celdas
  con borde/padding, alineacion a la derecha y formato de numeros (N0/N2) y fechas (yyyy-MM-dd).
- VERIFICADO en vivo: el diseniador se monta con toolbox completo (TextBox/Image/graficos/Table/Matrix/
  Tablix Wizard/KPI/Gauges/SubReport), abre el reporte (`POST /api/BoldReportsDesigner/PostDesignerAction
  -> 200`), y el imprimible afinado renderiza en el visor con datos reales. Log del servidor sin
  excepciones (los errores de circuito en el navegador integrado son negociacion SignalR del preview
  bajo la pagina pesada, ajenos al codigo).

**FIX del diseniador + ciclo editar->guardar VALIDADO end-to-end (2026-07-30):** el diseniador lanzaba
NRE al abrir. CAUSA (hallada por logging): `openReport(path)` asume Report Server; y el diseniador usa
GetData/SetData como ALMACEN TEMPORAL DE SESION (pedia `_setting.txt` y mi GetData devolvia vacio -> NRE).
FIX (patron documentado): (1) GetData/SetData reescritos como almacen de archivos temporal generico
(`%TEMP%/ecorex-bold-designer`); (2) abrir con `openReportDefinition(rdl)` CLIENT-SIDE, trayendo el RDL
del nuevo endpoint GET `/api/reporting/rdl/{id}`; (3) guardar con `saveReportDefinition(cb,"XML")` ->
POST al endpoint POST `/api/reporting/rdl/{id}` -> `UpdateRdlAsync`. Boton "Guardar" en la pagina del
editor. VALIDADO EN VIVO (Chrome MCP): crear reporte -> abrir diseniador (SIN NRE, muestra el Tablix con
encabezado indigo) -> editar el titulo -> Guardar ("Guardado.") -> el RDL en BD queda como la
serializacion propia de Bold (~13 KB) con "EDITADO EN EL DISENIADOR 2026" y SIN el titulo viejo -> el
VISOR renderiza el titulo editado + datos reales del tenant. Ciclo completo cerrado.

**Siguiente:** clave de licencia (marca de agua; la coloca el usuario) + confirmar Docker prod.

**Bloqueos:** solo la marca de agua (clave del usuario) y Docker prod.

**Reporte SHOWCASE "Panel de Actividades del Sistema" (2026-07-30):** pagina
`/reportes/actividades-sistema` que demuestra TODA la capacidad del motor sobre el datasource
tenant-safe con UNA consulta tabular pivotada en el servidor: 6 KPIs (Total/Abiertas/En progreso/
Cerradas/Suspendidas/Vencidas), dona por estado, area de tendencia (creadas/dia), barras por
prioridad, barras APILADAS estado x prioridad, una TABLA MATRIZ cross-tab Estado x Prioridad (con
totales de fila/columna, gran total y heatmap) y detalle reciente con chips de estado. Verificado en
vivo (Chrome): 4 canvases ECharts, matriz 6 filas, KPIs 219/214/5/4/1/33 (Vencidas por DueDate). Todo
ECharts por interop, cero cadena de conexion.

**Imprimible NATIVO Bold MULTI-PAGINA tipo "cuaderno" Power BI (2026-07-30):** `RichActivityReportRdl`
genera un RDL 2016 rico que Bold renderiza y exporta a PDF: Pag 1 = portada (titulo + subtitulo con
`=Count(...)`) + 6 KPIs (textboxes con expresiones de agregacion) + TABLA MATRIZ nativa (Tablix con
grupos de fila Estado y columna Prioridad + subtotales + gran total); Pag 2 = GRAFICO de columnas nativo
por estado (`<Chart>` RDL); Pags 3+ = Tablix de detalle (paginado). Datos = una consulta tabular
tenant-safe inyectada en ProcessingMode.Local. Servicio `SavePrintableRdlAsync(spec, rdl)`; boton
"Generar reporte completo (demo)" en `/reportes/imprimibles`. Verificado en vivo (Chrome, DOM): "of 6"
paginas, KPIs 219/214/5/4/1/33, matriz con totales (51+1+167=219). NOTA: el navegador integrado congela
el screenshot con el canvas pesado de 6 paginas; el contenido se confirma por el DOM y renderiza/exporta
en navegador normal. Bug corregido: los KPIs no salian por un Rectangle contenedor de tamanio 0.

**Decisiones:** ADR-0051 (stack). Assets Bold por CDN (no versionar JS propietario). Datos in-memory
tenant-safe en ProcessingMode.Local.

---

## 2026-07-29 - Motor de Reportes y BI: Ola 0 (gate de licencia)

**Agentes:** Claude (sesion worktree `informes`) + sub-agente de investigacion de licencias.

**Hecho:** arranque del proyecto Motor de Reportes y BI en un git worktree DEDICADO
`informes` (`C:/DesarrolloIA/ecorex-informes`, rama `feat/motor-reportes` desde `main`).
Config de puerto propio `informes-5260` agregada al `launch.json` del worktree (BD dev
local 5442), sin tocar el dev de la sesion principal (5234) ni las configs existentes.
Ningun proceso matado.

**Ola 0 (gate de licencia, SIN codigo de producto):** investigados con fuentes oficiales
los 4 gates del doc 01 y documentados en `ADR-0051`. Resultado:
- Gate 1 (elegibilidad Community): SI condicional. OJO: Bold Reports se separo de Syncfusion;
  la community de Syncfusion cubre solo el Viewer, NO el Report Designer -> hay que registrar
  la Community License PROPIA de Bold (que si incluye el editor). Elegibilidad: < 1M USD/anio,
  <= 5 devs, <= 10 empleados, < 3M USD capital externo (la debe autoconfirmar el usuario).
- Gate 2 (embebe en Blazor Server): SI. `BoldReports.Net.Core`, InteractiveServer, controllers.
- Gate 3 (datasource tenant-safe JSON/Web sin connection string): SI. JSON/Web data source +
  addDataSource/addDataSet + data-source extension.
- Gate 4 (redistribucion SaaS a tenants): SI. Community endosa SaaS multi-tenant / ISV.
- RIESGO AMBAR a escalar: **Docker bajo Community no esta claramente concedido** (community =
  "Windows and Linux"; Docker/K8s = pago). Prod corre Linux Docker. Requiere confirmacion
  escrita de Bold o alternativa de deployment/licencia.

**Siguiente:** con aprobacion del usuario en los 2 puntos abiertos (elegibilidad Bitcode +
Docker/Bold), construir Ola 1 (catalogo semantico + IReportDataSource tenant-safe + test de
aislamiento dual PG/SQL Server) que es INDEPENDIENTE de Bold y de Docker.

**Gates confirmados por el usuario:** Bitcode califica para la Community de Bold; se procede a
construir la Ola 1 ya (independiente de Bold/Docker) mientras el usuario pide a Bold confirmacion
escrita del deployment Docker antes de la Ola 2.

**Ola 1 CONSTRUIDA (capa propia, independiente de la libreria):** en `Ecorex.Application/Reporting/`:
- Modelo neutro declarativo: `ReportModel.cs` (ReportField/SourceDescriptor/DataSet, enums de tipo/
  operador/agregacion) + `ReportQuerySpec.cs` (spec + ReportContext + ReportValidationException).
- `IReportCatalog`/`ReportCatalog`: publica fuentes reportables = nativas curadas + contenedores del
  tenant derivados de DataContainer (limite de seguridad: lo que no esta en el catalogo no es reportable).
- `IReportableSource`/`TaskItemReportSource`: fuente NATIVA (Actividades) con LINQ tipado sobre el DbSet
  ya filtrado por tenant; filtros parametrizados + tabular + group-by/Count.
- `ContainerReportReader`: lee contenedores EAV (pivot de celdas, filtro texto por EXISTS en BD, group-by
  + Sum/Avg/Min/Max numerico en memoria sobre el conjunto ya acotado).
- `IReportDataSource`/`ReportDataSource`: EL CONTRATO CENTRAL. Choke point que valida el spec contra el
  catalogo (rechaza campos/ops fuera de catalogo) y despacha; el aislamiento lo garantiza el filtro global
  del DbContext (fail-closed), no la confianza en el ctx.
- Endpoints `/api/reporting/catalog` y `/api/reporting/query` en SuperAdmin (`RequireAuthorization
  ("TenantMember")`): el "JSON/Web data source" tenant-safe que consumira el visor Bold (Ola 2) / ECharts
  (Ola 3). Nunca cadena de conexion.
- DI: registrados en `Application/DependencyInjection.cs` (sumar una nativa = otro IReportableSource).

**SIN migraciones:** la Ola 1 no agrega entidades (catalogo = codigo + derivado). `ReportDefinition`
llega en la Ola 2.

**Verificacion:** test de integracion DUAL `ReportDataSourceTests` (PG + SQL Server via Testcontainers)
**12/12 verde**: tabular nativo, group-by/Count nativo resuelto en servidor, group-by/Sum sobre EAV,
AISLAMIENTO cross-tenant (nativa -> vacio para el otro tenant; contenedor -> fuente inexistente en su
catalogo) y rechazo de campo fuera de catalogo. Builds Application/SuperAdmin verdes.

**Siguiente:** Ola 2 (entidad ReportDefinition + editor/visor Bold RDL + export PDF) TRAS la confirmacion
escrita de Docker; luego Ola 3 (dashboards ECharts por interop) y Ola 4 (autoria IA sobre JSON-spec).

**Bloqueos:** Ola 2 espera la confirmacion escrita de Bold sobre Docker en community (o decision de
alternativa). Olas 1 no bloqueada.

**Decisiones:** ADR-0051 (stack + gates, ACEPTADA). Ola 1 desacoplada del vendor: la capa propia se
conserva aunque cambie la suite.

**Ola 3 CONSTRUIDA y VERIFICADA EN VIVO (dashboards ECharts, independiente de Bold):**
- ECharts vendorizado como `.js` ESTATICO (`wwwroot/lib/echarts/echarts.min.js`, v5.5.1 Apache-2.0,
  ~1 MB) + interop `wwwroot/js/echart-interop.js` (`window.ecorexEChart` init/update/dispose, resize,
  click opcional a .NET). Sin Node/npm. Scripts sumados en `App.razor`.
- Componente Blazor `Components/Shared/Reporting/EChart.razor`: serializa una "option" (Dictionary) a
  JSON y la pinta por interop; IAsyncDisposable con manejo de circuito cerrado.
- Pagina `Components/Pages/Reporting/ReportDashboard.razor` (ruta `/reportes/tablero`, policy
  `TenantMember`, InteractiveServer): 4 KPIs + dona por estado + area de creadas por dia + barras por
  prioridad + tabla de recientes, TODO via `IReportDataSource` tenant-safe. Filtro de rango de fechas
  (30/90 dias, Todo) que RE-CONSULTA el datasource. CSS scoped propio.
- VERIFICADO en vivo (mi server 5260 contra BD dev local, login owner@sky-system): dashboard carga
  datos reales (Total 219 = SOLO SKY SYSTEM, Plataforma ECOREX=0 -> aislamiento OK), 3 canvases ECharts
  renderizados, interop cargado, tabla poblada, cero errores de consola. Filtro probado: 30 dias
  re-consulta OK; rango futuro (2027) -> 0 en todo + "Sin actividades en el rango" (prueba que el
  filtro fluye al datasource EF, CreatedAt Between). Mi server detenido por PID/puerto propio tras
  verificar la ruta; el dev de la sesion principal (5234) intacto.
- NOTA: la "imagen de referencia" del prototipo no estaba disponible en el vault; el dashboard sigue
  un layout limpio on-brand (indigo). Cuando el usuario comparta la imagen se afina milimetricamente.
- Menu: la pagina es accesible por ruta+policy; el item en el menu dinamico es un follow-up menor.

**Ola 4 CONSTRUIDA y VERIFICADA (autoria por IA, independiente de Bold):**
- Artefacto declarativo compartido IA<->usuario: `ReportSpec` (DTO JSON amable, enums como texto) +
  `ReportSpecRenderer` (spec + ReportDataSet -> option de ECharts: Bar/Pie/Line; Table lo pinta la UI).
  El convertidor a RDL (imprimible) queda para la Ola 2 (Bold).
- `IReportAuthoringService`: pipeline determinista instruccion -> catalogo -> JSON-spec -> VALIDA
  contra el catalogo (rechaza campo/fuente fuera de catalogo) + ejecuta via el datasource tenant-safe
  -> option. El LLM esta detras de `IReportSpecGenerator` (fakeable en tests); el generador real
  `AiReportSpecGenerator` resuelve el agente/proveedor del tenant (patron WorkflowAgentInvoker) y
  registra consumo (AiUsageLog, source "report-authoring"). La IA NUNCA ve SQL ni columnas fisicas.
- Persistencia: entidad `ReportDefinition` (ITenantScoped, IVersioned, SIN soft-delete: enum de
  estado Active/Archived) + `IReportDefinitionService` (guardar/listar/obtener/editar/archivar/
  ejecutar). MIGRACIONES DUALES creadas (PG `jsonb` spec_json + SQL Server `nvarchar(max)`), con
  `--context` explicito por los dos DbContext.
- UI `/reportes/ia` (policy TenantMember): instruccion -> preview (EChart + tabla) -> guardar -> lista
  de guardados con Abrir/Archivar. Reusa el componente `EChart` de la Ola 3.

**Verificacion Ola 4:** 8 tests de integracion DUAL nuevos (`ReportAuthoringTests`) verdes -> el
conjunto de reportes queda **22/22** (PG + SQL Server): autoria nativa (barras) + autoria contenedor
(Sum) + rechazo de campo fuera de catalogo + persistencia y aislamiento cross-tenant del reporte
guardado. La migracion `AddReportDefinition` se aplico LIMPIAMENTE al PG local al arrancar (CREATE
TABLE report_definitions + indices). `/reportes/ia` validada EN VIVO (owner@sky-system): render OK,
manejo elegante sin agente de IA ("No hay un agente de IA activo..."), lista de guardados, y **Abrir**
ejecuta un spec guardado -> grafico ECharts + tabla con datos reales (Done 4/Suspended 1/Active 86/
Pending 123/InProgress 5 = 219 = SKY SYSTEM), cero errores de consola. La generacion por LLM en vivo
no se probo (local sin proveedor/clave, y no se deben versionar claves); su pipeline determinista
queda cubierto por los tests con el generador falso.

**Ola 2 - PARCIAL (parte independiente del vendor construida; el embed Bold sigue bloqueado):**
- Convertidor `ReportSpecToRdl` (`Ecorex.Application/Reporting/Authoring/ReportSpecToRdl.cs`): genera un
  RDL 2016 estandar (DataSources JSON logico "EcorexTenantSafe" -> endpoint tenant-safe, DataSet con un
  Field por columna del resultado, Tablix con una columna por campo + titulo). Es el camino T1/D6: la
  IA/usuario generan el MISMO artefacto RDL que abrira el editor/visor Bold.
- `IReportDefinitionService.SavePrintableAsync(spec, dataset, desc)`: persiste el imprimible con
  Kind=Printable + Rdl generado (el campo Rdl ya existia en la entidad desde la Ola 4).
- Test unitario `ReportRdlTests` (2/2 verde, puro, sin Docker): well-formed RDL 2016, namespace correcto,
  Field por columna, Tablix con enlace =Fields!X.Value por columna, y data source JSON logico (NUNCA
  cadena de conexion a BD). Se corrigieron dos test-doubles FakeAppDb (RowIngest/TenantUser) que
  implementan IApplicationDbContext y necesitaban el nuevo DbSet ReportDefinitions.

**Ola 2 - VISOR BOLD EMBEBIDO Y VERIFICADO (2026-07-30, modo evaluacion):** ver la entrada fechada
2026-07-30 arriba. Resumen: gate #2 (net10) confirmado; `BoldReportsApiController` (IReportController,
policy TenantMember) carga el RDL de la ReportDefinition e inyecta las filas TENANT-SAFE en
ProcessingMode.Local; paginas `/reportes/imprimibles` + `/reportes/imprimibles/{id}` + boton "Guardar
como imprimible"; assets Bold via CDN (no se versiona JS propietario). Verificado en vivo: imprimible
fresco renderiza en el visor Bold con datos reales del tenant (219 = SKY SYSTEM) + export. PENDIENTE:
clave de licencia (marca de agua; la coloca el usuario en `Bold:LicenseKey`), DISENIADOR drag-drop Bold,
confirmar Docker prod.

**Follow-ups menores:** items de menu dinamico para /reportes/tablero, /reportes/ia, /reportes/imprimibles;
imagen de referencia del dashboard para afinar milimetricamente.

---

## 2026-07-28 - Instalador MSI (WiX) self-contained del agente Colmena

**Hecho:** el agente Conector On-Prem "Colmena" (apps/agent) ya tiene INSTALADOR. Antes se corria a
mano; ahora hay un MSI self-contained (win-x64) que no exige runtime .NET en la maquina cliente.

- **Perfiles de publicacion self-contained** (`Properties/PublishProfiles/win-x64-selfcontained.pubxml`
  en `Ecorex.Agent.Service` y `Ecorex.Agent.Gui`): `SelfContained=true`, RID win-x64, sin PDB.
- **Proyecto WiX v5** (`apps/agent/installer/Ecorex.Agent.Installer.wixproj` + `Product.wxs`),
  construible con `dotnet build` via `WixToolset.Sdk/5.0.2` (sin herramienta global; extension
  `WixToolset.Util.wixext` para el origen del Visor de eventos). El MSI:
  - Instala en `%ProgramFiles%\Ecorex\Agente Colmena` (perMachine, elevado).
  - Registra el **servicio de Windows `EcorexAgent`** (LocalSystem, auto; arranca al instalar,
    detiene/elimina al desinstalar) y el origen de eventos "ECOREX Agente".
  - Instala la **GUI de bandeja** con acceso directo en Menu Inicio + AUTOSTART (llave Run HKLM con
    `--tray`, opcion NUEVA de la GUI que arranca minimizada a la bandeja sin parpadeo).
  - Crea y **PRESERVA** `%ProgramData%\Ecorex\Agent` (config compartida servicio/GUI). El MSI no
    gestiona `config.dat`, asi que el UPGRADE (UpgradeCode estable + MajorUpgrade) no borra la
    identidad del cliente. Desinstalacion limpia.
- **Script** `apps/agent/installer/build-installer.ps1` (idempotente): publica ambos proyectos al
  mismo staging, separa los 2 exe, compila el MSI y lo deja en `installer/dist/`.
  - **AUTO-LANZA la bandeja al terminar de instalar** (custom action `LaunchTrayGui`, tipo 18,
    impersonada, `--tray`, asyncNoWait, tras InstallFinalize, `NOT Installed`): el icono aparece de
    inmediato en la sesion del usuario sin re-login. Antes solo salia en el proximo logon (la llave
    Run dispara en el logon), sintoma reportado ("no quedo el agente activo en el tray icon").
- **Diagnostico de conexion (mini-log) en la GUI:** el flyout de Configuracion ahora muestra un
  REGISTRO con hora (mas nuevo arriba) de cada "Probar conexion": "Probando con <hub>...",
  "Conectando...", "Conectado: en linea.", "Sin conexion (offline)." y el motivo EXACTO del fallo
  ("Error: handshake rechazado 401...", "Rechazado: no admin..."). El servicio ya producia ese
  `LastError`; solo faltaba pintarlo. Cambios: `ViewModels/HiveViewModel.cs` + `MainWindow.xaml`.
- **Script** `apps/agent/installer/build-installer.ps1` (idempotente): publica ambos proyectos al
  mismo staging, separa los 2 exe, compila el MSI y lo deja en `installer/dist/`.
- **Verificado:** `dotnet build Ecorex.Agent.slnx` verde (0/0). El MSI se genera
  (`dist/Ecorex-AgenteColmena-1.0.0.msi`, ~58.6 MB); tablas MSI confirmadas por COM (ServiceInstall
  LocalSystem/auto, ServiceControl, Run key `--tray`, `LaunchTrayGui` tipo 210 tras InstallFinalize,
  Shortcut, CreateFolder config, Upgrade, EventLog source). Unico warning ICE61 benigno.
- **Cambios de codigo** (minimos y seguros): `Ecorex.Agent.Gui/App.xaml.cs` (arranque `--tray` a la
  bandeja) y `MainWindow.xaml.cs` (restaurar barra de tareas al abrir desde bandeja).

**Decisiones (ADR-0049):** self-contained en CARPETA (no single-file, mejor para WPF/WebView2);
autostart por llave Run HKLM (no Startup) + auto-launch al instalar; mini-log de diagnostico en la
GUI; config preservada por no gestionarla el MSI.

**Siguiente / abierto:** el MSI NO se firma (sin certificado) -> Windows muestra "editor desconocido";
la firma con signtool + cert Authenticode es un paso posterior de release. Sin bloqueos.

**Gates:** archivos nuevos en ASCII; sin secretos (la identidad la pone el cliente por la GUI); el
agente sigue referenciando solo `libs/Ecorex.Contracts.Agent`. NO se commiteo/pusheo/desplego.

## 2026-07-27 - Catalogo del SIMULADOR de SKY: remapeo de items para el lookup (sesion de DATOS)

**Remapeo de los items del Cotizador en SKY SYSTEM (por SQL directo):** llego un prompt de la sesion
de diseño para "cargar el catalogo COMPLETO (~1019 productos)" y dejar los items listos para el lookup
del SIMULADOR (form COT `59a91ffe`). Backup `ecorex-2026-07-27-0936.sql.gz`.

> **CORRECCION AL PROMPT (2a vez):** BASE_PRODUCTOS **NO tiene ~1019 productos, tiene 11**. El "1019" es
> `max_row-4`; hay **1009 filas VACIAS** con formato. El xlsx cambio de MD5 desde el 2026-07-21
> (`56aefcce` -> `4fee397f`) pero el conteo de productos sigue en 11 (cambio en otras hojas). Los 11 ya
> estaban cargados desde el 2026-07-21; esta sesion NO cargo catalogo nuevo, hizo el **REMAPEO** que el
> simulador necesita.

- **Remapeo de los 11 items** a la forma que espera el simulador:
  - `Price` = **COSTO SIN IVA (col H)** (antes tenia COSTO con IVA). Es el "costo" que autollena el grid.
  - Campo dinamico NUEVO `costo_con_iva` (Number) = COSTO con IVA (col F), solo referencia.
  - `exento_iva` normalizado a **"0"/"1"** (el grid es un select {0=No,1=Si}); todos los 11 traen EXENTO
    IVA vacio -> "0". El `ItemFieldDefinition` de exento_iva paso de Select(SI/NO) a **Text** para guardar
    el id 0/1 tal cual.
  - Se retiro el campo dinamico `costo_sin_iva` (su valor vive ahora en Price).
  - `proveedor` (Text) se conserva; marcas (HP/LG/SAMSUNG/LENOVO/GENERICO/GEFORCE/ASUS) y stock en
    **Bodega Central** siguen del cargue del 2026-07-21.
- Prerrequisitos del prompt que YA estaban hechos (2026-07-21): campos dinamicos, catalogo Brand,
  limpieza E2E (verificado: 0 marcas E2E, 0 bodegas E2E).
- **BLOQUEANTE para que el simulador funcione (NO es dato, es del diseñador):** el form COT `59a91ffe`
  **NO tiene configurado el lookup** en su columna `codigo` (0 columnas del grid con config de
  autollenado). Con los datos ya listos, falta que la sesion de diseño enganche el lookup del `codigo`
  a la fuente Item (valueField=sku) con el mapa de autollenado producto/detalle/marca/proveedor/
  costo<-Price/stock/exento_iva. Hasta entonces, teclear un codigo NO autollena.
- PENDIENTE de decision del usuario (lo pedia el prompt): la BODEGA del stock quedo en **Bodega Central**
  (del cargue anterior); si se quiere una "Bodega Sky System" dedicada, se mueve.

## 2026-07-25 - Tableros (etiquetas por columna, tiempo en columna, filtro), eliminar registro de formulario, GridDetail responsive + ancho por columna + ancho de tarjeta, autocompletado de contacto

**Agentes**: Claude (Opus 4.8) + 2 subagentes Explore (mapeo de Directorio/Tercero para el autocompletado).

### Hecho (todo verificado en Chrome salvo lo anotado)

**1. Autocompletado de contacto en el wizard de actividad.** Paso 2 (Contacto): el nombre del
solicitante ahora autocompleta desde el Directorio General (000232) reusando `TerceroLookupSource`
(via `IFormLookupService`, paginado y acotado al tenant). Nuevo componente `TerceroPicker`. El
campo SIGUE siendo texto libre (un solicitante que no esta en el directorio se escribe a mano).
Al elegir un tercero se rellenan identificacion/email/telefono con regla de sobreescritura
(lo del directorio gana; lo tecleado a mano no se pisa). PENDIENTE anotado: `CreateTaskItemRequest`
no persiste el vinculo al tercero (haria falta columna + migracion); hoy solo copia texto.

**2. Tableros: 4 mejoras + fixes.** Migracion dual `AddColumnTagsAndTimeInColumn`.
- **Etiquetas por columna** (restriccion): entidad `TaskBoardColumnTag`; en `/tableros`, cada
  estado define que etiquetas admite (vacio = todas, fail-open). El servicio es el guardian.
- **Crear/eliminar etiquetas con color** desde el panel de la columna (paleta de 12 tonos; la "x"
  borra del catalogo con confirmacion que avisa cuantas tarjetas la usan).
- **Marcar varias etiquetas al editar** la tarea (TaskDetailModal): chips con "x" + "+ Etiqueta"
  que solo ofrece las permitidas por la columna.
- **Tiempo en columna**: columna `ColumnEnteredAt` (se sella al mover, no al reordenar); chip
  "hace X" en la tarjeta que se pone ambar/rojo segun antiguedad; cae a CreatedAt para tareas viejas.
- **Fix del filtro del indice**: "Categoria" no se pasaba al filtro (corregido en DTO/servicio/UI).
- **Fix de crash al marcar etiqueta**: el re-render llamaba GetDetailAsync (6+ consultas por el
  tunel) y Npgsql reventaba; ahora attach/detach actualizan el DTO en memoria (sin recargar).

**3. Eliminar registro de formulario.** En la bandeja del formulario-modulo (`/m/{code}`) solo
existia "Anular" (soft-delete). Nuevo `DeleteRecordAsync` (borrado real en transaccion: limpia
FormRecordLink y desliga TerceroNota; FormFlowLink cae por cascada; libera el numero) + boton
"Eliminar" con confirmacion. Verificado end-to-end (creado FRM-028 de prueba, publicado, borrado
un registro, y limpiado: despublicado + archivado).

**4. GridDetail responsive (fix del cotizador COT, AGROMETALICAS, 25 columnas).**
- Scroll horizontal AISLADO a la tabla (`.dfr-grid-scroll`; el boton "Agregar fila" queda fuera).
- Primera columna y cabecera STICKY al desplazar (el identificador "Detalle" no se pierde).
- Panel de lookup de celda pasa a `position:fixed` posicionado por JS
  (`ecorexFormCapture.positionCellPanels`) para no ser recortado por el scroller.
- Impresion (FormPrint, A4): se encoge para caber (fuente reducida, texto partido), sin rotar a
  apaisado (decision del usuario).

**5. GridDetail: ancho por columna (data-driven).** `FormGridColumn.Width` (px, en options_json,
via "width"/"w"); `<colgroup>` + `table-layout:fixed`; default por tipo (calc 110, select 130,
1a columna 200, texto 100). Los anchos son DATO: la sesion de datos los ajusta sin tocar codigo.

**6. Ancho de tarjeta configurable por formulario (ADR-0047).** Enum `FormCardLayout`
(Normal/Ancho/Completo) + columna `card_layout` (migracion dual `AddFormCardLayout`, default
Normal). Selector en Propiedades del formulario. Se aplica solo en las superficies de llenado
(`/f`, `/m`, vista previa) via `ApplyCardWidth`; los usos embebidos (tercero, tarea) no se tocan.
No cambia la impresion.

**7. Fix: "Configuracion actividades" salia sin estilar** (iconos gigantes de 526px). Las clases
`inv-cfg-*` tenian CSS con ALCANCE en InventarioConfiguracion.razor.css y esta pagina no tenia su
`.razor.css`. Se le creo (ActividadConfiguracion.razor.css).

### Aplicado a prod (autorizado, con backups)

Backups previos: ecorex-2026-07-24-1033/1611 y 2026-07-25-1132. Migraciones duales aplicadas a la
BD de prod (arrancando el dev por el tunel): `AddGestorDocumental` (dia 24), `AddActivityCatalogs`
(ya estaba), `AddColumnTagsAndTimeInColumn` y `AddFormCardLayout`. Menu del Gestor Documental
reconciliado en los 5 tenants Standard con legacy_code 000894 (consecutivo, por decision del
usuario) + fila en module_definitions. Todas las migraciones verificadas TAMBIEN en local dual
(PG + SQL Server efimeros) antes de prod.

### Verificacion en Chrome

GridDetail responsive CONFIRMADO sobre el cotizador COT real (25 col): scroll horizontal, primera
columna sticky (Detalle se queda fija al desplazar), anchos por columna (tabla 2064px). El selector
"Ancho del formulario" aparece y funciona; card_layout persiste. QUEDO SIN CAPTURAR el preview en
modo Ancho mas ancho porque el tunel se cayo a mitad (problema recurrente del dev-sobre-tunel, no
del codigo); la logica de render es CSS por card_layout y el `/f` ya muestra la tarjeta fit-content.
El panel de lookup en celda no se pudo ejercitar (ningun grid ancho usa columna lookup), pero esta
desplegado y el JS confirmado cargado.

### Bloqueos / notas

- El tunel SSH a prod (15433) se cae con frecuencia; cada caida tumba el circuito Blazor y obliga a
  reloguear. Deuda: `AccessDeniedPath="/login"` hace que un 403 y una sesion muerta se vean igual.
- 537/537 tests unitarios verdes en cada corte.

### Siguiente

- Verificar el preview en modo Ancho/Completo cuando el tunel este estable.
- Persistir el vinculo tercero<->tarea del autocompletado (columna + migracion) si se decide.

---

## 2026-07-24 - Gestor Documental portado desde PROPIA (backend + UI, sin desplegar)

**Agentes**: Claude (Opus 4.8).

### Contexto

El usuario pidio traer el modulo **DOCUMENTOS** del sistema `C:\DesarrolloIA\Propia` (menu
"Comunicacion y documentos") y adaptarlo a ECOREX. **PROPIA NO es hermano del backbone**: es otro
sistema (.NET 9, Blazor Web App Auto + MudBlazor/NexLink, API separada con JWT, OpenIddict, solo
PostgreSQL). El modelo de datos y la logica se portan; la pagina se REESCRIBE.

Decisiones del usuario: las **dos mitades completas**, seccion de menu **nueva "Gestor Documental"**,
y los **5 tenants Standard** (AGROMETALICAS, BITCODE, CHUZO DE IVAN, EPRING, SOLDARCO).

### Hecho

**1. Dominio: 16 entidades nuevas.** `DocumentoEntities.cs` (Archivo central: categoria, carpeta,
documento, version, catalogo de etiquetas, union M:N, destacado personal, auditoria, consumo) y
`ExpedienteEntities.cs` (TRD: serie, subserie, tipologia, campo, expediente + sus tipologias y
campos). `DocumentoEnums.cs` con 6 enums, todos persistidos como TEXTO.

**2. Correcciones al portar (no son copia literal):**
- **Categorias y etiquetas base**: en PROPIA son GLOBALES (`TenantId NULL` + `EsBase`). Aqui eso
  viola la regla 1 (TenantId obligatorio + filtro global). Se siembra **una copia por tenant** y
  `EsBase` pasa a significar "sembrada por el sistema, no editable", no "compartida".
- **`OrigenDocumento`**: los origenes de PROPIA eran modulos de copropiedad (asamblea, pqrsd,
  porteria, mantenimiento). Se reemplazan por los de ECOREX: Tarea, Flujo, Formulario, Proyecto,
  Comunicacion, Sistema, Manual.
- **`Visibilidad`** era `string` libre ("PRIVADO"/"EQUIPO"/"PUBLICO"); pasa a enum verificable.
- **Obligatoriedad de las tipologias**: PROPIA la fijaba a fuego con `orden < 3` porque su subserie
  no guardaba la bandera. Aqui `SubserieTipologia.Obligatoria` existe y el expediente la COPIA.
- **Consecutivo del expediente**: PROPIA usaba `COUNT + 1`, que reutiliza un codigo ya emitido si
  se borra un expediente. Se calcula desde el MAXIMO.
- **Base64 -> byte[]**: PROPIA movia el binario en base64 porque la pagina hablaba por HTTP con una
  API. Aqui la consola es Blazor Server y llama en proceso: los bytes viajan como `byte[]`.

**3. Aplicacion:** `IDocumentoService`/`DocumentoService` (~700 lineas) y
`IExpedienteService`/`ExpedienteService`. Soft-delete en todo, transacciones en las operaciones
multi-tabla, bitacora append-only por cada cambio, y conteos por GROUP BY (sin N+1).
`IDocumentoFileStore` abstrae el almacen del binario.

**4. Infraestructura:** DbSets en `IApplicationDbContext` + `EcorexDbContext`, configuracion EF con
el criterio de borrado explicito y **consciente del DAL dual**: `Documento.VersionActualId` apunta
de vuelta a `DocumentoVersion`, asi que esa pareja va en `NoAction`/`Restrict` en AMBOS motores
para no formar el ciclo que SQL Server rechaza (error 1785).

**5. UI:** `/gestor-documental` (Blazor Server, ~900 lineas) con las dos vistas del origen
(Archivo central y Expedientes) reescritas con los tokens del workspace ECOREX
(`--brand`/`--ink`/`--surface`/`--line`), SVG inline y `.razor.css` con alcance, como
/conceptos y /contenedor-datos. Nada de MudBlazor ni de flaticon `fi fi-rr-*`.

**6. Subida de archivos con validacion REAL de contenido.** `DocumentUploadGuard` (hermano de
`ImageUploadGuard`): lista blanca de extensiones, tope de 25 MB y **bytes magicos** (PDF `%PDF`,
OOXML/ZIP `PK`, OLE `D0CF11E0`, imagenes). Sin `.svg` ni `.html` (XSS almacenado). La descarga NO
expone `/uploads/...`: pasa por el servicio, que comprueba el tenant y registra el consumo.
`DocumentoFileStore` resuelve la ruta y verifica que quede DENTRO de `wwwroot/uploads/documentos`.

**7. Menu y roles:** `EnsureGestorDocumentalMenuAsync` crea la seccion "Gestor Documental" (slug
`gesdoc`, al FINAL para no reordenar el menu validado el 2026-07-22) con su item "Documentos", y
`EnsureGestorDocumentalDefaultsAsync` siembra 9 categorias + 7 etiquetas por tenant. Disparador
`ECOREX_MENU_GESDOC=true`, limitado a `Kind == Standard`. La matriz de **roles no necesita codigo**:
`RolService.GetModuleCatalogAsync` la deriva de los nodos del menu, asi que el modulo aparece solo.

### Validacion

- `dotnet build Ecorex.sln`: 0 errores.
- **572/572 pruebas unitarias verdes** (35 Domain + 537 Application).
- **Migraciones duales aplicadas de verdad en local**, no solo generadas: `AddGestorDocumental`
  corrio contra PostgreSQL (5442) Y SQL Server (1443) en BD desechables, 16 tablas en cada motor.
  Que SQL Server la aceptara es la prueba de que no hay rutas de cascada multiples. Ambas BD de
  prueba borradas despues.

### Bloqueos

- **`legacy_code` del modulo: PENDIENTE.** La regla del 2026-07-22 prohibe inventar correlativos.
  Esta como `GesDocLegacyCode = null` en `DatabaseSeeder`, con el porque escrito al lado. El item
  de menu se crea sin codigo; poner el real es cambiar esa linea. Falta tambien la fila en
  `module_definitions`, que se indexa por `legacy_code`.
- **NADA aplicado a produccion.** La migracion NO se aplico a la BD de prod y el menu NO se
  reconcilio en los 5 tenants: ambas cosas exigen arrancar el dev (que en Development corre
  `MigrateAsync` contra la BD de prod por el tunel) y eso requiere autorizacion explicita.
- La app local quedo DETENIDA por lo mismo: relanzarla aplicaria la migracion a produccion.

### Pendiente del modulo (declarado, no silenciado)

Tres botones avisan "llega en la siguiente entrega" en vez de fingir que funcionan: el editor de
categorias/etiquetas, el editor de la TRD (series/subseries/tipologias/campos) y la creacion de
carpetas desde la UI. Los servicios YA tienen esas operaciones implementadas y probadas por
compilacion; lo que falta es su pantalla.

### Otro

- Validacion del lookup de Items de inventario (tarea con la que arranco la sesion): **sin hacer**.
  Se descubrio que SOLDARCO no tiene ningun `item_type`, asi que hay que crear el tipo "Producto"
  primero; el usuario lo autorizo, pero la sesion se desvio a este modulo antes de ejecutarlo.
- La sesion del dev se cayo sola dos veces al navegar; `AccessDeniedPath = "/login"` hace que un
  403 y una sesion muerta se vean IGUAL desde fuera. Deuda: mandar el 403 a una pantalla propia.

---

## 2026-07-23 (cont.) - Zoom del lienzo ER, fix del lookup y mejoras de listas del Contenedor

**Agentes**: Claude (Opus 4.8).

### Hecho

- **Zoom en el lienzo ER del Contenedor de datos.** Barra +/-/100%/Ajustar en la cabecera del
  modelo. El contenido (SVG + cajas) va en un wrapper con transform:scale; "Ajustar" mide el
  viewport por JS y encaja todas las tablas. El arrastre de cajas (dc-canvas.js) se reescribio para
  ser CONSCIENTE de la escala (acumula el delta / zoom); antes se descuadraba con zoom != 1.
- **Fix: el campo "Lista del Contenedor" no cargaba modelos.** El selector de Tipo usaba @bind sin
  @bind:after, asi que al pasar un campo a Lookup nunca se cargaba el catalogo de modelos y el
  desplegable salia vacio. Se agrego @bind:after -> OnCfgTypeChangedAsync. La pieza no estaba rota:
  faltaba el disparador.
- **Modo de presentacion del lookup: autocompletado o lista desplegable.** Nuevo DisplayMode en
  DataLookupConfig + selector en la config del campo. En modo lista, DataLookupField carga todas
  las filas (topadas) en un <select>.
- **Crear registros desde el campo lookup.** Flag AllowCreate en la config; en el llenado del
  tercero aparece "+ Nuevo" que abre un modal REUSANDO DataRecordsGrid (el gestor de registros del
  Contenedor); al cerrar refresca las opciones. TerceroModal pasa el ActorUserId cacheado.
- **"Limpiar tabla" en el visor de registros.** Boton en DataRecordsGrid (con permiso de borrar y
  filas > 0) que confirma y borra TODAS las filas. Nuevo IDataContainerService.ClearRowsAsync
  (borra filas + vinculos N:N y de relacion, en una transaccion, como el borrado de una fila).

### Notas

- Sin migracion: DisplayMode/AllowCreate viajan en el JSON de config del campo (no hay columnas
  nuevas), igual que el resto de la config del lookup.

---

## 2026-07-23 - Concepto por sede/entidad, formulario en el wizard y Configuracion de actividades

**Agentes**: Claude (Opus 4.8) + subagente Explore (mapeo de los catalogos de actividades).

### Hecho

**1. El concepto apunta a SEDES reales (entidades), no a texto libre.** El campo "Sedes que aplica"
del concepto (000270) era `string?` libre y estaba vacio en los 3 tenants. Se reemplazo por una
union M:N `ActividadSubcategoriaSede` (concepto <-> Entidad de "Configuracion de la entidad",
Cascade al concepto, Restrict a la entidad). El UI de Conceptos pasa a multi-seleccion de entidades
(chips + dropdown); vacio = aplica a todas. En el wizard, al elegir Empresa/Area la lista de
conceptos se filtra a los que aplican a esa entidad o a todas. Migracion dual `AddConceptoSedeEntidad`
(drop de la columna `sedes` vacia + tabla join). Enum TerceroFieldType.Lookup ya no aplica aqui.

**2. El wizard diligencia el formulario del concepto en el paso 3.** Antes el paso "Formulario" era
informativo. Ahora renderiza el DynamicFormRenderer (Fill) del formulario ligado al concepto; al
enviarlo captura la respuesta y, al guardar la actividad, la ancla por Number (misma mecanica que
FormFirstStarter). NO se toco el formulario por paso/proceso (sigue en el detalle) ni el form-first.

**3. Configuracion de actividades: 3 catalogos configurables desde cero.** Prioridades (000621),
Estados (000653) y Tipos de proyecto (000690) eran STUBS vacios sobre enums fijos. Se construyeron
como catalogos por tenant (entidades ActivityPriority/ActivityState/ProjectType con
IActivityCatalogEntity, IActivityCatalogService generico por kind, migracion dual AddActivityCatalogs).
Nuevo modulo /actividad-configuracion (cards + modal, patron inventarios) que reemplaza los 3 items
de menu por uno solo "Configuracion actividades". Cableado:
- Prioridades alimentan los chips del wizard (cada fila mapea a TaskPriority; la tarea guarda el enum).
- Tipos de proyecto: FK `Project.ProjectTypeId` + selector en crear/editar proyecto.
- Estados: catalogo de etiquetas DECOUPLED de TaskItemStateMachine (el ciclo de vida sigue en el enum).
Sembrado de valores por defecto por tenant. Reconciliacion de menu para los tenants existentes
(env ECOREX_MENU_ACTCONFIG).

### Decisiones (confirmadas con el usuario)

- Sedes: reemplazo total del texto libre (no habia datos que migrar).
- Concepto sin sedes = aplica a todas; el wizard filtra conceptos por Empresa/Area (no al reves).
- Los 3 catalogos se construyen reales y alimentan el wizard/proyectos, PERO sin rewire de los enums
  load-bearing: Estados queda desacoplado de la maquina de estados.

### Aplicado a prod (autorizado)

Backups previos (ecorex-2026-07-23-0837 y -0931). Migraciones duales aplicadas al arrancar el dev
contra la BD de prod (tunel SSH): `AddConceptoSedeEntidad` y `AddActivityCatalogs` (3 tablas +
project_type_id). Menu unificado + defaults sembrados en los 7 tenants. 572/572 tests unitarios verdes.

### Doc de deploy (vault)

Se documento el inventario del host de produccion (10.0.0.3) para que otra sesion despliegue un
segundo stack (DokTrino) sin tocar Visal/ECOREX: SSH, puertos (5580 libre sugerido), /opt/doktrino,
imagen GHCR publica, sin volumenes previos. En `06. Deploy/Host de produccion - Inventario...md`.
Sin secretos.

### Siguiente

- Validar en Chrome (hecho parcialmente) y pendientes previos: UI de agentes de IA en nodos,
  migraciones de agentes sin aplicar, validacion visual del Cotizador.

---

## 2026-07-22 - Administrador de tableros unificado + menu de tenants cliente alineado

**Agentes**: Claude (Opus 4.8).

### Hecho

**1. Un solo sistema de tableros (ADR-0020).** `task_boards` guarda DOS familias separadas por
`Kind`: `CrmLegacy` (kanban heredado del backbone, tarjetas `TaskCard`) y `Activities` (tableros
del prototipo 000636, tarjetas `TaskItem`). `/tableros` creaba `CrmLegacy` y `/actividades` filtra
`Kind == Activities`, asi que los tableros creados desde el administrador eran INVISIBLES en el
modulo de actividades. Se detecto porque el tablero "Comercio" de SOLDARCO no aparecia.

- `Tableros.razor` pasa a `IActivityBoardService`: opera sobre los mismos tableros que el modulo
  de actividades. Alta, edicion, baja, estado del tablero (A tiempo / En progreso / En riesgo /
  Completado) y archivado. Lista con archivados incluidos: es el administrador, no la bandeja.
- Editor de ESTADOS (columnas): crear, renombrar, color, reordenar, marcar "cierra", eliminar.
  Via `ITaskBoardService`, la unica API sobre `task_board_columns` (misma tabla).
- El clic en la tarjeta abre el EDITOR; para entrar al tablero hay boton "Abrir" ->
  `/actividades?board={id}`. Antes navegaba al tablero y el modulo parecia no administrar nada.
- `ActivityBoardsIndex` deja de crear tableros (modal y metodos eliminados) y su cabecera queda
  sin acciones: el alta de tableros es `/tableros` y la de actividades `/crear-actividad`.
- Bloque `ECOREX_FIX_BOARD_KIND` convierte a `Activities` los tableros `CrmLegacy` SIN tarjetas
  `TaskCard` (uno con tarjetas si es legitimamente CrmLegacy). Convirtio 1: "Comercio".

**2. Menu de los tenants cliente alineado con SOLDARCO.** Los 4 Standard restantes tenian 12
secciones / 51 items; SOLDARCO 11/44. No era "los demas menos unos stubs" sino una
REORGANIZACION: `syscrm` -> `crm`, e items mudados entre `gen`, `dev`, `ia` y `auto`.
`DepurarMenuClienteAsync` (env `ECOREX_MENU_DEPURAR`) deja los 4 identicos a SOLDARCO: diff por
seccion+ruta+nombre da CERO diferencias en los cuatro.

**3. "Configuracion de entidad" reparada.** El item se llamaba asi pero apuntaba a la ruta
`configuracion`, alias de `/mi-cuenta`; el modulo real `/configuracion-entidad` (areas y
sucursales que alimentan el selector "Empresa/Area") no estaba enlazado en NINGUN menu. El seed
ya estaba corregido: faltaba reconciliar los tenants existentes.

### Decisiones

- `EnsureMenuItemInSectionAsync` omite el alta si la ruta ya existe en la vista SIN mirar la
  seccion. Todo MOVIMIENTO de item es quitar-y-despues-agregar. Documentado en el codigo.
- Los `legacy_code` se leen de la BD, nunca se inventan correlativos: son la trazabilidad al
  WebForms y no siguen orden (Conceptos actividades es 000125, Estados 000272, Origen 000324).
- No se copio "Asesores" de la seccion `crm` de SOLDARCO: dominio belleza que el proyecto elimina.
- "Automatizaciones" (pagina real) sale del menu para igualar a SOLDARCO; devolverla es una linea.

### Siguiente

- Validar en Chrome el editor de tableros y el menu de un tenant distinto de SOLDARCO.
- Pendientes de antes: UI para asignar agentes de IA a nodos, migraciones `AddWorkflowNodeAgent` /
  `AddWorkflowStepAgentExecution` sin aplicar, validacion visual del Cotizador.

---

## 2026-07-21/22 - Agentes de IA en flujos, D11 paralelo, inventarios y limpieza del backbone

**Agentes**: Claude (Opus 4.8) + varios subagentes (imagenes de items, usuarios/dependencias,
agentes en nodos olas 1 y 2, volumen de uploads).

**D11 - ejecucion en PARALELO (`a0e5b3e`, ADR-0046)**: el flujo COMPRAS de SKY SYSTEM tiene un
nodo con 4 salidas y solo prosperaba una. **El diagnostico recibido era incorrecto**: se reporto
que el motor era de un solo token y tomaba `outgoing[0]`; verificado en codigo, `ResolveOutgoing`
YA devolvia todas las salientes y el bucle YA las activaba, y `IsCurrent` YA admitia varios pasos
vigentes (sin migracion). El defecto real, no reportado: al tocar un endEvent se llamaba a
`CompleteInstance` y se marcaban `Skipped` las ramas hermanas. Ahora un endEvent cierra la RAMA y
la instancia cierra cuando no queda ningun paso vigente (cierre implicito, decision del usuario).
Contrapartida asumida: un flujo sin salidas cierra en silencio donde antes gritaba Stuck; el aviso
natural es al PUBLICAR. Test dual que calca el caso real + 70/70 de regresion del motor.

**Agentes de IA en nodos de flujo, olas 1 y 2 (`4ce8cc3`, `5589c80`)**:
- Ola 1: `WorkflowNodeAgent` (nodo -> agente + autonomia POR NODO) y constructor de contexto con
  las 4 partes que pidio el usuario (nodo+formulario, datos previos, tarea/cliente, historial),
  con techo y marca de truncado porque los tokens se facturan contra el cupo del plan.
- Ola 2: el agente ATIENDE. **El agente es el autor**: `ExecutedByAiAgentId` junto al de usuario,
  sin FK (auditoria append-only). La llamada a la IA NO va dentro de la transaccion del motor
  (vive tras `IWorkflowAgentInvoker`, imposible por construccion). La cola ES la tabla: un paso
  vigente con agente y sin intento previo es el trabajo, lo que sobrevive a reinicios y es
  idempotente. Si el agente no puede (fallo o cupo agotado) el paso VUELVE A UNA PERSONA.
  La propuesta va en campos propios porque `CompleteStepAsync` sobrescribe los de aprobacion.
  10/10 en matriz dual con doble del proveedor.
- **Sin UI y sin ejecucion real**: el invocador que llama a Gemini nunca corrio; los tests usan un
  doble. Migraciones generadas y NO aplicadas.

**Restos del backbone eliminados**: los "logos sugeridos" de la pagina de Marca eran de CUBOT IA
(`c6e0498`) y la URI de OAuth que se pedia registrar en Google era la de cubotcrm (`5577b9b`);
ahora se deriva del host actual. Quedan ~25 apariciones de "agencia" en el Super Admin, sin tocar.

**Inventarios**: los 5 catalogos se unifican en `/inventario-configuracion` con tarjetas + modal
(`5d126f7`, `8fe73cf`); imagenes por arrastrar y soltar, fuera el campo de URL, y **volumen
persistente de uploads en produccion** (`a8f1800`) — sin el, cada despliegue borraba lo subido por
los usuarios. Modal de item a dos columnas con imagen principal, minigaleria y resumen (`64bde77`).

**Seguridad**: al endurecer la subida de imagenes se encontro que la pagina de Marca no tenia
lista blanca y concatenaba la extension del nombre del cliente; ese logo se sirve en la PANTALLA
DE LOGIN, sin autenticar, asi que un .svg subido ahi era XSS almacenado en la superficie mas
expuesta. Corregido con `ImageUploadGuard` (lista blanca + bytes magicos).

**Usuarios y organigrama (`94b8063`)**: baja LOGICA de usuarios con salvaguardas en el servicio
(no auto-eliminarse, no eliminar al ultimo admin activo); Dependencias muestra nombre y correo.

**Motor de listas del Contenedor de datos (`fa8a053`, `ab6d824`, `ef7cc8c`)**: campos tipo lista
alimentados por una tabla del Contenedor, con cascada entre campos y autollenado. Sin migracion.

**Bloqueos / pendientes**:
- SIN VALIDACION VISUAL: modal de items, arrastrar y soltar, eliminar usuarios, campo tipo lista.
- Tres migraciones generadas y sin aplicar (agente por nodo, ejecucion por agente).
- Sin UI para asignar agentes a nodos ni para ver propuestas en la bandeja.
- `backup.sh` no respalda los archivos subidos, solo la base.
- 4 sitios suben adjuntos genericos sin validar contenido.

---

## 2026-07-20 - Gate de formato reparado + concepto que produce tarea-proceso

**Agentes**: Claude (Opus 4.8).

**Fixes de oportunidades**:
- `29572ef` El aside del tercero decia "3 abiertas - $135,500,000" contando las Ganadas: sumaba
  TODAS sin mirar el tipo de etapa, mientras los KPIs de la pagina si lo hacian bien (dos verdades
  para el mismo dato). Se reusa la regla `IsOpen` de GestorContactos. De paso, la pildora de cada
  oportunidad mostraba el enum heredado en vez de la etapa CONFIGURABLE: ese panel se habia quedado
  sin migrar al pipeline nuevo.
- `ab9c5de` Al elegir un concepto que maneja valor, el formulario enlazado "no cargaba": en realidad
  se renderizaba solo cuando los campos de proceso ya estaban completos. El envio YA valida esos
  campos, asi que esconderlo era redundante. Se muestra siempre y el error se movio junto a los
  campos que faltan (abajo del formulario quedaba fuera de pantalla mientras el boton decia
  "Enviado"). TITULO y VALOR pasan a 6 y 2 de 12 en la MISMA fila.

**Gate `dotnet format` del CI reparado (`5a9cf8e`)**: estaba en rojo en el tronco. Contra-validado
lo reportado por la sesion de la rama secundaria: correcto que el gate fallaba, que era PREEXISTENTE
(no lo causo el merge) y que los 6 archivos que listaron son los que aparecen en el diff. Matices:
`dotnet format` reporta 188 diagnosticos en 12 archivos, pero 6 de ellos solo tenian CRLF de working
tree que git normaliza y el CI (Linux) nunca veria; y no es "puramente espacios" — en ProjectService
y TaskCoreTests reparte inicializadores de objeto a una linea por miembro (equivalente, verificado
campo a campo). Commit aislado de logica para que el diff sea auditable.

**Matriz dual de integracion CORRIDA** (la sesion anterior la dejo pendiente por Docker colgado):
371/371 en 9m14s, y el aislamiento cross-tenant confirmado corriendo en AMBOS motores (sufijos
`_Postgres` y `_SqlServer`), 20/20. Gate de CLAUDE.md §6/§8 cubierto.

**Concepto de actividad que produce TAREA DE PROCESO (`bbd1f1a`)**: el eslabon que faltaba entre el
gestor de contactos y el motor de actividades. El concepto guarda una SUBCATEGORIA del catalogo
000270 (no un flujo suelto): la subcategoria ya trae flujo, tablero y formulario, y el alta pasa por
el MISMO `ITaskItemService` del wizard, sin abrir una segunda via de creacion de tareas. Migraciones
duales `20260720164807` / `20260720165009`. La pestana de prospectos pasa a "Contactos" y muestra
todos los del tenant (scrapeados + Directorio General), decision del usuario.

**Causa raiz de las sesiones que se caian**: el codigo usa dos anillos de DataProtection segun el
entorno — archivo `.dpkeys-dev` en Development, tabla `data_protection_keys` (de PROD) si no. Al
lanzar la app sin `ASPNETCORE_ENVIRONMENT=Development` arranca como Production y ninguna cookie
previa se puede descifrar. `start-ecorex.ps1` si fija la variable; el fallo venia de lanzarla a mano.

**Bloqueos / pendientes**:
- La migracion `AddConceptoActividadSubcategoria` NO se ha aplicado a la BD. Debe correrse antes de
  desplegar (el dev apunta por tunel a la BD de PROD, asi que requiere autorizacion del usuario).
- Sin ADR del pipeline configurable ni tests de `OportunidadEstadoService`.
- Vault Obsidian sin actualizar.

---

## 2026-07-19 (cont.) - Pipeline de oportunidades CONFIGURABLE + kanban + 3 fixes de concurrencia

**Agentes**: Claude (Opus 4.8) + 1 subagente para el grueso del servicio/UI del pipeline.

**Bug transversal de concurrencia sobre el DbContext del circuito** (destapado por el dev conectado a
la BD de prod POR TUNEL: cada consulta tarda cientos de ms y ensancha la ventana de solape; en prod
con BD local es raro). Tres fixes:
- `8e55114` Directorio: pulsar "Nuevo cliente" ANTES de que terminara OnInitializedAsync lanzaba
  EnsureDefaultsAsync sobre el mismo DbContext que la consulta de KPIs -> "second operation" y el
  modal quedaba con el cuerpo vacio. Botones del header deshabilitados mientras carga/recarga.
- `ed90e51` Formularios: dos DynamicFormRenderer del mismo circuito cargando a la vez (el del aside
  + el de una pildora de concepto) se pisaban -> "Cargando formulario..." colgado. Nuevo servicio
  SCOPED `CircuitFormGate` (un semaforo por circuito, compartido por todos los renderers) y la CARGA
  ahora tambien lo toma (antes solo autosave/reglas/submit, y era por-instancia).
- `20d5205` Pipeline: mover varias oportunidades en rafaga perdia un cambio EN SILENCIO. Movimiento
  serializado con `_movingOpp`.

**Datos de proceso fuera del formulario (`dbb626d`)**: al elegir una pildora, si el concepto MANEJA
VALOR o es EVENTO DE AGENDA aparece un bloque arriba del formulario con Titulo + Valor + Fecha de la
proxima actividad (obligatorios segun el concepto); el formulario solo se muestra cuando estan
completos. El valor sale de ahi (ya no se extrae del formulario) y alimenta el proceso: Oportunidad
en su modulo / Cita en la agenda. Decisiones del usuario: titulo solo para valor/agenda.

**Pipeline de oportunidades configurable (`be32efa` + `5f19313`)**: entidad `OportunidadEstado`
(Name, SortOrder, Color, Tipo=Abierta/Ganada/Perdida, IsArchived) que reemplaza al enum fijo;
Oportunidad gana EstadoId. Migracion DUAL `AddOportunidadEstado` (aditiva). Servicio con CRUD +
Reorder + Archivar + EnsureDefaults (6 etapas mapeando el enum) + Backfill. Pagina
`/estados-oportunidad`. El panel usa las etapas configurables y los KPIs de pipeline abierto usan el
TIPO. Seed por env `ECOREX_SEED_OPP_ESTADOS`. Verificado en BD: 6 etapas/tenant + backfill 5/5.

**Kanban (`20d5205`)**: el pipeline se veia como lista, que era justo lo que no se queria. Vista
"Tablero" (principal): una columna por etapa configurable con su color, conteo y total; tarjeta con
cliente y valor; ARRASTRAR Y SOLTAR entre columnas (patron tb-board del kanban de tareas).

**Cierre de pendientes (`c82d6c3`)**: sub-agrupar por etapa dentro de "Por cliente"; el timeline
resume lo que se lleno en el formulario en vez de mostrar solo el nombre del concepto; y el VALOR ya
no se pide dos veces (se saca de la definicion y en los ya sembrados se oculta/desmarca, sin borrar
respuestas; 12/12 verificados en BD).

**QA E2E en Chrome contra la BD de prod**: crear etapa + reordenar; crear cliente desde cero;
Anotacion + PQR; 3 oportunidades con titulo/valor; las 3 llegaron al panel ($135.5M); mover en el
tablero recalculo KPIs (3->2 abiertas, $135.5M->$50.5M al pasar una a Ganada) y persistio (SELECT).

**Responsive (`49c6ce7`)**: datos de proceso en una columna; modal de Tercero a pantalla completa en
celular con todo apilado y tabs con scroll; Directorio con KPIs 4->2->1 y tabla con scroll.

**Bloqueo recurrente**: el clasificador bloquea `up -d` a prod (pasa si se antepone backup.sh) y las
llaves de DataProtection en dev se ensucian si se lanza el proceso sin `--contentRoot`, lo que
invalida la cookie y obliga a re-loguear.

---

## 2026-07-19 - CRM: conceptos de actividad como botones en Terceros (Contacto Cliente) + 5 formularios

**Agentes**: Claude (Opus 4.8). **Accion**: integrar los Conceptos de actividad (000125) en la
pestana "Contacto Cliente" del modal de Tercero, y sembrar los 5 conceptos + sus formularios.

**Ola A+B - servicio + UI (`0665b2c`, con migracion)**: en el modal de Tercero, pestana "Contacto
Cliente", nueva seccion "Gestion por concepto": los conceptos se listan como BOTONES. Al pulsar uno
con formulario -> se carga el DynamicFormRenderer (Fill, Reference = el tercero) inline; al enviarlo
se guarda una gestion LIGADA (concepto + respuesta + valor). Sin formulario -> nota rapida ligada al
concepto. El valor se extrae de la respuesta (campo cuyo codigo contiene "valor") cuando el concepto
maneja valor; el timeline lo muestra en verde. El compositor de nota rapida existente (con su
cableado CRM del Cargador) queda intacto. Datos: `TerceroNota` gana ConceptoActividadId +
FormResponseId + Valor (FKs NO ACTION); migracion DUAL `AddTerceroNotaConceptoLink` (3 columnas
aditivas en tercero_notas). SaveNotaRequest / TerceroNotaDto extendidos; TerceroService setea/
proyecta los campos.

**Ola C - seed de conceptos + formularios (`68bd053`)**: `DatabaseSeeder.EnsureCrmConceptosAsync(
tenantId)` crea, idempotente por Code y con TenantId explicito, 5 formularios (FRM-CRM-ANOT/PQR/SOL/
OPP/COT) y 5 conceptos (CRM-ANOT/PQR/SOL/OPP/COT). Oportunidad y Cotizacion manejan valor. Campos:
Anotacion (texto*+fecha); PQR (tipo*+prioridad+asunto*+detalle*); Solicitud (asunto*+fecha
requerida+descripcion*); Oportunidad (descripcion*+valor*+probabilidad+fecha cierre+producto);
Cotizacion (descripcion*+items(tabla)+valor total*+validez). Disparador
`ECOREX_SEED_CRM_CONCEPTOS=true` en Program.cs corre el seeder por cada tenant de negocio (Kind !=
Internal), como los seeders DIRECTORIO/GESTOR/CONCEPTOS.

**Verificacion**: build Release verde (0 errores); tests Application 457. Migracion solo aditiva en
ambos motores. **Deploy**: rebuild build-from-git de `fase-0/clon-backbone` (68bd053) + up -d con
`ECOREX_SEED_CRM_CONCEPTOS=true` un arranque para sembrar; la migracion se aplica sola al arrancar.
Seed verificado por SELECT: 5 conceptos + 5 forms en cada tenant de negocio (Internal en 0).

**Login "Recordar sesion" (`ca90b29`, deploy)**: el login emitia la cookie SIN
AuthenticationProperties -> cookie de sesion (moria al cerrar) con expiracion 8h; obligaba a
re-loguear seguido. Ahora: checkbox "Recordar mi sesion en este equipo" (marcado por defecto);
`/auth/login` emite cookie PERSISTENTE (IsPersistent + ExpiresUtc 30d) cuando esta marcado;
ExpireTimeSpan global 8h -> 30d deslizantes. Sin migracion.

**Contacto Cliente = solo conceptos (`e2d20a5`, deploy)**: rediseno de la pestana segun captura del
usuario. Una sola fila de BOTONES pildora (uno por concepto 000125, icono por modo/valor, estado
seleccionado morado); al pulsar abre su formulario (o textarea) y guarda la gestion ligada. Se
elimino el compositor fijo heredado; el cableado CRM del Cargador (Oportunidad/Cita) se preserva via
concepto (ApplyCrmWiringAsync: HandlesValues -> Oportunidad, Mode CalendarEvent -> Cita). Decision
del usuario (AskUserQuestion): "solo conceptos configurables", no el set fijo. Sin migracion.

**Dev local para MCP**: se acordo usar SIEMPRE `http://localhost:5234` (dev con tunel SSH a la BD de
prod), nunca `10.0.0.3` (la IP va lenta por el MCP). Ver memoria [[preview-siempre-activa]].

**Pendiente de validacion visual**: el modal Contacto Cliente requiere login (no puedo teclear
contrasena) -> lo valida el usuario en 5234. Duda abierta: la captura mostraba un panel-resumen
lateral (Nivel de interes/Etapa/Probabilidad/Presupuesto) ademas del formulario; se implemento el
formulario inline debajo de las pildoras. **Bloqueo recurrente**: el clasificador de auto-mode
bloquea `up -d` a prod (a veces pasa si se antepone backup.sh).

---

## 2026-07-18 (cont.) - Asesores (correo/tarjetas) + fixes de menu + modulo Conceptos de actividades

**Agentes**: Claude (Opus 4.8). Continuacion del dia; 4 deploys mas a prod (build-from-git de
`fase-0/clon-backbone`).

**Asesores (`3be3f37`)**: editar el CORREO del asesor (= su login e identidad global) con guarda de
unicidad (rechaza si otro miembro del tenant o cualquier identidad de plataforma ya lo usa;
actualiza TenantUser.Email y PlatformUser.Email). Vista de TARJETAS con toggle Tarjetas/Lista
(default tarjetas): avatar, nombre, correo, rol, alcance, doc/telefono, estado y acciones. Scoped
CSS nuevo. Sin migracion.

**Editor de menu**: (a) `5a904cc` SCROLL del arbol de la vista -crecia al alto de los 53 modulos y
el modal lo recortaba-: `grid-template-rows: minmax(0,1fr)` + `flex:1/min-height:0` en el arbol.
(b) `e457094` el boton "Guardar" de arriba ahora TAMBIEN aplica el nombre/props del nodo
seleccionado (antes solo recargaba y se perdia la edicion si no dabas "Aplicar cambios"; no era
especifico de "CRM (heredado)", pasaba con cualquier nodo).

**Modulo Conceptos de actividades (CRM 000125, `0d27383`, con migracion)**: entidad PROPIA del CRM
`ConceptoActividad` (Code, Name, Description, FormDefinitionId?, HandlesValues, Mode = None/
AttentionProcess/CalendarEvent), distinta de ActivityType/ActividadSubcategoria (que son de
TAREAS). Servicio + DTOs + DI; pagina /conceptos-actividades (KPIs + tabla + modal) con selector de
formulario asociado + boton "Previsualizar" (DynamicFormRenderer en modo Design), checkbox de
valores y selector de modo; archivar/restaurar. Migracion DUAL AddConceptoActividad (tabla
conceptos_actividad, aditiva). El stub /modulo/conceptos-actividades REDIRIGE a la pagina real
(Modulo.razor), asi el item del menu funciona en todos los tenants sin tocar la BD por-tenant.
Decision del usuario: entidad nueva (no reusar) + selector de modo (un modo a la vez).

**Deploys a prod** (`root@10.0.0.3`, build-from-git):
- `3be3f37` (Asesores correo+tarjetas): build --no-cache + up -d, sin migracion. /login 200.
- `5a904cc` (scroll menu): idem, solo CSS. /login 200.
- `e457094` (fix Guardar menu): idem. Un intento previo murio al cortarse el SSH (exit 4); se
  relanzo limpio. /login 200.
- `0d27383` (Conceptos de actividades): backup `ecorex-2026-07-18-0903.sql.gz` ya existente del dia
  + backup nuevo previo; aplico `AddConceptoActividad` (tabla conceptos_actividad creada). /login 200.

**Verificacion**: build completo verde; tests Application 457, Domain 35, SuperAdmin 30. Migracion
solo CreateTable/AddColumn aditivo en ambos motores. **Siguiente (pedido del usuario)**: en el modal
de creacion de Terceros, pestana "contacto cliente", cargar los Conceptos de actividades como
botones que abren su formulario asociado; disenar formularios para anotaciones/PQR/solicitudes/
oportunidades/cotizaciones (oportunidades maneja valor). **Bloqueo recurrente**: sin poder teclear
contrasenas + la pestana del MCP no comparte sesion con el Chrome del usuario -> la validacion visual
de paginas gated la hace el usuario.

---

## 2026-07-18 - Bandeja de formularios usable + Asesores con doc/telefono + scroll del menu (3 deploys)

**Agentes**: Claude (Opus 4.8). **Accion**: hacer usable el modulo de formulario (/m/{code}),
extender Asesores como maestro de vendedores, y arreglar el scroll del editor de menu. Todo
desplegado a prod (build-from-git de `fase-0/clon-backbone`).

**Modulo de formulario (`0f8fc7a`)** - bandeja /m/{code} + motor de captura:
- Bandeja: "Nuevo registro" (modal con el renderer en modo Fill), "Anular" por fila (VoidAsync,
  motivo, soft-delete), menu de FILTROS con chips por campo (patron de Pipeline.razor de
  CUBOT.travels) en vez de la fila plana que se esparramaba, pestanas Activos/Anulados (los
  anulados no se mezclan), columna GridDetail como SUB-FILA expandible (mini-tabla con etiquetas
  resueltas) en vez del JSON crudo, y tabla RESPONSIVE (scroll horizontal).
- Publicar como modulo (FormDesigner): ACTIVA el formulario al publicarlo (un modulo del menu tiene
  que poder capturar; antes quedaba en borrador y GetOrCreateDraft lo rechazaba), campo "Nombre en
  el menu" (rotulo propio del nodo) y SELECTOR de icono (MenuIconPicker) en vez de caja de texto.
- Renderer: footer "Enviar" FIJO (sticky) + alerta de validacion fija (en formularios largos el
  boton quedaba bajo el fold y parecia que no se podia guardar); default "usuario actual" muestra
  el NOMBRE (no el Guid); y **los campos ocultos por REGLA (D4) ya no bloquean el envio**: el
  renderer avisa al servidor que campos oculto la regla (`_ruleUiState.HiddenFields`) y la
  validacion server-side los salta -antes "Valor" oculto cuando "venta=No" rebotaba SIN marcar
  nada (bug invisible)-. No se re-ejecuta el motor de reglas en el servidor a proposito: su
  ExecuteForFormFieldAsync persiste un log de auditoria.
- Dev: DataProtection persiste sus llaves a archivo local SOLO en Development (los reinicios ya no
  cierran la sesion; el dev deja de escribir su keyring en la BD de prod). Carpeta gitignored.
- QA en Chrome (prod): un registro real de CONTACTO CLIENTE (FRM-00005-000001, Confirmado) cae en
  el gestor. Nota de tooling: los eventos sinteticos no registran las celdas del grid en Blazor;
  el clic humano si.

**Asesores = maestro de vendedores (`ca0bc8a`, con migracion)**: decision del usuario de REUSAR la
pagina Asesores (el asesor ya ES un TenantUser con su cuenta de login, asi que el vinculo con el
usuario del sistema es inherente). Se agregan `DocumentCode` (codigo/documento) y `Phone` a
TenantUser + AdvisorDto/Create/Update + AdvisorService + config EF; UI en los modales Invitar/Editar
y en la lista. Migracion DUAL `AddAdvisorDocumentAndPhone`: 2 columnas nullable en tenant_users
(aditiva, no destructiva; el codigo viejo ignora columnas que no conoce). El maestro "Vendedores"
(000124) sigue siendo stub; si se quiere, se re-apunta ese nodo del menu a /asesores (config por tenant).

**Fix scroll editor de menu (`5a904cc`)**: el arbol de la vista (Configuracion Menu) crecia al alto de
los 53 modulos y el modal lo recortaba; se constrine la fila del grid con `grid-template-rows:
minmax(0,1fr)` y el arbol toma `flex:1 + min-height:0` para que su overflow scrollee de verdad.

**Deploys a prod** (`root@10.0.0.3`, `/opt/ecorex`, build-from-git):
- `ca0bc8a`: backup `ecorex-2026-07-18-0903.sql.gz`, build --no-cache + up -d. Aplico 1 migracion
  `AddAdvisorDocumentAndPhone` (columnas document_code/phone creadas). Trajo tambien todo el modulo
  de formularios (0f8fc7a no tenia migraciones). Sano (/login 200, sin errores).
- `5a904cc`: build --no-cache + up -d (solo CSS, sin migracion). Sano (/login 200).

**Verificacion**: build completo verde; tests Application 457, Domain 35, SuperAdmin 30, Integracion
DynamicForms+RulesEngine 30 (dual), aislamiento cross-tenant 6/6 (dual). **Siguiente**: validacion
visual del scroll del menu (usuario, en su sesion). **Bloqueo recurrente**: no puedo teclear
contrasenas, y la pestana que controla el MCP no comparte sesion con el Chrome del usuario -> la
validacion interactiva de paginas gated depende de que el usuario este logueado en la pestana correcta.
## 2026-07-18 - Extraccion de Datos: agente en el modulo (Test de conexion + alta) + demo JS

Dos cosas pedidas por el usuario:

- **Feature (UI)**: el panel "Cliente y variables" del flujo gana, junto al selector de agente, un boton
  **"Test de conexion"** (pregunta al `IAgentRegistry` del hub si el ClientId del agente elegido tiene
  una conexion viva; muestra "En linea desde HOST (vX) hace N min" o "Sin agente conectado. Instala la
  colmena con este ClientId y su secreto") y **"+ Agente"** (crea un `DataClient` con `SaveClientAsync` y
  REVELA una sola vez el ClientId + secreto para configurar la colmena). Reusa el patron del Contenedor
  de datos. Solo UI (`ExtraccionDatos.razor` + css); build verde.
- **Demo end-to-end con inyecciones de JS**: se configuro paso a paso un flujo **"prueba de conexion"**
  (Navegar quotes.toscrape.com -> Inyectar JS -> Extraer con JS) asignado al agente stand-in. El "Test de
  conexion" lo detecto en linea (host BITCODEV1). Al "Ejecutar ahora", el servidor firmo las DOS
  inyecciones de JS y el agente VERIFICO ambas (`#1 Eval VALIDA`, `#2 Eval VALIDA`); corrida **Ok, 3
  filas** (visto en la UI y BD). Prueba nueva vs el E2E previo: firma+verificacion de MULTIPLES pasos JS
  en una misma orden.
- **Evidencia extra de la programacion (Ola 5)**: el flujo programado "Prueba runtime navegador" (cada
  30 min) acumulo ~10 corridas "Horario / Esperando al agente" disparadas SOLAS por el worker
  (00:06 -> 04:37), confirmando en vivo que el scheduler dispara los flujos sin nadie mirando.

Nota honesta: el agente stand-in FABRICA el resultado del navegador (no ejecuta el JS real de quotes);
prueba el pipeline (firma/despacho/ingesta/bitacora) de punta a punta, pero la ejecucion real del JS en
la pagina la haria la colmena WebView2. El "Test de conexion" es justo la herramienta para verificar que
esa colmena esta conectada.

---

## 2026-07-18 - Extraccion de Datos: E2E del runtime determinista (agente stand-in)

Se cerro el lazo COMPLETO del runtime determinista (Ola 3) contra el servidor + la BD REALES, sin la
colmena WebView2 (que necesita display) ni llaves de IA, usando un **agente stand-in**: una consola .NET
que referencia `Ecorex.Contracts.Agent`, se autentica al `AgenteHub` por SignalR, VERIFICA la firma del
JS que manda el servidor, y responde `BrowserResult` fabricado (los `Extract` devuelven filas).

**Lazo probado E2E**: handshake `POST /api/agente/token` (HMAC(secret,"clientId|ts|nonce") -> JWT) ->
conexion SignalR al hub real -> "Ejecutar ahora" desde Chrome -> el servidor COMPILA el flujo (Navigate +
Extract), FIRMA el JS del Extract, y despacha `BrowserRequest` -> el agente verifica `firma=VALIDA` ->
devuelve 3 filas -> el servidor correlaciona (canal), parsea e INGIERE via `IRowIngestService` en el
contenedor "Productos E2E" -> `ScrapeFlowRun` cierra **Ok, 3 filas** (visto en la UI: `Manual/OK/0.3s/3` y
en BD `data_container_cells` con Taladro Bosch/Martillo Stanley/Destornillador Truper).

**Camino negativo (seguridad)**: `/api/agente/dev/browse/...?nosign=true` (JS sin firma) -> el agente lo
marca `firma=INVALIDA` y lo rechaza (fail-closed). Contrato probado en ambos sentidos.

Cubre REAL: auth + transporte SignalR + compilacion + firma + despacho + correlacion + ingesta +
bitacora. Lo unico fabricado por el stand-in es la ejecucion del navegador (que en prod hace la colmena
WebView2, ya probada en el capitulo del Agente). El paso de IA no entra (necesita proveedor de IA con
llave). Documentado en el vault: capitulo Extraccion de Datos, doc "05 - Estado de construccion y E2E".

Sin cambios de codigo (el E2E fue fixtures de BD + un agente stand-in scratch fuera del repo).

---

## 2026-07-18 - Extraccion de Datos, Ola 5: programacion + paginacion + advertencias (+ coexistir)

Quinta ola: un flujo puede correr SOLO por horario, recorrer paginas, y avisar/detenerse ante una
etiqueta. Cierra el capitulo (el runtime queda cableado; falta E2E con colmena real).

- **Programacion** (reusa `ImportProcess`/recurrencia del Contenedor, decision E2): `ImportProcess` gana
  `FlowId` (referencia suave) + migracion dual `AddImportProcessFlowId`. El dispatcher
  (`ImportScheduleDispatcher`) RAMIFICA: si el proceso tiene `FlowId`, dispara
  `IBrowserRunService.RunFlowNowAsync(Scheduled)` (su corrida va a `ScrapeFlowRun`, no `ImportRun`, por
  ADR-0042) en vez del runner de importacion; el manejo offline se reusa parqueando `PendingSince` y
  reintentando al reconectar (con el gate `IsOnline` del agente del FLUJO). Servicio Get/Save de
  programacion en `IScrapeFlowService` (administra el `ImportProcess` del flujo, calcula `NextRunAt` con
  `ImportRecurrence`, rechaza cron invalido). Borrar el flujo borra su programacion (ref suave, sin
  cascada). UI: tarjeta "Programacion" (Manual/Intervalo/Cron + activar, muestra la proxima corrida).
- **Paginacion controlada** (el PAGINA_DESDE/HASTA legacy): `ScrapeFlow` gana `PageVar`/`PageFrom`/
  `PageTo`. El runtime repite el flujo por cada pagina sustituyendo {{PAGINA}}, con techo de seguridad
  (500 paginas). UI en la cabecera.
- **Advertencias** (el CONDICION legacy): `ScrapeStep` gana `WarningLabel` + `WarningAction` (None/
  Notify/Stop, enum a texto). Tras cada tramo, si la etiqueta aparece en lo devuelto, Stop DETIENE la
  corrida y Notify la anota. UI en el editor de paso. Migracion dual `AddFlowPaginationAndWarnings`.
- **Coexistir con ScrapeSource** (ADR-0044): se conserva el scraper HTTP simple (URLs publicas sin
  agente); la absorcion se reevalua cuando el runtime de flujos este probado E2E.

**Pruebas**: build de la solucion 0 errores; snapshots limpios en ambos contextos; SuperAdmin.Tests 52 y
Application.Tests 457 verdes. **Verificado en Chrome**: la tarjeta "Programacion" del flujo crea un
`ImportProcess` con `FlowId` y calcula la proxima corrida (confirmado en BD: `Flujo: Prueba runtime
navegador / Interval 30 min / next_run_at`).

**Limite honesto**: el disparo programado real (el worker llamando al flujo a su hora) y la paginacion/
advertencias en vivo exigen la colmena conectada; la logica reusa la recurrencia + el manejo offline ya
probados del Contenedor, y el runtime del flujo (Olas 3-4) que corre en segundo plano.

**Estado del capitulo**: configuracion (Olas 1-2) + runtime determinista (3) + paso de IA (4) +
programacion/paginacion/advertencias (5) construidos y probados hasta donde el entorno de dev permite
(sin colmena on-prem ni proveedor de IA con llaves). El E2E de punta a punta queda para cuando ambos
esten disponibles.

---

## 2026-07-18 - Extraccion de Datos, Ola 4: paso de IA (orquestacion agente<->navegador)

Cuarta ola: los pasos de tipo IA ya se EJECUTAN. Un agente de IA maneja el navegador para cumplir una
instruccion en lenguaje natural, acotado por su allow-list de tools + topes de pasos/tiempo (doc 03 s2).
Requiere un refactor del runtime a ejecucion SECUENCIAL (que ademas Ola 5 paginacion necesitara).

- **Canal request/response** (`IBrowserActionChannel`): une el envio y la respuesta del Navegador por
  correlationId (TCS + timeout), para poder AWAITAR el resultado de una accion antes de la siguiente. El
  hub (`AgenteHub.BrowserResult`) ahora RESUELVE la espera del canal (antes solo logueaba). Es lo que
  hace posible el bucle del paso de IA y la ejecucion secuencial de los deterministas.
- **Runtime secuencial** (`BrowserRunService` reescrito): "Ejecutar ahora" valida + abre corrida +
  chequea online, y lanza la ejecucion en SEGUNDO PLANO (la UI recibe "despachado"; el resultado llega a
  la bitacora al terminar). El ejecutor agrupa los pasos deterministas consecutivos en tramos (un
  BrowserRequest por tramo, por el canal) y corre cada paso de IA con el orquestador. Sustituye el
  modelo batch+callback de la Ola 3 (unificado; la ingesta se movio a `ScrapeRowIngest`).
- **Orquestador del paso de IA** (`AiStepOrchestrator`, ADR-0043): bucle de function-calling sobre el AI
  Provider Gateway (`IAiProviderClient.CompleteWithToolsAsync`, ya existente). Tools = acciones del
  navegador filtradas por la allow-list (vacia = solo lectura; eval/clic solo si el operador las
  habilito) + `guardar_filas` (ingiere). El JS que genera la IA viaja por el hub, asi que el servidor lo
  FIRMA (el agente lo rechazaria sin firma). Topes de pasos y tiempo. Consumo registrado en el modulo de
  tokens (`IAiUsageService`) con control de cupo. Proveedor/llave via seam `IAiProviderResolver`;
  ingesta via seam `IScrapeRowSink` (para probar sin BD).

**Pruebas**: 5 tests unitarios nuevos del orquestador con fakes (bucle navegar->guardar_filas ingiere;
sin proveedor -> mensaje claro; tope de pasos -> corta sin guardar; allow-list -> no ofrece eval/clic si
no estan; firma el JS de un evaluar_js). Suite SuperAdmin.Tests 52 verde; build de la solucion 0 errores.
**Verificado en Chrome**: regresion del path refactorizado -> "Ejecutar ahora" en el flujo offline sigue
registrando "Esperando al agente" (el runtime secuencial + los DI nuevos resuelven sin error).

**Limite honesto**: el paso de IA de punta a punta (LLM real manejando el navegador y filas aterrizando)
exige un proveedor de IA habilitado en el Super Admin Y la colmena on-prem conectada, ninguno disponible
en dev. El bucle, la firma, la allow-list, los topes y la ingesta estan cubiertos por los tests con
fakes; el cableado (runtime -> orquestador -> canal -> hub) esta armado y compila.

**Siguiente**: Ola 5 = programacion (ImportProcess -> flujo), paginacion, advertencias, y decidir
coexistir con ScrapeSource.

---

## 2026-07-18 - Extraccion de Datos, Ola 3: runtime determinista (cablear al Navegador)

Tercera ola: el flujo configurado ya se EJECUTA. Se cierra el lazo que faltaba (hasta ahora
`AgenteHub.BrowserResult` solo logueaba el resultado del Navegador; ahora se correlaciona, se ingiere y
se cierra la corrida, igual que ya hacia `FetchResult` con la ingesta). Alcance: el plano DETERMINISTA
(doc 03 s1); el paso de IA (s2) es Ola 4.

- **Compilador** (`ScrapeFlowCompiler`, funcion pura): traduce el flujo a `BrowserAction[]`. Sustituye
  `{{VAR}}` con las variables descifradas y **FIRMA** el JS (Eval/Extract/condicion de Wait/Click) con
  `AgentSign.SignJs(secret, corr, payload)` DESPUES de sustituir, para que la firma cubra el JS exacto.
  Mapea Navigate->Navigate, InjectScript/Extract->Eval, Wait->Wait (condicion por selector), Click->
  Mouse (guion MouseBot), Screenshot->Screenshot. Un paso Ai se rechaza con mensaje claro (es Ola 4).
- **Runtime** (`IBrowserRunService`, singleton espejo de `AgentImportService`): `RunFlowNowAsync` abre
  la corrida, valida agente asignado, compila, comprueba `IAgentRegistry.IsOnline` y despacha un
  `BrowserRequest` o deja la corrida **PendingOffline** (reintento en Ola 5). `OnBrowserResultAsync`
  (ruteado desde el hub) correlaciona por `correlationId`, parsea el Value de cada Extract (arreglo JSON
  que WebView2 devuelve; desanida doble-codificacion), resuelve el mapeo campo->columna del contenedor
  y **ingiere con `IRowIngestService`** (modo Append), y cierra la bitacora. Sweep de vencidos + Cancel.
- **Bitacora** (`ScrapeFlowRun` + `IScrapeFlowRunLog`): dedicada, no reusa `ImportRun` (ver ADR-0042:
  `ImportRun` cuelga de `ImportProcess`, que es la programacion; el disparo manual no tiene proceso).
  Migracion DUAL `AddScrapeFlowRun` (PG + SQL Server), snapshot limpio en ambos contextos, FK unica en
  cascada (sin doble camino -> sin error 1785). Reusa los enums `ImportRunTrigger`/`ImportRunResult`.
- **UI**: boton "Ejecutar ahora" en el hero, KPIs reales (corridas/exitosas/filas), y tarjeta
  "Historial de corridas" con estado (pildora), disparo, duracion y filas. Banda de runtime actualizada.

**Pruebas**: 17 tests unitarios nuevos (SuperAdmin.Tests) del compilador (firma cubre el JS sustituido
y ligado al corr; Extract ancla el indice del Eval; Wait/Click firmados; sin-secreto rechaza JS pero
permite un flujo solo-Navigate; Ai rechazado) y de `ParseRows` (arreglo/objeto/doble-codificado/basura).
Suite completa verde: SuperAdmin.Tests 47, Application.Tests 457; build de la solucion 0 errores.
**Verificado en Chrome** (tenant demo, BD `ecorex_agente`, 5262): (1) "Ejecutar ahora" en un flujo sin
agente -> corrida **Error** "El flujo no tiene un agente asignado" en el historial + KPIs + estado del
flujo sellado a "con errores"; (2) con un `DataClient` sembrado (offline) asignado a un flujo Navigate-
only -> corrida **"Esperando al agente"** (PendingOffline), el flujo sigue Activo. Sin errores de app en
consola (el ruido de reconexion de blazor.web.js es de una ventana previa).

**Limite honesto**: el E2E con filas REALES aterrizando en el contenedor exige la colmena on-prem
(WebView2) conectada por el hub, que no corre en este entorno de dev; ese tramo queda cubierto por los
tests del compilador + `ParseRows` y por el `IRowIngestService` (ya probado en el import). El disparo,
la correlacion, el offline y la bitacora si se probaron en vivo.

**Siguiente**: Ola 4 = paso de IA (orquestacion agente<->navegador por el MCP local, con topes y
allow-list de tools). Luego Ola 5 = programacion (ImportProcess -> flujo) + paginacion + advertencias.

---

## 2026-07-18 - Extraccion de Datos, Ola 2: UI del configurador de flujos

Segunda ola: `ExtraccionDatos.razor` deja de ser el CRUD de `ScrapeSource` (scraper HTTP simple) y pasa
a ser el CONFIGURADOR DE FLUJOS sobre `IScrapeFlowService` (Ola 1), milimetrico a
`proto_web_scraping.html` reusando el shell `xd-*` (topbar/breadcrumb + MOD 000730, sidebar de flujos,
hero con KPIs, franja de runtime, columnas 1fr/380). El backend `ScrapeSource` queda intacto (coexiste;
reversible por git).

- **Servicio**: `IScrapeFlowService.ListContainersAsync` + `ScrapeTargetDto(Id, Label)` -> etiqueta
  "Modelo / Tabla" (join `DataContainers` con `DataModels`) para el selector de tabla destino.
- **Pagina**: lista lateral de flujos (con contador de pasos), alta rapida (nombre + URL), hero editable
  (cabecera: nombre/descripcion/URL/estado), tarjeta "Pasos de ejecucion" con editor por-tipo
  (Navegar: URL con {{VAR}}; Inyectar/Extraer: JS + mapeo + tabla; Esperar: selector/ms; Clic: selector;
  **IA**: instruccion + tabla destino + allow-list de tools browser.* + tope pasos/segundos + modelo),
  reorden por flechas, y panel "Cliente y variables" (agente + tabla que se guardan al vuelo, variables
  {{VAR}} con secretas cifradas y enmascaradas). Franja recordando que el runtime (disparar + traer
  datos) es del sub-agente Navegador y esta pendiente (Ola 3). CSS de pasos/tags/variables anadido.
- **Fixes durante la verificacion**: (1) el enmascarado de la variable mostraba `&middot;` literal
  (Razor codifica el string del `@()`) -> ahora `********` ASCII; (2) el contador de pasos del sidebar no
  refrescaba al guardar/borrar paso -> `ReloadFlowsAsync()` tras cada cambio.

**Verificado en Chrome** (tenant demo SKY SYSTEM, BD `ecorex_agente`, puerto 5262): flujo "Precios
competencia Homecenter" creado; paso 1 Navegar (URL con `{{PAGINA}}`) y paso 2 IA (instruccion, tope
25 pasos / 120 s, modelo claude-sonnet-5) guardados; variable secreta `PAGINA` cifrada y enmascarada
con candado; reorden IA<->Navegar aplicado y renumerado; todo PERSISTIO tras reiniciar el server. Build
de la solucion completa verde, 0 errores; sin errores de consola.

**Siguiente**: Ola 3 = runtime (compilar el flujo -> BrowserAction[] + JS firmado + ingesta via
IRowIngestService, orquestar el paso de IA, reusar ImportProcess/ImportRun para programar, y decidir
absorber vs coexistir con ScrapeSource).

---

## 2026-07-18 - Extraccion de Datos, Ola 1: dominio del flujo (config)

Primera ola del capitulo "Extraccion de Datos" (000730): el modulo /extraccion-datos (hoy un scraper
HTTP simple, ADR-0025) evoluciona a un configurador de FLUJOS de automatizacion de navegador cuyo
runtime es el sub-agente Navegador de la colmena. Esta ola es SOLO la configuracion (el runtime es
diferido). Documentado antes en el vault (capitulo "Extraccion de Datos", 5 docs).

- **Dominio** (`Ecorex.Domain`): `ScrapeFlow` (maestro: nombre, URL, estado, FK a `DataClient` = "bot
  asignado" y a `DataContainer` = destino), `ScrapeStep` (tabla unica con discriminador
  `ScrapeStepKind`: Navigate/InjectScript/Extract/Wait/Click/Screenshot/Ai; campos por tipo), y
  `ScrapeVariable` (sustituciones {{VAR}}, secretas cifradas). Reusa `ScrapeSourceStatus` para el
  estado. `TargetContainerId` es referencia SUAVE (sin FK) para no crear un segundo camino
  DataContainer->ScrapeStep que SQL Server rechaza (error 1785).
- **Persistencia**: EF config + DbSets + enum a texto. Migracion DUAL `AddScrapeFlow` (PG + SQL
  Server). Aplicada a `ecorex_agente`; `has-pending-model-changes` limpio en AMBOS contextos.
- **Servicio**: `IScrapeFlowService` (CRUD de flujo + pasos + variables), variables secretas cifradas
  con `ISecretProtector` y NUNCA devueltas en claro (el DTO solo dice HasValue). Reorder de pasos.
- **Decisiones (E1-E4, con el usuario)**: destino = Contenedor de datos; programacion = reusar
  ImportProcess/ImportRun; paso de IA = instruccion + destino + allow-list de tools MCP + tope
  pasos/tiempo + modelo entre los que habilite el Super Admin; runtime = sub-agente Navegador (no Doom).

**Verificado en vivo** (smoke contra el Postgres real, reusando el EcorexDbContext real): crear flujo,
duplicado RECHAZADO, 3 pasos creados y REORDENADOS (orden invertido confirmado al releer), variable
secreta con el valor CIFRADO en BD (no en claro) y el DTO sin exponerlo, y BORRADO EN CASCADA (pasos +
variables a 0). Build Release verde; 522 pruebas verdes (los dobles de test ganaron los DbSets nuevos).

**Siguiente**: Ola 2 = la UI del configurador (milimetrica a `proto_web_scraping.html`, acento morado).

---

## 2026-07-17 - Merge del Agente Conector On-Prem al tronco + deploy a prod

**Agentes**: Claude (Opus 4.8). **Accion**: fusionar `feat/agente-colmena-gui` (34 commits, backbone
del Agente Conector On-Prem) al tronco `fase-0/clon-backbone`, y desplegar a produccion.

**El merge** quedo como `65f88e2`, un merge no-fast-forward de 2 padres: `5fcc5bf` (motor de
formularios D1-D4, ya en el tronco) y `4c9c1a8` (rama del agente). Divergencia real: 12 commits de
formularios en el tronco / 34 del agente. Conflicto unico: `CurrentPermissionsTests.cs` (se conservo
el superset del tronco, 4 tests). Los dos `ModelSnapshot` auto-fusionaron correctos (verificado con
`has-pending-model-changes`: sin cambios pendientes en PG **y** SQL Server, no a ojo). Migraciones del
agente (`AddConnectorQuery`, `AddImportRuns`, `AddImportPendingOffline`) en orden por timestamp encima
de `CamposCalculadosYAnchos`, sin duplicados ni huecos; el agente NO traia CamposCalculadosYAnchos, asi
que no hubo duplicado. Integridad: `apps/agent` identico a la rama del agente y `FormControlType`
identico al tronco -> ambos conjuntos intactos. Verificado: build Debug+Release 0 errores;
tests 457/30/35; **aislamiento cross-tenant 6/6 dual**; migraciones aplicadas en PG y SQL Server dev.

**Fix de deploy** (`364450a`): el build-from-git de prod fallaba con CS0246 -`Ecorex.SuperAdmin` ahora
referencia `apps/agent/libs/Ecorex.Contracts.Agent`, que vive FUERA de `apps/backend`, y
`Dockerfile.superadmin` solo hacia `COPY apps/backend`-. Localmente compilaba (la sln incluye todo);
el contexto Docker aislado no. Fix minimo: `COPY apps/agent/libs/Ecorex.Contracts.Agent/` (proyecto
hoja net10.0) a la ruta que la ProjectReference espera. Validado con `docker build --target build`
local antes de tocar prod.

**Deploy a prod** (`root@10.0.0.3`, `/opt/ecorex`, build-from-git de `fase-0/clon-backbone` @
`364450a`): backup previo (`backups/ecorex-2026-07-17-1638.sql.gz`), `build --no-cache` + `up -d`.
Prod aplico las 3 migraciones del agente (de `CamposCalculadosYAnchos` a `AddImportPendingOffline`),
arranco sin errores, `curl http://127.0.0.1:5480/login` -> **HTTP 200**. El backbone del Agente ya
esta en produccion.

**Esquema de ramas** respetado (sin reorganizar): tronco `fase-0/clon-backbone`, `origin/main` espejo,
doble push. **Siguiente**: la GUI/servicio del agente (WPF, Windows) NO va en la imagen web; se
instala aparte con `deploy/agent/`. Deuda viva: QA extra runtime de flujos (#68).

---

## 2026-07-17 - Cancel: el protocolo deja de mentir

`AgentHubMethods.Cancel` estaba declarado en el contrato pero **el agente no lo manejaba** (el
protocolo anunciaba algo que no existia). Ya no.

- **Contrato**: `CancelMsg(CorrelationId, Reason?)` tipado, junto a los demas mensajes.
- **Agente** (`RealHiveConnection`): un `ConcurrentDictionary<correlationId, CancellationTokenSource>`
  que se llena al empezar un `FetchRequest` y se vacia en el `finally`. `On(Cancel)` cancela el CTS del
  correlationId. **El token viaja hasta el `GatewayExecutor`** (que YA lo honraba en
  OpenAsync/ExecuteReaderAsync/ReadAsync pero nunca lo recibia): un Cancel aborta la consulta EN LA BD,
  no solo el bucle de envio. Al cancelar, el agente manda `FetchFailed` con codigo `CANCELLED`
  (retryable:false: no tiene sentido reintentar lo que se pidio abortar).
- **Servidor** (`AgentImportService`): `Pending` gana `ClientId` (a que agente mandarle el Cancel);
  `CancelAsync(correlationId, reason)` empuja `Cancel` al grupo del cliente y libera la peticion; y el
  **timeout sweep ahora tambien manda Cancel** -antes soltaba la peticion pero el agente seguia
  consultando y mandando chunks al vacio-. Endpoints dev para probar (`run-process/{id}`,
  `cancel/{corr}`).

**Verificado en vivo lo que de verdad tenia incertidumbre**: que Npgsql aborte una consulta EN CURSO al
cancelar el token. Prueba directa contra el `GatewayExecutor` con `SELECT ... CROSS JOIN pg_sleep(25)` y
el token cancelado a los 3s -> **CANCELADO en 3.1s, no 25s**. Eso es exactamente lo que hace
`OnCancel`. El resto de la cadena (servidor -> hub -> `On(Cancel)` -> CTS) es el MISMO push de SignalR
que ya usa `FetchRequest` (probado) mas 3 lineas de glue.

**NO verificado E2E de cadena completa**: requeria el agente elevado (lee su boveda de maquina) y el UAC
se cancelo (equipo desatendido), igual que paso con la prueba de instalacion. Lo incierto -la
cancelacion real de la consulta- si esta probado, arriba.

**Con esto, de la lista de pendientes de la Ola 6 solo quedan cosas de ESCALA** (limites por plan,
backplane Redis) y el guardrail de TLS estricto (no bloqueante si prod es HTTPS). El nucleo del agente
esta completo.

---

## 2026-07-17 - Reintento del agente offline (UC3) + reencuadre honesto del TLS

### El TLS no era tan urgente: se corrigio la documentacion

El usuario senalo, con razon, que si produccion va por HTTPS el canal ya va cifrado, asi que mi
"la contrasena viaja en claro cada N minutos" era una exageracion (era cierto SOLO en dev, con el hub
en `http://localhost`). Se separo lo que estaba mezclado y se corrigio ADR-0040 + los 4 docs del vault:

- **Cifrado del canal = lo da el DESPLIEGUE** detras de HTTPS (agente conecta por `wss://`). Eso
  resuelve el grueso, sin tocar el agente. La validacion de certificados de .NET ya esta activa (cert
  invalido ya se rechaza).
- **"TLS estricto" = que el AGENTE rechace una URL no-TLS.** Defensa contra config erronea/downgrade.
  Guardrail barato, NO bloqueante si prod es HTTPS. Baja de prioridad.

### Reintento del agente offline: construido y verificado en vivo

Antes, si el horario disparaba con el agente caido, la corrida quedaba en `Error` y solo se
reintentaba en la siguiente ventana natural (horas para un intervalo largo). Ahora:

- **`ImportRunResult.PendingOffline`** (enum, sin migracion): un agente dormido NO es un `Error` -es un
  "no llegue a intentarlo"-; se distingue para no ensuciar los KPIs.
- **`ImportProcess.PendingSince`** (columna nullable + indice; migracion dual `AddImportPendingOffline`):
  parquea la programacion "esperando a su agente". El runner la pone al fallar por offline y la limpia
  al despachar (no al ingerir: "offline" es especificamente "no llegue al agente").
- **Pase de reintento en el dispatcher**, gateado por `IsOnline`: reintenta las parqueadas SOLO cuando
  su agente volvio. El gate es lo que evita el spam -sin el, reintentar con el agente aun caido
  generaria una corrida PendingOffline cada minuto-. La discovery del worker se amplio (`FindTenants
  WithWorkAsync` = vencidas O parqueadas) para no perderse un tenant con solo cargas pendientes.

**Verificado E2E, con el reintento AISLADO del horario normal**: agente apagado -> el horario (cada 1
min) dejo la corrida `PendingOffline` y parqueo el proceso; se empujo `next_run_at` a +1h (para que el
horario normal NO pudiera dispararlo); se **encendio el agente** -> a los <70s el worker lo reintento
solo (log: *"agente reconecto, carga pendiente reintentada"*), la bitacora paso a `Ok` (4 filas),
`pending_since` se limpio y `next_run_at` **seguia a +1h** -prueba de que fue el reintento y no el
horario-. 462 pruebas verdes.

**Nota honesta**: mientras el agente esta caido, el horario normal genera un `PendingOffline` por
ventana (cada minuto en la prueba). Es ruido en la bitacora pero es VERDAD (el horario intento cada
minuto); no es un bucle de reintento (el pase de reintento esta gateado por IsOnline y no corre con el
agente caido). Para intervalos reales (15+ min) es despreciable. Lo que NO se implemento: `Cancel`
(sigue declarado sin manejar) y el TLS estricto (ahora un guardrail, no bloqueante).

---

## 2026-07-17 - Prueba de un CRON a 10 minutos: disparo exacto, y un bug que solo se ve corriendo

Se programo "Refresco Perfil Clientes" (contenedor TABLAS CRM, fuente PostgreSQL) con cron
`9 7 * * *` (07:09 Bogota) y se dejo correr sin tocar nada. **Disparo a las 12:09:00 UTC exactas** -la
hora pedida, al segundo-, cargo las 2 filas en una tabla que se habia vaciado a proposito (`ins=2
del=0`, antes 0 filas), dejo la bitacora *"17 Jul 07:09 · horario · 2 filas"* y reprogramo sola para
**el 18 a la misma hora**. Con esto el disparo automatico queda probado en los DOS motores (SQL Server
por intervalo, PostgreSQL por cron).

### El bug: `Cannot write DateTimeOffset with Offset=-05:00 to PostgreSQL type 'timestamp with time zone'`

Al guardar la programacion cron, reventaba. **Cronos devuelve el instante con el desfase de la ZONA
PEDIDA** (-05:00 en Bogota), y Npgsql solo acepta desfase 0 para `timestamptz`. En la rama de
`Interval` ya se normalizaba con `ToUniversalTime()`; en la de `Cron` faltaba.

**Lo interesante es por que las 12 pruebas del motor no lo vieron**: `DateTimeOffset` compara
INSTANTES, no representaciones, asi que `Assert.Equal(08:00Z, 03:00-05:00)` **pasa**. El motor estaba
"bien" segun sus pruebas y aun asi era imposible guardar un cron. Solo aparecio al escribir de verdad
en la BD.

Arreglado (`.ToUniversalTime()` en la rama cron) + 2 pruebas de regresion que afirman sobre
**`.Offset`**, que es lo unico que distingue el caso. Verificado que la prueba nueva FALLA sin el
arreglo y pasa con el. 462 pruebas verdes.

**Leccion para las proximas olas**: para fechas, un test de igualdad de `DateTimeOffset` no prueba que
la representacion sea la correcta. Si el destino es Postgres, hay que afirmar sobre el desfase.

---

## 2026-07-17 - Programaciones que disparan SOLAS + bitacora de corridas (Ola 4)

El horario ya ejecuta sin nadie delante, y cada corrida deja registro. **Verificado en vivo**: una
programacion "cada 1 minuto" disparo dos veces seguidas sin tocar nada -agente *"Orden 038ed2e9: OK 4
filas"*, servidor *"Importaciones programadas: 1 disparada(s)"*, y en la BD dos `import_runs` con
`trigger=Scheduled`, `result=Ok`, separadas exactamente un minuto-. La UI lo muestra como
*"17 Jul 06:46 · horario · 4 filas (reemplazo 4)"*.

### Por que la bitacora

Una programacion corre sin nadie mirando: sin registro, un fallo de madrugada es indistinguible de
"no habia datos nuevos". `ImportRun` copia el patron de `ScheduledJobRun` (000889), **incluido el
indice unico (TenantId, ProcessId, FiredAt)**, que es lo que da idempotencia: dos workers en la misma
ventana chocan al guardar en vez de pedirle al agente el mismo refresco dos veces. Por eso `FiredAt`
es la VENTANA y no "ahora".

Una corrida nace en `Running` (el resultado llega despues, por el canal) y la cierra `IImportRunLog`
contra el `correlationId`. Ese id lo genera **el runner**, no el canal: si lo generara el canal, un
agente rapido podria responder antes de que la corrida supiera su propio id y el resultado se perderia.

### Dedup y timeout: no eran un extra, eran la condicion

Sin ellos la bitacora MIENTE. Se cerraron primero, a proposito:
- **Dedup por ChunkIndex**: un chunk repetido (reintento, reconexion) duplicaba filas en silencio; la
  ingesta no puede distinguir "otra vez la fila 1" de "otra fila igual".
- **Timeout del pendiente** (10 min) + sweep en cada ciclo del worker: antes, un agente que se caia a
  mitad dejaba la peticion -y todas sus filas- en memoria PARA SIEMPRE, y su corrida se habria quedado
  en "Ejecutando" eternamente. `_outcomes` tampoco se limpiaba nunca.

### Decisiones

- **ADR-0041**: se agrega **Cronos** (MIT) para el cron, y se documenta por que NO se reusa el motor de
  000889: aquel recibe un `ScheduledJobRule` concreto y razona en frecuencias de calendario, que no
  contemplan ni "cada N minutos" ni cron. Se reusa lo que si es comun: `ResolveTimeZone`, el patron de
  bitacora y el del worker.
- El **cron se interpreta en hora del tenant** (probado: `0 3 * * *` en Bogota = 08:00 UTC).
- **Tras una caida larga NO se dispara la rafaga atrasada**: se salta al futuro. A nadie le sirve
  recibir los 12 refrescos que no ocurrieron anoche (probado).
- **Un horario invalido desactiva la programacion CON MOTIVO a la vista**, en vez de dejarla "activa"
  sin disparar nunca: ese silencio es justo lo que este modulo existe para evitar.

460 pruebas verdes (12 nuevas del motor de recurrencia). Migracion dual `AddImportRuns`.

---

## 2026-07-17 - Las TRES vias de alimentacion, probadas con datos reales (y un bug del lienzo)

Contenedor "PRUEBA CARGAS" con 3 tablas, una por via. Todo verificado en la BD, no solo en pantalla:

| Via | Fuente | Resultado |
|-----|--------|-----------|
| Base de datos (via agente) | SQL Server de Docker (`localhost:1443`, `prueba_cargas.dbo.PRODUCTOS`) | 4 filas; decimales intactos (`95000.50`) |
| API REST | `https://jsonplaceholder.typicode.com/users` (externa real) | 10 filas; mapeo CODIGO<-id, NOMBRE<-name, CORREO<-email |
| Archivo | `sucursales.xlsx` (ClosedXML, fechas reales) | 3 filas; fechas normalizadas a `yyyy-MM-dd` |

- **SQL Server cierra el DAL dual del Gateway**: el 16/07 se probo PostgreSQL; ahora el otro motor del
  mismo `GatewayExecutor`, con la credencial viajando desde la web (ADR-0040).
- **La API exige endpoint PUBLICO**: `ApiImportService.IsBlockedHost` rechaza localhost y rangos
  privados (anti-SSRF). No es un estorbo: es la defensa que impide que un conector se use para sondear
  la red interna del servidor. Por eso la prueba usa un servicio externo de verdad.

### Bug encontrado al probar: las cajas del lienzo ER nacian ENCIMADAS

Al ir a subir el Excel, el boton "Ver datos / importar Excel" de SUCURSALES **no respondia**. No era el
navegador: `document.elementFromPoint` sobre el boton devolvia un `div` de OTRA tabla. Las cajas se
repartian en cascada `40 + n*40` en AMBOS ejes, pero la caja mide **220 de ancho**: cada tabla nueva
caia sobre el ENCABEZADO de la anterior y le tapaba los 4 botones, que quedaban imposibles de pulsar
salvo que alguien arrastrara la caja. El comentario del codigo decia "para que no se apilen": la
intencion era correcta, el paso era demasiado corto.

Ademas el calculo estaba **duplicado**: `RebuildPositions` (pinta) y `SaveTableAsync` (PERSISTE). Por
eso arreglar solo uno no servia de nada -de hecho el primer intento no cambio nada, porque la cascada
ya estaba guardada en `canvas_x/canvas_y`-. Ahora hay un unico `SlotFor(index)` con una rejilla de 3
por fila, y ambos sitios lo llaman. Verificado: las 3 cajas en su celda y los 4 botones alcanzables.

---

## 2026-07-17 - "Actualizar datos": el circuito de negocio COMPLETO, verificado con filas reales

Primera vez que un dato de una base ajena aterriza en un contenedor **por el camino de produccion**:
un operador pulsa un boton en la web y el agente de la LAN trae las filas.

- **E2E real** (no simulado): conector `CLIENTES ALEGRA` -> Base de datos / PostgreSql /
  `localhost:5442` / `ecorex_agente`, consulta `SELECT CAST(id AS text) AS "CODIGO", name AS "NOMBRE"
  FROM tenants`; programacion "Refresco Perfil Clientes" con el cliente `cli_22e0790802bb`. Al pulsar
  **"Actualizar datos"**: agente *"Orden ea2c2a87: OK 2 filas"*, servidor *"corr=ea2c2a87 OK ins=2
  upd=0 del=8"*, UI *"Listo: 2 filas cargadas (se reemplazaron 8)"*, y las 2 filas verificadas en la
  BD (Plataforma ECOREX / SKY SYSTEM). El `del=8` confirma que el refresco es Replace, no Append.
- **Hueco encontrado al probar**: `DataConnector.Query` existia en la entidad y en la BD (migracion
  `AddConnectorQuery`), pero **ni el DTO ni el servicio ni la UI la transportaban**. El boton exige la
  consulta, asi que era inejecutable desde la web: solo se veia al intentar usarlo. Cableado de punta
  a punta (DTO + `SaveConnectorAsync` + textarea en el formulario de Base de datos).
- La credencial de la fuente se configura y se cifra desde la web (ADR-0040) y viaja en el
  `FetchRequest`; el agente armo la cadena con Npgsql sin configuracion local.

**Siguiente**: dedup de chunks por (correlationId, chunkIndex) y timeout del pending fetch, ANTES del
scheduler (Ola 4), que llamara a este mismo `IProcessRunner`.

**Recordatorio**: **TLS estricto sigue BLOQUEANTE para produccion** (ADR-0040) - hoy la contrasena de
la BD del cliente viaja por `http://` en dev.

---

## 2026-07-16 - Agente: prueba de punta a punta con identidad emitida por la WEB (no por un seeder)

Test corto pedido por el usuario, con una intuicion que resulto correcta: **el modulo de contenedores
YA emite identidades de agente; no habia nada que construir**.

- `ContenedorDatos.razor` -> detalle del contenedor -> **"Clientes remotos" -> "+ Crear cliente"**: crea
  el `DataClient` y **revela el secreto UNA sola vez** ("Guarda este secreto ahora: no se vuelve a
  mostrar") con boton de copiar, mas rotacion y borrado. Es exactamente el flujo que el agente
  necesita, y estaba hecho desde el modulo web.
- **Probado de verdad, sin seeders ni endpoints dev de identidad**: en la app local (SKY SYSTEM, Owner
  `owner@sky-system.local`) se creo el cliente **`cli_22e0790802bb`**; se le paso al agente con
  `--save-config`; el servicio conecto: *"Conectado a http://localhost:5232/hubs/agente como
  cli_22e0790802bb. Atendiendo Gateway y Archivos."*
- **Verificado desde el lado del servidor** (independiente del log del agente): el hub **despacho** una
  orden a ese ClientId -solo despacha a agentes en linea- y el agente respondio con el
  `correlationId` correcto (`b2d0055f`). Sin colmena abierta, la respuesta fue el NO explicito del
  Navegador, que es lo correcto.

**Lo que esto significa para la Ola 4**: el onboarding del agente (emitir identidad desde la web) NO
es trabajo pendiente. Lo que falta para cerrar el circuito de negocio es `DataConnector.RunsViaAgent`
+ el scheduler + el boton "Refrescar ahora".

**Nota**: corrio contra el Postgres local de Docker (NO prod). Queda en esa BD de dev el cliente
`cli_22e0790802bb` ("Colmena de prueba"), y su identidad en la boveda de la maquina.

---

## 2026-07-16 - Agente Conector On-Prem: Ola 5d - instalador (CONSTRUIDO; instalacion sin verificar)

Empaque del agente. **No hay Inno Setup ni WiX en la maquina**, asi que un `.iss` seria codigo que no
se puede compilar ni probar: se entrega un instalador **PowerShell real y ejecutable**, que ademas es
lo que un `.iss` acabaria invocando. Envolverlo en un `.exe` firmado queda pendiente (necesita la
herramienta y un certificado).

- **`deploy/agent/publish.ps1`**: publica AUTOCONTENIDO (`--self-contained`) servicio + colmena. Es a
  proposito: el criterio de aceptacion dice "maquina Windows LIMPIA", y exigir el runtime de .NET no
  es eso. Cuesta **271 MB** (dos apps con su runtime); el unico requisito que no se puede autocontener
  es el Runtime de WebView2 (de serie en Win11), y si falta, el Navegador falla con motivo y Gateway y
  Archivos siguen trabajando.
- **`deploy/agent/install.ps1`** (administrador): **crea la boveda el**, con owner = Administradores y
  ACL cerrada -esto NO es comodidad, es el hallazgo de ADR-0039: quien crea el directorio es su
  propietario y un propietario puede reescribir el ACL-; copia binarios a `%ProgramFiles%\ECOREX\
  Agente`; guarda la identidad cifrada; registra `EcorexAgent` (LocalSystem, arranque automatico) con
  reintentos escalonados ante fallo (5s/30s/60s), para que una caida no deje al cliente sin agente
  hasta el proximo reinicio; y deja la colmena en auto-arranque de sesion.
- **`deploy/agent/uninstall.ps1`**: por defecto **CONSERVA la boveda** (reinstalar no deberia obligar
  a reconfigurar; borrar secretos se pide con `-RemoveVault`, no es efecto colateral).
- **`deploy/agent/README.md`**: que se instala y por que son dos piezas, requisitos, donde queda cada
  cosa, seguridad (lo que hay que saber ANTES de aprobar un despliegue) y diagnostico.
- **`.gitignore`**: `out/` fuera del repo (271 MB estaban a punto de entrar; se detecto al revisar
  `git status` antes del commit).

**Bug real corregido al probar**: `$PSScriptRoot` llega VACIO al evaluar el valor por defecto de un
parametro cuando el script se invoca con `-File` (la ruta quedaba en `\out` y el instalador no
encontraba los binarios). Se resuelve en el cuerpo. Afectaba a `install.ps1` y `publish.ps1`.

**ACEPTACION CERRADA 2026-07-16 (instalado y desinstalado de verdad en la maquina)**:
- `install.ps1` -> servicio **Running**, arranque **Auto**, cuenta **LocalSystem**, y **sin consola**
  (headless de verdad; la consola que se veia antes era el exe corrido a mano para diagnosticar, no
  el producto).
- **Conectado**: el hub DESPACHO una orden al agente instalado (solo despacha a agentes en linea).
- **Colmena instalada <-> servicio instalado**: `Colmena conectada (administrador: no)` + `Colmena
  lista (presta escritorio al Navegador: si)`, y el panal muestra **"En linea"** con punto verde. Ese
  estado no es suyo: se lo publica el servicio por el pipe.
- `uninstall.ps1` -> servicio quitado, binarios borrados, auto-arranque quitado, origen de eventos
  quitado, **boveda conservada**. La maquina quedo sin rastro.

**Dos bugs REALES corregidos aqui** (los dos rompian promesas del propio README):
1. **El Visor de eventos estaba MUDO**. Dos causas encadenadas: el origen `"ECOREX Agente"` **no
   existia** (crear un origen exige privilegio y nadie lo hacia, y el proveedor de EventLog no avisa:
   simplemente no escribe), y ademas el proveedor **filtra en Warning por defecto**, asi que
   `LogInformation` -incluido "Conectado a X como Y", la linea que mas sirve para soporte- se
   descartaba igual. Ahora el instalador **registra el origen** y `Program.cs` baja el filtro a
   Information. Verificado: el Visor ya cuenta arranque, canal Online, conexion y entrada/salida de
   colmenas.
2. **El desinstalador dejaba `C:\Program Files\ECOREX` huerfana** (borraba `Agente` y se olvidaba del
   padre). Ahora la borra si quedo vacia.

**Pendiente de la ola**: `.iss` (Inno) + firma del ejecutable. El peso (271 MB) queda ASI por decision
del usuario (2026-07-16): esta bien a cambio de no exigir el runtime en la maquina del cliente.
**No verificado**: el arranque tras REINICIO real del equipo (exige reiniciar). Lo que si consta:
`StartMode=Auto`, que es el mecanismo, y que el servicio arranca y conecta solo.

---

## 2026-07-16 - Agente Conector On-Prem: Ola 5c - canal local (named pipe) servicio <-> colmena

Cierra el acoplamiento que 5b dejo al aire: la colmena ya no puede abrir la boveda, asi que sin este
canal no habia forma de configurar el agente. **Tambien cierra el pendiente de 5b**: el servicio
CONECTA al hub de punta a punta leyendo su identidad de la boveda de maquina (verificado).

**Lo elegante**: 5c no fue cirugia porque los dos seams ya existian. Son dos implementaciones nuevas:
- `PipeHiveConnection : IHiveConnection` -> la colmena habla con el servicio en vez del hub. **El
  ViewModel y el panal no cambiaron**: mismo seam de la Ola B.
- `DelegatedBrowserSubAgent : IBrowserSubAgent` -> el servicio pide prestado el escritorio de la
  colmena. El canal y el MCP siguen llamando `ExecuteAsync` sin enterarse.

**Seguridad (decidida aqui)**: el servicio corre como LocalSystem, asi que el pipe es superficie
privilegiada: quien ensanche la allow-list de Archivos le abre a la nube el disco entero como SYSTEM.
Por eso **leer estado y prestar escritorio = cualquier usuario interactivo; MUTAR (identidad,
allow-lists, consentimiento) = solo Administradores**, comprobado impersonando al cliente del pipe.
El **secreto nunca viaja al cliente**: se escribe, jamas se lee (la colmena ve un marcador `********`
y "vacio = no lo cambies").

**Tres bugs REALES encontrados al probar en la maquina** (ninguno teorico):
1. **ACL sin `Synchronize`**: la colmena no podia conectar. El sintoma enganya: `Connect` reintenta
   ante ACCESS_DENIED hasta agotar el plazo, asi que un ACL corto se presenta como **timeout**, no
   como acceso denegado.
2. **Buffers del pipe en 0** (`inBufferSize: 0, outBufferSize: 0`): con buffer cero cada escritura
   espera a que el otro lea -> el saludo se abrazaba a si mismo (servidor escribiendo `state`,
   cliente escribiendo `hello`, ninguno leyendo). Conexion viva y canal mudo. Ahora 64 KB.
3. **Falta `CreateNewInstance` en el ACL**: para anadir instancias a un pipe EXISTENTE, Windows exige
   ese derecho sobre el DACL ya puesto; la primera instancia se crea sin consultar nada. Resultado:
   el canal servia a UNA colmena y la siguiente quedaba fuera con ACCESS_DENIED. Se concede
   FullControl a LocalSystem (produccion) y Administradores (diagnostico).
Los tres estaban TAPADOS por `catch` mudos - el mismo pecado que se corrigio en 5b y que yo repeti
aqui. Ahora el bucle de aceptacion, el canal y el cliente registran su motivo.

**Politica del Navegador EMPUJADA** (`BrowserPolicy` en Contracts): `WebView2BrowserSubAgent` leia el
consentimiento y la allow-list de la boveda, pero vive en la colmena, que no puede abrirla -> fallaba
cerrado SIEMPRE. Ahora la politica viaja con el `state` desde el servicio (que si es dueno de la
boveda) y caduca sola si la colmena se queda sin servicio.

**Verificado E2E (hub real :5232 + Postgres local en Docker, NO prod)**:
- Servicio conectado al hub leyendo la boveda de maquina: `Conectado a .../hubs/agente como
  cli_dev_agent`. (Cierra el pendiente de 5b.)
- Colmena SIN elevar conecta al pipe: `Colmena conectada (administrador: no)` -> la impersonacion
  identifica bien al no-admin; `Colmena lista (presta escritorio al Navegador: si)`.
- Estado publicado al conectar (JSON `state` recibido; con el servicio sin elevar llega vacio, que es
  lo coherente: no puede leer la boveda).
- **Delegacion del Navegador probada por el cambio de mensaje**: sin colmena, "El Navegador exige una
  sesion interactiva"; con colmena, "Navegador no habilitado por el operador" -> esa respuesta la
  produjo el WebView2 DE LA COLMENA y volvio por el pipe. El circuito hub -> servicio -> pipe ->
  colmena -> WebView2 -> vuelta esta cerrado.
- Sin colmena: falla con motivo accionable, no se cuelga (escenario "servidor sin sesion").

**CERRADO 2026-07-16 (las dos pruebas que faltaban)**: solucion completa 0 errores / 0 advertencias.
- **Navegacion EXITOSA de punta a punta**: `Orden 17e19c3c: OK 3 acciones` (navigate + eval +
  captura), con el consentimiento y la allow-list `example.com` puestos en la boveda y **empujados**
  por el servicio a la colmena. Recorrido completo hub -> servicio -> pipe -> colmena -> WebView2 ->
  vuelta.
- **Reja de administrador verificada en vivo**: un `set-consent` mandado por el pipe desde un proceso
  SIN elevar recibe `ok:false` + "Cambiar la configuracion del agente exige permisos de administrador
  en este equipo.". El control de seguridad hace lo que dice.

**Siguiente**: 5d (instalador; recordar que **debe crear el la boveda**, ver hallazgo de propiedad
del directorio en ADR-0039).

---

## 2026-07-16 - Agente Conector On-Prem: Ola 5b - boveda de maquina + Worker Service (PARCIAL)

Sigue ADR-0039 (D8: despliegue = estacion Y servidor sin sesion). **Cuenta del servicio: LocalSystem**
(decidido por el usuario). Consecuencia asumida y anotada en el ADR: con DPAPI de maquina la llave no
cuelga del usuario, asi que el ACL del archivo es la UNICA puerta, y con LocalSystem un administrador
local puede llegar al secreto del tenant. Escalon futuro si se quiere least-privilege: cuenta virtual
`NT SERVICE\EcorexAgent` (solo cambia el instalador).

- **`AgentVault`** (nuevo): el P/Invoke a DPAPI y la ruta del store estaban **duplicados en los 5
  stores** (config, source, browser-allow, file-allow, consent) - el mismo olor que se acaba de quitar
  en `ApiImportService`. Ahora hay UN solo lugar que decide donde viven los secretos y como se cifran;
  los 5 stores adelgazaron a su logica propia. Mover la boveda fue, gracias a eso, una linea.
- **Boveda: `%ProgramData%\Ecorex\Agent` + DPAPI de MAQUINA + ACL** (rompe herencia; solo SYSTEM y
  Administradores). Verificado en la maquina real: el ACL quedo exacto y una shell sin elevar NO puede
  ni listar el directorio.
- **`Ecorex.Agent.Service`** (nuevo, Worker Service, `UseWindowsService`): hospeda el Core headless
  (canal + Gateway + Archivos). El mismo binario corre como servicio (log al Visor de eventos) o como
  consola (diagnostico). Navegador = `UnavailableBrowserSubAgent`: responde NO con motivo explicito en
  vez de colgar la peticion (la delegacion a la colmena llega en 5c).
- **`--save-config` vive en el SERVICIO**, no en la colmena, porque el dueno del store es el servicio.
  Normaliza la URL: la config guarda la URL COMPLETA del hub (el cliente SignalR se conecta a ella tal
  cual) pero un operador escribe la BASE; ese desliz se manifestaba como un "no pude conectar" mudo.
- **Diagnosticabilidad (bug real hallado al probar)**: `RealHiveConnection` se tragaba el motivo del
  fallo (`catch { return false; }`) y `AcquireTokenAsync` tambien. En un equipo on-prem sin escritorio
  eso es indepurable. Ahora hay `LastError` (con el motivo del handshake, que es el fallo mas probable
  en campo: secreto cambiado, ClientId inexistente, reloj desfasado >120s) y el worker lo registra.
- **Se RETIRO la migracion automatica del store heredado** que yo mismo habia escrito: se comprobo que
  es imposible por construccion (el unico que puede descifrar el `%APPDATA%` viejo es el usuario, que
  es justo quien ya no puede escribir la boveda; el servicio puede escribirla pero no descifrar lo del
  usuario). El ADR ya lo decia; el codigo pretendia lo contrario. Se reconfigura una vez.
- **Hallazgo de seguridad -> Ola 5d**: quien CREA el directorio es su propietario, y un propietario
  siempre puede reescribir el DACL. Si un usuario sin privilegios abre la colmena antes de que exista
  la boveda, queda de dueno y podria re-otorgarse acceso al secreto. **El instalador debe crear la
  boveda** (owner = Administradores); `EnsureDir()` queda como red de seguridad, no como el mecanismo.

**Verificado**: build 0 errores/0 warnings. Boveda con ACL correcto (SYSTEM+Admins, comprobado con
Get-Acl). Servicio en consola: arranca, apunta a la boveda correcta y **sin config avisa y reintenta
en vez de morirse**. Escritura y lectura de la boveda entre DOS procesos elevados distintos: OK (el
DPAPI de maquina hace su trabajo). Handshake HMAC contra el hub real (:5232, Postgres local en Docker,
NO prod): OK.

**NO verificado (pendiente)**: que el servicio CONECTE al hub de punta a punta. El primer intento
fallo por MI comando de prueba (pase la URL base en vez de la del hub; de ahi salio la normalizacion)
y el segundo intento no llego a correr porque se cancelo el UAC. Falta repetir con:
`Ecorex.Agent.Service.exe --save-config cli_dev_agent http://localhost:5232 dev-secret-ola-b` en
consola de ADMINISTRADOR, y luego correr el exe. **No se instalo ningun Servicio Windows** en la
maquina (eso es de la Ola 5d).

**Siguiente**: 5c (IPC named pipe: sin el, y esto lo confirmo la prueba, NO hay forma de configurar el
agente porque la colmena ya no puede tocar la boveda), luego 5d (instalador).

---

## 2026-07-16 - Agente Conector On-Prem: Ola 5a - seam del Navegador + nucleo Ecorex.Agent.Core

Arranca la Ola 5 (empaque). Antes de empacar hubo que decidir COMO, porque la D4 original
("Servicio Windows headless + WPF de config") se tomo ANTES de la expansion a colmena y choca con
dos hechos: (1) `DpapiConfigStore` cifra con DPAPI **de usuario** en `%APPDATA%`, y un servicio corre
con otro perfil/llave -> partido en dos procesos, el servicio no puede leer NADA de lo que escribio
la WPF; (2) WebView2 necesita escritorio y no vive en la sesion 0 de un servicio. El usuario confirmo
que el despliegue real es **ambos escenarios** (estaciones con sesion y servidores 24/7 sin sesion).

**ADR-0039** (nuevo): el Servicio es el UNICO dueno de identidad, canal y store (que pasa a
`ProgramData` + DPAPI de MAQUINA + ACL); la colmena WPF es su CLIENTE por named pipe (no descifra, no
conecta al hub) y le PRESTA el escritorio al navegador. Sin colmena: gateway y archivos siguen
atendiendo y el navegador falla con motivo claro, nunca cuelga. Se conserva WebView2; Playwright
headless queda como add-on si algun dia hace falta navegador sin sesion.

**Ola 5a (esta entrada)**: preparar el terreno, sin cambiar comportamiento.

- **Seam `IBrowserSubAgent`** (en Contracts, junto a `IHiveConnection`): `RealHiveConnection` y
  `AgentMcpServer` sostenian el `WebView2BrowserSubAgent` CONCRETO y hacian
  `Application.Current.Dispatcher.InvokeAsync` a mano. Ahora dependen de la interfaz y el marshalling
  al hilo de UI se escondio DENTRO de la impl WebView2 (`ExecuteAsync` es seguro desde cualquier
  hilo). Mismo truco que ya funciono con `IHiveConnection` en la Ola B.
- **`Ecorex.Agent.Core`** (net10.0-windows, `UseWPF=false`): 11 de los 12 servicios (~1400 de 1850
  lineas) se movieron con `git mv` (historia conservada) + cambio de namespace: canal, Gateway,
  Archivos, stores DPAPI, allow-lists, consentimiento, QueryGuard y MCP. En la Gui queda SOLO
  `WebView2BrowserSubAgent` + la UI. net10.0-windows (no net10.0) porque el store es DPAPI/crypt32:
  Windows por definicion, no por la GUI.
- **Prueba estructural**: el Core compila con `UseWPF=false`. Si algun archivo movido hubiera
  conservado una dependencia de WPF, no compilaria. Build de `Ecorex.Agent.slnx`: 0 errores, 0 warnings.
- **Verificado E2E (runtime, no solo compilacion)**: colmena levantada; `tools/list` = 14 tools;
  `browser.navigate https://example.com` -> OK, que recorre MCP (Core, sin WPF) -> seam -> WebView2 en
  el hilo de UI: si el marshalling interno estuviera mal, reventaria. Archivos: `file.read` dentro de
  una raiz permitida devuelve el contenido, y `C:\Windows\win.ini` sigue RECHAZADO (fail-closed intacto).

**Nota de entorno**: para desambiguar el caso positivo de Archivos sobreescribi la allow-list de
archivos de la maquina de dev (`file-allow.dat`) con una raiz de scratchpad. Era config dev que yo
mismo habia creado en la ola de Archivos; se reconfigura desde el flyout de la colmena.

**Siguiente**: 5b (`Ecorex.Agent.Service` + store de maquina), 5c (IPC named pipe), 5d (instalador).

---

## 2026-07-16 - Agente Conector On-Prem: el import REST comparte el nucleo de ingesta (cierra Ola 3)

Ultimo pendiente de la Ola 3, desbloqueado por el usuario (su ajuste en el modulo de contenedores
resulto ser visual y ya termino). Solo `Ecorex.Application/DataContainers`, sin migracion.

- **Un solo camino de escritura**: `ApiImportService` (REST) ya no inserta filas por su cuenta. Recibe
  `IRowIngestService` por constructor, abre una sesion (`CreateSession` + `PrepareAsync`, que hace el
  vaciado de Replace y la precarga de clave de Upsert) y manda **cada pagina como un chunk**. Se
  borraron sus privados `InsertRow` y `DeleteAllRowsAsync`; los contadores (ins/upd/del) salen de la
  sesion. Antes esa logica EAV estaba DUPLICADA entre REST y agente: dos copias que podian divergir.
- **Comportamiento conservado a proposito**: un SaveChanges por pagina (no por fila), el tope de 5000
  filas se evalua con los contadores de la sesion mas lo que lleva la pagina, y el outcome mantiene su
  regla (`success` si escribio algo o no hubo fallidos). El contrato publico no cambio.
- **Tests (lo que faltaba)**: `ApiImportService` no tenia NINGUN test. Se agrego
  `tests/Ecorex.Application.Tests/RowIngestServiceTests.cs` (5, EF InMemory) sobre el nucleo, que ahora
  gobierna los dos caminos: Append (fila + celdas + TenantId), Append en 2 chunks (acumula sin borrar),
  Replace (`del=1, ins=1`), Upsert por clave (`upd=1, ins=1`, sin duplicar) y Upsert con la misma clave
  repetida en una corrida (gana la ultima). Patron `InnerDb` + `FakeAppDb : IApplicationDbContext`.
- **Verificacion**: `dotnet build Ecorex.sln` 0 errores; `Ecorex.Application.Tests` **384/384** verde.
  El camino del agente ya estaba verificado E2E contra SQL Server real (`ciudades`). El camino REST
  **no se re-probo en vivo**: descansa en los tests nuevos + en que es el mismo codigo del nucleo.
- **Vault**: doc 03 s9 (nucleo compartido + tests), doc 05 Ola 3 `[CONSTRUIDO 2026-07-16]`, indice.

**Siguiente**: Ola 4 (`ImportSchedulerService` + `DataConnector.RunsViaAgent` + UI "Refrescar ahora"),
en pausa a peticion del usuario.

---

## 2026-07-15 - Agente Conector On-Prem: Archivos - binarios (base64) + permisos ro/rw por raiz

Ajustes menores del sub-agente Archivos (backlog). Solo lado agente.

- **Binarios**: `FileActionKind.ReadBytes` (aditivo) -> devuelve el archivo en **base64**, tope 5 MB
  (el `Read` de texto UTF-8 mantiene su tope de 1 MB). Expuesto por MCP como **`file.readBytes`**
  (el servidor MCP publica ahora 14 tools).
- **Permisos POR RAIZ (least privilege, doc 06 s4)**: en la allow-list, una raiz es de **SOLO LECTURA**
  por defecto; se antepone **`rw:`** para permitir escritura (`ro:` es opcional/explicito). Ejemplo:
  `C:\Datos` (ro) / `rw:C:\Salida` (rw). `FileAllowList.LoadRoots()` parsea el prefijo;
  `FileSubAgent.TryResolve` devuelve tambien si la raiz admite escritura y `Write`/`Delete`/`MakeDir`
  la exigen. Si una ruta cae en varias raices, gana la que permita escritura.
- **UI**: el hint del flyout de Archivos explica el prefijo `rw:`.
- **Verificado E2E por MCP**: `file.readBytes` de un PNG -> base64 (`iVBORw0KGgo...`); `file.read` en
  raiz ro -> ok; `file.write` en raiz ro -> RECHAZADO ("exige una raiz marcada 'rw:'"); `file.write`
  en raiz rw -> ok.
- **Nota**: el otro pendiente menor (migrar `ApiImportService` al nucleo `IRowIngestService`) NO se
  toco: vive en `Ecorex.Application/DataContainers`, el modulo que el usuario esta ajustando.

---

## 2026-07-15 - Agente Conector On-Prem: Consentimiento local + UI de allow-lists en la colmena

Cierra el ultimo guardrail de doc 06 s4 ("consentimiento local explicito"): el operador controla que
capacidades sensibles se activan, desde la propia colmena.

- **`CapabilityConsent`** (DPAPI, `consent.dat`): habilitacion por capacidad (browser/files),
  **fail-closed por defecto** (sin archivo = todo deshabilitado). `SetBrowser/SetFiles/IsXEnabled`.
- **Enforcement en los sub-agentes**: `WebView2BrowserSubAgent.ExecuteAsync` y `FileSubAgent.ExecuteAsync`
  rechazan TODA la orden si la capacidad no esta habilitada -aplica al HUB Y al MCP local- ("Navegador/
  Archivos no habilitado por el operador en la colmena").
- **UI en la colmena**: clic en la celda Navegador/Archivos abre un flyout con: toggle "Habilitada por
  mi (el operador)" + editor de la allow-list (una entrada por linea, monospace) + Guardar/Cerrar. En
  el VM: `OpenCapabilityConfig(kind)` carga estado; `SaveCapability` persiste consentimiento + allow-list.
- **Headless** `--enable <browser|files> <0|1>` (mismo efecto que el toggle) para despliegue/servicio.
- **Verificado E2E**: fail-closed por defecto -> `browser.navigate`/`file.exists` por MCP rechazados;
  al habilitar (UI o `--enable`) -> funcionan. Captura del flyout (toggle + allow-list example.com).
- **Estado**: con esto, TODOS los guardrails de doc 06 s4 estan implementados (allow-list por capacidad,
  acciones tipadas, JS firmado, handshake HMAC/tenant, consentimiento local). **Pendiente**: binarios
  base64 en archivos, read-only vs read-write por raiz.

---

## 2026-07-15 - Agente Conector On-Prem: Endurecimiento del Navegador (JS firmado por el servidor)

Guardrail de doc 06 s4: el JS que el servidor inyecta no puede ser arbitrario; debe ir FIRMADO.

- **Contrato**: `BrowserAction.Signature` + `AgentSign` (en `Ecorex.Contracts.Agent`): HMAC-SHA256 del
  secreto del cliente sobre `correlationId|payload`, hex, con `Verify` en tiempo constante. Ligar al
  correlationId da anti-replay/versionado ligero.
- **Agente**: `RealHiveConnection` verifica la firma de las acciones que inyectan JS del HUB (`Eval`,
  `Mouse`, `Wait` con condicion) ANTES de ejecutar; **fail-closed**: sin firma valida o sin secreto
  local, rechaza toda la orden. El JS por **MCP local** (loopback) NO requiere firma (confianza local).
- **Servidor**: el endpoint dev `dev/browse` firma el `eval` con el secreto del `DataClient`
  (`ISecretProtector` + `AgentSign`); `?nosign=true` omite la firma para probar el rechazo.
- **Fix colateral**: se subio el `MaximumReceiveMessageSize` del `AgenteHub` a 32 MB (los
  `BrowserResult` con screenshot base64 y los `FetchResult` grandes superaban el default de 32 KB de
  SignalR y el hub los rechazaba en silencio). Solo para ese hub, sin tocar los demas.
- **Verificado E2E**: firmado -> Navigate/Eval("Example Domain")/Screenshot ok; `nosign=true` ->
  "Firma de JS invalida o ausente para la accion Eval" (rechazado). MCP eval local sigue funcionando.
- **Pendiente**: UI de la allow-list en la colmena; consentimiento local explicito para capacidades
  sensibles (doc 06 s4).

---

## 2026-07-15 - Agente Conector On-Prem: Sub-agente ARCHIVOS (Files-1/2/3) + MCP file.*

Tercera capacidad de la colmena (doc 06 s3.2). Todo en `feat/agente-colmena-gui`.

- **Contrato (Files-1)**: en `Ecorex.Contracts.Agent` (aditivo): `FileActionKind` (List/Read/Write/
  Delete/Exists/MakeDir), `FileEntry`, `FileAction`, `FileRequestMsg`, `FileActionResult`,
  `FileResultMsg` + metodos `FileRequest`/`FileResult`.
- **Motor (Files-2)**: `Services/FileSubAgent` ejecuta las acciones tipadas; `Read` con tope 1 MB.
  `Services/FileAllowList`: rutas RAIZ permitidas cifradas con DPAPI, fail-closed si vacia. TODA ruta
  se canonicaliza (`Path.GetFullPath`, impide traversal `..`) y debe caer DENTRO de una raiz. No es un
  shell generico. `--save-file-allow` en runtime.
- **Cableado + MCP (Files-3)**: `RealHiveConnection` atiende `FileRequest` -> celda Archivos ->
  `FileResult`. Backend: `AgenteHub.FileResult` + endpoint dev `dev/files/{clientId}?op=&path=&content=`.
  El servidor MCP se renombro `BrowserMcpServer` -> **`AgentMcpServer`** y ahora publica **13 tools**:
  las 7 `browser.*` + las 6 `file.*` (list/read/write/delete/exists/mkdir). Mejora: ante un error el MCP
  devuelve un error JSON-RPC en vez de cerrar la conexion.
- **Verificado E2E** (SuperAdmin + agente + sandbox `%TEMP%\ecorex-files`): por el HUB -> Write "21 chars",
  List entries=1, Read "Hola colmena archivos"; leer `C:\Windows\win.ini` -> bloqueado. Por MCP ->
  `file.write`/`file.list`/`file.read` ok; leer fuera de la allow-list -> `isError:true`.
- **Pendiente**: lectura de binarios (base64), UI de la allow-list en la colmena, permisos read-only vs
  read-write por raiz.

---

## 2026-07-15 - Agente Conector On-Prem: Navegador Nav-4 (servidor MCP localhost + las 7 tools)

Completa el catalogo `browser.*` del prior-art (doc 07) y lo expone por MCP para clientes/IA locales.

- **2 tools faltantes**: `browser.mouse` (MouseBot: guion JSON de pasos click/type por selector, via JS
  acotado al dominio permitido) y `browser.downloads` (historial de descargas, tracker de
  `CoreWebView2.DownloadStarting`). Contrato extendido (`BrowserActionKind.Mouse/Downloads` +
  `BrowserAction.ScriptJson`).
- **`Services/BrowserMcpServer`**: servidor MCP embebido sobre TcpListener **solo 127.0.0.1** (loopback,
  como el legacy), JSON-RPC 2.0: `initialize` / `tools/list` / `tools/call`. Expone las 7 herramientas
  con su input schema; ejecuta via la MISMA instancia `WebView2BrowserSubAgent` que el hub (marshala al
  Dispatcher), respeta la allow-list, y devuelve contenido MCP (texto + imagen PNG). Se comparte el
  navegador creando una sola instancia en `MainWindow` y pasandola a `RealHiveConnection` + al MCP;
  arranca en modo real y se detiene al salir.
- **Verificado E2E por JSON-RPC** (curl a 127.0.0.1:8765): `tools/list` -> las 7 tools; `tools/call`
  `browser.navigate` example.com -> ok, `browser.eval` `document.title` -> "Example Domain",
  `browser.screenshot` -> contenido imagen PNG base64; navegar a un dominio NO permitido ->
  `isError:true` "Dominio no permitido por la allow-list local".
- **Pendiente**: JS firmado/versionado por el servidor (doc 06 s4), UI de la allow-list en la colmena,
  y el sub-agente Archivos.

---

## 2026-07-15 - Agente Conector On-Prem: Sub-agente NAVEGADOR (WebView2 + allow-list) - Nav-1/2/3

Segunda capacidad de la colmena (doc 06 s3.2, prior-art doc 07 Doom). Todo en `feat/agente-colmena-gui`.

- **Contrato (Nav-1)**: en `Ecorex.Contracts.Agent` (aditivo, no toca Gateway): `BrowserActionKind`
  (Navigate/Eval/Wait/Screenshot/Html), `BrowserAction`, `BrowserRequestMsg`, `BrowserActionResult`,
  `BrowserResultMsg` + metodos `BrowserRequest`/`BrowserResult` en `AgentHubMethods`.
- **Motor WebView2 (Nav-2)**: `Services/WebView2BrowserSubAgent` (Microsoft.Web.WebView2) hospeda un
  WebView2 en ventana visible y ejecuta la secuencia de acciones tipadas (catalogo browser.* de doc
  07). `Services/BrowserAllowList`: dominios permitidos LOCALES cifrados con DPAPI, fail-closed si
  vacia; `Navigate`/`Eval`/`Html` se rechazan fuera de la lista (doc 06 s4: nada fuera de lista, ni
  aunque la nube lo pida; solo acciones tipadas, no shell). `--save-browser-allow` en runtime.
- **Cableado (Nav-3)**: `RealHiveConnection` atiende `BrowserRequest` marshalando al hilo de UI ->
  enciende la celda Navegador -> ejecuta -> `BrowserResult`. Backend: `AgenteHub.BrowserResult`
  (loguea + guarda screenshots en temp) + endpoint dev `dev/browse/{clientId}?url=`.
- **Verificado E2E**: el servidor ordena navegar a example.com -> el agente abre WebView2, `Eval`
  `document.title` = "Example Domain", captura el PNG real de la pagina; navegar a `google.com`
  (fuera de la allow-list) -> `Navigate ok=False` (bloqueado), el `Eval` sigue en la pagina permitida.
- **Pendiente**: servidor MCP localhost (las 7 tools `browser.*` para herramientas locales/IA),
  `browser.mouse`/`browser.downloads`, JS firmado/versionado por el servidor, UI de la allow-list en la
  colmena; y el sub-agente Archivos.

---

## 2026-07-15 - Agente Conector On-Prem: Ola 3 (INGESTA en el servidor - FetchResult -> filas del contenedor)

Las filas que trae el agente aterrizan en un contenedor de datos reusando el motor EAV (doc 03 s6 /
doc 05 Ola 3). Autorizado a tocar apps/backend; Ecorex.sln sigue verde.

- **`IRowIngestService`** (`Ecorex.Application/DataContainers/RowIngestService.cs`): nucleo de ingesta
  EAV reutilizable, extraido de la logica de `ApiImportService` (Append/Replace/Upsert sobre
  fila+celdas). Trabaja por SESION (`PrepareAsync` + `IngestChunkAsync` + counts) para ingesta por
  chunk y conservar el dedup del Upsert. El origen se abstrae como filas campo->valor string (asi lo
  comparten el import REST -JSON- y el agente -FetchResult-). Registrado scoped en `AddApplication`.
- **`IAgentImportService`** (`Ecorex.SuperAdmin/Agents/AgentImportService.cs`, singleton): pending-fetch
  por `correlationId` (contenedor/mapa/modo/clave/tenant/acumulador). `DispatchFetchAsync` arma y empuja
  el `FetchRequest`; `OnFetchResultAsync` acumula chunks y en el ultimo ingiere via `IRowIngestService`
  en un scope propio con el tenant fijado (`AmbientTenantContext.Begin`); `OnFetchFailedAsync`. Cableado
  en `AgenteHub.FetchResult/FetchFailed`.
- **Dev endpoints** (Development): `dev/ingest/{clientId}` (crea/reusa contenedor "Ciudades (agente)" +
  columnas, dispara), `dev/ingest-status/{corr}`, `dev/container-count/{id}`.
- **Verificado E2E** (SuperAdmin :5237 + agente + SQL Server real de la LAN): `SELECT ... FROM ciudades`
  -> 20 filas -> contenedor. Replace `ins=20`; 2o Replace `del=20/ins=20`; Upsert por CODIGO_POSTAL
  `upd=20` sin duplicar (queda en 20 filas). `firstRow=[TAIWAN, 110231, ...]`.
- **Nota de entorno**: la BD dev tenia drift (faltaba `data_container_columns.referenced_container_id`
  pese al historial de migraciones); se corrigio con un ALTER puntual. NO es bug del codigo (la
  migracion existente la crea en una BD limpia).
- **Pendiente**: migrar `ApiImportService` (REST) al nucleo compartido (follow-up mecanico, se dejo
  intacto para no tocar el path REST sin sus tests de integracion Docker); `DataConnector.RunsViaAgent`
  + `ImportSchedulerService` (Ola 4) + UI "Refrescar ahora"/estado en linea.

---

## 2026-07-15 - Agente Conector On-Prem: Ola C (Gateway EJECUTA real contra SQL Server de la LAN)

El sub-agente Gateway pasa de acusar recibo a EJECUTAR de verdad (doc 05 Ola 2). C# (no VB.NET).

- **`Services/SqlServerGatewayExecutor`** (Microsoft.Data.SqlClient 6.0.2): abre la conexion, ejecuta
  la consulta parametrizada, lee por lotes (`pageSize`, tope `maxRows`) y produce `FetchResult` en
  chunks (columnas en el chunk 0) como `IAsyncEnumerable`.
- **`Services/QueryGuard`**: whitelist de SOLO lectura -un unico SELECT/CTE; bloquea insert/update/
  delete/merge/drop/alter/create/truncate/exec/sp_/xp_/into/etc.- defensa en profundidad ademas del
  usuario de BD de solo-lectura.
- **`Services/GatewaySourceStore`**: la cadena de conexion (con credencial de la LAN) se guarda
  LOCAL cifrada con DPAPI (`source.dat`), opcion b: la credencial NUNCA viaja por el canal ni se
  versiona. Se carga en runtime con `--save-source "<cadena>"`.
- **`RealHiveConnection`**: un `FetchRequest` Database resuelve la cadena local y ejecuta (stream de
  `FetchResult`); `FetchFailed` con codigo (QUERY_REJECTED / NO_SOURCE / UNSUPPORTED_ENGINE /
  AGENT_ERROR) en fallo. Otros conectores siguen acusando recibo.
- **Backend (dev)**: `dev/push` acepta `?q=` para probar consultas; el log del hub muestra columnas +
  primera fila del `FetchResult` (verificacion de datos reales).
- **Verificado E2E contra SQL Server REAL de la LAN** (`M700_GEN`, via el hub en :5237):
  `SELECT TOP 20 * FROM ciudades` -> `[AGENTE] FetchResult rows=20 cols=[DPTO,NOMBRE,PAIS,CODIGO_DIAN,
  DANE_DEP,...]` con datos reales; `DELETE FROM ciudades` y `SELECT * INTO x ...` -> `FetchFailed
  QUERY_REJECTED`. Credencial cargada en runtime (DPAPI), NUNCA en el repo.
- **Siguiente**: ingesta en el servidor (doc 03 s6: `IRowIngestService` + `IAgentImportService` para
  que el `FetchResult` termine como filas del contenedor), `RunsViaAgent`+scheduler, y sub-agentes
  Archivos/Navegador.

---

## 2026-07-15 - Agente Conector On-Prem: Hub REAL de servidor (apps/backend, doc 03 / doc 05 Ola 1)

Se construye el lado SERVIDOR del canal en `Ecorex.SuperAdmin` (host que ya tiene SignalR + auth).
Autorizado explicitamente a tocar `apps/backend`; sin romper su build (Ecorex.sln verde).

- **AgenteHub** (`RealTime/AgenteHub.cs`): `[Authorize(AuthenticationSchemes="Agent")]`, grupos
  `client:{id}`/`tenant:{id}`, presencia; recibe AgentHello/FetchResult/FetchFailed/Heartbeat.
- **Agents/** : `IAgentRegistry`+`InMemoryAgentRegistry` (en linea/offline), `AgentTokenIssuer`
  (JWT corto client_id/tenant_id), `AgentNonceCache` (anti-replay), `AgentChannel` (esquema bearer
  **"Agent" NO-default** -no altera la auth de cookies-, DI y endpoints).
- **Endpoints**: `POST /api/agente/token` (anonimo; valida `DataClient` activo + ts +/-120s + nonce +
  HMAC del secreto descifrado con `ISecretProtector` -> JWT 15m), `POST /api/agente/push/{clientId}`
  (admin, via `IHubContext`), `GET /api/agente/status/{clientId}`. Dev-only: `dev/seed-client` y
  `dev/push` (guardados por `IsDevelopment`).
- **Identidad**: la entidad **`DataClient`** existente (ClientId + `ClientSecretEncrypted`), sin
  entidades nuevas ni migracion. Query cross-tenant en el token via `IgnoreQueryFilters`.
- **Contrato compartido**: SuperAdmin referencia el MISMO `Ecorex.Contracts.Agent` que el agente
  (fuente unica del protocolo + `AgentHmac` identico en ambos lados). Paquete nuevo:
  `Microsoft.AspNetCore.Authentication.JwtBearer` en SuperAdmin.
- **Agente (lado cliente, opcion A)**: `RealHiveConnection` adquiere el JWT (HMAC -> `/api/agente/token`)
  y lo pasa por `AccessTokenProvider`; sin secreto conecta anonimo (sim). `AgentConfig` +`Secret`;
  campo "Secreto" en el flyout; `--save-config <id> <url> [secreto]`.
- **Verificado E2E contra la BD dev** (SuperAdmin en :5237 + Postgres 5442): NEGATIVO -> token con
  clientId inexistente 401, ts fuera de rango 401; POSITIVO -> seed de `DataClient`, el agente obtiene
  token, `[AGENTE] En linea` + `AgentHello caps=[Database,RestApi]`, push del servidor -> `[AGENTE]
  FetchResult corr=...`. Captura de la colmena "En linea" con Gateway + workers por ordenes reales.
- **Siguiente**: `DataConnector.RunsViaAgent` + `IRowIngestService` (ingesta compartida) +
  `IAgentImportService` + `ImportSchedulerService` (doc 03), y en el agente la Ola C (ejecucion real
  de la consulta contra la BD de la LAN, solo-lectura + whitelist).

---

## 2026-07-15 - Agente Conector On-Prem: Ola B (canal SignalR real, lado agente)

Rama `feat/agente-colmena-gui`. Se sustituye el mock por el cliente SignalR REAL detras del mismo
seam `IHiveConnection`; la GUI y el ViewModel NO cambian.

- **Protocolo compartido** (`libs/Ecorex.Contracts.Agent/AgentProtocol.cs`): `AgentProtocol`
  (ruta `/hubs/agente`, version), `AgentHubMethods` (FetchRequest/Ping/Cancel; AgentHello/FetchResult/
  FetchFailed/Heartbeat) y DTOs (`FetchRequestMsg`, `FetchResultMsg`, `FetchErrorMsg`, `AgentHelloMsg`,
  `ConnectorSpec`, `QuerySpec`, `PagingSpec`) fieles a doc 02. Fuente de verdad para agente y futuro hub.
- **`RealHiveConnection`** (`Services/`, Microsoft.AspNetCore.SignalR.Client 10.0.0): conexion saliente
  WS, `AgentHello` al conectar, reconexion con backoff 0/2/5/10/30/60s (`HiveRetryPolicy`), lifecycle
  -> `ConnectionChanged`, `On(FetchRequest)` -> `RequestStarted` (enciende capacidad + worker) -> acuse
  `FetchResult` -> `RequestFinished`. La EJECUCION real de la consulta es Ola C (aqui solo acuse).
- **Refactor al seam**: `HiveViewModel` depende de `IHiveConnection`; los comandos DEMO/seed solo
  aplican si la impl es el mock. `MainWindow` usa Real por defecto y Mock en modo captura
  (`ECOREX_AGENT_CAPTURE`) o `ECOREX_AGENT_FORCE_MOCK=1`. Auto-conecta al arrancar si hay config.
- **Arranque headless** `--save-config <clientId> <hubUrl>` (DPAPI) para despliegue/servicio y pruebas.
- **Simulador** `tools/Ecorex.Agent.HubSim` (ASP.NET Core minimal + `AgenteHub` + `FetchPump`): stand-in
  del backend orquestador para probar E2E SIN tocar `apps/backend`. Empuja `FetchRequest` (Database/
  RestApi) cada 3-4s y registra lo recibido.
- **Verificado E2E**: sim en :5280 + agente real -> logs del hub muestran `Agente CONECTADO`,
  `AgentHello client=cli_ola_b caps=[Database, RestApi]` y round-trip continuo `FetchRequest`->
  `FetchResult`. Captura de la colmena "En linea" con Navegador encendido + worker "pagina" por una
  orden RestApi real.
- **Restriccion respetada**: el agente referencia solo `libs/Ecorex.Contracts.Agent` + el NuGet cliente
  de SignalR; NO toca `apps/backend`. El hub de produccion (doc 03 / doc 05 Ola 1 lado servidor) queda
  como tarea del backend, fuera de este worktree.
- **Siguiente**: hub real en `apps/backend` (Authorize + token HMAC->JWT + registry), luego Ola C
  (ejecucion real del sub-agente Gateway contra BD de la LAN, solo-lectura + whitelist).

---

## 2026-07-15 - Agente Conector On-Prem: Ola A (cascara visual "colmena" WPF)

Rama `feat/agente-colmena-gui` (worktree). Se construye SOLO lo que se ve: la cascara visual del
agente de escritorio, sin SignalR real ni ejecucion de sub-agentes (olas siguientes).

- **HexTile** (`Controls/HexTile.xaml`): celda hexagonal (pointy-top 92x106) con 4 estados por
  `DataTrigger` sobre `HiveCellState`: Vacio (Idle, tenue), Lleno (Active, glow), Atendiendo
  (Working, pulso via Storyboard sobre el Effect y la escala) y Error (acento rojo, unico color del
  look monocromo). Nombre en ToolTip para que el panal interloque sin colision de texto.
- **HoneycombPanel** (`Controls/HoneycombPanel.cs`): `Panel` de teselado en panal; filas impares
  desplazadas media celda; columnas ~raiz(N) para un racimo compacto que crece/decrece con los
  workers efimeros.
- **HiveViewModel**: celdas fijas (Config ancla SIEMPRE llena + Gateway/Archivos/Navegador que nacen
  apagadas) + workers EFIMEROS que aparecen (Working) al llegar una peticion y se retiran al terminar
  (el panal crece y decrece). Config/estado de conexion; comandos Probar/Guardar/ToggleConfig/RunDemo.
- **Seam Ola A<->B**: `IHiveConnection` (en `libs/Ecorex.Contracts.Agent`) con `MockHiveConnection`
  como implementacion Ola A. La Ola B cambia el mock por el cliente SignalR real SIN tocar GUI ni VM.
- **Config**: flyout con Client ID / URL del Hub / Estado / "Probar conexion" (stub) y persistencia
  local **DPAPI** (`Services/DpapiConfigStore.cs`, P/Invoke a crypt32, sin NuGet; nunca en repo/plano).
- **Tray icon** (`System.Windows.Forms.NotifyIcon`, sin NuGet): Mostrar / Demo / Salir; cerrar oculta
  a la bandeja, solo "Salir" termina.
- **DEMO/mock**: atajo Ctrl+D (guion encender->atender->apagar + crecimiento) y Ctrl+K abre config.
  Hook de captura por env `ECOREX_AGENT_CAPTURE` (config|demo|busy), inerte en produccion.
- **DPI-aware** (app.manifest PerMonitorV2): nitidez correcta en pantallas escaladas (el equipo esta
  al 125%).
- **Verificado**: compila (`Ecorex.Agent.slnx`, 0 errores) y CORRE en Windows. Capturas de los 3
  estados: (a) colmena idle -> Config lleno, resto vacio, Offline; (b) panel de configuracion abierto;
  (c) colmena "atendiendo" -> capacidades encendidas + workers efimeros + En linea.
- **Restriccion respetada**: el agente referencia SOLO `libs/Ecorex.Contracts.Agent`; NO toca
  `apps/backend`. Solucion separada; el build del backend no se altera.
- **Siguiente**: Ola B (cliente SignalR real detras de `IHiveConnection`), luego ejecucion de
  sub-agentes (Gateway/Navegador/Archivos), allow-list de seguridad e instalador/servicio.
## 2026-07-16 - Contenedor: publicar tablas al menu (Flujo B) + relaciones fila-a-fila (Flujo A)

Sobre el cimiento de la Ola 0. **Nada de esto esta en prod.**

**Flujo B - publicacion (HECHO):**
- `IDataContainerModuleService` (`d24aa53`): publicar/despublicar una tabla raiz creando su nodo de
  menu. Espeja el patron de los formularios, y de ahi salen GRATIS la matriz de roles (su catalogo se
  deriva del menu, clave = Route) y el filtrado del sidebar. Corrige 3 defectos del original: ruta
  INMUTABLE (renombrar no la toca; es la clave del modulo y cambiarla dejaria huerfanos los permisos
  ya asignados), despublicar CONSERVA la ruta (al republicar los permisos siguen valiendo), y
  renombrar RECONCILIA el nombre del nodo. Ruta `dc/{slug}` sin barra (los forms usan `/m/{code}` CON
  barra: como la clave es el Route literal, hay que ser consistente). 12/12 tests duales.
- UI + pagina (`2f59095`): banderin por tabla en el lienzo + modal de publicacion (vista, grupo,
  icono, columnas de grilla y de filtro); `DataModule.razor` (`/dc/{Slug}`) sirve a TODAS las tablas
  publicadas reusando `DataRecordsGrid`. Validado en Chrome: publicar "Perfil Clientes" -> ruta
  `/dc/perfil-clientes` -> item en el menu -> pagina lista sus registros -> y el modulo aparece SOLO
  en Roles y permisos.

**B5 destapo un fallo de seguridad GENERAL (`5bc15b7`):** un usuario cuyo rol no tenia el permiso
igual veia la pagina. La causa no era del modulo nuevo: `CurrentPermissions` solo leia de
`IHttpContextAccessor`, y en un CIRCUITO Blazor (paginas con prerender:false) NO hay HttpContext, con
lo que fallaban el claim del usuario Y el tenant (y sin tenant el filtro global no encuentra el
TenantUser, que es tenant-scoped). Por cualquiera de las dos, `Perms.GetAsync()` devolvia SIEMPRE
`Unrestricted` (fail-open): **todo el gateado en pagina no restringia a nadie**, en cualquier pagina
que lo use; solo se salvaban las que tienen `[Authorize(Policy="Perm:...")]` (se evalua en la
peticion). Fix: resolver usuario+tenant tambien del AuthenticationState del circuito y fijar el
tenant ambiental. Validado: con ver=false -> "Sin acceso"; con ver=true -> entra.
**Observacion no resuelta (decision del dueno: se deja asi):** al abrir Roles y permisos, un modulo
recien publicado se agrega SOLO a los roles existentes con `can_view=true` (acceso por defecto).

**Flujo A - relaciones fila-a-fila (HECHO):**
- `IDataRelationLinkService` (`f59b17a`): ListForRowAsync / SetLinksAsync (reemplazo idempotente del
  set). Valida lo que el esquema no puede: cardinalidad (N:1 y N:N comparten tabla) y que ambos
  extremos pertenezcan a las tablas de la arista.
- **FIX del bug latente**: `DeleteRowAsync` limpiaba los `DataContainerLinks` (entidad vieja) pero NO
  los nuevos; como sus FKs a filas son Restrict (cascada por ambos extremos = rutas multiples, error
  1785 en SQL Server), borrar una fila vinculada habria reventado en cuanto se escribiera el primer
  vinculo. Se limpian en el mismo SaveChanges (una transaccion). 12/12 tests duales.
- `RowRelationPicker`: entra por el punto de extension `RowEditorExtras` de la Ola 0 (ni la grilla ni
  el editor cambiaron). N:1 -> select; N:N -> casillas. La fila NUEVA no tiene Id hasta guardarse, asi
  que la seleccion se persiste en `OnRowSaved`, ya con el Id definitivo.
- **Validado en Chrome** con el modelo demo (Pedidos->Clientes N:1, Pedidos->Productos N:N): se creo
  PED con Fecha+Total, ACME SAS y Teclado; los DOS vinculos quedaron en `data_model_relation_links`
  contra el Id de la fila nueva, y al reabrirla el picker los recupera.

**DESPLEGADO a prod 2026-07-16** (`fase-0/clon-backbone` @ `ff31b4e`, autorizado por el dueno):
backup `ecorex-2026-07-16-1201.sql.gz` -> `build --no-cache` -> `up -d`. Aplico 1 migracion
(`DataContainerModuleAndRelationLinks`, aditiva). Sano: /login 200, logs sin errores, las 5 columnas
de publicacion y la tabla `data_model_relation_links` creadas. OJO: este deploy incluye el fix de
permisos, asi que el gateado en pagina ahora SI se aplica en toda la consola (antes era fail-open).
Antes de desplegar se limpio el residuo de las pruebas en el dev local (se despublico "Perfil
Clientes", se borraron las filas del modelo demo y el permiso auto-agregado al rol).

**Pendiente:** (a) el orden por columna sigue siendo alfabetico (EAV como string); (b) decidir si la
relacion se muestra como columna en la grilla (hoy solo escalares).

---

## 2026-07-16 - Contenedor de datos: Ola 0 (cimiento para publicar tablas al menu)

Idea del dueno: que cada tabla dinamica del Contenedor se pueda **publicar al menu** para que el
usuario final gestione sus registros. Antes de codear se investigo el terreno; dos hallazgos
cambiaron el plan:

1. **Ya existe el patron**: un `FormDefinition` se publica como modulo (`IsModule` +
   `ModuleMenuNodeId` -> `SetModuleAsync` crea el MenuNode con ruta `/m/{code}` y `FormModule.razor`
   pinta la bandeja con `ListColumnsJson`/`FilterFieldsJson`). Se ESPEJA, no se reinventa.
2. **La cadena ya es data-driven**: `RolService.GetModuleCatalogAsync` deriva el catalogo de la
   matriz de roles **del menu real** (clave del modulo = `Route`), `PermissionPolicyProvider` fabrica
   `Perm:{ruta}:{accion}` al vuelo y `MenuPermissionFilter` poda el sidebar. O sea: publicar una
   tabla = crear un nodo de menu; permisos y filtrado salen gratis.

Decisiones (confirmadas con el dueno): **CRUD completo** en el modulo publicado; FASE 2 de
relaciones y publicacion **en paralelo**. Ruta **inmutable** (la clave del modulo ES el Route:
renombrar la tabla romperia los permisos ya asignados; es el bug que hoy tienen los formularios).
Permiso verificado en runtime (no cabe `[Authorize(Policy)]` con clave dinamica). Solo tablas raiz.

**Ola 0 (cimiento) - HECHA:**
- **Una sola migracion dual** (`DataContainerModuleAndRelationLinks`, PG + SQL Server) con AMBOS
  cambios, para que los dos flujos siguientes no vuelvan a tocar migraciones ni se peleen el
  snapshot: campos de publicacion en `DataContainer` (`ModuleRoute` unico por tenant, `MenuNodeId`
  SetNull, `ModuleIcon`, `ListColumnsJson`, `FilterColumnsJson`) + entidad `DataModelRelationLink`
  (vinculo fila-a-fila colgado de la ARISTA, la FASE 2 diferida). Up() 100% aditivo.
- **`ListRowsPagedAsync`**: busqueda, filtros por columna, orden y paginado EN EL SERVIDOR (antes:
  tope de 500 filas y filtrado en memoria). `ListRowsAsync` se conserva para configurador/selectores.
  Tests de integracion nuevos en **matriz dual (6/6 PG + SQL Server)**, que cazaron un bug real: el
  escape de LIKE no funcionaba en NINGUN motor porque la sobrecarga de 2 args emite `ESCAPE ''`
  (un usuario buscando "50%" no encontraba nada). Tambien: `ToLower+Like` en vez de `ILike`
  (Npgsql-only) y desempate por Id para que el OFFSET no repita/pierda filas.
  Limitacion documentada: el orden por columna es ALFABETICO (el EAV guarda todo como string).
- **`DataRecordsGrid`** (Components/Shared/Data, + CSS scoped propio porque los `dc-*` eran un
  `<style>` global de la pagina): grilla + editor de filas + import/export extraidos de
  `ContenedorDatos.razor` (2046 -> ~1790 lineas). Punto de extension `RowEditorExtras` (recibe el Id
  de fila; null = nueva) + `OnRowSaved`, que es donde enchufa la FASE 2 sin volver a tocarlo.
  El configurador ya lo consume dentro de su modal; el modulo publicado usara el MISMO.
- **Validado en Chrome**: el panel de Datos sigue funcionando con el componente (grilla, contador
  desde la consulta paginada, busqueda server-side 0/1, alta y borrado de fila, CSS scoped aplicado).
- **Pendiente**: Flujo A (servicio de vinculos + picker) y Flujo B (SetModuleAsync + UI de publicar +
  pagina `/dc/{slug}` con permiso en runtime). NO desplegado a prod.

---

## 2026-07-16 - DEPLOY a prod: modal de tercero compartido + formularios por tercero

Desplegado a prod (10.0.0.3, build-from-git de `fase-0/clon-backbone` @ `d76c5b3`) con autorizacion
explicita del usuario. Ambas ramas remotas quedaron al dia (`main` y `fase-0/clon-backbone`).

- **Backup previo**: `/opt/ecorex/backups/ecorex-2026-07-16-0547.sql.gz` (`./backup.sh`).
- **3 migraciones aplicadas al arrancar** (prod venia de `AddYCloudProvider`): `AddSqlConsoleLogs`,
  `RelationsAsEntity`, `AddTerceroFormLinks`.
- **`RelationsAsEntity` es destructiva**: se midio el impacto en prod ANTES (1 columna de relacion,
  3 celdas, 0 vinculos, 3 modelos, 6 tablas) -> asumible. Post-deploy verificado: la relacion
  sobrevivio como **1 arista** en `data_model_relations`, 0 columnas Reference/RelationMany
  restantes, `referenced_container_id` eliminada. Se perdieron 3 valores de celda (esperado; el
  vinculo dato-a-dato se re-cableara en la FASE 2 diferida).
- **Sano**: `ecorex-app` Up, `/login` HTTP 200, logs sin errores (solo el aviso benigno "Failed to
  determine the https port for redirect", preexistente). Tablas nuevas creadas y vacias
  (`tercero_form_links`, `sql_console_logs`).
- **Nota**: en prod los formularios del modal arrancan sin asociar (`tercero_form_links` vacia); se
  eligen con "Configurar campos" del Directorio General. Migracion SQL Server generada pero sin
  aplicar/probar (no hay instancia; prod es PG).

---

## 2026-07-15 - Cargador de contactos reusa EL MISMO modal de tercero (componente compartido)

Feedback del dueno: en Cargador de contactos (000740) el boton "Nuevo contacto" saltaba a
Directorio General (000232). Un primer intento abrio un modal propio (`_nc*`) dentro del Cargador;
el dueno lo rechazo: "un contacto ES un cliente; ambos modulos se alimentan de los MISMOS
registros de este modal, no debemos crear uno nuevo, la idea es reusarlo". Decision (AskUser):
**componente compartido in-place** (no navegar, no duplicar).

- **`TerceroModal.razor` (+ `.razor.css`) nuevo en `Components/Shared/`**: se EXTRAJO el modal grande
  de tercero (pestanas Datos / Relaciones / Contacto Cliente / Actividades, perfiles, fichas
  configurables por perfil) + el sub-modal de contacto de relacion desde `DirectorioGeneral.razor`.
  API por `@ref`: `OpenCreate()`, `OpenEditAsync(id)`, `OpenContacto(parentId, c)`; parametro
  `OnChanged` (EventCallback) para que el host refresque su lista/contadores. CSS scoped copiado de
  `DirectorioGeneral.razor.css` para fidelidad milimetrica.
- **`DirectorioGeneral.razor`**: dejo de tener el modal inline (se borraron ~825 lineas de markup +
  code-behind movido); ahora renderiza `<TerceroModal @ref=...>` y sus botones (nuevo cliente,
  editar tercero, agregar/editar contacto) lo invocan por `@ref`. Conserva lista/tabla/KPIs,
  configurador de campos, asignar-a-empresa y borrar-contacto. Se retiro el `?crear=1` (ya no hay
  salto que lo justifique).
- **`GestorContactos.razor`**: se elimino el modal propio `_nc*`; "Nuevo contacto" ahora abre el
  componente compartido in-place (`_terceroModal.OpenCreate()`), refresca con `OnChanged`.
- **Bug de concurrencia corregido**: el `OnInitializedAsync` del componente y el de la pagina
  compartian el mismo `EcorexDbContext` scoped y corrian en paralelo -> "A second operation was
  started on this context". Fix: el componente NO hace BD en `OnInitializedAsync`; permisos +
  `EnsureDefaults` + carga de campos se hacen PEREZOSAMENTE al abrir el modal (evento de usuario,
  nunca concurrente con la init del host).
- **Validado en Chrome (local 5253, Owner)**: Cargador "Nuevo contacto" abre el modal SIN salir de
  `/cargador-contactos`; creado "Distribuidora Andina Test SAS" -> aparece en Directorio General
  (mismos registros Tercero). Directorio General carga sin regresion y su "Nuevo cliente" abre el
  mismo modal. Build SuperAdmin 0 errores.
- **Pendiente**: (a) limpiar reglas CSS muertas del modal en `DirectorioGeneral.razor.css` (inocuas,
  se dejaron por bajo riesgo). (b) NO desplegado a prod (espera confirmacion del usuario).

---

## 2026-07-16 - Formularios elegibles por tercero en la 3a columna del modal

Item 2 del usuario: "en la herramienta de configuracion de campos podamos configurar los formularios
que se pueden cargar en el modal en la tercera columna; los datos del formulario deben quedar
asociados al tercero id". Decision del usuario (AskUser): **"varios formularios elegibles"**.

- **Hallazgo que ahorro una tabla**: NO existe FK response->tercero, pero el patron ya probado del
  arranque form-first ancla la respuesta por `FormResponse.Reference` (ahi guarda el numero de la
  tarea). Se reusa: `Reference = "TERCERO:{terceroId}"`, cubierto por el indice existente
  `(TenantId, DefinitionId, Reference)`. **No se creo tabla de respuestas.**
- **Dominio/EF**: `TerceroFormLink` (TenantEntity: FormDefinitionId + SortOrder) = solo CONFIG de que
  formularios se ofrecen por tenant. FK a FormDefinition con **Restrict** (quitar del modal es
  explicito, no efecto de borrar la definicion); indices `(TenantId, SortOrder)` y unico
  `(TenantId, FormDefinitionId)`. **Migracion DUAL** `AddTerceroFormLinks` (PG 20260716093705 +
  SQL Server), aditiva (solo CreateTable + indices; Down dropea).
- **Servicio**: `ITerceroFormService`/`TerceroFormService` (Ecorex.Application/Directorio) con
  `ListAsync` / `ListCandidatesAsync` (activos no archivados aun no ofrecidos) / `AddAsync`
  (idempotente) / `RemoveAsync`, + `static ReferenceFor(terceroId)` como unica fuente del ancla.
  Registrado en DI. Tenant-scoped por filtro global.
- **UI**: (a) "Configurar campos" (Directorio General) gana la seccion **FORMULARIOS DEL TERCERO**
  (lista + selector de candidatos + quitar); (b) `TerceroModal` gana la **3a columna** en la pestana
  Datos (chips de formularios + `DynamicFormRenderer` con `DefinitionId` + `Reference` +
  `Mode=Fill`), solo en modo edicion (un formulario necesita un tercero al cual anclarse).
- **Validado en Chrome (local 5253, Owner)**: asociado "Solicitud de cotizacion" (FRM-001) -> fila en
  `tercero_form_links`; al abrir ANDINA S.A.S aparece la 3a columna con el chip; al elegirlo se
  renderiza el formulario real (9 controles) y se crea el borrador
  `reference=TERCERO:019f4bd3-9679-7abf-b420-02c805b0a010` (= id de ANDINA); llenados 5 campos, el
  `data` jsonb los persiste contra ese tercero. La validacion server-side corre (pidio el lookup
  obligatorio "Cantidad estimada" del formulario demo). Build solucion 0 errores; 379/379 tests.
- **Correccion del dueno (misma sesion)**: la 3a columna NO era una columna nueva. El panel derecho
  del prototipo ("Prospecto de cliente / Oportunidad de negocio", `aside.dg-prosp`) **era justamente
  el espacio pensado para los formularios**; su contenido era solo una MUESTRA. Se retiro la 4a
  columna que se habia agregado (`dg-forms-col`) y los formularios pasaron a ese aside, con las
  oportunidades debajo (separador) solo bajo `CrmWiring`. Revalidado: el form renderiza dentro del
  aside sin desborde horizontal y RECUPERA los valores guardados del tercero.
- **Pendiente**: (a) migracion SQL Server sin aplicar/probar (no hay instancia levantada; PG si).
  (b) `SubmittedByTenantUserId` no se pasa al renderer (la respuesta no estampa el usuario).
  (c) reordenar formularios (SortOrder existe, sin UI). (d) NO desplegado a prod (espera confirmacion).

---

## 2026-07-15 (2) - Cargador: filas abren el modal compartido + oportunidades por tercero

Continuacion del reuso del modal de tercero. Feedback: en Cargador de contactos (000740) las filas
abrian OTRO modal (la "FICHA DEL CLIENTE" propia `_cm*`, que duplicaba el TerceroModal). Se jubilo.

- **TerceroModal** gana (parametros nuevos): `CrmWiring` (bool) y `OnAddOpportunity`
  (EventCallback). Con `CrmWiring=true` (solo Cargador) el aside reservado muestra las
  **oportunidades del tercero** (via `IGestorContactosService.ListOportunidadesByTerceroAsync`),
  la pestana Contacto Cliente habilita la **fecha de "Proxima atencion"**, y al registrar una nota
  "Oportunidad"/"Atencion" **crea la oportunidad/cita** (cableado CRM portado del `_cm`). Metodo
  publico `ReloadOpportunitiesAsync()`. En Directorio General (`CrmWiring=false`) las notas son solo
  bitacora (sin cambios). Estilos `tm-opp-*` en TerceroModal.razor.css.
- **GestorContactos**: se elimino el modal `_cm*` (markup + estado + OpenCliente/CloseCliente +
  AddNota/DeleteNota/ResetNoteForm). `OpenClienteAsync` ahora delega en
  `_terceroModal.OpenEditAsync(id)`; el componente se cablea con `CrmWiring="true"
  OnAddOpportunity="OnAddOpportunityFromModal"`; al crear una oportunidad se refresca el aside del
  modal. Se retiro el kanban por etapa de la pestana Oportunidades (con su drag&drop).
- **Pestana Oportunidades agrupada por tercero** (item del usuario): la vista "Por cliente" lista
  un grupo por tercero (avatar, nombre, N oportunidades, valor total) con sus oportunidades y chips
  de etapa; el encabezado del grupo abre el MISMO modal compartido. Toggle "Por cliente / Tabla".
  Estilos `gc-opp-*` en GestorContactos.razor.css.
- **Validado en Chrome (local 5253, Owner)**: pestana Oportunidades agrupa (ANDINA 4 opps $108.2M,
  INGETEL, Produvarios, Maria Fernanda); abrir el grupo ANDINA abre "EDITAR TERCERO" in-place con el
  aside "Oportunidades 4 abiertas $108,200,000" + "Agregar oportunidad". Build 0 errores.
- **Pendiente (item 2, proxima ola)**: formularios elegibles en la 3a columna del modal. Diseno
  confirmado por el usuario = **"varios formularios elegibles"**. Plan (segun mapeo del sistema de
  formularios): reusar `DynamicFormRenderer` con `Reference="TERCERO:{id}"` (NO hay FK response->tercero;
  el patron form-first ya ancla por `Reference`), + tabla pequena de CONFIG (que formularios se
  ofrecen por tenant) + migracion dual PG/SQL Server + seccion en "Configurar campos" + render en el
  modal. NO desplegado a prod (espera confirmacion).

---

## 2026-07-14 - Tareas de proceso: Ola 0 (decisiones) + Ola A1 (encargado del primer nodo)

Capitulo nuevo en el vault: `01. Requerimiento/Capa 2 Tareas y Proyectos/Tareas de proceso -
Arranque y encargado del flujo/` (docs 00 indice, 01 arquitectura, 02 cinco historias de usuario,
03 plan por olas). Continua "Modulo de Tareas - Creacion y ejecucion".

- **Auditoria (read-only)**: al crear una actividad desde el menu Mis Procesos, el enrolamiento en
  el flujo SI funciona (instancia + primer paso Pending/IsCurrent), pero el **ENCARGADO no**: el
  wizard lo pide a mano filtrado por los cargos del CONCEPTO (`TaskWizard.razor:515-524`), no por
  el cargo del **primer nodo BPMN**; `INodeAssigneeResolver` no se consumia desde el arranque; y el
  primer paso nace **sin** `AssignedToTenantUserId` (`WorkflowEngine.AddStep:639-656`), resolviendose
  perezosamente al reclamar. Ademas los conceptos `IniciaModulo` (form-first) igual abren el wizard
  de 4 pasos (el formulario es el paso 3), en vez de abrir DIRECTO el formulario.
- **Hallazgo que cambia el diseno**: `FormDefinitionId` existe SOLO en `ActividadSubcategoria`
  (`ActividadSubcategoria.cs:60`); **no hay formulario por nodo BPMN**.
- **Ola 0 - decisiones del usuario (CERRADA)**: **D1** el arranque form-first usa el formulario del
  CONCEPTO ahora, y el formulario POR NODO se compromete como **Ola D** (dominio + migracion dual +
  editor + runtime), no como backlog difuso; **D2** el flujo manda: el iniciador NO puede elegir
  encargado fuera del cargo del primer nodo (combo restringido + validacion server-side); **D3** un
  concepto con flujo sin publicar SI se ve en el menu, pero el arranque debe AVISAR con un banner
  que la actividad nacera SIN proceso (se ataca el silencio, no la visibilidad).
- **Ola A1 - HECHA**: `IWorkflowStartService` + `WorkflowStartService`
  (`Ecorex.Application/Workflows/`), registrado en DI. Dada una subcategoria, camina el grafo **EN
  SECO** (sin instancia, sin persistir) desde el `startEvent`, **atraviesa compuertas** y devuelve
  el **primer nodo Task** + sus **cargos** (`WorkflowNodePolicy`) + sus **candidatos**
  (`INodeAssigneeResolver`, reusado). La resolucion de compuertas es **espejo exacto** de
  `WorkflowEngine.ResolveOutgoing` con `approvalResult = null` -> el nodo que devuelve es **el mismo
  que el motor activara**. Nunca lanza: reporta `FirstStepStatus` (Ok / SinFlujo / FlujoNoPublicado /
  SinNodoTask / SinCargo / SinCandidatos), que es justo lo que necesitan A2 (preseleccionar),
  A3 (persistir) y C1 (el banner de D3).
- **Verificado**: `WorkflowStartServiceTests` (7 casos) **verde en matriz dual**: PostgreSQL 7/7 y
  SQL Server 7/7. Cubre flujo lineal (primer Task "Cotizar" + cargo + candidato unico), **compuerta
  justo despues del startEvent (la atraviesa)**, los 4 estados de config incompleta y el
  **aislamiento cross-tenant**. Solucion completa en verde.
- **Ola A2 - HECHA**: `TaskWizard.razor` consume `IWorkflowStartService`. En una actividad-proceso el
  combo "Encargado" se llena con los **candidatos del cargo del PRIMER NODO** (ya no con los cargos
  del concepto), queda **RESTRINGIDO** a ellos (D2: el flujo manda), muestra el chip
  **"Paso 1 - {nodo} - {cargo}"** + la nota "Lo dicta el flujo...", y **PRESELECCIONA** al candidato
  cuando hay uno solo. Si el cargo esta vacante, el combo se deshabilita y avisa que hay que
  ocuparlo en Dependencias. `ValidateStep(1)` rechaza un encargado fuera de los candidatos (atajo de
  UI; el servidor lo revalidara en A3). Una actividad SIN flujo conserva el comportamiento clasico.
- **Bug hallado en la validacion visual y corregido**: al cambiar de una actividad-proceso a una
  actividad simple, el encargado **que habia dictado el flujo** se quedaba pegado (el usuario nunca
  lo eligio). Ahora se limpia al salir del modo restringido.
- **Verificado en Chrome real (tenant demo)**: `Cotizacion de equipos` -> chip "Paso 1 - Requerimiento
  - Asesor Comercial", 1 sola opcion, preseleccionado **Operator**; `Compra urgente` -> chip "Paso 1 -
  Aprobacion jefe de compras - Aprobador", 1 sola opcion, preseleccionado **Admin** (cargo distinto ->
  persona distinta: el servicio lee de verdad el BPMN); `Solicitud de compra` (sin flujo) -> sin chip,
  encargado limpio, lista completa (11 usuarios). Regresion: **360/360** Application.Tests + **35/35**
  Domain.Tests.
- **Ola A3 - HECHA**: `TaskItemService.CreateAsync`, dentro de la MISMA transaccion y tras
  `StartInstanceAsync`: (1) toma el paso actual (IsCurrent/Pending); (2) **revalida D2 en SERVIDOR**
  -- si el nodo tiene cargo y el encargado no es candidato (`INodeAssigneeResolver`) -> **rollback
  total** + error tipado (restringir el combo del wizard no basta: un API podria saltarselo);
  (3) **FIJA** `WorkflowStepHistory.AssignedToTenantUserId` (el paso ya no nace colgando);
  (4) audita "ruto el primer paso del flujo a {usuario}". `INodeAssigneeResolver` se inyecta como
  parametro REQUERIDO: una regla de gobierno no debe poder desactivarse en silencio si falla la DI.
- **Verificado (tests)**: `WorkflowStartServiceTests` sube a 10 casos, **verde en matriz dual
  (PG 10/10 + SQL Server 10/10)**: el primer paso nace asignado + notificado; encargado fuera del
  cargo -> Invalid con rollback total (ni tarea, ni instancia, ni pasos); actividad sin flujo sigue
  aceptando cualquier encargado.
- **Verificado (CICLO COMPLETO en Chrome real)**: Owner entra por Mis Procesos > Cotizacion de
  equipos -> wizard con chip "Paso 1 - Requerimiento - Asesor Comercial" y Operator preseleccionado
  -> crea **T00216** -> en BD: ENROLADA / paso "Requerimiento" / Pending / **asignado a operator**
  (antes NULL) -> auditoria: creo la tarea -> notifico a operator -> inicio el flujo -> **ruto el
  primer paso del flujo a operator** -> **login como Operator: T00216 aparece en "Pendientes mios"
  (27) SIN reclamarla**. Ese claim manual era justo lo que A3 elimina.
- **BUG PREEXISTENTE ENCONTRADO Y CORREGIDO (no era de estas olas)**: el host **Ecorex.Api no
  arrancaba** desde el merge de la rama de formularios: `FormResponseService` exige
  `IFormRecordBroadcaster` y **solo la consola Blazor lo registraba**, asi que el contenedor de DI de
  la API no se podia construir -> **27 tests de endpoints en rojo** (Auth/Admin/TenantUsers/
  Onboarding...). Se agrega `NoOpFormRecordBroadcaster` junto a su interfaz (mismo patron que
  `NoOpTaskBroadcaster`, "para procesos sin SignalR (Api, tests)") y se registra en `Ecorex.Api`.
  Los 27 tests vuelven a verde.
- **Nota de coordinacion**: otra sesion estaba refactorizando en PARALELO el mismo working tree
  (extraer el aprovisionamiento del menu del seeder a `IMenuProvisioningService`, para que un tenant
  creado por el alta no nazca sin menu). Su refactor dejo el arbol sin compilar un rato; se completo
  el cableado que faltaba (`TenantAdminService` ahora exige `IMenuProvisioningService` -> doble
  `NoOpMenuProvisioning` en los tests que no ejercitan el menu).
- **Ola B1 - HECHA**: los conceptos form-first (`IniciaModulo` + `FormDefinitionId`) arrancan por el
  FORMULARIO, no por el wizard. Nuevo `FormFirstStarter.razor`; `ActivityBoardDetail` intenta
  `TryOpenAsync(sub)` y, si el concepto no es form-first o su formulario no sirve, **cae al wizard**
  (escape hatch). Nuevo `IFormResponseService.SetReferenceAsync` para anclar una respuesta ya enviada
  al numero de la tarea (que al diligenciarla aun no existia).
- **Lo importante de B1 es el ORDEN**: antes el wizard creaba la tarea AL ENTRAR al paso 3 (parche de
  la Ola 5), asi que un formulario invalido dejaba una **tarea huerfana con su flujo ya arrancado**.
  Ahora: formulario -> validacion en SERVIDOR -> recien entonces nace la actividad -> se ancla la
  respuesta.
- **Bug hallado en la validacion visual y corregido**: con `TituloAuto` = "Requerimiento infra -
  @cliente" y sin cliente capturado, la tarea nacia titulada "Requerimiento infra - " (separador
  colgando). `RenderConceptTemplate` limpia el borde cuando el token queda vacio.
- **Verificado en Chrome real**: el concepto form-first abre FRM-001 **directo** (sin wizard);
  enviarlo **vacio bloquea y NO crea tarea** (la ultima siguio siendo T00216) -- ese era el punto que
  fallaba; al llenarlo nace **T00218** con titulo limpio "Requerimiento infra", en el tablero del
  concepto, y la respuesta queda **Submitted con ref=T00218**. Regresion: 360/360 Application.Tests +
  64/64 Integration (flujos y formularios, matriz dual).
- **Pendiente menor anotado**: mapear un campo del formulario al token `@cliente` (hoy no hay
  convencion que diga QUE campo es el cliente; se limpia el separador para no romper el titulo).
- **Ola B2 - HECHA**: se retira del wizard TODO el parche form-first de la Ola 5: la rama
  `IsFormFirst` del paso 3, la **creacion anticipada de la tarea** en `NextStep`, `FormStepLocked`,
  los handlers `OnFormSubmittedAsync`/`OnFormSkipAsync` y el estado muerto. El wizard queda con **UN
  SOLO camino**: la tarea se crea unicamente al pulsar "Guardar actividad" / "Guardar y crear otra".
  El paso 3 queda informativo (el formulario se diligencia desde el DETALLE, ADR-0038).
- **Verificado en Chrome real (forzando el escape hatch)**: se desactivo FRM-001 en la BD local ->
  el concepto form-first **cayo al wizard** (escape hatch de B1 confirmado); se camino el wizard
  **hasta el paso 4**, pasando por el paso Formulario -- justo donde antes se creaba la tarea -- y la
  ultima tarea **siguio siendo T00218**: no se creo nada. El resumen seguia en "Borrador / Sin
  guardar". FRM-001 restaurado a Active. Solucion verde + 360/360 Application.Tests.
- **Ola C1 - HECHA**: guardas de coherencia (D3: avisar, no ocultar). Tres piezas: (1) banner ambar
  en el arranque (TaskWizard) cuando el flujo no es utilizable -- cubre FlujoNoPublicado / SinNodoTask
  / SinCargo; FormFirstStarter ya lo traia de B1; (2) chip "borrador" en el menu (NavMenu) para la
  hoja de un concepto cuyo flujo no esta publicado -- se resuelve con el set de WorkflowDefinitions
  publicadas del tenant; (3) WorkflowEngine.PublishAsync bloquea publicar un flujo SIN paso Task
  (irrecuperable). El caso "paso sin cargo" NO bloquea la publicacion (muy rigido) -> se avisa al
  crear con el banner.
- **Verificado**: test dual `Publish_FlowWithoutTaskNode_IsRejected` (PG + SQL Server 2/2); regresion
  45 workflow + 360 unitarios verdes. En Chrome real: despublicando COT-COM en local, la hoja mostro
  chip "borrador" y el wizard el banner "nacera sin proceso"; restaurado.
- **Ola C2 - HECHA (verificacion, sin codigo)**: QA end-to-end del arranque de tareas-proceso.
  Arranque E2E visual+BD (desde Mis Procesos se creo **T00219**, nace enrolada con el primer paso
  "Requerimiento" asignado a operator; operator la ve en "Pendientes mios" sin reclamarla). El ciclo
  runtime (atender -> avanzar -> gateway Aprobada/Rechazada -> reinicio) se apoya en
  `WorkflowInboxTests` **6/6 en matriz dual** (invocan los mismos servicios que los botones del
  detalle). Registrado en el vault (05. Pruebas/Historial de corridas, 2026-07-14).
- **Gaps de config del demo hallados (no son bug de codigo)**: el flujo COT-COM v1 tiene Facturacion/
  Entrega sin cargo, y el concepto "Cotizacion de equipos" no tiene columna de cierre (tablero previo
  al auto-tablero 388e895). Anotados en el backlog del capitulo para cerrarlos por configuracion.
- **Nota**: el clic visual "Completar paso" no se re-ejercito por inestabilidad del circuito Blazor
  Server tras reinicios; cubierto por los tests duales y la corrida previa (tarea #67).
- **Ola D - YA EXISTIA (verificada, sin codigo nuevo)**: al ir a construir "formulario por nodo" se
  hallo que YA esta completo desde FASE 4 (ADR-0015, migracion AddDynamicForms 2026-07-03), con un
  mecanismo mejor que el propuesto: entidad join `WorkflowNodeForm` (indice unico por nodo), no
  `WorkflowNodePolicy.FormDefinitionId`. D1 (dominio+migracion dual), D2 (selector en FlowEditor
  Acordeon 2 -> WorkflowDesignService.SetNodeFormAsync) y D3 (runtime GetTaskStepFormsAsync +
  seccion "Formularios del paso" en TaskDetailModal) existen y estan cableados. Verificado:
  `DynamicFormsTests` 16/16 dual, incluido el ciclo completo (asignar form al nodo Cotizacion ->
  avanzar -> el paso pide su form con gateway adelante -> enviar con "Aprobada" -> completa y avanza
  a Facturacion). No se construyo nada: habria sido redundante.
- **Unico punto abierto de la Ola D (decision de producto)**: la precedencia form-first (formulario
  del CONCEPTO, admision) vs form-por-nodo (formulario del PASO) NO existe; son fases ortogonales.
  La D1 original ("gana el del nodo") se penso creyendo que el form-por-nodo no existia. Recomendacion:
  dejarlos ortogonales; a decidir con el usuario.
- **Deuda menor**: dos APIs escriben WorkflowNodeForms (FormDefinitionService.AssignToWorkflowNodeAsync
  sin UI + WorkflowDesignService.SetNodeFormAsync del editor); unificar cual es canonica.
- **Siguiente**: cerrar la decision de precedencia + DEPLOY a prod del acumulado (A/B/C + previo).

## 2026-07-14 - Fix menu: "Directorio General" (000232) desaparecido de "Negocio"

- **Sintoma (reporte del usuario)**: en la seccion de menu "Negocio" faltaba el modulo para crear terceros.
- **Causa raiz**: el feature original (commit `83100d9`) agrego "Directorio General" (route `directorio-general`,
  el CRM de terceros: empresas/personas/contactos, boton "+ Nuevo cliente") SOLO al bloque de seed INICIAL del
  menu, que corre unicamente para tenants nuevos. Los tenants ya sembrados (demo SKY SYSTEM incluido) nunca lo
  recibieron -> `SELECT count(*) FROM menu_nodes WHERE route='directorio-general'` daba 0. No fue una regresion
  de esta sesion; el item nunca se propago a tenants existentes.
- **Fix**: se agrego una llamada idempotente `EnsureMenuItemInSectionAsync(tenantId, sectionSlug:"nego",
  route:"directorio-general", name:"Directorio General", legacyCode:"000232")` en el bloque de reconciliacion
  del `DatabaseSeeder`. Repone el item en la seccion "Negocio" de todos los tenants ya sembrados sin duplicar.
- **Verificado**: al reiniciar la app el log confirmo "item 'Directorio General' (directorio-general) agregado a
  1 vista(s)"; en Chrome real, "Negocio" ahora lista 4 items (Creacion de clientes, Seguimiento de clientes,
  Cargador de contactos, Directorio General) y la pagina `/directorio-general` carga el CRM de terceros.
- **Siguiente**: acumulado de cambios de menu/conceptos/tableros de esta tanda sigue PENDIENTE de deploy a prod.

## 2026-07-13 - Sesion (worktree formularios): F6 - permisos por campo (visibilidad por rol)

- **Hecho (F6, doc 01 D8)**: permisos a nivel de campo.
  - ESQUEMA: `FormQuestion.FieldVisibilityJson` = { "hide":[roles], "readonly":[roles] } (nombres de
    TenantRole: Owner/Admin/Supervisor/Advisor). Migracion dual `AddFormFieldVisibility`. Local; PENDIENTE prod.
  - RENDERER: lee el rol del claim `tenant_role` (cascading AuthState; null en el visor publico -> sin
    restriccion). Un rol en `hide` no ve el campo (RenderQuestion return); en `readonly` lo ve dentro de un
    `<fieldset disabled>` (deshabilita el control sin tocar RenderInput). El valor se conserva al guardar.
  - DESIGNER: checkboxes por rol "Ocultar para" y "Solo lectura para" en el tab Datos.
- **Verificado en navegador (rol Owner)**: NIT con hide=[Owner] -> NO se pinta; Ciudad con readonly=[Owner]
  -> input efectivamente deshabilitado (`:disabled` via `fieldset[disabled]`). Solution verde; 360 tests.
- **WEBHOOKS/INTEGRACIONES: PENDIENTE (decision del usuario 2026-07-13)**: el usuario quiere botones en el
  formulario con reglas de accion configurables (integraciones). El patron .NET correcto NO es reflexion
  abierta (el proyecto la prohibe) sino un REGISTRO DE VERBOS TIPADOS resuelto por DI -> ES EL RulesEngine
  YA EXISTENTE (`Ecorex.Application/Rules/Verbs/`, IRuleVerb). Mapeo: boton (FormControlType.Button) ->
  FormFieldRule -> verbo tipado (allow-list) -> integracion. El usuario analizara la documentacion antes de
  construirlo. NO construir webhooks hasta entonces.
- **Siguiente (resto de F6)**: mascaras de ENTRADA; impresion/PDF con plantilla (object storage); captura
  Tier 2 real (foto/firma/GPS/archivo/barcode) con object storage; botones con reglas de accion (ver nota).

## 2026-07-13 - Sesion (worktree formularios): F5 designer + F6 transversales (defaults dinamicos + formato)

- **F5 (cierre)**: el campo Subform ahora se crea/configura EN EL DESIGNER (tipo "Subformulario" en la
  paleta + selector "Formulario hijo (detalle)" en el tab Datos que lista las definiciones). VERIFICADO
  en navegador (FRM-002 seleccionado como hijo de FRM-021).
- **F6 ARRANQUE (transversales de campo, doc 01 D8)**:
  - ESQUEMA: `FormQuestion` += `DefaultDynamic` (enum None|Today|CurrentUser|CurrentEntidad) + `Format`
    (string: currency|percent|integer|decimal). Migracion dual `AddFormFieldTransversals`. Aplicada
    local; PENDIENTE prod (doc 04).
  - RENDERER: al abrir a llenar, el default DINAMICO gana sobre el literal (Hoy -> fecha de hoy, Usuario
    -> id); `FormatValue` muestra moneda/%/entero (aplicado a los campos calculados de solo lectura).
  - DESIGNER: dropdowns "Valor por defecto dinamico" y "Formato" en el tab Datos.
- **Verificado en navegador**: campo Fecha con default Today -> pre-llenado 2026-07-13; subtotal con
  format currency -> "$ 1,620,000". Solution verde; 360 tests.
- **Siguiente (resto de F6)**: permisos por campo (FieldVisibilityJson ver/editar por rol), impresion/PDF
  con plantilla + object storage, webhooks tipados al confirmar, captura Tier 2 real (foto/firma/GPS/
  archivo/barcode) con object storage, mascaras de entrada. Son piezas de integracion grandes (object
  storage, PDF, cola de webhooks) -> incrementos siguientes.

## 2026-07-13 - Sesion (worktree formularios): F5 - maestro-detalle entre formularios

- **Hecho (F5, doc 01 D7)**: el detalle son registros de OTRA definicion (no solo GridDetail embebido).
  - ESQUEMA: `FormControlType` += `Subform`; `FormQuestion.SubformDefinitionId`; **tabla nueva
    `form_record_links`** (padre-hijo: parent_response_id, parent_field_code, child_response_id, unico).
    Migracion dual `AddFormRecordLink` (1 tabla + 1 columna). Aplicada local; PENDIENTE prod (doc 04).
  - SERVICIO: `FormResponseService.ListChildrenAsync/AddChildAsync/UnlinkChildAsync` (crea el hijo como
    FormResponse propio de la definicion hija + enlace). `Subform` marcado IsNonInput (no va en el jsonb).
  - RENDERER: el campo Subform lista los hijos + "Agregar registro" -> abre el formulario hijo ANIDADO
    (DynamicFormRenderer recursivo, Fill) -> al enviar, el hijo (con su numero/estado) aparece enlazado.
    Quitar desengancha (conserva el hijo).
- **Verificado en navegador (`ecorex_forms`)**: FRM-021 con campo Subform -> hijo FRM-002; Agregar ->
  formulario FRM-002 anidado (Bodega/Fecha) -> Enviar -> hijo **FRM-002-000001 (Confirmado)** enlazado en
  la lista del padre; `form_record_links` con el enlace (detalle -> FRM-002-000001). Solution verde; 360 tests.
- **Siguiente**: designer para crear/configurar el campo Subform (elegir definicion hija) sin SQL;
  reportar el detalle aparte (ya es posible: cada hijo es un registro con numero). Luego F6.

## 2026-07-13 - Sesion (worktree formularios): F4 ARRANQUE - formulario como modulo (menu dinamico)

- **Hecho (F4, nucleo: promover a modulo con colocacion dinamica en el menu)**:
  - ESQUEMA: `FormDefinition` += `IsModule`, `ModuleMenuNodeId`, `ModuleIcon`, `ListColumnsJson`,
    `FilterFieldsJson`. Migracion dual `AddFormModule` (PG `20260713121447` + SQL Server), aditiva.
    Aplicada en local; **PENDIENTE en prod** (doc 04).
  - SERVICIO: `FormDefinitionService.SetModuleAsync` reusa `IMenuConfigService.CreateNodeAsync`
    (Kind=Item, Route=/m/{code}) para crear el nodo de menu EN LA VISTA + GRUPO que elige el usuario;
    al retirar borra el nodo. `FormResponseService.ListRecordsAsync` para la bandeja.
  - UI: panel "Propiedades del formulario" += toggle "Es un modulo" + selector de **vista** + selector
    de **grupo del menu (donde aparece)** (arbol aplanado a Section/Subgroup) + icono. Pagina bandeja
    `FormModule.razor` en `/m/{code}` con el listado de registros enviados.
- **Verificado en navegador (`ecorex_forms`)**: promover FRM-021 -> elegir grupo "Automatizacion" ->
  nodo de menu creado (Item, /m/FRM-021) bajo ese grupo, is_module=true; el modulo aparece en el menu y
  `/m/FRM-021` muestra la bandeja con el registro FRM-021-000001 (Confirmado). Encadena F3->F4.
  Solution verde; 360/360 tests.
- **Bandeja consultable (mismo dia)**: `FormModule.razor` += KPIs (registros / confirmados / anulados /
  este mes con % crecimiento vs mes anterior) + filtros (estado + busqueda por numero/referencia) +
  export CSV (data-URI). VERIFICADO en navegador: 3 reg (2 conf, 1 anul, este mes +100%); filtro
  Anulados -> 1 fila; busqueda "002" -> FRM-021-000002.
- **Bandeja EN VIVO (SignalR, mismo dia)**: `IFormRecordBroadcaster` (Application) + impl
  `SignalRFormRecordBroadcaster` (reusa `TaskHub` y su grupo por tenant, evento "FormRecord");
  `FormResponseService` emite tras confirmar un registro. `FormModule.razor` se suscribe (HubConnection
  server-side, patron de ActivityBoardsIndex) y recarga. VERIFICADO en navegador con DOS pestanas: enviar
  en el constructor -> la bandeja pasa de 4 a 5 SOLA (FRM-021-000011 arriba, sin recargar).
  OJO fix: el connect NO puede ir gated en `_def` dentro de OnAfterRenderAsync(firstRender) porque
  OnParametersSetAsync puede seguir cargando; el handler chequea `_def`.
- **Siguiente (resto de F4)**: vista aplanada para BI, policies `Form.{code}.*` (hoy [Authorize] +
  visibilidad del nodo), config de columnas/filtros de la bandeja en el designer, export a Excel (hoy
  CSV). F4 ya cubre lo esencial (modulo + menu dinamico + bandeja KPIs/filtros/export/en vivo). Luego F5/F6.
- **Decision (doc 03 B)**: "convertir en modulo" es opcional del formulario; el usuario elige la
  ubicacion en el menu (vista + grupo), no es fija.

## 2026-07-13 - Sesion (worktree formularios): F3 - logica confirmar/anular + identidad

- **Hecho (F3, logica sobre el esquema)**:
  - `FormResponseService.SaveAsync`: confirmar = enviar. Al enviar un form transaccional se asigna
    identidad ANTES de la transaccion (patron `ISequenceService`: EnsureSequence+Next fuera de la tx),
    RecordStatus=Confirmed, TransactionDate. Idempotente (no reasigna si ya Confirmed).
    - Modo Sequence: consume `TenantSequence` (code corto "F"+8hex del id porque `code` es varchar(10);
      OJO bug cazado: `FORM:{guid}` de 41 chars hacia fallar el INSERT y `EnsureSequenceAsync` se lo
      tragaba -> "contencion excesiva"). Numero legible: prefijo = codigo del form (FRM-021-000001).
    - Modo NaturalKey: numero = valor del campo `IdentitySourceFieldCode`; unicidad por indice unico
      filtrado (DbUpdateException -> error de validacion "clave duplicada").
  - `VoidAsync`: anula un registro Confirmed (Voided + motivo + auditoria; no libera el numero).
  - DTO `FormResponseDto` += RecordNumber/RecordStatus/TransactionDate; renderer muestra chip verde con
    el numero y chip "Anulado".
- **Verificado (navegador, `ecorex_forms`)**: FRM-021 transaccional (Sequence) -> Enviar -> registro
  FRM-021-000001, status=Submitted, rec_status=Confirmed, tx_date fijada, secuencia consumida (next=2).
  Build verde; 360/360 tests Application.
- **UI de F3 (mismo dia)**: panel "Propiedades del formulario" en el designer (boton Propiedades ->
  modal con toggle Es transaccional + selector de identidad Ninguna/Consecutivo/Clave natural +
  selector de campo clave) via `IFormDefinitionService.SetTransactionalAsync`; `FormDefinitionDetailDto`
  += IsTransactional/IdentityMode/IdentitySourceFieldCode. Boton "Anular" (con motivo) en el renderer
  cuando el registro esta Confirmed -> `IFormResponseService.VoidAsync`. VERIFICADO en navegador:
  el panel lee (transaccional=on, Sequence) y escribe (cambio a NaturalKey campo=nit persistido).
- **Con esto F3 queda funcional end-to-end** (esquema + logica + UI de config + anular). Pendiente fino:
  cierre por evento (firma) doc 03 B; indices de dimension para BI; test unit/integracion de confirmar.
  Luego F4 (formulario-modulo).

## 2026-07-12 - Sesion (worktree formularios): F3 ARRANQUE - esquema transaccional

- **Contexto**: se confirmo que los agregados de GridDetail (Sum/Count/Avg/Min/Max) YA funcionan todos
  (demo en navegador: Count->2, Avg->1250, Sum->6000); falta solo el selector en el designer.
- **Hecho (F3, esquema / checkpoint)**: la respuesta confirmada se vuelve un REGISTRO (doc 01 D2/D3).
  - ENUMS: `FormIdentityMode` (None|NaturalKey|Sequence), `FormRecordStatus` (Draft|Confirmed|Voided),
    en `ConfigureConventions`.
  - `FormDefinition` += `IsTransactional`, `IdentityMode`, `IdentitySourceFieldCode`,
    `UniqueKeyFieldsJson`, `SequenceId` (ref logica a `TenantSequence`).
  - `FormResponse` += `RecordNumber`, `RecordStatus`, `TransactionDate`, `VoidedAt`,
    `VoidedByTenantUserId`, `VoidReason`. Indice unico filtrado (tenant, definition, record_number).
  - MIGRACION DUAL `AddFormTransactional` (PG `20260712213148` + SQL Server), aditiva. Aplicada en
    local `ecorex_forms`; **PENDIENTE en prod** (registro en doc 04).
  - Build verde; 360/360 tests Application.
- **DECISION a validar**: se agrego `record_status` (ciclo transaccional) APARTE del `status` existente
  (Draft/Submitted, ciclo de envio), para no tocar el flujo actual. Confirmar con el usuario.
- **Siguiente (grueso de F3)**: logica de confirmar (consumir `ISequenceService` en modo Sequence /
  validar unicidad en NaturalKey, en transaccion; anular no libera el numero; idempotente por
  FormResponse.Id) en `FormResponseService`; DTOs; panel "Propiedades del formulario" (IsTransactional +
  identidad) en el designer; boton confirmar/anular en el renderer. Cierre por evento (ej. firma) segun
  doc 03 B.

## 2026-07-12 - Sesion (worktree formularios): F2 GridDetail - totales de columna + roll-up

- **Hecho (cierra F2)**: totales/calculo en tablas GridDetail.
  - `FormGridCalculator` (Application/Forms/Calc) COMPARTIDO renderer+servidor: parsea columnas con
    claves opcionales `calc`/`agg`/`rollup` (columnas viejas [{id,label}] siguen valiendo), evalua la
    formula por fila (reusa `FormExpressionEvaluator`), agrega la columna (Sum/Count/Avg/Min/Max) y
    devuelve el roll-up al campo del encabezado. 9 unit tests.
  - RENDERER: columna calculada por fila (solo lectura), fila de totales (`<tfoot>`) y roll-up a un
    campo del encabezado; recomputo ante cualquier cambio de celda (`RecomputeGrids`).
  - SERVIDOR: `FormResponseService.SaveAsync` recomputa las tablas antes del calculo escalar (el
    roll-up alimenta calcs del encabezado); el cliente no es fuente de verdad.
- **Verificado (navegador, `ecorex_forms`)**: tabla Lineas con Subtotal=`{cant}*{precio}` agg Sum rollup
  "Total general" -> filas 2x1500 y 3x1000 -> subtotales 3000/3000, fila de totales 6000, Total general=6000.
  Solution verde; 360/360 tests Application.
- **Siguiente**: designer para configurar columnas de GridDetail con formula/agg/rollup sin SQL (hoy la
  config de columna avanzada se hace por OptionsJson; el editor de columnas del designer es basico label).
  Luego F3 (transaccionalidad: consecutivo/estado/cierre).

## 2026-07-12 - Sesion (worktree formularios): Formularios avanzados OLA F2 (parcial) - Campo calculado

- **Agentes**: Claude (worktree `funny-bell-3f8562`). Mismo protocolo de esquema que F1 (ver doc 03 s.D / doc 04 del vault).
- **Hecho (F2, primer incremento: campos calculados escalares)**:
  - DOMINIO: `FormQuestion` += `CalcExpression (string?)`, `Aggregate (FormAggregate)`; enum `FormAggregate`
    (None|Sum|Count|Avg|Min|Max) registrado en `ConfigureConventions`.
  - MIGRACION DUAL `AddFormCalcFields` (PG `20260712202108` + SQL Server), aditiva. Aplicada en local
    `ecorex_forms`; **PENDIENTE en prod** (la aplica la sesion principal, registro en doc 04).
  - EVALUADOR: `FormExpressionEvaluator` (Application/Forms/Calc) - sandbox TIPADO con allow-list:
    aritmetica + parentesis + menos unario + refs `{codigo}`; sin codigo arbitrario ni reflexion
    (evita el RCE del legacy). Campo vacio = 0; expresion invalida = null. 16 unit tests verdes.
  - RENDERER: campo calculado = solo lectura, recomputado en cliente ante cualquier cambio
    (`RecomputeCalculatedFields`) con el MISMO evaluador; incluido en carga, SetValue, ToggleMulti y
    autollenado de lookup (encadena F1->F2).
  - SERVIDOR: `FormResponseService.SaveAsync` recomputa los calc en servidor y DESCARTA el valor del
    cliente (no confiable para montos; fuente de verdad = servidor).
  - DESIGNER: input "Formula (campo calculado)" en el tab Datos.
- **Verificado (navegador, `ecorex_forms`)**: campo Subtotal = `{cantidad} * {precio_item}` -> Cantidad=3,
  Precio=540000 -> Subtotal=1620000 en vivo, solo lectura. Solution build verde; 351/351 tests Application.
- **Siguiente (resto de F2)**: totales de columna en GridDetail (`Aggregate` -> fila de totales) + columna
  calculada por fila + roll-up al encabezado (doc 01 D5, doc 02 s4). Luego F3 (transaccionalidad).
- **Decisiones**: formulas modestas (aritmetica), "sin artilleria pesada" (doc 03 B). Evaluador compartido
  cliente/servidor. Valor del cliente para calc se ignora en el guardado.

## 2026-07-12 - Sesion (worktree formularios): Formularios avanzados OLA F1 - Lookups / autollenado

- **Agentes**: Claude (worktree `funny-bell-3f8562`, rama `claude/briefing-worktree-formularios-f50017`).
- **Contexto de trabajo (acordado con el usuario)**: se trabaja SOLO en formularios avanzados, en un
  worktree aparte, en paralelo con la sesion principal. El agente CODEA todo (incluida la migracion),
  la aplica en una BD LOCAL de trabajo (`ecorex_forms`, copia de dev) y deja el registro de cada cambio
  de esquema en el vault (doc 04) para que la sesion principal lo replique en PROD cuando pueda. La
  remota NO se toca desde aqui. Protocolo: vault doc 03 seccion D; registro de tablas: vault doc 04.
- **Hecho (F1 completa, spec vault Capa 4, doc 01 D4 + doc 02 s2)**:
  - DOMINIO: `FormQuestion` += 7 columnas (`SourceKind/SourceRef/DisplayField/ValueField/FilterJson/
    AutofillMapJson/Presentation`); enums nuevos `FormSourceKind` (Options|DataContainer|Tercero|Item)
    y `FormFieldPresentation` (Autocomplete|Dropdown|Modal), registrados en `ConfigureConventions`.
  - MIGRACION DUAL: `AddFormLookupFields` en PG (`Ecorex.Infrastructure`) y SQL Server
    (`Ecorex.Infrastructure.SqlServer`); ADITIVA (bajo riesgo). Validada en Postgres local efimero.
    **Reporte de campos entregado (doc 04). Estado: aplicada en local, PENDIENTE en prod (la aplica la
    sesion principal).**
  - SERVICIO: `IFormLookupService` + `IFormLookupSource` con 3 adaptadores (`TerceroLookupSource`,
    `ItemLookupSource`, `DataContainerLookupSource`) en `Application/Forms/Lookups`. Server-side,
    paginado, parametrizado; tenant por el filtro global; interfaz extensible (sumar fuente = registrar
    otro adaptador). `ResolveAsync` revalida el id elegido (existe + del tenant). Reusa
    `TerceroFieldService`/`ItemFieldService` (fichas dinamicas) y `IDataContainerService`.
  - UI: `DynamicFormRenderer` -> control lookup (autocompletar/lista/buscador); al elegir guarda el id y
    COPIA los campos de `AutofillMapJson` a los destinos; boton "Crear" deep-link al modulo si falta el
    dato. `FormDesigner` (tab Datos) -> bloque "Origen de datos" (Origen/Fuente/Presentacion/Mostrar/
    Filtro/mapa de autollenado), con metadata de campos (estandar + fichas) cargada por el servicio.
  - DTOs (`SaveFormQuestionRequest`/`FormQuestionDto`) y mapeo en `FormDefinitionService` extendidos;
    `ToRequest` del designer arrastra los campos de lookup (no se pierden al hacer patch).
- **Verificado (navegador, BD local `ecorex_forms`)**: campo Cliente (Directorio, Autocompletar) ->
  escribir "a" trae 5 terceros reales -> elegir "ANDINA S.A.S" autollena NIT=901.111.222 y Ciudad=Bogota.
  Designer muestra todo el bloque configurable (incluye fichas dinamicas del tenant). `dotnet build`
  solution verde; unit del dispatcher `FormLookupServiceTests` 4/4.
- **Siguiente**: (1) la sesion principal aplica `AddFormLookupFields` a prod (doc 04); (2) test de
  aislamiento cross-tenant del lookup en `Integration.Tests` (Testcontainers, dual) + round-trip
  guardar/leer; (3) probar en navegador los adaptadores Item y DataContainer; (4) revalidacion de
  servidor del id lookup en `FormResponseService.SaveAsync` (hoy la garantia es el filtro global +
  `ResolveAsync`). Luego OLA F2 (calculo/formulas).
- **Bloqueos**: el navegador integrado hace timeout en screenshots y su snapshot va con retraso con
  Blazor Server; se verifico manejando el DOM vivo con javascript_tool (los clicks/handlers SI corren).
- **Decisiones**: valor guardado = id de la entidad/fila; autollenado por COPIA (snapshot), no
  referencia (decision del usuario). `ValueField` fijo a "id" al elegir una fuente de datos.

## 2026-07-08 - Sesion: Editor bpmn-js (iconos) + deploy a prod + dev conectado a la BD de prod

**Agentes**: sesion principal + agente de fix de gateways (ADR-0037, ver entrada siguiente).

**Hecho**:
- **Editor de flujos (bpmn-js)**: la paleta y el context pad (herramientas sobre cada nodo) salian como
  cuadros en blanco porque el webfont `bpmn-icon-*` no viene con los assets vendoreados. Se reemplazaron
  por iconos SVG inline (data-URI, sin descargas): iconos de paleta mas marcados +
  `AcotadoContextPadProvider` que sobreescribe al provider nativo (anexar tarea/compuerta/fin, conectar,
  eliminar) + `injectStyle()`. Validado en Chrome (paleta 6 iconos, context pad 5). Commit `1c46c26`.
- **Fix de gateways (ADR-0037)**: ver entrada siguiente (agente aparte). Commit `a352de3`.
- **Deploy a produccion + dev conectado a la BD de prod** (decision del usuario):
  - Push de `main` local -> `fase-0/clon-backbone` (fast-forward `5829de6 -> d49d7d9`, luego `a418419`).
  - Redeploy del server `10.0.0.3` (`/opt/ecorex`, build-from-git): backup previo (`backup.sh`),
    `docker compose -f docker-compose.from-git.yml build --no-cache` + `up -d`. Prod migro `85 -> 88`
    (`AddMenuConfig`, `AddRoles`, `AddNodeAssignment` aplicadas al arrancar), login HTTP 200.
  - Dev local apuntando a la BD de prod via tunel SSH (`localhost:15433`), cadena en
    `appsettings.Development.local.json` (GITIGNORED). Guard `SkipDemoSeed` (Program.cs, `a418419`) para
    NO sembrar demo en prod. Validado en Chrome: login `admin@ecorex.local` (existe solo en prod) ->
    Dashboard Super Admin con datos de prod (1 empresa).
  - Doc de conexion para el equipo (onboarding): vault Obsidian ->
    `04. Notas para desarrollador/Conexion a la base de datos (dev y prod).md`.

**Siguiente**:
- (Opcional) redeploy de prod a `a418419` para dejar la imagen 1:1 con la rama (el guard no afecta a prod).
- Tunel SSH persistente (autossh / tarea programada) para que el dev no dependa de la sesion.
- Retomar validacion de EJECUCION de flujos en Chrome (pausada por el usuario).
- Compañero nuevo trabajara en rama `formularios` (ver doc de conexion).

**Bloqueos**: ninguno.

**Decisiones**:
- Iconos del editor por SVG inline (offline), no webfont.
- El dev local se conecta a la BD de PRODUCCION (`10.0.0.3`), no a un dev/staging aparte.
- Credenciales (BD/SSH) NUNCA en el repo publico: solo en `appsettings.*.local.json` gitignored y el
  `.env` del server (chmod 600).

---

## 2026-07-08 - Sesion: Compuertas exclusivas auto-resueltas en el motor (ADR-0037)

**Agentes**: agente de fix (runtime de flujos - GAP de gateways estancados).

**Pedido**: los `exclusiveGateway` se estancaban como paso current Pending y el caso no avanzaba.
Verificado en la BD dev: 25 instancias de COT-COM con "Cotizacion" Completed y el gateway "Aprobacion"
como paso ACTUAL sin resolver (0 gateways resueltos). Causa raiz: cuando "Cotizacion" tiene FORMULARIO,
`FormResponseService.SaveAsync` completaba el paso via `CompleteStepAsync` SIN approvalResult; el motor
dejaba el gateway Pending-current y la logica que lo completaba vivia SOLO en
`WorkflowInboxService.CompletePendingStepAsync` (que el camino de formulario no usa).

**Hecho** (SIN migracion: no se agrego ninguna columna):
- **Motor** (`WorkflowEngine`): un `exclusiveGateway` ya NO queda Pending-current. Al activarlo,
  `ActivateNodeAsync` lo marca `Completed` HEREDANDO el `ApprovalResult` del paso que lo activo; el bucle
  de `AdvanceAsync` lo procesa (IsReady) en la MISMA pasada y `ResolveOutgoing` enruta por
  ConditionExpression (o arista default). Sigue siendo fila de historial (auditoria, append-only). Sin
  condicion que case ni default -> Stuck (comportamiento previo). Tope de 50 intacto.
- **Rechazo**: `RejectStepAsync` ahora ATRAVIESA los gateways (nuevo `ResolveReactivableSources`, con
  visitados anti-ciclo) para reactivar el nodo humano real, no el gateway.
- **Inbox**: se ELIMINA de `CompletePendingStepAsync` la logica que completaba el gateway a mano; ahora
  solo completa el paso Task con la decision y el motor resuelve el gateway.
- **Camino de formulario**: `IFormResponseService.SaveAsync` acepta `approvalResult` opcional (lo propaga
  a `CompleteStepAsync`). `GetTaskStepFormsAsync` calcula `IsGatewayAhead` + `ApprovalOptions`
  (`WorkflowInboxProjection.ResolveGatewayAhead`), expuestos en `TaskStepFormDto`. `DynamicFormRenderer`
  recibe `ApprovalOptions`: muestra la decision (radio) junto al formulario, deshabilita "Enviar" hasta
  elegir y propaga la eleccion al enviar. Cableado en `MisPasos.razor` y `TaskDetailModal.razor`.
- **Bug de circuito destapado y corregido**: pasar `ApprovalOptions` re-disparaba `OnParametersSetAsync`
  del renderer durante su carga async -> dos operaciones concurrentes sobre el MISMO DbContext del circuito
  ("a second operation was started on this context") -> circuito caido y el formulario quedaba en "Cargando
  formulario". Se agrego un guard de reentrada (`_loadInProgress`) en `OnParametersSetAsync`.
- **Datos varados**: `DatabaseSeeder.ResolveStuckGatewaysAsync(engine)` (idempotente, Development) resuelve
  los gateways ya varados heredando la decision del paso previo (o default). Ademas
  `AlignDemoGatewayConditionsAsync`: el seed demo COT-COM traia condiciones en ingles
  (`approval == 'Approved'/'Rejected'`) que NUNCA casaban con las opciones en espanol (Aprobada/Rechazada);
  se corrige el XML del seed y se realinean idempotentemente las aristas ya sembradas. Encadenados en
  Program.cs con ambient del tenant demo.

**Tests**:
- Integracion DUAL (PG 5442 + SQL Server 1443), 38/38 verdes: `WorkflowEngineTests` (gateway approved->rama,
  rejected->reinicio, decision capturada en el paso previo; append-only con gateway Completed heredado;
  rechazo atraviesa gateway), `DynamicFormsTests` (NUEVO: form+gateway -> submit con decision Aprobada
  enruta a Facturacion y el gateway queda resuelto), `WorkflowInboxTests`.
- Unit 22/22 (`WorkflowConditionEvaluator`, `WorkflowInboxProjection`).
- E2E flujos verdes: `WorkflowFormTests` (form del paso + decision Aprobada -> el motor resuelve la compuerta
  y el paso vigente es Facturacion; ANTES estancaba en Gateway_Aprobacion), `WorkflowInboxTests`,
  `FlowsEditorTests`, `NodeAssignmentTests`.
- Verificado en vivo (5234): al diligenciar Cotizacion aparece la decision Aprobada/Rechazada junto al
  formulario, Enviar deshabilitado hasta elegir, y el envio enruta (no se estanca).

**Decisiones**: ADR-0037 (`docs/decisiones/0037-gateways-auto-resueltos.md`).

**Deudas**: el guard de reentrada del renderer es puntual; convendria una revision general de la
concurrencia DbContext/circuito del `DynamicFormRenderer`. La condicion de gateway sigue siendo un literal
simple (`approval == 'X'`) evaluado contra el Name de la arista; el RulesEngine tipado llegara en otra ola.
La reset del instance demo a Requerimiento fue manual en la BD dev (los E2E de bandeja son stateful).

---

## 2026-07-08 - Sesion: Runtime de flujos - bandeja "mis pasos" (ADR-0036, ola F2, final)

**Agentes**: agente de feature (runtime operativo de flujos - bandeja + atender).

**Pedido**: cerrar el objetivo de flujos operativos con la BANDEJA de "mis pasos pendientes",
ATENDER un paso (formulario del nodo o completar/aprobar) y AVANZAR el caso. El motor ya hacia
casi todo; esta ola es sobre todo la query + la UI + el cableado. Consume `INodeAssigneeResolver`
de la ola F1 (ADR-0035).

**Hecho** (SIN migracion: todo el modelo ya existia):
- **Servicio** `IWorkflowInboxService`/`WorkflowInboxService` (Application/Workflows, tenant-scoped,
  resultados tipados `WorkflowResult<T>`): `GetMyPendingStepsAsync(tenantUserId)` (pasos current+Pending
  de instancias Running que el usuario puede atender: asignado, o sin asignar y candidato del resolver;
  devuelve tarea/proceso/nodo, estado de asignacion, hasForm, isGatewayAhead + opciones, ciclo, fecha);
  `ClaimStepAsync` (modelo "cualquiera lo toma"); `ReassignStepAsync` (solo si el nodo AllowsAssignment,
  auditado); `CompletePendingStepAsync` (valida candidatura y delega en `IWorkflowEngine.CompleteStepAsync`).
  Registrado en DI.
- **Gateway adelante + opciones** (documentado): si una arista saliente del nodo apunta a un
  ExclusiveGateway, las opciones = los `Name` de las aristas salientes DEL gateway (Aprobada/Rechazada),
  que se pasan como `approvalResult` a CompleteStep (misma semantica que `ResolveOutgoing` del motor).
  Logica pura aislada en `WorkflowInboxProjection` (sin EF, patron `OrgAssigneeTree`).
- **UI**: `Components/Pages/MisPasos.razor` (+ `.css`), `@page "/mis-pasos"`, policy `MisPasos.Ver`
  (RequireClaim tenant_id), InteractiveServer, tokens ECOREX. Tarjetas con "Tomar" y panel "Atender"
  (DynamicFormRenderer si hasForm; botones Aprobada/Rechazada+comentario si gateway; "Completar" si no;
  "Reasignar" si el nodo lo permite). Empty state + boton "Actualizar". Item de menu "Mis pasos"
  (route `mis-pasos`, code 000637) en "Mis Procesos" (seed fresco vistas Completo+Simple + reconciliacion
  de demos ya sembrados).
- **Detalle de tarea**: `TaskDetailModal.razor` gana seccion "Flujo" que, si el usuario es candidato de
  un paso current de la tarea, ofrece Tomar/Completar/Aprobar reusando el servicio de bandeja (los pasos
  con formulario se siguen atendiendo por "Formularios del paso").
- **Seed** `EnsureWorkflowRuntimeDemoAsync` (idempotente, Development, encadenado en Program.cs con
  ambient del tenant demo tras la asignacion por nodo): crea una TAREA del ActivityType COT-COM via
  `ITaskItemService.CreateAsync` -> instancia Running con Requerimiento Pending sin reclamar; candidato
  = cargo Asesor Comercial (operator@). Al entrar como operator@ a /mis-pasos hay un paso listo.
- **Tests**: Application.Tests `WorkflowInboxProjectionTests` (CanAttend candidato/dueno + gateway-ahead
  y sus opciones, dedup/blanks); Integration.Tests `WorkflowInboxTests` DUAL PG+SQL (crear tarea -> paso
  en bandeja del candidato y NO de un extrano -> Claim -> CompletePendingStep avanza al siguiente cargo
  -> gateway Aprobada->Facturacion / Rechazada->reinicio ciclo 1; aislamiento cross-tenant); E2E
  `WorkflowInboxTests` (login operator@ -> /mis-pasos -> ve el paso demo -> Tomar -> Atender -> Completar
  -> desaparece).

**Gate**: `dotnet build Ecorex.sln` 0 errores; `dotnet format --verify-no-changes` limpio.

**Siguiente**: refresco SignalR de la bandeja (deuda declarada, no bloquea); selector de reasignacion
acotado a candidatos del nodo.

---

## 2026-07-07 - Sesion: Asignacion por nodo (dependencias/cargos, ADR-0035, ola F1)

**Agentes**: agente de feature (runtime de flujos - asignacion por nodo).

**Pedido**: definir QUIEN atiende cada nodo Task del flujo por DEPENDENCIAS/CARGOS del
organigrama (no por usuarios directos), decidido por el usuario (modelo `PERMISO_CARGO` del
legacy). Ola F1: modelo de dominio + resolver listo; la bandeja/atender es la ola F2.

**Hecho**:
- **Dominio**: enum `OrgUnitClassifier { Dependencia, Cargo, Funcionario }`; `OrgUnit.Classifier`
  (default Dependencia) + `OrgUnit.TenantUserId` (solo Funcionario). Entidad `WorkflowNodePolicy`
  (TenantEntity: WorkflowNodeId FK cascade, OrgUnitId FK NO ACTION, SortOrder; unico
  (WorkflowNodeId, OrgUnitId)). DbSet en IApplicationDbContext + EcorexDbContext + configs.
- **Migracion DUAL** `AddNodeAssignment` (PG 20260708010501 + SQL Server 20260708010542):
  columnas classifier (default 'Dependencia') + tenant_user_id en org_units; tabla
  workflow_node_policies. Aplicada y VERIFICADA en PG 5442 y SQL Server 1443 (esquema chequeado
  por psql/sqlcmd). Puramente aditiva.
- **Servicio + resolver** (Application, resultados tipados): `IOrgUnitService`/DTOs/`SaveOrgUnitRequest`
  extendidos con Classifier + TenantUserId + validacion de coherencia jerarquica (Cargo bajo
  Dependencia, Funcionario bajo Cargo con TenantUserId). `IWorkflowNodePolicyService`
  (List/Add/Remove + ListAssignableUnits; rechaza Funcionario y duplicados, tenant-scoped).
  `INodeAssigneeResolver.ResolveCandidatesAsync(nodeId)` -> TenantUserIds distintos (funcionarios
  descendientes + miembros + responsable), con la logica de arbol PURA en `OrgAssigneeTree`
  (testeable sin EF, tolera ciclos). Registrados en DI.
- **UI**: `FlowEditor.razor` acordeon "Asignar usuarios" REAL (reemplaza el placeholder): lista de
  dependencias/cargos con quitar, selector del arbol filtrado a Dependencia|Cargo + Asignar, y
  conteo "N funcionarios atenderan este paso"; mensaje si el nodo no admite asignacion. Vinculo por
  nodo permitido tambien en publicadas (como formulario/regla). `Dependencias.razor`: selector de
  Classifier + dropdown de usuario del tenant para Funcionario + badge de clasificador en el arbol.
  Bridge E2E `window.ecorexBpmnE2E.select` agregado.
- **Seed** `EnsureOrgAssignmentDemoAsync` (idempotente, Development, encadenado en Program.cs):
  Comercial->Asesor Comercial->Funcionario (operator/owner) y Finanzas->Aprobador->Funcionario
  (admin); policies sobre COT-COM (Task_Requerimiento->Asesor, Task_Cotizacion->Aprobador).
- **Tests (todos verdes)**: Application.Tests `OrgAssigneeTreeTests` 7/7 (cargo->funcionarios,
  dependencia->descendientes, miembros+responsable, vacio, distinct, ciclos); Integration.Tests
  `NodeAssignmentTests` 8/8 DUAL PG+SQL (persistencia+resolver releido, unicidad+rechazo
  Funcionario, aislamiento cross-tenant, cascada al borrar la definicion); E2E `NodeAssignmentTests`
  1/1 (crear borrador -> tarea -> permite asignacion -> asignar dependencia -> persiste). Suite
  Application.Tests completa 326/326.

**Gate**: `dotnet build Ecorex.sln` 0 errores; `dotnet format --verify-no-changes` limpio.

**Siguiente (ola F2)**: bandeja/atender que consume `INodeAssigneeResolver` (asignacion efectiva
del paso: elegir el usuario concreto de entre los candidatos). Reordenar policies en el editor.

**Decisiones**: ADR-0035 (clasificador Dependencia/Cargo/Funcionario, WorkflowNodePolicy solo
Cargo/Dependencia, resolver nodo->usuarios, editor panel real; asignacion efectiva y bandeja en F2).

---

## 2026-07-07 - Sesion: Editor de flujos migrado a bpmn-js (ADR-0034)

**Agentes**: agente de feature (migracion del editor BPMN del modulo 000291).

**Pedido**: reemplazar el canvas SVG propio del EDITOR de flujos (`/flujos`, ADR-0022) por
**bpmn-js** embebido via JS interop, con paleta ACOTADA. Solo el editor; sin tocar la semantica
del motor de ejecucion. Decisiones del usuario: bpmn-js vendored del legacy (self-hosted, sin
descargas), desviacion de fidelidad aprobada, palette acotado, parametrizacion en tablas por
BpmnElementId (no en extensionElements).

**Hecho**:
- **Assets vendoreados** a `Ecorex.SuperAdmin/wwwroot/lib/bpmnio/` desde el legacy GestionMovil:
  `bpmn-modeler.js` (bpmn-js **v8.8.2** UMD, `window.BpmnJS`), `bpmn.css`, `diagram-js.css` +
  `README.md` con nota de licencia MIT (bpmn.io). Cargados en `Components/App.razor` (link CSS +
  script). Sin CSP en SuperAdmin -> no hubo ajuste de CSP.
- **Interop** `wwwroot/js/ecorex-bpmn.js` (modulo ES on-demand): `init/exportXml/importXml/zoomFit/
  destroy`; callbacks `OnElementSelected` (element.click/selection.changed) y `OnGraphChanged`
  (commandStack.changed). **Palette ACOTADO** (PaletteProvider custom que sobreescribe
  `paletteProvider`): SOLO startEvent/endEvent/task/exclusiveGateway + connect/hand/lasso, con
  iconos SVG data-URI (no depende del webfont `bpmn-icon-*`, ausente en el legacy). Puente
  `window.ecorexBpmnE2E` solo para pruebas.
- **`Flujos.razor` / `FlowEditor.razor`**: se reemplazo SOLO la region del canvas SVG propio por
  `<div id="bpmn-canvas">`; se CONSERVARON el indice (KPIs, busqueda, tarjetas), el header
  (Propiedades/Importar/Exportar/Publicar/Guardar/Cerrar), el panel derecho (6 acordeones + "Saltar
  a otro flujo") y todos los modales. El panel opera sobre el ULTIMO grafo guardado y resuelve la
  seleccion por `BpmnElementId`. Export/Import pasaron de JSON a **XML BPMN**.
- **Guardado** (`IWorkflowDesignService.SaveBpmnAsync`): exportar XML de bpmn-js -> `EnsureDraft` +
  **resync in-place** de nodos/aristas/layout por BpmnElementId (conserva config y vinculos de los
  nodos que sobreviven, agrega nuevos, elimina los que desaparecen). Guarda el XML tal cual
  (portabilidad). Publicadas siguen inmutables (derivan borrador). Se agrego `GetBpmnXmlAsync` y
  `ImportBpmnAsync`. `BpmnXmlWriter` deprecado en el camino de edicion pero CONSERVADO (seeder,
  CreateDraft, EnsureDraft, ImportJson) con nota.
- **ADR-0034** creado (reemplaza la Decision #1 de ADR-0022).

**Tests / gate**: `dotnet build` 0 errores; `dotnet format --verify-no-changes` limpio. Unit
workflow 31/31; integracion workflow (dual PG+SQLServer) **38/38** (incluye 4 nuevos de SaveBpmn:
resync in-place preservando parametrizacion + derivar borrador desde publicada); E2E completo
**29/29** (escenario del editor adaptado a bpmn-js: agregar+conectar via API del modeler,
determinista, luego Guardar/reabrir/verificar por elementRegistry). Motor de ejecucion sin cambios.

**Deudas**: bpmn-js 8.8.2 (linea vigente 17+, deuda de actualizacion); el canvas no conmuta a modo
oscuro (fondo claro fijo); viewer de ejecucion pendiente (otra ola); "Saltar a otro flujo" sigue
visual (call activity pendiente en el motor).

**Siguiente**: viewer de ejecucion sobre bpmn-js; evaluar actualizar bpmn-js a la linea vigente.

**No commit / no push** (segun instruccion de la sesion).

---

## 2026-07-07 - Sesion: Roles de permisos dinamicos con matriz Modulo x Accion (Ola B1)

**Agentes**: agente de feature (roles + matriz de permisos). Referencia de modelo: hermano Visal
(Rol + RolPermiso), adaptado al menu real de ECOREX.

**Pedido**: Ola B1 de roles dinamicos por-tenant con matriz (Modulo x Accion), inspirado en Visal.
SIN enforcement en backend (eso es Ola B2), pero dejando lista la resolucion de permisos efectivos.

**Hecho**:
- **Dominio** (`Ecorex.Domain`): `Rol : TenantEntity` (Name unico/tenant, Description?, IsActive,
  IsSystem) y `RolPermiso : TenantEntity` (RolId FK cascade, ModuleKey=Route del MenuNode,
  CanView/CanCreate/CanEdit/CanDelete, unico (RolId, ModuleKey)). `TenantUser.RolId` (Guid?,
  nullable, FK NO ACTION). DbSets en `IApplicationDbContext`/`EcorexDbContext` + config Fluent.
- **Migracion DUAL `AddRoles`** (PG `20260707191724`, SQL Server `20260707191908`): tablas `roles`,
  `rol_permisos` + columna `tenant_users.rol_id`. Aplicada y **verificada** en Postgres 5442 y SQL
  Server 1443 (tablas + columna + FKs cascade/NO ACTION).
- **Servicio** (`Ecorex.Application/Roles`, resultados tipados `RolResult<T>`): `IRolService`/
  `RolService` con `ListAsync` (UserCount), `GetAsync`, `SaveAsync` (unicidad; IsSystem no se
  renombra), `DeleteAsync` (bloquea IsSystem y rol con usuarios), `SavePermisosAsync` (borra e
  reinserta solo filas con flag, transaccional), `GetModuleCatalogAsync` (DERIVA el catalogo de los
  MenuNode Item Ready de la vista IsDefault; Grupo=Section ancestro; fallback minimo),
  `ResolveEffectivePermissionsAsync` (Owner/Admin -> AllowAll; con rol -> set; sin rol -> vacio),
  `AssignRoleToUserAsync`. Logica pura en `PermissionResolver`/`EffectivePermissions` (Can(mod,acc)).
  Auditado y registrado en DI.
- **Pagina** `Components/Pages/RolesPermisos.razor` (+.css) `/roles-permisos`, policy
  `RolesPermisos.Administrar` (Program.cs, paso 1 tenant_id): cabecera de modulo, panel de roles con
  badges (Sistema/Inactivo/UserCount), editor con la MATRIZ (filas=modulos por Grupo, columnas Ver/
  Crear/Editar/Eliminar) + utilidades marcar fila/columna/grupo, modal crear/editar, modal "Asignar
  usuarios". Columna "Rol de permisos" agregada tambien a `AdmUsuarios.razor`.
- **Menu**: item "Roles y permisos" -> `roles-permisos` (Ready, LegacyCode libre 000198) en la
  seccion "gen"; alta idempotente en el seed y en la reconciliacion (`EnsureMenuItemInSectionAsync`)
  para propagarlo a demos ya sembrados.
- **Seed** `EnsureRolesDemoAsync` (Development, idempotente): rol de sistema "Administrador" (31
  modulos, todo en true) + "Asesor limitado" (Ver general + Crear en tareas/inventario, sin Eliminar)
  asignado a `simple@sky-system.local`. Catalogo real = 31 modulos derivados del menu demo.
- **Tests**: unit `PermissionResolverTests` (Owner/Admin AllowAll; con rol resuelve; sin rol vacio;
  Can(mod,acc); FilterPersistable dedup/whitelist/blank) **8/8**; integracion dual `RolesTests` (PG +
  SQL Server): crear+guardar+releer, resave reemplaza, asignar rol + effective, Owner AllowAll,
  unicidad, cross-tenant, Delete bloquea IsSystem/con-usuarios, Delete ok cascada, catalogo del menu
  **20/20**; E2E `RolesPermisosTests` (owner crea rol -> marca permisos -> guarda -> asigna) **1/1**.

**Decisiones**: ADR-0032 (modelo Rol+RolPermiso; catalogo derivado del menu; `TenantRole` poder
organico vs `Rol` permisos finos; enforcement = Ola B2).

**Siguiente / deuda**: **Ola B2 = enforcement**: hacer cumplir el set por modulo en policies/
endpoints usando `ResolveEffectivePermissionsAsync`, poblar el claim `Permissions` del token
(`AuthService.BuildToken`) y derivar `RolesPermisos.Administrar` (y demas) a Owner/Admin.

**Gate**: `dotnet build Ecorex.sln` 0 errores; `dotnet format --verify-no-changes` limpio; unit
8/8 (+311 suite total) + integracion dual 20/20 + E2E 1/1 verdes. Migracion dual aplicada/verificada.

---

## 2026-07-07 - Sesion: Modulo Administracion de usuarios del tenant (000073)

**Agentes**: agente de feature (modulo de usuarios del tenant). Backend reusado del backbone.

**Pedido**: construir el modulo de usuarios del tenant (legacy 000073, hoy stub) reutilizando
el backend existente: pagina + policy + un par de metodos de servicio + enganche del menu. Sin
roles/permisos dinamicos (otra ola).

**Hecho**:
- **Servicio ampliado** (`ITenantUserService`/`TenantUserService`, tenant-scoped, auditado,
  patron transaccional de `InviteAsync`):
  - `ResetPasswordAsync(tenantUserId, newPassword, actorUserId)`: hashea PBKDF2, actualiza
    `PlatformUser.PasswordHash`, reactiva `Invited -> Active`, valida clave min 6
    (`ArgumentException`), audita SIN la clave en claro.
  - `UpdateProfileAsync(tenantUserId, displayName, actorUserId)`: edita `DisplayName`, audita.
  - `Map` ampliado para poblar `DisplayName` en el DTO. Asignacion de vista via el
    `IMenuConfigService.AssignUserToViewAsync` existente (no se duplica).
- **Pagina** `Components/Pages/AdmUsuarios.razor` (`/admin-usuarios`, policy
  `AdmUsuarios.Editar`, InteractiveServer, tokens ECOREX): tabla Usuario/Email/Rol/Estado/
  Vista de menu/Acciones + modales Nuevo (Invite; vacio->Invited, con clave->Active; vista
  opcional), Editar (DisplayName/Rol/Estado/Vista), Cambiar clave (Reset + confirmar + Generar)
  y toast. Actor desde `ITenantContext.UserId`.
- **Policy** `AdmUsuarios.Editar` en Program.cs (paso 1: `RequireClaim("tenant_id")`; paso 2
  Owner/Admin pendiente).
- **Menu**: seed del item 000073 -> ruta real `admin-usuarios` (Ready) y paso de
  **reconciliacion idempotente** (`ReconcileMenuNodesAsync`, tenant-scoped) que ajusta
  Route/State/Name de los nodos 000073/000194 cuando la vista ya existe (demo ya sembrado
  refleja las paginas reales sin recrear la vista). Verificado en el Postgres dev (5442).
- **Tests**: unit `TenantUserServiceTests` (Application.Tests, EF InMemory: reset hashea +
  activa Invited, valida clave corta/vacia, user no encontrado, update DisplayName, blank ->
  null) **6/6**. Integracion dual `TenantUserAdminTests` (PG + SQL Server: invite con/sin
  clave, changeRole, setStatus, resetPassword, assignMenuView, aislamiento cross-tenant)
  **12/12**. E2E `AdmUsuariosTests` (owner crea usuario Asesor con clave -> aparece; cambia a
  Supervisor -> se refleja) **1/1**.

**Sin migracion**: `TenantUser`/`PlatformUser` ya tenian todos los campos. Sin cambios de esquema.

**Decisiones**: ADR-0031 (reusa el backend; pagina + policy + reconciliacion de menu; roles
dinamicos en la siguiente ola).

**Siguiente / deuda**: roles/permisos dinamicos; invitacion por correo real + self-service de
clave; paso 2 de la policy (Owner/Admin via `tenant_role`); salvaguarda "no dejar el tenant
sin ultimo Owner".

**Gate**: `dotnet build Ecorex.sln` 0 errores; `dotnet format --verify-no-changes` limpio;
unit 6/6 + integracion dual 12/12 + E2E 1/1 verdes.

---

## 2026-07-07 - Sesion: Menu configurable por vista (perfil) - Olas 1 y 2

**Agentes**: agente de feature (menu data-driven + editor). Referencia visual: prototipo
Claude Design "Administrador de Menu" (concepto TRONOX SGDEA) servido en
http://localhost:5234/config-menu-proto.html, adaptado a los TOKENS de ECOREX.

**Pedido**: hacer el sidebar del workspace configurable por perfil de usuario (Ola 1) y
construir la pagina editora que administra las vistas y sus nodos (Ola 2), con fidelidad al
prototipo pero con la identidad ECOREX (no el teal de TRONOX).

**Hecho (Ola 1, commit `bdda279`)**:
- Modelo: `MenuView` (perfil, Name unico por tenant, IsDefault, SortOrder) y `MenuNode`
  (adjacency-list, Kind QuickLink/Section/Subgroup/Item, IconKey, LegacyCode, Route,
  Description, HelpText, State Ready/InDevelopment/Disabled, IsVisible, SortOrder;
  self-ref NO ACTION, FK a la vista en cascada). `TenantUser.MenuViewId` (Guid? NO ACTION).
- `MenuTreeBuilder` (pura), `IMenuConfigService` (GetMenuForTenantUser, ListViews, CreateView,
  CloneView), `NavMenu.razor` data-driven identico al prototipo. Seed Completo(67)/Simple(10)
  + usuarios completo@/simple@sky-system.local.

**Hecho (Ola 2, esta sesion)**:
- **Servicio ampliado** (`MenuConfigService`, tenant-scoped, transaccional, resultados
  tipados): `UpdateViewAsync`, `DeleteViewAsync` (cascade + desasigna usuarios; prohibe borrar
  la IsDefault), `SetDefaultViewAsync`, `GetViewTreeAsync` (arbol completo incl. invisibles);
  nodos `CreateNodeAsync`/`UpdateNodeAsync`/`ToggleNodeVisibilityAsync`/`SetNodeStateAsync`/
  `MoveNodeAsync` (valida ciclos y coherencia de Kind)/`DeleteNodeAsync` (cascade a
  descendientes); `AssignUserToViewAsync`/`ListTenantUsersWithViewAsync`;
  `ExportViewAsync`/`ImportViewAsync` (System.Text.Json portable). Reglas de anidamiento
  extraidas a `MenuNodeKindRules` (pura, testeable sin BD).
- **Iconos compartidos**: diccionario `IconKey->SVG` extraido de NavMenu a
  `Components/Shared/MenuIcons.razor` (fuente unica: sidebar + arbol/selector del editor +
  vista previa). NavMenu ahora consume `MenuIcons.Render`.
- **Pagina** `Components/Pages/ConfiguracionMenu.razor` (`/configuracion-menu`,
  policy `ConfiguracionMenu.Administrar`): index de vistas (tarjetas con badges/contadores y
  Editar/Duplicar/Predeterminada/Eliminar), editor (KPIs, tabs Estructura/Vista previa, arbol
  con acciones por fila, toolbar buscar/expandir/contraer/+Seccion, panel de propiedades con
  selector de iconos grid, Exportar/Restablecer/Guardar) y modal de asignacion de usuarios.
  CSS scoped 100% con tokens ECOREX (--surface, --ink, --line, --brand, --ok/warn/danger,
  --t-*, --rad, --sh-*), conmuta claro/oscuro por html.dark. JS helper `js/menu-config.js`
  para descargar el JSON exportado.
- **Policy** `ConfiguracionMenu.Administrar` en Program.cs (paso 1: RequireClaim tenant_id;
  comentario del paso 2: restringir a Owner/Admin).
- **Seed**: item "Administrador de Menu" en "Sistema . General" reutilizando el code **000194**
  (antes "Roles y permisos", stub) apuntandolo a `/configuracion-menu` (rename, no alta; los
  conteos del seed no cambian).
- **Tests**: unit (Application.Tests `MenuConfigRulesTests`: reglas de Kind + round-trip JSON
  export/import); integracion DUAL (`MenuConfigEditorTests`: CRUD nodos, move-reorder,
  cascade delete, SetDefault, no-borrar-default, no-ciclo, export->import, assign refleja en
  GetMenuForTenantUser, aislamiento) PG+SQL; E2E (`MenuEditorTests`: owner crea vista, agrega
  seccion+item, guarda, asigna a usuario). MenuProfileTests de Ola 1 intactos.
- **ADR** `docs/decisiones/0030-menu-configurable.md` (cubre ambas olas).

**Migracion**: NINGUNA. El modelo de Ola 1 cubrio toda la Ola 2.

**Deudas**: (1) drag-and-drop real (hoy botones subir/bajar = MoveNode reorder); (2) paso 2 de
la policy (restringir a Owner/Admin via tenant_role); (3) "Guardar" del editor es
confirmacion/recarga porque cada accion persiste al vuelo.

**Siguiente**: conectar el paso 2 de las policies con el Module Registry (000109) + rol del
TenantUser; drag-and-drop del arbol.

---

## 2026-07-05 - Sesion: Primer DEPLOY a produccion Linux (10.0.0.3) + fix de bootstrap

**Agentes**: coordinador en rol de deploy (no feature). Referencia: patron de deploy de
Visal (C:\DesarrolloIA\Visal\deploy\docker-prod), modo build-from-git.

**Pedido**: desplegar ECOREX en un Linux que ya corre otro proyecto (Visal), sin chocar,
tras sondear el server "sin hacer nada" primero.

**Hecho**:
- Directorio nuevo `deploy/docker-prod/` (build-from-git): `docker-compose.from-git.yml`
  (el server clona el repo publico rama fase-0/clon-backbone y construye
  apps/backend/Dockerfile.superadmin; ecorex-app + Postgres AISLADOS: proyecto
  ecorex-prod, contenedores ecorex-app/ecorex-postgres-prod, red ecorex-net, volumen
  PERSISTENTE ecorex-pgdata). `.env.example`, `README-linux.md`, `backup.sh`, `.gitignore`
  y `caddy/` (overlay TLS opcional). Todo ASCII, `docker compose config` valido.
- Sondeo read-only de 10.0.0.3 (ssh con llave existente id_ed25519_visal): Ubuntu, Docker
  29.5, 120 GB libres, 5.5 GB RAM sin swap. HALLAZGO: NO hay Caddy ni proxy en 80/443
  (contrario al supuesto); cada app va en puerto plano (visal 5380, bookstack 6875...).
  80/443 y 5480 libres. Decision del usuario: exponer ECOREX en 0.0.0.0:5480 HTTP plano.
- Deploy real: build en el server (imagen ecorex-superadmin:local), up -d, migraciones
  aplicadas, /login 200. Volumen persistente creado.
- FIX DE BOOTSTRAP (bloqueante, cazado en la validacion): en Production nunca se creaba el
  Super Admin (SeedAsync solo corre en Development; el camino de prod solo aseguraba tenant
  interno y *actualizaba* la clave, con `if superAdmin is null return`). Nuevo
  `DatabaseSeeder.EnsureSuperAdminAsync(password)` (crea admin@ecorex.local si falta,
  idempotente, clave de ECOREX_SEED_ADMIN_PASSWORD) llamado en el arranque de Production
  antes de asegurar tenant/clave. Redeploy verificado: admin creado + tenant interno
  "Plataforma ECOREX". Login accesible en http://10.0.0.3:5480/login.

**Estado de datos**: produccion arranca LIMPIA (env Production, sin seeder demo): solo
admin@ecorex.local + tenant interno. NO comparte datos con desarrollo (que si corre
SeedAsync + Ensure*DemoAsync con SKY SYSTEM y demas). Mismo esquema (100 tablas), datos
distintos.

**Siguiente**: seguridad de 5480 (hoy HTTP plano publico): firewall a red/VPN o activar el
Caddy incluido (80/443 libres) para TLS con dominio. Comando de update y backup.sh en
README-linux.md.

**Bloqueos**: ninguno (el de bootstrap quedo resuelto).

**Decisiones**: build-from-git (repo publico, sin registry); stack aislado con volumen
persistente propio; exponer en puerto plano 5480 (no habia proxy en el box); el fix de
bootstrap se hizo en codigo (no atajo por SQL) para que todo deploy limpio futuro sea
self-service. Commits a01a3b9 (deploy dir), 7b9aa11 (fix bootstrap) en fase-0/clon-backbone.

---

## 2026-07-05 - Sesion: Infraestructura IA (menu propio) + desacople del cierre (ADR-0028)

**Agentes**: agente #3 de 3 integraciones CUBOT encadenadas. Tarea de REORGANIZACION +
DESACOPLE (no feature nueva): quirurgica, sin romper comportamiento existente.

**Pedido**: (A) extraer la infraestructura de IA del grupo "CRM (heredado)" a un grupo propio
"Infraestructura IA"; (B) desacoplar el toolset de cierre del agente del dominio CRM/Lead con una
costura (interfaz + NoOp default + adaptador CRM), sin romper la creacion actual de leads.

**Hecho**:
- Menu (`NavMenu.razor`): nuevo grupo "Infraestructura IA" (data-acc `ia`, 5 items) con Agentes
  (`/agentes`, 000867, venia de Automatizacion), Lineas WhatsApp (`/lineas`), Conversaciones
  (`/conversaciones`), Bitacora del agente (`/bitacora-agente`), Plantillas WhatsApp
  (`/plantillas-whatsapp`) — las 4 ultimas venian de "CRM (heredado)". Rutas y codigos de modulo
  INTACTOS (solo movimiento de menu). "Automatizacion" 4->3 (se quita `agentes`); "CRM (heredado)"
  7->3 (queda Asesores, Automatizaciones, Lista negra; el grupo NO se elimina). Mapa `GroupRoutes`
  y contadores actualizados en consecuencia.
- Costura de cierre (`Ecorex.Application/Tenancy`): `IAgentLeadSink` con
  `CreateLeadAsync(AgentLeadRequest, actor, ct)` y DTO `AgentLeadRequest`/`AgentLeadResult` en el
  namespace IA (no referencian Lead). `NoOpAgentLeadSink` (default, no crea nada, no lanza) y
  `PipelineLeadSink` (adaptador CRM vivo, unico punto de acoplamiento con Lead/BusinessUnit).
  `PipelineToolset.crear_lead` ahora delega en la interfaz; mismo contrato del tool. DI: NoOp
  registrado como default, `PipelineLeadSink` como implementacion viva (ultimo gana).
- SIN migracion: cambio schema-free (reorg de menu + costura de interfaz). DAL dual intacto.
- Tests: unit nuevos `AgentLeadSinkTests` (3, verdes): NoOp no crea lead / adaptador crea lead y
  mapea unidad b2b / sin nombre = error tipado. Application.Tests total 279 verde. Integracion
  dual `PipelineLeadTests`+`FollowUpTaskTests`+`DashboardTests` (5) verde en PG 5442 + SQL 1443.

**Siguiente**: (opcional) exponer el `NoOpAgentLeadSink` como perfil de despliegue sin CRM.

---

## 2026-07-05 - Sesion: Modulo de Plantillas HSM de WhatsApp (ADR-0029)

**Agentes**: agente #2 de 3 integraciones CUBOT encadenadas. Referencia origen:
CUBOT.travels (`WhatsAppTemplate`, `PlantillasWhatsApp.razor`, migracion 20260628124032).
Convenciones copiadas del modulo de Inventarios (ADR-0027).

**Pedido**: portar el gestor de plantillas HSM de WhatsApp de CUBOT.travels a ECOREX como
modulo NUEVO, adaptado a las convenciones (multi-tenant, DAL dual, resultados tipados), con
Submit/SyncStatus como STUBS (sin integracion real con Meta).

**Hecho**:
- Dominio (Ecorex.Domain): `WhatsAppTemplate` (TenantEntity) + enums
  `WhatsAppTemplateCategory/HeaderType/Status`. FK `WhatsAppLineId` NO ACTION a `WhatsAppLine`
  (linea del CRM heredado). Unica por (TenantId, Name, Language). `VariablesJson` jsonb/nvarchar
  dual; `BodyText` text/nvarchar(max) dual; `IsActive` (soft-delete).
- DbContext: DbSet + config inline (3 conversiones enum->string, indice unico (Name,Language),
  FK Restrict). IApplicationDbContext expone el DbSet.
- Migracion dual `AddWhatsAppTemplates` (PG 20260705120605 + SQL Server 20260705120649)
  generada, APLICADA y VERIFICADA en los contenedores dev (tabla `whats_app_templates` existe en
  PG 5442 y SQL Server 1443).
- Servicios (Ecorex.Application/Tenancy, `WhatsAppTemplateResult<T>` con NotImplemented):
  `IWhatsAppTemplateService` (CRUD + SetActive + Submit STUB + SyncStatus no-op). Logica pura en
  `WhatsAppTemplateCalculations` (NormalizeName, ExtractTokens, ValidateSave, CanEdit/CanSubmit).
  Auditoria via IAuditWriter. Registrado en DI.
- UI (Ecorex.SuperAdmin): `/plantillas-whatsapp` (tabla + badges de estado + modal crear/editar
  + accion Someter + banner "envio al proveedor no implementado"). NavMenu: item en grupo "CRM
  (heredado)" junto a Lineas WhatsApp (conteo 6->7). Policy `PlantillasWhatsApp.Editar` (paso 1).
- Seeder: `EnsureWhatsAppTemplatesDemoAsync` (linea Cloud demo si falta + 3 plantillas SKY SYSTEM
  en Draft/Submitted/Approved). Idempotente, llamado desde Program.cs.
- Tests: unit `WhatsAppTemplateCalculationsTests`, integracion dual `WhatsAppTemplatesTests`
  (round-trip, unicidad (Name,Language), aislamiento cross-tenant, transicion Submit), E2E
  `WhatsAppTemplatesTests` (crear plantilla y verla en la tabla).

**Deudas**: (1) integracion real con la WhatsApp Cloud API de Meta (Submit/SyncStatus son
stubs, no hay llamada HTTP); (2) policy en "paso 1" (Module Registry pendiente); (3) headers de
imagen/documento/video modelados pero no soportados en el editor.

**Siguiente**: agente #3 de las integraciones CUBOT.

---

## 2026-07-05 - Sesion: Modulo de Inventarios con catalogos normalizados (ADR-0027)

**Agentes**: coordinador (Opus) + 4 subagentes de exploracion (DbContext/DAL dual, servicios/
resultados tipados, UI/NavMenu/policies/seeder, suites de test). Referencia origen:
CUBOT.nails Product/Sede (NO se porto belleza).

**Pedido**: portar el MODELO DE ITEMS del backbone a ECOREX con CATALOGOS NORMALIZADOS para el
grupo "Sistema - Inventarios" (Bodegas 000556, Marcas 000502, Grupo 000506, Subgrupos 000606,
Tipos 000498, Items 000066): modelo + migracion dual, servicios con resultados tipados, UI,
policy, seeder y validacion (unit + integracion dual + E2E), arranque real y ADR.

**Hecho**:
- Dominio (Ecorex.Domain): `Warehouse/Brand/ItemGroup/ItemSubgroup/ItemType` (catalogos,
  interfaz comun `ICatalogEntity`), `Item` (+FieldValuesJson jsonb dual, Specifications text
  dual, Price 14,2, FKs de catalogo NO ACTION, SKU unico filtrado por tenant), `ItemImage`
  (cascade, Url 500), `ItemStock` (cascade al item, NO ACTION a bodega, unico ItemId+WarehouseId).
- DbContext: 8 DbSets + configuracion inline (indices unicos por (TenantId,Name)/(ItemId,
  WarehouseId), filtro por bodega). HasQueryFilter multi-tenant automatico por reflexion (ya
  existente) cubre las 8 entidades. IApplicationDbContext expone los 8 DbSets.
- Migracion dual `AddInventory` (PG 20260705110130 + SQL Server 20260705110220) generada,
  aplicada y VERIFICADA en los contenedores dev (PG 5442 y SQL Server 1443, 8 tablas cada uno).
  jsonb/text -> nvarchar(max) en SQL Server; FKs Restrict = NO ACTION en ambos motores.
- Servicios (Ecorex.Application/Inventory, `InventoryResult<T>`): `IInventoryCatalogService`
  (CRUD bodegas + catalogos genericos por `CatalogKind`, guards de archivado, subgrupo valida
  grupo) e `IItemService` (SKU unico, consecutivo "ITM" via ISequenceService, stock por bodega
  recreado en transaccion, imagenes por URL, list con filtros+paginado, detail con TotalStock/
  AvailableAt). Calculos puros en `InventoryCalculations`. Registrados en DI.
- UI (Ecorex.SuperAdmin): `/inventario-items` (grid + filtros + modal + activar/archivar),
  `CatalogManager.razor` generico + 4 paginas thin (marcas/grupos/subgrupos/tipos) +
  `/inventario-bodegas` aparte. NavMenu: los 6 items apuntan a rutas reales, 000066 movido de
  "Oferta - Catalogo" (grupo retirado) a "Sistema - Inventarios"; retiradas del stub
  `Modulo.razor`. Policy `Inventario.Ver` en Program.cs.
- Seeder: `EnsureInventoryDemoAsync` (2 bodegas, 3 marcas, 2 grupos x2 subgrupos, 3 tipos, 8
  items con stock repartido incl. ceros, imagenes placeholder; avanza el consecutivo ITM),
  llamado desde Program.cs. Idempotente, solo Development.

**Verificacion real** (app arrancada contra PG 5442 en puerto 5260):
- Build 0 errores / 0 advertencias en Ecorex.sln. `dotnet format --verify-no-changes` limpio.
- ASCII-only: 0 bytes no-ASCII en todos los archivos nuevos.
- Unit (Ecorex.Application.Tests): 9/9 verdes (total stock, disponibles, IsAvailableAt,
  validacion de nombre).
- Integracion dual (Ecorex.Integration.Tests): 12/12 verdes (6 casos x PG + SQL Server):
  round-trip item con stock por bodega, SKU unico por tenant, consecutivo ITM, subgrupo valida
  grupo, guards de archivado (grupo con subgrupos / bodega con stock), aislamiento cross-tenant
  items+catalogos (mismo SKU en 2 tenants no colisiona).
- E2E (Ecorex.E2E.Tests): 1/1 verde (crear bodega + marca + item con stock y verlo en el grid
  filtrando por bodega).
- Navegador (PG 5442, 5260): /inventario-items CLARO (grid con miniaturas, SKUs ITM..., stock
  total y chips por bodega, filtros) y /inventario-bodegas OSCURO (badges "Activa", tokens
  conmutan) + modal "Nuevo item" en oscuro OK. Procesos detenidos al final.

**Siguiente**: paso 2 de las policies (derivar `Inventario.Ver` del Module Registry), como el
resto de modulos. Subida real de imagenes (hoy por URL). Movimientos/kardex de stock si el
requerimiento lo pide.

**Bloqueos**: ninguno.

**Decisiones**: ADR-0027 (docs/decisiones/0027-inventario.md). FKs de catalogo NO ACTION para
evitar rutas multiples de cascada en SQL Server. Catalogos simples via `ICatalogEntity` +
`CatalogManager` generico para no duplicar CRUD. El seeder avanza el consecutivo ITM para que
los SKUs generados desde la UI no colisionen con los demo.

---

## 2026-07-05 - Sesion: Login "ventana al producto" (mockup del tablero kanban en el aside)

**Agentes**: coordinador (Opus). Lectura de AuthShell.razor, ActivityBoardDetail.razor + AbUi.cs
(paleta 1:1 del prototipo work), .auth-*/.ab-* de app.css y las 4 paginas de auth.

**Pedido**: un login MAS ACORDE AL TABLERO. El aside de marca (fondo gris + 3 bullets) debia
mostrar una VENTANA AL PRODUCTO: un mockup estatico y elegante del TABLERO KANBAN del workspace,
renderizado con los tokens exactos, para que el login "sepa" a lo que se entra.

**Decision de diseno**:
- AuthShell.razor gana un parametro `ShowBoardMock` (default false). El aside tiene DOS modos:
  * ShowBoardMock=true -> SOLO Login: composite "ventana al producto" (identidad arriba +
    eyebrow "Tu tablero de trabajo" + linea de valor "Tareas, flujos, formularios y reglas -
    configurables sin codigo" + tarjeta ventana con topbar falsa [3 dots + "Comercial -
    Requerimiento Infraestructura" + badge "En progreso"] + 4 columnas kanban [Por hacer /
    En progreso / En revision / Completado] con 1-2 tarjetas: titulo corto, barra "Progreso
    N/M" con color POR COLUMNA [t-blue/danger/t-amber/ok EXACTOS de AbUi.ColumnProgress],
    avatares solapados con AVPAL del prototipo, chip de fecha). La ventana asoma RECORTADA:
    overflow hidden, rotate(-1.1deg) + translateX, fade en borde inferior/derecho (sh-lg) para
    profundidad estilo hero SaaS. Debajo, 3 mini bullets con iconos.
  * ShowBoardMock=false -> Recuperar/Restablecer/Activar: se dejan como estaban (aside sobrio,
    headline + subtext, ya traian ShowBullets=false). El mockup distrae en flujos utilitarios,
    asi que NO aparece ahi. Documentado en el comentario de cabecera de AuthShell.razor.
- Login.razor pasa `ShowBoardMock="true"`. La tarjeta del formulario NO cambia (ids
  #login-email/#login-password, script mostrar/ocultar, submit .auth-submit, links intactos).
- Cero morados saturados: aside en --surface-2, columnas --surface-3, tarjetas --surface,
  acentos --brand-soft/--t-amber-bg. Todo con tokens -> conmuta solo con html.dark.
- RESPONSIVE: el breakpoint del aside se subio de 768px a 900px (el mockup pide ancho); a
  <=900px el aside se OCULTA y queda la tarjeta centrada con la marca arriba (sin cambios de
  comportamiento del login movil).

**Hecho**:
- Editado `Components/Shared/AuthShell.razor`: parametro ShowBoardMock + rama del composite del
  mockup (HTML/CSS estatico, aria-hidden, sin datos reales ni backend) manteniendo la rama
  sobria (headline/subtext/bullets) para el resto.
- Editado `Components/Pages/Login.razor`: ShowBoardMock="true".
- Editado `wwwroot/app.css`: bloque `.auth-mock-*` (window/topbar/board/col/card/bar/avs/due/
  points) con la paleta exacta; media query 768px -> 900px.
- Anadida config `superadmin-5256` a `.claude/launch.json` (PG 5442, puerto 5256) para verificar.

**Verificacion real** (app arrancada contra PG 5442 en puerto 5256):
- build 0 errores / 0 advertencias; `dotnet format --verify-no-changes` limpio en los .razor.
- ASCII-only: 0 bytes no-ASCII en los 3 archivos tocados (los 5 preexistentes de app.css estan
  fuera del alcance auth y no se tocaron).
- /login CLARO: aside con mockup legible, 4 columnas, barras blue/rose/amber/green verificadas
  por computed style (rgb 37,99,235 / 225,29,72 / 199,122,6 / 22,163,74 = tokens exactos).
- /login OSCURO (localStorage['ecorex-theme']='dark'): conmuta por html.dark; aside surface-2
  #1C1C1F, tarjetas surface #161618; barras vivas; formulario oscuro OK.
- /login MOVIL 380px: aside OCULTO, marca centrada arriba, formulario intacto y centrado.
- /recuperar: aside sobrio sin mockup (confirmada la decision).
- E2E COMPLETA: 19/19 verde, 0 fallos, 0 omitidos (ECOREX_E2E_BASEURL=http://localhost:5256).
  El test de login pasa -> selectores del formulario intactos, aterriza en /inicio.
- Capturas en scratchpad: login-light-desktop.png, login-dark-desktop.png, login-mobile-380.png,
  recuperar-light-desktop.png. Procesos detenidos (preview stop + puerto 5256 libre).

**Siguiente**: (opcional) exponer el titular/copy del mockup como campos de branding editables.

**Bloqueos**: ninguno.

---

## 2026-07-05 - Sesion: Modulo ADMINISTRACION DE EMPRESAS / ficha de tenant (000072, ADR-0026)

**Agentes**: coordinador + 1 subagente explorador de la solucion (mapa de Tenant/servicios/
NavMenu/policies/seeder). Lectura de las 3 fuentes (proto_adm_empresas.html, spec Capa 6 de
origen, spec Capa 1 con los 9 errores).

**Decision de area**: la ficha 000072 es GOBIERNO multi-tenant -> AREA PlatformAdmin (junto
a /tenants y /plans), policy nueva `AdmEmpresas.Ver` = RequireClaim("platform_role"). El item
000072 del NavMenu se MOVIO del menu del tenant (grupo "Sistema - General", contador 8->7,
policy TenantMember erronea) al bloque SUPER ADMIN SAAS como "Ficha de empresa 000072".

**Hecho**:
- Pagina real `/admin/empresas` (AdmEmpresas.razor + .razor.css) que REEMPLAZA el stub 000072.
  Estructura del proto proto_adm_empresas.html con TOKENS del workspace (ADR-0023): topbar
  14x24 + MOD 000072, layout grid 300px/1fr max 1440, sidebar sticky selector de empresa con
  dot de estado, header-card r10 avatar gradiente + plan-badge + estado, KPIs 5 cols (usuarios
  y estado REALES; modulos/actividades/reglas con tag "Pendiente"), secciones colapsables
  (details/summary nativo, chevron) numeradas 01/08 REALES + 02-10/C1 PLACEHOLDER.
- Campos REALES editables mapeados a `Tenant`: razon social (LegalName), nombre comercial
  (Name), NIT (TaxId), pais, ciudad, direccion, telefono, email de contacto, estado (via
  ChangeStatusAsync, maquina de estados existente, auditado). Usuarios del tenant (TenantUser)
  en tabla SOLO LECTURA (email/rol/estado).
- Backend aditivo SIN duplicar CRUD: se REUTILIZA ITenantAdminService. UpdateProfileAsync
  extendido con City/Address/Phone/Email; nuevo ListUsersAsync(tenantId) cross-tenant ACOTADO
  (IgnoreQueryFilters + Where TenantId, unico cross-tenant, solo operador por policy). DTOs
  TenantDetail/UpdateTenantProfileRequest extendidos + TenantUserListItem nuevo.
- Modelo: 4 columnas nuevas en `Tenant` (City, Address, Phone, Email, nullable). UNA migracion
  dual `AddTenantProfile` (Ecorex.Infrastructure 20260705044204 EcorexDbContext + Ecorex.
  Infrastructure.SqlServer 20260705044246 SqlServerEcorexDbContext), puramente aditiva (4
  AddColumn nullable, sin drops), APLICADA y verificada en PG 5442 (\d tenants) y MSSQL 1443
  (sys.columns). Config EF en el OnModelCreating compartido (SqlServer hereda EcorexDbContext).
- Seeder: campos de contacto en el tenant demo SKY SYSTEM (bases nuevas) + EnsureTenantProfile
  DemoAsync idempotente que rellena City/Address/Phone/Email si estan vacios (bases previas a
  la migracion). Encadenado en Program.cs tras EnsureDemoTemplateAssetsAsync.
- 9 secciones PLACEHOLDER visibles-deshabilitadas con tooltip/explicacion "Pendiente": modulos,
  actividades, cargar datos, copiar formularios, datos externos, reglas, configuraciones,
  integraciones, contador/revisor fiscal. Los flujos SQL peligrosos del legacy (copiar tablas
  via sys.tables+blacklist, copiar formularios con 5+ INSERT y db3dev, datos externos con
  cadena arbitraria) NO se reconstruyen: son parte de los 9 errores (ver ADR-0026).

**Validacion (probado de verdad)**:
- Build Ecorex.sln 0 errores; `dotnet format --verify-no-changes` limpio; archivos nuevos ASCII.
- Unit: Domain 35/35, Application 247/247 verdes (sin regresiones).
- Integracion dual +6 (3 tests x 2 motores PG+SQL Server via Testcontainers) TenantProfileTests
  verdes: UpdateProfile persiste City/Address/Phone/Email y vuelven en el detalle + Normalize
  vacia a null; ChangeStatus via maquina de estados reflejado en la ficha; ListUsers ACOTADO
  al tenant sin fuga entre empresas (A ve solo lo suyo, B lo suyo, orden por email). Test de
  aislamiento cross-tenant existente 6/6 sigue verde tras el cambio de modelo.
- E2E Playwright COMPLETA verde 19/19 (era 18, +1 AdmEmpresasTests): login operador de
  plataforma -> /admin/empresas (MOD 000072) -> seleccionar SKY SYSTEM -> usuarios reales +
  seccion "Cargar datos" Pendiente -> editar telefono -> guardar (flash ok) -> recargar ->
  telefono persistido.
- Verificacion manual claro/oscuro (preview 5253, login admin@ecorex.local): ficha SKY SYSTEM
  con plan "Plan Empresa", estado Activa, 4 usuarios reales, campos de contacto del seeder
  (Bogota / +57 601 234 5678 / contacto@sky-system.local), 9 secciones "Pendiente" con tag
  ambar; edicion de telefono guardada (persistida en BD: tenants.phone; auditoria escrita:
  super_admin_audit_logs action_name=tenant.profile.update). En dark los tokens conmutan
  (bg #0A0A0B, surface #161618, ink/brand invertidos) por construccion (solo tokens workspace).
  NavMenu muestra "Ficha de empresa 000072" en SUPER ADMIN SAAS; grupo tenant "Sistema-General"
  paso a 7 items.
- Procesos DETENIDOS (preview 5253 parado; proceso residual 5234 de sesion previa terminado;
  fixture E2E mata su app). Sin listeners en 525x/5234.

**Deudas / TODO** (documentadas en ADR-0026):
- Cada una de las 9 secciones placeholder necesita su ola: asignacion de modulos/actividades/
  parametros/integraciones POR EMPRESA (con servicios transaccionales), contador/revisor como
  entidad owned del Tenant, y plantillas versionadas transaccionales para reemplazar la copia
  de datos/formularios del legacy (nunca SQL crudo + blacklist + db3dev).
- Policy `AdmEmpresas.Ver` en paso 1 (solo platform_role); paso 2 = MFA para acciones criticas
  + derivar del rol real, como el resto de policies del proyecto.
- Sin commit (pedido explicito): cambios en working tree.

**Decisiones**: ver ADR-0026 (area PlatformAdmin, mapeo a Tenant real, gaps como placeholders,
por que NO se reconstruyen los flujos SQL peligrosos, relacion con /tenants existente).

---

## 2026-07-05 - Sesion: Modulo EXTRACCION DE DATOS / web scraping (000730, ADR-0025)

**Agentes**: agente unico (lectura proto+spec, modelo+DAL dual, servicio+guard SSRF,
UI, seeder+endpoint demo, tests unit/integracion/E2E, verificacion manual).

**Hecho**:
- Pagina real `/extraccion-datos` (ExtraccionDatos.razor + .css) que REEMPLAZA al stub
  generico. Estructura del proto `proto_web_scraping.html` con tokens del workspace
  (regla ADR-0023): topbar 14x24 + MOD 000730, layout 300px/1fr max 1500, sidebar
  sticky de fuentes con dot verde/rojo/gris, hero r12 gradiente --brand-2->--brand con
  4 KPIs (ejecuciones/exito 30d/registros/ultima corrida), franja ambar de alcance,
  cols 1fr/380 (preview tabla + JSON crudo en editor oscuro fijo | editor de fuente con
  selector CSS + ayuda), tabla de historial con pills. NavMenu: el item 000730 pasa de
  `modulo/extraccion-de-datos` a `extraccion-datos` (SOLO ese item); Modulo.razor retira
  su entrada del registro de stubs.
- Modelo + DAL dual: ScrapeSource (TenantEntity: Name, Url, Selector?, Kind
  enum Html|Json, Status Active|Inactive|Error, LastRunAt?, LastResultSummary?; indice
  unico tenant+name) y ScrapeRun (TenantEntity: SourceId FK cascade, Status
  Success|Failed, ItemCount, DurationMs, ErrorMessage?, ResultJson dual jsonb/nvarchar(max)
  recortado a 64 KB). DbSet + IApplicationDbContext + configuracion. UNA migracion dual
  `AddScraping` (Ecorex.Infrastructure 20260705033315 + Ecorex.Infrastructure.SqlServer
  20260705033251) APLICADA y verificada en los contenedores dev (PG 5442 \d scrape_runs
  con result_json jsonb + MSSQL 1443 sys.tables con result_json nvarchar(max)).
- IScrapeService (Application/Scraping): CRUD de fuentes con validaciones tipadas
  (ScrapeOpResult: Ok/NotFound/Invalid; nombre unico por tenant, URL absoluta http(s)
  sin credenciales, selector obligatorio en Html) + RunAsync que SIEMPRE persiste la
  corrida (exito o fallo) y actualiza LastRunAt/summary/estado de la fuente en UNA
  transaccion. Eliminar con historial -> Invalid (se ofrece desactivar, criterio ADR-0023).
- SEGURIDAD (nucleo del ADR): SsrfUrlGuard (puro, testeado) resuelve DNS y valida TODAS
  las IPs (fail-closed) contra loopback/privadas RFC1918/link-local+metadata 169.254.169.254/
  CGNAT/multicast/clase E/IPv6 ULA+link-local+mapeadas; solo http(s), sin user@host, solo
  puertos 80/443. ScrapeHttpFetcher: SOLO GET, User-Agent propio, timeout 15s total, tope
  2 MB (stream + Content-Length), AllowAutoRedirect=false y max 3 redirecciones seguidas
  A MANO re-validando cada salto. Excepcion AllowLoopback SOLO en Development (Program.cs
  re-registra el singleton) para el endpoint demo propio.
- Parser puro ScrapeContentParser: JSON (conteo + preview tabular) y HTML por selector CSS
  con AngleSharp. Se agrego el paquete **AngleSharp 1.5.1 estable** a Ecorex.Application
  (no estaba referenciado; justificado en ADR-0025: parser puro sin red/telemetria, el
  selector CSS es central en la spec).
- Endpoint demo `/api/demo/scrape-sample` en el SuperAdmin (JSON estatico de 8 items,
  AllowAnonymous) + seeder EnsureScrapingDemoAsync idempotente (fuente Json demo apuntando
  a ese endpoint; re-apunta la URL al puerto vivo). Policy nueva `ExtraccionDatos.Editar`
  (paso 1: nombre estable, requisito = tenant_id).

**Validacion**:
- Build Ecorex.sln 0 errores; `dotnet format --verify-no-changes` limpio.
- Unit: Application 247/247 verdes (+78 nuevos: SsrfUrlGuardTests exhaustivo -esquemas,
  IPv4/IPv6 privadas y mapeadas, DNS que resuelve a privada, mezcla publica+privada,
  puertos, excepcion loopback dev, bordes de rango-; ScrapeHttpFetcherTests -GET, UA,
  redireccion a privado bloqueada sin request, redireccion a publico seguida, tope de
  saltos, tope de bytes/Content-Length, HTTP 5xx tipado-; ScrapeContentParserTests -JSON
  array/propiedad/objeto/escalares/invalido/preview acotada, HTML selector valido/
  compuesto/invalido/sin selector/sin match, recorte de ResultJson sin perder el total-).
- Integracion dual +6 (3 tests x 2 motores PG+SQL Server via Testcontainers) ScrapingTests
  verdes: CRUD con validaciones + corrida real contra endpoint HttpListener local (8 items,
  ResultJson jsonb/nvarchar valido, metricas de la fuente, no-borrado con historial);
  historial que persiste FALLO (HTTP 500 -> fuente Error) y EXITO (vuelve a Active),
  ambas corridas conservadas; aislamiento cross-tenant (B no ve fuentes ni corridas de A,
  RunAsync/DeleteAsync de A desde B = NotFound, mismo nombre reutilizable por tenant).
- E2E Playwright COMPLETA verde 18/18 contra app real (PG 5442, puerto 5250 auto);
  +1 escenario ExtraccionDatosTests: crear fuente demo JSON al endpoint propio -> Ejecutar
  -> preview con 8 items (columnas sku/nombre/precio/stock) -> KPI registros 8 -> historial
  con pill dot verde "Exitoso" y 8 registros. (Una 1a corrida dio el flake conocido de
  ReglasTests -race de Blazor al crear regla, documentado en sesiones previas-; el rerun
  limpio fue 18/18.)
- Verificacion manual claro/oscuro (preview 5253): layout desktop grid 300px/1fr y cols
  1fr/380 (en viewport <1100 conmuta a 1 columna, responsive del proto), hero gradiente,
  franja ambar, tabla de preview 8 filas con SKU/nombre/precio/stock, editor JSON oscuro;
  en dark los tokens conmutan (bg #0A0A0B, ink #F4F4F5, brand invertido, amber/verde
  translucidos rgba). Corrida real persistida verificada en BD (scrape_runs Success 8 items
  result_json jsonb valido). El seeder re-apunto la fuente demo a 5253. NavMenu muestra el
  unico item "Extraccion de datos 000730" a la pagina real.
- Procesos DETENIDOS (preview 5253 parado; sin listeners en 525x/5232).

**Deudas / TODO** (documentadas en ADR-0025):
- Scheduler (CICLO del legacy): sin corridas programadas en esta ola; ira como
  BackgroundService en Ecorex.Workers con cola + rate-limit por tenant.
- DNS rebinding: la IP validada no se fija para la conexion posterior (TTL 0 podria
  re-resolver distinto entre validacion y GET); mitigacion via ConnectCallback = deuda.
- Multi-paso legacy (variables/credenciales cifradas, APIs, seguimiento trading),
  robots.txt + rate-limit por dominio, extraccion de atributos (href/src) ademas de texto.
- Sin commit (pedido explicito): cambios en working tree.

**Decisiones**: ver ADR-0025 (alcance acotado, guard SSRF estricto, AngleSharp 1.5.1,
excepcion loopback dev, TODO scheduler).

---

## 2026-07-03 - Sesion 1: Lectura del vault + FASE 0 (setup del repo)

**Agentes**: coordinador + 5 subagentes lectores del vault en paralelo.

**Hecho**:
- Lectura obligatoria completa del vault OBSIDIAN.tareas (5 lectores en paralelo:
  vision/prototipo, hoja de ruta/ADRs, multi-tenant/9 errores, DAL dual/MotherData,
  testing/ETL/db3dev). Entregado resumen de entendimiento: 15 puntos de arquitectura,
  aislamiento multi-tenant, DAL dual, 9 errores + fix, orden de construccion.
- Entorno verificado: .NET SDK 10.0.301 y 9.0.315, Docker 29.3.0, backbone CUBOT.nails
  con upstream cubotcrm.
- FASE 0 ejecutada:
  - Clon del backbone CUBOT.nails -> C:\DesarrolloIA\ECOREX.tareas (git init + fetch,
    preservando .claude/ existente). Base: rama main (= deploy, mismo commit bb00f69).
  - Remotes: origin=https://github.com/alexandercuartas665/EcorexV.git,
    upstream=https://github.com/alexandercuartas665/cubotcrm.git.
  - Renombrado estructural: 12 proyectos + sln CubotNails.* -> Ecorex.*,
    CubotSidebar.tsx -> EcorexSidebar.tsx, start/stop-cubot.ps1 -> start/stop-ecorex.ps1.
  - Reemplazo de contenido en 568 archivos (CubotNails->Ecorex, cubot->ecorex,
    CUBOT->ECOREX; `cubotcrm` preservado como nombre del upstream). Brand .nails -> .tareas.
  - `dotnet build Ecorex.sln`: verde (0 errores) tras el renombrado.
  - Docker dedicado: docker-compose reescrito con project name `ecorex-tareas`,
    prefijo `ecorex-tareas-` en contenedores/volumenes/red, puertos Postgres 5442,
    SQL Server 1443 (servicio NUEVO, mssql 2022), Redis 6389, RabbitMQ 5682/15682,
    Adminer 8092 (reemplaza pgAdmin). `.env.example` actualizado.
  - `preflight.ps1` + `preflight.sh` nuevos (docker vivo, puertos libres, contenedores
    previos, recursos minimos); integrado en start-ecorex.ps1.
  - CLAUDE.md reescrito apuntando al vault OBSIDIAN.tareas como fuente de verdad,
    con reglas inviolables, puertos dedicados y orden de fases.
  - PROGRESO.md creado (este archivo).

**Validacion FASE 0 (completada)**:
- Commit a482b47 con todo el renombrado + infra. Push a origin como rama
  `fase-0/clon-backbone` (push directo a main bloqueado por politica; merge a main
  queda como decision del usuario en GitHub).
- Pre-flight OK (6 puertos libres, Docker 15.6 GB RAM). `docker compose up -d`
  levanto los 5 servicios `ecorex-tareas-*` con healthchecks verdes
  (Postgres 5442, SQL Server 1443, Redis 6389, RabbitMQ 5682/15682, Adminer 8092).
- Consola Ecorex.SuperAdmin arranco contra la pila nueva: aplico las 72 migraciones,
  sembro PlatformAdmin + tenants (Agencia Demo, Plataforma ECOREX) + plan. /login 200.

**FASE 1 COMPLETADA** (3 subagentes: A seeders, B DAL dual, C test dual):
- Consola Super Admin operativa contra la pila dedicada (72 migraciones + seed, /login 200).
- Seeders segun vault (commit 0d09e16): tenant demo SKY SYSTEM (= legacy 01 BITCODE),
  PlatformAdmin admin@ecorex.local, owner/admin/operator/viewer@sky-system.local
  (Operator/Viewer mapeados a Advisor con TODO), plan "Plan Empresa".
- DAL dual (commit 0d09e16): proyecto Ecorex.Infrastructure.SqlServer con
  SqlServerEcorexDbContext y migracion inicial (77 tablas); seleccion por
  Database:Provider / ECOREX_DB_PROVIDER; jsonb->nvarchar(max), HasFilter por motor,
  cascadas ajustadas. Verificado E2E: la app migra y siembra en SQL Server real (1443)
  y el camino Postgres queda intacto (5442).
- TEST DE AISLAMIENTO CROSS-TENANT EN MATRIZ DUAL: TenantIsolationTestsBase +
  fixtures Testcontainers (postgres:16-alpine y mssql 2022) -> 6/6 verde
  (aislamiento A/B, fail-closed sin tenant, IgnoreQueryFilters admin, x2 motores).
  Canario verificado: al romper el filtro, el test FALLA (gate efectivo).

**Siguiente**:
- Decidir con ADR la migracion de TFM net9.0 -> net10.0 (SDK 10.0.301 disponible).
- Limpieza del dominio belleza/agenda (24 entidades: Service*, Resource*, Appointment*,
  Client, HairLength*, Shift*, Product*, Course*, Sede) con ADR y commits separados.
  Nota: al eliminarlas cae tambien la exclusion GiST anti-overbooking (gap SQL Server).
- FASE 2: menu del Prototipo Final (MainLayout doble panel, PRINCIPAL/MODULOS,
  stubs por policy) + revision de policies/MFA.
- Nucleo tareas/tableros/proyectos (el backbone ya trae TaskBoard/TaskCard como base).
- Actualizar vault: Registro de corridas (primera corrida dual) + ADR del DAL dual aplicado.

**Bloqueos**: ninguno. (db3dev no se ha tocado; se pedira la conexion al usuario
cuando llegue la fase de descubrimiento/ETL.)

**Decisiones**:
- Base del clon: rama `main` del backbone (identica a `deploy`).
- Adminer en lugar de pgAdmin (sirve Postgres Y SQL Server con una sola UI, puerto 8092).
- El nombre `cubotcrm` NO se renombra: identifica al repo upstream.

---

## 2026-07-03 - Sesion 2: Eliminacion del dominio belleza/agenda (ADR-0011)

**Agentes**: agente unico (barrido + migraciones + validacion).

**Hecho**:
- Eliminadas las 22 entidades belleza de Ecorex.Domain (Service*, Resource*,
  HairLength*, ShiftTemplate, ScheduleException, SalonFieldDefinition, Sede,
  Appointment*, Client, Product*, Course*) + 10 enums huerfanos.
- Eliminados 17 servicios/toolsets de Application (Agenda, Client, Course, Product,
  Resource, SalonField, ScheduleException, Sede, ServiceCatalog, ShiftTemplate,
  HairLength/HairClassifier, OnlineBooking y los 4 toolsets de IA belleza).
  El motor de agentes queda solo con PipelineToolset (crear_lead);
  AgendaToolResult -> AgentToolResult.
- Eliminadas 15 paginas + 3 componentes Blazor belleza de Ecorex.SuperAdmin,
  PublicBookingService (/r/{token}) y los endpoints /media/hair|hairref|asesor.
  NavMenu: solo se quitaron las entradas muertas (sin redisenar el menu).
- Seeders: fuera EnsureDemoProductsAsync, EnsureDemoCoursesAsync y
  EnsureDemoAgentCommercialFlowAsync (vendia productos/cursos). Demo de agente queda
  el one-shot TravelFans (CRM). EnsureDemoTemplateAssetsAsync se conserva.
- Tests: eliminados AppointmentOverbookingTests y AppointmentTierBookingTests.
  TenantIsolationTests intacto (usa TenantConfiguration, conservada): 6/6 dual verde.
- Migraciones DAL dual: Postgres `20260703175944_RemoveBellezaDomain` (drop de 22
  tablas, cae la exclusion GiST ck_appointments_no_overlap con la tabla); SQL Server
  regenerada la inicial `20260703180047_InitialCreateSqlServer` desde el modelo limpio
  (BD dev recreada). Ambas aplicadas a los contenedores dev; 55 tablas identicas por motor.
- Validado: build 0 errores, tests Domain/Application/TenantIsolation verdes,
  SuperAdmin arranca contra Postgres 5442 y /login responde 200.
- ADR nuevo: docs/decisiones/0011-eliminar-dominio-belleza.md.

**Siguiente**:
- Migracion TFM net9.0 -> net10.0 (ADR propio).
- FASE 2: menu del Prototipo Final + policies/MFA.
- Nucleo tareas/tableros/proyectos sobre TaskBoard/TaskCard.

---

## 2026-07-03 - Sesion 3: Menu del Prototipo Final (tarea funcional previa de FASE 2)

**Agentes**: agente unico (menu + stubs + validacion de rutas).

**Hecho**:
- NavMenu del workspace del tenant reorganizado segun el Prototipo Final:
  PRINCIPAL (Inicio /inicio, Anuncios /anuncios, Gestor de tareas /tableros+/tareas,
  Configuracion /configuracion), MODULOS con codigo legacy visible (Proyectos 000042,
  Actividades 000038/000636/000889, Flujos 000291, Formularios 000131, Reglas 000802),
  SISTEMA (Dependencias 000850, Modulos web 000109), CRM heredado colapsado
  (nada se borro), Super Admin SaaS intacto con su policy.
- Componente ModuleStub.razor (breadcrumb + chip de modulo legacy + tarjeta
  "se construye en Fase X") y 10 paginas nuevas; Inicio.razor con saludo contextual,
  tenant activo desde claim y 4 KPIs placeholder.
- Header del sidebar: Workspace / {tenant} / Plan {plan} - ECOREX (datos reales,
  fallback generico). Buscador placeholder estilo prototipo (Ctrl+K deshabilitado).
- Policies: stubs con [Authorize(Policy="TenantMember")] + TODO por modulo;
  PlatformOperator/SuperAdminOnly sin cambios.
- Validado: build 0 errores, unit tests 2/2, /login 200 y las 12 rutas del menu
  responden sin 404/500 (redirigen a login sin sesion).

**Siguiente**:
- Migracion TFM net9.0 -> net10.0 + EF Core 10 (ADR propio).
- FASE 3: nucleo tareas/tableros/proyectos sobre TaskBoard/TaskCard.
- Anuncios y dashboard de Inicio con datos reales.

**Bloqueos**: ninguno.

**Decisiones**:
- Inicio es pagina nueva del tenant (/inicio); Home.razor "/" sigue siendo el
  dashboard de PlatformOperator.
- Gestor de tareas mapea a Tableros.razor existente; Configuracion a Cuenta.razor.
- Sin toggle de modo oscuro aun (no existia; se hara con el rebrand visual fino).

**Bloqueos**: ninguno.

**Decisiones**:
- Tenant.PublicBooking*/OnlineBookingEnabled se conservan (regla "no tocar Tenant*"),
  quedan sin uso; retiro en fase posterior con su propia migracion.
- BusinessUnitModalKind.ImageAdvisory se conserva como valor legado (enum persistido
  como texto) para leer filas existentes; la UI ya no lo ofrece y los defaults de
  BusinessUnitService crean una sola unidad "General".

---

## 2026-07-03 - Sesion 4: Migracion a .NET 10 + EF Core 10 (ADR-0012)

**Agentes**: agente unico (migracion TFM + paquetes + validacion completa).

**Hecho**:
- TFM net9.0 -> net10.0 en los 13 csproj de la solucion (10 src + 3 tests).
- Stack completo a 10.x estable, sin mezclar majors en EF: EF Core (Core/Relational/
  Design/SqlServer) 9.0.4 -> 10.0.9; Npgsql.EntityFrameworkCore.PostgreSQL 9.0.4 ->
  10.0.2; EFCore.NamingConventions 9.0.0 -> 10.0.1 (existe estable para EF10, no hubo
  bloqueo); AspNetCore DataProtection*/JwtBearer/Mvc.Testing 9.0.4 -> 10.0.9;
  OpenApi/Components.WebAssembly(+Server) 9.0.16 -> 10.0.9; SignalR.Client 9.0.0 ->
  10.0.9; Extensions.Hosting 9.0.16 -> 10.0.9; Extensions.* 9.0.4 -> 10.0.9;
  tool local dotnet-ef 9.0.4 -> 10.0.9. Testcontainers/xunit/QuestPDF/PuppeteerSharp/
  System.IdentityModel.Tokens.Jwt sin cambios (el build no lo exigio).
- Unico fix de codigo por C# 14: variable local `field` -> `fieldDef` en accessor de
  Plantillas.razor (CS9273: `field` es keyword en accessors).
- Migraciones: has-pending-model-changes = "No changes" en ambos contextos
  (EcorexDbContext y SqlServerEcorexDbContext) bajo EF10. Sin Ef10ModelSync, sin
  tocar snapshots ni migraciones historicas. Nota: el contexto SqlServer requiere
  --startup-project src/Ecorex.Infrastructure.SqlServer (el factory design-time vive
  ahi; EF tools solo buscan factories en el startup assembly).
- ADR-0012 creado (docs/decisiones/0012-migracion-net10.md); ADR-0003 marcado como
  Reemplazado. CLAUDE.md seccion 4 actualizada con el stack real.

**Validacion (toda verde)**:
- dotnet build Ecorex.sln: 0 errores.
- Domain.Tests 1/1 y Application.Tests 1/1 en net10.0.
- Integration TenantIsolation 6/6 (matriz dual Testcontainers: postgres:16-alpine +
  mssql 2022).
- SuperAdmin /login 200 contra Postgres 5442 y contra SQL Server 1443
  (ECOREX_DB_PROVIDER=SqlServer); ambos procesos detenidos al terminar.

**Siguiente**:
- Actualizar imagenes base de Dockerfile.superadmin / Dockerfile.workers a 10.0
  antes del proximo deploy.
- Resolver NU1903 (Microsoft.OpenApi 2.0.0 transitiva, GHSA-v5pm-xwqc-g5wc) y
  ASPDEPR005 (KnownNetworks -> KnownIPNetworks en SuperAdmin/Program.cs).
- FASE 3: nucleo tareas/tableros/proyectos sobre TaskBoard/TaskCard.

**Bloqueos**: ninguno.

**Decisiones**:
- Todo el stack EF/AspNetCore queda en la misma major (10.x); EFCore.NamingConventions
  10.0.1 existia estable, asi que no aplico el plan B de quedarse en 9.x sobre net10.
- Sin commit (pedido explicito de la sesion): cambios en working tree.

---

## 2026-07-03 - Sesion 5: FASE 3 ola 1 - dominio + servicios del nucleo de tareas/proyectos (ADR-0013)

**Agentes**: 1 agente constructor.

**Hecho**:
- Dominio nuevo (Ecorex.Domain): entidades ActivityType, Project, ProjectMember,
  TaskItem, TaskItemTag (catalogo POR TENANT), TaskItemTagAssignment, TaskWorkLog,
  TaskItemActivity (reusa enum TaskActivityType), TaskItemAttachment y TenantSequence;
  enums ProjectStatus, TaskPriority, TaskItemStatus, WorkLogKind; maquina de estados
  TaskItemStateMachine (Domain/Rules) con Closed terminal e inmutable.
- Concurrencia optimista PORTABLE (decision del ADR-0013): columna Version (long) como
  ConcurrencyToken en TaskItem y Project via interfaz IVersioned; la incrementa
  AuditableTenantInterceptor en cada UPDATE. Elegida sobre xmin/rowversion para que
  modelo, migraciones y token de API sean identicos en ambos motores.
- Consecutivos: TenantSequence (TenantId+Code unico) + SequenceService con UPDATE
  condicional atomico (CAS con retry) via ExecuteUpdateAsync LINQ, sin SQL crudo,
  dentro de la transaccion del caso de uso. Reemplaza el MAX+1 legacy.
- Servicios (Application/Tenancy, patron interfaz+impl+DTOs, registrados en DI):
  ISequenceService, IActivityTypeService (CRUD + archivado), IProjectService (CRUD,
  soft-archive, miembros con CanEdit, CheckAccessAsync) e ITaskItemService (CreateAsync
  transaccional con consecutivo T00001.., UpdateAsync con token de concurrencia ->
  Conflict tipado, ChangeStatusAsync con maquina de estados -> InvalidTransition tipado,
  Assign/Unassign, tags attach/detach + catalogo, comentarios, adjuntos, worklogs con
  validacion 1..86400 s, ListAsync con filtros AND combinables + paginacion,
  GetDetailAsync compuesto). Resultados via TaskCoreResult<T>; IApplicationDbContext
  gana BeginTransactionAsync.
- Migraciones duales AddTaskCore generadas y APLICADAS: Postgres 5442 y SQL Server 1443
  (10 tablas nuevas verificadas en ambos; cc_emails jsonb vs nvarchar(max)).
- Seeder EnsureTaskCoreDemoAsync (Development, idempotente por tabla y por tenant):
  4 ActivityTypes, 3 tags (#urgente/#proveedor/#facturacion), proyecto PRJ-001
  "Implantacion ECOREX" (owner del tenant demo) y 5 TaskItems variados (uno con 2
  worklogs y 2 comentarios) + TenantSequence T05 en 6. Ejecutado y verificado con
  datos reales en AMBOS motores via SuperAdmin.
- Tests: Domain.Tests 35/35 (maquina de estados: validas, invalidas, Closed terminal);
  Integration TaskCoreTests base + clases Postgres/SqlServer (mismo patron que
  TenantIsolation): 10 creaciones CONCURRENTES -> T00001..T00010 sin duplicados,
  aislamiento cross-tenant de TaskItems, conflicto tipado con token viejo, y
  transiciones invalidas end-to-end. TaskCore 8/8 + TenantIsolation 6/6 verdes en
  ambos motores.
- ADR-0013 (docs/decisiones/0013-nucleo-taskitem.md): TaskItem primera clase,
  TaskBoard/TaskCard queda como kanban generico CRM (destino a decidir), estrategia
  Version portable, TenantSequence, hooks FASE 4 (WorkflowDefinitionId/RequiresForm).

**Validacion**:
- dotnet build Ecorex.sln: 0 errores.
- Unit + integracion nuevas verdes en matriz dual (Testcontainers).
- Migraciones aplicadas y seed verificado por SQL directo en los 2 contenedores dev.

**Bloqueos / hallazgos**:
- PREEXISTENTE (verificado en worktree limpio sobre HEAD 0c1c3b0): los 27 tests de
  Integration.Tests/Auth fallan porque el host Ecorex.Api no registra
  IAgentAssetReader (solo lo registra SuperAdmin) y ValidateOnBuild revienta. No es de
  esta sesion; fix sugerido: no-op por defecto en Application/DependencyInjection
  (patron NoOpChatBroadcaster).
- La BD dev de Postgres aun tiene el tenant demo legacy "Agencia Demo" (seed FASE 0);
  el seeder de tareas cae al primer Owner del tenant demo si no encuentra
  owner@sky-system.local.

**Decisiones**:
- Concurrencia: columna Version portable (NO xmin/rowversion condicional) - ADR-0013.
- Consecutivo con CAS + retry y EnsureSequenceAsync fuera de la transaccion (un error
  de unicidad en PG envenenaria la transaccion del caso de uso).
- Sin commit (pedido explicito): cambios en working tree.

## 2026-07-03 - Sesion 6: FASE 3 ola 2 - UI del nucleo de tareas (Blazor SuperAdmin)

**Agentes**: 1 agente implementador + validacion funcional en navegador real (preview).

**Hecho**:
- /actividades reemplaza el stub: TaskKanban con columnas fijas por estado
  (Pendiente/Activa/En proceso/Terminada/Suspendida; Closed solo en lista con toggle
  "ver cerradas"), tarjetas con numero, prioridad (chips color), avatar del encargado,
  entrega (rojo si vencida), tags y borde con Color; drag and drop nativo ->
  ChangeStatusAsync con toast del motivo si la transicion es invalida; vista Lista
  (tabla completa); barra de filtros combinables server-side via ListAsync (texto,
  estados, prioridad, encargado, tipo, proyecto, etiqueta, rango de entrega) + limpiar.
- Wizard "Nueva actividad" (TaskWizard, modal 3 pasos con barra de pasos): Informacion
  (categoria->tipo en cascada, encargado, entrega no-pasada, titulo max 200,
  descripcion, prioridad chips pastel, TagPicker con sugerencias + crear con Enter,
  max 10), Contacto y proyecto (solicitante, email validado, telefono, CC chips
  validados, proyecto opcional/preseleccionado), Confirmar (resumen + CreateAsync);
  errores en rojo bajo el campo, toast con el numero asignado al crear.
- Detalle de tarea (TaskDetailModal, modal grande 2 columnas): hero con numero +
  titulo editable inline y pills (encargado reasignable, entrega, tiempo usado,
  prioridad, estado SOLO con transiciones validas de TaskItemStateMachine); acciones
  Suspender/Reanudar/Cerrar con confirmacion; descripcion editable; worklog con
  CRONOMETRO via JS interop (wwwroot/js/task-timer.js, estado en JS, Guardar avance ->
  Kind=Timer) + entrada manual HH:MM (Kind=Manual) + historial (10) y total "4h 32m";
  card Resumen (tipo, proyecto con link, solicitante, fechas); card Actividad
  (comentarios + acciones automaticas); card Adjuntos por URL. Conflict -> aviso
  "otro usuario modifico la tarea" + recarga.
- /proyectos reemplaza el stub: grid de tarjetas (codigo, nombre, estado con color,
  owner, fechas, contadores) + modal crear/editar con validacion + archivar/restaurar;
  /proyectos/{id} (ProyectoDetalle): cabecera con estado (dropdown), owner, fechas,
  miembros con avatares y panel agregar/quitar/CanEdit + TaskKanban REUTILIZADO con
  ProjectId fijo + boton "+ Tarea" con proyecto preseleccionado en el wizard.
- Tiempo real: ITaskBroadcaster (Application, NoOp por defecto) + TaskHub +
  SignalRTaskBroadcaster (SuperAdmin/RealTime, patron ChatHub), MapHub /hubs/tasks;
  el kanban se suscribe al grupo del tenant y recarga al recibir "TaskChanged"
  {taskId, status}; los componentes difunden tras crear/editar/cambiar estado.
- Componentes en Components/Shared/Tasks/: TaskKanban, TaskWizard, TaskDetailModal,
  TagPicker, PriorityChip, TaskToasts, TaskUi.cs (labels/colores/formatos). CSS nuevo
  seccion "Nucleo de tareas" (tk-*) en app.css reutilizando patrones tb-*/pl-*.

**Fixes sobre ola 1 encontrados al probar contra PG real**:
- ProjectService.ListMembersAsync: OrderBy sobre propiedad del record DTO no
  traducible por EF (InvalidOperationException en PG real); ahora ordena por el campo
  antes de proyectar.
- TaskKanban recarga con scope EF propio (IServiceScopeFactory +
  AmbientTenantContext.Begin) + SemaphoreSlim: el reload disparado por el hub
  interlevaba consultas con el DbContext del circuito ("second operation started on
  this context").

**Validacion (probado de verdad)**:
- dotnet build Ecorex.sln 0 errores; tests 35 Domain + 1 Application + 41 Integration
  verdes (incluye TaskCore y TenantIsolation, Testcontainers duales).
- App contra Postgres 5442 (--no-launch-profile, :5233/:5234): /login 200; /actividades
  y /proyectos 302->login sin sesion, 200 con sesion.
- E2E en navegador real (preview + login demo-admin@ecorex.tareas): kanban con 5
  columnas y seed T00001..T00005; wizard crea T00006 (verificado en BD); validacion
  por paso; cascada categoria->tipo; detalle T00006: dropdown de estado solo con
  transiciones validas, cambio Pending->InProgress con actividad automatica,
  cronometro real (12s+ display JS) -> worklog Timer 23s, manual 1h30m, comentario;
  drag invalido Suspendida->Terminada: toast "Transicion invalida: Suspended -> Done"
  y la tarjeta vuelve; drag valido Suspendida->En proceso: toast + columna actualizada;
  vista Lista + filtro prioridad Alta server-side; /proyectos grid + modal validado;
  detalle de proyecto con kanban filtrado (solo 3 tareas del proyecto) y panel de
  miembros (agregar OK). /hubs/tasks negotiate 200.
- NO probado E2E: refresco cross-sesion por SignalR (requiere 2 navegadores; hub
  mapeado, broadcaster invocado sin errores en log), upload real de adjuntos (por
  diseno queda por URL).

**Deudas / TODO**:
- Archivar tarea: boton deshabilitado en el detalle; ITaskItemService no expone
  archive (IsArchived existe en la entidad). Agregar en una ola posterior.
- Adjuntos: upload real a object storage (FASE posterior); hoy nombre + URL.
- Paginacion de lista: PageSize 200 con aviso "mostrando X de Y"; falta paginador.
- Policies propias (Actividades.Editar / Proyectos.Editar) siguen TenantMember.
- .claude/launch.json: nueva config "superadmin-tasks" (pwsh + ECOREX_DB_CONNECTION
  dev 5442, puerto 5234) usada para la validacion en navegador.
- Sin commit (pedido explicito): cambios en working tree.

---

## 2026-07-03 - Sesion 7: FASE 4 ola 1 - WorkflowEngine (BPMN 2.0, ADR-0014)

**Agentes**: agente unico (port del AdmWorkflow legacy segun el vault, Capa 3).

**Hecho**:
- Dominio (5 entidades TenantEntity + 3 enums): WorkflowDefinition (ProcessCode,
  BpmnXml tal cual, versionado con unico (TenantId, ProcessCode, Version)),
  WorkflowNode (BpmnElementId, NodeType, RestartNodeId self-FK NO ACTION),
  WorkflowEdge (ConditionExpression; FKs a nodos Cascade en PG / ClientCascade en
  SQL Server, patron TaskCardTagAssignment), WorkflowInstance (Status
  Running/Completed/Cancelled/Stuck, CurrentCycle, Version IVersioned, TaskItemId
  unico filtrado) y WorkflowStepHistory APPEND-ONLY (CycleIndex, IsCurrent,
  IsCycleStart, ApprovalResult/Comment; indices (InstanceId, IsCurrent) e
  (InstanceId, NodeId, CycleIndex)). TaskItem.WorkflowInstanceId (FK sin cascada) y
  ActivityType.WorkflowDefinitionId pasa de placeholder a FK real NO ACTION.
- Motor (Ecorex.Application/Workflows): IWorkflowEngine + WorkflowEngine con
  ImportBpmnAsync (XDocument sobre el namespace OMG, acepta prefijos bpmn:/bpmn2:,
  valida 1 startEvent / >=1 endEvent / ids unicos / aristas coherentes; XML guardado
  SIN modificar para round-trip bpmn.io; reimportar = version max+1 NO publicada),
  PublishAsync (una sola version publicada por ProcessCode), SetRestartTargetAsync
  (ID_REINICIO legacy, fuera del XML estandar), StartInstanceAsync (startEvent se
  completa solo; enlaza TaskItem -> Active via TaskItemStateMachine + actividad
  "inicio el flujo"), GetCurrentStepsAsync, CompleteStepAsync y RejectStepAsync
  (reactiva el paso anterior como fila NUEVA, append-only). Avance interno (port de
  SiguienteEstado): while con tope de 50 iteraciones, compuertas exclusivas evaluadas
  contra ApprovalResult (WorkflowConditionEvaluator: "approval == 'X'"/"!=", vacio =
  default, fail-closed), ramas paralelas en nodos no-gateway, REINICIOS en LINQ/memoria
  (sin SQL crudo ni CTE: grafo completo en memoria; nodo alcanzado con RestartNodeId
  abre CycleIndex+1 con IsCycleStart), endEvent completa la instancia y pasa la tarea
  a Done + actividad "flujo completado" + ITaskBroadcaster.TaskChanged; tope de 50 ->
  instancia Stuck + resultado tipado StuckDetected (WorkflowResults, patron
  TaskCoreResults). Hook de reglas IWorkflowRuleHook (OnNodeActivatedAsync ->
  AutoComplete) con NoOpWorkflowRuleHook en DI para la ola RulesEngine.
- Integracion con creacion de tareas: TaskItemService.CreateAsync arranca la instancia
  si el ActivityType tiene definicion PUBLICADA, dentro de la MISMA transaccion
  (IApplicationDbContext.HasActiveTransaction nuevo: el motor se une a la transaccion
  del llamador; fallo del flujo -> rollback total de la tarea).
- Migraciones duales AddWorkflowEngine (Postgres 20260703215437, SqlServer
  20260703215556) generadas y APLICADAS a los contenedores dev (PG 5442 y MSSQL 1443,
  5 tablas workflow_* verificadas en ambos).
- Seeder Development idempotente EnsureWorkflowDemoAsync: flujo demo "Cotizacion
  Comercial" (COT-COM) construido via ImportBpmnAsync con XML BPMN embebido
  (start -> Requerimiento -> Cotizacion -> gateway Aprobacion; Approved ->
  Facturacion -> Entrega -> end; Rejected -> endEvent con RestartNodeId hacia
  Cotizacion), publicado y vinculado al ActivityType "Direccion Comercial/Cotizacion".
  Program.cs (SuperAdmin) lo invoca con AmbientTenantContext.Begin(tenant demo).
- Fixture BPMN real del vault copiado a tests/Ecorex.Integration.Tests/Fixtures/
  (ejemplo-bpmn-flujo-00001.bpmn, CopyToOutputDirectory).
- ADR-0014 (docs/decisiones/0014-workflow-engine.md): motor propio, XML estandar sin
  extensiones, tope 50 heredado, append-only, reinicios en LINQ sin CTE, hook de
  reglas, versionado que fija la version por instancia.

**Validacion (probado de verdad)**:
- dotnet build Ecorex.sln: 0 errores.
- Tests TODOS verdes: Domain 35, Application 26 (1 previa + 8 parser BPMN + 17 casos
  del evaluador de condiciones), Integration 57 (41 previas + 16 nuevas: 8 tests
  WorkflowEngine x 2 motores via Testcontainers PG/MSSQL): import del fixture real
  00001 (14 nodos = 1 start + 8 tasks + 3 gateways + 2 ends, 13 aristas, XML
  round-trip identico), versionado/publicacion exclusiva, flujo lineal con TaskItem
  auto-arrancado desde CreateAsync que termina Done, gateway Approved/Rejected con
  reinicio (CycleIndex=1, IsCycleStart, CurrentCycle=1), RejectStep reactivando el
  paso previo append-only, loop autonomo sin salida -> Stuck al tope de 50 (hook
  AutoComplete de prueba), aislamiento cross-tenant de definiciones/instancias/pasos
  e historial append-only tras reinicio (filas del ciclo 0 intactas).
- Seeder verificado contra el dev PG real (SuperAdmin arrancado en 5237): COT-COM v1
  publicado, 8 nodos con restart en End_Reinicio, ActivityType Cotizacion vinculado.

**Desviaciones del diseno pedido (con su porque)**:
- El fixture 00001 tiene 27 elementos ejecutables reales (14 nodos + 13 flows), no 42
  (ese conteo incluia anotaciones/asociaciones/DI, que el motor ignora); el test
  asegura los conteos reales y que endEvents son 2 (no "varios").
- Ademas del Stuck por tope de 50, se marca Stuck el caso "sin pasos vigentes y sin
  endEvent alcanzado" (ramas muertas del legacy, ej. tasks sin salida del fixture):
  evita instancias Running zombis.
- RejectStepAsync no reactiva un startEvent (no es reactivable por un humano):
  devuelve Invalid "no hay paso anterior reactivable".

**Deudas / TODO (proximas olas de FASE 4)**:
- Editor visual bpmn-js + UI de bandeja de pasos (esta ola es solo motor + seeder).
- RulesEngine reemplazando NoOpWorkflowRuleHook; condiciones de gateway sobre datos
  de formulario dinamico.
- Asignacion de encargados por paso (AssignedToTenantUserId existe pero nada lo
  puebla aun; el legacy lo resolvia con PERMISO_CARGO).
- Cancelacion manual de instancias (WorkflowInstanceStatus.Cancelled sin caso de uso).
- Sin commit (pedido explicito): cambios en working tree.

---

## 2026-07-03 - Sesion 8: FASE 4 ola 2 - DynamicFormRenderer (formularios dinamicos, ADR-0015)

**Agentes**: agente unico (port del constructor EAV legacy; en paralelo OTRO agente
trabajo SOLO la UI del layout - MainLayout/NavMenu/Login/Inicio/app.css/Home no se
tocaron desde esta ola).

**Hecho**:
- Entidades TenantEntity (Ecorex.Domain): FormDefinition (Code unico por tenant,
  Revision de negocio SEPARADA del token de concurrencia Version/IVersioned, Status
  Draft/Active/Inactive, IsArchived), FormContainer (arbol por ParentId self-FK NO
  ACTION, Segment/Table), FormQuestion (FieldCode unico por definicion = clave del
  documento JSON, ControlType con los 19 tipos del legacy pero solo Tier 1 renderizable,
  OptionsJson, ValidationJson, GridCol, Numeral), FormResponse (Data jsonb/nvarchar(max)
  { fieldCode: { value, type } }, patron dual de CcEmails, indice TenantId+DefinitionId+
  Reference, IVersioned), FormFlowLink (unico instancia+nodo+respuesta, Pending/
  Completed), FormToken (TokenHash SHA-256 unico por tenant, ExpiresAt, SingleUse,
  UsedAt, RevokedAt, AllowAnonymous) y WorkflowNodeForm (un formulario por nodo, indice
  unico NodeId).
- Enums persistidos como texto (patron existente): FormStatus, FormContainerType,
  FormControlType, FormResponseStatus, FormFlowLinkStatus.
- Servicios (Ecorex.Application/Forms, patron TaskCoreResults -> FormResult<T> con
  FieldErrors): IFormDefinitionService (CRUD definicion/contenedores/preguntas con
  FieldCode unico y formato identificador, opciones obligatorias y con ids unicos en
  Select/MultiCheck/Radio, pattern compilable, min<=max; ActivateAsync valida estructura;
  Revision++ en cambios estructurales sobre Active; AssignToWorkflowNodeAsync),
  IFormResponseService (GetOrCreateDraftAsync por definicion+referencia, SaveAsync con
  VALIDACION SERVIDOR completa por tipo devolviendo errores por fieldCode; al Submit con
  FormFlowLink Pending completa el paso via IWorkflowEngine.CompleteStepAsync en la MISMA
  transaccion - el motor se une via HasActiveTransaction; GetTaskStepFormsAsync asegura
  borrador+link idempotentes para pasos current con formulario), IFormTokenService
  (EmitAsync devuelve el token EN CLARO una sola vez y guarda solo el hash; ValidateAsync
  con las 4 verificaciones y el UNICO IgnoreQueryFilters permitido - cross-tenant acotado
  al hash exacto, devuelve el TenantId del token para fijar el ambient; RevokeAsync y
  MarkUsedAsync tenant-scoped). FormFieldValidator puro (sin EF), compartido por cliente
  y servidor. Registro DI completo.
- UI (Ecorex.SuperAdmin): /formularios reemplaza el stub (grid de definiciones con code/
  titulo/estado/revision/#preguntas + modal cabecera + boton Disenar);
  /formularios/{id}/disenar builder basico (arbol de contenedores como lista anidada,
  grid de preguntas con modal por tipo con opciones y validaciones, reordenar con
  botones arriba/abajo - SIN drag and drop, ola posterior), Vista previa (renderer en
  modo Design), Activar/Desactivar y Publicar por URL (modal que muestra la URL UNA vez
  + lista/revocacion de tokens). Componente DynamicFormRenderer
  (Components/Shared/Forms): parametros DefinitionId/Reference/Mode(Design,Fill,
  ReadOnly)/ResponseId/AmbientTenantId/OnSubmitted; arbol contenedores->preguntas con
  controles Tier 1 respetando GridCol (grid bootstrap), validacion cliente inmediata con
  el MISMO validador + errores del servidor bajo cada campo, autosave del borrador cada
  30s (timer) y boton Enviar. Visor publico /f/{token} ([AllowAnonymous], EmptyLayout):
  valida token, fija AmbientTenantContext.Begin(tenant del token), renderiza en Fill,
  marca usado si SingleUse y muestra pantalla de gracias; errores de token con mensaje
  NEUTRO (no distingue invalido/expirado/usado/revocado).
- Integracion flujo en TaskDetailModal (cambio MINIMO): seccion "Formularios del paso"
  en la columna lateral cuando la tarea tiene instancia con paso current cuyo nodo tiene
  WorkflowNodeForm; chip Pendiente/Enviado y modal con el renderer; el paso se completa
  UNICAMENTE enviando el formulario.
- Estilos de las paginas/componentes nuevos via CSS isolation (.razor.css) con los
  tokens EXACTOS del prototipo ECOREX.dc.html como fallback literal (var(--surface,
  #FFFFFF) etc.): si el layout define las variables globales del prototipo, se heredan.
- Seeder Development idempotente EnsureDynamicFormsDemoAsync: FRM-001 "Solicitud de
  cotizacion" (SKY SYSTEM) ACTIVO con 2 contenedores y 8 preguntas Tier 1 variadas
  (Text con min/max y pattern email, Select, Radio, Number con rango, Date, Toggle,
  TextArea) y WorkflowNodeForm hacia el nodo "Cotizacion" (Task_Cotizacion) del flujo
  demo COT-COM. Program.cs (SuperAdmin) lo invoca tras EnsureWorkflowDemoAsync.
- Migraciones duales AddDynamicForms (Postgres 20260703231608, SqlServer 20260703231718)
  generadas y APLICADAS a los contenedores dev (PG 5442 y MSSQL 1443; 7 tablas form_* +
  workflow_node_forms verificadas en ambos).
- ADR-0015 (docs/decisiones/0015-dynamic-forms.md): EAV -> documento JSON por respuesta,
  Revision separada de Version, Tier 1 primero con enum completo, token opaco hasheado
  con expiracion/un-solo-uso/revocacion, cross-tenant acotado del visor anonimo,
  FormFlowLink + WorkflowNodeForm.

**Validacion (probado de verdad)**:
- dotnet build Ecorex.sln (Release): 0 errores (el bin Debug del SuperAdmin estaba
  bloqueado por la instancia del agente de layout; esta ola valido y corrio en Release).
- Tests: Domain 35 verdes, Application 58 verdes (26 previas + 32 FormFieldValidator:
  required por tipo, longitudes, pattern y pattern invalido ignorado, rangos numericos,
  fechas, toggle, opcion unica/multiple invalida, parsers), Integration 67 verdes
  (57 previas + 10 nuevas: 5 tests DynamicForms x 2 motores via Testcontainers PG/MSSQL):
  CRUD + round-trip del documento identico (incluido type por campo y rechazo de
  FieldCode duplicado/Select sin opciones/regex rota), submit invalido con 6 errores por
  fieldCode y borrador intacto (autosave no valida), ciclo de vida del token (emitir ->
  validar -> usar -> reusar falla por single-use, expirado falla, revocado falla,
  garabateado falla) con scoping verificado (DbSet de B vacio, RevokeAsync cross NotFound,
  ValidateAsync devuelve el tenant del TOKEN), submit del formulario vinculado completa
  el paso (link Completed, motor avanza a Task_B, ExecutedBy = quien envio) y aislamiento
  cross-tenant de definiciones/preguntas/respuestas.
- Seeder + arranque real contra PG 5442 en puerto 5235: /formularios y /f/{token-invalido}
  responden sin 500 (login redirect y mensaje neutro respectivamente).

**Desviaciones del diseno pedido (con su porque)**:
- FormQuestion.ContainerId y FormContainer.ParentId son NO ACTION (pedido) y ademas
  DeleteContainerAsync reubica preguntas/hijos al padre en vez de fallar: evita el error
  1785 de SQL Server y no deja huerfanos.
- GetOrCreateDraftAsync con reference null SIEMPRE crea borrador nuevo (visor anonimo
  multi-uso: cada visitante su respuesta); con reference reutiliza el Draft existente.
- El chip "Pendiente Fase 4" del item Formularios en NavMenu.razor NO se toco (archivo
  del agente de layout): queda para el coordinador quitarlo.

**Deudas / TODO (proximas olas)**:
- Constructor visual completo con drag and drop y paleta de controles (esta ola: grid+modal).
- Componentes de los tipos multimedia (Image, Photo, Audio, Signature, Gps, Button,
  Chart, GridDetail, Html) que ya existen en el enum como placeholder.
- Policy propia (ej. "Formularios.Disenar") en vez de TenantMember.
- Condiciones de gateway sobre datos del documento de respuesta (RulesEngine, ola 3).
- Consultas por valor de campo (indice GIN jsonb / OPENJSON) cuando haya reportes.
- Sin commit (pedido explicito): cambios en working tree.

## 2026-07-03 - Sesion 9: Alineacion visual al Prototipo Final ECOREX (shell del workspace)

**Objetivo**: cerrar las brechas visuales de la consola contra las capturas del
prototipo (01-inicio-resumen, 01b, 02-tableros): marca, rail de iconos, landing,
Inicio con datos reales, botones negros y modo oscuro. Solo UI de layout/estilo;
sin tocar DbContext, seeder, migraciones ni las paginas nuevas de Formularios
(en curso por otra sesion paralela).

**Cambios**:
- Marca: fuera el icono de avion y el subtitulo "CRM Conversacional". El header
  del sidebar del tenant ahora es el patron del prototipo: tile cuadrado oscuro
  con la inicial + nombre del tenant + "{plan} - ECOREX" (corregido el doble
  "Plan Plan": el nombre del plan ya incluye la palabra). El default de
  PlatformBranding paso a "ECOREX.tareas / Sistema de Tareas" con propuesta de
  valor de gestion de tareas (el branding guardado en BD se sigue respetando).
- Login: tagline nueva ("Gestiona tareas, proyectos, flujos BPMN, formularios y
  reglas configurables sin codigo...") e icono SVG de tablero/checklist; el
  wording de registro paso de "agencia" a "empresa" (el name del input se
  conserva por compatibilidad con /auth/register).
- Rail de iconos (doble panel del prototipo): nav vertical fija de 56px a la
  izquierda del sidebar con Inicio, Gestor de tareas, Flujos, Formularios,
  Anuncios (NavLink con tooltip) y avatar abajo; se oculta en <= 991px. Solo se
  muestra a usuarios con claim tenant_id.
- Landing post-login: /auth/login y el callback de Google ahora redirigen a
  /inicio para usuarios de tenant (operadores siguen yendo a "/").
- Inicio.razor con datos reales (InteractiveServer con prerender): KPIs de
  Tareas activas (ITaskItemService.ListAsync TotalCount con Pending/Active/
  InProgress), Proyectos en curso (IProjectService, estados no cerrados),
  Flujos ejecutandose (WorkflowInstances Running via IApplicationDbContext con
  query filter de tenant) y Alertas (tareas vencidas sin terminar, DueTo <=
  ahora). Boton negro "+ Nueva actividad" que abre el TaskWizard existente y
  panel "Mis tareas de hoy" (asignadas al usuario, vencimiento hoy o vencidas,
  max 5, link al gestor). Saludo contextual + linea "Tienes X tareas y Y
  alertas".
- Topbar del workspace: breadcrumb "Equipos / {tenant} / {seccion}" (seccion
  derivada de la ruta) + campana de notificaciones placeholder; el area
  PlatformAdmin conserva su chip "Operador (punto medio) usuario".
- Botones primarios NEGROS solo en el workspace del tenant: clase ws-tenant en
  el shell cuando hay tenant_id sin platform_role; PlatformAdmin sigue violeta.
- Modo oscuro: toggle luna en el pie del sidebar (JS plano, sin circuito),
  clase "dark" en <html> con persistencia en localStorage y script temprano en
  App.razor (sin FOUC). Re-mapa de tokens oklch + overrides puntuales para los
  grises fijos de tableros/kanban/modales. Cubre shell + inicio + actividades +
  proyectos; paginas heredadas quedan usables sin pulir cada detalle.

**Archivos**: Program.cs (redirects), PlatformBrandingService.cs (default),
Login.razor, NavMenu.razor, MainLayout.razor, Inicio.razor, App.razor, app.css,
.claude/launch.json (config superadmin-5236 para preview).

**Validacion (probado de verdad)**:
- dotnet build Ecorex.sln: 0 errores. Tests unitarios verdes (Domain 35,
  Application 26).
- Arrancado contra Postgres 5442 en http://localhost:5236 y verificado con
  HTTP real (curl con cookies) y navegador: login demo-admin@ecorex.tareas ->
  302 a /inicio; el HTML servido trae el header "S / SKY SYSTEM / Plan Empresa
  - ECOREX" (0 ocurrencias de "Plan Plan"), breadcrumb "Equipos / SKY SYSTEM /
  Resumen", rail con 5 iconos, clase ws-tenant y KPIs con numeros reales
  (5 tareas activas, 1 proyecto, 0 flujos, 0 alertas del seed actual).
  /actividades, /proyectos, /tableros y /anuncios responden 200 con el shell
  nuevo. En navegador: el wizard "Nueva actividad" abre desde Inicio
  (circuito interactivo OK), el toggle luna pone html.dark, persiste en
  localStorage y el boton primario invierte a claro (verificado via computed
  styles; el valor "congelado" inicial era la transition de Bootstrap en la
  pestana oculta del preview, no un bug de cascada).
- Bug encontrado y corregido en vivo: Npgsql rechaza DateTimeOffset con offset
  != 0 en timestamptz; los filtros DueTo de Inicio ahora usan UtcNow /
  ToUniversalTime().
- Procesos detenidos al terminar (puerto 5236 libre).

**Deudas / TODO**:
- Si la BD tiene fila de PlatformBranding con la tagline vieja del CRM, se
  muestra esa (se respeta el branding configurado); actualizarla desde Super
  Admin -> Marca si se quiere el texto nuevo.
- Panel "Alertas del sistema" de la captura 01 no implementado (solo KPI);
  contador de anuncios del sidebar sigue placeholder 0, campana sin dropdown.
- Rail sin badge de notificaciones; en movil se oculta (como se pidio).
- El KPI de flujos cuenta WorkflowInstance Running; el seed demo actual no
  deja instancias corriendo, por eso muestra 0.
- Sin commit (pedido explicito): cambios en working tree.

## 2026-07-03 - Sesion 10: FASE 4 ola 3 - RulesEngine (motor de reglas, ADR-0016)

**Objetivo**: portar cl_gestion_reglas (modulo legacy 000802) cerrando sus tres
agujeros: RCE por Activator.CreateInstance sobre nombres del XML -> registro TIPADO
de verbos en DI; modo Execute (SQL directo) -> PROHIBIDO; historial perdido (tabla
inexistente) -> RuleExecutionLog SIEMPRE, append-only, con TTL de 90 dias.

**Cambios**:
- Dominio: RuleDocument (DocumentCode unico por tenant, categoria, RuleStatus,
  IsArchived), Rule (VerbName = clave del registro tipado, ParamsJson jsonb/nvarchar,
  SortOrder, indice DocumentId+SortOrder), RuleExecutionLog (snapshot de nombre,
  TriggerKind Manual/FormField/WorkflowNode, ContextJson, Status Success/Failed/
  Skipped, RecordsAffected, DurationMs, ExpiresAt con indices TenantId+RuleId+
  CreatedAt y ExpiresAt), FormFieldRule (pregunta->regla, unico por par, FK regla
  NO ACTION) y WorkflowNodeRule (nodo->regla, IsAutonomous). Enums RuleStatus,
  RuleTriggerKind, RuleExecutionStatus (persistidos como texto).
- Motor (Ecorex.Application/Rules): IRuleVerb { Name, Descriptor, ExecuteAsync } con
  RuleVerbDescriptor tipado (port del protocolo PARAM_XML) para que la UI renderice
  la configuracion; RuleContext (params deserializados + FormData mutable + contexto
  de tarea/flujo/respuesta); RuleVerbResult con acciones de UI TIPADAS (HideField/
  ShowField/SetFieldValue/SetRequired) y AutoCompleteStep. RulesEngine: resolucion
  por diccionario (verbo desconocido = error tipado + fila Failed), Stopwatch,
  historial SIEMPRE con TTL; ExecuteForFormFieldAsync (SortOrder, propaga FormData
  entre reglas encadenadas) y ExecuteForWorkflowNodeAsync (solo autonomas;
  AutoComplete solo si TODAS exito y alguna lo pide). Verbos resueltos DIFERIDOS del
  IServiceProvider (rompe el ciclo WorkflowEngine->hook->engine->verbos->
  ITaskItemService->WorkflowEngine).
- Verbos iniciales (5): PASAR_CAMPOS, BLOQUEAR_CAMPO_XCONDICION (equals/notEquals/
  empty/notEmpty con efecto inverso al no cumplirse), ASIGNAR_CONSECUTIVO
  (ISequenceService + anotacion en TaskItemActivity), GENERAR_TAREAS_DESDE_TABLA
  (ITaskItemService, filas de campo tabla o params.rows), NOTIFICAR (intencion en
  TaskItemActivity; envio real de correo TODO integracion). IA/importacion
  (GENERAR_TABLAS_IA, IMPORTAR_CSV, DATA_SERVER*) documentados como extension futura.
- Integraciones: WorkflowRuleHook reemplaza al NoOp en DI (nodo Task -> reglas
  autonomas -> AutoComplete); DynamicFormRenderer usa IFormRuleDispatcher +
  FormRuleUiState (cambio minimo encapsulado): campos disparadores en una consulta
  por carga, acciones aplicadas al onchange, ocultos por regla NO se validan como
  requeridos y SetRequired hace override en cliente.
- UI /reglas (reemplaza el stub): 2 tabs como el legacy 000802 (Documento de
  configuracion / Historial), CRUD de documento (archivar, nunca DELETE), grid de
  reglas con modal (verbo desde el catalogo registrado con formulario de params
  generado del Descriptor, orden, estado, JSON tipado), boton "Ejecutar prueba"
  (contexto vacio Manual; muestra resultado + acciones y queda en historial), y
  vinculacion regla->pregunta (combo formulario->pregunta) y regla->nodo Task
  (combo flujo->nodo + autonoma) creando FormFieldRule/WorkflowNodeRule. El
  FormDesigner NO se toco.
- Worker: RuleLogTtlCleanupWorker (Ecorex.Workers, diario) via
  IRuleExecutionLogCleaner (IgnoreQueryFilters + ExecuteDelete: unico DELETE fisico
  permitido, log con TTL documentado).
- Seeder Development idempotente: documento "OPERACIONES DE FORMULARIOS" (RUL-005,
  FORMULARIOS, Active) para SKY SYSTEM con PASAR_CAMPOS (nombre_solicitante->
  descripcion, vinculada a la pregunta nombre_solicitante de FRM-001),
  BLOQUEAR_CAMPO_XCONDICION (prioridad=baja oculta fecha_requerida, vinculada a
  prioridad) y ASIGNAR_CONSECUTIVO autonoma en Task_Cotizacion de COT-COM (sin
  autoComplete a proposito: no se salta el formulario del paso).
- Migraciones DUALES AddRulesEngine (PG + SQL Server) aplicadas y verificadas en los
  contenedores dev (5442/1443): rule_documents, rules, rule_execution_logs,
  form_field_rules, workflow_node_rules.
- ADR docs/decisiones/0016-rules-engine.md.

**Validacion (probado de verdad)**:
- dotnet build Ecorex.sln: 0 errores.
- Unit: Domain 35 verdes; Application 83 verdes (25 nuevos: params validos/invalidos
  y acciones por verbo, catalogo tipado con FindVerb null para verbos desconocidos).
- Integracion DUAL (Testcontainers PG16 + SQL Server 2022): 5 tests nuevos x2
  motores (historial siempre con TTL ~90d incl. verbo no registrado tipado;
  PASAR_CAMPOS end-to-end cambia el FormData persistido; regla autonoma con
  AutoCompleteStep avanza el flujo a Task_B; aislamiento cross-tenant de documentos/
  reglas/historial; TTL cleaner borra solo expirados cross-tenant e idempotente).
  Suite completa de integracion verde: 77/77 (67 previos + 10 nuevos), 0 errores,
  en AMBOS motores.
- Arranque real contra PG 5442 en puerto 5237: /reglas responde con el documento
  RUL-005 sembrado y "Ejecutar prueba" genera entrada de historial.

**Deudas / TODO (proximas olas)**:
- Verbos IA/importacion del legacy (GENERAR_TABLAS_IA, IMPORTAR_CSV, DATA_SERVER*).
- Evaluacion en SERVIDOR de verbos puros al hacer submit (hoy la exencion
  oculto-por-regla => no-requerido es del renderer; ver limitacion en ADR-0016).
- Envio real de NOTIFICAR via IEmailSender (hoy deja la intencion en la actividad).
- Policy propia (ej. "Reglas.Editar") en vez de TenantMember; el chip "Pendiente
  Fase 4" del item Reglas en NavMenu.razor es del agente de layout (no se toco).
- ETL FASE 6: portar los 8 documentos / 21 reglas del legacy mapeando Ensamblado a
  verbos del catalogo.
- Sin commit (pedido explicito): cambios en working tree.

## 2026-07-03 - Sesion 9b: Fidelidad milimetrica contra el FUENTE del prototipo (ECOREX.dc.html)

**Objetivo**: segunda pasada obligatoria sobre la alineacion visual (commit 96e196c):
auditar token por token contra el fuente real del prototipo (ECOREX.dc.html +
SPA "ECOREX - Prototipo Final.html") y corregir toda diferencia. Gana el prototipo.

**Tokens extraidos del fuente y aplicados EXACTOS en app.css (:root y html.dark)**:
--bg/--surface/--surface-2/--surface-3, --ink/--ink-2/--ink-3, --line/--line-2,
--brand #1B1B1E (negro; dark #F4F4F5) / --brand-2 / --brand-soft / --on-brand /
--glow, paleta --t-blue/rose/green/amber/violet/slate con sus *-bg, --ok/--warn/
--danger, --glass + --glass-blur 14px, --sh-sm/md/lg, --rad 20px, --pad 30px.
Tipografia del prototipo: 'Hanken Grotesk' 400-800 (importada; aplicada al
workspace via .ws-tenant; el admin conserva Plus Jakarta Sans).

**Re-mapa**: .ws-tenant redefine las variables legacy (--background, --card,
--primary, --muted, --border, --shadow-*, etc.) hacia los tokens del prototipo,
asi kanban/modales/formularios heredan la paleta exacta sin tocar PlatformAdmin
(que no lleva la clase y queda intacto). El modulo de Formularios consume estas
variables con fallback, como se acordo.

**Correcciones dimensionales (fuente)**: rail 68px (botones 42x42 r12, activo
brand/on-brand como el SPA final y las capturas, hover ink + sombra, hueco de
38px arriba, avatar 34px con anillo line-2 y color AVPAL por iniciales), sidebar
272px, header del workspace con fila hover (padding 16/16/12 + row 8px r13, tile
36x36 r11 fondo var(--ink), nombre 14.5/700 -.01em, sub 11.5 ink-3 con punto
medio "Plan Empresa (0xB7) ECOREX" via &#183; en el markup), buscador 40px r12 surface-2
borde line-2, nav activo = surface-3 + ink (ya no violeta), labels 10.5/700
ls .1em, codigos 9.5 ink-3 .75, badge Anuncios fondo ink 18px r9, footer 12px
con boton de tema 32x32 r9, topbar 56px glass+blur con crumb 13px (sep line-2,
actual ink 600) y campana 36x36 r10 con punto danger.

**Dashboard /inicio reescrito al pixel del fuente**: contenedor max 1280 con
--pad, fecha 13 ink-3, h1 32/800 -.03em lh1.04, sub 14.5 con bolds ink, boton
negro 44px r13 con icono "+" (hover opacity .9), KPI cards r20 p20 sh-sm
(hover sh-md) con tile 38 r11 en t-violet/t-green/t-blue/t-rose, chip delta
("urgente" cuando hay alertas), valor 34/800, label 13 ink-2; paneles grid
1.65fr/1fr: "Mis tareas de hoy" (rows 14/22, dot 9px, titulo 14.5/600 + sub
12 ink-3, chip prioridad 11/600 r8 rose/amber/green, due 12.5 ancho 58) y
"Alertas del sistema" NUEVO (tareas vencidas reales, icono 32 r9 t-rose-bg,
chip "N nuevas"). Chips de prioridad del kanban tambien pasan a tokens t-*
(r8, sin uppercase) dentro del workspace.

**Bug real encontrado y corregido**: con prerender interactivo, /inicio
compartia el DbContext del request con NavMenu ("A second operation was
started on this context instance", reproducido en navegador). Inicio ahora
resuelve TODOS sus servicios en un scope propio (IServiceScopeFactory), igual
que NavMenu con branding; 5 cargas consecutivas de /inicio sin fallo.

**Validacion (probado de verdad)** contra Postgres 5442 en localhost:5236 con
demo-admin@ecorex.tareas: build 0 errores; tests unitarios verdes (Domain 35,
Application 58). Verificado por computed styles en navegador real:
--brand #1B1B1E, --surface #FFFFFF, --rad 20px, fuente Hanken Grotesk, rail 68
(btn 42 r12, activo #1B1B1E/blanco), sidebar 272, topbar 56, tile 36 fondo ink,
boton "Nueva actividad" 44/r13/#1B1B1E, h1 32/800, KPI r20/p20 valor 34/800
tile #EEE8FD+#7C3AED, paneles 537/326px (1.65:1), head 18/22, sub del sidebar
"Plan Empresa (punto medio) ECOREX", crumb "Equipos / SKY SYSTEM / ...".
DARK exacto: --bg #0A0A0B, surface #161618, ink/brand #F4F4F5, boton invertido
claro, tile invertido, chips t-amber dark rgba(240,174,60,.16)/#F0C46A en
/actividades, /proyectos y /formularios legibles en ambos temas; wizard abre
desde el boton nuevo; toggle luna persiste en localStorage. HTML servido (curl
con cookies): 302 a /inicio, KPIs 5/1/0/0, "Plan Empresa &#xB7; ECOREX".
Procesos detenidos al terminar (puerto 5236 libre).

**Lo que NO se igualo (honesto, con porque)**:
- Rail activo: el fuente .dc trae 44x44 r13 blanco+glow, pero el SPA final y
  las capturas aprobadas traen 42x42 r12 fondo brand (cuadro negro): se siguio
  el SPA/capturas por ser el ejecutable aprobado.
- El sidebar del prototipo agrupa MODULOS en acordeones por grupo de negocio
  (Mis Procesos/Negocio/Oferta...) con conteos; la consola mantiene su
  estructura actual de items planos con codigos (contenido, no estilo).
- Topbar: boton "Compartir" y toggle de sidebar del prototipo no implementados
  (sin funcion detras); la campana quedo como placeholder con punto.
- El buscador Cmd+K sigue deshabilitado (placeholder), muestra "Ctrl K" ASCII
  en lugar del glifo de comando (convencion solo-ASCII del repo).
- Los KPI no muestran deltas "+3/+1/2 en pausa" (no hay serie temporal aun);
  solo el chip "urgente" del KPI de alertas cuando aplica.
- Login: el prototipo no define pantalla de login; se conservo el diseno
  actual con la tagline/icono ya corregidos.
- Sin commit (pedido explicito); no se tocaron DbContext/seeder/migraciones ni
  los componentes de Formularios (solo heredan variables). Durante la sesion
  el arranque fallo dos veces por el modelo a mitad del agente paralelo
  (AddDynamicForms y AddRulesEngine); se resolvio recompilando cuando sus
  migraciones quedaron en el arbol.

## 2026-07-03 - Sesion 9c: Gaps estructurales del prototipo (acordeones, Modulos, badges, topbar, rail)

**Objetivo**: cerrar los gaps estructurales que quedaron listados en 9b como
"no igualado": sidebar con acordeones, seccion Modulos del dashboard, deltas
de los KPI, toggle de colapso + Compartir en el topbar y railDeco.

**1. Sidebar con acordeones (NavMenu.razor + app.css)**: la seccion MODULOS
ahora replica los menuGroups del fuente (ECOREX.dc.html linea 116+): header
9/10 r10 13.5/600 ink-2 con icono coloreado (t-violet/t-blue/t-slate, stroke
1.85 como I() del fuente), contador 10px ink-3, chevron 15px sw2.2 rotado
180 grados al abrir (transition .2s); items indentados margin 1/0/5/19 +
padding-left 11 + borde izquierdo var(--line), 7/10 r8 12.5 (activo
surface-3 + ink + 600, inactivo ink-2 500) con codigo legacy 9.5 ink-3 .75.
Grupos SOLO con lo que existe hoy: Mis Procesos (2: Proyectos 000042,
Administrar actividades 000636), Automatizacion (3: Flujos 000291,
Formularios 000131, Reglas 000802), Sistema (2: Dependencias 000850,
Modulos web 000109) y CRM (heredado) (9 paginas del CRM, sin codigo).
Implementados como <details data-acc> (funcionan en SSR estatico): el
servidor decide el estado inicial (abierto si contiene la ruta activa o si
es misproc/auto, los defaults del prototipo) y un script en MainLayout
persiste el toggle del usuario en localStorage ('ecorex-acc') y lo reaplica
tras cada enhanced navigation con MutationObserver (sin cerrar nunca el
grupo de la ruta activa). Se elimino el CSS del viejo .ecorex-nav-group
(sin usos) y el label "Principal" (el fuente no lo tiene: quick nav suelto).
Quick nav del workspace subido a 14px/600 margin 2 (wsStyle exacto).

**2. Seccion "Modulos" en /inicio (Inicio.razor + app.css)**: h2 20/800 +
sub 13 ink-3, headers de categoria con punto 8x8 r3 + nombre 13/700 .03em +
desc 12.5 ink-3, grid auto-fill minmax(280px,1fr) gap 14, cards r16 p18
sh-sm hover translateY(-2px)+sh-md con tile 40 r12 (bg/color del tono de la
seccion), titulo 15/700, desc 12.5 ink-2 lh1.5 min-h 38 y pie border-top
"Ir al modulo ->" 12.5/600 + codigo 11 ink-3 .04em. Areas REALES:
OPERACIONES t-violet (Proyectos /proyectos 000042, Administrar actividades
/actividades 000636, Gestor de tareas /tableros sin codigo) y AUTOMATIZACION
t-blue (Flujos /flujos 000291, Formularios /formularios 000131, Reglas
/reglas 000802). Textos del fuente adaptados a ASCII.

**3. Badges de los KPI (Inicio.razor)**: deltas calculados con datos reales
(3 queries por CreatedAt/Status en el scope propio de la pagina): tareas
creadas hoy "+N" (tg), proyectos creados este mes "+N" (tg), instancias de
flujo Stuck "N en pausa" (ta, nueva clase .dash-kpi-delta.ta t-amber) y el
"urgente" (tr) de 9b. Valor 0 => el badge no se renderiza, como el prototipo.

**4. Topbar (MainLayout.razor + App.razor + app.css)**: boton de colapso del
sidebar 34x34 r9 borde line con el icono de panel del fuente, FUNCIONAL en
SSR: window.ecorexSidebar (App.razor, corre antes del CSS para no parpadear)
conmuta la clase html.sidebar-collapsed con persistencia en localStorage
('ecorex-sidebar'); CSS solo escritorio (>=992px): sidebar a width 0 +
opacity 0 con transition .2s como el prototipo (wsW 0px). Boton "Compartir"
h36 r10 borde line-2 13/600 con icono share 15px, deshabilitado (opacity
.55) con tooltip "Proximamente".

**5. Rail (MainLayout.razor)**: bloque inferior railDeco con los destinos
que existen: mensajes -> /conversaciones, indicadores (chart) -> /metricas
y la campana -> /anuncios (movida abajo como el fuente); arriba quedan
Inicio/Tareas/Flujos/Formularios. Calendario NO se agrego (no hay pagina).
Botones 42x42 r12 (SPA/capturas) sin cambios.

**Validacion (probado de verdad)** contra Postgres 5442 en localhost:5238
con demo-admin@ecorex.tareas: build 0 errores, tests Domain 35/35 y
Application 83/83 verdes. En navegador real (1440x900 y 1280x860):
acordeones con medidas computadas exactas (summary 13.5/600 pad 9/10 r10,
body margin 1/0/5/19 + borde line, item 12.5 pad 7/10 r8), toggle de
Sistema persiste {"sistema":true} y sobrevive navegaciones, item activo
/flujos con surface-3 #F2F2F3 + 600 y rail activo #1B1B1E, breadcrumb
actualizado; colapso del sidebar: click -> width 0/opacity 0 + localStorage
'collapsed', reload -> sigue colapsado, click -> 272px; seccion Modulos
renderiza las 2 categorias y las 6 cards navegan (click Proyectos ->
/proyectos); badges reales "+6" y "+1" (verde #DDF6E6/#16A34A r8) y ocultos
los de valor 0 (flujos en pausa, urgente); dark mode legible (card #161618,
delta rgba verde .16); /dependencias /modulos-web /metricas /conversaciones
/anuncios responden 200. Procesos detenidos al terminar (puerto 5238 libre).

**Lo que NO se igualo (honesto, con porque)**:
- Grupos del prototipo sin contenido real (Negocio, Oferta-Catalogo,
  Sistema-Inventarios/Actividades/CRM/General/Desarrollo completos) e items
  sin pagina (Crear una actividad 000038 como pagina propia, Programar
  actividad 000889, Comercial 000477 con subgrupo, Power BI 000788, Agentes
  IA 000867 del workspace): NO se inventaron, por instruccion.
- La seccion Modulos del dashboard omite las categorias NEGOCIO del fuente
  (Creacion/Seguimiento de clientes, Items-Inventarios): no existen esas
  paginas en ECOREX hoy.
- El rail no lleva Calendario (sin destino) y conserva 42x42 r12 del
  SPA/capturas aprobadas (el .dc trae 44x44 r13).
- "Compartir" es placeholder deshabilitado (sin funcion detras) y la campana
  sigue siendo placeholder con punto.
- Los deltas "+N" usan CreatedAt del dia/mes actual (no hay serie temporal
  historica); "en pausa" cuenta instancias Stuck reales.
- Durante la sesion el arranque fallo una vez por el modelo a mitad del
  agente paralelo (AddOrgAndModuleRegistry); se resolvio recompilando cuando
  sus migraciones quedaron en el arbol. No se tocaron Dependencias.razor,
  ModulosWeb.razor ni Domain/Application/migraciones (agente paralelo).
- Sin commit (pedido explicito): cambios en working tree. Se agrego la
  configuracion superadmin-5238 a .claude/launch.json para la verificacion.

## 2026-07-03 - Sesion 11: FASE 5 - Dependencias (000850) y Modulos web (000109) (ADR-0017)

**Objetivo**: los dos modulos de sistema del vault: organigrama del tenant
(Dependencias) y module registry global (Modulos web), con migraciones duales,
seeders demo, tests en matriz dual y UI segun las capturas del prototipo
(04-dependencias-organigrama, 04b-dependencias-detalle, 05-modulos-web-registro).

**Hecho**:
- Dominio: OrgUnit (TenantEntity: Name 150, Kind enum OrgUnitKind Area/Team,
  ParentId self-FK NO ACTION, ResponsibleTenantUserId?, Description? 600,
  SortOrder, IsArchived) y OrgUnitMember (FK cascade a la unidad, unico
  (OrgUnitId, TenantUserId), Role? 100). ModuleDefinition GLOBAL de plataforma
  (LegacyCode 6 digitos unico, Name, Description?, Route?, Area enum ModuleArea
  Principal/Operaciones/Automatizacion/Sistema/Crm, IsCore; SIN TenantId y SIN
  HasQueryFilter, justificado en ADR-0017: es catalogo, duplicarlo por tenant
  reintroduce la desincronizacion del legacy) y TenantModule (TenantEntity:
  FK Restrict al catalogo, IsEnabled, SettingsJson jsonb/nvarchar dual, unico
  (TenantId, ModuleDefinitionId)).
- IOrgUnitService (Application/Organization): GetTreeAsync (arbol anidado
  ordenado por SortOrder+nombre, raices = sin padre o padre no visible),
  ListAsync, GetAsync, GetKpisAsync (Dependencias / Usuarios distintos
  (miembros+responsables de unidades activas) / Areas), Create/Update con
  VALIDACION DE CICLOS (OrgUnitTree.WouldCreateCycle, funcion PURA con set de
  visitados: arbol corrupto = ciclo, fail-closed), SetArchived (soft-delete;
  bloqueado con hijas activas), Add/RemoveMember. Resultados tipados
  OrgResult<T> (patron ADR-0013/0016).
- IModuleRegistryService (Application/Modules): ListCatalogAsync (catalogo +
  estado del tenant activo; sin fila = deshabilitado), UpsertDefinitionAsync
  (SOLO PlatformAdmin, lo usa el seeder), SetModuleEnabledAsync (IsCore no se
  puede deshabilitar), UpdateSettingsAsync (valida objeto JSON),
  GetEnabledModulesAsync(tenantId) fail-closed (tenant ambiente distinto =>
  vacio; sin ambiente = plataforma) pensado para derivar el menu del registry
  (TODO policies por modulo documentado en la interfaz y el ADR).
- Migraciones DUALES AddOrgAndModuleRegistry (PG + SQL Server) aplicadas y
  verificadas en los contenedores dev (5442/1443): org_units, org_unit_members,
  module_definitions (unico legacy_code), tenant_modules (unico tenant+modulo,
  settings_json jsonb/nvarchar(max)).
- Seeders Development idempotentes: organigrama demo de 5 unidades para SKY
  SYSTEM (Direccion General > Comercial / Tecnologia > Desarrollo / Gestion
  Humana; owner responsable de la raiz y miembro; en la base dev actual el
  tenant demo solo tiene demo-admin@ecorex.tareas, el fallback por rol Owner lo
  resolvio) y catalogo global de 11 modulos reales (000038, 000042, 000636,
  000889, 000291, 000131, 000802, 000850, 000109, 000788 y 000867 placeholders
  sin ruta) TODOS habilitados para SKY SYSTEM.
- UI /dependencias (reemplaza el stub): cabecera modulo 000850, KPIs
  Dependencias/Usuarios/Areas, organigrama como arbol de cards CSS puro
  (chevron expandir/contraer, dot por tipo, avatar del responsable por
  iniciales, contador de miembros, boton + para sub-dependencia al hover),
  panel de detalle (ruta de ancestros, chips tipo/estado, responsable,
  miembros add/remove con rol, editar, archivar/restaurar) y modal de
  alta/edicion (nombre, tipo, padre, responsable, orden, descripcion). Estilos
  propios de la pagina con tokens del prototipo (--surface/--ink/--line/--t-*)
  y fallback a variables legacy; app.css NO se toco (agente paralelo).
- UI /modulos-web (reemplaza el stub): KPIs registrados/activos/nucleo, tabla
  del catalogo (codigo legacy, nombre+descripcion, chip de area, ruta, toggle
  de estado por tenant, settings), toggle solo para owner/admin del tenant
  (claim tenant_role) o platform_role, candado visual en modulos nucleo
  habilitados, modal de settings JSON con textarea validada (objeto JSON o
  vacio; el error del parser se muestra tipado).
- ADR docs/decisiones/0017-org-y-module-registry.md.

**Validacion (probado de verdad)**:
- dotnet build Ecorex.sln: 0 errores.
- Unit: Domain 35 verdes; Application 91 verdes (8 nuevos de OrgUnitTree:
  raiz, hermano valido, auto-referencia, hijo directo, descendiente profundo,
  re-colgar hacia ancestro valido, ciclo preexistente corrupto, padre fuera
  del mapa).
- Integracion DUAL (Testcontainers PG16 + SQL Server 2022): 4 tests nuevos x2
  motores (arbol CRUD + miembros + KPIs + ciclo y auto-referencia rechazados +
  archivado bloqueado con hijas y sin DELETE fisico; aislamiento cross-tenant
  de OrgUnit/OrgUnitMember/TenantModule incl. sin-tenant fail-closed; catalogo
  ModuleDefinition visible desde AMBOS tenants con estado y settings aislados
  y GetEnabledModulesAsync fail-closed; habilitar/deshabilitar por tenant con
  proteccion IsCore y NotFound tipado). Suite completa de integracion verde:
  85/85 (77 previos + 8 nuevos) en AMBOS motores.
- Dos bugs de traduccion LINQ (OrderBy sobre el DTO proyectado en
  GetEnabledModulesAsync y ListMembersAsync) encontrados por el test dual y el
  navegador real; corregidos ordenando antes de proyectar y cubiertos con
  asserts nuevos.
- Arranque real contra PG 5442 en puerto 5239 (navegador, login
  demo-admin@ecorex.tareas): /dependencias muestra KPIs 5/1/4 y el arbol demo;
  clic en nodo abre el detalle (responsable con avatar, miembro con rol);
  "+ Nueva dependencia" crea "Calidad" bajo Tecnologia (KPI pasa a 6, ruta
  "Direccion General > Tecnologia") y se archiva (KPI vuelve a 5, fila queda
  is_archived=t en BD). /modulos-web muestra 11/11/3; toggle de Flujos 000291
  apaga (Activos 10, is_enabled=f en BD) y enciende de nuevo; el toggle de un
  nucleo (000038) esta bloqueado con tooltip; settings de 000850 rechaza
  "esto no es json" con error tipado y persiste {"maxNiveles":4,...} en jsonb.
  Sin errores de consola ni de circuito. Proceso detenido al terminar.

**Deudas / TODO (proximas olas)**:
- Menu de la consola derivado de GetEnabledModulesAsync + policies por modulo
  (ej. "Modulo.000850.Usar"); hoy el NavMenu sigue estatico y las paginas bajo
  TenantMember.
- UI de administracion del catalogo global para PlatformAdmin (hoy solo seeder
  + UpsertDefinitionAsync listo con la exigencia de policy documentada).
- ETL FASE 6: portar las dependencias reales del tenant 01 (BITCODE) y el
  registro de modulos del legacy.
- Los KPIs del prototipo muestran una 4ta card cortada (carrusel); se
  implementaron las 3 principales.
- Sin commit (pedido explicito): cambios en working tree. Se agrego la
  configuracion superadmin-5239 a .claude/launch.json para la verificacion.

---

## 2026-07-03 - Sesion 12: FASE 7 ola 1 - CI en GitHub Actions (pr-check, ADR-0018)

**Agentes**: Claude Code (Fable 5).

**Hecho**:
- `.github/workflows/pr-check.yml` (nuevo; el backbone NO trajo `.github/`,
  no habia workflows de Railway que deshabilitar): triggers `pull_request`
  a main + `push` a main y `fase-0/**`; concurrency que cancela corridas
  previas de la misma rama; job unico `build-test` en ubuntu-latest con
  timeout de 30 min y pasos: gitleaks (gate de secretos, historia completa
  con fetch-depth 0), setup-dotnet 10.0.x, restore, build Release
  (solo errores bloquean; 4 warnings heredados), dotnet format
  --verify-no-changes (informativo por ahora, ver TODO), tests unitarios
  Domain + Application, tests de integracion (matriz DUAL via
  Testcontainers DENTRO del runner, sin `services:`) y resumen de .trx
  con dorny/test-reporter.
- ADR `docs/decisiones/0018-ci-github-actions.md`: que corre, que bloquea
  el merge y por que Testcontainers en el runner y no `services:` (la
  matriz vive en los fixtures de los tests, misma config que produccion).
- CLAUDE.md checklist: linea nueva con los gates que corre el CI en PR.

**Validacion (local; NO se hizo push, el workflow queda por estrenar)**:
- YAML validado con parser (PyYAML): sintaxis OK, 9 pasos.
- Comandos medidos tal cual en local: restore 6 s; build Release 45 s
  (0 errores, 4 warnings); dotnet format --verify-no-changes FALLA hoy
  (rc=2, 162 s): 33 errores WHITESPACE heredados en LeadService.cs,
  FormDefinitionService.cs, ChatService.cs (Application) y Program.cs
  (SuperAdmin) -> por eso el paso va con continue-on-error: true y TODO;
  unit tests 12 s (Domain 35 + Application 91 verdes); integracion dual
  217 s (85/85 verdes, PG16 + SQL Server 2022 via Testcontainers). Total
  local ~7.5 min; estimado en Actions 12-18 min (descarga de imagenes +
  runner mas lento), bajo el timeout de 30.
- Sin variables TESTCONTAINERS_*: en ubuntu-latest el daemon Docker es
  local y Ryuk funciona sin configuracion extra.

**Deudas / TODO**:
- Sanear los 33 errores de whitespace con `dotnet format Ecorex.sln` en un
  commit propio y quitar el continue-on-error (volver el paso gate).
- Subir el build a `-warnaserror` cuando se saneen los warnings heredados.
- Blue/green (deploy) queda para la siguiente ola de FASE 7.
- Sin commit (pedido explicito): todo en working tree; probar el workflow
  en el primer push/PR real.

---

## 2026-07-03 - Sesion 13: Cierre de 3 deudas del nucleo de tareas (archivar, paginador, policies)

**Agentes**: Claude Code (Fable 5). En paralelo otro agente creo tests/Ecorex.E2E.Tests
(proyecto nuevo) y docs; esta sesion no toco ese proyecto ni el .sln.

**Hecho**:
- ARCHIVAR TAREA: `ArchiveAsync`/`RestoreAsync` en ITaskItemService/TaskItemService
  (resultado tipado TaskCoreResult, registra TaskItemActivity "archivo la tarea" /
  "restauro la tarea", Conflict tipado ante DbUpdateConcurrencyException). Decision
  documentada en la interfaz: archivar SI se permite sobre tareas Closed (el archivado
  es visibilidad -IsArchived-, NO transicion de la maquina de estados; la solo-lectura
  de Closed aplica a la edicion de contenido). Doble archivado / restaurar no archivada
  -> Invalid tipado. UI: boton Archivar del TaskDetailModal habilitado con confirmacion
  inline ("Archivar la tarea?"), y boton Restaurar cuando la tarea esta archivada
  (badge "Archivada" en el hero ya existia).
- VISTA LISTA de /actividades (TaskKanban): toggle "Ver archivadas" (solo lista; el
  kanban NUNCA incluye archivadas: IncludeArchived solo se envia en vista lista),
  badge "Archivada" junto al estado y accion Restaurar por fila (columna visible con
  el toggle). ListAsync ya tenia el filtro IncludeArchived en TaskItemListFilter.
- PAGINADOR server-side real de la vista Lista: usa TotalCount/Page/PageSize que
  ListAsync ya devolvia. Controles Anterior/Siguiente + "N actividades - pagina X de Y"
  + selector de tamano 25/50/100 (default 25), estilos `.tk-pager` con tokens del
  prototipo (var(--card)/var(--border)/var(--muted-foreground)). Cambio de filtros o
  de tamano vuelve a pagina 1; si la pagina queda fuera de rango (ej. se archivo el
  ultimo item de la ultima pagina) se reubica en la ultima valida. El kanban conserva
  su carga de 200 (KanbanPageSize); se elimino el aviso "Mostrando X de Y".
- POLICIES POR MODULO (paso 1 del plan, nombres estables): en Program.cs del SuperAdmin
  se definieron "Tareas.Ver", "Proyectos.Ver", "Flujos.Ver", "Formularios.Disenar",
  "Reglas.Editar", "Dependencias.Ver" y "ModulosWeb.Administrar", HOY con el mismo
  requisito que TenantMember (RequireClaim tenant_id): mismo efecto neto, cero cambio
  de acceso. Aplicadas reemplazando [Authorize(Policy="TenantMember")] en: Actividades
  (Tareas.Ver), Proyectos y ProyectoDetalle (Proyectos.Ver), Flujos (Flujos.Ver),
  Formularios y FormDesigner (Formularios.Disenar), Reglas (Reglas.Editar),
  Dependencias (Dependencias.Ver) y ModulosWeb (ModulosWeb.Administrar). TODO
  documentado en Program.cs: paso 2 = derivar el requisito real desde el Module
  Registry sin tocar las paginas. Inicio/Tableros/etc. siguen en TenantMember.
- Tests: FakeTaskItemService de RuleVerbTests implementa los 2 metodos nuevos.
  TaskCoreTests (dual PG/SQL Server) sumo 2 tests: ArchiveAndRestore_ToggleList
  Visibility_AndRecordActivity (desaparece de ListAsync por defecto, aparece con
  IncludeArchived, restaurar la devuelve, doble archivado/restauro invalido, traza
  en TaskItemActivity) y Archive_OnClosedTask_IsAllowed (cerrada se archiva y
  restaura conservando Closed).

**Validacion**:
- dotnet build Ecorex.sln: 0 errores (6 warnings heredados). Tests unitarios verdes:
  Domain 35/35, Application 91/91. Filtro TaskCore dual completo: 12/12 verdes
  (6 tests x PG16 + SQL Server 2022 via Testcontainers, 17 s).
- Arranque real contra Postgres 5442 en http://localhost:5241 (config nueva
  superadmin-5241 en .claude/launch.json), login demo-admin@ecorex.tareas:
  archivar T00006 desde el detalle (confirmacion inline, badge Archivada, actividad
  "archivo la tarea", boton pasa a Restaurar); la tarea sale de la lista (5) y del
  kanban (5 tarjetas); toggle "Ver archivadas" la muestra con badge + Restaurar y
  al restaurar vuelve normal (6). Paginador probado con PageSize temporal 5 y 6
  tareas: "pagina 1 de 2" (5 filas), Siguiente -> pagina 2 de 2 (1 fila, boton
  deshabilitado), selector a 25 -> pagina 1 de 1 con 6 filas; luego se restauro el
  default 25 y se recompilo. Las 7 rutas con policy nueva responden 200 sin redirect
  a /login con demo-admin. Sin errores de consola. Proceso detenido al terminar.

**Deudas / TODO**:
- Paso 2 de policies: derivar requisitos desde el Module Registry (solo Program.cs).
- El paginador aplica a la vista Lista; el kanban sigue topado a 200 por columna-fuente.
- Sin commit (pedido explicito): cambios en working tree.

---

## 2026-07-03 - Sesion 13: Suite E2E con Playwright para .NET (ADR-0019)

**Agentes**: agente unico de la capa E2E (en paralelo OTRO agente trabajo
TaskItemService/paginas de tareas; esta sesion NO toco codigo de producto:
solo el proyecto de tests nuevo y documentacion).

**Hecho**:
- Proyecto nuevo `apps/backend/tests/Ecorex.E2E.Tests` (net10.0, xunit +
  Microsoft.Playwright 1.49 + Xunit.SkippableFact) agregado a Ecorex.sln, con
  README (instalacion de Chromium via `pwsh bin/Debug/net10.0/playwright.ps1
  install chromium`, variables y modos de arranque).
- Fixture `E2eAppFixture` (coleccion xunit `e2e`, secuencial): estrategia BASE
  por `ECOREX_E2E_BASEURL` (si esta definida usa esa app; si no responde /login
  200, la suite entera se SALTA con motivo, nunca rojo por entorno) + arranque
  automatico local como conveniencia (`dotnet run --no-build` de SuperAdmin en
  puerto libre 5250+, Development, `ECOREX_DB_CONNECTION` a PG 5442, espera
  /login 200 hasta 120 s, kill del arbol de procesos al terminar).
- 7 escenarios, cada uno con contexto de navegador propio y datos con sufijo
  unico por corrida: (a) login demo -> /inicio con saludo y 4 KPIs; (b) wizard
  3 pasos -> toast "Actividad T##### creada." + tarjeta en Pendiente; (c) cambio
  de estado Pendiente->Activa por el dropdown del DETALLE verificando que solo
  ofrece transiciones validas (drag and drop nativo descartado por fragil, ver
  ADR-0019); (d) worklog manual 0:30 con nota -> total "30m" + historial;
  (e) flujo demo COT-COM: crear tipo "Direccion Comercial/Cotizacion" (tarea
  nace Activa), completar "Requerimiento" via WorkflowEngine (backdoor
  documentado `E2eDbBackdoor`: ese paso no tiene UI, bandeja de pasos = deuda
  ADR-0014), seccion "Formularios del paso" con FRM-001 Pendiente, diligenciar
  y Enviar -> el motor avanza a Gateway_Aprobacion; (f) emitir URL publica
  single-use de FRM-001 desde el disenador, enviar en contexto ANONIMO ->
  pantalla de gracias, reuso -> mensaje neutro; (g) aislamiento con
  owner@sky-system.local -> SKIP explicativo (la BD dev solo tiene
  demo-admin@ecorex.tareas; con BD recien sembrada corre completo).
- ADR `docs/decisiones/0019-e2e-playwright.md`: alcance, estrategia
  baseurl/skip, por que dropdown en vez de drag and drop, backdoor del motor y
  PLAN de CI como job aparte de pr-check.yml (el yml NO se toco en esta sesion).

**Validacion (probado de verdad, suite completa contra la app local real)**:
- dotnet build Ecorex.sln: 0 errores. Suites existentes sin tocar.
- Corrida 1 (fixture auto-arranque): 6 PASS + 1 SKIP (escenario g), 49 s de
  tests (~74 s con el arranque de la app). Corrida 2 de estabilidad: igual.
- Escenarios a-f: PASS. Escenario g: SKIP con motivo (seed dev anterior sin
  owner@sky-system.local).

**Hallazgos de producto (la suite los destapo; NO se toco producto)**:
- BUG: dos @onchange rapidos sobre campos con reglas (FRM-001:
  nombre_solicitante/prioridad, RUL-005) tumban el circuito Blazor con
  "A second operation was started on this context instance"
  (DynamicFormRenderer.DispatchFieldRulesAsync usa el DbContext scoped del
  circuito sin aislar el scope, a diferencia de TaskKanban.ReloadAsync);
  el boton Enviar muere en silencio. Reproducible a velocidad Playwright y
  tabulando rapido. Mitigado en la suite (PublicFormFiller: orden + espera
  deterministica por la copia de PASAR_CAMPOS + pausa fija); fix real
  propuesto como tarea aparte.
- Los inputs del renderer/wizard usan @onchange: Playwright FillAsync solo
  dispara "input", hay que hacer blur explicito para que el servidor vea el
  valor (documentado en los helpers).

**Deudas / TODO**:
- Integrar la suite como job `e2e` NO bloqueante en pr-check.yml (plan en
  ADR-0019); promover a gate cuando se mida estabilidad en Actions.
- Reemplazar el backdoor del paso Requerimiento por la interaccion real cuando
  exista la bandeja de pasos del flujo (ADR-0014).
- Dos esperas fijas en PublicFormFiller (radio con regla sin efecto visible):
  quitarlas cuando el producto arregle la carrera del dispatcher de reglas.
- Escenario g completo exige resembrar la BD dev (docker compose down -v).
- Sin commit (pedido explicito): cambios en working tree.

---

## 2026-07-04 - Sesion 14: Tableros de actividades unificados - BACKEND (ADR-0020)

**Agentes**: agente unico backend (la UI del gestor de tableros es de OTRA ola).

**Hecho**:
- Modelo (ADR-0020): TaskBoard EXTENDIDO sin romper el CRM heredado (Code nullable 20
  unico por tenant con indice filtrado, Status enum TaskBoardStatus OnTime/InProgress/
  AtRisk/Completed default InProgress, DueDate, Kind enum TaskBoardKind CrmLegacy=0/
  Activities=1 default CrmLegacy). TaskItem gana BoardId/ColumnId (FKs NO ACTION a
  TaskBoard/TaskBoardColumn, coherencia columna-pertenece-al-board validada en
  Application), BoardSortOrder y StartDate (Gantt). NUEVAS TaskItemChecklistItem
  (Text 500, IsCompleted, CompletedAt/CompletedByTenantUserId informativo sin FK dura,
  indice TaskItemId+SortOrder, cascade) y TaskItemAssignment (asignados M:N del equipo,
  unico TaskItemId+TenantUserId, cascade; el ENCARGADO single AssigneeTenantUserId se
  conserva como responsable). Ambas TenantEntity con query filter global automatico.
- Servicios: NUEVO IActivityBoardService/ActivityBoardService (resultados tipados
  TaskCoreResult) que opera SOLO Kind=Activities: CRUD de tableros con Code autogenerado
  via TenantSequence "PRY" (PRY-####, padding 4) y columnas default del prototipo
  (reusa TaskBoardService.DefaultColumns, ahora internal); ListBoardsAsync (indice) con
  progreso % (checklist completado/total, fallback tareas en columna IsDone/total),
  miembros distintos con iniciales, conteo de tareas, KPIs globales (tableros, tareas,
  completadas=en columna IsDone, en riesgo=tableros AtRisk O con tareas vencidas -
  decision documentada) y filtros server-side (miembro/tag/tipo sobre las tareas, rango
  de fechas sobre el DueDate del tablero); GetBoardDetailAsync con filtros combinables
  AND en SQL (columnas[], asignados[] encargado-O-assignment, fechaLimite hoy/manana/
  con-fecha con corte de dia UTC, tags[], alcance team/mine/unassigned) y CONTADORES por
  alcance calculados con los demas filtros aplicados; MoveTaskAsync (valida columna del
  mismo board, transicion OPORTUNISTA a Done solo si TaskItemStateMachine lo permite,
  si no mueve la tarjeta sin tocar el estado y lo reporta en StatusNote, registra
  TaskItemActivity); AddTaskToBoard/RemoveFromBoard; QuickCreateTaskAsync que DELEGA en
  TaskItemService.CreateAsync (consecutivo T + tags + actividad + flujo del tipo en UNA
  transaccion) con la tarea ya colgada del board/columna (CreateTaskItemRequest gano
  StartDate/BoardId/ColumnId opcionales; tipo default = primer ActivityType activo).
- ITaskItemService ampliado: checklist (add/toggle con actividad al completar/remove/
  reorder), asignados M:N (add/remove con actividad), StartDate en create/update,
  GetDetailAsync incluye checklist + asignados (TaskItemDetailDto), summary expone
  StartDate/BoardId/ColumnId. DI: IActivityBoardService registrado.
- Logica pura extraida a ActivityBoardCalculations (BoardProgressPct, Pct, DueRangeUtc)
  + MemberInitials, con 24 unit tests nuevos (Application.Tests 115 verdes).
- Migracion dual AddActivityBoards (PG 20260704115154 / SQL Server 20260704115221)
  aplicada y VERIFICADA en los contenedores dev (PG 5442 y MSSQL 1443): columnas nuevas
  en task_boards/task_items y tablas task_item_checklist_items/task_item_assignments
  con sus FKs e indices. Sin rutas multiples de cascada en SQL Server (FKs de tablero
  NO ACTION; no hizo falta ClientCascade).
- Seeder Development idempotente EnsureActivityBoardsDemoAsync (SuperAdmin Program.cs):
  tablero PRY-0042 "Comercial - Requerimiento Infraestructura" (InProgress, vence
  2026-07-12, descripcion del prototipo, columnas default) con 10 tareas del prototipo
  (Cotizar equipos de red 0/4 tag Infraestructura due 1-jul; Migrar formulario a EAV 3/4
  due hoy; Aprobar cotizacion de proveedor 4/4 due hoy; Configurar consecutivo 0D7
  Completado due 6-jul; etc.), tags Infraestructura azul/Comercial rosa/Proyecto medio
  verde, encargados/asignados repartidos entre owner/admin/operator/viewer, 1 tarea sin
  asignar y 3 del owner; + 2 tableros simples (PRY-0040 OnTime, PRY-0041 AtRisk) para el
  KPI "3 Tableros". Secuencias coherentes (PRY=43, T05 continua).
- ADR nuevo docs/decisiones/0020-tableros-actividades-unificados.md.

**Validacion (probado de verdad)**:
- dotnet build Ecorex.sln: 0 errores (warnings heredados). Domain 35/35,
  Application 115/115 (91 previos + 24 nuevos).
- Integracion NUEVA ActivityBoardTests en matriz dual (fixtures TenantIsolation,
  Testcontainers PG16 + SQL Server 2022): 12/12 verdes (6 tests x 2 motores, 38 s):
  (1) board Activities con code PRY-0001/0002 autogenerado + columnas default + code
  explicito y duplicado Invalid; (2) QuickCreate cuelga con T00001/T00002 unicos,
  BoardSortOrder 0/1 y columna ajena Invalid; (3) filtros del detalle por columna /
  asignado (encargado Y assignment M:N) / tag / fecha hoy + alcances con contadores
  team 4 / mine 2 / unassigned 1 y combinacion AND; (4) MoveTask a columna IsDone:
  Active->Done aplicado con actividad, Pending NO rompe (mueve, estado intacto,
  StatusNote); (5) checklist toggle actualiza progreso de tarjeta (1/2=50%) y del
  indice (50%), con CompletedBy/At y actividad, y destoggle limpia; (6) aislamiento
  cross-tenant de boards/checklist/assignments (indice vacio, detalle NotFound, move
  NotFound, DbSets vacios). Suite de INTEGRACION COMPLETA: 101/101 verdes (2 m 6 s;
  una corrida previa dio 6 rojos por flake de arranque del contenedor MsSql de
  Testcontainers bajo carga -la app dev y el seed corrian en paralelo-, la corrida
  limpia paso entera).
- Seed verificado por consulta directa en PG 5442 tras arrancar SuperAdmin real:
  3 tableros Activities (PRY-0042/0040/0041 con estados InProgress/OnTime/AtRisk),
  PRY-0042 con 10 tareas T00010..T00019 repartidas en las 4 columnas con checklists
  0/4, 3/4, 4/4, 0/3 y 1/2, y contadores de alcance del owner team=10 / mine=3 /
  unassigned=1 (query directa). Segundo arranque: sigue 3/10 (idempotente).

**Decisiones**:
- Kind en TaskBoard (no entidades nuevas) para no romper el CRM heredado.
- Columna != estado: transicion oportunista a Done SOLO si la maquina la permite;
  mover fuera de IsDone no reabre.
- FKs tarea->tablero/columna NO ACTION: borrar tablero desacopla tareas primero
  (las actividades nunca mueren con el tablero).
- KPI "en riesgo" = tableros AtRisk O con tareas vencidas fuera de columna final.
- Corte de dia de los filtros hoy/manana en UTC (zona del tenant = deuda ola UI).
- QuickCreate sin tipo usa el primer ActivityType activo del tenant.

**Deudas / TODO**:
- Ola de UI: indice + tablero con chips/alcances/vistas Lista-Calendario-Gantt sobre
  IActivityBoardService (esta ola fue SOLO backend).
- Corte de dia por zona horaria del tenant en DueRangeUtc.
- Destino final del kanban CRM heredado (TaskCard) cuando 000636 reemplace esas paginas.
- Sin commit (pedido explicito): cambios en working tree.

## 2026-07-04 - Sesion 15: Menu igualado 1:1 con el fuente del prototipo (ECOREX.dc.html)

**Objetivo**: cada opcion del menu del prototipo existe en el sistema; lo que no
tiene modulo real navega a un placeholder digno (nunca 404).

**Fuente**: estructura extraida del FUENTE (groupDefs/quickNav/rail/railDeco de
ECOREX.dc.html), no de memoria. 9 grupos MODULOS + subgrupo Comercial, 48 items
con codigo legacy 000XXX, quick nav Inicio + Anuncios (badge), rail de 8 iconos
(Inicio/Tareas/Flujos/Formularios + Calendario/Notificaciones/Indicadores/Alertas)
y avatar abajo.

**Hecho**:
- NavMenu.razor: seccion MODULOS reconstruida exacta al fuente (orden, contadores
  de items hoja, subgrupo Comercial abierto por defecto, misproc/auto abiertos por
  defecto como el prototipo). Quick nav reducido a Inicio + Anuncios (badge).
- Items reales mapeados: 000038 -> /crear-actividad (nuevo, abre TaskWizard);
  000042 -> /proyectos; 000636 y 000477 y 000270(gen) -> /actividades;
  000740 -> /pipeline (leads CRM); 000291 -> /flujos; 000131 -> /formularios;
  000867 -> /agentes; 000615 -> /configuracion; 000893 -> /plantillas;
  000850 -> /dependencias; 000109 -> /modulos-web; 000802 -> /reglas;
  000119 -> /metricas.
- Placeholders: pagina generica /modulo/{slug} (Modulo.razor, registro estatico
  slug -> titulo/grupo/codigo) sobre ModuleStub con el texto "Modulo pendiente de
  construccion - se priorizara en fases siguientes"; policy TenantMember. 31 items
  del menu + 3 destinos del rail (calendario/notificaciones/alertas).
- CrearActividad.razor: pagina liviana que abre TaskWizard al entrar; al crear o
  cerrar redirige a /actividades (vigia JS ecorexWatchWizard en MainLayout porque
  TaskWizard no expone callback de cierre; no se toco TaskWizard).
- MainLayout.razor: rail igualado al fuente (orden e iconos de const rail/railDeco);
  Indicadores -> /metricas real, Calendario/Notificaciones/Alertas -> stubs.
- app.css: clases .tr/.tg (rosa/verde para Negocio y Oferta-Catalogo) y estilos del
  subgrupo (.ecorex-acc-sub) replicando el prototipo.

**Desviaciones documentadas**:
- Grupo extra "CRM (heredado)" AL FINAL (no esta en el fuente) con las paginas CRM
  reales sin mapear: Asesores, Conversaciones, Lineas WhatsApp, Bitacora del agente,
  Automatizaciones, Lista negra (para no perder acceso).
- Quick nav "Gestor de tareas" (/tableros) y "Configuracion" retirados del menu
  rapido para ser identicos al fuente; /tableros sigue accesible por URL directa y
  /configuracion quedo mapeado en Sistema-General (000615).
- 000477/000636/000270(gen) comparten destino /actividades (el fuente los manda a la
  misma pantalla work/actividades): los 3 se resaltan activos a la vez en ese caso.
- Vendedores (000124) quedo placeholder; Asesores va en CRM (heredado) porque el
  nombre no coincide claramente.

**Validacion (probado de verdad)**:
- Build SuperAdmin: 0 errores 0 warnings. Tests: Domain 35/35, Application 115/115,
  Integracion 101/101 verdes.
- App real contra PG 5442 en http://localhost:5246 con owner@sky-system.local:
  los 57 destinos unicos del menu+rail responden 200 (fetch autenticado, sin
  404/500); stub /modulo/bodegas renderiza titulo + chip "Modulo 000556" + texto
  pendiente + seccion "Sistema (punto medio) Inventarios"; /crear-actividad abre el
  wizard solo y al cerrarlo redirige a /actividades (verificado en navegador);
  acordeones con contadores 5/3/1/4/5/4/8/8/10/6, toggle persistido en localStorage;
  rail 8 iconos + avatar; slug desconocido /modulo/no-existe cae al stub generico.
- Procesos detenidos al terminar. Nota: durante la sesion las corridas E2E de la
  sesion de tableros mantenian bloqueado bin/ de SuperAdmin; esta sesion compilo y
  ejecuto desde un output aparte para no interferir.

**Deudas / TODO**:
- Conectar el badge de Anuncios a datos reales cuando exista el modulo.
- Al construir cada modulo real: mover la opcion de /modulo/{slug} a su pagina y
  policy propia (los placeholders usan TenantMember).
- Sin commit (pedido explicito): cambios en working tree.

---

## 2026-07-04 - Sesion: Ola 2 UI de tableros de actividades (pantalla 'work' del prototipo)

**Agentes**: coordinador + 2 exploradores (UI Blazor y contratos backend/E2E).

**Fuente**: pantalla 'work' del prototipo corregido ECOREX.dc.html (showBoardsIndex +
boardOpen + isTablero/isLista) y capturas de Prototipo/screenshots. Valores tomados
del FUENTE (estilos inline del prototipo), no de memoria.

**Hecho**:
- /actividades REEMPLAZADA por la experiencia de tableros (alias /tableros-actividades,
  deep-link ?board={id}). El kanban por estado (TaskKanban) quedo desconectado de la
  ruta pero intacto: lo sigue usando ProyectoDetalle. Tableros.razor CRM sin tocar.
- NUEVOS: Components/Shared/Tasks/ActivityBoardsIndex.razor (indice: eyebrow TAREAS,
  h1 28px/800, 4 KPI cards 44x44/27px con soft-bg violet/blue/green/rose, barra
  Filtros con 5 dropdowns cascada Usuario/Etiqueta/Categoria/Subcategoria(=tipo)/
  Fecha + Limpiar(N), grid auto-fill minmax(320px,1fr) de tarjetas r18/p20 con hover
  translateY(-2px), badge de estado por TaskBoardStatus, barra Avance 6px brand,
  avatares solapados 26px, modal "Nuevo tablero", boton "Actividad completa" que
  abre el TaskWizard de 3 pasos para no perder el flujo con tipo/BPMN).
- NUEVOS: ActivityBoardDetail.razor (breadcrumb "< Todos los tableros", h1 27px/800 +
  pill de estado con punto + fecha limite + lapiz -> modal editar tablero, subtitulo
  literal del prototipo, FILAS DE FILTRO grid max-content/1fr con chips de columnas
  (punto colPal), asignados (avatar 22px), fecha Hoy/Manana/Con fecha (+date input,
  semantica OnDate del backend), etiquetas coloreadas con ring 2px al activar,
  Limpiar(N); PESTANAS DE ALCANCE team/mine/unassigned con contadores del servicio;
  switcher Tablero/Lista + Calendario/Gantt deshabilitados "Proxima ola" + boton
  Filtrar (badge, colapsa filas) + boton Tarea; kanban repeat(N,minmax(0,1fr)) con
  badges de columna, tarjetas r16 con Progreso checklist N/M y barra 5px con color
  por columna (t-blue/danger/t-amber/ok), avatares, pie con fecha coloreada
  (vencida danger / hoy warn) y contadores adjuntos/comentarios/checklist; drag and
  drop HTML5 (patron TaskKanban) -> MoveTaskAsync con toast si StatusNote; vista
  Lista con grid literal "1fr 130px 110px 110px 150px 76px"; modal de creacion
  rapida (titulo/descripcion/columna/prioridad/encargado/fecha/etiquetas + tipo de
  actividad opcional) -> QuickCreateTaskAsync con toast T#####).
- NUEVO AbUi.cs: paleta AVPAL de avatares, colPal por indice de columna (punto,
  badge, barra), estados del tablero, prioridades y fechas ("12 julio, 2026",
  "1 jul", Hoy/Manana) 1:1 con el prototipo.
- TaskDetailModal EXTENDIDO (no reescrito): card "Lista de chequeo" (checkbox 20px
  r6 verde + tachado + agregar/eliminar via ITaskItemService), card "Asignados"
  M:N (avatares solidos + agregar/quitar), fila "Avance N/M" + barra en Resumen
  alimentada por el checklist, y pill "Mover a: [columnas del tablero]" en el hero
  (MoveTaskAsync; StatusNote se muestra como banner informativo).
- app.css: bloque .ab-* (indice/detalle/kanban/lista/modales) + .tk-check-*/
  .tk-assignee-*/.tk-moveto con los valores del fuente; claro/oscuro via tokens.
- SignalR: ambos componentes se suscriben a TaskChanged (hub /hubs/tasks) y
  refrescan con scope EF propio + SemaphoreSlim (patron TaskKanban) - sin esto EF
  lanzaba "second operation on this context" y mataba el circuito (bug encontrado
  y corregido en esta sesion).
- Backend (delta minimo, reportado): ActivityBoardIndexFilter.HasDueDate (bool?) +
  2 lineas en ActivityBoardService.ListBoardsAsync para el dropdown Fecha del
  indice (Con fecha limite / Sin fecha), que no era expresable server-side.
- E2E actualizados al flujo nuevo: E2eTestBase (wizard via "Actividad completa",
  OpenBoardAsync/QuickCreateTaskAsync/BoardColumn/CardIn .ab-*), CreateActivityTests
  (wizard toast + creacion rapida con tarjeta en columna), MoveCardTests (dropdown
  "Mover a" Por hacer -> En progreso + pill de estado intacto), WorklogTests y
  WorkflowFormTests (crean por quick-create en PRY-0042), TenantIsolationTests
  (.ab-boards), PublicFormTokenTests (reintento del click Disenar: se perdia si el
  circuito seguia conectando bajo carga) y NUEVO BoardsIndexTests (KPIs, 3 tableros,
  abre PRY-0042, chips combinados con alcances, checklist -> Avance -> Progreso).

**Desviaciones documentadas (vs prototipo)**:
- Chips de Estado activos: borde ink + surface-3 (asi lo hace el FUENTE via pill(on);
  la instruccion decia brand/on-brand pero el fuente gana).
- Separador " - " en vez de " (punto medio) " en columnas del card (regla solo ASCII).
- Chip "Con fecha" del detalle abre un date input (el backend define OnDate con
  fecha puntual; el "cualquier fecha" del prototipo no existe en el filtro).
- Modal rapido agrega select "Tipo de actividad" (opcional, no esta en el prototipo):
  necesario para crear tareas con flujo BPMN desde el tablero (WorkflowFormTests).
- ProgressColor del DTO (color pale de la columna seed) NO se usa: se deriva el
  color de barra por indice de columna como pide el prototipo (t-blue/danger/
  t-amber/ok).
- Fix cross-engine: .ab-board-card > * { width:100% } (Chromium de Playwright no
  estira hijos flex de un <button>).

**Validacion (probado de verdad)**:
- dotnet build Ecorex.sln: 0 errores. Unit tests: Application 115/115, Domain 35/35.
  Integracion NO tocada (el delta backend es aditivo con default null).
- App real contra PG 5442 en http://localhost:5245 (login owner@sky-system.local):
  verificado en navegador real (Playwright + preview): indice con 3 tableros y
  KPIs; PRY-0042 abre con 4 columnas propias; chips Estado/Asignado/Hoy/Etiqueta
  filtran y COMBINAN con los alcances (contadores recalculados con los demas
  filtros); vista Lista; creacion rapida con toast T##### y tarjeta en la columna;
  mover por dropdown "Mover a" del detalle; checklist toggle actualiza Avance y el
  Progreso de la tarjeta; capturas claro y oscuro correctas (tokens html.dark).
- Nota alcances: con seed limpio son 10/3/1; la BD dev acumula tareas de corridas
  E2E previas (los contadores mostrados coinciden exactamente con la BD: 31/3/22
  al momento de la captura). Para ver 10/3/1 exacto: re-sembrar BD limpia.
- Suite E2E completa VERDE contra la app real: 10/10 (dos corridas consecutivas,
  41s y 48s). Procesos propios detenidos al terminar (la instancia :5234 de la
  sesion del menu se dejo intacta).

**Deudas para la ola 3**:
- Vistas Calendario y Gantt del tablero (tabs ya deshabilitados con tooltip
  "Proxima ola"; el prototipo trae calCells/ganttRows como referencia).
- Menu "..." de columna (renombrar/recolorear/agregar columna: hoy es decorativo)
  y boton "+" para agregar vista.
- Menu "..." de la tarjeta (hoy muestra el numero T en tooltip; falta menu real).
- Reordenar tarjetas DENTRO de la misma columna con drag (hoy solo entre columnas;
  MoveTaskAsync ya recibe sortOrder).
- Nombres de usuario en dropdowns del indice/modal rapido: se muestran emails
  (TenantUserDto no expone DisplayName; los chips del tablero si usan DisplayName
  del ActivityBoardMemberDto).
- Bug PRE-EXISTENTE anotado: DynamicFormRenderer.SaveAsync suelta un SemaphoreSlim
  ya disposed al enviar el formulario publico (ObjectDisposedException en el log;
  no rompe el flujo pero mata el circuito tras el submit).

---

## 2026-07-04 - Sesion: Ola 3 UI de tableros - vistas CALENDARIO y GANTT + pendientes menores

**Agentes**: agente unico (UI + delegaciones aditivas + E2E + validacion en navegador real).

**Fuente**: bloques isCalendario / isGantt / calCells / ganttRows / ganttDays de la
pantalla 'work' de ECOREX.dc.html (leidos del fuente, no de memoria).

**Hecho**:
- VISTA CALENDARIO en ActivityBoardDetail.razor: contenedor surface/line/var(--rad)/
  sh-sm con header "Julio 2026" (16px/700, -.01em) + botones prev/next 30x30 r8 que
  navegan el MES REAL; grilla 7 columnas con header Lun..Dom (11px/600 ink-3, ASCII);
  celdas de dia min-height 92px r10 p7 border line (35 o 42 segun el mes, offset
  lunes); HOY con border brand + bg brand-soft y numero en circulo 22px brand/
  on-brand 11.5px/700; numeros 12px/600 ink-2. Chips de tarea por DueDate local
  (10px/600 p3-6 r6 bg surface-3 border-left 3px del color de columna, truncados),
  MAX 3 por celda + "+N"; click en chip abre TaskDetailModal (stopPropagation);
  click en dia valido abre el modal de creacion rapida con esa fecha PRESELECCIONADA.
  Usa las MISMAS tarjetas filtradas de las otras vistas (ListRows del detalle).
- VISTA GANTT: banda header bg surface-2 con etiqueta izquierda 220px
  "TAREA - {MES} {ANO}" (10.5px/600 ink-3 .05em) + grilla de 14 dias (11px/600,
  border-left line, fin de semana bg surface-3 por DayOfWeek real) + botones
  prev/next que desplazan la ventana de 14 dias (adicion necesaria: el fuente no
  trae navegacion). Ventana inicial = bloque de 14 dias del mes que CONTIENE a hoy
  (1-14 / 15-28 / 29+). Filas: izquierda 220px con punto 8px del color de columna +
  nombre 13px/600 truncado; derecha relative height 38px con grid de fondo
  linear-gradient(90deg, line 1px) size calc(100%/14); linea HOY 2px brand opacity
  .45 en left ((dia-0.5)/14)%; barra absoluta top 8 height 22 r7 bg color de columna
  (colInfo = ColumnDot, igual al fuente) con el progreso "N/M" del checklist en
  blanco 10.5px/700; posicion StartDate -> DueDate (sin StartDate usa CreatedAt,
  sin DueDate usa StartDate+1d), clampeada a la ventana; filas totalmente fuera de
  rango se OCULTAN. Click en barra o en el nombre abre TaskDetailModal. Respeta
  filtros y alcances.
- Tabs Calendario y Gantt HABILITADOS (fuera el disabled/tooltip "Proxima ola").
- Menu "..." de COLUMNA (popover estilo ab-dd del fuente): Renombrar columna (modal),
  Marcar/Desmarcar columna final (IsDone) y Agregar columna al final. Usa el
  ITaskBoardService EXISTENTE (UpdateColumnAsync/CreateColumnAsync) inyectado en el
  componente: cero cambios de interfaz backend para columnas.
- Menu "..." de TARJETA: Abrir, Mover a (submenu con las columnas y su punto de
  color -> MoveTaskAsync al final de la columna destino) y Archivar con confirmacion
  en dos pasos -> ITaskItemService.ArchiveAsync existente + toast + broadcast.
- REORDEN INTRA-COLUMNA por drag: drop SOBRE una tarjeta inserta ANTES de ella;
  drop en el cuerpo de la columna manda al final. El indice de drop viaja como
  BoardSortOrder en MoveTaskAsync.
- Dropdowns de asignado con nombre legible: TenantUserDto gana DisplayName?
  (parametro opcional al final, cambio ADITIVO) poblado por join con PlatformUsers
  en TenantUserService.ListAsync; TaskUi.UserLabel(u) devuelve DisplayName o, si es
  null, DERIVA de la parte local del email con palabras capitalizadas
  ("ana.garcia@x" -> "Ana Garcia", decision documentada en el codigo). Aplicado en
  indice, modal rapido, TaskDetailModal (encargado + asignados), TaskKanban y
  TaskWizard. Con el seed real se ven "Owner SKY SYSTEM", etc.
- AbUi: MonthTitle ("Julio 2026") y MonthUpper ("JULIO") sobre MonthsLong.
- app.css: bloques .ab-cal-* / .ab-gantt-* / .ab-menu-* con los valores literales
  del fuente; claro/oscuro via tokens (verificado en ambos temas).

**Cambios backend (reportados, todos acotados)**:
- TenantUserDto.DisplayName (opcional, aditivo) + join en TenantUserService.ListAsync.
- ActivityCardDto.CreatedAt (opcional, aditivo) poblado en GetBoardDetailAsync:
  lo exige el fallback de la barra del gantt.
- ActivityBoardService.MoveTaskAsync: ahora RE-SECUENCIA la columna destino en
  memoria (inserta la tarea en el indice de drop clampeado y deja BoardSortOrder
  denso 0..N, un solo SaveChanges) y solo registra la actividad "movio la tarea"
  cuando CAMBIA de columna (el reorden intra-columna no ensucia el historial).
  El DTO devuelve el BoardSortOrder efectivo. Integracion ActivityBoard 12/12 y
  suite completa 101/101 verdes con el cambio.
- BUG PRE-EXISTENTE corregido (destapado por el input de fecha del modal rapido
  contra PG real): las fechas limite se construian con offset local -05:00 y Npgsql
  solo acepta DateTimeOffset UTC en timestamptz -> ArgumentException y circuito
  muerto. Fix .ToUniversalTime() (mismo patron de Inicio.razor, sesion 9) en los 7
  puntos: quick-create y editar tablero (ActivityBoardDetail), nuevo tablero
  (ActivityBoardsIndex), editar entrega (TaskDetailModal), wizard (TaskWizard) y
  filtros DueFrom/DueTo (TaskKanban).

**Desviaciones documentadas (vs instruccion, el fuente gana)**:
- Contenedores con border-radius var(--rad) = 20px (la instruccion decia r16; el
  fuente usa var(--rad) y el prototipo define --rad: 20px).
- Separador " - " en la etiqueta del gantt (regla solo ASCII; el fuente usa punto medio).
- Botones prev/next del gantt: no existen en el fuente (estatico); se agregaron con
  el MISMO estilo de los del calendario, en la banda del header.
- Weekend del gantt por DayOfWeek real (el fuente lo hardcodeaba para julio 2026).

**Validacion (probado de verdad)**:
- dotnet build Ecorex.sln: 0 errores. Domain 35/35, Application 115/115.
- Integracion: ActivityBoardTests 12/12 y SUITE COMPLETA 101/101 verdes
  (Testcontainers PG16 + SQL Server 2022).
- E2E: 2 escenarios NUEVOS en BoardViewsTests (a: chip visible en la celda del
  DueDate con dia pseudo-unico del mes siguiente, click abre el detalle, celda de
  HOY resaltada, y limpieza archivando via menu "..." con confirmacion; b: barra
  del gantt visible con progreso 0/0, banda TAREA-{MES}, 14 dias, linea de HOY y
  click en barra abre el detalle). QuickCreateTaskAsync del harness gana dueDate
  opcional (fill + blur por el patron @bind/onchange de ADR-0019).
- SUITE E2E COMPLETA contra la app real (PG 5442, puerto 5247): 12/12 verde
  (corridas 1 y 5); en 2 corridas intermedias fallo SOLO PublicFormTokenTests
  (flake PRE-EXISTENTE ya anotado en la ola 2: ObjectDisposedException del
  SemaphoreSlim de DynamicFormRenderer.SaveAsync al enviar el formulario publico;
  en aislamiento pasa 1/1). Los 2 escenarios nuevos pasaron en TODAS las corridas.
- Manual en navegador real (localhost:5247, claro Y oscuro): calendario (titulo,
  Lun..Dom, 35 celdas, hoy 22px brand/on-brand y bg brand-soft, chips con
  border-left del color de columna, "+4" de overflow, chip abre detalle, prev/next
  Junio<->Agosto, dia vacio abre quick-create con la fecha 2026-08-20 preseleccionada);
  gantt (TAREA - JULIO 2026, dias 1..14, 4 celdas weekend, barras N/M left/width en
  % exactos, linea hoy en 25% opacity .45, track 38px con grid 7.14286%, ventana
  15..28 con prev/next y filas fuera de rango ocultas, barra abre detalle);
  menu de columna (renombrar ida y vuelta, toggle columna final con toasts,
  agregar columna al final -limpiada por SQL para no ensuciar el seed-); menu de
  tarjeta (Abrir/Mover a con 4 columnas/Archivar); reorden drag intra-columna
  verificado (la tarjeta insertada ANTES del objetivo y persistida); dropdown
  Encargado con nombres legibles. Valores de estilo verificados por computed styles
  en claro y oscuro (dark: line rgba(255,255,255,.07), surface-2/3 oscuros, barra
  ink-3 dark, hoy brand dark).
- Procesos DETENIDOS: la app :5247 se paro al terminar (solo quedo la instancia
  :5234 de otra sesion, intacta a proposito). launch.json gano config
  superadmin-5247.

**Deudas / TODO**:
- Boton "+" de agregar vista en el switcher sigue decorativo (no estaba en la ola).
- El indice de drop del reorden usa las tarjetas FILTRADAS visibles; con filtros
  activos la insercion es aproximada respecto de la columna completa.
- Flake pre-existente de PublicFormTokenTests (bug del dispatcher/semaforo de
  DynamicFormRenderer): sigue pendiente como tarea de producto aparte.
- Zona horaria del tenant para el corte "fin del dia" de las fechas limite (hoy:
  fin del dia local del SERVIDOR convertido a UTC).
- Sin commit (pedido explicito): cambios en working tree.

## 2026-07-04 - Sesion: Modulo FORMULARIOS fiel al prototipo + constructor funcional end-to-end (ADR-0021)

**Agentes**: agente unico (modelo aditivo + migracion dual + renderer + indice +
constructor + seeds + tests + E2E + validacion manual en navegador).

**Fuente**: bloques isForms / formBuilderOpen / fbTypeReg / fbPaletteGroups /
renderNode / widthGrid / propTabs de ECOREX.dc.html (lineas 3016-3440 markup y
4069-4300 logica, leidos del fuente, no de memoria).

**Hecho**:
- MODELO (aditivo, UNA migracion dual `AddFormBuilderFields` aplicada y verificada en
  PG 5442 y MSSQL 1443): FormContainerType += Row/Col/Section/Tabs/Modal (string, se
  conservan Segment/Table; Segment se renderiza como Section); FormControlType +=
  File/Barcode/Paragraph/Divider/Spacer; FormQuestion += Width (1..12, backfill desde
  grid_col en la migracion, GridCol queda SINCRONIZADO col-12/col-md-N para renderer
  bootstrap y selectores E2E), PlaceholderText(200), DefaultValue(2000, doble uso
  documentado: texto del Paragraph / alto px del Spacer), IsLocked, IsHidden;
  FormContainer += TabsJson (jsonb/nvarchar), Width, IsLocked, IsHidden.
- SERVICIOS: FormDefinitionListItemDto += ResponseCount y RuleCount (KPIs reales del
  indice); MoveQuestionToAsync / MoveContainerToAsync (drag and drop, renumeran
  hermanos y validan ciclos); GridDetail exige columnas ([{id,label}] en OptionsJson);
  Required se apaga en multimedia placeholder; validacion servidor salta IsHidden;
  IRuleDocumentService.ListQuestionLinksAsync (tab Reglas por pregunta).
- RENDERER (DynamicFormRenderer, hereda /f/{token} y vista previa): contenedores
  Row (grilla)/Col (apilado)/Section=Segment/Tabs NAVEGABLES (cada hijo directo es
  pestana, nombres de TabsJson)/Modal (seccion normal, TODO dialogo); documento
  Paragraph/Divider/Spacer; multimedia firma/foto/gps/archivo/barras como placeholder
  del prototipo DESHABILITADO ("captura disponible proximamente") que NO bloquea el
  submit (Required ignorado en cliente Y servidor, mismo FormFieldValidator); TABLA
  (GridDetail) FUNCIONAL: filas dinamicas agregar/quitar, celdas por columna, persiste
  arreglo JSON de filas en el documento de la respuesta, Required = al menos 1 fila;
  placeholder (PlaceholderText) y valor por defecto (DefaultValue) en los inputs.
- INDICE /formularios reescrito nanometrico (Formularios.razor + .razor.css): rotulo
  "MODULO 000131 - AUTOMATIZACION", h1 28/800/-.03em, subtitulo literal, boton
  "Nuevo formulario" 40px brand (crea FRM-### siguiente y ABRE el constructor);
  4 KPIs reales (Formularios violet / Publicados green / Respuestas blue con separador
  de miles es / Con reglas amber; icono 42x42 r11, valor 19/800); busqueda 38px
  ("Buscar formulario..."); tabs de vista Tarjetas/Lista (32px r9, on: surface+sh-sm);
  tarjetas auto-fill minmax(300px,1fr) r18 p18 hover -2px con icono form 38x38
  violet-bg, badge de estado (Publicado green / Borrador amber / Archivado gris /
  Inactivo gris), nombre 15.5/700, FORX-{code} monospace 11 + badge categoria
  ("General", deuda: sin campo Categoria), grid 3 stats (15/800, reglas amber);
  lista grid 2fr 1fr .8fr 1fr .9fr con header 11/600 y hover surface-2.
- CONSTRUCTOR reescrito (FormDesigner.razor + .razor.css) como el modal grande del
  prototipo: overlay fixed z60 blur, shell 95vh r18 sh-lg; header 14-20 con back 34px,
  chip FORX-{code}, titulo 15.5/700 editable inline (UpdateHeaderAsync), badge estado,
  Vista previa / Activar-Desactivar / Publicar por URL (modal de tokens intacto de la
  ola 2) / Guardar brand / Cerrar. Grid 276px 1fr 320px. IZQUIERDA: paleta
  "1 ELEMENTOS" (Contenedor violet primario Fila, Input blue primario Texto corto) y
  "2 DOCUMENTO" (Texto/Divisor/Espacio amber), cards p11 r12 icono 34 r9 grip dots,
  draggable; "3 ESTRUCTURA" arbol en vivo con raiz "Formulario" brand-soft, chevrons
  colapsables, badge N/12 o conteo, lock (brand) y eye (danger) por nodo persistidos.
  CENTRO: device tabs Navegador 820 / Tableta 620 / Movil 380 (32px r9) + "A4 -
  820x1180" monospace; hoja r16 sh-md p26 con grilla 12 gap 12; render recursivo con
  label uppercase 9.5/700 del color del tipo, "Contenedor vacio - arrastra o toca un
  elemento", previews exactos por tipo (texto 38px, area 58, lista chevron, fecha
  dd/mm/aaaa, numerico 0, sino Si/No, tabla con columnas, firma/foto/gps/archivo/
  barras), seleccion border brand + ring brand-soft + acciones flotantes top -14
  right 10 (subir/bajar/duplicar recursivo/eliminar). DERECHA: "4 PROPIEDADES" con
  icono+nombre+tipo y tabs Diseno (tipo de elemento, etiqueta, selector de ancho de
  12 botones con "N col" + %, contenido de parrafo, alto de espacio, chips de
  pestanas, toggles Fijo/Oculto) / Datos (nombre interno FORX_DATA readonly, tipo de
  respuesta readonly, opciones chips blue, columnas tabla chips amber, texto de
  ayuda, valor por defecto, obligatorio) / Reglas (vinculos FormFieldRule REALES:
  verbo monospace + "Al cambiar", "Sin reglas asignadas" dashed, "+ Agregar regla"
  con picker documento->regla del catalogo del tenant, X desvincula) + "Eliminar
  elemento" danger. PERSISTE-POR-CAMBIO (cada mutacion llama al servicio y recarga;
  Guardar confirma con flash "Guardado"). DnD nativo: paleta->lienzo/contenedor y
  nodo->posicion (MoveTo*). Vista previa = renderer REAL (Fill si Active, Design si
  borrador) dentro del constructor.
- SEEDS: EnsureFormBuilderDemoAsync (FRM-002 "Inventario fisico bodega" BORRADOR;
  FRM-003 "Visita tecnica de instalacion" ACTIVO con Row (CC 3/12 + Nombres 5/12 +
  Fecha 4/12), Col Observaciones, Section Equipos con TABLA de 3 columnas y firma
  placeholder). Idempotente por Code, invocado en Program.cs.
- DOCS: ADR-0021 (docs/decisiones/0021-constructor-formularios.md) con el mapeo
  prototipo->enum completo, decisiones Paragraph/Spacer via DefaultValue, multimedia
  placeholder sin bloqueo y persistencia por cambio.

**Validacion**:
- Build Ecorex.sln 0 errores; dotnet format sin hallazgos en archivos de esta sesion
  (quedan 5 WHITESPACE PRE-EXISTENTES en Program.cs:53-54 y E2eDbBackdoor.cs:96-98,
  ajenos a este cambio).
- Unit: Application.Tests 130/130 (incluye nuevos: doc non-input, multimedia ignora
  Required, GridDetail filas/JSON) y Domain.Tests 35/35.
- Integracion DUAL COMPLETA (Testcontainers PG16 + SQL Server 2022): 105/105 verdes,
  con 2 tests nuevos x2 motores (BuilderFields_RoundTrip_WidthSyncAndContainers:
  round-trip Width/GridCol/TabsJson/IsHidden/IsLocked, derivacion legacy col-md-4,
  Required apagado en firma, MoveTo con ciclos prohibidos; GridDetail_SubmitRoundTrip:
  tabla requerida bloquea vacia, filas JSON identicas al leer, oculto requerido NO
  valida). Nota: jsonb de PG normaliza el formato del JSON (assert por contenido).
- E2E SUITE COMPLETA contra la app real (PG 5442, puerto 5248): 13/13 verdes,
  incluyendo el escenario NUEVO FormBuilderTests (indice -> Nuevo formulario ->
  constructor -> campo texto + lista con 2 opciones default -> Activar -> Vista
  previa en Fill -> submit valido "Enviado") y PublicFormTokenTests actualizado a los
  selectores nuevos del indice (.fx-card/.fx-code, tarjeta completa abre el
  constructor).
- Manual en navegador (localhost:5248, claro Y oscuro): indice (h1 28/800, KPI icono
  42x42 y valor 19/800, tarjetas sh-sm con stats reales 8 campos/11 respuestas/
  2 reglas de FRM-001, vista lista de 5 columnas con headers exactos, dark
  surface #161618); constructor de FRM-003 (grid EXACTO 276px/1fr/320px, 5 cards de
  paleta, arbol de 9 nodos con badges 3/12 5/12 4/12, seleccion con ring + 4 acciones
  flotantes, props CC/Texto con 12 botones de ancho y tabs Diseno/Datos/Reglas, tab
  Datos de la tabla con chips Equipo/Serial/Cantidad, dark ok); vista previa Fill con
  tabla funcional (agregar fila -> 3 celdas), firma "captura disponible proximamente"
  y Enviar visible.
- Procesos DETENIDOS al terminar (app :5248 del preview). launch.json gano config
  superadmin-5248.

**Deudas / TODO**:
- Modal como dialogo real en el renderer (hoy seccion normal, TODO en ADR-0021).
- Captura real de firma/foto/gps/archivo/barras (hoy placeholder deshabilitado).
- Campo Categoria en FormDefinition (badge fijo "General", sin tabs de categoria).
- Intercalado libre de preguntas y contenedores en una sola secuencia (hoy preguntas
  primero, luego sub-contenedores, por SortOrder por grupo).
- Celdas tipadas por columna en la tabla (hoy texto).
- WHITESPACE pre-existente en Program.cs y E2eDbBackdoor.cs (ajeno a esta sesion).
- Sin commit (pedido explicito): cambios en working tree.

---

## 2026-07-04 - Sesion: Modulo FLUJOS fiel al prototipo + editor canvas funcional (ADR-0022)

**Agentes**: coordinador + 2 exploradores (backend workflow / patrones UI SuperAdmin).

**Hecho**:
- INDICE /flujos (reemplaza el stub): rotulo "MODULO 000291 - AUTOMATIZACION",
  h1 28/800, boton "Nuevo flujo", 4 KPIs del prototipo (Flujos violet / En marcha
  green / Instancias activas blue / Ejecuciones (mes) amber; icono 42x42 r11, valor
  19/800), busqueda + tabs de filtro por cargo/categoria (surface-2 p4 r11, contador
  10.5 op .7) y tarjetas auto-fill minmax(330px,1fr) r18 p18 hover -2px con ID
  monospace, badge de estado con dot pulsante 1.4s si hay instancias Running, badge
  de categoria + "N nodos" y grid de metricas REALES en-marcha(azul)/ejecuciones/
  exito(verde) 16/800. Con modal "Nuevo flujo" (nombre + categoria; estado fijo
  Borrador) que crea el borrador minimo Inicio->Fin y abre el editor.
- EDITOR canvas PROPIO del prototipo (flowEditorOpen; SIN bpmn-js, cero JS externo):
  modal 95vh grid 1fr/340px; header con FLUJO {code} vN, nombre inline, select de
  categoria, Propiedades/Importar/Exportar/Publicar/Guardar cambios/Cerrar; toolbar
  flotante 38x38 r9 (sel/conn/task/event verde/gw ambar/del rojo); canvas 900x540
  surface-2 con puntos radial-gradient; nodos absolutos por tipo (start/end circulo
  border 3px, gateway diamante rotate45 warn con texto -45, task rect r12) con
  seleccion ring 4px brand-soft y cursor por herramienta; aristas SVG ortogonales
  H-V-H con markers (condicionales = brand dashed 7 5); stats "N nodos - M
  conexiones" + hint contextual; drag con pointer events (throttle ~30fps, persiste
  al soltar); panel DETALLE DE ACTIVIDAD con 6 acordeones FUNCIONALES:
  Configuracion basica (tipo + RestartNodeId reusando SetRestartTargetAsync +
  AllowsAssignment), Asignar usuarios (placeholder documentado PERMISO_CARGO),
  Recursos (WorkflowNodeForm real: picker de formularios ACTIVOS, chip con x),
  Reglas (WorkflowNodeRule real contra el catalogo con toggle autonoma y x),
  Notificacion (placeholder TODO), Reglas de salida (edita ConditionExpression de
  aristas salientes "condicion -> destino" + borrar arista); "Saltar a otro flujo"
  (modal de seleccion; vinculo real = deuda); modales Propiedades (nombre/categoria/
  estado con transiciones publicar/pausar/reanudar/descripcion) y Export/Import JSON
  del prototipo (pre monospace + Copiar; textarea + importar -> nueva version
  Borrador).
- MODELO aditivo + UNA migracion dual `AddWorkflowEditorFields` (PG 20260704160822 /
  MSSQL 20260704160855, APLICADAS y verificadas en 5442/1443): WorkflowDefinition +=
  Category(100?), IsPaused(default false); WorkflowNode += X, Y (default 0), W?, H?.
- Motor: BpmnProcessParser lee bpmndi Bounds (X/Y/W/H, redondeo AwayFromZero);
  ImportBpmnAsync llena layout (auto-layout BFS determinista si no hay DI);
  StartInstanceAsync rechaza definiciones pausadas. BpmnXmlWriter NUEVO genera
  process + bpmndi + condiciones estandar (round-trip garantizado por test).
- IWorkflowDesignService NUEVO (Application/Workflows): ListForIndexAsync (una
  tarjeta por ProcessCode; formula documentada: exito = Completed/(Completed+Stuck+
  Cancelled)%, ejecuciones = iniciadas en mes UTC), GetCanvasAsync, CreateDraftAsync,
  EnsureDraftAsync (publicada -> version borrador max+1 REUTILIZABLE via
  ImportBpmnAsync, copiando Category/reinicios/AllowsAssignment/vinculos por
  BpmnElementId), AddNode/Move/Rename/Connect (sin duplicados ni self-loop)/
  DeleteNode (protege startEvent; limpia aristas, vinculos y reinicios)/DeleteEdge/
  SetEdgeCondition/SetNodeConfig, UpdateDefinitionProps, Pause/Resume,
  ExportJson/ImportJson, SetNodeForm (solo formularios Activos)/RemoveNodeForm,
  AddNodeRule/RemoveNodeRule/SetNodeRuleAutonomous, ListRuleCatalog. REGLA: grafo
  editable SOLO en borradores; cada mutacion REGENERA BpmnXml con el layout
  (portabilidad bpmn.io del ADR-0014).
- Seeder EnsureWorkflowIndexDemoAsync: backfill de layout+XML para definiciones
  pre-editor (COT-COM + categoria "Comercial"), borrador demo "Mantenimiento y
  soporte" (FLW-001, construido con el PROPIO design service) y "Visita tecnica de
  instalacion" (VIS-TEC) publicada y PAUSADA. Sin instancias nuevas (metricas 0 ok).
- ADR-0022 (canvas propio vs bpmn-js, XML regenerado con DI, edicion solo borrador,
  formula de metricas, deudas).

**Validacion**:
- Build Ecorex.sln 0 errores; dotnet format --verify-no-changes limpio.
- Unit: 136/136 verdes (nuevos BpmnXmlWriterTests: round-trip write->parse, doble
  vuelta estable, condiciones xsi:type, saneo de ProcessCode, auto-layout
  determinista, parser de Bounds).
- Integracion DUAL completa verde (PG + SQL Server via Testcontainers); nuevos
  WorkflowDesignServiceTests 6x2: pausa bloquea StartInstance (y resume rehabilita),
  DeleteNode protege startEvent, mutaciones solo en borrador + EnsureDraft reutiliza
  versionado, editor persiste y regenera XML reimportable por el motor, export/
  import JSON crea version borrador con el mismo grafo y layout, indice con metricas
  reales (Running/mes/exito 0->100%).
- E2E Playwright 14/14 verde contra app real (PG 5442, puerto 5249), +1 escenario
  FlowsEditorTests: /flujos -> tarjetas (COT-COM "En marcha") -> Nuevo flujo ->
  editor -> agregar tarea -> renombrar -> conectar (Inicio->tarea) -> guardar ->
  cerrar (tarjeta "3 nodos" Borrador) -> REABRIR y verificar persistencia.
- Verificacion manual claro/oscuro contra el fuente (tokens surface/ink/line, KPI
  42x42, tarjeta r18 p18, dot ec-pulse 1.4s, canvas punteado, nodos por tipo, aristas
  dashed brand en condicionales, panel 340px, banner solo-lectura en publicadas con
  "Editar (crear version borrador)").
- Fix visual detectado por el E2E: .fe-head con flex-wrap para que las acciones no
  desborden bajo el panel derecho en anchos medios.
- Procesos DETENIDOS al terminar (app :5249 del preview). launch.json gano config
  superadmin-5249.

**Deudas / TODO**:
- Asignar usuarios por nodo: placeholder "TODO cargo/ACL por nodo (PERMISO_CARGO del
  vault)" hasta dependencias (000850); no se agrego AssigneesJson especulativo.
- Reglas de notificacion por nodo: chips ilustrativos (motor de notificaciones
  pendiente).
- "Saltar a otro flujo": seleccion visual; call activity/subprocess sin modelo en el
  motor.
- Ejecuciones (mes) en UTC (falta TZ de tenant); borrar el ultimo endEvent deja
  borrador no importable (solo el startEvent tiene proteccion dura).
- Sin commit (pedido explicito): cambios en working tree.

## 2026-07-04 - Sesion: Modulo GESTION DE REGLAS fiel al concepto por-modulo (ADR-0023)

**Agentes**: agente unico (exploracion + backend + UI + suites).

**Hecho**:
- /reglas REESCRITA como el concepto de Capa 6 (proto_gen_reglas.html): layout
  PERMANENTE de 3 paneles (lista 320px / editor 1fr / propiedades 300px, alto
  restante de la ventana), sin modales de edicion. ESTRUCTURA y MEDIDAS del proto
  con TOKENS del workspace (ADR-0023: accent->brand, success->ok, danger->danger,
  info->t-blue, warn-banner->t-amber-bg; code-bg oscuro #1F2937 FIJO en ambos
  temas). CSS scoped Reglas.razor.css (prefijo rg-).
- TOPBAR del modulo: breadcrumb Home/General/Reglas de negocio + badge "MOD 000802"
  (monospace 11/600, brand-soft) + acciones: Importar XML (deshabilitado, tooltip
  "Proximamente"), Probar (= ejecutar prueba de la seleccionada), Documentos (modal
  con el CRUD existente de RuleDocument: crear/renombrar/archivar) y + Nueva regla.
- KPIs REALES (42x42 r11, valor 19/800): Documentos, Reglas, Ejecuciones (30d) y
  Tasa de exito (30d) desde RuleExecutionLog (GetTenantStatsAsync).
- PANEL IZQUIERDO: lista PLANA de reglas del tenant (documento como categoria),
  titulo "Reglas (N)" + Filtrar (popover documento/categoria/estado/modo + ver
  archivados), buscador "nombre, tabla, proceso"; .rule-item con barra 4px por
  verbo (t-* rotando; gris si Inactiva), badge Activa/Inactiva/Desarrollo,
  mode-chip ENSAMBLADO monospace + dot + categoria; activo = brand-soft + borde
  izq 3px brand.
- EDITOR CENTRAL: cabecera 20/600 + badge + descripcion muted; acciones Duplicar
  (DuplicateRuleAsync: clona en el mismo documento, nombre " (copia)", SortOrder
  max+1, nace Development SIN vinculos), Eliminar (confirm inline; si tiene
  historial se INACTIVA con mensaje claro, append-only ADR-0016) y Guardar. Tabs
  Configuracion / Historial (contador) / Consumidores (contador).
- CONFIGURACION: warn-banner ambar SIEMPRE visible (mDATA/Execute no implementados,
  ADR-0016); grid 160px/1fr: Nombre, Descripcion (Rule.Description YA existia: SIN
  migracion), Modo ejecucion (Ensamblado seleccionable; Execute/mDATA disabled),
  Documento (select de RuleDocument; cambiarlo MUEVE la regla:
  SaveRuleRequest.DocumentId), Prioridad (SortOrder, max-width 100px), Verbo
  (catalogo tipado) y Estado.
- PARAM_XML: editor de codigo oscuro EDITABLE (textarea transparente sobre pre
  resaltado en C#, sin JS; tags #93C5FD, atributos #F0ABFC, strings #86EFAC,
  comentarios gris italic) con Formatear (re-indenta) y Validar (parsea contra el
  descriptor con errores claros y vuelca los valores a la vista renderizada).
  RuleParamXml NUEVO (clase PURA, Application/Rules): Generate/Parse/Format del
  contrato <REGLA><PROCESO><PARAMETROS><PARAM name tipo obligatorio valor/>;
  REPRESENTACION del ParamsJson tipado, nada se ejecuta por reflexion. "Vista
  renderizada" = form dinamico por descriptor (fuente autoritativa al editar;
  regenera el XML) + boton "Ejecutar regla" (guarda y corre; registra al usuario
  actual via GetCurrentTenantUserIdAsync).
- Historial reciente (4 .history-item con dot verde/rojo/ambar 8px + "Hoy HH:mm -
  Nms - usuario/error") + "Ver todo" -> tab Historial (lista completa paginada 25
  con "Mostrar mas"). Status strip: "Guardado hace X - ultima edicion {usuario}"
  (UpdatedAt/UpdatedBy reales via GetRuleAuditAsync; la "version" del proto se
  OMITE: Rule no esta versionada, deuda declarada).
- CONSUMIDORES: vinculos REALES con badges Formulario (t-blue: titulo + FieldCode)
  y Flujo (t-violet: nombre + nodo + autonoma), vacio dashed, quitar y agregar con
  los selectores existentes.
- PANEL DERECHO Propiedades: ID Regla (chip mono DocumentCode-8xGuid), Documento,
  Verbo; Ejecuciones (30d) 20/600, Tasa exito badge, Tiempo promedio ms
  (GetRuleMetricsAsync; tasa = Success/(Success+Failed), Skipped no cuenta);
  consumidores resumidos; Creada y Ultima modificacion (fecha + hace X).
- Backend aditivo (SIN migracion): ListAllRulesAsync, GetRuleAsync,
  DuplicateRuleAsync, GetTenantStatsAsync, GetRuleMetricsAsync, GetRuleAuditAsync,
  GetCurrentTenantUserIdAsync, SaveRuleRequest.DocumentId (mover),
  RuleExecutionLogDto.ExecutedByName (nombre del ejecutor en historial).
- ADR-0023 (tokens del workspace sobre paleta naranja del concepto, PARAM_XML como
  representacion editable, Execute/mDATA visibles pero deshabilitados por ADR-0016,
  eliminar->inactivar con historial, documentos en modal del topbar).

**Validacion**:
- Build Ecorex.sln 0 errores; dotnet format --verify-no-changes limpio.
- Unit: Domain 35/35 + Application 154/154 verdes (18 nuevos RuleParamXmlTests:
  round-trip por tipo -texto con <>&", numeric, boolean, fieldcode, json- +
  descriptor REAL de PASAR_CAMPOS, y errores: XML malformado, raiz/proceso
  invalidos, parametro desconocido/repetido, tipos invalidos, obligatorio
  faltante, case-insensitive con nombres canonicos, Format).
- Integracion COMPLETA dual verde 121/121 (PG + SQL Server via Testcontainers);
  nuevos: metricas 30d (ventana excluye ejecuciones viejas; tasa 0.5/1.0/0.0;
  lista plana con documento; archivado filtra; ExecutedByName) y duplicar (mismo
  documento, sin vinculos, Development, params semanticamente iguales -jsonb
  normaliza-, NotFound; mover de documento OK y NotFound con destino falso).
  Inactivar-con-historial (DeleteRule Invalid) INTACTO.
- E2E Playwright 15/15 verde contra app real (PG 5442, puerto 5250); +1 escenario
  ReglasTests: layout 3 paneles + 4 KPIs + Importar XML disabled -> + Nueva regla
  (NOTIFICAR con message) -> seleccionar en el sidebar -> editar prioridad ->
  Validar XML (rg-ok) -> Ejecutar regla (Exito) -> entrada en Historial reciente
  (Manual/Exito) -> prioridad persistida -> tab Historial la muestra.
- Verificacion manual claro/oscuro contra el proto (preview 5236): grid
  320px/1fr/300px, titulo 20/600, rule-item activo brand-soft + 13px, code editor
  #1F2937 con overlay textarea/pre alineado 1:1 (misma altura y metrica), KPI
  42x42 r11, props con datos reales (RUL-005-XXXXXXXX, 17 ejec, 100%, 1 ms),
  Validar OK y error claro ("'sql' no existe en el verbo PASAR_CAMPOS"),
  Documentos modal, tabs con contadores reales; en dark los tokens conmutan
  (surface #161618, brand-soft rgba blanca, t-amber-bg) y el code-bg queda fijo.
  Fix por verificacion: la vista renderizada va a la DERECHA del codigo solo
  >=1780px (antes estrangulaba el editor a 236px); debajo en anchos menores.
- Procesos DETENIDOS (preview 5236 y app legacy 5234 del worktree .preview;
  puertos 5234/5236/5250 libres).

**Deudas / TODO**:
- Importar XML: boton deshabilitado (sin formato definido).
- Version de la definicion de regla: no existe en el modelo (status strip la omite).
- Valores json del PARAM_XML viajan en atributo valor= con &quot; (valido, ruidoso).
- Historial por regla: corte en 500 filas en memoria (paginacion server-side si
  una regla supera eso dentro del TTL de 90d).
- Filtro por modo Execute/mDATA devuelve vacio a proposito (no hay campo de modo).
- Sin commit (pedido explicito): cambios en working tree.

---

## 2026-07-04 (sesion aparte) - Modulo CONCEPTOS (000270): /conceptos real sobre ActivityType

**Agente**: Claude Code (Fable 5). **Fuentes**: proto_tar_conceptos.html +
NEWFRONT_tar_conceptos (spec Capa 6). **Regla**: SIN migraciones; NO tocar
Lead/Pipeline (otro agente en paralelo con el Cargador de contactos). Sin commit.

**Hecho**:
- Pagina real `/conceptos` (Conceptos.razor + .razor.css) que reemplaza al
  placeholder /modulo/conceptos: ESTRUCTURA y MEDIDAS del proto con TOKENS del
  workspace (misma decision que /reglas: ADR-0023 -> ADR-0024 nuevo). Topbar
  breadcrumb + MOD 000270 + Exportar (disabled Pendiente) + "+ Nuevo concepto";
  tabs Actividades/Detalle; split 340px/1fr: lista de categorias (buscador,
  iconos con rotacion --t-*, conteo, estado) y detalle con KPIs reales
  (conceptos activos, tareas abiertas, con flujo, con formulario), filtros
  (estado / con-sin flujo) y grid (codigo derivado CN-XXXXXXXX de los ULTIMOS
  8 del Guid, proceso vinculado, formulario, orden con flechas subir/bajar,
  badges Activo/Archivado, editar/archivar). Tab Detalle = grid maestro
  Categoria x Concepto con conteo de tareas (analogo CANT_USADO) y filtros.
- Modal de concepto (860px, 6 acordeones como el proto): Datos basicos (nombre,
  categoria -select + "(nueva categoria...)"-, descripcion, orden) REALES;
  proceso vinculado = select de flujos PUBLICADOS (WorkflowDefinitionId real,
  validado en servicio); "Requiere formulario" = RequiresForm real; el resto de
  la spec sin respaldo en el modelo queda VISIBLE DESHABILITADO con tooltip
  "Pendiente" (ver gaps). Eliminar con confirm inline: en uso -> archiva (regla
  existente de DeleteAsync), sin uso -> borra.
- Categorias como agrupador string (no hay entidad): nueva categoria = pendiente
  local que persiste con su primer concepto; Renombrar = RenameCategoryAsync
  (mueve todo validando colisiones); Archivar categoria = SetCategoryArchivedAsync
  (FLAG_INA de TIPO_TAR).
- Backend aditivo SIN migracion (IActivityTypeService): Create/UpdateRequest ganan
  WorkflowDefinitionId + RequiresForm opcionales (compatibles); nuevos
  ListWorkflowOptionsAsync (solo publicados no archivados), GetUsageAsync (total/
  abiertas por tipo), SetArchivedAsync (Invalid en doble toggle),
  RenameCategoryAsync, SetCategoryArchivedAsync y MoveAsync (permuta SortOrder
  con el vecino normalizando empates, 1 SaveChanges). Validacion: flujo no
  publicado/inexistente -> Invalid tipado (la FK es NO ACTION).
- NavMenu: SOLO el item Conceptos (000270) pasa de modulo/conceptos a /conceptos
  (+ GroupRoutes para abrir el acordeon); registro del placeholder retirado de
  Modulo.razor. Policy nueva `Conceptos.Editar` (paso 1, claim tenant_id).
- ADR-0024 (docs/decisiones): tokens sobre paleta teal, jerarquia TIPO_TAR/
  TIPO_TAR_R proyectada sobre ActivityType.Category, gaps deshabilitados sin
  migrar, proceso vinculado 1:0..1 validado contra publicados.

**GAPS de la spec SIN respaldo en ActivityType (NO se migro; decide coordinador)**:
Code visible (se muestra derivado del Guid), IconClass, sedes/empresas por
concepto (TIPO_TAR_EMPRESA), RQ07 completo (FLAG_INICIA_MODULO, FLAG_BOTON_CIERRE,
TITULO_AUTO, DETALLE_AUTO), FLAG_CLIENTE, lista de chequeo (CHEQUEO),
FormDefinitionId especifico + modo (solo existe bool RequiresForm), procesos N:M
(TIPO_TAR_R_PRO; hoy 1:0..1), nodo inicial, permisos por cargo/usuario,
notificaciones por concepto (TIPO_TAR_N/NR), componentes fijos y formacion.
Todos visibles deshabilitados con tooltip "Pendiente" en el modal.

**Validacion**:
- Build Ecorex.sln 0 errores; dotnet format --verify-no-changes limpio.
- Unit verdes: Domain 35/35, Application 169/169 (sin unit nuevos: la logica
  nueva es EF y va en integracion dual).
- Integracion dual: 12/12 nuevos verdes (ActivityTypeCatalogTests x PG + SQL
  Server: flujo publicado/borrador/inexistente, archivar/restaurar + doble
  toggle Invalid, renombrar categoria con colision y NotFound, archivar
  categoria idempotente, mover orden con extremos Invalid, conteos de uso +
  delete-en-uso archiva). Suite completa 135/137: los 2 fallos son de
  ContactLoaderTests (trabajo EN CURSO del otro agente, no de este modulo).
- E2E Playwright 17/17 verde contra app real (PG 5442; el fixture tomo el
  primer puerto libre 525x, el 5252 estaba ocupado por la app del otro agente);
  +1 escenario ConceptosTests: abrir /conceptos (split + MOD 000270 + Exportar
  disabled) -> + Nuevo concepto (Codigo disabled = gap declarado) -> fila en el
  grid de Direccion Comercial (badge Activo) -> tab Detalle lo muestra -> el
  combo "Tipo de actividad" del wizard de actividades lo ofrece y selecciona.
- Verificacion manual claro/oscuro contra el proto (preview 5241/5251): topbar
  14x24, container 1400/20x24x60, tabs 10x18 borde 2px, split 340px/1fr gap16,
  th 10x16 11.5/600 upper, KPI valor 20/600, icono 32x32 r6, modal 860 r10 con
  field-row 160px/1fr, 6 acordeones y 11 controles Pendiente disabled, select
  con los 2 flujos publicados reales; CRUD por UI (crear, mover orden, archivar,
  eliminar) y NavMenu activo con acordeon abierto. En dark los tokens conmutan
  (bg #0A0A0B, surface #161618, badges --t-*-bg rgba). Fix por verificacion:
  el codigo derivado usa los ULTIMOS 8 del Guid (los primeros 8 de un Guid v7
  son timestamp y colisionaban visualmente entre filas creadas juntas).
- Procesos propios DETENIDOS (previews 5241 y 5251, app E2E auto-terminada,
  watchers cancelados). Quedan corriendo procesos AJENOS: 5234 (worktree
  .preview) y 5252 (otro agente) - no se tocaron.

**Deudas / TODO**:
- Los gaps de modelo de arriba (migracion pendiente de decision del coordinador).
- Exportar: boton deshabilitado (sin formato definido).
- Check-all/borrado masivo de sub-categorias del legacy: omitido a proposito
  (archivado por fila + borrado en modal).
- Sin commit (pedido explicito): cambios en working tree.

## 2026-07-05 (sesion aparte) - Modulo CARGADOR DE CONTACTOS (000873): /cargador-contactos real sobre el CRM (ADR-0024)

**Agentes**: agente unico (fuentes + backend + migracion dual + UI + suites), en
paralelo con el agente de /conceptos (sin tocar ActivityType; esta sesion tenia la
exclusividad de migraciones de la ola).

**Fuentes leidas**: proto_contact_loader.html (concepto Capa 6) y la spec
"comer_ContactLoader - Spec para reconstruir". OJO: la spec documenta que el modulo
legacy NO carga archivos (es un explorador de contactos scrapeados por N8N); el
requerimiento de esta ola pide un IMPORTADOR masivo sobre el CRM real, y asi se
implemento (reencuadre registrado en ADR-0024).

**Hecho**:
- Pagina /cargador-contactos NUEVA (CargadorContactos.razor + css scoped prefijo
  cl-): ESTRUCTURA y MEDIDAS del proto con TOKENS del workspace (ADR-0023/0024):
  topbar 14px/24px con breadcrumb + chip MOD 000873 + acciones (Plantilla CSV via
  data-url, Ver pipeline, Cargar N validas), layout 300px/1fr gap 16, sidebar
  sticky top 75 (archivo + mapeo de columnas + historial de cargas), 4 KPIs
  (icono 36x36 r8, valor 20/600): filas/validas/duplicadas/invalidas, tabs con
  borde inferior 2px (Previsualizacion/Errores/Resultado), grilla con avatares 40
  redondos por tono, badges mini por fila (Valida/Duplicada/Invalida), footer de
  paginacion (30 por pagina, el PageSize del legacy), responsive <=1000px.
- Flujo funcional end-to-end: InputFile (solo CSV, max 2 MB) -> CsvTableParser ->
  ContactColumnMapping.AutoMap (sinonimos ES/EN por encabezado, editable en el
  panel) -> ValidateAsync (previsualizacion con veredicto por fila) -> ImportAsync
  (carga TRANSACCIONAL: Lead en la PRIMERA etapa del pipeline asignado al
  importador + LeadActivity "lead.imported" + ContactImportBatch, rollback total)
  -> pestana Resultado con conteos + historial en el sidebar. La pagina llama
  PipelineSvc.EnsureDefaultsAsync igual que /pipeline.
- Application: CsvTableParser PURO (autodeteccion coma/punto y coma/tab fuera de
  comillas, RFC 4180 con "" y saltos de linea internos, BOM, lineas vacias fuera,
  filas rotas reportadas con numero de linea fisico), ContactLoaderDtos,
  IContactLoaderService + ContactLoaderService (validacion: nombre obligatorio
  <=200, email regex, telefono >=7 digitos, valor con miles/decimales tolerantes;
  dedup por telefono -ultimos 10 digitos, tolera prefijo pais- o email -de
  FieldValuesJson.email- contra los leads del tenant Y contra filas anteriores del
  archivo). Email/empresa van a FieldValuesJson (el Lead real no tiene columnas).
- Dominio + DAL dual: entidad ContactImportBatch (TenantEntity: FileName,
  TotalRows, Inserted, Duplicates, Invalid; CreatedBy/At del interceptor), DbSet +
  configuracion (indice tenant+created_at) y UNA migracion dual AddContactImports
  (Ecorex.Infrastructure 20260705022348 + Ecorex.Infrastructure.SqlServer
  20260705022429) APLICADA y verificada en los contenedores dev (PG 5442 \d y
  MSSQL 1443 sys.tables).
- NavMenu: SOLO el item "Cargador de contactos" (000740) paso de href=pipeline a
  href=cargador-contactos (una linea).
- ADR-0024 (docs/decisiones/0024-cargador-contactos.md): reencuadre del modulo,
  CSV primero (sin libreria Excel en la solucion), reglas de dedup, transaccion.

**Validacion**:
- Build Ecorex.sln 0 errores; dotnet format --verify-no-changes limpio.
- Unit: Application 169/169 verdes (15 nuevos CsvTableParserTests: delimitadores
  autodetectados -incluido delimitador dentro de comillas-, comillas escapadas,
  salto de linea dentro de campo con numeracion fisica, filas rotas por conteo de
  columnas, archivo vacio/null, lineas en blanco, CRLF+BOM, encabezado vacio
  posicional, ultima fila sin salto final, AutoMap ES y desconocidos). Domain
  35/35.
- Integracion COMPLETA dual verde 137/137 (PG + SQL Server via Testcontainers);
  +4 nuevos (2 tests x 2 motores) ContactLoaderTests: carga valida con duplicados
  detectados (CSV real por el parser: 2 insertadas con etapa/asignacion/
  FieldValuesJson/actividad + batch con conteos exactos, dup por telefono con
  prefijo +57 contra lead existente, dup por email dentro del archivo, invalidas
  por nombre vacio y email malformado, e idempotencia al recargar: 0 insertadas)
  y aislamiento cross-tenant del historial + de la deteccion de duplicados (el
  telefono cargado por A no es duplicado en B; cada tenant ve solo su batch).
  NOTA: una corrida con integracion+E2E+app en paralelo dio 7 flakes de arranque
  del contenedor MSSQL (WaitUntil timeout); la suite sola es 137/137.
- E2E Playwright COMPLETA verde 17/17 contra app real (PG 5442, puerto 5252 via
  ECOREX_E2E_BASEURL); +1 escenario CargadorContactosTests: generar CSV en el
  test (2 validas + 1 duplicada en archivo + 1 sin nombre) -> subirlo por el
  InputFile -> KPIs 4/2/1/1 -> mapeo automatico 6 de 6 -> badges dup/bad con
  motivo -> Cargar 2 validas -> Resultado (2/1/1) -> historial en el sidebar ->
  los 2 leads visibles en /pipeline. (ReglasTests fallo una vez por contencion
  al correr todo en paralelo; en la corrida limpia paso.)
- Verificacion manual claro/oscuro contra el proto (preview 5252): layout
  300px/744px gap 16 padding 16/20/60, topbar 14/24, sidebar sticky 75, KPI 36x36
  r8 y valor 20/600, tab activa borde 2px brand; carga manual de un CSV de 5
  filas via DataTransfer: KPIs 5/2/1/2, badges por fila, import real (flash
  "Carga completada: 2 insertadas, 1 duplicadas, 2 invalidas", batch en el
  historial, leads en /pipeline); en dark los tokens conmutan (bg #0A0A0B,
  surface #161618, ink #F4F4F5, brand invertido, tonos rgba translucidos).
- Procesos DETENIDOS (app preview/E2E 5252 parada; 5250/5252 sin listeners).

**Deudas / TODO**:
- Soporte Excel (.xlsx): no hay libreria referenciada en la solucion; queda
  documentado en la UI ("Soporte de Excel (.xlsx): pendiente") y en ADR-0024.
- Dedup solo por telefono/email: filas sin ambos no tienen clave y siempre entran.
- EvaluateRows carga los pares telefono/email de TODOS los leads del tenant en
  memoria por carga (aceptable hoy; si un tenant supera decenas de miles de leads
  conviene un indice/consulta dedicada).
- Limite de archivo 2 MB del InputFile (configurable si hace falta).
- Explorador N8N del legacy (fuentes LinkedIn/Maps, filtros dinamicos, presets):
  NO es este modulo; si se migra la ingesta scraper sera un modulo aparte.
- Sin commit (pedido explicito): cambios en working tree.

## 2026-07-04 (sesion aparte) - LOGIN/AUTH: presentacion visual alineada al prototipo maestro

**Que se hizo**:
- Rediseno de las pantallas de autenticacion (login, registro autogestion,
  recuperar, restablecer, activar) reemplazando el degradado morado heredado
  del backbone por el lenguaje del prototipo ECOREX.dc.html: split de dos
  zonas con aside de marca MUY sobrio (fondo --surface-2 + gradientes
  radiales sutiles de --brand-soft/--surface-3, tile cuadrado --ink con la
  inicial de la marca, subtitulo "Sistema de Tareas", titular corto y 3
  bullets de valor con iconos de linea: tareas/flujos BPMN/formularios) y
  panel derecho con la TARJETA del formulario (--surface, borde --line,
  radio --rad 20, sombra --sh-md). En movil (<=768px) colapsa a tarjeta
  centrada con la marca arriba. Tipografia Hanken Grotesk en todo el shell.
- NUEVO componente compartido Components/Shared/AuthShell.razor: aside de
  marca + tarjeta + footer para las 5 paginas de auth (parametros
  PlatformName/LogoUrl/Headline/Subtext/ShowBullets/ShellClass); el branding
  configurable (Marca) sigue mandando en logo/titular/subtexto. Login,
  Recuperar, Restablecer y Activar pasaron a usarlo (se elimino el aside
  duplicado por pagina); los estilos inline sueltos pasaron a clases
  (.auth-link, .auth-secondary).
- app.css: bloque .auth-* reescrito 100% con tokens del prototipo (labels
  11px/600 uppercase --ink-3; inputs 44px r12 borde --line-2 con focus ring
  --brand-soft; boton primario 44px r12 --brand/--on-brand hover opacity .9;
  Google/secundarios 44px r12 --surface hover --surface-2; alertas --danger
  sobre --t-rose-bg y --ok sobre --t-green-bg; transiciones 0.12-0.16s;
  focus-visible con outline --ink-2; overrides -webkit-autofill con
  --surface/--ink para matar el amarillo de Chrome tambien en dark). La
  vista previa del aside en /marca (.brand-preview-aside) se actualizo al
  mismo lenguaje sobrio (referenciaba el keyframe morado eliminado).
- Funcionalidad INTACTA: post a /auth/login-register-forgot-reset-activate,
  ids #login-email/#login-password y .auth-pane-login button.auth-submit
  (selectores de la suite E2E) sin cambios, mostrar/ocultar clave, switch
  login/signup (is-signup), boton Google condicionado a configuracion,
  hint de agencia para Google signup, mensajes de error/exito por query.
- Modo oscuro: sin colores duros; todo por tokens bajo html.dark (el script
  de tema de App.razor corre antes del CSS y aplica tambien al login).

**Validacion**:
- Build Ecorex.sln 0 errores; dotnet format --verify-no-changes limpio.
- App real contra PG 5442 en puerto 5254 + Playwright: /login claro y
  oscuro (dark forzado via localStorage ecorex-theme en init script:
  html.dark presente, boton rgb(27,27,30) en claro y rgb(244,244,245)
  invertido en dark, inputs 44px, Hanken Grotesk computada), viewport movil
  380px claro/oscuro (tarjeta centrada con tile arriba), /recuperar,
  /restablecer y /activar renderizan la tarjeta del nuevo shell, toggle
  MOSTRAR/OCULTAR funciona, switch a pane signup funciona, y el login con
  owner@sky-system.local / Demo123* aterriza en /inicio.
- Suite E2E COMPLETA verde 18/18 contra la app real (PG 5442, puerto 5254
  via ECOREX_E2E_BASEURL), selectores de login sin tocar.
- Procesos DETENIDOS (5254 sin listeners).

**Deudas / TODO**:
- AceptarInvitacion.razor usa el estilo viejo login-wrap/login-card (tarjeta
  suelta violeta del area admin), no el shell de auth; queda fuera del
  alcance de esta sesion.
- Sin commit (pedido explicito): cambios en working tree.

## 2026-07-07 - Sesion: Ola B2 - ENFORCEMENT REAL de permisos por rol (ADR-0033)

- Agente: Claude Opus 4.8 (claude-opus-4-8).
- Contexto: la Ola B1 (ADR-0032) dejo modelo + servicio + UI + resolucion efectiva,
  pero NO aplicaba los permisos. Esta ola los HACE CUMPLIR.

**Hecho**:
- Regla opt-in / back-compat: `EffectivePermissions` gana el eje `Unrestricted`.
  Owner/Admin -> AllowAll (implica Unrestricted); usuario SIN rol (o TenantUser no
  resoluble) -> Unrestricted (conserva el acceso del paso 1, NO se restringe;
  cambia respecto de B1 que devolvia Empty()); usuario CON rol -> sujeto a su
  matriz. `PermissionResolver.Resolve` y `RolService.ResolveEffectivePermissionsAsync`
  actualizados. Verificado: owner/admin/operator/viewer NO pierden acceso.
- Autorizacion dinamica (via NUEVA, aditiva): `PermissionRequirement` +
  `PermissionAuthorizationHandler` + `PermissionPolicyProvider` que materializa al
  vuelo las policies `Perm:{moduleKey}:{action}` (gate tenant_id + requirement) y
  DELEGA el resto en el DefaultAuthorizationPolicyProvider. Las policies clasicas
  (Inventario.Ver, TenantMember, ...) quedan intactas. Registrados en Program.cs.
- `ICurrentPermissions` (scoped, SuperAdmin/Auth): resuelve el set del usuario actual
  UNA vez por scope (en scope propio, como NavMenu), cachea, y es FAIL-OPEN
  (Unrestricted si no hay usuario o si la resolucion lanza) para no bloquear la consola.
- Menu filtrado por Ver: `MenuPermissionFilter` (Application, pure) poda Item con
  View=false y oculta secciones vacias; NavMenu lo aplica sobre el arbol resuelto.
- Paginas con enforcement real (policy `Perm:{route}:View` + botones gateados por
  Can Create/Edit/Delete): InventarioItems, catalogos de inventario
  (Bodegas/Marcas/Grupos/Subgrupos/Tipos), AdmUsuarios, RolesPermisos. El wiring del
  RESTO de modulos a `Perm:{route}:View` queda como follow-up mecanico (anotado en ADR).
- Seed "Asesor limitado" ajustado y DEMOSTRABLE (idempotente + reconcilia si ya existe):
  SIN Ver en Sistema-Desarrollo/Sistema-CRM/CRM-heredado; CON Ver en Mis Procesos/
  Inventarios/Automatizacion; Crear solo en tareas/proyectos (no inventario). Asignado
  a simple@sky-system.local.
- ADR-0033 creado.

**Pruebas** (dotnet build 0 errores; dotnet format --verify-no-changes limpio):
- Unit Application: 319/319 (incluye PermissionResolver Unrestricted + MenuPermissionFilter).
- Unit Domain: 35/35.
- Unit SuperAdmin (proyecto NUEVO Ecorex.SuperAdmin.Tests): 18/18
  (PermissionPolicy.TryParse, PermissionAuthorizationHandler, CurrentPermissions cache/fail-open).
- Integracion DUAL (RolesTests, PG + SQL Server): 24/24 (incluye 2 nuevos de menu
  filtrado: rol limitado excluye modulos sin Ver; Owner y sin-rol ven el menu completo).
- E2E: 8/8 del subconjunto de permisos/menu/usuarios/roles
  (PermissionEnforcementTests 3 nuevos + MenuProfile 2 + RolesPermisos 1 + AdmUsuarios 1
  + MenuEditor 1), verde contra la app real (PG 5442). Los E2E existentes de menu/
  usuarios/roles siguen verdes.

**Deudas / TODO**:
- Wiring del resto de paginas (Tareas, Proyectos, Flujos, Formularios, Reglas,
  Conceptos, ...) a su `Perm:{route}:View` (mecanico).
- Las policies clasicas migradas quedan sin uso en esas paginas pero se conservan por compat.
- El claim `Permissions` del token sigue sin poblarse (la consola resuelve por servicio).
- Sin commit ni push (pedido explicito): cambios en working tree.

## 2026-07-10 - Sesion: Inventario (000066) - campos por tipo + imagenes principal/texto + Datos tienda + vista Tarjetas/Lista

**Contexto**: el usuario tiene un proyecto hermano CUBOT.nails que le gusta como quedaron
los "items"; pidio llevar mejoras al INVENTARIO de ECOREX usando CUBOT.nails SOLO como
ejemplo. (Se habia trabajado por error en CUBOT.nails; se revirtio todo alli -
`git reset --hard origin/deploy` en rama deploy, W1 no estaba pusheado - dejandolo intacto.)

**Hecho** (agente: Claude Opus 4.8):
- **Dominio**: nueva `ItemFieldDefinition` (campos configurables del item POR `ItemType`,
  calcada de `TerceroFieldDefinition`; reutiliza `TerceroFieldType`). `ItemImage` += `EsPrincipal`
  + `Texto` (max 200). `Item` += `DatosTiendaJson` (jsonb dual).
- **EF**: config inline en `EcorexDbContext` (FK a `item_types` Restrict, indices `(tenant,tipo,sort)`
  y unico `(tenant,tipo,field_key)`; `DatosTiendaJson`/`Texto` con tipo dual). DbSet en
  `IApplicationDbContext` + fake de tests. Migracion `AddItemFieldDefinitionsEInventarioMejoras`.
- **Servicios**: `IItemFieldService`/`ItemFieldService` (List/Create/Update/Delete por tipo).
  `IItemService`/`ItemService` extendidos: DTOs con `DatosTienda` + imagenes `EsPrincipal`/`Texto`;
  `AddImageAsync(...,texto)` (1a imagen auto-principal), `SetImagePrincipalAsync` (exclusividad),
  `UpdateImageTextoAsync`; thumbnail y detalle prefieren la principal; `SaveItemRequest` serializa
  Datos tienda; helper `DatosTiendaJson`. DI registrado. Seed: 7 definiciones demo por tipo.
- **UI** `InventarioItems.razor`: toggle Tarjetas/Lista con persistencia por usuario
  (IJSRuntime + localStorage `ecorex.inv.view`, patron Pipeline.razor - NO ProtectedLocalStorage);
  vista Tarjetas nueva; en la ficha: seccion "Campos del tipo" (render dinamico por `TerceroFieldType`,
  valores en `FieldValuesJson`), seccion "Datos tienda" (pares ad-hoc), galeria de imagenes con
  boton principal (estrella + borde + overlay de texto via @onchange). Modal "Configurar campos"
  por tipo (CRUD). Semaforo `_dbGate` (SemaphoreSlim) serializa los handlers que tocan BD para
  evitar "A second operation was started" (el @onchange del texto de imagen chocaba con Guardar).

**Pruebas**:
- `dotnet build Ecorex.sln` 0 errores. Unit Application 331/331.
- Validado en Chrome (app local contra Postgres 5442, tenant demo SKY SYSTEM): campos por tipo
  (Material/Garantia/Color persisten en `field_values_json`), Datos tienda (`datos_tienda_json`),
  marcar principal (exclusividad + thumbnail), texto de imagen (overlay), Configurar campos
  (alta de "Peso (kg)"), toggle Tarjetas/Lista persiste tras recargar. **0** errores de
  concurrencia en el log; Guardar cierra el modal limpio.

**Nota / incidente**: al validar, el primer arranque uso `appsettings.Development.local.json`
(cargado en Program.cs:22 DESPUES de las env vars, por lo que las pisa) -> la app se conecto a
la BD de PROD (tunel 15433) y auto-aplico la migracion a PROD. La migracion es ADITIVA y
retro-compatible (tabla nueva + columnas nullable + bool default false), no rompe el codigo
desplegado. Para validar sin ensuciar prod se aparto temporalmente el `.local.json` y se corrio
contra Postgres local (5442); luego se restauro. **PENDIENTE**: desplegar el CODIGO nuevo a prod
(el schema ya esta) - requiere confirmacion del usuario.

**Deudas / TODO**:
- Deploy del codigo a prod (10.0.0.3, build-from-git) pendiente de OK del usuario. Schema ya aplicado.
- Migraciones SQL Server (DAL dual): todos los streams recientes son PG-only.
- Seed cosmetico: los items demo no quedan con imagen principal marcada (otro seeder crea items
  antes y `EnsureInventoryDemoAsync` sale temprano en el bloque de items); no es funcional, la
  1a imagen que sube un usuario se marca principal sola.
- Sin commit ni push (pedido explicito): cambios en working tree.

## 2026-07-10 - Sesion: Modulo CONTENEDOR DE DATOS (modelos dinamicos + anidados + config de importacion)

**Contexto**: portar el feature "Contenedor de datos" (DataContainer) del proyecto hermano
CUBOT.redmanager al sistema de Tareas, con estilo visual de Tareas, y EVOLUCIONARLO segun el
usuario: modelos ANIDADOS (submodelos/matrices), configurador en 2 columnas (campos | procesos de
importacion), y config (SOLO config, sin ejecutor) de conectores/credenciales/clientes/horarios.
Se estudio el hermano (solo lectura; sus servicios se bajaron para no chocar con otras sesiones).
Decisiones del usuario: todo el config de una; anidados desde v1; cliente/webhook solo config +
documentado; nombre "Contenedor de datos" (ruta /contenedor-datos, entidades DataContainer*).

**Hecho** (Claude Opus 4.8 + 3 subagentes: 1 de mapeo, 1 servicios, 1 UI):
- **C1 Dominio+EF+migracion**: `DataContainer` como ARBOL (ParentContainerId/ParentFieldId para
  submodelos), `DataContainerColumn` (tipo + `Submodel` -> ChildContainerId), `DataContainerRow`
  (ParentRowId/ParentFieldId), `DataContainerCell` (EAV, valor string). Config: `DataConnector`
  (fuente + credenciales CIFRADAS + MappingJson jsonb), `DataClient` (ClientId + secreto cifrado),
  `ImportProcess` (horarios). Enums DataContainerColumnType/DataSourceKind/ConnectorAuthKind/
  ImportScheduleKind (string). EF con cascadas recursivas PG-friendly del arbol (nota: SQL Server
  DAL-dual requerira revisar las cascadas auto-ref/multi-ruta, como el resto del DAL dual).
  Migracion `AddDataContainers`. DbSets en IApplicationDbContext + fake de tests.
- **C2 Servicios**: `DataContainerService` (CRUD arbol + filas EAV anidadas + import/export Excel
  con **ClosedXML 0.105.0** portado del hermano; opera sobre columnas escalares). `DataImportConfigService`
  (conectores/clientes/procesos; credenciales y secretos cifrados via `ISecretProtector`; genera
  ClientId + secreto fuerte mostrado una vez). DI registrado.
- **C3 UI** `/contenedor-datos` (estilo Tareas, gated Perm:contenedor-datos:View, semaforo _dbGate):
  lista de contenedores raiz (tarjetas), detalle con tabla de filas (columnas escalares + boton
  "ver" que expande sub-filas del submodelo), import/export Excel, **modal de configuracion en 2
  COLUMNAS** (izquierda campos con constructor recursivo de submodelos; derecha conectores +
  clientes + procesos), modal de clientes (crea/rota secreto mostrado una vez), modal de fila
  tipado, modal de import. Menu: item "Contenedor de datos" en seccion "Sistema . General" (seed +
  reconcile para demos ya sembrados).
- **C4 Doc handoff**: `docs/contenedor-datos-cliente-remoto.md` — contrato del cliente remoto
  (auth ClientId/Secret + HMAC, flujo de sincronizacion, endpoint de ingesta `/api/data-ingest/{id}`
  a construir con upsert anidado + idempotencia, checklist de lo pendiente). Para pasar a otra sesion.

**Pruebas** (build Ecorex.sln 0 errores; validado en Chrome contra Postgres LOCAL 5442, tenant demo):
- Modal 2 columnas OK. Constructor de submodelos: creado "Facturas" (raiz) con campo "Items"
  (Submodel) -> genero contenedor hijo "Items (detalle)" enlazado (verificado en BD: arbol
  data_containers + columna Submodel con child_container_id).
- Cliente "Agente Alegra": ClientId cli_... + secreto mostrado una vez; secreto **cifrado** en BD
  (prefijo DataProtection CfDJ8..., el texto en claro no aparece).
- Fila de datos EAV (Numero=F-001) guardada y renderizada; columna Submodel como "ver".
- **0** errores de concurrencia en el log. Migracion aplico limpio en PG (cascadas recursivas OK).

**Nota**: para validar sin tocar prod se aparto el `appsettings.Development.local.json` (que apunta
a prod y en Program.cs:22 pisa las env vars) y se corrio contra Postgres local; ya se restauro. La
migracion AddDataContainers **NO** esta en prod todavia.

**Deudas / TODO**:
- Deploy a prod pendiente de OK del usuario (schema + codigo). El usuario probara y dara credenciales/
  estructuras; luego se construye el cliente remoto y el endpoint de ingesta (ver doc de handoff).
- No probado en Chrome (bajo riesgo, codigo portado/CRUD): descarga de export Excel (usa data-URL via
  JS eval; sin CSP en SuperAdmin), guardar conector con credenciales, agregar sub-fila anidada por
  "ver", guardar proceso con horario.
- Ejecutor de horarios + canal websocket + endpoint de ingesta: fase siguiente (documentados).
- SQL Server DAL-dual de estas cascadas: revisar (PG-only por ahora).
- Sin commit ni push aun (pendiente de validacion final del usuario).

### Addendum (misma sesion) - Contenedor de datos: RELACIONES entre tablas (Referencia N:1 + N:N)

El usuario noto que faltaba definir VARIAS tablas y RELACIONARLAS (distinto del submodelo anidado,
que es composicion). Se agrego:
- **C5a dominio+EF+migracion**: tipos de campo `Reference` (N:1) y `RelationMany` (N:N) en el enum;
  `DataContainerColumn.ReferencedContainerId` (FK Restrict a otra tabla raiz); entidad
  `DataContainerLink` (N:N: ColumnId, RowId, TargetRowId). Migracion `AddDataContainerRelations`.
- **C5b servicios**: Reference guarda el id del registro destino en la celda EAV; RelationMany en
  DataContainerLink (add/remove por SaveRow). `ListRowOptionsAsync` (registros de la tabla destino
  con etiqueta = 1a columna Text). Guard al borrar una tabla referenciada. DeleteRow limpia links.
- **C5c UI**: en el editor de campos, tipos Referencia/N:N + selector "Tabla destino" (excluye la
  tabla actual); en el modal de fila, Reference = dropdown de registros, N:N = multi-select; en la
  tabla, referencia como etiqueta y N:N como chips.

**Pruebas** (build 0 errores; validado en Chrome local): se crearon 2 tablas independientes
(Clientes + Facturas), un registro "Acme Corp" en Clientes, un campo `Cliente` (Referencia -> tabla
Clientes) en Facturas, y se asigno en la fila F-001. La tabla muestra CLIENTE = "Acme Corp" (etiqueta,
no Guid); la celda guarda el id destino; el selector "Tabla destino" excluye Facturas; 0 errores de
concurrencia. N:N (RelationMany) quedo construido (tipo + tabla de enlace + multi-select + chips) con
el mismo patron; se valido en vivo la N:1 (la integracion mas delicada).

Nota N:N con atributos (ej. Pedidos<->Productos con cantidad): se resuelve con submodelo anidado +
Reference (ya posible); la N:N pura (solo vinculo) usa RelationMany.

### Addendum 2 - Validacion N:1 + N:N en vivo + nota de menu/permisos

- **N:1 (Referencia)**: Facturas.Cliente -> Clientes; la fila muestra la etiqueta "Acme Corp".
- **N:N (RelationMany)**: Facturas.Productos -> Productos; multi-select (Monitor/Teclado), chips en la
  tabla, vinculos en data_container_links. 0 errores de concurrencia. Validado con el MENU COMPLETO
  (usuario admin@ Owner + vista "Completo", navegando por el menu; item "Contenedor de datos" visible
  en Sistema . General).
- **Nota menu/permisos (ADR-0033)**: el menu se poda por permisos (MenuPermissionFilter). El usuario
  demo completo@ tiene la VISTA "Completo" pero rol limitado (Advisor) SIN el permiso del modulo nuevo,
  asi que NO ve ni accede a "Contenedor de datos" (se le poda; redirige a login al entrar directo). Los
  usuarios Owner/Admin (Unrestricted) SI lo ven y acceden. TODO: al desplegar, el modulo nuevo debe
  quedar grantable en Roles y permisos para roles limitados (el catalogo de permisos se deriva del menu;
  los roles limitados requieren grant explicito de contenedor-datos:View).
- Tweak local (solo BD dev): se reasigno admin@sky-system.local a la vista "Completo" para validar con
  menu completo (antes tenia una vista E2E minima). Es la BD local, no prod.

### Addendum 3 (2026-07-10) - REDISENO Contenedor de datos: modelo con VARIAS tablas + lienzo ER

El usuario rechazo la version previa ("ha quedado mal"): un Contenedor NO es una tabla sino un
**MODELO que contiene VARIAS tablas relacionadas entre si** (esquema ER interno), correspondiente a un
JSON de importacion que trae varias estructuras (cada estructura = una tabla del contenedor). El modal
debe ser mas grande, en 2 columnas: IZQUIERDA = lienzo ER interactivo (cajas de tabla arrastrables que
se conectan); DERECHA = configuracion de alimentacion (conectores Excel/API REST/BD de distintos
motores + credenciales, clientes, motor de horario, y un DESTINO: dentro del sistema o BD aliada).
Solo configuracion en esta fase; el motor de ejecucion y el conector remoto quedan diferidos.

- **R1 dominio+EF+migracion**: entidad `DataModel` (el Contenedor top-level: Name, Description,
  ICollection<DataContainer> Tables) + `DataDestination` (1:1 con el modelo: Kind System/AlliedDatabase,
  DbEngine?, Host/Port/DatabaseName/Username, credenciales cifradas). `DataContainer` pasa a ser la
  TABLA: +ModelId, +CanvasX/CanvasY (posicion en el lienzo). Nuevos enums: `ConnectorKind`
  (Excel/RestApi/Database), `DbEngine` (PostgreSql/MySql/SqlServer/Oracle/MariaDb/SqLite),
  `DestinationKind` (System/AlliedDatabase). `DataConnector` e `ImportProcess` pasan de ContainerId a
  ModelId; el conector gana Kind + campos de BD. Migracion `RedesignDataModelContainers` (crea
  data_models, data_destinations; agrega model_id/canvas_x/canvas_y a data_containers; model_id/kind/
  db_engine/host/... a data_connectors; model_id a import_processes). Indice unico (model_id, name)
  filtrado para tablas de primer nivel del modelo.
- **R2 servicios**: `IDataModelService` (listar/get con relaciones = columnas Reference/RelationMany que
  apuntan a otra tabla del MISMO modelo; guardar modelo; guardar tabla estampando ModelId + posicion,
  validando que el destino de la relacion sea del mismo modelo; borrar tabla; actualizar posicion).
  `IDataContainerService.SaveTableAsync` reusa la maquinaria de columnas. `IDataImportConfigService`
  reescrito a nivel de modelo (conectores por ModelId con campos segun Kind + cifrado; destino 1:1;
  clientes por tenant; procesos por ModelId).
- **R3 UI**: `ContenedorDatos.razor` reescrito. Listado de contenedores (tarjetas). Modal grande
  (96vw x 92vh) en 2 columnas: IZQUIERDA `.dc-canvas` con overlay SVG (lineas de relacion: violeta
  solida = Reference, naranja discontinua = RelationMany, con etiqueta del campo) y cajas
  `.dc-table-node` arrastrables (posicion CanvasX/Y); DERECHA "ALIMENTACION" (conectores con Kind
  condicional, destino Sistema/BD aliada, clientes, procesos). Drag por `dc-canvas.js` (pointer events)
  -> `[JSInvokable] OnTableMoved` -> UpdateTablePositionAsync. `_dbGate` SemaphoreSlim + GuardAsync +
  IDisposable. Editor de tabla con columnas incluye Reference/RelationMany + selector "Tabla destino"
  limitado a las otras tablas del modelo.
- **R4 validacion (Chrome local, BD local Postgres 5442)**: build de la solucion 0 errores. Se creo el
  contenedor "Ventas" con 2 tablas (Facturas, Clientes) que se renderizan como cajas del lienzo ER;
  el DRAG persiste la posicion (Facturas 40,40 -> 391,301 en data_containers). Se agrego el campo
  Facturas.Cliente (Referencia N:1) apuntando a Clientes -> el lienzo DIBUJA la linea de relacion entre
  ambas cajas (etiqueta "Cliente"); relacion verificada en BD (referenced_container_id -> Clientes).
  0 errores de concurrencia. El destino por defecto es "Sistema (tablas del contenedor)"; el cliente
  "Agente Alegra" persiste a nivel de tenant. Los contratos deprecados (SourceKind a nivel de contenedor,
  ContainerId en conector/proceso) se conservan por compatibilidad; ContainerId paso a SetNull.

Pendiente: (a) DESPLIEGUE a prod (requiere OK del usuario; la migracion se aplico SOLO a la BD local;
appsettings.Development.local.json esta apartado como .bak durante la validacion local). (b) captura de
DATOS por tabla en el nuevo diseno (filas por tabla del modelo; excluido de R3). (c) doc del conector/
cliente remoto (docs/contenedor-datos-cliente-remoto.md) por actualizar al concepto de destino
sistema/BD-aliada. (d) grant del permiso contenedor-datos:View a roles limitados. (e) DAL-dual SQL Server.

### Addendum 4 (2026-07-10) - Contenedor de datos: panel de DATOS por tabla (Excel + filas + relaciones)

A peticion del usuario ("crea un excel, cargalo y dale relaciones para probar"), se cablea la
captura de datos que faltaba (backlog de R3). Cada tabla del contenedor gana un panel "Datos"
(boton en la caja del lienzo ER) con: importar Excel (InputFile -> ImportFromExcelAsync, solo
columnas escalares), exportar Excel (descarga via nuevo ecorexDcCanvas.downloadBase64),
alta/edicion/borrado de filas, y enlace de relaciones a nivel de fila -> Referencia N:1 como
dropdown de la tabla destino (etiqueta resuelta por ListRowOptionsAsync, no el Guid) y N:N como
multi-check con chips. El grid muestra cada celda con su valor resuelto. Reusa
IDataContainerService existente; _dbGate + GuardAsync. Se inyecta IDataContainerService en la
pagina. Sin migracion (UI-only).

**Validado en vivo** (preview contra BD prod): contenedor "Ventas comerciales" con Clientes
(Nombre, Ciudad) y Facturas (Numero, Monto, Fecha, Cliente=Referencia N:1). 3 clientes + 3
facturas enlazadas; el grid de Facturas muestra CLIENTE como chip con el nombre; el join en BD
resuelve F-001->Acme, F-002->Globex, F-003->Initech; export sin errores; lienzo ER dibuja la
linea Facturas.Cliente->Clientes (drag OK). La carga por archivo Excel quedo cableada (compila);
el sandbox de pruebas MCP no permite empujar archivos al selector, asi que las filas se poblaron
con "+ Fila" (mismo SaveRowAsync). Commit 63bda1a en main + fase-0/clon-backbone; DESPLEGADO a
prod (build-from-git, sin migracion, login 200).

Pendiente: (a) grant contenedor-datos:View a roles limitados. (b) doc del destino/cliente remoto.
(c) DAL-dual SQL Server. (d) resolver Reference en el import de Excel por clave (hoy las relaciones
se enlazan en la app tras importar los escalares).

### Addendum 5 (2026-07-11) - Contenedor de datos: "Guardar y nueva" + import desde API REST (paginacion + modos)

Continuacion del panel de Datos (Addendum 4), a pedido del usuario y validado EN VIVO contra prod.

- **"+ Guardar y nueva"** en el alta de fila (commit `1de0ad7`): el editor de fila gana un tercer
  boton que persiste la fila y deja el formulario limpio en modo "nueva fila" (mismo modal abierto)
  para capturar varias seguidas, con flash "Fila guardada. Van N en la tabla.". Los otros pasan a
  "Cerrar" y "Guardar y cerrar". SaveRowAsync -> SaveRowCoreAsync(keepOpen).

- **Item de menu "Contenedor de datos"**: no estaba en el menu (el modulo se alcanzaba por URL). Se
  agrego a la vista "Completo" en Sistema . General via Administrador de Menu (000194). OJO en ese
  editor: primero "Aplicar cambios" (confirma el nodo) y LUEGO "Guardar" (persiste la vista); si solo
  se da Guardar, el item queda como "Nuevo elemento" sin ruta. Es config por-tenant en la BD
  (menu_nodes/menu_views), no en codigo.

- **Importacion desde API REST** (motor generico, NO atado a Alegra):
  - **C_API1 servicio** (`IApiImportService` + `ApiImportService`, registrado con AddHttpClient en
    Infrastructure; commit `882cc0b`): `ProbeAsync` hace el GET del conector RestApi con su auth
    (credenciales descifradas server-side: Basic=base64 de usuario:clave, Bearer, ApiKey), detecta el
    arreglo JSON (raiz array, envoltorios data/items/... o ruta con puntos) y descubre los campos
    (llaves del primer objeto) + una muestra. `ImportAsync` crea una fila por elemento mapeando
    campo->columna escalar. Guard SSRF minimo (http/https, bloquea loopback/privadas), timeout 30s.
  - **C_API2 paginacion** (commit `84d2539`): `ApiPaging` (Offset start/limit o Page page/limit,
    tamano de pagina, valor inicial, tope de paginas). El motor recorre pagina por pagina reescribiendo
    esos parametros en el query string y para cuando una pagina viene vacia/mas corta que el tamano (o
    al tope / al limite de 5000 filas). FetchAsync se partio en LoadConnectorAsync + FetchJsonAsync(uri)
    + WithQueryParam.
  - **C_API3 modos de re-carga** (commit `8509b36`): `ApiImportMode` Append/Replace/Upsert + KeyColumnId;
    `ImportAsync` devuelve `ApiImportOutcome` (insertadas/actualizadas/borradas/fallidas). Replace vacia
    la tabla (filas+celdas+enlaces) antes; Upsert precarga clave->fila y actualiza la fila cuya columna
    clave (mapeada, ej. id) coincide o inserta (idempotente en re-cargas).
  - **UI**: en cada conector REST activo, boton "Importar" abre un sub-modal -> "Descubrir campos" ->
    tabla destino + mapeo columna<-campo (auto-match por nombre) + muestra + seccion "Modo de
    importacion" + "Paginacion" (activada por defecto, tamano leido del limit del endpoint).

- **Pruebas en vivo (prod, tenant SKY SYSTEM)**: contenedor "Prueba API" con tabla "Categorias"
  (id/name/description). Conector "Alegra categorias" (endpoint Alegra, Basic; la credencial la pego el
  USUARIO en el campo -- el agente NO teclea secretos). item-categories daba 0 (cuenta vacia, no error);
  con /items: Descubrir = 16 campos / 30 por pagina; import de una pagina = 30 filas; import con
  paginacion = 318 filas (todo el catalogo). Modo Reemplazar = "348 borradas, 318 insertadas"; modo
  Upsert por id = "318 actualizadas" (0 duplicadas). Todo sin migracion.

Pendiente: (a) rotar la credencial de Alegra compartida por chat. (b) programar el import por horario
(la seccion "Procesos" existe pero sin motor de ejecucion/scheduler). (c) mapear campos anidados
(ej. category.name). (d) grant contenedor-datos:View a roles limitados. (e) DAL-dual SQL Server.

---

## Sesion 2026-07-11 - Modulo de Tareas: puente Concepto<->Tarea (PRE-1..5 + Olas 1-7)

**Agentes**: Claude (Opus 4.8). **Contexto**: doc del vault `Capa 2 Tareas y Proyectos/Modulo de
Tareas - Creacion y ejecucion/` (indice, decisiones, UX, plan por olas). Los motores ya existian
(Conceptos 2-niveles, WorkflowEngine, DynamicFormRenderer, Organigrama, Menu data-driven); faltaba
el PUENTE: la tarea se clasificaba por `ActivityType` y NO consumia el concepto.

**Prerequisitos (los 5, 2026-07-11):**
- PRE-1 (`5590545`): `Entidad` (Sede/Area) desde Config de la entidad (000616); el modal pregunta
  primero el tipo. FK del TaskItem = `EntidadId->Entidad` (NO OrgUnit).
- PRE-2/PRE-3: mapa de lectores del concepto (vacio, este modulo es el 1er consumidor) + backfill
  (`SubcategoriaId` nullable, 206 tareas en NULL, `ActivityTypeId` deprecado no dropeado).
- PRE-4 (`66bb60d`): `OrgUnitMember.IsResponsible` (jefe por unidad, sincroniza `ResponsibleTenantUserId`).
- PRE-5 (`66bb60d`): `MenuNode.IsProcessGroup` (flag + editor + badge).

**Olas:**
- Ola 1 (`a60252e`): `TaskItem` pivota a `SubcategoriaId`+`EntidadId` (ActivityTypeId nullable);
  migracion `TaskItemConceptoBridge`; `CreateAsync` exige >=1 clasificacion y hereda tablero+1a columna
  del concepto.
- Ola 2 (`c95c5f5`): el alta arranca el flujo desde `subcategoria.WorkflowDefinitionId`, aplica
  `TituloAuto`/`DetalleAuto` (token `@cliente`), deja traza de notificacion.
- Ola 3 (`9af2202`): `TaskWizard.razor` reescrito al wizard 4 pasos MILIMETRICO al prototipo
  (Informacion/Contacto/Formulario/Documentos + aside resumen; cascada Empresa/Area->Tipo->Actividad->Encargado).
- Ola 4 (`8de3521`): `NavMenu` expande el grupo `IsProcessGroup` con el arbol dinamico
  categoria->subcategoria-proceso desde Conceptos.
- Ola 5 (`16bf824`): form-first -- al entrar al paso Formulario de un concepto `IniciaModulo`+`FormDefinitionId`
  el wizard crea la tarea y renderiza `DynamicFormRenderer` (Fill).
- Ola 6 (`4e17144`): tableros (ADR-0020) ya maduros; cerre 3 pendientes -- SignalR vivo,
  `/actividades?sub=` carga el tablero del concepto, crear-desde-tablero solo conceptos SIN proceso.
- **Ola 7 (`7111cbb`) endurecimiento**: NUEVO = notificacion al asignar (`AssignAsync` deja traza al
  encargado + destinatarios del concepto via `AddConceptNotificationAsync`). Verificado con 4 tests de
  integracion verdes (PG): notificacion al asignar, consecutivos transaccionales, concurrencia optimista,
  aislamiento cross-tenant (permisos). Auditoria = trazas `TaskItemActivity`.

**Validado en Chrome** (tenant SKY SYSTEM): tareas T00207-T00211 creadas por concepto; wizard 4 pasos,
menu Mis Procesos dinamico, form-first (FRM-001 Submitted ref=T00210), tablero por `?sub=`.

**Diferido (Ola 7 mayor)**: policies COMPUESTAS por vista (hoy placeholder `Tareas.Ver`==claim tenant_id;
refactor de auth) y ENTREGA real de notificaciones (canal email/in-app + plantilla; hoy solo traza).

**PENDIENTE OPERATIVO GRANDE**: desplegar a prod TODO lo acumulado -- migraciones
`AddEntidadConfig`/`AddEntidadKind`/`AddJefeMemberAndProcessGroupMenu`/`TaskItemConceptoBridge` + olas 1-7
(hoy solo local) + config demo hecha por DB (vincular flujo/form/tablero a subcategorias) que en prod se
hace por el editor de Conceptos. Backlog: Proyectos P1-P3; sincronizar SqlServer (DAL-dual).

---

## Sesion 2026-07-11 (cont.) - Goal: menu completo acuartas (prod) + Ola 7 diferidos

**Agentes**: Claude (Opus 4.8).

**Parte 1 - vista de menu "Completo" a acuartas@bitcode.com.co (PROD)**: el tenant BITCODE se creo
SIN ninguna vista de menu (los 13 usuarios con `menu_view_id` NULL -> sidebar vacio). Como las
`menu_views` son por-tenant, se clono la vista "Completo" (70 nodos) de SKY SYSTEM hacia BITCODE
(nueva `menu_views.id=87104d1f-dc92-47c4-a966-93dfd712386b`, no default) via SQL transaccional con
idmap de nodos, y se asigno a acuartas (Owner). Reversible. GAP: los otros 12 usuarios BITCODE siguen
sin vista (falta un seed/reconcile de vista Completo IsDefault por tenant real).

**Parte 2 - diferidos de endurecimiento (Ola 7), AMBOS resueltos:**
- **Policies COMPUESTAS por vista** (`f9ea27b`): el motor `Perm:{mod}:{accion}` (ADR-0033) ahora
  soporta AND multi-permiso (`Perm:m1:a1+m2:a2` -> varios `PermissionRequirement`). La familia Tareas
  dejo de ser placeholder de `tenant_id`: `Tareas.Ver`/`Proyectos.Ver`/`Flujos.Ver` exigen el permiso
  real; `Formularios.Disenar` es COMPUESTA (ver+editar). Sin tocar paginas. 26/26 unit tests verdes.
- **Entrega REAL de notificaciones in-app** (`ef9ef06`): entidad `Notification` (tenant-scoped, por
  usuario, leido/no leido) + `INotificationService` + migracion `AddNotifications` (PG local).
  `AssignAsync` entrega notificacion al encargado (TaskAssigned) y a los destinatarios del concepto
  (ConceptNotice), en la misma transaccion. La campana del topbar paso de placeholder a badge REAL con
  conteo -> pagina `/notificaciones` (marcar leida / todas / abrir). Test integracion verde + validado
  en Chrome (badge 2, marcar leida -> 1 no leida en BD).

**Backlog Ola 7 (documentado)**: canal EMAIL de notificaciones con plantilla (IEmailSender ya existe),
refresco en vivo del badge por SignalR, policies de gobierno (AdmUsuarios/RolesPermisos/ConfiguracionMenu
a Owner/Admin) y Conceptos.Editar/Dependencias.Ver.
**Pendiente operativo**: desplegar a prod la migracion `AddNotifications` (se suma a las 4 acumuladas).

---

## Sesion 2026-07-11 (cont.) - Proyectos P1 (hitos) + P3 (enlace actividad-hito)

**Agentes**: Claude (Opus 4.8). Descubrimiento clave (auditoria): el modulo Proyectos YA estaba casi
completo (entidad `Project` + `ProjectMember` ACL + `ProjectService` + UI lista/detalle con kanban +
seed PRJ-001, todo en la migracion AddTaskCore). Solo faltaban los HITOS (P1) y su consumo (P3) -- por
eso el usuario pidio "P1 y P3", no P2.

**P1 (commit `1dd1b90`)**: entidad `ProjectMilestone` (tenant-scoped: ProjectId/Name/DueDate?/SortOrder/
IsCompleted) + migracion `AddProjectMilestones` (PG local); `ProjectService` List/Add/Update/
SetCompleted/RemoveMilestone (+ `ProjectMilestoneDto` con TaskCount; Remove bloquea si hay actividades
enlazadas); panel "Hitos del proyecto" en `ProyectoDetalle` (agregar con fecha / completar / quitar /
conteo por hito); seed idempotente de 2 hitos para PRJ-001 (verificado que corre en BD nueva).
Presupuesto/costos/DOFA quedan en backlog.

**P3 (commit `1dd1b90`)**: `TaskItem.MilestoneId` (FK nullable Restrict) + DTOs; `TaskItemService`
valida hito<->proyecto y persiste; filtro por hito; summary con `MilestoneName`. El selector de Hito del
wizard (antes placeholder) carga los hitos del proyecto elegido (`OnProjectChanged`) y pasa `MilestoneId`
al crear. La actividad aparece en el tablero del proyecto (kanban por ProjectId, ya existente; SignalR
vivo) y suma al conteo del hito.

**Pruebas**: test integracion `CreateActivity_LinkedToProjectMilestone_IsPersisted_AndCrossProjectRejected`
(verde, PG). Validado en Chrome (owner@sky-system.local): panel de hitos OK; el wizard carga los hitos al
elegir PRJ-001; T00212 creada con PRJ-001 + "Kickoff y alcance" aparece en el tablero del proyecto y el
hito muestra "1 act.".

**Pendiente operativo**: desplegar `AddProjectMilestones` a prod (se suma a las acumuladas). Backlog:
presupuesto/costos/DOFA del proyecto; timeline/calendario del proyecto; SqlServer DAL-dual.

---

## Sesion 2026-07-11 (cont.) - DESPLIEGUE A PRODUCCION (Actividades Olas 1-7 + Proyectos P1/P3)

**Agentes**: Claude (Opus 4.8). **Accion**: despliegue a prod (`root@10.0.0.3`, `/opt/ecorex`,
build-from-git de `fase-0/clon-backbone` @ `877baa4`).

- **Backup previo**: `./backup.sh` -> `backups/ecorex-2026-07-11-1631.sql.gz`.
- **Rebuild**: `docker compose -f docker-compose.from-git.yml -p ecorex-prod build --no-cache` (trae el
  ultimo `fase-0/clon-backbone`) + `up -d`.
- **Migraciones aplicadas al arranque** (prod estaba en `AddDataModelContainers`): `AddEntidadConfig`,
  `AddEntidadKind`, `AddJefeMemberAndProcessGroupMenu`, `TaskItemConceptoBridge`, `AddNotifications`,
  `AddProjectMilestones` (6). Verificado: tablas `notifications`, `project_milestones`, `entidades`
  creadas; `__EFMigrationsHistory` al dia. App sana: HTTP 200 en `/login`, sin errores en log.

**Con esto, Actividades (Olas 1-7) + Proyectos (P1 hitos + P3 enlace) quedan EN PRODUCCION.**

**Pendientes tras el deploy (inventario en el vault doc 03):** (1) cablear la config de Conceptos en
prod via el editor (flujo/form/tablero por subcategoria; el seed demo NO corre en prod); (2) vistas de
menu de los demas usuarios reales (BITCODE); (3) DAL-dual SQL Server; (4) backlog endurecimiento (email+
plantilla, badge en vivo por SignalR, policies de gobierno) y de Proyectos (presupuesto/costos/DOFA,
timeline/calendario).

---

## Sesion 2026-07-12 - QA tipo usuario del tablero + fix "crear-asignada no notificaba" + deploy

**Agentes**: Claude (Opus 4.8). **Accion**: inspeccion QA end-to-end del tablero de actividades (via MCP
Chrome), bugfix hallado + desplegar a prod la cola acumulada.

**QA del tablero** (tenant demo, `owner@sky-system.local`): se creo una tarea NORMAL sin proceso
(Actividad = "Automatico") asignada a OTRO usuario (Operator SKY), y se recorrio todo el ciclo:
- **Lista (tabla)**: fila con TAREA/ESTADO/ASIGNADO/PRIORIDAD/PROGRESO/FECHA -> OK.
- **Tablero (tarjeta)**: tarjeta con titulo/avatar/barra -> OK.
- **Mover entre columnas**: menu tarjeta -> "Mover a" -> *Por hacer -> En progreso*, reflejado en Lista -> OK.
- **Comentarios**: anotacion agregada y visible en el feed -> OK.
- **Subtareas (checklist, ADR-0020)**: 2 items, marcar uno -> progreso 1/2 -> OK.
- **Gantt**: la tarea aparece con su progreso -> OK. Cada paso verificado ademas contra la BD.

**Bug encontrado y corregido**: `TaskItemService.CreateAsync` fijaba el encargado pero NO entregaba
notificacion (a diferencia de `AssignAsync`). Una tarea que NACE asignada (quick-create del tablero o
wizard con encargado) dejaba al asignado SIN notificacion in-app, SIN email y SIN badge SignalR. Se
replico la entrega de `AssignAsync`: notificacion `TaskAssigned` + traza + email best-effort + broadcast,
y entrega REAL a los destinatarios del concepto (antes solo dejaba traza). Test dual nuevo
`Create_BornAssigned_NotifiesAndEmailsAssignee` (6/6 verde PG+SqlServer). Verificado en la app: T00214
(post-fix) genera la notificacion al operator; T00213 (pre-fix) tenia 0.

**Deploy a prod** (`root@10.0.0.3`, `/opt/ecorex`, build-from-git de `fase-0/clon-backbone` @ `948a31e`):
el esquema de prod YA estaba en `AddProjectBudgetAndDofa` (deploy previo de la cola P2/endurecimiento),
asi que este redeploy shippeo SOLO el codigo de este fix (capa de servicio, SIN migracion nueva). Backup
previo (`backups/ecorex-2026-07-12-0841.sql.gz`), `build --no-cache` + `up -d`, arranque limpio
("Now listening"/"Application started", sin errores), `/login` y `/` -> HTTP 200. Puerto host `5480`.

**Pendiente**: refrescar en el vault (doc 03) el inventario; backlog post-v1 (form multimedia, vista
cliente-final, satelites legacy) sigue diferido a la fase de formularios avanzada.

---

## Sesion 2026-07-12 (cont.) - QA de flujos end-to-end + DECISION: retirar la bandeja "Mis pasos" (ADR-0038)

**Agentes**: Claude (Opus 4.8). **Accion**: QA por MCP Chrome de un proceso de compras completo +
formalizacion de una decision de arquitectura (solo documentacion, sin codigo de retiro aun).

**QA del runtime (contra BD local)**: se caligro por el editor bpmn-js (via puente E2E extendido) el flujo
**"Proceso de Compras"** (Inicio -> Aprobacion jefe de compras -> compuerta Aprobada? -> Generar orden de
compra -> Fin; rama Rechazada), se asignaron cargos por nodo (Aprobador, Asesor Comercial) en Dependencias,
se publico y se ligo al concepto **"Solicitud de compra"** (categoria Compras). Se creo la actividad
**T00215** y el flujo corrio de punta a punta **owner -> admin (Aprobador) -> operator (Asesor Comercial)**:
routing por cargo OK, reclamar/atender/completar OK, compuerta auto-resuelta (ADR-0037), caso CERRADO sin
estancarse (tarea Done / instancia Completed). Hallazgos: (a) el nodo "Decision" no ofrece Aprobar/Rechazar
explicito (solo Completar + comentario; la compuerta resuelve sola); (b) el `approval_comment` no persistio;
(c) el panel de atencion no permite adjuntar archivos (los adjuntos viven solo en el detalle, y son nombre+URL).

**DECISION formalizada (ADR-0038)**: se **RETIRA la bandeja standalone "Mis pasos"** (`/mis-pasos`,
`MisPasos.razor`, item de menu 000637, policy `MisPasos.Ver`). Los flujos se ejecutan **DENTRO de la tarea**
(seccion "Flujo" de `TaskDetailModal`); el paso pendiente se **descubre en el TABLERO ("mis pendientes")**.
ADR-0036 quedo anotada como parcialmente supersedida. Docs actualizados: **ADR-0038** (repo) + vault Capa 2
docs 00/01/03 + backlog `Pendientes y deudas tecnicas.md`.

**Pendiente de CODIGO (documentado, NO hecho en esta sesion)**: extender `ActivityBoardService.ApplyScope`
(alcance "Mine" incluye tareas con paso actual ruteado al usuario por asignado/cargo) + retirar la
pagina/ruta/menu 000637/policy `MisPasos.Ver`, conservando `IWorkflowInboxService`.

---

## Sesion 2026-07-12 (cont.) - IMPLEMENTACION ADR-0038: retiro de la bandeja "Mis pasos" + tablero como bandeja

**Agentes**: Claude (Opus 4.8). **Accion**: ejecutar la ola de codigo de ADR-0038 (el usuario aprobo "si dale")
y validar en Chrome que no queda nada de "Mis pasos".

**R1 - Retiro de la bandeja**:
- Borradas `MisPasos.razor` + `.css`. Quitada la policy `MisPasos.Ver` (`Program.cs`).
- Quitadas las 3 siembras de menu del item "Mis pasos"/000637 (`DatabaseSeeder`: 2 vistas por defecto +
  la reconciliacion). La reconciliacion idempotente de alta se cambio por `RemoveMenuItemByRouteAsync`,
  que RETIRA el nodo de los tenants ya sembrados al arrancar (log: "item 'mis-pasos' RETIRADO de 2 vista(s)").
- Borrado el E2E obsoleto `WorkflowInboxTests.cs` (navegaba a `/mis-pasos`). Se conserva
  `IWorkflowInboxService` (lo consumen el detalle de la tarea y el filtro del tablero).

**R2 - El tablero es la bandeja**: `ActivityBoardService.ApplyScope` (alcance Mine) ahora incluye ademas
las tareas cuyo PASO ACTUAL del flujo (current+Pending, instancia Running) esta ruteado al usuario: asignado
directo, o candidato por CARGO (via `INodeAssigneeResolver`, resuelto en memoria y cacheado por nodo, porque
no es SQL puro). El set se precomputa (`GetRoutedTaskIdsAsync`) y se inyecta al query. Se inyecto
`INodeAssigneeResolver` en el servicio.

**Validacion (Chrome + BD + tests)**: menu SIN "Mis pasos" (BD: 0 nodos route=mis-pasos); `/mis-pasos` ya no
renderiza (NotFound). En el tablero "Comercial - Requerimiento Infraestructura", operator@ ve **"Pendientes
mios = 26"** = 5 por asignacion + 21 ruteadas por cargo (Requerimiento -> Asesor Comercial, sin reclamar),
26 filas en Lista. Build de la solucion verde; `ActivityBoardTests` 12/12 (dual PG+SqlServer).

**Nota**: no desplegado a prod en esta sesion (el seeder retirara el item 000637 en el proximo deploy).

---

## Sesion 2026-07-13 - Paso corto: "Mis Procesos" -> tablero del concepto + modal rapido precargado

**Agentes**: Claude (Opus 4.8). **Accion**: al entrar por "Mis Procesos", abrir el TABLERO parametrizado
en el concepto y auto-abrir el modal "Nueva actividad" precargado con la (cat)/subcategoria del concepto.

- Ya existia: el concepto guarda `ActividadSubcategoria.TaskBoardId`; el menu enlaza a `/actividades?sub=`
  y la pagina resuelve el tablero del concepto (`Actividades.razor`).
- **Nuevo** (decision del usuario: usar el MODAL RAPIDO del tablero): `Actividades.razor` pasa el sub al
  detalle (`PresetSubcategoriaId`); `ActivityBoardDetail` incluye el concepto-proceso en el dropdown del
  modal rapido (antes solo no-proceso, Ola 6) y, al aterrizar via `?sub=`, AUTO-ABRE el modal precargado
  con el concepto (`OpenQuickCreate` + `_qcSubId`).
- **Validado en Chrome**: `/actividades?sub=<Cotizacion de equipos>` abre el tablero "Comercial -
  Requerimiento Infraestructura" y el modal rapido queda abierto con "Cotizacion de equipos"
  preseleccionado. Build verde.
- **Pendiente (siguiente paso corto)**: si el concepto NO tiene tablero (`TaskBoardId` null) hoy cae al
  indice; definir fallback (exigir tablero en Conceptos, o abrir el wizard). Nota de proceso: el modal
  rapido no tiene paso "Formulario" (form-first); para conceptos con formulario habra que iterar.

**Correccion (2026-07-13, feedback del usuario "unificar todo a Crear actividad")**: al aterrizar desde
"Mis Procesos" NO se abre el modal RAPIDO sino el **WIZARD grande "Nueva actividad" (4 pasos, form-first)**
precargado con el concepto. `ActivityBoardDetail` monta `<TaskWizard @ref=...>` y en `OnAfterRenderAsync`
llama `OpenAsync(presetSubcategoriaId)`; `OnCreated` recarga el tablero. Se revirtio el parche del dropdown
del rapido (vuelve a ser solo no-proceso). Ademas se unifico el NAMING de la creacion del tablero a
"actividad" (boton/modal/commit/header/toast). Validado en el Chrome del usuario: abre el wizard grande con
Proceso=Comercial + Actividad=Cotizacion de equipos preseleccionados (screenshot). Build verde.
## Sesion 2026-07-13 - Formularios avanzados F6 (transversales): cierre de mascaras + captura Tier 2

**Agentes**: Claude (Opus 4.8), worktree `funny-bell-3f8562` (rama `claude/briefing-worktree-formularios-f50017`).
**Contexto**: trabajo contra BD local `ecorex_forms` (no se despliega a prod hasta que el usuario lo indique;
el agente codea todo, incluidas migraciones, y documenta el esquema en el vault doc 04 para la sesion principal).

**Cerrados los 2 pendientes de F6 (sin migracion nueva: reutilizan columnas F6 ya existentes
`format`, `default_dynamic`, `field_visibility_json`)**:
- **Mascaras de entrada** (`DynamicFormRenderer.MaskInput`): en campos de texto la mascara reformatea al
  perder el foco. `phone` -> `(300) 123-4567`; `document` -> `900,123,456`. El valor GUARDADO queda crudo
  (la mascara es solo presentacion). Opciones "Telefono (mascara)" y "Documento (miles)" agregadas al
  dropdown Formato del disenador (pestana Datos).
- **Captura Tier 2 real** (`form-capture.js` + fragments en el renderer): **firma** en canvas -> dataURL PNG
  inline; **GPS** via `navigator.geolocation`; **archivo/foto** -> data-URI inline con tope de 1 MB
  (almacenamiento de objetos para adjuntos grandes queda como incremento posterior). Se cargo el script en
  `App.razor` y se agregaron estilos en `DynamicFormRenderer.razor.css`.

**Prueba E2E via MCP de Chrome** (usuario `completo@sky-system.local`, rol **Advisor**): se creo el
formulario **FRM-022** DESDE el disenador (drag del palette + Formato=phone persistido), se sembraron 7
campos F6 adicionales, se **Activo** y se lleno en Vista previa (modo Fill). Validado y persistido en BD:
mascara phone `3001234567`->`(300) 123-4567` (guarda crudo), mascara document `900123456`->`900,123,456`,
default dinamico Hoy = `2026-07-13`, firma dataURL PNG (4358 chars), GPS (cableado OK; permiso denegado en
sandbox), archivo data-URI PNG con preview, **solo-lectura por rol** (notas dentro de `fieldset[disabled]`
para Advisor) y **oculto por rol** (campo "codigo interno" no renderiza para Advisor). Envio -> "Enviado".

**Diferido explicitamente (NO construido, el usuario analiza la documentacion)**: webhooks / botones con
reglas de accion e integraciones por reflexion (verbos tipados). Tambien diferido: PDF con plantilla y
almacenamiento de objetos para adjuntos grandes.

**Siguiente**: marcar F6 completado en el vault (docs 00/03); a la espera de "ok deploy" del usuario para
que la sesion principal aplique a prod (no hay migracion nueva de F6, solo codigo).

---

## Sesion 2026-07-13 (cont.) - Impresion basica del registro (print / PDF nativo)

**Agentes**: Claude (Opus 4.8), worktree `funny-bell-3f8562`. **Sin migracion, sin dependencias nuevas.**

Version BASICA de la "impresion/PDF" de F6 (la avanzada con plantilla + object storage sigue diferida):
- **Pagina `/formularios/imprimir/{responseId}`** (`FormPrint.razor`, `EmptyLayout`, sin chrome): carga el
  registro + su definicion y arma un **documento limpio de solo lectura** (cabecera con titulo/codigo/
  numero/estado/fecha + filas etiqueta->valor). Respeta formato y mascara (phone/document/moneda/%),
  fecha `dd/MM/yyyy`, opciones->etiqueta, multi-check, toggle Si/No, **firma e imagen adjunta inline**
  (`<img>` del data-URI), **grid como tabla**, y **lookup resuelto a su etiqueta** (no el id). Al cargar
  lanza el **dialogo NATIVO** del navegador (`window.print()`) -> Imprimir o **Guardar como PDF**.
- **Boton "Imprimir"** en el footer del `DynamicFormRenderer` (solo sesion logueada; el visor publico no
  tiene tenant) que abre esa pagina en pestana nueva.

**Verificado E2E via Chrome** (FRM-022, `/formularios/imprimir/019f5c5d...`): documento con 8 filas ->
telefono `(300) 123-4567`, NIT `900,123,456`, fecha `13/07/2026`, firma e imagen adjunta como `<img>`,
GPS y notas OK. El boton "Imprimir" aparece en la vista previa apuntando a `/formularios/imprimir/{id}`.

**Diferido**: PDF con plantilla de servidor (logo/layout) + object storage para guardar/adjuntar el PDF.

---

## Sesion 2026-07-13 (cont.) - Integracion Formularios avanzados a fase-0/clon-backbone + DEPLOY a prod

**Agentes**: Claude (Opus 4.8, fork de la rama principal). **Accion**: integrar el trabajo del worktree
`claude/briefing-worktree-formularios-f50017` (Formularios avanzados F1-F6 + impresion, 16 commits) a
`fase-0/clon-backbone` y desplegar a prod.

- **Merge** (`3e33029`, base `1b97bca`): unico conflicto PROGRESO.md (se conservaron ambas entradas);
  ModelSnapshot PG+SqlServer auto-mergeados (quedaron `AddProjectBudgetAndDofa` + las 7 migraciones de
  formularios en orden). No se regenero ninguna migracion. Mi trabajo de la sesion (ADR-0038/Mis
  Procesos/wizard) preservado.
- **Build Release** de la solucion verde; **395 unit + 88 integracion** (16 de formularios + 72
  mios/reglas) verdes; Testcontainers aplico las 7 migraciones sobre contenedor fresco.
- **E2E en Chrome (del usuario)**, BD `ecorex_dev` con las 7 migraciones aplicadas al arrancar: crear
  formulario FRM-021, marcarlo transaccional con **Consecutivo del sistema**, llenar+enviar ->
  registro **FRM-021-000001** (Confirmed); marcarlo **modulo** -> nodo de menu "Formulario nuevo"
  (/m/FRM-021) + **bandeja** (KPIs, filtros, export Excel/CSV, columnas configuradas, el registro);
  impresion F6 OK. El diseñador expone los 13 tipos de objeto + los 3 modos de identidad + permisos por
  campo.
- **DEPLOY a prod** (`root@10.0.0.3`, `/opt/ecorex`, build-from-git de `fase-0/clon-backbone` @ `3e33029`):
  backup previo (`backups/ecorex-2026-07-13-1536.sql.gz`), `build --no-cache` + `up -d`. Al arrancar
  aplico las **7 migraciones de formularios** (prod estaba en `AddProjectBudgetAndDofa`). Verificado:
  `form_record_link` creada, `/login` HTTP 200. **Formularios avanzados EN PRODUCCION.**

**Diferido (decision del usuario, no construido)**: botones con reglas de accion + integraciones
(Siigo/WhatsApp/correo), PDF con plantilla de servidor + object storage, captura real de codigo de
barras/audio, y refinamientos menores (KPIs configurables, policies tipadas Form.{code}.*, etc.).

**Ajuste (2026-07-13): "solo el wizard" en el tablero.** Los 3 botones de crear del tablero (+Actividad,
+Anadir actividad por columna, celda de calendario) ahora abren el WIZARD grande (4 pasos), NO el modal
rapido. `TaskWizard.OpenAsync` gana `presetBoardId`/`presetColumnId` y los pasa al `CreateTaskItemRequest`
(BoardId/ColumnId) para que la actividad caiga EN ese tablero (`CreateAsync` respeta `request.BoardId`).
El modal rapido (`ab-quick-modal`, `OpenQuickCreate`) queda SIN USO -> limpieza pendiente. Ademas se
integro el merge de Formularios avanzados (7 migraciones) y la solucion compila unida (0 errores).
Validado en el Chrome del usuario: +Actividad abre el wizard, el modal pequeno no aparece.

**Menu (2026-07-14): reorganizar "Mis Procesos".** (A) Las categorias-proceso dinamicas (IsProcessGroup)
ahora se agrupan bajo un nodo colapsable "Procesos" en NavMenu (antes iban sueltas arriba del cuerpo).
(B) El item del formulario-modulo "Formulario nuevo" (/m/FRM-021) se movio a una carpeta nueva "Documentos"
bajo misproc (via SQL local, TEMPORAL -> no esta en el seed). Validado en el Chrome del usuario.
Pendiente de decision: quitar/ocultar "Crear una actividad" (000038, redundante con el wizard) y el
subgrupo estatico "Comercial" (sg-comercial) que no estan en el target del usuario.

**Menu (2026-07-14, cont.):** (1) Quitados los CODIGOS NUMERICOS (legacy_code) de todos los items del
menu lateral (NavMenu, deja solo el badge "proc" y los contadores). (2) "Mis Procesos" = solo el grupo
dinamico "Procesos" + Proyectos + Administrar actividades + Programar actividad: se retiro "Crear una
actividad" (creacion unificada al wizard) y el subgrupo estatico "Comercial" (sg-comercial). Permanente y
para TODOS los tenants via seeder: se quito del seed estatico (2 vistas) + reconciliacion
RemoveMenuItemByRouteAsync("crear-actividad") y nueva RemoveMenuSubtreeByRouteAsync("sg-comercial").
(3) La carpeta "Documentos" (con el formulario-modulo /m/FRM-021) se subio a nivel top-level (Section) via
SQL local -> demo-especifico, no en el seed. Validado en el Chrome del usuario (build verde, seeder OK).

**Conceptos<->Tableros (2026-07-14):** (1) "Actividades" (indice de tableros) retirado de Sistema.General
(redundante con Mis Procesos > Administrar actividades); seed + reconciliacion scopeada por seccion. (2) Se
enlazo CADA concepto (subcategoria) a SU tablero via la relacion existente TaskBoardId (1:1): para cada
concepto sin tablero se creo un tablero dedicado (columnas default Por hacer/En progreso/En revision/
Completado + code CNC-xxxxxx) y se fijo task_board_id + task_board_column_id (columna "Completado" = cierre).
Hecho por SQL local (demo). Validado en Chrome: "Compra urgente" ahora abre su tablero + wizard (antes caia
al indice). PENDIENTE de decision para PERMANENCIA/todos los tenants: (a) auto-crear el tablero al crear/
guardar un concepto (ActividadCatalogoService), o (b) reconciliacion en el seeder; y opcional "+ Crear
tablero" en la seccion "Tablero y estado de cierre" de Conceptos.

**Auto-tablero por concepto (2026-07-14):** `ActividadCatalogoService` ahora, al crear/editar una
subcategoria (concepto) que quede SIN tablero, CREA uno dedicado y lo enlaza (helper `EnsureConceptBoardAsync`:
TaskBoard kind=Activities + code CNC-xxxxxxxxxxxx + 4 columnas default; fija task_board_id + task_board_column_id
= "Completado"). Asi la creacion del tablero vive DENTRO de Conceptos, es permanente y para todos los tenants,
y ningun concepto queda huerfano. Validado en Chrome: concepto nuevo "QA auto-tablero" nacio con su tablero
(4 columnas + cierre). Build verde.

**Backfill de tenants y menus en PRODUCCION (2026-07-14, por SQL directo):** a peticion del usuario y con su
autorizacion explicita, se corrigio el estado de las EMPRESAS CLIENTE en la BD de produccion (10.0.0.3, DB
`ecorex`). Diagnostico previo: los tenants nuevos NACEN SIN MENU (el seeder solo siembra vistas para el tenant
Demo), por eso AGROMETALICAS y PLATAFORMA ECOREX tenian 0 vistas de menu y sus usuarios no veian nada; BITCODE
tenia "Completo" pero SIN marcar como IsDefault y 12/13 usuarios sin vista asignada; SKY SYSTEM arrastraba 4
vistas basura "E2E ..." de pruebas. Acciones (backup `ecorex-2026-07-14-1521.sql.gz` primero; todo en UNA
transaccion e idempotente):

1. Nombres de tenant a MAYUSCULA (convencion): `Plataforma ECOREX` -> PLATAFORMA ECOREX, `agrometalicas` -> AGROMETALICAS.
2. Alta de 2 tenants cliente nuevos: **SOLDARCO** y **CHUZO DE IVAN** (Kind=Standard, Status=Active).
3. Se borraron las 4 vistas basura "E2E ..." (+ sus 8 nodos) de SKY SYSTEM.
4. Se sembro la vista "Completo" (arbol canonico de 70 nodos, clonado del de SKY SYSTEM preservando la
   jerarquia con remapeo de ids) a los 4 tenants que no tenian NINGUNA: AGROMETALICAS, PLATAFORMA ECOREX,
   SOLDARCO, CHUZO DE IVAN.
5. "Completo" quedo IsDefault=true en TODOS los tenants (BITCODE incluido).
6. Los 30 usuarios sin vista quedaron asignados a la "Completo" de SU tenant (se respeto el usuario demo
   "Perfil Simple" de SKY SYSTEM, cuya vista reducida es intencional).

Resultado verificado: 6 tenants, 0 usuarios sin menu, 70 nodos "Completo" en cada uno.

> ADVERTENCIA (excepcion a la regla inviolable #5): este backfill se hizo por **SQL directo**, no por
> PlatformAdmin, por lo que **NO quedo traza en `AdminAuditLog`**. Excepcion autorizada explicitamente por el
> usuario para esta operacion puntual. Esta entrada de PROGRESO.md es su unica traza.

**PENDIENTE (causa raiz, en curso en otra sesion):** `IMenuProvisioningService.EnsureDefaultMenuAsync(tenantId)`
implementado por `DatabaseSeeder` y llamado desde `TenantAdminService.CreateAsync` y `OnboardingService`, para
que NINGUN tenant futuro nazca sin menu, + normalizacion del nombre a MAYUSCULA en el alta. Mientras eso no
este desplegado, todo tenant creado desde la UI hay que sembrarlo a mano.

**Migracion de usuarios SOLDARCO desde db3dev (2026-07-14, por SQL directo):** se dieron de alta en PRODUCCION
los usuarios de la sucursal `02` = SOLDARCO del legacy (db3dev, SQL Server, SOLO LECTURA). Origen: tabla
`USUARIO WHERE SUCURSAL='02'` (30 filas). Reglas de depuracion (mismas del onboarding 2026-07-09 + saltar
inactivos y filas de prueba): correo = login, clave = `ID_USUARIO` (cedula), rol Owner, vista Completo.
- **25 creados**, **5 omitidos**: 1 INACTIVO en legacy (Carlos.Rivera, FLAG_INAC=1), 2 de PRUEBA
  (NUEVO.USUARIO.02, USUARIO.NUEVO.03), 2 correos DUPLICADOS en el lote (Soldarco-empresa y VALLEJO.ALEXANDER;
  `administracion@` quedo para el activo Saul.Leon y `almacen@` para Rosas.Victor).
- Hash de clave: PBKDF2-SHA256 formato `v1.100000.{salt}.{key}` reproducido byte a byte con el de
  `Pbkdf2PasswordHasher` (validado con LOGIN REAL via Chrome MCP: `recepcion@soldarco.com` + cedula ->
  entro a SOLDARCO con el menu Completo, display_name con acentos correcto).
- Los 25 quedaron Owner/Active con vista Completo (70 nodos). Cada usuario debe cambiar su clave al primer ingreso.
- Correos con `@bitcode.com.co` (5) son personal de Bitcode registrado en la sucursal 02; se migraron a SOLDARCO
  por pertenecer a esa sucursal.

> Igual que el backfill de tenants: hecho por **SQL directo**, SIN traza en `AdminAuditLog` (excepcion
> autorizada). El mecanismo auditado equivalente es `LegacyOnboardingSeeder` (ECOREX_RUN_ONBOARDING), que hoy
> solo cubre sucursales '01' y '00136'; si se quiere repetir por la via auditada, extenderlo a '02'.
> VALLEJO.ALEXANDER (real, comparte `almacen@soldarco.com`) quedo sin cuenta: necesita un correo propio.

**Altas adicionales en PRODUCCION (2026-07-14, por SQL directo, sin AdminAuditLog):**
- **CHUZO DE IVAN**: 1er usuario `sml1144@hotmail.com` (Samantha Mora, clave = cedula 3244433514), Owner + menu Completo.
- **EPRING** (tenant NUEVO, legacy sucursal `00004`): creado + menu Completo clonado + sus **7 usuarios** Owner.
  Decisiones del usuario: (a) `agente@epring.com.co` es cuenta de AGENTE (no persona) y se creo con su
  literal `EPRING888` como clave (no es cedula); (b) 2 usuarios que en el legacy tenian correo
  `@equipelco.com` (Lady Johanna Perlaza, Juan Camilo Pineda) recibieron un login DERIVADO de su cedula
  sobre el dominio de la casa: `<cedula>@epring.co`; la clave sigue siendo la cedula.
- Se descarto INGETEL (el usuario se habia equivocado de empresa). Existe en el legacy como sucursal
  `00079` con 534 usuarios, por si alguna vez se migra.
- Nota de estado: el menu canonico "Completo" hoy tiene **64 nodos** (no 70): el seeder reconcilio y
  retiro `crear-actividad`, el subgrupo `sg-comercial` y `Actividades` en TODOS los tenants. Verificado:
  los 7 tenants tienen 64 nodos, consistentes.

**CORRECCION IMPORTANTE - SOLDARCO NO sale de db3dev (2026-07-14):** el cargue de 25 usuarios desde
db3dev sucursal 02 fue del **SERVIDOR EQUIVOCADO**. SOLDARCO tiene su propio servidor:
`192.168.0.8` / BD **`M700_GEN`** / tabla `USUARIO` (alli SOLDARCO es la sucursal `01`). La cadena de
conexion NO va al repo (dato del usuario, fuera de control de versiones).
Se rehizo el padron en UNA transaccion (backup `ecorex-2026-07-16-0858.sql.gz` antes):
1. Se verifico primero que los 25 usuarios viejos NO tenian datos asociados (0 filas en las 8 tablas
   con FK a `tenant_users`: notifications, task_items, assignments, work_logs, projects, etc.).
2. Se borraron sus 25 membresias + sus 25 `platform_users` **huerfanos** (solo los que no quedaban en
   ninguna otra empresa y sin `platform_role`), para no tocar a nadie de otro tenant. Huerfanos = 0 al final.
3. Se cargaron los **19** del padron correcto (Owner + menu Completo). De 25 filas origen: 19 creadas y
   6 imposibles (sin correo NI cedula: no hay login ni clave). Decisiones del usuario: `almacen@` para
   Vallejo (Wilson Mejia -> login por cedula); Hector F. Brinez tenia el correo invertido
   (`soldarco@comercial6`) -> login por cedula; DTRUESTAR (externo, NIT) se creo igual.
4. `acuartas@bitcode.com.co` YA existia (BITCODE): no se duplico, se **vinculo** el mismo platform_user
   a SOLDARCO -> primer caso real de multi-tenant (1 usuario en 2 empresas). Conserva su clave.
BITCODE quedo intacto (13 usuarios).

**Cabezotes de formularios de SOLDARCO migrados (2026-07-14, por SQL directo):** primer ETL de contenido
(no solo usuarios) desde el sistema viejo de SOLDARCO. Origen: `M700_GEN.dbo.ENCUESTAS_MOV` (17 filas,
solo encabezado: CODIGO/TITULO/DESCRIPCION/VERSION/TIPO_FORMATO/SUCURSAL). Destino: `form_definitions`
del tenant SOLDARCO. Backup previo `ecorex-2026-07-16-1758.sql.gz`.
- **14 de 17 migrados.** Omitidos por decision del usuario: `00004` (PRUEBA) y `00019` (PRUEBA 00014)
  por ser pruebas, y `00009` por venir **anulado** en el origen.
- `code` = **`FRM-<codigo legacy>`** (FRM-00001 ... FRM-00026): respeta la convencion de la casa Y
  conserva el numero del sistema viejo, que es la clave por la que enlazan las preguntas
  (`ENCUESTAS_MOV_PREGUNTAS.ENCUESTA` = `ENCUESTAS_MOV.CODIGO`). Sirve para el siguiente paso.
- `title` = TAL CUAL del origen (decision del usuario), `status` = **Draft** para todos, revision 1.
  OJO: los titulos del legacy traen el ESTADO embebido ("terminado/desarrollo/construccion"), que en el
  modelo nuevo es el campo `status` aparte. Queda pendiente decidir si se limpian.
- La `VERSION` del legacy es texto libre (V01/V0/0.0/v0/V2.0) y NO se migro: `revision` es numerico.
- Idempotente por (tenant_id, code). Verificado: 14 filas con acentos correctos.

**PENDIENTE de este ETL:** las **preguntas** (`ENCUESTAS_MOV_PREGUNTAS` -> `form_questions`), que es lo
gordo (hasta 80 preguntas en FRM-00011) e implica mapear `TIPO_RESPUESTA` del legacy a `FormControlType`.
Se migraron SOLO los cabezotes: hoy los 14 formularios estan vacios. El usuario hara un fork de la rama
para trabajar el diseno.

**Carta de EL CHUZO DE IVAN cargada en items/inventarios (2026-07-21, por SQL directo):** primer cargue
de catalogo desde un PDF (no desde una BD). Fuente: `carta menu el chuzo de ivan.pdf` (4 paginas).
Backup previo `ecorex-2026-07-21-0807.sql.gz`.
- **15 `item_groups`** = las categorias de la carta (Carnes, Sandwiches, Maicitos, Perros, Arepas,
  Desgranados, Chorizos, Alitas, Chuzos, Papas, Aplastados, Tostadas, Hamburguesas, Porciones
  adicionales, Bebidas), con `sort_order` en el orden del PDF.
- **121 `items`** con nombre, descripcion (los ingredientes entre parentesis de la carta), `price` y
  su grupo. Un `item_type` "Producto". Todos `is_active`.
- Decisiones del usuario: (a) nombre **TAL CUAL la carta** (el grupo los diferencia); esto importa
  porque hay nombres repetidos entre categorias (POLLO aparece 6 veces, RANCHERO 4). (b) **SKU por
  categoria** correlativo: `CAR-001`, `SAN-001`, `HAM-022`, `BEB-026`... (el SKU es unico por tenant y
  es lo que garantiza la unicidad real).
- Verificado: 121 items, 0 sin grupo, 0 sin precio; rangos coherentes con el PDF (Carnes 35k-83k,
  Bebidas 4k-15k). Idempotente por (tenant_id, sku).
- Nota: el PDF es ASCII-imposible (tildes/enies en los ingredientes); en la BD se cargo texto sin
  tildes para mantener la convencion del proyecto, salvo donde el dato lo exigia.

**Catalogo y clientes de SKY SYSTEM cargados desde Excel (2026-07-21, por SQL directo):** fuente
`048. SKy System/Cotizador.xlsx` (hojas BASE_PRODUCTOS y BASE_CLIENTES; el resto del libro -SIMULADOR,
FORMATO_COTIZACION, SEGUIMIENTO_COTIZACIONES- es la herramienta de cotizacion, no se migro).
Backup previo `ecorex-2026-07-21-1609.sql.gz`. **No se creo ningun modulo**: se reusaron los existentes.
- **11 items** (inventarios) + **7 brands** (HP, LG, SAMSUNG, LENOVO, GENERICO, GEFORCE, ASUS).
  `sku` = CODIGO del Excel (IMP1, PANT2, LAPT8...), sin colision con los SKU demo (ITM*/E2E*/QA*).
  Se reusaron el grupo **Tecnologia** y el tipo **Producto** que ya existian en el tenant.
- **38 terceros** (modulo negocio) con perfil **Cliente** (`perfiles = 1`, TerceroPerfil.Cliente),
  estado Activo. Separados por decision del usuario: 33 Empresa / 5 Persona (BERNARDO AGUILERA,
  WILSON ARIAS, YIMMI NESSIM, CARLOS VARELA, DR ACEVEDO). `id_tipo = 'Ninguno'` porque el Excel NO
  trae NIT ni cedula.
- Decisiones del usuario: `price` = **COSTO con IVA** (el COSTO SIN IVA queda anotado en
  `specifications` junto con el proveedor y el stock del Excel, para no perder el dato); las tarifas
  por cliente (pasajes, parqueadero, tipo) se guardaron en `fichas_json` bajo la clave `cliente`,
  respetando la forma que ya usa la app (`{"cliente": {"campo":"valor"}}`).
- Idempotente: items por (tenant,sku), terceros por (tenant,upper(nombre)), brands por (tenant,name).
- PENDIENTE si se quiere: las existencias reales (STOCK del Excel) NO se cargaron en `item_stocks`
  porque eso exige crear una bodega; hoy el stock vive como anotacion en `specifications`.

**SKY SYSTEM fase 2 - campos dinamicos, stock real y limpieza E2E (2026-07-21, por SQL directo):**
a peticion de la sesion de FORMULARIOS (que creo el formulario "SIMULADOR DE COTIZACIONES", def
`59a91ffe-...`, code COT). Esta sesion hizo SOLO los datos (secciones A/B/C de su prompt); la
**seccion D (D6-D10: lookup en columna de tabla, IF/CEILING, referencia al encabezado, SUMIF,
plantilla de texto) es DESARROLLO del motor de formulas y NO se toco** - corresponde a una sesion de
codigo. Backup previo `ecorex-2026-07-21-1625.sql.gz`.

> **CORRECCION AL BRIEFING DE ESA SESION:** decia "BASE_PRODUCTOS (~1019)". Es **FALSO**: son
> **11 productos**. Los archivos `Cotizador.xlsx` y `Cotizador Formulario.xlsx` son **identicos**
> (mismo MD5 `56aefcceb299`) y BASE_PRODUCTOS tiene `max_row=1023` pero solo **11 filas con datos**;
> las otras 1009 son filas vacias con formato. El "~1019" salio de leer `max_row`, no los datos.
> Por tanto la decision "cargar todo o una muestra" era irrelevante: ya esta el 100% del catalogo.

- **A) Tercero**: creadas las 5 `TerceroFieldDefinition` de la ficha `cliente` con los codigos
  EXACTOS de la spec (`pasaje`, `parq_valor`, `mano_obra`, `margen_sky`, `tipo_parq` Select
  FIJO/X HORA) y **realineados los 38 `fichas_json`** a esos codigos (el primer cargue habia usado
  claves propias: pasajes/parqueadero/manoObra/...).
- **B) Item**: creadas las 3 `ItemFieldDefinition` del tipo Producto (`proveedor` Text,
  `costo_sin_iva` Number, `exento_iva` Select SI/NO; el Excel trae EXENTO IVA vacio -> todos NO) y
  cargado `field_values_json` de los 11. **Stock real** en `item_stocks` sobre **Bodega Central**
  (existente, decision del usuario): IMP1=5, el resto=1.
- **C) Limpieza E2E**: el residuo SI estaba en uso (cada marca/bodega E2E referenciada por su item y
  su stock), asi que "borrar marcas/bodegas pero conservar los 15 items" era contradictorio. El
  usuario decidio **borrar el residuo completo**: 6 items + 6 stocks + 6 marcas + 6 bodegas E2E.
  SKY SYSTEM queda con **20 items** (9 demo + 11 del Cotizador).
- **Defecto corregido en el acto:** las `options` de los Select quedaron con `\r` (Python escribio el
  .sql con saltos de Windows), lo que habria roto la coincidencia del valor ("FIJO\r" != "FIJO").
  Se limpiaron con `replace(options, chr(13), '')`. Verificado: 0 opciones con CR.

**Depuracion de formularios de SKY SYSTEM (2026-07-21, por SQL directo):** el usuario pidio dejar solo
"SIMULADOR DE COTIZACIONES". Alcance acotado con el a **SOLO el tenant SKY SYSTEM** (habia 67
formularios en 7 tenants; los demas NO se tocaron). Backup `ecorex-2026-07-21-1737.sql.gz`.
- **Borrados 19**: los 17 de basura E2E (`FRM-004`..`FRM-020`, todos "Formulario nuevo" con 1
  respuesta) + los demo `FRM-002` (Inventario fisico) y `FRM-003` (Visita tecnica). Se arrastraron
  18 respuestas, 43 campos y 3 contenedores.
- **CONSERVADOS y por que** (hallazgos que cambiaron el plan original de "borrar todo menos COT"):
  1. Los 5 `FRM-CRM-*` (Anotacion/Cotizacion/Oportunidad/PQR/Solicitud) los **siembra
     `DatabaseSeeder.EnsureFormAsync`** en CADA tenant y los usa el modulo de Contactos/CRM:
     borrarlos es inutil (vuelven al reiniciar) y deja el CRM sin formularios mientras tanto.
  2. `FRM-001` "Solicitud de cotizacion" esta **enganchado a configuracion viva**: al concepto
     "Requerimiento infraestructura" (`actividad_subcategorias`) y a **2 nodos de flujo**
     (`workflow_node_forms`). Borrarlo rompe ese concepto y esos nodos, asi que quedo EN PAUSA
     pendiente de decision del usuario. Sus 56 respuestas son datos demo (sembradas 2026-07-04/08).
- El DELETE llevo **guardas** que abortan la transaccion si algun concepto o nodo de flujo referencia
  a los formularios objetivo (dieron 0, como se esperaba tras excluir FRM-001).
- SKY SYSTEM queda con 7 formularios: COT + FRM-001 + los 5 del CRM.

**Menu de SOLDARCO replicado a SKY SYSTEM (2026-07-21, por SQL directo):** el usuario noto que el menu
de SOLDARCO quedo mejor organizado y pidio traerlo a SKY SYSTEM. Backup `ecorex-2026-07-21-1800.sql.gz`.
- Contexto: el 2026-07-14 se clono el menu canonico DESDE SKY hacia los demas tenants, pero despues
  **SOLDARCO se reorganizo a mano** (53 nodos / 11 raices, `updated_at` 2026-07-14 20:26) mientras SKY
  se quedo con la version vieja (61 nodos / 12 raices). Ahora SKY adopta la de SOLDARCO.
- Que mejora: SOLDARCO **consolida el CRM en UNA seccion**; SKY arrastraba "CRM (heredado)" Y
  "Sistema - CRM" por separado. Ademas reubica Power BI Service, Contenedor de datos y Plantillas a
  "Sistema - Desarrollo", y Extraccion de datos / Lista negra a "Infraestructura IA".
- **Se conservo el `id` de la vista "Completo" de SKY**, de modo que los **17 usuarios asignados NO se
  tocaron** (0 usuarios sin menu al final). La vista "Simple" (10 nodos, 1 usuario) quedo intacta.
- SKY perdio 7 entradas que SOLDARCO ya no tiene, todas **stubs heredados** del sistema viejo
  (`Autocompletado formularios` 000801, `Notificaciones` 000288, `Objetos del sistema` 000137,
  `Parametros XML` 000057, `Servicios web` 000053, `Consecutivos` 000136 y `Automatizaciones`).
  Decision del usuario: copiar SOLDARCO tal cual.
- Guarda previa: se verifico que **ningun formulario-modulo** (`form_definitions.module_menu_node_id`)
  dependiera de los nodos a borrar (0), y el DO block aborta la transaccion si aparece alguno.
- SOLDARCO quedo intacto (sigue con sus 53 nodos): la copia es unidireccional.

**Flujo "Proceso de compras" implementado en SKY SYSTEM (2026-07-21, por SQL directo):** importado de
`diagrama.bpmn` (Downloads del usuario). `process_code` **COMPRAS**, v1, categoria Compras,
**BORRADOR** (`is_published=false`, decision del usuario para revisarlo en el editor).
Backup `ecorex-2026-07-21-1900.sql.gz`. 12 nodos + 11 aristas + `bpmn_xml` completo.
- Ruta: Requerimiento -> Cotizacion a Proveedores -> Se aprueba proveedor y cotizacion ->
  compuerta "Aprobacion de cliente" -> [Aprobada] Se aprueba compra por el cliente -> 4 tareas de
  cierre; [Rechazada] -> compuerta "El cliente rechaza" -> [Negocio perdido] | [Reiniciar cotizacion].
- Las **8 anotaciones de texto** del BPMN se guardaron en `workflow_nodes.note` (instrucciones
  operativas: "debe llenar formulario de cotizacion y % de incremento", "orden de compra igual a la
  tarea", "formato de entrega con firma del cliente", etc.). No se perdio nada del diagrama.
- Condiciones de compuerta: el **nombre de la arista es el boton** que ve el usuario y el valor que se
  compara (`ApprovalOptions` -> `approvalResult` -> `WorkflowConditionEvaluator`). Quedaron
  `approval == 'Aprobada'` / `'Rechazada'` / `'Negocio perdido'` / `'Reiniciar cotizacion'`.
- **Unica adaptacion inevitable**: el diagrama traia un `intermediateThrowEvent` ("Reiniciar a cotizar
  nuevo proveedor") y el motor **solo soporta 4 tipos** (`BpmnXmlMerger.LocalName` lanza con cualquier
  otro): StartEvent, Task, ExclusiveGateway, EndEvent. Se modelo como **EndEvent con
  `restart_node_id`** apuntando a "Cotizacion a Proveedores" (decision del usuario), que es el
  mecanismo nativo del motor para reinicios.

> **PENDIENTE DE DESARROLLO (D11) - ejecucion en PARALELO.** El nodo "Se aprueba compra por el cliente"
> tiene **4 salidas simultaneas** (Recibe producto / Entrega para gestion de pago / Generar Factura /
> Ingreso a Alegra). El motor es de **UN SOLO TOKEN**: `WorkflowStartService.cs:135` toma
> `outgoing[0]` y `WorkflowEngine.ResolveOutgoing` hace lo mismo, asi que **hoy solo se ejecutaria 1
> de las 4 ramas y las otras 3 quedarian muertas**. Por decision del usuario el diagrama se dejo TAL
> CUAL para no falsear el proceso real; hay que implementar la bifurcacion paralela (ParallelGateway
> / multi-token con join) en una sesion de DESARROLLO. Mientras tanto el flujo NO debe publicarse.
> Relacionado: tampoco hay EndEvents en las 4 tareas de cierre ni en "Negocio Perdido" (el diagrama
> no los trae), asi que la instancia no cerraria; conviene resolverlo junto con el paralelismo.

**Limpieza de flujos E2E en SKY SYSTEM (2026-07-21, por SQL directo):** se borraron los **27 flujos
basura** de pruebas automatizadas ("E2E editor ..." y "E2E asignacion ..."), equivalente a la limpieza
de formularios. Backup `ecorex-2026-07-21-1930.sql.gz`. Arrastraron **81 nodos, 54 aristas y 5
politicas de nodo**. Verificado ANTES: 0 conceptos, 0 tipos de actividad, 0 instancias y 0 tareas
dependian de ellos; el DELETE lleva guardas que abortan la transaccion si aparece alguno.
SKY SYSTEM queda con 5 definiciones reales: COMPRAS v1 (borrador), COT-COM v1 (publicado) y v2
(borrador), FLW-001 (publicado) y VIS-TEC (publicado, pausado). 0 nodos/aristas huerfanos.

**Borradas las actividades dummy de SKY SYSTEM (2026-07-22, por SQL directo):** las **206 tareas**
del tenant demo, TODAS creadas entre 2026-07-04 y 2026-07-08 (siembra demo + corridas E2E); ninguna
posterior. Backup `ecorex-2026-07-22-0947.sql.gz`.
- Verificado ANTES: **ninguna pertenecia a un usuario real del cliente**; 193 estaban sin asignar y
  13 en cuentas demo del tenant. El DELETE lleva una **guarda** que aborta la transaccion si alguna
  actividad esta asignada a un usuario que no sea `*@sky-system.local`.
- Arrastro el rastro completo: 407 `task_item_activities`, 40 checklist items, 25 `task_work_logs`,
  11 tag assignments, 3 assignments, **76 `workflow_instances`** con sus 213 `workflow_step_histories`
  y 32 `form_flow_links`.
- Detalle tecnico: hay **referencia circular** `task_items.workflow_instance_id` <->
  `workflow_instances.task_item_id`; hubo que poner en NULL el lado de la tarea antes de borrar las
  instancias.
- Verificado DESPUES: 0 actividades en SKY, y 0 huerfanos en instancias, worklogs e historial de pasos.
  Los demas tenants no tenian ninguna actividad, asi que no se toco nada mas.

**Terceros demo borrados en BITCODE y AGROMETALICAS (2026-07-22, por SQL directo):** el usuario pidio
"limpiar" tambien SOLDARCO, BITCODE y AGROMETALICAS. Hallazgo al inventariar: **los tres ya tenian 0
actividades** (la limpieza de actividades solo aplicaba a SKY SYSTEM), y lo unico dummy que quedaba
eran los **terceros DEMO sembrados por la app el 2026-07-09** (ANDINA S.A.S, INGETEL, Produvarios,
Maria Fernanda Lopez, Roberto Salcedo) mas "fulano" en BITCODE.
- **Decision explicita del usuario: en SOLDARCO NO se tocan los terceros.** Sus 3 registros de prueba
  (ALEXANDER, CLIENTE DEMO CONCEPTOS, INDUSTRIAS TEST QA), sus 33 respuestas de formulario y sus notas
  quedaron INTACTOS, igual que los 14 cabezotes migrados de su sistema viejo.
- Borrados 11 terceros (6 BITCODE + 5 AGROMETALICAS) con su rastro: 12 `citas`, 10 `oportunidades` y
  6 `tercero_contactos`. Guarda en el DELETE que aborta si el conjunto alcanza otro tenant.
- Resultado: BITCODE y AGROMETALICAS con el directorio en 0, listos para sus contactos reales.

> PENDIENTE menor: **SKY SYSTEM sigue con los 5 terceros demo** (ANDINA S.A.S, INGETEL, Produvarios,
> Maria Fernanda Lopez, Roberto Salcedo) mezclados con los 38 clientes reales del Cotizador (43 en
> total). No se tocaron porque el usuario acoto la limpieza a los otros tres tenants.

**Contenedor de datos "GESTION COMERCIAL" en SOLDARCO (2026-07-23, por SQL directo):** el usuario pidio
crear un contenedor (DataModel) con varias tablas y llenarlas desde `MAESTRO ECOREX V2.xlsx` (SOLO la
hoja **TABLAS**). Es carga MANUAL de catalogos maestros para usarlos luego en el resto del sistema (no
hay ingesta automatica todavia). Backup `ecorex-2026-07-23-0912.sql.gz`. Modelo del modulo:
`data_models` (contenedor) -> `data_containers` (tablas, source_kind=Manual, con canvas_x/y para el
lienzo ER) -> `data_container_columns` -> `data_container_rows` + `data_container_cells`.
- **9 tablas, 142 filas** (todas columnas Text): Estado del ciclo de vida (9), Origen del cliente (9),
  Calificacion comercial (4), Atencion comercial (10), Canal de contacto (32), Frecuencia de compra
  (6), Sector economico (22), Nivel de organizacion (3), y la tabla puente **Canal por mercado** (47).
- Los nombres de columna se derivaron de los encabezados de cada bloque del Excel; la 1a columna
  (Codigo) quedo `is_required`.
- **Bug propio detectado y corregido en el acto:** la idempotencia por "valor de la 1a columna" fallo
  en la tabla puente (su "Codigo canal" se repite entre mercados) -> cargo 29 de 47. Se recargo esa
  tabla completa (delete + insert de las 47). Las otras 8 tienen codigo unico y cargaron bien al 1er
  intento. Verificado: los 9 conteos coinciden con el Excel; acentos correctos (Sector economico).
- NO se modifico codigo (peticion del usuario): solo INSERTs en el modulo existente.

**Lideres y organigrama de AGROMETALICAS (2026-07-24, por SQL directo):** cargados del Excel
`consolidado de lideres y cargos AGROMETALICAS ROJAS.xlsx` (2 bloques: 7 lideres + 3 del area
comercial, con Richard repetido -> **9 personas unicas**). Backup `ecorex-2026-07-24-1105.sql.gz`.
- **8 usuarios nuevos** rol **Admin**, Active, menu Completo, clave = cedula. El 9no (Gustavo Adolfo
  Russi) YA existia como `calidad@agrometalicas.com` con la misma cedula 1116243150: no se duplico y
  **se dejo como Owner** (decision del usuario; es el unico Owner del tenant).
- **Organigrama** en el modulo Dependencias (`org_units`, classifier Dependencia/Cargo/Funcionario).
  1er intento (plano, corregido despues): dependencia AGROMETALICAS -> un unico cargo "Lider" con los
  9 usuarios. **El usuario lo rechazo**: queria la JERARQUIA REAL de cargos, que el Excel si trae en
  las columnas "Cargo" y "Area / Proceso". Estructura definitiva (rehecha 2026-07-24,
  backup `ecorex-2026-07-24-1523.sql.gz`): **raiz AGROMETALICAS -> 7 dependencias por area -> 9 cargos
  reales -> 1 titular por cargo**, cada uno marcado `is_responsible` y como
  `responsible_tenant_user_id` de su cargo.
  Gerencia/Mantenimiento(Gerente Administrativo=Diego), Planta(Supervisor de Planta=Julian),
  Gestion de Compras(Aux. Administrativa y de Compras=Erika), Mejora Continua(Coordinador de
  Calidad=Gustavo), Gestion Humana(Auxiliar Contable=Maria Jose), Gestion Financiera(Contador
  externo=Carlos) y **Comercial** con 3 cargos (Coordinador Comercial=Richard, Asesor Comercial=Jorge,
  Asesora Comercial Externa=Lilian; agrupados porque el Excel los separa en un bloque "Area Comercial").

**Formulario "SIMULADOR COTIZACIONES" en AGROMETALICAS (2026-07-25, por SQL directo):** cotizador de
lamina metalica creado desde `MODELO COTIZACION (2).xlsx`. Replica la estructura del COT de SKY: form
`form_definitions` code **COT** (unico por tenant), transaccional, **Draft**. Backup
`ecorex-2026-07-25-0954.sql.gz`.
- **16 campos de cabecera** (N. Cotizacion, Cliente, Telefono, % Descuento/Lamina/Servicios,
  encabezados de seccion, el GridDetail, y los totales Sub total / con descuento / IVA / Total /
  % Utilidad, Observaciones).
- **Tabla `items` (GridDetail)** con **25 columnas** en `options_json` (mismo mecanismo que COT:
  `{id,label,type,calc}`): detalle, cantidad, espesor, calibre, tipo_lamina (select HR/INOX/ALFAJOR/
  GALVANIZADA/CR), largo, ancho, kg_und, kg_total, precios de lamina, costos, cortes, doblez, rolado
  (select SI/NO), servicios y costos. **9 columnas con formula aritmetica** (peso, costos, precios,
  servicios, totales de linea): `kg_und={largo}*{ancho}*{espesor}*7.85/1000000`, etc.
- **PENDIENTE (mismo bloque de desarrollo que el COT de SKY, D6-D11):** (a) los precios de LAMINA y de
  SERVICIOS que en el Excel salen por LOOKUP de la hoja "32" (por tipo de lamina y por espesor) se
  dejaron como campos de ENTRADA -> requieren el motor de lookup en columnas de tabla; (b) los TOTALES
  de cabecera (subtotal/IVA/total) requieren SUMIF sobre la grilla; (c) el % IVA quedo implicito.
  El formulario queda en Draft; NO evaluara las formulas hasta que ese motor exista.
- El peso usa densidad 7.85 (acero) en todas las filas, igual que el Excel (verificado contra ITEM 1
  y el ALFAJOR). Si INOX/otros necesitan densidad propia, es ajuste posterior.

**CORRECCION IMPORTANTE - el motor de formulas SI FUNCIONA (2026-07-25):** al publicar el cotizador y
PROBARLO se comprobo que lo que yo habia anotado como "pendiente D6-D11" **ya esta implementado** en la
ola F2 (`FormExpressionEvaluator` + `FormGridCalculator` + fuentes de lookup DataContainer/Item). El
evaluador soporta `+ - * /`, parentesis, comparadores, referencia a fila `{campo}` y AL ENCABEZADO
`{#campo}`, y funciones `SI`, `REDONDEAR`, `REDONDEAR.SUPERIOR/INFERIOR`, `MIN`, `MAX`. Corre en cliente
(UX inmediata) y servidor (revalida al guardar).
- **Publicado**: se activo el formulario (`status=Active`) y se emitio un `form_token` publico anonimo
  (365 dias, reutilizable). El token se cifra SHA256 hex; se genero el claro por SQL conociendo el
  algoritmo. URL: `https://app2.bitcode.com.co/f/<token>`.
- **Prueba real (Chrome MCP, via app2)**: se lleno una fila con el ITEM 1 del Excel (cantidad 1,
  espesor 12, largo 427, ancho 2480, tipo HR, precio venta 4800, costo 5000, 2 cortes a 4500) y TODAS
  las columnas calculadas dieron EXACTO al Excel: Kg c/und=99.754032, Costo lamina=498770.16, Precio
  unitario=478819.3536, Servicios unitarios=9000, Costo unitario=487819.35, Costo total=487819.35.
- Lo unico NO automatico: (a) los TOTALES de cabecera (Sub total/IVA/Total) no hacen roll-up porque no
  se configuraron con `aggregate` sobre la grilla -> es CONFIG, no desarrollo; (b) los precios de
  lamina/servicio siguen siendo ENTRADA -> se pueden auto-llenar via lookup a un contenedor de tarifas
  (soportado por el motor) cuando se cargue la hoja "32". Ninguna de las dos exige tocar codigo.

**Ancho por columna + tarjeta ancha del cotizador, y REDESPLIEGUE de prod (2026-07-25):** el usuario
pidio columnas mas anchas segun contenido y ver el formulario en horizontal (tarjeta ancha). La sesion
de desarrollo lo implemento como CODIGO (commit `6e105c5`): `FormGridColumn.Width` (lee `"width"`/`"w"`
del options_json) renderizado como `<colgroup><col style=width>` con `table-layout:fixed`; y
`form_definitions.card_layout` (enum Normal/Ancho/Completo, migracion DUAL) aplicado via clase
`dfr-cw-*` en el renderer cuando el host pasa `ApplyCardWidth`.
- Esta sesion aplico los DATOS: `card_layout='Ancho'` + `"width"` por columna en el `options_json` del
  GridDetail COT (mapa por contenido: detalle 200, cantidad 70, ... costo_total 120). Backup
  `ecorex-2026-07-25-1226.sql.gz`.
- **DIAGNOSTICO clave:** al probar, NADA se veia. Causa: el CONTENEDOR de la app en prod era del
  **2026-07-23** y el commit del dev del **2026-07-25** -> prod corria el binario VIEJO (sin el render
  de colgroup/card-width). No era dato ni bug: **faltaba redesplegar**.
- **Redespliegue de prod (autorizado por el usuario):** backup `ecorex-2026-07-25-1256.sql.gz`, luego
  `docker compose -f docker-compose.from-git.yml -p ecorex-prod build --no-cache` (clona la rama de
  GitHub, que ya tenia `6e105c5`) + `up -d`. Migracion `AddFormCardLayout` ya estaba registrada (141
  migraciones) -> sin conflicto. App arranco sana (login 200, sin errores).
- **Verificado en la URL publica tras el deploy:** `<colgroup>` con los anchos (200/70/80...),
  `table-layout:fixed`, tabla de 2070px con scroll horizontal dentro de la tarjeta, y la tarjeta ancha
  (`dfr-cw-ancho`, 1160px). Las formulas siguen calculando.
- Nota: el redespliegue trajo TODO `6e105c5` (ademas de forms: etiquetas y tiempo por columna en
  tableros, eliminar registro, autocompletado de contacto).
- Excepciones de datos del Excel: (a) **Carlos Humberto Villa no trae cedula** -> clave temporal
  `Agro-2026*`, debe cambiarla y hay que comunicarsela; (b) Lilian traia DOS correos en una celda ->
  se uso el corporativo `ventas1@` (decision del usuario); (c) correos normalizados a minuscula.
- **Se intento cargar por la UI con Chrome MCP (peticion del usuario) y NO fue posible**: el login por
  POST nativo si entro, pero la extension empezo a dar timeouts de CDP (`Page.captureScreenshot` y
  `Runtime.evaluate` a 30-45s) y las pestanas rebotaban a `chrome://newtab`. Se verifico que **el
  problema NO era la app** (contenedor sano: CPU 0.3%, sin excepciones en el log). Se cargo por SQL.

**Diseno + construccion de CONTACTO CLIENTE (FRM-00005) (2026-07-17):** primera rama dedicada a formularios.
(1) Se diseno el formulario (artefacto visual entregado + mapa de campos) con decisiones del usuario:
consecutivo transaccional read-only, cliente texto libre, contactos en GridDetail, valor condicionado.
(2) Se detectaron 4 necesidades de desarrollo (D1-D4) y se anotaron en la nota nueva del vault
`04. Notas para desarrollador/Notas para el programador ECOREX.formularios.md`. (3) OTRA sesion implemento
D1-D4 (control Time/DateTime, consecutivo en borrador, columnas-lista en GridDetail, editor de reglas de
campo) y las pusheo. (4) Se **redesplego prod** desde `fase-0/clon-backbone` (backup
`ecorex-2026-07-17-1309.sql.gz`; se detecto que la imagen previa era ANTERIOR al commit D1-D4). (5) Se
construyo FRM-00005 por SQL: 13 campos + transaccional (identity_mode=Sequence) + GridDetail tipado + regla
condicional `BLOQUEAR_CAMPO_XCONDICION` (show valor si concreto_venta=si; 1er rule_document de SOLDARCO).
Verificado en BD. Hallazgo D5 (P3): las reglas de campo no se evaluan al cargar el form (solo al cambiar) y
la visibilidad es un OR, asi que "valor" nace visible; anotado para desarrollo. La verificacion VISUAL por
Chrome quedo pendiente: el login por automatizacion no dispara (binding de Blazor Server; el login manual si
funciona).

**Login por automatizacion RESUELTO (2026-07-17):** el login es un POST HTTP nativo a `/auth/login` (no un
handler de Blazor). Automatizar = fijar los inputs (setter nativo + evento `change`) y `form.requestSubmit()`.
Verificado (se entro como SOLDARCO). Guardado en memoria del agente. Con eso, FRM-00005 quedo confirmado en
la UI real (13 campos, 1 regla).

**SIMULADOR DE COTIZACIONES (SKY SYSTEM, code COT) (2026-07-17):** diseno del port del Excel Cotizador. Form
transaccional, 19 campos de encabezado + tabla de 20 columnas. Calculos aritmeticos ya activos
(precio_base, subtotal, descuento, subt_desc, total, total_parq, total_cotizacion, 5 totales por rollup) +
regla del parqueadero (FIJO/X HORA). Lo que exige codigo (lookup en columna, funciones SI/REDONDEAR, leer
encabezado, agregado condicional, default por columna) se entrego como prompt a la sesion de codigo y quedo
documentado en la nota del vault (C1-C5). Datos maestros: clientes->Terceros, productos->Items (otra sesion).

**Solicitud de Requerimientos (AGROMETALICAS, code FRM-REQ) (2026-07-17):** formulario simple de captura de
requerimiento (replica de imagen del usuario). 2 secciones, 11 elementos (9 campos + 2 headings): datos del
cliente/empresa y detalles del requerimiento; tipo de servicio como lista (metalmecanica agricola). No
transaccional. Verificado en BD. NOTA: no se pudo verificar en la UI porque el usuario
`calidad@agrometalicas.com` (alta manual, nunca ha ingresado) NO autentica con la clave documentada
1116243150; su password quedo en otro valor. Pendiente: resetearlo (con permiso) o que el usuario de la clave real.

**8 formularios de AGROMETALICAS desde sus PDFs (2026-07-23):** el usuario aporto la carpeta de recursos
`052. Agrometalicas Rojas/01. Recursos` (12 PDFs) y pidio crear SOLO los que sean formularios de captura.
Clasificacion (3 subagentes leyeron los 9 candidatos): NO-formularios descartados = FC-C-001 (ficha de
caracterizacion de proceso SGC), MANUAL GESTION COMERCIAL, PD-C-001 (procedimiento). Creados 8 (todos Draft,
NO transaccionales por decision del usuario; codigos y nombres originales de la empresa):
FT-C-001 Ingreso de Materiales, FT-C-002 Registro de Cotizaciones (log/grid), FT-C-004 Remision de Entrega,
FT-C-005 Encuesta de Satisfaccion (4 preguntas escala 1-5 como Radio), FT-C-006 Formato PQRSF,
FT-C-007 Seguimiento a PQRS, FT-C-008 Orden de Trabajo (35 campos: datos + casillas de procesos + tabla de
10 columnas), FT-C-009 Seguimiento O.T. (log/grid de 21 columnas). Total 101 campos. Por SQL (backup
`ecorex-2026-07-23-0923.sql.gz`), idempotente por (tenant, code). Verificado en BD. No verificado en UI
(mismo bloqueo del login de AGRO). Nota de diseno: el croquis/area de dibujo de la Orden de Trabajo y los
textos legales fijos de los PDFs no se modelaron como campos (no son captura).

---

## Sesion 2026-07-14 - Modulo "Programar actividad" (000889) ola P1 (rama tareasprogramadas)

**Agentes**: Claude (Opus 4.8), worktree `funny-bell-3f8562`, rama nueva `tareasprogramadas` (basada en el
main con Formularios ya integrado). **Modo**: agent codea todo incl. migracion dual, valida en la BD local
`ecorex_forms`, documenta el esquema en el vault para la sesion principal; **sin deploy a prod** hasta orden.

Modulo NUEVO desde cero, fiel al prototipo (pantalla `isProgramar`, ECOREX.dc.html). **Ola P1 HECHA**:
- **Dominio** (4 entidades tenant-scoped + 6 enums texto): `ScheduledJob` (cabecera, consecutivo PAC,
  concurrencia optimista) + `ScheduledJobRule` (recurrencia 1:N) + `ScheduledJobChannel` (N) +
  `ScheduledJobRun` (bitacora, la llena P2).
- **Migracion DUAL** `AddScheduledJobs` (PG + SQL Server), 4 tablas nuevas, aplicada SOLO a `ecorex_forms`.
  Registro de esquema para prod en el vault (doc del modulo, seccion "Esquema para PROD").
- **Servicio** `ScheduledJobService` (EF parametrizado, sin el SQL concatenado del legacy): List/Get/Save
  (crear-actualizar con reglas+canales, PAC via ISequenceService)/ToggleStatus/Delete + catalogo Conceptos.
- **UI** `/programar-actividad` (`ProgramarActividad.razor` + `.razor.css`): lista + modal "Nueva
  programacion" (tabs Notificacion/Actividad, nombre, categoria/subcat, N reglas, canales). ASCII en texto,
  milimetrico en layout/tokens. Nodo de menu 000889 corregido a la pagina real (antes stub modulo/...).

**Verificado E2E en Chrome** (`ecorex_forms`): Notificacion (Semanal Lun/Mie, Correo+WhatsApp) -> PAC-000001;
Actividad (Operaciones/Visita tecnica, Mensual primer Lunes) -> PAC-000002; editar recarga; enlace de menu
visible para Owner (filtrado por permisos para roles sin acceso, por diseno). Solucion completa en verde.

**Siguiente**: P2 (motor de recurrencia + worker + bitacora + KPIs). Commit: rama `tareasprogramadas`.

### Cierre de P1 (000889) - pausar/activar + tests duales + bug de edicion

Antes de arrancar P2 se cerraron los pendientes de P1:
- **Pausar/activar** desde la fila (chip de ESTADO como boton). Sin esto no habia forma de pausar, y el
  worker de P2 (que solo dispara las Activas) no seria verificable.
- **Nodo de menu en PROD**: 000889 agregado a `expected` de `ReconcileMenuNodesAsync` -> los tenants ya
  sembrados se auto-corrigen al arrancar (antes el modulo habria quedado inalcanzable desde el menu en prod).
- **BUG REAL cazado por los tests** (la prueba manual no lo vio porque nunca se llego a GUARDAR una
  edicion): al editar se hacia RemoveRange(hijos) + vaciar las navs del padre; con relacion en CASCADA eso
  marca huerfanos y EF emite un SEGUNDO DELETE sobre filas ya borradas -> DbUpdateConcurrencyException
  espuria -> **ninguna edicion se podia guardar**. Arreglado (reemplazo total via DbSet, sin tocar navs).
- **Tests de integracion DUALES** (PG + SQL Server): 10/10 verde, incluido el BLOQUEANTE de aislamiento
  cross-tenant y el consecutivo PAC por tenant.

**P1 CERRADA.** Siguiente: P2 (motor de recurrencia + worker + bitacora + KPIs).

## Sesion 2026-07-14 (cont.) - Programar actividad (000889) ola P2: el motor ya dispara

- **Motor de recurrencia** (puro, unit-testeable): proxima ejecucion en la **zona del tenant** (regla 9),
  devuelta en UTC. 4 frecuencias del prototipo + intervalos + dias + ordinal mensual + intradia + vigencia.
- **Worker** hosted service **dentro de SuperAdmin** (NO en Ecorex.Workers): el compose de prod solo levanta
  `ecorex-app`, asi que un worker en Ecorex.Workers **nunca correria en prod** (hallazgo importante).
- **Dispatcher**: barrido cross-tenant (unico IgnoreQueryFilters, solo ids) + ejecucion acotada con
  AmbientTenantContext. Solo dispara las Activas; bitacora con la VENTANA como fired_at; avanza NextRunAt.
  Notification -> in-app al encargado; Activity -> Skipped hasta P3.
- **Idempotencia** por indice unico (tenant, job, rule, fired_at) + **auto-reparacion** de reglas sin
  NextRunAt (quedarian muertas). Migracion dual aditiva `AddSchedulerEngine` (tenants.time_zone_id + indice).
- **UI**: KPIs (ejecutados hoy/errores/activas), "Proxima: dd/MM HH:mm" en la lista y bitacora en el modal.
- **34 tests verde** (12 unitarios + 22 integracion dual). En vivo: el worker disparo solo, dejo bitacora Ok
  y reprogramo a las 08:00 de Bogota.

**Siguiente**: P3 (tipo Actividad crea la TaskItem via ITaskItemService.CreateAsync con el SubcategoriaId).

## Sesion 2026-07-14 (cont.) - Programar actividad (000889) ola P3: crea la ACTIVIDAD real

Aplica la regla de dominio **tarea == actividad == TaskItem** (la misma del wizard de 4 pasos): el motor NO
duplica logica, llama al MISMO `ITaskItemService.CreateAsync` con el `SubcategoriaId` del concepto, que
dispara el puente Concepto->Tarea (titulo auto, tablero del concepto, flujo, destinatarios).

- Titulo: manda el TituloAuto del concepto; si no lo define, cae al nombre de la programacion.
- Encargado opcional -> AssigneeTenantUserId (vacio = nace Pendiente/sin asignar).
- Trazabilidad: el numero de la tarea queda en `scheduled_job_runs.created_entity_ref`.
- Sin concepto -> Error en la bitacora (no revienta el motor).

**26/26 tests DUAL verde.** En vivo: ventana vencida de PAC-000003 -> el worker creo la tarea REAL **T00215**
(Operaciones/Visita tecnica, encargado operator@); la UI muestra "2 ejecutados hoy". Sin cambios de esquema.

**Pendiente (P4)**: canales externos reales (Correo/WhatsApp/Slack; hoy solo in-app) + reintento/dead-letter.

## Sesion 2026-07-14 (cont.) - Programar actividad (000889) ola P4: canales reales + reintento. MODULO COMPLETO

- **BUG DE HONESTIDAD corregido**: la bitacora decia "Ok - Canales configurados: Email, Slack, WhatsApp"
  cuando solo se habia entregado la notificacion in-app. Ahora se entrega de verdad y se reporta canal por
  canal; un canal que falla vuelve la ejecucion un Error (antes fingia exito).
- **Canales** (allow-list tipada por DI, sin reflexion): **Correo REAL** (SMTP) al correo del encargado y
  **WhatsApp REAL** por las lineas del tenant (el numero del encargado = PhoneNumber de la linea que tiene
  asignada). **Slack/SMS no tienen integracion**: se retiran de los chips y la bitacora lo dice.
- **Reintento + dead-letter** (migracion dual aditiva `AddSchedulerRetry`): la ventana conserva su identidad
  y se reintenta la MISMA con backoff 5/10/15 min; a los 3 intentos -> dead-letter y la regla vuelve a su
  cadencia. Indice unico ahora incluye el intento (cada intento deja fila, ninguno se duplica).
- **32/32 tests DUAL verde.** En vivo: la bitacora reporto la verdad y el reintento corrio solo (2 filas
  Error con el mismo fired_at, backoff 5 -> 10 min).

**MODULO 000889 COMPLETO (P1..P4).** Pendiente de configuracion (no de codigo): SMTP para que el correo
entregue; linea de WhatsApp asignada al encargado. Slack/SMS requeririan integrarse desde cero.

---

## Sesion 2026-07-15 - Dos pendientes menores (post-deploy 0bf057d)

**Agentes**: Claude (Opus 4.8). Contexto: ya en prod el arranque de tareas-proceso (olas A-D) +
tareasprogramadas (000889) + IMenuProvisioning. Cierre de los dos pendientes menores que quedaban.

- **Item 1 - config demo COT-COM (SQL, local)**: los 4 nodos del flujo de cotizacion/comercial ya
  tenian cargo salvo Facturacion y Entrega (sin `WorkflowNodePolicy`); ademas el concepto no tenia
  columna de cierre. Se insertaron 2 policies (Facturacion->Aprobador/admin; Entrega->Asesor
  Comercial/operator, `ON CONFLICT DO NOTHING`) y se fijo `task_board_column_id` = Completado.
  Verificado: los 4 nodos con cargo + columna de cierre = Completado.
- **Item 2 - guardia form-first vs form-por-nodo (`9a7982b`)**: en Conceptos (000270), aviso ambar no
  bloqueante cuando el formulario de ADMISION del concepto coincide con el del PRIMER nodo del flujo
  (se pediria dos veces). Se resuelve sobre el flujo GUARDADO
  (`ResolveFirstStepAsync`->NodeId->`GetWorkflowNodeFormAsync`); al cambiar el flujo en vivo se limpia
  la cache para no avisar en falso. Build SuperAdmin verde (0 errores). Push a main + fase-0/clon-backbone.

**Nota**: cambio solo-codigo/UI; prod sigue en 0bf057d (no requiere redeploy). El aviso viajara a prod
en el proximo despliegue rutinario.

---

## Sesion 2026-07-15 (cont.) - "Traer" de CUBOT.travels: mirada + backport selectivo

**Agentes**: Claude (Opus 4.8) + 6 subagentes (4 de reconocimiento + 2 de planificacion).

**Contexto**: el usuario pidio "traer todo" del proyecto hermano CUBOT.travels. Reconocimiento con
agentes revelo que travels NO es un modulo ajeno: es otro fork del MISMO backbone (cubotcrm), y
ECOREX ya tiene esos modulos. Veredicto por area:
- **Spine** (billing/Wompi, auth, Google, superadmin, Mi cuenta, API leads): YA en ECOREX 1:1
  (ancestros compartidos). Nada que traer.
- **Agentes IA + MCP**: ECOREX va POR DELANTE (function-calling/tools reales; travels usa el motor
  viejo de marcadores de texto). Duplicar/cupos/lista negra/dispatcher ya existen. MCP destinos = viajes.
- **WhatsApp/conversaciones/chat**: a la par o mas evolucionado (Emulator, dual-provider). Unico
  faltante real: proveedor **YCloud**.
- **Pipeline CRM**: dos CRMs ya (Pipeline heredado + Gestor 000740). Avances de travels = menores
  (asignar asesor inline, gestor de columnas). Decision estrategica pendiente (no se toco).
- **Destinos/Planes de Viaje**: especifico de viajes, descartado.

**Mecanica**: cherry-pick cross-repo NO viable (namespaces CubotTravels->Ecorex, net9->net10, dual
migrations). Port manual, archivo por archivo.

**Lo que se trajo (eleccion del usuario):**
1. **Robustez del runtime del agente** (`9c3de08`): candado de asesor humano (se calla si el lead esta
   asignado y activo; retoma si esta archivado), retry con backoff 503 en las 2 rutas de
   function-calling, pausa entre adjuntos. Sin migraciones. NO se porto el modelo de marcadores.
2. **Proveedor YCloud** (`92eb858`): 3er BSP de linea (par de Evolution/Cloud). Cliente HTTP portado
   1:1, enum+entidad+DTOs+servicios+UI, migracion DUAL AddYCloudProvider (3 cols + indice). El canal
   WhatsApp del 000889 lo hereda sin tocarse. Fuera de alcance: sometimiento real de HSM a Meta via
   YCloud y webhook entrante YCloud (el proveedor es saliente).

**Verificado**: build sln verde (0 errores), unit 379+35 verdes, migraciones duales simetricas.
Ambos commits en main + fase-0/clon-backbone. **No desplegado** (prod sigue en 0bf057d).

---

## Sesion 2026-07-15 (cont.) - DEPLOY a produccion (robustez agente + YCloud)

**Accion**: despliegue a prod (root@10.0.0.3, /opt/ecorex, build-from-git de fase-0/clon-backbone @ 50f46ec).

- **Verificacion previa**: main == origin/main == origin/fase-0/clon-backbone == 50f46ec; worktrees de
  agente (focused-joliot, funny-bell) limpios y ya en main; worktree de preview solo con scratch
  obsoleto (version vieja de ecorex-bpmn.js que main ya supera + HTMLs de prototipo) -> nada que traer.
- **Backup previo**: backups/ecorex-2026-07-15-0542.sql.gz.
- **Rebuild**: docker compose -f docker-compose.from-git.yml build --no-cache (BUILD_OK ~6 min) + up -d.
- **Migracion aplicada al arranque**: 20260715102503_AddYCloudProvider (log EF + columnas
  y_cloud_api_key_encrypted / y_cloud_phone_number_id / y_cloud_waba_id presentes en whats_app_lines).
- **Salud**: GET /login -> HTTP 200 al primer intento; "Now listening on http://0.0.0.0:8080".
- Incluye robustez del runtime del agente (9c3de08) y proveedor YCloud (92eb858).

---

## Sesion 2026-07-15 (cont.) - Consola SQL admin (000077), backport de VISAL

**Agentes**: Claude (Opus 4.8). Otro proyecto hermano del backbone (C:\DesarrolloIA\Visal, VISAL.git)
tenia una consola SQL para consultar el sistema. El menu de ECOREX ya listaba el item 000077 como
stub (modulo/sql-admin) sin modulo detras. Se desarrollo respetando el diseno de ECOREX.

- **Dominio**: SqlConsoleLog (append-only, NO tenant-scoped; el tenant del actor es dato, no filtro).
- **Application**: ISqlConsoleService + DTOs en Ecorex.Application.Admin.
- **Infrastructure/Sql**: SqlConsoleService ejecuta SQL crudo via DbConnection del EcorexDbContext
  (proveedor activo por DI). SELECT -> filas (hasta 1000); DML/DDL -> ExecuteNonQuery. AUDITA SIEMPRE
  en sql_console_logs. Explorador de tablas guardado por IsNpgsql() (pg_stat_user_tables) con mapa de
  descripciones PROPIO de ECOREX (tareas/flujos/formularios/conceptos/...), no el clinico de VISAL.
- **Migracion DUAL** AddSqlConsoleLogs (PG + SQL Server): tabla + indices.
- **UI** /sql-admin con el sistema de diseno de ECOREX (tokens --brand/--surface/--line/--ink,
  monocromo, theme-aware) NO el azul de VISAL: explorador arbol + editor Ctrl+Enter + resultados +
  historial + export CSV. Policy Perm:sql-admin:View (Owner/Admin por gobierno).
- **Menu**: item 000077 pasa de stub a pagina real 'sql-admin' + Ready; reconcile repunta el nodo ya
  sembrado en prod al arrancar (patron 000889).

**Verificado**: build sln verde, unit 379/379. Commit b3004e5 en main + fase-0/clon-backbone.
**Pendiente**: deploy a prod (aplica migracion AddSqlConsoleLogs al arrancar + reconcile repunta el menu).

---

## Sesion 2026-07-15 (cont.) - Gestion de cuenta/planes: YA EXISTIA + catalogo demo

**Peticion**: traer de "cubot.crm" la gestion de cuenta donde el tenant selecciona plan y el super
admin define planes. **Hallazgo (3a vez el mismo patron)**: es backbone compartido de la familia
cubotcrm y ECOREX YA lo tiene a paridad total: entidades SaasPlan/SaasPlanLimit/TenantSubscription,
servicios PlanAdminService/SubscriptionAdminService/RecurringBillingService/WompiCheckoutService,
pagina /plans (super admin define planes, policy PlatformOperator) y /mi-cuenta (tenant: plan, limites,
"Cambiar de plan", checkout Wompi). No existe carpeta cubot.crm; los tamanos de Cuenta/Plans son casi
identicos a los hermanos (mismo codigo del backbone). No habia nada que portar.

**Lo unico que faltaba (operativo)**: solo habia 1 plan sembrado, asi que la SELECCION no tenia
opciones. Se agrego `EnsureDemoPlansAsync` (idempotente por nombre): catalogo Free (\$0) / Pro (\$49k) /
Empresa (\$99k) con limites; global; enganchado al bloque demo de Program.cs (NO corre en prod bajo
SkipDemoSeed, donde el super admin define sus planes reales). Commit `5b3fee0`, sin migracion.

**Validado en Chrome** (demo SKY SYSTEM, Owner): /mi-cuenta "Cambiar de plan" lista los 3 planes;
cambio Empresa->Pro aplica y actualiza limites (25 usuarios / 500k IA / 5 lineas). BD: 3 filas en
saas_plans. No desplegado (cambio solo-demo; prod define planes por /plans).

---

## Sesion 2026-07-15 (cont.) - Contenedor de datos: duda de relaciones + ejemplo demo

**Duda del usuario**: "no veo como crear una relacion entre tablas". Diagnostico: la relacion NO se
dibuja arrastrando; es un CAMPO de tipo Reference (N:1) o RelationMany (N:N) con tabla destino. El
selector "Tabla destino" solo lista OTRAS tablas del modelo; con un modelo de 1 tabla salia vacio y
parecia que la funcion no existia.

- **Fix UX** (`f578b8f`): cuando se elige un tipo de relacion y no hay otra tabla, se muestra una guia
  ("crea y guarda una segunda tabla, luego elige la tabla destino") en vez de un selector vacio.
- **Ejemplo demo** (`f78d3e8`): EnsureDataModelDemoAsync siembra el modelo "Ventas (demo)" con 3 tablas
  (Clientes/Productos/Pedidos) + 2 relaciones (Pedidos.Cliente->Clientes N:1; Pedidos.Productos<->Productos
  N:N). Idempotente, solo Development. Validado en Chrome: el lienzo ER dibuja la linea morada (N:1) y la
  naranja punteada (N:N). Sin migracion. No desplegado (solo-demo).

---

## Sesion 2026-07-15 (cont.) - Contenedor de datos: relaciones como ENTIDAD (aristas del ER)

**Feedback del dueno**: el 'tipo' de un campo mezclaba dos propiedades ortogonales (tipo de dato vs.
relacion). Se separo: una relacion es ahora una entidad de primera clase.

- **Dominio**: `DataModelRelation` (ModelId, FromTableId->ToTableId, Kind N:1/N:N, Name) + enum
  `DataModelRelationKind`. Se elimina `ReferencedContainerId` de la columna; Reference/RelationMany
  quedan deprecados en el enum (solo para el backfill). Submodel (anidamiento) intacto.
- **Servicios**: DataModelService deriva relaciones de la entidad; AddRelation/DeleteRelationAsync;
  DeleteTableAsync limpia aristas primero. DataContainerService: columnas solo escalares.
- **UI**: se quitan Referencia/Relacion del dropdown de tipo; nuevo panel 'Relaciones' (lista +
  form origen/destino/cardinalidad + eliminar). El lienzo dibuja las lineas desde la entidad.
- **Migracion DUAL RelationsAsEntity + BACKFILL**: convierte relaciones-columna en aristas y neutraliza
  las columnas. Orden: crear tabla -> backfill -> borrar columnas -> drop referenced_container_id.
- **Fase 2 diferida**: re-cablear el vinculo dato-a-dato (fila-a-fila) contra la relacion-entidad; el
  backfill descarta esos links (el esquema de relacion si se preserva). Riesgo marcado para prod.

**Verificado**: build sln 0/0; unit 379/379; backfill aplicado en local (2 aristas Pedidos->Clientes N:1
/ Pedidos->Productos N:N, 0 columnas de relacion restantes). Commit `8b980e9` en main + fase-0. NO
desplegado. La verificacion visual en Chrome quedo pendiente por inestabilidad del dev server (se cae al
arrancar); la capa de datos si se verifico por psql.

---

## Sesion 2026-07-16 (cont.) - Board de registros, selector de iconos, menu cerrado, archivar formularios

**Agentes**: Claude (Opus 4.8). Todo en `main` -> `fase-0/clon-backbone` + `main` remoto.

- **DataRecordsBoard** (`Components/Shared/Data/`, commit `870179d`): el modulo publicado de una tabla
  reusaba `DataRecordsGrid`, el panel denso del configurador. Se separan responsabilidades: el board es
  la pagina de usuario final (eyebrow + titulo, CTA oscuro, KPIs Campos/Relaciones, panel con buscador,
  Importar/Exportar, filtros, grid, pager y modal propio con `RowRelationPicker`); el grid se queda en el
  modal de configuracion. **Decision del dueno**: publicacion por TABLA (no por contenedor).
- **MenuIconPicker** (`Components/Shared/`, commit `92eb504`): el icono al publicar se tecleaba a mano
  ("cube, list..."); una letra de mas y el item salia con el trazo neutro sin decir por que. Se EXTRAE el
  selector que ya existia en ConfiguracionMenu a un componente compartido (no se duplica) y se usa en los
  dos sitios. Verificado: persiste `icon_key=database`.
- **Menu cerrado al entrar** (`NavMenu.razor`, commit `ce334fe`): `IsGroupOpen` abria misproc/auto/
  sg-comercial SIEMPRE (replicaba el estado en que quedo capturado el prototipo). Ahora solo abre el grupo
  que contiene la ruta activa. **Decision del dueno**: la memoria de localStorage se conserva.
- **Archivar formulario desde la tarjeta** (commit `7891fc7`): las tarjetas solo abrian el disenador.
  **Decision del dueno**: el boton ARCHIVA (no borra) y BLOQUEA si esta en uso. `SetArchivedAsync` existia
  pero no lo llamaba nadie; se le agrega `DescribeUsagesAsync` (concepto de actividad, pasos de flujo,
  subformulario, modal de terceros). Las respuestas NO bloquean (archivar no las toca y es reversible),
  solo se avisan. La tarjeta pasa de `<button>` a div con rol de boton: un boton dentro de otro es HTML
  invalido.

**Trampa de CSS que aparecio 3 veces**: los elementos que renderiza OTRO componente (el `<input>` de
`InputFile`, los `<svg>` de `MenuIcons`) NO llevan el scope del CSS del componente padre, asi que un
selector normal no los alcanza (el file input se veia crudo; los iconos salian a tamano natural). Va con
`::deep`.

**Verificado en Chrome**: board con 8 filas + modal; selector 24 iconos a 17px en sus dos sitios; 14 grupos
cerrados en /inicio y solo "gen" abierto en /dc/perfil-clientes; archivar -> badge Archivado + boton
Restaurar; archivar "Solicitud de cotizacion" -> BLOQUEADO enumerando los 3 usos. Residuo de prueba
restaurado.

**Desplegado a prod** (`root@10.0.0.3`, build-from-git de `fase-0/clon-backbone`), con backup previo
(`ecorex-2026-07-16-1724.sql.gz` y `-1820`). Sin migraciones: los 4 commits son de UI/servicio.

**Siguiente**: campos configurables de Terceros e Items — 3 anchos (1/3, 2/3, completo), mover campo entre
grupos, motor de formulas ([ADR-0029](docs/decisiones/ADR-0029-motor-de-formulas-campos-calculados.md),
propuesto) y extras de travels (separador, filtrable, repetir N veces). Pendiente de antes: clave de orden
tipada por celda (hoy "150000" ordena antes que "9").

**Nota**: `TerceroFieldDefinition` dice "calcado de PipelineFieldDefinition (CUBOT.travels)" pero se calco
a medias. Travels NO tiene motor de formulas: solo un tipo `Total` que suma claves listadas por coma.

---

## Sesion 2026-07-17 - Campos configurables: 3 anchos, mover de grupo y motor de formulas (ADR-0029)

**Agentes**: Claude (Opus 4.8). Origen: el dueno noto que la configuracion de campos de terceros
estaba peor que en el proyecto hermano CUBOT.travels.

**Hallazgo**: `TerceroFieldDefinition` dice "calcado de PipelineFieldDefinition (CUBOT.travels)"
pero se calco A MEDIAS: 2 anchos en vez de 3, sin mover de grupo, sin calculados. Un subagente
mapeo travels y encontro que ALLA NO HAY motor de formulas: su unico calculado es un tipo `Total`
que suma claves listadas por coma, evaluado dentro de la pagina Blazor. El dueno pidio formulas de
verdad -> [ADR-0029](docs/decisiones/ADR-0029-motor-de-formulas-campos-calculados.md).

- **Motor** (`Ecorex.Application/Formulas`, commit `5db1e92`): parser de descenso recursivo +
  evaluador acotado (+ - * / parentesis, ROUND/MIN/MAX/ABS/SUM), sin dependencias externas, todo en
  decimal. FormulaCalculator resuelve el CONJUNTO en orden de dependencia y DETECTA CICLOS
  nombrando el recorrido; con ciclo no se publica ningun calculado. 59 tests nuevos.
- **Datos**: enum gana `Calculated`; las 2 entidades ganan Formula, ShowInFilter y
  RepeatWithFieldKey. Migracion DUAL aditiva (`CamposCalculadosYAnchos`).
- **Servicios** (commit `a26ad5c`): Clamp(1,3), mover de ficha/tipo, validacion de formula.
- **Modal de tercero** (commit `95cc867`): rejilla de 3 SOLO en `.dg-ficha-body .dg-fields` (la
  `.dg-fields` general la comparte el form de configuracion), calculado readonly en vivo,
  separador. Se materializa al guardar.
- **Items**: configurador + ficha a la par (rejilla de 12: col-md-4 / col-md-8 / col-12).

**Lo que los datos reales corrigieron del ADR** (ver Addendum): la clave solo es unica POR FICHA y
existe `dias_de_pago` en cliente Y proveedor -> `{dias_de_pago}` es ambiguo y se rechaza nombrando
las fichas; en la UI sale tachada. Mover avisa si el destino ya tiene la clave (indice unico) y, en
items, si una formula del origen la usa.

**Deuda propia saldada**: `CurrentPermissionsTests` no compilaba desde `5bc15b7` (el fix de
seguridad del 16-jul le agrego un parametro al constructor y no se actualizo el test; solo se
compilaba el proyecto, no la sln). El CI llevaba un dia en rojo. Corregido + test de regresion que
ese fix nunca tuvo (sin HttpContext la identidad sale del AuthenticationState).

**Verificado**: sln 0 errores; 503 tests unitarios verdes. En Chrome: 4 formulas de tercero
(valida / falta un valor / clave inexistente / ambigua) al instante; rejilla de 3 medida (306 /
625 / 944 px); 1000000 -> 1190000 y 2500000 -> 2975000 en vivo; al guardar la BD queda con
"cupo_con_iva": "1190000". En items, campo creado DESDE LA UI: {material}+1 rechazado por no
numerico, {garantia_meses}*30 valido, ficha abre en 720 y recalcula 360 / 1080 / 0. Residuos de
prueba eliminados.

**Desplegado a prod** (`a26ad5c`, backup `ecorex-2026-07-17-0525.sql.gz`): la migracion se aplico
sola (MigrateAsync al arrancar) y se verificaron las 6 columnas en la BD de produccion. Los commits
del modal e items quedan SIN desplegar.

**Siguiente**: pendiente de antes, clave de orden tipada por celda en el Contenedor de datos (hoy
"150000" ordena antes que "9"). Tests de servicio de campos (ambiguedad, mover, ciclo) en la matriz
dual de integracion.

---

## Sesion 2026-07-17 (cont.) - Repaso de deudas: 3 cosas dadas por hechas que no lo estaban

**Agentes**: Claude (Opus 4.8). Origen: el dueno pregunto que deudas habia. Al verificarlas (en vez
de responder de memoria) apareció que TRES cosas declaradas como entregadas no cerraban el circuito:

- **"Campo filtrable"**: el flag se guardaba y en items hasta se pintaba la etiqueta, pero NINGUN
  listado lo usaba. Solo funcionaba en Pipeline, que ya lo traia.
- **"Repetir N veces"**: el selector se configuraba y el modal lo ignoraba. Peor que no tenerlo: la
  UI prometia algo que no ocurria.
- **"Mover de grupo en items"**: `MoveFieldToTypeAsync` estaba hecho y protegido, sin nadie que lo
  llamara.

Leccion (se repitio 3 veces en la misma sesion): estimar por lo que se ve en la superficie sin
comprobar el extremo final. Compilar no es funcionar.

**Cerrado** (commit `334da38`):
- **Multivalor en terceros**: no existia (AllowMultiple estaba en la entidad pero ni se configuraba
  ni se renderizaba). El arreglo JSON va DENTRO de la misma celda -> FichasJson sigue siendo
  ficha -> campo -> texto y el esquema no cambia. Tope de 20 filas (un cero de mas en el gobernante
  colgaria el circuito). Tolera el valor viejo no-arreglo; borra la celda si queda todo vacio.
- **Un repetido no entra en formulas**: su celda guarda ["12","8"], que el motor leeria como 0. Se
  rechaza con mensaje propio.
- **Filtros terceros**: el servicio extrae del FichasJson SOLO las claves marcadas y tolera JSON
  corrupto; la barra ofrece solo valores existentes + limpiar.
- **Filtros items**: el listado PAGINA en servidor -> se filtra ANTES de paginar (filtrar lo visible
  haria mentir al total). **No se usa LIKE sobre el JSON a proposito**: JsonSerializer escapa lo
  no-ASCII y el LIKE fallaria en silencio con los valores en espanol. Los filtrables se acotan al
  tipo elegido.
- **Mover en items**: el desplegable que faltaba.

**Verificado en Chrome**: filtro tercero 8 -> 1 y limpiar -> 8; repetido 3 -> 3 filas, 1 -> 1, vacio
-> aviso; BD con "sede_nombre": "[\"Sede Norte\",\"Sede Sur\"]"; filtro items 15 -> 1 -> 15. Residuo
eliminado (0 defs, 0 filtrables, ficha de ANDINA como estaba). Sln 0 errores, 503 tests verdes.

**Deuda REAL restante** (nada a medias, nada que prometa lo que no cumple):
- Clave de orden tipada por celda (Contenedor de datos): hoy "150000" ordena antes que "9".
- Tests de servicio de campos (ambiguedad, mover con choque, ciclo) en la matriz dual: hoy solo
  probados a mano en Chrome.
- QA extra del runtime de flujos (tarea #68): rechazo, reasignar, cross-tenant, notif, auditoria, movil.
- Decision abierta: si las relaciones se ven como columna en el grid del board de registros.

---

## Sesion 2026-07-17 (cont.) - Necesidades del motor de formularios (D1-D4, sesion de diseno)

**Agentes**: Claude (Opus 4.8). Modo de trabajo (regla del dueno): la sesion de DISENO arma
formularios reales y anota lo que el motor no cubre en el vault (`04. Notas para desarrollador/
Notas para el programador ECOREX.formularios.md`); ESTA sesion (desarrollo) implementa y marca el
estado. Origen: formulario CONTACTO CLIENTE de SOLDARCO (FRM-00005).

Antes de codear, un subagente + verificacion propia mapearon el estado REAL de las 4 (varias decian
"confirmar/extender", no "crear"). Decisiones del dueno: las 4 de corrido; D2 = previsualizar.

- **D1 (P2) Hora**: enum gana Time/DateTime (insertados ANTES de Literal: IsTier1 usa type<=Literal;
  el enum se persiste como string, insertar en medio es seguro). Renderer input type=time; validacion
  ValidateTime; paleta + lista de tipos.
- **D2 (P2) Consecutivo en borrador**: PREVISUALIZAR (no reservar; sin huecos). Chip "N.o por asignar"
  cuando es transaccional y no hay numero. Una linea en el renderer; el servicio de consecutivos no
  se toca.
- **D3 (P1, bloqueante) Columna Lista en GridDetail**: FormGridColumn gana Kind/Options/Required;
  ParseColumns los lee; renderer pinta <select>; editor por columna en el disenador; validacion
  requerido/opcion por celda. Serializador propio que NO ensucia las columnas viejas. Se unifico el
  doble parseo (AddGridRow usaba ParseOptions -> ahora GridColumnsOf).
- **D4 (P2) Regla condicional**: el runtime YA existia (FormRuleDispatcher/FormRuleUiState +
  verbo BLOQUEAR_CAMPO_XCONDICION con accion inversa). Faltaba la AUTORIA -> CreateFieldConditionRuleAsync
  (documento por formulario + crear regla + vincular en un paso) + editor inline en el tab Reglas.
  FormFieldValidator.IsCapture marca campos origen/objetivo.

**Verificado en Chrome de punta a punta** (diseñador + runtime): D3 columna Estado lista+obligatoria,
JSON correcto, <select> solo en esa columna; D1 input type=time; D2 chip "N.o por asignar"; D4 crear
la regla desde el diseñador y ver el campo ocultarse/mostrarse al vaiven del valor. Residuos
eliminados (FRM-003 y FRM-021 restaurados; regla+doc+logs de prueba borrados). Sln 0 errores, 503
tests verdes. Commit `54c4889`. Estados marcados [x] en la nota del vault.

**Nota**: hallazgo clave de que D4 ya tenia runtime y verbo -> no habia que construir el motor, solo
la UI de autoria. D1/D2 mas pequenos de lo que parecia; D3 (la P1) fue el grueso real.

---

## Sesion 2026-07-19 - Agentes Colmena al menu + diag log del agente + concurrencia VERIFICADA en vivo

**Agentes**: Claude (Opus 4.8). Rama `feat/agente-colmena-gui` (17 commits sobre el tronco
`fase-0/clon-backbone`), lista para unificar. Cierre del modulo Agentes Colmena (ADR-0045) y prueba
en vivo de la concurrencia del agente en TODOS los niveles de servicio.

- **Menu (Infraestructura IA)**: el item "Agentes Colmena" (000868) se sembraba en
  `EnsureDefaultMenuAsync` pero los tenants YA existentes no lo recibian (ese metodo no reprocesa
  vistas ya creadas). Se agrega `EnsureAgentesColmenaMenuItemAsync` (backfill idempotente: recorre
  toda Section con Route="ia" e inserta el item donde falte) + cableado en el arranque tras AMBAS
  ramas de siembra (skip/demo). Commit `21db1a2`. Verificado como usuario cliente tenant
  (owner@sky-system.local): item bajo Infraestructura IA, breadcrumb correcto, aislamiento por tenant.
- **Diag log del agente**: el Servicio corre headless/elevado; suelto su consola no se ve y como
  servicio Windows va al Visor de eventos. Se agrega `FileLoggerProvider` (sin NuGet): deja SIEMPRE
  copia del ciclo de conexion en `%PUBLIC%\Documents\ecorex-agent-diag.log`, + una linea con la
  config leida de la boveda (ClientId/Hub/Secreto). Con esto se ubico al instante que la boveda tenia
  un ClientId viejo (`cli_dev_agent`) en vez del esperado -> el "Sin conexion" no era bug de codigo.
  Commit `d3d26c3`.
- **Concurrencia VERIFICADA en vivo (colmena real elevada)**: 4 tareas programadas con el MISMO
  `next_run_at` "se pisaron" al dispararse: 2 de Navegador (quotes.toscrape.com page/1 y page/2) + 2
  de Gateway/DB (SELECT contra SQL Server). El agente abrio 2 WebView2 aisladas EN PARALELO (ambas
  ordenes al mismo timestamp .255, cerrando a distinto tiempo) + 2 fetch headless. 20 filas ingestadas
  (10 autores x 2 paginas), feed de actividad poblado, agente En linea. En BD AISLADA `ecorex_agente`
  (puerto 5262), no en la compartida `ecorex_dev`.
- **Consulta editable del conector de BD**: a peticion del dueno se confirmo (no habia que construir
  nada): el conector de BD ya se define por un SELECT LIBRE editable en la UI (textarea "Consulta") --
  tabla simple o consulta compleja (joins/where/columnas calculadas). Wired end-to-end
  (crear/editar/guardar/ejecutar). Solo-lectura; exige que sea SELECT.

**Pendiente menor**: el feed de la UI solo muestra las ordenes de Navegador; las de Gateway/fetch
salen en el diag del agente pero aun no en el feed (logging de la ruta fetch diferido, ADR-0045 Ola
5). Permiso propio del modulo (hoy reusa `ExtraccionDatos.Editar`).

**Para unificar al tronco**: 17 commits en `feat/agente-colmena-gui` sobre `fase-0/clon-backbone`.
Migraciones DUALES nuevas (`AgentActivityLog` PG + SqlServer, `AddConnectorQuery`). Ver prompt de
handoff a la sesion principal.

**SIMULADOR de cotizaciones TERMINADO (2026-07-27):** con C1-C5 del codigo y datos cargados, se configuro el lookup de la columna codigo (autofill de 6 campos desde Items), defaults por columna, formulas objetivo (REDONDEAR.SUPERIOR, SI, IVA del encabezado, totales excluyen sin-stock) y card_layout=Completo. Verificado en BD. Pendiente menor: la columna marca no autollena (Brand no expuesta por ItemLookupSource).

---

## 2026-07-28 - Ejecutor REST en el agente Colmena (RestExecutor + fan-out OCS)

**Agente**: Claude (Opus 4.8), sesion sobre `main`.

**Hecho** (ADR-0048): el agente Colmena ya ejecuta conectores `RestApi` (antes caian a acuse). Tres
olas entregadas, build verde entre olas.
- **Ola 1 - REST simple**: `Ecorex.Agent.Core/Services/RestExecutor.cs` (analogo a `GatewayExecutor`):
  HttpClient propio, solo GET, auth Basic/Bearer/ApiKey desde `ConnectorSpec.Secret` (ADR-0040, no se
  loguea), paginacion Offset (start/limit) y Page (page) reescribiendo el query string, parseo
  tolerante (arreglo | objeto-indexado `{"1":{...}}` | arrayPath | envoltorios data/items/results/
  records/rows) en `RestJson.cs`, streaming de `FetchResultMsg` en chunks (500 filas). Contrato nuevo
  `RestFetchSpec`/`RestPagingSpec`/`RestFanoutSpec`/`RestFieldMap` + `FetchRequestMsg.Rest` en
  libs/Ecorex.Contracts.Agent/AgentProtocol.cs.
- **Ola 2 - fan-out lista->detalle + aplanado**: por cada item GET al detalle, desanida el objeto
  indexado (`DetailUnwrapIndexed`), ubica el arreglo hijo (`ChildArrayPath`, en OCS clave vacia `""`)
  y emite UNA fila por elemento hijo repitiendo columnas del padre. Declarativo, mapea a las 18
  columnas de "Software OCS". Rutas con puntos e indices (`hardware.NAME`, `bios[0].SSN`,
  `accountinfo[0].TAG`).
- **Ola 3 - wiring + UI**: `RealHiveConnection` despacha RestApi->`RestExecutor`;
  `ProcessRunner`/`AgentImportService` dejan pasar RestApi (config en `DataConnector.MappingJson`, sin
  migracion). UI: el textarea "Mapeo JSON" de la seccion Conectores ya persistia MappingJson -> el caso
  OCS es configurable pegando el RestFetchSpec ahi (placeholder/ayuda mejorados). El RestFetchSpec y el
  JSON de ejemplo OCS quedan en ADR-0048.

**Tests**: nuevo proyecto `apps/agent/tests/Ecorex.Agent.Core.Tests` (29 pruebas verdes) - parseo
tolerante (array vs objeto-indexado, arrayPath, envoltorios, clave vacia "", rutas con indices) y
fan-out/aplanado de punta a punta con JSON tipo OCS (via costura de fetcher enlatado, sin red) +
paginacion + MaxRows + combine de URLs.

**Builds**: `dotnet build Ecorex.Agent.slnx` verde (0/0). `Ecorex.SuperAdmin` compila (0 CS/RZ);
los MSB3021/MSB3027 al enlazar son el dev web corriendo que bloquea DLLs (verificado compilando a
OutDir de scratch: "Compilacion correcta").

**Siguiente / pendiente**: diseNador visual de fan-out en la UI (hoy JSON pegado); logging de la ruta
fetch en el feed de la colmena (ADR-0045); credencial OCS la aporta el usuario (no se hardcodea). NO
commit/push/deploy en esta sesion.

## Sesion 2026-07-31 - Reportes (galeria/editor + deploy), Configurador en cascada (F1+F2) y VLOOKUP multi-clave en formularios

Sesion larga en la linea de desarrollo (main). Tres bloques:

**Reportes**: galeria de tarjetas + doble vista (Tablero ECharts / Imprimible Bold) + reporte-panel;
plantilla Excel de ejemplo (Items/Directorio); "Reportes con IA" y "Editor de reportes" repuestos al
menu; editor unificado (todos los reportes editables) + diseNador Bold a pantalla completa. Deployado a
prod (10.0.0.3, build-from-git @ 5651c9a) con `ECOREX_MENU_REPORTES=true`: menu Reportes sembrado para
todos los tenants; Bold en modo evaluacion (pendiente clave de licencia). Verificado en vivo.

**Configurador en cascada** (motor generico config-driven, NADA hardcodeado):
- Contrato: `FormControlType.CascadeConfigurator` + `FormQuestion.CascadeConfigJson` (jsonb/nvarchar dual,
  migracion `AddCascadeConfigJson`) + `CascadeConfig` (Parse/Validate). Esquema cerrado contra la config
  REAL de SOLDARCO (resolucion mixta de columnSet por herencia, rollup=destino, width CSS).
- F1 motor: `CascadeRuntime` (logica pura) + `CascadeConfigurator.razor` (N pasos, bloqueo secuencial,
  filtrado por padre, tablas por rama, precarga, calc reusando FormGridCalculator). Verificado end-to-end
  en vivo con SOLDARCO (subtotal 3x1000=3000, herencia G2#FULL).
- F2 editor visual: `CascadeConfigEditor.razor` (componer/editar niveles/columnas/juegos/tabla sin JSON +
  escape hatch JSON). Build verde; validacion en vivo pendiente (la sesion local cayo al reiniciar).

**VLOOKUP multi-clave en GridDetail** (cotizador AGROMETALICAS): nueva llave `resolve` en options_json
(match compuesto contra un Contenedor de datos + return + guarda `when`), match EXACTO (decision del
usuario). Renderer re-resuelve al cambiar dependencias / al cargar; servidor autoritativo re-resuelve
antes del calc. Reusa la capa de lookup existente. Sin migracion. ADR-0052. Tests parser 3/3.

**Commits (main)**: reportes d74ef25/5ea64a7/7aa6cac/772cd73/5651c9a (pusheados+deployados);
contrato cascada c7ca57d + cierre 117d330 (pusheados); F1 8353e65 + F2 65a1acd (local); resolve 92fe450.

**Siguiente / pendiente**: validar F2 cascada en vivo; enganchar los 3 `resolve` del COT (sesion DATOS) y
probar el cotizador real (requiere deploy); clave de licencia Bold para reportes; push de los commits de
formularios cuando se decida.

---

## 2026-07-31 (sesion datos - prod) - Listado de clientes AGROMETALICAS

**Agente**: Claude Opus 4.8 (sesion de datos de produccion).

**Hecho**: cargados 1206 clientes de `LISTADO DE CLIENTES JUL 2026.xlsx` (hoja Sheet1,
cabeceras fila 7, datos desde fila 8) al Directorio (`terceros`) del tenant AGROMETALICAS
(`019f478d-6428-7283-a5cd-b7e35f802ef3`). Mapeo: nombre<-col A; tipo NIT->Empresa /
Cedula|Pasaporte|Doc.extranjero->Persona; id_tipo Nit|Identificacion|Ninguno; id_valor<-C;
ciudad<-E; telefono<-F limpiado (quita guiones de envoltura, une multiples con " / ");
email<-G; direccion<-D en `fichas_json.cliente.direccion`; perfil Cliente(1) salvo la fila
"Cliente=No"; estado Activo/Inactivo fiel (1 inactivo real). Idempotente por (tenant,
upper(nombre)) - nombres unicos en el archivo, sin duplicados. Backup previo
`ecorex-2026-07-31-1013.sql.gz`. Resultado: 1084 empresas + 122 personas; total terceros del
tenant = 1208 (1206 + 2 de prueba `perensejp`/`adreseon` que quedan). Carga por SQL directo
(bypass de AdminAuditLog, excepcion autorizada de ETL).

**Siguiente / pendiente**: (sin cambios) enganchar los 3 `resolve` multi-clave del COT
(corte/doblez/rolado, prompt entregado a sesion de codigo); validar F2 cascada; clave Bold.

---

## 2026-07-31 (sesion datos - prod) - Contenedor siigo/clientes en AGROMETALICAS (carga puntual)

**Agente**: Claude Opus 4.8 (sesion de datos de produccion).

**Hecho**: creado el contenedor de datos `siigo` -> tabla `clientes` (source_kind=WebService,
14 columnas aplanadas) en el tenant AGROMETALICAS (`019f478d-6428-7283-a5cd-b7e35f802ef3`) y
cargados los **1792 clientes reales** desde la API de Siigo (`GET /v1/customers`). El flujo real
de Siigo es de 2 pasos: `POST /auth` {username, access_key} -> access_token (valido 24h), luego
`GET /v1/customers` con `Authorization: Bearer` + header `Partner-Id`. Traida por paginacion
(page/page_size=100, 18 paginas) y aplanados los campos anidados: person_type->Empresa/Persona,
id_type.name, identification, check_digit, name[], commercial_name, active, address.address,
address.city.city_name/state_name, phones[0].indicative+number, contacts[0].email,
metadata.created. UUIDs deterministas (uuid5) + ON CONFLICT DO NOTHING => idempotente.
IDs: modelo `a78491e1-7d25-5785-b24d-fa2f41d0ae1c`, contenedor `94ddca5f-dfeb-5001-9e8f-69da434c603b`.
Backup previo `ecorex-2026-07-31-1846.sql.gz`. Credenciales de Siigo NO versionadas (repo publico).

**Pendiente de codigo (sync repetible via agente Colmena)**: el motor REST (server y agente) hoy
NO soporta (a) auth de 2 pasos POST->token con cache, ni (b) headers arbitrarios tipo Partner-Id.
Se entrego prompt para: RestFetchSpec.Headers + preflight de token (nuevo ConnectorAuthKind), en
`AgentProtocol.cs` + `RestExecutor.cs` + DataConnector (columnas HeadersJson/auth-preflight) +
config UI + migracion dual + tests. Luego configurar DataConnector Upsert(siigo_id) contra el
contenedor existente y su horario.

**Siguiente / pendiente**: (sin cambios) resolve multi-clave del COT; validar F2 cascada; clave Bold.

---

## 2026-08-03 (sesion datos - prod) - Conector Siigo via API de Configuracion (hallazgo: rutas anidadas)

**Agente**: Claude Opus 4.8 (sesion de datos/implementacion).

**Hecho**: la sesion de codigo entrego la **API de Configuracion tenant-scoped** (ConfigApiEndpoints,
ADR-0058): tokens, containers (RO), connectors CRUD + secret + probe + run, agents. Con el token
del tenant AGROMETALICAS configure por HTTP el conector "Siigo clientes" (id
`019fc83c-5876-744c-a717-9a448da0b281`) contra el contenedor `siigo/clientes`: TokenExchange (auth
2 pasos POST /auth), header Partner-Id, arrayPath=results, paginacion Page (page/page_size, inicial
1, 100), mapeo de 14 columnas con rutas anidadas, secret via PUT /secret. **Probe OK** (autentico
con Siigo, 25 registros muestra, 16 campos). **Run Upsert por Siigo Id: updated=1792, inserted=0,
failed=0** (reconcilio sin duplicar).

**HALLAZGO / BUG**: el `run` de la API va **server-directo** (`ApiImportService`), que **solo resuelve
campos de PRIMER NIVEL** (`el.TryGetProperty`). Las **rutas anidadas/indexadas** del mapeo
(`id_type.name`, `name[0]`, `address.city.city_name`, `phones[0].number`, `contacts[0].email`,
`metadata.created`) quedaron **VACIAS**, y el Upsert **sobrescribio con vacio** la data buena
previa. El probe no lo detecta (solo descubre llaves, no aplica mapeo). El agente (`RestExecutor` +
`RestJson.TryResolve`) SI resuelve rutas anidadas; el server-directo NO.

**Fix de data**: recarga no destructiva de las celdas desde el JSON (uuid5 + ON CONFLICT DO UPDATE),
restaurando los 14 campos correctos (ej. Ciudad=Popayan, Telefono, Email). Backup
`ecorex-2026-08-03-1049.sql.gz`. **NO re-ejecutar el conector hasta el fix de codigo** (volveria a
vaciar los anidados).

**Pendiente de codigo (prompt entregado)**: el `/run` debe poblar campos anidados: (A) que
`ApiImportService` resuelva rutas con puntos e indices reusando el mismo resolver del agente
(`RestJson.TryResolve`), y/o (B) que `/run` despache al **agente conectado** (RestExecutor ya lo
hace) cuando el tenant tenga agente online -> ademas satisface "carga via agente Colmena". Agregar
test con fixture de campos anidados.

**Siguiente / pendiente**: fix de rutas anidadas en el run; luego re-ejecutar el conector y validar
que puebla los anidados; opcional: dispatch via agente.

---

## 2026-08-03 (sesion datos - prod) - Fix de rutas anidadas VALIDADO

**Hecho**: la sesion de codigo desplego el fix (`ffdbd6a` en `fase-0/clon-backbone`, v0.2.1; prod
redesplegado 16:37 UTC). Re-ejecute el conector Siigo por API (`/run` Upsert por Siigo Id): updated
1792 + inserted 1, failed 0. **Verificado en BD**: los campos anidados AHORA se pueblan
(Tipo identificacion=NIT, Nombre, Direccion, Ciudad=Popayan, Departamento=Cauca,
Telefono=phones[0].number, Email=contacts[0].email, Creado=metadata.created). El conector quedo
OPERATIVO y re-ejecutable por API sin perdida de datos.

Nota: los valores ahora son los CRUDOS de Siigo segun el mapeo (Tipo persona="Company", Activo="true",
Telefono solo numero sin indicativo, Creado con timestamp completo) - fieles a la fuente; una capa de
transformacion/formato seria trabajo aparte.

**Pendiente**: scheduling del conector (aun no hay endpoint /schedule en la Config API - Fase 2);
opcional dispatch del run via agente Colmena; opcional capa de transformacion de valores.

---

## 2026-08-03 (sesion datos - prod) - Sync diario del conector Siigo programado

**Hecho**: la sesion de codigo desplego el Schedule en la Config API (v0.3.0, commit 9f6fa1a):
`PUT/GET/DELETE /connectors/{id}/schedule`, `GET /connectors/{id}/runs`, `POST /connectors/{id}/preview`.
El PUT /schedule persiste Mode+KeyColumn y exige keyColumn para Upsert; la corrida programada usa el
MISMO camino del /run (ConnectorRunPlanner + rutas anidadas). Programe por API el conector Siigo
(`019fc83c-...`): Cron `0 6 * * *`, mode=Upsert, keyColumn="Siigo Id", activo. scheduleId
`019fc8f3-3e47-7fb7-841b-42cdef0983cb`. nextRunAt 2026-08-04T11:00:00Z = 6:00 AM hora Colombia (COT
UTC-5). Reconcilia (no duplica). Todo por HTTP, sin UI.

**Estado Siigo**: COMPLETO end-to-end - contenedor + 1792 clientes + conector (token 2 pasos +
Partner-Id + mapeo anidado) + agente registrado + sync diario automatico.

---

## 2026-08-05 (sesion datos/impl - prod) - Conector Siigo vía AGENTE Colmena: CERRADO

**Ciclo cerrado end-to-end.** Secuencia:
1. **Punto 2 (observabilidad)** commit `2870232` (v0.8.3): `AgentImportService` escribe `agent_activity_logs`
   (Kind=Fetch) en cada desenlace del camino de fetch/import. Antes el camino de datos via agente era
   INVISIBLE; esto lo hizo diagnosticable. + test `AgentFetchActivityLogTests` (rama del autor,
   cherry-pick al tronco). Descarte del "Punto 1" (frescura/pinger): NO era bug de presencia.
2. **Diagnostico**: la bitacora mostro `REST_LIST_NET: Host desconocido (api:443)` — el agente resolvia
   el host `api` en vez de `api.siigo.com`. Descartado: config, RestSpecBuilder, traza de RestExecutor,
   DNS, hosts, proxy. Causa raiz real: **slash final** en la URL de la lista.
3. **Fix** (sesion de codigo) commit `5eab532`: REST via agente sin slash final en la URL de la lista.
   Rebuild MSI + reinstalar agente `cli_a942beecf941`.
4. **Validacion**: `agent_activity_logs` = `Fetch|Ok`. Empty->fill POR AGENTE (`782c1521`: del 1797 +
   ins 1797 = Replace ejecutado por el agente). Verificado: contenedor 1798 filas, campos anidados
   poblados (Ciudad=Popayan, Telefono=phones[0].number). El agente Colmena SI llena el contenedor.

**Estado Siigo/AGROMETALICAS: COMPLETO** — contenedor + conector (token 2 pasos + Partner-Id + mapeo
anidado) + sync diario 06:00 COT (Upsert) + carga vía agente Colmena funcionando + observabilidad.

---

## 2026-08-06 (sesion datos - prod) - Comerciales AGROMETALICAS: usuarios + asesores + dependencias

**Hecho**: carga de comerciales de `comerciales.xlsx` en AGROMETALICAS (excluyendo la fila "nuevo asesor"
= n/a). Usuarios: los 3 buzones ya existian (ventas@/ventas1@/ventas2@); ventas2@ lo compartian Jorge
Arteaga y Lilian Loaiza (un login = una persona) -> decision del usuario: correo propio para Lilian por
CEDULA. Creado usuario nuevo Lilian `31656416@agrometalicas.com` (login = cedula 31656416, clonando la
plantilla comercial ventas1@: tenant_role=Admin, menu_view_id 1cc14de5..., lead_visibility OwnOnly);
login validado (302 -> /inicio). Asesores comerciales creados: Jorge Arteaga (ventas2@), Richard
Gonzales (ventas1@); relink de Lilian a su nuevo usuario; Julian ya existia. Dependencias
(org_units Funcionario) creados y ubicados: Jorge->Asesor Comercial, Richard->Coordinador Comercial,
Lilian->Asesora Comercial Externa, Julian->Supervisor de Planta. Backup `ecorex-2026-08-06-0623.sql.gz`.
Carga por SQL directo (bypass AdminAuditLog, excepcion ETL). Sesion forkeada (solo implementacion/datos).

---

## 2026-08-06 (sesion datos - prod) - Usuario Oscar Cuartas en AGROMETALICAS

**Hecho**: creado usuario Oscar Steven Cuartas Bejarano en AGROMETALICAS: correo `info@cuarsa.com`
(login), clave = cedula 1113633887, tenant_role Admin (decision del usuario; el tenant no tiene rol
"basico" - enum Owner/Admin/Supervisor/Advisor y todos son Admin), status Active, menu estandar
1cc14de5. SIN asesor ni Funcionario en dependencias (por indicacion). Clonando plantilla ventas1@.
Login validado (302 -> /inicio). Backup `ecorex-2026-08-06-0923.sql.gz`. SQL directo (excepcion ETL).

---

## 2026-08-07 (sesion datos - prod) - Facturas Siigo en el modelo siigo (AGROMETALICAS)

**Hecho**: agregadas 2 tablas al modelo de datos `siigo` de AGROMETALICAS (que ya tenia `clientes`):
`facturas` (cabecera, 15 cols, 8440 filas) y `facturas_items` (11 cols, 14069 filas), desde la API de
Siigo `GET /v1/invoices` (8440 facturas, token 2 pasos + Partner-Id). Cabecera aplanada: siigo_id, name,
prefix/number, date, customer.identification/id, seller, total/balance, stamp.status (DIAN), cufe,
observations, public_url, num_items. Items: una fila por linea (fan-out del arreglo items[] inline),
enlazada por `Factura Siigo Id`; campos code/description/quantity/price/IVA/total. Carga CONTROLADA por
SQL (no por conector: los items son arreglo anidado inline que el motor de conectores no aplana). uuid5
idempotente. Verificado (FV-3-6906 con sus 7 items). Backup `ecorex-2026-08-07-1608.sql.gz`. IDs:
facturas `8de607a4-028e-5def-8e21-b371f2a01321`, items `ab2680f3-5f49-538f-9004-2186c6537188`.

Nota: repetibilidad -> las cabeceras podrian tener conector + sync como clientes; los items requeririan
un modo "aplanar arreglo anidado inline" (feature de codigo) o recarga manual.

---

## 2026-08-10 (sesion datos - prod) - Contenedor "Maestro comercial" en AGROMETALICAS

**Hecho**: nuevo modelo/contenedor de datos "Maestro comercial" (id 8db1b5ed-e7f2-5f3c-a9dd-682edc39e34a)
en AGROMETALICAS, con 10 tablas-catalogo parseadas de la hoja TABLAS de `MAESTRO ECOREX V2 (1).xlsx`
(cada bloque = titulo + encabezado + filas): ESTADOS CICLO DE VIDA(9), ORIGENES CLIENTES(9),
CALIFICACION COMERCIAL(4), ATENCION COMERCIAL(10), CANAL CONTACTO(32), FRECUENCIA DE COMPRA(6),
SECTOR ECONOMICO(22), NIVEL DE ORGANIZACION(3), ORIGENES DE MERCADO(9), CANALES DE CONTACTO(47).
Todas las columnas Text; datos cargados. uuid5 idempotente. Backup `ecorex-2026-08-10-2021.sql.gz`.
SQL directo (excepcion ETL).

---

## 2026-08-10 (sesion datos - prod) - Maestro comercial: relacion + ceros + replica en SOLDARCO

**Hecho**: (1) ceros adelante (pad a 2 digitos) en todas las columnas 'codigo' de las 10 tablas del
contenedor Maestro comercial (AGROMETALICAS) - ojo: las columnas acentuadas 'Codigo' requerian
normalizar el acento para detectarlas. (2) Relacion CANALES DE CONTACTO -> ORIGENES DE MERCADO
(data_model_relations ManyToOne + 47 data_model_relation_links fila-a-fila por 'codigo origen de
mercado' <-> 'Codigo Mercado'). (3) Replica de TODA la estructura (10 tablas + datos + ceros +
relacion) en SOLDARCO tenant e3519cc4..., contenedor GESTION COMERCIAL (model 6f80eaef..., estaba
vacio). Verificado en ambos: codigos 01..32, relacion 47 links. Backup ecorex-2026-08-10-2103.sql.gz.
uuid5 idempotente (celdas DO UPDATE para el fix de ceros). SQL directo (excepcion ETL).

---

## 2026-08-11 (sesion datos - prod) - SOLDARCO: campos DIAN en ficha fiscal (Directorio)

**Hecho**: agregados a la ficha 'fiscal' (Datos fiscales) del Directorio General de SOLDARCO
(tenant e3519cc4-150f-4f63-a0cd-21eb9d59f1fa) los campos configurables DIAN del cliente (tercero_field_definitions):
6 DV, 26 num identificacion, 31/32 apellidos, 33 primer nombre, 34 otros nombres, 35 razon social,
36 nombre comercial, 37 sigla, 38 pais, 39 depto, 40 ciudad, 41 direccion principal, 42 correo,
43 codigo postal, 44/45 telefonos, 46/48 codigos actividad, 47/49 fechas inicio (mas 24/25 que ya
existian). Hallazgo: la ficha ya tenia 5 campos de SISTEMA renombrados por etiqueta a DIAN
(razon_social->26, sector_industria->31, tamano->32, sitio_web->33, representante_legal->34) con key
sin coincidir; el primer INSERT (dedup por field_key) genero 5 duplicados por etiqueta. Corregido:
verificado 0 datos capturados -> DELETE de los 5 campos de sistema renombrados + INSERT 35_razon_social.
Estado final: 23 campos DIAN limpios (key=etiqueta), sin dup, sin perdida. Backup ecorex-2026-08-11-0909.sql.gz.
Nota: queda un campo generico 'direccion' (sistema) redundante con '41 direccion principal'.

---

## 2026-08-11 (sesion datos - prod) - SOLDARCO: contenedor Maestro Fiscal DIAN + lookups

**Hecho**: creado contenedor de datos "Maestro Fiscal DIAN" (data_model 6ab6e99f-2677-55ac-b70e-e496b8e17c48)
en SOLDARCO con 3 tablas catalogo de la hoja TABLA FISCAL del MAESTRO ECOREX V2: "24 Tipo de
contribuyente" (12 filas), "25 Tipo de documento" (9 filas), "53 Responsabilidad -Calidades Y
Atributos" (21 filas). Enlazados los campos fiscales del Directorio (tercero_field_definitions) 24 y 25
como tipo Lookup (options=DataLookupConfig JSON: tableId+displayColumnId+displayMode=List) apuntando a
sus tablas. uuid5 deterministas + ON CONFLICT. Backup ecorex-2026-08-11-1019.sql.gz.

**Pendiente**: el campo fiscal 53 (Responsabilidad) NO existe aun; es MULTIPLE (el usuario lo explicara);
la tabla 53 ya esta lista para engancharla. Enlace 53 pendiente.

---

## 2026-08-11 (sesion datos - prod) - SOLDARCO: campo fiscal 53 (multiple) enlazado

**Hecho**: creado el campo fiscal '53_responsabilidad' ('53. Responsabilidad -Calidades y Atributos')
en la ficha 'fiscal' del Directorio de SOLDARCO como Lookup MULTIPLE (allow_multiple=true) enlazado a
la tabla "53 Responsabilidad -Calidades Y Atributos" del contenedor Maestro Fiscal DIAN
(tableId fc6ee607-c24c-5822-8e23-a8e49bd61051, displayMode List). Cada tercero puede tener varias
responsabilidades DIAN. Backup ecorex-2026-08-11-1034.sql.gz. Cierra el pendiente del conjunto:
Maestro Fiscal DIAN (3 tablas) + fiscal 24/25 (Lookup) + 53 (Lookup multiple), todo en SOLDARCO prod.

## 2026-08-11 (cont.) - SOLDARCO: ficha comercial (Directorio) reconstruida con lookups a GESTION COMERCIAL

Agente: Claude Opus 4.8 (sesion prod-data). Peticion del usuario: agregar ~18 campos CRM a la
"ficha comercial" del Directorio de SOLDARCO (el usuario la llamo "cliente sospechoso"; no existe
una ficha con ese nombre - las fichas son fijas fiscal/comercial/cliente/proveedor/empleado). El
usuario pidio que YO mapeara cada campo a las tablas del contenedor "GESTION COMERCIAL" (10 tablas)
porque el cliente no documento las relaciones. Decision confirmada: borrar los 8 campos de sistema
actuales (IsSystem, 0 datos) y dejar SOLO la lista nueva de 17 campos.

Resultado (tenant SOLDARCO e3519cc4-..., ficha_key='comercial', 17 campos, sort 1-17):
- 8 tipo Lookup enlazados por ID a GESTION COMERCIAL (model 6f80eaef-...):
  estado_ciclo_de_vida->ESTADOS CICLO DE VIDA, atencion_comercial->ATENCION COMERCIAL,
  calificacion_comercial->CALIFICACION COMERCIAL, frecuencia_de_compra->FRECUENCIA DE COMPRA,
  sector_economico_ciiu->SECTOR ECONOMICO, origen_del_cliente->ORIGENES CLIENTES,
  canal_de_contacto->CANAL CONTACTO, nivel_de_organizacion->NIVEL DE ORGANIZACION (displayMode List).
- 8 planos: fidelizacion, segmento, subsegmento, volumen_de_compra, antiguedad_relacion_comercial,
  zona_comercial, comercial_responsable (Text), fecha_ultimo_contacto (Date).
- 1 Select conservado: lista_de_precios (General/Mayorista/Distribuidor); sobrevivio al borrado por
  clave igual, se re-ordeno a 17 y se desmarco IsSystem.
Se dedujo "segmento" (venia 2 veces en la lista del usuario). Desambiguaciones mias: canal ->
CANAL CONTACTO (no CANALES DE CONTACTO, que es matriz canal x origen); origen -> ORIGENES CLIENTES
(no ORIGENES DE MERCADO). Backup ecorex-2026-08-11-1154.sql.gz. SQL directo (excepcion ETL
documentada; no pasa por AdminAuditLog).

---

## 2026-08-12 (sesion datos - prod) - Tabla vendedores en contenedor siigo (AGROMETALICAS)

**Hecho**: agregada tabla `vendedores` (id 46b5a7d1-e760-5343-a27b-c6bc8234d587) al modelo siigo de
AGROMETALICAS (que ya tenia clientes/facturas/facturas_items). Fuente: Siigo `GET /v1/users` (32
usuarios = vendedores). Columnas: Siigo Id (=facturas.Vendedor/seller), Usuario, Nombre, Apellido,
Nombre completo, Email, Identificacion, Activo. Verificado el enlace factura.Vendedor 645 -> Diego
Fernando Rojas Mendoza. Carga puntual SQL, uuid5 idempotente. Backup ecorex-2026-08-12-2055.sql.gz.

## 2026-08-20 - Usuario Natalia Guerrero Guetia (AGROMETALICAS)

Alta de usuario en produccion para el tenant AGROMETALICAS (019f478d-6428-7283-a5cd-b7e35f802ef3).
Nombre "Natalia Guerrero Guetia", cedula/clave 1114875322. El correo pedido venia como
"Diseno2@agrometalica.com" (sin s); confirmado con el usuario que el dominio del tenant es
agrometalicaS.com, se corrigio a login diseno2@agrometalicas.com (minusculas). Rol Admin (como el
resto del tenant), clonando la plantilla ventas1@agrometalicas.com (menu_view_id, lead_visibility,
auth_provider local, status Active). platform_users + tenant_users con hash PBKDF2
v1.100000 (Rfc2898DeriveBytes SHA256, 100000 iter). document_code=1114875322. Verificado: login
nativo POST /auth/login -> HTTP 302 /inicio (OK). Backup previo ecorex-2026-08-20-1601.sql.gz.
Sin duplicado previo (ni correo ni cedula existian). SQL directo idempotente (NOT EXISTS).
