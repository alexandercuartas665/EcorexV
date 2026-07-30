# Ola 2 - Embed del editor/visor Bold Reports (runbook)

> Estado: **VISOR EMBEBIDO Y VERIFICADO EN VIVO (modo evaluacion, sin clave)**. Un imprimible RDL se
> renderiza en el visor Bold con datos reales del tenant (tenant-safe, inyectados en memoria) y la
> barra de export (PDF/Excel). Falta solo: la CLAVE de licencia (quita la marca de agua, la pones tu),
> y el DISENIADOR drag-drop (pendiente). Contexto: ADR-0051. La clave NUNCA se versiona (repo publico).

## APRENDIZAJE CLAVE (resuelto en vivo): datos in-memory tenant-safe

Bold usa los datos inyectados por el controller SOLO en **ProcessingMode.Local** (en remote/RDL intenta
conectar a la BD del RDL e ignora la inyeccion). La receta que FUNCIONA:
- Cliente: `boldReportViewer({ processingMode: "local", ... })`.
- Controller `OnInitReportOptions`: `reportOption.ReportModel.ProcessingMode = ProcessingMode.Local;`
  + `ReportModel.Stream = <RDL>` + `ReportModel.DataSources.Add(new BoldReports.Web.ReportDataSource
  { Name = "<nombre>", Value = <DataTable tenant-safe> })`.
- El `ReportDataSource.Name` debe COINCIDIR con el `<DataSource Name>` del RDL Y con el `<DataSet Name>`
  y el `<Query><DataSourceName>` (todos alineados a un mismo valor, ej. "EcorexTenantSafe"). Si difieren,
  Bold reporta "data input collection null or empty for the data set".
- El `<DataSource>` del RDL usa `<ConnectionProperties>` embebido (provider "System.Data.DataSet",
  connectstring throwaway; se IGNORA en Local). NO usar `<DataSourceReference>` (Bold busca un data
  source compartido en disco -> error).
Esto ya esta implementado: `ReportSpecToRdl` (nombres alineados) + `BoldReportsApiController`
(ProcessingMode.Local + inyeccion) + `boldreports-interop.js` (processingMode local).

## Lo que YA esta hecho y verificado

- Paquete `BoldReports.Net.Core` 14.1.14 + `Microsoft.AspNetCore.Mvc.NewtonsoftJson` (restaura en net10).
- Assets Bold self-hosted en `wwwroot/lib/boldreports` + `wwwroot/lib/jquery` (carga on-demand por
  `boldreports-interop.js`, sin CDN en runtime).
- `BoldReportsApiController` (IReportController, policy TenantMember): carga el RDL de la
  `ReportDefinition` e inyecta las filas tenant-safe (via `IReportDefinitionService.GetPrintableAsync`).
- Paginas: `/reportes/imprimibles` (indice) + `/reportes/imprimibles/{id}` (visor) + boton "Guardar
  como imprimible" en `/reportes/ia` (genera el RDL y navega al visor).
- Registro de licencia en Program.cs (lee `Bold:LicenseKey`; sin clave = evaluacion).
- Convertidor `ReportSpecToRdl` + `SavePrintableAsync` + test `ReportRdlTests`.

## Lo que FALTA (accion del usuario / trabajo pendiente)

1. **Clave de licencia** (quita la marca de agua): registrar la Community de Bold y ponerla en
   `Bold:LicenseKey` (ver abajo). El visor ya funciona en evaluacion sin ella.
2. **Docker prod**: `System.Drawing.Common` + `Microsoft.Windows.Compatibility` pueden exigir libs
   nativas en Linux; confirmar con Bold antes del deploy.

DISENIADOR drag-drop: HECHO y verificado (modo evaluacion). `BoldReportsDesignerController`
(IReportDesignerController : IReportController): GetData abre el RDL por Id, SetData lo guarda via
`UpdateRdlAsync`; la vista previa reusa la inyeccion tenant-safe en Local. Pagina
`/reportes/imprimibles/editor/{id}` + boton "Editar" en el indice. Toolbox completo montado, reporte
abierto (`PostDesignerAction -> 200`). Nota: un ciclo completo editar->guardar por SetData no se
automatizo (el open + el controller estan verdes; SetData persiste en `ReportDefinition.Rdl`).

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

### a) Reclamar la Community License
1. Ir a https://www.boldreports.com/account/community-license (Claim Free License).
2. Crear/registrar la cuenta de Bitcode. Redirige al formulario de Community License.
3. Llenar los datos de elegibilidad (< 1M USD/anio, <= 5 devs, <= 10 empleados, < 3M capital externo).
   Se genera un ticket; Bold valida y aprueba.

### b) Generar el TOKEN de licencia (tras la aprobacion)
4. En la cuenta (https://www.boldreports.com/account), seccion **"Downloads & Keys / Claim License Key"**,
   generar el **online license token** para el producto **Bold Reports Embedded**.
5. IMPORTANTE: el token es **especifico por VERSION**. Debe ser el de la **v14** (nuestro paquete es
   `BoldReports.Net.Core` 14.1.14). Un token de otra major no activa estos ensamblados.
   (Alternativa sin internet en runtime: "offline license key file" — mismo origen.)

### c) Colocar el token FUERA del repo (gitignored)
6. Opcion recomendada en dev: user-secrets del proyecto `Ecorex.SuperAdmin`:
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

### 2. Registro de licencia (Program.cs, ANTES de que se inicialice cualquier control Bold)
```csharp
var boldKey = builder.Configuration["Bold:LicenseKey"];
if (!string.IsNullOrWhiteSpace(boldKey))
{
    Bold.Licensing.BoldLicenseProvider.RegisterLicense(boldKey);
}
```
(API oficial: `Bold.Licensing.BoldLicenseProvider.RegisterLicense(token)`. En ASP.NET Core/Blazor va en
el arranque, antes de construir/usar los componentes. El token debe ser el de la v14.)

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
