using System.Globalization;
using System.Text;
using CubotTravels.Application.Common;
using CubotTravels.Domain.Entities;
using CubotTravels.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotTravels.Application.Tenancy;

public sealed class PipelineService : IPipelineService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;

    public PipelineService(IApplicationDbContext db, ITenantContext tenantContext, IAuditWriter audit)
    {
        _db = db;
        _tenantContext = tenantContext;
        _audit = audit;
    }

    // Etapas y campos por defecto, tomados del prototipo de diseno (embudo Travel Fans).
    private static readonly (string Stage, (string Key, string Label, PipelineFieldType Type, int Col, string? Options)[] Fields)[] Defaults =
    [
        ("Lead",
        [
            ("asesor", "Asesor (ASE)", PipelineFieldType.Text, 1, null),
            ("fechaTentativa", "Fecha tentativa", PipelineFieldType.Text, 1, null),
            ("adultos", "Adultos (+12)", PipelineFieldType.Number, 1, null),
            ("ninos", "Ninos (-12)", PipelineFieldType.Text, 1, null),
            ("comentarios", "Comentarios", PipelineFieldType.TextArea, 2, null)
        ]),
        ("Cotizacion",
        [
            ("aerolinea", "Aerolinea", PipelineFieldType.Text, 1, null),
            ("vueloIda", "Vuelo ida", PipelineFieldType.Text, 1, null),
            ("vueloRegreso", "Vuelo regreso", PipelineFieldType.Text, 1, null),
            ("valorTotalVuelos", "Valor total vuelos", PipelineFieldType.Currency, 1, null),
            ("valorVuelosPax", "Valor x pax", PipelineFieldType.Currency, 1, null),
            ("vuelosNinos", "Vuelo ninos", PipelineFieldType.Currency, 1, null),
            ("hotel1", "Hotel 1", PipelineFieldType.Text, 1, null),
            ("valorNetoHotel1", "Valor neto hotel", PipelineFieldType.Currency, 1, null),
            ("totalAdulto1", "Total adulto", PipelineFieldType.Currency, 1, null),
            ("totalNino1", "Total nino", PipelineFieldType.Currency, 1, null)
        ]),
        ("Zona Inquietudes",
        [
            ("hotel2", "Hotel 2", PipelineFieldType.Text, 1, null),
            ("valorNetoHotel2", "Valor neto hotel 2", PipelineFieldType.Currency, 1, null),
            ("totalAdulto2", "Total adulto", PipelineFieldType.Currency, 1, null),
            ("totalNino2", "Total nino", PipelineFieldType.Currency, 1, null),
            ("inquietudesNota", "Notas / objeciones del cliente", PipelineFieldType.TextArea, 2, null)
        ]),
        ("Cierre",
        [
            ("propuesta", "Propuesta final", PipelineFieldType.TextArea, 2, null),
            ("resultado", "Resultado", PipelineFieldType.Select, 2, "Cerrado - Ganado\nCerrado - Perdido\nEn negociacion\nPospuesto")
        ]),
        ("Seguimientos",
        [
            ("seguimiento1", "1er seguimiento", PipelineFieldType.TextArea, 2, null),
            ("seguimiento2", "2do seguimiento", PipelineFieldType.TextArea, 2, null),
            ("seguimiento3", "3er seguimiento", PipelineFieldType.TextArea, 2, null)
        ])
    ];

    public async Task EnsureDefaultsAsync(Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return;
        }
        if (await _db.PipelineStages.AnyAsync(cancellationToken))
        {
            return;
        }

        var order = 0;
        foreach (var (stageName, fields) in Defaults)
        {
            var stage = new PipelineStage { TenantId = tenantId, Name = stageName, SortOrder = order++ };
            _db.PipelineStages.Add(stage);

            var fieldOrder = 0;
            foreach (var (key, label, type, col, options) in fields)
            {
                _db.PipelineFieldDefinitions.Add(new PipelineFieldDefinition
                {
                    TenantId = tenantId,
                    StageId = stage.Id,
                    FieldKey = key,
                    Label = label,
                    FieldType = type,
                    Column = col,
                    SortOrder = fieldOrder++,
                    Options = options
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PipelineStageDto>> ListStagesAsync(CancellationToken cancellationToken = default) =>
        await _db.PipelineStages
            .AsNoTracking()
            .OrderBy(s => s.SortOrder)
            .Select(s => new PipelineStageDto(s.Id, s.Name, s.SortOrder, s.IsClosedWon, s.IsClosedLost))
            .ToListAsync(cancellationToken);

    public async Task<PipelineStageDto?> CreateStageAsync(CreatePipelineStageRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return null;
        }

        var maxOrder = await _db.PipelineStages.Select(s => (int?)s.SortOrder).MaxAsync(cancellationToken) ?? -1;
        var stage = new PipelineStage
        {
            TenantId = tenantId,
            Name = request.Name.Trim(),
            SortOrder = request.SortOrder > 0 ? request.SortOrder : maxOrder + 1,
            IsClosedWon = request.IsClosedWon,
            IsClosedLost = request.IsClosedLost
        };
        _db.PipelineStages.Add(stage);
        _audit.Write(actorUserId, "pipeline-stage.create", nameof(PipelineStage), stage.Id,
            previousValue: null, newValue: new { stage.Name, stage.SortOrder }, tenantId: tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return new PipelineStageDto(stage.Id, stage.Name, stage.SortOrder, stage.IsClosedWon, stage.IsClosedLost);
    }

    public async Task<PipelineStageDto?> UpdateStageAsync(Guid stageId, UpdatePipelineStageRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var stage = await _db.PipelineStages.FirstOrDefaultAsync(s => s.Id == stageId, cancellationToken);
        if (stage is null)
        {
            return null;
        }
        stage.Name = request.Name.Trim();
        stage.IsClosedWon = request.IsClosedWon;
        stage.IsClosedLost = request.IsClosedLost;
        _audit.Write(actorUserId, "pipeline-stage.update", nameof(PipelineStage), stage.Id,
            previousValue: null, newValue: new { stage.Name, stage.IsClosedWon, stage.IsClosedLost }, tenantId: stage.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return new PipelineStageDto(stage.Id, stage.Name, stage.SortOrder, stage.IsClosedWon, stage.IsClosedLost);
    }

    public async Task ReorderStagesAsync(ReorderStagesRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var stages = await _db.PipelineStages.ToListAsync(cancellationToken);
        var order = 0;
        foreach (var id in request.OrderedStageIds)
        {
            var stage = stages.FirstOrDefault(s => s.Id == id);
            if (stage is not null)
            {
                stage.SortOrder = order++;
            }
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteStageAsync(Guid stageId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var stage = await _db.PipelineStages.FirstOrDefaultAsync(s => s.Id == stageId, cancellationToken);
        if (stage is null)
        {
            return false;
        }
        if (await _db.Leads.AnyAsync(l => l.StageId == stageId, cancellationToken))
        {
            return false; // no se puede borrar una etapa con leads
        }
        _db.PipelineStages.Remove(stage); // los campos caen por cascade
        _audit.Write(actorUserId, "pipeline-stage.delete", nameof(PipelineStage), stage.Id,
            previousValue: new { stage.Name }, newValue: null, tenantId: stage.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<PipelineFieldDto>> ListFieldsAsync(CancellationToken cancellationToken = default) =>
        await _db.PipelineFieldDefinitions
            .AsNoTracking()
            .OrderBy(f => f.SortOrder)
            .Select(f => new PipelineFieldDto(f.Id, f.StageId, f.FieldKey, f.Label, f.FieldType, f.Column, f.SortOrder, f.Options, f.Description, f.AllowMultiple, f.RepeatWithFieldKey, f.MultiWithDetail, f.TotalSourceKeys))
            .ToListAsync(cancellationToken);

    public async Task<PipelineFieldDto?> CreateFieldAsync(CreatePipelineFieldRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return null;
        }
        if (!await _db.PipelineStages.AnyAsync(s => s.Id == request.StageId, cancellationToken))
        {
            return null;
        }

        var key = string.IsNullOrWhiteSpace(request.FieldKey) ? Slugify(request.Label) : request.FieldKey.Trim();
        // Anti-duplicado: un campo del embudo no puede usar la clave de un campo de sistema (nativo del lead),
        // para evitar dos "Destino"/"Telefono"/etc. El usuario debe usar el marcador de sistema correspondiente.
        if (ReservedSystemKeys.Contains(key.ToLowerInvariant()))
        {
            return null;
        }
        var existingKeys = await _db.PipelineFieldDefinitions.Where(f => f.StageId == request.StageId).Select(f => f.FieldKey).ToListAsync(cancellationToken);
        key = EnsureUniqueKey(key, existingKeys);

        var maxOrder = await _db.PipelineFieldDefinitions.Where(f => f.StageId == request.StageId).Select(f => (int?)f.SortOrder).MaxAsync(cancellationToken) ?? -1;
        var field = new PipelineFieldDefinition
        {
            TenantId = tenantId,
            StageId = request.StageId,
            FieldKey = key,
            Label = request.Label.Trim(),
            FieldType = request.FieldType,
            Column = request.Column is 1 or 2 ? request.Column : 1,
            SortOrder = maxOrder + 1,
            Options = string.IsNullOrWhiteSpace(request.Options) ? null : request.Options.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            AllowMultiple = request.AllowMultiple,
            RepeatWithFieldKey = string.IsNullOrWhiteSpace(request.RepeatWithFieldKey) ? null : request.RepeatWithFieldKey.Trim(),
            MultiWithDetail = request.AllowMultiple && request.MultiWithDetail,
            TotalSourceKeys = request.FieldType == PipelineFieldType.Total && !string.IsNullOrWhiteSpace(request.TotalSourceKeys) ? request.TotalSourceKeys.Trim() : null
        };
        _db.PipelineFieldDefinitions.Add(field);
        _audit.Write(actorUserId, "pipeline-field.create", nameof(PipelineFieldDefinition), field.Id,
            previousValue: null, newValue: new { field.StageId, field.FieldKey, field.Label }, tenantId: tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return new PipelineFieldDto(field.Id, field.StageId, field.FieldKey, field.Label, field.FieldType, field.Column, field.SortOrder, field.Options, field.Description, field.AllowMultiple, field.RepeatWithFieldKey, field.MultiWithDetail, field.TotalSourceKeys);
    }

    public async Task<PipelineFieldDto?> UpdateFieldAsync(Guid fieldId, UpdatePipelineFieldRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var field = await _db.PipelineFieldDefinitions.FirstOrDefaultAsync(f => f.Id == fieldId, cancellationToken);
        if (field is null)
        {
            return null;
        }
        field.Label = request.Label.Trim();
        field.FieldType = request.FieldType;
        field.Column = request.Column is 1 or 2 ? request.Column : 1;
        field.Options = string.IsNullOrWhiteSpace(request.Options) ? null : request.Options.Trim();
        field.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        field.AllowMultiple = request.AllowMultiple;
        field.RepeatWithFieldKey = string.IsNullOrWhiteSpace(request.RepeatWithFieldKey) ? null : request.RepeatWithFieldKey.Trim();
        field.MultiWithDetail = request.AllowMultiple && request.MultiWithDetail;
        field.TotalSourceKeys = request.FieldType == PipelineFieldType.Total && !string.IsNullOrWhiteSpace(request.TotalSourceKeys) ? request.TotalSourceKeys.Trim() : null;
        _audit.Write(actorUserId, "pipeline-field.update", nameof(PipelineFieldDefinition), field.Id,
            previousValue: null, newValue: new { field.Label, field.FieldType }, tenantId: field.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return new PipelineFieldDto(field.Id, field.StageId, field.FieldKey, field.Label, field.FieldType, field.Column, field.SortOrder, field.Options, field.Description, field.AllowMultiple, field.RepeatWithFieldKey, field.MultiWithDetail, field.TotalSourceKeys);
    }

    public async Task<PipelineFieldDto?> MoveFieldToStageAsync(Guid fieldId, Guid targetStageId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var field = await _db.PipelineFieldDefinitions.FirstOrDefaultAsync(f => f.Id == fieldId, cancellationToken);
        if (field is null) { return null; }
        if (field.StageId == targetStageId) { return Map(field); }
        // La etapa destino debe existir (y por filtro de tenant, pertenecer a la agencia).
        if (!await _db.PipelineStages.AnyAsync(s => s.Id == targetStageId, cancellationToken)) { return null; }

        var maxOrder = await _db.PipelineFieldDefinitions.Where(f => f.StageId == targetStageId)
            .Select(f => (int?)f.SortOrder).MaxAsync(cancellationToken) ?? -1;
        field.StageId = targetStageId;
        field.SortOrder = maxOrder + 1;
        _audit.Write(actorUserId, "pipeline-field.move-stage", nameof(PipelineFieldDefinition), field.Id,
            previousValue: null, newValue: new { field.FieldKey, TargetStageId = targetStageId }, tenantId: field.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(field);
    }

    private static PipelineFieldDto Map(PipelineFieldDefinition f) =>
        new(f.Id, f.StageId, f.FieldKey, f.Label, f.FieldType, f.Column, f.SortOrder, f.Options, f.Description, f.AllowMultiple, f.RepeatWithFieldKey, f.MultiWithDetail, f.TotalSourceKeys);

    public async Task ReorderFieldsAsync(ReorderFieldsRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var fields = await _db.PipelineFieldDefinitions.ToListAsync(cancellationToken);
        var order = 0;
        foreach (var id in request.OrderedFieldIds)
        {
            var field = fields.FirstOrDefault(f => f.Id == id);
            if (field is not null) { field.SortOrder = order++; }
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteFieldAsync(Guid fieldId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var field = await _db.PipelineFieldDefinitions.FirstOrDefaultAsync(f => f.Id == fieldId, cancellationToken);
        if (field is null)
        {
            return false;
        }
        _db.PipelineFieldDefinitions.Remove(field);
        _audit.Write(actorUserId, "pipeline-field.delete", nameof(PipelineFieldDefinition), field.Id,
            previousValue: new { field.FieldKey, field.Label }, newValue: null, tenantId: field.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // Claves reservadas que ya existen como campos de sistema (nativos del lead): nombre, telefono,
    // destino, valor estimado y moneda. No se permite crear campos del embudo con estas claves exactas
    // (los campos legitimos como "telefonos" o "valorVuelosPax" no colisionan por ser claves distintas).
    private static readonly HashSet<string> ReservedSystemKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "nombre", "contactname",
        "telefono", "contactphone",
        "destino", "destination",
        "valor", "valor_estimado", "estimatedvalue",
        "moneda", "currency"
    };

    private static string EnsureUniqueKey(string key, IReadOnlyCollection<string> existing)
    {
        if (!existing.Contains(key))
        {
            return key;
        }
        var i = 2;
        while (existing.Contains($"{key}{i}"))
        {
            i++;
        }
        return $"{key}{i}";
    }

    private static string Slugify(string label)
    {
        var normalized = label.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '_') sb.Append('_');
        }
        var slug = sb.ToString().Trim('_');
        return string.IsNullOrEmpty(slug) ? "campo" : slug;
    }
}
