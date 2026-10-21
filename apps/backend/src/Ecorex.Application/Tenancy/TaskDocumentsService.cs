using Ecorex.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Adjuntos de una tarea resueltos por su NUMERO (no por Id). Lo usa el campo de formulario
/// "Documentos de la tarea" (<see cref="Ecorex.Domain.Enums.FormControlType.TaskDocuments"/>): el formulario
/// embebido conoce su Reference = numero de tarea y con eso muestra los documentos de esa tarea. Se mantiene
/// aparte de ITaskItemService a proposito (ese tiene fakes en tests; este es un lector minimo, sin impacto).
/// </summary>
public interface ITaskDocumentsService
{
    Task<IReadOnlyList<TaskItemAttachmentDto>> ListByTaskNumberAsync(string number, CancellationToken ct = default);
}

public sealed class TaskDocumentsService : ITaskDocumentsService
{
    private readonly IApplicationDbContext _db;

    public TaskDocumentsService(IApplicationDbContext db) { _db = db; }

    public async Task<IReadOnlyList<TaskItemAttachmentDto>> ListByTaskNumberAsync(string number, CancellationToken ct = default)
    {
        number = (number ?? string.Empty).Trim();
        if (number.Length == 0) { return Array.Empty<TaskItemAttachmentDto>(); }
        // El filtro global por tenant aplica tanto a TaskItems como a TaskItemAttachments.
        return await _db.TaskItemAttachments.AsNoTracking()
            .Where(a => a.TaskItem!.Number == number)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new TaskItemAttachmentDto(a.Id, a.FileName, a.Url, a.MimeType, a.SizeBytes, a.UploadedByName, a.CreatedAt))
            .ToListAsync(ct);
    }
}
