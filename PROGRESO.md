# PROGRESO - ECOREX Sistema de Tareas

> Bitacora de avance por sesion. Formato: fecha, agentes, hecho, siguiente, bloqueos, decisiones.
> Complementa (no reemplaza) los ADRs de `docs/decisiones/` y el vault Obsidian.

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
