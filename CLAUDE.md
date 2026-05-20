# CLAUDE.md - Memoria del agente de desarrollo para CUBOT.travels

> Este archivo es la primera lectura obligatoria para cualquier agente de desarrollo (Claude Code u otro) antes de modificar codigo en este repositorio. Reglas pequenas, concretas y verificables.

---

## 1. Contexto del proyecto

CUBOT.travels es un **SaaS multi-tenant para agencias turisticas** orientado a:

- Ventas por WhatsApp via Evolution API
- Embudos comerciales tipo Kanban
- Automatizacion de seguimientos
- Agentes de IA (copiloto comercial, clasificador, resumen, seguimiento)
- Facturacion SaaS con Wompi maestro
- Super Admin de plataforma separado del admin de tenant

Repo oficial: `https://github.com/alexandercuartas665/cubotcrm.git`. El contenido actual del repo es el **prototipo visual frontend** (TanStack Start + React 19), no la base backend. El backend .NET esta pendiente de scaffold en `apps/backend/`.

---

## 2. Fuente de verdad

Las especificaciones funcionales viven en el **vault Obsidian**:

```
C:\Users\acuartas\Documents\Personal\OneDrive\Proyectos\07. Agencias de Viajes\CUBOT.travels\CUBOT.travels
```

Documentos maestros (leer en este orden):

1. `02. Inventario de modulos/INVENTARIO GENERAL.md` - mapa de modulos, capas, dependencias, tracker
2. `03. Hoja de Ruta desarrollo/HOJA DE RUTA DESARROLLO.md` - plan paso a paso (este documento es el contrato de trabajo)
3. `Capa 0 Vision General/CUBOT.travels.md` - arquitectura general
4. `Capa 1 Gestion de tenant/Gestion de Tenant - Super Admin SaaS.md` - gobierno SaaS
5. `Capa 3 Agentes de IA/Agentes de IA - Arquitectura y Operacion.md` - capa IA
6. `04. Notas para desarrollador/Notas de desarrollo.md` - login Google y otros detalles

Antes de implementar un modulo, leer su documento correspondiente. No reinterpretar requerimientos a memoria.

---

## 3. Estructura del repositorio

```txt
CUBOT.travels/
├── apps/
│   ├── web-prototype/        # frontend TanStack Start (prototipo visual)
│   │   ├── src/              # codigo React/TS del prototipo
│   │   ├── public/
│   │   ├── package.json, vite.config.ts, wrangler.jsonc, tsconfig.json
│   │   └── components.json   # shadcn config
│   └── backend/              # solucion .NET (scaffold pendiente)
│       └── (placeholder)
├── deploy/
│   └── docker/               # docker-compose, .env.example, README operativo
├── docs/
│   ├── decisiones/           # ADRs (Architecture Decision Records)
│   └── arquitectura/
├── CLAUDE.md                 # este archivo
└── .gitignore
```

**Por que monorepo:** el repo originalmente trae el prototipo en `src/` y la hoja de ruta pide colocar .NET tambien en `src/`. Ver `docs/decisiones/0002-monorepo-apps.md`.

---

## 4. Stack tecnico

**Backend (pendiente de scaffold):**

- .NET 10 + ASP.NET Core 10 (objetivo). En esta maquina hay **.NET 9.0.314 instalado**, se usa como puente temporal hasta migrar - ver `docs/decisiones/` (ADR de excepcion .NET por crear).
- Blazor (Server/WASM/Hybrid) para frontend empresarial y Super Admin
- EF Core 10 con PostgreSQL
- Redis para cache, sesiones, locks
- RabbitMQ + MassTransit para event bus y workers
- SignalR para tiempo real (chat, dashboards)
- Serilog + OpenTelemetry para logs/trazas
- Clean Architecture + monolito modular preparado para microservicios

**Frontend del producto: 100% .NET Core / Blazor (regla firme).**

- El frontend se construye exclusivamente con Blazor + componentes Razor sobre el stack Microsoft.
- **Prohibido en el producto:** Node.js, npm, React, Vue, Vite o cualquier toolchain JavaScript para construir/compilar/desplegar la UI. Ver `docs/decisiones/0004-frontend-solo-dotnet.md`.
- DTOs, validaciones y contratos se comparten via `CubotTravels.Shared` entre Web y Api.
- Pruebas E2E con Playwright para .NET (`Microsoft.Playwright`), sin Node.

**Prototipo de referencia (NO es el producto):**

- `apps/web-prototype` es TanStack Start + React 19 + Vite + Tailwind + shadcn (generado con Lovable.dev, deploy Cloudflare).
- Sirve solo como guia visual de la experiencia. No se evoluciona como producto ni define el stack. Node.js solo se necesita si alguien quiere correrlo localmente.

**Infraestructura local:**

- Docker Compose con Postgres 16, Redis 7, RabbitMQ 3.13, pgAdmin 4
- Puertos host reasignados: 5434 (postgres), 6381 (redis), 5673/15673 (rabbit), 5051 (pgadmin) - ver `docs/decisiones/0001-puertos-docker-locales.md`

---

## 5. Multi-tenancy (regla bloqueante)

- Toda entidad operativa de tenant lleva `TenantId` obligatorio.
- Toda consulta tenant-scoped debe filtrar por tenant (Query Filters de EF Core).
- No permitir fuga de datos entre agencias. Tests de aislamiento son obligatorios desde el primer modulo.
- El rol Super Admin no se mezcla con el rol admin de tenant. Endpoints, politicas, UI y auditoria separadas.

---

## 6. Seguridad (regla no negociable)

- Secretos en `.env`, user-secrets, o secret store - **nunca** versionados.
- No loggear: tokens Evolution API, llaves Wompi, llaves IA, credenciales, mensajes privados completos, id tokens, refresh tokens, authorization codes.
- HTTPS obligatorio fuera de localhost.
- JWT propio de CUBOT.travels - Google es proveedor de identidad, no de permisos.
- Rate limiting en endpoints de auth.
- Auditoria de acciones sensibles (Super Admin, cambios de estado de tenant, configuracion Evolution, Wompi).

---

## 7. IA (regla no negociable)

- Ningun agente se ejecuta sin tenant activo.
- Ningun agente consume tokens si el plan del tenant no lo permite.
- Toda ejecucion de IA registra: proveedor, modelo, tokens entrada/salida, costo, agente, lead, tenant, correlation id.
- La IA inicia en **modo sugerencia**, no respuesta automatica.
- La IA no inventa precios, disponibilidad, reservas ni condiciones finales.
- Guardrails antes de enviar respuestas IA al cliente final.

---

## 8. Reglas de implementacion

- Cambios pequenos, commits frecuentes, tests por modulo.
- No construir modulos fuera del orden de `INVENTARIO GENERAL.md` sin un ADR.
- No duplicar reglas: si una regla vive en `Application`, no replicarla en `Api` ni en `Web`.
- Nombres de clases en ingles; mensajes de usuario final en espanol.
- Si una decision arquitectonica cambia algo del cerebro digital, registrar en `docs/decisiones/` y reflejar en Obsidian.

---

## 9. Orden inicial de modulos (hoja de ruta seccion 8)

```
0.1 Super Admin Console
  -> 0.2 Planes, Limites y Suscripciones
    -> 1.1 Onboarding y Activacion de Agencia
      -> 1.2 Usuarios, Roles y Permisos
        -> 1.3 Configuracion Evolution API
          -> 1.4 Gestion de Lineas WhatsApp
            -> 2.1 Embudo Comercial y Pipeline
              -> 2.2 Leads y Ficha Comercial
                -> 2.3 Chat Omnicanal WhatsApp
                  -> 2.5 Seguimientos y Automatizacion Basica
                    -> 2.6 Dashboard Comercial
                      -> 3.1 AI Provider Gateway
                        -> 3.2 Agent Orchestrator
                          -> 3.3 Copiloto Comercial
```

Romper este orden tiene costo: construir agentes IA antes de chat/leads/limites produce demo bonita pero ingobernable.

---

## 10. Checklist antes de cada commit

- [ ] `dotnet build` verde (cuando exista solucion .NET).
- [ ] `dotnet test` verde en proyectos tocados.
- [ ] Sin secretos versionados.
- [ ] Sin queries tenant-scoped sin filtro.
- [ ] Sin credenciales/tokens/mensajes privados en logs.
- [ ] Si toca Super Admin: auditoria presente.
- [ ] Si toca IA: medicion de tokens/costo.
- [ ] Si toca Wompi/Evolution: idempotencia de webhooks.
- [ ] Si cierra un modulo: actualizar `INVENTARIO GENERAL.md`.
- [ ] Si decision arquitectonica nueva: registrar en `docs/decisiones/`.

---

## 11. Comandos clave del entorno local

```powershell
# Levantar infraestructura
cd C:\DesarrolloIA\CUBOT.travels\deploy\docker
docker compose up -d
docker compose ps

# Bajar infraestructura (mantiene datos)
docker compose down

# Cadenas de conexion locales (puertos reasignados)
# Postgres : Host=localhost;Port=5434;Database=cubot_travels_dev;Username=cubot;Password=...
# Redis    : localhost:6381 (con password)
# RabbitMQ : amqp://cubot:...@localhost:5673  + UI http://localhost:15673
# pgAdmin  : http://localhost:5051

# Frontend prototipo
cd C:\DesarrolloIA\CUBOT.travels\apps\web-prototype
bun install
bun run dev
```

---

## 12. Riesgos a no romper

1. Fuga de datos entre tenants (tests de aislamiento desde el primer modulo).
2. Chat acoplado a Evolution API (usar conector + colas + persistencia).
3. Costos IA sin control (validar plan, registrar costo, modo sugerencia).
4. Super Admin mezclado con tenant admin (separar roles/politicas/UI).
5. Wompi duplicando pagos (idempotencia por `provider_event_id`).
6. Pipeline rigido (etapas configurables por tenant).
7. Spam de seguimientos (frecuencia, horarios, opt-out, detener al recibir respuesta).
8. Observabilidad tardia (Serilog + correlation id + health checks desde fase 1).
