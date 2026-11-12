// Conciliacion DIAN: soporte de la grilla (motor de tabla).
//  - apply(n): fija las primeras n columnas (sticky-left) calculando el offset desde el ancho real.
//              Ignora filas de grupo/subtotal (celdas con colspan) para no romper la alineacion.
//  - wireResize(dotnetRef): habilita el redimensionado de columnas por arrastre del tirador .cd-rz;
//              al soltar reporta el ancho final a .NET (OnColumnResized) para persistirlo por usuario.
window.ecorexCdCols = (function () {

    function clearCell(cell) {
        cell.style.position = '';
        cell.style.left = '';
        cell.style.zIndex = '';
        cell.classList.remove('cd-frozen', 'cd-frozen-edge');
    }

    function apply(n) {
        var table = document.querySelector('.cd-grid-wrap table.cd-grid');
        if (!table) return;
        var head = table.tHead;
        var headRow = head && head.rows[0];
        if (!headRow) return;

        var total = headRow.cells.length;
        n = Math.max(0, Math.min(n | 0, total));

        // Offset izquierdo acumulado tomando el ancho real de cada encabezado.
        var offsets = [];
        var left = 0;
        for (var i = 0; i < total; i++) {
            offsets[i] = left;
            left += headRow.cells[i].getBoundingClientRect().width;
        }

        var rows = [];
        if (head) { for (var h = 0; h < head.rows.length; h++) rows.push({ r: head.rows[h], head: true }); }
        if (table.tBodies) { for (var b = 0; b < table.tBodies.length; b++) { var tb = table.tBodies[b]; for (var k = 0; k < tb.rows.length; k++) rows.push({ r: tb.rows[k], head: false }); } }

        for (var ri = 0; ri < rows.length; ri++) {
            var item = rows[ri];
            var cells = item.r.cells;
            // Filas de grupo/vacias: menos celdas que el header (colspan) -> no congelar, limpiar por si acaso.
            if (cells.length !== total) {
                for (var c0 = 0; c0 < cells.length; c0++) clearCell(cells[c0]);
                continue;
            }
            for (var ci = 0; ci < cells.length; ci++) {
                var cell = cells[ci];
                if (ci < n) {
                    cell.style.position = 'sticky';
                    cell.style.left = offsets[ci] + 'px';
                    cell.style.zIndex = item.head ? '6' : '2';
                    cell.classList.add('cd-frozen');
                    if (ci === n - 1) cell.classList.add('cd-frozen-edge'); else cell.classList.remove('cd-frozen-edge');
                } else {
                    clearCell(cell);
                }
            }
        }
    }

    function wireResize(dotnetRef) {
        var table = document.querySelector('.cd-grid-wrap table.cd-grid');
        if (!table || !table.tHead) return;
        var handles = table.tHead.querySelectorAll('.cd-rz');
        for (var i = 0; i < handles.length; i++) {
            (function (h) {
                h.onmousedown = function (e) {
                    e.preventDefault(); e.stopPropagation();
                    var th = h.closest('th'); if (!th) return;
                    var key = th.getAttribute('data-col');
                    // El <th> es draggable (reordenar columnas): se desactiva durante el arrastre para que
                    // el drag nativo no robe el resize. Se restaura al soltar.
                    var prevDraggable = th.getAttribute('draggable');
                    th.setAttribute('draggable', 'false');
                    var startX = e.clientX, startW = th.getBoundingClientRect().width;
                    // La grilla usa table-layout:auto (desbordada); 'width' se ignora, 'min-width' SI manda.
                    function setW(w) { th.style.width = w + 'px'; th.style.minWidth = w + 'px'; }
                    function mv(ev) { setW(Math.max(60, Math.round(startW + (ev.clientX - startX)))); }
                    function up(ev) {
                        document.removeEventListener('mousemove', mv);
                        document.removeEventListener('mouseup', up);
                        if (prevDraggable === null) { th.removeAttribute('draggable'); } else { th.setAttribute('draggable', prevDraggable); }
                        var w = Math.max(60, Math.round(startW + (ev.clientX - startX)));
                        if (dotnetRef && key) { try { dotnetRef.invokeMethodAsync('OnColumnResized', key, w); } catch (_) { } }
                    }
                    document.addEventListener('mousemove', mv);
                    document.addEventListener('mouseup', up);
                };
                h.ondblclick = function (e) { // doble clic: autoajustar (ancho 0 => el server borra la preferencia)
                    e.preventDefault(); e.stopPropagation();
                    var th = h.closest('th'); if (!th) return; var key = th.getAttribute('data-col');
                    if (dotnetRef && key) { try { dotnetRef.invokeMethodAsync('OnColumnResized', key, 0); } catch (_) { } }
                };
            })(handles[i]);
        }
    }

    // Barra de scroll horizontal SUPERIOR (sobre los titulos) sincronizada con la grilla.
    //  - El espaciador replica el ancho real de la tabla para que la barra de arriba recorra lo mismo.
    //  - Mueve una barra y la otra la sigue (ida y vuelta) comparando scrollLeft para no entrar en bucle.
    //  - Se oculta sola cuando no hay desbordamiento. Idempotente: se puede invocar en cada render.
    function wireTopScroll() {
        var wrap = document.querySelector('.cd-grid-wrap');
        var top = document.querySelector('.cd-hscroll-top');
        if (!wrap || !top) return;
        var spacer = top.querySelector('.cd-hscroll-spacer');
        if (!spacer) return;

        function sync() {
            var table = wrap.querySelector('table.cd-grid');
            var w = table ? table.scrollWidth : wrap.scrollWidth;
            spacer.style.width = w + 'px';
            top.style.display = (w > wrap.clientWidth + 1) ? 'block' : 'none';
            if (top.scrollLeft !== wrap.scrollLeft) top.scrollLeft = wrap.scrollLeft;
        }

        if (!top._wired) {
            top._wired = true;
            top.addEventListener('scroll', function () {
                if (wrap.scrollLeft !== top.scrollLeft) wrap.scrollLeft = top.scrollLeft;
            });
            wrap.addEventListener('scroll', function () {
                if (top.scrollLeft !== wrap.scrollLeft) top.scrollLeft = wrap.scrollLeft;
            });
            if (window.ResizeObserver) {
                var ro = new ResizeObserver(function () { sync(); });
                ro.observe(wrap);
                top._ro = ro;
                top._roTable = null;
            }
            window.addEventListener('resize', sync);
        }
        // La tabla se regenera en cada render: re-observa la instancia vigente.
        if (top._ro) {
            var tbl = wrap.querySelector('table.cd-grid');
            if (tbl && tbl !== top._roTable) {
                try { if (top._roTable) top._ro.unobserve(top._roTable); top._ro.observe(tbl); top._roTable = tbl; } catch (_) { }
            }
        }
        sync();
    }

    // Tamano del viewport, para que el menu contextual (marcar fila) no se salga por el borde inferior/derecho.
    function viewport() { return { w: window.innerWidth || 0, h: window.innerHeight || 0 }; }

    // Texto actualmente seleccionado (para ofrecer "Copiar" en el menu de clic derecho sin perder el nativo).
    function selectionText() {
        try { var s = window.getSelection(); return s ? ('' + s).trim() : ''; } catch (_) { return ''; }
    }

    // Copia texto al portapapeles. Usa la API moderna y cae a execCommand si el navegador la bloquea.
    function copyText(text) {
        text = text || '';
        try {
            if (navigator.clipboard && navigator.clipboard.writeText) {
                navigator.clipboard.writeText(text);
                return true;
            }
        } catch (_) { }
        try {
            var ta = document.createElement('textarea');
            ta.value = text; ta.style.position = 'fixed'; ta.style.opacity = '0';
            document.body.appendChild(ta); ta.focus(); ta.select();
            var ok = document.execCommand('copy');
            document.body.removeChild(ta);
            return ok;
        } catch (_) { return false; }
    }

    return {
        apply: apply, wireResize: wireResize, wireTopScroll: wireTopScroll, viewport: viewport,
        selectionText: selectionText, copyText: copyText
    };
})();
