using Ecorex.Domain.Enums;

namespace Ecorex.Application.Directorio;

/// <summary>La ficha que arma una categoria del motor Modular: sus secciones (en orden) con sus campos.
/// Es lo que pinta el modal de crear/editar tercero (Capa 8, 2do motor de contactos).</summary>
public sealed record ModularFichaDto(string CategoriaKey, string CategoriaTitle, IReadOnlyList<ModularSeccionDto> Secciones);

/// <summary>Una seccion de la ficha (grupo de campos), con su naturaleza y sus campos ordenados.</summary>
public sealed record ModularSeccionDto(
    string FichaKey, string Title, string? Icono, string? Color, string? Descripcion, string? AplicaA, IReadOnlyList<ModularCampoDto> Campos);

/// <summary>Un campo de una seccion, listo para renderizar por tipo.</summary>
public sealed record ModularCampoDto(
    string FieldKey, string Label, TerceroFieldType Type, int Column,
    string? Options, string? RequeridoEn, bool ReadOnly, string? Descripcion);

/// <summary>Alta de un tercero desde el motor Modular: los valores por seccion (ficha -> campo -> valor).</summary>
public sealed record CreateModularTerceroRequest(
    string CategoriaKey,
    Dictionary<string, Dictionary<string, string>> Valores);

/// <summary>Un tercero del motor Modular listo para editar: su categoria (para armar la ficha), su estado
/// y los valores guardados (seccion -> campo -> valor).</summary>
public sealed record ModularEditDto(
    Guid Id, string? CategoriaKey, string Estado,
    Dictionary<string, Dictionary<string, string>> Valores);

// ---------------------------------------------------------------------------
// Lectura de estructura para el modal "Configurar directorio" (Capa 8, Fase 2).
// ---------------------------------------------------------------------------

/// <summary>La estructura completa del motor Modular para el modal de configuracion: todas las
/// secciones "mod_" con sus campos y las areas del catalogo (para pintar los chips de permiso).</summary>
public sealed record ModularEstructuraDto(
    IReadOnlyList<ModularSeccionConfigDto> Secciones,
    IReadOnlyList<ModularAreaDto> Areas);

/// <summary>Una seccion "mod_" como la ve el configurador: sus campos, sus areas autorizadas, su
/// naturaleza (AplicaA), si es protegida/sistema y que categorias la usan.</summary>
public sealed record ModularSeccionConfigDto(
    Guid Id, string FichaKey, string Title, string? Icono, string? Color, string? Descripcion,
    string? AplicaA, string? Areas, bool Protegida, int SortOrder,
    IReadOnlyList<ModularCampoConfigDto> Campos,
    IReadOnlyList<ModularUsoCategoriaDto> UsadaPor);

/// <summary>Un campo de una seccion como lo ve el configurador (incluye ancho y si es de sistema).</summary>
public sealed record ModularCampoConfigDto(
    Guid Id, string FieldKey, string Label, TerceroFieldType Type, string Ancho,
    string? Options, string? RequeridoEn, bool ReadOnly, bool IsSystem, string? Descripcion, int SortOrder,
    string? NotasDesarrollador);

/// <summary>Categoria que usa una seccion (para el bloque "Usada por las categorias").</summary>
public sealed record ModularUsoCategoriaDto(string CategoriaKey, string Title, string? Color);

/// <summary>Un area del catalogo de permisos del tenant (para los chips de "Areas que ven").</summary>
public sealed record ModularAreaDto(string Id, string Nombre, string? Icono);
