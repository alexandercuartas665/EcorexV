// Cronometro del worklog del detalle de tarea (FASE 3).
// El estado (segundos) vive en JS para sobrevivir a los re-render de Blazor;
// el componente solo consulta getSeconds() al guardar el avance.
window.ecorexTaskTimer = (function () {
    let seconds = 0;
    let handle = null;
    let displayId = null;

    function fmt(total) {
        const h = Math.floor(total / 3600);
        const m = Math.floor((total % 3600) / 60);
        const s = total % 60;
        return [h, m, s].map(function (n) { return String(n).padStart(2, "0"); }).join(":");
    }

    function paint() {
        if (!displayId) { return; }
        const el = document.getElementById(displayId);
        if (el) { el.textContent = fmt(seconds); }
    }

    return {
        start: function (elementId) {
            displayId = elementId;
            if (handle) { return; }
            handle = setInterval(function () { seconds++; paint(); }, 1000);
            paint();
        },
        pause: function () {
            if (handle) { clearInterval(handle); handle = null; }
        },
        reset: function () {
            if (handle) { clearInterval(handle); handle = null; }
            seconds = 0;
            paint();
        },
        // Repinta el display tras un re-render de Blazor (que pisa el textContent).
        sync: function (elementId) {
            displayId = elementId;
            paint();
        },
        getSeconds: function () { return seconds; },
        isRunning: function () { return handle !== null; }
    };
})();

// Menu de nodo del diagrama de flujo (detalle de tarea): posiciona el menu como popover FIJO
// anclado al nodo, para que NO lo recorte el scroller del diagrama. Devuelve coords de pantalla
// ya CLAMPeadas para que el menu (ancho ~230, alto ~320) quepa en el viewport.
window.ecorexFlow = {
    anchorRect: function (nodeId) {
        var el = document.querySelector('[data-flow-anchor="' + nodeId + '"]');
        if (!el) { return null; }
        var r = el.getBoundingClientRect();
        var W = 230, H = 320, margin = 8;
        var vw = window.innerWidth, vh = window.innerHeight;
        // Debajo del boton por defecto; si no cabe abajo, arriba del nodo.
        var top = r.bottom + 6;
        if (top + H > vh - margin) { top = Math.max(margin, r.top - H - 6); }
        // Centrado en el boton; clamp horizontal.
        var left = r.left + (r.width / 2) - (W / 2);
        if (left + W > vw - margin) { left = vw - W - margin; }
        if (left < margin) { left = margin; }
        return { left: Math.round(left), top: Math.round(top) };
    }
};
