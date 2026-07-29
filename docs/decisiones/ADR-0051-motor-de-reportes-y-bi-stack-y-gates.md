# ADR-0051 - Motor de Reportes y BI: decision de stack y gates de licencia (Ola 0)

- Estado: PROPUESTA (pendiente de confirmacion del usuario en 2 puntos abiertos)
- Fecha: 2026-07-29
- Rama / worktree: `feat/motor-reportes` en worktree `informes` (`C:/DesarrolloIA/ecorex-informes`)
- Relacionado: spec del vault "Capa 6 / Motor de Reportes y BI" (docs 00-03);
  memoria de proyecto `motor-reportes-decision`; ADR de deploy prod Docker.

## Contexto

ECOREX necesita un motor de reportes y BI dentro del producto (.NET 10 / Blazor
Server) para: (T1) documentos imprimibles, (T2) dashboards interactivos y (T3)
reportes ad-hoc creados por IA, sobre tablas nativas multi-tenant Y Contenedores
(DataModel). Requisito atado a las reglas inviolables: TODO dato via un datasource
ya filtrado por tenant (nunca cadena de conexion a la herramienta).

Stack pre-decidido con el usuario (2026-07-29): Syncfusion Essential Studio +
Bold Reports (editor visual + visor + export RDL) bajo Community License si
califican, ECharts para dashboards a medida, y una capa propia (catalogo semantico
+ IReportDataSource tenant-safe) como corazon independiente de la libreria.

La Ola 0 es un GATE: no se escribe codigo de producto hasta responder SI/NO a los
4 gates y confirmarlos con el usuario.

## Resultado de los 4 gates (con fuentes oficiales)

Hallazgo transversal de la investigacion: Bold Reports se **separo de Syncfusion**
y hoy tiene **licenciamiento propio**. La Community License de Syncfusion cubre en
Bold **solo el Report Viewer** (no el Report Designer). Para el editor drag-drop
(Report Designer) hace falta la **Community License propia de Bold Reports** (que
si lo incluye). Esto corrige el nombre del camino: no es "Syncfusion community",
es "Bold Reports community".

### Gate 1 - Elegibilidad Community License: SI (condicional)

- Existe tier gratis que cubre editor + visor: la Bold Reports Community License
  incluye Web Report Designer, Web Report Viewer, Report Writer y Standalone
  Designer, "same features as the paid license".
- Elegibilidad (identica en Syncfusion y Bold): < 1M USD ingresos brutos/anio,
  <= 5 devs, <= 10 empleados, y NUNCA > 3M USD de capital externo (PE/VC).
- CONDICION 1 (solo el usuario puede responder): confirmar que Bitcode cumple los
  4 umbrales. No es verificable por el agente.
- CONDICION 2: registrar bajo la Community License de **Bold Reports**, no la de
  Syncfusion (esta ultima solo daria el Viewer).
- Fuentes: boldreports.com/community-license/ ;
  help.boldreports.com/embedded-reporting/licensing/faq/can-the-report-designer-component-be-accessed-from-bold-reports-using-syncfusion-community-license/

### Gate 2 - Editor web embebe en Blazor Server con auth/cookies: SI

- Componentes Blazor oficiales; paquete `BoldReports.Net.Core` (crea el Web API
  service que procesa los reportes). Render mode InteractiveServer soportado.
  Designer implementa `IReportDesignerController`; Viewer `IReportController`.
- Matiz: el Designer/Viewer son componentes JS que hablan con un controller Web
  API ASP.NET Core alojado en la MISMA app, por lo que corren bajo el pipeline de
  auth propio; asegurarlo es responsabilidad nuestra (no hay paso de cookie
  documentado explicitamente). Suele requerir `AddNewtonsoftJson()`.
- Fuentes: help.boldreports.com/embedded-reporting/blazor-reporting/report-viewer/... ;
  .../report-designer/add-report-designer-to-a-blazor-application/

### Gate 3 - Datasource tenant-safe (JSON/Web, sin connection string): SI

- Soporta JSON data source y Web API data source; ademas inyeccion programatica
  via `addDataSource` / `addDataSet` en el evento de init del Designer, y
  extensiones de data source personalizadas. Encaja exacto con "filas ya
  preparadas por una API tenant-safe, sin cadena de conexion".
- Fuentes: help.boldreports.com/.../json-data-source/ ;
  help.boldreports.com/embedded-reporting/how-to/use-json-data-as-report-data/ ;
  support.boldreports.com/kb/article/12848/...

### Gate 4 - Redistribucion SaaS (exponer el editor a los tenants): SI

- La pagina de community license endosa el caso SaaS multi-tenant: "Embed
  reporting for your end customers with multi-tenant support", audiencia elegible
  "ISV and SaaS companies", "redistribution to others is allowed under our
  standard redistribution grant". La elegibilidad mira el tamanio de NUESTRA
  compania, no el numero de usuarios finales.
- Fuente: boldreports.com/community-license/

## Riesgo AMBAR a escalar (no es un NO duro, pero es material)

**Docker bajo Community License no esta claramente concedido.** La community
concede deployment "Windows and Linux"; Docker y Kubernetes/AKS aparecen en el
bucket de PAGO. Prod de ECOREX corre en Linux **Docker** (contenedor `ecorex-app`).
Tecnicamente funciona (FAQ oficial: "Embedded Reporting Tools support .NET Core on
Linux Docker"), pero el PERMISO de licencia para Docker no esta por escrito.
Ademas, "one application" no esta definido formalmente para un SaaS multi-tenant
escalado. Ambos puntos ameritan confirmacion escrita de Bold Reports (sales/legal)
antes de apoyarse en ellos para produccion.

Alternativas si Docker-en-community no se confirma: (a) desplegar el servicio de
reportes nativo en Linux (no contenedor) ; (b) licencia Embedded paga para
Docker/K8s ; (c) alternativa paga de suite (Stimulsoft/DevExpress/Telerik) ; (d)
construir editor a medida sobre la capa propia (meses).

## Decision

- Stack confirmado a nivel tecnico: **Bold Reports Embedded SDK (Designer+Viewer,
  RDL) + ECharts (interop, .js estatico) + capa propia (catalogo semantico +
  IReportDataSource tenant-safe)**. Los 4 gates dan SI (gate 1 condicionado a la
  elegibilidad de Bitcode y a registrar la community de Bold, no la de Syncfusion).
- **Desacople deliberado**: la Ola 1 (catalogo + datasource tenant-safe + test de
  aislamiento dual) NO depende de Bold Reports ni de Docker; se puede construir
  mientras se resuelven los 2 puntos abiertos. El vendor solo entra en la Ola 2.

## Puntos abiertos que requieren decision del usuario

1. Confirmar que Bitcode cumple la elegibilidad Community (< 1M USD/anio, <= 5
   devs, <= 10 empleados, sin > 3M USD de capital externo).
2. Docker-bajo-community: obtener confirmacion escrita de Bold, o elegir una
   alternativa de deployment/licencia de la lista de arriba.

## Consecuencias

- El valor propio (capa de definicion + catalogo + datasource) es independiente de
  la suite: si un dia se cambia de Bold a otra suite paga o a un editor a medida,
  esa capa se conserva. Bajo acoplamiento al vendor.
- El limite de seguridad es el catalogo semantico: lo que no esta en el catalogo no
  es reportable, lo que acota a la IA y al usuario.
