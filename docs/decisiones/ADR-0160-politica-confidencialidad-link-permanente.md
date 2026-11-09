# ADR-0160: Politica de confidencialidad por tenant con link permanente y constancia de aceptacion

**Status:** Accepted (Fase 1)
**Date:** 2026-11-09
**Deciders:** Alexander Cuartas (dev principal)

## Contexto

Cada tenant necesita publicar SU politica de confidencialidad por un enlace que pueda compartir (p.ej.
el consentimiento que Meta/WhatsApp exige para la mensajeria), de forma que quien la abra la lea y deje
CONSTANCIA de aceptacion. El pedido del usuario: ponerlo "donde estan las plantillas de WhatsApp"
(`/plantillas-whatsapp`), una politica por empresa, UN link permanente por tenant (que NO vence), y que
el link permita "ver y aceptar" dejando constancia (nombre e identificacion + fecha).

Ya existe el patron de pagina publica anonima por token: `/f/{token}` (FormPublic, ADR-0015) y
`/d/{token}` (Decision, ADR-0115). Ambas resuelven el token con `IgnoreQueryFilters` (unico punto
cross-tenant) y luego fijan el tenant como ambient (`AmbientTenantContext.Begin`). Se reutiliza ese patron.

## Decision

1. **Dos entidades tenant-scoped** (`ITenantScoped`), sin tocar las existentes:
   - `ConfidentialityPolicy` (soft-delete + `IVersioned`): UNA por tenant (indice unico en `TenantId`
     filtrado por `is_deleted = false`). Campos: `Titulo`, `Contenido` (HTML saneado), `PublicToken`,
     `Activa`.
   - `ConfidentialityPolicyAcceptance` (append-only, inmutable): `PolicyId`, `Nombre`, `Identificacion`,
     `Correo?`, `AceptadoAt`, `Ip?`, `UserAgent?`, `PolicyVersionSnapshot`, `ContenidoHashSnapshot`.

2. **Token EN CLARO** (no hash). A diferencia de `FormToken` (secreto, un-solo-uso, se muestra una vez),
   el link de la politica es un recurso PUBLICO y PERMANENTE que el admin copia cuando quiera. Se guarda
   en claro -misma convencion que `Tenant.PublicBookingToken`-; es un valor aleatorio de 256 bits
   (base64url), inenumerable. El link NO vence (no hay `ExpiresAt`); se puede REGENERAR (invalida el anterior).

3. **Resolucion cross-tenant acotada**: `ResolveByTokenAsync` busca por `PublicToken` exacto con
   `IgnoreQueryFilters` (el visor anonimo no tiene tenant), exige `Activa && !IsDeleted`, y devuelve el
   `TenantId` para que la pagina fije el ambient antes de registrar la aceptacion. Token invalido /
   inexistente / deshabilitado devuelven el MISMO resultado neutro.

4. **Constancia con snapshot**: cada aceptacion guarda la `Version` de la politica y el hash SHA-256 del
   contenido aceptado, de modo que un edit posterior de la politica no tergiverse lo que la persona acepto.

5. **Saneo de HTML por allowlist** (`ConfidentialityHtmlSanitizer`, con AngleSharp ya referenciado): el
   contenido lo escribe el admin pero se muestra en una pagina PUBLICA; al GUARDAR se descartan
   script/style/iframe/object/form/..., se desenvuelven los tags no permitidos conservando su texto, y de
   los permitidos solo sobreviven atributos de una lista corta (href/src validados http/https/mailto). Un
   `MarkupString` en la pagina publica no puede ejecutar script inyectado (XSS almacenado). La pagina
   re-sanea por defensa en profundidad.

6. **Concurrencia (ADR-0157/0138)**: lecturas por contexto EFIMERO (`IApplicationDbContextFactory`),
   escrituras por el `_db` del circuito. La pagina publica aisla la aceptacion en el ambient del token.

7. **UI**: panel `ConfidentialityPolicyPanel` incrustado en `/plantillas-whatsapp` (misma policy
   `PlantillasWhatsApp.Editar`): editor HTML + vista previa saneada, link permanente con copiar/abrir/
   regenerar, y tabla de constancias. Pagina publica `PoliticaConfidencialidad` (`/pc/{token}`,
   `[AllowAnonymous]`, `EmptyLayout`, sin prerender).

## Opciones consideradas

- **Reusar `FormToken` (hash, con expiracion)**: descartado; el link debe ser permanente y re-copiable, no
  un secreto de un solo uso.
- **Guardar el token hasheado**: obligaria a mostrarlo una sola vez; rompe "el admin copia el link cuando
  quiera". Un recurso publico no necesita hash.
- **Alcance por tercero (un link por contacto)**: el usuario pidio por tenant (un link por empresa).
- **Sin saneo (confiar en el admin)**: inaceptable para una pagina publica (XSS almacenado hacia los
  clientes del tenant).

## Consecuencias

- Mas facil: cada tenant publica y versiona su politica sin codigo; la constancia es verificable (version
  + hash del contenido aceptado).
- Mas dificil / a vigilar: el saneo por allowlist puede recortar HTML exotico del admin (por diseno). El
  link en claro es inenumerable pero publico: quien lo tenga ve la politica (es el objetivo).
- A revisar luego (Fase 2): captura de IP/User-Agent en la aceptacion (hoy quedan null en el visor
  interactivo), exportar constancias a CSV/PDF, y opcionalmente exigir correo o segundo campo configurable.

## Action Items

1. [x] Entidades + migracion dual `AddConfidentialityPolicies` (PG + SQL Server).
2. [x] Servicio `IConfidentialityPolicyService` + saneador + registro DI.
3. [x] Panel admin en `/plantillas-whatsapp` + pagina publica `/pc/{token}`.
4. [x] Tests de integracion dual (alta/saneo/resolucion/aceptacion/regeneracion/aislamiento).
5. [ ] Reflejar ADR-0160 en el vault.
6. [ ] Deploy a prod con OK explicito del usuario por version.
