namespace Ecorex.Application.Inventory;

/// <summary>
/// CRUD de los campos configurables del item de inventario (000066). Un campo es GENERAL
/// (itemTypeId == null: aplica a TODOS los items) o POR TIPO (ItemType: producto/servicio/insumo).
/// Cada tenant define, sin tocar codigo, que campos captura en la ficha de sus items. Calcado de
/// ITerceroFieldService (general + por ficha). Tenant-scoped por el filtro global. La clave del
/// campo es unica por (tenant, tipo); en el editor de un item se ven los generales + los del tipo.
/// </summary>
public interface IItemFieldService
{
    /// <summary>Todos los campos del tenant (generales + de todos los tipos), ordenados.</summary>
    Task<IReadOnlyList<ItemFieldDto>> ListAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Campos de UN ambito para el configurador: null = generales; un Guid = los de ese tipo.
    /// Vacio si el ambito no tiene campos.
    /// </summary>
    Task<IReadOnlyList<ItemFieldDto>> ListByTypeAsync(Guid? itemTypeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Campos que se muestran al capturar UN item: los GENERALES primero y, si el item tiene tipo,
    /// los de ese tipo despues. Un item sin tipo ve solo los generales.
    /// </summary>
    Task<IReadOnlyList<ItemFieldDto>> ListForItemAsync(Guid? itemTypeId, CancellationToken cancellationToken = default);

    /// <summary>Devuelve null si el tipo no existe, falta la etiqueta o la formula no valida.</summary>
    Task<ItemFieldDto?> CreateFieldAsync(CreateItemFieldRequest request, CancellationToken cancellationToken = default);
    Task<ItemFieldDto?> UpdateFieldAsync(Guid fieldId, UpdateItemFieldRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteFieldAsync(Guid fieldId, CancellationToken cancellationToken = default);

    /// <summary>Nuevo orden de los campos de un ambito: lista de ids en el orden deseado.</summary>
    Task ReorderFieldsAsync(IReadOnlyList<Guid> orderedFieldIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mueve un campo a otro ambito (null = General; un Guid = ese tipo), donde aterriza al final.
    /// Devuelve null si se movio, o el motivo si no se pudo (el destino ya tiene esa clave, etc.).
    /// </summary>
    Task<string?> MoveFieldToTypeAsync(Guid fieldId, Guid? targetItemTypeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Valida una formula contra el AMBITO visible (ADR-0029): un campo general ve solo generales;
    /// un campo por tipo ve los generales + los del mismo tipo. Null = valida.
    /// </summary>
    Task<string?> ValidateFormulaAsync(
        string? formula, Guid? itemTypeId, Guid? fieldId, string? fieldKey, CancellationToken cancellationToken = default);

    /// <summary>Valores de los campos calculados visibles en un item (generales + de su tipo).</summary>
    Task<IReadOnlyDictionary<string, string?>> ComputeCalculatedAsync(
        Guid? itemTypeId, IReadOnlyDictionary<string, string?> values, CancellationToken cancellationToken = default);
}
