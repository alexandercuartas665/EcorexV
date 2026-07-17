namespace Ecorex.Application.Inventory;

/// <summary>
/// CRUD de los campos configurables del item de inventario (000066), agrupados POR TIPO
/// (ItemType: producto/servicio/insumo...). Cada tenant define, sin tocar codigo, que campos
/// captura en la ficha de sus items segun el tipo. Calcado de ITerceroFieldService. Tenant-scoped
/// por el filtro global. La clave del campo es unica por (tenant, tipo).
/// </summary>
public interface IItemFieldService
{
    /// <summary>Todos los campos del tenant (todos los tipos), ordenados por tipo y orden.</summary>
    Task<IReadOnlyList<ItemFieldDto>> ListAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Campos de un tipo de item, en orden. Vacio si el tipo no tiene campos.</summary>
    Task<IReadOnlyList<ItemFieldDto>> ListByTypeAsync(Guid itemTypeId, CancellationToken cancellationToken = default);

    /// <summary>Devuelve null si el tipo no existe, falta la etiqueta o la formula no valida.</summary>
    Task<ItemFieldDto?> CreateFieldAsync(CreateItemFieldRequest request, CancellationToken cancellationToken = default);
    Task<ItemFieldDto?> UpdateFieldAsync(Guid fieldId, UpdateItemFieldRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteFieldAsync(Guid fieldId, CancellationToken cancellationToken = default);

    /// <summary>Nuevo orden de los campos de un tipo: lista de ids en el orden deseado.</summary>
    Task ReorderFieldsAsync(IReadOnlyList<Guid> orderedFieldIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mueve un campo a otro tipo de item, donde aterriza al final. Devuelve null si se movio, o el
    /// motivo si no se pudo (el tipo destino ya tiene esa clave).
    /// </summary>
    Task<string?> MoveFieldToTypeAsync(Guid fieldId, Guid targetItemTypeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Valida una formula contra los campos del MISMO tipo de item (ADR-0029): un item tiene un solo
    /// tipo, asi que referenciar campos de otro no tendria valores que leer. Null = valida.
    /// </summary>
    Task<string?> ValidateFormulaAsync(
        string? formula, Guid itemTypeId, Guid? fieldId, string? fieldKey, CancellationToken cancellationToken = default);

    /// <summary>Valores de los campos calculados de un item de ese tipo, listos para guardar/mostrar.</summary>
    Task<IReadOnlyDictionary<string, string?>> ComputeCalculatedAsync(
        Guid itemTypeId, IReadOnlyDictionary<string, string?> values, CancellationToken cancellationToken = default);
}
