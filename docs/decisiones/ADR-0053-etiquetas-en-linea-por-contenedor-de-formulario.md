# ADR-0053: Etiquetas en linea (label al frente del valor), configurable por contenedor

- Estado: Aceptado
- Fecha: 2026-08-01

## Contexto

En los formularios dinamicos (`DynamicFormRenderer`) cada campo pinta su etiqueta ARRIBA del
control (`.dfr-label { margin-bottom }`), un campo por bloque vertical. Es correcto para captura
normal, pero deja bloques muy altos cuando se apilan muchos campos cortos, tipo el bloque
"Totales" de un cotizador (Subtotal, IVA, Descuento, Total): con el label arriba cada linea ocupa
dos renglones y el bloque queda largo y disperso.

El usuario pidio poder compactar ESOS bloques poniendo el label al frente del valor (misma linea:
label a la izquierda con ancho fijo alineado a la derecha, el control llenando el resto). Requisito
no negociable: que sea REPLICABLE POR EL USUARIO sin codigo, en CUALQUIER contenedor de CUALQUIER
formulario, en la misma filosofia config-driven del resto (contenedores con `Style`, ancho de
tarjeta ADR-0047, etc.). No debia hardcodearse a un formulario ni contenedor concreto.

## Decision

Se agrega una propiedad booleana a nivel de contenedor, `FormContainer.InlineLabels`
(columna `inline_labels`, default `false`):

- **Config-driven, por contenedor**: es un toggle "Etiquetas en linea" en el panel de propiedades
  del disenador (`FormDesigner`), junto a los toggles existentes "Fijo en el layout" y "Oculto".
  Solo se muestra para grupos `Row`/`Col` (los contenedores transparentes que agrupan campos). El
  usuario lo activa/desactiva en cualquier contenedor y se persiste con el resto de props del
  contenedor (`SaveFormContainerRequest`).
- **Default = comportamiento actual** (label arriba): aditivo, no altera ningun formulario
  existente.
- **Render**: cuando `InlineLabels == true`, el renderer emite la clase `dfr-inline` en el
  `div.dfr-group` de ESE contenedor (junto a `dfr-row`/`dfr-col`). El CSS (`::deep`, porque
  `.col-*`/`.form-control` son de Bootstrap, fuera del scope del componente) pone el wrapper de
  cada campo en flex: label fijo ~150px a la derecha + control llenando el resto. En movil
  (<=640px) cae de nuevo a label-arriba. Caption/ayuda/error caen a la linea siguiente
  (`flex-wrap`), sin romper el par label+control.
- **Migracion dual** `AddFormContainerInlineLabels` en los dos proveedores (PostgreSQL `boolean` y
  SQL Server `bit`), `defaultValue = false`, sin indices, siguiendo el patron de `AddFormCardLayout`
  (ADR-0047). La columna `bool` mapea por convencion (como `IsLocked`/`IsHidden`), sin config EF
  explicita.

### Alternativas consideradas

- **Meterlo dentro del campo `Style` del contenedor** (CSS libre): mas flexible, pero es una caja
  de texto de CSS crudo, no un control que el usuario de negocio active con un clic. Un toggle es
  mas descubrible y consistente con "Fijo"/"Oculto".
- **Propiedad por CAMPO en vez de por contenedor**: mas granular, pero el caso real es "todo este
  bloque en linea"; una prop por contenedor lo resuelve con un solo toggle y menos ruido.
- **Un enum de layout (arriba/en-linea/...)** en vez de un bool: hoy solo hay dos modos; un bool es
  suficiente y mas simple. Si aparecen mas modos se promueve a enum sin romper datos.

## Consecuencias

- Un contenedor nuevo o existente queda en label-arriba sin cambios visibles.
- Un bloque tipo "Totales" se pone en "Etiquetas en linea" desde el disenador, sin tocar codigo, y
  se compacta en `/f`, `/m`, la vista previa y los usos embebidos del renderer.
- Aplica a grupos `Row`/`Col`. Los segmentos/secciones con marco no exponen el toggle (su layout de
  campos es la grilla de 12 normal).
