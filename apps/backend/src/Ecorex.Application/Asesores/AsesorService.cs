using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Asesores;

/// <summary>
/// Catalogo de asesores/vendedores del tenant (modulo 000074). Tenant-scoped por el filtro global.
/// Alimenta el campo "Vendedor asignado" de los terceros del Directorio General. Un asesor puede
/// estar vinculado a un usuario del tenant o ser un vendedor "suelto" sin login.
/// </summary>
public sealed class AsesorService : IAsesorService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;

    public AsesorService(IApplicationDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    public async Task<IReadOnlyList<AsesorDto>> ListAsync(bool includeInactive = true, CancellationToken cancellationToken = default)
    {
        var q = _db.Asesores.AsNoTracking().AsQueryable();
        if (!includeInactive) { q = q.Where(a => a.IsActive); }
        var asesores = await q.OrderByDescending(a => a.IsActive).ThenBy(a => a.Nombre).ToListAsync(cancellationToken);
        if (asesores.Count == 0) { return Array.Empty<AsesorDto>(); }

        // Nombre del usuario vinculado (si alguno), en una consulta aparte para no complicar la proyeccion.
        var userIds = asesores.Where(a => a.TenantUserId is not null).Select(a => a.TenantUserId!.Value).Distinct().ToList();
        var userNames = new Dictionary<Guid, string>();
        if (userIds.Count > 0)
        {
            userNames = await _db.TenantUsers.AsNoTracking()
                .Where(tu => userIds.Contains(tu.Id))
                .Join(_db.PlatformUsers.AsNoTracking(), tu => tu.PlatformUserId, pu => pu.Id,
                    (tu, pu) => new { tu.Id, pu.DisplayName })
                .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken);
        }

        // Cuantos terceros referencian a cada asesor (para la UI y la guarda de borrado).
        var ids = asesores.Select(a => a.Id).ToList();
        var counts = await _db.Terceros.AsNoTracking()
            .Where(t => t.VendedorAsesorId != null && ids.Contains(t.VendedorAsesorId!.Value))
            .GroupBy(t => t.VendedorAsesorId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, cancellationToken);

        return asesores.Select(a => new AsesorDto(
            a.Id, a.Nombre, a.Documento, a.Email, a.Telefono,
            a.TenantUserId,
            a.TenantUserId is Guid uid && userNames.TryGetValue(uid, out var nombre) ? nombre : null,
            a.IsActive,
            counts.TryGetValue(a.Id, out var c) ? c : 0,
            a.AssignableByAgent)).ToList();
    }

    public async Task<IReadOnlyList<AsesorOptionDto>> ListOptionsAsync(CancellationToken cancellationToken = default)
        => await _db.Asesores.AsNoTracking()
            .Where(a => a.IsActive)
            .OrderBy(a => a.Nombre)
            .Select(a => new AsesorOptionDto(a.Id, a.Nombre))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AsesorUserOptionDto>> ListLinkableUsersAsync(CancellationToken cancellationToken = default)
    {
        // El OrderBy va sobre la COLUMNA (pu.DisplayName) ANTES de proyectar al DTO: ordenar por una
        // propiedad del record recien construido no se puede traducir a SQL (EF lanza InvalidOperation y
        // reventaba el modal de asesor). DisplayName puede ser null -> cae a Email/"(sin nombre)".
        var rows = await _db.TenantUsers.AsNoTracking()
            .Join(_db.PlatformUsers.AsNoTracking(), tu => tu.PlatformUserId, pu => pu.Id,
                (tu, pu) => new { tu.Id, pu.DisplayName, tu.Email })
            .OrderBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);
        return rows
            .Select(x => new AsesorUserOptionDto(x.Id, x.DisplayName ?? x.Email ?? "(sin nombre)", x.Email))
            .ToList();
    }

    public async Task<AsesorDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var list = await ListAsync(includeInactive: true, cancellationToken);
        return list.FirstOrDefault(a => a.Id == id);
    }

    public async Task<AsesorResult<AsesorDto>> CreateAsync(SaveAsesorRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return AsesorResult<AsesorDto>.Invalid("Sin tenant activo.");
        }
        var validation = await ValidateAsync(request, cancellationToken);
        if (validation is not null) { return AsesorResult<AsesorDto>.Invalid(validation); }

        var entity = new Asesor
        {
            TenantId = tenantId,
            Nombre = request.Nombre.Trim(),
            Documento = Blank(request.Documento),
            Email = Blank(request.Email),
            Telefono = Blank(request.Telefono),
            TenantUserId = request.TenantUserId,
            IsActive = request.IsActive,
            AssignableByAgent = request.AssignableByAgent,
        };
        _db.Asesores.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return AsesorResult<AsesorDto>.Ok((await GetAsync(entity.Id, cancellationToken))!);
    }

    public async Task<AsesorResult<AsesorDto>> UpdateAsync(Guid id, SaveAsesorRequest request, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Asesores.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (entity is null) { return AsesorResult<AsesorDto>.NotFound(); }
        var validation = await ValidateAsync(request, cancellationToken);
        if (validation is not null) { return AsesorResult<AsesorDto>.Invalid(validation); }

        entity.Nombre = request.Nombre.Trim();
        entity.Documento = Blank(request.Documento);
        entity.Email = Blank(request.Email);
        entity.Telefono = Blank(request.Telefono);
        entity.TenantUserId = request.TenantUserId;
        entity.IsActive = request.IsActive;
        entity.AssignableByAgent = request.AssignableByAgent;
        await _db.SaveChangesAsync(cancellationToken);
        return AsesorResult<AsesorDto>.Ok((await GetAsync(entity.Id, cancellationToken))!);
    }

    public async Task<AsesorResult<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Asesores.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (entity is null) { return AsesorResult<bool>.NotFound(); }

        // Guarda de negocio: un asesor con terceros vinculados NO se puede eliminar. Se lo decimos
        // con el conteo para que el usuario sepa cuantos reasignar antes de borrar.
        var refs = await _db.Terceros.CountAsync(t => t.VendedorAsesorId == id, cancellationToken);
        if (refs > 0)
        {
            return AsesorResult<bool>.Conflict(
                $"No se puede eliminar: el asesor tiene {refs} tercero(s) vinculado(s) en el Directorio General. Reasignalos primero (o desactiva el asesor).");
        }

        _db.Asesores.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return AsesorResult<bool>.Ok(true);
    }

    private async Task<string?> ValidateAsync(SaveAsesorRequest r, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(r.Nombre)) { return "El nombre del asesor es obligatorio."; }
        // El vinculo a usuario es opcional; si viene, debe ser un usuario del tenant (el filtro global
        // hace que un usuario de otro tenant "no exista" para esta consulta -> cierra el paso cross-tenant).
        if (r.TenantUserId is Guid uid && !await _db.TenantUsers.AnyAsync(tu => tu.Id == uid, cancellationToken))
        {
            return "El usuario seleccionado para vincular no existe en esta empresa.";
        }
        return null;
    }

    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
