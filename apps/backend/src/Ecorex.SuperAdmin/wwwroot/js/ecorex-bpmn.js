// ============================================================================
// ecorex-bpmn.js - Interop Blazor <-> bpmn-js (v8.8.2, self-hosted) para el
// EDITOR de flujos (modulo 000291, /flujos). ADR-0034: reemplaza el canvas
// propio (ADR-0022) por bpmn-js embebido. SOLO editor (modeler); el viewer de
// ejecucion es otra ola.
//
// - Paleta ACOTADA al subconjunto que ejecuta el motor: startEvent, endEvent,
//   task, exclusiveGateway + herramientas connect/hand/lasso. Se registra un
//   PaletteProvider custom (patron MiPaletteBootstrapProvider del legacy) que
//   SOBREESCRIBE 'paletteProvider', asi el usuario no ve el catalogo completo
//   de BPMN (mensajes, subprocesos, etc. que el motor no soporta).
// - Los iconos de la paleta son SVG inline (data-URI), no el webfont bpmn-icon-*
//   (no viene con los assets del legacy; ver README de lib/bpmnio).
// - La parametrizacion por nodo (formularios/reglas/condiciones) NO viaja en el
//   XML: sigue en tablas por BpmnElementId. bpmn-js solo produce/consume el XML
//   de la topologia + layout (bpmndi).
// ============================================================================

const NS = 'http://www.omg.org/spec/BPMN/20100524/MODEL';

// SVG data-URIs para las entradas de la paleta acotada (sin depender del webfont).
function svgIcon(inner) {
    const svg =
        '<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" ' +
        'fill="none" stroke="#1f2937" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round">' +
        inner + '</svg>';
    return 'data:image/svg+xml;utf8,' + encodeURIComponent(svg);
}

const ICONS = {
    start: svgIcon('<circle cx="12" cy="12" r="8"/>'),
    end: svgIcon('<circle cx="12" cy="12" r="8" stroke-width="2.8"/>'),
    task: svgIcon('<rect x="4" y="7" width="16" height="10" rx="2"/>'),
    gateway: svgIcon('<path d="M12 3 21 12 12 21 3 12Z"/><path d="M9 9l6 6M15 9l-6 6"/>'),
    connect: svgIcon('<path d="M4 12h13"/><path d="M13 7l5 5-5 5"/>'),
    trash: svgIcon('<path d="M3 6h18"/><path d="M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/><path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"/>'),
    hand: svgIcon('<path d="M8 11V7a1.6 1.6 0 0 1 3.2 0M11.2 8V5.5a1.6 1.6 0 0 1 3.2 0V8M14.4 7.5a1.6 1.6 0 0 1 3.2 0V13a5 5 0 0 1-5 5h-1.6a5 5 0 0 1-3.9-2l-2.3-3.2"/>'),
    lasso: svgIcon('<rect x="4" y="4" width="16" height="16" rx="2" stroke-dasharray="3 3"/>')
};

// ---- PaletteProvider ACOTADO ------------------------------------------------
// Devuelve SOLO las entradas soportadas por el motor. Al registrarse como
// 'paletteProvider' (mismo nombre) sobreescribe el provider por defecto de
// bpmn-js, de modo que la paleta completa nunca se muestra.
function AcotadoPaletteProvider(palette, create, elementFactory, spaceTool, lassoTool, handTool, globalConnect, translate) {
    this._create = create;
    this._elementFactory = elementFactory;
    this._lassoTool = lassoTool;
    this._handTool = handTool;
    this._translate = translate;
    palette.registerProvider(this);
}

AcotadoPaletteProvider.$inject = [
    'palette', 'create', 'elementFactory', 'spaceTool',
    'lassoTool', 'handTool', 'globalConnect', 'translate'
];

AcotadoPaletteProvider.prototype.getPaletteEntries = function () {
    const create = this._create;
    const elementFactory = this._elementFactory;
    const handTool = this._handTool;
    const lassoTool = this._lassoTool;

    function startCreate(type, options) {
        return function (event) {
            const shape = elementFactory.createShape(Object.assign({ type: type }, options));
            create.start(event, shape);
        };
    }

    return {
        'hand-tool': {
            group: 'tools',
            title: 'Activar herramienta de mano (mover el lienzo)',
            imageUrl: ICONS.hand,
            action: { click: function (e) { handTool.activateHand(e); } }
        },
        'lasso-tool': {
            group: 'tools',
            title: 'Activar seleccion por lazo',
            imageUrl: ICONS.lasso,
            action: { click: function (e) { lassoTool.activateSelection(e); } }
        },
        'tool-separator': { group: 'tools', separator: true },
        'create.start-event': {
            group: 'event',
            title: 'Evento de inicio',
            imageUrl: ICONS.start,
            action: { dragstart: startCreate('bpmn:StartEvent'), click: startCreate('bpmn:StartEvent') }
        },
        'create.end-event': {
            group: 'event',
            title: 'Evento de fin',
            imageUrl: ICONS.end,
            action: { dragstart: startCreate('bpmn:EndEvent'), click: startCreate('bpmn:EndEvent') }
        },
        'create.exclusive-gateway': {
            group: 'gateway',
            title: 'Compuerta exclusiva',
            imageUrl: ICONS.gateway,
            action: {
                dragstart: startCreate('bpmn:ExclusiveGateway'),
                click: startCreate('bpmn:ExclusiveGateway')
            }
        },
        'create.task': {
            group: 'activity',
            title: 'Tarea',
            imageUrl: ICONS.task,
            action: { dragstart: startCreate('bpmn:Task'), click: startCreate('bpmn:Task') }
        }
    };
};

const AcotadoPaletteModule = {
    __init__: ['paletteProvider'],
    paletteProvider: ['type', AcotadoPaletteProvider]
};

// ---- ContextPadProvider ACOTADO (SVG inline, sin webfont bpmn-icon) ---------
// bpmn-js pinta el context pad nativo con clases 'bpmn-icon-*' que dependen del
// webfont (no vendoreado) -> cuadros en blanco. Se sobreescribe 'contextPadProvider'
// para dar SOLO las acciones soportadas (conectar / anexar tarea|compuerta|fin /
// eliminar) con iconos SVG data-URI. Un endEvent o una arista solo ofrecen borrar.
function AcotadoContextPadProvider(contextPad, modeling, elementFactory, create, connect, autoPlace, translate) {
    this._modeling = modeling;
    this._elementFactory = elementFactory;
    this._create = create;
    this._connect = connect;
    this._autoPlace = autoPlace;
    contextPad.registerProvider(this);
}

AcotadoContextPadProvider.$inject = [
    'contextPad', 'modeling', 'elementFactory', 'create', 'connect', 'autoPlace', 'translate'
];

AcotadoContextPadProvider.prototype.getContextPadEntries = function (element) {
    const modeling = this._modeling;
    const elementFactory = this._elementFactory;
    const create = this._create;
    const connect = this._connect;
    const autoPlace = this._autoPlace;
    const entries = {};

    function removeAction(event, el) { modeling.removeElements([el]); }

    // Anexar un nodo nuevo conectado desde 'element' (auto-place si esta disponible,
    // si no se arrastra para colocar).
    function appendAction(type, title, imageUrl) {
        function appendStart(event, el) {
            const shape = elementFactory.createShape({ type: type });
            create.start(event, shape, { source: el });
        }
        function append(event, el) {
            if (autoPlace) {
                const shape = elementFactory.createShape({ type: type });
                autoPlace.append(el, shape);
            } else {
                appendStart(event, el);
            }
        }
        return { group: 'model', title: title, imageUrl: imageUrl, action: { dragstart: appendStart, click: append } };
    }

    function startConnect(event, el) { connect.start(event, el); }

    const isShape = element && element.type
        && element.type !== 'bpmn:SequenceFlow'
        && element.type !== 'label';

    if (isShape && element.type !== 'bpmn:EndEvent') {
        entries['append.task'] = appendAction('bpmn:Task', 'Anexar tarea', ICONS.task);
        entries['append.gateway'] = appendAction('bpmn:ExclusiveGateway', 'Anexar compuerta', ICONS.gateway);
        entries['append.end-event'] = appendAction('bpmn:EndEvent', 'Anexar fin', ICONS.end);
        entries['connect'] = {
            group: 'connect', title: 'Conectar', imageUrl: ICONS.connect,
            action: { click: startConnect, dragstart: startConnect }
        };
    }

    entries['delete'] = {
        group: 'edit', title: 'Eliminar', imageUrl: ICONS.trash,
        action: { click: removeAction }
    };

    return entries;
};

const AcotadoContextPadModule = {
    __init__: ['contextPadProvider'],
    contextPadProvider: ['type', AcotadoContextPadProvider]
};

// Estilos inline (sin archivo .css aparte): dimensiona los iconos SVG (imageUrl)
// de la paleta y del context pad para que se vean nitidos y centrados.
function injectStyle() {
    if (document.getElementById('ecorex-bpmn-style')) { return; }
    const css =
        '.djs-palette .entry img, .djs-context-pad .entry img { width: 20px; height: 20px; }' +
        '.djs-palette .entry, .djs-context-pad .entry { display: flex; align-items: center; justify-content: center; }' +
        '.djs-palette .entry { width: 44px; height: 44px; }';
    const style = document.createElement('style');
    style.id = 'ecorex-bpmn-style';
    style.textContent = css;
    document.head.appendChild(style);
}

// Diagrama en blanco: un unico startEvent (el motor exige exactamente uno).
const EMPTY_DIAGRAM =
    '<?xml version="1.0" encoding="UTF-8"?>' +
    '<bpmn:definitions xmlns:bpmn="' + NS + '" ' +
    'xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI" ' +
    'xmlns:dc="http://www.omg.org/spec/DD/20100524/DC" ' +
    'id="Definitions_1" targetNamespace="http://bpmn.io/schema/bpmn">' +
    '<bpmn:process id="Process_1" isExecutable="true">' +
    '<bpmn:startEvent id="StartEvent_1" name="Inicio"/>' +
    '</bpmn:process>' +
    '<bpmndi:BPMNDiagram id="BPMNDiagram_1">' +
    '<bpmndi:BPMNPlane id="BPMNPlane_1" bpmnElement="Process_1">' +
    '<bpmndi:BPMNShape id="StartEvent_1_di" bpmnElement="StartEvent_1">' +
    '<dc:Bounds x="80" y="150" width="36" height="36"/>' +
    '</bpmndi:BPMNShape>' +
    '</bpmndi:BPMNPlane>' +
    '</bpmndi:BPMNDiagram>' +
    '</bpmn:definitions>';

// Registro de instancias por contenedor (una pagina puede abrir/cerrar el editor).
const instances = new Map();

function resolveElement(element) {
    if (!element) { return null; }
    if (element.type === 'label' && element.labelTarget) { return element.labelTarget; }
    return element;
}

// Solo reportamos a Blazor los nodos parametrizables (no SequenceFlow/Process/label).
function isSelectableNode(element) {
    if (!element || !element.type) { return false; }
    return element.type !== 'bpmn:SequenceFlow'
        && element.type !== 'bpmn:Process'
        && element.type !== 'bpmn:Collaboration'
        && element.type !== 'label';
}

export async function init(containerId, dotnetRef, xml) {
    if (typeof window.BpmnJS !== 'function') {
        console.error('[ecorex-bpmn] window.BpmnJS no esta cargado (revisa lib/bpmnio/bpmn-modeler.js).');
        return false;
    }
    destroy(containerId);

    const container = document.getElementById(containerId);
    if (!container) {
        console.error('[ecorex-bpmn] contenedor no encontrado:', containerId);
        return false;
    }

    injectStyle();

    const modeler = new window.BpmnJS({
        container: container,
        additionalModules: [AcotadoPaletteModule, AcotadoContextPadModule]
    });

    const state = { modeler: modeler, dotnetRef: dotnetRef, dirty: false };
    instances.set(containerId, state);

    try {
        await modeler.importXML(xml && xml.trim().length > 0 ? xml : EMPTY_DIAGRAM);
    } catch (err) {
        console.error('[ecorex-bpmn] no se pudo importar el XML BPMN:', err);
    }

    zoomFit(containerId);

    const eventBus = modeler.get('eventBus');

    // Seleccion (click o cambio de seleccion) -> Blazor carga el panel por BpmnElementId.
    function reportSelection(element) {
        const node = resolveElement(element);
        if (!isSelectableNode(node)) {
            dotnetRef.invokeMethodAsync('OnElementSelected', null, null, null);
            return;
        }
        const bo = node.businessObject || {};
        dotnetRef.invokeMethodAsync('OnElementSelected', node.id, node.type, bo.name || null);
    }

    eventBus.on('element.click', function (e) { reportSelection(e.element); });
    eventBus.on('selection.changed', function (e) {
        const selection = (e && e.newSelection) || [];
        reportSelection(selection.length === 1 ? selection[0] : null);
    });

    // Cambios del grafo -> marca dirty y avisa a Blazor (autosave opcional).
    modeler.get('commandStack').changed && eventBus.on('commandStack.changed', function () {
        state.dirty = true;
        dotnetRef.invokeMethodAsync('OnGraphChanged').catch(function () { /* circuito cerrado */ });
    });

    return true;
}

export async function exportXml(containerId) {
    const state = instances.get(containerId);
    if (!state) { return null; }
    try {
        const result = await state.modeler.saveXML({ format: true });
        state.dirty = false;
        return result.xml;
    } catch (err) {
        console.error('[ecorex-bpmn] no se pudo exportar el XML:', err);
        return null;
    }
}

export async function importXml(containerId, xml) {
    const state = instances.get(containerId);
    if (!state) { return false; }
    try {
        await state.modeler.importXML(xml && xml.trim().length > 0 ? xml : EMPTY_DIAGRAM);
        zoomFit(containerId);
        state.dirty = false;
        return true;
    } catch (err) {
        console.error('[ecorex-bpmn] no se pudo importar el XML:', err);
        return false;
    }
}

export function zoomFit(containerId) {
    const state = instances.get(containerId);
    if (!state) { return; }
    try {
        state.modeler.get('canvas').zoom('fit-viewport', 'auto');
    } catch (err) { /* aun sin diagrama */ }
}

export function isDirty(containerId) {
    const state = instances.get(containerId);
    return state ? state.dirty : false;
}

export function destroy(containerId) {
    const state = instances.get(containerId);
    if (!state) { return; }
    try { state.modeler.destroy(); } catch (err) { /* ya destruido */ }
    instances.delete(containerId);
}

// ---- Helpers deterministas para pruebas E2E --------------------------------
// El click programatico de la paleta de bpmn-js no dispara igual que el mouse
// real (nota del vault). Para pruebas se crea la forma y la conexion por la API
// del modeler (elementFactory + modeling), de forma determinista.
export function e2eAddTaskAndConnect(containerId, name) {
    const state = instances.get(containerId);
    if (!state) { return null; }
    const modeler = state.modeler;
    const elementFactory = modeler.get('elementFactory');
    const modeling = modeler.get('modeling');
    const elementRegistry = modeler.get('elementRegistry');
    const canvas = modeler.get('canvas');

    const root = canvas.getRootElement();
    const start = elementRegistry.filter(function (el) { return el.type === 'bpmn:StartEvent'; })[0];
    const baseX = start ? start.x + 180 : 300;
    const baseY = start ? start.y : 160;

    const taskShape = elementFactory.createShape({ type: 'bpmn:Task' });
    const task = modeling.createShape(taskShape, { x: baseX, y: baseY }, root);
    if (name) { modeling.updateProperties(task, { name: name }); }
    if (start) { modeling.connect(start, task); }
    return task.id;
}

export function e2eElementCount(containerId, type) {
    const state = instances.get(containerId);
    if (!state) { return 0; }
    return state.modeler.get('elementRegistry').filter(function (el) { return el.type === type; }).length;
}

// Selecciona un nodo por su BpmnElementId (o por nombre, si no hay id): dispara el mismo
// flujo que el click real (selection.changed -> Blazor carga el panel del nodo).
export function e2eSelectElement(containerId, idOrName) {
    const state = instances.get(containerId);
    if (!state) { return null; }
    const modeler = state.modeler;
    const registry = modeler.get('elementRegistry');
    let element = registry.get(idOrName);
    if (!element) {
        element = registry.filter(function (el) {
            const bo = el.businessObject || {};
            return bo.name === idOrName;
        })[0];
    }
    if (!element) { return null; }
    modeler.get('selection').select(element);
    return element.id;
}

// Puente global SOLO para pruebas E2E (Playwright via page.evaluate no puede alcanzar el
// modulo ES importado dentro del circuito Blazor). No lo usa la app en runtime normal.
window.ecorexBpmnE2E = {
    addTaskAndConnect: function (containerId, name) { return e2eAddTaskAndConnect(containerId, name); },
    count: function (containerId, type) { return e2eElementCount(containerId, type); },
    select: function (containerId, idOrName) { return e2eSelectElement(containerId, idOrName); },
    ready: function (containerId) { return instances.has(containerId); }
};
