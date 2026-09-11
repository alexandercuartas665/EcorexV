using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Cards;

/// <summary>
/// Implementacion de <see cref="ICardTagService"/> (ADR-0097 Fase A). Tenant-scoped por el filtro
/// global de EF; las escrituras fijan TenantId con el contexto de tenant. Flow usa FlowTags (ProcessCode)
/// y Form usa FormTags (FormCode); el catalogo de etiquetas es <see cref="CardTag"/> con Scope.
/// </summary>
public sealed class CardTagService : ICardTagService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public CardTagService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    private Guid TenantId => _tenant.TenantId ?? throw new InvalidOperationException("No hay tenant activo.");

    public async Task<IReadOnlyList<CardTagDto>> ListTagsAsync(CardTagScope scope, CancellationToken cancellationToken = default)
        => await _db.CardTags.AsNoTracking()
            .Where(t => t.Scope == scope)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .Select(t => new CardTagDto(t.Id, t.Name, t.Color, t.SortOrder))
            .ToListAsync(cancellationToken);

    public async Task<CardTagDto?> CreateTagAsync(CardTagScope scope, string name, string? color, CancellationToken cancellationToken = default)
    {
        name = (name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 60) { return null; }
        // Idempotente por (scope, nombre): si ya existe, se devuelve la existente.
        var existing = await _db.CardTags
            .FirstOrDefaultAsync(t => t.Scope == scope && t.Name == name, cancellationToken);
        if (existing is not null)
        {
            return new CardTagDto(existing.Id, existing.Name, existing.Color, existing.SortOrder);
        }
        var maxOrder = await _db.CardTags.Where(t => t.Scope == scope)
            .Select(t => (int?)t.SortOrder).MaxAsync(cancellationToken) ?? 0;
        var tag = new CardTag
        {
            TenantId = TenantId,
            Scope = scope,
            Name = name,
            Color = NullIfBlank(color),
            SortOrder = maxOrder + 1,
        };
        _db.CardTags.Add(tag);
        await _db.SaveChangesAsync(cancellationToken);
        return new CardTagDto(tag.Id, tag.Name, tag.Color, tag.SortOrder);
    }

    public async Task<bool> UpdateTagAsync(Guid tagId, string name, string? color, CancellationToken cancellationToken = default)
    {
        name = (name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 60) { return false; }
        var tag = await _db.CardTags.FirstOrDefaultAsync(t => t.Id == tagId, cancellationToken);
        if (tag is null) { return false; }
        tag.Name = name;
        tag.Color = NullIfBlank(color);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteTagAsync(Guid tagId, CancellationToken cancellationToken = default)
    {
        var tag = await _db.CardTags.FirstOrDefaultAsync(t => t.Id == tagId, cancellationToken);
        if (tag is null) { return false; }
        // Los enlaces (FlowTag/FormTag) caen por FK cascade; no se toca ningun flujo/formulario.
        _db.CardTags.Remove(tag);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task ReorderTagsAsync(CardTagScope scope, IReadOnlyList<Guid> orderedIds, CancellationToken cancellationToken = default)
    {
        var tags = await _db.CardTags.Where(t => t.Scope == scope).ToListAsync(cancellationToken);
        var order = 0;
        foreach (var id in orderedIds)
        {
            var tag = tags.FirstOrDefault(t => t.Id == id);
            if (tag is not null) { tag.SortOrder = order++; }
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<Guid>>> GetAssignmentsAsync(CardTagScope scope, CancellationToken cancellationToken = default)
    {
        var map = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);
        if (scope == CardTagScope.Flow)
        {
            var rows = await _db.FlowTags.AsNoTracking()
                .Select(x => new { x.ProcessCode, x.CardTagId }).ToListAsync(cancellationToken);
            foreach (var r in rows)
            {
                if (!map.TryGetValue(r.ProcessCode, out var list)) { map[r.ProcessCode] = list = new(); }
                list.Add(r.CardTagId);
            }
        }
        else
        {
            var rows = await _db.FormTags.AsNoTracking()
                .Select(x => new { x.FormCode, x.CardTagId }).ToListAsync(cancellationToken);
            foreach (var r in rows)
            {
                if (!map.TryGetValue(r.FormCode, out var list)) { map[r.FormCode] = list = new(); }
                list.Add(r.CardTagId);
            }
        }
        return map.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<Guid>)kv.Value, StringComparer.Ordinal);
    }

    public async Task SetCardTagsAsync(CardTagScope scope, string cardCode, IReadOnlyList<Guid> tagIds, CancellationToken cancellationToken = default)
    {
        cardCode = (cardCode ?? "").Trim();
        if (string.IsNullOrWhiteSpace(cardCode)) { return; }
        // Solo etiquetas existentes del mismo ambito (evita enlazar basura).
        var valid = await _db.CardTags.Where(t => t.Scope == scope && tagIds.Contains(t.Id))
            .Select(t => t.Id).ToListAsync(cancellationToken);
        var validSet = valid.ToHashSet();

        if (scope == CardTagScope.Flow)
        {
            var existing = await _db.FlowTags.Where(x => x.ProcessCode == cardCode).ToListAsync(cancellationToken);
            _db.FlowTags.RemoveRange(existing.Where(x => !validSet.Contains(x.CardTagId)));
            var have = existing.Select(x => x.CardTagId).ToHashSet();
            foreach (var id in validSet.Where(id => !have.Contains(id)))
            {
                _db.FlowTags.Add(new FlowTag { TenantId = TenantId, ProcessCode = cardCode, CardTagId = id });
            }
        }
        else
        {
            var existing = await _db.FormTags.Where(x => x.FormCode == cardCode).ToListAsync(cancellationToken);
            _db.FormTags.RemoveRange(existing.Where(x => !validSet.Contains(x.CardTagId)));
            var have = existing.Select(x => x.CardTagId).ToHashSet();
            foreach (var id in validSet.Where(id => !have.Contains(id)))
            {
                _db.FormTags.Add(new FormTag { TenantId = TenantId, FormCode = cardCode, CardTagId = id });
            }
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task BackfillFlowCategoriesAsync(CancellationToken cancellationToken = default)
    {
        // (ProcessCode, Category) distintos con categoria no vacia. Category es por version; se toma
        // cualquiera no vacia por flujo (la mas reciente por CreatedAt).
        var pairs = await _db.WorkflowDefinitions.AsNoTracking()
            .Where(d => d.Category != null && d.Category != "")
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new { d.ProcessCode, d.Category })
            .ToListAsync(cancellationToken);
        if (pairs.Count == 0) { return; }

        var byFlow = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in pairs)
        {
            if (!byFlow.ContainsKey(p.ProcessCode)) { byFlow[p.ProcessCode] = p.Category!.Trim(); }
        }

        // Etiquetas Flow existentes (por nombre) y enlaces existentes.
        var tags = await _db.CardTags.Where(t => t.Scope == CardTagScope.Flow).ToListAsync(cancellationToken);
        var tagByName = tags.ToDictionary(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase);
        var linkedCodes = (await _db.FlowTags.AsNoTracking().Select(x => x.ProcessCode).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        var maxOrder = tags.Count == 0 ? 0 : tags.Max(t => t.SortOrder);

        var changed = false;
        foreach (var (code, category) in byFlow)
        {
            if (linkedCodes.Contains(code)) { continue; } // ya tiene etiquetas: no se toca.
            if (string.IsNullOrWhiteSpace(category)) { continue; }
            if (!tagByName.TryGetValue(category, out var tag))
            {
                tag = new CardTag { TenantId = TenantId, Scope = CardTagScope.Flow, Name = category, SortOrder = ++maxOrder };
                _db.CardTags.Add(tag);
                tagByName[category] = tag;
            }
            _db.FlowTags.Add(new FlowTag { TenantId = TenantId, ProcessCode = code, CardTagId = tag.Id });
            changed = true;
        }
        if (changed) { await _db.SaveChangesAsync(cancellationToken); }
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
