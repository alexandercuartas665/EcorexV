namespace Ecorex.Application.Tenancy;

/// <summary>
/// CRUD de los campos configurables de las tareas, agrupados POR TABLERO (ADR-0065). Cada tenant
/// define, sin tocar codigo, que campos captura en el detalle de las tareas de UN tablero; esos
/// campos SOLO existen en ese tablero. Calcado de IItemFieldService (que agrupa por tipo de item).
/// Tenant-scoped por el filtro global. La clave del campo es unica por (tenant, tablero).
/// </summary>
public interface ITaskFieldService
{
    /// <summary>Todos los campos del tenant (todos los tableros), ordenados por tablero y orden.</summary>
    Task<IReadOnlyList<TaskFieldDto>> ListAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Campos de un tablero, en orden. Vacio si el tablero no tiene campos.</summary>
    Task<IReadOnlyList<TaskFieldDto>> ListByBoardAsync(Guid boardId, CancellationToken cancellationToken = default);

    /// <summary>Devuelve null si el tablero no existe, falta la etiqueta o la formula no valida.</summary>
    Task<TaskFieldDto?> CreateFieldAsync(CreateTaskFieldRequest request, CancellationToken cancellationToken = default);
    Task<TaskFieldDto?> UpdateFieldAsync(Guid fieldId, UpdateTaskFieldRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteFieldAsync(Guid fieldId, CancellationToken cancellationToken = default);

    /// <summary>Nuevo orden de los campos de un tablero: lista de ids en el orden deseado.</summary>
    Task ReorderFieldsAsync(IReadOnlyList<Guid> orderedFieldIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mueve un campo a otro tablero, donde aterriza al final. Devuelve null si se movio, o el
    /// motivo si no se pudo (el tablero destino ya tiene esa clave, o una formula del origen la usa).
    /// </summary>
    Task<string?> MoveFieldToBoardAsync(Guid fieldId, Guid targetBoardId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Valida una formula contra los campos del MISMO tablero (ADR-0029): una tarea vive en un solo
    /// tablero, asi que referenciar campos de otro no tendria valores que leer. Null = valida.
    /// </summary>
    Task<string?> ValidateFormulaAsync(
        string? formula, Guid boardId, Guid? fieldId, string? fieldKey, CancellationToken cancellationToken = default);

    /// <summary>Valores de los campos calculados de una tarea de ese tablero, listos para guardar/mostrar.</summary>
    Task<IReadOnlyDictionary<string, string?>> ComputeCalculatedAsync(
        Guid boardId, IReadOnlyDictionary<string, string?> values, CancellationToken cancellationToken = default);
}
