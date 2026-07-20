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

    // ---- Proyectos P1: hitos ----

    public async Task<IReadOnlyList<ProjectMilestoneDto>> ListMilestonesAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var milestones = await _db.ProjectMilestones.AsNoTracking()
            .Where(m => m.ProjectId == projectId)
            .OrderBy(m => m.SortOrder)
            .ToListAsync(cancellationToken);
        if (milestones.Count == 0) { return Array.Empty<ProjectMilestoneDto>(); }

        var ids = milestones.Select(m => m.Id).ToList();
        var taskCounts = await _db.TaskItems.AsNoTracking()
            .Where(t => t.MilestoneId != null && ids.Contains(t.MilestoneId.Value) && !t.IsArchived)
            .GroupBy(t => t.MilestoneId!.Value)
            .Select(g => new { MilestoneId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.MilestoneId, x => x.Count, cancellationToken);

        return milestones.Select(m => ToMilestoneDto(m,
            taskCounts.TryGetValue(m.Id, out var c) ? c : 0)).ToList();
    }

    public async Task<TaskCoreResult<ProjectMilestoneDto>> AddMilestoneAsync(Guid projectId, CreateMilestoneRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return TaskCoreResult<ProjectMilestoneDto>.Invalid("No hay tenant activo.");
        }
        var name = (request.Name ?? "").Trim();
        if (name.Length == 0)
        {
            return TaskCoreResult<ProjectMilestoneDto>.Invalid("El nombre del hito es obligatorio.");
        }
        var project = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (project is null)
        {
            return TaskCoreResult<ProjectMilestoneDto>.NotFound("Proyecto no encontrado.");
        }
        var nextOrder = await _db.ProjectMilestones.Where(m => m.ProjectId == projectId)
            .Select(m => (int?)m.SortOrder).MaxAsync(cancellationToken) ?? -1;

        var milestone = new ProjectMilestone
        {
            TenantId = tenantId,
            ProjectId = projectId,
            Name = name,
            Description = Normalize(request.Description),
            DueDate = request.DueDate,
            SortOrder = nextOrder + 1
        };
        _db.ProjectMilestones.Add(milestone);
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<ProjectMilestoneDto>.Ok(ToMilestoneDto(milestone, 0));
    }

    public async Task<TaskCoreResult<ProjectMilestoneDto>> UpdateMilestoneAsync(Guid milestoneId, UpdateMilestoneRequest request, CancellationToken cancellationToken = default)
    {
        var milestone = await _db.ProjectMilestones.FirstOrDefaultAsync(m => m.Id == milestoneId, cancellationToken);
        if (milestone is null)
        {
            return TaskCoreResult<ProjectMilestoneDto>.NotFound("Hito no encontrado.");
        }
        var name = (request.Name ?? milestone.Name).Trim();
        if (name.Length == 0)
        {
            return TaskCoreResult<ProjectMilestoneDto>.Invalid("El nombre del hito es obligatorio.");
        }
        milestone.Name = name;
        milestone.Description = Normalize(request.Description);
        milestone.DueDate = request.DueDate;
        await _db.SaveChangesAsync(cancellationToken);
        var count = await _db.TaskItems.CountAsync(t => t.MilestoneId == milestoneId && !t.IsArchived, cancellationToken);
        return TaskCoreResult<ProjectMilestoneDto>.Ok(ToMilestoneDto(milestone, count));
    }

    public async Task<TaskCoreResult<ProjectMilestoneDto>> SetMilestoneCompletedAsync(Guid milestoneId, bool completed, CancellationToken cancellationToken = default)
    {
        var milestone = await _db.ProjectMilestones.FirstOrDefaultAsync(m => m.Id == milestoneId, cancellationToken);
        if (milestone is null)
        {
            return TaskCoreResult<ProjectMilestoneDto>.NotFound("Hito no encontrado.");
        }
        milestone.IsCompleted = completed;
        milestone.CompletedAt = completed ? DateTimeOffset.UtcNow : null;
        await _db.SaveChangesAsync(cancellationToken);
        var count = await _db.TaskItems.CountAsync(t => t.MilestoneId == milestoneId && !t.IsArchived, cancellationToken);
        return TaskCoreResult<ProjectMilestoneDto>.Ok(ToMilestoneDto(milestone, count));
    }

    public async Task<TaskCoreResult<bool>> RemoveMilestoneAsync(Guid milestoneId, CancellationToken cancellationToken = default)
    {
        var milestone = await _db.ProjectMilestones.FirstOrDefaultAsync(m => m.Id == milestoneId, cancellationToken);
        if (milestone is null)
        {
            return TaskCoreResult<bool>.NotFound("Hito no encontrado.");
        }
        // El FK TaskItem.MilestoneId es Restrict: no se borra un hito con actividades enlazadas.
        if (await _db.TaskItems.AnyAsync(t => t.MilestoneId == milestoneId, cancellationToken))
        {
            return TaskCoreResult<bool>.Invalid("El hito tiene actividades enlazadas; desenlazalas antes de borrarlo.");
        }
        _db.ProjectMilestones.Remove(milestone);
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<bool>.Ok(true);
    }

    private static ProjectMilestoneDto ToMilestoneDto(ProjectMilestone m, int taskCount) => new(
        m.Id, m.ProjectId, m.Name, m.Description, m.DueDate, m.SortOrder, m.IsCompleted, taskCount);

    // ---- Proyectos P2: presupuesto/costos ----

    public async Task<IReadOnlyList<ProjectBudgetItemDto>> ListBudgetItemsAsync(Guid projectId, CancellationToken cancellationToken = default)
        => await _db.ProjectBudgetItems.AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .Select(x => new ProjectBudgetItemDto(x.Id, x.ProjectId, x.Name, x.Category, x.PlannedAmount, x.ActualAmount, x.Notes, x.SortOrder))
            .ToListAsync(cancellationToken);

    public async Task<TaskCoreResult<ProjectBudgetItemDto>> AddBudgetItemAsync(Guid projectId, CreateBudgetItemRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return TaskCoreResult<ProjectBudgetItemDto>.Invalid("No hay tenant activo.");
        }
        var name = (request.Name ?? "").Trim();
        if (name.Length == 0)
        {
            return TaskCoreResult<ProjectBudgetItemDto>.Invalid("El nombre del rubro es obligatorio.");
        }
        if (!await _db.Projects.AnyAsync(p => p.Id == projectId, cancellationToken))
        {
            return TaskCoreResult<ProjectBudgetItemDto>.NotFound("Proyecto no encontrado.");
        }
        var nextOrder = (await _db.ProjectBudgetItems.Where(x => x.ProjectId == projectId)
            .Select(x => (int?)x.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
        var item = new ProjectBudgetItem
        {
            TenantId = tenantId,
            ProjectId = projectId,
            Name = name,
            Category = Normalize(request.Category),
            Notes = Normalize(request.Notes),
            PlannedAmount = request.PlannedAmount,
            ActualAmount = request.ActualAmount,
            SortOrder = nextOrder
        };
        _db.ProjectBudgetItems.Add(item);
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<ProjectBudgetItemDto>.Ok(ToBudgetDto(item));
    }

    public async Task<TaskCoreResult<ProjectBudgetItemDto>> UpdateBudgetItemAsync(Guid itemId, UpdateBudgetItemRequest request, CancellationToken cancellationToken = default)
    {
        var item = await _db.ProjectBudgetItems.FirstOrDefaultAsync(x => x.Id == itemId, cancellationToken);
        if (item is null) { return TaskCoreResult<ProjectBudgetItemDto>.NotFound("Rubro no encontrado."); }
        var name = (request.Name ?? item.Name).Trim();
        if (name.Length == 0) { return TaskCoreResult<ProjectBudgetItemDto>.Invalid("El nombre del rubro es obligatorio."); }
        item.Name = name;
        item.Category = Normalize(request.Category);
        item.Notes = Normalize(request.Notes);
        item.PlannedAmount = request.PlannedAmount;
        item.ActualAmount = request.ActualAmount;
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<ProjectBudgetItemDto>.Ok(ToBudgetDto(item));
    }

    public async Task<TaskCoreResult<bool>> RemoveBudgetItemAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        var item = await _db.ProjectBudgetItems.FirstOrDefaultAsync(x => x.Id == itemId, cancellationToken);
        if (item is null) { return TaskCoreResult<bool>.NotFound("Rubro no encontrado."); }
        _db.ProjectBudgetItems.Remove(item);
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<bool>.Ok(true);
    }

    private static ProjectBudgetItemDto ToBudgetDto(ProjectBudgetItem x) => new(
        x.Id, x.ProjectId, x.Name, x.Category, x.PlannedAmount, x.ActualAmount, x.Notes, x.SortOrder);

    // ---- Proyectos P2: analisis DOFA/SWOT ----

    public async Task<IReadOnlyList<ProjectDofaDto>> ListDofaAsync(Guid projectId, CancellationToken cancellationToken = default)
        => await _db.ProjectDofas.AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .OrderBy(x => x.Quadrant).ThenBy(x => x.SortOrder)
            .Select(x => new ProjectDofaDto(x.Id, x.ProjectId, x.Quadrant, x.Text, x.SortOrder))
            .ToListAsync(cancellationToken);

    public async Task<TaskCoreResult<ProjectDofaDto>> AddDofaAsync(Guid projectId, CreateDofaRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return TaskCoreResult<ProjectDofaDto>.Invalid("No hay tenant activo.");
        }
        var text = (request.Text ?? "").Trim();
        if (text.Length == 0) { return TaskCoreResult<ProjectDofaDto>.Invalid("El texto es obligatorio."); }
        if (!await _db.Projects.AnyAsync(p => p.Id == projectId, cancellationToken))
        {
            return TaskCoreResult<ProjectDofaDto>.NotFound("Proyecto no encontrado.");
        }
        var nextOrder = (await _db.ProjectDofas.Where(x => x.ProjectId == projectId && x.Quadrant == request.Quadrant)
            .Select(x => (int?)x.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
        var entry = new ProjectDofa { TenantId = tenantId, ProjectId = projectId, Quadrant = request.Quadrant, Text = text, SortOrder = nextOrder };
        _db.ProjectDofas.Add(entry);
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<ProjectDofaDto>.Ok(new ProjectDofaDto(entry.Id, entry.ProjectId, entry.Quadrant, entry.Text, entry.SortOrder));
    }

    public async Task<TaskCoreResult<ProjectDofaDto>> UpdateDofaAsync(Guid dofaId, UpdateDofaRequest request, CancellationToken cancellationToken = default)
    {
        var entry = await _db.ProjectDofas.FirstOrDefaultAsync(x => x.Id == dofaId, cancellationToken);
        if (entry is null) { return TaskCoreResult<ProjectDofaDto>.NotFound("Entrada DOFA no encontrada."); }
        var text = (request.Text ?? entry.Text).Trim();
        if (text.Length == 0) { return TaskCoreResult<ProjectDofaDto>.Invalid("El texto es obligatorio."); }
        entry.Text = text;
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<ProjectDofaDto>.Ok(new ProjectDofaDto(entry.Id, entry.ProjectId, entry.Quadrant, entry.Text, entry.SortOrder));
    }

    public async Task<TaskCoreResult<bool>> RemoveDofaAsync(Guid dofaId, CancellationToken cancellationToken = default)
    {
        var entry = await _db.ProjectDofas.FirstOrDefaultAsync(x => x.Id == dofaId, cancellationToken);
        if (entry is null) { return TaskCoreResult<bool>.NotFound("Entrada DOFA no encontrada."); }
        _db.ProjectDofas.Remove(entry);
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<bool>.Ok(true);
    }

    private static ProjectDto ToDto(Project p, int taskCount, int memberCount) => new(
        p.Id, p.Code, p.Name, p.Description, p.Status, p.StartDate, p.EndDate,
        p.OwnerTenantUserId, p.IsArchived, p.Version, taskCount, memberCount);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
