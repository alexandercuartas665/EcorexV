// Vista Lista de tableros (ADR-0065): barra de scroll horizontal SUPERIOR sincronizada con la
// inferior del contenedor de la tabla. La llama Blazor en OnAfterRenderAsync cuando la vista es
// Lista. Idempotente: actualiza ancho/visibilidad en cada llamada y engancha los listeners una vez
// por elemento (si Blazor recrea el nodo, se vuelven a enganchar).
window.ecorexBoardList = {
    syncScroll: function () {
        var scroller = document.querySelector('.tkl-scroll');
        var top = document.querySelector('.tkl-topscroll');
        if (!scroller || !top) { return; }
        var inner = top.querySelector('.tkl-topscroll-inner');
        var table = scroller.querySelector('table.tkl');
        if (!inner || !table) { return; }

        // El riel superior mide lo mismo que la tabla, para que su scrollbar coincida.
        inner.style.width = table.scrollWidth + 'px';
        // Solo se muestra si hay desbordamiento horizontal real.
        top.style.display = (table.scrollWidth > scroller.clientWidth + 1) ? '' : 'none';

        if (top.__ecorexSynced) { return; }
        top.__ecorexSynced = true;

        var lock = false;
        top.addEventListener('scroll', function () {
            if (lock) { return; }
            lock = true; scroller.scrollLeft = top.scrollLeft; lock = false;
        });
        scroller.addEventListener('scroll', function () {
            if (lock) { return; }
            lock = true; top.scrollLeft = scroller.scrollLeft; lock = false;
        });
    },

    // Redimensionar columnas de la Lista arrastrando el borde derecho del encabezado. El ancho se
    // persiste por tablero (invoca a .NET SaveColumnWidthAsync en el mouseup). Idempotente: cada
    // tirador se engancha una sola vez.
    initResizers: function (dotnetRef) {
        var table = document.querySelector('table.tkl');
        if (!table) { return; }
        var handles = table.querySelectorAll('.tkl-resizer');
        handles.forEach(function (h) {
            if (h.__wired) { return; }
            h.__wired = true;
            h.addEventListener('mousedown', function (e) {
                e.preventDefault();
                e.stopPropagation();
                var cols = table.querySelectorAll('colgroup > col');
                var ci = parseInt(h.getAttribute('data-ci'), 10);
                var key = h.getAttribute('data-key');
                var col = cols[ci];
                if (!col) { return; }

                // Congela los anchos actuales de todas las columnas ANTES de pasar a layout fijo, para
                // que no haya un salto de maquetacion al empezar a arrastrar.
                var dataRow = table.querySelector('tbody tr.tkl-row');
                if (dataRow && dataRow.children.length >= cols.length) {
                    for (var i = 0; i < cols.length; i++) {
                        if (!cols[i].style.width) {
                            cols[i].style.width = Math.round(dataRow.children[i].getBoundingClientRect().width) + 'px';
                        }
                    }
                }
                table.classList.add('tkl-fixed');

                var startX = e.clientX;
                var startW = h.parentElement.getBoundingClientRect().width;
                var finalW = Math.round(startW);
                document.body.style.userSelect = 'none';
                document.body.style.cursor = 'col-resize';

                function move(ev) {
                    finalW = Math.max(60, Math.round(startW + (ev.clientX - startX)));
                    col.style.width = finalW + 'px';
                    if (window.ecorexBoardList) { window.ecorexBoardList.syncScroll(); }
                }
                function up() {
                    document.removeEventListener('mousemove', move);
                    document.removeEventListener('mouseup', up);
                    document.body.style.userSelect = '';
                    document.body.style.cursor = '';
                    if (dotnetRef && key) {
                        try { dotnetRef.invokeMethodAsync('SaveColumnWidthAsync', key, finalW); } catch (err) { }
                    }
                }
                document.addEventListener('mousemove', move);
                document.addEventListener('mouseup', up);
            });
        });
    }
};

// Descarga un texto como archivo (usado por Export de formularios a JSON). Global para toda pagina.
window.ecorexDownloadText = function (filename, text) {
    try {
        var blob = new Blob([text], { type: 'application/json;charset=utf-8' });
        var url = URL.createObjectURL(blob);
        var a = document.createElement('a');
        a.href = url;
        a.download = filename || 'export.json';
        document.body.appendChild(a);
        a.click();
        setTimeout(function () { document.body.removeChild(a); URL.revokeObjectURL(url); }, 150);
    } catch (e) { }
};

// Reajusta el riel superior (ancho/visibilidad) cuando cambia el tamano de la ventana.
window.addEventListener('resize', function () {
    if (window.__ecorexListResizeT) { clearTimeout(window.__ecorexListResizeT); }
    window.__ecorexListResizeT = setTimeout(function () {
        if (window.ecorexBoardList) { window.ecorexBoardList.syncScroll(); }
    }, 120);
});
