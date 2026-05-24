using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CubotTravels.Application.Admin;
using CubotTravels.Application.Common;
using CubotTravels.Domain.Entities;
using CubotTravels.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotTravels.Application.Tenancy;

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
        var defs = await _db.PipelineFieldDefinitions.IgnoreQueryFilters()
            .Where(f => f.TenantId == tenantId)
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

        if (request.Fields is { Count: > 0 })
        {
            var clean = new Dictionary<string, string?>();
            foreach (var kv in request.Fields)
            {
                if (string.IsNullOrWhiteSpace(kv.Key)) { continue; }
                clean[kv.Key.Trim()] = FieldValueToString(kv.Value);
            }
            if (clean.Count > 0) { lead.FieldValuesJson = JsonSerializer.Serialize(clean); }
        }

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

    // Convierte el valor recibido al formato de almacenamiento que entiende el formulario del lead:
    // arreglo -> string con JSON array de textos (campos multiples/repetidos); escalar -> texto.
    private static string? FieldValueToString(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Array => JsonSerializer.Serialize(
            el.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.GetRawText()).ToList()),
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => el.GetRawText()
    };
}
