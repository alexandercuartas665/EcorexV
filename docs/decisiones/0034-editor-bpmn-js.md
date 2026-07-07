# ADR-0034: Editor de flujos con bpmn-js embebido (reemplaza el canvas propio)

- Estado: aceptada
- Fecha: 2026-07-07
- Reemplaza: la Decision #1 de ADR-0022 (canvas propio -> bpmn-js)
- Relacionada con: ADR-0014 (workflow engine BPMN), ADR-0015 (dynamic forms),
  ADR-0016 (rules engine), ADR-0019 (E2E), ADR-0022 (editor de flujos)

## Contexto

ADR-0022 construyo el EDITOR de flujos (modulo 000291, `/flujos`) con un canvas
SVG PROPIO en Blazor Server (pointer events, nodos absolutos, aristas ortogonales)
para replicar el prototipo. Funciona, pero reimplementa a mano el modelado BPMN
(arrastrar, conectar, layout, waypoints) que una libreria madura ya resuelve. El
vault (Vision Flujos D1) y el legacy GestionMovil ya usan **bpmn-js** (bpmn.io).
El usuario decidio migrar el EDITOR a bpmn-js, aceptando que la pantalla del editor
se desvie del canvas del prototipo (se envuelve bpmn-js en el shell y los tokens de
ECOREX). Solo el editor (modeler); el viewer de ejecucion es otra ola.

## Decisiones

### 1. bpmn-js v8.8.2 vendored del legacy, self-hosted (reemplaza #1 de ADR-0022)

Se vendorea el bundle UMD de bpmn-js del legacy (NO se descarga de internet) a
`Ecorex.SuperAdmin/wwwroot/lib/bpmnio/`: `bpmn-modeler.js` (expone `window.BpmnJS`),
`bpmn.css` y `diagram-js.css`. Se cargan en `Components/App.razor`. Licencia **MIT**
de bpmn.io (clausula "powered by bpmn.io"; el watermark del canvas no se remueve);
nota de licencia en el README junto a los assets. El canvas SVG propio del editor se
elimina; el `web-prototype` React sigue siendo referencia secundaria.

### 2. Palette ACOTADO al subconjunto que ejecuta el motor

Un `PaletteProvider` custom (patron `MiPaletteBootstrapProvider` del legacy) que
SOBREESCRIBE `paletteProvider` y expone SOLO: startEvent, endEvent, task,
exclusiveGateway + herramientas connect/hand/lasso. El catalogo completo de BPMN
(mensajes, subprocesos, pools...) que el motor NO ejecuta nunca se muestra. Los
iconos de la paleta son SVG inline (data-URI), no el webfont `bpmn-icon-*` (el legacy
no trae la fuente `bpmn.*`), asi la paleta no depende de assets ausentes.

### 3. Interop Blazor <-> bpmn-js (`wwwroot/js/ecorex-bpmn.js`)

Modulo ES importado on-demand desde el componente. Expone `init/exportXml/importXml/
zoomFit/destroy`. Suscribe `element.click` y `selection.changed` -> Blazor
(`OnElementSelected`) y `commandStack.changed` -> dirty (`OnGraphChanged`). Cero
`__doPostBack`; todo por `DotNetObjectReference`.

### 4. Editor-only; parametrizacion en tablas por BpmnElementId (NO en extensionElements)

La asignacion/formularios/reglas/condiciones por nodo siguen en tablas
(`WorkflowNodeForm`, `WorkflowNodeRule`, `WorkflowEdge.ConditionExpression`,
`RestartNodeId`/`AllowsAssignment`) emparejadas por `BpmnElementId`. El panel derecho
(6 acordeones + "Saltar a otro flujo") se CONSERVA intacto y opera sobre el ULTIMO
grafo GUARDADO; editar un formulario/regla/condicion NO pasa por bpmn-js.

### 5. Guardado: exportar XML -> resync in-place del grafo (`SaveBpmnAsync`)

Guardar exporta el XML de bpmn-js y lo pasa a `IWorkflowDesignService.SaveBpmnAsync`,
que hace `EnsureDraft` + **re-sincroniza** `workflow_node`/`workflow_edge` y el layout
(bpmndi) IN PLACE sobre el borrador (equivalente a Reparar/EliminarNodos del legacy):
empareja nodos por `BpmnElementId` (conserva config y vinculos de los que sobreviven),
agrega los nuevos, elimina los que desaparecieron (con sus aristas/vinculos) y guarda
el XML de bpmn-js TAL CUAL (portabilidad bpmn.io, ADR-0014). SOLO borradores; las
publicadas siguen inmutables y derivan su borrador. El `BpmnXmlWriter` deja de usarse
en el camino de EDICION (bpmn-js produce el XML); se CONSERVA (deprecacion parcial)
porque lo siguen usando el seeder, `CreateDraftAsync`, `EnsureDraftAsync` e
`ImportJsonAsync`. Se agrego `ImportBpmnAsync` (import de `.bpmn`/XML) que reemplaza a
`ImportJsonAsync` en el editor; el JSON del prototipo queda deprecado pero disponible.

### 6. Desviacion de fidelidad y modo oscuro (aprobado por el usuario)

El canvas ahora es el de bpmn-js con `bpmn.css`/`diagram-js.css`; el header, el panel
derecho y los tokens siguen siendo los de ECOREX (claro/oscuro por `html.dark`).
bpmn-js NO conmuta a modo oscuro por si solo: el lienzo se deja con **fondo claro
fijo** aunque `html.dark` este activo (limitacion documentada).

## Consecuencias

- No hubo cambio de modelo ni migracion EF (mismas tablas node/edge/forms/rules).
- No hay CSP configurada en SuperAdmin: no hizo falta ajustar CSP (los assets son
  self-hosted, mismo origen). Si se agrega CSP, permitir `script-src 'self'` para
  `bpmn-modeler.js`/`ecorex-bpmn.js` y `style-src 'unsafe-inline'` (diagram-js inyecta
  estilos inline en el SVG del canvas).
- E2E: el escenario del editor se adapto a bpmn-js. Como el click programatico de la
  paleta NO dispara como el mouse real (nota del vault), el paso "agregar tarea +
  conectar" se hace de forma DETERMINISTA por la API del modeler (elementFactory +
  modeling) a traves de un puente `window.ecorexBpmnE2E` que expone el modulo interop;
  luego se Guarda, se reabre y se verifica el grafo por el `elementRegistry`.

## Deudas

- **Version antigua (bpmn-js 8.8.2)**: la linea vigente es 17+. Actualizar es deuda
  (revisar breaking changes de la API del modeler y del PaletteProvider).
- **Modo oscuro del canvas**: bpmn-js no soporta dark; el lienzo queda claro fijo.
- **Viewer de ejecucion**: sigue pendiente (otra ola); esta migracion es solo el editor.
- **Saltar a otro flujo (call activity)**: sigue siendo visual (deuda heredada de ADR-0022).
