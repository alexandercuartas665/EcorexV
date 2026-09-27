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
export async function init(id, initialHtml) {
    await ensureTinyLoaded();
    // Si ya habia un editor sobre ese id, se elimina antes de recrear.
    destroy(id);
    const dark = document.documentElement.getAttribute('data-theme') === 'dark'
        || (window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches
            && document.documentElement.getAttribute('data-theme') !== 'light');
    await window.tinymce.init({
        selector: '#' + id,
        license_key: 'gpl',
        height: 460,
        menubar: false,
        branding: false,
        promotion: false,
        statusbar: true,
        plugins: 'lists link table code autolink pagebreak searchreplace',
        toolbar: 'undo redo | blocks | bold italic underline forecolor | '
            + 'alignleft aligncenter alignright | bullist numlist | '
            + 'table link pagebreak | removeformat code',
        skin: dark ? 'oxide-dark' : 'oxide',
        content_css: dark ? 'dark' : 'default',
        content_style: 'body{font-family:Arial,Helvetica,sans-serif;font-size:12pt;color:#111}',
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
