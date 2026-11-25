// Visor PDF del modulo "Autorizacion facturas compras DIAN" (port del legacy WebForms).
// Render propio con PDF.js: paginas en canvas, zoom (+/-/Ancho) y arrastre con click sostenido.
// pdf.js se carga bajo demanda la primera vez que se abre un documento.
window.ecorexAutVisor = (function () {
    var pdfDoc = null;
    var escala = 0;          // 0 = ajustar al ancho del visor
    var ultimaEscala = 1;    // ultima escala efectiva aplicada
    var urlActual = '';      // documento ya dibujado (no re-descargar en re-render)
    var pdfjsReady = null;   // promesa de carga de la libreria
    var panWired = false;    // listeners globales de arrastre: una sola vez
    var activeScroll = null, drag = false, px = 0, py = 0, sx = 0, sy = 0;

    var PDFJS = 'https://cdnjs.cloudflare.com/ajax/libs/pdf.js/2.16.105/pdf.min.js';
    var PDFJS_WORKER = 'https://cdnjs.cloudflare.com/ajax/libs/pdf.js/2.16.105/pdf.worker.min.js';

    function ensurePdfjs() {
        if (window.pdfjsLib) { return Promise.resolve(); }
        if (pdfjsReady) { return pdfjsReady; }
        pdfjsReady = new Promise(function (res, rej) {
            var s = document.createElement('script');
            s.src = PDFJS;
            s.onload = function () { window.pdfjsLib.GlobalWorkerOptions.workerSrc = PDFJS_WORKER; res(); };
            s.onerror = function () { rej(new Error('no pdfjs')); };
            document.head.appendChild(s);
        });
        return pdfjsReady;
    }

    function cargar(url) {
        var cont = document.getElementById('acPdfPages');
        var scroll = document.getElementById('acPdfScroll');
        if (!cont || !scroll || !url) { return; }
        initPan();
        // Mismo documento ya dibujado: nada que rehacer (evita re-descargar en cada re-render de Blazor)
        if (urlActual === url && pdfDoc && cont.querySelectorAll('canvas').length > 0) { return; }
        urlActual = url;
        cont.innerHTML = "<div class='ac-pdf-loading'>Cargando documento…</div>";
        ensurePdfjs().then(function () {
            return pdfjsLib.getDocument(url).promise;
        }).then(function (pdf) {
            pdfDoc = pdf; escala = 0; render();
        }).catch(function () {
            cont.innerHTML = "<div class='ac-pdf-loading'>No fue posible cargar el documento.</div>";
        });
    }

    function render() {
        if (!pdfDoc) { return; }
        var cont = document.getElementById('acPdfPages');
        var scroll = document.getElementById('acPdfScroll');
        if (!cont || !scroll) { return; }
        cont.innerHTML = '';
        // Marcadores en orden: el render es asincrono y las paginas no deben desordenarse
        for (var i = 1; i <= pdfDoc.numPages; i++) {
            var h = document.createElement('div');
            h.id = 'acPag' + i;
            cont.appendChild(h);
        }
        pdfDoc.getPage(1).then(function (p1) {
            var base = p1.getViewport({ scale: 1 });
            ultimaEscala = escala > 0 ? escala : ((scroll.clientWidth - 30) / base.width);
            for (var n = 1; n <= pdfDoc.numPages; n++) {
                (function (num) {
                    pdfDoc.getPage(num).then(function (page) {
                        var vp = page.getViewport({ scale: ultimaEscala });
                        var c = document.createElement('canvas');
                        c.className = 'ac-pdf-page';
                        c.width = vp.width;
                        c.height = vp.height;
                        var holder = document.getElementById('acPag' + num);
                        if (holder) { holder.appendChild(c); }
                        page.render({ canvasContext: c.getContext('2d'), viewport: vp });
                    });
                })(n);
            }
        });
    }

    function zoomMas() { escala = Math.min(ultimaEscala * 1.25, 4); render(); }
    function zoomMenos() { escala = Math.max(ultimaEscala / 1.25, 0.4); render(); }
    function zoomAncho() { escala = 0; render(); }

    // Arrastre: click sostenido dentro del documento para moverlo. Los listeners de window
    // se enganchan UNA sola vez; el de mousedown se engancha por elemento (data-pan).
    function initPan() {
        var scroll = document.getElementById('acPdfScroll');
        if (!scroll) { return; }
        if (!panWired) {
            panWired = true;
            window.addEventListener('mousemove', function (e) {
                if (!drag || !activeScroll) { return; }
                activeScroll.scrollLeft = sx - (e.clientX - px);
                activeScroll.scrollTop = sy - (e.clientY - py);
            });
            window.addEventListener('mouseup', function () {
                drag = false;
                if (activeScroll) { activeScroll.classList.remove('arrastrando'); }
            });
        }
        if (scroll.dataset.pan !== '1') {
            scroll.dataset.pan = '1';
            scroll.addEventListener('mousedown', function (e) {
                drag = true; activeScroll = scroll;
                scroll.classList.add('arrastrando');
                px = e.clientX; py = e.clientY;
                sx = scroll.scrollLeft; sy = scroll.scrollTop;
                e.preventDefault();
            });
        }
    }

    // Re-ajusta el PDF al ancho si el usuario no fijo un zoom (cambio de vista o del ancho de la columna).
    function ajustar() { if (pdfDoc && escala === 0) { render(); } }

    // ---- Distribucion: vista "columna" del formulario (ancho ajustable) y aviso de ventana angosta ----
    var layoutRef = null, layoutMin = 930, layoutNarrow = null, winTimer = null, galObserver = null;

    // "Angosta" = la GALERIA (no la ventana) no tiene espacio para lista + visor + columna minima. Asi cuenta
    // tambien el menu lateral del sistema (abierto o plegado).
    function reportNarrow() {
        var g = document.getElementById('acGallery');
        var ancho = g ? g.clientWidth : window.innerWidth;
        var n = ancho < layoutMin;
        if (n === layoutNarrow) { return; }
        layoutNarrow = n;
        if (layoutRef) { layoutRef.invokeMethodAsync('OnViewportChanged', n).catch(function () { }); }
    }
    function onWinResize() {
        clearTimeout(winTimer);
        winTimer = setTimeout(function () { reportNarrow(); ajustar(); }, 150);
    }
    function initLayout(ref, minWidth) {
        layoutRef = ref;
        layoutMin = minWidth || 930;
        layoutNarrow = null;
        window.removeEventListener('resize', onWinResize);
        window.addEventListener('resize', onWinResize);
        if (galObserver) { galObserver.disconnect(); galObserver = null; }
        var g = document.getElementById('acGallery');
        if (g && window.ResizeObserver) { galObserver = new ResizeObserver(onWinResize); galObserver.observe(g); }
        reportNarrow();
    }
    function disposeLayout() {
        window.removeEventListener('resize', onWinResize);
        if (galObserver) { galObserver.disconnect(); galObserver = null; }
        layoutRef = null;
    }

    // Tirador en el borde izquierdo de la columna: arrastrar cambia --ac-panel-w en vivo; al soltar se avisa al
    // servidor (que lo guarda en la preferencia del usuario). Doble clic = ancho por defecto.
    function wireResize(minW, maxW, defW) {
        var h = document.getElementById('acPanelRz');
        var g = document.getElementById('acGallery');
        if (!h || !g || h.dataset.wired === '1') { return; }
        h.dataset.wired = '1';
        function avisar(w) {
            ajustar();
            if (layoutRef) { layoutRef.invokeMethodAsync('OnPanelResized', w).catch(function () { }); }
        }
        h.addEventListener('mousedown', function (e) {
            e.preventDefault();
            var rect = g.getBoundingClientRect();
            // Deja al menos 260 (lista) + 300 (visor) + separaciones.
            var max = Math.max(minW, Math.min(maxW, Math.round(rect.width - 260 - 300 - 28)));
            var ultimo = null;
            h.classList.add('on');
            document.body.style.cursor = 'col-resize';
            document.body.style.userSelect = 'none';
            function mv(ev) {
                var w = Math.round(rect.right - ev.clientX);
                w = Math.max(minW, Math.min(max, w));
                g.style.setProperty('--ac-panel-w', w + 'px');
                ultimo = w;
            }
            function up() {
                window.removeEventListener('mousemove', mv);
                window.removeEventListener('mouseup', up);
                h.classList.remove('on');
                document.body.style.cursor = '';
                document.body.style.userSelect = '';
                if (ultimo !== null) { avisar(ultimo); }
            }
            window.addEventListener('mousemove', mv);
            window.addEventListener('mouseup', up);
        });
        h.addEventListener('dblclick', function () {
            g.style.setProperty('--ac-panel-w', defW + 'px');
            avisar(defW);
        });
    }

    return {
        cargar: cargar, zoomMas: zoomMas, zoomMenos: zoomMenos, zoomAncho: zoomAncho,
        ajustar: ajustar, initLayout: initLayout, disposeLayout: disposeLayout, wireResize: wireResize
    };
})();
