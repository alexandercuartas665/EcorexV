namespace Ecorex.Application.Confidentiality;

/// <summary>
/// Politica de confidencialidad por tenant + link PERMANENTE /pc/{token} con constancia de
/// aceptacion (ADR-0160). Hay UNA politica por tenant. Las operaciones de admin son tenant-scoped;
/// la resolucion por token es el UNICO punto cross-tenant (ver impl). El link NO vence.
/// </summary>
public interface IConfidentialityPolicyService
{
    // ---- Admin (tenant-scoped) ----

    /// <summary>Politica del tenant activo, o null si aun no se ha creado.</summary>
    Task<ConfidentialityPolicyDto?> GetForTenantAsync(CancellationToken cancellationToken = default);

    /// <summary>Crea (primera vez, generando el token del link) o actualiza la politica del tenant.</summary>
    Task<ConfidentialityPolicyDto> SaveAsync(ConfidentialityPolicySaveRequest request, CancellationToken cancellationToken = default);

    /// <summary>Regenera el token del link permanente (invalida el anterior). Devuelve la politica.</summary>
    Task<ConfidentialityPolicyDto?> RegenerateLinkAsync(CancellationToken cancellationToken = default);

    /// <summary>Constancias de aceptacion del tenant, mas reciente primero.</summary>
    Task<IReadOnlyList<ConfidentialityAcceptanceDto>> ListAcceptancesAsync(CancellationToken cancellationToken = default);

    // ---- Publico (cross-tenant por token) ----

    /// <summary>Resuelve el token del visor anonimo (IgnoreQueryFilters); null si no aplica.</summary>
    Task<ConfidentialityPolicyPublicDto?> ResolveByTokenAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Registra una constancia. El llamador fija el ambient del tenant (AmbientTenantContext.Begin).</summary>
    Task<ConfidentialityAcceptStatus> AcceptAsync(string token, ConfidentialityAcceptRequest request, CancellationToken cancellationToken = default);
}
