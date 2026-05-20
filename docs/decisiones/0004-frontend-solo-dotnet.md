# ADR-0004: Frontend exclusivamente .NET Core (Blazor), sin Node.js

**Fecha:** 2026-05-19
**Estado:** Aceptado
**Contexto del proyecto:** CUBOT.travels - Definicion de stack frontend

## Contexto

La hoja de ruta listaba Node.js 22 como prerequisito ("herramientas frontend, Playwright, linters") y la vision general dejaba abierta la opcion de usar React o Vue ("Aunque React o Vue podrian utilizarse..."). Ademas, el repositorio trae un prototipo frontend real en React/TanStack (`apps/web-prototype`) que si depende de Node.

El dueno del proyecto detecto la anomalia y decidio cerrar la ambiguedad: el frontend del producto debe ser 100% .NET Core (Blazor), sin Node.js.

## Decision

El frontend del producto CUBOT.travels se construye **exclusivamente** con .NET Core: Blazor (Web App / WebAssembly / Server segun el caso) y componentes Razor, sobre la misma solucion .NET del backend.

**Queda prohibido en el producto:** Node.js, npm/pnpm/bun, React, Vue, Vite, Webpack o cualquier toolchain JavaScript para desarrollar, compilar o desplegar la interfaz.

- DTOs, contratos y validaciones se comparten via `CubotTravels.Shared` entre `CubotTravels.Web` y `CubotTravels.Api`.
- Tiempo real con SignalR (cliente .NET), no librerias JS.
- Pruebas E2E con **Playwright para .NET** (`Microsoft.Playwright`); los navegadores se instalan con el script `playwright.ps1` generado, sin Node.

El prototipo `apps/web-prototype` (React/TanStack) se conserva **solo como referencia visual**. No es codigo de produccion, no se evoluciona como producto y no condiciona el stack. Node.js solo es necesario si alguien quiere ejecutarlo localmente para inspeccionar la experiencia.

## Consecuencias

- Node.js sale de la lista de prerequisitos del entorno de desarrollo del producto.
- La estructura de carpetas frontend de la vision general se reescribe en terminos de proyectos Blazor (Components/Pages/Layout/Services/State/Hubs) en vez de la estructura estilo React (hooks/stores).
- CI/CD no necesita pasos de `npm install`/`npm build` para el producto; solo `dotnet`.
- La validacion visual y E2E usa Playwright .NET, integrado al pipeline `dotnet test`.
- Si en el futuro se quisiera una SPA JS, requeriria un nuevo ADR que revierta esta decision.

## Documentos actualizados

- `03. Hoja de Ruta desarrollo/HOJA DE RUTA DESARROLLO.md` (stack + prerequisitos + nota "Frontend sin Node.js").
- `01. Requerimiento/Capa 0 Vision General/CUBOT.travels.md` (decision definitiva + estructura de carpetas Blazor).
- `CLAUDE.md` (seccion 4: frontend del producto vs prototipo de referencia).
