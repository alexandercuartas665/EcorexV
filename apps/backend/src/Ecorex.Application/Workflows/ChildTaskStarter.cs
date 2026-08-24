using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Workflows;

/// <summary>
/// Implementacion del salto de flujo -> tarea hija (ADR-0076). Ver <see cref="IChildTaskStarter"/>.
/// Usa el MISMO IApplicationDbContext scoped que el motor (se resuelve del mismo scope), asi todo queda
/// en la transaccion del avance del padre: si algo falla, se revierte el cierre del padre y la hija juntos.
/// </summary>
public sealed class ChildTaskStarter : IChildTaskStarter
{
    private const string SequenceCode = "T05";
    private const string SequencePrefix = "T";
    private const int SequencePadding = 5;

    private readonly IApplicationDbContext _db;
    private readonly ISequenceService _sequences;
    private readonly IWorkflowEngine _engine;

    public ChildTaskStarter(IApplicationDbContext db, ISequenceService sequences, IWorkflowEngine engine)
    {
        _db = db;
        _sequences = sequences;
        _engine = engine;
    }

    public async Task<Guid?> StartChildFromJumpAsync(
        Guid parentTaskId, Guid jumpDefinitionId, CancellationToken cancellationToken = default)
    {
        var parent = await _db.TaskItems.FirstOrDefaultAsync(t => t.Id == parentTaskId, cancellationToken);
        if (parent is null) { return null; }

        // Idempotencia: si ya existe una hija de este padre corriendo este flujo, no crear otra (el FIN puede
        // alcanzarse mas de una vez si el padre se reabre/re-cierra).
        var alreadyExists = await (
            from t in _db.TaskItems.AsNoTracking()
            where t.ParentId == parentTaskId && t.WorkflowInstanceId != null
            join i in _db.WorkflowInstances.AsNoTracking() on t.WorkflowInstanceId equals i.Id
            where i.DefinitionId == jumpDefinitionId
            select t.Id).AnyAsync(cancellationToken);
        if (alreadyExists) { return null; }

        // El flujo destino debe estar publicado (StartInstance lo rechaza si no; se valida aqui para poder
        // saltar el salto sin ensuciar la transaccion del padre).
        var jumpDef = await _db.WorkflowDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == jumpDefinitionId, cancellationToken);
        if (jumpDef is null || !jumpDef.IsPublished || jumpDef.IsArchived) { return null; }

        // Consecutivo. La fila ya existe (el tenant ya creo tareas), asi que Ensure es no-op: seguro dentro
        // de la transaccion del padre.
        await _sequences.EnsureSequenceAsync(SequenceCode, cancellationToken);
        var number = await _sequences.NextAsync(SequenceCode, SequencePrefix, SequencePadding, cancellationToken);

        // Tablero/columna inicial: el del padre. Al arrancar el flujo hijo, su primer paso pendiente movera la
        // tarjeta al tablero/columna que ese nodo tenga configurado (MoveTaskToNodeTargetAsync).
        Guid? boardId = parent.BoardId;
        Guid? columnId = null;
        if (boardId is Guid board)
        {
            columnId = await _db.TaskBoardColumns.AsNoTracking()
                .Where(c => c.BoardId == board)
                .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
                .Select(c => (Guid?)c.Id).FirstOrDefaultAsync(cancellationToken);
        }

        var child = new TaskItem
        {
            TenantId = parent.TenantId,
            Number = number,
            Title = parent.Title,
            Description = parent.Description,
            EntidadId = parent.EntidadId,   // conexion de negocio heredada del padre
            ParentId = parent.Id,           // enlace al padre (la "conexion" padre<->hija)
            BoardId = boardId,
            ColumnId = columnId,
            Priority = parent.Priority,
            Status = TaskItemStatus.Pending
        };
        _db.TaskItems.Add(child);

        // Adjuntos heredados: se copian las filas apuntando a la hija reusando el MISMO Url (el fichero vive en
        // Url, no como blob), asi la hija referencia los mismos archivos del padre sin duplicarlos.
        var parentAttachments = await _db.TaskItemAttachments.AsNoTracking()
            .Where(a => a.TaskItemId == parentTaskId)
            .ToListAsync(cancellationToken);
        foreach (var a in parentAttachments)
        {
            _db.TaskItemAttachments.Add(new TaskItemAttachment
            {
                TenantId = parent.TenantId,
                TaskItemId = child.Id,
                FileName = a.FileName,
                Url = a.Url,
                MimeType = a.MimeType,
                SizeBytes = a.SizeBytes,
                UploadedBy = a.UploadedBy,
                UploadedByName = a.UploadedByName
            });
        }

        // Persistir la hija ANTES de arrancarle el flujo: StartInstanceAsync la busca por Id en la BD.
        await _db.SaveChangesAsync(cancellationToken);

        // Arranca el flujo destino en la hija. Se une a la transaccion activa (HasActiveTransaction) del padre.
        // El actor es el asignado del padre (tenant_user valido) o null; la herencia robusta (ADR-0075) resuelve
        // el encargado de la hija por su propio cargo / paso previo.
        var started = await _engine.StartInstanceAsync(
            jumpDefinitionId, child.Id, parent.AssigneeTenantUserId, "Salto de flujo", cancellationToken);
        if (started.Status is not (WorkflowEngineStatus.Ok or WorkflowEngineStatus.StuckDetected))
        {
            // No se pudo arrancar el flujo hijo: propagar para que la transaccion del padre se revierta entera
            // (no dejar una hija a medias). Es un caso raro: el flujo destino ya se valido publicado arriba.
            throw new InvalidOperationException($"No se pudo iniciar el flujo hijo del salto: {started.Error}");
        }

        return child.Id;
    }
}
