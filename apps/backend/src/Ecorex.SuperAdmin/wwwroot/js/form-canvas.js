// form-canvas.js - Editor de CROQUIS/DIBUJO para el control Canvas de los formularios (ADR Canvas).
// Patron: global window.ecorexFormCanvas (como form-capture.js), sin ES modules, sin frameworks.
// El motor es 100% cliente (Blazor Server no puede round-trip de pointermove). La escena es la fuente
// de verdad; se re-renderiza a un <svg> nativo. Exporta/importa SVG autocontenido (imagenes como
// data-URL embebido) para guardar en FormResponse.Data y para imprimir nitido.
//
// API (todas por ID del contenedor, no ElementReference):
//   init(id, optsJson, existingSvg)  -> crea el editor (idempotente); carga el SVG previo la 1a vez.
//   getSvg(id)                       -> string SVG exportado (con cuadricula tenue de fondo).
//   setTool(id, tool)                -> 'select' | 'rect' | 'ellipse' | 'text' | 'pen' | 'image'
//   setColor(id, color)              -> color de trazo/texto actual.
//   undo(id) / redo(id)              -> deshacer / rehacer.
//   del(id)                          -> borra la seleccion.
//   addImage(id, dataUrl)            -> inserta una imagen (data-URL) en el centro.
//   clearAll(id)                     -> vacia el dibujo.
window.ecorexFormCanvas = (function () {
    var SVGNS = 'http://www.w3.org/2000/svg';
    var editors = {}; // id -> estado

    function ed(id) { return editors[id]; }

    function parseOpts(optsJson) {
        var o = { cell: 20, grid: true, height: 360, maxPages: 20 };
        try {
            if (optsJson) {
                var p = JSON.parse(optsJson);
                if (p && typeof p === 'object') {
                    if (p.cell > 0) { o.cell = Math.max(6, Math.min(120, p.cell | 0)); }
                    if (p.grid === false) { o.grid = false; }
                    if (p.height > 0) { o.height = Math.max(120, Math.min(2000, p.height | 0)); }
                    if (p.maxPages > 0) { o.maxPages = Math.max(1, Math.min(50, p.maxPages | 0)); }
                }
            }
        } catch (e) { /* opts invalidas: default */ }
        return o;
    }

    // ---- Escena (fuente de verdad) ----
    function snapshot(s) { return JSON.stringify(s.scene); }
    function pushUndo(s) {
        s.undo.push(snapshot(s));
        if (s.undo.length > 60) { s.undo.shift(); }
        s.redo.length = 0;
    }
    function restore(s, json) {
        try { s.scene = JSON.parse(json) || []; } catch (e) { s.scene = []; }
        s.sel = null;
        render(s);
    }

    function newShape(s, type) {
        s.nextId = (s.nextId || 0) + 1;
        return { id: 'sh' + s.nextId + '_' + Math.round(performance.now() % 1e6), type: type };
    }

    // ==================== LIBRERIA DE FORMAS PARAMETRICAS (piezas de lamina) ====================
    // Cada forma define su tamano base y una funcion geom(w,h) -> { parts, cotas }.
    //   parts: primitivas del contorno en coords LOCALES (0..w, 0..h): {t:'line'|'rect'|'ellipse'|'circle'|'path'}.
    //   cotas: puntos de medida clicables {key, label, x, y}. El valor se guarda en sh.dims[key].
    // Para AGREGAR una forma: anade una entrada a SHAPES con {label, w, h, geom}. Nada mas (paleta, dibujo,
    // cotas, export/import y edicion son genericos). El SVG se reconstruye desde data-ecx-kind/dims (round-trip).
    var SHAPES = {
        lamina: {
            label: 'Lamina', w: 220, h: 140,
            geom: function (w, h) {
                return {
                    parts: [{ t: 'rect', x: 0, y: 0, w: w, h: h }],
                    cotas: [
                        { key: 'ancho', label: 'ancho', x: w / 2, y: h + 14 },
                        { key: 'largo', label: 'largo', x: w + 14, y: h / 2 },
                        { key: 'espesor', label: 'espesor', x: 0, y: -12 }
                    ]
                };
            }
        },
        brida_cuadrada: {
            label: 'Brida cuadrada', w: 160, h: 160,
            geom: function (w, h) {
                var r = Math.max(5, Math.min(w, h) * 0.07), m = Math.min(w, h) * 0.18;
                return {
                    parts: [
                        { t: 'rect', x: 0, y: 0, w: w, h: h },
                        { t: 'circle', cx: m, cy: m, r: r }, { t: 'circle', cx: w - m, cy: m, r: r },
                        { t: 'circle', cx: m, cy: h - m, r: r }, { t: 'circle', cx: w - m, cy: h - m, r: r }
                    ],
                    cotas: [
                        { key: 'lado', label: 'lado', x: w / 2, y: h + 14 },
                        { key: 'perforacion', label: 'perf', x: m, y: m }
                    ]
                };
            }
        },
        brida_circular: {
            label: 'Brida circular', w: 170, h: 170,
            geom: function (w, h) {
                var cx = w / 2, cy = h / 2, re = Math.min(w, h) / 2, ri = re * 0.55, rp = re * 0.08;
                var holes = [];
                for (var i = 0; i < 6; i++) { var a = i / 6 * Math.PI * 2; holes.push({ t: 'circle', cx: cx + Math.cos(a) * (re + ri) / 2, cy: cy + Math.sin(a) * (re + ri) / 2, r: rp }); }
                return {
                    parts: [{ t: 'ellipse', cx: cx, cy: cy, rx: re, ry: re }, { t: 'ellipse', cx: cx, cy: cy, rx: ri, ry: ri }].concat(holes),
                    cotas: [
                        { key: 'diam_ext', label: 'D ext', x: cx, y: -12 },
                        { key: 'diam_int', label: 'D int', x: cx, y: cy },
                        { key: 'perforaciones', label: 'perfs', x: cx + re, y: cy }
                    ]
                };
            }
        },
        angulo: {
            label: 'Angulo / L', w: 180, h: 180,
            geom: function (w, h) {
                var t = Math.min(w, h) * 0.22;
                return {
                    parts: [{ t: 'path', d: 'M 0 0 L ' + t + ' 0 L ' + t + ' ' + (h - t) + ' L ' + w + ' ' + (h - t) + ' L ' + w + ' ' + h + ' L 0 ' + h + ' Z' }],
                    cotas: [
                        { key: 'ala1', label: 'ala', x: w / 2, y: h + 14 },
                        { key: 'ala2', label: 'ala', x: -14, y: h / 2 },
                        { key: 'angulo', label: 'ang', x: t + 8, y: h - t + 8 },
                        { key: 'largo', label: 'largo', x: w + 14, y: h - t }
                    ]
                };
            }
        },
        canal_u: {
            label: 'Canal U/C', w: 180, h: 150,
            geom: function (w, h) {
                var t = Math.min(w, h) * 0.16;
                return {
                    parts: [{ t: 'path', d: 'M 0 0 L 0 ' + h + ' L ' + w + ' ' + h + ' L ' + w + ' 0 L ' + (w - t) + ' 0 L ' + (w - t) + ' ' + (h - t) + ' L ' + t + ' ' + (h - t) + ' L ' + t + ' 0 Z' }],
                    cotas: [
                        { key: 'altura', label: 'altura', x: -14, y: h / 2 },
                        { key: 'ala', label: 'ala', x: t / 2, y: -12 },
                        { key: 'angulo', label: 'ang', x: t + 8, y: h - t + 8 }
                    ]
                };
            }
        },
        canal_ce: {
            label: 'Canal CE', w: 180, h: 150,
            geom: function (w, h) {
                var t = Math.min(w, h) * 0.16, lip = t * 0.9;
                return {
                    parts: [{ t: 'path', d: 'M ' + lip + ' 0 L 0 0 L 0 ' + h + ' L ' + lip + ' ' + h + ' L ' + lip + ' ' + (h - t) + ' L ' + t + ' ' + (h - t) + ' L ' + t + ' ' + t + ' L ' + lip + ' ' + t + ' Z' }],
                    cotas: [
                        { key: 'altura', label: 'altura', x: -14, y: h / 2 },
                        { key: 'ala1', label: 'a1', x: lip / 2, y: -12 },
                        { key: 'ala2', label: 'a2', x: lip / 2, y: h + 14 },
                        { key: 'ala3', label: 'a3', x: t, y: t },
                        { key: 'ala4', label: 'a4', x: t, y: h - t },
                        { key: 'angulo', label: 'ang', x: t + 8, y: h / 2 }
                    ]
                };
            }
        },
        canal_omega: {
            label: 'Canal Omega', w: 200, h: 130,
            geom: function (w, h) {
                var f = w * 0.18, up = h * 0.55;
                return {
                    parts: [{ t: 'path', d: 'M 0 ' + h + ' L 0 ' + up + ' L ' + f + ' ' + up + ' L ' + f + ' 0 L ' + (w - f) + ' 0 L ' + (w - f) + ' ' + up + ' L ' + w + ' ' + up + ' L ' + w + ' ' + h }],
                    cotas: [
                        { key: 'ancho', label: 'ancho', x: w / 2, y: -12 },
                        { key: 'altura', label: 'altura', x: (w - f), y: up / 2 },
                        { key: 'ala1', label: 'a1', x: 0, y: (up + h) / 2 },
                        { key: 'ala2', label: 'a2', x: w, y: (up + h) / 2 }
                    ]
                };
            }
        },
        bandeja_sencilla: {
            label: 'Bandeja sencilla', w: 210, h: 150,
            geom: function (w, h) {
                var t = h * 0.28;
                return {
                    parts: [{ t: 'path', d: 'M 0 0 L 0 ' + h + ' L ' + w + ' ' + h + ' L ' + w + ' 0 M 0 ' + t + ' L ' + w + ' ' + t }],
                    cotas: [
                        { key: 'ancho', label: 'ancho', x: w / 2, y: h + 14 },
                        { key: 'altura', label: 'altura', x: -14, y: h / 2 },
                        { key: 'a1', label: 'a1', x: 0, y: t / 2 }, { key: 'a2', label: 'a2', x: w, y: t / 2 },
                        { key: 'a3', label: 'a3', x: w / 4, y: t }, { key: 'a4', label: 'a4', x: 3 * w / 4, y: t }
                    ]
                };
            }
        },
        bandeja_doble: {
            label: 'Bandeja doble ala', w: 230, h: 160,
            geom: function (w, h) {
                var t = h * 0.25, f = w * 0.12;
                return {
                    parts: [{ t: 'path', d: 'M ' + f + ' 0 L 0 0 L 0 ' + h + ' L ' + w + ' ' + h + ' L ' + w + ' 0 L ' + (w - f) + ' 0 M ' + f + ' ' + t + ' L ' + (w - f) + ' ' + t }],
                    cotas: [
                        { key: 'ancho', label: 'ancho', x: w / 2, y: h + 14 },
                        { key: 'altura', label: 'altura', x: -14, y: h / 2 },
                        { key: 'a1', label: 'a1', x: f / 2, y: 0 }, { key: 'a2', label: 'a2', x: 0, y: t },
                        { key: 'a3', label: 'a3', x: w / 3, y: t }, { key: 'a4', label: 'a4', x: 2 * w / 3, y: t },
                        { key: 'b1', label: 'b1', x: w - f / 2, y: 0 }, { key: 'b2', label: 'b2', x: w, y: t },
                        { key: 'b3', label: 'b3', x: w / 4, y: h }, { key: 'b4', label: 'b4', x: 3 * w / 4, y: h }
                    ]
                };
            }
        },
        cilindro: {
            label: 'Cilindro', w: 130, h: 190,
            geom: function (w, h) {
                var ry = w * 0.18;
                return {
                    parts: [
                        { t: 'ellipse', cx: w / 2, cy: ry, rx: w / 2, ry: ry },
                        { t: 'path', d: 'M 0 ' + ry + ' L 0 ' + (h - ry) + ' M ' + w + ' ' + ry + ' L ' + w + ' ' + (h - ry) },
                        { t: 'path', d: 'M 0 ' + (h - ry) + ' A ' + (w / 2) + ' ' + ry + ' 0 0 0 ' + w + ' ' + (h - ry) }
                    ],
                    cotas: [
                        { key: 'diametro', label: 'diam', x: w / 2, y: ry },
                        { key: 'altura', label: 'altura', x: w + 14, y: h / 2 },
                        { key: 'radio', label: 'radio', x: 0, y: ry }
                    ]
                };
            }
        },
        perfil_rolado: {
            label: 'Perfil rolado', w: 190, h: 150,
            geom: function (w, h) {
                return {
                    parts: [{ t: 'ellipse', cx: w / 2, cy: h / 2, rx: w / 2, ry: h / 2 }],
                    cotas: [
                        { key: 'diam_mayor', label: 'D mayor', x: w / 2, y: -12 },
                        { key: 'diam_menor', label: 'D menor', x: -14, y: h / 2 }
                    ]
                };
            }
        },
        cono: {
            label: 'Cono / transicion', w: 190, h: 170,
            geom: function (w, h) {
                var top = w * 0.28, tx = (w - top) / 2, ry = w * 0.10;
                return {
                    parts: [
                        { t: 'ellipse', cx: w / 2, cy: ry, rx: top / 2, ry: ry * (top / w) },
                        { t: 'path', d: 'M ' + tx + ' ' + ry + ' L 0 ' + (h - ry) + ' M ' + (tx + top) + ' ' + ry + ' L ' + w + ' ' + (h - ry) },
                        { t: 'path', d: 'M 0 ' + (h - ry) + ' A ' + (w / 2) + ' ' + ry + ' 0 0 0 ' + w + ' ' + (h - ry) }
                    ],
                    cotas: [
                        { key: 'diam_sup', label: 'D sup', x: w / 2, y: ry },
                        { key: 'diam_inf', label: 'D inf', x: w / 2, y: h },
                        { key: 'altura', label: 'altura', x: w + 14, y: h / 2 }
                    ]
                };
            }
        }
    };
    var SHAPE_ORDER = ['lamina', 'brida_cuadrada', 'brida_circular', 'angulo', 'canal_u', 'canal_ce',
        'canal_omega', 'bandeja_sencilla', 'bandeja_doble', 'cilindro', 'perfil_rolado', 'cono'];

    // Geometria efectiva de un grupo (escala la base a w,h actuales; dims solo alimentan las etiquetas).
    function groupGeom(sh) {
        var def = SHAPES[sh.kind]; if (!def) { return { parts: [], cotas: [] }; }
        return def.geom(sh.w, sh.h, sh.dims || {});
    }
    function cotaText(sh, cota) {
        var v = (sh.dims || {})[cota.key];
        return (v != null && String(v).trim() !== '') ? (cota.label + ': ' + v) : cota.label;
    }

    // ---- Render de la escena al <svg> de edicion ----
    function el(tag, attrs) {
        var e = document.createElementNS(SVGNS, tag);
        if (attrs) { for (var k in attrs) { if (attrs[k] != null) { e.setAttribute(k, attrs[k]); } } }
        return e;
    }

    // Grupo (forma predefinida): <g> con el contorno + marcadores de cota (punto amarillo + etiqueta).
    // Se serializa con data-ecx-kind/dims/w/h para reconstruirlo al recargar (round-trip) e imprimir.
    function groupToNode(sh) {
        var g = el('g', {
            'data-ecx': sh.id, 'data-ecx-kind': sh.kind,
            'data-ecx-dims': JSON.stringify(sh.dims || {}),
            'data-ecx-w': r2(sh.w), 'data-ecx-h': r2(sh.h)
        });
        var tr = 'translate(' + r2(sh.x) + ' ' + r2(sh.y) + ')';
        if (sh.rot) { tr += ' rotate(' + r2(sh.rot) + ' ' + r2(sh.w / 2) + ' ' + r2(sh.h / 2) + ')'; }
        g.setAttribute('transform', tr);
        var stroke = sh.stroke || '#1B1B1E', sw = sh.sw || 2;
        var geom = groupGeom(sh);
        geom.parts.forEach(function (p) {
            var e = null;
            if (p.t === 'rect') { e = el('rect', { x: p.x, y: p.y, width: p.w, height: p.h, fill: 'none', stroke: stroke, 'stroke-width': sw }); }
            else if (p.t === 'line') { e = el('line', { x1: p.x1, y1: p.y1, x2: p.x2, y2: p.y2, stroke: stroke, 'stroke-width': sw }); }
            else if (p.t === 'ellipse') { e = el('ellipse', { cx: p.cx, cy: p.cy, rx: Math.abs(p.rx), ry: Math.abs(p.ry), fill: 'none', stroke: stroke, 'stroke-width': sw }); }
            else if (p.t === 'circle') { e = el('circle', { cx: p.cx, cy: p.cy, r: Math.abs(p.r), fill: 'none', stroke: stroke, 'stroke-width': sw }); }
            else if (p.t === 'path') { e = el('path', { d: p.d, fill: 'none', stroke: stroke, 'stroke-width': sw, 'stroke-linecap': 'round', 'stroke-linejoin': 'round' }); }
            if (e) { g.appendChild(e); }
        });
        // Cotas: etiqueta + punto amarillo clicable (data-ecx-cota). Se exportan e imprimen con el grupo.
        geom.cotas.forEach(function (c) {
            var label = el('text', { x: c.x + 8, y: c.y - 6, fill: '#7a5b00', 'font-size': 12, 'font-family': 'Inter, Arial, sans-serif', 'data-ecx-cotalabel': c.key });
            label.textContent = cotaText(sh, c);
            g.appendChild(label);
            g.appendChild(el('circle', { cx: c.x, cy: c.y, r: 5, fill: '#FFC400', stroke: '#7a5b00', 'stroke-width': 1.2, 'data-ecx-cota': c.key, style: 'cursor:pointer' }));
        });
        return g;
    }

    function shapeToNode(sh) {
        if (sh.type === 'group') { return groupToNode(sh); }
        var n = null;
        if (sh.type === 'rect') {
            n = el('rect', { x: sh.x, y: sh.y, width: sh.w, height: sh.h, rx: 2, fill: 'none', stroke: sh.stroke, 'stroke-width': sh.sw || 2 });
        } else if (sh.type === 'ellipse') {
            n = el('ellipse', { cx: sh.x + sh.w / 2, cy: sh.y + sh.h / 2, rx: Math.abs(sh.w / 2), ry: Math.abs(sh.h / 2), fill: 'none', stroke: sh.stroke, 'stroke-width': sh.sw || 2 });
        } else if (sh.type === 'text') {
            n = el('text', { x: sh.x, y: sh.y, fill: sh.stroke, 'font-size': sh.fs || 16, 'font-family': 'Inter, Arial, sans-serif' });
            n.textContent = sh.text || '';
        } else if (sh.type === 'image') {
            n = el('image', { x: sh.x, y: sh.y, width: sh.w, height: sh.h, preserveAspectRatio: 'xMidYMid meet' });
            // href PLANO (SVG2), sin xlink: XMLSerializer NO declara xmlns:xlink en la raiz, asi que un
            // xlink:href sin prefijo se serializa como "ns1:href", que el parser HTML de la impresion NO
            // reconoce (la imagen no cargaba). El href plano sobrevive serializar -> reinyectar en HTML y
            // Chromium (edicion e impresion Puppeteer) lo rinde. (ADR-0080, fix v0.15.98)
            n.setAttribute('href', sh.href);
        } else if (sh.type === 'path') {
            n = el('path', { d: sh.d, fill: 'none', stroke: sh.stroke, 'stroke-width': sh.sw || 2, 'stroke-linecap': 'round', 'stroke-linejoin': 'round' });
        }
        if (n) {
            n.setAttribute('data-ecx', sh.id);
            // Rotacion: transform rotate(deg cx cy) alrededor del centro de la figura. Se serializa en el
            // SVG exportado, asi que se recarga e imprime con el mismo giro.
            if (sh.rot) {
                var cb = boundsOf(sh);
                n.setAttribute('transform', 'rotate(' + r2(sh.rot) + ' ' + r2(cb.x + cb.w / 2) + ' ' + r2(cb.y + cb.h / 2) + ')');
            }
        }
        return n;
    }

    // Figuras que admiten rotacion por manija (objetos e imagenes).
    function rotatable(sh) { return sh && (sh.type === 'rect' || sh.type === 'ellipse' || sh.type === 'image' || sh.type === 'text' || sh.type === 'path' || sh.type === 'group'); }
    // Pasa un punto de pantalla al espacio LOCAL sin rotar (gira -deg alrededor del centro).
    function unrotate(px, py, cx, cy, deg) {
        var r = -deg * Math.PI / 180, cos = Math.cos(r), sin = Math.sin(r), dx = px - cx, dy = py - cy;
        return { x: cx + dx * cos - dy * sin, y: cy + dx * sin + dy * cos };
    }

    function boundsOf(sh) {
        if (sh.type === 'text') { return { x: sh.x, y: (sh.y - (sh.fs || 16)), w: Math.max(40, (sh.text || '').length * (sh.fs || 16) * 0.55), h: (sh.fs || 16) * 1.3 }; }
        if (sh.type === 'path') {
            var xs = [], ys = []; (sh.pts || []).forEach(function (p) { xs.push(p[0]); ys.push(p[1]); });
            if (!xs.length) { return { x: sh.x || 0, y: sh.y || 0, w: 10, h: 10 }; }
            var minx = Math.min.apply(null, xs), miny = Math.min.apply(null, ys);
            return { x: minx, y: miny, w: Math.max(6, Math.max.apply(null, xs) - minx), h: Math.max(6, Math.max.apply(null, ys) - miny) };
        }
        return { x: Math.min(sh.x, sh.x + sh.w), y: Math.min(sh.y, sh.y + sh.h), w: Math.abs(sh.w), h: Math.abs(sh.h) };
    }

    function render(s) {
        // capa de formas
        var g = s.gShapes;
        while (g.firstChild) { g.removeChild(g.firstChild); }
        s.scene.forEach(function (sh) { var n = shapeToNode(sh); if (n) { g.appendChild(n); } });
        // capa de seleccion (no se exporta)
        var o = s.gOverlay;
        while (o.firstChild) { o.removeChild(o.firstChild); }
        var selSh = s.scene.find(function (x) { return x.id === s.sel; });
        if (selSh) {
            var b = boundsOf(selSh);
            var cx = b.x + b.w / 2, cy = b.y + b.h / 2, rot = selSh.rot || 0;
            // El overlay se pinta en un grupo rotado igual que la figura, para que la caja y las manijas
            // acompanen el giro.
            var grp = el('g');
            if (rot) { grp.setAttribute('transform', 'rotate(' + r2(rot) + ' ' + r2(cx) + ' ' + r2(cy) + ')'); }
            grp.appendChild(el('rect', { x: b.x - 3, y: b.y - 3, width: b.w + 6, height: b.h + 6, fill: 'none', stroke: '#2563EB', 'stroke-width': 1.2, 'stroke-dasharray': '5 4' }));
            // manija de redimension (esquina inf-der) para rect/ellipse/image/group (escala la forma)
            if (selSh.type === 'rect' || selSh.type === 'ellipse' || selSh.type === 'image' || selSh.type === 'group') {
                grp.appendChild(el('rect', { x: b.x + b.w - 4, y: b.y + b.h - 4, width: 10, height: 10, fill: '#2563EB', stroke: '#fff', 'stroke-width': 1.5, 'data-handle': '1', style: 'cursor:nwse-resize' }));
            }
            // manija de ROTACION: linea + circulo arriba del centro.
            if (rotatable(selSh)) {
                grp.appendChild(el('line', { x1: cx, y1: b.y - 3, x2: cx, y2: b.y - 24, stroke: '#2563EB', 'stroke-width': 1.2 }));
                grp.appendChild(el('circle', { cx: cx, cy: b.y - 24, r: 6, fill: '#2563EB', stroke: '#fff', 'stroke-width': 1.5, 'data-rothandle': '1', style: 'cursor:grab' }));
            }
            o.appendChild(grp);
        }
    }

    // ---- Puntero: dibujar / seleccionar / mover / redimensionar ----
    function pt(s, evt) {
        var r = s.svg.getBoundingClientRect();
        return { x: (evt.clientX - r.left) * (s.vw / r.width), y: (evt.clientY - r.top) * (s.vh / r.height) };
    }

    function hitShape(s, x, y) {
        for (var i = s.scene.length - 1; i >= 0; i--) {
            var b = boundsOf(s.scene[i]);
            if (x >= b.x - 4 && x <= b.x + b.w + 4 && y >= b.y - 4 && y <= b.y + b.h + 4) { return s.scene[i]; }
        }
        return null;
    }

    function onDown(s, evt) {
        if (s.readonly) { return; }
        evt.preventDefault();
        var p = pt(s, evt);
        s.svg.setPointerCapture && s.svg.setPointerCapture(evt.pointerId);
        if (s.tool === 'select') {
            // Cota clicable de una forma predefinida: un clic en el punto amarillo abre el input inline
            // para escribir la medida (no mueve el grupo).
            var cotaEl = evt.target && evt.target.closest ? evt.target.closest('[data-ecx-cota]') : null;
            if (cotaEl) {
                var gEl = cotaEl.closest('[data-ecx-kind]');
                var gid = gEl && gEl.getAttribute('data-ecx');
                var gsh = gid ? s.scene.find(function (x) { return x.id === gid; }) : null;
                if (gsh) { s.sel = gsh.id; openCotaInput(s, gsh, cotaEl.getAttribute('data-ecx-cota'), evt); render(s); return; }
            }
            var cur = s.scene.find(function (x) { return x.id === s.sel; });
            // manija de ROTACION?
            if (cur && evt.target && evt.target.getAttribute && evt.target.getAttribute('data-rothandle')) {
                var rb = boundsOf(cur); pushUndo(s);
                s.drag = { mode: 'rotate', sh: cur, cx: rb.x + rb.w / 2, cy: rb.y + rb.h / 2 };
                return;
            }
            // manija de redimension?
            if (cur && evt.target && evt.target.getAttribute && evt.target.getAttribute('data-handle')) {
                pushUndo(s); s.drag = { mode: 'resize', sh: cur }; return;
            }
            var hit = hitShape(s, p.x, p.y);
            s.sel = hit ? hit.id : null;
            if (hit) { pushUndo(s); s.drag = { mode: 'move', sh: hit, ox: p.x, oy: p.y }; }
            render(s);
            return;
        }
        if (s.tool === 'text') {
            openTextInput(s, p.x, p.y);
            return;
        }
        if (s.tool === 'pen') {
            pushUndo(s);
            var pen = newShape(s, 'path'); pen.stroke = s.color; pen.sw = 2; pen.pts = [[r2(p.x), r2(p.y)]]; pen.d = 'M ' + r2(p.x) + ' ' + r2(p.y);
            s.scene.push(pen); s.sel = pen.id; s.drag = { mode: 'pen', sh: pen };
            render(s);
            return;
        }
        if (s.tool === 'line') {
            // Linea recta = path de 2 puntos (reusa export/import/rotacion/mover de 'path').
            pushUndo(s);
            var ln = newShape(s, 'path'); ln.stroke = s.color; ln.sw = 2;
            ln.pts = [[r2(p.x), r2(p.y)], [r2(p.x), r2(p.y)]]; ln.d = ptsToD(ln.pts);
            s.scene.push(ln); s.sel = ln.id; s.drag = { mode: 'line', sh: ln, ox: p.x, oy: p.y };
            render(s);
            return;
        }
        // rect / ellipse: arranca una forma nueva
        pushUndo(s);
        var sh = newShape(s, s.tool); sh.x = r2(p.x); sh.y = r2(p.y); sh.w = 0; sh.h = 0; sh.stroke = s.color; sh.sw = 2;
        s.scene.push(sh); s.sel = sh.id; s.drag = { mode: 'draw', sh: sh, ox: p.x, oy: p.y };
        render(s);
    }

    function onMove(s, evt) {
        if (!s.drag) { return; }
        var p = pt(s, evt);
        var d = s.drag;
        if (d.mode === 'draw') {
            d.sh.x = r2(Math.min(d.ox, p.x)); d.sh.y = r2(Math.min(d.oy, p.y));
            d.sh.w = r2(Math.abs(p.x - d.ox)); d.sh.h = r2(Math.abs(p.y - d.oy));
        } else if (d.mode === 'move') {
            var dx = p.x - d.ox, dy = p.y - d.oy; d.ox = p.x; d.oy = p.y;
            moveShape(d.sh, dx, dy);
        } else if (d.mode === 'resize') {
            var b = boundsOf(d.sh);
            var rrot = d.sh.rot || 0;
            if (rrot) {
                // La figura esta rotada: redimension SIMETRICA alrededor del centro (pasa el puntero al
                // espacio local sin rotar y toma el semi-ancho/alto). Evita que el pivote se corra.
                var rcx = b.x + b.w / 2, rcy = b.y + b.h / 2, lp = unrotate(p.x, p.y, rcx, rcy, rrot);
                var hw = Math.max(3, Math.abs(lp.x - rcx)), hh = Math.max(3, Math.abs(lp.y - rcy));
                d.sh.w = r2(hw * 2); d.sh.h = r2(hh * 2); d.sh.x = r2(rcx - hw); d.sh.y = r2(rcy - hh);
            } else {
                d.sh.w = r2(Math.max(6, p.x - b.x)); d.sh.h = r2(Math.max(6, p.y - b.y));
            }
        } else if (d.mode === 'rotate') {
            // El mango esta ARRIBA del centro (angulo -90 en pantalla): rot = atan2(dy,dx) + 90.
            var ang = Math.atan2(p.y - d.cy, p.x - d.cx) * 180 / Math.PI + 90;
            if (evt.shiftKey) { ang = Math.round(ang / 15) * 15; } // Shift: pasos de 15 grados
            d.sh.rot = r2(((ang % 360) + 360) % 360);
        } else if (d.mode === 'pen') {
            d.sh.pts.push([r2(p.x), r2(p.y)]); d.sh.d += ' L ' + r2(p.x) + ' ' + r2(p.y);
        } else if (d.mode === 'line') {
            var ex = p.x, ey = p.y;
            if (evt.shiftKey) {
                // Snap del angulo a multiplos de 45 grados (horizontal/vertical/diagonales), misma longitud.
                var ang = Math.atan2(p.y - d.oy, p.x - d.ox);
                var snap = Math.round(ang / (Math.PI / 4)) * (Math.PI / 4);
                var dist = Math.hypot(p.x - d.ox, p.y - d.oy);
                ex = d.ox + dist * Math.cos(snap); ey = d.oy + dist * Math.sin(snap);
            }
            d.sh.pts[1] = [r2(ex), r2(ey)]; d.sh.d = ptsToD(d.sh.pts);
        }
        render(s);
    }

    function onUp(s, evt) {
        if (!s.drag) { return; }
        var d = s.drag; s.drag = null;
        // descartar formas degeneradas
        if ((d.mode === 'draw') && (Math.abs(d.sh.w) < 3 && Math.abs(d.sh.h) < 3)) {
            s.scene = s.scene.filter(function (x) { return x.id !== d.sh.id; }); s.sel = null;
        }
        if (d.mode === 'pen' && (d.sh.pts || []).length < 2) {
            s.scene = s.scene.filter(function (x) { return x.id !== d.sh.id; }); s.sel = null;
        }
        // Linea degenerada (arranque sin arrastre): descartar.
        if (d.mode === 'line') {
            var la = d.sh.pts[0], lb = d.sh.pts[1];
            if (Math.hypot(lb[0] - la[0], lb[1] - la[1]) < 3) { s.scene = s.scene.filter(function (x) { return x.id !== d.sh.id; }); s.sel = null; }
        }
        // 'pen' y 'line' permanecen activos (dibujar varios trazos/segmentos seguidos, p.ej. un triangulo).
        if (s.tool !== 'select' && s.tool !== 'pen' && s.tool !== 'line') { s.tool = 'select'; if (s.onTool) { try { s.onTool('select'); } catch (e) { } } }
        render(s);
    }

    function moveShape(sh, dx, dy) {
        if (sh.type === 'path') { sh.pts = sh.pts.map(function (p) { return [r2(p[0] + dx), r2(p[1] + dy)]; }); sh.d = ptsToD(sh.pts); }
        else { sh.x = r2(sh.x + dx); sh.y = r2(sh.y + dy); }
    }
    function ptsToD(pts) { return pts.map(function (p, i) { return (i === 0 ? 'M ' : 'L ') + p[0] + ' ' + p[1]; }).join(' '); }
    function r2(n) { return Math.round(n * 10) / 10; }

    // ---- Texto: input HTML flotante ----
    function openTextInput(s, x, y) {
        var inp = document.createElement('input');
        inp.type = 'text'; inp.placeholder = 'Anotacion...';
        inp.className = 'ecx-canvas-textinput';
        var r = s.svg.getBoundingClientRect();
        inp.style.position = 'absolute';
        inp.style.left = (x * (r.width / s.vw)) + 'px';
        inp.style.top = ((y - 14) * (r.height / s.vh)) + 'px';
        s.root.appendChild(inp);
        setTimeout(function () { inp.focus(); }, 10);
        function commit() {
            var t = inp.value.trim();
            if (inp.parentNode) { inp.parentNode.removeChild(inp); }
            if (t) {
                pushUndo(s);
                var sh = newShape(s, 'text'); sh.x = r2(x); sh.y = r2(y); sh.text = t; sh.stroke = s.color; sh.fs = 16;
                s.scene.push(sh); s.sel = sh.id; render(s);
            }
            s.tool = 'select'; if (s.onTool) { try { s.onTool('select'); } catch (e) { } }
        }
        inp.addEventListener('keydown', function (e) { if (e.key === 'Enter') { commit(); } else if (e.key === 'Escape') { if (inp.parentNode) { inp.parentNode.removeChild(inp); } } });
        inp.addEventListener('blur', commit);
    }

    // ---- Cota: input flotante para escribir la medida de un punto de una forma ----
    function openCotaInput(s, sh, key, evt) {
        var rootRect = s.root.getBoundingClientRect();
        var px = (evt && evt.clientX ? evt.clientX - rootRect.left : 10);
        var py = (evt && evt.clientY ? evt.clientY - rootRect.top : 10);
        var inp = document.createElement('input');
        inp.type = 'text'; inp.placeholder = 'medida';
        inp.value = (sh.dims && sh.dims[key] != null) ? sh.dims[key] : '';
        inp.className = 'ecx-cota-input';
        inp.style.cssText = 'position:absolute;left:' + px + 'px;top:' + py + 'px;width:76px;z-index:30;'
            + 'font:12px Inter,Arial,sans-serif;padding:2px 5px;border:1px solid #7a5b00;border-radius:5px;background:#FFF8E1;';
        s.root.appendChild(inp);
        var done = false;
        setTimeout(function () { inp.focus(); inp.select(); }, 10);
        function commit() {
            if (done) { return; } done = true;
            pushUndo(s);
            sh.dims = sh.dims || {};
            var v = inp.value.trim();
            if (v === '') { delete sh.dims[key]; } else { sh.dims[key] = v; }
            if (inp.parentNode) { inp.parentNode.removeChild(inp); }
            render(s);
        }
        inp.addEventListener('keydown', function (e) { if (e.key === 'Enter') { commit(); } else if (e.key === 'Escape') { done = true; if (inp.parentNode) { inp.parentNode.removeChild(inp); } } });
        inp.addEventListener('blur', commit);
    }

    // ---- Insertar una forma predefinida al centro del lienzo ----
    function addShape(id, kind) {
        var s = ed(id); if (!s) { return; }
        var def = SHAPES[kind]; if (!def) { return; }
        pushUndo(s);
        var sh = newShape(s, 'group');
        sh.kind = kind; sh.w = def.w; sh.h = def.h;
        sh.x = r2((s.vw - def.w) / 2); sh.y = r2((s.vh - def.h) / 2);
        sh.stroke = s.color || '#1B1B1E'; sh.sw = 2; sh.rot = 0; sh.dims = {};
        s.scene.push(sh); s.sel = sh.id; s.tool = 'select';
        if (s.onTool) { try { s.onTool('select'); } catch (e) { } }
        render(s);
        closePalette(s);
    }

    // ---- Paleta de formas: panel flotante con miniaturas (una sola fuente: SHAPES) ----
    function miniSvg(kind) {
        var def = SHAPES[kind]; if (!def) { return ''; }
        var pad = 14, geom = def.geom(def.w, def.h, {});
        var body = '';
        geom.parts.forEach(function (p) {
            if (p.t === 'rect') { body += '<rect x="' + p.x + '" y="' + p.y + '" width="' + p.w + '" height="' + p.h + '" fill="none" stroke="#1B1B1E" stroke-width="3"/>'; }
            else if (p.t === 'line') { body += '<line x1="' + p.x1 + '" y1="' + p.y1 + '" x2="' + p.x2 + '" y2="' + p.y2 + '" stroke="#1B1B1E" stroke-width="3"/>'; }
            else if (p.t === 'ellipse') { body += '<ellipse cx="' + p.cx + '" cy="' + p.cy + '" rx="' + Math.abs(p.rx) + '" ry="' + Math.abs(p.ry) + '" fill="none" stroke="#1B1B1E" stroke-width="3"/>'; }
            else if (p.t === 'circle') { body += '<circle cx="' + p.cx + '" cy="' + p.cy + '" r="' + Math.abs(p.r) + '" fill="none" stroke="#1B1B1E" stroke-width="3"/>'; }
            else if (p.t === 'path') { body += '<path d="' + p.d + '" fill="none" stroke="#1B1B1E" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>'; }
        });
        return '<svg viewBox="' + (-pad) + ' ' + (-pad) + ' ' + (def.w + pad * 2) + ' ' + (def.h + pad * 2) + '" width="100%" height="100%">' + body + '</svg>';
    }
    function closePalette(s) { if (s.palette && s.palette.parentNode) { s.palette.parentNode.removeChild(s.palette); } s.palette = null; }
    function togglePalette(id) {
        var s = ed(id); if (!s) { return; }
        if (s.palette) { closePalette(s); return; }
        var panel = document.createElement('div');
        panel.className = 'ecx-shape-palette';
        panel.style.cssText = 'position:absolute;left:8px;top:8px;z-index:40;width:min(420px,92%);max-height:78%;overflow:auto;'
            + 'background:#fff;border:1px solid #C3C7CF;border-radius:12px;box-shadow:0 10px 30px rgba(15,15,18,.18);padding:10px;'
            + 'display:grid;grid-template-columns:repeat(3,1fr);gap:8px;';
        SHAPE_ORDER.forEach(function (kind) {
            var def = SHAPES[kind]; if (!def) { return; }
            var btn = document.createElement('button');
            btn.type = 'button';
            btn.style.cssText = 'display:flex;flex-direction:column;align-items:center;gap:4px;padding:8px 4px;border:1px solid #E4E6EB;'
                + 'border-radius:9px;background:#fff;cursor:pointer;';
            btn.innerHTML = '<div style="width:100%;height:56px">' + miniSvg(kind) + '</div>'
                + '<span style="font:600 11px Inter,Arial,sans-serif;color:#1B1B1E;text-align:center">' + def.label + '</span>';
            btn.addEventListener('mouseenter', function () { btn.style.borderColor = '#2563EB'; });
            btn.addEventListener('mouseleave', function () { btn.style.borderColor = '#E4E6EB'; });
            btn.addEventListener('click', function () { addShape(id, kind); });
            panel.appendChild(btn);
        });
        s.root.appendChild(panel);
        s.palette = panel;
    }

    // ---- Construccion del editor ----
    function init(id, optsJson, existingSvg) {
        var root = document.getElementById(id);
        if (!root) { return; }
        if (root.dataset.ecxInit === '1') {
            // ya inicializado: no recargar (Blazor re-renderiza en cada ciclo). Solo asegurar opciones.
            return;
        }
        root.dataset.ecxInit = '1';
        var opts = parseOpts(optsJson);
        // Espacio logico FIJO (independiente del ancho de pantalla) para que el dibujo se persista y se
        // recargue fiel a cualquier ancho. Escala UNIFORME (width:100% + height:auto) -> cuadricula cuadrada.
        var vw = 1000, vh = opts.height; // 'alto' = proporcion horizontal como la parrilla del PDF

        root.classList.add('ecx-canvas-root');
        root.style.position = 'relative';

        var svg = el('svg', { viewBox: '0 0 ' + vw + ' ' + vh, class: 'ecx-canvas-svg' });
        svg.style.width = '100%';
        svg.style.height = 'auto';
        svg.style.touchAction = 'none';
        // cuadricula tenue (papel milimetrado) como <pattern> de fondo; se INCLUYE al exportar (muy tenue)
        var defs = el('defs');
        var pat = el('pattern', { id: 'ecxgrid-' + id, width: opts.cell, height: opts.cell, patternUnits: 'userSpaceOnUse' });
        pat.appendChild(el('path', { d: 'M ' + opts.cell + ' 0 L 0 0 0 ' + opts.cell, fill: 'none', stroke: '#D8DBE0', 'stroke-width': 0.6 }));
        var pat5 = el('pattern', { id: 'ecxgrid5-' + id, width: opts.cell * 5, height: opts.cell * 5, patternUnits: 'userSpaceOnUse' });
        pat5.appendChild(el('rect', { width: opts.cell * 5, height: opts.cell * 5, fill: 'url(#ecxgrid-' + id + ')' }));
        pat5.appendChild(el('path', { d: 'M ' + (opts.cell * 5) + ' 0 L 0 0 0 ' + (opts.cell * 5), fill: 'none', stroke: '#C3C7CF', 'stroke-width': 0.9 }));
        defs.appendChild(pat); defs.appendChild(pat5);
        svg.appendChild(defs);
        // fondo blanco + grid
        svg.appendChild(el('rect', { x: 0, y: 0, width: vw, height: vh, fill: '#ffffff' }));
        if (opts.grid) { svg.appendChild(el('rect', { x: 0, y: 0, width: vw, height: vh, fill: 'url(#ecxgrid5-' + id + ')', 'data-ecx-grid': '1' })); }
        var gShapes = el('g', { 'data-ecx-shapes': '1' });
        var gOverlay = el('g', { 'data-ecx-overlay': '1' }); // NO se exporta
        svg.appendChild(gShapes); svg.appendChild(gOverlay);
        root.appendChild(svg);

        var s = {
            id: id, root: root, svg: svg, defs: defs, gShapes: gShapes, gOverlay: gOverlay,
            scene: [], sel: null, tool: 'select', color: '#1B1B1E', opts: opts, vw: vw, vh: vh,
            undo: [], redo: [], drag: null, nextId: 0, readonly: !!root.dataset.readonly, onTool: null,
            pages: [], pageIdx: 0
        };
        editors[id] = s;

        // Multipagina: el valor puede ser un SVG suelto (legacy = 1 pagina) o {"v":1,"pages":[svg,...]}.
        s.pages = parseValueToPages(s, existingSvg);
        s.pageIdx = 0; s.scene = s.pages[0].scene; s.nextId = s.pages[0].nextId;
        render(s);

        if (!s.readonly) {
            svg.addEventListener('pointerdown', function (e) { onDown(s, e); });
            svg.addEventListener('pointermove', function (e) { onMove(s, e); });
            svg.addEventListener('pointerup', function (e) { onUp(s, e); });
            svg.addEventListener('pointercancel', function (e) { onUp(s, e); });
        }
    }

    // ---- Cargar un SVG -> escena (array de shapes). No toca el estado global; devuelve la escena. ----
    function parseSvgToScene(s, svgStr) {
        var scene = [];
        try {
            var doc = new DOMParser().parseFromString(svgStr, 'image/svg+xml');
            var container = doc.querySelector('[data-ecx-shapes]');
            if (container) {
                // Se iteran los hijos DIRECTOS y se despacha: un <g data-ecx-kind> es una forma predefinida
                // (no se re-parsean sus hijos primitivos); lo demas son primitivas sueltas.
                Array.prototype.slice.call(container.children).forEach(function (n) {
                    var tag = n.tagName.toLowerCase();
                    if (tag === 'g' && n.getAttribute('data-ecx-kind')) {
                        var gs = groupNodeToShape(s, n); if (gs) { scene.push(gs); }
                    } else if (['rect', 'ellipse', 'text', 'image', 'path'].indexOf(tag) >= 0) {
                        if (n.getAttribute('data-ecx-grid') || n.getAttribute('data-ecx-bg')) { return; }
                        var sh = nodeToShape(s, n); if (sh) { scene.push(sh); }
                    }
                });
            } else {
                // Legacy sin contenedor: primitivas sueltas (no habia formas predefinidas antes).
                doc.documentElement.querySelectorAll('rect,ellipse,text,image,path').forEach(function (n) {
                    if (n.getAttribute('data-ecx-grid') || n.getAttribute('data-ecx-bg')) { return; }
                    var sh = nodeToShape(s, n); if (sh) { scene.push(sh); }
                });
            }
        } catch (e) { /* svg invalido: escena vacia */ }
        return scene;
    }

    // <g data-ecx-kind> -> shape 'group' (reconstruye desde los data-attributes; ignora sus hijos SVG).
    function groupNodeToShape(s, n) {
        var kind = n.getAttribute('data-ecx-kind'); if (!SHAPES[kind]) { return null; }
        var sh = newShape(s, 'group'); sh.kind = kind;
        sh.w = parseFloat(n.getAttribute('data-ecx-w')) || SHAPES[kind].w;
        sh.h = parseFloat(n.getAttribute('data-ecx-h')) || SHAPES[kind].h;
        try { sh.dims = JSON.parse(n.getAttribute('data-ecx-dims') || '{}') || {}; } catch (e) { sh.dims = {}; }
        var tr = n.getAttribute('transform') || '';
        var mt = tr.match(/translate\(\s*(-?[\d.]+)[ ,]+(-?[\d.]+)/);
        sh.x = mt ? parseFloat(mt[1]) : 0; sh.y = mt ? parseFloat(mt[2]) : 0;
        var mr = tr.match(/rotate\(\s*(-?[\d.]+)/);
        sh.rot = mr ? parseFloat(mr[1]) : 0;
        var strokeEl = n.querySelector('[stroke]');
        sh.stroke = strokeEl ? (strokeEl.getAttribute('stroke') || '#1B1B1E') : '#1B1B1E';
        sh.sw = strokeEl ? (parseFloat(strokeEl.getAttribute('stroke-width')) || 2) : 2;
        return sh;
    }

    // Valor guardado -> lista de paginas [{scene, nextId}]. Compatibilidad: '<svg' = 1 pagina (legacy);
    // '{' con pages:[] = multipagina; vacio = 1 pagina en blanco.
    function parseValueToPages(s, value) {
        var v = (value || '').trim();
        if (!v) { return [{ scene: [], nextId: 0 }]; }
        var svgs = [];
        if (v.charAt(0) === '{') {
            try { var o = JSON.parse(v); if (o && Array.isArray(o.pages)) { svgs = o.pages; } } catch (e) { svgs = []; }
        }
        if (!svgs.length) { svgs = [v]; } // legacy: un SVG suelto (o fallback)
        return svgs.map(function (svg) { return { scene: parseSvgToScene(s, svg), nextId: 0 }; });
    }

    // ---- Paginas (multipagina): cada pagina es su propio lienzo; misma cuadricula/opts ----
    function savePage(s) { s.pages[s.pageIdx] = { scene: s.scene, nextId: s.nextId }; }
    function loadPage(s, i) {
        if (i < 0 || i >= s.pages.length) { return; }
        savePage(s);
        s.pageIdx = i; s.scene = s.pages[i].scene; s.nextId = s.pages[i].nextId;
        s.sel = null; s.undo = []; s.redo = []; s.drag = null;
        render(s);
    }
    function pageInfoStr(s) { return (s.pageIdx + 1) + '/' + s.pages.length; }
    function addPage(id) {
        var s = ed(id); if (!s) { return ''; }
        savePage(s);
        if (s.pages.length < (s.opts.maxPages || 20)) {
            s.pages.push({ scene: [], nextId: 0 });
            loadPage(s, s.pages.length - 1);
        }
        return pageInfoStr(s);
    }
    function deletePage(id) {
        var s = ed(id); if (!s) { return ''; }
        if (s.pages.length > 1) {
            s.pages.splice(s.pageIdx, 1);
            var ni = Math.min(s.pageIdx, s.pages.length - 1);
            s.pageIdx = ni; s.scene = s.pages[ni].scene; s.nextId = s.pages[ni].nextId;
            s.sel = null; s.undo = []; s.redo = []; s.drag = null; render(s);
        }
        return pageInfoStr(s);
    }
    function prevPage(id) { var s = ed(id); if (s) { loadPage(s, s.pageIdx - 1); return pageInfoStr(s); } return ''; }
    function nextPage(id) { var s = ed(id); if (s) { loadPage(s, s.pageIdx + 1); return pageInfoStr(s); } return ''; }
    function gotoPage(id, i) { var s = ed(id); if (s) { loadPage(s, (i | 0)); return pageInfoStr(s); } return ''; }
    function pageInfo(id) { var s = ed(id); return s ? pageInfoStr(s) : ''; }

    function nodeToShape(s, n) {
        var tag = n.tagName.toLowerCase();
        var stroke = n.getAttribute('stroke') || '#1B1B1E';
        var sw = parseFloat(n.getAttribute('stroke-width') || '2');
        var sh = null;
        if (tag === 'rect') {
            if (n.getAttribute('fill') && n.getAttribute('fill').indexOf('url(') === 0) { return null; } // fondo
            sh = newShape(s, 'rect'); sh.x = num(n, 'x'); sh.y = num(n, 'y'); sh.w = num(n, 'width'); sh.h = num(n, 'height'); sh.stroke = stroke; sh.sw = sw;
        } else if (tag === 'ellipse') {
            var cx = num(n, 'cx'), cy = num(n, 'cy'), rx = num(n, 'rx'), ry = num(n, 'ry');
            sh = newShape(s, 'ellipse'); sh.x = cx - rx; sh.y = cy - ry; sh.w = rx * 2; sh.h = ry * 2; sh.stroke = stroke; sh.sw = sw;
        } else if (tag === 'text') {
            sh = newShape(s, 'text'); sh.x = num(n, 'x'); sh.y = num(n, 'y'); sh.text = n.textContent || ''; sh.stroke = n.getAttribute('fill') || stroke; sh.fs = parseFloat(n.getAttribute('font-size') || '16');
        } else if (tag === 'image') {
            var href = n.getAttribute('href') || n.getAttributeNS('http://www.w3.org/1999/xlink', 'href');
            sh = newShape(s, 'image'); sh.x = num(n, 'x'); sh.y = num(n, 'y'); sh.w = num(n, 'width'); sh.h = num(n, 'height'); sh.href = href;
        } else if (tag === 'path') {
            sh = newShape(s, 'path'); sh.d = n.getAttribute('d') || ''; sh.stroke = stroke; sh.sw = sw; sh.pts = dToPts(sh.d);
        }
        if (sh) {
            // Recuperar la rotacion del transform rotate(deg ...), si la tiene.
            var tr = n.getAttribute('transform');
            var mm = tr && tr.match(/rotate\(\s*(-?\d+(?:\.\d+)?)/);
            if (mm) { sh.rot = parseFloat(mm[1]) || 0; }
        }
        return sh;
    }
    function num(n, a) { return parseFloat(n.getAttribute(a) || '0') || 0; }
    function dToPts(d) {
        var pts = []; (d.match(/-?\d+(\.\d+)?/g) || []).forEach(function (v, i, arr) { if (i % 2 === 0 && arr[i + 1] != null) { pts.push([parseFloat(v), parseFloat(arr[i + 1])]); } });
        return pts;
    }

    // ---- Exportar ----
    // Una escena -> SVG autocontenido (grid tenue + shapes; sin overlay de seleccion).
    function buildPageSvg(s, scene) {
        var out = el('svg', { xmlns: SVGNS, viewBox: '0 0 ' + s.vw + ' ' + s.vh, width: s.vw, height: s.vh });
        out.appendChild(s.defs.cloneNode(true));
        out.appendChild(el('rect', { x: 0, y: 0, width: s.vw, height: s.vh, fill: '#ffffff', 'data-ecx-bg': '1' }));
        if (s.opts.grid) { out.appendChild(el('rect', { x: 0, y: 0, width: s.vw, height: s.vh, fill: 'url(#ecxgrid5-' + s.id + ')', 'data-ecx-grid': '1' })); }
        var g = el('g', { 'data-ecx-shapes': '1' });
        scene.forEach(function (sh) { var n = shapeToNode(sh); if (n) { g.appendChild(n); } });
        out.appendChild(g);
        return new XMLSerializer().serializeToString(out);
    }
    // Valor a guardar: SIEMPRE el sobre multipagina {"v":1,"pages":[svg,...]} (aunque sea 1 pagina).
    // Al leer se acepta ademas el legacy (un SVG suelto). Type sigue = "Canvas".
    function getSvg(id) {
        var s = ed(id); if (!s) { return ''; }
        savePage(s);
        return JSON.stringify({ v: 1, pages: s.pages.map(function (p) { return buildPageSvg(s, p.scene); }) });
    }

    // ---- Comandos de la barra de herramientas (llamados desde Blazor) ----
    function setTool(id, tool) { var s = ed(id); if (s) { s.tool = tool; s.sel = (tool === 'select' ? s.sel : null); render(s); } }
    function setColor(id, color) { var s = ed(id); if (s) { s.color = color || '#1B1B1E'; var sel = s.scene.find(function (x) { return x.id === s.sel; }); if (sel) { pushUndo(s); sel.stroke = s.color; render(s); } } }
    function undo(id) { var s = ed(id); if (s && s.undo.length) { s.redo.push(snapshot(s)); restore(s, s.undo.pop()); } }
    function redo(id) { var s = ed(id); if (s && s.redo.length) { s.undo.push(snapshot(s)); restore(s, s.redo.pop()); } }
    function del(id) { var s = ed(id); if (s && s.sel) { pushUndo(s); s.scene = s.scene.filter(function (x) { return x.id !== s.sel; }); s.sel = null; render(s); } }
    function clearAll(id) { var s = ed(id); if (s) { pushUndo(s); s.scene = []; s.sel = null; render(s); } }
    // Carga una data-URL en un Image (promesa). Sirve para medir y reescalar antes de incrustar.
    function loadImageEl(dataUrl) {
        return new Promise(function (res, rej) {
            var im = new Image();
            im.onload = function () { res(im); };
            im.onerror = function () { rej(new Error('no se pudo decodificar la imagen')); };
            im.src = dataUrl;
        });
    }

    // Reencoda la imagen a JPEG a un lado maximo y una calidad dados (fondo blanco: JPEG no tiene alfa,
    // sin esto una imagen transparente saldria en negro). Devuelve la data-URL resultante.
    function encodeJpeg(im, maxDim, quality) {
        var w = im.naturalWidth || im.width, h = im.naturalHeight || im.height;
        var scale = Math.min(1, maxDim / Math.max(w, h));
        var tw = Math.max(1, Math.round(w * scale)), th = Math.max(1, Math.round(h * scale));
        var c = document.createElement('canvas'); c.width = tw; c.height = th;
        var ctx = c.getContext('2d');
        ctx.fillStyle = '#ffffff'; ctx.fillRect(0, 0, tw, th);
        ctx.drawImage(im, 0, 0, tw, th);
        return c.toDataURL('image/jpeg', quality);
    }

    // Compresion ADAPTATIVA: baja dimension y calidad por pasos hasta caer bajo un presupuesto de bytes
    // (~700 KB por imagen). Asi una foto muy pesada (o varias en el mismo croquis) siempre entra en el tope
    // de ~2 MB del guardado. A mayor peso original, mas se comprime. Una imagen ya pequena y liviana
    // (<500 KB y <=1600 px de lado) se conserva TAL CUAL (mantiene PNG nitido, con su transparencia).
    function prepImage(dataUrl) {
        return loadImageEl(dataUrl).then(function (im) {
            var w = im.naturalWidth || im.width, h = im.naturalHeight || im.height;
            if (!w || !h) { return dataUrl; }
            if (Math.max(w, h) <= 1600 && dataUrl.length < 500000) { return dataUrl; }

            var TARGET = 700000; // ~700 KB objetivo por imagen (longitud de la data-URL base64)
            // Pasos de (lado maximo, calidad), del mejor al mas comprimido.
            var steps = [[1600, 0.82], [1400, 0.75], [1200, 0.68], [1024, 0.6], [800, 0.52], [640, 0.45]];
            var best = null;
            for (var i = 0; i < steps.length; i++) {
                var out;
                try { out = encodeJpeg(im, steps[i][0], steps[i][1]); }
                catch (e) { return best || dataUrl; }
                best = out;
                if (out.length <= TARGET) { return out; }
            }
            return best || dataUrl; // no bajo del objetivo: se usa lo mas comprimido alcanzado
        }).catch(function () { return dataUrl; });
    }

    function addImage(id, dataUrl) {
        var s = ed(id); if (!s || !dataUrl) { return; }
        return prepImage(dataUrl).then(function (finalUrl) {
            var s2 = ed(id); if (!s2) { return; }
            pushUndo(s2);
            var w = Math.min(360, s2.vw * 0.4), h = w * 0.7;
            var sh = newShape(s2, 'image'); sh.x = r2((s2.vw - w) / 2); sh.y = r2((s2.vh - h) / 2); sh.w = r2(w); sh.h = r2(h); sh.href = finalUrl;
            s2.scene.push(sh); s2.sel = sh.id; s2.tool = 'select'; render(s2);
        });
    }

    return {
        init: init, getSvg: getSvg, setTool: setTool, setColor: setColor,
        undo: undo, redo: redo, del: del, addImage: addImage, clearAll: clearAll,
        addPage: addPage, deletePage: deletePage, prevPage: prevPage, nextPage: nextPage,
        gotoPage: gotoPage, pageInfo: pageInfo,
        addShape: addShape, togglePalette: togglePalette
    };
})();
