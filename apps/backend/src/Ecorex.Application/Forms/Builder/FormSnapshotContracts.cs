using Ecorex.Application.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Forms.Builder;

/// <summary>Item del historial de versiones (snapshots) de un formulario.</summary>
public sealed record FormSnapshotItemDto(
    Guid Id, string Label, FormSnapshotTrigger Trigger, DateTimeOffset CreatedAt);

/// <summary>
/// VERSIONADO del asistente de formularios: toma snapshots (formulario + plantillas) para poder REVERTIR
/// si el agente o el usuario dañan el formulario. El snapshot se toma automatico antes de aplicar un lote
/// de mutaciones confirmado en el chat, o manualmente. Restaurar reconstruye el MISMO formulario in-place.
/// </summary>
public interface IFormSnapshotService
{
    /// <summary>Toma un snapshot del formulario (Export JSON) y de las plantillas del tenant. Devuelve el id
    /// del snapshot, o null si el formulario no pudo exportarse. No lanza: es un mecanismo de seguridad.</summary>
    Task<Guid?> SnapshotAsync(Guid formDefinitionId, string label, FormSnapshotTrigger trigger,
        Guid? conversationId, Guid? actorTenantUserId, CancellationToken cancellationToken = default);

    /// <summary>Historial de snapshots del formulario, mas reciente primero.</summary>
    Task<IReadOnlyList<FormSnapshotItemDto>> ListAsync(Guid formDefinitionId, CancellationToken cancellationToken = default);

    /// <summary>Restaura el formulario (y las plantillas capturadas) al estado de un snapshot, in-place.</summary>
    Task<FormResult<bool>> RestoreAsync(Guid snapshotId, Guid? actorTenantUserId, CancellationToken cancellationToken = default);
}
