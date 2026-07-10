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

    Task<ItemFieldDto?> CreateFieldAsync(CreateItemFieldRequest request, CancellationToken cancellationToken = default);
    Task<ItemFieldDto?> UpdateFieldAsync(Guid fieldId, UpdateItemFieldRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteFieldAsync(Guid fieldId, CancellationToken cancellationToken = default);
}
