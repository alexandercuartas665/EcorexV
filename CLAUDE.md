# CLAUDE.md - Memoria del agente de desarrollo para ECOREX.tareas

> Primera lectura obligatoria para cualquier agente (Claude Code u otro) antes de
> modificar codigo en este repositorio. Reglas pequenas, concretas y verificables.
> Convencion del proyecto: **solo ASCII** en archivos nuevos (sin tildes ni enie).

---

## 1. Contexto del proyecto

ECOREX - Sistema de Tareas es la **reconstruccion sobre .NET 10** de un gestor de
**tareas, proyectos, tableros Kanban, flujos de proceso BPMN 2.0, formularios
dinamicos y reglas de negocio** que hoy corre en WebForms/VB.NET (GestionMovil,
legacy en `C:\Desarrollo\core`, SOLO referencia). Es un SaaS multi-tenant donde
**el proceso, el formulario y la regla se configuran sin codigo**.

Pilares:

- **Multi-tenant real**: `TenantId` (Guid v7) + `HasQueryFilter` global + RLS en BD.
- **DAL dual**: PostgreSQL y SQL Server tras `IEcorexDbContext`, elegido por `Database:Provider`.
- **Tres motores**: WorkflowEngine (BPMN, bpmn-js), DynamicFormRenderer (EAV -> jsonb), RulesEngine (verbos tipados).
- **PlatformAdmin** (Super Admin) separado del admin de tenant, con MFA y auditoria inmutable.
- **Agentes de IA gobernados** (AI Provider Gateway multi-proveedor, cupos por plan).

### Origen del codigo (importante)

Este repo se **clono del backbone `CUBOT.nails`** (a su vez derivado de `cubotcrm.git`,
la familia SaaS ECOREX) y se renombro `CubotNails.*` -> `Ecorex.*`. La columna
vertebral SaaS (Super Admin, identidad JWT, planes, integraciones, AI Gateway)
**ya viene funcionando**; lo que se construye nuevo es el **nucleo de tareas,
tableros y proyectos + los tres motores**, y se ELIMINA el dominio belleza/agenda.

- `origin`   -> https://github.com/alexandercuartas665/EcorexV.git (push del proyecto)
- `upstream` -> https://github.com/alexandercuartas665/cubotcrm.git (backports del backbone via fetch + cherry-pick)

---

## 2. Fuente de verdad (vault Obsidian)

Las especificaciones funcionales viven en:

```
C:\Users\acuartas\OneDrive - Bitcode IT Services S.A.S\Bitcode\13. Proyectos\044. Tareas\OBSIDIAN.tareas
```

Repo del vault: https://github.com/alexandercuartas665/EcorexV_obsidian.git
(empezar SIEMPRE por `00 - INDICE.md`).

Documentos maestros (leer en este orden):

1. `01. Requerimiento/Capa 0 Vision General/Vision y entorno.md` - que es ECOREX Tareas, stack, shell
2. `01. Requerimiento/Prototipo/00 - Prototipo Final ECOREX.md` - aspecto y navegacion DEFINITIVOS (abrir el HTML)
3. `03. Hoja de Ruta desarrollo/HOJA DE RUTA DESARROLLO.md` - plan de construccion (contrato de trabajo)
4. `01. Requerimiento/Capa 1 Gestion de tenant/Gestion de Empresas - Admin multi-tenant.md` - los 9 errores heredados
5. `01. Requerimiento/Capa 5 Librerias Base/00 - Vision MotherData.md` - DAL dual (origen del concepto)
6. `04. Notas para desarrollador/Seguridad y Autenticacion multi-tenant.md` - JWT, MFA, policies, RLS
7. `04. Notas para desarrollador/ADRs - Decisiones de arquitectura.md` - 10 ADRs con su porque
8. `05. Pruebas/Modelo de pruebas/Estrategia de Testing (.NET 10).md` + `CREDENCIALES - Usuarios y claves.md`
9. `02. Inventario de modulos/` (INVENTARIO GENERAL + Modelo Entidad-Relacion logico) - plano del ETL

Antes de implementar un modulo, leer su documento. No reinterpretar requerimientos de memoria.

---

## 3. Estructura del repositorio

```txt
ECOREX.tareas/
+-- apps/
|   +-- web-prototype/                # prototipo React heredado (SOLO referencia, no es el producto)
|   +-- backend/
|       +-- Ecorex.sln
|       +-- src/
|       |   +-- Ecorex.Domain/             (entidades + enums)
|       |   +-- Ecorex.Application/        (servicios, DTOs, casos de uso)
|       |   +-- Ecorex.Infrastructure/     (EF Core, migraciones, integraciones)
|       |   +-- Ecorex.Shared/             (contratos compartidos)
|       |   +-- Ecorex.Api/                (Minimal API + JWT)
|       |   +-- Ecorex.SuperAdmin/         (CONSOLA UNIFICADA Blazor: PlatformAdmin Y tenant, separados por policies)
|       |   +-- Ecorex.Web/                (Blazor Web App heredado + .Client WASM; rol final por decidir)
|       |   +-- Ecorex.Workers/            (BackgroundServices)
|       +-- tests/
|           +-- Ecorex.Domain.Tests/
|           +-- Ecorex.Application.Tests/
|           +-- Ecorex.Integration.Tests/  (Testcontainers - requiere Docker)
+-- deploy/docker/                    # docker-compose, .env.example, preflight, README
+-- docs/decisiones/                  # ADRs del repo
+-- PROGRESO.md                       # bitacora de avance por sesion (OBLIGATORIA)
+-- CLAUDE.md                         # este archivo
```

Pendientes de adaptacion del backbone (ver PROGRESO.md): agregar
`Ecorex.Infrastructure.SqlServer` (DAL dual), eliminar dominio belleza/agenda,
construir nucleo tareas/tableros/proyectos + motores, menu del Prototipo Final.

---

## 4. Stack tecnico

- **.NET 10**: toda la solucion (13 csproj) apunta a **net10.0** (SDK 10.0.301;
  migrada el 2026-07-03, ADR-0012, que reemplaza al puente net9 de ADR-0003).
- Blazor (Server interactivo) para consola. SignalR para tableros/notificaciones en vivo.
- EF Core **10.0.9** (paquetes ASP.NET Core/Extensions tambien 10.0.9): PostgreSQL
  (Npgsql.EntityFrameworkCore.PostgreSQL 10.0.2, snake_case via EFCore.NamingConventions
  10.0.1, jsonb) Y SQL Server (DAL dual). Query filters por tenant. Tool local
  dotnet-ef 10.0.9. Sin migracion Ef10ModelSync: EF10 no altero el modelo.
- Redis: cache, locks, rate limiting. RabbitMQ + MassTransit para eventos de negocio.
- Serilog + OpenTelemetry. Docker para infraestructura local.
- Clean Architecture + monolito modular preparado para microservicios.

**Frontend del producto: 100% .NET / Blazor (regla firme).** Prohibido Node/npm/React/Vue/Vite
para construir o desplegar la UI del producto. E2E con Playwright para .NET.

**Fidelidad visual (regla del usuario): la UI del workspace debe replicar
MILIMETRICAMENTE el prototipo** cuya FUENTE UNICA es
`01. Requerimiento/Prototipo/ECOREX.dc.html` del vault (+ `support.js` y las
capturas de `screenshots/`). OJO: version corregida del 2026-07-04 — el Design
habia generado 2 archivos y el SPA viejo era erroneo (ya eliminado del vault).
Toda tarea de UI extrae los tokens exactos de ese HTML (colores hex, tipografia,
espaciados, rail/sidebar, sombras, radios) y los replica tal cual; ante
cualquier duda visual gana el prototipo. Regla funcional clave: el "menu rapido"
del rail y "Administrar actividades" (000636) son EL MISMO sistema de tableros
(tableros primero -> tablero con filtros por chips, alcances equipo/mias/no
asignadas y vistas Tablero/Lista/Calendario/Gantt).
El `web-prototype` React heredado es solo referencia secundaria.

**Infraestructura local (bloque de puertos DEDICADO, prefijo `ecorex-tareas-`):**

| Servicio        | Puerto host | Contenedor                |
|-----------------|-------------|---------------------------|
| PostgreSQL 16   | 5442        | ecorex-tareas-postgres    |
| SQL Server 2022 | 1443        | ecorex-tareas-sqlserver   |
| Redis           | 6389        | ecorex-tareas-redis       |
| RabbitMQ        | 5682/15682  | ecorex-tareas-rabbitmq    |
| Adminer         | 8092        | ecorex-tareas-adminer     |

Correr `deploy/docker/preflight.ps1` ANTES de `docker compose up -d`.

---

## 5. REGLAS INVIOLABLES (los 9 errores heredados que NO se heredan)

1. **Multi-tenant REAL**: `TenantId` + `HasQueryFilter` global + RLS. PROHIBIDO filtrar
   a mano por columna. Una query cross-tenant debe ser imposible por construccion.
2. **DAL dual**: nunca SQL crudo fuera de repositorios por proveedor. Toda prueba de
   integracion corre en Postgres Y SQL Server (Testcontainers).
3. **SQL parametrizado** (cero concatenacion). Prohibido `FromSqlRaw` con interpolacion de usuario.
4. **Transacciones** en toda operacion multi-tabla; rollback total si un paso falla.
5. **Soft-delete + cascada diferida** (nunca DELETE directo de agregados). Auditoria
   inmutable (`AdminAuditLog`) en cada accion de PlatformAdmin, dentro de la transaccion.
6. **Secretos SOLO en `.env` local / Key Vault / DataProtection.** El repo es PUBLICO:
   nunca commitear credenciales, tokens ni cadenas de conexion.
7. **db3dev (BD legacy): SOLO LECTURA.** Pedir autorizacion al usuario antes de usarla.
   La conexion NO esta en el repo (ver nota del vault).
8. **Concurrencia optimista** (rowversion / xmin) en tareas y flujos.
9. **Zona horaria del tenant + UTC** para toda fecha; nada de GETDATE() implicito.

---

## 6. Multi-tenancy (regla bloqueante)

- Toda entidad de negocio implementa `ITenantScoped { Guid TenantId }`.
- `HasQueryFilter` global aplicado por reflexion en `OnModelCreating` a toda entidad scoped.
- RLS en BD como defensa en profundidad (Postgres `CREATE POLICY`; SQL Server `SECURITY POLICY`).
- Resolucion de tenant: claim JWT `tenant_id` -> subdominio/header -> 400 (nunca default silencioso).
- PlatformAdmin es el UNICO acceso cross-tenant, via `IPlatformDbContext` auditado, con MFA.
- **El test de aislamiento cross-tenant DEBE fallar si algo de esto se rompe** y corre
  en AMBOS motores. Es condicion de merge.

---

## 7. Orden de construccion (hoja de ruta del vault)

```
FASE 0  Setup: clon del backbone, renombrado, docker dedicado, CLAUDE.md, PROGRESO.md   [esta fase]
FASE 1  Multi-tenancy + Super Admin operativo (PRIMER ENTREGABLE):
        seeders PlatformAdmin + tenant demo SKY SYSTEM + planes,
        proveedor SQL Server (DAL dual), test de aislamiento dual  [BLOQUEANTE]
FASE 2  Auth (JWT + refresh + policies + MFA) y MENU del Prototipo Final (stubs por policy)
FASE 3  Nucleo operativo: TaskItem -> detalle + worklog -> tableros Kanban SignalR -> proyectos
FASE 4  Motores: WorkflowEngine (BPMN) -> DynamicFormRenderer (jsonb) -> RulesEngine
FASE 5  Modulos de sistema: Dependencias (000850) y Modulos web (000109)
FASE 6  ETL desde db3dev (tenant 01=BITCODE), idempotente, round-trip + conteos
FASE 7  CI/CD: matriz dual, gates de merge, blue/green
```

ELIMINAR del backbone (dominio belleza, no aplica): agenda, citas, turnos, asesores,
disponibilidad, no-show, Wompi de salon. Hacerlo con ADR y en commits separados.

---

## 8. Checklist antes de cada commit

- [ ] `dotnet build` verde en `apps/backend/Ecorex.sln`.
- [ ] `dotnet test` verde en proyectos tocados (Integration.Tests requiere Docker, corre dual).
- [ ] Test de aislamiento cross-tenant pasa (cuando exista, en AMBOS motores).
- [ ] Sin secretos versionados; sin credenciales/tokens en logs.
- [ ] Sin queries tenant-scoped sin filtro global; sin SQL crudo fuera de repos por proveedor.
- [ ] Operaciones multi-tabla en transaccion; borrados como soft-delete.
- [ ] Si toca PlatformAdmin: auditoria. Si toca IA: `AiUsageLog`.
- [ ] Actualizar `PROGRESO.md` (cada sesion) y, si cierra modulo, `INVENTARIO GENERAL.md` del vault.
- [ ] Decision arquitectonica nueva -> ADR en `docs/decisiones/` + reflejo en el vault.
- [ ] Archivos nuevos en ASCII.
- [ ] El CI (`.github/workflows/pr-check.yml`, ADR-0018) corre estos gates en cada PR a main: gitleaks, build Release, dotnet format, tests unitarios y la matriz dual de integracion (PG + SQL Server via Testcontainers).

---

## 9. Comandos clave del entorno local

```powershell
# Build / test
cd C:\DesarrolloIA\ECOREX.tareas\apps\backend
dotnet build Ecorex.sln
dotnet test  Ecorex.sln

# Infraestructura local (pre-flight primero)
cd C:\DesarrolloIA\ECOREX.tareas\deploy\docker
.\preflight.ps1 ; docker compose up -d ; docker compose ps

# Migraciones EF (startup-project = Infrastructure, ejecutar desde apps/backend)
dotnet ef migrations add NombreMigracion `
  --project src/Ecorex.Infrastructure --startup-project src/Ecorex.Infrastructure
dotnet ef database update `
  --project src/Ecorex.Infrastructure --startup-project src/Ecorex.Infrastructure

# Levantar la consola unificada
.\start-ecorex.ps1        # desde la raiz del repo
```

---

## 10. Registro de avance (OBLIGATORIO, cada sesion)

- **En este repo**: agregar entrada a `PROGRESO.md` (fecha, agentes, hecho, siguiente,
  bloqueos, decisiones) y ADRs nuevos en `docs/decisiones/`.
- **En el vault** (repo aparte, commit + push): INVENTARIO GENERAL al cerrar modulo,
  ADRs al decidir, `05. Pruebas/Historial de pruebas/00 - Registro de corridas.md` al testear.
