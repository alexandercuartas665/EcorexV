using System.Security.Cryptography;
using Ecorex.Application.Common;
using Ecorex.Application.Common.Auth;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

public sealed class AdvisorService : IAdvisorService
{
    private const int InviteValidDays = 7;

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly TimeProvider _timeProvider;
    private readonly IAuditWriter _audit;

    public AdvisorService(
        IApplicationDbContext db,
        ITenantContext tenantContext,
        IPasswordHasher passwordHasher,
        TimeProvider timeProvider,
        IAuditWriter audit)
    {
        _db = db;
        _tenantContext = tenantContext;
        _passwordHasher = passwordHasher;
        _timeProvider = timeProvider;
        _audit = audit;
    }

    public async Task<IReadOnlyList<AdvisorDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        // Filtro global: solo los miembros del tenant activo.
        return await _db.TenantUsers
            .AsNoTracking()
            .OrderBy(u => u.Email)
            .Join(_db.PlatformUsers.AsNoTracking(),
                tu => tu.PlatformUserId, pu => pu.Id,
                (tu, pu) => new AdvisorDto(
                    tu.Id, tu.PlatformUserId, tu.Email, pu.DisplayName, pu.AvatarUrl,
                    tu.TenantRole, tu.Status, tu.LeadVisibility,
                    tu.InvitationToken != null, tu.InvitationToken, tu.InvitationExpiresAt,
                    tu.DocumentCode, tu.Phone))
            .ToListAsync(cancellationToken);
    }

    public async Task<AdvisorDto?> CreateAsync(CreateAdvisorRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return null;
        }

        var email = request.Email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var platformUser = await _db.PlatformUsers.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
        if (platformUser is null)
        {
            platformUser = new PlatformUser
            {
                Email = email,
                DisplayName = request.DisplayName?.Trim(),
                EmailVerified = false,
                AuthProvider = "local",
                Status = PlatformUserStatus.Invited
            };
            _db.PlatformUsers.Add(platformUser);
        }

        // Filtro global: detecta si ya es miembro del tenant activo.
        var alreadyMember = await _db.TenantUsers.AnyAsync(tu => tu.PlatformUserId == platformUser.Id, cancellationToken);
        if (alreadyMember)
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow();
        var tenantUser = new TenantUser
        {
            TenantId = tenantId,
            PlatformUserId = platformUser.Id,
            Email = email,
            TenantRole = request.Role,
            Status = PlatformUserStatus.Invited,
            LeadVisibility = request.LeadVisibility,
            DocumentCode = string.IsNullOrWhiteSpace(request.DocumentCode) ? null : request.DocumentCode.Trim(),
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            InvitationToken = NewToken(),
            InvitationExpiresAt = now.AddDays(InviteValidDays)
        };
        _db.TenantUsers.Add(tenantUser);

        _audit.Write(actorUserId, "advisor.invite", nameof(TenantUser), tenantUser.Id,
            previousValue: null,
            newValue: new { email, tenantUser.TenantRole, tenantUser.LeadVisibility },
            tenantId: tenantId);

        await _db.SaveChangesAsync(cancellationToken);
        return Map(tenantUser, platformUser);
    }

    public async Task<AdvisorDto?> UpdateAsync(Guid tenantUserId, UpdateAdvisorRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var tenantUser = await _db.TenantUsers.FirstOrDefaultAsync(tu => tu.Id == tenantUserId, cancellationToken);
        if (tenantUser is null)
        {
            return null;
        }
        var platformUser = await _db.PlatformUsers.FirstOrDefaultAsync(p => p.Id == tenantUser.PlatformUserId, cancellationToken);

        tenantUser.TenantRole = request.Role;
        tenantUser.LeadVisibility = request.LeadVisibility;
        tenantUser.DocumentCode = string.IsNullOrWhiteSpace(request.DocumentCode) ? null : request.DocumentCode.Trim();
        tenantUser.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        // Cambio de correo (= correo de LOGIN e identidad global del asesor). Guarda de unicidad:
        // se rechaza si otro miembro del tenant o cualquier otra identidad de plataforma ya lo usa.
        var newEmail = request.Email?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(newEmail) && !string.Equals(newEmail, tenantUser.Email, StringComparison.OrdinalIgnoreCase))
        {
            var tenantConflict = await _db.TenantUsers.AnyAsync(tu => tu.Id != tenantUser.Id && tu.Email == newEmail, cancellationToken);
            var platformConflict = await _db.PlatformUsers.AnyAsync(p => p.Id != tenantUser.PlatformUserId && p.Email == newEmail, cancellationToken);
            if (tenantConflict || platformConflict)
            {
                return null; // correo en uso -> la UI muestra el error
            }
            tenantUser.Email = newEmail;
            if (platformUser is not null) { platformUser.Email = newEmail; }
        }
        if (platformUser is not null && !string.IsNullOrWhiteSpace(request.DisplayName))
        {
            platformUser.DisplayName = request.DisplayName.Trim();
        }

        _audit.Write(actorUserId, "advisor.update", nameof(TenantUser), tenantUser.Id,
            previousValue: null,
            newValue: new { tenantUser.TenantRole, tenantUser.LeadVisibility },
            tenantId: tenantUser.TenantId);

        await _db.SaveChangesAsync(cancellationToken);
        return Map(tenantUser, platformUser);
    }

    public async Task<AdvisorDto?> SetStatusAsync(Guid tenantUserId, PlatformUserStatus status, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var tenantUser = await _db.TenantUsers.FirstOrDefaultAsync(tu => tu.Id == tenantUserId, cancellationToken);
        if (tenantUser is null)
        {
            return null;
        }
        var platformUser = await _db.PlatformUsers.FirstOrDefaultAsync(p => p.Id == tenantUser.PlatformUserId, cancellationToken);

        tenantUser.Status = status;
        _audit.Write(actorUserId, "advisor.set-status", nameof(TenantUser), tenantUser.Id,
            previousValue: null, newValue: new { status }, tenantId: tenantUser.TenantId);

        await _db.SaveChangesAsync(cancellationToken);
        return Map(tenantUser, platformUser);
    }

    public async Task<AdvisorDto?> ResendInviteAsync(Guid tenantUserId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var tenantUser = await _db.TenantUsers.FirstOrDefaultAsync(tu => tu.Id == tenantUserId, cancellationToken);
        if (tenantUser is null)
        {
            return null;
        }
        var platformUser = await _db.PlatformUsers.FirstOrDefaultAsync(p => p.Id == tenantUser.PlatformUserId, cancellationToken);

        var now = _timeProvider.GetUtcNow();
        tenantUser.InvitationToken = NewToken();
        tenantUser.InvitationExpiresAt = now.AddDays(InviteValidDays);
        tenantUser.Status = PlatformUserStatus.Invited;

        _audit.Write(actorUserId, "advisor.resend-invite", nameof(TenantUser), tenantUser.Id,
            previousValue: null, newValue: new { tenantUser.InvitationExpiresAt }, tenantId: tenantUser.TenantId);

        await _db.SaveChangesAsync(cancellationToken);
        return Map(tenantUser, platformUser);
    }

    public async Task<AdvisorInvitationInfo?> GetInvitationAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var tenantUser = await _db.TenantUsers.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(tu => tu.InvitationToken == token, cancellationToken);
        if (tenantUser is null)
        {
            return null;
        }

        var tenantName = await _db.Tenants.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.Id == tenantUser.TenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? "tu agencia";

        var valid = tenantUser.InvitationExpiresAt is null || tenantUser.InvitationExpiresAt > _timeProvider.GetUtcNow();
        return new AdvisorInvitationInfo(valid, tenantUser.Email, tenantName);
    }

    public async Task<string?> AcceptInvitationAsync(AcceptInvitationRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.Password))
        {
            return null;
        }

        var tenantUser = await _db.TenantUsers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(tu => tu.InvitationToken == request.Token, cancellationToken);
        if (tenantUser is null)
        {
            return null;
        }
        if (tenantUser.InvitationExpiresAt is { } exp && exp <= _timeProvider.GetUtcNow())
        {
            return null; // invitacion expirada
        }

        var platformUser = await _db.PlatformUsers
            .FirstOrDefaultAsync(p => p.Id == tenantUser.PlatformUserId, cancellationToken);
        if (platformUser is null)
        {
            return null;
        }

        platformUser.PasswordHash = _passwordHasher.Hash(request.Password);
        platformUser.Status = PlatformUserStatus.Active;
        platformUser.EmailVerified = true;
        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            platformUser.DisplayName = request.DisplayName.Trim();
        }
        if (!string.IsNullOrWhiteSpace(request.AvatarUrl))
        {
            platformUser.AvatarUrl = request.AvatarUrl;
        }

        tenantUser.Status = PlatformUserStatus.Active;
        tenantUser.InvitationToken = null;
        tenantUser.InvitationExpiresAt = null;

        _audit.Write(platformUser.Id, "advisor.accept-invite", nameof(TenantUser), tenantUser.Id,
            previousValue: null, newValue: new { tenantUser.Email }, tenantId: tenantUser.TenantId);

        await _db.SaveChangesAsync(cancellationToken);
        return platformUser.Email;
    }

    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    private static AdvisorDto Map(TenantUser tu, PlatformUser? pu) =>
        new(tu.Id, tu.PlatformUserId, tu.Email, pu?.DisplayName, pu?.AvatarUrl,
            tu.TenantRole, tu.Status, tu.LeadVisibility,
            tu.InvitationToken != null, tu.InvitationToken, tu.InvitationExpiresAt,
            tu.DocumentCode, tu.Phone);
}
