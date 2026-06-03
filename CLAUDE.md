# CLAUDE.md - Memoria del agente de desarrollo para CUBOT.nails

> Primera lectura obligatoria para cualquier agente (Claude Code u otro) antes de
> modificar codigo en este repositorio. Reglas pequenas, concretas y verificables.
> Convencion del proyecto: **solo ASCII** en archivos nuevos (sin tildes ni enie).

---

## 1. Contexto del proyecto

CUBOT.nails es un **SaaS multi-tenant de agenda y turnos para centros de belleza**
(salones de unas, peluquerias, spas, barberias). El activo critico del negocio es
el **tiempo disponible de cada profesional y cada estacion**, no un catalogo ni un
embudo de ventas. El corazon del producto es un **motor de agenda** que calcula
disponibilidad real, evita choques de horario y permite reservar, reprogramar y
liberar citas con trazabilidad completa.

Pilares:

- Reservas y confirmaciones por **WhatsApp** via Evolution API.
- Recordatorios automaticos y control de **no-show**.
- **Agentes de IA** (recepcionista virtual que propone horarios reales, nunca inventa).
- **Facturacion SaaS** con Wompi maestro y planes con limites.
- **Super Admin** de plataforma separado del admin de tenant (salon).

### Origen del codigo (importante)

Este repo se **clono desde `cubotcrm.git`** (codigo de CUBOT.travels, el hermano
mayor de la familia CUBOT) y se renombro `CubotTravels` -> `CubotNails`. Toda la
columna vertebral SaaS (Super Admin, IA, Evolution, identidad, marca, Wompi) **ya
viene funcionando**; lo que se construye nuevo es el **nucleo operativo del salon**
(agenda, turnos, citas, asesores, cadenas, puntualidad).

- `origin`   -> https://github.com/alexandercuartas665/CUBOT.nails.git (push del proyecto nails)
- `upstream` -> https://github.com/alexandercuartas665/cubotcrm.git (backports de la columna vertebral hermana via fetch + cherry-pick)

---

## 2. Fuente de verdad (vault Obsidian)

Las especificaciones funcionales viven en:

```
C:\Users\acuartas\Documents\Personal\OneDrive\Proyectos\08. Agente Belleza\CUBOT.nails
```

Documentos maestros (leer en este orden):

1. `02. Inventario de modulos/INVENTARIO GENERAL.md` - modulos, capas, dependencias, tracker
2. `03. Hoja de Ruta desarrollo/HOJA DE RUTA DESARROLLO.md` - plan paso a paso (contrato de trabajo)
3. `01. Requerimiento/Capa 0 Vision General/CUBOT.nails.md` - arquitectura general
4. `01. Requerimiento/Capa 2 Agenda y Turnos/Agenda, Turnos y Citas - Nucleo Operativo.md` - el corazon
5. `01. Requerimiento/Capa 1 Gestion de tenant/Gestion de Tenant - Super Admin SaaS.md` - gobierno SaaS
6. `01. Requerimiento/Capa 3 Agentes de IA/Agentes de IA - Arquitectura y Operacion.md` - capa IA
7. `02. Inventario de modulos/Modelo de Datos - Entidades y Tablas.md` - modelo de datos
8. `04. Notas para desarrollador/Notas de desarrollo.md` - concurrencia, anti-overbooking, login
9. `05. Pruebas/Modelo de pruebas/CREDENCIALES - Usuarios y claves por perfil.md` - credenciales demo

Antes de implementar un modulo, leer su documento. No reinterpretar requerimientos de memoria.

---

## 3. Estructura del repositorio

```txt
CUBOT.nails/
+-- apps/
|   +-- web-prototype/                # prototipo React/TanStack (SOLO referencia visual, no es el producto)
|   +-- backend/
|       +-- CubotNails.sln
|       +-- src/
|       |   +-- CubotNails.Domain/            (entidades + enums)
|       |   +-- CubotNails.Application/        (servicios, DTOs, casos de uso)
|       |   +-- CubotNails.Infrastructure/     (EF Core, migraciones, integraciones)
|       |   +-- CubotNails.Shared/             (contratos compartidos)
|       |   +-- CubotNails.Api/                (Minimal API + JWT)
|       |   +-- CubotNails.SuperAdmin/         (CONSOLA UNIFICADA Blazor: super admin Y tenant)
|       |   +-- CubotNails.Web/                (Blazor Web App heredado + .Client WASM)
|       |   +-- CubotNails.Workers/            (BackgroundServices)
|       +-- tests/
|           +-- CubotNails.Domain.Tests/
|           +-- CubotNails.Application.Tests/
|           +-- CubotNails.Integration.Tests/  (Testcontainers - requiere Docker)
+-- deploy/docker/                    # docker-compose, .env.example, README
+-- docs/decisiones/                  # ADRs
+-- docs/arquitectura/
+-- CLAUDE.md                         # este archivo
```

**Decision clave (heredada de CUBOT.travels):** la consola es **UNA SOLA** app Blazor
(`CubotNails.SuperAdmin`) que sirve paginas de super admin Y de tenant, separadas por
**politicas** (`PlatformOperator` vs `TenantMember`), no por aplicaciones distintas.

`CubotNails.Web` viene heredado; su rol final aun se decide (ver pendientes). No borrarlo
sin un ADR.

---

## 4. Stack tecnico

- **.NET**: el codigo apunta hoy a **net9.0** (puente). Objetivo declarado .NET 10;
  migrar a `net10.0` es un cambio aparte que requiere su propio ADR y validacion.
  En esta maquina hay SDK 10.0.300 y 9.0.314.
- Blazor (Server interactivo) para consola y web del salon. SignalR para tiempo real.
- EF Core + PostgreSQL (snake_case, jsonb, query filters por tenant).
- Redis: cache de disponibilidad, **locks de reserva**, rate limiting.
- RabbitMQ + MassTransit para eventos/recordatorios (en MVP pueden ser in-process via MediatR).
- Serilog + OpenTelemetry. Docker para infraestructura local.
- Clean Architecture + monolito modular preparado para microservicios.

**Frontend del producto: 100% .NET / Blazor (regla firme).** Prohibido Node/npm/React/Vue/Vite
para construir o desplegar la UI del producto. E2E con Playwright para .NET. El `web-prototype`
React solo es guia visual; no se evoluciona como producto.

**Infraestructura local (puertos reasignados, ver docker-compose):**
Postgres 5434 (`cubot_nails_dev`), Redis 6381, RabbitMQ 5673 / UI 15673, pgAdmin 5051.

---

## 5. REGLA DE ORO DEL DOMINIO: nunca overbooking

El motor de agenda **jamas** puede permitir dos citas en el mismo cupo. Defensa de dos capas,
ambas obligatorias (ver `04. Notas para desarrollador/Notas de desarrollo.md`):

1. **Base de datos (fuente de verdad):** `UNIQUE(tenant_id, resource_id, appointment_date, start_time)`.
   Si dos transacciones chocan, Postgres deja pasar una y rechaza la otra; el servicio traduce
   la violacion a un mensaje amable ("ese horario acaba de ocuparse").
2. **Aplicacion (mejor experiencia):** lock corto en Redis (`SET NX EX ~10s`) antes de tocar la BD.

Reprogramar = liberar origen + ocupar destino en **una sola transaccion** (rollback si el destino
viola el unique; registrar `RescheduledFromId`). La IA usa la **misma** ruta de reserva: no tiene
atajos que salten estas defensas. El **test de concurrencia** que prueba la imposibilidad de doble
reserva es el hito mas critico del producto.

Zona horaria: la cita se guarda con fecha/hora **local del salon** (`DateOnly`+`TimeOnly`) + `Tenant.TimeZone`.
UTC solo para auditoria.

---

## 6. Multi-tenancy (regla bloqueante)

- Toda entidad operativa de tenant lleva `TenantId` obligatorio.
- Toda consulta tenant-scoped filtra por tenant (Query Filters de EF Core).
- Sin fuga de datos entre salones. Tests de aislamiento desde el primer modulo.
- Super Admin no se mezcla con admin de tenant: endpoints, politicas, UI y auditoria separados.
- Roles de salon: **Owner, Admin, Reception, Professional**. Un `Professional` se vincula a su
  `Resource` (`LinkedResourceId`) para ver solo su agenda.

---

## 7. Seguridad (regla no negociable)

- Secretos en `.env` / user-secrets / DataProtection cifrado en BD - **nunca** versionados ni en claro.
- No loggear: tokens Evolution API, llaves Wompi, llaves IA, credenciales, mensajes privados, id/refresh tokens.
- HTTPS obligatorio fuera de localhost. JWT propio de CUBOT.nails (Google es identidad, no permisos).
- Rate limiting en auth. Auditoria de acciones sensibles (Super Admin, estado de tenant, Evolution, Wompi).

---

## 8. IA (regla no negociable)

- Ningun agente se ejecuta sin tenant activo ni si el plan no lo permite.
- Toda ejecucion registra `AiUsageLog`: proveedor, modelo, tokens in/out, costo, agente, tenant, correlation id.
- La IA **no inventa** disponibilidad, precios, reservas ni condiciones. Solo ofrece cupos que el motor valido.
- La IA inicia en **modo sugerencia**; auto-confirmar solo con configuracion explicita del salon.

---

## 9. Orden de construccion (hoja de ruta nails, seccion 4.5)

Como el clon ya trae Super Admin + IA + Evolution + identidad + marca, el 80% del trabajo nuevo
esta en el **Nucleo Operativo (Capa 2)**:

```
YA VIENE (renombrar/sembrar/rebrandear):
  Plataforma multi-tenant, Super Admin, Onboarding, Usuarios/Roles,
  Lineas WhatsApp + Evolution + SignalR, AI Gateway + Agentes, Branding, Auditoria,
  Conversaciones, Plantillas, Automatizaciones.

CONSTRUIR NUEVO (en este orden):
  Menu del sistema (bloqueante de Fase 1)
   -> Servicios -> Recursos (Asesores de imagen) -> Turnos base
    -> Motor de disponibilidad -> Citas (ANTI-OVERBOOKING, hito critico)
     -> Excepciones (globales + por asesor) -> Vista Dia / Semana / Asignacion
      -> Cadena multi-estacion + Puntualidad 3 estados -> Reprogramaciones
       -> Recordatorios 24h/2h + No-Show worker -> Dashboard -> Capa IA de reservas

ELIMINAR del menu (son del CRM, no aplican a un salon):
  Pipeline.razor, Tableros.razor, TableroDetalle.razor y reportes de embudo.
```

**Primera tarea funcional (antes de cualquier modulo de negocio):** reorganizar el menu/`NavMenu.razor`
por secciones (Operacion diaria / Configuracion del salon / Comunicacion e IA / Gestion del negocio),
con stubs de paginas y politicas. Detalle en la Hoja de Ruta.

---

## 10. Checklist antes de cada commit

- [ ] `dotnet build` verde en `apps/backend/CubotNails.sln`.
- [ ] `dotnet test` verde en proyectos tocados (Integration.Tests requiere Docker).
- [ ] Sin secretos versionados; sin credenciales/tokens/mensajes privados en logs.
- [ ] Sin queries tenant-scoped sin filtro de tenant.
- [ ] Ninguna ruta de reserva puede producir overbooking (unique + lock presentes).
- [ ] Si toca Super Admin: auditoria. Si toca IA: medicion de tokens. Si toca Wompi/Evolution: webhooks idempotentes.
- [ ] Si cierra un modulo: actualizar `INVENTARIO GENERAL.md`. Si hay decision nueva: ADR en `docs/decisiones/`.
- [ ] Archivos nuevos en ASCII.

---

## 11. Comandos clave del entorno local

```powershell
# Build / test
cd C:\DesarrolloIA\CUBOT.nails\apps\backend
dotnet build CubotNails.sln
dotnet test  CubotNails.sln

# Infraestructura local
cd C:\DesarrolloIA\CUBOT.nails\deploy\docker
docker compose up -d ; docker compose ps

# Migraciones EF (Infrastructure = proyecto, SuperAdmin = startup)
dotnet ef database update `
  --project   apps/backend/src/CubotNails.Infrastructure `
  --startup-project apps/backend/src/CubotNails.SuperAdmin

# Levantar la consola unificada
dotnet run --project apps/backend/src/CubotNails.SuperAdmin --launch-profile https
```

---

## 12. Riesgos a no romper

1. **Overbooking** (la regla de oro): unique + lock Redis + test de concurrencia.
2. Fuga de datos entre salones: tests de aislamiento desde el primer modulo.
3. IA inventando horarios: solo ofrece lo que el motor de disponibilidad valida.
4. Costos IA sin control: validar plan, registrar `AiUsageLog`, modo sugerencia.
5. Citas huerfanas al recortar turnos: validacion + confirmacion explicita.
6. No-show alto: recordatorios + confirmacion + sena opcional.
7. Zona horaria: fecha/hora local del salon + `Tenant.TimeZone`.
8. Super Admin mezclado con tenant: roles/politicas/UI/auditoria separados.
9. Wompi duplicando pagos: idempotencia por `provider_event_id`.
