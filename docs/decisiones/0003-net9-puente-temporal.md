# ADR-0003: .NET 9 como puente temporal hasta migrar a .NET 10

**Fecha:** 2026-05-19
**Estado:** Reemplazado por ADR-0012 (migracion a .NET 10, 2026-07-03)
**Contexto del proyecto:** ECOREX.tareas - Scaffold inicial (hoja de ruta seccion 4)

## Contexto

La hoja de ruta recomienda **.NET 10** como stack base (`dotnet new ... -f net10.0`). La maquina actual de desarrollo tiene instalado **.NET SDK 9.0.314**. La propia hoja de ruta seccion 1 contempla:

> Si el repositorio se crea en un entorno donde .NET 10 no esta disponible, debe documentarse la excepcion y usar .NET 9 solo como puente temporal, dejando una tarea explicita de migracion antes del primer piloto.

## Decision

Scaffoldear toda la solucion `apps/backend/` con TFM **`net9.0`** y dejar una tarea pendiente explicita de migracion a `net10.0` antes del primer piloto en produccion.

Esto aplica a los 8 proyectos `src/` y a los 3 proyectos `tests/` indicados en la hoja de ruta seccion 4.

## Consecuencias

- Los `dotnet new` se ejecutan con `-f net9.0`.
- Paquetes NuGet (EF Core, Serilog, MassTransit, OpenTelemetry, etc.) deben elegirse en versiones compatibles con net9. Las que tengan version preview-net10 se posponen.
- Plantilla Blazor `--interactivity Auto` y `Server` esta disponible en .NET 9 y se usa segun la hoja de ruta.
- La migracion a net10 se hace en un PR aislado cuando .NET 10 GA este instalado y todos los paquetes principales tengan release estable para net10. Implica:
  - Actualizar `<TargetFramework>` en cada `.csproj` (11 archivos).
  - Revisar breaking changes de EF Core 10, ASP.NET Core 10 y SignalR.
  - Re-correr todos los tests de aislamiento y autenticacion.
  - Actualizar este ADR a estado **Reemplazado por** un ADR de migracion.

## Tareas derivadas

- [ ] **Migrar a .NET 10** antes del primer piloto productivo. Requisitos:
  - .NET SDK 10 instalado en CI y maquinas de dev.
  - Paquetes principales con release estable para net10.
  - PR aislado, sin mezclar con cambios funcionales.
- [ ] Documentar en `INVENTARIO GENERAL.md` la version actual del backend (net9) y la migracion pendiente, para que sea visible a otros agentes/desarrolladores.

## Validacion

```powershell
dotnet --version          # debe mostrar 9.x.x
dotnet build apps\backend\Ecorex.sln
```

Cuando se ejecute la migracion futura, este ADR queda como historico y se crea un ADR-00XX nuevo con estado "Aceptado" que reemplace este.
