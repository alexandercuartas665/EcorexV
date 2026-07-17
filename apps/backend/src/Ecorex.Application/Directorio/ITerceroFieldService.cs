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

    /// <summary>Devuelve null si no hay tenant activo, la ficha es invalida o la formula no valida.</summary>
    Task<TerceroFieldDto?> CreateFieldAsync(CreateTerceroFieldRequest request, CancellationToken cancellationToken = default);
    Task<TerceroFieldDto?> UpdateFieldAsync(Guid fieldId, UpdateTerceroFieldRequest request, CancellationToken cancellationToken = default);
    Task ReorderFieldsAsync(ReorderFieldsRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteFieldAsync(Guid fieldId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mueve un campo a otra ficha (ej. de fiscal a comercial), donde aterriza al final. La clave no
    /// cambia, asi que los valores ya capturados y las formulas que lo referencian siguen validos.
    /// Devuelve null si se movio, o el motivo si no se pudo (la ficha destino ya tiene esa clave).
    /// </summary>
    Task<string?> MoveFieldToFichaAsync(Guid fieldId, string targetFichaKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Valida una formula contra los campos del tenant (ADR-0029): sintaxis, que lo referenciado
    /// exista y sea numerico, y que no cierre un ciclo. Devuelve null si esta bien, o el motivo en
    /// lenguaje llano. La UI lo llama mientras se escribe; Create/Update lo repiten por si acaso.
    /// </summary>
    /// <param name="fieldId">Campo que se esta editando (null si es nuevo), para excluirlo del ciclo.</param>
    /// <param name="fieldKey">Clave que tendra el campo, para detectar autorreferencia.</param>
    Task<string?> ValidateFormulaAsync(string? formula, Guid? fieldId, string? fieldKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Valores de los campos calculados de un tercero, ya listos para guardar/mostrar. Se resuelven
    /// contra TODAS las fichas: en un tercero los valores viven juntos en FichasJson, asi que un
    /// calculado comercial puede usar un dato fiscal.
    /// </summary>
    Task<IReadOnlyDictionary<string, string?>> ComputeCalculatedAsync(
        IReadOnlyDictionary<string, string?> values, CancellationToken cancellationToken = default);
}
