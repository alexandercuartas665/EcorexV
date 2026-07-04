# ADR-0016: RulesEngine (motor de reglas con verbos tipados) - FASE 4, ola 3

- Estado: aceptada
- Fecha: 2026-07-03
- Relacionada con: ADR-0001 (DAL dual), ADR-0013 (nucleo TaskItem), ADR-0014
  (WorkflowEngine), ADR-0015 (DynamicFormRenderer)

## Contexto

El legacy GestionMovil ejecuta reglas de negocio configurables con `cl_gestion_reglas`
(modulo 000802): 8 documentos de reglas y 21 reglas activas en produccion. Tiene tres
modos (`mDATA`, `Execute`, `Ensamblado`) de los que SOLO Ensamblado se usa de verdad, y
dos defectos graves:

1. **RCE por reflexion**: el ejecutor hacia `Activator.CreateInstance` sobre nombres de
   clase leidos del XML de la regla (protocolo PARAM_XML). Cualquiera que editara la
   configuracion podia instanciar tipos arbitrarios.
2. **Historial perdido**: el codigo escribia el log de ejecucion contra una tabla que no
   existia; ninguna ejecucion quedo registrada jamas.

Ademas el modo `Execute` recibia SQL directo desde la configuracion (inyeccion por
diseno). Esta ola porta el nucleo del motor cerrando esos tres agujeros.

## Decision

### 1. Registro TIPADO en DI en vez de reflexion (el RCE no se hereda)

Cada verbo es UNA clase que implementa `IRuleVerb { Name, Descriptor, ExecuteAsync }` y
se registra EXPLICITAMENTE en `DependencyInjection` (`AddScoped<IRuleVerb, XxxVerb>()`).
El ejecutor (`RulesEngine`) resuelve el verbo por `Rule.VerbName` en un diccionario
construido desde `GetServices<IRuleVerb>()`: un VerbName desconocido es un **error
tipado** (`RuleServiceStatus.Invalid` + fila `Failed` en el historial), nunca
`Activator.CreateInstance` sobre texto. Los verbos se resuelven de forma DIFERIDA desde
el `IServiceProvider` para romper el ciclo de construccion `WorkflowEngine ->
IWorkflowRuleHook -> IRulesEngine -> verbos -> ITaskItemService -> IWorkflowEngine`.

### 2. El modo Execute (SQL directo) queda PROHIBIDO

No existe ni existira verbo que reciba SQL en parametros. Toda logica de reglas pasa por
verbos tipados que operan via servicios de Application (LINQ parametrizado). Es la misma
regla inviolable n.3 del proyecto aplicada al motor. Los modos legacy `mDATA` y
`Execute` NO se portan; solo la semantica de `Ensamblado` (el unico usado).

### 3. Descriptor de parametros tipado (port del protocolo PARAM_XML)

Cada verbo declara `RuleVerbDescriptor` con sus parametros (nombre, etiqueta, tipo
`Text/Number/Boolean/FieldCode/Json`, obligatorio, descripcion). La UI de /reglas
renderiza el formulario de configuracion desde el descriptor sin conocer la clase, y
`RuleDocumentService` valida al guardar que el JSON sea objeto y que esten los
obligatorios. `Rule.ParamsJson` persiste como jsonb (PG) / nvarchar(max) (SQL Server),
mismo patron dual del resto del modelo.

### 4. Historial SIEMPRE, append-only y con TTL de 90 dias

`RulesEngine.ExecuteRuleAsync` escribe `RuleExecutionLog` en TODA ejecucion (Success,
Failed o Skipped) con `Stopwatch` (DurationMs), snapshot del nombre, payload de
invocacion (`ContextJson`) y `ExpiresAt = ahora + 90 dias`. Una regla con historial no
se puede borrar (error tipado: inactivala). El worker `RuleLogTtlCleanupWorker`
(Ecorex.Workers, diario) ejecuta el UNICO DELETE fisico permitido del modulo:
`RuleExecutionLog` con `ExpiresAt` vencido, cross-tenant a proposito
(`IgnoreQueryFilters` + `ExecuteDelete`), implementado en `IRuleExecutionLogCleaner`
para poder probarlo en la matriz dual.

### 5. Acciones de UI tipadas (el renderer no interpreta strings)

Los verbos devuelven `RuleAction` tipadas: `HideField`, `ShowField`, `SetFieldValue`,
`SetRequired`. El `DynamicFormRenderer` las aplica via `FormRuleUiState` (campos ocultos
por regla NO se validan como requeridos en cliente; la obligatoriedad respeta los
overrides). La integracion con el renderer esta encapsulada en `IFormRuleDispatcher`
(que campos disparan reglas + despacho al cambiar un campo): el renderer no conoce el
motor.

### 6. Disparadores y elegibilidad

- **Manual** ("Ejecutar prueba" en /reglas): corre reglas Active y Development (asi se
  prueban antes de activarlas); Inactive o documento archivado -> `Skipped` registrado.
- **FormField** (`FormFieldRule`, port de FORX_DATA.EJECUTA_PARAM): al cambiar el campo
  en el renderer, en `SortOrder`, propagando el FormData entre reglas encadenadas. Solo
  reglas Active de documentos Active.
- **WorkflowNode** (`WorkflowNodeRule` + `WorkflowRuleHook`, que reemplaza al
  `NoOpWorkflowRuleHook` en DI): al activar un nodo Task corren las reglas IsAutonomous;
  si TODAS tienen exito y alguna pide `AutoCompleteStep`, el hook devuelve AutoComplete
  y el motor completa el paso solo. Un fallo de regla NUNCA atasca el flujo: el paso
  queda Pending (humano).

### 7. Catalogo inicial de 5 verbos y extension futura

Esta ola registra: `PASAR_CAMPOS`, `BLOQUEAR_CAMPO_XCONDICION`, `ASIGNAR_CONSECUTIVO`
(usa ISequenceService, CAS atomico), `GENERAR_TAREAS_DESDE_TABLA` (usa ITaskItemService)
y `NOTIFICAR` (deja la intencion en TaskItemActivity; el envio real de correo es TODO de
integracion). Los verbos legacy de IA e importacion (`GENERAR_TABLAS_IA`,
`IMPORTAR_CSV`, `DATA_SERVER*`) NO van en esta ola: se agregaran como clases nuevas
registradas en DI sin tocar el motor (el catalogo es abierto por diseno).

## Consecuencias

- El aislamiento multi-tenant es por construccion: entidades `TenantEntity` + filtro
  global; ejecutar una regla de otro tenant devuelve NotFound sin log fantasma.
- El log participa de la transaccion ambiente: si un caso de uso que engloba la
  ejecucion hace rollback (p.ej. conflicto de concurrencia del flujo), esa fila de
  historial se revierte con el (coherencia sobre visibilidad).
- Limitacion conocida: la exencion "oculto por regla => no requerido" aplica en el
  RENDERER; la re-validacion del servidor (`FormResponseService.SaveAsync`) no ejecuta
  reglas (evita efectos colaterales tipo consumo de consecutivos en validacion). Un
  campo Required oculto por regla y vacio fallaria el submit en servidor: configura la
  regla con `SetRequired(false)` ademas de Hide, o usa campos opcionales como objetivo.
  Evaluacion servidor de verbos puros queda para una ola posterior.
- Los 8 documentos y 21 reglas del legacy se portaran en el ETL (FASE 6) mapeando cada
  regla Ensamblado a un verbo del catalogo (o marcandola Inactive si su verbo aun no
  existe).
