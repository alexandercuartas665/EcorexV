# ADR-0002: Estructura monorepo con apps/ explicitas

**Fecha:** 2026-05-19
**Estado:** Aceptado
**Contexto del proyecto:** ECOREX.tareas - Fase de scaffolding inicial

## Contexto

El repositorio oficial [cubotcrm](https://github.com/alexandercuartas665/cubotcrm.git) clonado en `C:\DesarrolloIA\ECOREX.tareas` es un **prototipo frontend** (TanStack Start + React 19 + Vite + Tailwind + shadcn/Radix UI, desplegable en Cloudflare via wrangler, generado con Lovable.dev). Ocupa la carpeta `src/` en la raiz del repo.

La hoja de ruta del vault Obsidian (`03. Hoja de Ruta desarrollo/HOJA DE RUTA DESARROLLO.md`, seccion 4) instruye crear una solucion .NET 10 con 8 proyectos en `src/`:

- `src/Ecorex.Domain`, `.Application`, `.Infrastructure`, `.Shared`
- `src/Ecorex.Api`, `.Web` (Blazor), `.SuperAdmin` (Blazor), `.Workers`

Mismo path, dos significados incompatibles. Habia tres opciones:

- A. `backend/` (.NET en `backend/`, frontend intacto)
- B. `prototype/` (mover frontend a `prototype/`, .NET en `src/`)
- C. **Monorepo con `apps/` explicitas** (frontend en `apps/web-prototype/`, .NET en `apps/backend/`)

## Decision

Adoptar **opcion C**. Estructura final del repo:

```txt
ECOREX.tareas/
├── apps/
│   ├── web-prototype/    # frontend TanStack Start (cubotcrm original)
│   │   ├── src/, public/, package.json, vite.config.ts, wrangler.jsonc, ...
│   └── backend/          # solucion .NET (scaffold pendiente, hoja de ruta seccion 4)
│       ├── src/Ecorex.*
│       └── tests/
├── deploy/docker/        # infraestructura local (Postgres, Redis, RabbitMQ, pgAdmin)
├── docs/decisiones/      # ADRs
└── docs/arquitectura/
```

El frontend se movio con `git mv` para preservar historial. El `apps/backend/` queda vacio con un placeholder hasta ejecutar el scaffold .NET de la seccion 4 de la hoja de ruta.

## Consecuencias

- La hoja de ruta seccion 4 debe ejecutarse desde `apps/backend/` (no desde `src/`). Los `dotnet new` que dicen `cd src` ahora son `cd apps/backend/src`.
- El frontend se ejecuta desde `apps/web-prototype/` (`bun install && bun run dev` en esa carpeta).
- Si en el futuro hay despliegues Cloudflare apuntando al repo, hay que actualizar "carpeta raiz" del proyecto a `apps/web-prototype/` en Cloudflare Pages / Lovable / wrangler dashboard.
- Permite agregar mas apps despues sin reestructurar otra vez (mobile, landing, etc.).
- El `.gitignore` raiz se mantiene sin cambios; las reglas `node_modules`, `dist`, `.wrangler/`, etc. siguen aplicando recursivamente y atrapan los artefactos del frontend dentro de `apps/web-prototype/`.

## Validacion pendiente

Antes de cerrar esta decision:

1. Ejecutar `bun install && bun run build` dentro de `apps/web-prototype/` para confirmar que ningun path absoluto se rompio.
2. Cuando se aborde la seccion 4 de la hoja de ruta, scaffoldear .NET en `apps/backend/`.
