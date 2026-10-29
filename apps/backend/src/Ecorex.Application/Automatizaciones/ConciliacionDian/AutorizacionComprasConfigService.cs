using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Crm;
using Ecorex.Application.Forms;
using Ecorex.Application.Rules;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Automatizaciones.ConciliacionDian;

/// <summary>Config del modulo "Autorizacion facturas compras DIAN": el concepto de actividad (categoria/
/// subcategoria 000270) elegido + el formulario a asociar. Es lo que se muestra en el panel del modulo.</summary>
public sealed record AutorizacionModConfigDto(
    Guid? SubcategoriaId, string? Subcategoria, string? Categoria,
    Guid? FormDefinitionId, string? FormTitle);

/// <summary>Un mapeo del "Direccionar compra": el valor del campo <see cref="FormField"/> del formulario de
/// la tarea se escribe en la columna <see cref="Target"/> del renglon del conciliador. Target = "oc" |
/// "numsol" | "dyn:{fieldKey}".</summary>
public sealed record DireccionarMapDto(string FormField, string Target);

/// <summary>Config del boton "Direccionar compra": habilitado + los mapeos campo-del-formulario -> columna.</summary>
public sealed record DireccionarConfigDto(bool Enabled, IReadOnlyList<DireccionarMapDto> Mappings);

/// <summary>Opcion de campo del formulario (para el editor de mapeos del modal).</summary>
public sealed record FormFieldOptionDto(string Code, string Label);

/// <summary>
/// Una "salida al flujo de tareas": un concepto de actividad (000270, que trae su flujo) + el formulario a usar
/// + su propio "Direccionar compra". Permite que distintos tipos de autorizacion (con modelos de permiso
/// diferentes) tengan cada uno su flujo y su formulario. Cuando hay varias, la grilla pregunta cual usar al crear.
/// </summary>
public sealed record SalidaFlujoDto(
    Guid Id, string Nombre,
    Guid? SubcategoriaId, string? Subcategoria, string? Categoria,
    Guid? FormDefinitionId, string? FormTitle,
    bool DireccionarEnabled, IReadOnlyList<DireccionarMapDto> DireccionarMaps,
    // Secciones (contenedores) del formulario a OCULTAR cuando este se muestra en el modulo de autorizacion
    // (ej. el visor de documentos sobra ahi porque el PDF ya se ve aparte). Ids de FormContainer.
    IReadOnlyList<Guid>? HiddenContainerIds = null);

/// <summary>Una seccion (contenedor con nombre) de un formulario, para el selector de "secciones a ocultar".</summary>
public sealed record FormContainerOptionDto(Guid Id, string Name);

/// <summary>
/// Regla del asistente de IA POR PROVEEDOR: cuando el agente procesa una factura de ese proveedor, valida con la
/// instruccion (prompt) SI la regla se cumple (si/no); si se cumple, se aplica la asignacion PRE-ELEGIDA aqui:
/// el usuario (<see cref="AssignUserId"/>) y/o la salida/concepto de actividad (<see cref="SalidaId"/>). Asi el
/// agente solo decide si la condicion se cumple y el "selector" ya esta resuelto por configuracion. Es solo
/// configuracion (el cableado de la IA viene despues). Proveedor = NIT o nombre (texto libre).
/// </summary>
public sealed record AsistenteProveedorReglaDto(
    Guid Id, string Proveedor, string Prompt,
    Guid? AssignUserId = null, Guid? SalidaId = null);

/// <summary>
/// Configuracion POR TENANT del modulo de autorizacion. Guarda en TenantConfiguration (clave/valor, JSON) la
/// subcategoria de actividad (000270) y el formulario a asociar. El formulario se elige APARTE (no se hereda).
/// NO dispara la accion de autorizacion (crear la actividad): eso va en otra fase.
/// </summary>
public interface IAutorizacionComprasConfigService
{
    Task<AutorizacionModConfigDto> GetAsync(CancellationToken ct = default);
    Task SetAsync(Guid? subcategoriaId, Guid? formDefinitionId, CancellationToken ct = default);
    /// <summary>Conceptos de actividad = subcategorias 000270 (con su categoria).</summary>
    Task<IReadOnlyList<TareaProcesoOpcionDto>> ListSubcategoriasAsync(CancellationToken ct = default);
    /// <summary>Formularios disponibles para asociar.</summary>
    Task<IReadOnlyList<FormDefinitionListItemDto>> ListFormulariosAsync(CancellationToken ct = default);

    /// <summary>Usuario asignado (borrador) a una factura para la autorizacion. null si no hay.</summary>
    Task<Guid?> GetAsignadoAsync(Guid renglonId, CancellationToken ct = default);
    /// <summary>Guarda (o limpia con null) el usuario asignado a una factura. Persiste en un unico
    /// TenantConfiguration (mapa JSON renglon->usuario); es el "Guardar cambios" del panel.</summary>
    Task SetAsignadoAsync(Guid renglonId, Guid? userId, CancellationToken ct = default);

    /// <summary>Numero de actividad ya solicitada para una factura (anti-duplicado). null si no se ha solicitado.</summary>
    Task<string?> GetSolicitadaAsync(Guid renglonId, CancellationToken ct = default);
    Task SetSolicitadaAsync(Guid renglonId, string taskNumber, CancellationToken ct = default);
    /// <summary>Quita el vinculo renglon-&gt;tarea del mapa "solicitadas" (al devolver el documento al conciliador,
    /// para que un re-direccionamiento cree una tarea nueva). Idempotente.</summary>
    Task ClearSolicitadaAsync(Guid renglonId, CancellationToken ct = default);
    /// <summary>Mapa COMPLETO renglon->numero de actividad ya creada (una sola lectura; para pintar la grilla
    /// del conciliador en bloque). Lo comparten la autorizacion y la creacion de tareas por linea.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetSolicitadasMapAsync(CancellationToken ct = default);
    /// <summary>Resuelve el Id de la tarea por su numero (para abrir el detalle desde el badge del conciliador).
    /// null si no existe. El filtro global por tenant aplica.</summary>
    Task<Guid?> ResolveTaskIdByNumberAsync(string number, CancellationToken ct = default);

    // ---- "Direccionar compra": boton del formulario que devuelve datos del form al renglon del conciliador ----
    /// <summary>Config del boton "Direccionar compra" (habilitado + mapeos campo->columna).</summary>
    Task<DireccionarConfigDto> GetDireccionarAsync(CancellationToken ct = default);
    /// <summary>Guarda la config y, si queda habilitada con mapeos, AUTO-CABLEA en el formulario el boton
    /// "Direccionar compra" + la regla DIRECCIONAR_CONCILIADOR (idempotente).</summary>
    Task SetDireccionarAsync(bool enabled, IReadOnlyList<DireccionarMapDto> mappings, Guid? formDefinitionId, CancellationToken ct = default);
    /// <summary>Resuelve el renglon del conciliador a partir del numero de tarea (reverse del mapa solicitadas;
    /// no hay FK task->renglon). null si no se encuentra.</summary>
    Task<Guid?> ResolveRenglonByTaskNumberAsync(string taskNumber, CancellationToken ct = default);
    /// <summary>Resuelve el renglon desde la REFERENCIA del formulario, venga como quiera: primero como numero
    /// de tarea (form abierto dentro de la tarea creada), y si no, como id de renglon directo (form abierto en
    /// el PANEL del modulo de autorizacion, cuya referencia ES el id del renglon). null si ninguna aplica.</summary>
    Task<Guid?> ResolveRenglonByReferenceAsync(string reference, CancellationToken ct = default);
    /// <summary>Campos (code+label) de un formulario, para el editor de mapeos (excluye no-dato: Button/Heading/etc.).</summary>
    Task<IReadOnlyList<FormFieldOptionDto>> ListFormFieldsAsync(Guid formDefinitionId, CancellationToken ct = default);

    /// <summary>Codigos de campo que son texto/numero/fecha PLANOS (SourceKind=Options): los unicos seguros para
    /// prellenar con datos crudos de la factura. EXCLUYE campos repurposados a lookup del Contenedor, usuario del
    /// sistema, tercero, item, etc., para no pisarlos con el valor crudo (p. ej. meter la fecha en un campo que
    /// ahora es un selector de usuario).</summary>
    Task<IReadOnlySet<string>> GetPlainTextFieldCodesAsync(Guid formDefinitionId, CancellationToken ct = default);
    /// <summary>Secciones (contenedores de nivel superior, con nombre) de un formulario, para elegir cuales ocultar.</summary>
    Task<IReadOnlyList<FormContainerOptionDto>> ListFormContainersAsync(Guid formDefinitionId, CancellationToken ct = default);
    /// <summary>Secciones a ocultar en autorizacion para el formulario dado (salen de la salida cuyo form coincide).</summary>
    Task<IReadOnlyList<Guid>> GetHiddenContainerIdsForFormAsync(Guid formDefinitionId, CancellationToken ct = default);

    // ---- Salidas al flujo de tareas (N conceptos+formularios configurables; cada una con su Direccionar) ----
    /// <summary>Lista las salidas configuradas (con nombres resueltos). Si no hay salidas pero existe la config
    /// legacy (un solo concepto+formulario), la migra a UNA salida por defecto (idempotente).</summary>
    Task<IReadOnlyList<SalidaFlujoDto>> ListSalidasAsync(CancellationToken ct = default);
    /// <summary>Guarda la lista de salidas. Por cada salida: asocia el formulario a su concepto (para que el form
    /// "caiga" en la tarea) y, si tiene Direccionar habilitado con mapeos, auto-cablea el boton+regla en su formulario.</summary>
    Task SaveSalidasAsync(IReadOnlyList<SalidaFlujoDto> salidas, CancellationToken ct = default);
    /// <summary>Config de "Direccionar compra" para el formulario dado (busca la salida cuyo formulario coincide;
    /// si ninguna, cae a la config legacy). Lo usa el verbo DIRECCIONAR_CONCILIADOR para resolver los mapeos por form.</summary>
    Task<DireccionarConfigDto> GetDireccionarByFormAsync(Guid formDefinitionId, CancellationToken ct = default);

    // ---- Asistente de IA del conciliador (agente elegido para apoyar la gestion) ----
    /// <summary>Id del agente de IA elegido como asistente del conciliador (null si no hay). Por-tenant.</summary>
    Task<Guid?> GetAsistenteAgentIdAsync(CancellationToken ct = default);
    /// <summary>Guarda (o limpia con null) el agente de IA asistente del conciliador.</summary>
    Task SetAsistenteAgentIdAsync(Guid? agentId, CancellationToken ct = default);

    /// <summary>Reglas del asistente por proveedor (proveedor + prompt para elegir la ruta de flujo). Por-tenant.</summary>
    Task<IReadOnlyList<AsistenteProveedorReglaDto>> GetAsistenteReglasAsync(CancellationToken ct = default);
    /// <summary>Guarda las reglas del asistente por proveedor (descarta las vacias).</summary>
    Task SaveAsistenteReglasAsync(IReadOnlyList<AsistenteProveedorReglaDto> reglas, CancellationToken ct = default);
}

public sealed class AutorizacionComprasConfigService : IAutorizacionComprasConfigService
{
    private const string ConfigKey = "autorizacion-compras-dian.config";
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IConceptoActividadService _conceptos;
    private readonly IFormDefinitionService _forms;
    private readonly IRuleDocumentService _rules;
    private readonly IConciliacionAsignacionHistorialService _hist;

    public AutorizacionComprasConfigService(IApplicationDbContext db, ITenantContext tenant,
        IConceptoActividadService conceptos, IFormDefinitionService forms, IRuleDocumentService rules,
        IConciliacionAsignacionHistorialService hist)
    {
        _db = db; _tenant = tenant; _conceptos = conceptos; _forms = forms; _rules = rules; _hist = hist;
    }

    private sealed record Stored(Guid? Sub, Guid? Form);

    public Task<IReadOnlyList<TareaProcesoOpcionDto>> ListSubcategoriasAsync(CancellationToken ct = default)
        => _conceptos.ListTareasProcesoAsync(ct);

    public Task<IReadOnlyList<FormDefinitionListItemDto>> ListFormulariosAsync(CancellationToken ct = default)
        => _forms.ListAsync(false, ct);

    public async Task<AutorizacionModConfigDto> GetAsync(CancellationToken ct = default)
    {
        var raw = await _db.TenantConfigurations.AsNoTracking()
            .Where(c => c.ConfigKey == ConfigKey)
            .Select(c => c.ConfigValue)
            .FirstOrDefaultAsync(ct);
        var stored = Parse(raw);

        string? subNombre = null, categoria = null, formTitle = null;
        if (stored.Sub is Guid sid)
        {
            var sub = (await _conceptos.ListTareasProcesoAsync(ct)).FirstOrDefault(x => x.SubcategoriaId == sid);
            subNombre = sub?.Nombre; categoria = sub?.Categoria;
        }
        if (stored.Form is Guid fid)
        {
            var f = (await _forms.ListAsync(true, ct)).FirstOrDefault(x => x.Id == fid);
            formTitle = f?.Title;
        }
        return new AutorizacionModConfigDto(stored.Sub, subNombre, categoria, stored.Form, formTitle);
    }

    public async Task SetAsync(Guid? subcategoriaId, Guid? formDefinitionId, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return; }
        var row = await _db.TenantConfigurations.FirstOrDefaultAsync(c => c.ConfigKey == ConfigKey, ct);
        if (subcategoriaId is null && formDefinitionId is null)
        {
            if (row is not null) { _db.TenantConfigurations.Remove(row); await _db.SaveChangesAsync(ct); }
            return;
        }
        var json = JsonSerializer.Serialize(new Stored(subcategoriaId, formDefinitionId));
        if (row is null)
        {
            _db.TenantConfigurations.Add(new TenantConfiguration { TenantId = tid, ConfigKey = ConfigKey, ConfigValue = json });
        }
        else { row.ConfigValue = json; }

        // ASOCIAR el formulario a la subcategoria: la tarea muestra ActividadSubcategoria.FormDefinitionId,
        // asi que al elegir el form aqui queda enlazado en la actividad que nace del concepto.
        if (subcategoriaId is Guid sid)
        {
            var sub = await _db.ActividadSubcategorias.FirstOrDefaultAsync(s => s.Id == sid, ct);
            if (sub is not null) { sub.FormDefinitionId = formDefinitionId; }
        }
        await _db.SaveChangesAsync(ct);
    }

    private static Stored Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) { return new(null, null); }
        try { return JsonSerializer.Deserialize<Stored>(raw) ?? new(null, null); }
        catch { return new(null, null); }
    }

    // ---- Asignaciones por factura (mapa renglon->usuario en un unico TenantConfiguration) ----
    private const string AsignKey = "autorizacion-compras-dian.asignaciones";

    public async Task<Guid?> GetAsignadoAsync(Guid renglonId, CancellationToken ct = default)
    {
        var map = await LeerAsignacionesAsync(ct);
        return map.TryGetValue(renglonId.ToString(), out var v) && Guid.TryParse(v, out var g) ? g : null;
    }

    public async Task SetAsignadoAsync(Guid renglonId, Guid? userId, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return; }
        var row = await _db.TenantConfigurations.FirstOrDefaultAsync(c => c.ConfigKey == AsignKey, ct);
        var map = ParseMap(row?.ConfigValue);
        var key = renglonId.ToString();
        var prev = map.TryGetValue(key, out var pv) && Guid.TryParse(pv, out var pg) ? (Guid?)pg : null;
        if (userId is Guid uid) { map[key] = uid.ToString(); } else { map.Remove(key); }
        var json = JsonSerializer.Serialize(map);
        if (row is null) { _db.TenantConfigurations.Add(new TenantConfiguration { TenantId = tid, ConfigKey = AsignKey, ConfigValue = json }); }
        else { row.ConfigValue = json; }

        // Historial: contar un direccionamiento solo cuando se asigna a un usuario REAL y DISTINTO del anterior
        // (evita doble conteo si se re-escribe el mismo). Al direccionar, el documento deja de estar "rechazado".
        if (userId is Guid nuid && nuid != prev)
        {
            await _hist.RegistrarAsync(renglonId, nuid, null, ConciliacionAsignacionAccion.Asignado, ct);
            var r = await _db.ConciliacionDianRenglones.FirstOrDefaultAsync(x => x.Id == renglonId, ct);
            if (r is not null) { r.Rechazado = false; }
        }
        await _db.SaveChangesAsync(ct);
    }

    private async Task<Dictionary<string, string>> LeerAsignacionesAsync(CancellationToken ct)
    {
        var raw = await _db.TenantConfigurations.AsNoTracking()
            .Where(c => c.ConfigKey == AsignKey).Select(c => c.ConfigValue).FirstOrDefaultAsync(ct);
        return ParseMap(raw);
    }

    // ---- Facturas ya solicitadas (mapa renglon->numero de actividad) ----
    private const string SolicKey = "autorizacion-compras-dian.solicitadas";

    public async Task<string?> GetSolicitadaAsync(Guid renglonId, CancellationToken ct = default)
    {
        var raw = await _db.TenantConfigurations.AsNoTracking()
            .Where(c => c.ConfigKey == SolicKey).Select(c => c.ConfigValue).FirstOrDefaultAsync(ct);
        return ParseMap(raw).TryGetValue(renglonId.ToString(), out var v) ? v : null;
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetSolicitadasMapAsync(CancellationToken ct = default)
    {
        var raw = await _db.TenantConfigurations.AsNoTracking()
            .Where(c => c.ConfigKey == SolicKey).Select(c => c.ConfigValue).FirstOrDefaultAsync(ct);
        var result = new Dictionary<Guid, string>();
        foreach (var kv in ParseMap(raw)) { if (Guid.TryParse(kv.Key, out var g)) { result[g] = kv.Value; } }
        return result;
    }

    public async Task<Guid?> ResolveTaskIdByNumberAsync(string number, CancellationToken ct = default)
    {
        number = (number ?? string.Empty).Trim();
        if (number.Length == 0) { return null; }
        return await _db.TaskItems.AsNoTracking()
            .Where(t => t.Number == number)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task SetSolicitadaAsync(Guid renglonId, string taskNumber, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return; }
        var row = await _db.TenantConfigurations.FirstOrDefaultAsync(c => c.ConfigKey == SolicKey, ct);
        var map = ParseMap(row?.ConfigValue);
        map[renglonId.ToString()] = taskNumber;
        var json = JsonSerializer.Serialize(map);
        if (row is null) { _db.TenantConfigurations.Add(new TenantConfiguration { TenantId = tid, ConfigKey = SolicKey, ConfigValue = json }); }
        else { row.ConfigValue = json; }
        await _db.SaveChangesAsync(ct);
    }

    public async Task ClearSolicitadaAsync(Guid renglonId, CancellationToken ct = default)
    {
        var row = await _db.TenantConfigurations.FirstOrDefaultAsync(c => c.ConfigKey == SolicKey, ct);
        if (row is null) { return; }
        var map = ParseMap(row.ConfigValue);
        if (!map.Remove(renglonId.ToString())) { return; } // ya no estaba: idempotente
        row.ConfigValue = JsonSerializer.Serialize(map);
        await _db.SaveChangesAsync(ct);
    }

    private static Dictionary<string, string> ParseMap(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) { return new(); }
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(raw) ?? new(); }
        catch { return new(); }
    }

    // ================= "Direccionar compra" (boton del form -> columna del conciliador) =================
    private const string DireccionarKey = "conciliacion-dian.direccionar";
    private const string DireccionarVerb = "DIRECCIONAR_CONCILIADOR";
    private const string DireccionarButtonCode = "btn_direccionar";
    private const string DireccionarDocCode = "DIRECCIONAR-CONCILIADOR";
    private sealed record DireccionarStored(bool Enabled, List<DireccionarMapDto> Mappings);

    public async Task<DireccionarConfigDto> GetDireccionarAsync(CancellationToken ct = default)
    {
        var raw = await _db.TenantConfigurations.AsNoTracking()
            .Where(c => c.ConfigKey == DireccionarKey).Select(c => c.ConfigValue).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(raw)) { return new DireccionarConfigDto(false, Array.Empty<DireccionarMapDto>()); }
        try
        {
            var s = JsonSerializer.Deserialize<DireccionarStored>(raw);
            return new DireccionarConfigDto(s?.Enabled ?? false,
                (IReadOnlyList<DireccionarMapDto>?)s?.Mappings ?? Array.Empty<DireccionarMapDto>());
        }
        catch { return new DireccionarConfigDto(false, Array.Empty<DireccionarMapDto>()); }
    }

    public async Task SetDireccionarAsync(bool enabled, IReadOnlyList<DireccionarMapDto> mappings, Guid? formDefinitionId, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return; }
        var clean = (mappings ?? Array.Empty<DireccionarMapDto>())
            .Where(m => !string.IsNullOrWhiteSpace(m.FormField) && !string.IsNullOrWhiteSpace(m.Target))
            .Select(m => new DireccionarMapDto(m.FormField.Trim(), m.Target.Trim()))
            .ToList();
        var json = JsonSerializer.Serialize(new DireccionarStored(enabled, clean));
        var row = await _db.TenantConfigurations.FirstOrDefaultAsync(c => c.ConfigKey == DireccionarKey, ct);
        if (row is null) { _db.TenantConfigurations.Add(new TenantConfiguration { TenantId = tid, ConfigKey = DireccionarKey, ConfigValue = json }); }
        else { row.ConfigValue = json; }
        await _db.SaveChangesAsync(ct);

        // Al habilitar con mapeos, asegurar el boton + la regla en el formulario elegido (idempotente).
        if (enabled && clean.Count > 0 && formDefinitionId is Guid fid)
        {
            await EnsureDireccionarWiringAsync(fid, ct);
        }
    }

    public async Task<Guid?> ResolveRenglonByTaskNumberAsync(string taskNumber, CancellationToken ct = default)
    {
        taskNumber = (taskNumber ?? string.Empty).Trim();
        if (taskNumber.Length == 0) { return null; }
        var map = await GetSolicitadasMapAsync(ct);
        foreach (var kv in map)
        {
            if (string.Equals(kv.Value, taskNumber, StringComparison.OrdinalIgnoreCase)) { return kv.Key; }
        }
        return null;
    }

    public async Task<Guid?> ResolveRenglonByReferenceAsync(string reference, CancellationToken ct = default)
    {
        reference = (reference ?? string.Empty).Trim();
        if (reference.Length == 0) { return null; }
        // 1) Form abierto DENTRO de la tarea: la referencia es el numero de tarea.
        var byTask = await ResolveRenglonByTaskNumberAsync(reference, ct);
        if (byTask is Guid g) { return g; }
        // 2) Form abierto en el PANEL del modulo: la referencia ES el id del renglon. Se valida que exista
        //    (y sea del tenant, via el filtro global) para no escribir sobre un id cualquiera.
        if (Guid.TryParse(reference, out var rid)
            && await _db.ConciliacionDianRenglones.AsNoTracking().AnyAsync(r => r.Id == rid, ct))
        {
            return rid;
        }
        return null;
    }

    public async Task<IReadOnlyList<FormFieldOptionDto>> ListFormFieldsAsync(Guid formDefinitionId, CancellationToken ct = default)
    {
        var def = await _forms.GetAsync(formDefinitionId, ct);
        if (def is null) { return Array.Empty<FormFieldOptionDto>(); }
        var skip = new HashSet<FormControlType>
        {
            FormControlType.Button, FormControlType.Heading, FormControlType.Literal, FormControlType.Html, FormControlType.Chart,
        };
        return def.Questions.Where(q => !skip.Contains(q.ControlType))
            .OrderBy(q => q.SortOrder)
            .Select(q => new FormFieldOptionDto(q.FieldCode, q.Label))
            .ToList();
    }

    public async Task<IReadOnlySet<string>> GetPlainTextFieldCodesAsync(Guid formDefinitionId, CancellationToken ct = default)
    {
        var def = await _forms.GetAsync(formDefinitionId, ct);
        if (def is null) { return new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
        // Solo campos "crudos": texto/area/numero/fecha con origen de opciones (no lookups ni usuarios).
        var plain = new HashSet<FormControlType>
        {
            FormControlType.Text, FormControlType.TextArea, FormControlType.Number, FormControlType.Date,
        };
        return def.Questions
            .Where(q => q.SourceKind == FormSourceKind.Options && plain.Contains(q.ControlType))
            .Select(q => q.FieldCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<FormContainerOptionDto>> ListFormContainersAsync(Guid formDefinitionId, CancellationToken ct = default)
    {
        var def = await _forms.GetAsync(formDefinitionId, ct);
        if (def is null) { return Array.Empty<FormContainerOptionDto>(); }
        // Secciones de nivel superior (las que el usuario oculta). Nombre con fallback por tipo.
        return def.Containers
            .Where(c => c.ParentId is null)
            .OrderBy(c => c.SortOrder)
            .Select(c => new FormContainerOptionDto(c.Id, string.IsNullOrWhiteSpace(c.Name) ? $"Seccion ({c.ContainerType})" : c.Name))
            .ToList();
    }

    public async Task<IReadOnlyList<Guid>> GetHiddenContainerIdsForFormAsync(Guid formDefinitionId, CancellationToken ct = default)
    {
        var salidas = await ListSalidasAsync(ct);
        var match = salidas.FirstOrDefault(s => s.FormDefinitionId == formDefinitionId);
        return match?.HiddenContainerIds ?? Array.Empty<Guid>();
    }

    // Auto-cablea (idempotente) el boton "Direccionar compra" + la regla DIRECCIONAR_CONCILIADOR (Active) en el form.
    private async Task EnsureDireccionarWiringAsync(Guid formId, CancellationToken ct)
    {
        var def = await _forms.GetAsync(formId, ct);
        if (def is null) { return; }

        Guid questionId;
        var existing = def.Questions.FirstOrDefault(q => q.FieldCode == DireccionarButtonCode);
        if (existing is not null) { questionId = existing.Id; }
        else
        {
            var qReq = new SaveFormQuestionRequest(ContainerId: null, FieldCode: DireccionarButtonCode,
                Label: "Direccionar compra", ControlType: FormControlType.Button);
            var qRes = await _forms.AddQuestionAsync(formId, qReq, ct);
            if (!qRes.IsOk || qRes.Value is null) { return; }
            questionId = qRes.Value.Id;
        }

        // Ya enlazado al verbo -> listo.
        var links = await _rules.ListQuestionLinksAsync(questionId, ct);
        if (links.Any(l => string.Equals(l.VerbName, DireccionarVerb, StringComparison.OrdinalIgnoreCase))) { return; }

        // Documento de reglas (reutiliza por codigo si existe).
        var docs = await _rules.ListDocumentsAsync(false, ct);
        var doc = docs.FirstOrDefault(d => string.Equals(d.DocumentCode, DireccionarDocCode, StringComparison.OrdinalIgnoreCase));
        Guid docId;
        if (doc is not null) { docId = doc.Id; }
        else
        {
            var docRes = await _rules.CreateDocumentAsync(new SaveRuleDocumentRequest(
                DocumentCode: DireccionarDocCode, Name: "Direccionar compra (conciliador)",
                Category: "Conciliacion", Status: RuleStatus.Active), ct);
            if (!docRes.IsOk || docRes.Value is null) { return; }
            docId = docRes.Value.Id;
        }

        // Regla con el verbo. Status Active: si no, el boton no dispara (GetTriggerFieldCodesAsync solo carga Active).
        var ruleRes = await _rules.CreateRuleAsync(docId, new SaveRuleRequest(
            Name: "Direccionar compra", VerbName: DireccionarVerb, Status: RuleStatus.Active), ct);
        if (!ruleRes.IsOk || ruleRes.Value is null) { return; }

        await _rules.LinkToQuestionAsync(ruleRes.Value.Id, questionId, 0, ct);
    }

    // ================= Salidas al flujo de tareas (N conceptos+formularios, cada una con su Direccionar) =================
    private const string SalidasKey = "conciliacion-dian.salidas";
    private sealed record SalidaStored(Guid Id, string Nombre, Guid? Sub, Guid? Form, bool DireEnabled, List<DireccionarMapDto> DireMaps, List<Guid>? HiddenSecs = null);

    public async Task<IReadOnlyList<SalidaFlujoDto>> ListSalidasAsync(CancellationToken ct = default)
    {
        var stored = await LeerSalidasAsync(ct);
        if (stored.Count == 0)
        {
            // Migracion desde la config legacy (un solo concepto+formulario+direccionar) a UNA salida por defecto.
            var legacy = Parse(await LeerConfigRawAsync(ct));
            var dire = await GetDireccionarAsync(ct);
            if (legacy.Sub is not null || legacy.Form is not null || dire.Enabled)
            {
                stored.Add(new SalidaStored(Guid.CreateVersion7(), "Autorizacion de compra",
                    legacy.Sub, legacy.Form, dire.Enabled, dire.Mappings.ToList()));
                await GuardarSalidasAsync(stored, ct);   // persiste la migracion (idempotente)
            }
        }
        if (stored.Count == 0) { return Array.Empty<SalidaFlujoDto>(); }

        var subs = await _conceptos.ListTareasProcesoAsync(ct);
        var forms = await _forms.ListAsync(true, ct);
        return stored.Select(s =>
        {
            var sub = s.Sub is Guid sid ? subs.FirstOrDefault(x => x.SubcategoriaId == sid) : null;
            var form = s.Form is Guid fid ? forms.FirstOrDefault(x => x.Id == fid) : null;
            return new SalidaFlujoDto(s.Id, s.Nombre, s.Sub, sub?.Nombre, sub?.Categoria,
                s.Form, form?.Title, s.DireEnabled, s.DireMaps ?? new List<DireccionarMapDto>(),
                s.HiddenSecs ?? new List<Guid>());
        }).ToList();
    }

    public async Task SaveSalidasAsync(IReadOnlyList<SalidaFlujoDto> salidas, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid) { return; }
        var list = (salidas ?? Array.Empty<SalidaFlujoDto>()).Select((s, i) => new SalidaStored(
            s.Id == Guid.Empty ? Guid.CreateVersion7() : s.Id,
            string.IsNullOrWhiteSpace(s.Nombre) ? $"Salida {i + 1}" : s.Nombre.Trim(),
            s.SubcategoriaId, s.FormDefinitionId, s.DireccionarEnabled,
            (s.DireccionarMaps ?? Array.Empty<DireccionarMapDto>())
                .Where(m => !string.IsNullOrWhiteSpace(m.FormField) && !string.IsNullOrWhiteSpace(m.Target))
                .Select(m => new DireccionarMapDto(m.FormField.Trim(), m.Target.Trim())).ToList(),
            (s.HiddenContainerIds ?? Array.Empty<Guid>()).Distinct().ToList()))
            .ToList();

        await GuardarSalidasAsync(list, ct);

        // Por cada salida: asociar el formulario a su concepto (el form "cae" en la tarea via
        // ActividadSubcategoria.FormDefinitionId) y, si Direccionar esta activo con mapeos, cablear el boton+regla.
        foreach (var s in list)
        {
            if (s.Sub is Guid sid)
            {
                var sub = await _db.ActividadSubcategorias.FirstOrDefaultAsync(x => x.Id == sid, ct);
                if (sub is not null) { sub.FormDefinitionId = s.Form; }
            }
        }
        await _db.SaveChangesAsync(ct);

        foreach (var s in list)
        {
            if (s.DireEnabled && s.DireMaps.Count > 0 && s.Form is Guid fid)
            {
                await EnsureDireccionarWiringAsync(fid, ct);
            }
        }
    }

    public async Task<DireccionarConfigDto> GetDireccionarByFormAsync(Guid formDefinitionId, CancellationToken ct = default)
    {
        var stored = await LeerSalidasAsync(ct);
        var match = stored.FirstOrDefault(s => s.Form == formDefinitionId);
        if (match is not null)
        {
            return new DireccionarConfigDto(match.DireEnabled,
                (IReadOnlyList<DireccionarMapDto>)(match.DireMaps ?? new List<DireccionarMapDto>()));
        }
        // Sin salidas (o sin coincidencia): cae a la config legacy compartida.
        return await GetDireccionarAsync(ct);
    }

    private async Task<string?> LeerConfigRawAsync(CancellationToken ct)
        => await _db.TenantConfigurations.AsNoTracking()
            .Where(c => c.ConfigKey == ConfigKey).Select(c => c.ConfigValue).FirstOrDefaultAsync(ct);

    private async Task<List<SalidaStored>> LeerSalidasAsync(CancellationToken ct)
    {
        var raw = await _db.TenantConfigurations.AsNoTracking()
            .Where(c => c.ConfigKey == SalidasKey).Select(c => c.ConfigValue).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(raw)) { return new(); }
        try { return JsonSerializer.Deserialize<List<SalidaStored>>(raw) ?? new(); }
        catch { return new(); }
    }

    private async Task GuardarSalidasAsync(List<SalidaStored> list, CancellationToken ct)
    {
        if (_tenant.TenantId is not Guid tid) { return; }
        var json = JsonSerializer.Serialize(list);
        var row = await _db.TenantConfigurations.FirstOrDefaultAsync(c => c.ConfigKey == SalidasKey, ct);
        if (row is null) { _db.TenantConfigurations.Add(new TenantConfiguration { TenantId = tid, ConfigKey = SalidasKey, ConfigValue = json }); }
        else { row.ConfigValue = json; }
        await _db.SaveChangesAsync(ct);
    }

    // ================= Asistente de IA (agente elegido para apoyar el conciliador) =================
    private const string AsistenteKey = "conciliacion-dian.asistente-ia";

    public async Task<Guid?> GetAsistenteAgentIdAsync(CancellationToken ct = default)
    {
        var raw = await _db.TenantConfigurations.AsNoTracking()
            .Where(c => c.ConfigKey == AsistenteKey).Select(c => c.ConfigValue).FirstOrDefaultAsync(ct);
        return Guid.TryParse(raw, out var g) ? g : null;
    }

    public async Task SetAsistenteAgentIdAsync(Guid? agentId, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return; }
        var row = await _db.TenantConfigurations.FirstOrDefaultAsync(c => c.ConfigKey == AsistenteKey, ct);
        if (agentId is not Guid aid)
        {
            if (row is not null) { _db.TenantConfigurations.Remove(row); await _db.SaveChangesAsync(ct); }
            return;
        }
        if (row is null) { _db.TenantConfigurations.Add(new TenantConfiguration { TenantId = tid, ConfigKey = AsistenteKey, ConfigValue = aid.ToString() }); }
        else { row.ConfigValue = aid.ToString(); }
        await _db.SaveChangesAsync(ct);
    }

    // Reglas del asistente por proveedor (proveedor + prompt). Solo configuracion; la IA se cablea despues.
    private const string AsistenteReglasKey = "conciliacion-dian.asistente-reglas";

    public async Task<IReadOnlyList<AsistenteProveedorReglaDto>> GetAsistenteReglasAsync(CancellationToken ct = default)
    {
        var raw = await _db.TenantConfigurations.AsNoTracking()
            .Where(c => c.ConfigKey == AsistenteReglasKey).Select(c => c.ConfigValue).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(raw)) { return Array.Empty<AsistenteProveedorReglaDto>(); }
        try { return JsonSerializer.Deserialize<List<AsistenteProveedorReglaDto>>(raw) ?? new(); }
        catch { return Array.Empty<AsistenteProveedorReglaDto>(); }
    }

    public async Task SaveAsistenteReglasAsync(IReadOnlyList<AsistenteProveedorReglaDto> reglas, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return; }
        var clean = (reglas ?? Array.Empty<AsistenteProveedorReglaDto>())
            .Where(r => !string.IsNullOrWhiteSpace(r.Proveedor) || !string.IsNullOrWhiteSpace(r.Prompt))
            .Select(r => new AsistenteProveedorReglaDto(
                r.Id == Guid.Empty ? Guid.CreateVersion7() : r.Id,
                (r.Proveedor ?? string.Empty).Trim(), (r.Prompt ?? string.Empty).Trim(),
                r.AssignUserId, r.SalidaId))
            .ToList();
        var json = JsonSerializer.Serialize(clean);
        var row = await _db.TenantConfigurations.FirstOrDefaultAsync(c => c.ConfigKey == AsistenteReglasKey, ct);
        if (row is null) { _db.TenantConfigurations.Add(new TenantConfiguration { TenantId = tid, ConfigKey = AsistenteReglasKey, ConfigValue = json }); }
        else { row.ConfigValue = json; }
        await _db.SaveChangesAsync(ct);
    }
}
