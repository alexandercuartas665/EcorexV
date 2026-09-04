namespace Ecorex.Application.Directorio;

/// <summary>
/// Categoria del 2do motor de contactos ("Directorio Modular", Capa 8). Arma su ficha componiendo
/// secciones y autoriza areas. Ver <see cref="Ecorex.Domain.Entities.DirectorioCategoria"/>.
/// </summary>
public sealed record DirectorioCategoriaDto(
    Guid Id,
    string CategoriaKey,
    string Title,
    string? Description,
    string? Icono,
    string? Color,
    string? Areas,
    bool Protegido,
    string? HomologaSeccion,
    int SortOrder,
    bool IsHidden,
    bool IsSystem);

/// <summary>Una seccion incluida en una categoria (composicion), con su orden.</summary>
public sealed record DirectorioCategoriaSeccionDto(string FichaKey, int Orden);

/// <summary>Alta de una categoria (la CategoriaKey se genera desde el titulo).</summary>
public sealed record CreateDirectorioCategoriaRequest(
    string Title,
    string? Icono = null,
    string? Color = null,
    string? Areas = null);

/// <summary>Edicion de una categoria (no cambia su CategoriaKey).</summary>
public sealed record UpdateDirectorioCategoriaRequest(
    string Title,
    string? Icono = null,
    string? Color = null,
    string? Areas = null,
    string? HomologaSeccion = null,
    bool IsHidden = false);
