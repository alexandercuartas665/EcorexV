// Pad de firma minimo (canvas -> PNG) para la pagina publica de decision /d/{token}.
// Sin dependencias. Soporta mouse y tactil. La firma se lee con dataUrl() al enviar.
export function init(canvas) {
    if (!canvas) { return; }
    const ctx = canvas.getContext('2d');
    ctx.lineWidth = 2.2;
    ctx.lineJoin = 'round';
    ctx.lineCap = 'round';
    ctx.strokeStyle = '#111';
    let drawing = false;
    let dirty = false;

    function pos(e) {
        const r = canvas.getBoundingClientRect();
        const t = (e.touches && e.touches.length) ? e.touches[0] : e;
        return [
            (t.clientX - r.left) * (canvas.width / r.width),
            (t.clientY - r.top) * (canvas.height / r.height)
        ];
    }
    function start(e) {
        drawing = true;
        const [x, y] = pos(e);
        ctx.beginPath();
        ctx.moveTo(x, y);
        if (e.cancelable) { e.preventDefault(); }
    }
    function move(e) {
        if (!drawing) { return; }
        const [x, y] = pos(e);
        ctx.lineTo(x, y);
        ctx.stroke();
        dirty = true;
        if (e.cancelable) { e.preventDefault(); }
    }
    function end() { drawing = false; }

    canvas.addEventListener('mousedown', start);
    canvas.addEventListener('mousemove', move);
    window.addEventListener('mouseup', end);
    canvas.addEventListener('touchstart', start, { passive: false });
    canvas.addEventListener('touchmove', move, { passive: false });
    canvas.addEventListener('touchend', end);

    canvas._isDirty = () => dirty;
    canvas._clear = () => {
        ctx.clearRect(0, 0, canvas.width, canvas.height);
        dirty = false;
    };
}

export function isEmpty(canvas) {
    return !(canvas && canvas._isDirty && canvas._isDirty());
}

export function clear(canvas) {
    if (canvas && canvas._clear) { canvas._clear(); }
}

export function dataUrl(canvas) {
    return canvas ? canvas.toDataURL('image/png') : null;
}
