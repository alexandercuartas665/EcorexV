using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Campo configurable de una tarea, agrupado POR TABLERO (BoardId). Calcado del patron de
/// campos del inventario (ItemFieldDto), agrupando por tablero en vez de por tipo de item. El
/// valor por tarea se guarda en TaskItem.CustomFieldsJson indexado por FieldKey.
/// </summary>
/// <param name="Column">Ancho en la rejilla de 3: 1 = pequena (1/3), 2 = media (2/3), 3 = completo.</param>
/// <param name="Formula">Solo si FieldType=Calculated. Ver ADR-0029.</param>
/// <param name="RepeatWithFieldKey">Clave del campo numerico que dice cuantas veces se repite este.</param>
public sealed record TaskFieldDto(
    Guid Id,
    Guid BoardId,
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
    string? RepeatWithFieldKey = null);

/// <summary>Alta de un campo configurable para un tablero.</summary>
public sealed record CreateTaskFieldRequest(
    Guid BoardId,
    string Label,
    TerceroFieldType FieldType,
    int Column = 1,
    string? Options = null,
    string? FieldKey = null,
    string? Description = null,
    bool AllowMultiple = false,
    string? Formula = null,
    string? RepeatWithFieldKey = null);

/// <summary>
/// Edicion de un campo configurable. La clave no cambia (los valores guardados y las formulas la
/// referencian); para cambiarlo de tablero esta <see cref="ITaskFieldService.MoveFieldToBoardAsync"/>.
/// </summary>
public sealed record UpdateTaskFieldRequest(
    string Label,
    TerceroFieldType FieldType,
    int Column,
    string? Options,
    string? Description = null,
    bool AllowMultiple = false,
    string? Formula = null,
    string? RepeatWithFieldKey = null);
