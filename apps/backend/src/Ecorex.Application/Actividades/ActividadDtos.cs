namespace Ecorex.Application.Actividades;

/// <summary>
/// Categoria del catalogo de Conceptos (000270, nivel 1) con el conteo de subcategorias
/// activas para la lista izquierda del prototipo.
/// </summary>
public sealed record ActividadCategoriaDto(
    Guid Id,
    string Codigo,
    string Nombre,
    string? Descripcion,
    int SortOrder,
    bool IsArchived,
    int SubcategoriasActivas,
    int SubcategoriasTotales);

/// <summary>Subcategoria (concepto, nivel 2) con sus flags, vinculos y relaciones M:N.</summary>
public sealed record ActividadSubcategoriaDto(
    Guid Id,
    Guid CategoriaId,
    string Codigo,
    string Nombre,
    string? Chequeo,
    string? Descripcion,
    int SortOrder,
    bool IsArchived,
    bool RequiereCliente,
    bool IniciaModulo,
    bool CierreManual,
    string? TituloAuto,
    string? DetalleAuto,
    Guid? WorkflowDefinitionId,
    Guid? FormDefinitionId,
    Guid? TaskBoardId,
    Guid? TaskBoardColumnId,
    IReadOnlyList<Guid> CargoIds,
    IReadOnlyList<Guid> TerceroIds,
    IReadOnlyList<string> Sedes,
    IReadOnlyList<Guid> NotificacionUserIds)
{
    /// <summary>Numero de items de la lista de chequeo (separados por ';').</summary>
    public int ChequeoCount => string.IsNullOrWhiteSpace(Chequeo)
        ? 0
        : Chequeo.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
}

/// <summary>Alta/edicion de una categoria.</summary>
public sealed record SaveCategoriaRequest(
    string Nombre,
    string? Descripcion = null,
    int? SortOrder = null);

/// <summary>Alta/edicion de una subcategoria (concepto) con sus flags, vinculos y M:N.</summary>
public sealed record SaveSubcategoriaRequest(
    Guid CategoriaId,
    string Nombre,
    string? Chequeo = null,
    string? Descripcion = null,
    int? SortOrder = null,
    bool RequiereCliente = false,
    bool IniciaModulo = false,
    bool CierreManual = false,
    string? TituloAuto = null,
    string? DetalleAuto = null,
    Guid? WorkflowDefinitionId = null,
    Guid? FormDefinitionId = null,
    Guid? TaskBoardId = null,
    Guid? TaskBoardColumnId = null,
    IReadOnlyList<Guid>? CargoIds = null,
    IReadOnlyList<Guid>? TerceroIds = null,
    IReadOnlyList<string>? Sedes = null,
    IReadOnlyList<Guid>? NotificacionUserIds = null);

/// <summary>KPIs de cabecera del modulo (como el prototipo).</summary>
public sealed record ActividadKpisDto(
    int Categorias,
    int Subcategorias,
    int ConCliente,
    int AutoInicio);

// ---- Opciones de los combos (poblar selects/chips del editor) ----

public sealed record WorkflowOptionDto(Guid Id, string Codigo, string Nombre, int Version);
public sealed record FormOptionDto(Guid Id, string Codigo, string Titulo);
public sealed record BoardOptionDto(Guid Id, string Nombre, IReadOnlyList<BoardColumnOptionDto> Columnas);
public sealed record BoardColumnOptionDto(Guid Id, string Nombre, bool IsDone);
public sealed record CargoOptionDto(Guid Id, string Nombre);
public sealed record TerceroOptionDto(Guid Id, string Nombre);
public sealed record UsuarioOptionDto(Guid Id, string Nombre);

/// <summary>Todas las opciones de los combos del editor, cargadas de una sola vez.</summary>
public sealed record ActividadComboOptionsDto(
    IReadOnlyList<WorkflowOptionDto> Workflows,
    IReadOnlyList<FormOptionDto> Forms,
    IReadOnlyList<BoardOptionDto> Boards,
    IReadOnlyList<CargoOptionDto> Cargos,
    IReadOnlyList<TerceroOptionDto> Terceros,
    IReadOnlyList<UsuarioOptionDto> Usuarios);
