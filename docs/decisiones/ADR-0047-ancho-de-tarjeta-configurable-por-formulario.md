# ADR-0047: Ancho de la tarjeta del formulario, configurable por formulario

- Estado: Aceptado
- Fecha: 2026-07-25

## Contexto

Los formularios dinamicos se llenan en una tarjeta centrada de ancho fijo (~720px) en tres
superficies que comparten `DynamicFormRenderer`: la pagina publica por token (`/f/{token}`), la
bandeja del formulario-modulo (`/m/{code}`, modal "Nuevo registro") y la vista previa del
disenador. Ese ancho es correcto para un formulario de captura normal, pero se queda corto para
formularios con tablas anchas (GridDetail): el cotizador de AGROMETALICAS (form COT) tiene una
tabla de 25 columnas y en una tarjeta de 720px obliga a mucho scroll horizontal.

El fix responsive previo (scroll horizontal aislado, primera columna/cabecera sticky, panel de
lookup en `position:fixed`) hizo la tabla usable dentro de la tarjeta angosta, pero el usuario
pidio poder ver el formulario mas ancho. Descarto explicitamente rotar a apaisado (impresion o
layout): solo quiere una tarjeta mas ancha, y que sea configurable POR FORMULARIO (no global ni
hardcodeado), porque conviven cotizadores anchos con formularios de contacto normales.

## Decision

Se agrega un preset de ancho de tarjeta por formulario, `FormDefinition.CardLayout`
(enum `FormCardLayout`, guardado como texto):

- `Normal`   ~720px  (comportamiento actual; DEFAULT para no alterar los formularios existentes)
- `Ancho`    ~1160px (min(1160px, 94vw))
- `Completo` casi toda la ventana (min(1560px, 96vw))

Detalles de implementacion:

- **Migracion dual** `AddFormCardLayout` en los dos proveedores (PostgreSQL y SQL Server), con
  `defaultValue = "Normal"`: aditiva, no toca los formularios existentes.
- Se configura en "Propiedades del formulario" del disenador (un `<select>` Normal/Ancho/Completo)
  y se persiste con el resto de props del panel (`SetTransactionalAsync`).
- El ancho lo aplica el propio `DynamicFormRenderer` a su raiz (`.dfr-root`) mediante una clase
  (`dfr-cw-normal|ancho|completo`), pero SOLO cuando el host lo pide con el parametro
  `ApplyCardWidth`. Lo activan las tres superficies de llenado; los usos EMBEBIDOS del renderer
  (ficha de tercero, wizard de tarea) NO lo activan, asi su ancho lo sigue mandando el modal que
  los aloja. Las tarjetas de esos tres hosts pasan a `width: fit-content` para ceñirse al ancho
  que fija el renderer.
- Los anchos usan unidades de viewport (`vw`) para no depender del ancho del padre (que es
  `fit-content`), evitando dependencias circulares de layout.

### Alternativas consideradas

- **Ancho libre en px (`card_max_width int?`)** en vez de presets: mas flexible, pero mas dificil
  de exponer bien en el disenador y de mantener consistente. Los tres presets cubren el caso real
  (normal / cotizador ancho / pantalla completa) y son mas simples.
- **Rotar la impresion a apaisado** para tablas anchas: descartado por pedido explicito del
  usuario. La impresion (`FormPrint`, A4 vertical) mantiene el scroll/escala ya existente.
- **Widen global** de la tarjeta: descartado; convive cotizador ancho con formularios normales, el
  ancho es una propiedad del formulario.

## Consecuencias

- Un formulario nuevo o existente queda en `Normal` sin cambios visibles.
- Un cotizador se pone en `Ancho`/`Completo` desde el disenador, sin tocar codigo, y se ve mas
  ancho en `/f`, `/m` y la vista previa. La impresion no cambia de orientacion.
- El ancho POR COLUMNA de la tabla es un cambio ortogonal (dato en `options_json`, sin migracion):
  cada columna declara su `width`, o cae a un default por tipo.
