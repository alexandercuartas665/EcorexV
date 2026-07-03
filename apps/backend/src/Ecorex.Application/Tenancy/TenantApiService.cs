using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ecorex.Application.Admin;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

/// <summary>Info de un campo del embudo para generar el ejemplo de curl dinamicamente.</summary>
public sealed record ApiFieldInfo(string FieldKey, string Label, bool IsArray, string Sample);

/// <summary>Config de la API de ingestion del tenant para Mi cuenta (incluye la key en claro y los campos del embudo).</summary>
public sealed record TenantApiConfigDto(Guid TenantId, string? ApiKey, bool IsEnabled, bool HasKey, DateTimeOffset? LastUsedAt, IReadOnlyList<ApiFieldInfo> Fields);

/// <summary>
/// Payload de creacion de lead via API publica. Fields va indexado por FieldKey del embudo; cada valor
/// puede ser un texto o un arreglo de textos (para campos multiples/repetidos).
/// </summary>
public sealed record ApiCreateLeadRequest(
    string? ContactName,
    string? ContactPhone,
    string? Destination,
    decimal? EstimatedValue,
    string? Currency,
    Dictionary<string, JsonElement>? Fields);

public sealed record ApiLeadResult(bool Ok, Guid? LeadId = null, string? Error = null);

public interface ITenantApiService
{
    Task<TenantApiConfigDto?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<TenantApiConfigDto> RegenerateAsync(Guid tenantId, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<TenantApiConfigDto?> SetEnabledAsync(Guid tenantId, bool enabled, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Resuelve el tenant a partir de la API key (debe estar habilitada). Actualiza LastUsedAt.</summary>
    Task<Guid?> ResolveTenantAsync(string apiKey, CancellationToken cancellationToken = default);

    /// <summary>Crea un lead para el tenant indicado (uso de la API publica, sin contexto de sesion).</summary>
    Task<ApiLeadResult> CreateLeadAsync(Guid tenantId, ApiCreateLeadRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// API publica de ingestion de leads por agencia. Cada tenant tiene una API key (hash para buscar,
/// cifrada para mostrarla en Mi cuenta) y un switch on/off. Permite crear un lead y llenar cualquier
/// campo del embudo desde sistemas externos.
/// </summary>
public sealed class TenantApiService : ITenantApiService
{
    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _secretProtector;
    private readonly TimeProvider _timeProvider;
    private readonly IAuditWriter _audit;

    public TenantApiService(IApplicationDbContext db, ISecretProtector secretProtector, TimeProvider timeProvider, IAuditWriter audit)
    {
        _db = db;
        _secretProtector = secretProtector;
        _timeProvider = timeProvider;
        _audit = audit;
    }

    public async Task<TenantApiConfigDto?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var fields = await LoadFieldsAsync(tenantId, cancellationToken);
        var cfg = await _db.TenantApiConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);
        if (cfg is null) { return new TenantApiConfigDto(tenantId, null, false, false, null, fields); }
        return new TenantApiConfigDto(tenantId, Decrypt(cfg.ApiKeyEncrypted), cfg.IsEnabled, !string.IsNullOrEmpty(cfg.ApiKeyEncrypted), cfg.LastUsedAt, fields);
    }

    private async Task<IReadOnlyList<ApiFieldInfo>> LoadFieldsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        // Los campos Total son calculados (solo lectura): no se exponen en la API para que no se envien.
        var defs = await _db.PipelineFieldDefinitions.IgnoreQueryFilters()
            .Where(f => f.TenantId == tenantId && f.FieldType != PipelineFieldType.Total)
            .OrderBy(f => f.SortOrder)
            .Select(f => new { f.FieldKey, f.Label, f.FieldType, f.AllowMultiple, f.RepeatWithFieldKey })
            .ToListAsync(cancellationToken);
        return defs.Select(f => new ApiFieldInfo(
            f.FieldKey, f.Label,
            f.AllowMultiple || !string.IsNullOrEmpty(f.RepeatWithFieldKey),
            SampleFor(f.FieldType))).ToList();
    }

    private static string SampleFor(PipelineFieldType type) => type switch
    {
        PipelineFieldType.Number or PipelineFieldType.Currency => "0",
        PipelineFieldType.Date => "2026-06-15",
        PipelineFieldType.Phone => "573001234567",
        _ => "valor"
    };

    public async Task<TenantApiConfigDto> RegenerateAsync(Guid tenantId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var cfg = await _db.TenantApiConfigs.FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);
        var isNew = cfg is null;
        if (cfg is null) { cfg = new TenantApiConfig { TenantId = tenantId, IsEnabled = true }; _db.TenantApiConfigs.Add(cfg); }

        var key = "cbt_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        cfg.ApiKeyHash = Hash(key);
        cfg.ApiKeyEncrypted = _secretProtector.Protect(key);

        _audit.Write(actorUserId, isNew ? "tenant-api.create" : "tenant-api.regenerate",
            nameof(TenantApiConfig), cfg.Id, previousValue: null, newValue: new { cfg.IsEnabled }, tenantId: tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return new TenantApiConfigDto(tenantId, key, cfg.IsEnabled, true, cfg.LastUsedAt, await LoadFieldsAsync(tenantId, cancellationToken));
    }

    public async Task<TenantApiConfigDto?> SetEnabledAsync(Guid tenantId, bool enabled, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var cfg = await _db.TenantApiConfigs.FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);
        if (cfg is null) { return null; }
        cfg.IsEnabled = enabled;
        _audit.Write(actorUserId, "tenant-api.toggle", nameof(TenantApiConfig), cfg.Id,
            previousValue: null, newValue: new { enabled }, tenantId: tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return new TenantApiConfigDto(tenantId, Decrypt(cfg.ApiKeyEncrypted), cfg.IsEnabled, true, cfg.LastUsedAt, await LoadFieldsAsync(tenantId, cancellationToken));
    }

    public async Task<Guid?> ResolveTenantAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) { return null; }
        var hash = Hash(apiKey.Trim());
        var cfg = await _db.TenantApiConfigs.FirstOrDefaultAsync(c => c.ApiKeyHash == hash, cancellationToken);
        if (cfg is null || !cfg.IsEnabled) { return null; }
        cfg.LastUsedAt = _timeProvider.GetUtcNow();
        await _db.SaveChangesAsync(cancellationToken);
        return cfg.TenantId;
    }

    public async Task<ApiLeadResult> CreateLeadAsync(Guid tenantId, ApiCreateLeadRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ContactName))
        {
            return new ApiLeadResult(false, null, "contactName es obligatorio.");
        }

        // Sin contexto de sesion: se ignora el filtro global y se busca por tenant explicito.
        var stage = await _db.PipelineStages.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId)
            .OrderBy(s => s.SortOrder)
            .FirstOrDefaultAsync(cancellationToken);
        if (stage is null)
        {
            return new ApiLeadResult(false, null, "La agencia no tiene etapas de embudo configuradas.");
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

        // Definiciones del embudo del tenant: para convertir valores segun el tipo y calcular los Total.
        var fieldDefs = await _db.PipelineFieldDefinitions.IgnoreQueryFilters()
            .Where(f => f.TenantId == tenantId)
            .Select(f => new { f.FieldKey, f.FieldType, f.AllowMultiple, f.MultiWithDetail, f.TotalSourceKeys })
            .ToListAsync(cancellationToken);
        var defByKey = fieldDefs
            .GroupBy(f => f.FieldKey)
            .ToDictionary(g => g.Key, g => g.First());

        var clean = new Dictionary<string, string?>();
        if (request.Fields is { Count: > 0 })
        {
            foreach (var kv in request.Fields)
            {
                if (string.IsNullOrWhiteSpace(kv.Key)) { continue; }
                var key = kv.Key.Trim();
                // Los Total son calculados; se ignora cualquier valor enviado por el cliente.
                if (defByKey.TryGetValue(key, out var def) && def.FieldType == PipelineFieldType.Total) { continue; }
                var withDetail = def is { AllowMultiple: true, MultiWithDetail: true };
                clean[key] = FieldValueToString(kv.Value, withDetail);
            }
        }

        // Calcula los campos Total sumando sus origenes (los multiples suman todos sus registros).
        foreach (var tf in fieldDefs.Where(f => f.FieldType == PipelineFieldType.Total && !string.IsNullOrWhiteSpace(f.TotalSourceKeys)))
        {
            decimal sum = 0m;
            foreach (var src in tf.TotalSourceKeys!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (clean.TryGetValue(src, out var raw)) { sum += SumNumeric(raw); }
            }
            clean[tf.FieldKey] = sum.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (clean.Count > 0) { lead.FieldValuesJson = JsonSerializer.Serialize(clean); }

        _db.Leads.Add(lead);
        _db.LeadActivities.Add(new LeadActivity
        {
            TenantId = tenantId,
            LeadId = lead.Id,
            ActivityType = "lead.created",
            Description = $"Lead creado via API en etapa {stage.Name}"
        });
        await _db.SaveChangesAsync(cancellationToken);
        return new ApiLeadResult(true, lead.Id);
    }

    private string? Decrypt(string? enc)
    {
        if (string.IsNullOrEmpty(enc)) { return null; }
        try { return _secretProtector.Unprotect(enc); } catch { return null; }
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    // Convierte el valor recibido al formato de almacenamiento que entiende el formulario del lead.
    // Escalar -> texto. Arreglo (campos multiples/repetidos) -> JSON array de textos. Cuando el campo
    // es "multiple con detalle", cada item se normaliza a objeto {d:detalle, v:valor} (acepta texto suelto).
    private static string? FieldValueToString(JsonElement el, bool withDetail)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.String:
                return el.GetString();
            case JsonValueKind.Null or JsonValueKind.Undefined:
                return null;
            case JsonValueKind.Array:
                if (withDetail)
                {
                    var items = el.EnumerateArray().Select(x =>
                    {
                        if (x.ValueKind == JsonValueKind.Object)
                        {
                            var d = x.TryGetProperty("d", out var dp) && dp.ValueKind == JsonValueKind.String ? dp.GetString() : null;
                            var v = x.TryGetProperty("v", out var vp) ? (vp.ValueKind == JsonValueKind.String ? vp.GetString() : vp.GetRawText()) : null;
                            return new { d, v };
                        }
                        return new { d = (string?)null, v = x.ValueKind == JsonValueKind.String ? x.GetString() : x.GetRawText() };
                    }).ToList();
                    return JsonSerializer.Serialize(items);
                }
                return JsonSerializer.Serialize(
                    el.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.GetRawText()).ToList());
            default:
                return el.GetRawText();
        }
    }

    // Suma el contenido numerico de un valor guardado: escalar, arreglo de textos, o arreglo de objetos {v}.
    private static decimal SumNumeric(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) { return 0m; }
        var t = raw.TrimStart();
        if (t.StartsWith("["))
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                decimal s = 0m;
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var token = el.ValueKind == JsonValueKind.Object && el.TryGetProperty("v", out var vp)
                        ? (vp.ValueKind == JsonValueKind.String ? vp.GetString() : vp.GetRawText())
                        : (el.ValueKind == JsonValueKind.String ? el.GetString() : el.GetRawText());
                    if (decimal.TryParse(token, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var n)) { s += n; }
                }
                return s;
            }
            catch { return 0m; }
        }
        return decimal.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var scalar) ? scalar : 0m;
    }
}
