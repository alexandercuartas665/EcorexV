// Lector de codigo de barras para el modulo movil (ADR pendiente). Usa la API nativa BarcodeDetector
// (Chrome Android / navegadores modernos) + getUserMedia con la camara trasera. Si no hay soporte,
// devuelve ok:false y la vista cae a la entrada manual. Al detectar el PRIMER codigo, detiene la
// camara e invoca OnScanned(valor) en el componente Blazor.
window.ecorexScan = (function () {
    let stream = null;
    let timer = null;
    let detector = null;
    let active = false;

    function supported() {
        return typeof window.BarcodeDetector !== 'undefined'
            && !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia);
    }

    // Arranca la camara en SEGUNDO PLANO y devuelve de inmediato (no bloquea el circuito Blazor esperando
    // el permiso de camara). Si la camara falla / se niega el permiso, invoca OnScanUnavailable para que la
    // vista caiga a la entrada manual. Al leer el primer codigo invoca OnScanned(valor).
    function start(videoEl, dotnetRef) {
        if (!supported()) { return { ok: false, reason: 'unsupported' }; }
        (async () => {
            try {
                const formats = ['code_128', 'ean_13', 'ean_8', 'code_39', 'code_93',
                    'upc_a', 'upc_e', 'itf', 'codabar', 'qr_code', 'data_matrix'];
                let use = formats;
                try {
                    const supp = await window.BarcodeDetector.getSupportedFormats();
                    if (Array.isArray(supp) && supp.length) { use = formats.filter(f => supp.includes(f)); }
                } catch (e) { /* usar la lista completa */ }
                detector = new window.BarcodeDetector({ formats: use });

                stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: { ideal: 'environment' } } });
                videoEl.setAttribute('playsinline', 'true');
                videoEl.srcObject = stream;
                await videoEl.play();
                active = true;

                const tick = async () => {
                    if (!active) { return; }
                    try {
                        const codes = await detector.detect(videoEl);
                        if (codes && codes.length && codes[0].rawValue) {
                            const val = codes[0].rawValue;
                            stop();
                            dotnetRef.invokeMethodAsync('OnScanned', val);
                            return;
                        }
                    } catch (e) { /* frame no legible: seguir */ }
                    timer = setTimeout(tick, 250);
                };
                timer = setTimeout(tick, 250);
            } catch (e) {
                stop();
                try { dotnetRef.invokeMethodAsync('OnScanUnavailable'); } catch (_) { }
            }
        })();
        return { ok: true };
    }

    function stop() {
        active = false;
        if (timer) { clearTimeout(timer); timer = null; }
        if (stream) { stream.getTracks().forEach(t => { try { t.stop(); } catch (e) { } }); stream = null; }
    }

    return { supported, start, stop };
})();
