# Ola 2 - Embed del editor/visor Bold Reports (runbook turnkey)

> Estado: LISTO PARA EJECUTAR cuando el usuario provea la clave de licencia Community de Bold.
> El resto de la Ola 2 (convertidor `ReportSpec -> RDL` + persistencia del imprimible) ya esta hecho
> y probado (commit 71c6624, `ReportSpecToRdl` + `SavePrintableAsync` + `ReportRdlTests`).
> Contexto: ADR-0051. Regla inviolable: la clave NUNCA se versiona (repo publico).

## Gates de la Ola 2 (estado)

- Gate #1 (Community License): SI, confirmado por el usuario (Bitcode califica). Para el Report
  Designer hay que registrar la Community **de Bold Reports** (no la de Syncfusion).
- Gate #2 (paquetes net10): **SI**. `BoldReports.Net.Core` 14.1.14 publica build **net10.0**
  explicitamente (verificado en el .nuspec). Dependencias: Bold.Licensing 14.1.14, Syncfusion.Pdf/
  XlsIO/DocIO/Compression.Net.Core 33.1.44, SkiaSharp 3.119.1, Microsoft.Data.SqlClient 6.0.1,
  System.Drawing.Common 10.0.1, Microsoft.Windows.Compatibility 10.0.1, Newtonsoft.Json 13.0.3.
- Gate #3 (datasource tenant-safe): SI, ya existe `/api/reporting/query` (Ola 1).
- Gate #4 (redistribucion SaaS): SI (community endosa SaaS multi-tenant).
- RIESGO ABIERTO (Docker prod): `System.Drawing.Common` + `Microsoft.Windows.Compatibility` pueden
  exigir libs nativas en Linux/Docker (libgdiplus/fuentes). Ademas Docker no esta claramente concedido
  por la Community (ver ADR-0051). CONFIRMAR con Bold antes del deploy a prod; en dev Windows no aplica.

## Pre-requisito del usuario (una sola vez)

1. Registrar la **Bold Reports Community License** a nombre de Bitcode en https://www.boldreports.com
   y obtener la **clave de licencia**.
2. Colocarla FUERA del repo (gitignored). Opcion recomendada en dev: user-secrets del proyecto
   `Ecorex.SuperAdmin`:
   ```
   cd apps/backend/src/Ecorex.SuperAdmin
   dotnet user-secrets init
   dotnet user-secrets set "Bold:LicenseKey" "<CLAVE>"
   ```
   En prod: variable de entorno `Bold__LicenseKey` (nunca en appsettings versionado).

## Pasos que ejecuta el agente cuando la clave este puesta

### 1. Paquetes (a `Ecorex.SuperAdmin.csproj`)
```xml
<PackageReference Include="BoldReports.Net.Core" Version="14.1.14" />
```
(El visor/diseniador Blazor usan los componentes JS de Bold servidos como static assets + el
controller Web API de `BoldReports.Net.Core`. Confirmar en el restore si hace falta ademas un paquete
Blazor especifico de la version 14.x; si existe `BoldReports.Blazor` net10, agregarlo.)
Restaurar y compilar para confirmar que el grafo resuelve en la solucion (no solo en el .nuspec).

### 2. Registro de licencia (Program.cs, antes de build)
```csharp
var boldKey = builder.Configuration["Bold:LicenseKey"];
if (!string.IsNullOrWhiteSpace(boldKey))
{
    BoldReports.ReportViewerControl.BoldLicenseProvider.RegisterLicense(boldKey);
}
```
(Nombre exacto del API de registro segun la version; validar en el restore.)

### 3. Web Reporting API (controller alojado en SuperAdmin, tenant-safe)
- Viewer: implementar `IReportController` (namespace `BoldReports.Web`) -> endpoint p.ej.
  `/api/bold-reports/viewer`. `[Authorize(Policy="TenantMember")]`.
- Designer: implementar `IReportDesignerController` -> `/api/bold-reports/designer`.
- Los datos NO salen de una connection string: el RDL usa el data source JSON logico
  "EcorexTenantSafe" (ver `ReportSpecToRdl`), que se resuelve contra `/api/reporting/query`
  (IReportDataSource, filtro global de tenant). Cablear el `OnInitReportOptions`/data provider del
  controller para inyectar el `ReportDataSet` del `IReportDataSource` como dataset del RDL.

### 4. Paginas Blazor (tras policy)
- `/reportes/imprimibles/editor` -> componente Report Designer de Bold (crear/editar RDL). Al guardar,
  persistir con `IReportDefinitionService` (Kind=Printable, Rdl). El RDL inicial puede venir de
  `ReportSpecToRdl.ToRdl(spec, ds)` (autoria por IA -> imprimible).
- `/reportes/imprimibles/{id}` -> componente Report Viewer de Bold que carga el RDL de la
  `ReportDefinition` y exporta PDF/Excel (export nativo del visor).
- Cargar los static assets JS/CSS de Bold desde el paquete (sin CDN externo, coherente con "UI Blazor
  sin Node/npm build"); vendorizar si el paquete referencia un CDN.

### 5. Verificacion (con la clave puesta)
- Un usuario crea/edita un RDL en el editor, lo guarda (Kind=Printable), lo abre en el visor con datos
  reales de SU tenant y exporta a PDF. Otro tenant no ve ese reporte ni esos datos (aislamiento por el
  filtro global + `IReportDataSource`).
- Confirmar que NO aparece marca de agua (prueba de que la licencia quedo registrada).

## Por que no se pre-agregaron los paquetes

El embed solo se puede VERIFICAR (render sin marca de agua, export PDF) con la clave. Agregar el grafo
pesado (Syncfusion + SkiaSharp + Windows.Compatibility) antes de poder verificarlo solo ensuciaria la
rama y ralentizaria todos los builds sin payoff comprobable. Gate #2 ya quedo confirmado verde desde el
.nuspec autoritativo; el resto se ejecuta y verifica en una sola pasada cuando la clave este disponible.
