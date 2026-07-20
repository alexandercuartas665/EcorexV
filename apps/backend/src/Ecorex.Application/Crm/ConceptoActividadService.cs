using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Crm;

/// <summary>
/// Catalogo de conceptos de actividad del CRM (000125). Tenant-scoped por el filtro global.
/// Estos conceptos gobiernan las actividades que se ejecutan desde el gestor de contactos.
/// </summary>
public sealed class ConceptoActividadService : IConceptoActividadService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;

    public ConceptoActividadService(IApplicationDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    public async Task<IReadOnlyList<ConceptoActividadDto>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var q = _db.ConceptosActividad.AsNoTracking().AsQueryable();
        if (!includeArchived) { q = q.Where(c => !c.IsArchived); }
        var items = await q
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .GroupJoin(_db.FormDefinitions.AsNoTracking(),
                c => c.FormDefinitionId, f => (Guid?)f.Id, (c, fs) => new { c, fs })
            .SelectMany(x => x.fs.DefaultIfEmpty(), (x, f) => Project(x.c, f))
            .ToListAsync(cancellationToken);
        return await FillSubcategoriasAsync(items, cancellationToken);
    }

    /// <summary>
    /// Completa el nombre de la tarea-proceso en una consulta aparte. Se hace asi (y no con otro
    /// GroupJoin encadenado) para no volver ilegible la proyeccion: son pocos conceptos por tenant.
    /// </summary>
    private async Task<IReadOnlyList<ConceptoActividadDto>> FillSubcategoriasAsync(
        List<ConceptoActividadDto> items, CancellationToken cancellationToken)
    {
        var ids = items.Where(i => i.SubcategoriaId is not null)
            .Select(i => i.SubcategoriaId!.Value).Distinct().ToList();
        if (ids.Count == 0) { return items; }
        var subs = await _db.ActividadSubcategorias.AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.Nombre, Categoria = s.Categoria!.Nombre })
            .ToListAsync(cancellationToken);
        var map = subs.ToDictionary(s => s.Id);
        return items.Select(i => i.SubcategoriaId is Guid sid && map.TryGetValue(sid, out var s)
            ? i with { SubcategoriaNombre = s.Nombre, SubcategoriaCategoria = s.Categoria }
            : i).ToList();
    }

    /// <summary>Subcategorias vivas del catalogo 000270, para el selector "tarea de proceso".</summary>
    public async Task<IReadOnlyList<TareaProcesoOpcionDto>> ListTareasProcesoAsync(
        CancellationToken cancellationToken = default)
        => await _db.ActividadSubcategorias.AsNoTracking()
            .Where(s => !s.IsArchived)
            .OrderBy(s => s.Categoria!.Nombre).ThenBy(s => s.SortOrder).ThenBy(s => s.Nombre)
            .Select(s => new TareaProcesoOpcionDto(
                s.Id, s.Nombre, s.Categoria!.Nombre, s.WorkflowDefinitionId != null))
            .ToListAsync(cancellationToken);

    public async Task<ConceptoActividadDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var c = await _db.ConceptosActividad.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (c is null) { return null; }
        var f = c.FormDefinitionId is Guid fid
            ? await _db.FormDefinitions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == fid, cancellationToken)
            : null;
        var dto = Project(c, f);
        return (await FillSubcategoriasAsync(new List<ConceptoActividadDto> { dto }, cancellationToken))[0];
    }

    public async Task<ConceptoResult<ConceptoActividadDto>> CreateAsync(SaveConceptoActividadRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return ConceptoResult<ConceptoActividadDto>.Fail("Sin tenant activo.");
        }
        var validation = Validate(request);
        if (validation is not null) { return ConceptoResult<ConceptoActividadDto>.Fail(validation); }

        var code = request.Code.Trim();
        if (await _db.ConceptosActividad.AnyAsync(c => c.Code == code, cancellationToken))
        {
            return ConceptoResult<ConceptoActividadDto>.Fail($"Ya existe un concepto con el codigo '{code}'.");
        }
        if (await FormInvalidAsync(request.FormDefinitionId, cancellationToken))
        {
            return ConceptoResult<ConceptoActividadDto>.Fail("El formulario seleccionado no existe.");
        }
        if (await SubcategoriaInvalidAsync(request.SubcategoriaId, cancellationToken))
        {
            return ConceptoResult<ConceptoActividadDto>.Fail("La tarea de proceso seleccionada no existe.");
        }

        var next = await _db.ConceptosActividad.CountAsync(cancellationToken);
        var entity = new ConceptoActividad
        {
            TenantId = tenantId,
            Code = code,
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            FormDefinitionId = request.FormDefinitionId,
            SubcategoriaId = request.SubcategoriaId,
            HandlesValues = request.HandlesValues,
            Mode = request.Mode,
            SortOrder = next,
        };
        _db.ConceptosActividad.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return ConceptoResult<ConceptoActividadDto>.Success((await GetAsync(entity.Id, cancellationToken))!);
    }

    public async Task<ConceptoResult<ConceptoActividadDto>> UpdateAsync(Guid id, SaveConceptoActividadRequest request, CancellationToken cancellationToken = default)
    {
        var entity = await _db.ConceptosActividad.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (entity is null) { return ConceptoResult<ConceptoActividadDto>.Fail("El concepto no existe."); }
        var validation = Validate(request);
        if (validation is not null) { return ConceptoResult<ConceptoActividadDto>.Fail(validation); }

        var code = request.Code.Trim();
        if (await _db.ConceptosActividad.AnyAsync(c => c.Id != id && c.Code == code, cancellationToken))
        {
            return ConceptoResult<ConceptoActividadDto>.Fail($"Ya existe un concepto con el codigo '{code}'.");
        }
        if (await FormInvalidAsync(request.FormDefinitionId, cancellationToken))
        {
            return ConceptoResult<ConceptoActividadDto>.Fail("El formulario seleccionado no existe.");
        }
        if (await SubcategoriaInvalidAsync(request.SubcategoriaId, cancellationToken))
        {
            return ConceptoResult<ConceptoActividadDto>.Fail("La tarea de proceso seleccionada no existe.");
        }

        entity.Code = code;
        entity.Name = request.Name.Trim();
        entity.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        entity.FormDefinitionId = request.FormDefinitionId;
        entity.SubcategoriaId = request.SubcategoriaId;
        entity.HandlesValues = request.HandlesValues;
        entity.Mode = request.Mode;
        await _db.SaveChangesAsync(cancellationToken);
        return ConceptoResult<ConceptoActividadDto>.Success((await GetAsync(entity.Id, cancellationToken))!);
    }

    public async Task<ConceptoResult<bool>> SetArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default)
    {
        var entity = await _db.ConceptosActividad.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (entity is null) { return ConceptoResult<bool>.Fail("El concepto no existe."); }
        entity.IsArchived = archived;
        await _db.SaveChangesAsync(cancellationToken);
        return ConceptoResult<bool>.Success(true);
    }

    private static string? Validate(SaveConceptoActividadRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Code)) { return "El codigo es obligatorio."; }
        if (string.IsNullOrWhiteSpace(r.Name)) { return "El nombre es obligatorio."; }
        return null;
    }

    private async Task<bool> FormInvalidAsync(Guid? formId, CancellationToken cancellationToken)
        => formId is Guid fid && !await _db.FormDefinitions.AnyAsync(f => f.Id == fid, cancellationToken);

    // El filtro global por tenant hace que una subcategoria de OTRO tenant no exista para esta
    // consulta, asi que esta comprobacion tambien cierra el paso a una FK cross-tenant.
    private async Task<bool> SubcategoriaInvalidAsync(Guid? subId, CancellationToken cancellationToken)
        => subId is Guid sid && !await _db.ActividadSubcategorias.AnyAsync(s => s.Id == sid, cancellationToken);

    private static ConceptoActividadDto Project(ConceptoActividad c, FormDefinition? f) => new(
        c.Id, c.Code, c.Name, c.Description,
        c.FormDefinitionId, f == null ? null : f.Title, f == null ? null : f.Code,
        c.HandlesValues, c.Mode, c.IsArchived, c.SortOrder, c.SubcategoriaId);
}
