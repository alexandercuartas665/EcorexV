using Ecorex.Application.Common;
using Ecorex.Application.Forms;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Marketplace;

/// <summary>
/// Implementacion de <see cref="IMarketplaceService"/> (ADR-0097 Ola B2). Publica tomando el SNAPSHOT
/// portable ya existente: <see cref="IFlowPackageService"/> para flujos, <see cref="IFormDefinitionService"/>
/// para formularios. Los items son de plataforma (sin filtro de tenant): el catalogo es global.
/// </summary>
public sealed class MarketplaceService : IMarketplaceService
{
    /// <summary>Tope de la imagen embebida (data-URL): ~650 KB de base64 (~480 KB de imagen).</summary>
    private const int MaxImageRefLength = 900_000;

    private readonly IApplicationDbContext _db;
    private readonly IFlowPackageService _flowPackage;
    private readonly IFormDefinitionService _forms;
    private readonly TimeProvider _clock;

    public MarketplaceService(
        IApplicationDbContext db, IFlowPackageService flowPackage, IFormDefinitionService forms, TimeProvider clock)
    {
        _db = db;
        _flowPackage = flowPackage;
        _forms = forms;
        _clock = clock;
    }

    public async Task<MarketplaceResult<MarketplaceItemDto>> PublishFlowAsync(
        Guid definitionId, MarketplacePublishInput input, Guid? platformUserId, CancellationToken cancellationToken = default)
    {
        var check = ValidateInput(input);
        if (check is not null) { return MarketplaceResult<MarketplaceItemDto>.Fail(check); }

        var snap = await _flowPackage.ExportAsync(definitionId, cancellationToken);
        if (!snap.IsOk || string.IsNullOrWhiteSpace(snap.Value))
        {
            return MarketplaceResult<MarketplaceItemDto>.Fail(snap.Error ?? "No se pudo empaquetar el flujo.");
        }
        var sourceCode = await _db.WorkflowDefinitions.AsNoTracking()
            .Where(d => d.Id == definitionId).Select(d => d.ProcessCode).FirstOrDefaultAsync(cancellationToken);

        return await CreateItemAsync(MarketplaceItemKind.Flow, input, snap.Value!, sourceCode, platformUserId, cancellationToken);
    }

    public async Task<MarketplaceResult<MarketplaceItemDto>> PublishFormAsync(
        Guid definitionId, MarketplacePublishInput input, Guid? platformUserId, CancellationToken cancellationToken = default)
    {
        var check = ValidateInput(input);
        if (check is not null) { return MarketplaceResult<MarketplaceItemDto>.Fail(check); }

        var snap = await _forms.ExportAsync(definitionId, cancellationToken);
        if (!snap.IsOk || string.IsNullOrWhiteSpace(snap.Value))
        {
            return MarketplaceResult<MarketplaceItemDto>.Fail(snap.Error ?? "No se pudo empaquetar el formulario.");
        }
        var sourceCode = await _db.FormDefinitions.AsNoTracking()
            .Where(d => d.Id == definitionId).Select(d => d.Code).FirstOrDefaultAsync(cancellationToken);

        return await CreateItemAsync(MarketplaceItemKind.Form, input, snap.Value!, sourceCode, platformUserId, cancellationToken);
    }

    private async Task<MarketplaceResult<MarketplaceItemDto>> CreateItemAsync(
        MarketplaceItemKind kind, MarketplacePublishInput input, string snapshot, string? sourceCode,
        Guid? platformUserId, CancellationToken cancellationToken)
    {
        var item = new MarketplaceItem
        {
            Kind = kind,
            Title = input.Title.Trim(),
            Description = NullIfBlank(input.Description),
            Category = NullIfBlank(input.Category),
            ImageRef = NullIfBlank(input.ImageRef),
            SnapshotJson = snapshot,
            SnapshotFormatVersion = 1,
            SourceCode = sourceCode,
            IsActive = true,
            ImportCount = 0,
            PublishedByPlatformUserId = platformUserId,
            PublishedAt = _clock.GetUtcNow(),
        };
        _db.MarketplaceItems.Add(item);
        await _db.SaveChangesAsync(cancellationToken);
        return MarketplaceResult<MarketplaceItemDto>.Success(ToDto(item));
    }

    public async Task<IReadOnlyList<MarketplaceItemDto>> ListAsync(
        MarketplaceItemKind? kind, string? category, string? query, bool includeInactive, CancellationToken cancellationToken = default)
    {
        var q = _db.MarketplaceItems.AsNoTracking().AsQueryable();
        if (!includeInactive) { q = q.Where(i => i.IsActive); }
        if (kind is MarketplaceItemKind k) { q = q.Where(i => i.Kind == k); }
        if (!string.IsNullOrWhiteSpace(category)) { q = q.Where(i => i.Category == category); }
        var text = query?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            q = q.Where(i => i.Title.ToLower().Contains(text.ToLower())
                || (i.Description != null && i.Description.ToLower().Contains(text.ToLower())));
        }
        return await q.OrderByDescending(i => i.PublishedAt)
            .Select(i => new MarketplaceItemDto(i.Id, i.Kind, i.Title, i.Description, i.ImageRef, i.Category,
                i.SourceCode, i.IsActive, i.ImportCount, i.PublishedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListCategoriesAsync(CancellationToken cancellationToken = default)
        => await _db.MarketplaceItems.AsNoTracking()
            .Where(i => i.IsActive && i.Category != null && i.Category != "")
            .Select(i => i.Category!).Distinct().OrderBy(c => c).ToListAsync(cancellationToken);

    public async Task<MarketplaceItemDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var i = await _db.MarketplaceItems.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return i is null ? null : new MarketplaceItemDetailDto(
            i.Id, i.Kind, i.Title, i.Description, i.ImageRef, i.Category, i.SourceCode, i.IsActive, i.ImportCount,
            i.PublishedAt, i.SnapshotJson, i.SnapshotFormatVersion);
    }

    public async Task<MarketplaceResult<MarketplaceItemDto>> UpdateAsync(
        Guid id, MarketplacePublishInput input, bool isActive, CancellationToken cancellationToken = default)
    {
        var check = ValidateInput(input);
        if (check is not null) { return MarketplaceResult<MarketplaceItemDto>.Fail(check); }
        var i = await _db.MarketplaceItems.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (i is null) { return MarketplaceResult<MarketplaceItemDto>.Fail("El item no existe."); }
        i.Title = input.Title.Trim();
        i.Description = NullIfBlank(input.Description);
        i.Category = NullIfBlank(input.Category);
        i.ImageRef = NullIfBlank(input.ImageRef);
        i.IsActive = isActive;
        await _db.SaveChangesAsync(cancellationToken);
        return MarketplaceResult<MarketplaceItemDto>.Success(ToDto(i));
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var i = await _db.MarketplaceItems.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (i is null) { return false; }
        _db.MarketplaceItems.Remove(i);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // ---- Ola B3: "Traer" al tenant activo ----

    public async Task<MarketplaceImportPreviewDto?> GetImportPreviewAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var i = await _db.MarketplaceItems.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.IsActive, cancellationToken);
        if (i is null) { return null; }

        var (hasForms, count) = i.Kind == MarketplaceItemKind.Flow
            ? FlowPackageInspector.CountNodeForms(i.SnapshotJson)
            : (false, 0);
        return new MarketplaceImportPreviewDto(i.Id, i.Kind, i.Title, hasForms, count);
    }

    public async Task<MarketplaceResult<MarketplaceImportResult>> ImportAsync(
        Guid id, bool includeNodeForms, CancellationToken cancellationToken = default)
    {
        var item = await _db.MarketplaceItems.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null) { return MarketplaceResult<MarketplaceImportResult>.Fail("La plantilla no existe."); }
        if (!item.IsActive) { return MarketplaceResult<MarketplaceImportResult>.Fail("La plantilla esta inactiva."); }

        MarketplaceImportResult result;
        if (item.Kind == MarketplaceItemKind.Flow)
        {
            var rep = await _flowPackage.ImportAsync(item.SnapshotJson, new FlowImportOptions(includeNodeForms), cancellationToken);
            if (!rep.IsOk || rep.Value is null)
            {
                return MarketplaceResult<MarketplaceImportResult>.Fail(rep.Error ?? "No se pudo traer el flujo.");
            }
            var r = rep.Value;
            result = new MarketplaceImportResult(MarketplaceItemKind.Flow, r.NewDefinitionId, r.NewProcessCode, item.Title,
                r.FormsImported, r.UnmappedCargos, r.UnmappedAgents, r.UnmappedRules, r.Warnings);
        }
        else
        {
            var imp = await _forms.ImportAsync(item.SnapshotJson, cancellationToken);
            if (!imp.IsOk || imp.Value is null)
            {
                return MarketplaceResult<MarketplaceImportResult>.Fail(imp.Error ?? "No se pudo traer el formulario.");
            }
            var d = imp.Value;
            result = new MarketplaceImportResult(MarketplaceItemKind.Form, d.Id, d.Code, d.Title,
                0, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
        }

        // Contador de "traidas" (best-effort, se lee en la galeria/admin).
        item.ImportCount += 1;
        await _db.SaveChangesAsync(cancellationToken);
        return MarketplaceResult<MarketplaceImportResult>.Success(result);
    }

    private static string? ValidateInput(MarketplacePublishInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Title)) { return "El titulo es obligatorio."; }
        if (input.Title.Trim().Length > 200) { return "El titulo supera los 200 caracteres."; }
        if (input.ImageRef is { Length: > MaxImageRefLength }) { return "La imagen es demasiado grande (max ~480 KB)."; }
        return null;
    }

    private static MarketplaceItemDto ToDto(MarketplaceItem i)
        => new(i.Id, i.Kind, i.Title, i.Description, i.ImageRef, i.Category, i.SourceCode, i.IsActive, i.ImportCount, i.PublishedAt);

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
