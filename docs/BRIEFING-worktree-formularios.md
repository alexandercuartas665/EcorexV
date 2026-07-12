# BRIEFING - Worktree "formularios dinamicos" (leer ANTES de tocar codigo)

> Para la sesion nueva que trabajara SOLO en Formularios Dinamicos en un git worktree,
> compartiendo la infraestructura local con la sesion principal (que sigue en pruebas/ajustes).
> Objetivo: avanzar fuerte en formularios SIN romper la sesion principal ni la BD compartida.
> Convencion del repo: ASCII, multi-tenant real, DAL-dual (Postgres + SQL Server). Ver CLAUDE.md.

---

## 1. Donde estan las bases de datos (local)

Bloque Docker DEDICADO, prefijo `ecorex-tareas-` (definido en `deploy/docker/docker-compose.yml`).
Estos contenedores son GLOBALES: los comparten la sesion principal, este worktree y los tests.

| Servicio        | Contenedor              | Puerto host | DB / credenciales locales                                  |
|-----------------|-------------------------|-------------|------------------------------------------------------------|
| PostgreSQL 16   | ecorex-tareas-postgres  | **5442**    | DB `ecorex_dev`, user `ecorex`, pass `EcorexDev2026pg`      |
| SQL Server 2022 | ecorex-tareas-sqlserver | **1443**    | DB `ecorex_dev`, user `sa`, pass `EcorexDev2026sql`         |
| Redis           | ecorex-tareas-redis     | 6389        | -                                                          |
| RabbitMQ        | ecorex-tareas-rabbitmq  | 5682/15682  | -                                                          |
| Adminer         | ecorex-tareas-adminer   | 8092        | consola web para inspeccionar Postgres                     |

- **`ecorex_dev` en Postgres:5442 es la BD que usa la app de la sesion principal** (la que corre en
  `localhost:5234`). ES LA BD "que tenemos ahora".
- SQL Server:1443 se usa para los tests de integracion DAL-dual (Testcontainers levanta contenedores
  efimeros aparte; el 1443 fijo es para migraciones/design-time).
- **Produccion (`10.0.0.3`, app en puerto 5480, tunel SSH `localhost:15433`) queda FUERA de alcance de
  este worktree.** No conectarse a prod, no desplegar. Trabajar SOLO contra local.

---

## 2. El riesgo #1: migraciones EF sobre la BD compartida

La app aplica migraciones al arrancar y el modelo EF exige que el esquema case con el snapshot. Si este
worktree agrega migraciones y las aplica sobre `ecorex_dev` (la BD compartida), pasa esto:

- Cambia el esquema y `__EFMigrationsHistory` por debajo de la sesion principal.
- El **model snapshot** diverge -> cuando CUALQUIERA de las dos sesiones agregue otra migracion, chocan
  los snapshots (conflicto de git + error "pending model changes" de EF).
- Si una migracion de formularios ALTERA/renombra/borra una columna que la sesion principal usa, la app
  de la sesion principal se rompe en runtime.
- Dos apps arrancando contra la misma BD ademas compiten al sembrar el demo (DatabaseSeeder).

### Recomendacion FUERTE: BD propia para el worktree (misma infra, esquema aislado)

Usa el MISMO servidor Postgres (mismo contenedor, mismo puerto 5442) pero una **BD distinta**, p.ej.
`ecorex_forms`. Asi migras libremente sin tocar `ecorex_dev`.

```bash
# crear la BD del worktree en el MISMO Postgres
docker exec ecorex-tareas-postgres createdb -U ecorex ecorex_forms

# (opcional) copiar los datos actuales para trabajar con data real
docker exec ecorex-tareas-postgres sh -c "pg_dump -U ecorex ecorex_dev | psql -U ecorex ecorex_forms"
```

Y apunta la app del worktree a `ecorex_forms` (ver seccion 4). Cuando merges a la rama de integracion,
las migraciones viajan en el codigo y se aplican a `ecorex_dev`/prod de forma controlada.

### Si INSISTES en usar el mismo `ecorex_dev` (data compartida): reglas estrictas

1. **Un solo dueno de migraciones a la vez.** Mientras el worktree hace churn de esquema, la sesion
   principal NO agrega migraciones. Coordinar el merge.
2. **Solo aditivo.** Los formularios son TABLAS NUEVAS (`form_*`) -> no alterar/borrar tablas o columnas
   que la sesion principal usa (task_items, notifications, projects, etc.).
3. **Apaga el seed y el migrate-al-arrancar del worktree** para no re-sembrar `ecorex_dev`
   (`Ecorex:SkipDemoSeed=true`; no correr `database update` de golpe si la principal esta arriba).
4. Aun asi el snapshot diverge: planea el merge y rebasea seguido.

---

## 3. Aislar el worktree (lo que comparte y lo que no)

`git worktree` aisla el **arbol de trabajo + la rama**. NO aisla: contenedores Docker, bases de datos ni
puertos de host. Cuidados:

- **Crear el worktree** desde la rama de integracion:
  ```bash
  git worktree add ../ECOREX.formularios -b formularios fase-0/clon-backbone
  ```
- **NO correr `docker compose down` ni `preflight.ps1`** desde el worktree: tumbaria/recrearia los
  contenedores que la sesion principal esta usando (perderias su BD si algo va mal). Los contenedores ya
  estan arriba; reutilizarlos.
- **Puerto de la app del worktree DISTINTO de 5234.** Ej: `5236`. Nunca dos apps en el mismo puerto.
- La app de la sesion principal quedo corriendo en `localhost:5234` (no matarla).

---

## 4. Levantar la app del worktree (BD propia, puerto propio)

`GetConnectionString("Default")` gana sobre `ECOREX_DB_CONNECTION`. La forma simple es setear el env y
correr el SuperAdmin en otro puerto:

```powershell
# desde el worktree: apps/backend/src/Ecorex.SuperAdmin
$env:ECOREX_DB_CONNECTION = 'Host=localhost;Port=5442;Database=ecorex_forms;Username=ecorex;Password=EcorexDev2026pg'
$env:ASPNETCORE_URLS       = 'http://localhost:5236'
$env:ASPNETCORE_ENVIRONMENT= 'Development'
dotnet run --no-launch-profile
```

Login demo (tenant SKY SYSTEM): `owner@sky-system.local` / `Demo123*` (fixtures de prueba, no son
secretos reales). Si copiaste `ecorex_dev` a `ecorex_forms` ya tendras esos usuarios.

---

## 5. DAL-dual: toda migracion va en DOS contextos

Hay dos DbContext y dos assemblies de migraciones (regla inviolable del proyecto):

- **Postgres**: `EcorexDbContext` (en `Ecorex.Infrastructure`), migraciones en
  `src/Ecorex.Infrastructure/Persistence/Migrations`.
- **SQL Server**: `SqlServerEcorexDbContext`, migraciones en `src/Ecorex.Infrastructure.SqlServer/Migrations`.

Comandos EF (ejecutar desde `apps/backend`):

```powershell
# 1) Postgres (startup-project = Infrastructure)
dotnet ef migrations add NombreMigracion `
  --project src/Ecorex.Infrastructure --startup-project src/Ecorex.Infrastructure

# 2) SQL Server (mismo cambio, otro contexto/assembly)
dotnet ef migrations add NombreMigracion `
  --project src/Ecorex.Infrastructure.SqlServer --startup-project src/Ecorex.Infrastructure.SqlServer `
  --context SqlServerEcorexDbContext
```

Reglas al modelar entidades de formularios:
- Nueva entidad de negocio -> hereda de `TenantEntity` / implementa `ITenantScoped { Guid TenantId }`.
  El `HasQueryFilter` global se aplica por reflexion; no filtrar tenant a mano.
- **Enum nuevo -> registrarlo en `ConfigureConventions` (HaveConversion<string>()) de EcorexDbContext**,
  o EF lo mapea como int y rompe el patron.
- FKs: en `OnModelCreating` el proyecto usa comportamiento condicional por proveedor
  (`isNpgsql ? Cascade : Restrict`) para evitar el error 1785 de SQL Server (ciclos/multiples cascadas).
  Copiar ese patron para relaciones nuevas con auto-referencia o multiples caminos.
- Dinero: `HasColumnType(isNpgsql ? "numeric(18,2)" : "decimal(18,2)")`.

---

## 6. El modulo de formularios YA EXISTE (extender, no crear de cero)

Antes de disenar nada, leer lo que hay:

- **Dominio**: `FormDefinition`, `FormContainer`, `FormQuestion`, `FormResponse`, `FormFieldRule`,
  `FormToken`, `FormFlowLink`, `WorkflowNodeForm` (en `src/Ecorex.Domain/Entities/`).
- **UI**: `FormDesigner.razor` (+ `.css`), `FormPublic.razor`, `Formularios.razor` (en
  `src/Ecorex.SuperAdmin/Components/Pages/`).
- Concepto EAV -> jsonb (ver Vision MotherData / motores en CLAUDE.md seccion 1 y 4).
- Tu documento fuerte de spec manda sobre el requerimiento; el codigo existente manda sobre la forma
  actual. Reconciliar antes de romper contratos.

---

## 7. Archivos "calientes" (posible conflicto con la sesion principal)

Estos los tocan ambas sesiones -> cambios localizados y rebase frecuente:

- `src/Ecorex.Infrastructure/Persistence/EcorexDbContext.cs` (OnModelCreating, DbSets, ConfigureConventions).
- `src/Ecorex.SuperAdmin/Program.cs` (DI, policies).
- Config de menu (`MenuConfig*`) si agregas entradas de menu para formularios.

Mantener el trabajo de formularios lo mas contenido posible (tablas `form_*`, servicios y paginas
propias) reduce el area de conflicto.

---

## 8. Checklist del worktree antes de cada commit (subset de CLAUDE.md)

- [ ] `dotnet build apps/backend/Ecorex.sln` verde.
- [ ] Migracion agregada en AMBOS contextos (PG + SQL Server) si hubo cambio de esquema.
- [ ] Entidades nuevas: TenantId + hereda TenantEntity; enums registrados en ConfigureConventions.
- [ ] Sin secretos versionados (repo PUBLICO). Credenciales solo en `.env`/`appsettings.*.local.json`.
- [ ] No tocaste prod ni `docker compose down`. App del worktree en su puerto (5236), BD propia.
- [ ] Actualizar PROGRESO.md (tu sesion) y rebasear sobre `fase-0/clon-backbone`.

---

## TL;DR

1. La BD viva es **Postgres:5442 / `ecorex_dev`** (la usa la sesion principal en `localhost:5234`).
2. **No compartas el esquema para migrar**: crea `ecorex_forms` en el MISMO Postgres y trabaja ahi.
3. App del worktree en **otro puerto (5236)**; **no** `docker compose down`, **no** prod.
4. Todo cambio de esquema -> migracion en **PG y SQL Server**; enums en ConfigureConventions; TenantId.
5. Formularios YA existe: leer `FormDefinition`/`FormDesigner.razor` antes de disenar.
