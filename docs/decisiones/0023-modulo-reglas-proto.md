# ADR-0023: Modulo de gestion de reglas fiel al concepto por-modulo (proto_gen_reglas)

- Estado: aceptada
- Fecha: 2026-07-04
- Relacionada con: ADR-0016 (rules engine, verbos tipados, SQL prohibido),
  ADR-0019 (E2E Playwright), ADR-0021 (constructor de formularios),
  ADR-0022 (editor de flujos)

## Contexto

El RulesEngine era FUNCIONAL (ADR-0016: 5 verbos tipados con descriptores de
parametros, RuleDocument/Rule/RuleExecutionLog con TTL 90d, vinculos
FormFieldRule/WorkflowNodeRule, historial append-only) pero la pagina /reglas era
una vista de 2 tabs estilo legacy con modales. El concepto por-modulo de Capa 6
(`proto_gen_reglas.html`) define la experiencia objetivo: layout PERMANENTE de 3
paneles (lista 320px / editor 1fr / propiedades 300px), topbar con breadcrumb +
badge de modulo (MOD 000802), tabs Configuracion/Historial/Consumidores, editor de
codigo oscuro con el contrato PARAM_XML y status strip de guardado.

## Decisiones

### 1. Tokens del workspace SOBRE la paleta naranja del concepto

`ECOREX.dc.html` es el sistema visual maestro del producto; el concepto
proto_gen_reglas trae una paleta naranja propia (accent #D97706, accent-soft
#FEF3C7). Se replica la ESTRUCTURA y las MEDIDAS del proto (grid 320/1fr/300,
topbar 14x24, rule-item 12x16 con barra 4px, titulo 20/600, editor-meta 160px/1fr,
tabs con borde inferior 2px, props 16px, history dot 8px, status strip 12x24) pero
con los TOKENS del workspace en `Reglas.razor.css`:

| Proto (naranja)        | Workspace                         |
|------------------------|-----------------------------------|
| --accent / accent-soft | --brand / --brand-soft            |
| --success              | --ok / --t-green(+bg)             |
| --danger               | --danger / --t-rose(+bg)          |
| --info                 | --t-blue(+bg)                     |
| warn-banner amarillo   | --t-amber-bg / --t-amber          |
| --muted / --muted-2    | --ink-2 / --ink-3                 |
| --card / --border      | --surface / --line(-2)            |
| --code-bg #1F2937      | SE MANTIENE fijo (claro y oscuro) |

El editor de codigo conserva sus colores propios de sintaxis (tags #93C5FD,
atributos #F0ABFC, strings #86EFAC, comentarios #64748B italic) porque el fondo
oscuro es fijo en ambos temas. Los KPIs no existen en el proto: usan el patron ya
establecido del workspace (icono 42x42 r11, valor 19/800, tonos --t-*).

### 2. PARAM_XML como REPRESENTACION editable del ParamsJson tipado

El legacy configuraba reglas con un PARAM_XML interpretado por reflexion (el RCE
que ADR-0016 prohibio). Aqui el XML es SOLO una vista editable del ParamsJson:

- `RuleParamXml` (clase PURA en Application/Rules, con tests unitarios de
  round-trip y de errores) genera
  `<REGLA><PROCESO>verbo</PROCESO><PARAMETROS><PARAM name tipo obligatorio valor/>`
  desde el ParamsJson + el descriptor del verbo, y parsea el XML de vuelta
  VALIDANDO contra el descriptor (proceso coincidente, nombres validos, tipos
  numeric/boolean/json/string/fieldcode, obligatorios). Nada del XML se ejecuta ni
  se resuelve por reflexion.
- El editor oscuro es un textarea transparente sobre un `<pre>` resaltado en C#
  (sin JS): Formatear re-indenta, Validar parsea y vuelca los valores a la "Vista
  renderizada" (el formulario dinamico por descriptor ya existente), que sigue
  siendo la fuente autoritativa al editarla (regenera el XML).
- Trade-off documentado: los valores json viajan en el atributo `valor` y el
  serializador XML escapa las comillas como `&quot;` (valido, algo ruidoso);
  Generate emite todos los parametros del descriptor y descarta claves ajenas
  (el guardado ya las rechaza).

### 3. Execute/mDATA visibles pero DESHABILITADOS (ADR-0016)

El select "Modo ejecucion" muestra los 3 modos del legacy para que la brecha sea
explicita, pero Execute (SQL directo) y mDATA estan `disabled` y el banner ambar
(siempre visible) lo explica citando ADR-0016. El motor no tiene ningun campo de
modo: todas las reglas reales son Ensamblado y el filtro por modo del sidebar
refleja eso (Execute/mDATA devuelven vacio). "Importar XML" queda deshabilitado
con tooltip "Proximamente": no hay formato de importacion definido.

### 4. Eliminar conserva la regla de negocio del historial append-only

DeleteRuleAsync sigue rechazando el borrado si hay historial; la UI, ante ese
Invalid, INACTIVA la regla (UpdateRuleAsync con Status=Inactive) y lo dice claro
en el status strip. Duplicar clona en el MISMO documento (nombre + " (copia)",
SortOrder max+1) pero nace en Development y SIN vinculos para evitar ejecuciones
dobles accidentales.

### 5. Backend aditivo minimo (sin migracion)

- `Rule.Description` YA existia: no hubo migracion (verificado antes de crearla).
- `IRuleDocumentService` gana: `ListAllRulesAsync` (lista plana con el documento
  como categoria), `GetRuleAsync`, `DuplicateRuleAsync`, `GetTenantStatsAsync` y
  `GetRuleMetricsAsync` (ventana 30d; tasa = Success/(Success+Failed), las Skipped
  no cuentan; promedio en ms), `GetRuleAuditAsync` (nombres legibles de
  CreatedBy/UpdatedBy) y `GetCurrentTenantUserIdAsync` (la prueba manual registra
  quien ejecuta). `SaveRuleRequest.DocumentId` permite MOVER la regla de documento
  desde el select del editor. `RuleExecutionLogDto.ExecutedByName` resuelve el
  ejecutor para el historial estilo proto.
- El status strip usa UpdatedAt/UpdatedBy reales; la "version" del proto se OMITE:
  Rule no esta versionada (el historial append-only cubre la trazabilidad de
  ejecucion; versionar la definicion queda como deuda declarada).

### 6. La gestion de documentos pasa a un modal del topbar

El proto no tiene panel de documentos: la lista del sidebar es PLANA (todas las
reglas del tenant) con el documento como categoria visible y filtrable. El CRUD de
documentos existente (crear/renombrar/archivar) vive en el boton "Documentos" del
topbar; "+ Nueva regla" usa el documento de la regla seleccionada o lo pide en un
mini modal.

## Consecuencias

- /reglas queda visualmente coherente con el resto del workspace (claro/oscuro via
  tokens) y estructuralmente fiel al concepto de Capa 6.
- El contrato PARAM_XML legacy es visible y editable sin reabrir la superficie de
  ataque del ejecutor por reflexion.
- Nuevos tests: unit (RuleParamXmlTests, 18), integracion dual (metricas 30d +
  lista plana, duplicar + mover de documento) y E2E (seleccionar en sidebar ->
  prioridad -> Validar XML -> Ejecutar -> Historial reciente).
- Deuda declarada: import de XML (boton deshabilitado), versionado de la
  definicion de regla, envio real de notificaciones del verbo NOTIFICAR
  (ADR-0016) y paginacion de historial en servidor si una regla supera 500
  ejecuciones vivas.
