using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

public sealed class TaskBoardService : ITaskBoardService
{
    /// <summary>
    /// Columnas por defecto al crear un tablero nuevo (replica el prototipo). Internal:
    /// ActivityBoardService (ADR-0020) reusa exactamente el mismo set para los tableros
    /// de actividades.
    /// </summary>
    internal static readonly (string Name, string Color, bool IsDone)[] DefaultColumns =
    {
        ("Por hacer",   "#e2e8f0", false),
        ("En progreso", "#bfdbfe", false),
        ("En revision", "#fed7aa", false),
        ("Completado",  "#bbf7d0", true)
    };

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;

    public TaskBoardService(IApplicationDbContext db, ITenantContext tenantContext, IAuditWriter audit)
    {
        _db = db;
        _tenantContext = tenantContext;
        _audit = audit;
    }

    public async Task<IReadOnlyList<TaskBoardDto>> ListBoardsAsync(bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var boards = await _db.TaskBoards.AsNoTracking()
            .Where(b => includeArchived || !b.IsArchived)
            .OrderBy(b => b.SortOrder).ThenBy(b => b.Name)
            .ToListAsync(cancellationToken);
        if (boards.Count == 0) { return Array.Empty<TaskBoardDto>(); }

        var boardIds = boards.Select(b => b.Id).ToList();
        var doneColumnIds = await _db.TaskBoardColumns.AsNoTracking()
            .Where(c => boardIds.Contains(c.BoardId) && c.IsDone)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        var counts = await _db.TaskCards.AsNoTracking()
            .Where(c => boardIds.Contains(c.BoardId) && !c.IsArchived)
            .GroupBy(c => c.BoardId)
            .Select(g => new
            {
                BoardId = g.Key,
                Total = g.Count(),
                Done = g.Count(c => doneColumnIds.Contains(c.ColumnId))
            })
            .ToDictionaryAsync(x => x.BoardId, cancellationToken);

        return boards.Select(b =>
        {
            counts.TryGetValue(b.Id, out var c);
            return new TaskBoardDto(b.Id, b.Name, b.Description, b.Color, b.SortOrder, b.IsArchived,
                c?.Total ?? 0, c?.Done ?? 0);
        }).ToList();
    }

    public async Task<TaskBoardDto?> CreateBoardAsync(CreateTaskBoardRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var name = (request.Name ?? "Tablero").Trim();
        if (name.Length == 0) { return null; }

        var nextOrder = (await _db.TaskBoards.Select(b => (int?)b.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
        var board = new TaskBoard
        {
            TenantId = tenantId,
            Name = name,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Color = string.IsNullOrWhiteSpace(request.Color) ? null : request.Color.Trim(),
            SortOrder = nextOrder
        };
        _db.TaskBoards.Add(board);

        // Sembramos columnas por defecto para que el tablero sea usable desde el primer click.
        for (int i = 0; i < DefaultColumns.Length; i++)
        {
            var (cname, ccolor, isDone) = DefaultColumns[i];
            _db.TaskBoardColumns.Add(new TaskBoardColumn
            {
                TenantId = tenantId,
                BoardId = board.Id,
                Name = cname,
                Color = ccolor,
                SortOrder = i,
                IsDone = isDone
            });
        }

        _audit.Write(actorUserId, "task-board.create", nameof(TaskBoard), board.Id,
            previousValue: null, newValue: new { board.Name }, tenantId: tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return new TaskBoardDto(board.Id, board.Name, board.Description, board.Color, board.SortOrder, board.IsArchived, 0, 0);
    }

    public async Task<TaskBoardDto?> UpdateBoardAsync(Guid boardId, UpdateTaskBoardRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var board = await _db.TaskBoards.FirstOrDefaultAsync(b => b.Id == boardId, cancellationToken);
        if (board is null) { return null; }
        board.Name = (request.Name ?? board.Name).Trim();
        board.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        board.Color = string.IsNullOrWhiteSpace(request.Color) ? null : request.Color.Trim();
        board.IsArchived = request.IsArchived;
        await _db.SaveChangesAsync(cancellationToken);
        return new TaskBoardDto(board.Id, board.Name, board.Description, board.Color, board.SortOrder, board.IsArchived, 0, 0);
    }

    public async Task<bool> DeleteBoardAsync(Guid boardId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var board = await _db.TaskBoards.FirstOrDefaultAsync(b => b.Id == boardId, cancellationToken);
        if (board is null) { return false; }
        // Limpieza explicita de asignaciones de etiquetas del tablero: en SQL Server la FK
        // tag_assignment->tag es NO ACTION y el orden interno de la cascada del board no esta
        // garantizado; en PostgreSQL es redundante e inocua.
        await _db.TaskCardTagAssignments
            .Where(a => _db.TaskCardTags.Any(t => t.Id == a.TagId && t.BoardId == boardId))
            .ExecuteDeleteAsync(cancellationToken);
        _db.TaskBoards.Remove(board);
        _audit.Write(actorUserId, "task-board.delete", nameof(TaskBoard), board.Id,
            previousValue: new { board.Name }, newValue: null, tenantId: board.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<TaskBoardColumnDto>> ListColumnsAsync(Guid boardId, CancellationToken cancellationToken = default)
    {
        var columns = await _db.TaskBoardColumns.AsNoTracking()
            .Where(c => c.BoardId == boardId)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);
        if (columns.Count == 0) { return Array.Empty<TaskBoardColumnDto>(); }

        // Un tablero es kanban (TaskCard) O de actividades (TaskItem, ADR-0020); sumamos ambos para que
        // el conteo sea veraz en los dos modelos (antes solo contaba TaskCards -> 0 en actividades).
        var cardCounts = await _db.TaskCards.AsNoTracking()
            .Where(c => c.BoardId == boardId && !c.IsArchived)
            .GroupBy(c => c.ColumnId)
            .Select(g => new { ColumnId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var itemCounts = await _db.TaskItems.AsNoTracking()
            .Where(t => t.BoardId == boardId && t.ColumnId != null && !t.IsArchived)
            .GroupBy(t => t.ColumnId!.Value)
            .Select(g => new { ColumnId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var counts = new Dictionary<Guid, int>();
        foreach (var x in cardCounts) { counts[x.ColumnId] = x.Count; }
        foreach (var x in itemCounts) { counts[x.ColumnId] = (counts.TryGetValue(x.ColumnId, out var e) ? e : 0) + x.Count; }

        return columns.Select(c => new TaskBoardColumnDto(c.Id, c.BoardId, c.Name, c.Color, c.SortOrder, c.IsDone,
            counts.TryGetValue(c.Id, out var n) ? n : 0)).ToList();
    }

    public async Task<TaskBoardColumnDto?> CreateColumnAsync(CreateTaskColumnRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var board = await _db.TaskBoards.FirstOrDefaultAsync(b => b.Id == request.BoardId, cancellationToken);
        if (board is null) { return null; }
        var name = (request.Name ?? "Columna").Trim();
        if (name.Length == 0) { return null; }

        var nextOrder = (await _db.TaskBoardColumns.Where(c => c.BoardId == request.BoardId).Select(c => (int?)c.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
        var col = new TaskBoardColumn
        {
            TenantId = tenantId,
            BoardId = request.BoardId,
            Name = name,
            Color = string.IsNullOrWhiteSpace(request.Color) ? null : request.Color.Trim(),
            IsDone = request.IsDone,
            SortOrder = nextOrder
        };
        _db.TaskBoardColumns.Add(col);
        await _db.SaveChangesAsync(cancellationToken);
        return new TaskBoardColumnDto(col.Id, col.BoardId, col.Name, col.Color, col.SortOrder, col.IsDone, 0);
    }

    public async Task<TaskBoardColumnDto?> UpdateColumnAsync(Guid columnId, UpdateTaskColumnRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var col = await _db.TaskBoardColumns.FirstOrDefaultAsync(c => c.Id == columnId, cancellationToken);
        if (col is null) { return null; }
        col.Name = (request.Name ?? col.Name).Trim();
        col.Color = string.IsNullOrWhiteSpace(request.Color) ? null : request.Color.Trim();
        col.IsDone = request.IsDone;
        await _db.SaveChangesAsync(cancellationToken);
        var count = await _db.TaskCards.Where(c => c.ColumnId == col.Id && !c.IsArchived).CountAsync(cancellationToken)
            + await _db.TaskItems.Where(t => t.ColumnId == col.Id && !t.IsArchived).CountAsync(cancellationToken);
        return new TaskBoardColumnDto(col.Id, col.BoardId, col.Name, col.Color, col.SortOrder, col.IsDone, count);
    }

    public async Task<bool> DeleteColumnAsync(Guid columnId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var col = await _db.TaskBoardColumns.FirstOrDefaultAsync(c => c.Id == columnId, cancellationToken);
        if (col is null) { return false; }

        // No permitimos borrar un estado que todavia tiene tarjetas ACTIVAS (visibles): el usuario debe
        // moverlas primero. Cubre los DOS modelos de tablero: TaskCard (kanban) y TaskItem (actividades,
        // ADR-0020). Antes solo se miraba TaskCards, por eso un tablero de actividades pasaba el guard y
        // luego la FK TaskItem.ColumnId (Restrict) tumbaba el SaveChanges.
        var hasActiveCards = await _db.TaskCards.AnyAsync(c => c.ColumnId == columnId && !c.IsArchived, cancellationToken);
        var hasActiveTasks = await _db.TaskItems.AnyAsync(t => t.ColumnId == columnId && !t.IsArchived, cancellationToken);
        if (hasActiveCards || hasActiveTasks) { return false; }

        // Las tarjetas ARCHIVADAS conservan su FK al estado (ColumnId es Restrict en ambos modelos): si no
        // las reubicamos, el DELETE del estado viola la FK. Las mandamos a otro estado del mismo tablero
        // (preferimos otro estado de cierre; si no, el primero que quede) manteniendolas archivadas.
        var hasArchived = await _db.TaskCards.AnyAsync(c => c.ColumnId == columnId && c.IsArchived, cancellationToken)
            || await _db.TaskItems.AnyAsync(t => t.ColumnId == columnId && t.IsArchived, cancellationToken);
        var conceptoRefs = await _db.ActividadSubcategorias.AnyAsync(s => s.TaskBoardColumnId == columnId, cancellationToken);

        Guid? fallbackColumnId = null;
        if (hasArchived)
        {
            fallbackColumnId = await _db.TaskBoardColumns
                .Where(c => c.BoardId == col.BoardId && c.Id != columnId)
                .OrderByDescending(c => c.IsDone)   // preferimos otro estado de cierre
                .ThenBy(c => c.SortOrder)
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync(cancellationToken);
            // No hay a donde reubicar las tarjetas archivadas: no borramos para no perderlas.
            if (fallbackColumnId is null) { return false; }
        }

        // Reubicamos con entidades TRACKEADAS (no ExecuteUpdate) a proposito: al quitar la columna, EF
        // aplicaria ClientSetNull a las dependientes trackeadas que aun apunten a ella (ColumnId es
        // opcional) y les pondria ColumnId = null, pisando cualquier reasignacion por SQL directo. Al
        // cargarlas y repuntarlas aqui, ninguna queda apuntando a la columna: un unico SaveChanges las
        // persiste junto con el Remove de forma atomica. Es una accion de admin poco frecuente.
        if (fallbackColumnId is Guid dest)
        {
            var cards = await _db.TaskCards.Where(c => c.ColumnId == columnId && c.IsArchived).ToListAsync(cancellationToken);
            foreach (var c in cards) { c.ColumnId = dest; }
            var items = await _db.TaskItems.Where(t => t.ColumnId == columnId && t.IsArchived).ToListAsync(cancellationToken);
            foreach (var t in items) { t.ColumnId = dest; }
        }
        // Un concepto (000270) que apuntaba su estado "terminado" a este estado queda sin marcador: el
        // admin lo re-elige. Sin esto la FK NO ACTION (TaskBoardColumnId) bloquearia el borrado.
        if (conceptoRefs)
        {
            var conceptos = await _db.ActividadSubcategorias.Where(s => s.TaskBoardColumnId == columnId).ToListAsync(cancellationToken);
            foreach (var s in conceptos) { s.TaskBoardColumnId = null; }
        }

        _db.TaskBoardColumns.Remove(col);
        _audit.Write(actorUserId, "task-board-column.delete", nameof(TaskBoardColumn), col.Id,
            previousValue: new { col.Name, col.BoardId }, newValue: null, tenantId: col.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ReorderColumnsAsync(Guid boardId, IReadOnlyList<Guid> orderedColumnIds, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var columns = await _db.TaskBoardColumns.Where(c => c.BoardId == boardId).ToListAsync(cancellationToken);
        for (int i = 0; i < orderedColumnIds.Count; i++)
        {
            var col = columns.FirstOrDefault(c => c.Id == orderedColumnIds[i]);
            if (col is not null) { col.SortOrder = i; }
        }
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<TaskCardTagDto>> ListBoardTagsAsync(Guid boardId, CancellationToken cancellationToken = default)
    {
        return await _db.TaskCardTags.AsNoTracking()
            .Where(t => t.BoardId == boardId)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .Select(t => new TaskCardTagDto(t.Id, t.Name, t.Color))
            .ToListAsync(cancellationToken);
    }

    public async Task<TaskCardTagDto?> CreateBoardTagAsync(CreateBoardTagRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var name = (request.Name ?? "").Trim();
        if (name.Length == 0) { return null; }
        // El indice unico (boardId, name) impide duplicados; verificamos antes para devolver un null amigable.
        if (await _db.TaskCardTags.AnyAsync(t => t.BoardId == request.BoardId && t.Name == name, cancellationToken))
        {
            return null;
        }
        var nextOrder = (await _db.TaskCardTags.Where(t => t.BoardId == request.BoardId).Select(t => (int?)t.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
        var tag = new TaskCardTag
        {
            TenantId = tenantId,
            BoardId = request.BoardId,
            Name = name,
            Color = string.IsNullOrWhiteSpace(request.Color) ? null : request.Color.Trim(),
            SortOrder = nextOrder
        };
        _db.TaskCardTags.Add(tag);
        await _db.SaveChangesAsync(cancellationToken);
        return new TaskCardTagDto(tag.Id, tag.Name, tag.Color);
    }

    public async Task<TaskCardTagDto?> UpdateBoardTagAsync(Guid tagId, UpdateBoardTagRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var tag = await _db.TaskCardTags.FirstOrDefaultAsync(t => t.Id == tagId, cancellationToken);
        if (tag is null) { return null; }
        tag.Name = (request.Name ?? tag.Name).Trim();
        tag.Color = string.IsNullOrWhiteSpace(request.Color) ? null : request.Color.Trim();
        await _db.SaveChangesAsync(cancellationToken);
        return new TaskCardTagDto(tag.Id, tag.Name, tag.Color);
    }

    public async Task<bool> DeleteBoardTagAsync(Guid tagId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var tag = await _db.TaskCardTags.FirstOrDefaultAsync(t => t.Id == tagId, cancellationToken);
        if (tag is null) { return false; }
        // Limpieza explicita de asignaciones: en SQL Server la FK tag_assignment->tag es
        // NO ACTION (no admite la doble ruta de cascada); en PostgreSQL es redundante e inocua.
        await _db.TaskCardTagAssignments.Where(a => a.TagId == tagId).ExecuteDeleteAsync(cancellationToken);
        _db.TaskCardTags.Remove(tag);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
