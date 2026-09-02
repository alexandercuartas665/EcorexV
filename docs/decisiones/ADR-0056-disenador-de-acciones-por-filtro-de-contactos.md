# ADR-0056: Disenador de acciones por filtro de contactos (port del ucWorkflowDesigner legacy)

- Estado: Aceptado (Fase 1 y Fase 2 implementadas; Fase 3 pendiente)
- Fecha: 2026-08-03
- Autores: Agente de diseno (Claude)
- Contexto de fase: Capa 1/2 (Gestor de Contactos 000740) + motor de ejecucion nuevo
- Relacionados: ADR-0055 (WhatsApp LID remoteJid), ADR-0054 (conector RestApi), modulo
  000889 (Motor de programaciones / ScheduledJobWorker), Gestor de Contactos 000740.

> SOLO diseno. Este ADR NO introduce codigo de aplicacion ni migraciones. Las entidades,
> columnas y migraciones EF las crea la sesion de implementacion cuando esta fase arranque.

---

## 1. Contexto y problema

El legacy VB.NET (WebForms) tiene un "disenador de acciones" (`ucWorkflowDesigner.ascx`)
que permite armar, como una LISTA SECUENCIAL de pasos, una secuencia de acciones de contacto
(conectar, mensaje de red, WhatsApp, email, llamada CRM) con una o varias "ventanas de horario"
por paso. Se quiere portar ese disenador a ECOREX (Blazor Server, .NET 10) y, a diferencia
del legacy, atarlo FORMALMENTE a cada **filtro guardado** de contactos (`TerceroFiltro`) para
que la secuencia se ejecute sobre el segmento de contactos que ese filtro define.

### Lo que hace el legacy (mapeado)

- Paleta de 5 acciones arrastrables (`data-type|data-icon|data-color|data-label`):
  - `conectar`  / fa-link          / #334155 / Conectar    (sub: Enlace)
  - `mensaje-red` / fa-comment-dots / #6366f1 / Mensaje Red (sub: Notificacion)
  - `whatsapp`  / fa-whatsapp       / #10b981 / WhatsApp    (sub: Directo)
  - `email`     / fa-envelope       / #f43f5e / E-mail      (sub: SMTP)
  - `llamada`   / fa-phone-volume   / #f59e0b / Llamada CRM (sub: Gestion)
- Solo `llamada` tiene campos propios: Comercial, Prioridad, Categoria, Subcategoria.
- Cada paso tiene N ventanas de horario (`ScheduleRange`): StartDate, EndDate, StartTime,
  EndTime, ActiveDays ("1,2,3,4,5"), TemplateId, AccountID, RepeatEvery, PackageSize.
- Persistencia legacy en 3 tablas SQL Server (base GENE, cross-tenant fisico por columna
  `SUCURSAL`): `TAR_WORKFLOW_PROYECTOS` (maestro), `TAR_WORKFLOW_PASOS` (STEP_ID, STEP_TYPE,
  STEP_LABEL, ORDEN, CONFIG_JSON), `TAR_WORKFLOW_HORARIOS`.
- El legacy es una LISTA de pasos (no un grafo BPMN), NO tiene motor de ejecucion, y NO tiene
  FK a un filtro: ata por convencion de string `PROYECTO = "PROY_" + id`.
- Debilidades heredadas que NO se heredan (ver CLAUDE.md seccion 5): SQL crudo concatenado
  (SQL injection), sin tenant real (`SUCURSAL` a mano), sin transaccion, borrado fisico de
  pasos/horarios al re-guardar, `GETDATE()` implicito.

### Piezas ECOREX reutilizables (ya existen)

| Necesidad             | Pieza ECOREX                                                         |
|-----------------------|---------------------------------------------------------------------|
| Enviar WhatsApp       | `IWhatsAppConnectorService.SendTestAsync(...)` (soporta remoteJid/LID) |
| Enviar email SMTP     | `IEmailSender.SendAsync(to, subject, htmlBody)` (config en email_configs) |
| Responder redes / chat| dispatcher del agente (`AgentReplyDispatcher`) + `AiAgentRunLog`     |
| Crear gestion CRM     | `ITaskItemService.CreateAsync(CreateTaskItemRequest{ SubcategoriaId })` |
| Scheduler cross-tenant| `ScheduledJobWorker` (000889): barrido vencidos -> `AmbientTenantContext.Begin(tenantId)` |
| Filtro guardado       | `TerceroFiltro` (TenantEntity: Nombre, Descripcion, Fuente, CriteriosJson) |
| Menu "..." del filtro | `GestorContactos.razor:99-132` (hoy Filtrar ahora / Eliminar)       |

---

## 2. Decision

### 2.1 Modelo de datos (recomendado: entidades relacionales, NO un unico JSON)

Se reemplazan las 3 tablas legacy por **tres entidades tenant-scoped** con vinculo FORMAL al
filtro (lo que faltaba en el legacy). Se DESCARTA la alternativa de "un solo JSON de workflow
por filtro" porque el motor de ejecucion (Fase 2) necesita consultar y actualizar el estado de
CADA envio (idempotencia, dedupe, reintentos) sin reescribir un blob completo, y porque el
scheduler barre ventanas vencidas: eso exige filas consultables por indice, no un jsonb opaco.
Se conserva JSON SOLO para los parametros libres de cada paso (donde el esquema varia por tipo).

**Entidad A - `ContactWorkflow`** (: TenantEntity) - reemplaza `TAR_WORKFLOW_PROYECTOS`

| Columna            | Tipo               | Notas                                             |
|--------------------|--------------------|---------------------------------------------------|
| Id                 | Guid v7 (BaseEntity) | PK                                              |
| TenantId           | Guid (TenantEntity)  | filtro global                                   |
| TerceroFiltroId    | Guid (FK)          | **vinculo FORMAL 1:1 con el filtro guardado**     |
| Nombre             | string             | por defecto "Acciones de <filtro>"                |
| Activo             | bool               | on/off del workflow completo                      |
| RowVersion/xmin    | concurrencia       | ADR regla 8 (concurrencia optimista)              |
| Soft-delete + audit| BaseEntity         | nunca DELETE fisico                               |

Relacion recomendada **1:1** filtro<->workflow (un filtro tiene a lo sumo un disenador de
acciones). Se implementa con FK unica (`TerceroFiltroId` UNIQUE por tenant). Alternativa 1:N
queda como decision abierta (ver seccion 5) si el negocio pide varias secuencias por filtro.

**Entidad B - `ContactWorkflowStep`** (: TenantEntity) - reemplaza `TAR_WORKFLOW_PASOS`

| Columna          | Tipo    | Notas                                                    |
|------------------|---------|----------------------------------------------------------|
| Id               | Guid    | PK                                                       |
| ContactWorkflowId| Guid FK | paso -> workflow                                         |
| StepType         | string  | conectar | mensaje-red | whatsapp | email | llamada     |
| Label            | string  | etiqueta visible                                         |
| Orden            | int     | orden secuencial (0..n) - es una LISTA, no un grafo      |
| ParamsJson       | string? | jsonb; parametros propios del tipo (p.ej. campos CRM)    |

`ParamsJson` de `llamada` = { Comercial, Prioridad, Categoria, Subcategoria }. En ECOREX
"Categoria/Subcategoria" mapean a los conceptos 000270 y `SubcategoriaId` que ya usa
`CreateTaskItemRequest`. Los otros 4 tipos hoy no tienen params, pero la columna deja espacio
(p.ej. `mensaje-red` podria llevar el id del agente/canal en el futuro).

**Entidad C - `ContactWorkflowSchedule`** (: TenantEntity) - reemplaza `TAR_WORKFLOW_HORARIOS`

| Columna          | Tipo     | Notas                                                     |
|------------------|----------|-----------------------------------------------------------|
| Id               | Guid     | PK                                                        |
| ContactWorkflowStepId | Guid FK | ventana -> paso                                       |
| StartDate/EndDate| date?    | rango de vigencia de la ventana                           |
| StartTime/EndTime| time     | franja horaria (default 09:00-18:00)                      |
| ActiveDays       | string   | "1,2,3,4,5" (dias ISO). Mantener formato legacy simplifica |
| TemplateId       | string?  | plantilla de mensaje (WhatsApp/email); ver Fase 3         |
| AccountId        | Guid?    | cuenta/linea de mensajeria (WhatsAppLine, email_config)   |
| RepeatEvery      | int?     | minutos entre disparos dentro de la ventana               |
| PackageSize      | int?     | cuantos contactos por disparo (rate limiting por lote)    |

**Zona horaria**: StartTime/EndTime se interpretan en la zona del tenant; el scheduler compara
contra UTC convirtiendo con la zona del tenant (CLAUDE.md regla 9). Nada de GETDATE().

**Entidad D (Fase 2, control de ejecucion) - `ContactWorkflowRun`** (: TenantEntity)

Registro por (contacto x paso x ventana) ya ejecutado. Es la CLAVE DE IDEMPOTENCIA y dedupe:
antes de enviar, el motor verifica que no exista un run OK para esa tripleta en la ventana.

| Columna | Tipo | Notas |
|---------|------|-------|
| Id | Guid | PK |
| ContactWorkflowStepId | Guid FK | paso ejecutado |
| ContactWorkflowScheduleId | Guid FK | ventana que disparo |
| TerceroId | Guid FK | contacto destino |
| DispatchedAtUtc | DateTimeOffset | cuando |
| Status | enum | Pending / Sent / Failed / Skipped |
| Channel | string | whatsapp/email/chat/crm |
| ExternalRef | string? | id del mensaje enviado / id de la tarea CRM creada |
| Error | string? | motivo si Failed (sin secretos) |

Indice unico `(ContactWorkflowStepId, ContactWorkflowScheduleId, TerceroId)` = garantia de
"un contacto no recibe dos veces el mismo paso en la misma ventana".

### 2.2 Las 5 acciones y como se EJECUTAN

| Accion (StepType) | Params        | Servicio ECOREX de ejecucion                              |
|-------------------|---------------|-----------------------------------------------------------|
| `whatsapp`        | TemplateId, AccountId (=WhatsAppLine) | `IWhatsAppConnectorService.SendTestAsync(lineId, phone, text, actor, remoteJid)`. Se resuelve el remoteJid/LID del Tercero (ADR-0055). |
| `email`           | TemplateId, AccountId (=email_config) | `IEmailSender.SendAsync(tercero.Email, subject, htmlBody)`. |
| `mensaje-red`     | (agente/canal futuro) | dispatcher del agente / chat (`AgentReplyDispatcher`), registrando `AiAgentRunLog`. |
| `llamada`         | Comercial, Prioridad, Categoria, Subcategoria | `ITaskItemService.CreateAsync(CreateTaskItemRequest{ SubcategoriaId, EntidadId=terceroId, assignee=Comercial, priority=Prioridad })` = crea la gestion CRM asignada. |
| `conectar`        | (enlace)      | Paso "no-envio": marca el contacto como enlazado/entrado al workflow (transicion de estado en la bolsa). No dispara mensajeria; util como primer paso. |

Regla de resolucion de destinatario: el motor recorre los `Tercero` del segmento del filtro
(evaluando `TerceroFiltro.CriteriosJson` con el mismo evaluador que usa "Filtrar ahora"),
y por cada contacto valido dispara el paso segun su tipo. Contactos sin el dato requerido
(sin telefono para whatsapp, sin email para email) se registran como `Skipped`.

### 2.3 Motor de ejecucion (net-new, Fase 2)

**Donde corre**: se REUSA el patron y el host del `ScheduledJobWorker` (000889), que ya vive
DENTRO de `Ecorex.SuperAdmin` (no en `Ecorex.Workers`, que en prod no se levanta - ver comentario
del propio worker). Dos opciones, se recomienda la (a):

- (a) **Nuevo dispatcher, worker existente**: agregar un `IContactWorkflowDispatcher` y engancharlo
  en el barrido del `ScheduledJobWorker` (o un `ContactWorkflowWorker` gemelo con el MISMO patron).
  Cadencia 1 min (unidad minima del prototipo es la hora, sobra).
- (b) Worker propio nuevo. Solo si se quiere aislar la cadencia/So.

**Ciclo del motor** (identico al patron 000889):
1. Barrido de plataforma: que tenants tienen alguna `ContactWorkflowSchedule` VENCIDA de un
   `ContactWorkflow.Activo` (devuelve SOLO ids de tenant, ningun dato de negocio).
2. Por cada tenant: `AmbientTenantContext.Begin(tenantId)` (el query filter de EF aisla sin
   HttpContext), abre scope propio y ejecuta.
3. Por cada workflow activo del tenant con ventana vencida:
   - resolver el segmento (`Tercero` del filtro),
   - recorrer pasos EN ORDEN; por cada paso, por cada contacto:
     - respetar la ventana (dia activo, franja horaria en zona del tenant, vigencia StartDate/EndDate),
     - **idempotencia/dedupe**: saltar si ya existe `ContactWorkflowRun` OK para (paso, ventana, contacto),
     - **rate limiting**: enviar en lotes de `PackageSize`, respetando `RepeatEvery` entre lotes,
     - ejecutar el servicio del tipo, escribir `ContactWorkflowRun` (transaccion por lote),
     - un fallo por contacto NO frena a los demas (patron try/catch por item del worker).

**Idempotencia** = el indice unico de `ContactWorkflowRun` + verificacion previa. Reintentos de un
`Failed` permitidos hasta un tope; `Sent`/`Skipped` nunca se repiten en la misma ventana.

### 2.4 UI del disenador (Blazor Server)

- **Enfoque**: lista secuencial de pasos con paleta de acciones (agregar por click y/o
  arrastrar), IGUAL que el legacy - **NO** un grafo BPMN (eso es otro motor, WorkflowEngine).
  Reproduce las 5 tarjetas de la paleta con sus colores/iconos, la zona de "drop"/lista
  ordenada de pasos, el panel de campos CRM visible solo para `llamada`, y N bloques de
  "ventana de horario" por paso (dias como chips, horas, plantilla, cuenta, repetir cada,
  tamano de paquete).
- **Donde vive**: un **modal/pagina** que se abre desde una nueva opcion **"Acciones"** en el
  menu "..." del filtro (`GestorContactos.razor:120-132`), junto a "Filtrar ahora"/"Eliminar".
  Se recomienda componente propio `ContactWorkflowDesigner.razor` (mas un `.razor.css` con los
  tokens del prototipo) abierto en modal sobre el Gestor.
- **Como se guarda**: al guardar, el componente llama a un `IContactWorkflowService.SaveAsync`
  (Application) que persiste workflow+pasos+ventanas en UNA transaccion (CLAUDE.md regla 4),
  con soft-delete de los pasos removidos (regla 5), no borrado fisico como el legacy. Estado del
  disenador en memoria del componente Blazor (no ViewState).

---

## 3. Plan por fases

### Fase 1 - Modelo + entidad atada al filtro + UI del disenador + persistencia (M)

Tamano: **M** (mediano). Entregable: se puede abrir "Acciones" desde un filtro, disenar la
secuencia y guardarla. SIN ejecucion todavia.

Archivos exactos que se tocan/crean en Fase 1:

- `apps/backend/src/Ecorex.Domain/Entities/ContactWorkflow.cs` (NUEVO)
- `apps/backend/src/Ecorex.Domain/Entities/ContactWorkflowStep.cs` (NUEVO)
- `apps/backend/src/Ecorex.Domain/Entities/ContactWorkflowSchedule.cs` (NUEVO)
- `apps/backend/src/Ecorex.Application/Tenancy/IContactWorkflowService.cs` (NUEVO)
- `apps/backend/src/Ecorex.Application/Tenancy/ContactWorkflowService.cs` (NUEVO)
- `apps/backend/src/Ecorex.Application/Tenancy/ContactWorkflowDtos.cs` (NUEVO)
- Config EF + registro de entidades + query filter tenant + migracion
  (en `Ecorex.Infrastructure`; **lo hace la sesion que migra**, no este ADR).
- `apps/backend/src/Ecorex.SuperAdmin/Components/Pages/GestorContactos.razor`
  (agregar boton "Acciones" en el bloque `@if (_filtroMenu == ff.Id)` lineas 120-132, y el
  handler que abre el modal).
- `apps/backend/src/Ecorex.SuperAdmin/Components/.../ContactWorkflowDesigner.razor` (+ `.razor.css`) (NUEVO)
- Registro DI del `IContactWorkflowService`.

### Fase 2 - Motor de ejecucion + scheduler (M/L)

Tamano: **M-L**. Entrega el `ContactWorkflowRun`, el `IContactWorkflowDispatcher`, el enganche
en el patron `ScheduledJobWorker`, la resolucion del segmento del filtro, ventanas/dedupe/rate,
y el cableado a los 5 servicios de ejecucion. Auditoria y logs sin secretos.

### Fase 3 - Plantillas y cuentas de mensajeria (S/M)

Tamano: **S-M**. Selector real de plantillas (WhatsApp/email) y de cuentas/lineas (`AccountId`
apuntando a `WhatsAppLine`/`email_config`), reemplazando los ids sueltos por selects poblados.
Equivale al `ucScheduleRange.CargarCuentas/CargarPlantillas` del legacy, pero tenant-safe.

---

## 4. Consecuencias

- Positivas: vinculo formal filtro<->acciones (lo que faltaba), multi-tenant real por
  construccion, motor idempotente con dedupe y rate limiting, reuso de servicios y del patron
  de scheduler ya probado (000889), UI fiel al legacy sin arrastrar su deuda.
- Negativas / costo: net-new de motor de ejecucion (Fase 2) no trivial; 3-4 entidades nuevas +
  migracion dual (PG + SQL Server); hay que definir plantillas/cuentas (Fase 3) para que los
  envios sean utiles.

## 5. Riesgos y decisiones abiertas

1. **1:1 vs 1:N filtro<->workflow**: se propone 1:1 (FK unica). Si el negocio quiere varias
   secuencias por filtro, subir a 1:N y agregar selector en el modal. DECIDIR con el usuario.
2. **Plantillas de mensaje**: modelo de plantillas (WhatsApp/email) aun no definido en ECOREX;
   Fase 1/2 pueden usar texto libre + `TemplateId` opcional hasta que exista (Fase 3).
3. **Cuentas de mensajeria**: `AccountId` debe resolver a un `WhatsAppLine`/`email_config` del
   MISMO tenant; validar en el servicio (nunca aceptar un id cross-tenant).
4. **Dedupe de envios**: el indice unico de `ContactWorkflowRun` es la garantia dura; definir la
   politica de reintento de `Failed` (tope de intentos, backoff).
5. **Limites de rate**: `PackageSize`/`RepeatEvery` acotan el ritmo, pero conviene un tope global
   por linea WhatsApp/servidor Evolution para no gatillar bloqueos del proveedor.
6. **Segmento dinamico vs snapshot**: al ejecutar se evalua el filtro EN VIVO (el segmento cambia
   entre corridas). Confirmar que ese es el comportamiento deseado (vs congelar el segmento al
   activar el workflow).
7. **Consentimiento/opt-out**: envios masivos de WhatsApp/email requieren respetar bajas; contemplar
   un flag de exclusion por Tercero antes de produccion.

---

## 6. Apendice: Fase 2 tal como se implemento (motor de ejecucion)

Estado: IMPLEMENTADA. Piezas entregadas:

- **Entidad `ContactWorkflowRun`** (`Ecorex.Domain/Entities`, tenant-scoped): columnas `ContactWorkflowStepId`,
  `ContactWorkflowScheduleId`, `TerceroId`, `WindowDate` (fecha local del tenant), `DispatchedAtUtc`,
  `Status` (enum `ContactWorkflowRunStatus` = Pending/Sent/Failed/Skipped, string), `Channel`, `ExternalRef`,
  `Error`. Migracion DUAL `AddContactWorkflowRuns` (PG `Ecorex.Infrastructure` + SQL Server
  `Ecorex.Infrastructure.SqlServer`), encadenada tras `AddImportProcessRunMode`. DbSet en
  `IApplicationDbContext`/`EcorexDbContext`.
- **Dedupe / ventana**: la "ventana" del indice unico es `(ScheduleId + WindowDate)`, NO solo el Schedule. Es
  decir, un contacto recibe un paso a lo sumo UNA vez POR DIA por ventana. Indice unico
  `(TenantId, StepId, ScheduleId, TerceroId, WindowDate)`. Ventaja: una re-corrida el mismo dia no reenvia
  (idempotente), y al dia siguiente la ventana vuelve a estar disponible (respeta ActiveDays). El dispatcher
  hace un pre-check (query de `TerceroId` ya ejecutados) ademas del indice, para no depender del choque de BD.
- **Ventana de horario**: se evalua en la zona del tenant (`ScheduledJobRecurrence.ResolveTimeZone`): rango
  `StartDate/EndDate`, dia ISO en `ActiveDays`, y franja `StartTime/EndTime` (soporta franja nocturna que
  cruza medianoche). Nada de `GETDATE()` implicito (CLAUDE.md regla 9).
- **Rate**: `PackageSize` = tope de contactos por corrida (default `DefaultBatchCap=50` si es null, techo duro
  `HardBatchCap=500`); `RepeatEvery` = minutos minimos entre corridas de la MISMA ventana (se mira el ultimo
  `DispatchedAtUtc` de esa ventana en el dia). Un fallo por contacto NO frena a los demas.
- **Segmento**: se evalua el filtro EN VIVO con `ContactFilterEvaluator` (extraido de `GestorContactosService`
  para que "Filtrar ahora" y el motor usen la MISMA logica). Universo = terceros no inactivos.
- **Dispatcher + worker**: `IContactWorkflowDispatcher`/`ContactWorkflowDispatcher` (`Ecorex.Application/Gestor`)
  + `ContactWorkflowWorker` (`Ecorex.SuperAdmin/RealTime`, hosted service, barrido 1 min). Mismo patron que
  `ScheduledJobWorker`/`ImportSchedulerWorker`: barrido cross-tenant SOLO ids (IgnoreQueryFilters) ->
  `AmbientTenantContext.Begin(tenantId)` -> ejecucion acotada por el filtro global.
- **Mapeo REAL de las 5 acciones** (a servicios existentes):
  - `WhatsApp` -> `IWhatsAppConnectorService.SendTestAsync(lineId, phone, text, actor, remoteJid)`. La linea
    sale de `AccountId` o, si es null, de la primera linea CONECTADA del tenant; `text` = `TemplateId` (texto
    libre hasta Fase 3); `remoteJid` se resuelve de una `Conversation` previa por telefono (soporta LID,
    ADR-0055). Sin telefono, sin linea o sin texto -> `Skipped` con motivo.
  - `Email` -> `IEmailSender.SendAsync(tercero.Email, subject, body)`; sin correo/sin texto -> `Skipped`.
  - `Llamada` -> `ITaskItemService.CreateAsync(CreateTaskItemRequest{ SubcategoriaId, Priority, AssigneeTenantUserId,
    RequesterName/Email/Phone })`, mapeando `ParamsJson` (Comercial->assignee, Prioridad->TaskPriority,
    Subcategoria->SubcategoriaId = puente Concepto->Tarea). Sin subcategoria valida -> `Skipped`.
  - `Conectar` -> paso "no-envio": `Sent` con canal `conectar`, sin mensajeria.
  - `MensajeRed` -> **`Skipped` documentado**: hoy no hay canal para INICIAR una conversacion saliente de
    redes (el dispatcher del agente solo RESPONDE mensajes entrantes). Se resolvera en Fase 3.
- **Tests**: `ContactWorkflowDispatcherTests` (EF InMemory) verifica: corre una vez sobre 2 contactos (2 envios,
  2 runs Sent), re-correr respeta el dedupe (0 envios nuevos), y fuera de la ventana/dia inactivo no envia nada.
- **Banner del disenador**: cambiado de "aun no se disparan (Fase 2)" a "Motor programado" (dispara dentro de
  la ventana, una vez por contacto y dia).

Limites documentados (anti-spam): por corrida se envia a lo sumo `PackageSize` (o 50) contactos por ventana;
`RepeatEvery` espacia las corridas; el dedupe garantiza una entrega por contacto/ventana/dia. Consentimiento/
opt-out (decision abierta 7) sigue pendiente antes de uso masivo en produccion.

## Adenda 2026-09-02: config de LLAMADA IA en el paso "Llamada" (solo configuracion)

El paso "Llamada" del disenador ahora admite dos modos:

- **Gestion CRM** (default, `Modo` null/"crm"): comportamiento actual intacto (4 campos ->
  puente Concepto->Tarea).
- **Llamada IA** (`Modo` "ia"): configura una llamada con un **AiAgent REUTILIZADO** (no se crea un
  modelo de agente nuevo; se referencia por `Id` y su `SystemPrompt` es el prompt base). Campos:
  agente, `PromptExtra` (texto que se anexa al prompt para esa llamada), `Objetivo`
  (`ContactCallObjetivo`: OfrecerProducto | LlenarFormulario | Personalizado) y
  `FormulariosPermitidos` (formularios que el agente puede llenar).

Todo se persiste como JSON en `params_json` (sin migracion). El motor de voz IA es de la fase
siguiente: el dispatcher, ante `Modo == "ia"`, devuelve `Skipped/"voz-ia"` con nota "pendiente de
motor". Validacion minima en el disenador: modo IA exige agente; objetivo "Llenar formulario" exige
al menos un formulario permitido. Round-trip cubierto por `ContactWorkflowCallParamsTests`.

## Adenda 2026-09-02: MOTOR DE VOZ IA (Retell/Telnyx) - Fase B

La Fase A dejo la config del paso "Llamada IA" y un hook en `ContactWorkflowDispatcher.ExecuteLlamadaAsync`
(rama `Modo=="ia"`). La Fase B implementa el motor que coloca la llamada:

- **Proveedor**: Retell (agente de voz) sobre Telnyx (Elastic SIP Trunking). Salientes exigen el header
  `X-Telnyx-Username` en `custom_sip_headers`.
- **El prompt de ECOREX reemplaza el del agente Retell** (requisito). Como `agent_override` NO permite pisar
  el prompt por-llamada, el prompt compuesto (`AiAgent.SystemPrompt` + `PromptExtra` + directiva de objetivo)
  se materializa como el `general_prompt` del Retell LLM. Se provisiona un agente Retell **keyed por hash del
  prompt** (`RetellAgentMap`) y se reutiliza para prompts identicos.
- **Secretos por tenant cifrados** (`TenantRetellConfig`: RetellApiKey/SipPassword con `ISecretProtector`).
  El DTO solo expone `HasApiKey`/`HasSipPassword`; jamas se loguean ni se muestran.
- **Persistencia** de cada llamada en `VoiceCall` (call_id, status, transcripcion, analisis, costo, whitelist
  de formularios). El **webhook** (`/api/voice/retell/webhook`) verifica la firma (HMAC-SHA256 con la key del
  tenant, anti-replay) y, si el objetivo era LlenarFormulario, vuelca los datos capturados a `FormResponse`
  usando SOLO los `FormulariosPermitidos` (whitelist dura).
- **Dedup/idempotencia**: el `ContactWorkflowRun` (ExternalRef = call_id) evita re-llamar; `create-phone-call`
  NUNCA se reintenta (un 5xx pudo haber colocado la llamada).

Migracion DUAL aditiva `AddVoiceCalls` (PG + SQL Server): `tenant_retell_configs`, `voice_calls`,
`retell_agent_maps`. El motor de voz no coloca ninguna llamada hasta que un tenant configure y habilite Retell.
