namespace Ecorex.Application.Directorio;

/// <summary>
/// Campos configurables por ficha del Directorio General (modulo 000232). Tenant-scoped
/// (filtro global + estampado en alta); aqui NUNCA se filtra a mano por TenantId. Calcado
/// del patron ya probado de IPipelineService (CUBOT.travels), agrupando por ficha en vez de
/// por etapa.
/// </summary>
public interface ITerceroFieldService
{
    /// <summary>Siembra los campos por defecto de cada ficha (IsSystem=true) si el tenant aun no tiene ninguno.</summary>
    Task EnsureDefaultsAsync(CancellationToken cancellationToken = default);

    /// <summary>Todos los campos del tenant, ordenados por ficha + orden.</summary>
    Task<IReadOnlyList<TerceroFieldDto>> ListFieldsAsync(CancellationToken cancellationToken = default);

    /// <summary>Campos de una ficha (fiscal/comercial/cliente/proveedor/empleado), ordenados.</summary>
    Task<IReadOnlyList<TerceroFieldDto>> ListByFichaAsync(string fichaKey, CancellationToken cancellationToken = default);

    /// <summary>Devuelve null si no hay tenant activo o la ficha es invalida.</summary>
    Task<TerceroFieldDto?> CreateFieldAsync(CreateTerceroFieldRequest request, CancellationToken cancellationToken = default);
    Task<TerceroFieldDto?> UpdateFieldAsync(Guid fieldId, UpdateTerceroFieldRequest request, CancellationToken cancellationToken = default);
    Task ReorderFieldsAsync(ReorderFieldsRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteFieldAsync(Guid fieldId, CancellationToken cancellationToken = default);
}
