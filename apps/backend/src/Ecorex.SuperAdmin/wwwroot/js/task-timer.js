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
