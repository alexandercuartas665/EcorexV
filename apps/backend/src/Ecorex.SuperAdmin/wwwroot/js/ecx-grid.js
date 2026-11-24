// Grilla comun EcxGrid (ADR-0207): soporte de navegador, por INSTANCIA (raiz .eg-root pasada desde .NET).
//  - sync(root, n, dotnetRef): fija las primeras n celdas de cada fila (sticky-left, ignora filas de
//    grupo/subtotal/detalle con colspan), cablea el redimensionado de columnas (.eg-rz) y la barra de
//    scroll horizontal superior. Idempotente: se invoca en cada render.
//  - viewport(): tamano de la ventana, para que el menu de columna no se salga por los bordes.
// Derivado del motor de tabla del Conciliador DIAN (conciliacion-dian.js), generalizado por instancia.
window.ecxGrid = (function () {

    function clearCell(cell) {
        cell.style.position = '';
        cell.style.left = '';
        cell.style.zIndex = '';
        cell.classList.remove('eg-frozen', 'eg-frozen-edge');
    }

    function freeze(table, n) {
        var head = table.tHead;
        var headRow = head && head.rows[0];
        if (!headRow) return;
        var total = headRow.cells.length;
        n = Math.max(0, Math.min(n | 0, total));

        var offsets = [];
        var left = 0;
        for (var i = 0; i < total; i++) {
            offsets[i] = left;
            left += headRow.cells[i].getBoundingClientRect().width;
        }

        var rows = [];
        for (var h = 0; h < head.rows.length; h++) rows.push({ r: head.rows[h], head: true });
        for (var b = 0; b < table.tBodies.length; b++) {
            var tb = table.tBodies[b];
            for (var k = 0; k < tb.rows.length; k++) rows.push({ r: tb.rows[k], head: false });
        }

        for (var ri = 0; ri < rows.length; ri++) {
            var item = rows[ri];
            var cells = item.r.cells;
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
                    cell.classList.add('eg-frozen');
                    if (ci === n - 1) cell.classList.add('eg-frozen-edge'); else cell.classList.remove('eg-frozen-edge');
                } else {
                    clearCell(cell);
                }
            }
        }
    }

    function wireResize(table, dotnetRef) {
        if (!table.tHead) return;
        var handles = table.tHead.querySelectorAll('.eg-rz');
        for (var i = 0; i < handles.length; i++) {
            (function (h) {
                h.onmousedown = function (e) {
                    e.preventDefault(); e.stopPropagation();
                    var th = h.closest('th'); if (!th) return;
                    var key = th.getAttribute('data-col');
                    var prevDraggable = th.getAttribute('draggable');
                    th.setAttribute('draggable', 'false');
                    var startX = e.clientX, startW = th.getBoundingClientRect().width;
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
                h.ondblclick = function (e) {
                    e.preventDefault(); e.stopPropagation();
                    var th = h.closest('th'); if (!th) return;
                    var key = th.getAttribute('data-col');
                    if (dotnetRef && key) { try { dotnetRef.invokeMethodAsync('OnColumnResized', key, 0); } catch (_) { } }
                };
            })(handles[i]);
        }
    }

    function wireTopScroll(root, wrap) {
        var top = root.querySelector('.eg-hscroll-top');
        if (!top) return;
        var spacer = top.querySelector('.eg-hscroll-spacer');
        if (!spacer) return;

        function sync() {
            var table = wrap.querySelector('table.eg-grid');
            var w = table ? table.scrollWidth : wrap.scrollWidth;
            spacer.style.width = w + 'px';
            top.style.display = (w > wrap.clientWidth + 1) ? 'block' : 'none';
            if (top.scrollLeft !== wrap.scrollLeft) top.scrollLeft = wrap.scrollLeft;
        }

        if (top._egWrap !== wrap) {
            top._egWrap = wrap;
            top.onscroll = function () { if (wrap.scrollLeft !== top.scrollLeft) wrap.scrollLeft = top.scrollLeft; };
            wrap.onscroll = function () { if (top.scrollLeft !== wrap.scrollLeft) top.scrollLeft = wrap.scrollLeft; };
            if (window.ResizeObserver) {
                if (top._egRo) { try { top._egRo.disconnect(); } catch (_) { } }
                top._egRo = new ResizeObserver(function () { sync(); });
                top._egRo.observe(wrap);
            }
        }
        sync();
    }

    function sync(root, n, dotnetRef) {
        if (!root) return;
        var wrap = root.querySelector('.eg-grid-wrap');
        if (!wrap) return;
        var table = wrap.querySelector('table.eg-grid');
        if (table) {
            freeze(table, n);
            wireResize(table, dotnetRef);
        }
        wireTopScroll(root, wrap);
    }

    function viewport() { return { w: window.innerWidth || 0, h: window.innerHeight || 0 }; }

    return { sync: sync, viewport: viewport };
})();
