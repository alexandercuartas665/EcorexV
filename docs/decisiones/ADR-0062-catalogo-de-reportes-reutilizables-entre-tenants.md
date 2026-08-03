# ADR-0062 - Catalogo de reportes reutilizables entre tenants (plantillas + activacion hibrida)

- Estado: ACEPTADA (2026-08-03). Implementado en el tronco de prod: entidad global
  `ReportTemplate` (mismo patron que PlatformUser/SaasPlan en EcorexDbContext), columna
  `TemplateId` en `report_definitions`, migraciones duales (AddReportTemplates, PG + SQL Server),
  servicio de activacion (Activate/Deactivate/Resync/ActivateCompatible/ListActivatable),
  `CreateExampleReportsAsync` refactorizado a template-based, seed de las 2 plantillas base,
  pagina PlatformAdmin `/plantillas-reportes` (auditada) y auto-activacion en la galeria.
  Test de aislamiento cross-tenant dual (PG + SQL Server) en verde.
- Fecha: 2026-08-03
- Rama / worktree: propuesta desde `feat/motor-reportes` (worktree `informes`); se implementa
  sobre `fase-0/clon-backbone` (tronco de prod, que ya tiene galeria + gobernanza por roles).
- Relacionado: [ADR-0051 Motor de Reportes y BI]; doc 04 (gobernanza por roles), doc 05
  (enganche Panel OCS), doc 06 (prompt de este ADR) del vault "Capa 6 / Motor de Reportes y BI";
  memoria de proyecto `motor-reportes-decision`.

## Contexto

El motor de reportes ya guarda cada reporte como fila en `report_definitions` (ITenantScoped:
pertenece a UN tenant) y lo gobierna por rol con `report_definition_roles` (quien lo ve dentro
del tenant). Falta el nivel de ARRIBA: un reporte suele ser util para varios tenants (p.ej. un
"Panel de Actividades" nativo, o un "Panel OCS" para todo cliente que tenga el contenedor OCS),
pero hoy no hay forma de definirlo UNA vez y ofrecerlo a varios; habria que recrearlo tenant por
tenant.

Requisito duro (reglas inviolables): compartir la DEFINICION no puede filtrar datos entre tenants.
Esto ya se cumple por construccion, porque un reporte no lleva datos: guarda un `SourceKey` + spec/
panel/RDL y SIEMPRE se ejecuta contra `IReportDataSource`, que aplica el filtro global por tenant.
La misma definicion en el tenant A y en el B pinta los datos de cada uno; A jamas ve datos de B.

Casos que se ven hoy: el Panel de Actividades usa una fuente NATIVA (tareas) -> aplica a cualquier
tenant. El Panel OCS usa una fuente CONTENEDOR ("Software OCS") -> solo aplica donde exista ese
contenedor (lo busca por nombre; si no esta, avisa).

## Decision

Adoptar un catalogo de reportes de **3 capas** con activacion **HIBRIDA** (plantilla global +
instancia tenant-scoped al activar):

1. **Plantillas de plataforma (globales).** Nueva entidad `ReportTemplate`, propiedad de la
   plataforma (NO tenant-scoped; se administra desde PlatformAdmin y se lee via un servicio que
   expone solo las publicadas). Es el "molde" reutilizable.

2. **Activacion por tenant (instancia ligera).** Al activar una plantilla en un tenant se crea una
   fila `report_definitions` tenant-scoped con un `TemplateId` (FK a la plantilla) y un SNAPSHOT de
   `SourceKey`/`Kind`/`SpecJson`/`Rdl`. Hibrido = referencia (sabe de que plantilla vino) + copia
   (el tenant puede personalizar su instancia; opcion de "re-sincronizar" desde la plantilla).

3. **Reportes propios del tenant.** Los que el cliente crea con IA/editor: `report_definitions` con
   `TemplateId = null`. Es lo que ya existe hoy; no cambia.

**Gobernanza:** la instancia activada se rige por `report_definition_roles` como cualquier reporte
(sin asignacion = todos; Owner/Admin ven todo). La plantilla puede sugerir un rol por defecto.

**Compatibilidad de fuente (validada en la activacion):**
- Fuente NATIVA (TaskItem, etc.): activable en cualquier tenant.
- Fuente CONTENEDOR o panel que depende de contenedor (`RequiredContainerName`, p.ej. "Software
  OCS"): activable SOLO si el tenant tiene ese contenedor; si no, se bloquea con mensaje claro.
- **Auto-activacion por condicion**: para plantillas de contenedor, si el tenant ya tiene el
  contenedor requerido, la plantilla se ofrece/activa automaticamente (asi el Panel OCS aparece
  solo, y solo, donde hay datos OCS).

**Aislamiento multi-tenant:** la instancia es ITenantScoped y corre via `IReportDataSource` (filtro
global fail-closed). La plantilla es solo metadato; el dato nunca viaja con ella. El unico punto
cross-tenant es la administracion del catalogo maestro, que es PlatformAdmin y va AUDITADO.

## Modelo de datos (resumen)

- `ReportTemplate` (plataforma): Id, Name, Description, Kind (Dashboard|Printable|Panel), SourceKey,
  SpecJson?, Rdl?, RequiredSourceKind (Native|Container), RequiredContainerName?, Category, Icon,
  IsPublished, audit. NO tenant.
- `report_definitions` (tenant): + columna `TemplateId` (uuid?, FK logica a ReportTemplate; null =
  reporte propio del tenant).
- Migraciones DUALES (PG + SQL Server), `--context` explicito.

## Consecuencias

Positivas:
- Un reporte se define UNA vez y se ofrece a muchos tenants; cada uno ve SUS datos (seguro por
  construccion).
- El tenant puede personalizar su instancia sin romper el molde; puede re-sincronizar si quiere.
- PlatformAdmin cura el catalogo maestro (calidad y consistencia entre clientes).
- El Panel OCS y el Panel de Actividades pasan a ser plantillas; el Panel OCS se auto-activa donde
  haya contenedor OCS.

Negativas / costos:
- Dos conceptos que mantener (plantilla vs instancia) y una politica de re-sincronizacion (por
  defecto snapshot: los cambios en la plantilla NO se propagan solos; se ofrecen como "actualizar").
- Nueva tabla de plataforma + columna nueva en `report_definitions` + migraciones duales.
- La administracion del catalogo maestro debe quedar auditada (AdminAuditLog) por ser cross-tenant.

## Alternativas consideradas

- **Solo referencia** (sin copia): un molde central, los cambios se propagan solos, pero el tenant
  no puede personalizar. Rechazada: demasiado rigida para clientes con necesidades distintas.
- **Solo copia** (sin vinculo): se duplica en cada tenant sin rastrear origen ni permitir
  actualizaciones en bloque. Rechazada: se vuelve inmanejable a escala.
- **Hibrido** (elegida): plantilla global + instancia tenant-scoped vinculada, con re-sincronizacion
  opcional y auto-activacion por condicion. Equilibra reutilizacion, aislamiento y personalizacion.
