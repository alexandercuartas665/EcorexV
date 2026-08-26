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
        var o = { cell: 20, grid: true, height: 360 };
        try {
            if (optsJson) {
                var p = JSON.parse(optsJson);
                if (p && typeof p === 'object') {
                    if (p.cell > 0) { o.cell = Math.max(6, Math.min(120, p.cell | 0)); }
                    if (p.grid === false) { o.grid = false; }
                    if (p.height > 0) { o.height = Math.max(120, Math.min(2000, p.height | 0)); }
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

    // ---- Render de la escena al <svg> de edicion ----
    function el(tag, attrs) {
        var e = document.createElementNS(SVGNS, tag);
        if (attrs) { for (var k in attrs) { if (attrs[k] != null) { e.setAttribute(k, attrs[k]); } } }
        return e;
    }

    function shapeToNode(sh) {
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
            n.setAttributeNS('http://www.w3.org/1999/xlink', 'href', sh.href);
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
    function rotatable(sh) { return sh && (sh.type === 'rect' || sh.type === 'ellipse' || sh.type === 'image' || sh.type === 'text' || sh.type === 'path'); }
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
            // manija de redimension (esquina inf-der) para rect/ellipse/image
            if (selSh.type === 'rect' || selSh.type === 'ellipse' || selSh.type === 'image') {
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
        if (s.tool !== 'select' && s.tool !== 'pen') { s.tool = 'select'; if (s.onTool) { try { s.onTool('select'); } catch (e) { } } }
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
            undo: [], redo: [], drag: null, nextId: 0, readonly: !!root.dataset.readonly, onTool: null
        };
        editors[id] = s;

        if (existingSvg) { loadSvg(s, existingSvg); }
        render(s);

        if (!s.readonly) {
            svg.addEventListener('pointerdown', function (e) { onDown(s, e); });
            svg.addEventListener('pointermove', function (e) { onMove(s, e); });
            svg.addEventListener('pointerup', function (e) { onUp(s, e); });
            svg.addEventListener('pointercancel', function (e) { onUp(s, e); });
        }
    }

    // ---- Cargar un SVG previo -> escena ----
    function loadSvg(s, svgStr) {
        try {
            var doc = new DOMParser().parseFromString(svgStr, 'image/svg+xml');
            var g = doc.querySelector('[data-ecx-shapes]') || doc.documentElement;
            var nodes = g.querySelectorAll('rect,ellipse,text,image,path');
            nodes.forEach(function (n) {
                if (n.getAttribute('data-ecx-grid') || n.getAttribute('data-ecx-bg')) { return; }
                var sh = nodeToShape(s, n);
                if (sh) { s.scene.push(sh); }
            });
        } catch (e) { /* svg invalido: escena vacia */ }
    }

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

    // ---- Exportar SVG autocontenido (con grid tenue, sin overlay de seleccion) ----
    function getSvg(id) {
        var s = ed(id); if (!s) { return ''; }
        var out = el('svg', { xmlns: SVGNS, viewBox: '0 0 ' + s.vw + ' ' + s.vh, width: s.vw, height: s.vh });
        out.appendChild(s.defs.cloneNode(true));
        out.appendChild(el('rect', { x: 0, y: 0, width: s.vw, height: s.vh, fill: '#ffffff', 'data-ecx-bg': '1' }));
        if (s.opts.grid) { out.appendChild(el('rect', { x: 0, y: 0, width: s.vw, height: s.vh, fill: 'url(#ecxgrid5-' + id + ')', 'data-ecx-grid': '1' })); }
        var g = el('g', { 'data-ecx-shapes': '1' });
        s.scene.forEach(function (sh) { var n = shapeToNode(sh); if (n) { g.appendChild(n); } });
        out.appendChild(g);
        return new XMLSerializer().serializeToString(out);
    }

    // ---- Comandos de la barra de herramientas (llamados desde Blazor) ----
    function setTool(id, tool) { var s = ed(id); if (s) { s.tool = tool; s.sel = (tool === 'select' ? s.sel : null); render(s); } }
    function setColor(id, color) { var s = ed(id); if (s) { s.color = color || '#1B1B1E'; var sel = s.scene.find(function (x) { return x.id === s.sel; }); if (sel) { pushUndo(s); sel.stroke = s.color; render(s); } } }
    function undo(id) { var s = ed(id); if (s && s.undo.length) { s.redo.push(snapshot(s)); restore(s, s.undo.pop()); } }
    function redo(id) { var s = ed(id); if (s && s.redo.length) { s.undo.push(snapshot(s)); restore(s, s.redo.pop()); } }
    function del(id) { var s = ed(id); if (s && s.sel) { pushUndo(s); s.scene = s.scene.filter(function (x) { return x.id !== s.sel; }); s.sel = null; render(s); } }
    function clearAll(id) { var s = ed(id); if (s) { pushUndo(s); s.scene = []; s.sel = null; render(s); } }
    function addImage(id, dataUrl) {
        var s = ed(id); if (!s || !dataUrl) { return; }
        pushUndo(s);
        var w = Math.min(360, s.vw * 0.4), h = w * 0.7;
        var sh = newShape(s, 'image'); sh.x = r2((s.vw - w) / 2); sh.y = r2((s.vh - h) / 2); sh.w = r2(w); sh.h = r2(h); sh.href = dataUrl;
        s.scene.push(sh); s.sel = sh.id; s.tool = 'select'; render(s);
    }

    return {
        init: init, getSvg: getSvg, setTool: setTool, setColor: setColor,
        undo: undo, redo: redo, del: del, addImage: addImage, clearAll: clearAll
    };
})();
