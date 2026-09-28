using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Forms.Builder;

/// <summary>
/// Implementacion del versionado (snapshots) del asistente de formularios. Un snapshot guarda el JSON
/// portable del formulario (ExportAsync) y el de las plantillas de impresion del tenant. Restaurar borra
/// y reconstruye el MISMO formulario in-place (ReplaceStructureFromJsonAsync) y revierte el contenido de
/// las plantillas capturadas (upsert por id). Tenant-scoped por el filtro global.
/// </summary>
public sealed class FormSnapshotService : IFormSnapshotService
{
    private const int MaxHistory = 40;

    private readonly IApplicationDbContext _db;
    private readonly IFormDefinitionService _forms;

    public FormSnapshotService(IApplicationDbContext db, IFormDefinitionService forms)
    {
        _db = db;
        _forms = forms;
    }

    private sealed record TemplateSnap(Guid Id, string Name, string HtmlContent, bool IsDefault, bool SendAsImage);

    public async Task<Guid?> SnapshotAsync(Guid formDefinitionId, string label, FormSnapshotTrigger trigger,
        Guid? conversationId, Guid? actorTenantUserId, CancellationToken cancellationToken = default)
    {
        var export = await _forms.ExportAsync(formDefinitionId, cancellationToken);
        if (!export.IsOk || string.IsNullOrWhiteSpace(export.Value)) { return null; }

        var templates = await _db.QuoteTemplates.AsNoTracking()
            .Select(t => new TemplateSnap(t.Id, t.Name, t.HtmlContent, t.IsDefault, t.SendAsImage))
            .ToListAsync(cancellationToken);

        var snapshot = new FormBuilderSnapshot
        {
            FormDefinitionId = formDefinitionId,
            Label = string.IsNullOrWhiteSpace(label) ? "Version" : label.Trim()[..Math.Min(label.Trim().Length, 200)],
            Trigger = trigger,
            ConversationId = conversationId,
            FormJson = export.Value!,
            TemplatesJson = JsonSerializer.Serialize(templates),
            CreatedByTenantUserId = actorTenantUserId,
        };
        _db.FormBuilderSnapshots.Add(snapshot);
        await _db.SaveChangesAsync(cancellationToken);
        return snapshot.Id;
    }

    public async Task<IReadOnlyList<FormSnapshotItemDto>> ListAsync(Guid formDefinitionId, CancellationToken cancellationToken = default)
        => await _db.FormBuilderSnapshots.AsNoTracking()
            .Where(s => s.FormDefinitionId == formDefinitionId)
            .OrderByDescending(s => s.CreatedAt)
            .Take(MaxHistory)
            .Select(s => new FormSnapshotItemDto(s.Id, s.Label, s.Trigger, s.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task<FormResult<bool>> RestoreAsync(Guid snapshotId, Guid? actorTenantUserId, CancellationToken cancellationToken = default)
    {
        var snap = await _db.FormBuilderSnapshots.AsNoTracking().FirstOrDefaultAsync(s => s.Id == snapshotId, cancellationToken);
        if (snap is null) { return FormResult<bool>.NotFound("Version no encontrada."); }

        // 1) Formulario in-place.
        var restored = await _forms.ReplaceStructureFromJsonAsync(snap.FormDefinitionId, snap.FormJson, cancellationToken);
        if (!restored.IsOk) { return FormResult<bool>.Invalid(restored.Error ?? "No se pudo restaurar el formulario."); }

        // 2) Plantillas capturadas: revertir su contenido (upsert por id). No se borran plantillas creadas
        //    despues del snapshot (evita romper otros formularios); solo se revierte lo que existia.
        if (!string.IsNullOrWhiteSpace(snap.TemplatesJson))
        {
            List<TemplateSnap>? tpls = null;
            try { tpls = JsonSerializer.Deserialize<List<TemplateSnap>>(snap.TemplatesJson!); }
            catch (JsonException) { /* snapshot viejo/invalido: se omiten las plantillas */ }
            if (tpls is { Count: > 0 })
            {
                foreach (var t in tpls)
                {
                    var existing = await _db.QuoteTemplates.FirstOrDefaultAsync(x => x.Id == t.Id, cancellationToken);
                    if (existing is null)
                    {
                        _db.QuoteTemplates.Add(new QuoteTemplate
                        {
                            Id = t.Id,
                            Name = t.Name,
                            HtmlContent = t.HtmlContent,
                            IsDefault = t.IsDefault,
                            SendAsImage = t.SendAsImage,
                        });
                    }
                    else
                    {
                        existing.Name = t.Name;
                        existing.HtmlContent = t.HtmlContent;
                        existing.IsDefault = t.IsDefault;
                        existing.SendAsImage = t.SendAsImage;
                    }
                }
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        // 3) Cancela las PROPUESTAS PENDIENTES de las conversaciones de este formulario: tras revertir la
        //    estructura, una propuesta vieja apuntaria a contenedores/campos que ya no existen y fallaria.
        var convIds = _db.FormBuilderConversations
            .Where(c => c.FormDefinitionId == snap.FormDefinitionId)
            .Select(c => c.Id);
        await _db.FormBuilderMessages
            .Where(m => convIds.Contains(m.ConversationId)
                        && m.Role == FormBuilderMessageRole.Proposal
                        && m.ProposalState == FormBuilderProposalState.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ProposalState, FormBuilderProposalState.Rejected), cancellationToken);

        return FormResult<bool>.Ok(true);
    }
}
