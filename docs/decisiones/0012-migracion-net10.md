# ADR-0012: Migracion de la solucion a .NET 10 y EF Core 10

**Fecha:** 2026-07-03
**Estado:** Aceptado
**Reemplaza a:** ADR-0003 (.NET 9 como puente temporal)
**Contexto del proyecto:** ECOREX.tareas - migracion de TFM prevista desde el scaffold

## Contexto

ADR-0003 dejo la solucion en `net9.0` como puente temporal por falta de SDK 10 en la
maquina de desarrollo, con tarea explicita de migrar a .NET 10 antes del primer piloto.
Hoy se cumplen las condiciones que ese ADR exigia:

- SDK **10.0.301** instalado (junto a 9.0.315).
- Todo el stack principal tiene release **estable** para net10: EF Core 10.0.x,
  ASP.NET Core 10.0.x, `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.2 y
  `EFCore.NamingConventions` 10.0.1 (verificado en nuget.org; su ausencia habria sido
  bloqueante para subir EF, por la regla de no mezclar majors en el stack EF).
- PR/cambio aislado, sin mezclar con trabajo funcional.

## Decision

1. **TFM final: `net10.0`** en los 13 `.csproj` de la solucion (10 en `src/`, 3 en `tests/`).
2. **Stack EF Core y ASP.NET Core completo en 10.x** (misma major en todo el stack EF,
   sin mezclas 9/10):

| Paquete | Antes | Despues |
|---|---|---|
| Microsoft.EntityFrameworkCore (+ Relational, Design, SqlServer) | 9.0.4 | 10.0.9 |
| Npgsql.EntityFrameworkCore.PostgreSQL | 9.0.4 | 10.0.2 |
| EFCore.NamingConventions | 9.0.0 | 10.0.1 |
| Microsoft.AspNetCore.DataProtection (+ EntityFrameworkCore, Extensions) | 9.0.4 | 10.0.9 |
| Microsoft.AspNetCore.Authentication.JwtBearer | 9.0.4 | 10.0.9 |
| Microsoft.AspNetCore.OpenApi | 9.0.16 | 10.0.9 |
| Microsoft.AspNetCore.SignalR.Client | 9.0.0 | 10.0.9 |
| Microsoft.AspNetCore.Mvc.Testing | 9.0.4 | 10.0.9 |
| Microsoft.AspNetCore.Components.WebAssembly (+ .Server) | 9.0.16 | 10.0.9 |
| Microsoft.Extensions.Hosting | 9.0.16 | 10.0.9 |
| Microsoft.Extensions.DependencyInjection.Abstractions / Configuration.Abstractions / Http / Options | 9.0.4 | 10.0.9 |
| dotnet-ef (tool local, .config/dotnet-tools.json) | 9.0.4 | 10.0.9 |

3. **Sin cambios** (el build no lo exigio): Testcontainers 4.12.0, xunit 2.9.2,
   xunit.runner.visualstudio 2.8.2, coverlet.collector 6.0.2, Microsoft.NET.Test.Sdk 17.12.0,
   QuestPDF 2026.5.0, PuppeteerSharp 20.0.5, System.IdentityModel.Tokens.Jwt 8.3.1.

## Cambios de codigo requeridos

- `src/Ecorex.SuperAdmin/Components/Pages/Plantillas.razor`: variable local `field`
  renombrada a `fieldDef` dentro de un accessor de propiedad. En C# 14 (net10) `field`
  es palabra clave dentro de accessors (error CS9273 / warning CS9258).
- Ningun otro cambio de codigo fue necesario.

## Migraciones EF

- `dotnet ef migrations has-pending-model-changes` reporta **"No changes"** en ambos
  contextos (EcorexDbContext / Postgres y SqlServerEcorexDbContext / SQL Server) bajo
  EF Core 10: las convenciones EF10 no alteran el modelo. **No** se genero migracion
  `Ef10ModelSync`, no se regeneraron snapshots ni se tocaron migraciones historicas.
- Nota operativa: para el contexto SQL Server el comando debe correr con
  `--startup-project src/Ecorex.Infrastructure.SqlServer` (el design-time factory vive
  ahi y las EF tools solo buscan factories en el startup assembly); con
  `--startup-project src/Ecorex.Infrastructure` no se resuelve el factory.

## Consecuencias

- Toda la solucion compila y corre sobre .NET 10 (SDK 10.0.301); el SDK 9 deja de ser
  necesario para este repo.
- Railway/Docker: `Dockerfile.superadmin` y `Dockerfile.workers` deben usar imagenes
  base 10.0 cuando se despliegue (verificar tags antes del proximo deploy).
- Validacion completa en verde (2026-07-03): build 0 errores; Domain.Tests 1/1 y
  Application.Tests 1/1; Integration TenantIsolation 6/6 en matriz dual con
  Testcontainers (postgres:16-alpine + mssql 2022); SuperAdmin /login 200 contra
  Postgres real (5442) y contra SQL Server real (1443, ECOREX_DB_PROVIDER=SqlServer).
- ADR-0003 queda **Reemplazado** por este ADR.

## Pendiente

- Warning NU1903: `Microsoft.OpenApi` 2.0.0 (transitiva de Microsoft.AspNetCore.OpenApi)
  tiene vulnerabilidad conocida de gravedad alta (GHSA-v5pm-xwqc-g5wc); evaluar pin
  directo a una version parcheada.
- Warning ASPDEPR005 en `Ecorex.SuperAdmin/Program.cs`: `ForwardedHeadersOptions.KnownNetworks`
  esta obsoleto en ASP.NET Core 10; migrar a `KnownIPNetworks`.
- Actualizar imagenes base de los Dockerfiles a 10.0 en el proximo trabajo de deploy.
- Reflejar el cambio en el vault (INVENTARIO GENERAL) segun el flujo de registro.
