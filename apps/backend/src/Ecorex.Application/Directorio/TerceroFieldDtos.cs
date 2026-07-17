using Ecorex.Domain.Enums;

namespace Ecorex.Application.Directorio;

/// <summary>
/// Campo configurable de una ficha del Directorio General (modulo 000232). Calcado del
/// patron de campos del pipeline (CUBOT.travels), agrupado por ficha en vez de por etapa.
/// </summary>
/// <param name="Column">Ancho en la rejilla de 3: 1 = pequena (1/3), 2 = media (2/3), 3 = completo.</param>
/// <param name="Formula">Solo si FieldType=Calculated. Ver ADR-0029.</param>
/// <param name="ShowInFilter">El campo se ofrece como filtro en el listado.</param>
/// <param name="RepeatWithFieldKey">Clave del campo numerico que dice cuantas veces se repite este.</param>
public sealed record TerceroFieldDto(
    Guid Id,
    string FichaKey,
    string FieldKey,
    string Label,
    TerceroFieldType FieldType,
    int Column,
    int SortOrder,
    string? Options,
    string? Description = null,
    bool AllowMultiple = false,
    bool IsSystem = false,
    string? Formula = null,
    bool ShowInFilter = false,
    string? RepeatWithFieldKey = null);

/// <summary>Alta de un campo configurable en una ficha.</summary>
public sealed record CreateTerceroFieldRequest(
    string FichaKey,
    string Label,
    TerceroFieldType FieldType,
    int Column = 1,
    string? Options = null,
    string? FieldKey = null,
    string? Description = null,
    bool AllowMultiple = false,
    string? Formula = null,
    bool ShowInFilter = false,
    string? RepeatWithFieldKey = null);

/// <summary>
/// Edicion de un campo configurable. La ficha y la clave no cambian aqui: la clave es estable
/// porque los valores guardados y las formulas la referencian, y para cambiar de ficha esta
/// <see cref="ITerceroFieldService.MoveFieldToFichaAsync"/>, que ademas recoloca el orden.
/// </summary>
public sealed record UpdateTerceroFieldRequest(
    string Label,
    TerceroFieldType FieldType,
    int Column,
    string? Options,
    string? Description = null,
    bool AllowMultiple = false,
    string? Formula = null,
    bool ShowInFilter = false,
    string? RepeatWithFieldKey = null);

/// <summary>Nuevo orden de los campos: lista de ids en el orden deseado.</summary>
public sealed record ReorderFieldsRequest(IReadOnlyList<Guid> OrderedFieldIds);
