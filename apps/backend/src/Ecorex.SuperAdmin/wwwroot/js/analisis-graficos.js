// Graficos del modulo Analisis / libros de indicadores (ADR-0158 Ola 7a/7b) sobre Apache ECharts
// (lib/echarts/echarts.min.js, ya vendorizado; sin Node/npm). El servidor arma la "option" (AnalisisGraficos.cs)
// y aqui se completa lo que JSON no puede llevar:
//  - Colores como tokens del prototipo: "var(--t-blue)" y "var(--t-blue)/0.4" (con alfa) se resuelven contra
//    el tema ACTIVO del elemento; al cambiar claro/oscuro (html.dark) se repinta.
//  - __fmt (raiz): formato de numeros (numero|decimal|moneda|porcentaje) para tooltip, ejes y etiquetas.
//  - __kind (raiz): formateadores especiales (pie, funnel, treemap, pareto, calendar, spark).
//  - ResizeObserver: el grafico se reacomoda al cambiar el ancho del widget o abrir el modal.
//  - Ola 7c: clic en un elemento -> Blazor (eje/serie/valor/posicion) para filtrar la hoja o ver registros;
//    el elemento elegido queda resaltado y los demas atenuados.
//  - Paginas ESTATICAS (PDF por Puppeteer): elementos [data-aw-grafico] se pintan solos en SVG sin animacion.
window.ecorexAnalisisGrafico = (function () {
  'use strict';

  var charts = new Map(); // id -> { chart, el, raw, ro }
  var VAR_RE = /^var\((--[\w-]+)\)(?:\/([\d.]+))?$/;
  var MESES = ['ene', 'feb', 'mar', 'abr', 'may', 'jun', 'jul', 'ago', 'sep', 'oct', 'nov', 'dic'];

  // ---------------------------------------------------------------- colores

  function cssVar(el, name) {
    var v = getComputedStyle(el || document.documentElement).getPropertyValue(name);
    return v ? v.trim() : '';
  }

  function withAlpha(color, a) {
    if (!color) { return color; }
    if (color.charAt(0) === '#') {
      var h = color.slice(1);
      if (h.length === 3) { h = h.split('').map(function (c) { return c + c; }).join(''); }
      var n = parseInt(h.slice(0, 6), 16);
      return 'rgba(' + ((n >> 16) & 255) + ',' + ((n >> 8) & 255) + ',' + (n & 255) + ',' + a + ')';
    }
    var m = color.match(/^rgba?\(([^)]+)\)$/);
    if (m) {
      var p = m[1].split(',').map(function (s) { return s.trim(); });
      var base = p.length === 4 ? parseFloat(p[3]) : 1;
      return 'rgba(' + p[0] + ',' + p[1] + ',' + p[2] + ',' + (base * a).toFixed(3) + ')';
    }
    return color; // otros espacios de color: sin alfa (raro en los tokens --t-*)
  }

  function resolve(node, el) {
    if (typeof node === 'string') {
      var m = node.match(VAR_RE);
      if (!m) { return node; }
      var c = cssVar(el, m[1]) || '#888888';
      return m[2] ? withAlpha(c, parseFloat(m[2])) : c;
    }
    if (Array.isArray(node)) { return node.map(function (x) { return resolve(x, el); }); }
    if (node && typeof node === 'object') {
      var out = {};
      for (var k in node) { if (Object.prototype.hasOwnProperty.call(node, k)) { out[k] = resolve(node[k], el); } }
      return out;
    }
    return node;
  }

  // ---------------------------------------------------------------- numeros

  function num(v) {
    if (Array.isArray(v)) { v = v[v.length - 1]; }
    if (v === null || v === undefined || v === '' || v === '-') { return null; }
    var n = Number(v);
    return isNaN(n) ? null : n;
  }

  function fmt(v, f) {
    var n = num(v);
    if (n === null) { return '-'; }
    switch (f) {
      case 'decimal': return n.toLocaleString('es-CO', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
      case 'moneda': return '$ ' + Math.round(n).toLocaleString('es-CO');
      case 'porcentaje': return (Math.round(n * 10) / 10).toLocaleString('es-CO', { maximumFractionDigits: 1 }) + ' %';
      default: return (Math.round(n * 10) / 10).toLocaleString('es-CO', { maximumFractionDigits: 1 });
    }
  }

  function compact(v, f) {
    var n = num(v);
    if (n === null) { return ''; }
    var s = Math.abs(n) >= 10000
      ? n.toLocaleString('es-CO', { notation: 'compact', maximumFractionDigits: 1 })
      : fmt(n, f === 'decimal' ? 'decimal' : 'numero');
    return f === 'moneda' ? '$ ' + s : f === 'porcentaje' ? s + ' %' : s;
  }

  function esc(s) {
    return String(s === null || s === undefined ? '' : s)
      .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
  }

  function fecha(iso) {
    var p = String(iso).split('-');
    return p.length === 3 ? (parseInt(p[2], 10) + ' ' + MESES[parseInt(p[1], 10) - 1] + ' ' + p[0]) : String(iso);
  }

  function asArray(x) { return x === undefined || x === null ? [] : (Array.isArray(x) ? x : [x]); }

  // ---------------------------------------------------------------- formateadores por tipo

  function applyFormat(opt, el) {
    var f = opt.__fmt || 'numero';
    var kind = opt.__kind || '';
    var base = opt.baseOption || opt;
    var ink = cssVar(el, '--ink'), ink2 = cssVar(el, '--ink-2');

    base.textStyle = base.textStyle || {};
    base.textStyle.fontFamily = getComputedStyle(el).fontFamily;

    // Ejes de valor: numeros compactos.
    asArray(base.xAxis).concat(asArray(base.yAxis)).forEach(function (ax) {
      if (ax && ax.axisLabel && ax.axisLabel.__compacto) {
        ax.axisLabel.formatter = function (v) { return compact(v, f); };
      }
    });

    var series = asArray(base.series);
    series.forEach(function (s) {
      if (s.label && s.label.__fmt) {
        s.label.formatter = function (p) { return fmt(p.value, f); };
      }
      if (s.type === 'funnel' && s.label) {
        s.label.formatter = function (p) { return p.name + '  ' + fmt(p.value, f); };
      }
      if (s.type === 'treemap') {
        if (s.label) { s.label.formatter = function (p) { return p.name + '\n' + fmt(p.value, f); }; }
        if (s.upperLabel) { s.upperLabel.formatter = function (p) { return p.name + '  ' + fmt(p.value, f); }; }
      }
      if (s.type === 'gauge' && s.detail && s.detail.__fmt) {
        var d0 = asArray(s.data)[0] || {};
        var real = d0.__real;
        s.detail.formatter = function (v) { return fmt(real !== undefined ? real : v, f); };
      }
    });

    var tt = base.tooltip;
    if (tt && tt.show !== false) {
      tt.valueFormatter = function (v) { return fmt(v, f); };
      if (kind === 'pie') {
        tt.formatter = function (p) {
          return p.marker + ' ' + esc(p.name) + '<br/><b>' + fmt(p.value, f) + '</b> &middot; ' + p.percent + '%';
        };
      } else if (kind === 'funnel') {
        tt.formatter = function (p) { return p.marker + ' ' + esc(p.name) + '<br/><b>' + fmt(p.value, f) + '</b>'; };
      } else if (kind === 'treemap') {
        tt.formatter = function (p) {
          var ruta = p.treePathInfo && p.treePathInfo.length > 1
            ? p.treePathInfo.slice(1).map(function (x) { return x.name; }).join(' / ')
            : p.name;
          return esc(ruta) + '<br/><b>' + fmt(p.value, f) + '</b>';
        };
      } else if (kind === 'calendar') {
        tt.formatter = function (p) { return fecha(p.value[0]) + '<br/><b>' + fmt(p.value[1], f) + '</b>'; };
      } else if (kind === 'pareto') {
        tt.valueFormatter = undefined;
        tt.formatter = function (ps) {
          var rows = asArray(ps).map(function (p) {
            var val = p.seriesIndex === 1 ? fmt(p.value, 'porcentaje') : fmt(p.value, f);
            return p.marker + ' ' + esc(p.seriesName) + ': <b>' + val + '</b>';
          });
          return esc(asArray(ps)[0].axisValueLabel) + '<br/>' + rows.join('<br/>');
        };
      } else if (kind === 'spark') {
        tt.formatter = function (ps) {
          var p = asArray(ps)[0];
          return esc(p.axisValueLabel) + '<br/><b>' + fmt(p.value, f) + '</b>';
        };
      } else if (series.length && series[0].type === 'heatmap') {
        var xs = asArray(base.xAxis)[0], ys = asArray(base.yAxis)[0];
        tt.formatter = function (p) {
          var fila = ys && ys.data ? ys.data[p.value[1]] : '', col = xs && xs.data ? xs.data[p.value[0]] : '';
          return esc(fila) + ' / ' + esc(col) + '<br/><b>' + fmt(p.value[2], f) + '</b>';
        };
      }
    }

    // Dona: leyenda con valor y porcentaje (como "Uso por modulo" del prototipo).
    if (kind === 'pie' && base.legend && series.length) {
      var datos = asArray(series[0].data);
      var total = datos.reduce(function (a, d) { return a + (num(d.value) || 0); }, 0);
      var mapa = {};
      datos.forEach(function (d) { mapa[d.name] = num(d.value) || 0; });
      base.legend.formatter = function (name) {
        var v = mapa[name] || 0;
        var pct = total ? Math.round(v / total * 100) : 0;
        var corto = name.length > 22 ? name.slice(0, 21) + '...' : name;
        return '{n|' + corto + '}  {v|' + fmt(v, f) + ' \u00b7 ' + pct + '%}';
      };
      base.legend.textStyle = base.legend.textStyle || {};
      base.legend.textStyle.rich = {
        n: { color: ink, fontWeight: 600, fontSize: 12.5 },
        v: { color: ink2, fontWeight: 600, fontSize: 12, fontFamily: 'monospace' }
      };
    }

    return opt;
  }

  // ---------------------------------------------------------------- seleccion por clic (Ola 7c)

  var ATENUADO = 0.28;

  function nombreCat(data, i) {
    var d = asArray(data)[i];
    return d !== null && typeof d === 'object' ? d.value : d;
  }

  function ejeCategoria(b) {
    var xs = asArray(b.xAxis)[0], ys = asArray(b.yAxis)[0];
    if (xs && xs.type === 'category') { return xs; }
    if (ys && ys.type === 'category') { return ys; }
    return null;
  }

  function apagar(d, a) {
    var o = (d !== null && typeof d === 'object' && !Array.isArray(d)) ? Object.assign({}, d) : { value: d };
    o.itemStyle = Object.assign({}, o.itemStyle, { opacity: a });
    if (o.children) { o.children = o.children.map(function (c) { return apagar(c, a); }); }
    return o;
  }

  function valorDe(d) { return d !== null && typeof d === 'object' && !Array.isArray(d) ? d.value : d; }

  // Resalta el elemento elegido (sel = {eje, serie}) atenuando los demas; en lineas marca la categoria.
  function marcar(opt, sel) {
    if (!sel || !sel.eje) { return opt; }
    var b = opt.baseOption || opt;
    var xs = asArray(b.xAxis)[0], ys = asArray(b.yAxis)[0];
    var cat = ejeCategoria(b);
    var lineaMarcada = false;
    asArray(b.series).forEach(function (s) {
      var datos = asArray(s.data);
      if (!datos.length) { return; }
      if (s.type === 'heatmap' && s.coordinateSystem === 'calendar') {
        s.data = datos.map(function (d) { return String(valorDe(d)[0]) === sel.eje ? d : apagar(d, ATENUADO); });
      } else if (s.type === 'heatmap') {
        s.data = datos.map(function (d) {
          var v = valorDe(d);
          var ok = nombreCat(ys && ys.data, v[1]) === sel.eje && (!sel.serie || nombreCat(xs && xs.data, v[0]) === sel.serie);
          return ok ? d : apagar(d, ATENUADO);
        });
      } else if (s.type === 'pie' || s.type === 'funnel') {
        s.data = datos.map(function (d) { return d && d.name === sel.eje ? d : apagar(d, ATENUADO); });
      } else if (s.type === 'treemap') {
        s.data = datos.map(function (d) {
          if (!d || d.name !== sel.eje) { return apagar(d, ATENUADO); }
          if (!sel.serie || !d.children) { return d; }
          var o = Object.assign({}, d);
          o.children = d.children.map(function (c) { return c.name === sel.serie ? c : apagar(c, ATENUADO); });
          return o;
        });
      } else if ((s.type === 'bar' || s.type === 'line') && cat) {
        var otraSerie = !!sel.serie && s.name !== sel.serie;
        if (s.type === 'line') {
          if (otraSerie) {
            s.lineStyle = Object.assign({}, s.lineStyle, { opacity: ATENUADO });
            s.itemStyle = Object.assign({}, s.itemStyle, { opacity: ATENUADO });
            if (s.areaStyle) { s.areaStyle = Object.assign({}, s.areaStyle, { opacity: 0.06 }); }
          }
          if (!lineaMarcada && cat === xs) {
            lineaMarcada = true;
            s.markLine = {
              silent: true, symbol: 'none', label: { show: false },
              lineStyle: { type: 'dashed', width: 1.5, color: s.itemStyle && s.itemStyle.color || '#888' },
              data: [{ xAxis: sel.eje }]
            };
          }
          return;
        }
        s.data = datos.map(function (d, i) {
          return nombreCat(cat.data, i) === sel.eje && !otraSerie ? d : apagar(d, ATENUADO);
        });
      }
    });
    return opt;
  }

  // Click de ECharts -> {eje, serie, valor} segun el tipo de serie (null = no es un elemento de datos).
  function punto(chart, p) {
    if (!p || p.componentType !== 'series') { return null; }
    var opt = chart.getOption();
    var s = asArray(opt.series)[p.seriesIndex] || {};
    var v = p.value;
    if (s.type === 'gauge') { return null; }
    if (s.type === 'heatmap' && s.coordinateSystem === 'calendar') {
      return { eje: String(asArray(v)[0]), serie: null, valor: num(asArray(v)[1]) };
    }
    if (s.type === 'heatmap') {
      return {
        eje: String(nombreCat(asArray(opt.yAxis)[0] && asArray(opt.yAxis)[0].data, v[1])),
        serie: String(nombreCat(asArray(opt.xAxis)[0] && asArray(opt.xAxis)[0].data, v[0])),
        valor: num(v[2])
      };
    }
    if (s.type === 'treemap') {
      var ruta = (p.treePathInfo || []).slice(1).map(function (x) { return x.name; });
      if (!ruta.length) { return null; }
      return { eje: String(ruta[0]), serie: ruta.length > 1 ? String(ruta[1]) : null, valor: num(v) };
    }
    if (p.name === undefined || p.name === null || p.name === '') { return null; }
    return { eje: String(p.name), serie: p.seriesName ? String(p.seriesName) : null, valor: num(v) };
  }

  function build(el, raw, isStatic, sel, clicable) {
    var parsed = typeof raw === 'string' ? JSON.parse(raw) : raw;
    var opt = marcar(applyFormat(resolve(parsed, el), el), sel);
    var b = opt.baseOption || opt;
    if (isStatic) {
      b.animation = false;
    }
    if (!clicable) {
      asArray(b.series).forEach(function (s) { s.cursor = 'default'; });
    }
    return opt;
  }

  function leerSel(selJson) {
    if (!selJson) { return null; }
    try { return typeof selJson === 'string' ? JSON.parse(selJson) : selJson; } catch (x) { return null; }
  }

  // ---------------------------------------------------------------- ciclo de vida (Blazor interactivo)

  // dotnet (opcional): DotNetObjectReference con [JSInvokable] Tocado(eje, serie, valor, x, y).
  function init(id, json, dotnet, selJson) {
    if (typeof window.echarts === 'undefined') { return false; }
    var el = document.getElementById(id);
    if (!el) { return false; }
    dispose(id);
    var sel = leerSel(selJson);
    var chart = window.echarts.init(el, null, { renderer: 'canvas' });
    try { chart.setOption(build(el, json, false, sel, !!dotnet), true); } catch (e) { console.error('ecorexAnalisisGrafico', e); }
    var ro = null;
    if (typeof ResizeObserver !== 'undefined') {
      ro = new ResizeObserver(function () { try { chart.resize(); } catch (e) { /* noop */ } });
      ro.observe(el);
    }
    var entry = { chart: chart, el: el, raw: json, ro: ro, ref: dotnet || null, sel: sel };
    if (dotnet) {
      chart.on('click', function (p) {
        var pt = punto(chart, p);
        if (!pt || !entry.ref) { return; }
        // El menu del elemento reemplaza al tooltip (si no, quedan encimados). ECharts lo vuelve a mostrar
        // al terminar de procesar el mismo clic: se oculta tambien en el siguiente ciclo y cuando ya esta
        // el fondo del menu (que tapa el grafico, asi que no reaparece mientras el menu esta abierto).
        var ocultarTip = function () { try { chart.dispatchAction({ type: 'hideTip' }); } catch (x) { /* noop */ } };
        ocultarTip();
        setTimeout(ocultarTip, 0);
        setTimeout(ocultarTip, 150);
        var ev = p.event || {};
        var x = typeof ev.offsetX === 'number' ? ev.offsetX : 0;
        var y = typeof ev.offsetY === 'number' ? ev.offsetY : 0;
        entry.ref.invokeMethodAsync('Tocado', pt.eje, pt.serie, pt.valor, x, y).catch(function () { /* circuito cerrado */ });
      });
    }
    charts.set(id, entry);
    return true;
  }

  function update(id, json, selJson) {
    var e = charts.get(id);
    if (!e) { return init(id, json, null, selJson); }
    e.raw = json;
    e.sel = leerSel(selJson);
    try { e.chart.setOption(build(e.el, json, false, e.sel, !!e.ref), true); } catch (x) { console.error('ecorexAnalisisGrafico', x); }
    return true;
  }

  // Cambia solo el elemento resaltado (sin nuevos datos).
  function seleccionar(id, selJson) {
    var e = charts.get(id);
    if (!e) { return false; }
    e.sel = leerSel(selJson);
    try { e.chart.setOption(build(e.el, e.raw, false, e.sel, !!e.ref), true); } catch (x) { console.error('ecorexAnalisisGrafico', x); }
    return true;
  }

  function dispose(id) {
    var e = charts.get(id);
    if (!e) { return; }
    try { if (e.ro) { e.ro.disconnect(); } } catch (x) { /* noop */ }
    try { e.chart.dispose(); } catch (x) { /* noop */ }
    charts.delete(id);
  }

  // Cambio de tema (html.dark): repinta con los colores del tema nuevo.
  var oscuro = document.documentElement.classList.contains('dark');
  if (typeof MutationObserver !== 'undefined') {
    new MutationObserver(function () {
      var ahora = document.documentElement.classList.contains('dark');
      if (ahora === oscuro) { return; }
      oscuro = ahora;
      charts.forEach(function (e) {
        try { e.chart.setOption(build(e.el, e.raw, false, e.sel, !!e.ref), true); } catch (x) { /* noop */ }
      });
    }).observe(document.documentElement, { attributes: true, attributeFilter: ['class', 'data-theme'] });
  }

  // ---------------------------------------------------------------- paginas estaticas (PDF)

  function pintarEstaticos() {
    if (typeof window.echarts === 'undefined') { return; }
    var nodos = document.querySelectorAll('[data-aw-grafico]');
    for (var i = 0; i < nodos.length; i++) {
      var el = nodos[i];
      if (el.getAttribute('data-aw-pintado')) { continue; }
      el.setAttribute('data-aw-pintado', '1');
      try {
        var chart = window.echarts.init(el, null, { renderer: 'svg' });
        chart.setOption(build(el, el.getAttribute('data-aw-grafico'), true, null, false), true);
        // SVG escalable: si el ancho de impresion difiere del de pantalla, el grafico se ajusta sin cortarse.
        // OJO: ECharts posiciona el <svg> en absoluto dentro de su envoltorio, asi que el contenedor CONSERVA
        // su alto (con height:auto colapsaria a 0 y el grafico quedaria oculto); el svg llena la caja.
        var svg = el.querySelector('svg');
        if (svg) {
          var w = svg.getAttribute('width'), h = svg.getAttribute('height');
          if (w && h) {
            svg.setAttribute('viewBox', '0 0 ' + parseFloat(w) + ' ' + parseFloat(h));
            svg.setAttribute('preserveAspectRatio', 'xMidYMid meet');
            svg.style.width = '100%';
            svg.style.height = '100%';
            var wrap = svg.parentElement;
            if (wrap && wrap !== el) { wrap.style.width = '100%'; wrap.style.height = '100%'; }
          }
        }
      } catch (e) {
        console.error('ecorexAnalisisGrafico (estatico)', e);
      }
    }
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', pintarEstaticos);
  } else {
    pintarEstaticos();
  }

  return { init: init, update: update, seleccionar: seleccionar, dispose: dispose, pintarEstaticos: pintarEstaticos };
})();
