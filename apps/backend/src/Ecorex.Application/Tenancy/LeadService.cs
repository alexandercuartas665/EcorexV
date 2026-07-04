using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

public sealed class LeadService : ILeadService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public LeadService(IApplicationDbContext db, ITenantContext tenantContext, TimeProvider timeProvider)
    {
        _db = db;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<LeadDto>> ListAsync(Guid? stageId = null, CancellationToken cancellationToken = default)
    {
        // El tablero solo muestra leads activos; los enviados a historial se excluyen.
        var query = _db.Leads.AsNoTracking().Where(l => l.ArchivedAt == null);
        if (stageId is Guid s)
        {
            query = query.Where(l => l.StageId == s);
        }

        // Alcance de visibilidad: un asesor con OwnOnly solo ve los leads que tiene asignados.
        // Owner/Admin/Supervisor (o asesores con visibilidad All) ven todo el embudo.
        if (_tenantContext.UserId is Guid userId)
        {
            var me = await _db.TenantUsers.AsNoTracking()
                .FirstOrDefaultAsync(tu => tu.PlatformUserId == userId, cancellationToken);
            if (me is not null && me.TenantRole == TenantRole.Advisor && me.LeadVisibility == LeadVisibility.OwnOnly)
            {
                query = query.Where(l => l.AssignedToTenantUserId == me.Id);
            }
        }

        return await query
            .OrderByDescending(l => l.StageChangedAt)
            .Select(l => Map(l))
            .ToListAsync(cancellationToken);
    }

    public async Task<LeadDetailDto?> GetAsync(Guid leadId, CancellationToken cancellationToken = default)
    {
        var lead = await _db.Leads.AsNoTracking().FirstOrDefaultAsync(l => l.Id == leadId, cancellationToken);
        if (lead is null)
        {
            return null;
        }

        var activities = await _db.LeadActivities
            .AsNoTracking()
            .Where(a => a.LeadId == leadId)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new LeadActivityDto(a.Id, a.ActivityType, a.Description, a.CreatedAt,
                _db.PlatformUsers.Where(p => p.Id == a.CreatedBy).Select(p => p.DisplayName ?? p.Email).FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return new LeadDetailDto(Map(lead), activities);
    }

    public async Task<LeadDto?> CreateAsync(CreateLeadRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return null;
        }

        // Etapa destino: la indicada (validada) o la de menor SortOrder.
        PipelineStage? stage;
        if (request.StageId is Guid stageId)
        {
            stage = await _db.PipelineStages.FirstOrDefaultAsync(s => s.Id == stageId, cancellationToken);
        }
        else
        {
            stage = await _db.PipelineStages.OrderBy(s => s.SortOrder).FirstOrDefaultAsync(cancellationToken);
        }

        if (stage is null)
        {
            return null;
        }

        // El lead queda asignado al asesor que lo crea, para que un asesor con visibilidad
        // "solo sus clientes" lo siga viendo despues de crearlo.
        Guid? assignedTo = null;
        if (_tenantContext.UserId is Guid creatorUserId)
        {
            assignedTo = await _db.TenantUsers
                .Where(tu => tu.PlatformUserId == creatorUserId)
                .Select(tu => (Guid?)tu.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var now = _timeProvider.GetUtcNow();
        var lead = new Lead
        {
            TenantId = tenantId,
            ContactName = request.ContactName.Trim(),
            ContactPhone = request.ContactPhone?.Trim(),
            Destination = request.Destination?.Trim(),
            EstimatedValue = request.EstimatedValue,
            Currency = request.Currency?.Trim(),
            StageId = stage.Id,
            Status = LeadStatus.Open,
            StageChangedAt = now,
            AssignedToTenantUserId = assignedTo,
            BusinessUnitId = request.BusinessUnitId
        };
        _db.Leads.Add(lead);
        AddActivity(tenantId, lead.Id, "lead.created", $"Lead creado en etapa {stage.Name}");

        await _db.SaveChangesAsync(cancellationToken);
        return Map(lead);
    }

    public async Task<LeadDto?> UpdateAsync(Guid leadId, UpdateLeadRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, cancellationToken);
        if (lead is null)
        {
            return null;
        }

        var previousPhoneDigits = PhoneDigits(lead.ContactPhone);

        lead.ContactName = request.ContactName.Trim();
        lead.ContactPhone = request.ContactPhone?.Trim();
        lead.Destination = request.Destination?.Trim();
        lead.EstimatedValue = request.EstimatedValue;
        lead.Currency = request.Currency?.Trim();
        lead.BusinessUnitId = request.BusinessUnitId;

        var values = request.FieldValues ?? new Dictionary<string, string?>();
        // Se descartan claves vacias para no inflar el documento.
        var clean = values.Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                          .ToDictionary(kv => kv.Key, kv => kv.Value);
        lead.FieldValuesJson = clean.Count == 0 ? null : JsonSerializer.Serialize(clean);

        // Si cambia el telefono, la conversacion de WhatsApp debe seguir al nuevo numero (el chat
        // se envia al ContactPhone de la conversacion, no al del lead). De lo contrario los mensajes
        // seguirian yendo al numero anterior.
        await SyncConversationPhoneAsync(lead, previousPhoneDigits, cancellationToken);

        AddActivity(lead.TenantId, lead.Id, "lead.updated", "Datos del lead actualizados");

        await _db.SaveChangesAsync(cancellationToken);
        return Map(lead);
    }

    public async Task<LeadDto?> MoveAsync(Guid leadId, MoveLeadRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, cancellationToken);
        if (lead is null)
        {
            return null;
        }

        var stage = await _db.PipelineStages.FirstOrDefaultAsync(s => s.Id == request.StageId, cancellationToken);
        if (stage is null)
        {
            return null;
        }

        if (lead.StageId != stage.Id)
        {
            lead.StageId = stage.Id;
            lead.StageChangedAt = _timeProvider.GetUtcNow();
            lead.Status = stage.IsClosedWon ? LeadStatus.Won : stage.IsClosedLost ? LeadStatus.Lost : LeadStatus.Open;
            lead.LossReason = stage.IsClosedLost ? request.LossReason?.Trim() : null;

            AddActivity(lead.TenantId, lead.Id, "lead.stage.changed",
                $"Movido a {stage.Name}{(stage.IsClosedLost && !string.IsNullOrWhiteSpace(request.LossReason) ? $" (motivo: {request.LossReason})" : string.Empty)}");

            await _db.SaveChangesAsync(cancellationToken);
        }

        return Map(lead);
    }

    public async Task<LeadDto?> AssignAsync(Guid leadId, Guid? tenantUserId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, cancellationToken);
        if (lead is null)
        {
            return null;
        }

        var label = "Lead sin asignar";
        if (tenantUserId is Guid userId)
        {
            var email = await _db.TenantUsers.Where(tu => tu.Id == userId)
                .Select(tu => tu.Email).FirstOrDefaultAsync(cancellationToken);
            if (email is null)
            {
                return null;
            }
            label = $"Asignado a {email}";
        }

        lead.AssignedToTenantUserId = tenantUserId;
        AddActivity(lead.TenantId, lead.Id, "lead.assigned", label);

        await _db.SaveChangesAsync(cancellationToken);
        return Map(lead);
    }

    public async Task<bool> ArchiveAsync(Guid leadId, string reason, string? note, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, cancellationToken);
        if (lead is null) { return false; }

        var actorName = await ResolveActorNameAsync(actorUserId, lead.TenantId, cancellationToken);
        lead.ArchivedAt = _timeProvider.GetUtcNow();
        lead.ArchiveReason = string.IsNullOrWhiteSpace(reason) ? "Otro" : reason.Trim();
        lead.ArchiveNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        lead.ArchivedByName = actorName;

        var desc = $"Enviado a historial - {lead.ArchiveReason}" + (lead.ArchiveNote is null ? "" : $": {lead.ArchiveNote}");
        AddActivity(lead.TenantId, lead.Id, "lead.archived", desc);

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<ArchivedLeadDto>> ListArchivedAsync(CancellationToken cancellationToken = default)
    {
        var query = _db.Leads.AsNoTracking().Where(l => l.ArchivedAt != null);

        // Misma visibilidad que el embudo: un asesor OwnOnly solo ve su propio historial.
        if (_tenantContext.UserId is Guid userId)
        {
            var me = await _db.TenantUsers.AsNoTracking()
                .FirstOrDefaultAsync(tu => tu.PlatformUserId == userId, cancellationToken);
            if (me is not null && me.TenantRole == TenantRole.Advisor && me.LeadVisibility == LeadVisibility.OwnOnly)
            {
                query = query.Where(l => l.AssignedToTenantUserId == me.Id);
            }
        }

        var rows = await query
            .OrderByDescending(l => l.ArchivedAt)
            .Select(l => new
            {
                l.Id,
                l.ContactName,
                l.ContactPhone,
                l.Destination,
                l.EstimatedValue,
                l.Currency,
                l.ArchiveReason,
                l.ArchiveNote,
                l.ArchivedAt,
                l.ArchivedByName,
                l.AssignedToTenantUserId,
                l.StageId,
                l.Status,
                l.StageChangedAt,
                l.FieldValuesJson
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new ArchivedLeadDto(r.Id, r.ContactName, r.ContactPhone, r.Destination,
            r.EstimatedValue, r.Currency, r.ArchiveReason, r.ArchiveNote, r.ArchivedAt, r.ArchivedByName,
            r.AssignedToTenantUserId, r.StageId, r.Status, r.StageChangedAt, DeserializeValues(r.FieldValuesJson))).ToList();
    }

    public async Task<PurgeArchivedResult?> PurgeArchivedHistoryAsync(CancellationToken cancellationToken = default)
    {
        // Candado de rol: solo el administrador del tenant (Owner/Admin) puede vaciar el historial.
        // Es una accion destructiva e irreversible. Defensa en profundidad: la UI tambien la oculta.
        if (_tenantContext.UserId is not Guid userId) { return null; }
        var me = await _db.TenantUsers.AsNoTracking()
            .FirstOrDefaultAsync(tu => tu.PlatformUserId == userId, cancellationToken);
        if (me is null || me.TenantRole is not (TenantRole.Owner or TenantRole.Admin)) { return null; }

        // Ids de los leads en historial del tenant activo (el filtro global ya limita por tenant).
        var ids = await _db.Leads
            .Where(l => l.ArchivedAt != null)
            .Select(l => l.Id)
            .ToListAsync(cancellationToken);
        if (ids.Count == 0) { return new PurgeArchivedResult(0, Array.Empty<string>()); }

        // URLs de los binarios para que el llamador los borre del disco.
        var fileUrls = await _db.LeadFiles
            .Where(f => ids.Contains(f.LeadId))
            .Select(f => f.Url)
            .ToListAsync(cancellationToken);

        // Hijos primero (FK), luego soltamos el enlace de la conversacion y por ultimo los leads.
        await _db.LeadNotes.Where(n => ids.Contains(n.LeadId)).ExecuteDeleteAsync(cancellationToken);
        await _db.LeadActivities.Where(a => ids.Contains(a.LeadId)).ExecuteDeleteAsync(cancellationToken);
        await _db.LeadFiles.Where(f => ids.Contains(f.LeadId)).ExecuteDeleteAsync(cancellationToken);
        await _db.FollowUpTasks.Where(t => ids.Contains(t.LeadId)).ExecuteDeleteAsync(cancellationToken);
        await _db.Conversations.Where(c => c.LeadId != null && ids.Contains(c.LeadId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LeadId, (Guid?)null), cancellationToken);
        var deleted = await _db.Leads.Where(l => ids.Contains(l.Id)).ExecuteDeleteAsync(cancellationToken);

        return new PurgeArchivedResult(deleted, fileUrls);
    }

    public async Task<LeadDto?> UnarchiveAsync(Guid leadId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, cancellationToken);
        if (lead is null) { return null; }

        lead.ArchivedAt = null;
        lead.ArchiveReason = null;
        lead.ArchiveNote = null;
        lead.ArchivedByName = null;

        AddActivity(lead.TenantId, lead.Id, "lead.restored", "Regresado al tablero desde historial");

        await _db.SaveChangesAsync(cancellationToken);
        return Map(lead);
    }

    public async Task<IReadOnlyList<LeadNoteDto>> ListNotesAsync(Guid leadId, CancellationToken cancellationToken = default)
    {
        return await _db.LeadNotes.AsNoTracking()
            .Where(n => n.LeadId == leadId)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new LeadNoteDto(n.Id, n.Content, n.Color, n.CreatedAt,
                _db.PlatformUsers.Where(p => p.Id == n.CreatedBy).Select(p => p.DisplayName ?? p.Email).FirstOrDefault()))
            .ToListAsync(cancellationToken);
    }

    public async Task<LeadNoteDto?> AddNoteAsync(Guid leadId, string content, string color, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content)) { return null; }
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, cancellationToken);
        if (lead is null) { return null; }

        var note = new LeadNote
        {
            TenantId = lead.TenantId,
            LeadId = leadId,
            Content = content.Trim(),
            Color = string.IsNullOrWhiteSpace(color) ? "yellow" : color.Trim()
        };
        _db.LeadNotes.Add(note);
        await _db.SaveChangesAsync(cancellationToken);

        var actorName = await ResolveActorNameAsync(actorUserId, lead.TenantId, cancellationToken);
        return new LeadNoteDto(note.Id, note.Content, note.Color, note.CreatedAt, actorName);
    }

    public async Task<bool> DeleteNoteAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        var note = await _db.LeadNotes.FirstOrDefaultAsync(n => n.Id == noteId, cancellationToken);
        if (note is null) { return false; }
        _db.LeadNotes.Remove(note);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<LeadFileDto>> ListFilesAsync(Guid leadId, CancellationToken cancellationToken = default)
    {
        return await _db.LeadFiles.AsNoTracking()
            .Where(f => f.LeadId == leadId)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new LeadFileDto(f.Id, f.FileName, f.Url, f.ContentType, f.SizeBytes, f.CreatedAt,
                _db.PlatformUsers.Where(p => p.Id == f.CreatedBy).Select(p => p.DisplayName ?? p.Email).FirstOrDefault()))
            .ToListAsync(cancellationToken);
    }

    public async Task<LeadFileDto?> AddFileAsync(Guid leadId, string fileName, string url, string contentType, long sizeBytes, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(url)) { return null; }
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, cancellationToken);
        if (lead is null) { return null; }

        var file = new LeadFile
        {
            TenantId = lead.TenantId,
            LeadId = leadId,
            FileName = fileName.Trim(),
            Url = url.Trim(),
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim(),
            SizeBytes = sizeBytes
        };
        _db.LeadFiles.Add(file);
        AddActivity(lead.TenantId, lead.Id, "lead.file.added", $"Archivo adjuntado: {file.FileName}");
        await _db.SaveChangesAsync(cancellationToken);

        var actorName = await ResolveActorNameAsync(actorUserId, lead.TenantId, cancellationToken);
        return new LeadFileDto(file.Id, file.FileName, file.Url, file.ContentType, file.SizeBytes, file.CreatedAt, actorName);
    }

    public async Task<string?> DeleteFileAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var file = await _db.LeadFiles.FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken);
        if (file is null) { return null; }
        var url = file.Url;
        _db.LeadFiles.Remove(file);
        await _db.SaveChangesAsync(cancellationToken);
        return url;
    }

    private async Task<string?> ResolveActorNameAsync(Guid actorUserId, Guid tenantId, CancellationToken ct)
    {
        if (actorUserId == Guid.Empty) { return null; }
        var name = await _db.PlatformUsers.AsNoTracking()
            .Where(p => p.Id == actorUserId)
            .Select(p => p.DisplayName ?? p.Email)
            .FirstOrDefaultAsync(ct);
        return name;
    }

    private void AddActivity(Guid tenantId, Guid leadId, string type, string description)
    {
        _db.LeadActivities.Add(new LeadActivity
        {
            TenantId = tenantId,
            LeadId = leadId,
            ActivityType = type,
            Description = description
        });
    }

    // Mantiene la conversacion de WhatsApp del lead apuntando a su telefono actual. Si el numero no
    // cambio no hace nada. Si cambio: cuando ya existe una conversacion con el numero nuevo, religa el
    // lead a esa (evita romper la unicidad por telefono); si no, mueve la conversacion del lead al numero nuevo.
    private async Task SyncConversationPhoneAsync(Lead lead, string previousPhoneDigits, CancellationToken cancellationToken)
    {
        var newDigits = PhoneDigits(lead.ContactPhone);
        if (string.IsNullOrEmpty(newDigits) || newDigits == previousPhoneDigits)
        {
            return;
        }

        var linked = await _db.Conversations.FirstOrDefaultAsync(c => c.LeadId == lead.Id, cancellationToken);
        var withNewNumber = await _db.Conversations.FirstOrDefaultAsync(c => c.ContactPhone == newDigits, cancellationToken);

        if (withNewNumber is not null)
        {
            // Ya hay una conversacion con el numero nuevo: religar el lead a ella y soltar la anterior.
            if (linked is not null && linked.Id != withNewNumber.Id)
            {
                linked.LeadId = null;
            }
            withNewNumber.LeadId = lead.Id;
            if (string.IsNullOrWhiteSpace(withNewNumber.ContactName))
            {
                withNewNumber.ContactName = lead.ContactName;
            }
        }
        else if (linked is not null)
        {
            // Mover la conversacion existente (con su historial) al numero nuevo.
            linked.ContactPhone = newDigits;
        }
    }

    private static string PhoneDigits(string? s) => string.IsNullOrEmpty(s) ? string.Empty : new string(s.Where(char.IsDigit).ToArray());

    private static LeadDto Map(Lead l) =>
        new(l.Id, l.ContactName, l.ContactPhone, l.Destination, l.EstimatedValue, l.Currency, l.StageId, l.Status, l.AssignedToTenantUserId, l.StageChangedAt, DeserializeValues(l.FieldValuesJson), l.BusinessUnitId, l.CreatedAt);

    private static IReadOnlyDictionary<string, string?> DeserializeValues(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string?>();
        }
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(json) ?? new Dictionary<string, string?>();
        }
        catch
        {
            return new Dictionary<string, string?>();
        }
    }
}
