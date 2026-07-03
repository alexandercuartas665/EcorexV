using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

public sealed class ProjectService : IProjectService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;

    public ProjectService(IApplicationDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    public async Task<IReadOnlyList<ProjectDto>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var projects = await _db.Projects.AsNoTracking()
            .Where(p => includeArchived || !p.IsArchived)
            .OrderBy(p => p.Code)
            .ToListAsync(cancellationToken);
        if (projects.Count == 0) { return Array.Empty<ProjectDto>(); }

        var ids = projects.Select(p => p.Id).ToList();
        var taskCounts = await _db.TaskItems.AsNoTracking()
            .Where(t => t.ProjectId != null && ids.Contains(t.ProjectId.Value) && !t.IsArchived)
            .GroupBy(t => t.ProjectId!.Value)
            .Select(g => new { ProjectId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ProjectId, x => x.Count, cancellationToken);
        var memberCounts = await _db.ProjectMembers.AsNoTracking()
            .Where(m => ids.Contains(m.ProjectId))
            .GroupBy(m => m.ProjectId)
            .Select(g => new { ProjectId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ProjectId, x => x.Count, cancellationToken);

        return projects.Select(p => ToDto(p,
            taskCounts.TryGetValue(p.Id, out var t) ? t : 0,
            memberCounts.TryGetValue(p.Id, out var m) ? m : 0)).ToList();
    }

    public async Task<ProjectDto?> GetAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (project is null) { return null; }
        var taskCount = await _db.TaskItems.CountAsync(t => t.ProjectId == projectId && !t.IsArchived, cancellationToken);
        var memberCount = await _db.ProjectMembers.CountAsync(m => m.ProjectId == projectId, cancellationToken);
        return ToDto(project, taskCount, memberCount);
    }

    public async Task<TaskCoreResult<ProjectDto>> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return TaskCoreResult<ProjectDto>.Invalid("No hay tenant activo.");
        }
        var code = (request.Code ?? "").Trim();
        var name = (request.Name ?? "").Trim();
        if (code.Length == 0 || name.Length == 0)
        {
            return TaskCoreResult<ProjectDto>.Invalid("Codigo y nombre son obligatorios.");
        }
        if (request.StartDate is not null && request.EndDate is not null && request.EndDate < request.StartDate)
        {
            return TaskCoreResult<ProjectDto>.Invalid("La fecha de fin no puede ser anterior al inicio.");
        }
        // El indice unico (TenantId, Code) respalda esta validacion amigable.
        if (await _db.Projects.AnyAsync(p => p.Code == code, cancellationToken))
        {
            return TaskCoreResult<ProjectDto>.Invalid($"Ya existe un proyecto con codigo '{code}'.");
        }
        // El owner debe ser un usuario del tenant activo (el filtro global lo garantiza).
        if (!await _db.TenantUsers.AnyAsync(u => u.Id == request.OwnerTenantUserId, cancellationToken))
        {
            return TaskCoreResult<ProjectDto>.Invalid("El owner indicado no pertenece al tenant.");
        }

        var project = new Project
        {
            TenantId = tenantId,
            Code = code,
            Name = name,
            Description = Normalize(request.Description),
            Status = request.Status,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            OwnerTenantUserId = request.OwnerTenantUserId
        };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<ProjectDto>.Ok(ToDto(project, 0, 0));
    }

    public async Task<TaskCoreResult<ProjectDto>> UpdateAsync(Guid projectId, UpdateProjectRequest request, CancellationToken cancellationToken = default)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (project is null)
        {
            return TaskCoreResult<ProjectDto>.NotFound("Proyecto no encontrado.");
        }
        // Token de concurrencia optimista (ADR-0013): un token viejo es conflicto tipado,
        // no excepcion. El ConcurrencyToken de EF cubre ademas la carrera leer-guardar.
        if (project.Version != request.Version)
        {
            return TaskCoreResult<ProjectDto>.Conflict("El proyecto fue modificado por otro usuario. Recarga e intenta de nuevo.");
        }
        var name = (request.Name ?? project.Name).Trim();
        if (name.Length == 0)
        {
            return TaskCoreResult<ProjectDto>.Invalid("El nombre es obligatorio.");
        }
        if (request.StartDate is not null && request.EndDate is not null && request.EndDate < request.StartDate)
        {
            return TaskCoreResult<ProjectDto>.Invalid("La fecha de fin no puede ser anterior al inicio.");
        }
        if (!await _db.TenantUsers.AnyAsync(u => u.Id == request.OwnerTenantUserId, cancellationToken))
        {
            return TaskCoreResult<ProjectDto>.Invalid("El owner indicado no pertenece al tenant.");
        }

        project.Name = name;
        project.Description = Normalize(request.Description);
        project.Status = request.Status;
        project.StartDate = request.StartDate;
        project.EndDate = request.EndDate;
        project.OwnerTenantUserId = request.OwnerTenantUserId;
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TaskCoreResult<ProjectDto>.Conflict("El proyecto fue modificado por otro usuario. Recarga e intenta de nuevo.");
        }
        var taskCount = await _db.TaskItems.CountAsync(t => t.ProjectId == projectId && !t.IsArchived, cancellationToken);
        var memberCount = await _db.ProjectMembers.CountAsync(m => m.ProjectId == projectId, cancellationToken);
        return TaskCoreResult<ProjectDto>.Ok(ToDto(project, taskCount, memberCount));
    }

    public async Task<TaskCoreResult<bool>> SetArchivedAsync(Guid projectId, bool archived, CancellationToken cancellationToken = default)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (project is null)
        {
            return TaskCoreResult<bool>.NotFound("Proyecto no encontrado.");
        }
        if (project.IsArchived == archived)
        {
            return TaskCoreResult<bool>.Ok(false);
        }
        project.IsArchived = archived;
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TaskCoreResult<bool>.Conflict("El proyecto fue modificado por otro usuario. Recarga e intenta de nuevo.");
        }
        return TaskCoreResult<bool>.Ok(true);
    }

    public async Task<IReadOnlyList<ProjectMemberDto>> ListMembersAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var ownerId = await _db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => (Guid?)p.OwnerTenantUserId)
            .FirstOrDefaultAsync(cancellationToken);
        if (ownerId is null) { return Array.Empty<ProjectMemberDto>(); }

        // OrderBy sobre el campo de la entidad ANTES de proyectar al DTO: ordenar por la
        // propiedad del record (posicional) no es traducible a SQL por EF (falla en PG real).
        return await _db.ProjectMembers.AsNoTracking()
            .Where(m => m.ProjectId == projectId)
            .Join(_db.TenantUsers.AsNoTracking(), m => m.TenantUserId, u => u.Id,
                (m, u) => new { m.TenantUserId, u.Email, m.CanEdit })
            .OrderBy(x => x.Email)
            .Select(x => new ProjectMemberDto(x.TenantUserId, x.Email, x.CanEdit, x.TenantUserId == ownerId))
            .ToListAsync(cancellationToken);
    }

    public async Task<TaskCoreResult<ProjectMemberDto>> AddMemberAsync(Guid projectId, Guid tenantUserId, bool canEdit, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return TaskCoreResult<ProjectMemberDto>.Invalid("No hay tenant activo.");
        }
        var project = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (project is null)
        {
            return TaskCoreResult<ProjectMemberDto>.NotFound("Proyecto no encontrado.");
        }
        var user = await _db.TenantUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == tenantUserId, cancellationToken);
        if (user is null)
        {
            return TaskCoreResult<ProjectMemberDto>.Invalid("El usuario indicado no pertenece al tenant.");
        }
        if (await _db.ProjectMembers.AnyAsync(m => m.ProjectId == projectId && m.TenantUserId == tenantUserId, cancellationToken))
        {
            return TaskCoreResult<ProjectMemberDto>.Invalid("El usuario ya es miembro del proyecto.");
        }

        var member = new ProjectMember
        {
            TenantId = tenantId,
            ProjectId = projectId,
            TenantUserId = tenantUserId,
            CanEdit = canEdit
        };
        _db.ProjectMembers.Add(member);
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<ProjectMemberDto>.Ok(
            new ProjectMemberDto(tenantUserId, user.Email, canEdit, project.OwnerTenantUserId == tenantUserId));
    }

    public async Task<TaskCoreResult<ProjectMemberDto>> SetMemberCanEditAsync(Guid projectId, Guid tenantUserId, bool canEdit, CancellationToken cancellationToken = default)
    {
        var member = await _db.ProjectMembers
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.TenantUserId == tenantUserId, cancellationToken);
        if (member is null)
        {
            return TaskCoreResult<ProjectMemberDto>.NotFound("El usuario no es miembro del proyecto.");
        }
        member.CanEdit = canEdit;
        await _db.SaveChangesAsync(cancellationToken);
        var email = await _db.TenantUsers.AsNoTracking()
            .Where(u => u.Id == tenantUserId).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken) ?? "";
        var ownerId = await _db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId).Select(p => p.OwnerTenantUserId).FirstOrDefaultAsync(cancellationToken);
        return TaskCoreResult<ProjectMemberDto>.Ok(new ProjectMemberDto(tenantUserId, email, canEdit, ownerId == tenantUserId));
    }

    public async Task<TaskCoreResult<bool>> RemoveMemberAsync(Guid projectId, Guid tenantUserId, CancellationToken cancellationToken = default)
    {
        var member = await _db.ProjectMembers
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.TenantUserId == tenantUserId, cancellationToken);
        if (member is null)
        {
            return TaskCoreResult<bool>.NotFound("El usuario no es miembro del proyecto.");
        }
        _db.ProjectMembers.Remove(member);
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<bool>.Ok(true);
    }

    public async Task<ProjectAccessDto> CheckAccessAsync(Guid projectId, Guid tenantUserId, CancellationToken cancellationToken = default)
    {
        // Owner: acceso y edicion totales. El filtro global oculta proyectos de otros tenants.
        var isOwner = await _db.Projects.AsNoTracking()
            .AnyAsync(p => p.Id == projectId && p.OwnerTenantUserId == tenantUserId, cancellationToken);
        if (isOwner) { return new ProjectAccessDto(true, true); }

        var member = await _db.ProjectMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.TenantUserId == tenantUserId, cancellationToken);
        return member is null
            ? new ProjectAccessDto(false, false)
            : new ProjectAccessDto(true, member.CanEdit);
    }

    private static ProjectDto ToDto(Project p, int taskCount, int memberCount) => new(
        p.Id, p.Code, p.Name, p.Description, p.Status, p.StartDate, p.EndDate,
        p.OwnerTenantUserId, p.IsArchived, p.Version, taskCount, memberCount);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
