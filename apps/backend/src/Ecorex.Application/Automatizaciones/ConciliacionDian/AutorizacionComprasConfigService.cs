using Ecorex.Application.Common;
using Ecorex.Application.Crm;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Automatizaciones.ConciliacionDian;

/// <summary>Config del modulo "Autorizacion facturas compras DIAN": el concepto de actividad elegido
/// (trae su categoria/subcategoria y su formulario asociado). Lo que se muestra en el panel del modulo.</summary>
public sealed record AutorizacionModConfigDto(
    Guid? ConceptoId, string? ConceptoNombre, string? Categoria, string? Subcategoria,
    Guid? FormDefinitionId, string? FormTitle);

/// <summary>
/// Configuracion POR TENANT del modulo de autorizacion. Guarda en TenantConfiguration (clave/valor) el
/// concepto de actividad elegido; de ahi salen la categoria/subcategoria y el formulario a mostrar. NO
/// dispara la accion de autorizacion (crear la actividad): eso va en otra fase.
/// </summary>
public interface IAutorizacionComprasConfigService
{
    Task<AutorizacionModConfigDto> GetAsync(CancellationToken ct = default);
    Task SetConceptoAsync(Guid? conceptoId, CancellationToken ct = default);
    Task<IReadOnlyList<ConceptoActividadDto>> ListConceptosAsync(CancellationToken ct = default);
}

public sealed class AutorizacionComprasConfigService : IAutorizacionComprasConfigService
{
    private const string ConfigKey = "autorizacion-compras-dian.concepto";
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IConceptoActividadService _conceptos;

    public AutorizacionComprasConfigService(IApplicationDbContext db, ITenantContext tenant, IConceptoActividadService conceptos)
    {
        _db = db; _tenant = tenant; _conceptos = conceptos;
    }

    public Task<IReadOnlyList<ConceptoActividadDto>> ListConceptosAsync(CancellationToken ct = default)
        => _conceptos.ListAsync(false, ct);

    public async Task<AutorizacionModConfigDto> GetAsync(CancellationToken ct = default)
    {
        var val = await _db.TenantConfigurations.AsNoTracking()
            .Where(c => c.ConfigKey == ConfigKey)
            .Select(c => c.ConfigValue)
            .FirstOrDefaultAsync(ct);
        if (!Guid.TryParse(val, out var cid)) { return new(null, null, null, null, null, null); }
        var c = await _conceptos.GetAsync(cid, ct);
        if (c is null) { return new(null, null, null, null, null, null); }
        return new(c.Id, c.Name, c.SubcategoriaCategoria, c.SubcategoriaNombre, c.FormDefinitionId, c.FormTitle);
    }

    public async Task SetConceptoAsync(Guid? conceptoId, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return; }
        var row = await _db.TenantConfigurations.FirstOrDefaultAsync(c => c.ConfigKey == ConfigKey, ct);
        if (conceptoId is null)
        {
            if (row is not null) { _db.TenantConfigurations.Remove(row); await _db.SaveChangesAsync(ct); }
            return;
        }
        if (row is null)
        {
            _db.TenantConfigurations.Add(new TenantConfiguration { TenantId = tid, ConfigKey = ConfigKey, ConfigValue = conceptoId.ToString() });
        }
        else { row.ConfigValue = conceptoId.ToString(); }
        await _db.SaveChangesAsync(ct);
    }
}
