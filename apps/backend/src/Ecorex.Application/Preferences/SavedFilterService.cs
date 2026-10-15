using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Preferences;

/// <summary>Datos de un filtro guardado (sin exponer la entidad).</summary>
public sealed record SavedFilterDto(Guid Id, string Module, string Name, string DefinitionJson, string? CreatedByName, DateTimeOffset UpdatedAt);

/// <summary>
/// Filtros guardados y COMPARTIDOS por el tenant para una grilla/modulo. A diferencia de las preferencias de
/// vista (por usuario), estos los ve todo el tenant. Multi-tenant por el filtro global sobre SavedFilter.
/// Clave logica: (TenantId, Module, Name) -> "Guardar" con un nombre existente lo reemplaza.
/// </summary>
public interface ISavedFilterService
{
    Task<IReadOnlyList<SavedFilterDto>> ListAsync(string module, CancellationToken ct = default);
    Task<SavedFilterDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<SavedFilterDto?> SaveAsync(string module, string name, string definitionJson, string? createdByName, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class SavedFilterService : ISavedFilterService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public SavedFilterService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<SavedFilterDto>> ListAsync(string module, CancellationToken ct = default)
    {
        var rows = await _db.SavedFilters.AsNoTracking()
            .Where(x => x.Module == module)
            .OrderBy(x => x.Name)
            .ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task<SavedFilterDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var r = await _db.SavedFilters.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return r is null ? null : Map(r);
    }

    public async Task<SavedFilterDto?> SaveAsync(string module, string name, string definitionJson, string? createdByName, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return null; }
        name = (name ?? "").Trim();
        if (name.Length == 0) { return null; }
        if (name.Length > 120) { name = name[..120]; }

        var existing = await _db.SavedFilters.FirstOrDefaultAsync(x => x.Module == module && x.Name == name, ct);
        if (existing is null)
        {
            existing = new SavedFilter
            {
                TenantId = tid,
                Module = module,
                Name = name,
                DefinitionJson = definitionJson,
                CreatedByUserId = _tenant.UserId,
                CreatedByName = createdByName,
            };
            _db.SavedFilters.Add(existing);
        }
        else
        {
            existing.DefinitionJson = definitionJson;
            existing.CreatedByUserId = _tenant.UserId;
            existing.CreatedByName = createdByName;
        }
        await _db.SaveChangesAsync(ct);
        return Map(existing);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var r = await _db.SavedFilters.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) { return false; }
        _db.SavedFilters.Remove(r);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static SavedFilterDto Map(SavedFilter f) =>
        new(f.Id, f.Module, f.Name, f.DefinitionJson, f.CreatedByName, f.UpdatedAt ?? f.CreatedAt);
}
