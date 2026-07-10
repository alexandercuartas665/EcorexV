// Arrastre de las cajas (tablas) del lienzo ER del Contenedor de datos.
// Blazor Server no puede hacer el drag por eventos servidor (latencia), asi que el
// movimiento se hace 100% en el cliente (pointerdown/move/up sobre el header de la caja)
// y al soltar se devuelve la posicion final a .NET via invokeMethodAsync('OnTableMoved').
// Delegacion a nivel document: sobrevive a los re-render de Blazor del lienzo.
window.ecorexDcCanvas = (function () {
  let dotnet = null;
  let wired = false;
  let drag = null;

  function nodeOf(t) { return t && t.closest ? t.closest('.dc-table-node') : null; }

  function onDown(e) {
    if (e.button !== 0) { return; }
    const head = e.target && e.target.closest ? e.target.closest('.dc-node-head') : null;
    if (!head) { return; }
    // No arrancar el drag si se toco un boton del header (Editar/Eliminar).
    if (e.target.closest('button')) { return; }
    const node = nodeOf(head);
    if (!node) { return; }
    const canvas = node.closest('.dc-canvas');
    if (!canvas) { return; }
    const nRect = node.getBoundingClientRect();
    drag = {
      node: node,
      canvas: canvas,
      id: node.getAttribute('data-table-id'),
      offX: e.clientX - nRect.left,
      offY: e.clientY - nRect.top
    };
    node.classList.add('dragging');
    try { node.setPointerCapture(e.pointerId); } catch (_) { }
    e.preventDefault();
  }

  function onMove(e) {
    if (!drag) { return; }
    const cRect = drag.canvas.getBoundingClientRect();
    let x = e.clientX - cRect.left - drag.offX + drag.canvas.scrollLeft;
    let y = e.clientY - cRect.top - drag.offY + drag.canvas.scrollTop;
    if (x < 0) { x = 0; }
    if (y < 0) { y = 0; }
    drag.node.style.left = x + 'px';
    drag.node.style.top = y + 'px';
  }

  function onUp() {
    if (!drag) { return; }
    const d = drag;
    drag = null;
    d.node.classList.remove('dragging');
    const x = parseFloat(d.node.style.left) || 0;
    const y = parseFloat(d.node.style.top) || 0;
    if (dotnet && d.id) {
      dotnet.invokeMethodAsync('OnTableMoved', d.id, x, y);
    }
  }

  function wire() {
    if (wired) { return; }
    wired = true;
    document.addEventListener('pointerdown', onDown, true);
    document.addEventListener('pointermove', onMove, true);
    document.addEventListener('pointerup', onUp, true);
    document.addEventListener('pointercancel', onUp, true);
  }

  // Descarga un archivo binario (ej. xlsx exportado) desde base64, sin librerias.
  function downloadBase64(filename, b64, mime) {
    try {
      const bin = atob(b64);
      const len = bin.length;
      const bytes = new Uint8Array(len);
      for (let i = 0; i < len; i++) { bytes[i] = bin.charCodeAt(i); }
      const blob = new Blob([bytes], { type: mime || 'application/octet-stream' });
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = filename || 'export.xlsx';
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      window.URL.revokeObjectURL(url);
      return true;
    } catch (e) { return false; }
  }

  return {
    init: function (ref) { dotnet = ref; wire(); },
    dispose: function () { dotnet = null; },
    downloadBase64: downloadBase64
  };
})();
