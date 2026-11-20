/* Agente del editor de maquetas de ECOREX (ADR-0180).
   El editor (js/maqueta-editor.js) lo inyecta DENTRO del iframe de la maqueta; como la maqueta se sirve en el mismo
   origen (/maqueta/{slug}/...), el editor lo llama directo (window.__ecxMaquetaAgent), sin postMessage.
   Hace lo que necesita el DOM de la maqueta: leer las metas maq-*, aplicar un tema (si la maqueta trae
   anotador.config.js con window.ANOTADOR_MANIFEST), seleccionar un elemento, capturar con html2canvas y resaltar
   un selector. La maqueta queda como HTML puro: nada de esto vive en el ZIP. Solo ASCII. */
(function () {
  'use strict';
  if (window.__ecxMaquetaAgent) { return; }

  var H2C_URL = '/lib/html2canvas/html2canvas.min.js';

  function mf() { return window.ANOTADOR_MANIFEST || {}; }
  function metaOf(name) { var m = document.querySelector('meta[name="maq-' + name + '"]'); return m ? (m.getAttribute('content') || '') : ''; }

  function contexto() {
    return {
      ruta: metaOf('route'),
      modulo: metaOf('modulo'),
      pantalla: metaOf('titulo') || (document.title || '').replace(/ - .*/, ''),
      origenApp: metaOf('origen'),
      handoff: metaOf('handoff')
    };
  }

  function temas() {
    var t = mf().temas || [];
    return t.map(function (x) { return { id: String(x.id), name: String(x.name || x.id) }; });
  }

  function aplicarTema(id) {
    var m = mf();
    if (!id || typeof m.aplicarTema !== 'function') { return false; }
    try { m.aplicarTema(document, id); return true; } catch (e) { return false; }
  }

  /* ---------- selector de elementos ---------- */
  var pick = { on: false, box: null, hover: null, onPicked: null, onCancel: null };

  function pickStart(onPicked, onCancel) {
    pick.onPicked = onPicked; pick.onCancel = onCancel;
    if (pick.on) { return; }
    pick.on = true;
    pick.box = document.createElement('div');
    pick.box.setAttribute('data-ecx-hl', '1');
    pick.box.style.cssText = 'position:absolute;z-index:2147483645;pointer-events:none;border:2px solid #7c3aed;' +
      'background:rgba(124,58,237,.10);border-radius:4px;display:none;';
    document.body.appendChild(pick.box);
    document.addEventListener('mousemove', onHover, true);
    document.addEventListener('click', onPick, true);
    document.addEventListener('keydown', onEsc, true);
    document.documentElement.style.cursor = 'crosshair';
  }

  function pickStop() {
    pick.on = false;
    document.removeEventListener('mousemove', onHover, true);
    document.removeEventListener('click', onPick, true);
    document.removeEventListener('keydown', onEsc, true);
    document.documentElement.style.cursor = '';
    var old = document.querySelector('[data-ecx-hl]');
    if (old) { old.remove(); }
    pick.box = null; pick.hover = null;
  }

  function onHover(e) {
    var t = e.target;
    if (!t || t === pick.hover || !pick.box || (t.getAttribute && t.getAttribute('data-ecx-hl'))) { return; }
    pick.hover = t;
    var r = t.getBoundingClientRect();
    pick.box.style.display = 'block';
    pick.box.style.left = (r.left + window.scrollX) + 'px';
    pick.box.style.top = (r.top + window.scrollY) + 'px';
    pick.box.style.width = r.width + 'px';
    pick.box.style.height = r.height + 'px';
  }

  function onEsc(e) {
    if (e.key !== 'Escape') { return; }
    e.preventDefault();
    pickStop();
    if (pick.onCancel) { pick.onCancel(); }
  }

  function onPick(e) {
    var t = e.target;
    if (!t || (t.getAttribute && t.getAttribute('data-ecx-hl'))) { return; }
    e.preventDefault();
    e.stopImmediatePropagation();
    pickStop();
    var r = t.getBoundingClientRect();
    var ctx = contexto();
    ctx.elemento = {
      tag: t.tagName.toLowerCase(),
      texto: (t.innerText || t.value || t.getAttribute('aria-label') || t.getAttribute('title') || '').trim().replace(/\s+/g, ' ').slice(0, 120),
      clases: (t.getAttribute('class') || '').trim().slice(0, 300),
      selector: cssSelector(t),
      rect: { x: Math.round(r.left), y: Math.round(r.top), w: Math.round(r.width), h: Math.round(r.height) }
    };
    var done = pick.onPicked;
    captura(t).then(function (dataUrl) { if (done) { done(ctx, dataUrl, null); } })
      .catch(function (err) { if (done) { done(ctx, null, String((err && err.message) || err)); } });
  }

  function cssSelector(node) {
    if (!node || node.nodeType !== 1) { return ''; }
    if (node.id) { return '#' + node.id; }
    var parts = [], n = node, depth = 0;
    while (n && n.nodeType === 1 && n.tagName.toLowerCase() !== 'body' && depth < 6) {
      var seg = n.tagName.toLowerCase();
      var cls = ((n.getAttribute && n.getAttribute('class')) || '').trim().split(/\s+/).filter(Boolean)
        .filter(function (c) { return /^[A-Za-z_-][A-Za-z0-9_-]*$/.test(c); }).slice(0, 2);
      if (cls.length) { seg += '.' + cls.join('.'); }
      var parent = n.parentNode;
      if (parent && parent.children) {
        var tag = n.tagName;
        var same = Array.prototype.filter.call(parent.children, function (c) { return c.tagName === tag; });
        if (same.length > 1) { seg += ':nth-of-type(' + (same.indexOf(n) + 1) + ')'; }
      }
      parts.unshift(seg);
      if (n.id) { parts[0] = '#' + n.id; break; }
      n = n.parentNode; depth++;
    }
    return parts.join(' > ');
  }

  /* ---------- captura ---------- */
  function ensureH2C() {
    return new Promise(function (res, rej) {
      if (window.html2canvas) { return res(window.html2canvas); }
      var s = document.createElement('script');
      s.src = H2C_URL;
      s.onload = function () { res(window.html2canvas); };
      s.onerror = function () { rej(new Error('No se pudo cargar html2canvas')); };
      document.head.appendChild(s);
    });
  }

  function captura(node) {
    return ensureH2C().then(function (h2c) {
      var rect = node.getBoundingClientRect();
      var hl = document.createElement('div');
      hl.style.cssText = 'position:absolute;z-index:2147483646;pointer-events:none;border:3px solid #e11d48;' +
        'box-shadow:0 0 0 2px rgba(255,255,255,.9),0 0 0 5px rgba(225,29,72,.35);border-radius:4px;' +
        'left:' + (rect.left + window.scrollX - 3) + 'px;top:' + (rect.top + window.scrollY - 3) + 'px;' +
        'width:' + (rect.width + 6) + 'px;height:' + (rect.height + 6) + 'px;';
      document.body.appendChild(hl);
      var bg = getComputedStyle(document.body).backgroundColor;
      // Solo la parte visible (no la pagina entera): la captura queda liviana y muestra lo que el usuario vio.
      return h2c(document.body, {
        backgroundColor: bg && bg !== 'rgba(0, 0, 0, 0)' ? bg : '#ffffff',
        scale: Math.min(window.devicePixelRatio || 1, 1.5), useCORS: true, logging: false, imageTimeout: 0,
        x: window.scrollX, y: window.scrollY, width: window.innerWidth, height: window.innerHeight,
        windowWidth: document.documentElement.clientWidth, windowHeight: window.innerHeight
      }).then(function (canvas) { hl.remove(); return canvas.toDataURL('image/png'); },
        function (e) { hl.remove(); throw e; });
    });
  }

  /* ---------- resaltar un selector (ir a la solicitud) ---------- */
  function highlight(selector) {
    if (!selector) { return false; }
    var el = null;
    try { el = document.querySelector(selector); } catch (e) { el = null; }
    if (!el) { return false; }
    el.scrollIntoView({ block: 'center', behavior: 'smooth' });
    var r = el.getBoundingClientRect();
    var hl = document.createElement('div');
    hl.style.cssText = 'position:absolute;z-index:2147483646;pointer-events:none;border:3px solid #e11d48;' +
      'background:rgba(225,29,72,.08);border-radius:4px;transition:opacity .6s;' +
      'left:' + (r.left + window.scrollX - 3) + 'px;top:' + (r.top + window.scrollY - 3) + 'px;' +
      'width:' + (r.width + 6) + 'px;height:' + (r.height + 6) + 'px;';
    document.body.appendChild(hl);
    setTimeout(function () {
      var r2 = el.getBoundingClientRect();
      hl.style.left = (r2.left + window.scrollX - 3) + 'px';
      hl.style.top = (r2.top + window.scrollY - 3) + 'px';
    }, 500);
    setTimeout(function () { hl.style.opacity = '0'; }, 2600);
    setTimeout(function () { hl.remove(); }, 3300);
    return true;
  }

  window.__ecxMaquetaAgent = {
    hello: function () { var m = mf(); return { nombre: m.nombre || '', temas: temas() }; },
    context: contexto,
    theme: aplicarTema,
    pickStart: pickStart,
    pickStop: pickStop,
    highlight: highlight
  };
})();
