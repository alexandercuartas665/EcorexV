namespace Ecorex.Application.Common;

/// <summary>
/// Expone el tenant y usuario del contexto de ejecucion actual (request HTTP, worker, etc.).
/// Lo resuelve la capa de presentacion desde el claim tenant_id del JWT. En procesos sin
/// tenant (seed, workers globales) TenantId puede ser null y el filtro de consulta es fail-closed.
/// </summary>
public interface ITenantContext
{
    Guid? TenantId { get; }
    Guid? UserId { get; }
}
