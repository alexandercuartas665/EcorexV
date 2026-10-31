using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Automatizaciones.ConciliacionDian;

/// <summary>Un evento del historial de asignaciones de un renglon, para mostrar en el popup de la grilla.</summary>
public sealed record ConciliacionAsignacionHistItemDto(
    DateTimeOffset Fecha, Guid? UserId, string Usuario, string? TaskNumber, ConciliacionAsignacionAccion Accion);

/// <summary>
/// Historial append-only de asignaciones de renglones de la Conciliacion DIAN. Registra cada vez que un
/// documento se direcciona a un usuario (<see cref="ConciliacionAsignacionAccion.Asignado"/>) o se devuelve al
/// conciliador (<see cref="ConciliacionAsignacionAccion.Rechazado"/>). La grilla lo usa para contar cuantas
/// veces se direcciono cada documento y mostrar a quienes y cuando.
/// </summary>
public interface IConciliacionAsignacionHistorialService
{
    /// <summary>Registra un evento del historial. No persiste por si mismo: deja la entidad agregada al
    /// contexto compartido (_db) para que la operacion llamante decida el SaveChanges/transaccion.</summary>
    Task RegistrarAsync(Guid renglonId, Guid? userId, string? taskNumber, ConciliacionAsignacionAccion accion, CancellationToken ct = default);

    /// <summary>Historial completo de un renglon, mas reciente primero, con el email del usuario resuelto.</summary>
    Task<IReadOnlyList<ConciliacionAsignacionHistItemDto>> GetHistorialAsync(Guid renglonId, CancellationToken ct = default);

    /// <summary>Conteo de veces DIRECCIONADO (solo <see cref="ConciliacionAsignacionAccion.Asignado"/>) por
    /// renglon, en lote para pintar la columna de la grilla sin N+1.</summary>
    Task<IReadOnlyDictionary<Guid, int>> GetConteosAsync(IEnumerable<Guid> renglonIds, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class ConciliacionAsignacionHistorialService : IConciliacionAsignacionHistorialService
{
    private readonly IApplicationDbContext _db;
    private readonly IApplicationDbContextFactory _dbFactory;
    private readonly ITenantContext _tenant;

    public ConciliacionAsignacionHistorialService(
        IApplicationDbContext db,
        IApplicationDbContextFactory dbFactory,
        ITenantContext tenant)
    {
        _db = db;
        _dbFactory = dbFactory;
        _tenant = tenant;
    }

    public Task RegistrarAsync(Guid renglonId, Guid? userId, string? taskNumber, ConciliacionAsignacionAccion accion, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tid) { return Task.CompletedTask; }
        _db.ConciliacionDianAsignaciones.Add(new ConciliacionDianAsignacion
        {
            TenantId = tid,
            RenglonId = renglonId,
            AssignedToTenantUserId = userId,
            TaskNumber = string.IsNullOrWhiteSpace(taskNumber) ? null : taskNumber.Trim(),
            Accion = accion,
        });
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<ConciliacionAsignacionHistItemDto>> GetHistorialAsync(Guid renglonId, CancellationToken ct = default)
    {
        await using var db = _dbFactory.Create();
        var eventos = await db.ConciliacionDianAsignaciones.AsNoTracking()
            .Where(a => a.RenglonId == renglonId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new { a.CreatedAt, a.AssignedToTenantUserId, a.TaskNumber, a.Accion })
            .ToListAsync(ct);
        if (eventos.Count == 0) { return Array.Empty<ConciliacionAsignacionHistItemDto>(); }

        var ids = eventos.Where(e => e.AssignedToTenantUserId is Guid).Select(e => e.AssignedToTenantUserId!.Value).Distinct().ToList();
        var emails = ids.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.TenantUsers.AsNoTracking()
                .Where(u => ids.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Email, ct);

        return eventos.Select(e => new ConciliacionAsignacionHistItemDto(
            e.CreatedAt,
            e.AssignedToTenantUserId,
            e.AssignedToTenantUserId is Guid uid && emails.TryGetValue(uid, out var em) ? em : "(sin usuario)",
            e.TaskNumber,
            e.Accion)).ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetConteosAsync(IEnumerable<Guid> renglonIds, CancellationToken ct = default)
    {
        var ids = (renglonIds ?? Array.Empty<Guid>()).Distinct().ToList();
        if (ids.Count == 0) { return new Dictionary<Guid, int>(); }
        await using var db = _dbFactory.Create();
        var rows = await db.ConciliacionDianAsignaciones.AsNoTracking()
            .Where(a => ids.Contains(a.RenglonId) && a.Accion == ConciliacionAsignacionAccion.Asignado)
            .GroupBy(a => a.RenglonId)
            .Select(g => new { RenglonId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.RenglonId, r => r.Count);
    }
}
