# ADR-0084: Conexiones de datos externas PROPIAS del tenant (extension de ADR-0064)

- Estado: Aceptado
- Fecha: 2026-09-01
- Contexto: ADR-0064 introdujo "Fuentes de datos externas" como catalogo de PLATAFORMA (PlatformAdmin
  registra la fuente cifrada de SOLO LECTURA y la concede a tenants por grant; los reportes las consumen).
  Un tenant no puede registrar SUS propias conexiones: cada empresa tiene servidores que otras no
  (p.ej. SOLDARCO -> aplicaciones.Soldarco.com), y necesita gestionarlos ella misma, incluida ESCRITURA.

## Decision

Se extiende el modelo existente (sin duplicarlo) para permitir conexiones **propias del tenant**, con
escritura opcional por conexion, gestionadas desde el menu del tenant (grupo Sistema / Desarrollo).

### Datos (reusa ExternalDataSource / ExternalDataSet, sin entidad nueva)

- `ExternalDataSource.OwnerTenantId` (Guid?, nullable). NULL = fuente GLOBAL de plataforma (comportamiento
  ADR-0064 intacto). Con valor = conexion PROPIEDAD de ese tenant. Sin `HasQueryFilter` (la entidad sigue
  siendo de plataforma); el aislamiento por tenant es EXPLICITO en `TenantDataConnectionService` (todo
  lookup exige `OwnerTenantId == tenant activo`). Indice por `owner_tenant_id`.
- `ExternalDataSource.AllowWrite` (bool, default false). Habilita INSERT/UPDATE/DELETE contra el servidor
  externo. Las fuentes globales de reportes lo dejan en false (sin regresion).
- Migracion dual aditiva (`AddExternalDataSourceTenantOwner`) en Postgres y SQL Server.

### Ejecutor (AdoExternalQueryExecutor)

- `ExternalQuery.AllowWrite` (default false). Cuando es false: se aplica `ExternalReadOnlyGuard` +
  (en PG) transaccion `SET TRANSACTION READ ONLY` (ADR-0064, sin cambios; reportes siguen solo lectura).
  Cuando es true: se OMITE el guard y se confirma la transaccion (permite escritura). El flag viaja SIEMPRE
  desde `ExternalDataSource.AllowWrite`, no desde entrada libre.

### Servicio y UI (tenant)

- `ITenantDataConnectionService` (Application/Tenancy/DataConnections): CRUD de conexiones propias, prueba
  de conexion, CRUD de datasets, ejecucion de dataset y **consulta directa** (SQL ad-hoc). Todo acotado al
  tenant por `OwnerTenantId`; secreto cifrado con `ISecretProtector`, nunca devuelto en claro; cada
  ejecucion se AUDITA (que conexion, si escribe, tope de filas; el SQL completo no se registra).
- Pagina `/conexiones-datos` (policy `Conexiones.Editar`, claim `tenant_id`), en el grupo de menu
  "Sistema / Desarrollo". Formulario con toggle "Permitir escritura"; grilla de resultados; datasets.

## Consecuencias

- Multi-tenant: una empresa nunca ve ni ejecuta las conexiones de otra (filtrado explicito por dueño).
- La pantalla de PLATAFORMA (ADR-0064) y sus grants siguen funcionando para fuentes globales/compartidas.
- Riesgo asumido: con `AllowWrite` una consulta puede MODIFICAR el servidor externo. Es opt-in por conexion
  (default apagado), auditado, y el aviso es visible en la UI. El usuario de BD externo deberia acotar
  permisos segun corresponda.
- El menu es data-driven por tenant: el nodo "Conexiones de datos" se agrega por tenant (SQL/config), no por
  seed global.

## Pendientes / futuro (fuera de alcance)

- Que los AGENTES listen las conexiones del tenant y ejecuten sus datasets (siguiente fase).
- Parametros tipados en los datasets del tenant (hoy son consultas planas; la infra de params de ADR-0064
  esta disponible si se necesita).
