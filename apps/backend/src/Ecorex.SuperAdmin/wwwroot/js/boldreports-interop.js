// Interop de Bold Reports (Motor de Reportes, Ola 2). Bold no tiene componente Blazor nativo: se monta
// el widget jQuery (boldReportViewer / boldReportDesigner) sobre un <div> via interop. Los assets de
// Bold pesan ~18 MB, asi que se cargan ON-DEMAND solo cuando se abre una pagina de reportes (no en
// App.razor global). Todo self-hosted desde /lib (sin CDN en runtime). El visor pide sus datos al
// controller Web API tenant-safe (reportServiceUrl); nunca una cadena de conexion.
window.ecorexBold = (function () {
  // Assets de Bold servidos desde su CDN oficial (patron documentado; sin Node/npm build). NO se
  // versionan en el repo publico (18 MB de JS propietario de Bold). La version debe coincidir con el
  // paquete BoldReports.Net.Core (14.1.14).
  const V = "14.1.14";
  const CDN = "https://cdn.boldreports.com/" + V;
  const JQUERY = "https://cdnjs.cloudflare.com/ajax/libs/jquery/3.6.0/jquery.min.js";
  const CSS_VIEWER = CDN + "/content/v2.0/tailwind-light/bold.report-viewer.min.css";
  const CSS_DESIGNER = CDN + "/content/v2.0/tailwind-light/bold.report-designer.min.css";
  const JS_COMMON = CDN + "/scripts/v2.0/common/bold.reports.common.min.js";
  const JS_WIDGETS = CDN + "/scripts/v2.0/common/bold.reports.widgets.min.js";
  const JS_VIEWER = CDN + "/scripts/v2.0/bold.report-viewer.min.js";
  const JS_DESIGNER = CDN + "/scripts/v2.0/bold.report-designer.min.js";
  let loading = null;

  function addCss(href) {
    if (document.querySelector('link[data-bold="' + href + '"]')) { return; }
    const link = document.createElement("link");
    link.rel = "stylesheet";
    link.href = href;
    link.setAttribute("data-bold", href);
    document.head.appendChild(link);
  }

  function addScript(src) {
    return new Promise((resolve, reject) => {
      const existing = document.querySelector('script[data-bold="' + src + '"]');
      if (existing) {
        if (existing.getAttribute("data-loaded")) { resolve(); }
        else { existing.addEventListener("load", () => resolve()); existing.addEventListener("error", reject); }
        return;
      }
      const s = document.createElement("script");
      s.src = src;
      s.setAttribute("data-bold", src);
      s.onload = () => { s.setAttribute("data-loaded", "1"); resolve(); };
      s.onerror = reject;
      document.head.appendChild(s);
    });
  }

  // Carga secuencial (el orden importa: jQuery -> common -> widgets -> viewer/designer).
  function ensureAssets(includeDesigner) {
    if (loading) { return loading; }
    addCss(CSS_VIEWER);
    if (includeDesigner) { addCss(CSS_DESIGNER); }

    loading = (async () => {
      // jQuery 3.6.0 solo si no hay uno compatible ya cargado.
      if (typeof window.jQuery === "undefined") {
        await addScript(JQUERY);
      }
      await addScript(JS_COMMON);
      await addScript(JS_WIDGETS);
      await addScript(JS_VIEWER);
      if (includeDesigner) { await addScript(JS_DESIGNER); }
    })();
    return loading;
  }

  async function renderViewer(elementId, reportPath, serviceUrl) {
    await ensureAssets(false);
    const el = window.jQuery("#" + elementId);
    if (!el.length) { return false; }
    el.boldReportViewer({
      reportPath: reportPath,
      reportServiceUrl: serviceUrl,
      // Local: el controller inyecta los datos ya filtrados por tenant (nunca conexion a BD).
      processingMode: "local"
    });
    return true;
  }

  async function renderDesigner(elementId, serviceUrl, reportPath) {
    await ensureAssets(true);
    const el = window.jQuery("#" + elementId);
    if (!el.length) { return false; }
    const opts = { serviceUrl: serviceUrl };
    el.boldReportDesigner(opts);
    if (reportPath) {
      const designer = el.data("boldReportDesigner");
      if (designer && typeof designer.openReport === "function") {
        try { designer.openReport(reportPath); } catch (e) { /* noop */ }
      }
    }
    return true;
  }

  function dispose(elementId) {
    try {
      const el = window.jQuery ? window.jQuery("#" + elementId) : null;
      if (el && el.length) { el.empty(); }
    } catch (e) { /* noop */ }
  }

  return { renderViewer, renderDesigner, dispose };
})();
