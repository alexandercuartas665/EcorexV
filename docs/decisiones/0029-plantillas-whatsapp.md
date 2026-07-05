# ADR-0029: Modulo de Plantillas HSM de WhatsApp

Fecha: 2026-07-05
Estado: Aceptado

## Contexto

ECOREX arrastra un stack de WhatsApp bajo el grupo "CRM (heredado)" (entidad `WhatsAppLine`,
conversaciones, agente). El backbone hermano CUBOT.travels tiene un gestor de plantillas HSM
(mensajes plantilla que Meta debe aprobar: categoria, idioma, variables y ciclo de aprobacion
Draft -> Submitted -> Approved/Rejected). Se porta ese modulo a ECOREX como modulo NUEVO,
adaptado a las convenciones del proyecto: multi-tenant real (HasQueryFilter), DAL dual
PostgreSQL/SQL Server con una migracion por proveedor, servicios con resultados tipados y
fidelidad visual del workspace.

CUBOT.travels somete las plantillas a Meta via un cliente YCloud (`IYCloudApiClient`). ECOREX
NO tiene ese gateway de proveedor todavia, y la instruccion de este corte es NO inventar
llamadas HTTP a Meta.

## Decision

### Modelo (Ecorex.Domain, TenantEntity)

- `WhatsAppTemplate`: `Name` (nombre tecnico Meta, normalizado a minusculas/guion_bajo),
  `Language` (es, en_US...), `Category` (enum Marketing/Utility/Authentication),
  `HeaderType?` (enum None/Text/Image/Document/Video), `HeaderText?`, `BodyText` (requerido,
  editable con tokens {{cliente}}), `FooterText?`, `VariablesJson` (jsonb/nvarchar(max) dual),
  `Provider?`, `WhatsAppLineId` (Guid FK NO ACTION a `WhatsAppLine`), `WabaId?`,
  `Status` (enum Draft/Submitted/Approved/Rejected/Paused/Disabled, default Draft),
  `ProviderTemplateId?`, `RejectionReason?`, `SubmittedAt?`, `ReviewedAt?`, `IsActive`.
- Enums nuevos: `WhatsAppTemplateCategory`, `WhatsAppTemplateHeaderType`,
  `WhatsAppTemplateStatus` (persistidos como string, HaveConversion<string>().HaveMaxLength(40)).
- **Almacenamiento DUAL**: `VariablesJson` es `jsonb` en PostgreSQL y `nvarchar(max)` en SQL
  Server; `BodyText` usa el tipo de texto largo por proveedor (text / nvarchar(max)). Igual que
  `Item.FieldValuesJson` (ADR-0027).
- **FK NO ACTION** (`DeleteBehavior.Restrict`) hacia `WhatsAppLine`: borrar/archivar una linea no
  arrastra plantillas por cascada y evita rutas multiples de cascada en SQL Server (error 1785).
- **Unica por tenant en (Name, Language)**: la misma plantilla en otro idioma coexiste.
- Migracion dual `AddWhatsAppTemplates` (Ecorex.Infrastructure + Ecorex.Infrastructure.SqlServer),
  aplicada y verificada en los contenedores dev (PG 5442, SQL Server 1443).

### Servicios (Ecorex.Application/Tenancy, resultados tipados)

`WhatsAppTemplateResult<T>` (Ok/NotFound/Invalid/Conflict/NotImplemented), mismo patron que
`InventoryResult<T>` (ADR-0027).

- `IWhatsAppTemplateService`: CRUD (create/update/list/get/setActive-archivar) con resultados
  tipados; unicidad (Name, Language) validada con mensaje claro + indice unico; auditoria
  (`IAuditWriter`) en las acciones sensibles.
- `WhatsAppTemplateCalculations` (logica pura, probada de forma unitaria): normalizacion del
  nombre tecnico, extraccion de tokens {{x}} del cuerpo, validacion de la solicitud y reglas de
  transicion (solo Draft/Rejected se editan o someten).

### DEUDA: proveedor stub (sin integracion real con Meta)

- `SubmitAsync` es un **STUB**: cambia `Status` a `Submitted` + `SubmittedAt = now` y escribe
  auditoria. NO compila el cuerpo a placeholders posicionales ni llama a la WhatsApp Cloud API de
  Meta. No se invoca ningun endpoint HTTP externo.
- `SyncStatusAsync` es un **no-op**: devuelve `NotImplemented` sin tocar la plantilla.
- Cuando exista el gateway multi-proveedor de ECOREX, aqui iran la compilacion del cuerpo
  ({{cliente}} -> {{1}}) y la llamada real; hoy queda documentado como deuda.

### UI (Ecorex.SuperAdmin)

- `/plantillas-whatsapp` (policy `PlantillasWhatsApp.Editar`): tabla (nombre, idioma, categoria,
  linea, estado con badge por color), modal crear/editar (nombre, idioma, categoria, linea,
  encabezado, cuerpo con deteccion de variables, pie), accion "Someter" (llama al stub) y
  archivar/restaurar. Banner permanente que documenta que el envio al proveedor NO esta
  implementado (consistente con como ECOREX marca modos no implementados).
- Tokens del workspace (--brand/--surface/--ink/--t-blue..., claro/oscuro).
- NavMenu: item "Plantillas WhatsApp" en el grupo "CRM (heredado)", junto a "Lineas WhatsApp"
  (grupo con las paginas del stack WhatsApp existente). Conteo del grupo 6 -> 7.

### Policy (paso 1)

`PlantillasWhatsApp.Editar` en Program.cs con `RequireClaim("tenant_id")`, mismo patron "paso 1"
que el resto de modulos (Inventario.Ver, etc.): fija el nombre estable de la policy. El paso 2
(derivar el requisito real del Module Registry + rol del TenantUser) queda pendiente, igual que
en los demas modulos.

### Seeder (Development, idempotente)

`EnsureWhatsAppTemplatesDemoAsync` en `DatabaseSeeder`, llamado desde SuperAdmin Program.cs:
siembra una linea de WhatsApp Cloud demo (si no hay ninguna del tenant, sin credenciales reales)
y 3 plantillas del tenant demo SKY SYSTEM en categorias y estados distintos (Utility/Draft,
Utility/Submitted, Marketing/Approved). Idempotente por tabla vacia (guard por tenant).

## Consecuencias

- El modulo de plantillas HSM queda operativo (CRUD + submit stub) reusando los patrones
  existentes (resultados tipados, filtro global, DAL dual, auditoria). Ningun envio real a Meta.
- Cobertura: unit (normalizacion, extraccion de tokens, validaciones, transiciones), integracion
  dual (round-trip, unicidad (Name,Language) por tenant, aislamiento cross-tenant, transicion por
  Submit + SyncStatus NotImplemented) y un E2E (crear plantilla y verla en la tabla).
- Deudas documentadas: (1) integracion real con la WhatsApp Cloud API de Meta (Submit/SyncStatus
  son stubs); (2) la policy sigue el "paso 1" (Module Registry pendiente); (3) headers de imagen/
  documento/video modelados pero no soportados en el editor de este corte.
