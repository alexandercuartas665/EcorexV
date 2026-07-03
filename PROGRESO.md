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
