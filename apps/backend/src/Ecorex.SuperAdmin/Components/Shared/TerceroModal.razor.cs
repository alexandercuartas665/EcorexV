// Code-behind de TerceroModal (Directorio Clasico). Separado del .razor por la regla de tope de
// 2000 lineas/archivo (Ola 6, refactor): el markup queda en el .razor y toda la logica (el antiguo
// bloque @code) vive aqui SIN cambios de comportamiento. Los servicios inyectados (@inject) siguen en
// el .razor y son accesibles por ser la MISMA clase parcial.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Ecorex.Application.Catalogos;
using Ecorex.Application.Directorio;
using Ecorex.Application.Formulas;
using Ecorex.Application.Forms;
using Ecorex.Application.Gestor;
using Ecorex.Application.Roles;
using Ecorex.Domain.Enums;
using Ecorex.SuperAdmin.Auth;
using Ecorex.SuperAdmin.Components.Shared.Forms;

namespace Ecorex.SuperAdmin.Components.Shared;

public partial class TerceroModal
{
    /// <summary>Se dispara tras cualquier cambio persistente (crear/editar tercero, alta/baja de
    /// contacto de relacion, nota que crea cita/oportunidad) para que el host refresque su lista.</summary>
    [Parameter] public EventCallback OnChanged { get; set; }

    /// <summary>Opcional: si el host lo provee, al CREAR un tercero el modal NO se queda en edicion: cierra
    /// y notifica el Id del recien creado (para que el host lo SELECCIONE, p.ej. la wizard de tareas).</summary>
    [Parameter] public EventCallback<Guid> OnCreated { get; set; }

    /// <summary>Cuando true (Cargador de contactos 000740) habilita el cableado CRM: muestra las
    /// oportunidades del tercero en el aside, la fecha de "Proxima atencion" y crea cita/oportunidad
    /// al registrar esas notas. En Directorio General (false) las notas son solo bitacora.</summary>
    [Parameter] public bool CrmWiring { get; set; }

    /// <summary>Se dispara cuando el usuario pulsa "Incorporar al directorio" sobre un prospecto scrapeado
    /// abierto en este modal (modo <see cref="_prospectoId"/>). El host promueve el prospecto (crea el
    /// Tercero rico) y reabre el modal en edicion sobre ese tercero. La incorporacion es EXPLICITA.</summary>
    [Parameter] public EventCallback<Guid> OnIncorporarProspecto { get; set; }

    /// <summary>Variante Especializada del Directorio (ADR-0088): solo cambia detalles visuales de este
    /// modal (marcador en el encabezado). La logica es la MISMA para ambas variantes.</summary>
    [Parameter] public bool Especializado { get; set; }

    // Actor para el alta de tareas-proceso (quien registra la gestion).
    [CascadingParameter] private Task<Microsoft.AspNetCore.Components.Authorization.AuthenticationState>? AuthState { get; set; }

    private bool _busy;
    private List<OportunidadDto> _opps = new();

    // ---- Formularios asociados (3a columna). Respuestas ancladas por Reference = "TERCERO:{id}". ----
    private List<TerceroFormLinkDto> _forms = new();
    private Guid? _formSel;

    // ---- Permisos (sub-permisos nombrados del Directorio General, ADR-0033) ----
    private bool _canCrearEmpresa = true;
    private bool _canCrearCliente = true;
    private bool _canCrearSospechoso = true;

    // ---- Modal crear / editar ----
    private bool _modalOpen;
    private Guid? _editingId;
    // Prospecto scrapeado abierto en modo "incorporar" (aun NO es Tercero): el modal muestra su ficha en
    // solo-lectura sobre los datos base y el pie cambia el guardar por "Incorporar al directorio". null en
    // el uso normal (crear/editar terceros). Se limpia SIEMPRE en OpenCreate/OpenEditAsync.
    private Guid? _prospectoId;
    private string? _prospectoFuente;
    // Empresa a la que pertenece este tercero (Persona). Al CREAR un contacto asociado se fija a la
    // empresa padre; al EDITAR se carga la existente para PRESERVARLA (antes se mandaba null y editar
    // una persona la desvinculaba de su empresa). null para empresas y personas individuales.
    private Guid? _empresaId;
    // Cuando se crea/edita un contacto DESDE la pestana Relaciones de una empresa, se recuerda esa
    // empresa para VOLVER a ella (a su pestana Relaciones) al guardar o cancelar. Con una sola
    // instancia de modal evitamos anidar dos TerceroModal sobre el mismo DbContext scoped.
    private Guid? _returnToParentId;
    private string _mTab = "datos";
    private TerceroTipo _mTipo = TerceroTipo.Empresa;
    private TerceroPerfil _mPerfiles = TerceroPerfil.Ninguno;
    private TerceroEstado _mEstado = TerceroEstado.Activo;
    private TerceroIdTipo _mIdTipo = TerceroIdTipo.Nit;
    private string _mIdValor = "";
    // Popover flotante de "Identificar por" sobre el boton de tipo (libera el ancho de la ficha).
    private bool _idboxOpen;

    // Buscador vivo del modo CREAR: busca terceros existentes; si no hay, se crea con lo escrito.
    private List<TerceroListItemDto> _buscadorMatches = new();
    private bool _buscadorOpen;
    private string _mNombre = "";
    private string _mVendedor = "";
    // Vendedor asignado desde el catalogo de asesores (000074). _mVendedor queda como texto legado.
    private Guid? _mVendedorAsesorId;
    private List<Ecorex.Application.Asesores.AsesorOptionDto> _asesorOptions = new();
    private string MVendedorAsesorIdStr
    {
        get => _mVendedorAsesorId?.ToString() ?? "";
        set => _mVendedorAsesorId = Guid.TryParse(value, out var g) ? g : (Guid?)null;
    }
    private string _mCiudad = "";
    // Autocomplete de ciudad (catalogo global): _ciudadQuery es el texto que se ve/filtra; el valor
    // confirmado vive en _mCiudad (solo se escribe al elegir una ciudad real de la lista).
    private string _ciudadQuery = "";
    private bool _ciudadOpen;
    private IReadOnlyList<CiudadDto> _ciudadMatches = Array.Empty<CiudadDto>();
    private string _mSector = "";
    private string _mCargo = "";
    private string _mEmail = "";
    private string _mTelefono = "";
    /// <summary>Foto del tercero (URL en wwwroot/uploads/terceros/{tenant}). Se sube en la cabecera del
    /// modal (crear o editar) y viaja en el SaveTerceroRequest.</summary>
    private string _mImagenUrl = "";
    private bool _fotoBusy;
    private readonly Dictionary<string, Dictionary<string, string>> _fichaValues = new(StringComparer.Ordinal);
    private readonly HashSet<string> _fichaOpen = new(StringComparer.Ordinal);
    private string? _modalError;

    // ---- Tab Relaciones (contactos del tercero en edicion) ----
    private IReadOnlyList<TerceroContactoDto> _relContactos = Array.Empty<TerceroContactoDto>();

    // ---- Editor de contacto (sub-modal de la pestana Relaciones y de la tabla del host) ----
    private bool _coOpen;
    private Guid _coParentId;
    private Guid? _coEditId;
    private string _coNombre = "", _coCargo = "", _coEmail = "", _coTelefono = "";
    private string? _coError;

    // ---- Tab Contacto Cliente (gestiones por concepto de actividad, 000125) ----
    private IReadOnlyList<TerceroNotaDto> _notas = Array.Empty<TerceroNotaDto>();
    private string _noteText = "";
    // Datos del PROCESO (fuera del formulario): titulo, valor (si maneja valor) y fecha de la proxima
    // actividad (si es evento de agenda). Alimentan el modulo de Oportunidades / la agenda.
    private string _procTitulo = "";
    private string _procValor = "";
    private DateTime? _procFecha;
    // Conceptos de actividad del CRM: botones que cargan su formulario asociado (000125).
    private IReadOnlyList<Ecorex.Application.Crm.ConceptoActividadDto> _conceptos = Array.Empty<Ecorex.Application.Crm.ConceptoActividadDto>();
    private Ecorex.Application.Crm.ConceptoActividadDto? _noteConcepto;
    private string? _noteError;

    // ---- Campos configurables por ficha (cache; se recarga al abrir el modal). ----
    private readonly Dictionary<string, List<TerceroFieldDto>> _fichaFields = new(StringComparer.Ordinal);

    private bool _permsLoaded;
    private Guid _actorId;

    // Permisos + defaults de campos se cargan PEREZOSAMENTE al abrir el modal (evento de usuario),
    // NO en OnInitializedAsync: el host y este componente comparten el mismo DbContext scoped y sus
    // OnInit corren en paralelo, lo que dispara "A second operation was started on this context".
    private async Task EnsureReadyAsync()
    {
        if (!_permsLoaded)
        {
            var eff = await Perms.GetAsync();
            _canCrearEmpresa = eff.Can(DirectorioSubPermisos.CrearEmpresa, PermissionAction.Create);
            _canCrearCliente = eff.Can(DirectorioSubPermisos.CrearCliente, PermissionAction.Create);
            _canCrearSospechoso = eff.Can(DirectorioSubPermisos.CrearSospechoso, PermissionAction.Create);
            _actorId = await ActorIdAsync();
            _permsLoaded = true;
        }
        await FieldSvc.EnsureDefaultsAsync();
        _fichas = (await FichaSvc.ListAsync()).ToList();
        // Asesores activos para el selector "Vendedor asignado". Se recarga en cada apertura para
        // reflejar altas recientes del catalogo; es una lista corta por tenant.
        _asesorOptions = (await AsesorSvc.ListOptionsAsync()).ToList();
    }

    private async Task LoadFichaFieldsAsync()
    {
        _fichaFields.Clear();
        foreach (var m in _fichas)
        {
            var list = await FieldSvc.ListByFichaAsync(m.FichaKey);
            _fichaFields[m.FichaKey] = list.OrderBy(f => f.SortOrder).ToList();
        }
    }

    private IReadOnlyList<TerceroFieldDto> FieldsFor(string ficha)
        => _fichaFields.TryGetValue(ficha, out var l) ? l : (IReadOnlyList<TerceroFieldDto>)Array.Empty<TerceroFieldDto>();

    private static IEnumerable<string> SplitOptions(string? options)
        => string.IsNullOrWhiteSpace(options)
            ? Array.Empty<string>()
            : options.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string InputType(TerceroFieldType t) => t switch
    {
        TerceroFieldType.Number => "number",
        TerceroFieldType.Currency => "number",
        TerceroFieldType.Date => "date",
        TerceroFieldType.Phone => "tel",
        _ => "text"
    };

    // ---- Autocomplete de Ciudad (catalogo global de municipios de Colombia) ----
    private async Task OnCiudadFocus()
    {
        _ciudadOpen = true;
        _ciudadMatches = await CiudadSvc.SearchAsync(_ciudadQuery, 30);
    }

    private async Task OnCiudadInput(ChangeEventArgs e)
    {
        _ciudadQuery = e.Value?.ToString() ?? "";
        _ciudadOpen = true;
        _ciudadMatches = await CiudadSvc.SearchAsync(_ciudadQuery, 30);
    }

    private void SelectCiudad(CiudadDto c)
    {
        _mCiudad = c.Nombre;      // valor confirmado que se guarda
        _ciudadQuery = c.Nombre;  // texto visible
        _ciudadOpen = false;
    }

    private async Task OnCiudadBlur()
    {
        _ciudadOpen = false;
        var q = (_ciudadQuery ?? "").Trim();
        if (q.Length == 0)
        {
            // Ciudad es opcional: dejar vacio es valido.
            _mCiudad = "";
            _ciudadQuery = "";
            return;
        }
        // Restringe a ciudades reales: acepta el texto solo si coincide EXACTO (case-insensitive)
        // con una ciudad del catalogo; si no, revierte al ultimo valor confirmado (_mCiudad).
        var matches = await CiudadSvc.SearchAsync(q, 10);
        var exact = matches.FirstOrDefault(m => string.Equals(m.Nombre, q, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            _mCiudad = exact.Nombre;
            _ciudadQuery = exact.Nombre;
        }
        else
        {
            _ciudadQuery = _mCiudad ?? "";
        }
    }

    // ---- Crear / editar ----
    /// <summary>Abre el modal en modo "crear tercero". Con <paramref name="preselectPerfil"/> nace con esa
    /// pildora de perfil ya activada y su(s) ficha(s) abiertas (p.ej. al crear desde el filtro "Clientes").</summary>
    public async Task OpenCreate(TerceroPerfil preselectPerfil = TerceroPerfil.Ninguno, string? nombre = null)
    {
        await EnsureReadyAsync();
        _editingId = null;
        _prospectoId = null;
        _prospectoFuente = null;
        _empresaId = null;
        _returnToParentId = null;
        _mTab = "datos";
        _mTipo = _canCrearEmpresa ? TerceroTipo.Empresa : TerceroTipo.Persona;
        _mPerfiles = preselectPerfil;
        _mEstado = TerceroEstado.Activo;
        _mIdTipo = _mTipo == TerceroTipo.Empresa ? TerceroIdTipo.Nit : TerceroIdTipo.Identificacion;
        _mIdValor = "";
        _idboxOpen = false;
        _confirmDelContactoId = null;
        _buscadorMatches = new();
        _buscadorOpen = false;
        _mNombre = _mVendedor = _mCiudad = _mSector = _mCargo = _mEmail = _mTelefono = "";
        _mImagenUrl = "";
        // Prefill del nombre (p.ej. lo que el operador escribio en el autocompletar antes de "Crear tercero").
        if (!string.IsNullOrWhiteSpace(nombre)) { _mNombre = nombre.Trim(); }
        _mVendedorAsesorId = null;
        _ciudadQuery = "";
        _ciudadOpen = false;
        _ciudadMatches = Array.Empty<CiudadDto>();
        _fichaValues.Clear();
        _fichaOpen.Clear();
        await LoadFichaFieldsAsync();
        // Si nace con un perfil preseleccionado, abre sus fichas de datos (las que ese perfil hace visibles).
        if (preselectPerfil != TerceroPerfil.Ninguno)
        {
            foreach (var f in VisibleFichas()) { _fichaOpen.Add(f.FichaKey); }
        }
        _relContactos = Array.Empty<TerceroContactoDto>();
        _notas = Array.Empty<TerceroNotaDto>();
        _opps = new();
        _forms = new();
        _formSel = null;
        ResetNoteForm();
        _modalError = null;
        _modalOpen = true;
        StateHasChanged();
    }

    /// <summary>
    /// Abre el modal para CREAR un contacto asociado como Tercero Persona vinculado a
    /// <paramref name="empresaId"/> (mismo modal completo, no un mini-modal: un contacto ES un tercero
    /// con sus mismos datos y relaciones). Si viene <paramref name="returnToParent"/>, al guardar o
    /// cancelar se vuelve a esa empresa (pestana Relaciones).
    /// </summary>
    public async Task OpenCreateContactAsync(Guid empresaId, Guid? returnToParent = null)
    {
        await OpenCreate();
        _mTipo = TerceroTipo.Persona;
        _empresaId = empresaId;
        _returnToParentId = returnToParent;
        // Documento por defecto de una persona.
        _mIdTipo = TerceroIdTipo.Identificacion;
        StateHasChanged();
    }

    /// <summary>Abre EL MISMO modal grande sobre un prospecto scrapeado que AUN NO es Tercero: pre-carga sus
    /// datos base y, en el pie, cambia el guardar por "Incorporar al directorio" (la incorporacion es
    /// EXPLICITA). Reusa el modo crear para que el operador vea la ficha completa; al incorporar, el host
    /// promueve el prospecto (Tercero rico con ficha base + encadenado de empresa) y reabre en edicion.</summary>
    public async Task OpenFromProspectoAsync(
        Guid prospectoId, string nombre, bool esEmpresa, string? fuente = null,
        string? cargo = null, string? empresa = null, string? ciudad = null,
        string? email = null, string? telefono = null, string? imagenUrl = null)
    {
        await OpenCreate();
        _prospectoId = prospectoId;
        _prospectoFuente = fuente;
        _editingId = null;
        _mTipo = esEmpresa ? TerceroTipo.Empresa : TerceroTipo.Persona;
        _mIdTipo = esEmpresa ? TerceroIdTipo.Nit : TerceroIdTipo.Identificacion;
        _mNombre = nombre?.Trim() ?? "";
        _mCargo = cargo?.Trim() ?? "";
        _mSector = empresa?.Trim() ?? "";
        _mCiudad = ciudad?.Trim() ?? "";
        _ciudadQuery = _mCiudad;
        _mEmail = email?.Trim() ?? "";
        _mTelefono = telefono?.Trim() ?? "";
        _mImagenUrl = imagenUrl ?? "";
        StateHasChanged();
    }

    /// <summary>Incorpora al Directorio el prospecto abierto (modo <see cref="_prospectoId"/>): delega en el
    /// host (promueve + reabre en edicion). No hace el alta aqui para no duplicar el Tercero.</summary>
    private async Task IncorporarProspectoAsync()
    {
        if (_prospectoId is not Guid pid) { return; }
        _busy = true;
        var id = pid;
        _prospectoId = null;
        CloseModal();
        await OnIncorporarProspecto.InvokeAsync(id);
        _busy = false;
    }

    /// <summary>Abre el modal en modo "editar tercero". Con <paramref name="returnToParent"/> vuelve a
    /// esa empresa al guardar/cancelar (edicion de un contacto desde la pestana Relaciones).</summary>
    public async Task OpenEditAsync(Guid id, Guid? returnToParent = null)
    {
        await EnsureReadyAsync();
        var d = await TerceroSvc.GetAsync(id);
        if (d is null) { return; }
        await LoadFichaFieldsAsync();
        _editingId = id;
        _prospectoId = null;
        _prospectoFuente = null;
        _empresaId = d.EmpresaId;
        _returnToParentId = returnToParent;
        _mTab = "datos";
        _mTipo = d.Tipo;
        _mPerfiles = d.Perfiles;
        _mEstado = d.Estado == TerceroEstado.Inactivo ? TerceroEstado.Activo : d.Estado;
        _mIdTipo = d.IdTipo;
        _mIdValor = d.IdValor ?? "";
        _idboxOpen = false;
        _confirmDelContactoId = null;
        _mNombre = d.Nombre;
        _mVendedor = d.Vendedor ?? "";
        _mVendedorAsesorId = d.VendedorAsesorId;
        _mCiudad = d.Ciudad ?? "";
        _ciudadQuery = _mCiudad;
        _ciudadOpen = false;
        _ciudadMatches = Array.Empty<CiudadDto>();
        _mSector = d.Sector ?? "";
        _mCargo = d.Cargo ?? "";
        _mEmail = d.Email ?? "";
        _mImagenUrl = d.ImagenUrl ?? "";
        _mTelefono = d.Telefono ?? "";
        _fichaValues.Clear();
        _fichaOpen.Clear();
        foreach (var ficha in d.Fichas)
        {
            var campos = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var campo in ficha.Value)
            {
                campos[campo.Key] = campo.Value ?? "";
            }
            _fichaValues[ficha.Key] = campos;
        }
        RecalcCalculated();   // al abrir: los calculados se muestran ya resueltos, sin tocar nada
        _relContactos = await TerceroSvc.ListContactosAsync(id);
        _notas = await TerceroSvc.ListNotasAsync(id);
        _opps = CrmWiring ? (await GestorSvc.ListOportunidadesByTerceroAsync(id)).ToList() : new();
        _forms = (await FormsSvc.ListAsync()).ToList();
        _conceptos = await ConceptosSvc.ListAsync();
        _formSel = null;
        ResetNoteForm();
        _modalError = null;
        _modalOpen = true;
        StateHasChanged();
    }

    /// <summary>Recarga las oportunidades del tercero en edicion (el host la llama tras crear una).</summary>
    public async Task ReloadOpportunitiesAsync()
    {
        if (CrmWiring && _editingId is Guid id)
        {
            _opps = (await GestorSvc.ListOportunidadesByTerceroAsync(id)).ToList();
            StateHasChanged();
        }
    }

    private void CloseModal()
    {
        _modalOpen = false;
        _editingId = null;
        _empresaId = null;
        _returnToParentId = null;
    }

    /// <summary>Cancelar/cerrar: si estabamos en un contacto abierto DESDE una empresa, volvemos a ella
    /// (pestana Relaciones) en vez de cerrar todo; si no, se cierra el modal.</summary>
    private async Task BackOrCloseAsync()
    {
        if (_returnToParentId is Guid pid)
        {
            _returnToParentId = null;
            await OpenEditAsync(pid);
            _mTab = "rel";
            StateHasChanged();
            return;
        }
        CloseModal();
    }

    private void SetTipoModal(TerceroTipo tipo)
    {
        _mTipo = tipo;
        // Ajusta el documento por defecto solo si aun no hay valor capturado.
        if (string.IsNullOrEmpty(_mIdValor))
        {
            _mIdTipo = tipo == TerceroTipo.Empresa ? TerceroIdTipo.Nit : TerceroIdTipo.Identificacion;
        }
        // Al elegir el tipo, abre la tarjeta flotante de identificacion (se queda abierta).
        _idboxOpen = true;
    }

    // ---- Buscador (modo crear): encuentra terceros existentes por nombre/documento ----
    private CancellationTokenSource? _buscadorCts;

    private async Task OnBuscadorInput(ChangeEventArgs e)
    {
        _mNombre = e.Value?.ToString() ?? "";
        var q = _mNombre.Trim();
        // Cancela la busqueda pendiente anterior: al teclear rapido evita consultas SOLAPADAS sobre el
        // mismo DbContext (que reventaban el circuito con "second operation on this context").
        _buscadorCts?.Cancel();
        if (q.Length < 2) { _buscadorMatches = new(); _buscadorOpen = false; return; }
        var cts = new CancellationTokenSource();
        _buscadorCts = cts;
        try
        {
            await Task.Delay(250, cts.Token); // debounce: solo busca tras una pausa al teclear
            var rows = await TerceroSvc.ListAsync(new TerceroListFilter(Busqueda: q), cts.Token);
            if (cts.IsCancellationRequested) { return; }
            _buscadorMatches = rows.Take(6).ToList();
            _buscadorOpen = _buscadorMatches.Count > 0;
            StateHasChanged();
        }
        catch (OperationCanceledException) { /* un teclazo nuevo cancelo esta busqueda */ }
    }

    private void OnBuscadorFocus() { _buscadorOpen = _buscadorMatches.Count > 0; }
    private void OnBuscadorBlur() { _buscadorOpen = false; }

    /// <summary>El usuario eligio un tercero YA existente del buscador: abre su ficha en el mismo modal.</summary>
    private async Task OpenExistingAsync(Guid id)
    {
        _buscadorOpen = false;
        _buscadorMatches = new();
        await OpenEditAsync(id, _returnToParentId);
    }

    private bool HasPerfil(TerceroPerfil p) => (_mPerfiles & p) == p;

    private void TogglePerfil(TerceroPerfil p)
    {
        if (HasPerfil(p)) { _mPerfiles &= ~p; }
        else { _mPerfiles |= p; }
    }

    private void ToggleFicha(string key)
    {
        if (!_fichaOpen.Add(key)) { _fichaOpen.Remove(key); }
    }

    private string GetFicha(string ficha, string field)
        => _fichaValues.TryGetValue(ficha, out var m) && m.TryGetValue(field, out var v) ? v : "";

    private void SetFicha(string ficha, string field, string? val)
    {
        if (!_fichaValues.TryGetValue(ficha, out var m))
        {
            m = new Dictionary<string, string>(StringComparer.Ordinal);
            _fichaValues[ficha] = m;
        }
        m[field] = val ?? "";

        // Cualquier dato puede alimentar una formula, asi que los calculados se rehacen aqui. Es
        // aritmetica en memoria sobre unas pocas claves: no toca la base ni el servicio.
        RecalcCalculated();
    }

    // ---- Campos tipo lista del Contenedor de datos ----

    /// <summary>
    /// Configuracion del campo, leida de Options. Se cachea por campo porque el render la pide en
    /// cada pasada y deserializar en cada una seria gratuito solo en apariencia.
    /// </summary>
    private readonly Dictionary<Guid, Ecorex.Application.DataLookups.DataLookupConfig?> _lookupCfgs = new();

    private Ecorex.Application.DataLookups.DataLookupConfig? LookupCfg(TerceroFieldDto f)
    {
        if (_lookupCfgs.TryGetValue(f.Id, out var cached)) { return cached; }
        var cfg = Ecorex.Application.DataLookups.DataLookupConfig.TryParse(f.Options);
        _lookupCfgs[f.Id] = cfg;
        return cfg;
    }

    /// <summary>
    /// Estado del formulario COMPLETO con las llaves cualificadas por ficha ("comercial/ciudad"),
    /// que es como la configuracion referencia los campos origen. Sin cualificar, dos campos
    /// homonimos en fichas distintas serian indistinguibles y la cascada miraria el equivocado.
    /// </summary>
    private Dictionary<string, string?> TodosLosValores()
    {
        var todos = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (ficha, campos) in _fichaValues)
        {
            foreach (var (key, val) in campos)
            {
                todos[$"{ficha}/{key}"] = val;
            }
        }
        return todos;
    }

    /// <summary>
    /// Vuelca en otros campos lo que trajo la fila elegida. Los destinos vienen cualificados por
    /// ficha; un destino que ya no exista se ignora en vez de crear una entrada huerfana.
    /// </summary>
    private void AplicarAutollenado(IReadOnlyDictionary<string, string?> valores)
    {
        foreach (var (target, val) in valores)
        {
            var i = target.IndexOf('/');
            if (i <= 0 || i >= target.Length - 1) { continue; }
            var ficha = target[..i];
            var campo = target[(i + 1)..];
            if (!_fichaFields.ContainsKey(ficha)) { continue; }
            SetFicha(ficha, campo, val);
        }
        StateHasChanged();
    }

    /// <summary>Escribe un valor sin disparar el recalculo (para volcar el resultado ya calculado).</summary>
    private void SetFichaRaw(string ficha, string field, string val)
    {
        if (!_fichaValues.TryGetValue(ficha, out var m))
        {
            m = new Dictionary<string, string>(StringComparer.Ordinal);
            _fichaValues[ficha] = m;
        }
        m[field] = val;
    }

    // ---- Campos repetidos (RepeatWithFieldKey) ----
    //
    // Un campo repetido captura VARIAS veces el mismo dato: tantas como diga otro campo numerico
    // (ej. "acompanantes" = 3 -> "nombre" se pide 3 veces). Los valores se guardan como un arreglo
    // JSON dentro de la MISMA celda, asi que FichasJson sigue siendo ficha -> campo -> texto y el
    // formato de datos no cambia. Es lo que hace el proyecto hermano.

    /// <summary>Cuantas veces se repite el campo, segun el valor del campo que lo gobierna.</summary>
    private int RepeatCount(string ficha, string repeatFieldKey)
    {
        var raw = GetFicha(ficha, repeatFieldKey);
        // El gobernante puede vivir en otra ficha: si no esta aqui, se busca en todas.
        if (string.IsNullOrWhiteSpace(raw))
        {
            foreach (var (_, campos) in _fichaValues)
            {
                if (campos.TryGetValue(repeatFieldKey, out var v) && !string.IsNullOrWhiteSpace(v)) { raw = v; break; }
            }
        }
        // Tope de 20: evita que un cero de mas en el gobernante pinte mil inputs y cuelgue el circuito.
        return int.TryParse(raw, out var n) ? Math.Clamp(n, 0, 20) : 0;
    }

    /// <summary>Etiqueta del campo que gobierna la repeticion, para poder nombrarlo al usuario.</summary>
    private string FieldLabel(string fieldKey)
        => AllFieldDefs().FirstOrDefault(f => f.FieldKey == fieldKey)?.Label ?? fieldKey;

    /// <summary>Valores del campo repetido, ajustados a las filas pedidas.</summary>
    private List<string> RepeatValues(string ficha, string field, int count)
    {
        var list = new List<string>();
        var raw = GetFicha(ficha, field);
        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                if (raw.TrimStart().StartsWith('['))
                {
                    list = JsonSerializer.Deserialize<List<string>>(raw) ?? new();
                }
                else
                {
                    list.Add(raw);   // venia de cuando el campo no se repetia: se conserva como el 1o
                }
            }
            catch (JsonException)
            {
                list = new List<string> { raw };
            }
        }
        while (list.Count < count) { list.Add(""); }
        if (list.Count > count) { list = list.Take(count).ToList(); }
        return list;
    }

    private void SetRepeatValue(string ficha, string field, int index, int count, string? value)
    {
        var list = RepeatValues(ficha, field, count);
        if (index < 0 || index >= list.Count) { return; }
        list[index] = value ?? "";
        // Todo vacio -> se borra la celda, para no guardar ["","",""] .
        SetFicha(ficha, field, list.Any(v => !string.IsNullOrWhiteSpace(v)) ? JsonSerializer.Serialize(list) : "");
    }

    /// <summary>Ancho del campo en la rejilla de 3 (ADR-0029). El separador siempre ocupa la fila.</summary>
    private static string WidthClass(TerceroFieldDto f) => f.FieldType == TerceroFieldType.Separator || f.Column >= 3
        ? "dg-fld-full"
        : f.Column == 2 ? "dg-fld-med" : "";

    /// <summary>
    /// Resultado de cada campo calculado, por clave. Null = no se pudo calcular. Se aplanan los
    /// valores de TODAS las fichas porque una formula puede cruzarlas (los valores del tercero viven
    /// juntos en FichasJson); las claves repetidas entre fichas ya las rechaza el configurador.
    /// </summary>
    private Dictionary<string, string?> _calculated = new(StringComparer.Ordinal);

    /// <summary>Definiciones de TODAS las fichas, aplanadas (los campos viven agrupados por ficha).</summary>
    private IEnumerable<TerceroFieldDto> AllFieldDefs() => _fichaFields.SelectMany(kv => kv.Value);

    private void RecalcCalculated()
    {
        var calculated = AllFieldDefs()
            .Where(f => f.FieldType == TerceroFieldType.Calculated && !string.IsNullOrWhiteSpace(f.Formula))
            .Select(f => new CalculatedField(f.FieldKey, f.Formula!))
            .ToList();

        if (calculated.Count == 0)
        {
            if (_calculated.Count > 0) { _calculated = new(StringComparer.Ordinal); }
            return;
        }

        var flat = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (_, campos) in _fichaValues)
        {
            foreach (var (key, value) in campos) { flat[key] = value; }
        }

        _calculated = FormulaCalculator.EvaluateAll(calculated, flat).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
    }

    private string IdLabel() => _mIdTipo switch
    {
        TerceroIdTipo.Nit => "NIT",
        TerceroIdTipo.Identificacion => "Numero de identificacion",
        TerceroIdTipo.Correo => "Correo electronico",
        TerceroIdTipo.Telefono => "Telefono",
        _ => "Documento"
    };

    private string IdPlaceholder() => _mIdTipo switch
    {
        TerceroIdTipo.Nit => "900.123.456-7",
        TerceroIdTipo.Identificacion => "1.234.567.890",
        TerceroIdTipo.Correo => "correo@empresa.com",
        TerceroIdTipo.Telefono => "300 000 0000",
        _ => ""
    };

    private async Task SaveAsync(bool andNew = false)
    {
        _modalError = null;
        var nombre = _mNombre.Trim();
        if (nombre.Length == 0)
        {
            _modalError = "El nombre es obligatorio.";
            return;
        }
        if (_mIdTipo != TerceroIdTipo.Ninguno && string.IsNullOrWhiteSpace(_mIdValor))
        {
            _modalError = $"{IdLabel()} es obligatorio (o elige 'No se identifica').";
            return;
        }

        // Los calculados se MATERIALIZAN al guardar (ADR-0029): asi reportes, exportaciones y API
        // leen el numero sin reimplementar el motor. Se rehacen antes por si quedo algo sin recalcular.
        RecalcCalculated();
        foreach (var def in AllFieldDefs().Where(f => f.FieldType == TerceroFieldType.Calculated))
        {
            var value = _calculated.GetValueOrDefault(def.FieldKey);
            SetFichaRaw(def.FichaKey, def.FieldKey, value ?? "");
        }

        // Serializa solo las fichas visibles con al menos un campo con valor.
        var fichas = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var def in VisibleFichas())
        {
            if (!_fichaValues.TryGetValue(def.FichaKey, out var vals)) { continue; }
            var nonEmpty = vals
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            if (nonEmpty.Count > 0) { fichas[def.FichaKey] = nonEmpty; }
        }
        var fichasJson = fichas.Count > 0 ? JsonSerializer.Serialize(fichas) : null;

        var request = new SaveTerceroRequest(
            Nombre: nombre,
            Tipo: _mTipo,
            Perfiles: _mPerfiles,
            Estado: _mEstado,
            Vendedor: Blank(_mVendedor),
            VendedorAsesorId: _mVendedorAsesorId,
            Ciudad: Blank(_mCiudad),
            IdTipo: _mIdTipo,
            IdValor: _mIdTipo == TerceroIdTipo.Ninguno ? null : Blank(_mIdValor),
            Sector: _mTipo == TerceroTipo.Empresa ? Blank(_mSector) : null,
            Cargo: _mTipo == TerceroTipo.Persona ? Blank(_mCargo) : null,
            Email: _mTipo == TerceroTipo.Persona ? Blank(_mEmail) : null,
            Telefono: _mTipo == TerceroTipo.Persona ? Blank(_mTelefono) : null,
            // Persona: conserva/asigna su empresa (el servicio lo ignora para tipo Empresa). Antes iba
            // null y editar una persona la desvinculaba de su empresa.
            EmpresaId: _empresaId,
            FichasJson: fichasJson,
            ImagenUrl: Blank(_mImagenUrl));

        var wasCreate = _editingId is null;
        _busy = true;
        var res = wasCreate
            ? await TerceroSvc.CreateAsync(request)
            : await TerceroSvc.UpdateAsync(_editingId!.Value, request);
        _busy = false;

        if (!res.IsOk)
        {
            _modalError = res.Error;
            return;
        }
        await OnChanged.InvokeAsync();
        // Si veniamos de la pestana Relaciones de una empresa, VOLVEMOS a ella (con el nuevo contacto ya
        // visible), en vez de cerrar; el modal es uno solo, no dos anidados.
        if (_returnToParentId is Guid parentId)
        {
            _returnToParentId = null;
            await OpenEditAsync(parentId);
            _mTab = "rel";
            StateHasChanged();
            return;
        }
        // Al CREAR se DEJA EL MODAL ABIERTO en modo edicion del registro recien creado: confirma el
        // guardado (se recarga desde el servidor) y permite seguir editando/agregando sin reabrir. Al
        // EDITAR se cierra como siempre.
        if (wasCreate && res.Value is { } created)
        {
            // "Guardar y nuevo": guarda y abre otra ficha de creacion en blanco (crear uno tras otro).
            if (andNew)
            {
                await OpenCreate();
                StateHasChanged();
                return;
            }
            // Modo "picker-create" (p.ej. la wizard de tareas): notifica el recien creado y CIERRA, para
            // que el host lo seleccione. Sin OnCreated, se queda en edicion como siempre (DirectorioGeneral).
            if (OnCreated.HasDelegate)
            {
                await OnCreated.InvokeAsync(created.Id);
                CloseModal();
                return;
            }
            await OpenEditAsync(created.Id);
            StateHasChanged();
            return;
        }
        // Al EDITAR NO se cierra: se recarga el registro y el modal SIGUE ABIERTO (se cierra con Cancelar/x).
        if (_editingId is Guid editedId)
        {
            await OpenEditAsync(editedId);
            StateHasChanged();
            return;
        }
        CloseModal();
    }

    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    /// <summary>Sube la foto del tercero (crear o editar): valida y guarda el archivo en
    /// wwwroot/uploads/terceros/{tenant} y pone su URL en _mImagenUrl (viaja en el SaveTerceroRequest al
    /// guardar). Mismo patron/guardas que Inventario (ImageUploadGuard: formato + firma + tamano; carpeta
    /// por tenant + nombre con Guid, asi un tenant no puede pisar el archivo de otro).</summary>
    private async Task OnFotoSelectedAsync(InputFileChangeEventArgs e)
    {
        var file = e.File;
        if (file is null) { return; }
        _fotoBusy = true;
        _busy = true;
        _modalError = null;
        try
        {
            var url = await StoreFotoAsync(file);
            if (url is not null) { _mImagenUrl = url; }
        }
        finally { _fotoBusy = false; _busy = false; }
    }

    private async Task<string?> StoreFotoAsync(IBrowserFile file)
    {
        if (TenantCtx.TenantId is not Guid tenantId) { _modalError = "No hay tenant activo."; return null; }
        var ext = Ecorex.SuperAdmin.Services.ImageUploadGuard.ResolveExtension(file.Name);
        if (ext is null) { _modalError = $"Formato no permitido. Solo {Ecorex.SuperAdmin.Services.ImageUploadGuard.FormatosTexto}."; return null; }
        const long max = 4L * 1024 * 1024;
        if (file.Size <= 0) { _modalError = "El archivo esta vacio."; return null; }
        if (file.Size > max) { _modalError = $"La imagen supera {max / (1024 * 1024)} MB."; return null; }
        try
        {
            using var ms = new System.IO.MemoryStream();
            await file.OpenReadStream(max).CopyToAsync(ms);
            var bytes = ms.ToArray();
            if (!Ecorex.SuperAdmin.Services.ImageUploadGuard.MatchesSignature(bytes, ext))
            {
                _modalError = $"El contenido no es una imagen {ext.TrimStart('.').ToUpperInvariant()} valida.";
                return null;
            }
            var dir = System.IO.Path.Combine(Env.WebRootPath, "uploads", "terceros", tenantId.ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            var stored = Ecorex.SuperAdmin.Services.ImageUploadGuard.BuildStoredFileName("tercero", ext);
            await System.IO.File.WriteAllBytesAsync(System.IO.Path.Combine(dir, stored), bytes);
            return $"/uploads/terceros/{tenantId:N}/{stored}";
        }
        catch { _modalError = "No se pudo subir la foto."; return null; }
    }

    // ---- Contactos de relacion (sub-modal reusado por la tabla del host y por la pestana Relaciones) ----
    /// <summary>Abre el sub-modal para agregar (c=null) o editar un contacto de relacion del tercero.</summary>
    public void OpenContacto(Guid parentId, TerceroContactoDto? c)
    {
        _coParentId = parentId;
        _coEditId = c?.Id;
        _coNombre = c?.Nombre ?? "";
        _coCargo = c?.Cargo ?? "";
        _coEmail = c?.Email ?? "";
        _coTelefono = c?.Telefono ?? "";
        _coError = null;
        _coOpen = true;
        StateHasChanged();
    }

    private void CloseContacto() => _coOpen = false;

    private async Task SaveContactoAsync()
    {
        _coError = null;
        var nombre = _coNombre.Trim();
        if (nombre.Length == 0)
        {
            _coError = "El nombre del contacto es obligatorio.";
            return;
        }
        var req = new SaveContactoRequest(nombre, Blank(_coCargo), Blank(_coEmail), Blank(_coTelefono));
        _busy = true;
        var res = _coEditId is null
            ? await TerceroSvc.AddContactoAsync(_coParentId, req)
            : await TerceroSvc.UpdateContactoAsync(_coEditId.Value, req);
        _busy = false;
        if (!res.IsOk)
        {
            _coError = res.Error;
            return;
        }
        // Actualizacion OPTIMISTA en memoria (no re-consulta): el DbContext scoped es compartido con
        // el host y una re-consulta disparada aqui podia perderse (carrera "second operation" con el
        // tunel lento), dejando el contacto guardado pero invisible en la lista. Con el DTO devuelto
        // reflejamos el alta/edicion al instante y de forma deterministica.
        if (_editingId == _coParentId && res.Value is { } dto)
        {
            var lista = _relContactos.Where(c => c.Id != dto.Id).ToList();
            lista.Add(dto);
            _relContactos = lista.OrderBy(c => c.Nombre, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        _coOpen = false;
        await OnChanged.InvokeAsync();
        StateHasChanged();
    }

    // Doble confirmacion: la papelera arma _confirmDelContactoId; este metodo es el 2o clic ("Si, eliminar").
    private Guid? _confirmDelContactoId;

    private async Task ConfirmDeleteContactoAsync(Guid parentId, Guid contactoId)
    {
        _busy = true;
        var res = await TerceroSvc.DeleteContactoAsync(contactoId);
        _busy = false;
        _confirmDelContactoId = null;
        if (!res.IsOk) { return; }
        // Optimista: quita la fila borrada de la lista abierta sin re-consultar (misma razon que el alta).
        if (_editingId == parentId)
        {
            _relContactos = _relContactos.Where(c => c.Id != contactoId).ToList();
        }
        await OnChanged.InvokeAsync();
        StateHasChanged();
    }

    /// <summary>Edita un contacto ligero heredado: lo PROMUEVE a Tercero Persona (unifica el modal) y abre
    /// el modal completo del nuevo tercero, con retorno a la empresa actual.</summary>
    private async Task EditContactoLigeroAsync(TerceroContactoDto k)
    {
        if (_editingId is not Guid parentId) { return; }
        await PromoteAndEditContactoAsync(k.Id, parentId);
    }

    /// <summary>Editar un contacto ligero heredado desde CUALQUIER lugar (p.ej. la lista del directorio):
    /// lo PROMUEVE a Tercero y abre el modal COMPLETO. Ya no existe el mini-modal.</summary>
    public async Task PromoteAndEditContactoAsync(Guid contactoId, Guid? returnToParent = null)
    {
        _busy = true;
        var res = await TerceroSvc.PromoteContactoToTerceroAsync(contactoId);
        _busy = false;
        if (!res.IsOk || res.Value == Guid.Empty)
        {
            _modalError = res.Error ?? "No se pudo abrir el contacto.";
            StateHasChanged();
            return;
        }
        await OnChanged.InvokeAsync();
        await OpenEditAsync(res.Value, returnToParent);
    }

    /// <summary>Quita un contacto que es Tercero Persona de la empresa (desvincula, EmpresaId=null): la
    /// persona sigue existiendo como individual, no se elimina. Optimista, como el alta/baja.</summary>
    private async Task UnassignContactoAsync(Guid personaId)
    {
        _busy = true;
        var res = await TerceroSvc.UnassignFromEmpresaAsync(personaId);
        _busy = false;
        if (!res.IsOk) { return; }
        _relContactos = _relContactos.Where(c => c.Id != personaId).ToList();
        await OnChanged.InvokeAsync();
        StateHasChanged();
    }

    // ---- Notas / gestiones "Contacto Cliente" ----
    private void ResetNoteForm()
    {
        _noteText = "";
        _procTitulo = "";
        _procValor = "";
        _procFecha = null;
        _noteError = null;
        _noteConcepto = null;
    }

    // ---- Gestion por concepto de actividad (000125) ----
    private void SelectConcepto(Ecorex.Application.Crm.ConceptoActividadDto c)
    {
        _noteConcepto = c;
        _noteText = "";
        _procTitulo = "";
        _procValor = "";
        _procFecha = null;
        _noteError = null;
    }

    // Datos del proceso (fuera del formulario): un concepto pide titulo+valor si maneja valor, y
    // titulo+fecha si es evento de agenda. Titulo solo se pide para esos casos (decision del usuario).
    private static bool ProcFieldsRequired(Ecorex.Application.Crm.ConceptoActividadDto c)
        => c.HandlesValues || c.Mode == Ecorex.Domain.Enums.ConceptoActividadMode.CalendarEvent;

    private bool ProcFieldsComplete(Ecorex.Application.Crm.ConceptoActividadDto c)
    {
        if (!ProcFieldsRequired(c)) { return true; }
        if (string.IsNullOrWhiteSpace(_procTitulo)) { return false; }
        if (c.HandlesValues && ParseValor(_procValor) is null) { return false; }
        if (c.Mode == Ecorex.Domain.Enums.ConceptoActividadMode.CalendarEvent && _procFecha is null) { return false; }
        return true;
    }

    /// <summary>Valor del input number (formato invariante): decimal >= 0 o null.</summary>
    private static decimal? ParseValor(string? s)
        => decimal.TryParse((s ?? "").Trim(), System.Globalization.NumberStyles.Any,
               System.Globalization.CultureInfo.InvariantCulture, out var d) && d >= 0
           ? d : (decimal?)null;

    /// <summary>El formulario del concepto se envio: guarda la gestion ligada (concepto + respuesta +
    /// datos del proceso) y alimenta el modulo de Oportunidades / la agenda.</summary>
    private async Task OnConceptoFormSubmittedAsync(FormResponseDto resp)
    {
        if (_editingId is not Guid id || _noteConcepto is not { } conc) { return; }
        if (!ProcFieldsComplete(conc)) { _noteError = "Completa los datos del proceso (titulo, valor y/o fecha)."; return; }
        var valor = conc.HandlesValues ? ParseValor(_procValor) : null;
        var texto = GestionTexto(conc, valor, resp);
        _busy = true;
        var res = await TerceroSvc.AddNotaAsync(id, new SaveNotaRequest(
            texto, conc.Name, conc.Code, null, null,
            ConceptoActividadId: conc.Id, FormResponseId: resp.Id, Valor: valor));
        if (res.IsOk)
        {
            await ApplyProcessWiringAsync(id, conc, valor);
            _notas = await TerceroSvc.ListNotasAsync(id);
            _noteConcepto = null;
        }
        else { _noteError = res.Error; }
        _busy = false;
    }

    /// <summary>Concepto sin formulario: nota rapida ligada al concepto (+ datos del proceso).</summary>
    private async Task AddConceptoNotaAsync()
    {
        if (_editingId is not Guid id || _noteConcepto is not { } conc) { return; }
        if (!ProcFieldsComplete(conc)) { _noteError = "Completa los datos del proceso (titulo, valor y/o fecha)."; return; }
        var texto = ProcFieldsRequired(conc) ? _procTitulo.Trim() : _noteText.Trim();
        if (texto.Length == 0) { _noteError = "Escribe la gestion antes de registrarla."; return; }
        var valor = conc.HandlesValues ? ParseValor(_procValor) : null;
        _busy = true;
        var res = await TerceroSvc.AddNotaAsync(id, new SaveNotaRequest(
            texto, conc.Name, conc.Code, null, null, ConceptoActividadId: conc.Id, Valor: valor));
        if (res.IsOk)
        {
            await ApplyProcessWiringAsync(id, conc, valor);
            _notas = await TerceroSvc.ListNotasAsync(id);
            _noteConcepto = null;
            _noteText = "";
        }
        else { _noteError = res.Error; }
        _busy = false;
    }

    /// <summary>
    /// Texto de la gestion en el timeline: el titulo del proceso (con su valor) cuando el concepto
    /// lo pide; si no, un RESUMEN de lo que se lleno en el formulario (antes salia solo el nombre
    /// del concepto, que no decia nada). Ultimo recurso: el nombre del concepto.
    /// </summary>
    private string GestionTexto(Ecorex.Application.Crm.ConceptoActividadDto conc, decimal? valor, FormResponseDto? resp = null)
    {
        var baseTexto = ProcFieldsRequired(conc) && !string.IsNullOrWhiteSpace(_procTitulo)
            ? _procTitulo.Trim()
            : (ResumenRespuesta(resp) ?? conc.Name);
        return valor is decimal v ? $"{baseTexto} - {MoneyFmt(v)}" : baseTexto;
    }

    /// <summary>
    /// Resumen legible de una respuesta de formulario para el timeline: prioriza los campos con
    /// significado (asunto/titulo/texto/descripcion/detalle) y si no hay, toma el primer valor con
    /// contenido. Recortado a una linea.
    /// </summary>
    private static string? ResumenRespuesta(FormResponseDto? resp)
    {
        if (resp is null) { return null; }
        foreach (var clave in new[] { "asunto", "titulo", "texto", "descripcion", "detalle" })
        {
            foreach (var (code, field) in resp.Data)
            {
                if (code.Contains(clave, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(field.Value))
                {
                    return RecortarResumen(field.Value!);
                }
            }
        }
        foreach (var (_, field) in resp.Data)
        {
            if (!string.IsNullOrWhiteSpace(field.Value)) { return RecortarResumen(field.Value!); }
        }
        return null;
    }

    private static string RecortarResumen(string s)
    {
        var t = s.Trim().ReplaceLineEndings(" ");
        return t.Length <= 120 ? t : t[..117] + "...";
    }

    /// <summary>
    /// Efecto de proceso de la gestion por concepto: si el concepto MANEJA VALOR crea una Oportunidad
    /// en el modulo de Oportunidades (con su titulo y valor, estado inicial) para gestionar sus
    /// estados aparte; si es EVENTO DE AGENDA crea una Cita con la fecha de la proxima actividad.
    /// Se ejecuta siempre (no solo en el Cargador): esos registros deben existir en sus modulos.
    /// </summary>
    private async Task ApplyProcessWiringAsync(Guid terceroId, Ecorex.Application.Crm.ConceptoActividadDto conc, decimal? valor)
    {
        var titulo = ProcFieldsRequired(conc) && !string.IsNullOrWhiteSpace(_procTitulo) ? _procTitulo.Trim() : conc.Name;
        if (conc.HandlesValues)
        {
            await GestorSvc.CreateOportunidadAsync(terceroId, new SaveOportunidadRequest(titulo, OportunidadEtapa.Nueva, Valor: valor ?? 0m));
            if (CrmWiring) { _opps = (await GestorSvc.ListOportunidadesByTerceroAsync(terceroId)).ToList(); }
        }
        if (conc.Mode == Ecorex.Domain.Enums.ConceptoActividadMode.CalendarEvent && _procFecha is DateTime fecha)
        {
            await GestorSvc.CreateCitaAsync(new SaveCitaRequest(
                terceroId, null, titulo, CitaTipo.Reunion, CombineInicio(fecha, "09:00"), 60));
        }
        // Tarea-proceso: el concepto puede producir una actividad del catalogo 000270. El alta
        // pasa por el MISMO ITaskItemService que usa el wizard (no hay una segunda via), y el
        // servicio deriva tablero/columna/flujo desde la subcategoria.
        if (conc.SubcategoriaId is Guid subId)
        {
            var res = await TasksSvc.CreateAsync(
                new Ecorex.Application.Tenancy.CreateTaskItemRequest(
                    Title: titulo, ActivityTypeId: null, SubcategoriaId: subId),
                await ActorIdAsync(), await ActorNameAsync());
            // Si la tarea falla, la gestion ya quedo registrada: se avisa en vez de callar.
            if (!res.IsOk) { _noteError = res.Error ?? "La gestion se registro, pero no se pudo crear la tarea de proceso."; }
        }
        await OnChanged.InvokeAsync();
    }

    private async Task<Guid> ActorIdAsync()
    {
        if (AuthState is null) { return Guid.Empty; }
        var st = await AuthState;
        return Guid.TryParse(st.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) ? id : Guid.Empty;
    }

    private async Task<string> ActorNameAsync()
    {
        if (AuthState is null) { return "Usuario"; }
        var st = await AuthState;
        return st.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "Usuario";
    }

    // ---- Oportunidades (presentacion; el CRM es IGestorContactosService) ----
    private static string MoneyFmt(decimal v)
        => "$ " + v.ToString("N0", CultureInfo.InvariantCulture);

    private static string EtapaLabel(OportunidadEtapa e) => e switch
    {
        OportunidadEtapa.Nueva => "Nueva",
        OportunidadEtapa.Calificada => "Calificada",
        OportunidadEtapa.Propuesta => "Propuesta",
        OportunidadEtapa.Negociacion => "Negociacion",
        OportunidadEtapa.Ganada => "Ganada",
        _ => "Perdida"
    };

    // Abierta = etapa configurable con Tipo Abierta cuando existe; si la oportunidad aun no tiene
    // etapa configurable (EstadoTipo null), cae al enum heredado. Misma regla que /gestor-contactos.
    private static bool IsOpen(OportunidadDto o) => o.EstadoTipo is OportunidadEstadoTipo tipo
        ? tipo == OportunidadEstadoTipo.Abierta
        : o.Etapa != OportunidadEtapa.Ganada && o.Etapa != OportunidadEtapa.Perdida;

    // La pildora muestra la etapa CONFIGURABLE del pipeline; solo cae al enum heredado
    // mientras la oportunidad no tenga estado asignado.
    private static (string Bg, string Fg, string Label) OppChip(OportunidadDto o)
    {
        if (!string.IsNullOrWhiteSpace(o.EstadoNombre))
        {
            var c = o.EstadoColor;
            if (!string.IsNullOrWhiteSpace(c) && c.StartsWith("--t-", StringComparison.Ordinal))
            {
                return ($"var({c}-bg)", $"var({c})", o.EstadoNombre!);
            }
            return string.IsNullOrWhiteSpace(c)
                ? ("var(--surface-3)", "var(--ink-2)", o.EstadoNombre!)
                : ("var(--surface-3)", c!, o.EstadoNombre!);
        }
        var i = EtapaInfo(o.Etapa);
        return ($"var({i.Bg})", $"var({i.Color})", EtapaLabel(o.Etapa));
    }

    private static TagInfo EtapaInfo(OportunidadEtapa e) => e switch
    {
        OportunidadEtapa.Nueva => new("Nueva", "--t-slate", "--t-slate-bg"),
        OportunidadEtapa.Calificada => new("Calificada", "--t-blue", "--t-blue-bg"),
        OportunidadEtapa.Propuesta => new("Propuesta", "--t-amber", "--t-amber-bg"),
        OportunidadEtapa.Negociacion => new("Negociacion", "--t-violet", "--t-violet-bg"),
        OportunidadEtapa.Ganada => new("Ganada", "--t-green", "--t-green-bg"),
        _ => new("Perdida", "--t-rose", "--t-rose-bg")
    };

    private static DateTimeOffset CombineInicio(DateTime fecha, string hora)
    {
        var t = TimeSpan.TryParse(hora, out var ts) ? ts : new TimeSpan(9, 0, 0);
        var dt = fecha.Date + t;
        return new DateTimeOffset(dt, DateTimeOffset.Now.Offset).ToUniversalTime();
    }

    private async Task DeleteNotaAsync(Guid notaId)
    {
        if (_editingId is not Guid id) { return; }
        _busy = true;
        await TerceroSvc.DeleteNotaAsync(notaId);
        _busy = false;
        _notas = await TerceroSvc.ListNotasAsync(id);
    }

    private static TagInfo AccionInfo(string accion) => accion switch
    {
        "Oportunidad" => new("Oportunidad", "--t-violet", "--t-violet-bg"),
        "Solicitud" => new("Solicitud", "--t-blue", "--t-blue-bg"),
        "Actividad" => new("Actividad", "--t-green", "--t-green-bg"),
        "PQR" => new("PQR", "--t-amber", "--t-amber-bg"),
        "Cotizacion" => new("Cotizacion", "--t-blue", "--t-blue-bg"),
        "Atencion" => new("Proxima atencion", "--t-rose", "--t-rose-bg"),
        "Nota" or "Anotacion" => new("Nota", "--t-slate", "--t-slate-bg"),
        // Concepto configurable sin color propio: usa su nombre como etiqueta.
        _ => new(accion, "--t-slate", "--t-slate-bg")
    };

    // Icono de la pildora del concepto (000125). Se elige por modo/valor para variar el aspecto sin
    // depender de un catalogo de iconos por concepto.
    private static string ConceptoIcon(Ecorex.Application.Crm.ConceptoActividadDto c)
    {
        if (c.HandlesValues)
        {
            // Valor / oportunidad: tendencia.
            return "<svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2.1' stroke-linecap='round' stroke-linejoin='round'><path d='M23 6l-9.5 9.5-5-5L1 18'/><path d='M17 6h6v6'/></svg>";
        }
        return c.Mode switch
        {
            Ecorex.Domain.Enums.ConceptoActividadMode.AttentionProcess =>
                "<svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2.1' stroke-linecap='round' stroke-linejoin='round'><path d='M9 11l3 3L22 4'/><path d='M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11'/></svg>",
            Ecorex.Domain.Enums.ConceptoActividadMode.CalendarEvent =>
                "<svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2.1' stroke-linecap='round' stroke-linejoin='round'><rect x='3' y='4' width='18' height='18' rx='2'/><path d='M16 2v4M8 2v4M3 10h18'/></svg>",
            _ =>
                "<svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2.1' stroke-linecap='round' stroke-linejoin='round'><path d='M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z'/></svg>"
        };
    }

    // ---- Fichas configurables por tenant (data-driven; cargadas al abrir el modal). ----
    private List<TerceroFichaDto> _fichas = new();

    // Fichas visibles segun los perfiles del tercero: Perfil vacio = siempre visible; si trae
    // perfiles (coma-separados) se muestra cuando el tercero tiene alguno de ellos. Reemplaza el
    // mapeo fragil por indice posicional del catalogo hardcodeado.
    private List<TerceroFichaDto> VisibleFichas()
    {
        var perfiles = new HashSet<string>(StringComparer.Ordinal);
        if (HasPerfil(TerceroPerfil.Cliente)) { perfiles.Add("cliente"); }
        if (HasPerfil(TerceroPerfil.Sospechoso)) { perfiles.Add("sospechoso"); }
        if (HasPerfil(TerceroPerfil.Proveedor)) { perfiles.Add("proveedor"); }
        if (HasPerfil(TerceroPerfil.Empleado)) { perfiles.Add("empleado"); }
        return _fichas.Where(f => !f.IsHidden && (string.IsNullOrWhiteSpace(f.Perfil)
            || f.Perfil.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(perfiles.Contains)))
            .ToList();
    }

    // Estilo del badge de una ficha a partir de su color hex (tinte de fondo + trazo del color).
    private static string FichaIcoStyle(string? color)
    {
        var c = string.IsNullOrWhiteSpace(color) ? "#6b7280" : color!;
        return $"background:color-mix(in srgb,{c} 16%,transparent);color:{c}";
    }

    // ---- Helpers de presentacion ----
    private sealed record TagInfo(string Label, string Color, string Bg);

    private static readonly string[] AvatarPalette =
        { "--t-violet", "--t-blue", "--t-amber", "--t-green", "--t-rose", "--t-slate" };

    private static string AvatarColor(string name)
    {
        var hash = 0;
        foreach (var ch in name) { hash = (hash * 31 + ch) & 0x7fffffff; }
        return $"var({AvatarPalette[hash % AvatarPalette.Length]})";
    }

    private static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) { return "?"; }
        return parts.Length == 1
            ? parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant()
            : (parts[0][..1] + parts[1][..1]).ToUpperInvariant();
    }
}
