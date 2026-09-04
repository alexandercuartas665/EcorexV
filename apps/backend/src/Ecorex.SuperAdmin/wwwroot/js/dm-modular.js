// Helper del Directorio Modular (Capa 8): al guardar con obligatorios pendientes, lleva el scroll a la
// seccion del primer campo faltante y le da foco (replica el comportamiento del prototipo).
window.dmScrollToField = function (secId, fldId) {
    try {
        var sec = document.getElementById(secId);
        if (sec && sec.scrollIntoView) { sec.scrollIntoView({ behavior: 'smooth', block: 'start' }); }
        var fld = document.getElementById(fldId);
        if (fld && fld.focus) { setTimeout(function () { try { fld.focus(); } catch (e) { } }, 250); }
    } catch (e) { /* no romper el guardado por un fallo de scroll */ }
};
