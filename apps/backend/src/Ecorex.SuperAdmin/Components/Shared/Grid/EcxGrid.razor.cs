using System.Globalization;
using System.Text.Json;
using ClosedXML.Excel;
using Ecorex.Application.Preferences;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Ecorex.SuperAdmin.Components.Shared.Grid;

/// <summary>
/// Grilla comun del producto (ADR-0207), extraida del motor de tabla del Conciliador DIAN: filtro Y/O
/// anidado, filtros guardados del equipo, ordenar multi-criterio, agrupar apilable con subtotales y total
/// general, columnas (mover/ocultar/ancho/fijas) y exportar a Excel lo que se ve. La vista de cada usuario
/// (columnas, orden, grupos, fijas, tamano de pagina) se guarda en BD por <see cref="TableKey"/>.
/// Trabaja EN MEMORIA sobre <see cref="Items"/>: la pagina entrega las filas ya acotadas por sus filtros
/// rapidos (pestanas, buscador) y la grilla hace el resto.
/// </summary>
public partial class EcxGrid<TItem> : ComponentBase, IAsyncDisposable
{
    [Inject] private IUserTablePrefsService Prefs { get; set; } = default!;
    [Inject] private ISavedFilterService SavedFilters { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private AuthenticationStateProvider Auth { get; set; } = default!;

    [Parameter, EditorRequired] public IReadOnlyList<TItem> Items { get; set; } = Array.Empty<TItem>();
    [Parameter, EditorRequired] public IReadOnlyList<GridColumn<TItem>> Columns { get; set; } = Array.Empty<GridColumn<TItem>>();
    /// <summary>Clave de las preferencias del usuario (UserTablePreference).</summary>
    [Parameter, EditorRequired] public string TableKey { get; set; } = "";
    /// <summary>Modulo de los filtros guardados del equipo (SavedFilter). Null = sin filtros guardados.</summary>
    [Parameter] public string? FilterModule { get; set; }
    [Parameter] public Func<TItem, object>? RowKey { get; set; }
    /// <summary>Detalle bajo la fila (p.ej. contactos de una empresa) cuando <see cref="IsExpanded"/> es true.</summary>
    [Parameter] public RenderFragment<TItem>? RowDetail { get; set; }
    [Parameter] public Func<TItem, bool>? IsExpanded { get; set; }
    [Parameter] public Func<TItem, string?>? RowClass { get; set; }
    /// <summary>Botones extra de la pagina, a la derecha de la barra.</summary>
    [Parameter] public RenderFragment? ToolbarExtra { get; set; }
    [Parameter] public string ExportName { get; set; } = "Tabla";
    [Parameter] public string EmptyText { get; set; } = "Sin registros.";
    [Parameter] public bool Loading { get; set; }
    [Parameter] public int DefaultPageSize { get; set; } = 50;

    private static readonly int[] PageSizes = { 25, 50, 100, 200, 500 };

    // ---- Estado de la vista (persistido por usuario) ----
    private List<string> _order = new();
    private HashSet<string> _hidden = new();
    private HashSet<string> _known = new();
    private Dictionary<string, int> _widths = new();
    private List<SortSpec> _sorts = new();
    private List<string> _groups = new();
    private int _frozen;
    private int _pageSize;
    private int _page;

    // ---- Filtro ----
    private GridFilterNode _filterRoot = GridFilterNode.NewGroup("and");
    private bool _filterOpen;
    private string? _saveName;
    private string? _activeFilterName;
    private List<SavedFilterDto> _savedFilters = new();
    private string? _userName;

    // ---- UI ----
    private bool _ready;
    private string? _openMenu;
    private string? _dragCol;
    private string? _colMenuKey;
    private double _colMenuX, _colMenuY;
    private bool _exporting;
    private string? _msg;
    private ElementReference _root;
    private DotNetObjectReference<EcxGrid<TItem>>? _selfRef;

    // ---- Resultado (filas agrupadas) ----
    private Dictionary<string, GridColumn<TItem>> _cols = new(StringComparer.Ordinal);
    private IReadOnlyList<GridColumn<TItem>>? _lastColumns;
    private List<GridFilterField> _filterFields = new();
    private Dictionary<string, GridFilterField> _filterByKey = new(StringComparer.Ordinal);
    private List<GItem> _gridItems = new();
    private GItem? _grand;
    private int _filteredCount;
    private int _lastItemsCount = -1;

    private sealed class SortSpec { public string K { get; set; } = ""; public int Dir { get; set; } = 1; }

    private enum GKind { Group, Data, Subtotal }

    private sealed class GItem
    {
        public GKind Kind;
        public int Level;
        public string? Label;
        public int Count;
        public TItem? Row;
        public Dictionary<string, decimal>? Sums;
        public bool Grand;
        public int Seq;
    }

    private sealed class PrefsDto
    {
        public List<string>? order { get; set; }
        public List<string>? hidden { get; set; }
        public List<string>? known { get; set; }
        public Dictionary<string, int>? widths { get; set; }
        public List<SortSpec>? sorts { get; set; }
        public List<string>? groups { get; set; }
        public int frozen { get; set; }
        public int pageSize { get; set; }
    }

    // ======================= Ciclo de vida =======================

    protected override async Task OnInitializedAsync()
    {
        _pageSize = DefaultPageSize;
        try
        {
            var json = await Prefs.GetAsync(TableKey);
            if (!string.IsNullOrWhiteSpace(json) && JsonSerializer.Deserialize<PrefsDto>(json) is { } p)
            {
                _order = p.order ?? new();
                _hidden = new HashSet<string>(p.hidden ?? new());
                _known = new HashSet<string>(p.known ?? new());
                _widths = p.widths ?? new();
                _sorts = p.sorts ?? new();
                _groups = p.groups ?? new();
                _frozen = Math.Max(0, p.frozen);
                if (p.pageSize > 0) { _pageSize = p.pageSize; }
            }
        }
        catch { /* preferencias corruptas o BD no disponible: vista por defecto */ }

        if (FilterModule is not null)
        {
            await LoadSavedFiltersAsync();
            try { _userName = (await Auth.GetAuthenticationStateAsync()).User.Identity?.Name; }
            catch { _userName = null; }
        }
        _ready = true;
    }

    protected override void OnParametersSet()
    {
        if (!_ready) { return; }
        if (!ReferenceEquals(Columns, _lastColumns))
        {
            _lastColumns = Columns;
            _cols = Columns.GroupBy(c => c.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            _filterFields = Columns.Where(c => c.Filterable).Select(c => new GridFilterField(c.Key, c.Label, c.Type, c.Section)).ToList();
            _filterByKey = _filterFields.ToDictionary(f => f.Key, StringComparer.Ordinal);
            EnsureLayout();
        }
        // Si la pagina cambio el conjunto (pestana, buscador, filtro rapido) se vuelve a la primera pagina.
        if (Items.Count != _lastItemsCount) { _lastItemsCount = Items.Count; _page = 0; }
        Rebuild();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_ready) { return; }
        _selfRef ??= DotNetObjectReference.Create(this);
        try { await JS.InvokeVoidAsync("ecxGrid.sync", _root, Math.Min(_frozen, VisibleCols().Count()), _selfRef); }
        catch (JSDisconnectedException) { }
        catch (InvalidOperationException) { /* prerender o circuito cerrandose */ }
    }

    public ValueTask DisposeAsync()
    {
        _selfRef?.Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    /// <summary>Concilia la vista guardada con las columnas actuales: claves nuevas al final (ocultas si la
    /// columna lo pide), claves que ya no existen fuera.</summary>
    private void EnsureLayout()
    {
        _order = _order.Where(_cols.ContainsKey).Distinct().ToList();
        foreach (var c in Columns)
        {
            if (!_order.Contains(c.Key)) { _order.Add(c.Key); }
            if (_known.Add(c.Key) && c.DefaultHidden) { _hidden.Add(c.Key); }
        }
        _sorts.RemoveAll(s => !_cols.ContainsKey(s.K));
        _groups.RemoveAll(g => !_cols.TryGetValue(g, out var c) || !c.Groupable);
    }

    // ======================= Columnas =======================

    private GridColumn<TItem>? ColByKey(string key) => _cols.TryGetValue(key, out var c) ? c : null;

    private IEnumerable<GridColumn<TItem>> VisibleCols()
        => _order.Select(ColByKey).Where(c => c is not null && !_hidden.Contains(c.Key)).Select(c => c!);

    private string LabelOf(string key) => ColByKey(key)?.Label ?? key;

    private int ColSpanAll => Math.Max(1, VisibleCols().Count());

    private GridFilterField? FieldOf(string key) => _filterByKey.TryGetValue(key, out var f) ? f : null;

    private int FilterCount => GridFilter.CountConditions(_filterRoot, FieldOf);

    // ======================= Construccion de la vista =======================

    private void Rebuild()
    {
        var src = FilterCount > 0
            ? Items.Where(it => GridFilter.Evaluate(_filterRoot, FieldOf, k => ColByKey(k)?.Value(it), k => ColByKey(k)?.TextOf(it) ?? "")).ToList()
            : Items.ToList();
        var items = new List<GItem>();
        BuildItems(src, 0, items);
        for (var i = 0; i < items.Count; i++) { items[i].Seq = i; }
        _gridItems = items;
        _filteredCount = src.Count;
        _grand = src.Count > 0 && (_groups.Count > 0 || HasVisibleSums)
            ? new GItem { Kind = GKind.Subtotal, Count = src.Count, Sums = SumsOf(src), Grand = true, Seq = -1 }
            : null;
        ClampPage();
    }

    private void BuildItems(List<TItem> rows, int level, List<GItem> outl)
    {
        if (level >= _groups.Count)
        {
            foreach (var r in Sorted(rows)) { outl.Add(new GItem { Kind = GKind.Data, Row = r, Level = level }); }
            return;
        }
        var col = ColByKey(_groups[level]);
        if (col is null) { BuildItems(rows, level + 1, outl); return; }
        var groups = rows.GroupBy(r => GroupText(col, r))
            .OrderBy(g => g.Key == SinDato ? 1 : 0)
            .ThenBy(g => SortValue(col, g.First()), ObjCmp.I);
        foreach (var g in groups)
        {
            var glist = g.ToList();
            outl.Add(new GItem { Kind = GKind.Group, Level = level, Label = $"{col.Label}: {g.Key}", Count = glist.Count });
            BuildItems(glist, level + 1, outl);
            // Subtotal solo si hay columnas sumables visibles: el conteo ya va en la cabecera del grupo.
            if (HasVisibleSums) { outl.Add(new GItem { Kind = GKind.Subtotal, Level = level, Count = glist.Count, Sums = SumsOf(glist) }); }
        }
    }

    private bool HasVisibleSums => VisibleCols().Any(c => c.Summable);

    private const string SinDato = "(sin dato)";

    private static string GroupText(GridColumn<TItem> col, TItem r)
    {
        var t = col.TextOf(r);
        return string.IsNullOrWhiteSpace(t) ? SinDato : t.Trim();
    }

    private static object? SortValue(GridColumn<TItem> col, TItem r) => col.Type switch
    {
        GridColType.Number => GridFormat.ToDecimal(col.Value(r)),
        GridColType.Date => GridFormat.ToDate(col.Value(r)),
        GridColType.Bool => GridFormat.ToBool(col.Value(r)),
        _ => col.TextOf(r),
    };

    private IEnumerable<TItem> Sorted(IEnumerable<TItem> src)
    {
        IOrderedEnumerable<TItem>? o = null;
        foreach (var s in _sorts)
        {
            var col = ColByKey(s.K);
            if (col is null) { continue; }
            Func<TItem, object?> ks = r => SortValue(col, r);
            var cmp = s.Dir >= 0 ? ObjCmp.I : ObjCmp.Desc;
            o = o is null ? src.OrderBy(ks, cmp) : o.ThenBy(ks, cmp);
        }
        return o ?? src;
    }

    private Dictionary<string, decimal> SumsOf(IReadOnlyCollection<TItem> rows)
    {
        var d = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var c in Columns.Where(c => c.Summable))
        {
            d[c.Key] = rows.Sum(r => GridFormat.ToDecimal(c.Value(r)) ?? 0m);
        }
        return d;
    }

    /// <summary>Comparador de valores mixtos; los vacios van SIEMPRE al final (en ambas direcciones).</summary>
    private sealed class ObjCmp : IComparer<object?>
    {
        public static readonly ObjCmp I = new(false);
        public static readonly ObjCmp Desc = new(true);
        private readonly bool _desc;
        private ObjCmp(bool desc) { _desc = desc; }

        public int Compare(object? a, object? b)
        {
            var ea = a is null || (a is string sa && sa.Length == 0);
            var eb = b is null || (b is string sb && sb.Length == 0);
            if (ea || eb) { return ea == eb ? 0 : (ea ? 1 : -1); }
            var r = (a, b) switch
            {
                (decimal x, decimal y) => x.CompareTo(y),
                (DateTime x, DateTime y) => x.CompareTo(y),
                (bool x, bool y) => x.CompareTo(y),
                _ => string.Compare(a!.ToString(), b!.ToString(), GridFormat.Culture, CompareOptions.IgnoreCase),
            };
            return _desc ? -r : r;
        }
    }

    // ======================= Paginacion =======================

    private int PageCount => Math.Max(1, (int)Math.Ceiling(_gridItems.Count / (double)Math.Max(1, _pageSize)));

    private IEnumerable<GItem> PageItems => _gridItems.Skip(_page * _pageSize).Take(_pageSize);

    private void ClampPage() => _page = Math.Clamp(_page, 0, PageCount - 1);

    private void GoPage(int p) { _page = Math.Clamp(p, 0, PageCount - 1); }

    private async Task OnPageSizeChanged(ChangeEventArgs e)
    {
        if (int.TryParse(e.Value?.ToString(), out var s) && s > 0) { _pageSize = s; _page = 0; await SavePrefsAsync(); }
    }

    /// <summary>Lleva la grilla a la primera pagina (la pagina la llama al cambiar sus filtros rapidos).</summary>
    public void ResetPage() => _page = 0;

    // ======================= Menus de la barra =======================

    private void ToggleMenu(string m) { _openMenu = _openMenu == m ? null : m; _colMenuKey = null; }
    private void CloseMenu() { _openMenu = null; _colMenuKey = null; }

    // Ordenar
    private async Task AddSort()
    {
        var k = VisibleCols().Where(c => c.Sortable).Select(c => c.Key).FirstOrDefault(k => _sorts.All(s => s.K != k));
        if (k is null) { return; }
        _sorts.Add(new SortSpec { K = k, Dir = 1 });
        await ViewChangedAsync();
    }
    private async Task ChangeSortField(SortSpec s, ChangeEventArgs e) { s.K = e.Value?.ToString() ?? s.K; await ViewChangedAsync(); }
    private async Task ToggleSortDir(SortSpec s) { s.Dir = s.Dir >= 0 ? -1 : 1; await ViewChangedAsync(); }
    private async Task RemoveSort(SortSpec s) { _sorts.Remove(s); await ViewChangedAsync(); }

    private async Task QuickSort(string key)
    {
        if (ColByKey(key) is not { Sortable: true }) { return; }
        var cur = _sorts.Count == 1 && _sorts[0].K == key ? _sorts[0] : null;
        _sorts = new List<SortSpec> { new() { K = key, Dir = cur is null ? 1 : -cur.Dir } };
        await ViewChangedAsync();
    }

    // Agrupar
    private async Task AddGroup()
    {
        var k = Columns.Where(c => c.Groupable).Select(c => c.Key).FirstOrDefault(k => !_groups.Contains(k));
        if (k is null) { return; }
        _groups.Add(k);
        _page = 0;
        await ViewChangedAsync();
    }
    private async Task ChangeGroupField(int idx, ChangeEventArgs e)
    {
        if (idx < 0 || idx >= _groups.Count) { return; }
        _groups[idx] = e.Value?.ToString() ?? _groups[idx];
        _page = 0;
        await ViewChangedAsync();
    }
    private async Task RemoveGroup(int idx)
    {
        if (idx < 0 || idx >= _groups.Count) { return; }
        _groups.RemoveAt(idx);
        _page = 0;
        await ViewChangedAsync();
    }

    // Columnas
    private async Task MoveCol(int idx, int delta)
    {
        var j = idx + delta;
        if (idx < 0 || j < 0 || idx >= _order.Count || j >= _order.Count) { return; }
        (_order[idx], _order[j]) = (_order[j], _order[idx]);
        await SavePrefsAsync();
    }
    private async Task ToggleHidden(string key)
    {
        if (!_hidden.Remove(key)) { _hidden.Add(key); }
        await ViewChangedAsync();
    }
    private async Task ShowAllCols() { _hidden.Clear(); await ViewChangedAsync(); }
    private async Task ResetCols()
    {
        _order = Columns.Select(c => c.Key).ToList();
        _hidden = Columns.Where(c => c.DefaultHidden).Select(c => c.Key).ToHashSet();
        _widths.Clear();
        _frozen = 0;
        await ViewChangedAsync();
    }

    private async Task OnDropCol(string targetKey)
    {
        if (_dragCol is null || _dragCol == targetKey) { _dragCol = null; return; }
        var from = _order.IndexOf(_dragCol);
        var to = _order.IndexOf(targetKey);
        _dragCol = null;
        if (from < 0 || to < 0) { return; }
        var k = _order[from];
        _order.RemoveAt(from);
        _order.Insert(to, k);
        await SavePrefsAsync();
    }

    // Columnas fijas
    private async Task SetFrozenAsync(int n) { _frozen = Math.Max(0, n); CloseMenu(); await SavePrefsAsync(); }

    [JSInvokable]
    public async Task OnColumnResized(string key, int width)
    {
        if (width <= 0) { _widths.Remove(key); } else { _widths[key] = width; }
        await SavePrefsAsync();
        StateHasChanged();
    }

    // Menu de columna (caret del encabezado)
    private async Task OpenColMenu(GridColumn<TItem> c, MouseEventArgs e)
    {
        _openMenu = null;
        double x = e.ClientX, y = e.ClientY + 8;
        try
        {
            var vp = await JS.InvokeAsync<Viewport>("ecxGrid.viewport");
            x = Math.Max(8, Math.Min(x, vp.W - 230));
            y = Math.Max(8, Math.Min(y, vp.H - 240));
        }
        catch { /* sin JS: posicion del clic */ }
        _colMenuX = x; _colMenuY = y;
        _colMenuKey = c.Key;
    }

    private sealed record Viewport(double W, double H);

    private async Task ColMenuSort(int dir)
    {
        if (_colMenuKey is { } k) { _sorts = new List<SortSpec> { new() { K = k, Dir = dir } }; }
        CloseMenu();
        await ViewChangedAsync();
    }

    private async Task ColMenuGroup()
    {
        if (_colMenuKey is { } k && !_groups.Contains(k)) { _groups.Add(k); _page = 0; }
        CloseMenu();
        await ViewChangedAsync();
    }

    private async Task ColMenuFreeze()
    {
        if (_colMenuKey is { } k)
        {
            var idx = VisibleCols().Select(c => c.Key).ToList().IndexOf(k);
            if (idx >= 0) { _frozen = idx + 1; }
        }
        CloseMenu();
        await SavePrefsAsync();
    }

    private async Task ColMenuHide()
    {
        if (_colMenuKey is { } k && VisibleCols().Count() > 1) { _hidden.Add(k); }
        CloseMenu();
        await ViewChangedAsync();
    }

    private async Task ViewChangedAsync()
    {
        Rebuild();
        await SavePrefsAsync();
    }

    private async Task SavePrefsAsync()
    {
        var dto = new PrefsDto
        {
            order = _order,
            hidden = _hidden.ToList(),
            known = _known.ToList(),
            widths = _widths,
            sorts = _sorts,
            groups = _groups,
            frozen = _frozen,
            pageSize = _pageSize,
        };
        try { await Prefs.SaveAsync(TableKey, JsonSerializer.Serialize(dto)); }
        catch { /* la vista sigue funcionando aunque no se pueda guardar */ }
    }

    // ======================= Filtro =======================

    private void OpenFilterBuilder()
    {
        CloseMenu();
        if (_filterRoot.Children.Count == 0) { _filterRoot.Children.Add(GridFilterNode.NewCond()); }
        _filterOpen = true;
    }

    private void CloseFilterBuilder() => _filterOpen = false;

    private void OnFilterChanged()
    {
        _activeFilterName = null;
        _page = 0;
        Rebuild();
    }

    private void ClearFilter()
    {
        _filterRoot = GridFilterNode.NewGroup("and");
        _activeFilterName = null; _saveName = null;
        _page = 0;
        Rebuild();
    }

    private void ClearFilterTree()
    {
        _filterRoot = GridFilterNode.NewGroup("and");
        _filterRoot.Children.Add(GridFilterNode.NewCond());
        _activeFilterName = null;
        Rebuild();
    }

    /// <summary>Valores distintos de una columna de texto (sugerencias del constructor de filtros).</summary>
    private IReadOnlyList<string> Suggestions(string key)
    {
        if (ColByKey(key) is not { } col) { return Array.Empty<string>(); }
        var vals = Items.Select(col.TextOf).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(201).ToList();
        // Con demasiados valores distintos (nombres, NIT) sugerir no ayuda: se omite.
        return vals.Count > 200 ? Array.Empty<string>() : vals.OrderBy(v => v, StringComparer.Create(GridFormat.Culture, true)).ToList();
    }

    private async Task LoadSavedFiltersAsync()
    {
        if (FilterModule is null) { return; }
        try { _savedFilters = (await SavedFilters.ListAsync(FilterModule)).ToList(); }
        catch { _savedFilters = new(); }
    }

    private async Task SaveFilterAsync()
    {
        var name = (_saveName ?? "").Trim();
        if (FilterModule is null || name.Length == 0 || FilterCount == 0) { return; }
        var saved = await SavedFilters.SaveAsync(FilterModule, name, JsonSerializer.Serialize(_filterRoot), _userName);
        if (saved is not null) { _activeFilterName = saved.Name; }
        await LoadSavedFiltersAsync();
    }

    private void ApplySaved(SavedFilterDto sf)
    {
        CloseMenu();
        try
        {
            _filterRoot = JsonSerializer.Deserialize<GridFilterNode>(sf.DefinitionJson) ?? GridFilterNode.NewGroup("and");
            _activeFilterName = sf.Name;
            _saveName = sf.Name;
            _page = 0;
            Rebuild();
        }
        catch { /* definicion corrupta: se ignora */ }
    }

    private void EditSaved(SavedFilterDto sf)
    {
        CloseMenu();
        try { _filterRoot = JsonSerializer.Deserialize<GridFilterNode>(sf.DefinitionJson) ?? GridFilterNode.NewGroup("and"); }
        catch { _filterRoot = GridFilterNode.NewGroup("and"); }
        if (_filterRoot.Children.Count == 0) { _filterRoot.Children.Add(GridFilterNode.NewCond()); }
        _activeFilterName = sf.Name;
        _saveName = sf.Name;
        _filterOpen = true;
        Rebuild();
    }

    private async Task DeleteSavedAsync(SavedFilterDto sf)
    {
        await SavedFilters.DeleteAsync(sf.Id);
        if (_activeFilterName == sf.Name) { _activeFilterName = null; }
        await LoadSavedFiltersAsync();
    }

    // ======================= Exportar a Excel =======================

    /// <summary>Exporta EXACTAMENTE la vista: filtro, columnas visibles en su orden, orden y agrupacion con
    /// cabeceras de grupo, subtotales y total general (todas las paginas).</summary>
    private async Task ExportExcelAsync()
    {
        if (_exporting) { return; }
        _exporting = true; _msg = null;
        try
        {
            var vis = VisibleCols().Where(c => c.Exportable).ToList();
            if (vis.Count == 0) { _msg = "No hay columnas visibles para exportar."; return; }
            var lblCol = Math.Max(0, vis.FindIndex(c => !c.Summable));

            using var wb = new XLWorkbook();
            var sheet = new string(ExportName.Where(ch => !"[]:*?/\\".Contains(ch)).ToArray());
            var ws = wb.Worksheets.Add(sheet.Length == 0 ? "Datos" : sheet[..Math.Min(31, sheet.Length)]);
            var rowN = 1;
            for (var i = 0; i < vis.Count; i++)
            {
                var cell = ws.Cell(rowN, i + 1);
                cell.Value = vis[i].Label;
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F2937");
                cell.Style.Font.FontColor = XLColor.White;
                if (vis[i].Type == GridColType.Number) { cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right; }
            }
            ws.SheetView.FreezeRows(1);
            rowN++;

            var all = _grand is null ? _gridItems : _gridItems.Append(_grand);
            foreach (var it in all)
            {
                if (it.Kind == GKind.Group)
                {
                    var cell = ws.Cell(rowN, 1);
                    cell.Value = $"{it.Label} ({it.Count})";
                    ws.Range(rowN, 1, rowN, vis.Count).Merge();
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF2FF");
                    if (it.Level > 0) { cell.Style.Alignment.Indent = it.Level; }
                }
                else if (it.Kind == GKind.Data && it.Row is { } r)
                {
                    for (var i = 0; i < vis.Count; i++)
                    {
                        var c = vis[i];
                        var cell = ws.Cell(rowN, i + 1);
                        var raw = c.Value(r);
                        if (c.Type == GridColType.Number && GridFormat.ToDecimal(raw) is decimal n)
                        {
                            cell.Value = n; cell.Style.NumberFormat.Format = "#,##0.##";
                        }
                        else if (c.Type == GridColType.Date && GridFormat.ToDate(raw) is DateTime d)
                        {
                            cell.Value = d; cell.Style.DateFormat.Format = "dd/MM/yyyy";
                        }
                        else { cell.Value = c.TextOf(r); }
                    }
                }
                else if (it.Kind == GKind.Subtotal)
                {
                    for (var i = 0; i < vis.Count; i++)
                    {
                        var c = vis[i];
                        var cell = ws.Cell(rowN, i + 1);
                        if (c.Summable && it.Sums is { } s && s.TryGetValue(c.Key, out var sv)) { cell.Value = sv; cell.Style.NumberFormat.Format = "#,##0.##"; }
                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.BackgroundColor = it.Grand ? XLColor.FromHtml("#D1D5DB") : XLColor.FromHtml("#F3F4F6");
                    }
                    ws.Cell(rowN, lblCol + 1).Value = $"{(it.Grand ? "TOTAL GENERAL" : "Subtotal")} - {it.Count} reg.";
                }
                rowN++;
            }

            ws.Columns().AdjustToContents(1, Math.Min(rowN, 400));
            foreach (var col in ws.ColumnsUsed()) { if (col.Width > 60) { col.Width = 60; } }
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            var fname = $"{ExportName.Replace(' ', '_')}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
            await JS.InvokeVoidAsync("ecorexDescargarArchivo", fname,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", Convert.ToBase64String(ms.ToArray()));
        }
        catch (Exception ex) { _msg = "Error al exportar a Excel: " + ex.Message; }
        finally { _exporting = false; }
    }

    private static string FmtSum(decimal v) => v.ToString("#,##0.##", GridFormat.Culture);
}
