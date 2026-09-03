using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.JSInterop;
using Ecorex.Application.Admin;
using Ecorex.Application.Asesores;
using Ecorex.Application.DataLookups;
using Ecorex.Application.Directorio;
using Ecorex.Application.Roles;
using Ecorex.Domain.Enums;
using Ecorex.SuperAdmin;
using Ecorex.SuperAdmin.Auth;
using Ecorex.SuperAdmin.Components.Shared;
using Ecorex.SuperAdmin.Components.Shared.Data;
using Ecorex.SuperAdmin.Components.Shared.Lookups;

namespace Ecorex.SuperAdmin.Components.Pages;

/// <summary>
/// Capa de logica COMPARTIDA del Directorio General (ADR-0088). Las dos vistas (DirectorioGeneral =
/// Ligero, DirectorioEspecializado = Especializado) heredan de aqui: consumen el MISMO backend/estado y
/// solo difieren en su markup/CSS (el "front"). Cada vista declara su <see cref="ViewVariant"/> y la base
/// redirige a la otra si el tenant configuro una variante distinta.
/// </summary>
public abstract class DirectorioSharedBase : ComponentBase
{
    [Inject] protected ITerceroService TerceroSvc { get; set; } = default!;
    [Inject] protected ITerceroFieldService FieldSvc { get; set; } = default!;
    [Inject] protected ITerceroFichaService FichaSvc { get; set; } = default!;
    [Inject] protected ITerceroFormService FormsSvc { get; set; } = default!;
    [Inject] protected ICurrentPermissions Perms { get; set; } = default!;
    [Inject] protected IJSRuntime JS { get; set; } = default!;
    [Inject] protected IDataLookupService LookupSvc { get; set; } = default!;
    [Inject] protected Ecorex.Application.Asesores.IAsesorService AsesorSvc { get; set; } = default!;
    [Inject] protected IDirectoryVariantService DirVariant { get; set; } = default!;
    [Inject] protected NavigationManager Nav { get; set; } = default!;

    /// <summary>Variante que ATIENDE esta vista concreta (la declara cada .razor).</summary>
    protected abstract DirectoryVariant ViewVariant { get; }

    // ---- Estado de lista ----
    protected bool _loading = true;
    // Recarga en curso (lista + KPIs). Bloquea las acciones que abren el editor mientras una
    // consulta del DbContext scoped esta en vuelo: abrir el modal dispara EnsureDefaultsAsync sobre
    // el MISMO contexto y "second operation on this context" tumba el circuito (se veia sobre todo
    // con la BD remota por tunel, donde las queries tardan y la ventana de solape es amplia).
    protected bool _reloading;
    protected bool _busy;
    protected string? _pageError;
    protected TerceroKpisDto _kpis = new(0, 0, 0, 0);
    protected IReadOnlyList<TerceroListItemDto> _allRows = Array.Empty<TerceroListItemDto>();
    protected TerceroTabTipo _tipo = TerceroTabTipo.Todos;
    protected TerceroTabNaturaleza _naturaleza = TerceroTabNaturaleza.Todos;
    protected string _search = "";
    protected readonly HashSet<Guid> _expanded = new();
    protected readonly Dictionary<Guid, IReadOnlyList<TerceroContactoDto>> _kids = new();

    // ---- Permisos (sub-permisos nombrados del Directorio General, ADR-0033) ----
    protected bool _canCrearEmpresa = true;
    protected bool _canCrearCliente = true;
    protected bool _canCrearSospechoso = true;
    protected bool _canEdit = true;
    protected bool _canDelete = true;
    protected bool CanCreateAny => _canCrearEmpresa || _canCrearCliente || _canCrearSospechoso;

    // ---- Modal asignar ----
    protected bool _assignOpen;
    protected TerceroListItemDto? _assignPersona;

    // ---- Editor de tercero compartido (crear/editar/contacto). Invocado por @ref. ----
    protected TerceroModal? _terceroModal;

    /// <summary>Filtros activos por campo configurable: FieldKey -> valor exigido.</summary>
    protected readonly Dictionary<string, string> _fieldFilters = new(StringComparer.Ordinal);

    protected IEnumerable<TerceroListItemDto> Displayed
    {
        get
        {
            var rows = _naturaleza switch
            {
                TerceroTabNaturaleza.Empresas => _allRows.Where(r => r.EsEmpresa),
                TerceroTabNaturaleza.Contactos => _allRows.Where(r => r.EsPersona),
                _ => _allRows
            };

            // Los filtros de campo se aplican en memoria: el listado ya trae solo los valores
            // filtrables (el servicio no carga la ficha entera) y son pocas filas.
            foreach (var (key, value) in _fieldFilters)
            {
                rows = rows.Where(r => r.Filtrables is not null
                    && r.Filtrables.TryGetValue(key, out var v)
                    && string.Equals(v, value, StringComparison.OrdinalIgnoreCase));
            }
            return rows;
        }
    }

    // Paginacion de la parrilla (todo ya esta en memoria y filtrado; solo se corta la pagina visible).
    protected static readonly int[] PageSizes = { 25, 50, 100, 200 };
    protected int _pageSize = 50;
    protected int _page;

    protected int FilteredCount => Displayed.Count();
    protected int PageCount => Math.Max(1, (int)Math.Ceiling(FilteredCount / (double)_pageSize));
    protected IEnumerable<TerceroListItemDto> DisplayedPage
        => Displayed.Skip(ClampPage() * _pageSize).Take(_pageSize);

    protected int ClampPage()
    {
        var max = PageCount - 1;
        _page = Math.Clamp(_page, 0, max);
        return _page;
    }
    protected void GoPage(int p) => _page = Math.Clamp(p, 0, PageCount - 1);
    protected void OnPageSizeChanged(ChangeEventArgs e)
    {
        if (int.TryParse(e.Value?.ToString(), out var s) && s > 0) { _pageSize = s; _page = 0; }
    }

    /// <summary>Campos marcados como filtrables, en el orden en que se configuraron.</summary>
    protected List<TerceroFieldDto> FilterableFields()
        => _cfgAllFields.Where(f => f.ShowInFilter).OrderBy(f => f.FichaKey).ThenBy(f => f.SortOrder).ToList();

    /// <summary>Valores que existen de verdad para esa clave: ofrecer opciones vacias solo estorba.</summary>
    protected List<string> FilterOptions(string fieldKey)
        => _allRows
            .Select(r => r.Filtrables is not null && r.Filtrables.TryGetValue(fieldKey, out var v) ? v : null)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.CurrentCulture)
            .ToList();

    protected void SetFieldFilter(string fieldKey, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) { _fieldFilters.Remove(fieldKey); }
        else { _fieldFilters[fieldKey] = value; }
        _page = 0;
    }

    protected void ClearFieldFilters() => _fieldFilters.Clear();

    protected int CountTodos => _allRows.Count;
    protected int CountEmpresas => _allRows.Count(r => r.EsEmpresa);
    protected int CountContactos => _allRows.Count(r => r.EsPersona);

    protected bool _downloadingTpl;

    /// <summary>Descarga la plantilla Excel de importacion de terceros: hoja "Terceros" con filas de
    /// ejemplo + LISTAS DESPLEGABLES y hojas de codigos (Tipos/Perfiles/Estados/TiposId/Ciudades/
    /// Sectores/Vendedores). Vendedores sale del catalogo vivo de asesores; Ciudades se enriquece con
    /// las que ya existen en el tenant.</summary>
    protected async Task DownloadTemplateAsync()
    {
        _downloadingTpl = true;
        try
        {
            var asesores = await AsesorSvc.ListOptionsAsync();
            var vendedores = asesores.Select(a => a.Nombre);
            var ciudades = _allRows.Select(r => r.Ciudad).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c!);
            var bytes = TerceroTemplateXlsx.Build(vendedores, ciudades);
            await JS.InvokeVoidAsync("ecorexDescargarArchivo", "plantilla-directorio.xlsx", TerceroTemplateXlsx.Mime, Convert.ToBase64String(bytes));
        }
        finally { _downloadingTpl = false; }
    }

    // ===================== IMPORTAR EXCEL (carga en lote) =====================
    protected bool _impOpen;
    protected bool _impBusy;                       // parseando o importando
    protected string? _impFileName;
    protected string? _impFatal;                   // error que impide leer el archivo
    protected TerceroImportXlsx.TerceroImportParse? _impParse;
    protected readonly HashSet<int> _impDupRows = new(); // RowNumbers que ya existen (o repetidos en el archivo)
    protected int _impDone;                        // filas ya creadas (progreso)
    protected int _impFailed;                      // filas que fallaron al crear
    protected int _impSkipped;                     // filas omitidas por duplicadas
    protected bool _impFinished;                   // ya se corrio la importacion

    // Filas que de verdad se van a crear: validas y NO duplicadas.
    protected int ImportableCount => _impParse?.Rows.Count(r => r.IsValid && !_impDupRows.Contains(r.RowNumber)) ?? 0;
    protected int DupCount => _impDupRows.Count;
    // 12 MB: una plantilla de terceros con miles de filas cabe de sobra.
    protected const long ImpMaxBytes = 12L * 1024 * 1024;

    protected async Task OpenImportAsync()
    {
        if (_loading || _reloading) { return; }
        _impOpen = true;
        _impParse = null; _impFatal = null; _impFileName = null;
        _impDone = 0; _impFailed = 0; _impSkipped = 0; _impFinished = false;
        _impDupRows.Clear();
        await Task.CompletedTask;
    }

    protected void CloseImport()
    {
        if (_impBusy) { return; }
        _impOpen = false;
    }

    protected async Task OnImportFileAsync(InputFileChangeEventArgs e)
    {
        var file = e.File;
        _impFileName = file.Name;
        _impParse = null; _impFatal = null; _impDone = 0; _impFailed = 0; _impSkipped = 0; _impFinished = false;
        _impDupRows.Clear();
        _impBusy = true;
        try
        {
            if (file.Size > ImpMaxBytes)
            {
                _impFatal = "El archivo supera el limite de 12 MB.";
                return;
            }
            using var ms = new MemoryStream();
            await file.OpenReadStream(ImpMaxBytes).CopyToAsync(ms);
            ms.Position = 0;

            // Indice de asesores por nombre para resolver la columna Vendedor.
            var asesores = await AsesorSvc.ListOptionsAsync();
            var byName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in asesores) { byName[a.Nombre] = a.Id; }

            _impParse = TerceroImportXlsx.Parse(ms, byName);
            if (_impParse.FatalError is not null) { _impFatal = _impParse.FatalError; }
            else { await ComputeDuplicatesAsync(); }
        }
        catch (Exception ex)
        {
            _impFatal = "No se pudo procesar el archivo: " + ex.Message;
        }
        finally
        {
            _impBusy = false;
        }
    }

    // Marca como duplicadas las filas cuyo documento (o, si no hay, cuyo nombre) YA existe en el tenant,
    // o que se repiten dentro del mismo archivo. La primera aparicion se importa; las siguientes se omiten.
    protected async Task ComputeDuplicatesAsync()
    {
        _impDupRows.Clear();
        if (_impParse is null) { return; }

        var keys = await TerceroSvc.GetDedupKeysAsync();
        var seenDocs = new HashSet<string>(keys.Documentos, StringComparer.Ordinal);
        var seenNames = new HashSet<string>(keys.Nombres, StringComparer.Ordinal);

        foreach (var row in _impParse.Rows.Where(r => r.IsValid))
        {
            bool dup;
            if (row.DocKey is not null)
            {
                dup = !seenDocs.Add(row.DocKey);            // ya estaba (BD o fila previa)
            }
            else
            {
                dup = !seenNames.Add(row.NameKey);
            }
            if (dup) { _impDupRows.Add(row.RowNumber); }
        }
    }

    protected async Task RunImportAsync()
    {
        if (_impParse is null || _impBusy) { return; }
        var toCreate = _impParse.Rows.Where(r => r.IsValid && !_impDupRows.Contains(r.RowNumber)).ToList();
        _impSkipped = _impParse.Rows.Count(r => r.IsValid && _impDupRows.Contains(r.RowNumber));
        if (toCreate.Count == 0) { _impFinished = true; return; }

        _impBusy = true;
        _impDone = 0; _impFailed = 0;
        try
        {
            foreach (var row in toCreate)
            {
                try
                {
                    var res = await TerceroSvc.CreateAsync(row.ToRequest());
                    if (res.IsOk) { _impDone++; } else { _impFailed++; }
                }
                catch { _impFailed++; }
                // Refresca el contador en vivo cada pocas filas para no saturar el render.
                if ((_impDone + _impFailed) % 10 == 0) { StateHasChanged(); }
            }
            _impFinished = true;
        }
        finally
        {
            _impBusy = false;
        }
        await ReloadAsync();
    }

    protected override async Task OnInitializedAsync()
    {
        // Cada vista ATIENDE solo su variante. Si el tenant configuro la otra, redirige (misma logica base).
        var configured = await DirVariant.GetAsync();
        if (configured != ViewVariant)
        {
            Nav.NavigateTo(
                configured == DirectoryVariant.Especializado ? "/directorio-especializado" : "/directorio-general",
                forceLoad: false, replace: true);
            return;
        }

        var eff = await Perms.GetAsync();
        _canCrearEmpresa = eff.Can(DirectorioSubPermisos.CrearEmpresa, PermissionAction.Create);
        _canCrearCliente = eff.Can(DirectorioSubPermisos.CrearCliente, PermissionAction.Create);
        _canCrearSospechoso = eff.Can(DirectorioSubPermisos.CrearSospechoso, PermissionAction.Create);
        _canEdit = eff.Can(DirectorioSubPermisos.ModuleRoute, PermissionAction.Edit);
        _canDelete = eff.Can(DirectorioSubPermisos.ModuleRoute, PermissionAction.Delete);
        await FieldSvc.EnsureDefaultsAsync();
        // Las definiciones se cargan de entrada (no solo al abrir el configurador): la barra de
        // filtros necesita saber que campos estan marcados como filtrables.
        _cfgAllFields = (await FieldSvc.ListFieldsAsync()).ToList();
        await ReloadAsync();
        _loading = false;
    }

    // Abre el editor en modo crear. Guarda: si la pagina aun carga o recarga, NO abras el modal
    // (abrirlo dispara EnsureDefaultsAsync sobre el DbContext scoped que la consulta en vuelo esta
    // usando -> "second operation on this context"). Los botones ya salen deshabilitados; esto cubre
    // cualquier disparo residual.
    protected async Task OpenCreateAsync()
    {
        if (_loading || _reloading || _terceroModal is null) { return; }
        // El filtro TIPO activo preselecciona el perfil: crear desde "Clientes" nace con la pildora Cliente
        // activada y su ficha abierta (idem Proveedores/Empleados). "Todos" nace sin perfil.
        var perfil = _tipo switch
        {
            TerceroTabTipo.Clientes => TerceroPerfil.Cliente,
            TerceroTabTipo.Proveedores => TerceroPerfil.Proveedor,
            TerceroTabTipo.Empleados => TerceroPerfil.Empleado,
            _ => TerceroPerfil.Ninguno
        };
        await _terceroModal.OpenCreate(perfil);
    }

    // Refresca lista + KPIs + contactos expandidos tras un cambio en el modal compartido.
    // Refresca los contactos expandidos ANTES que el listado: si agregas un contacto a una fila
    // desplegada, ese sub-listado (_kids) es lo primero que el usuario espera ver actualizado, y no
    // debe quedar a merced de que la recarga del listado principal falle.
    protected async Task OnModalChangedAsync()
    {
        foreach (var id in _expanded.ToList())
        {
            _kids[id] = await TerceroSvc.ListContactosAsync(id);
        }
        await ReloadAsync();
        StateHasChanged();
    }


    protected static string TypeLabel(TerceroFieldType t) => t switch
    {
        TerceroFieldType.Text => "Texto",
        TerceroFieldType.Number => "Numero",
        TerceroFieldType.Currency => "Moneda",
        TerceroFieldType.TextArea => "Texto largo",
        TerceroFieldType.Select => "Lista",
        TerceroFieldType.Date => "Fecha",
        TerceroFieldType.Phone => "Telefono",
        TerceroFieldType.Separator => "Separador",
        TerceroFieldType.Calculated => "Calculado",
        _ => t.ToString()
    };

    protected async Task ReloadAsync()
    {
        _reloading = true;
        try
        {
            // Se pide siempre naturaleza=Todos: los tabs de naturaleza filtran en cliente para
            // que los contadores (Todos/Empresas/Contactos) queden siempre visibles.
            _allRows = await TerceroSvc.ListAsync(new TerceroListFilter(_tipo, TerceroTabNaturaleza.Todos, _search));
            _kpis = await TerceroSvc.GetKpisAsync();
            // Purga expansiones/contactos de filas ya no visibles.
            _expanded.RemoveWhere(id => _allRows.All(r => r.Id != id));
        }
        finally { _reloading = false; }
    }

    protected async Task SetTipo(TerceroTabTipo tipo)
    {
        _tipo = tipo;
        await ReloadAsync();
    }

    protected async Task OnSearch(ChangeEventArgs e)
    {
        _search = e.Value?.ToString() ?? "";
        _page = 0;
        await ReloadAsync();
    }

    protected async Task ToggleExpandAsync(TerceroListItemDto row)
    {
        if (_expanded.Contains(row.Id))
        {
            _expanded.Remove(row.Id);
            return;
        }
        _expanded.Add(row.Id);
        if (!_kids.ContainsKey(row.Id))
        {
            _kids[row.Id] = await TerceroSvc.ListContactosAsync(row.Id);
        }
    }

    // ---- Asignar ----
    protected void OpenAssign(TerceroListItemDto persona)
    {
        _assignPersona = persona;
        _assignOpen = true;
    }

    protected void CloseAssign()
    {
        _assignOpen = false;
        _assignPersona = null;
    }

    protected async Task AssignToAsync(Guid empresaId)
    {
        if (_assignPersona is null) { return; }
        _busy = true;
        var res = await TerceroSvc.AssignToEmpresaAsync(_assignPersona.Id, empresaId);
        _busy = false;
        if (!res.IsOk)
        {
            _pageError = res.Error;
            return;
        }
        _kids.Remove(empresaId);
        CloseAssign();
        await ReloadAsync();
    }

    // ---- Eliminar ----
    protected async Task DeleteAsync(TerceroListItemDto row)
    {
        _busy = true;
        _pageError = null;
        var res = await TerceroSvc.DeleteAsync(row.Id);
        _busy = false;
        if (!res.IsOk)
        {
            _pageError = res.Error;
            return;
        }
        await ReloadAsync();
    }


    protected async Task DeleteContactoAsync(Guid parentId, Guid contactoId)
    {
        _busy = true;
        var res = await TerceroSvc.DeleteContactoAsync(contactoId);
        _busy = false;
        if (!res.IsOk)
        {
            _pageError = res.Error;
            return;
        }
        if (_expanded.Contains(parentId)) { _kids[parentId] = await TerceroSvc.ListContactosAsync(parentId); }
        await ReloadAsync();
    }

    // Quita de la empresa un contacto que es Tercero Persona (desvincula, no elimina): sigue existiendo
    // como cliente individual.
    protected async Task UnassignContactoAsync(Guid parentId, Guid personaId)
    {
        _busy = true;
        var res = await TerceroSvc.UnassignFromEmpresaAsync(personaId);
        _busy = false;
        if (!res.IsOk)
        {
            _pageError = res.Error;
            return;
        }
        if (_expanded.Contains(parentId)) { _kids[parentId] = await TerceroSvc.ListContactosAsync(parentId); }
        await ReloadAsync();
    }


    /// <summary>Ficha seleccionada en el configurador (para la barra de herramientas de ficha).</summary>
    protected TerceroFichaDto? SelFicha => _fichas.FirstOrDefault(x => x.FichaKey == _cfgFicha);

    // ---- Configurador de campos ----
    protected bool _cfgOpen;
    protected string _cfgFicha = "fiscal";
    protected List<TerceroFieldDto> _cfgFields = new();
    protected Guid? _cfgEditingId;
    protected string _cfgLabel = "";
    protected TerceroFieldType _cfgType = TerceroFieldType.Text;
    protected int _cfgColumn = 1;
    protected string _cfgOptions = "";
    protected string _cfgDescription = "";
    protected string? _cfgError;

    // Campos calculados y extras (ADR-0029).
    protected string _cfgFormula = "";
    protected string? _cfgFormulaError;
    protected bool _cfgShowInFilter;
    protected string _cfgRepeatWith = "";

    /// <summary>Todos los campos del tenant: la formula puede referenciar cualquier ficha.</summary>
    protected List<TerceroFieldDto> _cfgAllFields = new();

    // ---- Fichas (pildoras) configurables por tenant (data-driven) ----
    protected List<TerceroFichaDto> _fichas = new();
    protected bool _fichaCfgOpen;
    protected Guid? _fichaEditId;
    protected bool _fichaIsSystem;
    protected string _fichaName = "";
    protected string? _fichaColor;
    protected string _fichaPerfil = "";
    protected bool _fichaHidden;
    protected string? _fichaError;

    /// <summary>Estilo de acento (color) de la pildora en el configurador.</summary>
    protected static string ChipStyle(TerceroFichaDto m)
        => string.IsNullOrWhiteSpace(m.Color) ? "" : $"border-color:{m.Color};box-shadow:inset 0 -2px 0 {m.Color}";

    protected void StartNewFichaAsync()
    {
        _fichaError = null;
        _fichaEditId = null;
        _fichaIsSystem = false;
        _fichaName = "";
        _fichaColor = "#6b7280";
        _fichaPerfil = "";
        _fichaHidden = false;
        _fichaCfgOpen = true;
    }

    protected void OpenFichaCfg(TerceroFichaDto f)
    {
        _fichaError = null;
        _fichaEditId = f.Id;
        _fichaIsSystem = f.IsSystem;
        _fichaName = f.Title;
        _fichaColor = f.Color ?? "#6b7280";
        _fichaPerfil = f.Perfil ?? "";
        _fichaHidden = f.IsHidden;
        _fichaCfgOpen = true;
    }

    protected void CloseFichaCfg()
    {
        _fichaCfgOpen = false;
        _fichaError = null;
    }

    protected async Task SaveFichaAsync()
    {
        if (string.IsNullOrWhiteSpace(_fichaName)) { return; }
        _busy = true;
        _fichaError = null;
        try
        {
            if (_fichaEditId is Guid id)
            {
                _fichaError = await FichaSvc.UpdateAsync(id, _fichaName, _fichaColor, _fichaPerfil);
                if (_fichaError is not null) { return; }
                _fichaError = await FichaSvc.SetHiddenAsync(id, _fichaHidden);
                if (_fichaError is not null) { return; }
            }
            else
            {
                var created = await FichaSvc.CreateAsync(_fichaName, _fichaColor, _fichaPerfil);
                if (created is null) { _fichaError = "No se pudo crear la ficha."; return; }
                if (_fichaHidden) { await FichaSvc.SetHiddenAsync(created.Id, true); }
                _cfgFicha = created.FichaKey;
            }
            _fichas = (await FichaSvc.ListAsync()).ToList();
            _fichaCfgOpen = false;
            await SelectCfgFichaAsync(_cfgFicha);
        }
        finally { _busy = false; }
    }

    protected async Task DeleteFichaFromModalAsync()
    {
        if (_fichaEditId is not Guid id) { return; }
        _busy = true;
        _fichaError = null;
        try
        {
            _fichaError = await FichaSvc.DeleteAsync(id);
            if (_fichaError is not null) { return; }
            _fichas = (await FichaSvc.ListAsync()).ToList();
            _cfgFicha = _fichas.FirstOrDefault()?.FichaKey ?? "";
            _fichaCfgOpen = false;
            await SelectCfgFichaAsync(_cfgFicha);
        }
        finally { _busy = false; }
    }

    protected async Task ReorderFromModalAsync(bool up)
    {
        if (_fichaEditId is not Guid id) { return; }
        _busy = true;
        _fichaError = null;
        try
        {
            await FichaSvc.ReorderAsync(id, up);
            _fichas = (await FichaSvc.ListAsync()).ToList();
        }
        finally { _busy = false; }
    }

    // ---- Formularios ofrecidos en el modal de tercero (3a columna) ----
    protected IReadOnlyList<TerceroFormLinkDto> _cfgForms = Array.Empty<TerceroFormLinkDto>();
    protected IReadOnlyList<TerceroFormCandidateDto> _cfgFormCandidates = Array.Empty<TerceroFormCandidateDto>();
    protected string _cfgFormPick = "";

    protected async Task LoadCfgFormsAsync()
    {
        _cfgForms = await FormsSvc.ListAsync();
        _cfgFormCandidates = await FormsSvc.ListCandidatesAsync();
        _cfgFormPick = "";
    }

    protected async Task AddCfgFormAsync()
    {
        if (!Guid.TryParse(_cfgFormPick, out var defId)) { return; }
        _busy = true;
        try
        {
            await FormsSvc.AddAsync(defId);
            await LoadCfgFormsAsync();
        }
        finally { _busy = false; }
    }

    protected async Task RemoveCfgFormAsync(Guid linkId)
    {
        _busy = true;
        try
        {
            await FormsSvc.RemoveAsync(linkId);
            await LoadCfgFormsAsync();
        }
        finally { _busy = false; }
    }

    protected async Task OpenConfigAsync()
    {
        _fichaCfgOpen = false;
        _fichaError = null;
        _fichas = (await FichaSvc.ListAsync()).ToList();
        _cfgFicha = _fichas.FirstOrDefault()?.FichaKey ?? "fiscal";
        ResetCfgForm();
        await FieldSvc.EnsureDefaultsAsync();
        await LoadCfgFieldsAsync();
        await LoadCfgFormsAsync();
        _cfgOpen = true;
    }

    protected void CloseConfig()
    {
        // El modal compartido de tercero recarga su propia cache de campos al abrirse,
        // por lo que aqui basta con cerrar el configurador.
        _cfgOpen = false;
    }

    protected async Task LoadCfgFieldsAsync()
    {
        var list = await FieldSvc.ListByFichaAsync(_cfgFicha);
        _cfgFields = list.OrderBy(f => f.SortOrder).ToList();
        // Todas las fichas: una formula puede referenciar campos de cualquiera (los valores del
        // tercero viven juntos), asi que el selector de claves los necesita todos.
        _cfgAllFields = (await FieldSvc.ListFieldsAsync()).ToList();
    }

    protected async Task SelectCfgFichaAsync(string ficha)
    {
        _cfgFicha = ficha;
        ResetCfgForm();
        await LoadCfgFieldsAsync();
    }

    // ---- Campo tipo lista del Contenedor de datos (TerceroFieldType.Lookup) ----
    // La configuracion no vive en columnas nuevas: se serializa a JSON dentro de Options, en el
    // mismo sitio donde un Select guarda sus opciones de texto.
    protected List<LookupModelDto> _lkModels = new();
    protected List<LookupTableDto> _lkTables = new();
    protected List<LookupColumnDto> _lkColumns = new();
    protected Guid? _lkModelId;
    protected Guid? _lkTableId;
    protected Guid? _lkDisplayCol;
    protected DataLookupDisplayMode _lkDisplayMode = DataLookupDisplayMode.Typeahead;
    protected bool _lkAllowCreate;
    protected readonly List<AutofillRow> _lkAutofill = new();
    protected readonly List<FilterRow> _lkFilters = new();

    protected sealed class AutofillRow { public Guid ColumnId { get; set; } public string TargetKey { get; set; } = ""; }
    protected sealed class FilterRow
    {
        public Guid ColumnId { get; set; }
        public string FromKey { get; set; } = "";
        public string Value { get; set; } = "";
        public bool Require { get; set; }
    }

    protected void AddAutofill()
        => _lkAutofill.Add(new AutofillRow { ColumnId = _lkColumns.Count > 0 ? _lkColumns[0].Id : Guid.Empty });

    protected void AddFilter()
        => _lkFilters.Add(new FilterRow { ColumnId = _lkColumns.Count > 0 ? _lkColumns[0].Id : Guid.Empty });

    /// <summary>
    /// Campos de TODA la ficha (menos el que se edita) para elegir destino de autollenado u
    /// origen de un filtro. La llave se cualifica con la ficha porque solo es unica dentro de
    /// ella: sin eso, dos campos "ciudad" en fichas distintas serian indistinguibles.
    /// </summary>
    protected List<(string Key, string Label)> OtrosCamposDeLaFicha()
        => _cfgFields
            .Where(f => f.Id != _cfgEditingId
                && f.FieldType != TerceroFieldType.Separator
                && f.FieldType != TerceroFieldType.Calculated)
            .Select(f => ($"{f.FichaKey}/{f.FieldKey}", $"{f.Label}"))
            .ToList();

    protected async Task LoadLookupCatalogAsync()
    {
        if (_lkModels.Count == 0) { _lkModels = (await LookupSvc.ListModelsAsync()).ToList(); }
    }

    /// <summary>Al elegir el tipo "Lista del Contenedor" carga el catalogo de modelos de datos para
    /// que el desplegable "Modelo de datos" no salga vacio.</summary>
    protected async Task OnCfgTypeChangedAsync()
    {
        if (_cfgType == TerceroFieldType.Lookup) { await LoadLookupCatalogAsync(); }
    }

    protected async Task OnLookupModelChangedAsync(ChangeEventArgs e)
    {
        _lkModelId = Guid.TryParse(e.Value?.ToString(), out var g) ? g : null;
        _lkTableId = null;
        _lkDisplayCol = null;
        _lkColumns.Clear();
        _lkAutofill.Clear();
        _lkFilters.Clear();
        _lkTables = _lkModelId is Guid mid ? (await LookupSvc.ListTablesAsync(mid)).ToList() : new();
    }

    protected async Task OnLookupTableChangedAsync(ChangeEventArgs e)
    {
        _lkTableId = Guid.TryParse(e.Value?.ToString(), out var g) ? g : null;
        // Cambiar de tabla invalida columnas: lo configurado apuntaba a otras.
        _lkDisplayCol = null;
        _lkAutofill.Clear();
        _lkFilters.Clear();
        _lkColumns = _lkTableId is Guid tid ? (await LookupSvc.ListColumnsAsync(tid)).ToList() : new();
    }

    protected void ResetLookupCfg()
    {
        _lkModelId = null;
        _lkTableId = null;
        _lkDisplayCol = null;
        _lkDisplayMode = DataLookupDisplayMode.Typeahead;
        _lkAllowCreate = false;
        _lkTables = new();
        _lkColumns = new();
        _lkAutofill.Clear();
        _lkFilters.Clear();
    }

    /// <summary>Carga en el formulario la configuracion guardada de un campo Lookup.</summary>
    protected async Task LoadLookupCfgAsync(string? options)
    {
        ResetLookupCfg();
        var cfg = DataLookupConfig.TryParse(options);
        if (cfg is null) { return; }

        await LoadLookupCatalogAsync();
        _lkTableId = cfg.TableId;
        _lkDisplayCol = cfg.DisplayColumnId;
        _lkDisplayMode = cfg.DisplayMode;
        _lkAllowCreate = cfg.AllowCreate;
        _lkModelId = cfg.ModelId;
        if (_lkModelId is Guid mid) { _lkTables = (await LookupSvc.ListTablesAsync(mid)).ToList(); }
        _lkColumns = (await LookupSvc.ListColumnsAsync(cfg.TableId)).ToList();

        foreach (var a in cfg.Autofill ?? [])
        {
            _lkAutofill.Add(new AutofillRow { ColumnId = a.ColumnId, TargetKey = a.TargetFieldKey });
        }
        foreach (var f in cfg.Filters ?? [])
        {
            _lkFilters.Add(new FilterRow
            {
                ColumnId = f.ColumnId,
                FromKey = f.FromFieldKey ?? "",
                Value = f.Value ?? "",
                Require = f.RequireSource
            });
        }
    }

    /// <summary>Serializa lo configurado. Devuelve null si aun no hay tabla elegida.</summary>
    protected string? BuildLookupOptions()
    {
        if (_lkTableId is not Guid tid) { return null; }
        var tabla = _lkTables.FirstOrDefault(t => t.Id == tid);
        var cfg = new DataLookupConfig(
            tid,
            ModelId: _lkModelId,
            DisplayColumnId: _lkDisplayCol,
            // Se descartan las filas a medio llenar en vez de guardarlas rotas.
            Filters: _lkFilters
                .Where(f => f.ColumnId != Guid.Empty
                    && (!string.IsNullOrWhiteSpace(f.FromKey) || !string.IsNullOrWhiteSpace(f.Value)))
                .Select(f => new DataLookupFilterConfig(
                    f.ColumnId,
                    string.IsNullOrWhiteSpace(f.Value) ? null : f.Value.Trim(),
                    string.IsNullOrWhiteSpace(f.FromKey) ? null : f.FromKey,
                    f.Require))
                .ToList(),
            Autofill: _lkAutofill
                .Where(a => a.ColumnId != Guid.Empty && !string.IsNullOrWhiteSpace(a.TargetKey))
                .Select(a => new DataLookupAutofillConfig(a.ColumnId, a.TargetKey))
                .ToList(),
            TableName: tabla?.Name,
            DisplayColumnName: _lkColumns.FirstOrDefault(c => c.Id == _lkDisplayCol)?.Name,
            DisplayMode: _lkDisplayMode,
            AllowCreate: _lkAllowCreate);
        return cfg.ToJson();
    }

    protected void ResetCfgForm()
    {
        _cfgEditingId = null;
        _cfgLabel = "";
        _cfgType = TerceroFieldType.Text;
        _cfgColumn = 1;
        _cfgOptions = "";
        ResetLookupCfg();
        _cfgDescription = "";
        _cfgError = null;
        _cfgFormula = "";
        _cfgFormulaError = null;
        _cfgShowInFilter = false;
        _cfgRepeatWith = "";
    }

    protected async Task EditCfgFieldAsync(TerceroFieldDto f)
    {
        _cfgEditingId = f.Id;
        _cfgLabel = f.Label;
        _cfgType = f.FieldType;
        _cfgColumn = Math.Clamp(f.Column, 1, 3);
        _cfgOptions = f.Options ?? "";
        _cfgDescription = f.Description ?? "";
        _cfgError = null;
        _cfgFormula = f.Formula ?? "";
        _cfgFormulaError = null;
        _cfgShowInFilter = f.ShowInFilter;
        _cfgRepeatWith = f.RepeatWithFieldKey ?? "";
        if (f.FieldType == TerceroFieldType.Lookup) { await LoadLookupCfgAsync(f.Options); }
        else { ResetLookupCfg(); }
    }

    protected void CancelEditCfgField() => ResetCfgForm();

    protected async Task SaveCfgFieldAsync()
    {
        _cfgError = null;
        var label = _cfgLabel.Trim();
        if (label.Length == 0)
        {
            _cfgError = "La etiqueta es obligatoria.";
            return;
        }
        var options = _cfgType switch
        {
            TerceroFieldType.Select => string.IsNullOrWhiteSpace(_cfgOptions) ? null : _cfgOptions.Trim(),
            TerceroFieldType.Lookup => BuildLookupOptions(),
            _ => null
        };
        if (_cfgType == TerceroFieldType.Lookup && options is null)
        {
            _cfgError = "Elige el modelo y la tabla que alimentan la lista.";
            return;
        }
        var desc = string.IsNullOrWhiteSpace(_cfgDescription) ? null : _cfgDescription.Trim();

        var formula = _cfgType == TerceroFieldType.Calculated ? _cfgFormula.Trim() : null;
        if (_cfgType == TerceroFieldType.Calculated)
        {
            // Se revalida contra el servidor con la clave real: mientras se escribe se valida con la
            // del campo en edicion, pero uno nuevo aun no tiene clave asignada.
            _cfgFormulaError = await FieldSvc.ValidateFormulaAsync(formula, _cfgEditingId, _cfgEditingId is null ? null : CfgEditingKey());
            if (_cfgFormulaError is not null) { return; }
        }
        var repeat = string.IsNullOrWhiteSpace(_cfgRepeatWith) ? null : _cfgRepeatWith;

        _busy = true;
        try
        {
            if (_cfgEditingId is Guid editId)
            {
                var upd = await FieldSvc.UpdateFieldAsync(editId, new UpdateTerceroFieldRequest(
                    label, _cfgType, _cfgColumn, options, desc, false, formula, _cfgShowInFilter, repeat));
                if (upd is null)
                {
                    _cfgError = "No se pudo guardar el campo.";
                    return;
                }
            }
            else
            {
                var created = await FieldSvc.CreateFieldAsync(new CreateTerceroFieldRequest(
                    _cfgFicha, label, _cfgType, _cfgColumn, options, null, desc, false, formula, _cfgShowInFilter, repeat));
                if (created is null)
                {
                    _cfgError = "No se pudo crear el campo. Revisa la ficha seleccionada.";
                    return;
                }
            }
            ResetCfgForm();
            await LoadCfgFieldsAsync();
        }
        finally { _busy = false; }
    }

    /// <summary>Clave del campo que se esta editando (para validar su formula sin autorreferencia).</summary>
    protected string? CfgEditingKey()
        => _cfgFields.FirstOrDefault(f => f.Id == _cfgEditingId)?.FieldKey;

    /// <summary>Valida la formula mientras se teclea, contra el servidor (que conoce los otros campos).</summary>
    protected async Task OnFormulaInputAsync(ChangeEventArgs e)
    {
        _cfgFormula = e.Value?.ToString() ?? string.Empty;
        _cfgFormulaError = string.IsNullOrWhiteSpace(_cfgFormula)
            ? null
            : await FieldSvc.ValidateFormulaAsync(_cfgFormula, _cfgEditingId, CfgEditingKey());
    }

    /// <summary>Campos que una formula puede usar: los numericos y los ya calculados, de cualquier ficha.</summary>
    protected List<TerceroFieldDto> NumericFieldsForFormula()
        => _cfgAllFields
            .Where(f => f.Id != _cfgEditingId)
            .Where(f => f.FieldType is TerceroFieldType.Number or TerceroFieldType.Currency or TerceroFieldType.Calculated)
            .OrderBy(f => f.FichaKey).ThenBy(f => f.SortOrder)
            .ToList();

    /// <summary>
    /// Claves que existen en mas de una ficha (la clave solo es unica DENTRO de la ficha). En una
    /// formula {clave} no diria a cual apunta, asi que el servidor las rechaza; aqui se marcan para
    /// no ofrecer algo que no se puede usar.
    /// </summary>
    protected HashSet<string> AmbiguousKeys()
        => _cfgAllFields
            .GroupBy(f => f.FieldKey, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);

    protected async Task InsertFormulaKeyAsync(string key)
    {
        _cfgFormula = string.IsNullOrWhiteSpace(_cfgFormula) ? $"{{{key}}}" : $"{_cfgFormula} {{{key}}}";
        _cfgFormulaError = await FieldSvc.ValidateFormulaAsync(_cfgFormula, _cfgEditingId, CfgEditingKey());
    }

    protected static string ColumnLabel(int column) => column switch
    {
        >= 3 => "grande",
        2 => "media",
        _ => "pequena"
    };

    protected string FichaLabel(string fichaKey)
        => _fichas.FirstOrDefault(f => f.FichaKey == fichaKey)?.Title ?? fichaKey;

    protected async Task MoveCfgFieldToFichaAsync(TerceroFieldDto f, string? targetFicha)
    {
        if (string.IsNullOrWhiteSpace(targetFicha)) { return; }
        _busy = true;
        _cfgError = null;
        try
        {
            _cfgError = await FieldSvc.MoveFieldToFichaAsync(f.Id, targetFicha);
            if (_cfgError is null)
            {
                if (_cfgEditingId == f.Id) { ResetCfgForm(); }
                await LoadCfgFieldsAsync();
            }
        }
        finally { _busy = false; }
    }

    protected async Task DeleteCfgFieldAsync(TerceroFieldDto f)
    {
        _busy = true;
        try
        {
            await FieldSvc.DeleteFieldAsync(f.Id);
            if (_cfgEditingId == f.Id) { ResetCfgForm(); }
            await LoadCfgFieldsAsync();
        }
        finally { _busy = false; }
    }

    protected async Task MoveCfgFieldAsync(int from, int to)
    {
        if (to < 0 || to >= _cfgFields.Count) { return; }
        var ordered = _cfgFields.Select(f => f.Id).ToList();
        var moved = ordered[from];
        ordered.RemoveAt(from);
        ordered.Insert(to, moved);
        _busy = true;
        try
        {
            await FieldSvc.ReorderFieldsAsync(new ReorderFieldsRequest(ordered));
            await LoadCfgFieldsAsync();
        }
        finally { _busy = false; }
    }

    /// <summary>
    /// Cicla el ancho pequena -> media -> grande -> pequena, sin abrir el editor. Antes solo alternaba
    /// entre dos porque solo habia dos.
    /// </summary>
    protected async Task CycleCfgColumnAsync(TerceroFieldDto f)
    {
        var newCol = f.Column >= 3 ? 1 : f.Column + 1;
        _busy = true;
        try
        {
            await FieldSvc.UpdateFieldAsync(f.Id, new UpdateTerceroFieldRequest(
                f.Label, f.FieldType, newCol, f.Options, f.Description, f.AllowMultiple,
                f.Formula, f.ShowInFilter, f.RepeatWithFieldKey));
            await LoadCfgFieldsAsync();
        }
        finally { _busy = false; }
    }

    // ---- Helpers de presentacion ----
    protected sealed record TagInfo(string Label, string Color, string Bg);

    protected static List<TagInfo> TipoTags(TerceroPerfil perfiles)
    {
        var tags = new List<TagInfo>();
        if ((perfiles & TerceroPerfil.Cliente) == TerceroPerfil.Cliente) { tags.Add(new("Cliente", "--t-violet", "--t-violet-bg")); }
        if ((perfiles & TerceroPerfil.Sospechoso) == TerceroPerfil.Sospechoso) { tags.Add(new("Sospechoso", "--t-rose", "--t-rose-bg")); }
        if ((perfiles & TerceroPerfil.Proveedor) == TerceroPerfil.Proveedor) { tags.Add(new("Proveedor", "--t-amber", "--t-amber-bg")); }
        if ((perfiles & TerceroPerfil.Empleado) == TerceroPerfil.Empleado) { tags.Add(new("Empleado", "--t-blue", "--t-blue-bg")); }
        return tags;
    }

    protected static TagInfo EstadoInfo(TerceroEstado estado) => estado switch
    {
        TerceroEstado.Activo => new("Activo", "--t-green", "--t-green-bg"),
        TerceroEstado.Prospecto => new("Prospecto", "--t-amber", "--t-amber-bg"),
        _ => new("Inactivo", "--t-slate", "--t-slate-bg")
    };

    protected static readonly string[] AvatarPalette =
        { "--t-violet", "--t-blue", "--t-amber", "--t-green", "--t-rose", "--t-slate" };

    protected static string AvatarColor(string name)
    {
        var hash = 0;
        foreach (var ch in name) { hash = (hash * 31 + ch) & 0x7fffffff; }
        return $"var({AvatarPalette[hash % AvatarPalette.Length]})";
    }

    protected static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) { return "?"; }
        return parts.Length == 1
            ? parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant()
            : string.Concat(char.ToUpperInvariant(parts[0][0]), char.ToUpperInvariant(parts[1][0]));
    }
}
