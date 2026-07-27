// Captura Tier 2 de formularios (ola F6): firma en canvas, GPS y archivo->dataURL.
// Sin callbacks a .NET: las funciones devuelven valores que Blazor obtiene con InvokeAsync.
// Todas operan por id del elemento (document.getElementById) para no cablear ElementReference por campo.
window.ecorexFormCapture = (function () {
  function canvasOf(id) {
    const el = document.getElementById(id);
    return el && el.getContext ? el : null;
  }

  // Inicializa el trazo del canvas de firma (idempotente por marca en el elemento).
  function initSignature(id) {
    const canvas = canvasOf(id);
    if (!canvas || canvas.dataset.ecxInit === '1') { return; }
    canvas.dataset.ecxInit = '1';
    const ctx = canvas.getContext('2d');
    ctx.lineWidth = 2.2; ctx.lineCap = 'round'; ctx.strokeStyle = '#1B1B1E';
    let drawing = false, last = null;
    const pos = (e) => {
      const r = canvas.getBoundingClientRect();
      const t = e.touches ? e.touches[0] : e;
      return { x: t.clientX - r.left, y: t.clientY - r.top };
    };
    const down = (e) => { drawing = true; last = pos(e); e.preventDefault(); };
    const move = (e) => {
      if (!drawing) { return; }
      const p = pos(e);
      ctx.beginPath(); ctx.moveTo(last.x, last.y); ctx.lineTo(p.x, p.y); ctx.stroke();
      last = p; e.preventDefault();
    };
    const up = () => { drawing = false; };
    canvas.addEventListener('pointerdown', down);
    canvas.addEventListener('pointermove', move);
    window.addEventListener('pointerup', up);
  }

  function signatureData(id) {
    const canvas = canvasOf(id);
    return canvas ? canvas.toDataURL('image/png') : '';
  }

  function clearSignature(id) {
    const canvas = canvasOf(id);
    if (canvas) { canvas.getContext('2d').clearRect(0, 0, canvas.width, canvas.height); }
  }

  // Dibuja un trazo de prueba (para verificacion automatizada) y devuelve el dataURL.
  function testStroke(id) {
    const canvas = canvasOf(id);
    if (!canvas) { return ''; }
    const ctx = canvas.getContext('2d');
    ctx.lineWidth = 2.2; ctx.strokeStyle = '#1B1B1E';
    ctx.beginPath(); ctx.moveTo(10, 30); ctx.lineTo(60, 10); ctx.lineTo(110, 40); ctx.lineTo(160, 15); ctx.stroke();
    return canvas.toDataURL('image/png');
  }

  function geolocate() {
    return new Promise((resolve) => {
      if (!navigator.geolocation) { resolve('sin-geolocalizacion'); return; }
      navigator.geolocation.getCurrentPosition(
        (p) => resolve(p.coords.latitude.toFixed(5) + ', ' + p.coords.longitude.toFixed(5)),
        (e) => resolve('error: ' + e.message),
        { timeout: 8000 });
    });
  }

  // ---- Panel de lookup de CELDA de tabla (GridDetail) ----
  //
  // El panel de resultados de una celda-lookup es position:fixed (ver .dfr-gridlk-panel) para no
  // ser recortado por el scroller horizontal de la tabla. Aqui se ancla al input de su celda:
  // se abre justo debajo, alineado a la izquierda, y se voltea hacia arriba si no cabe abajo.
  // Se llama tras cada render con paneles abiertos y ante cualquier scroll/resize (por eso los
  // listeners con capture:true, que si atrapan el scroll del div interno).

  function placeOnePanel(panel) {
    const cell = panel.closest('.dfr-gridlk');
    const input = cell ? cell.querySelector('input') : null;
    if (!input) { return; }
    const r = input.getBoundingClientRect();
    const margen = 4;
    const anchoVp = document.documentElement.clientWidth;
    const altoVp = document.documentElement.clientHeight;

    // Ancho: al menos el del input, respetando el min-width del CSS; sin salirse del viewport.
    const ancho = Math.min(Math.max(r.width, 240), anchoVp - 16);
    panel.style.width = ancho + 'px';

    // Izquierda: alineado al input, clampeado para no salirse por la derecha.
    let left = r.left;
    if (left + ancho > anchoVp - 8) { left = anchoVp - 8 - ancho; }
    if (left < 8) { left = 8; }
    panel.style.left = left + 'px';

    // Arriba/abajo: debajo del input; si no cabe (queda mas espacio arriba), se voltea.
    const alto = panel.offsetHeight || 260;
    const espacioAbajo = altoVp - r.bottom;
    if (espacioAbajo < alto + margen && r.top > espacioAbajo) {
      panel.style.top = Math.max(8, r.top - alto - margen) + 'px';
    } else {
      panel.style.top = (r.bottom + margen) + 'px';
    }
  }

  let listenersOn = false;
  function positionCellPanels() {
    const panels = document.querySelectorAll('.dfr-gridlk-panel');
    panels.forEach(placeOnePanel);
    // Alta perezosa de listeners: solo la primera vez que hay un panel. capture:true atrapa el
    // scroll de contenedores internos (el scroller de la tabla) ademas del de la ventana.
    if (!listenersOn && panels.length > 0) {
      listenersOn = true;
      const reflow = () => {
        if (document.querySelector('.dfr-gridlk-panel')) { positionCellPanels(); }
      };
      window.addEventListener('scroll', reflow, true);
      window.addEventListener('resize', reflow);
    }
  }

  // Descarga un archivo desde base64 (usado por exportar Excel / plantilla de la tabla).
  function downloadBase64(filename, base64, mime) {
    const bin = atob(base64);
    const bytes = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) { bytes[i] = bin.charCodeAt(i); }
    const blob = new Blob([bytes], { type: mime || 'application/octet-stream' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename || 'descarga';
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    setTimeout(() => URL.revokeObjectURL(url), 1500);
  }

  return { initSignature, signatureData, clearSignature, testStroke, geolocate, positionCellPanels, downloadBase64 };
})();
