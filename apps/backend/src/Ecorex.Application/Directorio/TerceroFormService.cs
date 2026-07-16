using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Directorio;

/// <summary>
/// Implementacion de ITerceroFormService: config de que formularios se ofrecen en el modal de
/// tercero. Aislamiento por tenant via filtro global (nunca se filtra a mano por TenantId); el
/// alta estampa el TenantId del contexto. Calcado del patron de TerceroFieldService.
/// </summary>
public sealed class TerceroFormService : ITerceroFormService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public TerceroFormService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<TerceroFormLinkDto>> ListAsync(CancellationToken cancellationToken = default)
        => await _db.TerceroFormLinks
            .OrderBy(l => l.SortOrder)
            .Select(l => new TerceroFormLinkDto(
                l.Id,
                l.FormDefinitionId,
                l.FormDefinition!.Title,
                l.FormDefinition.Code,
                l.SortOrder))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TerceroFormCandidateDto>> ListCandidatesAsync(CancellationToken cancellationToken = default)
    {
        var linked = await _db.TerceroFormLinks.Select(l => l.FormDefinitionId).ToListAsync(cancellationToken);
        return await _db.FormDefinitions
            .Where(f => !f.IsArchived && f.Status == FormStatus.Active && !linked.Contains(f.Id))
            .OrderBy(f => f.Title)
            .Select(f => new TerceroFormCandidateDto(f.Id, f.Title, f.Code))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> AddAsync(Guid formDefinitionId, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId)
        {
            return false;
        }
        // El formulario debe existir en el tenant (el filtro global ya acota la consulta).
        var exists = await _db.FormDefinitions.AnyAsync(f => f.Id == formDefinitionId, cancellationToken);
        if (!exists)
        {
            return false;
        }
        if (await _db.TerceroFormLinks.AnyAsync(l => l.FormDefinitionId == formDefinitionId, cancellationToken))
        {
            return true; // idempotente
        }
        var maxOrder = await _db.TerceroFormLinks.Select(l => (int?)l.SortOrder).MaxAsync(cancellationToken) ?? -1;
        _db.TerceroFormLinks.Add(new TerceroFormLink
        {
            TenantId = tenantId,
            FormDefinitionId = formDefinitionId,
            SortOrder = maxOrder + 1
        });
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RemoveAsync(Guid linkId, CancellationToken cancellationToken = default)
    {
        var link = await _db.TerceroFormLinks.FirstOrDefaultAsync(l => l.Id == linkId, cancellationToken);
        if (link is null)
        {
            return false;
        }
        _db.TerceroFormLinks.Remove(link);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
