using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Crm;
using Ecorex.Application.Forms;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Automatizaciones.ConciliacionDian;

/// <summary>Config del modulo "Autorizacion facturas compras DIAN": el concepto de actividad (categoria/
/// subcategoria 000270) elegido + el formulario a asociar. Es lo que se muestra en el panel del modulo.</summary>
public sealed record AutorizacionModConfigDto(
    Guid? SubcategoriaId, string? Subcategoria, string? Categoria,
    Guid? FormDefinitionId, string? FormTitle);

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
}

public sealed class AutorizacionComprasConfigService : IAutorizacionComprasConfigService
{
    private const string ConfigKey = "autorizacion-compras-dian.config";
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IConceptoActividadService _conceptos;
    private readonly IFormDefinitionService _forms;

    public AutorizacionComprasConfigService(IApplicationDbContext db, ITenantContext tenant,
        IConceptoActividadService conceptos, IFormDefinitionService forms)
    {
        _db = db; _tenant = tenant; _conceptos = conceptos; _forms = forms;
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
        if (userId is Guid uid) { map[key] = uid.ToString(); } else { map.Remove(key); }
        var json = JsonSerializer.Serialize(map);
        if (row is null) { _db.TenantConfigurations.Add(new TenantConfiguration { TenantId = tid, ConfigKey = AsignKey, ConfigValue = json }); }
        else { row.ConfigValue = json; }
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

    private static Dictionary<string, string> ParseMap(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) { return new(); }
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(raw) ?? new(); }
        catch { return new(); }
    }
}
