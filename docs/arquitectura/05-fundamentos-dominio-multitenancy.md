# Fundamentos de dominio y multi-tenancy

**Estado:** Propuesta para alineacion (pre-codigo)
**Fecha:** 2026-05-19
**Corresponde a:** Hoja de ruta seccion 5
**Fuente de verdad:** `Capa 1 Gestion de tenant/Gestion de Tenant - Super Admin SaaS.md` (modelo de datos seccion 14) y `04. Notas para desarrollador/Notas de desarrollo.md` (PlatformUser / login Google)

Este documento fija las decisiones de base antes de escribir entidades. Nada se considera implementado hasta que exista codigo + test verde. Convencion de nombres: clases en ingles, mensajes de usuario en espanol, archivos del proyecto en ASCII.

---

## Punto 1 - BaseEntity y TenantEntity

Dos clases base en `CubotTravels.Domain`:

```
BaseEntity (abstracta)
  Id          : Guid          # PK, ver punto 2
  CreatedAt   : DateTimeOffset # UTC, lo setea el interceptor
  CreatedBy   : Guid?          # PlatformUser/TenantUser que creo el registro
  UpdatedAt   : DateTimeOffset?
  UpdatedBy   : Guid?

TenantEntity : BaseEntity (abstracta)
  TenantId    : Guid          # obligatorio, no Guid.Empty
```

Reglas:

- Toda entidad **operativa de un tenant** hereda de `TenantEntity` y por lo tanto lleva `TenantId`.
- Las entidades **globales / administrativas** heredan de `BaseEntity` (sin `TenantId` propio aunque puedan referenciar un tenant como FK; ver distincion en punto 4).
- `CreatedAt`/`UpdatedAt` los gestiona un interceptor de `SaveChanges`, no el codigo de negocio.
- `CreatedBy`/`UpdatedBy` se resuelven desde el contexto de usuario actual cuando exista; pueden ser null en procesos de sistema (seed, workers) y eso queda reflejado en auditoria.

**Soft delete: DIFERIDO.** No se agrega `DeletedAt` en esta fase (decision acordada). La retencion de datos exigida por el producto al **suspender un tenant** se resuelve via `TenantStatus`, no via borrado logico de filas. Si mas adelante se necesita soft delete por entidad, se agrega `DeletedAt` nullable + filtro global; el diseno de `BaseEntity` lo permite sin romper datos.

---

## Punto 2 - Tipo de clave primaria

**Decision: `Guid` (UUID) generado en la aplicacion con version 7 (time-ordered).**

- Coincide con el modelo conceptual del Super Admin SaaS (`id UUID PRIMARY KEY`).
- `Guid.CreateVersion7()` (disponible en .NET 9) produce GUIDs ordenables por tiempo -> mejor localidad de indice en PostgreSQL que un GUID v4 aleatorio, evitando fragmentacion de paginas.
- La generacion en app (no en BD) simplifica el patron unit-of-work, permite conocer el Id antes de persistir y facilita pruebas deterministas.
- Columna PostgreSQL: `uuid`. No usamos `gen_random_uuid()` en BD porque el Id lo asigna el dominio.

Alternativas descartadas: `int`/`bigint` identity (revela volumen, complica multi-tenant y sharding futuro), GUID v4 (fragmenta indices).

---

## Punto 3 - Entidades globales (Capa 0 / administrativas)

Heredan de `BaseEntity`. Las administra el Super Admin / plataforma. **No** llevan filtro de query por tenant (se protegen con politicas de autorizacion de plataforma).

| Clase | Tabla | Origen | Notas clave |
|-------|-------|--------|-------------|
| `Tenant` | `tenants` | SaaS sec.14 | name, legal_name, tax_id, country, currency, status (`TenantStatus`), kind (`TenantKind`) |
| `SaasPlan` | `saas_plans` | SaaS sec.14 | name, description, monthly_price, yearly_price, currency, is_active |
| `SaasPlanLimit` | `saas_plan_limits` | SaaS sec.14 | plan_id FK, limit_key, limit_value (bigint), limit_unit, enforcement_mode (`LimitEnforcementMode`) |
| `TenantSubscription` | `tenant_subscriptions` | SaaS sec.14 | tenant_id FK, plan_id FK, status (`SubscriptionStatus`), billing_frequency (`BillingFrequency`), starts_at, current_period_ends_at, grace_period_ends_at |
| `TenantPayment` | `tenant_payments` | SaaS sec.14 | tenant_id FK, subscription_id FK, provider, provider_reference, amount, currency, status (`PaymentStatus`), billing_period_start/end, created_at, confirmed_at |
| `WompiWebhookEvent` | `wompi_webhook_events` | SaaS sec.14 | provider_event_id UNIQUE, signature_valid, raw_payload jsonb, processing_status (`WebhookProcessingStatus`), received_at, processed_at |
| `PlatformErrorLog` | `platform_error_logs` | SaaS sec.14 | severity (`ErrorSeverity`), module, tenant_id (nullable), correlation_id, message, sanitized_payload jsonb, status |
| `SuperAdminAuditLog` | `super_admin_audit_logs` | SaaS sec.14 | actor_user_id, actor_type (`AuditActorType`), action_name, entity_name, entity_id, previous_value/new_value jsonb, reason, ip_address, created_at |
| `PlatformUser` | `platform_users` | Notas dev sec.1.5 | email, email_verified, display_name, avatar_url, google_subject, auth_provider, status, last_login_at, platform_role (`PlatformRole`, nullable) |
| `ExternalLogin` | `external_logins` | Notas dev sec.1.5 | platform_user_id FK, provider, provider_subject, email_at_login, linked_at, last_used_at. **Opcional**, habilita multi-proveedor; se puede diferir a la fase de login |

Para esta fase 5 se crean al menos: `Tenant`, `SaasPlan`, `SaasPlanLimit`, `TenantSubscription`, `TenantPayment`, `PlatformUser`, `SuperAdminAuditLog`. `WompiWebhookEvent`, `PlatformErrorLog` y `ExternalLogin` pueden entrar aqui o en su modulo correspondiente (facturacion / observabilidad / login).

---

## Punto 4 - Entidades tenant-scoped (Capa 1)

Heredan de `TenantEntity` (llevan `TenantId`) y **si** reciben filtro global de query por tenant.

| Clase | Tabla | Notas |
|-------|-------|-------|
| `TenantUser` | `tenant_users` | platform_user_id FK, tenant_id, tenant_role (`TenantRole`), status, permisos internos. Indice unico `(tenant_id, platform_user_id)` y `(tenant_id, email)` |
| `TenantConfiguration` | `tenant_configurations` | tenant_id, clave/valor de configuracion comercial base (etapas por defecto, horarios, tono). Indice unico `(tenant_id, config_key)` |

**Distincion critica (FK de tenant != tenant-scoped):**

- `TenantSubscription`, `TenantPayment`, `PlatformErrorLog` tienen una **columna** `tenant_id` pero son **globales**: las consulta y administra el Super Admin sobre todos los tenants. NO reciben filtro global de query.
- El filtro global de query (`HasQueryFilter`) se aplica **solo** a entidades que un usuario de agencia opera en su dia a dia (`TenantUser`, `TenantConfiguration`, y mas adelante leads, chats, pipelines, etc.).
- Confundir ambos casos romperia la consola Super Admin (no podria ver datos de varios tenants) o abriria fuga de datos (un tenant veria suscripciones de otro). La frontera es: **operativo del tenant -> query filter; administrativo del SaaS -> autorizacion de plataforma.**

Se marca con una interfaz `ITenantScoped { Guid TenantId { get; } }` para que el DbContext aplique el filtro por reflexion solo a quien la implementa.

---

## Punto 5 - Enums

En `CubotTravels.Domain` (o `CubotTravels.Shared` si el frontend los necesita). Se persisten como **texto** en PostgreSQL (no int) para legibilidad y estabilidad ante reordenamientos; columnas `varchar`.

```
TenantStatus            : Trial, Active, PendingPayment, PastDue, Suspended, Blocked, Closing, Archived
TenantKind              : Standard, Demo, Internal, Test          # tipo de cuenta (sec.5), separado del estado
SubscriptionStatus      : Trialing, Active, PendingPayment, PastDue, GracePeriod, Suspended, Cancelled, AdminException
BillingFrequency        : Monthly, Yearly
PaymentStatus           : Pending, Approved, Declined, Voided, Error, NeedsReview   # alineado a estados Wompi + revision interna
PlatformRole            : SuperAdmin, FinanceOperator, SupportOperator, TechnicalOperator, Auditor, Analyst   # sec.13
```

Enums de soporte (mismo criterio, texto):

```
LimitEnforcementMode    : Hard, Soft                              # sec.6 limites duros/blandos
WebhookProcessingStatus : Pending, Processed, Failed, Ignored, NeedsReview
ErrorSeverity           : Info, Warning, Error, Critical
AuditActorType          : Human, System                          # sec.12 humano vs worker
TenantRole              : Owner, Admin, Supervisor, Advisor       # rol interno de agencia (se refina en modulo 1.2)
```

`TenantRole` se deja minimo aqui; su detalle vive en el futuro documento "Usuarios Roles y Permisos del Tenant".

---

## Punto 6 - CubotTravelsDbContext

Vive en `CubotTravels.Infrastructure`. Provider Npgsql.

Componentes:

1. **DbSets** para todas las entidades de los puntos 3 y 4.
2. **`ITenantContext`** (interfaz en `CubotTravels.Application`, implementacion en Infrastructure/Api): expone `Guid? TenantId` y `Guid? UserId` del request actual. Resuelto desde el claim `tenant_id` del JWT (ver Notas dev). En workers/seed puede no haber tenant.
3. **Filtro global de query**: en `OnModelCreating`, para cada entidad que implemente `ITenantScoped`, aplicar
   `modelBuilder.Entity<T>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId)`.
   Si `TenantId` del contexto es null, el filtro no devuelve filas tenant-scoped (fail-closed). Acceso administrativo cross-tenant usa `IgnoreQueryFilters()` en servicios de plataforma controlados.
4. **snake_case**: usar paquete `EFCore.NamingConventions` -> `optionsBuilder.UseSnakeCaseNamingConvention()`. Asi `TenantUser.PlatformUserId` -> columna `platform_user_id`, tabla `tenant_users`.
5. **Interceptor de auditoria** (`SaveChangesInterceptor`): setea `CreatedAt`/`CreatedBy` en Added y `UpdatedAt`/`UpdatedBy` en Modified, usando `ITenantContext.UserId` y un `TimeProvider` (UTC).
6. **Indices**: unicos por tenant donde aplique (`tenant_users (tenant_id, platform_user_id)`, `tenant_configurations (tenant_id, config_key)`); unico global `wompi_webhook_events (provider_event_id)`; unico `platform_users (google_subject)` y `(email)` normalizado.
7. **Tipos**: `DateTimeOffset` -> `timestamptz`; `decimal(12,2)` para montos; `jsonb` para payloads/valores de auditoria.
8. **Migraciones**: EF Core Migrations contra la BD Docker local (`Host=localhost;Port=5434;Database=cubot_travels_dev`). Carpeta `apps/backend/src/CubotTravels.Infrastructure/Migrations`.

Paquetes NuGet (versiones compatibles net9): `Npgsql.EntityFrameworkCore.PostgreSQL`, `EFCore.NamingConventions`, `Microsoft.EntityFrameworkCore.Design`.

---

## Punto 7 - Test de aislamiento (bloqueante)

Proyecto `CubotTravels.Integration.Tests`. **Bloqueante**: ningun modulo tenant-scoped avanza sin esto verde.

Estrategia de BD para el test:

- **Testcontainers.PostgreSql** (levanta un Postgres efimero por corrida). Razon: reproducible en CI sin depender de la pila Docker local ni de puertos; aislado entre corridas. Requiere Docker disponible en el runner (ya lo esta en local; en CI se documenta en seccion 12 de la hoja de ruta).
- Alternativa local rapida: apuntar a la BD `cubot_travels_dev` del compose, pero se prefiere Testcontainers para no ensuciar datos.

Escenario:

```
1. Migrar esquema en la BD efimera.
2. Crear tenant A y tenant B (entidades globales, sin filtro).
3. Con ITenantContext = A: insertar N TenantUser/TenantConfiguration de A.
4. Con ITenantContext = B: insertar M registros de B.
5. Aserciones:
   - Con contexto A activo: la consulta tenant-scoped devuelve solo filas de A (no ve B).
   - Con contexto B activo: devuelve solo filas de B (no ve A).
   - Con contexto sin tenant (null): la consulta tenant-scoped NO devuelve filas (fail-closed).
   - Las entidades globales (Tenant, SaasPlan) se acceden con permisos de plataforma / IgnoreQueryFilters.
```

Criterios de aceptacion (hoja de ruta sec.5.3): sin tenant activo no hay datos tenant-scoped; con A activo no se ve B; con B activo no se ve A; globales solo via servicios administrativos controlados.

---

## Orden de implementacion propuesto (cuando se apruebe)

1. Enums + `BaseEntity`/`TenantEntity` + interfaz `ITenantScoped` en Domain.
2. Entidades globales y tenant-scoped en Domain.
3. `ITenantContext` en Application.
4. `CubotTravelsDbContext` + naming + filtros + interceptor + configuraciones en Infrastructure.
5. Migracion inicial contra Postgres local.
6. Test de aislamiento en Integration.Tests con Testcontainers.
7. `dotnet build` + `dotnet test` verdes -> commit.

---

## Puntos que NO se deciden aqui (quedan abiertos)

- Detalle fino de `TenantRole` y permisos -> documento "Usuarios Roles y Permisos del Tenant".
- Cobro automatico vs enlaces manuales Wompi -> pregunta abierta SaaS sec.18.
- Cobro por exceso vs bloqueo duro de limites -> pregunta abierta SaaS sec.18.
- Campos tributarios Colombia -> pregunta abierta SaaS sec.18.
- Impersonacion Super Admin -> pregunta abierta SaaS sec.18.
