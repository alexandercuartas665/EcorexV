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
