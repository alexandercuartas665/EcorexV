// Graficos del modulo Analisis / libros de indicadores (ADR-0158 Ola 7a/7b) sobre Apache ECharts
// (lib/echarts/echarts.min.js, ya vendorizado; sin Node/npm). El servidor arma la "option" (AnalisisGraficos.cs)
// y aqui se completa lo que JSON no puede llevar:
//  - Colores como tokens del prototipo: "var(--t-blue)" y "var(--t-blue)/0.4" (con alfa) se resuelven contra
//    el tema ACTIVO del elemento; al cambiar claro/oscuro (html.dark) se repinta.
//  - __fmt (raiz): formato de numeros (numero|decimal|moneda|porcentaje) para tooltip, ejes y etiquetas.
//  - __kind (raiz): formateadores especiales (pie, funnel, treemap, pareto, calendar, spark).
//  - ResizeObserver: el grafico se reacomoda al cambiar el ancho del widget o abrir el modal.
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

  function build(el, raw, isStatic) {
    var parsed = typeof raw === 'string' ? JSON.parse(raw) : raw;
    var opt = applyFormat(resolve(parsed, el), el);
    if (isStatic) {
      var b = opt.baseOption || opt;
      b.animation = false;
    }
    return opt;
  }

  // ---------------------------------------------------------------- ciclo de vida (Blazor interactivo)

  function init(id, json) {
    if (typeof window.echarts === 'undefined') { return false; }
    var el = document.getElementById(id);
    if (!el) { return false; }
    dispose(id);
    var chart = window.echarts.init(el, null, { renderer: 'canvas' });
    try { chart.setOption(build(el, json, false), true); } catch (e) { console.error('ecorexAnalisisGrafico', e); }
    var ro = null;
    if (typeof ResizeObserver !== 'undefined') {
      ro = new ResizeObserver(function () { try { chart.resize(); } catch (e) { /* noop */ } });
      ro.observe(el);
    }
    charts.set(id, { chart: chart, el: el, raw: json, ro: ro });
    return true;
  }

  function update(id, json) {
    var e = charts.get(id);
    if (!e) { return init(id, json); }
    e.raw = json;
    try { e.chart.setOption(build(e.el, json, false), true); } catch (x) { console.error('ecorexAnalisisGrafico', x); }
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
        try { e.chart.setOption(build(e.el, e.raw, false), true); } catch (x) { /* noop */ }
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
        chart.setOption(build(el, el.getAttribute('data-aw-grafico'), true), true);
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

  return { init: init, update: update, dispose: dispose, pintarEstaticos: pintarEstaticos };
})();
