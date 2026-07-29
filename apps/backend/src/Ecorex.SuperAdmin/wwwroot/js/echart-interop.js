// Interop de Apache ECharts para Blazor Server (Motor de Reportes y BI, ADR-0051, Ola 3).
// ECharts entra como .js ESTATICO (lib/echarts/echarts.min.js, sin Node/npm build); este modulo es el
// puente fino con .NET. Cada grafico se identifica por el id del div contenedor. La "option" (config
// JSON de ECharts) la arma el servidor desde el ReportDataSet tenant-safe (o la IA en la Ola 4): aqui
// solo se pinta. La interactividad (tooltip, zoom) es nativa de ECharts; el click puede volver a .NET.
window.ecorexEChart = (function () {
  const charts = new Map(); // id -> { chart, dotnet, onResize }

  function ensureLib() {
    return typeof window.echarts !== 'undefined';
  }

  function init(id, optionJson, dotnetRef) {
    if (!ensureLib()) { return false; }
    const el = document.getElementById(id);
    if (!el) { return false; }

    dispose(id); // idempotente: re-init limpio si Blazor re-renderiza

    const chart = window.echarts.init(el, null, { renderer: 'canvas' });
    const option = parse(optionJson);
    if (option) { chart.setOption(option, true); }

    const onResize = () => { try { chart.resize(); } catch (e) { /* noop */ } };
    window.addEventListener('resize', onResize);

    if (dotnetRef) {
      chart.on('click', (p) => {
        try {
          dotnetRef.invokeMethodAsync('OnPointClickJs', p && p.name != null ? String(p.name) : '', p && p.value != null ? String(p.value) : '');
        } catch (e) { /* noop */ }
      });
    }

    charts.set(id, { chart, dotnet: dotnetRef || null, onResize });
    return true;
  }

  function update(id, optionJson) {
    const entry = charts.get(id);
    if (!entry) { return false; }
    const option = parse(optionJson);
    if (option) {
      // notMerge=true: reemplaza la option completa (evita mezclar series viejas al re-consultar filtros).
      entry.chart.setOption(option, true);
    }
    return true;
  }

  function dispose(id) {
    const entry = charts.get(id);
    if (!entry) { return; }
    try { window.removeEventListener('resize', entry.onResize); } catch (e) { /* noop */ }
    try { entry.chart.dispose(); } catch (e) { /* noop */ }
    charts.delete(id);
  }

  function parse(optionJson) {
    if (!optionJson) { return null; }
    try { return typeof optionJson === 'string' ? JSON.parse(optionJson) : optionJson; }
    catch (e) { console.error('ecorexEChart: option JSON invalida', e); return null; }
  }

  return { init, update, dispose };
})();
