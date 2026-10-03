// Splitter del disenador de formularios: hace redimensibles las columnas del grid .fb-body arrastrando
// las barras .fb-gutter. Cada gutter ajusta una variable CSS (--fb-left / --fb-right / --fb-chat) que
// controla el ancho de su pista en el grid-template-columns. Vanilla JS (sin dependencias).

// Auto-scroll del chat del asistente de formularios: lleva el contenedor de mensajes (.fbc-msgs) al final,
// para que SIEMPRE se vea el ultimo mensaje. Lo invoca FormBuilderChatPanel en OnAfterRender.
window.fbChatScrollBottom = function () {
    var el = document.querySelector('.fbc-msgs');
    if (el) { el.scrollTop = el.scrollHeight; }
};

(function () {
    function clamp(v, min, max) { return Math.max(min, Math.min(max, v)); }

    function initGutter(body, gutter) {
        if (gutter.__fbInit) { return; }
        gutter.__fbInit = true;

        var cssVar = gutter.getAttribute('data-fb-var');            // ej. --fb-left
        var panelSel = gutter.getAttribute('data-fb-panel');        // ej. .fb-left
        var dir = parseFloat(gutter.getAttribute('data-fb-dir') || '1');   // +1 o -1
        var min = parseFloat(gutter.getAttribute('data-fb-min') || '180');
        var max = parseFloat(gutter.getAttribute('data-fb-max') || '640');
        if (!cssVar || !panelSel) { return; }

        gutter.addEventListener('pointerdown', function (e) {
            if (e.button !== 0) { return; }
            e.preventDefault();
            var panel = body.querySelector(panelSel);
            if (!panel) { return; }
            var startX = e.clientX;
            var startW = panel.getBoundingClientRect().width;
            gutter.classList.add('fb-drag');
            document.body.style.cursor = 'col-resize';
            try { gutter.setPointerCapture(e.pointerId); } catch (_) { }

            function move(ev) {
                var w = clamp(startW + dir * (ev.clientX - startX), min, max);
                body.style.setProperty(cssVar, w + 'px');
            }
            function up() {
                gutter.classList.remove('fb-drag');
                document.body.style.cursor = '';
                try { gutter.releasePointerCapture(e.pointerId); } catch (_) { }
                gutter.removeEventListener('pointermove', move);
                gutter.removeEventListener('pointerup', up);
                gutter.removeEventListener('pointercancel', up);
            }
            gutter.addEventListener('pointermove', move);
            gutter.addEventListener('pointerup', up);
            gutter.addEventListener('pointercancel', up);
        });

        // Doble clic en la barra: restablece la pista a su ancho por defecto.
        gutter.addEventListener('dblclick', function () {
            body.style.removeProperty(cssVar);
        });
    }

    window.fbSplitter = {
        // Idempotente: se puede llamar tras cada render; solo cablea las barras aun sin inicializar
        // (util cuando la columna del chat aparece/desaparece).
        init: function (bodySelector) {
            var body = document.querySelector(bodySelector || '.fb-body');
            if (!body) { return; }
            var gutters = body.querySelectorAll('.fb-gutter');
            for (var i = 0; i < gutters.length; i++) { initGutter(body, gutters[i]); }
        }
    };
})();
