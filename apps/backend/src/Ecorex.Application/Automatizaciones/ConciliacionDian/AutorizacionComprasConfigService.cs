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
}
