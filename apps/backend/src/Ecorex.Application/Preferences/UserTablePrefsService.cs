using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Preferences;

/// <summary>
/// Preferencias de vista de una tabla para el usuario actual (orden/ocultas/anchos/sorts/grupos/fijas).
/// Persistidas en BD (viajan con el usuario entre equipos). El contenido es JSON opaco para el servicio:
/// quien lo produce/consume es la pagina. Multi-tenant por el filtro global; ademas filtra por UserId.
/// </summary>
public interface IUserTablePrefsService
{
    Task<string?> GetAsync(string tableKey, CancellationToken ct = default);
    Task SaveAsync(string tableKey, string preferencesJson, CancellationToken ct = default);
}

public sealed class UserTablePrefsService : IUserTablePrefsService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public UserTablePrefsService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<string?> GetAsync(string tableKey, CancellationToken ct = default)
    {
        if (_tenant.UserId is not Guid uid) { return null; }
        var p = await _db.UserTablePreferences.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == uid && x.TableKey == tableKey, ct);
        return p?.PreferencesJson;
    }

    public async Task SaveAsync(string tableKey, string preferencesJson, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid || _tenant.UserId is not Guid uid) { return; }
        var p = await _db.UserTablePreferences
            .FirstOrDefaultAsync(x => x.UserId == uid && x.TableKey == tableKey, ct);
        if (p is null)
        {
            _db.UserTablePreferences.Add(new UserTablePreference
            {
                TenantId = tid,
                UserId = uid,
                TableKey = tableKey,
                PreferencesJson = preferencesJson,
            });
        }
        else
        {
            p.PreferencesJson = preferencesJson;
        }
        await _db.SaveChangesAsync(ct);
    }
}
