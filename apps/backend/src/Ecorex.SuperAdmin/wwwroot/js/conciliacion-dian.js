// Conciliacion DIAN: columnas fijas (freeze) configurables por el usuario.
// El usuario elige hasta que columna congelar; se guarda en localStorage (por navegador)
// y se aplica con position:sticky + left calculado desde el ancho real de cada columna.
window.ecorexCdCols = (function () {
    const KEY = 'cd-frozen-cols';

    function getFrozen() {
        try { const v = parseInt(localStorage.getItem(KEY) || '0', 10); return isNaN(v) ? 0 : v; }
        catch (e) { return 0; }
    }

    function setFrozen(n) {
        try { localStorage.setItem(KEY, String(n | 0)); } catch (e) { /* modo privado */ }
    }

    function clearCell(cell) {
        cell.style.position = '';
        cell.style.left = '';
        cell.style.zIndex = '';
        cell.classList.remove('cd-frozen', 'cd-frozen-edge');
    }

    // Aplica (o limpia) las primeras n columnas como fijas. n=0 => ninguna.
    function apply(n) {
        const table = document.querySelector('.cd-grid-wrap table.cd-grid');
        if (!table) return;
        const head = table.tHead;
        const headRow = head && head.rows[0];
        if (!headRow) return;

        const total = headRow.cells.length;
        n = Math.max(0, Math.min(n | 0, total));

        // Offset izquierdo acumulado tomando el ancho real de cada encabezado.
        const offsets = [];
        let left = 0;
        for (let i = 0; i < total; i++) {
            offsets[i] = left;
            left += headRow.cells[i].getBoundingClientRect().width;
        }

        const rows = [];
        if (head) { for (const r of head.rows) rows.push({ r, head: true }); }
        if (table.tBodies) { for (const tb of table.tBodies) for (const r of tb.rows) rows.push({ r, head: false }); }

        for (const item of rows) {
            const cells = item.r.cells;
            for (let i = 0; i < cells.length; i++) {
                const cell = cells[i];
                if (i < n) {
                    cell.style.position = 'sticky';
                    cell.style.left = offsets[i] + 'px';
                    cell.style.zIndex = item.head ? '6' : '2';
                    cell.classList.add('cd-frozen');
                    cell.classList.toggle('cd-frozen-edge', i === n - 1);
                } else {
                    clearCell(cell);
                }
            }
        }
    }

    return { getFrozen, setFrozen, apply };
})();
