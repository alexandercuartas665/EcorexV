using Ecorex.Domain.Enums;

namespace Ecorex.Application.Inventory;

/// <summary>
/// Campo configurable de un item de inventario (000066), agrupado POR TIPO (ItemType). Calcado
/// del patron de campos del Directorio General (TerceroFieldDto), agrupando por tipo en vez de
/// por ficha. El valor por item se guarda en Item.FieldValuesJson indexado por FieldKey.
/// </summary>
public sealed record ItemFieldDto(
    Guid Id,
    Guid ItemTypeId,
    string FieldKey,
    string Label,
    TerceroFieldType FieldType,
    int Column,
    int SortOrder,
    string? Options,
    string? Description = null,
    bool IsRequired = false,
    bool IsSystem = false);

/// <summary>Alta de un campo configurable para un tipo de item.</summary>
public sealed record CreateItemFieldRequest(
    Guid ItemTypeId,
    string Label,
    TerceroFieldType FieldType,
    int Column = 1,
    string? Options = null,
    string? FieldKey = null,
    string? Description = null,
    bool IsRequired = false);

/// <summary>Edicion de un campo configurable (el tipo y la clave no cambian).</summary>
public sealed record UpdateItemFieldRequest(
    string Label,
    TerceroFieldType FieldType,
    int Column,
    string? Options,
    string? Description = null,
    bool IsRequired = false);
