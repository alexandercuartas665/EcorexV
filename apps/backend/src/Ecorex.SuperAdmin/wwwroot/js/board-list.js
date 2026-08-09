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
    }
};

// Reajusta el riel superior (ancho/visibilidad) cuando cambia el tamano de la ventana.
window.addEventListener('resize', function () {
    if (window.__ecorexListResizeT) { clearTimeout(window.__ecorexListResizeT); }
    window.__ecorexListResizeT = setTimeout(function () {
        if (window.ecorexBoardList) { window.ecorexBoardList.syncScroll(); }
    }, 120);
});
