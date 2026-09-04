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
