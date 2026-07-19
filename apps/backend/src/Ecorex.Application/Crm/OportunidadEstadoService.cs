using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Crm;

/// <summary>
/// Catalogo de etapas configurables del pipeline de oportunidades (000740). Tenant-scoped por el
/// filtro global (nunca se filtra a mano por TenantId); el alta estampa el TenantId del contexto.
/// Soft-delete via IsArchived (nunca hard-delete: conserva las oportunidades historicas).
/// </summary>
public sealed class OportunidadEstadoService : IOportunidadEstadoService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;

    // Set por defecto que mapea el enum heredado OportunidadEtapa (SortOrder == (int)Etapa).
    private static readonly (string Name, string Color, OportunidadEstadoTipo Tipo)[] Defaults =
    {
        ("Nueva", "--t-slate", OportunidadEstadoTipo.Abierta),
        ("Calificada", "--t-blue", OportunidadEstadoTipo.Abierta),
        ("Propuesta", "--t-amber", OportunidadEstadoTipo.Abierta),
        ("Negociacion", "--t-violet", OportunidadEstadoTipo.Abierta),
        ("Ganada", "--t-green", OportunidadEstadoTipo.Ganada),
        ("Perdida", "--t-rose", OportunidadEstadoTipo.Perdida),
    };

    public OportunidadEstadoService(IApplicationDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    public async Task<IReadOnlyList<OportunidadEstadoDto>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var q = _db.OportunidadEstados.AsNoTracking().AsQueryable();
        if (!includeArchived) { q = q.Where(e => !e.IsArchived); }
        return await q
            .OrderBy(e => e.SortOrder).ThenBy(e => e.Name)
            .Select(e => new OportunidadEstadoDto(e.Id, e.Name, e.SortOrder, e.Color, e.Tipo, e.IsArchived))
            .ToListAsync(cancellationToken);
    }

    public async Task<OportunidadEstadoDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var e = await _db.OportunidadEstados.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return e is null ? null : new OportunidadEstadoDto(e.Id, e.Name, e.SortOrder, e.Color, e.Tipo, e.IsArchived);
    }

    public async Task<ConceptoResult<OportunidadEstadoDto>> CreateAsync(SaveOportunidadEstadoRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return ConceptoResult<OportunidadEstadoDto>.Fail("Sin tenant activo.");
        }
        var validation = Validate(request);
        if (validation is not null) { return ConceptoResult<OportunidadEstadoDto>.Fail(validation); }

        // Nueva etapa al final del pipeline.
        var next = (await _db.OportunidadEstados.Select(e => (int?)e.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
        var entity = new OportunidadEstado
        {
            TenantId = tenantId,
            Name = request.Name.Trim(),
            Color = NormalizeColor(request.Color),
            Tipo = request.Tipo,
            SortOrder = next,
        };
        _db.OportunidadEstados.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return ConceptoResult<OportunidadEstadoDto>.Success((await GetAsync(entity.Id, cancellationToken))!);
    }

    public async Task<ConceptoResult<OportunidadEstadoDto>> UpdateAsync(Guid id, SaveOportunidadEstadoRequest request, CancellationToken cancellationToken = default)
    {
        var entity = await _db.OportunidadEstados.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (entity is null) { return ConceptoResult<OportunidadEstadoDto>.Fail("La etapa no existe."); }
        var validation = Validate(request);
        if (validation is not null) { return ConceptoResult<OportunidadEstadoDto>.Fail(validation); }

        entity.Name = request.Name.Trim();
        entity.Color = NormalizeColor(request.Color);
        entity.Tipo = request.Tipo;
        await _db.SaveChangesAsync(cancellationToken);
        return ConceptoResult<OportunidadEstadoDto>.Success((await GetAsync(entity.Id, cancellationToken))!);
    }

    public async Task<ConceptoResult<bool>> ReorderAsync(IReadOnlyList<Guid> orderedIds, CancellationToken cancellationToken = default)
    {
        if (orderedIds is null || orderedIds.Count == 0)
        {
            return ConceptoResult<bool>.Success(true);
        }
        var entities = await _db.OportunidadEstados
            .Where(e => orderedIds.Contains(e.Id))
            .ToListAsync(cancellationToken);
        var byId = entities.ToDictionary(e => e.Id);
        var order = 0;
        foreach (var id in orderedIds)
        {
            if (byId.TryGetValue(id, out var e)) { e.SortOrder = order; order++; }
        }
        await _db.SaveChangesAsync(cancellationToken);
        return ConceptoResult<bool>.Success(true);
    }

    public async Task<ConceptoResult<bool>> SetArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default)
    {
        var entity = await _db.OportunidadEstados.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (entity is null) { return ConceptoResult<bool>.Fail("La etapa no existe."); }

        // Guarda de usabilidad: no dejar el pipeline sin ninguna etapa Abierta al archivar.
        if (archived && !entity.IsArchived && entity.Tipo == OportunidadEstadoTipo.Abierta)
        {
            var otrasAbiertas = await _db.OportunidadEstados
                .CountAsync(e => e.Id != id && !e.IsArchived && e.Tipo == OportunidadEstadoTipo.Abierta, cancellationToken);
            if (otrasAbiertas == 0)
            {
                return ConceptoResult<bool>.Fail("No se puede archivar la unica etapa abierta del pipeline.");
            }
        }
        entity.IsArchived = archived;
        await _db.SaveChangesAsync(cancellationToken);
        return ConceptoResult<bool>.Success(true);
    }

    public async Task EnsureDefaultsAsync(CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return; }
        // Idempotente: solo siembra si el tenant no tiene ninguna etapa (incluye archivadas).
        var any = await _db.OportunidadEstados.IgnoreQueryFilters()
            .AnyAsync(e => e.TenantId == tenantId, cancellationToken);
        if (any) { return; }

        var order = 0;
        foreach (var d in Defaults)
        {
            _db.OportunidadEstados.Add(new OportunidadEstado
            {
                TenantId = tenantId,
                Name = d.Name,
                Color = d.Color,
                Tipo = d.Tipo,
                SortOrder = order,
            });
            order++;
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task BackfillAsync(CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid) { return; }

        // Mapa SortOrder -> EstadoId de las etapas del tenant (el default mapea SortOrder == (int)Etapa).
        var estados = await _db.OportunidadEstados.AsNoTracking()
            .Select(e => new { e.Id, e.SortOrder })
            .ToListAsync(cancellationToken);
        if (estados.Count == 0) { return; }
        var bySort = estados
            .GroupBy(e => e.SortOrder)
            .ToDictionary(g => g.Key, g => g.First().Id);

        var pendientes = await _db.Oportunidades
            .Where(o => o.EstadoId == null)
            .ToListAsync(cancellationToken);
        if (pendientes.Count == 0) { return; }

        var toque = false;
        foreach (var o in pendientes)
        {
            if (bySort.TryGetValue((int)o.Etapa, out var estadoId))
            {
                o.EstadoId = estadoId;
                toque = true;
            }
        }
        if (toque) { await _db.SaveChangesAsync(cancellationToken); }
    }

    private static string? Validate(SaveOportunidadEstadoRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) { return "El nombre de la etapa es obligatorio."; }
        if (r.Name.Trim().Length > 80) { return "El nombre no puede superar 80 caracteres."; }
        return null;
    }

    private static string NormalizeColor(string? color)
        => string.IsNullOrWhiteSpace(color) ? "--t-slate" : color.Trim();
}
