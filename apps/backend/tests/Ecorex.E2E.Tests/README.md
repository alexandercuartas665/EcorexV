# Ecorex.E2E.Tests

Suite E2E de la consola Blazor (Ecorex.SuperAdmin) con **Playwright para .NET + xunit**,
headless Chromium. Ver `docs/decisiones/0019-e2e-playwright.md` para alcance y decisiones.

## Requisitos

1. Postgres dev arriba (contenedor `ecorex-tareas-postgres`, puerto 5442 con la BD
   `ecorex_dev` sembrada): `deploy/docker/preflight.ps1` + `docker compose up -d`.
2. Solucion compilada (el arranque automatico usa `dotnet run --no-build`):

   ```powershell
   cd apps\backend
   dotnet build Ecorex.sln
   ```

3. Binarios de Chromium de Playwright (UNA sola vez por maquina; el paquete NuGet no
   los trae):

   ```powershell
   cd apps\backend\tests\Ecorex.E2E.Tests
   pwsh bin/Debug/net10.0/playwright.ps1 install chromium
   ```

## Como corre

```powershell
cd apps\backend
dotnet test tests\Ecorex.E2E.Tests
```

Modos de resolucion de la app bajo prueba (fixture `E2eAppFixture`):

- **ECOREX_E2E_BASEURL definida** (ej. `http://localhost:5250`): usa esa app ya corriendo.
  Es el modo pensado para CI o para depurar contra una consola arrancada a mano. Si la URL
  no responde `/login` con 200, la suite completa se SALTA con ese motivo (no falla).
- **Sin la variable** (conveniencia local): el fixture arranca la consola solo
  (`dotnet run --project src/Ecorex.SuperAdmin --no-build` en el primer puerto libre
  5250-5299, `ASPNETCORE_ENVIRONMENT=Development`, `ECOREX_DB_CONNECTION` al Postgres
  5442), espera `/login` 200 hasta 120 s (el arranque aplica migraciones + seed demo) y
  al terminar mata el proceso. Si no puede (sin build, sin Postgres, sin Chromium), los
  tests se saltan con el motivo exacto.

Variables:

| Variable              | Default                                                                  |
|-----------------------|--------------------------------------------------------------------------|
| `ECOREX_E2E_BASEURL`  | (vacia: arranque automatico)                                             |
| `ECOREX_DB_CONNECTION`| `Host=localhost;Port=5442;Database=ecorex_dev;Username=ecorex;Password=EcorexDev2026pg` |

## Notas de diseno

- Todas las clases comparten la coleccion xunit `e2e`: corren en SECUENCIA sobre una
  misma app y browser; cada test usa un contexto de navegador nuevo (cookies aisladas)
  y datos con sufijo unico por corrida (idempotencia contra la BD dev persistente).
- Selectores por rol/texto accesible o clases CSS estables del prototipo. El producto no
  tiene `data-testid` y esta suite NO puede agregarlos (regla de la tarea).
- Mover tarjeta: se usa el dropdown de estado del DETALLE (misma TaskItemStateMachine),
  no drag and drop nativo (fragil de automatizar sobre Blazor Server; ver ADR-0019).
- `E2eDbBackdoor`: completa el paso "Requerimiento" del flujo demo via WorkflowEngine
  porque ese paso no tiene UI todavia (bandeja de pasos = deuda ADR-0014). Solo arregla
  y consulta estado del motor; los asserts de UI siguen siendo por navegador.
