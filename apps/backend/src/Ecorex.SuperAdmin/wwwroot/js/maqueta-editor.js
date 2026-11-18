/* Editor de maquetas de ECOREX (ADR-0180): puente entre la pagina Blazor MaquetaEditor y el iframe de la maqueta.
   La maqueta se sirve en el MISMO origen (/maqueta/{slug}/...), asi que el editor inyecta su agente
   (js/maqueta-agente.js) en cada pagina que carga el iframe y lo llama directo. Si la maqueta trae
   anotador.config.js (manifiesto de temas), se inyecta antes del agente. Solo ASCII. */
window.ecxMaquetaEditor = (function () {
  'use strict';
  var S = null;

  function win() { try { return S && S.frame ? S.frame.contentWindow : null; } catch (e) { return null; } }
  function doc() { try { return S && S.frame ? S.frame.contentDocument : null; } catch (e) { return null; } }
  function agent() { var w = win(); try { return w ? (w.__ecxMaquetaAgent || null) : null; } catch (e) { return null; } }
  function encodePath(file) { return String(file || '').split('/').map(encodeURIComponent).join('/'); }

  function currentFile() {
    var w = win();
    if (!w) { return ''; }
    try {
      var path = w.location.pathname || '';
      if (path.indexOf(S.base) !== 0) { return ''; }
      return decodeURIComponent(path.slice(S.base.length));
    } catch (e) { return ''; }
  }

  function inject(cb) {
    var d = doc(), w = win();
    if (!d || !d.body || !w) { cb(false); return; }
    if (agent()) { cb(true); return; }
    function addAgent() {
      var a = d.createElement('script');
      a.src = '/js/maqueta-agente.js?v=1';
      a.onload = function () { cb(!!agent()); };
      a.onerror = function () { cb(false); };
      d.body.appendChild(a);
    }
    var hasManifest = false;
    try { hasManifest = !!w.ANOTADOR_MANIFEST; } catch (e) { hasManifest = false; }
    if (S.manifest && !hasManifest) {
      var c = d.createElement('script');
      c.src = S.manifest;
      c.onload = addAgent;
      c.onerror = addAgent;
      d.body.appendChild(c);
    } else {
      addAgent();
    }
  }

  function onLoad() {
    if (!S) { return; }
    var mine = S;
    inject(function (ok) {
      if (S !== mine) { return; }
      var ag = ok ? agent() : null;
      var info = { temas: [] };
      try { if (ag) { info = ag.hello() || info; } } catch (e) { info = { temas: [] }; }
      if (ag && S.theme) { try { ag.theme(S.theme); } catch (e) { } }
      if (ag && S.picking) { startPick(); }
      if (ag && S.pendingHighlight) {
        var sel = S.pendingHighlight;
        S.pendingHighlight = null;
        setTimeout(function () { try { ag.highlight(sel); } catch (e) { } }, 200);
      }
      S.dn.invokeMethodAsync('OnFrameReady', currentFile(), JSON.stringify(info.temas || []), !!ag)
        .catch(function () { });
    });
  }

  function startPick() {
    var ag = agent();
    if (!ag) { return; }
    ag.pickStart(function (ctx, dataUrl, err) {
      if (!S) { return; }
      S.picking = false;
      ctx = ctx || {};
      ctx.archivoMaqueta = currentFile();
      S.dn.invokeMethodAsync('OnPicked', JSON.stringify(ctx), dataUrl || null, err || null).catch(function () { });
    }, function () {
      if (!S) { return; }
      S.picking = false;
      S.dn.invokeMethodAsync('OnPickCancel').catch(function () { });
    });
  }

  return {
    init: function (frameId, basePath, manifestUrl, dotnet) {
      if (S && S.frame) { S.frame.removeEventListener('load', S.handler); }
      var frame = document.getElementById(frameId);
      if (!frame) { return false; }
      S = { frame: frame, base: basePath, manifest: manifestUrl || null, dn: dotnet, theme: '', picking: false, pendingHighlight: null };
      S.handler = onLoad;
      frame.addEventListener('load', S.handler);
      return true;
    },
    load: function (file) { if (S) { S.frame.src = S.base + encodePath(file); } },
    reload: function () {
      if (!S) { return; }
      var w = win();
      try { w.location.reload(); } catch (e) { S.frame.src = S.frame.src; }
    },
    setTheme: function (id) {
      if (!S) { return; }
      S.theme = id || '';
      var ag = agent();
      if (ag) { try { ag.theme(S.theme); } catch (e) { } }
    },
    pick: function (on) {
      if (!S) { return false; }
      S.picking = !!on;
      var ag = agent();
      if (!ag) { return false; }
      if (on) { startPick(); } else { ag.pickStop(); }
      return true;
    },
    goto: function (file, selector) {
      if (!S) { return; }
      if (file && file !== currentFile()) {
        S.pendingHighlight = selector || null;
        S.frame.src = S.base + encodePath(file);
        return;
      }
      var ag = agent();
      if (ag && selector) { try { ag.highlight(selector); } catch (e) { } }
    },
    currentUrl: function () {
      var w = win();
      try { return w ? w.location.href : ''; } catch (e) { return ''; }
    },
    dispose: function () {
      if (S && S.frame) { S.frame.removeEventListener('load', S.handler); }
      S = null;
    }
  };
})();
