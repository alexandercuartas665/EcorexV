/* Cerebro digital de la maqueta (ADR-0183): grafo de notas con ECharts (serie 'graph', layout 'force'), clic en los
   [[enlaces]] de la vista previa y autocompletado al escribir "[[" en el editor. Sin npm: usa window.echarts que ya
   carga App.razor. Solo ASCII. */
window.ecxMaquetaCerebro = (function () {
  'use strict';

  var charts = new WeakMap();
  var KIND = {
    note: { name: 'Nota', color: '#7c3aed' },
    page: { name: 'Pantalla', color: '#2563eb' },
    task: { name: 'Tarjeta', color: '#c77a06' },
    tag: { name: 'Etiqueta', color: '#16a34a' },
    missing: { name: 'Pendiente', color: '#94a3b8' },
    file: { name: 'Adjunto', color: '#64748b' }
  };
  var ORDER = ['note', 'page', 'task', 'tag', 'missing'];

  function cssVar(name, fallback) {
    try {
      var v = getComputedStyle(document.documentElement).getPropertyValue(name);
      return (v && v.trim()) || fallback;
    } catch (e) { return fallback; }
  }

  // ------------------------------------------------------------------ grafo

  function renderGraph(el, data, dotnet, opts) {
    if (!el || !window.echarts) { return false; }
    opts = opts || {};
    var chart = charts.get(el);
    if (!chart) {
      chart = window.echarts.init(el, null, { renderer: 'canvas' });
      charts.set(el, chart);
      var ro = new ResizeObserver(function () { try { chart.resize(); } catch (e) { } });
      ro.observe(el);
      el.__ecxRo = ro;
    }
    var ink = cssVar('--ink', '#1b1b1e');
    var line = cssVar('--line-2', '#d4d4d8');
    var nodes = (data && data.nodes) || [];
    var edges = (data && data.edges) || [];
    var compact = !!opts.compact;
    var many = nodes.length > 80;
    var focus = opts.focus || null;

    var seriesNodes = nodes.map(function (n) {
      var k = KIND[n.kind] ? n.kind : 'note';
      var size = Math.min(compact ? 26 : 44, (compact ? 7 : 9) + Math.sqrt(n.degree || 0) * (compact ? 4 : 6));
      var isFocus = focus && n.id === focus;
      return {
        id: n.id,
        name: n.label,
        category: ORDER.indexOf(k),
        symbolSize: isFocus ? size + 6 : size,
        value: n.degree || 0,
        kind: n.kind,
        key: n.key || '',
        noteId: n.noteId || '',
        itemStyle: k === 'missing'
          ? { color: 'rgba(148,163,184,.15)', borderColor: KIND.missing.color, borderType: 'dashed', borderWidth: 1.5 }
          : (isFocus ? { borderColor: ink, borderWidth: 2.5 } : undefined),
        label: { show: compact ? true : (!many || (n.degree || 0) >= 3 || isFocus) }
      };
    });

    chart.setOption({
      animationDurationUpdate: 300,
      tooltip: {
        trigger: 'item',
        confine: true,
        formatter: function (p) {
          if (p.dataType !== 'node') { return ''; }
          var k = KIND[p.data.kind] || KIND.note;
          var safe = String(p.data.name || '').replace(/[&<>"]/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]; });
          return '<b>' + safe + '</b><br><span style="color:' + k.color + '">' + k.name + '</span> &middot; ' + (p.data.value || 0) + ' conexiones';
        }
      },
      legend: compact ? { show: false } : {
        data: ORDER.map(function (k) { return KIND[k].name; }),
        bottom: 6, textStyle: { color: ink, fontSize: 12 }, icon: 'circle'
      },
      series: [{
        type: 'graph',
        layout: 'force',
        roam: true,
        draggable: true,
        data: seriesNodes,
        links: edges.map(function (e) { return { source: e.source, target: e.target }; }),
        categories: ORDER.map(function (k) { return { name: KIND[k].name, itemStyle: { color: KIND[k].color } }; }),
        force: compact
          ? { repulsion: 90, edgeLength: [30, 70], gravity: 0.12, friction: 0.2 }
          : { repulsion: many ? 140 : 220, edgeLength: [50, 130], gravity: 0.06, friction: 0.15 },
        label: { position: 'right', color: ink, fontSize: compact ? 10 : 12, formatter: '{b}', overflow: 'truncate', width: 160 },
        lineStyle: { color: line, width: 1, curveness: 0.05, opacity: 0.9 },
        emphasis: { focus: 'adjacency', label: { show: true }, lineStyle: { width: 2 } },
        scaleLimit: { min: 0.2, max: 5 }
      }]
    }, true);

    chart.off('click');
    chart.on('click', function (p) {
      if (p.dataType !== 'node' || !dotnet) { return; }
      dotnet.invokeMethodAsync('OnGraphNode', p.data.kind || '', p.data.key || '', p.data.noteId || '', p.data.name || '');
    });
    return true;
  }

  function disposeGraph(el) {
    if (!el) { return; }
    var chart = charts.get(el);
    if (chart) { try { chart.dispose(); } catch (e) { } charts.delete(el); }
    if (el.__ecxRo) { try { el.__ecxRo.disconnect(); } catch (e) { } el.__ecxRo = null; }
  }

  // ------------------------------------------------------------------ vista previa: clic en [[enlaces]] y #etiquetas

  function bindPreview(el, dotnet) {
    if (!el || el.__ecxBound) { return; }
    el.__ecxBound = true;
    el.addEventListener('click', function (ev) {
      var a = ev.target && ev.target.closest ? ev.target.closest('.mb-wl, .mb-tag') : null;
      if (!a || !el.contains(a)) { return; }
      ev.preventDefault();
      dotnet.invokeMethodAsync('OnBrainLink', a.getAttribute('data-kind') || '', a.getAttribute('data-key') || '');
    });
  }

  // ------------------------------------------------------------------ editor: autocompletado "[[", Ctrl+S

  var editors = new WeakMap();

  function bindEditor(ta, dotnet) {
    if (!ta) { return; }
    var st = editors.get(ta);
    if (st) { st.dotnet = dotnet; return; }
    st = { dotnet: dotnet, items: [], menu: null, open: false, sel: 0, start: -1, list: [] };
    editors.set(ta, st);

    var menu = document.createElement('div');
    menu.className = 'mb-ac';
    menu.style.display = 'none';
    ta.parentNode.appendChild(menu);
    st.menu = menu;

    function close() { st.open = false; menu.style.display = 'none'; }

    function query() {
      var pos = ta.selectionStart, text = ta.value.slice(0, pos);
      var open = text.lastIndexOf('[[');
      if (open < 0) { return null; }
      var between = text.slice(open + 2);
      if (between.indexOf(']]') >= 0 || between.indexOf('\n') >= 0 || between.indexOf('|') >= 0 || between.length > 80) { return null; }
      return { start: open + 2, q: between.toLowerCase() };
    }

    function render() {
      menu.innerHTML = '';
      st.list.forEach(function (it, i) {
        var row = document.createElement('div');
        row.className = 'mb-ac-item' + (i === st.sel ? ' on' : '');
        var k = document.createElement('span');
        k.className = 'mb-ac-kind k-' + it.kind;
        k.textContent = (KIND[it.kind] || KIND.note).name;
        var t = document.createElement('span');
        t.className = 'mb-ac-text';
        t.textContent = it.label;
        row.appendChild(k);
        row.appendChild(t);
        row.addEventListener('mousedown', function (e) { e.preventDefault(); pick(i); });
        menu.appendChild(row);
      });
      menu.style.display = st.list.length ? 'block' : 'none';
    }

    function update() {
      var q = query();
      if (!q) { close(); return; }
      st.start = q.start;
      var words = q.q.split(/\s+/).filter(Boolean);
      st.list = st.items.filter(function (it) {
        var l = (it.label + ' ' + (it.value || '')).toLowerCase();
        return words.every(function (w) { return l.indexOf(w) >= 0; });
      }).slice(0, 12);
      if (q.q && !st.items.some(function (it) { return it.label.toLowerCase() === q.q; })) {
        st.list.push({ kind: 'missing', label: 'Nueva nota: ' + ta.value.slice(q.start, ta.selectionStart).trim(), value: ta.value.slice(q.start, ta.selectionStart).trim() });
      }
      st.sel = 0;
      st.open = st.list.length > 0;
      render();
    }

    function pick(i) {
      var it = st.list[i];
      if (!it) { close(); return; }
      var value = it.value || it.label;
      var before = ta.value.slice(0, st.start), after = ta.value.slice(ta.selectionStart);
      if (after.indexOf(']]') === 0) { after = after.slice(2); }
      ta.value = before + value + ']]' + after;
      var caret = (before + value + ']]').length;
      ta.setSelectionRange(caret, caret);
      close();
      ta.dispatchEvent(new Event('input', { bubbles: true }));
      ta.focus();
    }

    ta.addEventListener('input', update);
    ta.addEventListener('click', update);
    ta.addEventListener('blur', function () { setTimeout(close, 150); });
    ta.addEventListener('keydown', function (e) {
      if ((e.ctrlKey || e.metaKey) && (e.key === 's' || e.key === 'S')) {
        e.preventDefault();
        ta.dispatchEvent(new Event('input', { bubbles: true }));
        st.dotnet.invokeMethodAsync('OnSaveShortcut');
        return;
      }
      if (!st.open) { return; }
      if (e.key === 'ArrowDown') { e.preventDefault(); st.sel = (st.sel + 1) % st.list.length; render(); }
      else if (e.key === 'ArrowUp') { e.preventDefault(); st.sel = (st.sel - 1 + st.list.length) % st.list.length; render(); }
      else if (e.key === 'Enter' || e.key === 'Tab') { e.preventDefault(); pick(st.sel); }
      else if (e.key === 'Escape') { e.preventDefault(); close(); }
    });
  }

  /** Candidatos del autocompletado: [{kind, label, value}] (value = lo que se inserta). */
  function setCandidates(ta, items) {
    var st = ta ? editors.get(ta) : null;
    if (st) { st.items = items || []; }
  }

  /** Inserta texto en el cursor del editor (y avisa a Blazor con un evento input). */
  function insert(ta, text, block) {
    if (!ta) { return; }
    var s = ta.selectionStart || 0, e = ta.selectionEnd || 0;
    // block: el texto va en su propia linea (adjuntos embebidos ![[...]]).
    if (block) {
      if (s > 0 && ta.value.charAt(s - 1) !== '\n') { text = '\n' + text; }
      if (e < ta.value.length && ta.value.charAt(e) !== '\n') { text = text + '\n'; }
    }
    ta.value = ta.value.slice(0, s) + text + ta.value.slice(e);
    var caret = s + text.length;
    ta.focus();
    ta.setSelectionRange(caret, caret);
    ta.dispatchEvent(new Event('input', { bubbles: true }));
  }

  function focusEnd(ta) {
    if (!ta) { return; }
    ta.focus();
    var n = ta.value.length;
    ta.setSelectionRange(n, n);
  }

  function confirmLeave(message) { return window.confirm(message); }

  return {
    renderGraph: renderGraph,
    disposeGraph: disposeGraph,
    renderGraphById: function (id, data, dotnet, opts) { return renderGraph(document.getElementById(id), data, dotnet, opts); },
    disposeGraphById: function (id) { disposeGraph(document.getElementById(id)); },
    bindPreview: bindPreview,
    bindEditor: bindEditor,
    setCandidates: setCandidates,
    insert: insert,
    focusEnd: focusEnd,
    confirm: confirmLeave
  };
})();
