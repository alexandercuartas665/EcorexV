using System.Text.Json;
using CubotTravels.Application.Common;
using CubotTravels.Domain.Entities;
using CubotTravels.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotTravels.Application.Tenancy;

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
        var query = _db.Leads.AsNoTracking();
        if (stageId is Guid s)
        {
            query = query.Where(l => l.StageId == s);
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
            .Select(a => new LeadActivityDto(a.Id, a.ActivityType, a.Description, a.CreatedAt))
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
            StageChangedAt = now
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

        lead.ContactName = request.ContactName.Trim();
        lead.ContactPhone = request.ContactPhone?.Trim();
        lead.Destination = request.Destination?.Trim();
        lead.EstimatedValue = request.EstimatedValue;
        lead.Currency = request.Currency?.Trim();

        var values = request.FieldValues ?? new Dictionary<string, string?>();
        // Se descartan claves vacias para no inflar el documento.
        var clean = values.Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                          .ToDictionary(kv => kv.Key, kv => kv.Value);
        lead.FieldValuesJson = clean.Count == 0 ? null : JsonSerializer.Serialize(clean);

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

        if (tenantUserId is Guid userId)
        {
            var belongs = await _db.TenantUsers.AnyAsync(tu => tu.Id == userId, cancellationToken);
            if (!belongs)
            {
                return null;
            }
        }

        lead.AssignedToTenantUserId = tenantUserId;
        AddActivity(lead.TenantId, lead.Id, "lead.assigned",
            tenantUserId is null ? "Lead sin asignar" : $"Asignado a {tenantUserId}");

        await _db.SaveChangesAsync(cancellationToken);
        return Map(lead);
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

    private static LeadDto Map(Lead l) =>
        new(l.Id, l.ContactName, l.ContactPhone, l.Destination, l.EstimatedValue, l.Currency, l.StageId, l.Status, l.AssignedToTenantUserId, l.StageChangedAt, DeserializeValues(l.FieldValuesJson));

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
