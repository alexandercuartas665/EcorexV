using System.Globalization;
using System.Text;
using System.Text.Json;
using Ecorex.Application.Admin;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

public sealed record HairClassificationResultDto(
    bool Ok, string? Error, Guid? ClassificationId,
    Guid? CategoryId, string? CategoryName, int Confidence, string? Rationale, string? PhotoUrl);

/// <summary>
/// Clasifica por IA de VISION el largo del cabello de una foto de clienta contra las medidas del salon
/// (HairLengthCategory + imagenes de referencia). Usa el proveedor de IA con vision (Gemini o Claude),
/// respeta la cuota del plan y registra AiUsageLog. NO escribe archivos: recibe el base64 de la foto.
/// </summary>
public interface IHairClassifierService
{
    Task<HairClassificationResultDto> ClassifyAsync(string clientPhotoBase64, string mime, string? storedPhotoFileName, Guid actorUserId, CancellationToken cancellationToken = default);
}

public sealed class HairClassifierService : IHairClassifierService
{
    private const int MaxRefsPerCategory = 2; // controla el costo de tokens

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ISecretProtector _secretProtector;
    private readonly IAiProviderClient _client;
    private readonly IAiUsageService _usage;

    public HairClassifierService(IApplicationDbContext db, ITenantContext tenantContext, ISecretProtector secretProtector,
        IAiProviderClient client, IAiUsageService usage)
    {
        _db = db; _tenantContext = tenantContext; _secretProtector = secretProtector;
        _client = client; _usage = usage;
    }

    public async Task<HairClassificationResultDto> ClassifyAsync(string clientPhotoBase64, string mime, string? storedPhotoFileName, Guid actorUserId, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return Err("No hay un tenant activo."); }
        if (string.IsNullOrWhiteSpace(clientPhotoBase64)) { return Err("Falta la foto de la clienta."); }

        // Proveedor con vision: preferimos Gemini, luego Claude.
        var resolved = await ResolveVisionProviderAsync(ct);
        if (resolved is null) { return Err("No hay un proveedor de IA con vision habilitado (Gemini o Claude) en Servidores de IA."); }
        var (provider, apiKey, model, baseUrl) = resolved.Value;

        // Cuota del plan.
        var quota = await _usage.GetQuotaAsync(ct);
        if (quota.Exceeded && quota.Hard)
        {
            return Err($"Alcanzaste el limite de tokens de IA de tu plan este mes ({quota.MonthlyLimitTokens:N0}).");
        }

        // Categorias activas + imagenes de referencia.
        var cats = await _db.HairLengthCategories.AsNoTracking()
            .Where(c => c.IsActive).OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct);
        if (cats.Count == 0) { return Err("Define al menos una medida de cabello (con fotos de referencia) antes de clasificar."); }
        var catIds = cats.Select(c => c.Id).ToList();
        var refs = await _db.HairLengthReferenceImages.AsNoTracking()
            .Where(i => catIds.Contains(i.CategoryId)).OrderBy(i => i.SortOrder).ToListAsync(ct);
        var refsByCat = refs.GroupBy(i => i.CategoryId).ToDictionary(g => g.Key, g => g.Take(MaxRefsPerCategory).ToList());

        // Prompt multimodal: ejemplos por medida + foto de la clienta.
        var parts = new List<AiVisionPart>
        {
            new(Text: "Eres un evaluador experto de largo de cabello para un salon de belleza. A continuacion te muestro " +
                      "las MEDIDAS del salon con fotos de ejemplo de cada una; luego una FOTO de una clienta. Decide a que " +
                      "medida corresponde el largo del cabello de la clienta basandote en los ejemplos.")
        };
        foreach (var c in cats)
        {
            parts.Add(new(Text: $"MEDIDA: \"{c.Name}\".{(string.IsNullOrWhiteSpace(c.Description) ? "" : " " + c.Description)} Ejemplos:"));
            if (refsByCat.TryGetValue(c.Id, out var imgs))
            {
                foreach (var img in imgs)
                {
                    if (img.Content is { Length: > 0 })
                    {
                        parts.Add(new(ImageBase64: Convert.ToBase64String(img.Content),
                            ImageMime: string.IsNullOrWhiteSpace(img.ContentType) ? "image/jpeg" : img.ContentType));
                    }
                }
            }
        }
        parts.Add(new(Text: "FOTO DE LA CLIENTA A CLASIFICAR:"));
        parts.Add(new(ImageBase64: clientPhotoBase64, ImageMime: string.IsNullOrWhiteSpace(mime) ? "image/jpeg" : mime));
        parts.Add(new(Text: "Responde UNICAMENTE un objeto JSON, sin texto adicional, con esta forma: " +
                            "{\"medida\":\"<el nombre EXACTO de una de las medidas listadas>\",\"confianza\":<entero 0-100>,\"motivo\":\"<una frase breve>\"}."));

        const string system = "Clasificas el largo de cabello. Responde solo JSON valido, sin explicaciones fuera del JSON.";
        var result = await _client.CompleteVisionAsync(provider, apiKey, baseUrl, model, system, parts, ct);

        await _usage.RecordAsync(null, provider, model, result.InputTokens, result.OutputTokens, "hair-classify", result.Ok, ct);

        if (!result.Ok) { return Err(result.Error ?? "La IA no pudo clasificar la imagen."); }

        var (medida, confianza, motivo) = ParseAnswer(result.Text);
        var matched = cats.FirstOrDefault(c => Normalize(c.Name) == Normalize(medida))
            ?? cats.FirstOrDefault(c => !string.IsNullOrWhiteSpace(medida) && Normalize(c.Name).Contains(Normalize(medida)));

        var entity = new HairLengthClassification
        {
            TenantId = tenantId,
            PhotoFileName = storedPhotoFileName,
            PredictedCategoryId = matched?.Id,
            PredictedName = string.IsNullOrWhiteSpace(medida) ? null : medida,
            Confidence = Math.Clamp(confianza, 0, 100),
            Rationale = string.IsNullOrWhiteSpace(motivo) ? null : motivo
        };
        _db.HairLengthClassifications.Add(entity);
        await _db.SaveChangesAsync(ct);

        return new HairClassificationResultDto(true, null, entity.Id, matched?.Id,
            matched?.Name ?? (string.IsNullOrWhiteSpace(medida) ? "Sin determinar" : medida),
            entity.Confidence, entity.Rationale,
            storedPhotoFileName is null ? null : $"/media/hair/{entity.Id}");
    }

    private async Task<(AiProvider Provider, string ApiKey, string Model, string? BaseUrl)?> ResolveVisionProviderAsync(CancellationToken ct)
    {
        foreach (var provider in new[] { AiProvider.Gemini, AiProvider.Claude })
        {
            var cfg = await _db.AiProviderConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.Provider == provider, ct);
            if (cfg is null || !cfg.IsEnabled || string.IsNullOrWhiteSpace(cfg.ApiKeyEncrypted)) { continue; }
            string key;
            try { key = _secretProtector.Unprotect(cfg.ApiKeyEncrypted); }
            catch { continue; }
            var meta = AiProviderCatalog.For(provider);
            var model = !string.IsNullOrWhiteSpace(cfg.Model) ? cfg.Model! : meta.DefaultModel;
            return (provider, key, model, string.IsNullOrWhiteSpace(cfg.BaseUrl) ? meta.DefaultBaseUrl : cfg.BaseUrl);
        }
        return null;
    }

    // Extrae medida/confianza/motivo del JSON de la IA (tolera ```json ... ``` y texto alrededor).
    private static (string medida, int confianza, string motivo) ParseAnswer(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) { return ("", 0, ""); }
        var s = text.Trim();
        var start = s.IndexOf('{');
        var end = s.LastIndexOf('}');
        if (start >= 0 && end > start) { s = s.Substring(start, end - start + 1); }
        try
        {
            using var doc = JsonDocument.Parse(s);
            var root = doc.RootElement;
            var medida = GetStr(root, "medida") ?? GetStr(root, "categoria") ?? "";
            var motivo = GetStr(root, "motivo") ?? GetStr(root, "razon") ?? "";
            var conf = 0;
            if (root.TryGetProperty("confianza", out var cf) || root.TryGetProperty("confidence", out cf))
            {
                if (cf.ValueKind == JsonValueKind.Number && cf.TryGetInt32(out var ci)) { conf = ci; }
                else if (cf.ValueKind == JsonValueKind.String && int.TryParse(new string(cf.GetString()!.Where(char.IsDigit).ToArray()), out var cs)) { conf = cs; }
            }
            return (medida.Trim(), conf, motivo.Trim());
        }
        catch { return ("", 0, ""); }
    }

    private static string? GetStr(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Normalize(string s)
    {
        var n = (s ?? string.Empty).Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in n)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) { sb.Append(c); }
        }
        return sb.ToString();
    }

    private static HairClassificationResultDto Err(string msg) => new(false, msg, null, null, null, 0, null, null);
}
