# ADR-0080: Control de formulario Canvas (Croquis / Dibujo)

- Estado: Aceptado
- Fecha: 2026-08-26
- Contexto: formularios dinamicos (ADR-0015), captura Tier 2 por canvas+interop (Signature, form-capture.js),
  formato FT-C-008 "Orden de Trabajo" que en papel tiene una parrilla milimetrada para bosquejar la pieza.

## Contexto

La OT (FT-C-008) tiene, bajo la tabla de items, una gran parrilla cuadriculada donde el dibujante bosqueja la
pieza. En el formulario digital se reemplaza esa zona por un LIENZO DE DIBUJO editable: rectangulos, elipses,
texto, imagenes y trazo libre sobre una cuadricula clara (papel milimetrado). El dibujo se guarda con el
registro y se imprime en la plantilla de la OT.

## Decision

1. **Nuevo `FormControlType.Canvas`** (AL FINAL del enum, preserva ordinales). Etiqueta UI "Croquis / Dibujo".
   En el validador se trata como `IsPlaceholderCapture` (como Signature: captura un valor, no bloquea submit).

2. **Motor JS propio `form-canvas.js`** (`window.ecorexFormCanvas`), estilo `form-capture.js` (global, sin ES
   modules, sin frameworks). Editor sobre un `<svg>` nativo con escena como fuente de verdad. Herramientas:
   seleccionar/mover (+ redimensionar rect/elipse/imagen), rectangulo, elipse, texto, lapiz libre, imagen,
   color, deshacer/rehacer, borrar seleccion, limpiar. Fondo: cuadricula milimetrada TENUE (patron SVG).
   Espacio logico FIJO (viewBox 1000 x alto) con escala uniforme -> cuadricula cuadrada y recarga fiel.

3. **Persistencia**: el dibujo se guarda como **SVG autocontenido** (rect/ellipse/text/path/image con imagenes
   embebidas como data-URL, y la cuadricula tenue incluida) en `FormResponse.Data` por field_code, igual que
   Signature (`FormFieldValue{ Value = <svg...>, Type = "Canvas" }`). El MISMO SVG sirve para editar (se
   recarga en el editor) y para imprimir. Se lee/escribe por `_values[field_code]` (SetValue), init idempotente
   en OnAfterRenderAsync que carga el SVG previo. Boton "Guardar dibujo" vuelca el SVG al campo (como "Guardar
   firma"). Limites: SVG total <= 2 MB; imagen insertada <= 1.5 MB (avisa si se excede).

4. **Impresion**: ambos caminos emiten el dibujo sin escapar.
   - Plantilla del usuario (`FormTemplateRenderService`): `{{campo.codigo}}` ahora pasa por `EmitField`: si el
     valor empieza con `<svg` se emite inline; si empieza con `data:image` se emite como `<img>`; el resto se
     escapa normal. Chromium (PuppeteerSharp) renderiza el SVG inline en el PDF.
   - Impresion directa (`FormPrint.razor`): nuevo `case Canvas` que emite el SVG via `MarkupString`.

5. **Disenador**: el control aparece en el combobox de tipo ("Croquis / Dibujo") y en `ControlReg`
   (icono/etiqueta). Propiedades en la pestana Datos: alto del lienzo, tamano de celda de la cuadricula,
   mostrar/ocultar cuadricula. Se guardan como OBJETO JSON en `OptionsJson` (`{height,cell,grid}`), sin columna
   ni migracion nuevas (ya lo round-trippea ToRequest). `DefaultOptions(Canvas)` siembra la config por defecto.

## Consecuencias

- UI del producto 100% Blazor: solo un modulo JS propio + interop (sin npm/React/Vue/Vite). Multi-tenant
  intacto (el dibujo vive en FormResponse.Data del tenant). SVG autocontenido = editable, imprimible nitido y
  portable, sin almacenamiento binario aparte.
- El "alto" es proporcional (viewBox fijo, escala uniforme), no px exactos, para mantener la cuadricula
  cuadrada y la recarga fiel a cualquier ancho de tarjeta.

## Evolucion (v0.15.91 / v0.15.93)

- v0.15.91: ROTACION de objetos/imagenes. Cada figura gana un angulo `rot`; manija de giro sobre la
  seleccion (Shift = pasos de 15 grados); se persiste como `transform="rotate(deg cx cy)"` en el SVG del
  objeto (imprime igual, sin tocar la impresion). Redimension estando rotado = simetrica desde el centro.
- v0.15.93: CANVAS MULTIPAGINA. El editor agrega/elimina paginas de dibujo (barra "Pagina X de N" +
  Anterior/Siguiente/+Agregar/Eliminar); cada pagina es su propio lienzo (misma cuadricula/opts).
  - Guardado (compatible hacia atras): el valor pasa a ser el sobre JSON `{"v":1,"pages":["<svg>",...]}`
    (Type sigue = "Canvas"). Al LEER: si (trim) empieza con `<svg` -> 1 pagina (legacy); si empieza con `{`
    y trae `pages:[]` -> multipagina. Los registros viejos (SVG suelto) siguen sirviendo.
  - Impresion: helper compartido `FormCanvasHtml.Render(value)` (Application) expande CADA pagina a una HOJA
    imprimible propia: `<div class="dfr-canvas-page" style="break-before:always">` + el SVG + un pie
    `<div class="dfr-canvas-pageno">Pagina X de N</div>` (contador solo si hay >1 pagina). Lo usan la ruta de
    plantilla (`FormTemplateRenderService.EmitField`) y la directa (`FormPrint.razor`). Marcador sin cambios:
    `{{campo.croquis_pieza}}` ahora rinde N hojas.
  - Prop nueva `maxPages` en options_json (default 20, clamp 1..50) + input en el disenador. Limite de tamano
    del valor completo: 2 MB (varias paginas con imagenes embebidas -> avisa si se excede).

## Cabezote e identificacion por hoja (v0.15.94)

Cada hoja de croquis puede repetir un CABEZOTE de proyecto y un numeral configurable en la impresion:
- `FormCanvasHtml.Render(value, pageHeaderHtml = null, counterLabel = "Grafico")`: si pageHeaderHtml no
  esta vacio, se emite `<div class="dfr-canvas-hd">{header}</div>` ARRIBA del SVG de cada hoja; el pie pasa a
  `"{counterLabel} X de N"` (solo si hay >1 pagina). Sin header -> como antes; legacy 1-SVG intacto.
- Config por campo en options_json: `printHeader` (HTML con tokens {{campo.codigo}}) y `printCounterLabel`
  (default "Grafico"). En la ruta de plantilla (FormTemplateRenderService) se lee el printHeader del campo
  Canvas, se resuelven sus {{campo.x}} contra el registro (valor formateado y ESCAPADO; el HTML del cabezote
  NO se escapa) y se pasa como pageHeaderHtml. `PatchCanvasCfgAsync` del disenador ahora hace merge sobre el
  options_json (preserva printHeader/printCounterLabel al editar height/cell/grid/maxPages).
- Estilos inline (`.dfr-canvas-hd`: font 11px, borde inferior fino). Se pueden override por clase en la plantilla.

## Fix impresion de imagenes (v0.15.98)

Las IMAGENES no aparecian al imprimir (las figuras rect/elipse/texto/path si). Causa raiz: en
`buildPageSvg` el `<svg>` raiz NO declara `xmlns:xlink`, y el `<image>` fijaba el href con
`setAttributeNS(xlink,'href',...)` SIN prefijo. El `XMLSerializer` del editor lo serializaba como
`ns1:href="..."` (prefijo inventado). Al reinyectar ese SVG como texto en el HTML de impresion, el
PARSER HTML solo reconoce la forma literal `xlink:href` en contenido foraneo (SVG); `ns1:href` queda
como atributo inerte y la imagen se quedaba sin href -> no cargaba. Las figuras no usan href, por eso
imprimian.

- JS (`form-canvas.js` shapeToNode): el `<image>` ahora usa **href PLANO (SVG2)** unicamente
  (`setAttribute('href', ...)`), que sobrevive serializar->reinyectar en HTML y Chromium (edicion e
  impresion Puppeteer) rinde. Cache-bust `?v=4`. La lectura (`parseSvgToScene`) ya leia `href` primero.
- Servidor (`FormCanvasHtml.ExtractPages`): normaliza cada pagina reescribiendo `xlink:href`/`ns\d+:href`
  de imagen a `href` plano (regex acotada; base64 no lleva comillas). Asi los dibujos GUARDADOS antes del
  fix (formato roto) tambien imprimen, sin re-guardar.
- Formato guardado: los dibujos NUEVOS guardan `href` plano en el `<image>` del SVG (el sobre
  `{"v":1,"pages":[...]}` no cambia). Los viejos se normalizan al imprimir.

## Pendientes / supuestos

- El SVG se persiste al pulsar "Guardar dibujo" (igual que Signature "Guardar firma"); no hay flush automatico
  en submit. Se puede agregar en una ola siguiente (leer getSvg de cada Canvas antes de enviar).
- Redimension solo por la esquina inf-der (rect/elipse/imagen); texto/lapiz solo mover. Suficiente para bosquejo.

## Herramienta LINEA recta (v0.15.102)

Nueva herramienta 'line' en la barra ("Linea"): click-arrastrar traza un segmento recto. Con **Shift**
el angulo se ajusta a multiplos de 45 grados (horizontal / vertical / diagonales), conservando la
longitud; sin Shift es libre. Permite armar triangulos/poligonos con varios segmentos.

- **Representacion**: la linea se guarda como un `path` de 2 puntos (`<path d="M x1 y1 L x2 y2">`), NO
  como `<line>`. Asi reusa TODO el pipeline existente de `path`: shapeToNode, boundsOf, moveShape,
  rotacion por manija (rotatable ya incluye 'path'), parse (dToPts -> pts) y export (buildPageSvg).
  Imprime igual que el lapiz. Sin cambios en export/import ni en la impresion.
- Es SELECCIONABLE / movible / rotable / borrable como las demas figuras; respeta el color de trazo
  actual (setColor). La herramienta 'line' PERMANECE activa tras cada trazo (como 'pen') para encadenar
  segmentos (p.ej. un triangulo con 3 lineas). Cache-bust `?v=5`.
- Polilinea cerrada de un trazo (click por vertice): NO implementada (queda como pendiente opcional);
  el triangulo/poligono se arma con lineas sueltas, que ya cumple la aceptacion.

## Omitir cabezote en la 1a hoja (v0.15.103)

Opcion `printHeaderSkipFirst` (bool, default false) en el options_json del campo Canvas: cuando la
PAGINA 1 de la plantilla ya trae el membrete del documento, el cabezote (printHeader) lo duplicaba.
Con la opcion activa, `FormCanvasHtml.Render(..., skipHeaderOnFirst)` NO emite el
`<div class="dfr-canvas-hd">` en la hoja i==0; las hojas 2+ lo llevan igual. El contador
"Grafico X de N" no cambia. `FormTemplateRenderService.ParseCanvasPrint` lee la clave (true, o "true"
string) y la pasa a Render. Sin la opcion -> cabezote en todas (como antes); legacy 1-SVG intacto.

## Marcador de codigo de barras en la plantilla (v0.15.104)

Nuevo marcador de plantilla `{{barcode:...}}` (motor de plantillas, no exclusivo del Canvas pero
pensado para la OT): genera un codigo de barras LINEAL escaneable server-side, sin libs ni JS.
- Sintaxis: `{{barcode:numero}}` (codifica el RecordNumber) y `{{barcode:campo.<codigo>}}` (codifica el
  valor de un campo). Altura opcional en px: `{{barcode:numero:48}}` (default 44, clamp 16..200).
- Simbologia **Code39** (charset A-Z 0-9 espacio - . $ / + %; start/stop '*'; sin digito de control).
  `Barcode.Code39Svg(data, height)` emite un SVG autocontenido (barras negras sobre blanco + valor
  legible debajo), inline SIN escapar (Chromium lo rinde). Caracteres no soportados se descartan.
- Se resuelve en AMBOS lugares donde se resuelven los {{campo.x}}: el merge principal
  (`FormTemplateMerge.Render` -> `ResolveBarcodes`) Y el cabezote por hoja del Canvas
  (`ResolveHeaderTokens`, que ahora recibe `numero`), para que la OT lo repita en cada pagina.
- No cambia el guardado de datos. Verificado por decode-back (el SVG decodifica a `*OT-000001*`).

## Autoguardado del croquis (v0.15.105)

Resuelve el pendiente "no hay flush automatico en submit": el renderer ahora vuelca el SVG de CADA
croquis MONTADO a su campo (`FlushCanvasesAsync`) ANTES de construir el documento, tanto en el
AUTOGUARDADO (cada 30 s) como en el ENVIO y en "Listo" del wizard (via SaveAsync/FlushDraftAsync). Ya
no hace falta pulsar "Guardar dibujo" para que el dibujo quede guardado. No borra: si getSvg viene
vacio (editor no montado) se omite; marca dirty SOLO si el dibujo cambio (no dispara autoguardados en
vano). El boton "Guardar dibujo" se conserva (guardado manual explicito).

## Pie por hoja + nombre completo (v0.15.106)

- PIE por hoja: nueva opcion `printFooter` (HTML con {{campo.x}}) en el options_json del campo Canvas,
  analoga a `printHeader`. `FormCanvasHtml.Render(value, pageHeaderHtml, counterLabel, skipHeaderOnFirst,
  pageFooterHtml)` la emite al FONDO de CADA hoja (incluida la 1a) en una fila `div.dfr-canvas-ft`
  (flex: pie `div.dfr-canvas-emit` a la izquierda, contador `div.dfr-canvas-pageno` a la derecha).
  ParseCanvasPrint lee `printFooter`, resuelve sus {{campo.x}} (ResolveHeaderTokens) y lo pasa. Vacio ->
  sin pie (compat; el contador se conserva). Aplica a todas las hojas.
- NOMBRE COMPLETO: el default dinamico CurrentUser ("emitido por") ya NO usa solo user.Identity.Name
  (que puede ser login/email o un claim viejo). El renderer resuelve el DisplayName FRESCO del PlatformUser
  via ITenantUserService.ResolveDisplayNameAsync(platformUserId) (dentro del gate del circuito), con
  fallback a Identity.Name. Asi emitido_por = "Alexander Cuartas".

## Marcadores de fecha/hora en la plantilla (v0.15.107)

Dos marcadores nuevos del motor de plantillas (hora local, formato "dd/MM/yyyy HH:mm"), resueltos en el
merge principal Y en el cabezote/pie del Canvas (ResolveHeaderTokens), igual que {{fecha}}/{{barcode}}:
- `{{fechahora}}`: fecha y hora del REGISTRO (TransactionDate ?? SubmittedAt ?? CreatedAt, ya en local).
  Gemelo con hora de {{fecha}}.
- `{{impreso}}`: fecha y hora ACTUAL de impresion (DateTimeOffset.Now del render).
Helper compartido `ResolveDateTokens(html, fecha)` (fecha/fechahora/impreso). Texto plano (no markup).

## Extension v0.15.121: paleta de FORMAS predefinidas con COTAS clicables

El dibujante puede insertar PIEZAS TIPICAS de lamina en vez de dibujar desde cero. Todo es cliente
(form-canvas.js), sin cambiar el formato de guardado (Type=Canvas, SVG multipagina).

- LIBRERIA de formas parametricas: `var SHAPES` (mapa kind -> {label, w, h, geom(w,h)->{parts, cotas}}) +
  `SHAPE_ORDER`. Agregar una forma = una entrada mas; la paleta, el dibujo, las cotas, el export/import y la
  edicion son genericos. Set inicial: lamina, brida cuadrada/circular, angulo, canal U/CE/omega, bandeja
  sencilla/doble, cilindro, perfil rolado, cono.
- Nuevo tipo de shape `group`: se serializa como `<g data-ecx-kind data-ecx-dims data-ecx-w data-ecx-h
  transform="translate() rotate()">` con el contorno + por cada cota un punto AMARILLO clicable
  (`data-ecx-cota`) y su etiqueta. Reusa el pipeline existente (seleccion/mover/rotar/escalar).
- COTAS: un clic en el punto amarillo abre un input inline; el valor se guarda en `sh.dims[key]` y la
  etiqueta muestra "label: valor". Marcador elegido: punto amarillo (alto contraste, claramente "editable").
- Round-trip: `parseSvgToScene` itera los hijos DIRECTOS del contenedor `[data-ecx-shapes]`; un
  `<g data-ecx-kind>` reconstruye UN `group` (sin re-parsear sus primitivas hijas). Compatible con croquis
  viejos (sin grupos). El grupo se guarda e imprime como cualquier dibujo (1 pagina y multipagina).
- UI: boton "Formas" en la barra del croquis -> `ecorexFormCanvas.togglePalette(id)` (panel de miniaturas
  `miniSvg`, misma libreria) -> `addShape(id, kind)`. Cache-bust form-canvas.js?v=6.
