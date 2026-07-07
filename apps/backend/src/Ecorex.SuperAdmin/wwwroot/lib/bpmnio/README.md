# bpmn-js (vendored, self-hosted)

Assets del editor de flujos BPMN (modulo 000291, pagina `/flujos`). ADR-0034
reemplaza el canvas propio (ADR-0022) por bpmn-js embebido via JS interop.

## Contenido

| Archivo             | Que es                                                              | Version  |
|---------------------|--------------------------------------------------------------------|----------|
| `bpmn-modeler.js`   | Bundle UMD del modeler de bpmn-js (expone `window.BpmnJS`)          | 8.8.2    |
| `bpmn.css`          | Iconografia y estilos propios de BPMN (`bpmn-icon-*`, formas)       | 8.8.2    |
| `diagram-js.css`    | Estilos base de diagram-js (canvas, paleta, context pad)            | 8.8.2    |

Copiados TAL CUAL del proyecto legacy GestionMovil
(`C:\Desarrollo\core\Bootstrap\Frontend4\...\plugins\bpmnio`). NO se descargo
nada de internet.

## Nota de licencia

bpmn-js es software libre de bpmn.io (camunda Services GmbH), publicado bajo la
**licencia MIT** con la clausula "powered by bpmn.io" (el watermark del canvas no
debe removerse). Ver https://bpmn.io/license para el texto completo.

    Copyright (c) 2014-present, camunda Services GmbH
    Licensed under the MIT License. Uses bpmn.io.

## Limitaciones conocidas (deudas, ver ADR-0034)

- **Version antigua (8.8.2)**: la linea vigente de bpmn-js es la 17+. Actualizar
  es deuda tecnica pendiente (revisar breaking changes de la API del modeler).
- **Fuente de iconos `bpmn`**: el legacy NO trae el webfont `bpmn.*` que usan las
  clases `bpmn-icon-*`. Por eso la paleta ACOTADA de ECOREX (`ecorex-bpmn.js`) no
  usa esas clases: define sus entradas con `imageUrl` (SVG inline como data-URI),
  y NO depende del webfont. El `@font-face` de `bpmn.css` queda inerte (sin
  archivos de fuente) y es inofensivo.
- **Modo oscuro**: bpmn-js no conmuta a dark por si solo. El canvas se deja con
  fondo claro aunque `html.dark` este activo (el resto del shell de ECOREX si
  respeta el tema). Documentado como limitacion.
