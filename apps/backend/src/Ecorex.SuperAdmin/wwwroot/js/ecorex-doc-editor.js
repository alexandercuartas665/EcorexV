// ============================================================================
// ecorex-doc-editor.js - Interop Blazor <-> TinyMCE para el EDITOR de plantillas
// de documento (configuracion, /plantillas-documentos) y, en olas siguientes, el
// editor de documentos de una tarea (Gestor Documental).
//
// TinyMCE 7 (GPL) se carga BAJO DEMANDA desde el CDN jsDelivr (no se versiona en
// el repo publico ~30MB). El editor escribe en un <textarea> oculto; Blazor lee
// el HTML con getContent(id) al guardar. init/getContent/setContent/destroy son
// idempotentes y tolerantes a llamadas repetidas del ciclo de vida de Blazor.
// ============================================================================

const TINY_SRC = 'https://cdn.jsdelivr.net/npm/tinymce@7.6.1/tinymce.min.js';
let _loading = null;

// Carga el UMD de TinyMCE una sola vez (expone window.tinymce).
function ensureTinyLoaded() {
    if (window.tinymce) { return Promise.resolve(); }
    if (_loading) { return _loading; }
    _loading = new Promise((resolve, reject) => {
        const s = document.createElement('script');
        s.src = TINY_SRC;
        s.referrerPolicy = 'origin';
        s.onload = () => resolve();
        s.onerror = () => reject(new Error('No se pudo cargar TinyMCE desde el CDN.'));
        document.head.appendChild(s);
    });
    return _loading;
}

// Inicializa el editor sobre el textarea #id con el HTML inicial dado.
// opts (opcional): { letter: true } muestra el area de edicion como una HOJA tipo carta
// (papel blanco centrado sobre un fondo gris) y mas alta; sin opts es el editor normal.
// El documento SIEMPRE se ve BLANCO (papel), sin importar el tema oscuro de la app.
export async function init(id, initialHtml, opts) {
    await ensureTinyLoaded();
    // Si ya habia un editor sobre ese id, se elimina antes de recrear.
    destroy(id);
    opts = opts || {};
    const letter = opts.letter === true;
    // Cuerpo del documento: papel blanco con texto oscuro (fijo, ignora el modo oscuro).
    const baseBody = 'font-family:Arial,Helvetica,sans-serif;font-size:12pt;color:#111;background:#ffffff';
    // En modo carta el <body> se pinta como una hoja centrada con margenes de documento, sobre
    // un "escritorio" gris; sin carta es el cuerpo blanco simple.
    const contentStyle = letter
        ? 'html{background:#e9e9ee}'
        + 'body{' + baseBody + ';max-width:720px;margin:26px auto;padding:90px 80px;'
        + 'min-height:960px;box-shadow:0 1px 12px rgba(0,0,0,.18);border-radius:2px}'
        : 'body{' + baseBody + ';padding:10px}';
    await window.tinymce.init({
        selector: '#' + id,
        license_key: 'gpl',
        height: letter ? 640 : 460,
        menubar: false,
        branding: false,
        promotion: false,
        statusbar: true,
        plugins: 'lists link table code autolink pagebreak searchreplace',
        toolbar: 'undo redo | blocks | bold italic underline forecolor | '
            + 'alignleft aligncenter alignright | bullist numlist | '
            + 'table link pagebreak | removeformat code',
        // Editor SIEMPRE en claro (papel blanco), aunque la app este en modo oscuro.
        skin: 'oxide',
        content_css: 'default',
        content_style: contentStyle,
    });
    const ed = window.tinymce.get(id);
    if (ed && typeof initialHtml === 'string') { ed.setContent(initialHtml); }
    return true;
}

// Devuelve el HTML actual del editor (o el valor del textarea si no arranco).
export function getContent(id) {
    const ed = window.tinymce && window.tinymce.get(id);
    if (ed) { return ed.getContent(); }
    const el = document.getElementById(id);
    return el ? el.value : '';
}

export function setContent(id, html) {
    const ed = window.tinymce && window.tinymce.get(id);
    if (ed) { ed.setContent(html || ''); }
}

// Inserta texto (un token {ns.clave}) en la posicion del cursor.
export function insertToken(id, token) {
    const ed = window.tinymce && window.tinymce.get(id);
    if (ed) { ed.insertContent(token); ed.focus(); }
}

export function destroy(id) {
    const ed = window.tinymce && window.tinymce.get(id);
    if (ed) { ed.remove(); }
}
