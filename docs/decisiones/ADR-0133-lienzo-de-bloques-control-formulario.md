# ADR-0133: Control de formulario "Lienzo de bloques" (preset Business Model Canvas)

Fecha: 2026-10-26
Estado: Aceptada

## Contexto

Se necesita capturar en un formulario una matriz de bloques con etiqueta + texto libre, al estilo de un Business
Model Canvas (BMC): varias celdas de tamanos distintos dispuestas en una grilla, cada una con su titulo y un area
de texto. El control existente `Canvas` (FormControlType.Canvas) NO sirve: es un editor de CROQUIS/DIBUJO que guarda
un SVG y esta pensado para reemplazar una parrilla milimetrada en papel. Aqui queremos texto estructurado y
REPORTABLE (consultable como cualquier campo), no un dibujo. Tambien se pidio que el bloque pueda nacer pre-llenado
a partir de otras respuestas del mismo formulario, y que todo sea editable desde el disenador (regla firme:
self-serve en FormDesigner).

## Decision

Nuevo tipo de control **`FormControlType.LienzoBloques`** (agregado al FINAL del enum para preservar ordinales):

1. **Generico, config-driven**: el autor define los bloques (id, etiqueta, posicion col/fila, colSpan/rowSpan,
   acento y `source` opcional) y el numero de columnas de la grilla. Nada hardcodeado. **BMC** es solo un PRESET de
   9 bloques (`LienzoBloquesConfig.Bmc()`, grilla de 10 columnas internas para un bottom 50/50 limpio).
2. **Captura valor (reportable)**: el valor del campo es un objeto JSON `{ blockId: texto }` guardado en
   `FormResponse.Data` como cualquier otro campo. NO es display-only. Se reusa el pipeline de valores del
   DynamicFormRenderer (`SetValue`), autosave incluido.
3. **Pre-llenado por `source`**: si un bloque declara `source` (field codes del mismo formulario), al cargar se
   concatena esas respuestas con salto de linea y se siembra el bloque (editable). Respeta lo ya escrito por el
   usuario y el modo bloqueado (`AutoComposeLienzos`).
4. **Config en `OptionsJson` raiz** `{preset, columns, blocks}` (`LienzoBloquesConfig.Parse/Serialize`), tolerante:
   sin config o invalida -> BMC por defecto; columnas fuera de 1..24 -> 10.
5. **Self-serve en FormDesigner**: tarjeta en la paleta, opcion en el selector de tipo, vista de diseno
   (mini-grilla con las etiquetas en su posicion) y editor de propiedades (boton "Preset BMC", columnas, y por
   bloque etiqueta/acento/col/fila/ancho/alto + mapeo de Origen; agregar/quitar bloques).

## Consecuencias

- **SIN migracion**: el tipo es codigo (enum) y la config/el valor son datos de formulario (OptionsJson / Data).
  `has-pending-model-changes` limpio en PG y SQL Server.
- Un formulario puede llevar un BMC (u otra matriz de bloques de texto) sin SQL y sin tocar el motor de render para
  cada caso: el mismo control sirve cualquier disposicion.
- Primer consumidor: CANVAS-EMP (tenant BITCODE). Queda convivir con el control `Canvas` (croquis), que es otra
  cosa; la doc del enum lo aclara para no confundirlos.
- Fuera de alcance (posible despues): exportar/imprimir el lienzo como imagen/PDF con su maquetacion.
