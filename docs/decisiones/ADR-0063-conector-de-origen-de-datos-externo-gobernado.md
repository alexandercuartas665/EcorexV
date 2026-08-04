# ADR-0063 - Conector de origen de datos externo GOBERNADO para el Motor de Reportes

- Estado: PROPUESTA (2026-08-04). Decision de modelo tomada con el usuario (camino B); pendiente de
  implementar en la sesion de desarrollo sobre el tronco de prod.
- Fecha: 2026-08-04
- Rama / worktree: propuesta desde `feat/motor-reportes`; se implementa sobre `fase-0/clon-backbone`.
- Relacionado: [ADR-0051 Motor de Reportes y BI], [ADR-0062 plantillas entre tenants]; reglas
  inviolables 1-9 (multi-tenant real, DAL, SQL parametrizado, secretos solo en .env/Key Vault, db3dev
  SOLO LECTURA); vault "Capa 6 / Motor de Reportes y BI" doc 09 (prompt de este ADR).

## Contexto

Hay reportes (p.ej. RDLs de Reporting Services que consultan la BD legacy db3dev) que necesitan datos
de una BD EXTERNA al sistema. Hoy el motor NO lo permite a proposito: TODO dato entra por
`IReportDataSource` (tenant-safe), el visor Bold corre en ProcessingMode.Local con datos inyectados y
IGNORA el DataSource/ConnectionString del RDL. Importar un RDL con su propia conexion violaria varios
de los 9 errores heredados: secreto (password) dentro del reporte/repo, acceso directo a BD sin filtro
por tenant, y SQL potencialmente no parametrizado.

Se necesita una forma de leer datos externos SIN romper esas reglas: la conexion no puede vivir en el
reporte, debe ser de solo lectura, parametrizada, con secreto cifrado, y gobernada por tenant/rol.

## Decision

Agregar un **conector de origen de datos externo GOBERNADO**, integrado al motor como una fuente mas:

1. **`ExternalDataSource` (nivel plataforma, PlatformAdmin).** Id, Name, Provider (SqlServer|Postgres|
   ...), **ConnectionStringEncrypted** (DataProtection/Key Vault; NUNCA en claro, ni en repo, ni en el
   reporte, ni en logs), IsReadOnly (default true), IsEnabled, audit. La administra el operador de
   plataforma; toda accion AUDITADA (AdminAuditLog).

2. **`ExternalDataSet` (consulta gobernada = limite de seguridad).** Id, ExternalDataSourceId, Name,
   CommandText (SELECT parametrizado, curado/revisado), lista de parametros (nombre/tipo), y metadatos
   de campos reportables. SOLO se ejecutan consultas registradas aqui (no SQL libre desde el reporte).

3. **Concesion por tenant/rol.** Que tenants (y roles) pueden usar cada ExternalDataSource/DataSet.
   Como el dato externo NO se filtra solo por tenant, la gobernanza es EXPLICITA: una fuente externa
   solo es usable por los tenants a los que se concede, y los parametros de alcance (p.ej. userid,
   sucursal) se enlazan desde el CONTEXTO de confianza (tenant/usuario), no desde entrada libre.

4. **Exposicion uniforme via `IReportDataSource`.** Nueva `ReportSourceKind.External`, clave de fuente
   `external:{dataSetId}`. El conector abre una conexion de SOLO LECTURA desde la cadena cifrada,
   ejecuta la consulta parametrizada (parametros enlazados de forma segura, cero concatenacion) y
   devuelve un `ReportDataSet` neutro. El reporte referencia la clave logica, jamas la cadena.

5. **Render.** Imprimibles RDL: ProcessingMode.Local; NUESTRO controlador ejecuta el ExternalDataSet
   por el conector e INYECTA los DataTable (el RDL nunca ejecuta su propia conexion; sus datasets se
   mapean a ExternalDataSets). Dashboards/spec: el mismo conector alimenta ECharts.

## Consecuencias

Positivas:
- Reportes con datos externos/en vivo SIN secreto en el reporte, read-only, parametrizados y
  gobernados. Permite importar RDLs de SSRS mapeando sus datasets a consultas gobernadas.
- El armado de estos reportes lo puede hacer la persona de reportes desde PlatformAdmin + mapeo, sin
  tocar codigo cada vez.

Negativas / costos:
- Nuevas tablas de plataforma + conector + UI de gobernanza. Es codigo SENSIBLE: hay que forzar
  read-only, SQL parametrizado, cifrado del secreto y concesiones con rigor.
- Onboarding por reporte: mapear los datasets del RDL a ExternalDataSets curados.
- Riesgo si se relaja: un ExternalDataSet mal escrito podria exponer datos cruzados; por eso el alcance
  se enlaza desde el contexto de confianza y las consultas se revisan.

## Alternativas consideradas

- **El reporte lleva su propia conexion (estilo SSRS):** RECHAZADA. Viola reglas inviolables (secreto
  en el reporte, sin gobernanza, sin frontera de tenant).
- **Solo ingesta a Contenedor (camino A, ADR/patron existente OCS/Siigo):** valida para datos
  periodicos (copia tenant-scoped), NO para datos en vivo. Se mantiene como la otra opcion; este ADR
  agrega la via de datos externos EN VIVO gobernada.

## Nota de seguridad operativa

- El secreto de conexion va cifrado (DataProtection/Key Vault). Si una cadena se expuso (p.ej. pegada
  en un chat/archivo), ROTAR la credencial. Usar un usuario de BD de SOLO LECTURA. db3dev es SOLO
  LECTURA por regla del proyecto.
