using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Organization;

/// <summary>
/// Implementacion de IOrgUnitService (ADR-0017). El aislamiento por tenant lo garantiza el
/// filtro global; la validacion de ciclos del arbol es pura (OrgUnitTree.WouldCreateCycle)
/// sobre el mapa Id -&gt; ParentId de las unidades del tenant.
/// </summary>
public sealed class OrgUnitService : IOrgUnitService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;

    public OrgUnitService(IApplicationDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    // ---- Arbol / consulta ----

    public async Task<IReadOnlyList<OrgUnitNodeDto>> GetTreeAsync(
        bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var units = await LoadUnitsWithMetaAsync(includeArchived, cancellationToken);

        var byParent = units
            .GroupBy(u => u.ParentId)
            .ToDictionary(g => g.Key ?? Guid.Empty, g => g.OrderBy(u => u.SortOrder).ThenBy(u => u.Name).ToList());

        List<OrgUnitNodeDto> BuildChildren(Guid? parentId)
        {
            if (!byParent.TryGetValue(parentId ?? Guid.Empty, out var children))
            {
                return [];
            }
            return children.Select(u => new OrgUnitNodeDto(
                u.Id, u.Name, u.Kind, u.ParentId, u.ResponsibleTenantUserId, u.ResponsibleName,
                u.Description, u.SortOrder, u.IsArchived, u.MemberCount,
                BuildChildren(u.Id), u.Classifier, u.TenantUserId, u.OccupantName)).ToList();
        }

        // Raices: sin padre O con padre fuera del conjunto visible (ej. padre archivado
        // cuando includeArchived = false): asi ninguna unidad visible queda huerfana.
        var visibleIds = units.Select(u => u.Id).ToHashSet();
        var roots = units
            .Where(u => u.ParentId is null || !visibleIds.Contains(u.ParentId.Value))
            .OrderBy(u => u.SortOrder).ThenBy(u => u.Name)
            .Select(u => new OrgUnitNodeDto(
                u.Id, u.Name, u.Kind, u.ParentId, u.ResponsibleTenantUserId, u.ResponsibleName,
                u.Description, u.SortOrder, u.IsArchived, u.MemberCount,
                BuildChildren(u.Id), u.Classifier, u.TenantUserId, u.OccupantName))
            .ToList();
        return roots;
    }

    public async Task<IReadOnlyList<OrgUnitDto>> ListAsync(
        bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var units = await LoadUnitsWithMetaAsync(includeArchived, cancellationToken);
        var nameById = units.ToDictionary(u => u.Id, u => u.Name);
        return units
            .OrderBy(u => u.SortOrder).ThenBy(u => u.Name)
            .Select(u => new OrgUnitDto(
                u.Id, u.Name, u.Kind, u.ParentId,
                u.ParentId is Guid pid && nameById.TryGetValue(pid, out var pname) ? pname : null,
                u.ResponsibleTenantUserId, u.ResponsibleName, u.Description,
                u.SortOrder, u.IsArchived, u.MemberCount,
                u.Classifier, u.TenantUserId, u.OccupantName))
            .ToList();
    }

    public async Task<OrgUnitDto?> GetAsync(Guid unitId, CancellationToken cancellationToken = default)
    {
        var unit = await _db.OrgUnits.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == unitId, cancellationToken);
        if (unit is null)
        {
            return null;
        }
        return await ToDtoAsync(unit, cancellationToken);
    }

    public async Task<OrgKpisDto> GetKpisAsync(CancellationToken cancellationToken = default)
    {
        var totalUnits = await _db.OrgUnits.CountAsync(u => !u.IsArchived, cancellationToken);
        var areas = await _db.OrgUnits.CountAsync(u => !u.IsArchived && u.Kind == OrgUnitKind.Area, cancellationToken);

        // Usuarios asignados: miembros + responsables de unidades activas, sin duplicar.
        var memberUsers = await _db.OrgUnitMembers
            .Where(m => !m.OrgUnit!.IsArchived)
            .Select(m => m.TenantUserId)
            .ToListAsync(cancellationToken);
        var responsibleUsers = await _db.OrgUnits
            .Where(u => !u.IsArchived && u.ResponsibleTenantUserId != null)
            .Select(u => u.ResponsibleTenantUserId!.Value)
            .ToListAsync(cancellationToken);
        var assignedUsers = memberUsers.Concat(responsibleUsers).Distinct().Count();

        return new OrgKpisDto(totalUnits, assignedUsers, areas);
    }

    // ---- CRUD ----

    public async Task<OrgResult<OrgUnitDto>> CreateAsync(
        SaveOrgUnitRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return OrgResult<OrgUnitDto>.Invalid("No hay tenant activo.");
        }
        var error = await ValidateAsync(request, cancellationToken);
        if (error is not null)
        {
            return OrgResult<OrgUnitDto>.Invalid(error);
        }
        if (request.ParentId is Guid parentId
            && !await _db.OrgUnits.AnyAsync(u => u.Id == parentId, cancellationToken))
        {
            return OrgResult<OrgUnitDto>.NotFound("La unidad padre no existe.");
        }

        var unit = new OrgUnit
        {
            TenantId = tenantId,
            Name = request.Name.Trim(),
            Kind = request.Kind,
            ParentId = request.ParentId,
            ResponsibleTenantUserId = request.ResponsibleTenantUserId,
            Description = Normalize(request.Description),
            SortOrder = request.SortOrder,
            Classifier = request.Classifier,
            // TenantUserId solo aplica a Funcionario; para Dependencia/Cargo se ignora.
            TenantUserId = request.Classifier == OrgUnitClassifier.Funcionario ? request.TenantUserId : null
        };
        _db.OrgUnits.Add(unit);
        await _db.SaveChangesAsync(cancellationToken);
        return OrgResult<OrgUnitDto>.Ok(await ToDtoAsync(unit, cancellationToken));
    }

    public async Task<OrgResult<OrgUnitDto>> UpdateAsync(
        Guid unitId, SaveOrgUnitRequest request, CancellationToken cancellationToken = default)
    {
        var unit = await _db.OrgUnits.FirstOrDefaultAsync(u => u.Id == unitId, cancellationToken);
        if (unit is null)
        {
            return OrgResult<OrgUnitDto>.NotFound("La dependencia no existe.");
        }
        var error = await ValidateAsync(request, cancellationToken);
        if (error is not null)
        {
            return OrgResult<OrgUnitDto>.Invalid(error);
        }

        if (request.ParentId != unit.ParentId && request.ParentId is Guid newParentId)
        {
            if (newParentId == unitId)
            {
                return OrgResult<OrgUnitDto>.Invalid("Una dependencia no puede ser su propio padre.");
            }
            if (!await _db.OrgUnits.AnyAsync(u => u.Id == newParentId, cancellationToken))
            {
                return OrgResult<OrgUnitDto>.NotFound("La unidad padre no existe.");
            }
            // Validacion de ciclos: el padre propuesto no puede ser descendiente de la unidad.
            var parentByUnit = await _db.OrgUnits.AsNoTracking()
                .Select(u => new { u.Id, u.ParentId })
                .ToDictionaryAsync(u => u.Id, u => u.ParentId, cancellationToken);
            if (OrgUnitTree.WouldCreateCycle(unitId, newParentId, parentByUnit))
            {
                return OrgResult<OrgUnitDto>.Invalid(
                    "El padre seleccionado crearia un ciclo: una dependencia no puede ser su propio ancestro.");
            }
        }

        unit.Name = request.Name.Trim();
        unit.Kind = request.Kind;
        unit.ParentId = request.ParentId;
        unit.ResponsibleTenantUserId = request.ResponsibleTenantUserId;
        unit.Description = Normalize(request.Description);
        unit.SortOrder = request.SortOrder;
        unit.Classifier = request.Classifier;
        unit.TenantUserId = request.Classifier == OrgUnitClassifier.Funcionario ? request.TenantUserId : null;
        await _db.SaveChangesAsync(cancellationToken);
        return OrgResult<OrgUnitDto>.Ok(await ToDtoAsync(unit, cancellationToken));
    }

    public async Task<OrgResult<bool>> SetArchivedAsync(
        Guid unitId, bool archived, CancellationToken cancellationToken = default)
    {
        var unit = await _db.OrgUnits.FirstOrDefaultAsync(u => u.Id == unitId, cancellationToken);
        if (unit is null)
        {
            return OrgResult<bool>.NotFound("La dependencia no existe.");
        }
        if (archived && await _db.OrgUnits.AnyAsync(u => u.ParentId == unitId && !u.IsArchived, cancellationToken))
        {
            return OrgResult<bool>.Invalid("La dependencia tiene sub-dependencias activas; archivalas primero.");
        }
        unit.IsArchived = archived;
        await _db.SaveChangesAsync(cancellationToken);
        return OrgResult<bool>.Ok(true);
    }

    // ---- Miembros ----

    public async Task<IReadOnlyList<OrgUnitMemberDto>> ListMembersAsync(
        Guid unitId, CancellationToken cancellationToken = default)
    {
        return await _db.OrgUnitMembers.AsNoTracking()
            .Where(m => m.OrgUnitId == unitId)
            .Join(_db.TenantUsers, m => m.TenantUserId, tu => tu.Id, (m, tu) => new { m, tu })
            .GroupJoin(_db.PlatformUsers, x => x.tu.PlatformUserId, pu => pu.Id, (x, pus) => new { x.m, x.tu, pus })
            .SelectMany(x => x.pus.DefaultIfEmpty(), (x, pu) => new
            {
                x.m.Id,
                x.m.OrgUnitId,
                x.m.TenantUserId,
                x.tu.Email,
                DisplayName = pu != null ? pu.DisplayName : null,
                x.m.Role,
                x.m.IsResponsible
            })
            .OrderByDescending(x => x.IsResponsible).ThenBy(x => x.Email)
            .Select(x => new OrgUnitMemberDto(x.Id, x.OrgUnitId, x.TenantUserId, x.Email, x.DisplayName, x.Role, x.IsResponsible))
            .ToListAsync(cancellationToken);
    }

    public async Task<OrgResult<OrgUnitMemberDto>> AddMemberAsync(
        Guid unitId, Guid tenantUserId, string? role = null, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return OrgResult<OrgUnitMemberDto>.Invalid("No hay tenant activo.");
        }
        var unit = await _db.OrgUnits.AsNoTracking().FirstOrDefaultAsync(u => u.Id == unitId, cancellationToken);
        if (unit is null)
        {
            return OrgResult<OrgUnitMemberDto>.NotFound("La dependencia no existe.");
        }
        // El usuario debe ser miembro del MISMO tenant (el filtro global lo garantiza).
        var tenantUser = await _db.TenantUsers.AsNoTracking()
            .FirstOrDefaultAsync(tu => tu.Id == tenantUserId, cancellationToken);
        if (tenantUser is null)
        {
            return OrgResult<OrgUnitMemberDto>.NotFound("El usuario no pertenece al tenant.");
        }
        if (await _db.OrgUnitMembers.AnyAsync(
                m => m.OrgUnitId == unitId && m.TenantUserId == tenantUserId, cancellationToken))
        {
            return OrgResult<OrgUnitMemberDto>.Conflict("El usuario ya es miembro de la dependencia.");
        }
        var trimmedRole = Normalize(role);
        if (trimmedRole is { Length: > 100 })
        {
            return OrgResult<OrgUnitMemberDto>.Invalid("El rol no puede superar 100 caracteres.");
        }

        var member = new OrgUnitMember
        {
            TenantId = tenantId,
            OrgUnitId = unitId,
            TenantUserId = tenantUserId,
            Role = trimmedRole
        };
        _db.OrgUnitMembers.Add(member);
        await _db.SaveChangesAsync(cancellationToken);

        var displayName = await _db.PlatformUsers.AsNoTracking()
            .Where(pu => pu.Id == tenantUser.PlatformUserId)
            .Select(pu => pu.DisplayName)
            .FirstOrDefaultAsync(cancellationToken);
        return OrgResult<OrgUnitMemberDto>.Ok(new OrgUnitMemberDto(
            member.Id, unitId, tenantUserId, tenantUser.Email, displayName, member.Role, member.IsResponsible));
    }

    public async Task<OrgResult<bool>> RemoveMemberAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        var member = await _db.OrgUnitMembers.FirstOrDefaultAsync(m => m.Id == memberId, cancellationToken);
        if (member is null)
        {
            return OrgResult<bool>.NotFound("El miembro no existe.");
        }
        // Si el miembro era el jefe/responsable, tambien limpiar el responsable de la unidad.
        if (member.IsResponsible)
        {
            var unit = await _db.OrgUnits.FirstOrDefaultAsync(u => u.Id == member.OrgUnitId, cancellationToken);
            if (unit is not null && unit.ResponsibleTenantUserId == member.TenantUserId)
            {
                unit.ResponsibleTenantUserId = null;
            }
        }
        _db.OrgUnitMembers.Remove(member);
        await _db.SaveChangesAsync(cancellationToken);
        return OrgResult<bool>.Ok(true);
    }

    public async Task<OrgResult<bool>> SetMemberResponsibleAsync(
        Guid memberId, bool isResponsible, CancellationToken cancellationToken = default)
    {
        var member = await _db.OrgUnitMembers.FirstOrDefaultAsync(m => m.Id == memberId, cancellationToken);
        if (member is null)
        {
            return OrgResult<bool>.NotFound("El miembro no existe.");
        }
        var unit = await _db.OrgUnits.FirstOrDefaultAsync(u => u.Id == member.OrgUnitId, cancellationToken);
        if (unit is null)
        {
            return OrgResult<bool>.NotFound("La dependencia no existe.");
        }

        if (isResponsible)
        {
            // A lo sumo un jefe/responsable por unidad: desmarcar a los demas.
            var others = await _db.OrgUnitMembers
                .Where(m => m.OrgUnitId == member.OrgUnitId && m.Id != member.Id && m.IsResponsible)
                .ToListAsync(cancellationToken);
            foreach (var o in others)
            {
                o.IsResponsible = false;
            }
            member.IsResponsible = true;
            // Reconciliar con el responsable de la unidad (fuente del Encargado por defecto).
            unit.ResponsibleTenantUserId = member.TenantUserId;
        }
        else
        {
            member.IsResponsible = false;
            if (unit.ResponsibleTenantUserId == member.TenantUserId)
            {
                unit.ResponsibleTenantUserId = null;
            }
        }
        // Una sola SaveChanges = una transaccion (miembros + unidad atomicos).
        await _db.SaveChangesAsync(cancellationToken);
        return OrgResult<bool>.Ok(true);
    }

    // ---- Internos ----

    private sealed record UnitMeta(
        Guid Id, string Name, OrgUnitKind Kind, Guid? ParentId, Guid? ResponsibleTenantUserId,
        string? ResponsibleName, string? Description, int SortOrder, bool IsArchived, int MemberCount,
        OrgUnitClassifier Classifier, Guid? TenantUserId, string? OccupantName);

    private async Task<List<UnitMeta>> LoadUnitsWithMetaAsync(bool includeArchived, CancellationToken cancellationToken)
    {
        var query = _db.OrgUnits.AsNoTracking();
        if (!includeArchived)
        {
            query = query.Where(u => !u.IsArchived);
        }
        var rows = await query
            .Select(u => new
            {
                u.Id,
                u.Name,
                u.Kind,
                u.ParentId,
                u.ResponsibleTenantUserId,
                u.Description,
                u.SortOrder,
                u.IsArchived,
                u.Classifier,
                u.TenantUserId,
                MemberCount = _db.OrgUnitMembers.Count(m => m.OrgUnitId == u.Id),
                ResponsibleName = _db.TenantUsers
                    .Where(tu => tu.Id == u.ResponsibleTenantUserId)
                    .Join(_db.PlatformUsers, tu => tu.PlatformUserId, pu => pu.Id,
                        (tu, pu) => pu.DisplayName ?? tu.Email)
                    .FirstOrDefault(),
                OccupantName = _db.TenantUsers
                    .Where(tu => tu.Id == u.TenantUserId)
                    .Join(_db.PlatformUsers, tu => tu.PlatformUserId, pu => pu.Id,
                        (tu, pu) => pu.DisplayName ?? tu.Email)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new UnitMeta(
            r.Id, r.Name, r.Kind, r.ParentId, r.ResponsibleTenantUserId, r.ResponsibleName,
            r.Description, r.SortOrder, r.IsArchived, r.MemberCount,
            r.Classifier, r.TenantUserId, r.OccupantName)).ToList();
    }

    private async Task<OrgUnitDto> ToDtoAsync(OrgUnit unit, CancellationToken cancellationToken)
    {
        var parentName = unit.ParentId is Guid parentId
            ? await _db.OrgUnits.AsNoTracking().Where(u => u.Id == parentId).Select(u => u.Name).FirstOrDefaultAsync(cancellationToken)
            : null;
        var responsibleName = unit.ResponsibleTenantUserId is Guid responsibleId
            ? await _db.TenantUsers.AsNoTracking()
                .Where(tu => tu.Id == responsibleId)
                .Join(_db.PlatformUsers, tu => tu.PlatformUserId, pu => pu.Id, (tu, pu) => pu.DisplayName ?? tu.Email)
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        var memberCount = await _db.OrgUnitMembers.CountAsync(m => m.OrgUnitId == unit.Id, cancellationToken);
        var occupantName = unit.TenantUserId is Guid occupantId
            ? await _db.TenantUsers.AsNoTracking()
                .Where(tu => tu.Id == occupantId)
                .Join(_db.PlatformUsers, tu => tu.PlatformUserId, pu => pu.Id, (tu, pu) => pu.DisplayName ?? tu.Email)
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        return new OrgUnitDto(
            unit.Id, unit.Name, unit.Kind, unit.ParentId, parentName,
            unit.ResponsibleTenantUserId, responsibleName, unit.Description,
            unit.SortOrder, unit.IsArchived, memberCount,
            unit.Classifier, unit.TenantUserId, occupantName);
    }

    private async Task<string?> ValidateAsync(SaveOrgUnitRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return "El nombre es obligatorio.";
        }
        if (request.Name.Trim().Length > 150)
        {
            return "El nombre no puede superar 150 caracteres.";
        }
        if (request.Description is { } description && description.Trim().Length > 600)
        {
            return "La descripcion no puede superar 600 caracteres.";
        }
        if (request.ResponsibleTenantUserId is Guid responsibleId
            && !await _db.TenantUsers.AnyAsync(tu => tu.Id == responsibleId, cancellationToken))
        {
            return "El responsable no pertenece al tenant.";
        }

        // Coherencia del clasificador de asignacion por nodo (ADR-0035). Jerarquia suave:
        // un Cargo cuelga de una Dependencia (o raiz); un Funcionario cuelga de un Cargo y
        // exige TenantUserId (el usuario que ocupa el puesto, del mismo tenant).
        var parentClassifier = request.ParentId is Guid parentUnitId
            ? await _db.OrgUnits.AsNoTracking()
                .Where(u => u.Id == parentUnitId)
                .Select(u => (OrgUnitClassifier?)u.Classifier)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        switch (request.Classifier)
        {
            case OrgUnitClassifier.Cargo:
                if (parentClassifier is OrgUnitClassifier.Cargo or OrgUnitClassifier.Funcionario)
                {
                    return "Un Cargo debe colgar de una Dependencia (o ser raiz).";
                }
                break;
            case OrgUnitClassifier.Funcionario:
                if (request.TenantUserId is not Guid occupantId)
                {
                    return "Un Funcionario requiere el usuario del tenant que ocupa el puesto.";
                }
                if (!await _db.TenantUsers.AnyAsync(tu => tu.Id == occupantId, cancellationToken))
                {
                    return "El usuario ocupante no pertenece al tenant.";
                }
                if (parentClassifier is not null and not OrgUnitClassifier.Cargo)
                {
                    return "Un Funcionario debe colgar de un Cargo.";
                }
                break;
        }
        return null;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
