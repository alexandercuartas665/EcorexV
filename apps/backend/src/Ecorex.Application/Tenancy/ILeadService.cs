namespace Ecorex.Application.Tenancy;

/// <summary>Leads del embudo del tenant activo (modulo 2.2). Tenant-scoped.</summary>
public interface ILeadService
{
    Task<IReadOnlyList<LeadDto>> ListAsync(Guid? stageId = null, CancellationToken cancellationToken = default);
    Task<LeadDetailDto?> GetAsync(Guid leadId, CancellationToken cancellationToken = default);

    /// <summary>Devuelve null si no hay tenant activo o no existen etapas / la etapa indicada no es valida.</summary>
    Task<LeadDto?> CreateAsync(CreateLeadRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Actualiza datos de contacto y valores de los campos configurables. Null si no existe.</summary>
    Task<LeadDto?> UpdateAsync(Guid leadId, UpdateLeadRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Mueve el lead a otra etapa; cierra (Won/Lost) si la etapa es terminal. Null si lead o etapa invalidos.</summary>
    Task<LeadDto?> MoveAsync(Guid leadId, MoveLeadRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<LeadDto?> AssignAsync(Guid leadId, Guid? tenantUserId, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Envia el lead a historial (lo quita del embudo) con motivo y observacion; registra actividad.</summary>
    Task<bool> ArchiveAsync(Guid leadId, string reason, string? note, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Quita el lead del historial y lo regresa al tablero; registra actividad. Null si no existe.</summary>
    Task<LeadDto?> UnarchiveAsync(Guid leadId, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Lista los leads en historial (archivados), respetando la visibilidad por rol del asesor.</summary>
    Task<IReadOnlyList<ArchivedLeadDto>> ListArchivedAsync(CancellationToken cancellationToken = default);

    /// <summary>Vacia TODO el historial del tenant: borra de forma permanente e irreversible los leads
    /// archivados y sus datos asociados (notas, actividades, archivos, tareas). Solo administrador del
    /// tenant (Owner/Admin); devuelve null si no tiene permiso. Las URLs de archivos vuelven para que el
    /// llamador borre los binarios del disco.</summary>
    Task<PurgeArchivedResult?> PurgeArchivedHistoryAsync(CancellationToken cancellationToken = default);

    /// <summary>Notas de seguimiento del lead (mas recientes primero).</summary>
    Task<IReadOnlyList<LeadNoteDto>> ListNotesAsync(Guid leadId, CancellationToken cancellationToken = default);

    /// <summary>Agrega una nota de seguimiento con color. Null si el lead no existe o el contenido esta vacio.</summary>
    Task<LeadNoteDto?> AddNoteAsync(Guid leadId, string content, string color, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Elimina una nota de seguimiento. False si no existe.</summary>
    Task<bool> DeleteNoteAsync(Guid noteId, CancellationToken cancellationToken = default);

    /// <summary>Archivos/documentos adjuntos al lead (mas recientes primero).</summary>
    Task<IReadOnlyList<LeadFileDto>> ListFilesAsync(Guid leadId, CancellationToken cancellationToken = default);

    /// <summary>Registra un archivo ya guardado en disco. Null si el lead no existe.</summary>
    Task<LeadFileDto?> AddFileAsync(Guid leadId, string fileName, string url, string contentType, long sizeBytes, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Elimina el registro de un archivo y devuelve su Url para borrar el binario. Null si no existe.</summary>
    Task<string?> DeleteFileAsync(Guid fileId, CancellationToken cancellationToken = default);
}
