# ADR-0005: Autenticacion seccion 6 - JWT propio, local-first, Google diferido

**Fecha:** 2026-05-19
**Estado:** Aceptado
**Corresponde a:** Hoja de ruta seccion 6 y Notas de desarrollo sec.1 (login Google)

## Contexto

La seccion 6 pide login, selector de tenant, claims (sub, tenant_id, platform_role, tenant_role, permissions), politicas de autorizacion y pruebas de acceso cruzado. Las Notas de desarrollo describen login con Google (OIDC) en profundidad, pero eso requiere credenciales de Google Cloud (client id/secret, redirect URIs) que aun no estan provisionadas.

Tanto el login local como el de Google terminan igual: ECOREX.tareas emite su propio JWT tras validar la identidad. Esa parte (emision de token, claims, /connect/me, switch-tenant, politicas) es agnostica al proveedor.

## Decision

Implementar primero el flujo **credenciales locales -> JWT propio**, testeable end-to-end, con todo el andamiaje compartido. El login con **Google OIDC se difiere** a un slice posterior cuando existan credenciales de Google Cloud.

Detalles:

- **Credenciales locales:** `PlatformUser.PasswordHash` (nullable). Hash con **PBKDF2** (Rfc2898 SHA256, 100k iteraciones, salt 16 bytes, subkey 32 bytes), formato versionado, sin dependencias externas.
- **JWT propio:** HMAC-SHA256 simetrico. `Issuer`, `Audience`, `SigningKey`, `AccessTokenMinutes` desde configuracion (`Jwt` section). El `SigningKey` NO se versiona: en Development se genera una clave efimera por arranque si no esta configurada (con warning); en otros entornos es obligatoria via env/secrets. Los tests inyectan una clave fija.
- **Claims:** `sub` (PlatformUser.Id), `email`, `name`, `tenant_id` (cuando hay tenant activo), `platform_role` (operador SaaS), `tenant_role` (rol en la agencia activa), `permissions` (vacio por ahora).
- **Resolucion de tenant:** en login se consultan las membresias del usuario con `IgnoreQueryFilters` (operacion cross-tenant legitima). 1 tenant -> token con tenant_id; varios -> token sin tenant_id y `TenantSelectionRequired=true` (obligando /connect/switch-tenant); 0 tenants -> token autenticado sin tenant. Super Admin no depende de tenant.
- **Politicas:** `SuperAdminOnly` (claim platform_role=SuperAdmin) y `TenantMember` (claim tenant_id presente).
- **ITenantContext:** implementacion `HttpContextTenantContext` que lee `tenant_id` y `sub` de los claims del request.

## Endpoints (seccion 6.1)

| Endpoint | Proposito |
|----------|-----------|
| `POST /connect/token` | Login local: email+password (+tenantId opcional) -> JWT |
| `GET /connect/me` | Usuario, rol, tenants disponibles y tenant activo |
| `POST /connect/switch-tenant` | Cambia tenant activo y reemite token |
| `GET /platform/me` | Info de operador (requiere SuperAdminOnly) |
| `GET /tenant/configurations` | Recurso tenant-scoped de ejemplo (requiere TenantMember) para validar aislamiento por JWT |

## Consecuencias

- Application gana una dependencia de `Microsoft.EntityFrameworkCore` via `IApplicationDbContext` (patron clean-arch estandar) para alojar `AuthService` como caso de uso.
- Google OIDC pendiente: implementar `GET /connect/google` + `GET /signin-google`, validacion de id_token, vinculacion via invitacion/ExternalLogin. Requiere ADR/credenciales.
- `permissions` granulares quedan para el modulo 1.2 (Usuarios, Roles y Permisos).
- Refresh tokens y logout server-side se difieren (token de vida corta por ahora).

## Validacion (seccion 6.3)

Test de integracion con WebApplicationFactory + Testcontainers.PostgreSql:
- Login devuelve token; usuario con 1 tenant trae tenant_id; con varios exige selector.
- /connect/me lista tenants; switch-tenant reemite con tenant_id.
- Endpoint protegido sin token -> 401.
- Usuario de tenant en /platform/me -> 403 (SuperAdminOnly).
- Acceso cruzado: token de tenant A solo ve datos tenant-scoped de A.
