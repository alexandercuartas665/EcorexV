namespace CubotTravels.Application.Tenancy;

/// <summary>Leads del embudo del tenant activo (modulo 2.2). Tenant-scoped.</summary>
public interface ILeadService
{
    Task<IReadOnlyList<LeadDto>> ListAsync(Guid? stageId = null, CancellationToken cancellationToken = default);
    Task<LeadDetailDto?> GetAsync(Guid leadId, CancellationToken cancellationToken = default);

    /// <summary>Devuelve null si no hay tenant activo o no existen etapas / la etapa indicada no es valida.</summary>
    Task<LeadDto?> CreateAsync(CreateLeadRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Mueve el lead a otra etapa; cierra (Won/Lost) si la etapa es terminal. Null si lead o etapa invalidos.</summary>
    Task<LeadDto?> MoveAsync(Guid leadId, MoveLeadRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<LeadDto?> AssignAsync(Guid leadId, Guid? tenantUserId, Guid actorUserId, CancellationToken cancellationToken = default);
}
