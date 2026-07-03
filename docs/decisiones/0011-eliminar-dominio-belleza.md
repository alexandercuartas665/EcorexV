# ADR 0011 - Eliminar el dominio belleza/agenda heredado del backbone

- Fecha: 2026-07-03
- Estado: Aceptado

## Contexto

ECOREX Tareas se construyo clonando el backbone `CUBOT.nails` (familia SaaS ECOREX),
que era un SaaS de agenda para salones de belleza. Con el clon llego un dominio
vertical completo que NO aplica a este producto: agenda y citas (con anti-overbooking),
turnos y excepciones de horario, asesores de imagen (recursos), catalogo de servicios
con tarifas por largo de cabello, clasificacion de cabello por vision de IA, productos
retail con stock por sede, cursos con inscripciones, sedes de salon, campos dinamicos
del salon y reserva publica online por link.

ECOREX Tareas construye un nucleo de tareas, tableros Kanban, proyectos, flujos BPMN,
formularios dinamicos y reglas de negocio. Mantener el stack de belleza solo agregaba
ruido, superficie de ataque y costo de mantenimiento en el modelo dual PostgreSQL /
SQL Server.

## Decision

Eliminar las 22 entidades del dominio belleza y todo su stack vertical:

- Entidades (Ecorex.Domain): Service, ServiceImage, ServicePriceTier, Resource,
  ResourcePhoto, ResourceServiceLink, HairLengthCategory, HairLengthReferenceImage,
  HairLengthClassification, ShiftTemplate, ScheduleException, SalonFieldDefinition,
  Sede, Appointment, AppointmentServiceItem, AppointmentMessage, Client, Product,
  ProductImage, ProductStock, Course, CourseRegistration.
- Enums huerfanos: AppointmentStatus, BookingChannel, ExceptionReason, ExceptionScope,
  HairLength, Punctuality, ResourceKind, SalonFieldScope, SalonFieldType, SchedulingMode.
- Servicios de Application: AgendaService, AgendaToolset, ClientService, CourseService,
  CourseToolset, HairClassifierService, HairLengthService, HairLengthToolset,
  ProductService, ProductToolset, ResourceService, SalonFieldService,
  ScheduleExceptionService, SedeService, ServiceCatalogService, ShiftTemplateService,
  OnlineBookingService.
- UI Blazor (Ecorex.SuperAdmin): paginas DiaSalon, Asignacion, Semana, Reprogramaciones,
  Clientes, Sedes, AsesoresImagen, TurnosBase, Servicios, Productos, Cursos,
  MedidasCabello, ReservasOnline, ReservaPublica, Excepciones; componentes BookingModal,
  SalonFieldsConfig, SalonFieldsEditor; servicio PublicBookingService y los endpoints
  /media/hair, /media/hairref y /media/asesor. Las entradas muertas del NavMenu se
  quitaron sin redisenar el menu (eso viene en la fase del Prototipo Final).
- Seeders: EnsureDemoProductsAsync, EnsureDemoCoursesAsync y
  EnsureDemoAgentCommercialFlowAsync (el flujo comercial demo del agente de IA vendia
  productos/cursos/servicios del salon). El agente demo generico se conserva via el
  sembrador one-shot TravelFans (/admin/seed-travelfans), que es del dominio CRM y no
  referencia entidades eliminadas. EnsureDemoTemplateAssetsAsync se conserva
  (las cotizaciones son del CRM).
- Tests: AppointmentOverbookingTests y AppointmentTierBookingTests. El gate de
  aislamiento multi-tenant (TenantIsolationTests) no se toco: su entidad scoped es
  TenantConfiguration, que se conserva.
- El toolset de IA queda reducido a PipelineToolset (crear_lead). El tipo compartido
  AgendaToolResult se renombro a AgentToolResult (Json + SessionCompleted).

## Migraciones (DAL dual, ADR-001)

- PostgreSQL: migracion `20260703175944_RemoveBellezaDomain` que dropea las 22 tablas.
  Las migraciones historicas de Postgres NO se tocan (conservan el linaje del esquema).
- SQL Server: nadie habia desplegado aun ese proveedor, asi que se REGENERO la
  migracion inicial (`20260703180047_InitialCreateSqlServer`) desde el modelo ya
  limpio, y se recreo la BD dev `ecorex_dev` del contenedor local.

## Consecuencias

- Desaparece el exclusion constraint GiST anti-overbooking (`ck_appointments_no_overlap`)
  junto con la tabla `appointments`, y con el se cierra el gap que tenia SQL Server
  (ese motor no soporta EXCLUDE/GiST y dependia de validacion en aplicacion). Ya no
  hay asimetria entre motores por este concepto.
- El modelo queda con 55 tablas identicas en ambos motores (columna vertebral SaaS +
  CRM + tableros + IA).
- Los modulos de tareas/proyectos del producto se construiran sobre TaskBoard/TaskCard
  existentes y los nuevos motores (BPMN, formularios, reglas), no sobre el viejo
  nucleo de citas.
- Restos conservados a proposito:
  - `Tenant.OnlineBookingEnabled/PublicBookingToken/PublicBookingBaseUrl`: columnas
    legado en la entidad Tenant (regla: no tocar Tenant*); quedan sin uso y se
    retiraran en una fase posterior con su propia migracion.
  - `BusinessUnitModalKind.ImageAdvisory`: valor de enum persistido como texto en
    `business_units`; se conserva para poder leer filas existentes. La UI ya no lo
    ofrece ni lo rutea (todas las unidades abren el modal generico) y los defaults de
    BusinessUnitService ahora crean una sola unidad "General".
  - `AutomationRule.ShiftName` y la accion AssignToShift: son del CRM (turnos de
    asesores comerciales), no de la agenda del salon.
