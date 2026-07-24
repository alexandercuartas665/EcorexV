// Gestor Documental: entrega de un archivo al navegador como descarga.
//
// El binario llega desde el servidor en base64 por el circuito de Blazor y NUNCA se expone la
// ruta fisica del archivo: asi la descarga pasa siempre por el servicio, que es quien comprueba
// el tenant y registra el consumo. Servir /uploads/... directo se saltaria ambas cosas.
window.ecorexDescargarArchivo = function (nombreArchivo, tipoMime, contenidoBase64) {
    try {
        const binario = atob(contenidoBase64);
        const bytes = new Uint8Array(binario.length);
        for (let i = 0; i < binario.length; i++) {
            bytes[i] = binario.charCodeAt(i);
        }

        const blob = new Blob([bytes], { type: tipoMime || 'application/octet-stream' });
        const url = URL.createObjectURL(blob);

        const enlace = document.createElement('a');
        enlace.href = url;
        enlace.download = nombreArchivo || 'archivo';
        document.body.appendChild(enlace);
        enlace.click();
        document.body.removeChild(enlace);

        // Se libera en el siguiente tick: revocar de inmediato aborta la descarga en algunos
        // navegadores porque el click aun no ha terminado de leer el blob.
        setTimeout(function () { URL.revokeObjectURL(url); }, 0);
    } catch (e) {
        console.error('ecorexDescargarArchivo:', e);
    }
};
