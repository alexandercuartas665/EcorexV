using Ecorex.Domain.Enums;

namespace Ecorex.Application.Inventory;

/// <summary>
/// Campo configurable de un item de inventario (000066), agrupado POR TIPO (ItemType). Calcado
/// del patron de campos del Directorio General (TerceroFieldDto), agrupando por tipo en vez de
/// por ficha. El valor por item se guarda en Item.FieldValuesJson indexado por FieldKey.
/// </summary>
/// <param name="Column">Ancho en la rejilla de 3: 1 = pequena (1/3), 2 = media (2/3), 3 = completo.</param>
/// <param name="Formula">Solo si FieldType=Calculated. Ver ADR-0029.</param>
/// <param name="ShowInFilter">El campo se ofrece como filtro en el listado de items.</param>
/// <param name="RepeatWithFieldKey">Clave del campo numerico que dice cuantas veces se repite este.</param>
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
    bool IsSystem = false,
    string? Formula = null,
    bool ShowInFilter = false,
    string? RepeatWithFieldKey = null);

/// <summary>Alta de un campo configurable para un tipo de item.</summary>
public sealed record CreateItemFieldRequest(
    Guid ItemTypeId,
    string Label,
    TerceroFieldType FieldType,
    int Column = 1,
    string? Options = null,
    string? FieldKey = null,
    string? Description = null,
    bool IsRequired = false,
    string? Formula = null,
    bool ShowInFilter = false,
    string? RepeatWithFieldKey = null);

/// <summary>
/// Edicion de un campo configurable. La clave no cambia (los valores guardados y las formulas la
/// referencian); para cambiarlo de tipo de item esta <see cref="IItemFieldService.MoveFieldToTypeAsync"/>.
/// </summary>
public sealed record UpdateItemFieldRequest(
    string Label,
    TerceroFieldType FieldType,
    int Column,
    string? Options,
    string? Description = null,
    bool IsRequired = false,
    string? Formula = null,
    bool ShowInFilter = false,
    string? RepeatWithFieldKey = null);
