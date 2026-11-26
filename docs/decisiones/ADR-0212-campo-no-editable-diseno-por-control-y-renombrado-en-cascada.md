# ADR-0212: Campo no editable, diseno por control y renombrado del nombre interno en cascada

**Estado:** Aceptado
**Fecha:** 2026-11-26
**Decisores:** Alexander Cuartas (usuario), ECOREX.desarrollo

## Contexto

En el disenador de formularios el usuario pidio tres cosas:

1. Una caja de texto o checkbox **no editable**. Ya existia "Solo lectura para roles" (FieldVisibilityJson),
   pero depende del rol del usuario: en el formulario publico `/f/` no hay rol y el campo quedaba editable.
2. **Diseno por control**: hoy por campo solo habia etiqueta visible y ancho; color, tema y tarjetas son
   globales del formulario (ThemeJson / CustomCss).
3. **Cambiar el nombre interno** (FieldCode). Estaba bloqueado en la UI porque es la llave de
   `FormResponse.Data` y lo referencian calculos, condicionales, grillas, reglas, plantillas y reportes.
   Ademas `UpdateQuestionAsync` (y el MCP `update_question`) ya permitian cambiarlo SIN propagar nada: al
   siguiente guardado `FormResponseService` descarta la clave vieja y el valor se pierde.

## Decision

- **No editable**: nueva columna `FormQuestion.IsReadOnly` (bool, default false). El renderer la suma a
  `IsFieldReadonly` (fieldset disabled), asi aplica sin rol y en `/f/`. Se aplica en la UI; el servidor no
  rechaza el valor porque las reglas (SetValue), el prellenado y el valor por defecto lo escriben del lado
  del cliente y se guardan con el mismo SaveAsync.
- **Diseno por control**: nueva columna `FormQuestion.StyleJson` (json dual) con el esquema de
  `FormFieldStyle` (etiqueta: tamano/negrita/cursiva/color/ocultar; caja: fondo/borde/grosor/redondeo/alto/
  alineacion; valor: tamano/negrita/color; bloque: margenes/fondo). Se SANEA al guardar (colores solo #hex,
  numeros acotados): el renderer solo emite clases bandera `fs-*` + variables CSS `--fs-*`, nunca CSS libre.
- **Renombrado en cascada**: `IFormDefinitionService.RenameFieldCodeAsync(questionId, newCode, apply)`.
  - `apply=false` es una simulacion (carga sin seguimiento, no guarda) que lista que se cambiaria.
  - `apply=true` reescribe en UN SaveChanges (transaccion unica) todo lo que es inequivoco y del formulario:
    campo, CalcExpression `{x}`, VisibleWhenJson de campos y secciones, AutofillMapJson (solo valores),
    FilterJson (solo cascade.fromField), OptionsJson (rollup/grid/targetField/target_grid/lienzo/sources,
    `{#x}`, `campo.x`; nunca ids de columna), propiedades del formulario (consecutivo, codigo automatico,
    columnas y filtros del modulo, escalon de estados, KPIs, regla de cierre, CSS `.field-x`), overrides de
    reglas de los vinculos, `Rule.ParamsJson` solo si la regla no la usa nadie mas, claves de
    `FormResponse.Data`, `FormRecordLink.ParentFieldCode`, `FormSignatureToken.FieldCode` y columnas
    `form:{defId}:x` de tableros.
  - Lo que vive fuera y puede servir a varios formularios (plantillas `{{campo.x}}`, `{form.x}` en avisos de
    flujo/agentes/documentos, reportes `form:{code}`, configuraciones de conciliacion/RFQ, conversiones de
    otros formularios hacia este, reglas compartidas) se devuelve como ADVERTENCIA para revision manual.
  - `UpdateQuestionAsync` usa el mismo nucleo cuando el FieldCode cambia (el MCP queda cubierto).
  - No se reescriben historicos inmutables (`FormResponseAudit`, `FormBuilderSnapshot`).

## Opciones consideradas

- **Guardar "no editable" dentro de FieldVisibilityJson** (`readonlyAll`): sin migracion, pero mezcla un
  comportamiento con permisos por rol y el editor de roles reescribia el JSON perdiendo la bandera.
  Descartada por una columna explicita.
- **Renombrar solo si no hay respuestas**: mas simple, pero el usuario eligio la cascada.
- **Reemplazo de texto global** del codigo viejo: descartado; ids de columna, claves de fuentes y textos
  libres coinciden por casualidad (inventario de 2026-11-26).

## Consecuencias

- Migracion dual aditiva `AddFormQuestionReadOnlyStyle` (bool con default + json nullable): el codigo viejo
  sigue funcionando contra la BD migrada.
- Al renombrar, el usuario ve antes que se cambia y que debe revisar a mano.
- Fix de paso: el `ToRequest` del disenador no arrastraba `VisibleWhenJson`, asi que cualquier cambio de un
  campo borraba su "Mostrar solo si".
- El MCP de formularios acepta `is_read_only` y `style_json` en add/update_question.
