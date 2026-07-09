using Ecorex.Domain.Enums;

namespace Ecorex.Application.Directorio;

/// <summary>
/// Campo configurable de una ficha del Directorio General (modulo 000232). Calcado del
/// patron de campos del pipeline (CUBOT.travels), agrupado por ficha en vez de por etapa.
/// </summary>
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
    bool IsSystem = false);

/// <summary>Alta de un campo configurable en una ficha.</summary>
public sealed record CreateTerceroFieldRequest(
    string FichaKey,
    string Label,
    TerceroFieldType FieldType,
    int Column = 1,
    string? Options = null,
    string? FieldKey = null,
    string? Description = null,
    bool AllowMultiple = false);

/// <summary>Edicion de un campo configurable (la ficha y la clave no cambian).</summary>
public sealed record UpdateTerceroFieldRequest(
    string Label,
    TerceroFieldType FieldType,
    int Column,
    string? Options,
    string? Description = null,
    bool AllowMultiple = false);

/// <summary>Nuevo orden de los campos: lista de ids en el orden deseado.</summary>
public sealed record ReorderFieldsRequest(IReadOnlyList<Guid> OrderedFieldIds);
