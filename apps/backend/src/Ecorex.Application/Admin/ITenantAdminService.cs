using Ecorex.Domain.Enums;

namespace Ecorex.Application.Admin;

public interface ITenantAdminService
{
    Task<TenantDetail> CreateAsync(CreateTenantRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TenantListItem>> ListAsync(TenantStatus? status = null, string? search = null, CancellationToken cancellationToken = default);
    Task<TenantDetail?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TenantDetail?> ChangeStatusAsync(Guid id, ChangeTenantStatusRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Actualiza el perfil de la agencia (nombre, datos fiscales, contacto, logo). Devuelve null si no existe.</summary>
    Task<TenantDetail?> UpdateProfileAsync(Guid id, UpdateTenantProfileRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Lista los usuarios de un tenant para la ficha de empresa del operador de plataforma
    /// (modulo 000072, solo lectura). Acceso cross-tenant acotado a este tenant (ADR-0026).</summary>
    Task<IReadOnlyList<TenantUserListItem>> ListUsersAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
