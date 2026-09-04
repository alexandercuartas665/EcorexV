# ADR-0064 - Conector de origen de datos externo GOBERNADO para el Motor de Reportes

- Estado: ACEPTADA (2026-08-06). Implementado en la rama `feat/conector-datos-externos`
  (worktree), rebaseada sobre `origin/fase-0/clon-backbone`, PR a `fase-0/clon-backbone`.
- Fecha: 2026-08-06
- Rama / worktree: `feat/conector-datos-externos` rebaseada sobre `origin/fase-0/clon-backbone`.
- Relacionado: [ADR-0051 Motor de Reportes y BI], [ADR-0062 Catalogo de reportes reutilizables],
  regla inviolable 3 (SQL parametrizado), 6 (secretos solo cifrados) y 7 (db3dev SOLO LECTURA)
  de CLAUDE.md; patron de secretos de SMTP por tenant / Azure Blob (ISecretProtector).

## Contexto

Hoy TODO dato del Motor de Reportes entra por `IReportDataSource` (tenant-safe) y el visor Bold
corre en `ProcessingMode.Local` con las filas ya filtradas inyectadas como `DataTable`, IGNORANDO
el `ConnectionString` embebido en el RDL. Eso es correcto y seguro para las fuentes nativas y los
contenedores EAV del propio tenant.

Falta la via para datos EXTERNOS en vivo: un reporte SSRS heredado (p.ej. "REPORTE DE SISTEMA DE
TAREAS PERSONAL" sobre la legacy db3dev) trae su propia cadena de conexion y sus datasets ejecutan
SQL directo. Dejar que un RDL lleve su conexion viola 3 de los 9 errores heredados: secreto en el
reporte, acceso directo a la BD y sin filtro por tenant. Se necesita una via CORRECTA para leer
datos externos que preserve las reglas inviolables.

## Decision

Agregar un conector externo GOBERNADO, administrado SOLO por PlatformAdmin y auditado, con estas
piezas (todas nuevas, sin tocar el camino tenant-safe existente):

1. **`ExternalDataSource`** (entidad de PLATAFORMA, no tenant-scoped): motor (SqlServer|Postgres),
   `ConnectionStringEncrypted` (ISecretProtector/DataProtection; nunca en claro/repo/log/reporte),
   `IsReadOnly=true`, `IsEnabled`, `LastValidatedAt`. Se exige usuario de BD de SOLO LECTURA.

2. **`ExternalDataSet`** (limite de seguridad): consulta CURADA (`CommandText`, un unico SELECT
   parametrizado) + parametros tipados declarados + metadatos de campos. SOLO se ejecutan consultas
   registradas aqui; nunca SQL libre desde el reporte.

3. **`ExternalDataSourceGrant`** (concesion por tenant): el dato externo NO se filtra por tenant
   (vive en otra base), asi que la gobernanza es EXPLICITA. Una fuente/dataset solo es visible y
   ejecutable por los tenants con concesion vigente.

4. **Conector** (`ExternalReportReader`, analogo a `ContainerReportReader`): nueva
   `ReportSourceKind.External`, clave `external:{externalDataSetId}`. Verifica la concesion del
   tenant activo (fail-closed), descifra la cadena SOLO en memoria, enlaza los parametros y delega
   en el executor. Los parametros de ALCANCE (userid, sucursal, tenant) se enlazan del CONTEXTO de
   confianza (`ExternalParameterBinder`), no de entrada libre; los de entrada (fechas, filtros) se
   convierten al tipo declarado y viajan como parametro TIPADO (cero concatenacion). El catalogo
   (`IReportCatalog`) expone solo los datasets concedidos al tenant activo.

5. **Executor** (`AdoExternalQueryExecutor`, en Infrastructure porque tiene los drivers
   Microsoft.Data.SqlClient / Npgsql): abre conexion de SOLO LECTURA, FUERZA la lectura con la
   guarda `ExternalReadOnlyGuard` (un unico SELECT/WITH, sin verbos de escritura ni multi-statement)
   y, en Postgres, con `SET TRANSACTION READ ONLY` real; enlaza `DbParameter` tipados. Defensa en
   profundidad ADEMAS del usuario de solo lectura.

6. **Render imprimible** (ADR-0051, Ola 2): `ReportDefinition` gana `ExternalBindingJson` (mapeo de
   cada dataset del RDL a un `ExternalDataSet` + los inputs guardados). `ExternalReportBindingService`
   importa el RDL y, al renderizar, ejecuta cada dataset por el conector e inyecta UNA `DataTable`
   por dataset del RDL. El RDL NUNCA usa su conexion; el `BoldReportsApiController` ya corria en
   `ProcessingMode.Local` y solo se extendio para inyectar multiples datasets.

7. **PlatformAdmin**: pagina `/fuentes-externas` (policy `PlatformOperator`) para CRUD de
   fuentes/datasets/concesiones + prueba de conexion de solo lectura. Toda mutacion escribe
   `SuperAdminAuditLog` dentro de la transaccion; el secreto no se vuelve a mostrar (solo se indica
   si hay o no cadena).

8. **Migraciones DUALES** (`AddExternalDataConnector`, PG + SQL Server, `--context` explicito):
   3 tablas nuevas + columna `external_binding_json` en `report_definitions`.

## Consecuencias

- Un reporte externo se alimenta EN VIVO sin llevar su secreto ni su conexion: la cadena vive
  cifrada en el catalogo de plataforma y se descifra solo en memoria al ejecutar.
- El aislamiento del dato externo NO depende de un filtro por tenant (imposible: es otra base) sino
  de la concesion explicita; un tenant sin concesion no ve ni ejecuta la fuente. Es una diferencia
  deliberada frente a las fuentes nativas/contenedor (que si van por filtro global).
- La superficie de ataque queda acotada: solo consultas curadas registradas, solo lectura forzada,
  parametros tipados, alcance del contexto de confianza. Un intento de inyeccion queda como el valor
  literal de un parametro.
- Se asume que el operador de plataforma configura un usuario de BD de solo lectura; el conector lo
  refuerza pero no puede sustituir la buena practica de minimo privilegio en la BD externa.

## Alternativas descartadas

- **Dejar que el RDL lleve su ConnectionString** (comportamiento SSRS nativo): descartado, viola 3
  reglas inviolables (secreto en el reporte, acceso directo, sin gobernanza por tenant).
- **Filtrar el dato externo por tenant como las fuentes internas**: imposible por construccion (la
  BD externa no conoce el TenantId de ECOREX); de ahi la concesion explicita + alcance por contexto.
- **SQL libre desde el reporte con solo la guarda de solo lectura**: insuficiente; se exige ademas
  el dataset curado como allowlist de consultas.

## Casos de prueba (gates)

- `ExternalReadOnlyGuardTests` (unit): rechaza INSERT/UPDATE/DELETE/DDL/multi-statement/SELECT INTO;
  acepta un unico SELECT/WITH.
- `ExternalParameterBinderTests` (unit): alcance desde el contexto (ignora inputs), inputs tipados,
  intento de inyeccion queda como valor de parametro, valor malformado -> NULL tipado.
- `ExternalConnectorGovernanceTests` (integracion dual PG + SQL Server): tenant concedido ve/describe
  la fuente; tenant sin concesion no la ve y ejecutar LANZA (sin invocar el executor); revocar quita
  el acceso; la cadena se guarda cifrada (nunca en claro en la tabla).

## Nota (v0.15.157) - el default de autoria de un RowLimit NO capa el reporte

Un parametro que acota filas (TOP/LIMIT) suele llevar un DefaultValue pequeno para probar en el editor
(p.ej. @limite=5). Si un PANEL cae a ese default, el reporte se trunca. Se agrego el binding
`ExternalDataParameterBinding.RowLimit`: en contexto de REPORTE, `ExternalParameterBinder.Bind` lo enlaza
al tope duro del sistema (`ExternalReportReader.ReportMaxRows`, y la query usa el mismo MaxRows), NO a su
DefaultValue. La consola "Ejecutar" del editor (que no pasa `reportRowLimit`) sigue tomando el valor
tecleado o el DefaultValue. Asi el default de autoria no limita la salida de un dashboard.

## Nota (v0.15.168) - AllowBatch: opt-in por dataset a consultas multi-statement / batch

El guard de solo lectura exige un unico SELECT/WITH; eso rechaza batches T-SQL legitimos de RDL reales
(SSRS): DECLARE de tablas + INSERT INTO @tabla + EXEC sp_... + WITH + SELECT final. Corren en la consola
"Ejecutar" cuando la conexion tiene AllowWrite, pero la ruta de REPORTES los rechazaba.

Decision (opcion B): un OPT-IN por dataset, `ExternalDataSet.AllowBatch` (default false). NO es un
relajamiento global: el guard sigue siendo el default estricto para todo lo demas.

- Cuando `AllowBatch=true`, el ejecutor (AdoExternalQueryExecutor) OMITE el guard de solo lectura y NO
  fuerza la transaccion read-only (en Postgres no aplica `SET TRANSACTION READ ONLY`). La decision del
  bypass se centraliza en `ExternalReadOnlyGuard.EnsureReadOnly(cmd, allowWrite, allowBatch)` y la comparte
  con `AllowWrite` (conexion propia con escritura). Independientes: una conexion de solo lectura puede tener
  UN dataset con batch habilitado.
- Los parametros se siguen enlazando como DbParameter TIPADOS (cero interpolacion): la proteccion
  anti-inyeccion NO cambia. Se mantienen MaxRows y timeout.
- Lo activa el OWNER del tenant sobre SU conexion (tenant-scoped por OwnerTenantId) en /conexiones-datos,
  con un checkbox y AVISO en rojo (la consulta puede modificar el servidor externo). La cadena sigue
  cifrada y nunca se expone.
- Trazabilidad: al ejecutar en la ruta de reportes un dataset con AllowBatch se registra en auditoria
  (dataset id + tenant + usuario) via IAuditWriter (ExternalReportReader).
- Migracion dual aditiva (`allow_batch` bool NOT NULL DEFAULT false) en PG y SQL Server.

## Casos de prueba (gates) - AllowBatch

- `ExternalReadOnlyGuardTests` (unit): el overload con opt-in acepta el batch cuando allowBatch/allowWrite
  es true y lo rechaza (igual que antes) cuando ambos son false; el caso normal de un SELECT unico sigue
  intacto.
