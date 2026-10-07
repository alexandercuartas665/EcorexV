using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Auto-archivado de tareas cerradas (ADR-0123). Archiva las tareas que: estan CERRADAS (Status
/// Done/Closed), en una columna de CIERRE (IsDone), y llevan en esa columna mas dias de los que el
/// TABLERO permite (<see cref="TaskBoard.AutoArchiveDoneDays"/>, 0 = nunca). El reloj es
/// <see cref="TaskItem.ColumnEnteredAt"/>, sellado tanto por el arrastre manual como por el flujo.
///
/// Anclar en "cerrada" (no solo "en columna de cierre") protege a una actividad VIVA que un flujo
/// haya parqueado en una columna de cierre intermedia: esa no esta Done, asi que no se archiva.
///
/// TENANT-SCOPED: cada barrido corre bajo el tenant ambiente (el worker lo fija), de modo que el
/// filtro global de EF aisla al tenant. El archivado es reversible (papelera del tablero).
/// </summary>
public interface ITaskAutoArchiveService
{
    /// <summary>Ids de TODOS los tenants (barrido cross-tenant, solo ids). El worker itera y, por cada uno,
    /// fija el tenant ambiente y llama <see cref="ArchiveDueForTenantAsync"/>.</summary>
    Task<IReadOnlyList<Guid>> ListTenantIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>Archiva las tareas vencidas del tenant ambiente. Devuelve cuantas archivo.</summary>
    Task<int> ArchiveDueForTenantAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
}

public sealed class TaskAutoArchiveService : ITaskAutoArchiveService
{
    private readonly IApplicationDbContext _db;

    public TaskAutoArchiveService(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<Guid>> ListTenantIdsAsync(CancellationToken cancellationToken = default)
        => await _db.Tenants.IgnoreQueryFilters()
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

    public async Task<int> ArchiveDueForTenantAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        // Candidatas: tareas cerradas, en columna de cierre, cuyo tablero tiene plazo (>0) y con reloj sellado.
        // El corte por dias (que depende del tablero) se evalua en memoria para portar PG/SQL Server.
        var candidates = await (
            from t in _db.TaskItems
            where !t.IsArchived
                  && (t.Status == TaskItemStatus.Done || t.Status == TaskItemStatus.Closed)
                  && t.ColumnId != null && t.BoardId != null && t.ColumnEnteredAt != null
            join c in _db.TaskBoardColumns on t.ColumnId!.Value equals c.Id
            join b in _db.TaskBoards on t.BoardId!.Value equals b.Id
            where c.IsDone && b.AutoArchiveDoneDays > 0
            select new { Task = t, b.AutoArchiveDoneDays })
            .ToListAsync(cancellationToken);

        var due = candidates
            .Where(x => x.Task.ColumnEnteredAt!.Value <= nowUtc.AddDays(-x.AutoArchiveDoneDays))
            .ToList();
        if (due.Count == 0) { return 0; }

        foreach (var x in due)
        {
            x.Task.IsArchived = true;
            _db.TaskItemActivities.Add(new TaskItemActivity
            {
                TenantId = x.Task.TenantId,
                TaskItemId = x.Task.Id,
                Type = TaskActivityType.Action,
                ActorUserId = null,
                ActorName = "Sistema",
                Text = $"archivada automaticamente ({x.AutoArchiveDoneDays} dias cerrada en el tablero)"
            });
        }
        await _db.SaveChangesAsync(cancellationToken);
        return due.Count;
    }
}
