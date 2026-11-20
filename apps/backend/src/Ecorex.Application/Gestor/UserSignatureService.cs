using Ecorex.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Gestor;

/// <summary>
/// Firma de correo del usuario actual en el tenant activo (ADR-0175). Se edita en Mi cuenta y alimenta la
/// variable {firma} de las plantillas cuando el usuario es el asesor asignado del contacto.
/// </summary>
public interface IUserSignatureService
{
    /// <summary>Firma HTML del usuario actual; null si no tiene o no hay membresia en el tenant.</summary>
    Task<string?> GetMineAsync(CancellationToken cancellationToken = default);

    /// <summary>Guarda la firma del usuario actual (vacio = sin firma). false si no hay membresia.</summary>
    Task<bool> SaveMineAsync(string? firmaHtml, CancellationToken cancellationToken = default);
}

public sealed class UserSignatureService : IUserSignatureService
{
    /// <summary>Tope de la firma (columna de 4000): una firma es un bloque corto, no un correo.</summary>
    public const int MaxLength = 4000;

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public UserSignatureService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<string?> GetMineAsync(CancellationToken cancellationToken = default)
    {
        if (_tenant.UserId is not Guid userId) { return null; }
        return await _db.TenantUsers.AsNoTracking()
            .Where(u => u.PlatformUserId == userId)
            .Select(u => u.FirmaHtml)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> SaveMineAsync(string? firmaHtml, CancellationToken cancellationToken = default)
    {
        if (_tenant.UserId is not Guid userId) { return false; }
        var tu = await _db.TenantUsers.FirstOrDefaultAsync(u => u.PlatformUserId == userId, cancellationToken);
        if (tu is null) { return false; }
        var v = firmaHtml?.Trim();
        tu.FirmaHtml = string.IsNullOrWhiteSpace(v) ? null : (v.Length > MaxLength ? v[..MaxLength] : v);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
