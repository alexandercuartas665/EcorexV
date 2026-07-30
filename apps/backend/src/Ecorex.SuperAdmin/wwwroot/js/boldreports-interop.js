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

  // El reporte se carga con openReportDefinition (RDL client-side): openReport(path) asume Report
  // Server y no aplica a nuestro almacen en BD. El serviceUrl sigue montado para recursos/preview.
  async function renderDesigner(elementId, serviceUrl, rdlUrl) {
    await ensureAssets(true);
    const el = window.jQuery("#" + elementId);
    if (!el.length) { return false; }
    el.boldReportDesigner({ serviceUrl: serviceUrl });
    const designer = el.data("boldReportDesigner");
    window.__ecorexDesigner = designer;

    if (designer && rdlUrl) {
      let rdl = null;
      try {
        const r = await fetch(rdlUrl, { credentials: "same-origin" });
        if (r.ok) { rdl = await r.text(); }
      } catch (e) { console.error("ecorexBold: no se pudo traer el RDL", e); }

      if (rdl) {
        // Esperar a que el diseniador termine de inicializar antes de inyectar el RDL.
        const div = document.getElementById(elementId);
        let tries = 0;
        const load = function () {
          tries++;
          if (div && div.childElementCount > 3 && typeof designer.openReportDefinition === "function") {
            try { designer.openReportDefinition(rdl); } catch (e) { console.error("openReportDefinition", e); }
          } else if (tries < 60) {
            setTimeout(load, 250);
          }
        };
        setTimeout(load, 500);
      }
    }

    return true;
  }

  // Guarda el RDL editado: saveReportDefinition serializa a XML y lo entrega en el callback; se
  // POSTea a nuestro endpoint (persistencia en BD por Id). Devuelve una promesa con el resultado.
  function saveDesigner(saveUrl) {
    return new Promise(function (resolve) {
      const d = window.__ecorexDesigner;
      if (!d || typeof d.saveReportDefinition !== "function") {
        resolve({ ok: false, error: "El diseniador no esta listo." });
        return;
      }

      let done = false;
      const finish = function (r) { if (!done) { done = true; resolve(r); } };

      try {
        d.saveReportDefinition(function () {
          const a = arguments[0];
          const rdl = (typeof a === "string") ? a : (a && (a.definition || a.reportDefinition || a.data)) || null;
          if (!rdl || rdl.indexOf("<Report") < 0) { finish({ ok: false, error: "El diseniador no devolvio RDL." }); return; }
          fetch(saveUrl, {
            method: "POST",
            credentials: "same-origin",
            headers: { "Content-Type": "application/xml" },
            body: rdl
          }).then(function (r) { finish({ ok: r.ok }); })
            .catch(function (e) { finish({ ok: false, error: String(e) }); });
        }, "XML");
      } catch (e) {
        finish({ ok: false, error: String(e) });
      }

      setTimeout(function () { finish({ ok: false, error: "Tiempo de espera agotado al guardar." }); }, 12000);
    });
  }

  function dispose(elementId) {
    try {
      const el = window.jQuery ? window.jQuery("#" + elementId) : null;
      if (el && el.length) { el.empty(); }
    } catch (e) { /* noop */ }
  }

  return { renderViewer, renderDesigner, saveDesigner, dispose };
})();
