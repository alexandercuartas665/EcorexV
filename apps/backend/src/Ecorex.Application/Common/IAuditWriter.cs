using Ecorex.Domain.Enums;

namespace Ecorex.Application.Common;

/// <summary>
/// Registra acciones sensibles del Super Admin/sistema en super_admin_audit_logs.
/// Solo agrega la entrada al contexto; el caso de uso decide cuando persistir (SaveChanges).
/// </summary>
public interface IAuditWriter
{
    void Write(
        Guid actorUserId,
        string actionName,
        string entityName,
        Guid? entityId,
        object? previousValue,
        object? newValue,
        Guid? tenantId = null,
        string? reason = null,
        AuditActorType actorType = AuditActorType.Human);
}
