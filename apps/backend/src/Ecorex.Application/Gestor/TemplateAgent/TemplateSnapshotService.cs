using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Gestor.TemplateAgent;

/// <summary>
/// Versiones de plantillas (ADR-0175). Append-only: cada cambio del agente toma antes una foto del contenido
/// (via el toolset del tipo) y "Restaurar" vuelve a ese contenido in-place, guardando antes la version actual
/// para poder deshacer la restauracion.
/// </summary>
public sealed class TemplateSnapshotService : ITemplateSnapshotService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IReadOnlyDictionary<TemplateAgentKind, ITemplateAgentToolset> _toolsets;

    public TemplateSnapshotService(IApplicationDbContext db, ITenantContext tenant, IEnumerable<ITemplateAgentToolset> toolsets)
    {
        _db = db;
        _tenant = tenant;
        _toolsets = toolsets.ToDictionary(t => t.Kind);
    }

    public async Task SnapshotAsync(TemplateAgentKind kind, Guid templateId, string label, Guid? conversationId, Guid? actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId || !_toolsets.TryGetValue(kind, out var toolset)) { return; }
        var payload = await toolset.SnapshotPayloadAsync(templateId, cancellationToken);
        if (payload is null) { return; }
        _db.TemplateSnapshots.Add(new TemplateSnapshot
        {
            TenantId = tenantId,
            Kind = kind,
            TemplateId = templateId,
            Label = label.Length > 200 ? label[..200] : label,
            PayloadJson = payload,
            ConversationId = conversationId,
            CreatedByUserId = actorUserId,
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TemplateSnapshotDto>> ListAsync(TemplateAgentKind kind, Guid templateId, int take = 30, CancellationToken cancellationToken = default)
        => await _db.TemplateSnapshots.AsNoTracking()
            .Where(s => s.Kind == kind && s.TemplateId == templateId)
            .OrderByDescending(s => s.CreatedAt)
            .Take(take)
            .Select(s => new TemplateSnapshotDto(s.Id, s.Label, s.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task<bool> RestoreAsync(Guid snapshotId, Guid? actorUserId, CancellationToken cancellationToken = default)
    {
        var snap = await _db.TemplateSnapshots.AsNoTracking().FirstOrDefaultAsync(s => s.Id == snapshotId, cancellationToken);
        if (snap is null || !_toolsets.TryGetValue(snap.Kind, out var toolset)) { return false; }
        // Etiqueta plana (sin anidar "Antes de restaurar: Antes de restaurar: ..."): referencia la version por su fecha.
        await SnapshotAsync(snap.Kind, snap.TemplateId, $"Antes de restaurar la version del {snap.CreatedAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC",
            snap.ConversationId, actorUserId, cancellationToken);
        return await toolset.RestoreAsync(snap.TemplateId, snap.PayloadJson, cancellationToken);
    }
}
