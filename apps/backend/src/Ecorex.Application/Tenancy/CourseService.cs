using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

public sealed record CourseRegistrationDto(Guid Id, string PersonName, string? Phone, bool IsPaid, DateTimeOffset RegisteredAt);

public sealed record CourseDto(Guid Id, string Name, string? Description, DateOnly Date, TimeOnly? StartTime,
    int Capacity, decimal? Price, bool IsActive, int RegisteredCount, int PaidCount, bool IsArchived = false)
{
    /// <summary>Cupos libres (cupo - inscritos).</summary>
    public int SpotsLeft => Math.Max(0, Capacity - RegisteredCount);
}

public sealed record CourseDetailDto(CourseDto Course, IReadOnlyList<CourseRegistrationDto> Registrations);

public sealed record SaveCourseRequest(string Name, string? Description, DateOnly Date, TimeOnly? StartTime, int Capacity, decimal? Price);

/// <summary>Cursos eventuales del salon + sus inscripciones. Tenant-scoped CRUD.</summary>
public interface ICourseService
{
    /// <summary>Lista cursos. archived=false (defecto) excluye archivados; true devuelve solo archivados; null devuelve todos.</summary>
    Task<IReadOnlyList<CourseDto>> ListAsync(bool includeInactive = true, bool upcomingOnly = false, DateOnly? today = null, bool? archived = false, CancellationToken cancellationToken = default);
    Task<CourseDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CourseDto?> CreateAsync(SaveCourseRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<CourseDto?> UpdateAsync(Guid id, SaveCourseRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> SetActiveAsync(Guid id, bool isActive, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> SetArchivedAsync(Guid id, bool isArchived, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);
    /// <summary>Inscribe a una persona en el curso. Devuelve null si el curso no existe o no hay cupo.</summary>
    Task<CourseRegistrationDto?> AddRegistrationAsync(Guid courseId, string personName, string? phone, bool isPaid, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> SetRegistrationPaidAsync(Guid registrationId, bool isPaid, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> RemoveRegistrationAsync(Guid registrationId, Guid actorUserId, CancellationToken cancellationToken = default);
}

public sealed class CourseService : ICourseService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _clock;

    public CourseService(IApplicationDbContext db, ITenantContext tenantContext, IAuditWriter audit, TimeProvider clock)
    {
        _db = db; _tenantContext = tenantContext; _audit = audit; _clock = clock;
    }

    public async Task<IReadOnlyList<CourseDto>> ListAsync(bool includeInactive = true, bool upcomingOnly = false, DateOnly? today = null, bool? archived = false, CancellationToken cancellationToken = default)
    {
        var query = _db.Courses.AsNoTracking().Where(c => includeInactive || c.IsActive);
        if (archived is bool ar) { query = query.Where(c => c.IsArchived == ar); }
        if (upcomingOnly)
        {
            var t = today ?? DateOnly.FromDateTime(_clock.GetUtcNow().ToOffset(TimeSpan.FromHours(-5)).Date);
            query = query.Where(c => c.Date >= t);
        }
        var courses = await query.OrderBy(c => c.Date).ThenBy(c => c.Name).ToListAsync(cancellationToken);
        var ids = courses.Select(c => c.Id).ToList();
        var regs = ids.Count == 0
            ? new List<CourseRegistration>()
            : await _db.CourseRegistrations.AsNoTracking().Where(r => ids.Contains(r.CourseId)).ToListAsync(cancellationToken);
        return courses.Select(c =>
        {
            var rs = regs.Where(r => r.CourseId == c.Id).ToList();
            return new CourseDto(c.Id, c.Name, c.Description, c.Date, c.StartTime, c.Capacity, c.Price, c.IsActive, rs.Count, rs.Count(r => r.IsPaid), c.IsArchived);
        }).ToList();
    }

    public async Task<CourseDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var c = await _db.Courses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (c is null) { return null; }
        var regs = await _db.CourseRegistrations.AsNoTracking()
            .Where(r => r.CourseId == id)
            .OrderByDescending(r => r.RegisteredAt)
            .ToListAsync(cancellationToken);
        var dto = new CourseDto(c.Id, c.Name, c.Description, c.Date, c.StartTime, c.Capacity, c.Price, c.IsActive, regs.Count, regs.Count(r => r.IsPaid));
        var regDtos = regs.Select(r => new CourseRegistrationDto(r.Id, r.PersonName, r.Phone, r.IsPaid, r.RegisteredAt)).ToList();
        return new CourseDetailDto(dto, regDtos);
    }

    public async Task<CourseDto?> CreateAsync(SaveCourseRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0) { return null; }
        var course = new Course
        {
            TenantId = tenantId,
            Name = name,
            Description = Clean(request.Description),
            Date = request.Date,
            StartTime = request.StartTime,
            Capacity = Math.Max(0, request.Capacity),
            Price = request.Price,
            IsActive = true
        };
        _db.Courses.Add(course);
        _audit.Write(actorUserId, "course.create", nameof(Course), course.Id, null, new { course.Name, course.Date }, tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return new CourseDto(course.Id, course.Name, course.Description, course.Date, course.StartTime, course.Capacity, course.Price, course.IsActive, 0, 0);
    }

    public async Task<CourseDto?> UpdateAsync(Guid id, SaveCourseRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var course = await _db.Courses.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (course is null) { return null; }
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0) { return null; }
        course.Name = name;
        course.Description = Clean(request.Description);
        course.Date = request.Date;
        course.StartTime = request.StartTime;
        course.Capacity = Math.Max(0, request.Capacity);
        course.Price = request.Price;
        _audit.Write(actorUserId, "course.update", nameof(Course), course.Id, null, new { course.Name, course.Date }, course.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        var regs = await _db.CourseRegistrations.AsNoTracking().Where(r => r.CourseId == id).ToListAsync(cancellationToken);
        return new CourseDto(course.Id, course.Name, course.Description, course.Date, course.StartTime, course.Capacity, course.Price, course.IsActive, regs.Count, regs.Count(r => r.IsPaid));
    }

    public async Task<bool> SetActiveAsync(Guid id, bool isActive, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var course = await _db.Courses.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (course is null) { return false; }
        course.IsActive = isActive;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SetArchivedAsync(Guid id, bool isArchived, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var course = await _db.Courses.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (course is null) { return false; }
        course.IsArchived = isArchived;
        // Archivar = retirar (no se ofrece por el agente); desarchivar lo vuelve a activar.
        course.IsActive = !isArchived;
        _audit.Write(actorUserId, isArchived ? "course.archive" : "course.unarchive", nameof(Course), course.Id, null, new { course.Name }, course.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var course = await _db.Courses.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (course is null) { return false; }
        var regs = await _db.CourseRegistrations.Where(r => r.CourseId == id).ToListAsync(cancellationToken);
        _db.CourseRegistrations.RemoveRange(regs);
        _db.Courses.Remove(course);
        _audit.Write(actorUserId, "course.delete", nameof(Course), course.Id, new { course.Name }, null, course.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<CourseRegistrationDto?> AddRegistrationAsync(Guid courseId, string personName, string? phone, bool isPaid, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var course = await _db.Courses.FirstOrDefaultAsync(c => c.Id == courseId, cancellationToken);
        if (course is null) { return null; }
        var name = (personName ?? string.Empty).Trim();
        if (name.Length == 0) { return null; }
        // Respeta el cupo: no inscribe si ya esta lleno.
        var count = await _db.CourseRegistrations.CountAsync(r => r.CourseId == courseId, cancellationToken);
        if (count >= course.Capacity) { return null; }
        var reg = new CourseRegistration
        {
            TenantId = tenantId,
            CourseId = courseId,
            PersonName = name,
            Phone = Clean(phone),
            IsPaid = isPaid,
            RegisteredAt = _clock.GetUtcNow()
        };
        _db.CourseRegistrations.Add(reg);
        await _db.SaveChangesAsync(cancellationToken);
        return new CourseRegistrationDto(reg.Id, reg.PersonName, reg.Phone, reg.IsPaid, reg.RegisteredAt);
    }

    public async Task<bool> SetRegistrationPaidAsync(Guid registrationId, bool isPaid, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var reg = await _db.CourseRegistrations.FirstOrDefaultAsync(r => r.Id == registrationId, cancellationToken);
        if (reg is null) { return false; }
        reg.IsPaid = isPaid;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RemoveRegistrationAsync(Guid registrationId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var reg = await _db.CourseRegistrations.FirstOrDefaultAsync(r => r.Id == registrationId, cancellationToken);
        if (reg is null) { return false; }
        _db.CourseRegistrations.Remove(reg);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
