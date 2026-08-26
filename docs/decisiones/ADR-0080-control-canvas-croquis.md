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

## Pendientes / supuestos

- El SVG se persiste al pulsar "Guardar dibujo" (igual que Signature "Guardar firma"); no hay flush automatico
  en submit. Se puede agregar en una ola siguiente (leer getSvg de cada Canvas antes de enviar).
- Redimension solo por la esquina inf-der (rect/elipse/imagen); texto/lapiz solo mover. Suficiente para bosquejo.
