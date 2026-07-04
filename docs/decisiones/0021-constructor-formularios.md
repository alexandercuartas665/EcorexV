# ADR-0021: Constructor de formularios fiel al prototipo (pantalla forms)

- Estado: aceptada
- Fecha: 2026-07-04
- Relacionada con: ADR-0015 (dynamic forms), ADR-0016 (rules engine), ADR-0019 (E2E)

## Contexto

El modulo Formularios (000131) tenia motor funcional (ADR-0015) pero una UI basica de
grillas y modales. El prototipo maestro (ECOREX.dc.html, pantalla 'forms' + estado
formBuilderOpen) define un INDICE con KPIs y vistas tarjetas/lista, y un CONSTRUCTOR de
3 columnas (paleta + estructura / lienzo con presets de dispositivo / propiedades con
tabs Diseno-Datos-Reglas). Esta ola lleva la UI a fidelidad nanometrica del fuente y
completa la funcionalidad end-to-end (crear -> disenar -> activar -> llenar -> publicar
por token) con cambios ADITIVOS minimos en el modelo y UNA migracion dual
(`AddFormBuilderFields`).

## Decisiones

### 1. Mapeo de tipos prototipo -> enums existentes

Contenedores (`FormContainerType`, se persiste como string: agregar valores es seguro):

| Prototipo | Enum          | Nota                                                     |
|-----------|---------------|----------------------------------------------------------|
| row       | Row (nuevo)   | hijos en grilla de 12 columnas                            |
| col       | Col (nuevo)   | hijos apilados a lo ancho                                 |
| section   | Section (nuevo) | seccion con titulo; Segment legacy se renderiza IGUAL   |
| tabs      | Tabs (nuevo)  | nombres de pestanas en `TabsJson` (arreglo JSON)          |
| modal     | Modal (nuevo) | el renderer lo pinta como seccion normal (TODO: dialogo real en ola posterior) |

Controles (`FormControlType`):

| Prototipo | Enum               | Prototipo | Enum                |
|-----------|--------------------|-----------|---------------------|
| texto     | Text               | tabla     | GridDetail (FUNCIONAL) |
| area      | TextArea           | firma     | Signature (placeholder) |
| lista     | Select             | foto      | Photo (placeholder) |
| fecha     | Date               | gps       | Gps (placeholder)   |
| numerico  | Number             | archivo   | File (NUEVO, placeholder) |
| sino      | Toggle             | barras    | Barcode (NUEVO, placeholder) |
| parrafo   | Paragraph (NUEVO)  | divisor   | Divider (NUEVO)     |
| espacio   | Spacer (NUEVO)     |           |                     |

Los tipos legacy (Heading, Literal, Radio, MultiCheck, Image, Audio, Button, Chart,
Html) se conservan editables en el constructor aunque no tienen tarjeta en la paleta.

### 2. Campos nuevos (aditivos) y sincronizacion Width/GridCol

- `FormQuestion`: `Width` (1..12, default 12), `PlaceholderText` (200), `DefaultValue`
  (2000), `IsLocked`, `IsHidden`.
- `FormContainer`: `TabsJson` (jsonb/nvarchar), `Width`, `IsLocked`, `IsHidden`.
- **Width es la fuente de verdad del layout** del constructor; `GridCol` queda
  SINCRONIZADO (`col-12` / `col-md-N`) por el servicio para no romper el renderer
  bootstrap ni los selectores E2E (`[class*='col-']`). Compatibilidad: si un caller
  viejo manda Width=12 (default) con un GridCol parseable, Width se deriva del GridCol.
  La migracion hace backfill de `width` desde `grid_col` en ambos motores.

### 3. Paragraph / Spacer usan DefaultValue (doble uso documentado)

- `Paragraph`: el TEXTO del parrafo vive en `DefaultValue` (fallback: Label). No se
  agrego una columna `Text` dedicada porque los elementos de documento no capturan
  datos y `DefaultValue` estaba libre en ellos por construccion (IsNonInput).
- `Spacer`: el alto en px vive en `DefaultValue` (default 24).
- Ambos (y `Divider`) son `IsNonInput`: nunca validan ni persisten valor.

### 4. Multimedia sin captura real = placeholder que NO bloquea

`Signature/Photo/Image/Audio/Gps/File/Barcode` son `IsPlaceholderCapture`:
- El renderer pinta el placeholder visual del prototipo ("Firma aqui", "Capturar",
  "Ubicacion", "Subir archivo", "Escanear") deshabilitado con la nota
  "captura disponible proximamente".
- La validacion (cliente Y servidor, mismo FormFieldValidator) IGNORA `Required` en
  estos tipos: un requerido imposible de llenar bloquearia todo submit. Ademas el
  servicio apaga `Required` al guardar la pregunta. Si llega valor externo (import),
  se acepta tal cual.

### 5. Tabla (GridDetail) FUNCIONAL

- Columnas en `OptionsJson` con el mismo shape `[{id,label}]` (reuso del campo, sin
  columna nueva); el servicio exige al menos una columna al guardar/activar.
- El valor del campo es un arreglo JSON de filas `[{colId:"valor"}]` que persiste
  dentro del documento de la respuesta. El renderer captura filas dinamicas
  (agregar/quitar fila, una celda de texto por columna).
- `Required` = al menos una fila.

### 6. Ocultos y bloqueados del disenador

- `IsHidden` (pregunta o contenedor): no se pinta en el renderer y la validacion del
  submit lo salta (servidor incluido). El arbol del constructor lo muestra atenuado.
- `IsLocked`: el constructor bloquea reordenar/arrastrar ese nodo (los servicios no lo
  imponen: es una marca de UX del disenador, igual que el prototipo).

### 7. Persistencia del constructor: por cambio (no batch)

Cada mutacion del constructor llama al servicio real y recarga la definicion (fuente de
verdad servidor, latencia LAN de Blazor Server aceptable). El boton "Guardar" del
header confirma refrescando y muestra "Guardado". Se descarto el batch en memoria por
complejidad de reconciliacion y riesgo de perdida ante cierre del circuito.

- Drag and drop nativo: paleta -> lienzo/contenedor (agrega) y nodo -> posicion
  (`MoveQuestionToAsync` / `MoveContainerToAsync`, nuevos en IFormDefinitionService,
  renumeran ambos grupos de hermanos y validan ciclos).
- Dentro de un contenedor el orden visual es: preguntas primero, luego
  sub-contenedores (el modelo no intercala tipos distintos en una sola secuencia:
  SortOrder es por grupo). Deuda menor documentada.
- Borrar un contenedor conserva la semantica de la ola 2: sus hijos SUBEN al padre
  (el prototipo borra el subarbol; se prefirio no cambiar el contrato del servicio).

### 8. Tab Reglas contra el catalogo real

`IRuleDocumentService.ListQuestionLinksAsync(questionId)` (nuevo) lista los
FormFieldRule de la pregunta con verbo y documento; "+ Agregar regla" vincula via
`LinkToQuestionAsync` (documento -> regla del tenant) y la X desvincula. El disparador
mostrado es "Al cambiar" (unico trigger de reglas de campo, ADR-0016).

### 9. Indice y estados

- KPIs reales: `FormDefinitionListItemDto` agrega `ResponseCount` y `RuleCount`.
- Estados: Active=Publicado (verde), Draft=Borrador (ambar), IsArchived=Archivado
  (gris); Inactive usa gris con rotulo "Inactivo" (no existe en el prototipo).
- El modelo no tiene Categoria: el badge de categoria muestra "General" (deuda).
- "Nuevo formulario" crea `FRM-###` (siguiente sufijo numerico) y abre el constructor.

## Consecuencias

- Migracion dual `AddFormBuilderFields` (PG 5442 + MSSQL 1443) con backfill de width.
- El renderer y el visor publico /f/{token} heredan contenedores nuevos, documento,
  placeholders multimedia y tabla funcional sin cambios de contrato.
- Suite: unit (validador ampliado), integracion dual (round-trip Width/TabsJson,
  tabla submit, ocultos), E2E nuevo `FormBuilderTests` (crear -> disenar -> activar ->
  llenar en vista previa -> submit) + selectores del indice actualizados en
  `PublicFormTokenTests`.

## Deudas

- Modal como dialogo real en el renderer (hoy seccion normal).
- Captura real de firma/foto/gps/archivo/barras (hoy placeholder).
- Campo Categoria en FormDefinition (badge fijo "General" y sin tabs de categoria).
- Intercalado libre de preguntas y contenedores en una sola secuencia de orden.
- Celdas tipadas por columna en la tabla (hoy texto).
