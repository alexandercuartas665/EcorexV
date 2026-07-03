using System.Text.Json;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Common;

public sealed class AuditWriter : IAuditWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IApplicationDbContext _db;

    public AuditWriter(IApplicationDbContext db) => _db = db;

    public void Write(
        Guid actorUserId,
        string actionName,
        string entityName,
        Guid? entityId,
        object? previousValue,
        object? newValue,
        Guid? tenantId = null,
        string? reason = null,
        AuditActorType actorType = AuditActorType.Human)
    {
        _db.SuperAdminAuditLogs.Add(new SuperAdminAuditLog
        {
            ActorUserId = actorUserId,
            ActorType = actorType,
            ActionName = actionName,
            EntityName = entityName,
            EntityId = entityId,
            TenantId = tenantId,
            PreviousValue = previousValue is null ? null : JsonSerializer.Serialize(previousValue, JsonOptions),
            NewValue = newValue is null ? null : JsonSerializer.Serialize(newValue, JsonOptions),
            Reason = reason
        });
    }
}
