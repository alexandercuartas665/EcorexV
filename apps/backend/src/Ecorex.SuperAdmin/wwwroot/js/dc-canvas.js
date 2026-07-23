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

  // Escala actual del lienzo (transform:scale del wrapper). Se calcula por geometria real
  // (ancho renderizado / ancho logico) para no tener que parsear el transform ni pasar el zoom.
  function scaleOf(content) {
    if (!content) { return 1; }
    const w = content.offsetWidth;
    if (!w) { return 1; }
    const s = content.getBoundingClientRect().width / w;
    return (s && isFinite(s) && s > 0) ? s : 1;
  }

  function onDown(e) {
    if (e.button !== 0) { return; }
    const head = e.target && e.target.closest ? e.target.closest('.dc-node-head') : null;
    if (!head) { return; }
    // No arrancar el drag si se toco un boton del header (Editar/Eliminar).
    if (e.target.closest('button')) { return; }
    const node = nodeOf(head);
    if (!node) { return; }
    const content = node.closest('.dc-canvas-content');
    // Posicion LOGICA (sin escalar) de partida; el movimiento se acumula sobre ella / escala.
    drag = {
      node: node,
      content: content,
      scale: scaleOf(content),
      id: node.getAttribute('data-table-id'),
      startLeft: parseFloat(node.style.left) || 0,
      startTop: parseFloat(node.style.top) || 0,
      startClientX: e.clientX,
      startClientY: e.clientY
    };
    node.classList.add('dragging');
    try { node.setPointerCapture(e.pointerId); } catch (_) { }
    e.preventDefault();
  }

  function onMove(e) {
    if (!drag) { return; }
    const s = drag.scale || 1;
    let x = drag.startLeft + (e.clientX - drag.startClientX) / s;
    let y = drag.startTop + (e.clientY - drag.startClientY) / s;
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

  // Tamano visible (clientWidth/Height) del area con scroll del lienzo, para el "Ajustar" zoom.
  function viewport(sel) {
    const el = document.querySelector(sel || '.dc-canvas');
    return el ? [el.clientWidth, el.clientHeight] : [0, 0];
  }

  return {
    init: function (ref) { dotnet = ref; wire(); },
    dispose: function () { dotnet = null; },
    downloadBase64: downloadBase64,
    viewport: viewport
  };
})();
